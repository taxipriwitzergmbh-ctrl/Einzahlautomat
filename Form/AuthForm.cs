using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Drawing.Drawing2D;
using TaMi_Einzahlautomat.UI.Layout;

namespace TaMi_Einzahlautomat
{
    public class AuthForm : Form
    {
        private readonly int _pid;
        private readonly PersonalInfo _personal;
        private readonly NV200_SSP _ssp;
        private readonly Action<PersonalInfo> _onSuccessOpenTarget;

        private ModernHeaderPanel _header;
        private Label _lblInfo;
        private TextBox _txtCode;
        private ModernGradientButton _btnOk;
        private ModernGradientButton _btnCancel;
        private Panel _numPadPanel;
        private PictureBox _picNfc;
        private bool _requireNfc;
        private bool _allowCode;

        private TextBox _txtNfcHidden;
        private string _nfcBuffer = string.Empty;
        private Timer _nfcIdleTimer;
        private const int NfcIdleTimeoutMs = 800;
        private bool _nfcCollectMode = false; // collect full token (letters+digits)

        private Timer _tmrAutoClose;
        private int _autoCloseRemainingSec;
        private int _autoCloseConfiguredSec;
        private Label _lblHeaderCountdown;

        public AuthForm(int pid, PersonalInfo personal, NV200_SSP ssp, Action<PersonalInfo> onSuccessOpenTarget)
        {
            _pid = pid;
            _personal = personal;
            _ssp = ssp;
            _onSuccessOpenTarget = onSuccessOpenTarget;
            DetermineAuthMode();
            try
            {
                int sec = 0;
                var raw = IniHelper.ReadValue("UI", "AutoCloseDocsHoursSec", AppSettings.IniPath);
                if (!string.IsNullOrWhiteSpace(raw)) int.TryParse(raw.Trim(), out sec);
                if (sec < 0) sec = 0;
                _autoCloseConfiguredSec = sec;
                _autoCloseRemainingSec = sec;
            }
            catch { _autoCloseRemainingSec = 0; }
            BuildUi();
        }

        private void DetermineAuthMode()
        {
            _requireNfc = false; _allowCode = true;
            try
            {
                var onlyNfcRaw = IniHelper.ReadValue("Device", "OnlyNFC", AppSettings.IniPath);
                bool onlyNfc = !string.IsNullOrWhiteSpace(onlyNfcRaw) && (onlyNfcRaw.Equals("true", StringComparison.OrdinalIgnoreCase) || onlyNfcRaw.Equals("1") || onlyNfcRaw.Equals("yes", StringComparison.OrdinalIgnoreCase) || onlyNfcRaw.Equals("on", StringComparison.OrdinalIgnoreCase));
                if (onlyNfc) { _requireNfc = true; _allowCode = false; }
            }
            catch { }
        }

