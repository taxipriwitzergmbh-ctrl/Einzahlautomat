using System;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Drawing.Drawing2D;

namespace Geldautomat
{
    public class GeneralSettingsForm : Form
    {
        private Panel _headerPanel;
        private Label _lblTitle;
        private Button _btnHeaderClose;
        private Point _mouseDownLocation;

        private CheckBox _chkOnlyNfc;
        private CheckBox _chkHideRemote; // NEU: Fernwartungsbutton ausblenden
        private Label _lblRestartInfo;
        private TextBox _txtMaintPwd;
        private TextBox _txtMaintPwd2;
        private TextBox _txtAdminBackdoor;
        private TextBox _txtAdminBackdoor2;
        private Button _btnSave;
        private Button _btnClose;

        private TextBox _txtDeviceId; // NEU
        private Label _lblDeviceHint; // NEU
        private Label _lblBusyUnlock; // NEU
        private TextBox _txtBusyUnlock; // NEU

        public GeneralSettingsForm()
        {
            BuildUi();
            LoadValues();
        }

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

        private void BuildUi()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.White;
            DoubleBuffered = true;
            Size = new Size(600, 560); // vergrößert

            // Header
            _headerPanel = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(ClientSize.Width, 60),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            _headerPanel.Paint += HeaderPanel_Paint;
            _headerPanel.MouseDown += HeaderPanel_MouseDown;
            _headerPanel.MouseMove += HeaderPanel_MouseMove;
            Controls.Add(_headerPanel);

            _lblTitle = new Label
            {
                Text = "Allgemeine Einstellungen",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Variable", 18F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(24, 0),
                Size = new Size(380, 60),
                BackColor = Color.Transparent
            };
            _headerPanel.Controls.Add(_lblTitle);

            _btnHeaderClose = new Button
            {
                Text = "\u2715",
                Font = new Font("Segoe UI Symbol", 16F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(48, 48),
                Location = new Point(ClientSize.Width - 56, 6),
                TabStop = false
            };
            _btnHeaderClose.FlatAppearance.BorderSize = 0;
            _btnHeaderClose.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 80, 80);
            _btnHeaderClose.Click += (s, e) => Close();
            _headerPanel.Controls.Add(_btnHeaderClose);

