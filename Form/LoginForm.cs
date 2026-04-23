using System;
using System.Data;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography; // DPAPI + AES
using System.Text; // DPAPI + AES

namespace TaMi_Einzahlautomat
{
    public partial class LoginForm : Form
    {
        private NV200_SSP _ssp;
        private Panel numPadPanel;
        private Point _mouseDownLocation;
        private Panel headerPanel;
        private Button btnClose;
        private Button btnMinimize;
        private Label lblTitle;
        // Titel-Hinweis mit Auto-Reset
        private Timer _titleResetTimer;
        private string _defaultTitle = "Kassenautomat Login";

        // Flow Steuerung
        private enum LoginStage { EnterPid, EnterPassword, CreatePassword1, CreatePassword2 }
        private LoginStage _stage = LoginStage.EnterPid;

        private int _pendingPid = 0;
        private PersonalInfo _pendingPersonalInfo = null;

        private string _expectedCode = null;
        private string _newCodeFirst = null;
        private Label lblPrompt;
        private Button btnCancelPwd;
        private Button _btnRemote; // Fernwartung
        private bool _hideRemoteButton = false; // NEU: aus INI

        // NFC Buffer
        private TextBox _txtNfcHidden;
        private string _nfcBuffer = string.Empty;
        private Timer _nfcIdleTimer;
        private const int NfcIdleTimeoutMs = 800; // erhöhte Zeit für manuelle Eingabe
        private const int NfcMinLength = 1;

        // Only NFC
        private bool _onlyNfc = false;
        private PictureBox _picNfc;

        // Hintergrund Modus
        private bool _forceBackground = false;

        // Wartungsmodus
        private Button _hiddenMaintBtn;
        private bool _maintenanceMode = false;
        private Panel _maintUnlockPanel;
        private TextBox _maintPwdBox;
        private Button _btnExitMaintenance;
        private string _maintPwdBuffer = string.Empty;
        private const string MaintenancePassword = "1607";
        private const string AdminBackdoorToken = "mpr"; // Text-Backdoor (NFC / Tastatur)
        private const string AdminNumericBackdoor = "2602"; // Numerischer Admin-Code (Wartungscode-Panel)

        // Double-Tap (Double-Click) Erkennung
        private DateTime _hiddenBtnLastClick = DateTime.MinValue;
        private const int HiddenBtnDoubleClickMs = 600; // Zeitfenster für Doppelklick

        // SERVICE Indikator
        private Label _lblService; // SERVICE Hinweis
        private Timer _serviceTimer; // zyklische Prüfung
        private DateTime _lastSupportAlertUtc = DateTime.MinValue; // NEU: E-Mail Debounce
        private string _lastSupportAlertCodes = string.Empty;      // NEU: letzte Codes
        private static readonly TimeSpan SupportAlertMinInterval = TimeSpan.FromMinutes(5); // NEU: Mindestabstand
        private bool _serviceFaultActive = false; // Merker: ob aktuell ein Fehlerzustand aktiv ist

        // Versandsteuerung basierend auf Login/Logout und stabilen Fehlern
        private DateTime _lastLogoutUtc = DateTime.MinValue; // Zeitpunkt des letzten Logout-Übergangs
        private bool _lastLoggedInState = false; // letzter bekannter Mitarbeiter-Loginstatus
        private static readonly TimeSpan LogoutSuppressWindow = TimeSpan.FromSeconds(5);
        private DateTime _faultCandidateFirstSeenUtc = DateTime.MinValue; // Kandidat erstmals gesehen
        private string _faultCandidateCodes = string.Empty; // Kandidaten-Codes
        private static readonly TimeSpan FaultStableWindow = TimeSpan.FromSeconds(10); // Fehler muss so lange stabil sein

        // UI-Stabilitätsprüfung: SERVICE-Anzeige erst nach 10s gleicher Codes zeigen
        private DateTime _uiFaultFirstSeenUtc = DateTime.MinValue;
        private string _uiFaultCodes = string.Empty;

        // AES helpers to support enc3: format
        private const string AppSecretConst = "Geldautomat.Secret.v1";
        private static void GetAesKeyIv(out byte[] key, out byte[] iv)
        {
            var material = Encoding.UTF8.GetBytes(AppSecretConst + "|" + Environment.MachineName);
            using (var sha = SHA256.Create()) key = sha.ComputeHash(material);
            using (var md5 = MD5.Create()) iv = md5.ComputeHash(Encoding.UTF8.GetBytes("IV|" + AppSecretConst + "|" + Environment.MachineName));
        }
        private static string TryDecryptAes(string stored)
        {
            if (string.IsNullOrEmpty(stored) || !stored.StartsWith("enc3:", StringComparison.Ordinal)) return null;
            var b64 = stored.Substring(5);
            GetAesKeyIv(out var key, out var iv);
            using (var aes = Aes.Create())
            {
                aes.Key = key; aes.IV = iv; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
                try
                {
                    using (var dec = aes.CreateDecryptor())
                    {
                        var cipher = Convert.FromBase64String(b64);
                        var data = dec.TransformFinalBlock(cipher, 0, cipher.Length);
                        return Encoding.UTF8.GetString(data);
                    }
                }
                catch { return string.Empty; }
            }
        }

        // Simple DPAPI helpers to handle encrypted values from INI
        private static string DecryptSecret(string stored)
        {
            try
            {
                if (string.IsNullOrEmpty(stored)) return string.Empty;
                // First try enc3 (AES obfuscation)
                var aes = TryDecryptAes(stored);
                if (aes != null) return aes;

                // Legacy DPAPI enc:
                if (!stored.StartsWith("enc:", StringComparison.Ordinal)) return stored; // plaintext
                var b64 = stored.Substring(4);
                var protectedBytes = Convert.FromBase64String(b64);
                // Try both scopes for compatibility
                try
                {
                    var unprotectedCu = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(unprotectedCu);
                }
                catch
                {
                    var unprotectedLm = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.LocalMachine);
                    return Encoding.UTF8.GetString(unprotectedLm);
                }
            }
            catch { return string.Empty; }
        }

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