        private void BuildUi()
        {
            try { SuspendLayout(); } catch { }
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ClientSize = _allowCode ? new Size(520, 740) : (_requireNfc ? new Size(500, 700) : new Size(520, 420));
            BackColor = Color.White;
            DoubleBuffered = true;
            KeyPreview = true;
            try { AcceptButton = null; CancelButton = null; } catch { }

            string titleText = _requireNfc ? "Bitte erneut authentifizieren" : (_allowCode ? "Passwort eingeben / NFC vorhalten" : "Verifizierung");
            _header = new ModernHeaderPanel { Title = titleText, ShowMinimize = false };
            _header.CloseClicked += () => { try { Close(); } catch { } };
            Controls.Add(_header);

            // Countdown links neben dem X
            try
            {
                _lblHeaderCountdown = new Label
                {
                    AutoSize = false,
                    Size = new Size(86, _header.Height),
                    TextAlign = ContentAlignment.MiddleRight,
                    Font = new Font("Segoe UI Variable", 10F, FontStyle.Bold),
                    ForeColor = Color.White,
                    BackColor = Color.Transparent,
                    Visible = false
                };
                _header.Controls.Add(_lblHeaderCountdown);
                _header.Controls.SetChildIndex(_lblHeaderCountdown, 0);
                _header.Resize += (s, e) =>
                {
                    try
                    {
                        int rightPad = 74; // mehr Platz, damit Text nicht vom X überdeckt wird
                        _lblHeaderCountdown.Location = new Point(Math.Max(0, _header.Width - rightPad - _lblHeaderCountdown.Width - 10), 0);
                        _lblHeaderCountdown.Height = _header.Height;
                    }
                    catch { }
                };
                try
                {
                    int rightPad = 74;
                    _lblHeaderCountdown.Location = new Point(Math.Max(0, _header.Width - rightPad - _lblHeaderCountdown.Width - 10), 0);
                    _lblHeaderCountdown.Height = _header.Height;
                }
                catch { }
            }
            catch { }

            try
            {
                _header.ApplyRoundedRegionToForm(this);
                SizeChanged += (s, e) => { try { _header.ApplyRoundedRegionToForm(this); } catch { } };
            }
            catch { }

            int contentTop = _header.Bottom + 12;
            _lblInfo = new Label { Text = $"Mitarbeiter: {_personal?.Vorname} {_personal?.Name}", AutoSize = false, Location = new Point(24, contentTop), Size = new Size(ClientSize.Width - 48, 30), Font = new Font("Segoe UI", 12F, FontStyle.Bold) };
            Controls.Add(_lblInfo);

            // TextBox initial unsichtbar bauen, um ein kurzes "Aufblinken" links beim ersten Paint zu vermeiden.
            // Sie wird in `OnShown` nach finalem Relayout wieder eingeblendet.
            _txtCode = new TextBox { Location = new Point(24, _lblInfo.Bottom + 12), Size = new Size(ClientSize.Width - 48, 50), Font = new Font("Segoe UI Variable", 20F), UseSystemPasswordChar = true, TextAlign = HorizontalAlignment.Center, Visible = false };
            Controls.Add(_txtCode);

            int padTop = _txtCode.Bottom + 12;
            // Panel-Höhe so wählen, dass die letzte Tastenreihe nicht abgeschnitten wird
            int numPadBtnW = 110, numPadBtnH = 60, numPadGap = 12;
            int numPadRows = 4;
            int numPadHeight = numPadRows * numPadBtnH + (numPadRows - 1) * numPadGap;
            // Numpad initial unsichtbar bauen, um ein kurzes "Aufblinken" links beim ersten Paint zu vermeiden.
            // Es wird in `OnShown` nach finalem Relayout wieder eingeblendet.
            _numPadPanel = new Panel { Location = new Point(24, padTop), Size = new Size(ClientSize.Width - 48, numPadHeight), Visible = false };
            Controls.Add(_numPadPanel);
            if (_allowCode) BuildNumPad();

            _txtNfcHidden = new TextBox { Visible = false, TabStop = false, Size = new Size(1, 1), Location = new Point(-100, -100) };
            Controls.Add(_txtNfcHidden);

            if (_requireNfc || !_allowCode)
            {
                int topY = _lblInfo.Bottom + 12;
                int availableH = ClientSize.Height - topY - 100;
                int availableW = ClientSize.Width - 48;
                if (availableH < 100) availableH = ClientSize.Height - _header.Height - 40;
                if (availableW < 100) availableW = ClientSize.Width;
                _picNfc = new PictureBox
                {
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BackColor = Color.White,
                    Location = new Point(24, topY),
                    Size = new Size(availableW, availableH),
                    Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                    Visible = true
                };
                Controls.Add(_picNfc);
                try
                {
                    string baseDir = Application.StartupPath;
                    string p1 = Path.Combine(baseDir, "Resources", "NFC.png");
                    string p2 = Path.Combine(baseDir, "Ressourcen", "NFC.png");
                    string chosen = File.Exists(p1) ? p1 : (File.Exists(p2) ? p2 : null);
                    if (chosen != null) { using (var img = Image.FromFile(chosen)) _picNfc.Image = new Bitmap(img); }
                }
                catch { }

                // Fallback: wenn keine Datei vorhanden ist, das eingebettete Resource-Bild benutzen
                try
                {
                    if (_picNfc.Image == null)
                    {
                        _picNfc.Image = TaMi_Einzahlautomat.Properties.Resources.NFC;
                    }
                }
                catch { }

                if (_requireNfc)
                {
                    _numPadPanel.Visible = false;
                    _txtCode.Visible = false;
                    try { ActiveControl = _txtNfcHidden; _txtNfcHidden.Focus(); } catch { }
                }
            }

            // Centered buttons
            int contentBottom = 0;
            try
            {
                if (_allowCode) contentBottom = _numPadPanel.Bottom;
                else if (_picNfc != null) contentBottom = _picNfc.Bottom;
                else contentBottom = _lblInfo.Bottom;
            }
            catch { contentBottom = _allowCode ? _numPadPanel.Bottom : _lblInfo.Bottom; }

            int buttonsTop = contentBottom + 18;
            int btnW = 140, btnH = 44;
            int totalW = btnW * 2 + 20;
            int startX = (ClientSize.Width - totalW) / 2;

            _btnOk = new ModernGradientButton { Text = "Weiter", Location = new Point(startX, buttonsTop), Size = new Size(btnW, btnH), GradientStart = UiTheme.SuccessStart, GradientEnd = UiTheme.SuccessEnd, TabStop = false };
            _btnOk.Click += async (s, e) => await TryCompleteAsyncByCode();
            _btnOk.Visible = _allowCode;
            Controls.Add(_btnOk);

            _btnCancel = new ModernGradientButton { Text = "Abbrechen", Location = new Point(startX + btnW + 20, buttonsTop), Size = new Size(btnW, btnH), GradientStart = UiTheme.DangerStart, GradientEnd = UiTheme.DangerEnd, TabStop = false };
            _btnCancel.Click += (s, e) => { try { Close(); } catch { } };
            Controls.Add(_btnCancel);

            // Falls durch Header/Fonts weniger Platz ist, Buttons nach oben schieben, damit nichts abgeschnitten wird
            try
            {
                int bottomMargin = 18;
                int overflow = (_btnCancel.Bottom + bottomMargin) - ClientSize.Height;
                if (overflow > 0)
                {
                    _btnOk.Top = Math.Max(_header.Bottom + 10, _btnOk.Top - overflow);
                    _btnCancel.Top = _btnOk.Top;
                }
            }
            catch { }

            // Wenn Buttons in den sichtbaren Content reinragen (z.B. NFC-Bild), Buttons zusätzlich nach unten/oben korrigieren
            try
            {
                int minTop = contentBottom + 12;
                if (_btnCancel.Top < minTop)
                {
                    _btnOk.Top = minTop;
                    _btnCancel.Top = minTop;
                }

                // sicherstellen, dass sie niemals unten abgeschnitten sind
                int bottomMargin = 18;
                int overflow2 = (_btnCancel.Bottom + bottomMargin) - ClientSize.Height;
                if (overflow2 > 0)
                {
                    _btnOk.Top = Math.Max(_header.Bottom + 10, _btnOk.Top - overflow2);
                    _btnCancel.Top = _btnOk.Top;
                }
            }
            catch { }

            KeyDown += AuthForm_KeyDown;
            KeyPress += AuthForm_KeyPress;
            _nfcIdleTimer = new Timer { Interval = NfcIdleTimeoutMs };
            _nfcIdleTimer.Tick += (s, e) => { _nfcIdleTimer.Stop(); _nfcBuffer = string.Empty; _nfcCollectMode = false; };

            try { if (Program.CoinFeeder != null) Program.CoinFeeder.NfcReceived += OnCoinFeederNfc; } catch { }

            try
            {
                this.Shown += (s, e) => StartAutoCloseIfEnabled();
                this.Activated += (s, e) => StartAutoCloseIfEnabled();
                this.Deactivate += (s, e) => { try { _tmrAutoClose?.Stop(); } catch { } };
                this.FormClosed += (s, e) => { try { _tmrAutoClose?.Stop(); _tmrAutoClose?.Dispose(); _tmrAutoClose = null; } catch { } };
            }
            catch { }
        }