            // abgerundete Ecken
            try { Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, Width, Height, 24, 24)); } catch { }

            // Inhalte
            int left = 28;
            int y = _headerPanel.Bottom + 20;

            var lblOnly = new Label { Text = "Nur NFC Anmeldung erlauben", AutoSize = true, Location = new Point(left + 26, y), Font = new Font("Segoe UI Variable", 12F, FontStyle.Bold), BackColor = Color.Transparent };
            _chkOnlyNfc = new CheckBox { Location = new Point(left, y + 2), AutoSize = true };
            _lblRestartInfo = new Label { Text = "Neustart erforderlich", AutoSize = true, Location = new Point(left + 26, y + 22), Font = new Font("Segoe UI", 9.5f, FontStyle.Regular), ForeColor = Color.FromArgb(80,80,80), BackColor = Color.Transparent };

            Controls.Add(_chkOnlyNfc);
            Controls.Add(lblOnly);
            Controls.Add(_lblRestartInfo);

            y += 58; // etwas mehr Abstand nach NFC Block

            // Fernwartungsbutton ausblenden (Login)
            _chkHideRemote = new CheckBox { Location = new Point(left, y), AutoSize = true };
            var lblHideRemote = new Label { Text = "Fernwartungsbutton im Login ausblenden", AutoSize = true, Location = new Point(left + 26, y - 2), Font = new Font("Segoe UI Variable", 12F, FontStyle.Bold), BackColor = Color.Transparent };
            Controls.Add(_chkHideRemote);
            Controls.Add(lblHideRemote);

            y += 40;

            // Device ID (nur von SuE anzupassen)
            var lblDev = new Label { Text = "Device-ID", AutoSize = true, Location = new Point(left, y), Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold), BackColor = Color.Transparent };
            _txtDeviceId = new TextBox { Location = new Point(left, y + 26), Width = 120, Font = new Font("Segoe UI", 11F) };
            _lblDeviceHint = new Label { Text = "(nur von SuE anzupassen)", AutoSize = true, Location = new Point(_txtDeviceId.Right + 12, y + 30), Font = new Font("Segoe UI", 9F, FontStyle.Italic), ForeColor = Color.FromArgb(140,140,140) };
            Controls.Add(lblDev);
            Controls.Add(_txtDeviceId);
            Controls.Add(_lblDeviceHint);

            y += 80;

            // NEU: Timeout für Freigabe bei Gerätefehler (Sekunden)
            _lblBusyUnlock = new Label { Text = "Timeout Freigabe (Sek.) bei Gerätefehler", AutoSize = true, Location = new Point(left, y), Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold), BackColor = Color.Transparent };
            _txtBusyUnlock = new TextBox { Location = new Point(left, y + 26), Width = 120, Font = new Font("Segoe UI", 11F) };
            Controls.Add(_lblBusyUnlock);
            Controls.Add(_txtBusyUnlock);

            y += 80;

            var lblMaint = new Label { Text = "Wartungscode (Maintenance)", AutoSize = true, Location = new Point(left, y), Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold), BackColor = Color.Transparent };
            Controls.Add(lblMaint);
            y += 26;
            _txtMaintPwd = new TextBox { Location = new Point(left, y), Width = 240, UseSystemPasswordChar = true, Font = new Font("Segoe UI", 11F) };
            var lblMaint2 = new Label { Text = "Wiederholen", AutoSize = true, Location = new Point(left + 260, y - 22), Font = new Font("Segoe UI", 9.5f, FontStyle.Regular), ForeColor = Color.DimGray };
            _txtMaintPwd2 = new TextBox { Location = new Point(left + 260, y), Width = 240, UseSystemPasswordChar = true, Font = new Font("Segoe UI", 11F) };
            Controls.Add(_txtMaintPwd);
            Controls.Add(lblMaint2);
            Controls.Add(_txtMaintPwd2);

            y += 50;
            var lblAdmin = new Label { Text = "Admin-Backdoor (numerisch)", AutoSize = true, Location = new Point(left, y), Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold), BackColor = Color.Transparent };
            Controls.Add(lblAdmin);
            y += 26;
            _txtAdminBackdoor = new TextBox { Location = new Point(left, y), Width = 240, UseSystemPasswordChar = true, Font = new Font("Segoe UI", 11F) };
            var lblAdmin2 = new Label { Text = "Wiederholen", AutoSize = true, Location = new Point(left + 260, y - 22), Font = new Font("Segoe UI", 9.5f, FontStyle.Regular), ForeColor = Color.DimGray };
            _txtAdminBackdoor2 = new TextBox { Location = new Point(left + 260, y), Width = 240, UseSystemPasswordChar = true, Font = new Font("Segoe UI", 11F) };
            Controls.Add(_txtAdminBackdoor);
            Controls.Add(lblAdmin2);
            Controls.Add(_txtAdminBackdoor2);

            // Buttons unten rechts
            _btnSave = new Button { Text = "Speichern", Size = new Size(160, 48), Location = new Point(ClientSize.Width - 180 - 180, ClientSize.Height - 80), BackColor = Color.FromArgb(33, 150, 243), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI Variable", 12F, FontStyle.Bold), Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
            _btnSave.FlatAppearance.BorderSize = 0;
            _btnSave.Click += (s, e) => SaveValues();
            Controls.Add(_btnSave);

            _btnClose = new Button { Text = "Schließen", Size = new Size(160, 48), Location = new Point(ClientSize.Width - 180, ClientSize.Height - 80), BackColor = Color.FromArgb(158, 158, 158), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI Variable", 12F, FontStyle.Bold), Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
            _btnClose.FlatAppearance.BorderSize = 0;
            _btnClose.Click += (s, e) => Close();
            Controls.Add(_btnClose);
        }

        private void HeaderPanel_Paint(object sender, PaintEventArgs e)
        {
            using (var brush = new LinearGradientBrush(_headerPanel.ClientRectangle, Color.FromArgb(33, 150, 243), Color.FromArgb(33, 203, 243), 0f))
            {
                e.Graphics.FillRectangle(brush, _headerPanel.ClientRectangle);
            }
        }
        private void HeaderPanel_MouseDown(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) _mouseDownLocation = e.Location; }
        private void HeaderPanel_MouseMove(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Left += e.X - _mouseDownLocation.X; Top += e.Y - _mouseDownLocation.Y; } }

        private void LoadValues()
        {
            try
            {
                var only = IniHelper.ReadValue("Device", "OnlyNFC", AppSettings.IniPath);
                _chkOnlyNfc.Checked = !string.IsNullOrWhiteSpace(only) &&
                    (only.Equals("true", StringComparison.OrdinalIgnoreCase) || only.Equals("1") || only.Equals("yes", StringComparison.OrdinalIgnoreCase) || only.Equals("on", StringComparison.OrdinalIgnoreCase));
            }
            catch { }
            try
            {
                var hideRemote = IniHelper.ReadValue("UI", "HideRemoteButton", AppSettings.IniPath);
                _chkHideRemote.Checked = !string.IsNullOrWhiteSpace(hideRemote) &&
                    (hideRemote.Equals("true", StringComparison.OrdinalIgnoreCase) || hideRemote.Equals("1") || hideRemote.Equals("yes", StringComparison.OrdinalIgnoreCase) || hideRemote.Equals("on", StringComparison.OrdinalIgnoreCase));
            }
            catch { }
            try
            {
                var devIdRaw = IniHelper.ReadValue("Device", "ID", AppSettings.IniPath);
                if (!string.IsNullOrWhiteSpace(devIdRaw)) _txtDeviceId.Text = devIdRaw.Trim();
                else _txtDeviceId.Text = AppSettings.DeviceId > 0 ? AppSettings.DeviceId.ToString() : "1";
            }
            catch { _txtDeviceId.Text = "1"; }
            try
            {
                var bu = IniHelper.ReadValue("UI", "BusyUnlockTimeoutSec", AppSettings.IniPath);
                if (!string.IsNullOrWhiteSpace(bu)) _txtBusyUnlock.Text = bu.Trim(); else _txtBusyUnlock.Text = "0"; // 0 = aus
            }
            catch { _txtBusyUnlock.Text = "0"; }
            try
            {
                var maint = IniHelper.ReadValue("Security", "MaintenancePassword", AppSettings.IniPath);
                if (!string.IsNullOrEmpty(maint)) { _txtMaintPwd.Text = maint; _txtMaintPwd2.Text = maint; }
            }
            catch { }
            try
            {
                var adm = IniHelper.ReadValue("Security", "AdminNumericBackdoor", AppSettings.IniPath);
                if (!string.IsNullOrEmpty(adm)) { _txtAdminBackdoor.Text = adm; _txtAdminBackdoor2.Text = adm; }
            }
            catch { }
        }

        private void SaveValues()
        {
            // Validierung Wiederholungsfelder
            try
            {
                var m1 = (_txtMaintPwd.Text ?? string.Empty).Trim();
                var m2 = (_txtMaintPwd2.Text ?? string.Empty).Trim();
                if (!string.Equals(m1, m2, StringComparison.Ordinal))
                {
                    MessageBox.Show(this, "Wartungscode stimmt nicht überein.", "Eingabe prüfen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _txtMaintPwd2.Focus();
                    return;
                }
            }
            catch { }
            try
            {
                var a1 = (_txtAdminBackdoor.Text ?? string.Empty).Trim();
                var a2 = (_txtAdminBackdoor2.Text ?? string.Empty).Trim();
                if (!string.Equals(a1, a2, StringComparison.Ordinal))
                {
                    MessageBox.Show(this, "Admin-Backdoor stimmt nicht überein.", "Eingabe prüfen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _txtAdminBackdoor2.Focus();
                    return;
                }
            }
            catch { }

            // Device-ID prüfen
            int newDevId = AppSettings.DeviceId;
            try
            {
                var raw = (_txtDeviceId.Text ?? string.Empty).Trim();
                if (!string.IsNullOrEmpty(raw))
                {
                    if (!int.TryParse(raw, out newDevId) || newDevId <= 0)
                    {
                        MessageBox.Show(this, "Ungültige Device-ID. Bitte eine positive Zahl eingeben.", "Eingabe prüfen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        _txtDeviceId.Focus();
                        return;
                    }
                }
            }
            catch { }

            try
            {
                IniHelper.WriteValue("Device", "OnlyNFC", _chkOnlyNfc.Checked ? "True" : "False", AppSettings.IniPath);
            }
            catch { }
            try
            {
                IniHelper.WriteValue("UI", "HideRemoteButton", _chkHideRemote.Checked ? "True" : "False", AppSettings.IniPath);
            }
            catch { }
            try
            {
                IniHelper.WriteValue("Device", "ID", newDevId.ToString(), AppSettings.IniPath);
            }
            catch { }
            try
            {
                var raw = (_txtBusyUnlock.Text ?? string.Empty).Trim();
                int sec = 0; if (!string.IsNullOrEmpty(raw)) int.TryParse(raw, out sec); if (sec < 0) sec = 0;
                IniHelper.WriteValue("UI", "BusyUnlockTimeoutSec", sec.ToString(), AppSettings.IniPath);
            }
            catch { }
            try
            {
                var maint = (_txtMaintPwd.Text ?? string.Empty).Trim();
                if (!string.IsNullOrEmpty(maint))
                    IniHelper.WriteValue("Security", "MaintenancePassword", maint, AppSettings.IniPath);
                else
                    IniHelper.WriteValue("Security", "MaintenancePassword", string.Empty, AppSettings.IniPath);
            }
            catch { }
            try
            {
                var adm = (_txtAdminBackdoor.Text ?? string.Empty).Trim();
                if (!string.IsNullOrEmpty(adm))
                    IniHelper.WriteValue("Security", "AdminNumericBackdoor", adm, AppSettings.IniPath);
                else
                    IniHelper.WriteValue("Security", "AdminNumericBackdoor", string.Empty, AppSettings.IniPath);
            }
            catch { }

            try { AppLogger.Log($"Allgemeine Einstellungen gespeichert. DeviceID={newDevId}"); } catch { }
            try { MessageBox.Show(this, "Einstellungen gespeichert.\nHinweis: Änderungen an NFC/Device-ID wirken erst nach Neustart.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information); } catch { }
        }
    }
}