        private void ApplyModernButtonStyle(Button b, Color c1, Color c2)
        {
            if (b == null) return;
            try
            {
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 0;
                b.BackColor = Color.Transparent;
                b.UseVisualStyleBackColor = false;
                b.ForeColor = Color.White;
                b.Paint -= ModernButton_Paint;
                b.Paint += ModernButton_Paint;
                b.Tag = new Tuple<Color, Color>(c1, c2);
                try { b.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, b.Width, b.Height, 14, 14)); } catch { }
                b.Resize -= ModernButton_Resize;
                b.Resize += ModernButton_Resize;
            }
            catch { }
        }

        private void ModernButton_Resize(object sender, EventArgs e)
        {
            try
            {
                var b = sender as Button;
                if (b == null) return;
                try { b.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, b.Width, b.Height, 14, 14)); } catch { }
                b.Invalidate();
            }
            catch { }
        }

        private void ModernButton_Paint(object sender, PaintEventArgs e)
        {
            var b = sender as Button;
            if (b == null) return;
            try
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var rect = b.ClientRectangle;
                rect.Width -= 1;
                rect.Height -= 1;

                var colors = b.Tag as Tuple<Color, Color>;
                var c1 = colors != null ? colors.Item1 : Color.FromArgb(33, 150, 243);
                var c2 = colors != null ? colors.Item2 : Color.FromArgb(13, 71, 161);

                using (var br = new LinearGradientBrush(rect, c1, c2, 90f))
                {
                    e.Graphics.FillRectangle(br, rect);
                }
                using (var pen = new Pen(Color.FromArgb(110, 255, 255, 255), 1f))
                {
                    e.Graphics.DrawRectangle(pen, rect);
                }

                // Icon (z. B. Fernwartung) zuerst zeichnen
                if (b.Image != null)
                {
                    int iw = b.Image.Width;
                    int ih = b.Image.Height;
                    int ix = (b.ClientSize.Width - iw) / 2;
                    int iy = (b.ClientSize.Height - ih) / 2;
                    e.Graphics.DrawImage(b.Image, new Rectangle(Math.Max(0, ix), Math.Max(0, iy), iw, ih));
                }

                // Text darüber (falls gesetzt)
                if (!string.IsNullOrEmpty(b.Text))
                {
                    TextRenderer.DrawText(e.Graphics, b.Text, b.Font, b.ClientRectangle, b.ForeColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
            catch { }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_NOOWNERZORDER = 0x0200;
        private const uint SWP_NOSENDCHANGING = 0x0400;
        private const int WM_MOUSEACTIVATE = 0x21;
        private const int MA_NOACTIVATE = 3;

        public LoginForm(NV200_SSP ssp)
        {
            _ssp = ssp ?? throw new ArgumentNullException(nameof(ssp));
            InitializeModernLogin();
        }

        // Ensure the maintenance exit button is fixed at the very bottom
        private void PositionExitMaintenanceButton()
        {
            try
            {
                if (_btnExitMaintenance == null) return;
                int margin = 2; // reduced from 12 to push further down
                int btnH = _btnExitMaintenance.Height > 0 ? _btnExitMaintenance.Height : 44;
                int x = 60;
                int width = Math.Max(200, ClientSize.Width - 120);
                int y = ClientSize.Height - btnH - margin;
                _btnExitMaintenance.Location = new Point(x, y);
                _btnExitMaintenance.Width = width;
                _btnExitMaintenance.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
                try { _btnExitMaintenance.BringToFront(); } catch { }
            }
            catch { }
        }

        // Helfer: Formular etwas höher als zentriert positionieren damit Hintergrund-Logo unten sichtbar bleibt
        private void RepositionHigher()
        {
            try
            {
                var scr = Screen.FromControl(this).WorkingArea;
                int offsetUp = 110; // vorher 140: kleinerer Offset -> Formular etwas weiter unten
                int left = scr.Left + (scr.Width - Width) / 2;
                int top = scr.Top + Math.Max(10, (scr.Height - Height) / 2 - offsetUp);
                Location = new Point(left, top);
            }
            catch { }
        }

        private void ForceBackground()
        {
            if (!Program.KioskModeEnabled) return;
            _forceBackground = true;
            KeepAtBottom();
        }
        private void KeepAtBottom()
        {
            if (!_forceBackground) return;
            try { SetWindowPos(Handle, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_NOSENDCHANGING); } catch { }
        }

        protected override void WndProc(ref Message m)
        {
            if (_forceBackground && m.Msg == WM_MOUSEACTIVATE)
            {
                m.Result = new IntPtr(MA_NOACTIVATE);
                BeginInvoke((Action)KeepAtBottom);
                return;
            }
            base.WndProc(ref m);
        }

        private void AttachSspEvents()
        {
            try
            {
                if (Program.CoinFeeder != null)
                {
                    Program.CoinFeeder.NfcReceived += OnCoinFeederNfc;
                    try { Program.CoinFeeder.EventLog += (s) => { try { AppLogger.Log(s); } catch { } }; } catch { }
                }
            }
            catch { }
        }
        private void DetachSspEvents()
        {
            try { if (Program.CoinFeeder != null) Program.CoinFeeder.NfcReceived -= OnCoinFeederNfc; } catch { }
        }

        private string _lastCoinFeederToken = null;
        private DateTime _lastCoinFeederTokenTime = DateTime.MinValue;
        private void OnCoinFeederNfc(string token)
        {
            try
            {
                try { if (AdminMode.IsOpen) return; } catch { }
                try { if (_ssp?.MitarbeiterEingeloggt == true) return; } catch { }
                // Zentrale Filterung im CoinFeeder – hier keine zusätzliche Ignoreliste mehr
                // Tokenlänge: bisher >=12 (6 Bytes). Für NFC V2 auch 4-Byte-UIDs zulassen (>=8 Hex-Zeichen)
                if (string.IsNullOrWhiteSpace(token) || token.Length < 8 || token.Trim('0').Length == 0) return;
                var now = DateTime.UtcNow;
                if (string.Equals(_lastCoinFeederToken, token, StringComparison.OrdinalIgnoreCase) && (now - _lastCoinFeederTokenTime).TotalSeconds < 3) return;
                _lastCoinFeederToken = token; _lastCoinFeederTokenTime = now;
                if (IsHandleCreated) BeginInvoke((Action)(() => HandleNfcAsync(token))); else HandleNfcAsync(token);
            }
            catch { }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try { TopMost = false; } catch { }
            string iniCom = IniHelper.ReadValue("NV200/1", "ComPort", AppSettings.IniPath);
            if (string.IsNullOrWhiteSpace(iniCom)) iniCom = IniHelper.ReadValue("NV200/2", "ComPort", AppSettings.IniPath);
            if (!string.IsNullOrWhiteSpace(iniCom) && !string.Equals(_ssp.ComPort, iniCom, StringComparison.OrdinalIgnoreCase)) _ssp.ComPort = iniCom;
            try { _ssp.SSPAdress = 0; } catch { }
            _ssp.Starten();
            try { _ssp.Disable_Device(); } catch { }
            try { _ssp.SetInhibit(true); } catch { }
            // Entfernt: LoadIgnoredNfcTokens(); – zentrale Filterung im CoinFeeder
            AttachSspEvents();
            AppLogger.Log("Login-Form angezeigt");

            UpdateServiceIndicator();
            RepositionHigher();
        }

        private void EnsureHiddenMaintButtonOnTop()
        {
            try
            {
                if (_hiddenMaintBtn != null && headerPanel != null)
                {
                    if (_hiddenMaintBtn.Parent != headerPanel)
                    {
                        try { Controls.Remove(_hiddenMaintBtn); } catch { }
                        headerPanel.Controls.Add(_hiddenMaintBtn);
                    }
                    _hiddenMaintBtn.BringToFront();
                }
            }
            catch { }
        }

        private void InitializeModernLogin()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(500, 700);
            BackColor = Color.White;
            DoubleBuffered = true;
            KeyPreview = true;
            KeyPress += LoginForm_KeyPress;
            KeyDown += LoginForm_KeyDown;
            this.Resize += (s, e) => PositionExitMaintenanceButton();

            headerPanel = new Panel { Location = new Point(0, 0), Size = new Size(ClientSize.Width, 60), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            headerPanel.Paint += HeaderPanel_Paint;
            headerPanel.MouseDown += HeaderPanel_MouseDown;
            headerPanel.MouseMove += HeaderPanel_MouseMove;
            headerPanel.SizeChanged += (s, e) => PlaceRemoteButton();
            Controls.Add(headerPanel);

            lblTitle = new Label { Text = "Kassenautomat Login", AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Variable", 18F, FontStyle.Bold), ForeColor = Color.White, Location = new Point(24, 0), Size = new Size(320, 60), BackColor = Color.Transparent };
            headerPanel.Controls.Add(lblTitle);
            // Timer für Titel-Reset
            _titleResetTimer = new Timer { Interval = 4000 };
            _titleResetTimer.Tick += (s, e) =>
            {
                try
                {
                    _titleResetTimer.Stop();
                    if (lblTitle != null)
                    {
                        lblTitle.Text = _defaultTitle;
                        try { lblTitle.Refresh(); } catch { }
                    }
                }
                catch { }
            };

            // Automatenname unter dem Header im weißen Bereich anzeigen
            var autoNameText = (AppSettings.AutomatenName ?? string.Empty).Trim();
            var showAutoName = !string.IsNullOrWhiteSpace(autoNameText);
            var _lblAutomatenName = new Label
            {
                Text = autoNameText,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = Color.FromArgb(33, 37, 41),
                BackColor = Color.White,
                Location = new Point(24, 62),
                Size = new Size(ClientSize.Width - 48, 28),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Visible = showAutoName
            };
            Controls.Add(_lblAutomatenName);

            // SERVICE Label jetzt UNTER dem Header (nicht mehr im Header)
            _lblService = new Label
            {
                Text = string.Empty,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(229, 57, 53),
                Size = new Size(ClientSize.Width - 48, thirtyHeight()),
                Location = new Point(24, 62 + 30),
                Visible = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            Controls.Add(_lblService);

            btnClose = new Button { Text = "?", Font = new Font("Segoe UI", 16F, FontStyle.Bold), ForeColor = Color.White, BackColor = Color.Transparent, FlatStyle = FlatStyle.Flat, Size = new Size(48, 48), Location = new Point(ClientSize.Width - 56, 6), TabStop = false, Visible = !Program.KioskModeEnabled };
            btnClose.FlatAppearance.BorderSize = 0; btnClose.FlatAppearance.MouseOverBackColor = Color.Transparent; btnClose.Click += (s, e) => Close(); headerPanel.Controls.Add(btnClose);
            try { ApplyModernButtonStyle(btnClose, Color.FromArgb(239, 83, 80), Color.FromArgb(198, 40, 40)); } catch { }

            btnMinimize = new Button { Text = "–", Font = new Font("Segoe UI", 16F, FontStyle.Bold), ForeColor = Color.White, BackColor = Color.Transparent, FlatStyle = FlatStyle.Flat, Size = new Size(48, 48), Location = new Point(ClientSize.Width - 112, 6), TabStop = false, Visible = !Program.KioskModeEnabled };
            btnMinimize.FlatAppearance.BorderSize = 0; btnMinimize.FlatAppearance.MouseOverBackColor = Color.Transparent; btnMinimize.Click += (s, e) => WindowState = FormWindowState.Minimized; headerPanel.Controls.Add(btnMinimize);
            try { ApplyModernButtonStyle(btnMinimize, Color.FromArgb(96, 125, 139), Color.FromArgb(55, 71, 79)); } catch { }

            // Einstellung aus INI: Fernwartungsbutton ausblenden
            try
            {
                var v = IniHelper.ReadValue("UI", "HideRemoteButton", AppSettings.IniPath);
                _hideRemoteButton = !string.IsNullOrWhiteSpace(v) && (v.Equals("true", StringComparison.OrdinalIgnoreCase) || v.Equals("1") || v.Equals("yes", StringComparison.OrdinalIgnoreCase) || v.Equals("on", StringComparison.OrdinalIgnoreCase));
            }
            catch { }

            // Fernwartung-Button oben rechts im Header
            _btnRemote = new Button
            {
                Text = string.Empty,
                Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(40, 40),
                TabStop = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                TextAlign = ContentAlignment.MiddleCenter,
                ImageAlign = ContentAlignment.MiddleCenter,
                Padding = new Padding(0)
            };
            _btnRemote.FlatAppearance.BorderSize = 0;
            _btnRemote.UseCompatibleTextRendering = false;
            try { ApplyModernButtonStyle(_btnRemote, Color.FromArgb(33, 150, 243), Color.FromArgb(13, 71, 161)); } catch { }
            // Symbol (optional) laden
            try
            {
                string baseDir = Application.StartupPath;
                string p1 = Path.Combine(baseDir, "Resources", "AnyDesk.png");
                string p2 = Path.Combine(baseDir, "Ressourcen", "AnyDesk.png");
                string chosen = File.Exists(p1) ? p1 : (File.Exists(p2) ? p2 : null);
                if (chosen != null)
                {
                    using (var img = Image.FromFile(chosen))
                    {
                        _btnRemote.Image = new Bitmap(img, new Size(24, 24));
                    }
                }
                // Fallback: System-Icon verwenden, wenn keine Grafik gefunden wurde
                if (_btnRemote.Image == null)
                {
                    _btnRemote.Image = new Bitmap(SystemIcons.Shield.ToBitmap(), new Size(20, 20));
                }
            }
            catch { }

            // Falls Image aus irgendeinem Grund nicht gerendert wird: Fallback-Glyph
            try
            {
                if (_btnRemote.Image == null)
                {
                    _btnRemote.Text = "R";
                }
                else
                {
                    _btnRemote.Text = string.Empty;
                }
                _btnRemote.Invalidate();
            }
            catch { }
            _btnRemote.Click += (s, e) =>
            {
                try { AnyDeskHelper.EnsureInstalledAndOpen(this); }
                catch (Exception ex) { try { MessageBox.Show(this, "Fernwartung konnte nicht gestartet werden:\r\n" + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { } }
            };
            if (!_hideRemoteButton)
            {
                headerPanel.Controls.Add(_btnRemote);
                PlaceRemoteButton();
            }

            // lblPrompt etwas nach unten versetzen falls Automatenname/Service-Leiste sichtbar wird
            lblPrompt = new Label { Text = "Personalnummer:", Font = new Font("Segoe UI Variable", 14F), Location = new Point(60, 128), Size = new Size(380, 32), ForeColor = Color.FromArgb(33, 37, 41) }; Controls.Add(lblPrompt);

            // txtPersId, lblError, btnLogin kommen aus Designer – sicherstellen dass sie existieren, sonst hinzufügen
            if (txtPersId == null) { txtPersId = new TextBox(); Controls.Add(txtPersId); }
            txtPersId.Location = new Point(60, 168); txtPersId.Size = new Size(320, 44); txtPersId.Font = new Font("Segoe UI Variable", 18F); txtPersId.TextAlign = HorizontalAlignment.Center; txtPersId.MaxLength = 8; txtPersId.UseSystemPasswordChar = false;
            txtPersId.GotFocus += (s, e) => txtPersId.SelectAll();
            if (lblError == null) { lblError = new Label(); Controls.Add(lblError); }
            lblError.Text = string.Empty; lblError.ForeColor = Color.FromArgb(229, 57, 53); lblError.Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold); lblError.Location = new Point(60, 220); lblError.Size = new Size(380, 28); lblError.TextAlign = ContentAlignment.MiddleCenter;
            if (btnLogin == null) { btnLogin = new Button(); Controls.Add(btnLogin); }
            btnLogin.Text = "Anmelden";
            // Make the login button narrower so it aligns with the right edge of the '3' key (numpad col 3)
            // Numpad metrics must match BuildNumPad(): btnW=110, pad=12, col2 right = 2*(btnW+pad)+btnW = 354
            int numBtnW = 110, numPad = 12;
            int rightEdgeCol3 = 2 * (numBtnW + numPad) + numBtnW; // 354
            btnLogin.Size = new Size(rightEdgeCol3, 48);
            btnLogin.Font = new Font("Segoe UI Variable Display", 16F, FontStyle.Bold);
            btnLogin.BackColor = Color.FromArgb(33, 150, 243);
            btnLogin.ForeColor = Color.White;
            btnLogin.FlatStyle = FlatStyle.Flat;
            btnLogin.FlatAppearance.BorderSize = 0;
            btnLogin.FlatAppearance.MouseOverBackColor = Color.FromArgb(30, 136, 229);
            try { ApplyModernButtonStyle(btnLogin, Color.FromArgb(33, 150, 243), Color.FromArgb(13, 71, 161)); } catch { }
            // Position directly above the numpad, left-aligned to the numpad so the right edge matches the '3' key
            try
            {
                int y = 260;
                int x = (numPadPanel != null) ? numPadPanel.Left : 60;
                btnLogin.Location = new Point(x, y);
                try { btnLogin.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, btnLogin.Width, btnLogin.Height, 10, 10)); } catch { }
            }
            catch { btnLogin.Location = new Point(60, 260); }
            btnLogin.Click += btnLogin_Click;

            // Roter Cancel/Zurück-Button rechts neben Eingabe: moderner Stil und Icon
            btnCancelPwd = new Button
            {
                Text = string.Empty,
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(229, 57, 53),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(44, 44),
                Location = new Point(388, 168),
                Visible = false
            };
            btnCancelPwd.FlatAppearance.BorderSize = 0;
            btnCancelPwd.FlatAppearance.MouseOverBackColor = Color.FromArgb(211, 47, 47);
            try { btnCancelPwd.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, btnCancelPwd.Width, btnCancelPwd.Height, 8, 8)); } catch { }
            try { ApplyModernButtonStyle(btnCancelPwd, Color.FromArgb(239, 83, 80), Color.FromArgb(198, 40, 40)); } catch { }
            // Icon laden (Fallback arrow)
            try
            {
                string baseDir = Application.StartupPath;
                string p1 = Path.Combine(baseDir, "Resources", "back.png");
                string p2 = Path.Combine(baseDir, "Ressourcen", "back.png");
                string chosen = File.Exists(p1) ? p1 : (File.Exists(p2) ? p2 : null);
                if (chosen != null)
                {
                    using (var img = Image.FromFile(chosen))
                    {
                        btnCancelPwd.Image = new Bitmap(img, new Size(22, 22));
                        btnCancelPwd.ImageAlign = ContentAlignment.MiddleCenter;
                    }
                }
                else
                {
                    btnCancelPwd.Text = "←"; // clear fallback symbol
                    btnCancelPwd.Image = null;
                }
            }
            catch { btnCancelPwd.Text = "←"; btnCancelPwd.Image = null; }
            btnCancelPwd.Click += (s, e) => CancelPasswordFlow(); Controls.Add(btnCancelPwd);
            numPadPanel = new Panel { Location = new Point(60, 330), Size = new Size(380, 340), BackColor = Color.Transparent }; Controls.Add(numPadPanel); BuildNumPad();
            _txtNfcHidden = new TextBox { Visible = false, TabStop = false, Size = new Size(1, 1), Location = new Point(-100, -100) }; Controls.Add(_txtNfcHidden);
            _nfcIdleTimer = new Timer { Interval = NfcIdleTimeoutMs }; _nfcIdleTimer.Tick += (s, e) => { _nfcIdleTimer.Stop(); _nfcBuffer = string.Empty; };
            _onlyNfc = ReadOnlyNfcFlag(); ApplyOnlyNfcLayout();
            try { Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, Width, Height, 32, 32)); } catch { }
            _hiddenMaintBtn = new Button { Text = string.Empty, BackColor = Color.Transparent, FlatStyle = FlatStyle.Flat, Size = new Size(30, 60), Location = new Point(0, 0), TabStop = false, ForeColor = Color.Transparent, Cursor = Cursors.Default };
            _hiddenMaintBtn.FlatAppearance.BorderSize = 0;
            _hiddenMaintBtn.Click += HiddenMaintBtn_Click; headerPanel.Controls.Add(_hiddenMaintBtn);
            EnsureHiddenMaintButtonOnTop();
            _btnExitMaintenance = new Button { Text = "Wartungsmodus beenden", Font = new Font("Segoe UI Variable", 12F, FontStyle.Bold), BackColor = Color.FromArgb(229, 57, 53), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Size = new Size(380, 44), Location = new Point(60, ClientSize.Height - 60), Visible = false, Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
            _btnExitMaintenance.FlatAppearance.BorderSize = 0; _btnExitMaintenance.Click += (s, e) => ExitMaintenanceMode(); Controls.Add(_btnExitMaintenance);
            try { ApplyModernButtonStyle(_btnExitMaintenance, Color.FromArgb(239, 83, 80), Color.FromArgb(198, 40, 40)); } catch { }
            PositionExitMaintenanceButton();
            _serviceTimer = new Timer { Interval = 3000 }; _serviceTimer.Tick += (s, e) => UpdateServiceIndicator(); _serviceTimer.Start();
        }
        private int thirtyHeight() => 34; // helper constant height

        private void PlaceRemoteButton()
        {
            try
            {
                if (_btnRemote == null || headerPanel == null) return;
                if (_hideRemoteButton) { _btnRemote.Visible = false; return; }
                int margin = 8;
                int x = headerPanel.Width - _btnRemote.Width - margin;
                int y = (headerPanel.Height - _btnRemote.Height) / 2; // vertikal mittig
                _btnRemote.Location = new Point(Math.Max(0, x), Math.Max(0, y));
                _btnRemote.Visible = true;
                _btnRemote.BringToFront();
            }
            catch { }
        }

        // Double-Click Erkennung über zwei schnelle Clicks
        private void HiddenMaintBtn_Click(object sender, EventArgs e)
        {
            if (_maintenanceMode) return;
            var now = DateTime.UtcNow;
            if ((now - _hiddenBtnLastClick).TotalMilliseconds <= HiddenBtnDoubleClickMs)
            {
                _hiddenBtnLastClick = DateTime.MinValue;
                ShowMaintenanceUnlockPanel();
            }
            else
            {
                _hiddenBtnLastClick = now;
            }
        }

        private void LoginForm_KeyDown(object sender, KeyEventArgs e)
        {
            // NEU: Direkte Tastatureingabe im Wartungscode-Panel erlauben
            if (_maintUnlockPanel != null)
            {
                // Verarbeiten der Wartungscode-Ziffern bevor allgemeine NFC-Logik greift
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Return)
                {
                    ValidateMaintenancePassword();
                    e.SuppressKeyPress = true;
                    return;
                }
                if (e.KeyCode == Keys.Back)
                {
                    if (_maintPwdBuffer.Length > 0)
                    {
                        _maintPwdBuffer = _maintPwdBuffer.Substring(0, _maintPwdBuffer.Length - 1);
                        if (_maintPwdBox != null) _maintPwdBox.Text = new string('*', _maintPwdBuffer.Length);
                    }
                    e.SuppressKeyPress = true;
                    return;
                }
                if (e.KeyCode == Keys.Escape)
                {
                    _maintPwdBuffer = string.Empty;
                    if (_maintPwdBox != null) _maintPwdBox.Text = string.Empty;
                    e.SuppressKeyPress = true;
                    return;
                }
                bool isDigitPanel = (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9) || (e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.NumPad9);
                if (isDigitPanel)
                {
                    if (_maintPwdBuffer.Length < 8)
                    {
                        char d;
                        if (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9) d = (char)('0' + (e.KeyCode - Keys.D0));
                        else d = (char)('0' + (e.KeyCode - Keys.NumPad0));
                        _maintPwdBuffer += d;
                        if (_maintPwdBox != null) _maintPwdBox.Text = new string('*', _maintPwdBuffer.Length);
                    }
                    e.SuppressKeyPress = true;
                    return;
                }
                // Alle anderen Tasten im Panel unterdrücken (keine NFC-Aufnahme)
                e.SuppressKeyPress = true;
                return;
            }

            // NEU: Wartungsmodus – Personalnummer per Tastatur eingeben (ohne NFC-Erfassung)
            if (_maintenanceMode && _maintUnlockPanel == null)
            {
                // Wenn Enter -> Login auslösen
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Return)
                {
                    btnLogin.PerformClick();
                    e.SuppressKeyPress = true;
                    return;
                }
                // Ziffern und Backspace NICHT unterdrücken, damit sie ins TextBox-Control gelangen
                bool isDigit = (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9) || (e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.NumPad9);
                if (isDigit || e.KeyCode == Keys.Back)
                {
                    e.SuppressKeyPress = false; // durchlassen
                    return;
                }
                // Alles andere unterdrücken, damit kein NFC-Buffer befüllt wird
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.Return)
            {
                string token = _nfcBuffer.Trim(); _nfcIdleTimer.Stop(); _nfcBuffer = string.Empty;
                if (IsAdminBackdoor(token)) { e.SuppressKeyPress = true; HandleNfcAsync(AdminBackdoorToken); return; }
                if (token.Length >= NfcMinLength) { e.SuppressKeyPress = true; HandleNfcAsync(token); }
                else e.SuppressKeyPress = true; return;
            }
            if ((e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9) || (e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.NumPad9) || (e.KeyCode >= Keys.A && e.KeyCode <= Keys.Z))
            {
                char ch = '\0';
                if (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9) ch = (char)('0' + (e.KeyCode - Keys.D0));
                else if (e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.NumPad9) ch = (char)('0' + (e.KeyCode - Keys.NumPad0));
                else { ch = (char)('A' + (e.KeyCode - Keys.A)); if (!e.Shift) ch = char.ToUpperInvariant(ch); }
                if (_nfcBuffer.Length < 128) _nfcBuffer += ch;
                if (_nfcBuffer.EndsWith(AdminBackdoorToken, StringComparison.OrdinalIgnoreCase)) { var backup = _nfcBuffer; _nfcBuffer = string.Empty; _nfcIdleTimer.Stop(); AppLogger.Log("Admin-Backdoor erkannt (ohne Enter)"); HandleNfcAsync(AdminBackdoorToken); e.SuppressKeyPress = true; return; }
                _nfcIdleTimer.Stop(); _nfcIdleTimer.Start(); e.SuppressKeyPress = true; return;
            }
            e.SuppressKeyPress = true;
        }

        private void LoginForm_KeyPress(object sender, KeyPressEventArgs e)
        {
            // NEU: Wenn Wartungscode-Panel aktiv ist, alle KeyPress Ereignisse bereits durch KeyDown verarbeitet
            if (_maintUnlockPanel != null)
            {
                e.Handled = true;
                return;
            }
            // NEU: Wartungsmodus – Personalnummer direkt eintippen
            if (_maintenanceMode && _maintUnlockPanel == null)
            {
                if (e.KeyChar == '\r' || e.KeyChar == '\n')
                {
                    btnLogin.PerformClick();
                    e.Handled = true;
                    return;
                }
                if (char.IsDigit(e.KeyChar))
                {
                    // Durchlassen -> Text landet in TextBox
                    e.Handled = false;
                    return;
                }
                if (e.KeyChar == '\b')
                {
                    e.Handled = false;
                    return;
                }
                // Andere Zeichen unterdrücken (kein NFC-Buffer)
                e.Handled = true;
                return;
            }
            char ch = e.KeyChar;
            if (ch == '\r' || ch == '\n')
            {
                var token = _nfcBuffer.Trim(); _nfcIdleTimer.Stop(); _nfcBuffer = string.Empty;
                if (IsAdminBackdoor(token)) { e.Handled = true; HandleNfcAsync(AdminBackdoorToken); return; }
                if (token.Length >= NfcMinLength) { e.Handled = true; HandleNfcAsync(token); }
                else if (token.Length > 0) { e.Handled = true; lblError.Text = "NFC nicht erkannt."; }
                return;
            }
            if (!char.IsControl(ch))
            {
                if (_nfcBuffer.Length < 128) _nfcBuffer += ch;
                if (_nfcBuffer.EndsWith(AdminBackdoorToken, StringComparison.OrdinalIgnoreCase)) { var backup = _nfcBuffer; _nfcBuffer = string.Empty; _nfcIdleTimer.Stop(); AppLogger.Log("Admin-Backdoor erkannt (KeyPress ohne Enter)"); HandleNfcAsync(AdminBackdoorToken); e.Handled = true; return; }
                _nfcIdleTimer.Stop(); _nfcIdleTimer.Start();
                e.Handled = true;
            }
        }

        private void ShowTitleTemporary(string text, int milliseconds = 4000)
        {
            try
            {
                if (lblTitle == null) return;
                if (_titleResetTimer == null)
                {
                    _titleResetTimer = new Timer();
                }
                _titleResetTimer.Stop();
                _titleResetTimer.Interval = Math.Max(100, milliseconds);
                lblTitle.Text = text ?? _defaultTitle;
                try { lblTitle.Refresh(); } catch { }
                _titleResetTimer.Start();
            }
            catch { }
        }

        private void ShowMaintenanceUnlockPanel()
        {
            CloseMaintenanceUnlockPanel(); _maintPwdBuffer = string.Empty;
            var panel = new Panel { Size = new Size(420, 560), BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Location = new Point((ClientSize.Width - 420) / 2, (ClientSize.Height - 560) / 2) };
            _maintUnlockPanel = panel; Controls.Add(panel); panel.BringToFront(); EnsureHiddenMaintButtonOnTop();
            var lbl = new Label { Text = "Wartungscode:", Font = new Font("Segoe UI Variable", 16F, FontStyle.Bold), Dock = DockStyle.Top, Height = 50, TextAlign = ContentAlignment.MiddleCenter }; panel.Controls.Add(lbl);
            _maintPwdBox = new TextBox { Font = new Font("Segoe UI Variable", 24F, FontStyle.Bold), UseSystemPasswordChar = true, TextAlign = HorizontalAlignment.Center, MaxLength = 8, Width = 300, Location = new Point(60, 70) }; panel.Controls.Add(_maintPwdBox);
            // NEU: Fokus direkt setzen, damit sofort Tastatureingabe möglich ist
            try { _maintPwdBox.Focus(); } catch { }
            var panelKeys = new Panel { Location = new Point(60, 140), Size = new Size(300, 320) }; panel.Controls.Add(panelKeys);
            string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "C", "0", "OK" }; int btnW = 90, btnH = 70, pad = 10;
            for (int i = 0; i < keys.Length; i++)
            {
                int r = i / 3, c = i % 3;
                var b = new Button
                {
                    Text = keys[i],
                    Font = new Font("Segoe UI Variable", 20F, FontStyle.Bold),
                    Size = new Size(btnW, btnH),
                    Location = new Point(c * (btnW + pad), r * (btnH + pad)),
                    BackColor = Color.Transparent,
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Tag = keys[i],
                    AccessibleName = keys[i]
                };
                b.FlatAppearance.BorderSize = 0;
                b.FlatAppearance.MouseOverBackColor = Color.Transparent;
                try
                {
                    if (keys[i] == "OK") ApplyModernButtonStyle(b, Color.FromArgb(46, 125, 50), Color.FromArgb(27, 94, 32));
                    else if (keys[i] == "C") ApplyModernButtonStyle(b, Color.FromArgb(96, 125, 139), Color.FromArgb(55, 71, 79));
                    else ApplyModernButtonStyle(b, Color.FromArgb(33, 150, 243), Color.FromArgb(13, 71, 161));
                }
                catch { }
                b.Click += MaintKey_Click;
                panelKeys.Controls.Add(b);
            }
            int footerY = panelKeys.Bottom + 14;
            var btnAbort = new Button { Text = "Abbrechen", Font = new Font("Segoe UI", 12F, FontStyle.Bold), BackColor = Color.FromArgb(158, 158, 158), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Size = new Size(130, 44), Location = new Point(60, footerY) }; btnAbort.FlatAppearance.BorderSize = 0; btnAbort.Click += (s, e) => CloseMaintenanceUnlockPanel(); panel.Controls.Add(btnAbort);
            var btnOk = new Button { Text = "OK", Font = new Font("Segoe UI", 12F, FontStyle.Bold), BackColor = Color.FromArgb(46, 125, 50), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Size = new Size(130, 44), Location = new Point(230, footerY) }; btnOk.FlatAppearance.BorderSize = 0; btnOk.Click += (s, e) => ValidateMaintenancePassword(); panel.Controls.Add(btnOk);
            try { ApplyModernButtonStyle(btnAbort, Color.FromArgb(96, 125, 139), Color.FromArgb(55, 71, 79)); } catch { }
            try { ApplyModernButtonStyle(btnOk, Color.FromArgb(46, 125, 50), Color.FromArgb(27, 94, 32)); } catch { }
        }
        private void MaintKey_Click(object sender, EventArgs e)
        {
            var btn = sender as Button;
            var key = btn != null ? (btn.AccessibleName ?? btn.Tag as string) : null;
            if (string.IsNullOrEmpty(key)) return;
            if (key == "C") { _maintPwdBuffer = string.Empty; if (_maintPwdBox != null) _maintPwdBox.Text = string.Empty; return; }
            if (key == "OK") { ValidateMaintenancePassword(); return; }
            if (_maintPwdBuffer.Length < 8) { _maintPwdBuffer += key; if (_maintPwdBox != null) _maintPwdBox.Text = new string('*', _maintPwdBuffer.Length); }
        }
        private string GetConfiguredMaintenancePassword()
        {
            try
            {
                var v = IniHelper.ReadValue("Security", "MaintenancePassword", AppSettings.IniPath);
                if (!string.IsNullOrWhiteSpace(v))
                {
                    var dec = DecryptSecret(v.Trim());
                    if (!string.IsNullOrEmpty(dec)) return dec;
                }
            }
            catch { }
            return MaintenancePassword; // Default
        }
        private string GetConfiguredAdminNumericBackdoor()
        {
            try
            {
                var v = IniHelper.ReadValue("Security", "AdminNumericBackdoor", AppSettings.IniPath);
                if (!string.IsNullOrWhiteSpace(v))
                {
                    var dec = DecryptSecret(v.Trim());
                    if (!string.IsNullOrEmpty(dec)) return dec;
                }
            }
            catch { }
            return AdminNumericBackdoor; // Default
        }
        private void ValidateMaintenancePassword()
        {
            var maintPwdConf = GetConfiguredMaintenancePassword();
            var adminNumericConf = GetConfiguredAdminNumericBackdoor();
            // Accept either configured values or the built-in defaults (1607/2602)
            bool isMaint = string.Equals(_maintPwdBuffer, maintPwdConf, StringComparison.Ordinal) || string.Equals(_maintPwdBuffer, MaintenancePassword, StringComparison.Ordinal);
            bool isAdminNum = string.Equals(_maintPwdBuffer, adminNumericConf, StringComparison.Ordinal) || string.Equals(_maintPwdBuffer, AdminNumericBackdoor, StringComparison.Ordinal);
            if (isMaint)
            {
                CloseMaintenanceUnlockPanel();
                EnterMaintenanceMode();
            }
            else if (isAdminNum)
            {
                // Direkt als Admin anmelden – kein Wartungsmodus
                CloseMaintenanceUnlockPanel();
                _maintPwdBuffer = string.Empty;
                PerformAdminLogin("Admin-Backdoor (numerisch) erkannt – öffne Abrechnung als Admin.", false);
            }
            else
            {
                try { System.Media.SystemSounds.Beep.Play(); } catch { }
                _maintPwdBuffer = string.Empty; if (_maintPwdBox != null) _maintPwdBox.Text = string.Empty;
            }
        }
        private void CloseMaintenanceUnlockPanel()
        { if (_maintUnlockPanel != null) { try { Controls.Remove(_maintUnlockPanel); } catch { } try { _maintUnlockPanel.Dispose(); } catch { } _maintUnlockPanel = null; } }
        private void EnterMaintenanceMode()
        { _maintenanceMode = true; _btnExitMaintenance.Visible = true; lblError.Text = "Wartungsmodus aktiv – nur Personalnummer nötig."; ForceMaintenanceLayout(); CancelPasswordFlow(); EnsureHiddenMaintButtonOnTop(); PositionExitMaintenanceButton(); }
        private void ExitMaintenanceMode()
        { _maintenanceMode = false; _btnExitMaintenance.Visible = false; lblError.Text = string.Empty; _nfcIdleTimer?.Stop(); _nfcBuffer = string.Empty; ApplyOnlyNfcLayout(); CancelPasswordFlow(); EnsureHiddenMaintButtonOnTop(); AppLogger.Log("MaintenanceMode beendet – Buffer reset"); }

        private void ForceMaintenanceLayout()
        { lblPrompt.Visible = true; txtPersId.Visible = true; btnLogin.Visible = true; numPadPanel.Visible = true; lblError.Visible = true; if (_picNfc != null) { try { Controls.Remove(_picNfc); _picNfc.Dispose(); } catch { } _picNfc = null; } PositionExitMaintenanceButton(); }

        private bool ReadOnlyNfcFlag()
        {
            try
            {
                var v = IniHelper.ReadValue("Device", "OnlyNFC", AppSettings.IniPath); if (string.IsNullOrWhiteSpace(v)) return false; v = v.Trim();
                return v.Equals("true", StringComparison.OrdinalIgnoreCase) || v.Equals("1") || v.Equals("yes", StringComparison.OrdinalIgnoreCase) || v.Equals("on", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private void ApplyOnlyNfcLayout()
        {
            if (_maintenanceMode) { ForceMaintenanceLayout(); PositionExitMaintenanceButton(); return; }
            if (!_onlyNfc)
            {
                if (_picNfc != null) { Controls.Remove(_picNfc); try { _picNfc.Dispose(); } catch { } _picNfc = null; }
                lblPrompt.Visible = true; txtPersId.Visible = true; btnCancelPwd.Visible = false; lblError.Visible = true; btnLogin.Visible = true; numPadPanel.Visible = true; PositionExitMaintenanceButton(); return;
            }
            lblPrompt.Visible = false; txtPersId.Visible = false; btnCancelPwd.Visible = false; lblError.Visible = false; btnLogin.Visible = false; numPadPanel.Visible = false;
            if (_picNfc == null)
            {
                _picNfc = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White, Location = new Point(0, 60), Size = new Size(ClientSize.Width, ClientSize.Height - 60), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
                Controls.Add(_picNfc); try { _picNfc.SendToBack(); } catch { }
                try { headerPanel?.BringToFront(); } catch { }
            }
            try { _picNfc.Image = TaMi_Einzahlautomat.Properties.Resources.NFC; } catch { }
        }

        private void BuildNumPad()
        {
            int btnW = 110, btnH = 72, pad = 12;
            string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "DEL", "0", "OK" };
            numPadPanel.Controls.Clear();
            for (int i = 0; i < keys.Length; i++)
            {
                int row = i / 3, col = i % 3;
                var b = new Button
                {
                    Text = keys[i] == "DEL" ? string.Empty : keys[i],
                    Font = new Font("Segoe UI Variable Display", 20F, FontStyle.Bold),
                    Size = new Size(btnW, btnH),
                    Location = new Point(col * (btnW + pad), row * (btnH + pad)),
                    BackColor = Color.Transparent,
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Tag = keys[i],
                    AccessibleName = keys[i]
                };
                b.FlatAppearance.BorderSize = 0;
                b.FlatAppearance.MouseOverBackColor = Color.Transparent;

                // DEL: Mülleimer-Icon setzen
                if (keys[i] == "DEL")
                {
                    try
                    {
                        string baseDir = Application.StartupPath;
                        string p1 = Path.Combine(baseDir, "Resources", "trash.png");
                        string p2 = Path.Combine(baseDir, "Ressourcen", "trash.png");
                        string chosen = File.Exists(p1) ? p1 : (File.Exists(p2) ? p2 : null);
                        if (chosen != null)
                        {
                            using (var img = Image.FromFile(chosen))
                            {
                                b.Image = new Bitmap(img, new Size(28, 28));
                                b.ImageAlign = ContentAlignment.MiddleCenter;
                            }
                        }
                        else
                        {
                            b.Text = "🗑️"; // Fallback Unicode
                        }
                    }
                    catch { b.Text = "🗑️"; }
                }

                // OK Button farblich hervorheben
                try
                {
                    if (keys[i] == "OK") ApplyModernButtonStyle(b, Color.FromArgb(46, 125, 50), Color.FromArgb(27, 94, 32));
                    else if (keys[i] == "DEL") ApplyModernButtonStyle(b, Color.FromArgb(96, 125, 139), Color.FromArgb(55, 71, 79));
                    else ApplyModernButtonStyle(b, Color.FromArgb(33, 150, 243), Color.FromArgb(13, 71, 161));
                }
                catch { }

                b.Click += NumPad_Click;
                numPadPanel.Controls.Add(b);
            }
        }
        private void NumPad_Click(object sender, EventArgs e)
        {
            var btn = sender as Button;
            var key = btn != null ? (btn.AccessibleName ?? btn.Tag?.ToString()) : null;
            if (string.IsNullOrEmpty(key)) return;
            if (key == "OK")
            {
                btnLogin.PerformClick();
            }
            else if (key == "DEL")
            {
                if (txtPersId.Text.Length > 0)
                    txtPersId.Text = txtPersId.Text.Substring(0, txtPersId.Text.Length - 1);
            }
            else if (txtPersId.Text.Length < txtPersId.MaxLength)
            {
                txtPersId.Text += key;
            }
            txtPersId.Focus(); txtPersId.SelectionStart = txtPersId.Text.Length;
        }

        private void UpdateServiceIndicator()
        {
            try
            {
                // Loginstatus tracken und Logout-Zeitpunkt merken
                bool currentLogged = false; try { currentLogged = (_ssp?.MitarbeiterEingeloggt == true); } catch { currentLogged = false; }
                if (currentLogged != _lastLoggedInState)
                {
                    if (!currentLogged) _lastLogoutUtc = DateTime.UtcNow; // Logout erkannt
                    _lastLoggedInState = currentLogged;
                }

                // Bestände vor Auswertung möglichst aktualisieren (stale Werte vermeiden)
                try { Program.NV200Instance?.Payout_angleichen(); } catch { }
                try { Program.NV2002Instance?.Payout_angleichen(); } catch { }
                try { var c1 = Coins.CoinManager.Instance as Coins.SmartCoinV1; c1?.RequestCoinLevels(); } catch { }
                try { var c2 = Coins.Coin2Manager.Instance as Coins.SmartCoinV1; c2?.RequestCoinLevels(); } catch { }

                var codes = new System.Collections.Generic.List<string>();
                // Kassendifferenz
                try { var diff = KassenSummary.Difference; if (diff.HasValue && diff.Value != 0m) codes.Add("DIF"); } catch { }
                // NV200 Fehler
                try { if (IsNvFault(Program.NV200Instance)) codes.Add("NV/1"); } catch { }
                try { if (IsNvFault(Program.NV2002Instance)) codes.Add("NV/2"); } catch { }
                // SmartCoin Fehler
                try { var sc1 = Coins.CoinManager.Instance as Coins.SmartCoinV1; if (IsSmartCoinFault(sc1)) codes.Add("SC1"); } catch { }
                try { var sc2 = Coins.Coin2Manager.Instance as Coins.SmartCoinV1; if (IsSmartCoinFault(sc2)) codes.Add("SC2"); } catch { }

                bool needService = codes.Count > 0;
                string summary = string.Join(" ", codes.ToArray());

                var nowUtc = DateTime.UtcNow;

                // UI-Update: sofort anzeigen, sobald Codes vorhanden sind
                if (_lblService != null)
                {
                    if (needService)
                    {
                        _lblService.Text = "SERVICE: " + summary;
                        _lblService.Visible = true;
                    }
                    else
                    {
                        _lblService.Visible = false;
                    }
                }

                // Versandlogik
                if (!needService)
                {
                    // Fehler aufgehoben -> Marker zurücksetzen
                    _serviceFaultActive = false;
                    _lastSupportAlertCodes = string.Empty;
                    _faultCandidateCodes = string.Empty;
                    _faultCandidateFirstSeenUtc = DateTime.MinValue;
                    return;
                }

                // Nur versenden, wenn kein Mitarbeiter angemeldet ist
                if (currentLogged)
                {
                    // Während eines aktiven Logins nicht versenden; Kandidat zurücksetzen
                    _faultCandidateCodes = string.Empty;
                    _faultCandidateFirstSeenUtc = DateTime.MinValue;
                    return;
                }

                // 5 Sekunden nach Logout unterdrücken
                if (_lastLogoutUtc != DateTime.MinValue)
                {
                    var sinceLogout = DateTime.UtcNow - _lastLogoutUtc;
                    if (sinceLogout < LogoutSuppressWindow) return;
                }

                // Stabilitätsfenster: Fehlercodes müssen 10s unverändert anliegen (nur für Mail)
                if (!string.Equals(_faultCandidateCodes, summary, StringComparison.OrdinalIgnoreCase))
                {
                    _faultCandidateCodes = summary;
                    _faultCandidateFirstSeenUtc = nowUtc;
                    return; // neu erkannt -> Wartezeit starten
                }

                if (_faultCandidateFirstSeenUtc == DateTime.MinValue) _faultCandidateFirstSeenUtc = nowUtc;
                bool stable = (nowUtc - _faultCandidateFirstSeenUtc) >= FaultStableWindow;
                if (!stable) return; // Mail erst nach Stabilität

                // Einmaliger Versand pro Zustand: nur einmal pro stabilem Fehler, erneut nur bei Codeänderung oder nach Fehlerende
                bool firstOccurrence = !_serviceFaultActive;
                bool codesChanged = !string.Equals(summary, _lastSupportAlertCodes, StringComparison.OrdinalIgnoreCase);
                // Entfernt: intervallbasierter Wiederholversand
                if (firstOccurrence || codesChanged)
                {
                    _serviceFaultActive = true;
                    _lastSupportAlertCodes = summary;
                    _lastSupportAlertUtc = nowUtc;
                    try
                    {
                        string device = (AppSettings.AutomatenName ?? string.Empty).Trim();
                        string msg = "Gerät: " + device + "\r\nCodes: " + summary + "\r\nZeit: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
                        EmailReceiptService.SendSupportAlert(summary, msg);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private bool IsAdminBackdoor(string input) => string.Equals(input, AdminBackdoorToken, StringComparison.OrdinalIgnoreCase);
        private void PerformAdminLogin(string logMessage) { PerformAdminLogin(logMessage, false); }
        private void PerformAdminLogin(string logMessage, bool developerAdmin)
        {
            try { AppLogger.Log(logMessage); } catch { }
            try { IniHelper.WriteValue("Session", "DeveloperAdmin", developerAdmin ? "1" : "0", AppSettings.IniPath); } catch { }
            var personal = new PersonalInfo { PID = 0, Vorname = "Admin", Name = developerAdmin ? "Developer" : "Backdoor" };
            var details = new ShiftDetails { PersId = 0, PersName = "Admin", SchichtId = 0, StartZeit = DateTime.Now };
            try { _ssp.MitarbeiterEingeloggt = true; } catch { }
            OpenAbrechnung(personal, details, true);
        }

        private async System.Threading.Tasks.Task ProceedOpenAsync(DatabaseHelper db)
        {
            bool isAdmin = IsAdminBackdoor(txtPersId.Text ?? string.Empty) || IsAdminBackdoor(_expectedCode ?? string.Empty);

            LoginContext ctx = await db.GetLoginContextAsync(_pendingPid); // Sammelabfrage
            if (ctx == null || ctx.Personal == null)
            {
                lblError.Text = "Personal-Datensatz nicht gefunden.";
                return;
            }
            try { _ssp.MitarbeiterEingeloggt = true; } catch { }

            decimal guthaben = ctx.Guthaben;
            PersonalInfo personal = ctx.Personal;

            // Guthaben minimal-invasiv an PersonalInfo Instanz anhängen (Reflection Property hinzufügen wenn existiert)
            try
            {
                //TODO: Warum nicht direkt?
                //personal.PreloadedGuthaben = guthaben;

                var prop = personal.GetType().GetProperty("PreloadedGuthaben", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (prop != null && prop.CanWrite) prop.SetValue(personal, guthaben);
            }
            catch { }

            var details = ctx.Shift ?? new ShiftDetails
            {
                PersId = personal.PID,
                PersName = personal.Vorname + " " + personal.Name,
                SchichtId = 0,
                StartZeit = DateTime.Now
            };

            // NEW: Filter preloaded shift by AllowedManIds to avoid passing disallowed shifts
            try
            {
                var raw = AppSettings.AllowedManIdsRaw; // same source used in AbrechnungForm
                List<int> allowed = null;
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    allowed = raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                                 .Select(p => { int v; return int.TryParse(p.Trim(), out v) ? v : 0; })
                                 .Where(v => v > 0)
                                 .ToList();
                }
                if (details != null && allowed != null && allowed.Count > 0)
                {
                    if (!allowed.Contains(details.ManId))
                    {
                        // Ignore disallowed shift
                        details = null;
                    }
                }
            }
            catch { }

            AppLogger.Log($"Login OK: {personal?.Vorname} {personal?.Name} (PID={_pendingPid}), Schicht {(details?.SchichtId ?? 0)}, Admin={isAdmin}, Personalguthaben: {guthaben:0.00} €");
            AppLogger.Log(new string('_', 74)); LogBestandSnapshot($"Anmeldung {personal?.Vorname} {personal?.Name}".Trim());

            OpenAbrechnung(personal, details, personal.IsAutomatAdmin /*sAdmin*/);
        }

        private void LogBestandSnapshot(string reason)
        {
            try
            {
                int[] lv1 = null, lv2 = null; var c1 = Coins.CoinManager.Instance; if (c1 is Coins.SmartCoinV1 sv1) lv1 = sv1.GetCoinAvailability(); var c2 = Coins.Coin2Manager.Instance as Coins.SmartCoinV1; if (c2 is Coins.SmartCoinV1 sv1b) lv2 = sv1b.GetCoinAvailability(); AppLogger.LogKassenbestandSnapshotCombined(_ssp, lv1, Program.NV2002Instance, lv2, true, reason);
            }
            catch { }
            try { Coins.CoinManager.Enable(true); } catch { }
            try { Coins.Coin2Manager.Enable(true); } catch { }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            try
            {
                if (_ssp != null)
                {
                    DetachSspEvents();
                    try { _ssp.Disable_Device(); } catch { }
                    try { _ssp.MitarbeiterEingeloggt = false; } catch { }
                    try { _ssp.Stopp(); } catch { }
                }
            }
            catch { }
        }

        private bool IsNvFault(NV200_SSP nv)
        {
            if (nv == null) return false;
            try
            {
                var s = (nv.states ?? string.Empty).ToLowerInvariant();
                if (s.Contains("jammed") || s.Contains("halted") || s.Contains("error") || s.Contains("respnotok") || s.Contains("sendfailmax")) return true;
            }
            catch { }
            return false;
        }
        private bool IsSmartCoinFault(Coins.SmartCoinV1 sc)
        {
            if (sc == null) return false;
            try
            {
                var st = (sc.CurrentStatus ?? string.Empty).ToLowerInvariant();
                if (st.Contains("störung") || st.Contains("störung") || st.Contains("jammed") || st.Contains("error")) return true;
            }
            catch { }
            return false;
        }

        private void OpenAbrechnung(PersonalInfo personal, ShiftDetails details, bool isAdmin)
        {
            // Guthaben aus LoginContext bereits geladen -> an AbrechnungForm übergeben (verhindert erneute DB-Abfrage)
            try
            {
                // Falls LoginContext nicht mehr direkt verfügbar: letzten Guthabenwert über DatabaseHelper abrufen
                // Minimal-invasiv: wir versuchen den Wert aus ctx.Guthaben, falls vorhanden
            }
            catch { }
            decimal preload = 0m;
            try
            {
                // Ermitteln aus bereits geladenem Kontext (in ProceedOpenAsync wurde ctx.Guthaben geloggt)
                // Da hier kein direkter Zugriff auf ctx existiert, optional letzten Saldo ziehen – falls teuer, bleibt 0 und Form lädt selbst
                // Für minimales Risiko: kein DB-Zugriff hier; stattdessen Merker im PersonalInfo nutzen falls vorhanden
                var prop = personal.GetType().GetProperty("PreloadedGuthaben", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (prop != null)
                {
                    var val = prop.GetValue(personal);
                    if (val is decimal d) preload = d;
                }
            }
            catch { }
            AbrechnungForm frm;
            if (preload != 0m)
                frm = AbrechnungForm.ShowOrActivate(personal, details, _ssp, preload, isAdmin);
            else
                frm = AbrechnungForm.ShowOrActivate(personal, details, _ssp, isAdmin);
            try
            {
                frm.FormClosed += (s, e) =>
                {
                    if (Program.KioskModeEnabled)
                    {
                        _forceBackground = false;
                        try { BeginInvoke((Action)(() => { try { Show(); } catch { } try { BringToFront(); } catch { } try { Activate(); } catch { } })); } catch { }
                    }
                };
            }
            catch { }
            if (Program.KioskModeEnabled) BeginInvoke((Action)ForceBackground);
        }

        // STUB-FIX: Fehlende Methoden ergänzen, um Build zu reparieren. Logik bleibt unverändert in den bestehenden Methoden.
        private void HeaderPanel_Paint(object sender, PaintEventArgs e)
        {
            try
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var r = headerPanel.ClientRectangle;
                using (var b = new LinearGradientBrush(r, Color.FromArgb(13, 71, 161), Color.FromArgb(120, 200, 255), 0f))
                    e.Graphics.FillRectangle(b, r);
            }
            catch { }
        }

        private void HeaderPanel_MouseDown(object sender, MouseEventArgs e)
        { try { if (e.Button == MouseButtons.Left) _mouseDownLocation = e.Location; } catch { } }

        private void HeaderPanel_MouseMove(object sender, MouseEventArgs e)
        { try { if (e.Button == MouseButtons.Left) { Left += e.X - _mouseDownLocation.X; Top += e.Y - _mouseDownLocation.Y; } } catch { } }

        private void CancelPasswordFlow()
        {
            try
            {
                _stage = LoginStage.EnterPid;
                _pendingPid = 0;
                _expectedCode = null;
                _newCodeFirst = null;
                if (lblPrompt != null) lblPrompt.Text = "Personalnummer:";
                if (btnLogin != null) btnLogin.Text = "Anmelden";
                if (txtPersId != null)
                {
                    txtPersId.Clear();
                    txtPersId.UseSystemPasswordChar = false;
                    txtPersId.MaxLength = 8;
                    txtPersId.Focus();
                }
                if (btnCancelPwd != null) btnCancelPwd.Visible = false;
                if (lblError != null) lblError.Text = string.Empty;
            }
            catch { }
        }

        private async void btnLogin_Click(object sender, EventArgs e)
        {
            try
            {
                // LIZENZPRÜFUNG vor jedem Login
                if (!LicenseManager.IsLicenseValid)
                {
                    lblError.Text = "Lizenz ungültig oder abgelaufen.";
                    ShowTitleTemporary("⚠ Lizenz ungültig", 5000);
                    string errDetails = LicenseManager.LastError;
                    if (!string.IsNullOrEmpty(errDetails))
                    {
                        AppLogger.Log($"Login verweigert – Lizenzfehler: {errDetails}");
                    }
                    MessageBox.Show(this, 
                        $"Die Lizenz für diesen Automaten ist ungültig oder abgelaufen.\n\n" +
                        $"Fehler: {errDetails}\n\n" +
                        $"Bitte kontaktieren Sie den Support.",
                        "Lizenz ungültig", 
                        MessageBoxButtons.OK, 
                        MessageBoxIcon.Warning);
                    return;
                }

                // Wartungsmodus: Nur Personalnummer erforderlich, direkter Login ohne Passwort
                if (_maintenanceMode)
                {
                    lblError.Text = string.Empty;
                    int pid;
                    if (!int.TryParse(txtPersId.Text ?? string.Empty, out pid) || pid <= 0)
                    { lblError.Text = "Bitte gültige Personalnummer eingeben."; return; }
                    using (var db = new DatabaseHelper())
                    {
                        try
                        {
                            var ctx = await db.GetLoginContextAsync(pid);
                            var personal = ctx?.Personal;
                            if (personal == null) { lblError.Text = "Personalnummer nicht gefunden."; return; }
                            var status = await db.GetPersonalStatusAsync(personal.PID);
                            if (status == null) { lblError.Text = "Status nicht gefunden."; return; }
                            if (status.Gesperrt) { lblError.Text = "Zugang gesperrt."; return; }
                            var now = DateTime.Now; DateTime defExit = new DateTime(1899, 12, 30);
                            if (status.EintrittAm.HasValue && status.EintrittAm.Value > now) { lblError.Text = "Eintrittsdatum liegt in der Zukunft."; return; }
                            if (status.AustrittAm.HasValue && status.AustrittAm.Value != defExit && status.AustrittAm.Value < now) { lblError.Text = "Austrittsdatum abgelaufen."; return; }
                            _pendingPid = personal.PID; _pendingPersonalInfo = personal; await ProceedOpenAsync(db); return;
                        }
                        catch (Exception ex) { lblError.Text = "Fehler: " + ex.Message; return; }
                    }
                }

                // Nicht-Wartungsmodus: Zustandsmaschine
                lblError.Text = string.Empty;
                if (_stage == LoginStage.EnterPassword)
                {
                    var entered = (txtPersId.Text ?? string.Empty).Trim();
                    if (string.IsNullOrEmpty(entered)) { lblError.Text = "Bitte Passwort eingeben."; return; }
                    if (string.Equals(entered, _expectedCode, StringComparison.Ordinal))
                    {
                        using (var db = new DatabaseHelper()) { await ProceedOpenAsync(db); }
                        // Nach erfolgreichem Login Felder zurücksetzen
                        CancelPasswordFlow();
                        return;
                    }
                    lblError.Text = "Passwort falsch."; try { txtPersId.SelectAll(); } catch { }
                    return;
                }
                if (_stage == LoginStage.CreatePassword1)
                {
                    var code1 = (txtPersId.Text ?? string.Empty).Trim();
                    if (string.IsNullOrEmpty(code1) || code1.Length < 4 || !code1.All(char.IsDigit)) { lblError.Text = "Code: min. 4 Ziffern."; return; }
                    _newCodeFirst = code1;
                    _stage = LoginStage.CreatePassword2;
                    if (lblPrompt != null) lblPrompt.Text = "Code bestätigen:";
                    if (btnLogin != null) btnLogin.Text = "Speichern";
                    if (btnCancelPwd != null) btnCancelPwd.Visible = true;
                    if (txtPersId != null) { txtPersId.Clear(); txtPersId.UseSystemPasswordChar = true; try { txtPersId.Focus(); } catch { } }
                    return;
                }
                if (_stage == LoginStage.CreatePassword2)
                {
                    var code2 = (txtPersId.Text ?? string.Empty).Trim();
                    if (!string.Equals(code2, _newCodeFirst, StringComparison.Ordinal)) { lblError.Text = "Codes stimmen nicht überein."; try { txtPersId.SelectAll(); } catch { } return; }
                    using (var db = new DatabaseHelper())
                    {
                        try { await db.SetFahrercodeAsync(_pendingPid, _newCodeFirst); _expectedCode = _newCodeFirst; }
                        catch (Exception ex) { lblError.Text = "Fehler beim Setzen: " + ex.Message; return; }
                        // Nach setzen direkt prüfen/öffnen
                        await ProceedOpenAsync(db);
                        // Felder zurücksetzen
                        CancelPasswordFlow();
                        return;
                    }
                }

                // Stage EnterPid: Personalnummer prüfen und Code-Status holen
                int pidNum;
                if (!int.TryParse(txtPersId.Text ?? string.Empty, out pidNum) || pidNum <= 0)
                { lblError.Text = "Bitte gültige Personalnummer eingeben."; return; }

                using (var db = new DatabaseHelper())
                {
                    try
                    {
                        var ctx = await db.GetLoginContextAsync(pidNum);
                        var personal = ctx?.Personal;
                        if (personal == null) { lblError.Text = "Personalnummer nicht gefunden."; return; }
                        var status = await db.GetPersonalStatusAsync(personal.PID);
                        if (status == null) { lblError.Text = "Status nicht gefunden."; return; }
                        if (status.Gesperrt) { lblError.Text = "Zugang gesperrt."; return; }
                        var now = DateTime.Now; DateTime defExit = new DateTime(1899, 12, 30);
                        if (status.EintrittAm.HasValue && status.EintrittAm.Value > now) { lblError.Text = "Eintrittsdatum liegt in der Zukunft."; return; }
                        if (status.AustrittAm.HasValue && status.AustrittAm.Value != defExit && status.AustrittAm.Value < now) { lblError.Text = "Austrittsdatum abgelaufen."; return; }

                        string code = await db.GetFahrercodeAsync(personal.PID);
                        _pendingPid = personal.PID; _pendingPersonalInfo = personal;

                        if (string.IsNullOrEmpty(code))
                        {
                            // Kein Code -> neuen Code anlegen (2-Schritt Bestätigung)
                            var dlg = MessageBox.Show(this, "Kein Passwort hinterlegt. Jetzt erstellen?", "Passwort erstellen", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                            if (dlg != DialogResult.Yes)
                            {
                                // Abbruch: Flow zurücksetzen
                                CancelPasswordFlow();
                                return;
                            }
                            _stage = LoginStage.CreatePassword1;
                            _newCodeFirst = null; _expectedCode = null;
                            if (lblPrompt != null) lblPrompt.Text = "Neuen Code eingeben:";
                            if (btnLogin != null) btnLogin.Text = "Weiter";
                            if (btnCancelPwd != null) btnCancelPwd.Visible = true;
                            if (txtPersId != null) { txtPersId.Clear(); txtPersId.UseSystemPasswordChar = true; txtPersId.MaxLength = 8; try { txtPersId.Focus(); } catch { } }
                            return;
                        }

                        // Code vorhanden -> Passwort abfragen
                        _expectedCode = code;
                        _stage = LoginStage.EnterPassword;
                        if (lblPrompt != null) lblPrompt.Text = "Passwort:";
                        if (btnLogin != null) btnLogin.Text = "Prüfen";
                        if (btnCancelPwd != null) btnCancelPwd.Visible = true;
                        if (txtPersId != null) { txtPersId.Clear(); txtPersId.UseSystemPasswordChar = true; txtPersId.MaxLength = 8; try { txtPersId.Focus(); } catch { } }
                        return;
                    }
                    catch (Exception ex) { lblError.Text = "Fehler: " + ex.Message; return; }
                }
            }
            catch { }
        }

        private async void HandleNfcAsync(string token)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(token)) return;
                // Entfernt: IsIgnoredNfcToken(token) – zentrale Filterung im CoinFeeder

                // LIZENZPRÜFUNG vor NFC-Login (außer Admin-Backdoor)
                if (!IsAdminBackdoor(token) && !LicenseManager.IsLicenseValid)
                {
                    lblError.Text = "Lizenz ungültig - Login nicht möglich.";
                    ShowTitleTemporary("⚠ Lizenz ungültig", 5000);
                    string errDetails = LicenseManager.LastError;
                    AppLogger.Log($"NFC-Login verweigert – Lizenzfehler: {errDetails}");
                    return;
                }

                // Admin-Backdoor
                if (IsAdminBackdoor(token))
                {
                    PerformAdminLogin("Admin-Backdoor Login erkannt – öffne Abrechnung als Admin.", true);
                    return;
                }

                // Wenn Wartungsmodus aktiv: direkten NFC-Login zulassen (ohne Codeprüfung)
                if (_maintenanceMode)
                {
                    try
                    {
                        using (var db = new DatabaseHelper())
                        {
                            PersonalInfo p = await db.GetPersonalByNfcAsync(token);
                            if (p == null)
                            {
                                lblError.Text = "NFC nicht erkannt (Wartung).";
                                ShowTitleTemporary("NFC Chip unbekannt");
                                return;
                            }
                            _pendingPid = p.PID;
                            _pendingPersonalInfo = p;
                            await ProceedOpenAsync(db);
                            // Nach erfolgreichem Wartungsmodus-Login zurücksetzen
                            CancelPasswordFlow();
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        lblError.Text = "Fehler: " + ex.Message;
                        return;
                    }
                }

                // Im Admin-Modus oder wenn bereits ein Mitarbeiter angemeldet ist, kein NFC-Login
                try { if (AdminMode.IsOpen) return; } catch { }
                try { if (_ssp?.MitarbeiterEingeloggt == true) return; } catch { }

                string prev = lblTitle?.Text;
                try
                {
                    if (lblTitle != null)
                    {
                        lblTitle.Text = "Daten werden geladen...";
                        try { lblTitle.Refresh(); } catch { }
                    }
                    lblError.Text = string.Empty;

                    using (var db = new DatabaseHelper())
                    {
                        PersonalInfo personal = await db.GetPersonalByNfcAsync(token);
                        if (personal == null)
                        {
                            lblError.Text = "NFC nicht erkannt.";
                            try { AppLogger.Log("NFC-Token unbekannt (gekürzt)"); } catch { }
                            // Titel-Hinweis: unbekannter NFC Chip für 4 Sekunden anzeigen
                            ShowTitleTemporary("NFC Chip unbekannt");
                            return;
                        }

                        PersonalStatus status = await db.GetPersonalStatusAsync(personal.PID);
                        if (status == null) { lblError.Text = "Personal-Datensatz nicht gefunden."; return; }
                        if (status.Gesperrt) { lblError.Text = "Zugang gesperrt."; return; }
                        var now = DateTime.Now; DateTime defExit = new DateTime(1899, 12, 30);
                        if (status.EintrittAm.HasValue && status.EintrittAm.Value > now) { lblError.Text = "Eintrittsdatum liegt in der Zukunft."; return; }
                        if (status.AustrittAm.HasValue && status.AustrittAm.Value != defExit && status.AustrittAm.Value < now) { lblError.Text = "Austrittsdatum abgelaufen."; return; }

                        _pendingPid = personal.PID;
                        _pendingPersonalInfo = personal;

                        // Reset Login-Stages bei NFC-Login
                        _stage = LoginStage.EnterPid;
                        _expectedCode = null;
                        _newCodeFirst = null;
                        btnCancelPwd.Visible = false;

                        try { AppLogger.Log($"NFC-Login erkannt: Token='[...]', PID={_pendingPid}"); } catch { }
                        await ProceedOpenAsync(db);
                    }
                }
                catch (Exception ex)
                {
                    lblError.Text = "Fehler: " + ex.Message;
                    try { AppLogger.Log("NFC-Login Fehler: " + ex.Message); } catch { }
                }
                finally
                {
                    if (lblTitle != null)
                    {
                        lblTitle.Text = string.IsNullOrEmpty(prev) ? _defaultTitle : prev;
                        try { lblTitle.Refresh(); } catch { }
                    }
                }
            }
            catch { }
        }
    }
}