        private void StartAutoCloseIfEnabled()
        {
            try
            {
                if (_autoCloseConfiguredSec <= 0) return;
                if (_autoCloseRemainingSec <= 0 || _autoCloseRemainingSec > _autoCloseConfiguredSec)
                    _autoCloseRemainingSec = _autoCloseConfiguredSec;

                try
                {
                    if (_lblHeaderCountdown != null)
                    {
                        _lblHeaderCountdown.Visible = true;
                        _lblHeaderCountdown.Text = _autoCloseRemainingSec.ToString() + "s";
                    }
                }
                catch { }

                if (_tmrAutoClose == null)
                {
                    _tmrAutoClose = new Timer { Interval = 1000 };
                    _tmrAutoClose.Tick += (s, e) =>
                    {
                        try
                        {
                            if (!Focused && Form.ActiveForm != this) { _tmrAutoClose.Stop(); return; }
                            if (_autoCloseRemainingSec > 0) _autoCloseRemainingSec--;
                            try { if (_lblHeaderCountdown != null && _lblHeaderCountdown.Visible) _lblHeaderCountdown.Text = Math.Max(0, _autoCloseRemainingSec).ToString() + "s"; } catch { }
                            if (_autoCloseRemainingSec <= 0)
                            {
                                _tmrAutoClose.Stop();
                                try { Close(); } catch { }
                            }
                        }
                        catch { }
                    };
                }
                if (!_tmrAutoClose.Enabled) _tmrAutoClose.Start();
            }
            catch { }
        }

