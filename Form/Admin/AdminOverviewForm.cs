using System.Windows.Forms;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Drawing.Drawing2D;
using TaMi_Einzahlautomat.Coins;
using System;
using TaMi_Einzahlautomat.Devices; // NEU
using System.IO.Ports; // NEU
using TaMi_Einzahlautomat; // <--- Hinzugef�gt f�r LogViewForm
using System.Reflection; // NEU f�r Versionsinfo
using System.IO; // NEU f�r Timestamp
using TaMi_Einzahlautomat.UI.Layout;

namespace TaMi_Einzahlautomat
{
    public class AdminOverviewForm : Form
    {
        private ModernHeaderPanel _header;
        // Tabs
        private TabControl _tabs;
        private TabPage _tabDevices;
        private TabPage _tabSettings;
        private Button btnNV200_1;
        private Button btnNV200_2;
        private Button btnKassenbestand; // NEU
        private Button btnCoinAdmin;     // NEU
        private Button btnCoinAdmin2;    // NEU: M�nzpr�fer/2
        private Button btnKassensturz; // NEU
        private Button btnExitProgram; // NEU: Programm beenden
        private Button btnPersonal; // NEU: Personalverwaltung
        private Button btnPaymentSettings; // NEU: Zahlungseinstellungen
        private Button btnRemoteSupport; // NEU: Fernwartung
        private NV200_SSP _ssp;
        private Button btnMailSettings; // NEU: Maileinstellungen

        // NEU: Immer bedienbarer Abmelden-Button (ersetzt ToggleOps)
        private Button btnAbmeldenImmer;

        // NEU: M�nzpr�fer + INI
        private ICoinValidator _coin;
        private readonly string _iniPath = @"C:\\ProgramData\\SuE-Software\\SuE-TaMi Client SQL\\Einzahlautomat.ini";

        private Timer _statusTimer; // NEU
        private Button btnCoinFeeder; // NEU
        private CoinFeederController _coinFeeder; // NEU
        private Button btnShowLog; // NEU: Log anzeigen
        private Button btnPrinterConfig; // NEU
        private Label _lblExeInfo; // NEU: Build-/Versionsinfo
        private Button btnCheckUpdate; // NEU: Update prüfen
        private Button btnUpdateHints; // NEU: Updatehinweise anzeigen

        private string _lastNv1Bezel = null; // track last applied bezel color as "R,G,B"
        private string _lastNv2Bezel = null;

        // Cache f�r Einzelinstanz-Fenster
        private T ShowOrActivate<T>(Func<T> factory) where T : Form
        {
            bool prevTopMost = this.TopMost;
            T target = null;
            try
            {
                foreach (Form f in Application.OpenForms)
                {
                    if (f is T existing)
                    {
                        target = existing;
                        break;
                    }
                }
            }
            catch { }

            if (target != null)
            {
                try
                {
                    if (prevTopMost) this.TopMost = false;
                    try { if (target.Owner != this) this.AddOwnedForm(target); } catch { }
                    if (target.WindowState == FormWindowState.Minimized)
                        target.WindowState = FormWindowState.Normal;
                    target.TopMost = true;
                    target.BringToFront();
                    target.Activate();
                    target.Focus();
                    if (prevTopMost)
                    {
                        target.FormClosed += (s, e) =>
                        {
                            try { this.TopMost = true; this.BringToFront(); this.Activate(); } catch { }
                        };
                    }
                }
                catch { }
                return target;
            }

            T frm = null;
            try { frm = factory(); } catch { return null; }

            try
            {
                if (prevTopMost) this.TopMost = false;
                frm.StartPosition = FormStartPosition.CenterScreen;
                frm.TopMost = true;
                try { this.AddOwnedForm(frm); } catch { }
                frm.FormClosed += (s, e) =>
                {
                    try
                    {
                        if (prevTopMost)
                        {
                            this.TopMost = true;
                            this.BringToFront();
                            this.Activate();
                        }
                    }
                    catch { }
                };
                frm.Show(this);
                frm.BringToFront();
                frm.Activate();
                frm.Focus();
            }
            catch { }
            return frm;
        }

