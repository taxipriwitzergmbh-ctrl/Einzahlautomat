using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Drawing.Drawing2D;

namespace Geldautomat
{
    public class AuthForm : Form
    {
        private readonly int _pid;
        private readonly PersonalInfo _personal;
        private readonly NV200_SSP _ssp;
        private readonly Action<PersonalInfo> _onSuccessOpenTarget;

        private Panel _header;
        private Label _title;
        private Label _lblInfo;
        private TextBox _txtCode;
        private Button _btnOk;
        private Button _btnCancel;
        private Panel _numPadPanel;
        private PictureBox _picNfc;
        private bool _requireNfc;
        private bool _allowCode;

        private TextBox _txtNfcHidden;
        private string _nfcBuffer = string.Empty;
        private Timer _nfcIdleTimer;
        private const int NfcIdleTimeoutMs = 800;

        public AuthForm(int pid, PersonalInfo personal, NV200_SSP ssp, Action<PersonalInfo> onSuccessOpenTarget)
        {
            _pid = pid;
            _personal = personal;
            _ssp = ssp;
            _onSuccessOpenTarget = onSuccessOpenTarget;
            DetermineAuthMode();
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
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ClientSize = _requireNfc ? new Size(500, 700) : new Size(520, 420);
            BackColor = Color.White;
            DoubleBuffered = true;
            KeyPreview = true;
            try { AcceptButton = null; CancelButton = null; } catch { }
            try { Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, Width, Height, 18, 18)); } catch { }

            _header = new Panel { Dock = DockStyle.Top, Height = 72 };
            _header.Paint += (s, e) => { using (var brush = new LinearGradientBrush(_header.ClientRectangle, Color.FromArgb(33, 150, 243), Color.FromArgb(33, 203, 243), 0f)) e.Graphics.FillRectangle(brush, _header.ClientRectangle); };
            Controls.Add(_header);

            _title = new Label { Text = _requireNfc ? "Bitte erneut authentifizieren" : "Verifizierung", AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Variable", 20F, FontStyle.Bold), ForeColor = Color.White, Dock = DockStyle.Fill, Padding = new Padding(16, 0, 0, 0), BackColor = Color.Transparent };
            _header.Controls.Add(_title);

            int contentTop = _header.Bottom + 12;
            _lblInfo = new Label { Text = $"Mitarbeiter: {_personal?.Vorname} {_personal?.Name}", AutoSize = false, Location = new Point(24, contentTop), Size = new Size(ClientSize.Width - 48, 30), Font = new Font("Segoe UI", 12F, FontStyle.Bold) };
            Controls.Add(_lblInfo);

            _txtCode = new TextBox { Location = new Point(24, _lblInfo.Bottom + 12), Size = new Size(ClientSize.Width - 48, 46), Font = new Font("Segoe UI Variable", 18F), UseSystemPasswordChar = true, TextAlign = HorizontalAlignment.Center, Visible = _allowCode };
            Controls.Add(_txtCode);

            _numPadPanel = new Panel { Location = new Point(24, _txtCode.Bottom + 12), Size = new Size(ClientSize.Width - 48, 220), Visible = _allowCode };
            Controls.Add(_numPadPanel);
            if (_allowCode) BuildNumPad();

            _txtNfcHidden = new TextBox { Visible = false, TabStop = false, Size = new Size(1, 1), Location = new Point(-100, -100) };
            Controls.Add(_txtNfcHidden);

            if (_requireNfc || !_allowCode)
            {
                int topY = _lblInfo.Bottom + 12;
                int availableH = ClientSize.Height - topY - 80;
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

                if (_requireNfc)
                {
                    _numPadPanel.Visible = false;
                    _txtCode.Visible = false;
                    try { ActiveControl = _txtNfcHidden; _txtNfcHidden.Focus(); } catch { }
                }
            }

            _btnOk = new Button { Text = "Weiter", Location = new Point(ClientSize.Width - 240, ClientSize.Height - 64), Size = new Size(110, 44), BackColor = Color.FromArgb(76, 175, 80), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            _btnOk.FlatAppearance.BorderSize = 0; _btnOk.TabStop = false; _btnOk.Click += async (s, e) => await TryCompleteAsyncByCode(); _btnOk.Visible = _allowCode; Controls.Add(_btnOk);

            _btnCancel = new Button { Text = "Abbrechen", Location = new Point(ClientSize.Width - 120, ClientSize.Height - 64), Size = new Size(110, 44), BackColor = Color.FromArgb(229, 57, 53), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            _btnCancel.FlatAppearance.BorderSize = 0; _btnCancel.TabStop = false; _btnCancel.DialogResult = DialogResult.None; _btnCancel.Click += (s, e) => Close(); Controls.Add(_btnCancel);

            KeyDown += DocumentsAuthForm_KeyDown;
            KeyPress += DocumentsAuthForm_KeyPress;
            _nfcIdleTimer = new Timer { Interval = NfcIdleTimeoutMs };
            _nfcIdleTimer.Tick += (s, e) => { _nfcIdleTimer.Stop(); _nfcBuffer = string.Empty; };

            try { if (Program.CoinFeeder != null) Program.CoinFeeder.NfcReceived += OnCoinFeederNfc; } catch { }
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
            int btnW = 100, btnH = 56, pad = 12;
            string[] keys = { "1","2","3","4","5","6","7","8","9","?","0","OK" };
            for (int i = 0; i < keys.Length; i++)
            {
                int r = i / 3, c = i % 3;
                var b = new Button { Text = keys[i], Font = new Font("Segoe UI Variable", 16F, FontStyle.Bold), Size = new Size(btnW, btnH), Location = new Point(c * (btnW + pad), r * (btnH + pad)), BackColor = Color.FromArgb(245, 247, 250), FlatStyle = FlatStyle.Flat, Tag = keys[i] };
                b.FlatAppearance.BorderSize = 0;
                b.Click += (s, e) =>
                {
                    var key = (string)((Button)s).Tag;
                    if (key == "OK") { _btnOk.PerformClick(); return; }
                    if (key == "?") { if (_txtCode.Text.Length > 0) _txtCode.Text = _txtCode.Text.Substring(0, _txtCode.Text.Length - 1); return; }
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

        private void DocumentsAuthForm_KeyDown(object sender, KeyEventArgs e)
        {
            if ((_requireNfc || !_allowCode) && e.Control && e.KeyCode == Keys.V)
            {
                try { var clip = Clipboard.GetText(); if (!string.IsNullOrWhiteSpace(clip)) { _nfcBuffer = clip.Trim(); _nfcIdleTimer.Stop(); } } catch { }
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

        private void DocumentsAuthForm_KeyPress(object sender, KeyPressEventArgs e)
        {
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

        [System.Runtime.InteropServices.DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);
    }
}