        private void ResetAutoCloseCountdown()
        {
            try
            {
                if (_autoCloseConfiguredSec <= 0) return;
                _autoCloseRemainingSec = _autoCloseConfiguredSec;
                try { if (_lblHeaderCountdown != null && _lblHeaderCountdown.Visible) _lblHeaderCountdown.Text = _autoCloseRemainingSec.ToString() + "s"; } catch { }
                try { StartAutoCloseIfEnabled(); } catch { }
            }
            catch { }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try
            {
                Rectangle bounds;
                if (Owner != null) bounds = Owner.Bounds; else { var screen = Screen.FromControl(this) ?? Screen.PrimaryScreen; bounds = screen.WorkingArea; }
                int x = bounds.Left + (bounds.Width - Width) / 2;
                int y = bounds.Top + (bounds.Height - Height) / 2;
                Location = new Point(Math.Max(bounds.Left, x), Math.Max(bounds.Top, y));
                BringToFront(); Activate();
                if (_requireNfc && _txtNfcHidden != null) { ActiveControl = _txtNfcHidden; _txtNfcHidden.Focus(); }
            }
            catch { }

            void RelayoutCentered()
            {
                try
                {
                    int marginX = 24;
                    int contentW = ClientSize.Width - marginX * 2;
                    if (_lblInfo != null) { _lblInfo.Left = marginX; _lblInfo.Width = contentW; }
                    if (_txtCode != null) { _txtCode.Left = marginX; _txtCode.Width = contentW; }

                    if (_allowCode && _numPadPanel != null)
                    {
                        // Numpad mit fester Breite zentrieren (3 Spalten)
                        int btnW = 110, gap = 12;
                        int numPadW = btnW * 3 + gap * 2;
                        _numPadPanel.Width = Math.Min(contentW, numPadW);
                        _numPadPanel.Left = marginX + (contentW - _numPadPanel.Width) / 2;
                    }

                    // Buttons zentriert unter dem Content
                    int contentBottom = 0;
                    try
                    {
                        if (_allowCode) contentBottom = _numPadPanel.Bottom;
                        else if (_picNfc != null) contentBottom = _picNfc.Bottom;
                        else contentBottom = _lblInfo.Bottom;
                    }
                    catch { contentBottom = _allowCode ? _numPadPanel.Bottom : _lblInfo.Bottom; }

                    int buttonsTop = contentBottom + 18;
                    int btnW2 = 140, btnH2 = 44;
                    int totalW = btnW2 * 2 + 20;
                    int startX = (ClientSize.Width - totalW) / 2;

                    if (_btnOk != null)
                    {
                        _btnOk.Location = new Point(startX, buttonsTop);
                        _btnOk.Size = new Size(btnW2, btnH2);
                    }
                    if (_btnCancel != null)
                    {
                        _btnCancel.Location = new Point(startX + btnW2 + 20, buttonsTop);
                        _btnCancel.Size = new Size(btnW2, btnH2);
                    }

                    // nichts unten abschneiden
                    int bottomMargin = 18;
                    int overflow = (_btnCancel.Bottom + bottomMargin) - ClientSize.Height;
                    if (overflow > 0)
                    {
                        int newTop = Math.Max(_header.Bottom + 10, _btnCancel.Top - overflow);
                        if (_btnOk != null) _btnOk.Top = newTop;
                        _btnCancel.Top = newTop;
                    }
                }
                catch { }
            }

            try { RelayoutCentered(); } catch { }
            try
            {
                if (_allowCode)
                {
                    if (_txtCode != null) _txtCode.Visible = true;
                    if (_numPadPanel != null) _numPadPanel.Visible = true;
                }
            }
            catch { }

            try { ResumeLayout(true); } catch { }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            try
            {
                // Vor dem ersten Anzeigen layouten, damit beim initialen Paint nichts "links" aufblitzt
                void RelayoutCentered_PrePaint()
                {
                    try
                    {
                        int marginX = 24;
                        int contentW = ClientSize.Width - marginX * 2;
                        if (_allowCode && _numPadPanel != null)
                        {
                            int btnW = 110, gap = 12;
                            int numPadW = btnW * 3 + gap * 2;
                            _numPadPanel.Width = Math.Min(contentW, numPadW);
                            _numPadPanel.Left = marginX + (contentW - _numPadPanel.Width) / 2;
                        }
                    }
                    catch { }
                }

                RelayoutCentered_PrePaint();
                Resize += (s, ev) => { try { RelayoutCentered_PrePaint(); } catch { } };
            }
            catch { }
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            try { if (_requireNfc && _txtNfcHidden != null) { ActiveControl = _txtNfcHidden; _txtNfcHidden.Focus(); } } catch { }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            try { if (Program.CoinFeeder != null) Program.CoinFeeder.NfcReceived -= OnCoinFeederNfc; } catch { }
        }