        public AdminOverviewForm(NV200_SSP ssp)
        {
            _ssp = ssp;
            try { CoinManager.InitFromIni(AppSettings.IniPath); } catch { }
            _coin = CoinManager.Instance;
            try { _coin?.Connect(); } catch { }
            _coinFeeder = Program.CoinFeeder; // globale Instanz
            InitializeModernOverview();
            InitializeStatusTimer();
            InitExeInfoLabel(); // NEU
            this.FormClosed += (s, e) => { try { AdminMode.Exit(); } catch { } };
        }

        public AdminOverviewForm(NV200_SSP ssp, ICoinValidator coin)
            : this(ssp)
        {
            _coin = coin ?? CoinManager.Instance;
        }

        private Button CreateMenuButton(Control parent, string text, int y, int height, Color backColor, Font font = null)
        {
            var btn = new ModernGradientButton
            {
                Text = text,
                Size = new Size(400, height),
                Location = new Point(Math.Max(20, ((parent?.ClientSize.Width ?? ClientSize.Width) - 400) / 2), y),
                Font = font ?? new Font("Segoe UI Variable", 16F, FontStyle.Bold),
                GradientStart = backColor,
                GradientEnd = ControlPaint.Dark(backColor),
                TabStop = false
            };
            (parent ?? (Control)this).Controls.Add(btn);
            return btn;
        }