        private void BuildNumPad()
        {
            try { _numPadPanel.SuspendLayout(); } catch { }
            int btnW = 110, btnH = 60, pad = 12;
            string[] keys = { "1","2","3","4","5","6","7","8","9","←","0","OK" };
            int cols = 3;
            for (int i = 0; i < keys.Length; i++)
            {
                int r = i / cols, c = i % cols;
                var keyText = keys[i];
                var b = new ModernGradientButton
                {
                    Text = keyText,
                    Font = new Font("Segoe UI Variable", 18F, FontStyle.Bold),
                    Size = new Size(btnW, btnH),
                    Location = new Point(c * (btnW + pad), r * (btnH + pad)),
                    Tag = keyText,
                    TabStop = false,
                    GradientStart = UiTheme.PrimaryStart,
                    GradientEnd = UiTheme.PrimaryEnd,
                    ForeColor = Color.White
                };
                if (keyText == "←")
                {
                    b.GradientStart = UiTheme.SecondaryStart;
                    b.GradientEnd = UiTheme.SecondaryEnd;
                }
                else if (keyText == "OK")
                {
                    b.GradientStart = UiTheme.SuccessStart;
                    b.GradientEnd = UiTheme.SuccessEnd;
                }
                b.Click += (s, e) =>
                {
                    var key = (string)((Button)s).Tag;
                    if (key == "OK") { _btnOk.PerformClick(); return; }
                    if (key == "←") { if (_txtCode.Text.Length > 0) _txtCode.Text = _txtCode.Text.Substring(0, _txtCode.Text.Length - 1); return; }
                    if (_txtCode.Text.Length < 8) _txtCode.Text += key;
                    _txtCode.Focus(); _txtCode.SelectionStart = _txtCode.Text.Length;
                };
                _numPadPanel.Controls.Add(b);
            }
        }