        private void InitializeModernOverview()
        {
            // Fenster-Setup
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(450, 1010); // etwas höher, damit der untere Button vollständig sichtbar bleibt
            BackColor = Color.White;
            DoubleBuffered = true;

            _header = new ModernHeaderPanel { Title = "Admin Übersicht" };
            _header.CloseClicked += () => { try { Close(); } catch { } };
            Controls.Add(_header);
            _header.BringToFront();
            try { _header.ApplyRoundedRegionToForm(this); } catch { }

            // Tabs anlegen (Geräte / Einstellungen)
            _tabs = new TabControl
            {
                Location = new Point(0, _header.Bottom),
                // Höhe etwas reduziert, damit unten Platz für ExeInfo-Label bleibt
                Size = new Size(ClientSize.Width, ClientSize.Height - _header.Height - 52),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Font = new Font("Segoe UI Variable", 12F, FontStyle.Bold),
                ItemSize = new Size(200, 36),
                Padding = new Point(12, 6)
            };
            _tabDevices = new TabPage("Geräte") { BackColor = Color.White };
            _tabSettings = new TabPage("Einstellungen") { BackColor = Color.White, AutoScroll = true, Padding = new Padding(8) };
            _tabs.TabPages.Add(_tabDevices);
            _tabs.TabPages.Add(_tabSettings);
            Controls.Add(_tabs);

            // Adjusted layout constants to reduce gaps so everything fits (Tabs haben eigene Kopfzeile)
            int startY = 20;      // etwas kleinerer Top-Abstand
            int gapSmall = 10;    // was 12
            int gapLarge = 20;    // etwas kleiner, damit alles ohne Scrollen sichtbarer bleibt
            int h70 = 64;
            int h60 = 56;
            Color blue = Color.FromArgb(33, 150, 243);
            Font f16 = new Font("Segoe UI Variable", 16F, FontStyle.Bold);
            Font f14 = new Font("Segoe UI Variable", 14F, FontStyle.Bold);

            // Inhalte Tab Geräte
            int y = startY;
            btnKassenbestand = CreateMenuButton(_tabDevices, "Kassenbestand", y, h70, blue, f16); y += h70 + gapSmall;
            btnKassensturz = CreateMenuButton(_tabDevices, "Kassensturz", y, h70, blue, f16); y += h70 + gapSmall;
            // Zusätzlicher Abstand zwischen Kassensturz und Log
            y += gapLarge;
            btnShowLog = CreateMenuButton(_tabDevices, "Log anzeigen", y, h60, blue, f14); y += h60 + gapLarge;

            // Neuer immer aktiver Abmelden-Button
            btnAbmeldenImmer = CreateMenuButton(_tabDevices, "Abmelden (immer)", y, h60, Color.FromArgb(46,125,50), f14);
            btnAbmeldenImmer.Click += (s,e)=> ForceLogoutFromAbrechnung();
            y += h60 + gapLarge;

            // Geräte
            btnNV200_1 = CreateMenuButton(_tabDevices, "Scheinautomat NV200/1", y, h70, blue, f16); y += h70 + gapSmall;
            btnNV200_2 = CreateMenuButton(_tabDevices, "Scheinautomat NV200/2", y, h70, blue, f16); y += h70 + gapSmall;
            // Etwas kleinerer Abstand zwischen NV200/2 und Münzpr�fer/1
            y += gapSmall;
            btnCoinAdmin = CreateMenuButton(_tabDevices, "Münzprüfer/1", y, h70, blue, f16); y += h70 + gapSmall;
            btnCoinAdmin2 = CreateMenuButton(_tabDevices, "Münzprüfer/2", y, h70, blue, f16); y += h70 + gapSmall;
            // Etwas kleinerer Abstand zwischen Münzprüfer/2 und Coinfeeder
            y += gapSmall;
            btnCoinFeeder = CreateMenuButton(_tabDevices, "NFC / Coinfeeder", y, h60, blue, f16); y += h60 + gapSmall;

            // Repositioned: printer config now directly below Coinfeeder
            btnPrinterConfig = CreateMenuButton(_tabDevices, "Quittungsdrucker", y, h60, blue, f14);
            btnPrinterConfig.Click += (s, e) => ShowOrActivate(() => new PrinterConfigForm());
            y += h60 + gapLarge;

            // Exit-Button wie die anderen Buttons anordnen
            btnExitProgram = CreateMenuButton(_tabDevices, "Programm beenden", y, h60, Color.FromArgb(229,57,53), f14);
            btnExitProgram.Click += (s, e) => Program.RequestControlledShutdown();
            y += h60 + gapLarge;

            // Click handlers for remaining buttons
            btnKassenbestand.Click += (s,e)=> ShowOrActivate(()=> new KassenbestandForm(_ssp));
            btnKassensturz.Click   += (s,e)=> ShowOrActivate(()=> new KassensturzForm());
            btnShowLog.Click       += (s,e)=> ShowOrActivate(()=> new LogViewForm());
            btnNV200_1.Click       += (s,e)=> ShowOrActivate(()=> new AdminNV200Form(_ssp));
            btnNV200_2.Click       += (s,e)=> { var nv2 = Program.NV2002Instance ?? _ssp; ShowOrActivate(()=> new AdminNV200_2Form(nv2)); };
            btnCoinAdmin.Click     += (s,e)=> ShowOrActivate(()=>
            {
                var coin = _coin ?? CoinManager.Instance;
                if (coin == null)
                {
                    try { CoinManager.InitFromIni(AppSettings.IniPath); } catch { }
                    coin = CoinManager.Instance;
                }
                if (coin == null) return null;
                _coin = coin;
                try { coin.Connect(); } catch { }
                return new AdminCoinForm(coin);
            });
            btnCoinAdmin2.Click    += (s,e)=> ShowOrActivate(()=> { try { Coin2Manager.InitFromIni(AppSettings.IniPath); } catch { } return new AdminCoin2Form(Coin2Manager.Instance); });
            btnCoinFeeder.Click    += (s,e)=> ShowOrActivate(()=> new AdminCoinFeederForm(_iniPath));

            // Inhalte Tab "Einstellungen"
            int ys = startY;
            // Personal-Button in Einstellungen-Seite
            btnPersonal = CreateMenuButton(_tabSettings, "Personal", ys, h60, blue, f16);
            btnPersonal.Click      += (s,e)=> ShowOrActivate(()=> new PersonalVerwaltungForm());
            ys += h60 + gapLarge;
            btnPaymentSettings = CreateMenuButton(_tabSettings, "Zahlungseinstellungen", ys, h70, blue, f16);
            btnPaymentSettings.Click += (s, e) =>
            {
                try { ShowOrActivate(() => new PaymentSettingsForm()); } catch { }
            };

            // NEU: Allgemeine Einstellungen (OnlyNFC, Passwörter)
            ys += h60 + gapLarge;
            var btnGeneralSettings = CreateMenuButton(_tabSettings, "Allgemeine Einstellungen", ys, h60, blue, f14);
            btnGeneralSettings.Click += (s, e) => { try { ShowOrActivate(() => new GeneralSettingsForm()); } catch { } };

            // NEU: Update prüfen Button
            ys += h60 + gapSmall;
            btnCheckUpdate = CreateMenuButton(_tabSettings, "Nach Update suchen", ys, h60, Color.FromArgb(0, 122, 204), f14);
            btnCheckUpdate.Click += (s, e) =>
            {
                try
                {
                    Program.CheckForUpdateNow(this);
                }
                catch { }
            };

            ys += h60 + gapSmall;
            btnUpdateHints = CreateMenuButton(_tabSettings, "Updatehinweise", ys, h60, Color.FromArgb(0, 122, 204), f14);
            btnUpdateHints.Click += (s, e) =>
            {
                try
                {
                    Program.ShowUpdateHints(this);
                }
                catch { }
            };

            // Fernwartung (AnyDesk) Button unter Einstellungen
            ys += h60 + gapLarge;
            btnRemoteSupport = CreateMenuButton(_tabSettings, "Fernwartung", ys, h60, blue, f14);
            try
            {
                string baseDir = Application.StartupPath;
                string p1 = System.IO.Path.Combine(baseDir, "Resources", "AnyDesk.png");
                string p2 = System.IO.Path.Combine(baseDir, "Ressourcen", "AnyDesk.png");
                string chosen = System.IO.File.Exists(p1) ? p1 : (System.IO.File.Exists(p2) ? p2 : null);
                if (chosen != null)
                {
                    using (var img = Image.FromFile(chosen))
                    {
                        btnRemoteSupport.Image = new Bitmap(img, new Size(24, 24));
                        btnRemoteSupport.ImageAlign = ContentAlignment.MiddleLeft;
                        btnRemoteSupport.TextAlign = ContentAlignment.MiddleLeft;
                        btnRemoteSupport.Padding = new Padding(8, 0, 8, 0);
                    }
                }
            }
            catch { }
            btnRemoteSupport.Click += (s, e) =>
            {
                try { AnyDeskHelper.EnsureInstalledAndOpen(this); }
                catch (Exception ex) { try { MessageBox.Show(this, "Fernwartung konnte nicht gestartet werden:\r\n" + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { } }
            };

            // NEU: Maileinstellungen Button unter Einstellungen
            ys += h60 + gapLarge;
            btnMailSettings = CreateMenuButton(_tabSettings, "Maileinstellungen", ys, h60, blue, f14);
            btnMailSettings.Click += (s, e) => { try { ShowOrActivate(() => new MailSettingsForm()); } catch { } };
        }

        private void InitializeStatusTimer()
        {
            _statusTimer = new Timer();
            _statusTimer.Interval = 1000; // 1 Sekunde
            _statusTimer.Tick += (s, e) => UpdateDeviceStatus();
            _statusTimer.Start();
            UpdateDeviceStatus(); // Initiales Update
        }

        private void UpdateDeviceStatus()
        {
            // Sicherstellen, dass Disabled-Flags aus INI geladen sind (falls jeweilige Admin-Form noch nie geöffnet wurde)
            try
            {
                // NV200/1
                try
                {
                    var v = IniHelper.ReadValue("NV200/1", "Disabled", _iniPath);
                    if (!string.IsNullOrWhiteSpace(v))
                        AdminNV200Form.DeviceDisabled = v.Trim().Equals("1", StringComparison.OrdinalIgnoreCase) || v.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                }
                catch { }
                // NV200/2
                try
                {
                    var v = IniHelper.ReadValue("NV200/2", "Disabled", _iniPath);
                    if (!string.IsNullOrWhiteSpace(v))
                        AdminNV200_2Form.DeviceDisabled = v.Trim().Equals("1", StringComparison.OrdinalIgnoreCase) || v.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                }
                catch { }
                // SmartCoin/1
                try
                {
                    var v = IniHelper.ReadValue("SmartCoin/1", "Disabled", _iniPath);
                    if (!string.IsNullOrWhiteSpace(v))
                        AdminCoinForm.DeviceDisabled = v.Trim().Equals("1", StringComparison.OrdinalIgnoreCase) || v.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                }
                catch { }
                // SmartCoin/2
                try
                {
                    var v = IniHelper.ReadValue("SmartCoin/2", "Disabled", _iniPath);
                    if (!string.IsNullOrWhiteSpace(v))
                        AdminCoin2Form.DeviceDisabled = v.Trim().Equals("1", StringComparison.OrdinalIgnoreCase) || v.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                }
                catch { }
            }
            catch { }

            // Status f?r NV200/1
            if (_ssp != null && btnNV200_1 != null)
            {
                string status = _ssp.states ?? "-";
                if (AdminNV200Form.DeviceDisabled) status = "deaktiviert";
                else if (!string.IsNullOrWhiteSpace(status) && status.IndexOf("unlizenziert", StringComparison.OrdinalIgnoreCase) >= 0) status = "keine Lizenz";
                btnNV200_1.Text = $"Scheinautomat NV200/1\nStatus: {status}";
                if (!AdminNV200Form.DeviceDisabled)
                    TryApplyBezel(Program.NV200Instance ?? _ssp, status, isNv2: false);
            }

            // Status f?r NV200/2
            var nv2 = Program.NV2002Instance;
            if (nv2 != null && btnNV200_2 != null)
            {
                string status2 = nv2.states ?? "-";
                if (AdminNV200_2Form.DeviceDisabled) status2 = "deaktiviert";
                else if (!string.IsNullOrWhiteSpace(status2) && status2.IndexOf("unlizenziert", StringComparison.OrdinalIgnoreCase) >= 0) status2 = "keine Lizenz";
                btnNV200_2.Text = $"Scheinautomat NV200/2\nStatus: {status2}";
                if (!AdminNV200_2Form.DeviceDisabled)
                    TryApplyBezel(nv2, status2, isNv2: true);
            }
            else if (btnNV200_2 != null)
            {
                btnNV200_2.Text = "Scheinautomat NV200/2\nStatus: -";
                TryApplyBezel(null, null, isNv2: true);
            }

            // Münzprüfer/1
            if (btnCoinAdmin != null)
            {
                var coin1 = CoinManager.Instance;
                string zustand1 = null;
                if (AdminCoinForm.DeviceDisabled)
                {
                    zustand1 = "deaktiviert";
                }
                else if (coin1 != null)
                {
                    zustand1 = (coin1 as TaMi_Einzahlautomat.Coins.SmartCoinV1)?.CurrentStatus;
                }
                if (!string.IsNullOrWhiteSpace(zustand1) && zustand1.IndexOf("unlizenziert", StringComparison.OrdinalIgnoreCase) >= 0)
                    zustand1 = "keine Lizenz";
                if (string.IsNullOrWhiteSpace(zustand1))
                    zustand1 = (coin1 != null && coin1.Connected) ? "Verbunden" : "Nicht verbunden";
                btnCoinAdmin.Text = $"Münzprüfer/1\nZustand: {zustand1}";
            }

            // Münzprüfer/2
            if (btnCoinAdmin2 != null)
            {
                if (Coin2Manager.Instance == null)
                {
                    try { Coin2Manager.InitFromIni(AppSettings.IniPath); } catch { }
                }
                var coin2 = Coin2Manager.Instance;
                string zustand2 = null;
                if (AdminCoin2Form.DeviceDisabled)
                {
                    zustand2 = "deaktiviert";
                }
                else if (coin2 != null)
                {
                    zustand2 = (coin2 as TaMi_Einzahlautomat.Coins.SmartCoinV1)?.CurrentStatus;
                }
                if (!string.IsNullOrWhiteSpace(zustand2) && zustand2.IndexOf("unlizenziert", StringComparison.OrdinalIgnoreCase) >= 0)
                    zustand2 = "keine Lizenz";
                if (string.IsNullOrWhiteSpace(zustand2))
                    zustand2 = (coin2 != null && coin2.Connected) ? "Verbunden" : "Nicht verbunden";
                btnCoinAdmin2.Text = $"Münzprüfer/2\nZustand: {zustand2}";
            }
        }

        // Immer Abmelden erzwingen � unabh�ngig von AdminMode
        private void ForceLogoutFromAbrechnung()
        {
            try
            {
                AbrechnungForm target = null;
                foreach (Form f in Application.OpenForms)
                {
                    if (f is AbrechnungForm af) { target = af; break; }
                }
                if (target == null)
                {
                    MessageBox.Show(this, "Keine Abrechnungsmaske geöffnet.", "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                // Versuche bevorzugt �ffentliche ForceAbmeldenFromAdminAsync (falls vorhanden)
                var mi = target.GetType().GetMethod("ForceAbmeldenFromAdminAsync", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (mi != null)
                {
                    try { mi.Invoke(target, null); return; } catch { }
                }
                // Fallback: Nutzer informieren falls Methode nicht existiert (�ltere Version)
                MessageBox.Show(this, "Diese Version der Abrechnungsmaske unterstützt das erzwungene Abmelden nicht.", "Abmelden", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Abmelden fehlgeschlagen: " + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void TryApplyBezel(NV200_SSP dev, string state, bool isNv2)
        {
            try
            {
                if (dev == null) { if (isNv2) _lastNv2Bezel = null; else _lastNv1Bezel = null; return; }
                var t = (state ?? string.Empty).ToLowerInvariant();
                int r = 0, g = 0, b = 0;
                // Map states -> colors
                if (t.Contains("idle") || t.Contains("bereit") || t.Contains("started") || t.Contains("connected") || t.Contains("synchron"))
                {
                    // Gr�n = bereit/idle
                    r = 0; g = 255; b = 0;
                }
                else if (t.Contains("disabled") || t.Contains("jammed") || t.Contains("halted") || t.Contains("failed") || t.Contains("timeout") || t.Contains("open sspcomport") || t.Contains("neustart") || t.Contains("unlizenziert") || t.Contains("keine lizenz"))
                {
                    // Rot = gesperrt/Fehler
                    r = 255; g = 0; b = 0;
                }
                else if (t.Contains("dispens") || t.Contains("stack") || t.Contains("reading") || t.Contains("read note") || t.Contains("bez"))
                {
                    // Orange = aktiv/busy
                    r = 255; g = 140; b = 0;
                }
                else
                {
                    // Standard: dezent Blau
                    r = 0; g = 64; b = 255;
                }

                string sig = $"{r},{g},{b}";
                if (isNv2)
                {
                    if (_lastNv2Bezel == sig) return;
                    _lastNv2Bezel = sig;
                }
                else
                {
                    if (_lastNv1Bezel == sig) return;
                    _lastNv1Bezel = sig;
                }

                // Anwenden (API erwartet byte)
                dev.ConfigureBezel((byte)r, (byte)g, (byte)b);
            }
            catch { }
        }

        private void InitExeInfoLabel()
        {
            try
            {
                if (_lblExeInfo == null)
                {
                    _lblExeInfo = new Label
                    {
                        AutoSize = false,
                        Height = 40,
                        Width = ClientSize.Width - 12,
                        Location = new Point(6, ClientSize.Height - 42),
                        Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                        TextAlign = ContentAlignment.MiddleLeft,
                        Font = new Font("Segoe UI", 8.25F, FontStyle.Regular),
                        ForeColor = Color.FromArgb(90,90,90),
                        BackColor = Color.White
                    };
                    Controls.Add(_lblExeInfo);
                    _lblExeInfo.BringToFront();
                    this.Resize += (s, e) =>
                    {
                        try
                        {
                            _lblExeInfo.Width = ClientSize.Width - 12;
                            _lblExeInfo.Top = ClientSize.Height - 42;
                            _lblExeInfo.BringToFront();
                        }
                        catch { }
                    };
                }
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                var ver = asm.GetName().Version;
                DateTime dt;
                try { dt = System.IO.File.GetLastWriteTime(Application.ExecutablePath); } catch { dt = DateTime.Now; }
                string verStr = ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}.{ver.Revision}" : "-";
                string systemId = LicenseManager.CurrentSystemId;
                _lblExeInfo.Text = string.IsNullOrWhiteSpace(systemId)
                    ? $"Version {verStr}  |  Build {dt:dd.MM.yyyy HH:mm}"
                    : $"Version {verStr}  |  Build {dt:dd.MM.yyyy HH:mm}{Environment.NewLine}SystemId {systemId}";
                _lblExeInfo.BringToFront();
            }
            catch { }
        }

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);
    }
}