        private async void OnCoinFeederNfc(string token)
        {
            if (!(_requireNfc || !_allowCode)) return;
            if (string.IsNullOrWhiteSpace(token)) return;
            try { ResetAutoCloseCountdown(); } catch { }
            try { if (InvokeRequired) { BeginInvoke((Action)(async () => await TryCompleteAsyncByNfc(token))); return; } await TryCompleteAsyncByNfc(token); } catch { }
        }

        private async System.Threading.Tasks.Task TryCompleteAsyncByCode()
        {
            if (!_allowCode) return;
            string input = (_txtCode.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(input)) return;
            try
            {
                using (var db = new DatabaseHelper())
                {
                    var expected = await db.GetFahrercodeAsync(_pid);
                    if (string.IsNullOrWhiteSpace(expected)) { MessageBox.Show(this, "Kein Fahrercode hinterlegt.", "Verifizierung", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                    if (!string.Equals(input, expected)) { MessageBox.Show(this, "Code falsch.", "Verifizierung", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                    OpenTarget();
                }
            }
            catch (Exception ex) { MessageBox.Show(this, "Fehler: " + ex.Message, "Verifizierung", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private async System.Threading.Tasks.Task TryCompleteAsyncByNfc(string token)
        {
            try
            {
                using (var db = new DatabaseHelper())
                {
                    var p = await db.GetPersonalByNfcAsync(token);
                    if (p == null || p.PID != _pid) { MessageBox.Show(this, "NFC nicht erkannt.", "Verifizierung", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                    var status = await db.GetPersonalStatusAsync(_pid);
                    if (status == null || status.Gesperrt) { MessageBox.Show(this, "Zugang gesperrt.", "Verifizierung", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                    OpenTarget();
                }
            }
            catch (Exception ex) { MessageBox.Show(this, "Fehler: " + ex.Message, "Verifizierung", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void OpenTarget()
        {
            try
            {
                if (_onSuccessOpenTarget != null)
                {
                    var personal = _personal;
                    Close();
                    try { BeginInvoke((Action)(() => { try { _onSuccessOpenTarget(personal); } catch { } })); }
                    catch { try { _onSuccessOpenTarget(personal); } catch { } }
                    return;
                }
                Close();
            }
            catch { }
        }

        private void AuthForm_KeyDown(object sender, KeyEventArgs e)
        {
            try { ResetAutoCloseCountdown(); } catch { }
            if ((_requireNfc || !_allowCode) && e.Control && e.KeyCode == Keys.V)
            {
                try { var clip = Clipboard.GetText(); if (!string.IsNullOrWhiteSpace(clip)) { _nfcBuffer = clip.Trim(); _nfcCollectMode = true; _nfcIdleTimer.Stop(); } } catch { }
            }

            // Start NFC collect mode when any letter typed
            if (e.KeyCode >= Keys.A && e.KeyCode <= Keys.Z)
            {
                char ch = (char)('A' + (e.KeyCode - Keys.A));
                if (_nfcBuffer.Length < 128) _nfcBuffer += ch;
                _nfcCollectMode = true; _nfcIdleTimer.Stop(); _nfcIdleTimer.Start(); e.SuppressKeyPress = true; return;
            }

            // NEW: also start NFC collect mode when any digit typed (top row or numpad)
            if ((e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9) || (e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.NumPad9))
            {
                char ch = '\0';
                if (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9) ch = (char)('0' + (e.KeyCode - Keys.D0));
                else if (e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.NumPad9) ch = (char)('0' + (e.KeyCode - Keys.NumPad0));
                if (ch != '\0' && _nfcBuffer.Length < 128) _nfcBuffer += ch;
                _nfcCollectMode = true; _nfcIdleTimer.Stop(); _nfcIdleTimer.Start(); e.SuppressKeyPress = true; return;
            }

            if (_nfcCollectMode)
            {
                if (e.KeyCode == Keys.Back)
                {
                    if (_nfcBuffer.Length > 0) _nfcBuffer = _nfcBuffer.Substring(0, _nfcBuffer.Length - 1);
                    e.SuppressKeyPress = true; return;
                }
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Return)
                {
                    string token = _nfcBuffer.Trim(); _nfcIdleTimer.Stop(); _nfcBuffer = string.Empty; _nfcCollectMode = false;
                    e.SuppressKeyPress = true;
                    if (token.Length > 0) { TryCompleteAsyncByNfc(token).ConfigureAwait(false); }
                    return;
                }
            }

            if (_requireNfc || !_allowCode)
            {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Return)
                {
                    string token = _nfcBuffer.Trim(); _nfcIdleTimer.Stop(); _nfcBuffer = string.Empty;
                    e.SuppressKeyPress = true;
                    if (token.Length > 0) { TryCompleteAsyncByNfc(token).ConfigureAwait(false); }
                    return;
                }
                if ((e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.Z) || (e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.NumPad9))
                {
                    char ch = '\0';
                    if (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9) ch = (char)('0' + (e.KeyCode - Keys.D0));
                    else if (e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.NumPad9) ch = (char)('0' + (e.KeyCode - Keys.NumPad0));
                    else if (e.KeyCode >= Keys.A && e.KeyCode <= Keys.Z) ch = (char)('A' + (e.KeyCode - Keys.A));
                    if (ch != '\0' && _nfcBuffer.Length < 128) _nfcBuffer += ch;
                    _nfcIdleTimer.Stop(); _nfcIdleTimer.Start(); e.SuppressKeyPress = true; return;
                }
            }
            if (_allowCode)
            {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Return) { _btnOk.PerformClick(); e.SuppressKeyPress = true; return; }
            }
            e.SuppressKeyPress = true;
        }

        private void AuthForm_KeyPress(object sender, KeyPressEventArgs e)
        {
            try { ResetAutoCloseCountdown(); } catch { }
            // Letters or digits begin/continue NFC buffer and enable collect mode
            if (char.IsLetterOrDigit(e.KeyChar))
            {
                if (_nfcBuffer.Length < 128) _nfcBuffer += char.ToUpperInvariant(e.KeyChar);
                _nfcCollectMode = true; _nfcIdleTimer.Stop(); _nfcIdleTimer.Start(); e.Handled = true; return;
            }

            if (_nfcCollectMode)
            {
                if (e.KeyChar == '\r' || e.KeyChar == '\n')
                {
                    var token = _nfcBuffer.Trim(); _nfcIdleTimer.Stop(); _nfcBuffer = string.Empty; _nfcCollectMode = false;
                    e.Handled = true; if (token.Length > 0) { TryCompleteAsyncByNfc(token).ConfigureAwait(false); }
                    return;
                }
                if (e.KeyChar == '\b') { e.Handled = true; return; }
            }

            if (_requireNfc || !_allowCode)
            {
                char ch = e.KeyChar;
                if (ch == '\r' || ch == '\n')
                {
                    var token = _nfcBuffer.Trim(); _nfcIdleTimer.Stop(); _nfcBuffer = string.Empty;
                    e.Handled = true;
                    if (token.Length > 0) { TryCompleteAsyncByNfc(token).ConfigureAwait(false); }
                    return;
                }
                if (!char.IsControl(ch))
                {
                    if (_nfcBuffer.Length < 128) _nfcBuffer += ch;
                    _nfcIdleTimer.Stop(); _nfcIdleTimer.Start(); e.Handled = true; return;
                }
            }
            if (_allowCode)
            {
                if (e.KeyChar == '\r' || e.KeyChar == '\n') { _btnOk.PerformClick(); e.Handled = true; return; }
                if (char.IsDigit(e.KeyChar)) { e.Handled = false; return; }
                if (e.KeyChar == '\b') { e.Handled = false; return; }
            }
            e.Handled = true;
        }

        
    }
}
