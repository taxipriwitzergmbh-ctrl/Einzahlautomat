using System;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Drawing.Drawing2D;
using System.Security.Cryptography; // added for DPAPI
using System.Text; // added for DPAPI

namespace Geldautomat
{
    public class GeneralSettingsForm : Form
    {
        private Panel _headerPanel;
        private Label _lblTitle;
        private Button _btnHeaderClose;
        private Point _mouseDownLocation;

        private CheckBox _chkOnlyNfc;
        private CheckBox _chkHideRemote;
        private Label _lblRestartInfo;
        private TextBox _txtMaintPwd;
        private TextBox _txtMaintPwd2;
        private TextBox _txtAdminBackdoor;
        private TextBox _txtAdminBackdoor2;
        private Button _btnSave;
        private Button _btnClose;

        private TextBox _txtDeviceId;
        private Label _lblDeviceHint;
        private Label _lblBusyUnlock;
        private TextBox _txtBusyUnlock;

        private CheckBox _chkReceiptPromptEnabled;
        private CheckBox _chkQrCodeEnabled;
        private CheckBox _chkDocumentsEnabled;
        private Label _lblDocStore;
        private TextBox _txtDocStore;
        private Button _btnDocStoreBrowse;

        private CheckBox _chkTimeTrackingEnabled;
        private CheckBox _chkTimeTrackingPwd;
        private Label _lblTimeTrackingPwd;

        public GeneralSettingsForm()
        {
            BuildUi();
            LoadValues();
        }

        // Simple DPAPI helpers (local to this form)
        private static string EncryptSecret(string plain)
        {
            try
            {
                if (string.IsNullOrEmpty(plain)) return string.Empty;
                var data = Encoding.UTF8.GetBytes(plain);
                var protectedBytes = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
                return "enc:" + Convert.ToBase64String(protectedBytes);
            }
            catch { return plain ?? string.Empty; }
        }
        private static string DecryptSecret(string stored)
        {
            try
            {
                if (string.IsNullOrEmpty(stored)) return string.Empty;
                if (!stored.StartsWith("enc:", StringComparison.Ordinal)) return stored; // backward plaintext
                var b64 = stored.Substring(4);
                var protectedBytes = Convert.FromBase64String(b64);
                var unprotected = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(unprotected);
            }
            catch { return string.Empty; }
        }

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

        private void BuildUi()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.White;
            DoubleBuffered = true;
            Size = new Size(700, 760); // mehr Platz, damit nichts �berlappt

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
                Size = new Size(420, 60),
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
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                TabStop = false
            };
            _btnHeaderClose.FlatAppearance.BorderSize = 0;
            _btnHeaderClose.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 80, 80);
            _btnHeaderClose.Click += (s, e) => Close();
            _headerPanel.Controls.Add(_btnHeaderClose);

            try { Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, Width, Height, 24, 24)); } catch { }

            // Scrollbarer Content unter dem Header
            var content = new Panel
            {
                Location = new Point(0, _headerPanel.Bottom),
                Size = new Size(ClientSize.Width, ClientSize.Height - _headerPanel.Height - 80),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                AutoScroll = true,
                BackColor = Color.White
            };
            Controls.Add(content);

            int left = 28;
            int y = 20;
            int rowGap = 28;

            var lblOnly = new Label { Text = "Nur NFC Anmeldung erlauben", AutoSize = true, Location = new Point(left + 26, y), Font = new Font("Segoe UI Variable", 12F, FontStyle.Bold) };
            _chkOnlyNfc = new CheckBox { Location = new Point(left, y - 2), AutoSize = true };
            _lblRestartInfo = new Label { Text = "Neustart erforderlich", AutoSize = true, Location = new Point(left + 26, y + 18), Font = new Font("Segoe UI", 9.5f), ForeColor = Color.FromArgb(80, 80, 80) };
            content.Controls.Add(_chkOnlyNfc); content.Controls.Add(lblOnly); content.Controls.Add(_lblRestartInfo);
            y += 44;

            _chkHideRemote = new CheckBox { Location = new Point(left, y - 2), AutoSize = true };
            var lblHideRemote = new Label { Text = "Fernwartungsbutton im Login ausblenden", AutoSize = true, Location = new Point(left + 26, y - 2), Font = new Font("Segoe UI Variable", 12F, FontStyle.Bold) };
            content.Controls.Add(_chkHideRemote); content.Controls.Add(lblHideRemote);
            y += rowGap;

            var lblPrompt = new Label { Text = "Quittungsabfrage aktiviert", AutoSize = true, Location = new Point(left + 26, y), Font = new Font("Segoe UI Variable", 12F, FontStyle.Bold) };
            _chkReceiptPromptEnabled = new CheckBox { Location = new Point(left, y - 2), AutoSize = true };
            content.Controls.Add(lblPrompt); content.Controls.Add(_chkReceiptPromptEnabled);
            y += rowGap;

            var lblQr = new Label { Text = "QR-Code aktiviert", AutoSize = true, Location = new Point(left + 26, y), Font = new Font("Segoe UI Variable", 12F, FontStyle.Bold) };
            _chkQrCodeEnabled = new CheckBox { Location = new Point(left, y - 2), AutoSize = true };
            content.Controls.Add(lblQr); content.Controls.Add(_chkQrCodeEnabled);
            y += rowGap;

            var lblDocs = new Label { Text = "Dokumente/Lohnabrechnungen aktiviert", AutoSize = true, Location = new Point(left + 26, y), Font = new Font("Segoe UI Variable", 12F, FontStyle.Bold) };
            _chkDocumentsEnabled = new CheckBox { Location = new Point(left, y - 2), AutoSize = true };
            _chkDocumentsEnabled.CheckedChanged += (s, e) => { ToggleDocStoreUi(_chkDocumentsEnabled.Checked); };
            content.Controls.Add(lblDocs); content.Controls.Add(_chkDocumentsEnabled);
            y += rowGap;

            _lblDocStore = new Label { Text = "Dokumente-Pfad (DocStore)", AutoSize = true, Location = new Point(left + 26, y), Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold), Visible = false };
            _txtDocStore = new TextBox { Location = new Point(left + 26, y + 24), Width = 420, Font = new Font("Segoe UI", 11F), Visible = false };
            _btnDocStoreBrowse = new Button { Text = "Pfad w�hlen...", Location = new Point(_txtDocStore.Right + 12, y + 22), Size = new Size(140, 32), BackColor = Color.FromArgb(33, 150, 243), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI Variable", 10.5F, FontStyle.Bold), Visible = false };
            _btnDocStoreBrowse.FlatAppearance.BorderSize = 0;
            _btnDocStoreBrowse.Click += (s, e) => BrowseDocStore();
            content.Controls.Add(_lblDocStore); content.Controls.Add(_txtDocStore); content.Controls.Add(_btnDocStoreBrowse);
            y += 24 + 24 + 12;

            var lblDev = new Label { Text = "Device-ID", AutoSize = true, Location = new Point(left + 26, y), Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold) };
            _txtDeviceId = new TextBox { Location = new Point(left + 26, y + 24), Width = 150, Font = new Font("Segoe UI", 11F) };
            _lblDeviceHint = new Label { Text = "(nur von SuE anzupassen)", AutoSize = true, Location = new Point(_txtDeviceId.Right + 12, y + 26), Font = new Font("Segoe UI", 9F, FontStyle.Italic), ForeColor = Color.FromArgb(140, 140, 140) };
            content.Controls.Add(lblDev); content.Controls.Add(_txtDeviceId); content.Controls.Add(_lblDeviceHint);
            y += 24 + 24 + 12;

            _lblBusyUnlock = new Label { Text = "Timeout Freigabe (Sek.) bei Ger�tefehler", AutoSize = true, Location = new Point(left + 26, y), Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold) };
            _txtBusyUnlock = new TextBox { Location = new Point(left + 26, y + 24), Width = 150, Font = new Font("Segoe UI", 11F) };
            content.Controls.Add(_lblBusyUnlock); content.Controls.Add(_txtBusyUnlock);
            y += 24 + 24 + 12;

            var lblMaint = new Label { Text = "Wartungscode (Maintenance)", AutoSize = true, Location = new Point(left + 26, y), Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold) };
            content.Controls.Add(lblMaint);
            y += 26;
            _txtMaintPwd = new TextBox { Location = new Point(left + 26, y), Width = 240, UseSystemPasswordChar = true, Font = new Font("Segoe UI", 11F) };
            var lblMaint2 = new Label { Text = "Wiederholen", AutoSize = true, Location = new Point(left + 280, y - 18), Font = new Font("Segoe UI", 9.5f), ForeColor = Color.DimGray };
            _txtMaintPwd2 = new TextBox { Location = new Point(left + 280, y), Width = 240, UseSystemPasswordChar = true, Font = new Font("Segoe UI", 11F) };
            content.Controls.Add(_txtMaintPwd); content.Controls.Add(lblMaint2); content.Controls.Add(_txtMaintPwd2);
            y += 24 + 20;

            var lblAdmin = new Label { Text = "Admin-Backdoor (numerisch)", AutoSize = true, Location = new Point(left + 26, y), Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold) };
            content.Controls.Add(lblAdmin);
            y += 26;
            _txtAdminBackdoor = new TextBox { Location = new Point(left + 26, y), Width = 240, UseSystemPasswordChar = true, Font = new Font("Segoe UI", 11F) };
            var lblAdmin2 = new Label { Text = "Wiederholen", AutoSize = true, Location = new Point(left + 280, y - 18), Font = new Font("Segoe UI", 9.5f), ForeColor = Color.DimGray };
            _txtAdminBackdoor2 = new TextBox { Location = new Point(left + 280, y), Width = 240, UseSystemPasswordChar = true, Font = new Font("Segoe UI", 11F) };
            content.Controls.Add(_txtAdminBackdoor); content.Controls.Add(lblAdmin2); content.Controls.Add(_txtAdminBackdoor2);
            y += 24 + 20;

            var lblHours = new Label { Text = "Zeiterfassung anzeigen", AutoSize = true, Location = new Point(left + 26, y), Font = new Font("Segoe UI Variable", 12F, FontStyle.Bold) };
            _chkTimeTrackingEnabled = new CheckBox { Location = new Point(left, y - 2), AutoSize = true };
            content.Controls.Add(lblHours); content.Controls.Add(_chkTimeTrackingEnabled);
            y += rowGap;

            _lblTimeTrackingPwd = new Label { Text = "Passwortabfrage f�r Zeiterfassung", AutoSize = true, Location = new Point(left + 26, y), Font = new Font("Segoe UI Variable", 12F, FontStyle.Bold), Visible = false };
            _chkTimeTrackingPwd = new CheckBox { Location = new Point(left, y - 2), AutoSize = true, Visible = false };
            content.Controls.Add(_lblTimeTrackingPwd); content.Controls.Add(_chkTimeTrackingPwd);
            _chkTimeTrackingEnabled.CheckedChanged += (s, e) => { ToggleTimeTrackingPwd(_chkTimeTrackingEnabled.Checked); };

            // Footer mit Buttons unten fixiert
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 80, BackColor = Color.White };
            Controls.Add(footer);
            _btnSave = new Button { Text = "Speichern", Size = new Size(180, 48), BackColor = Color.FromArgb(33, 150, 243), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI Variable", 12F, FontStyle.Bold) };
            _btnSave.FlatAppearance.BorderSize = 0; _btnSave.Click += (s, e) => SaveValues();
            _btnClose = new Button { Text = "Schlie�en", Size = new Size(180, 48), BackColor = Color.FromArgb(158, 158, 158), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI Variable", 12F, FontStyle.Bold) };
            _btnClose.FlatAppearance.BorderSize = 0; _btnClose.Click += (s, e) => Close();
            footer.Resize += (s, e) =>
            {
                int spacing = 16; int total = _btnSave.Width + _btnClose.Width + spacing;
                int startX = (footer.ClientSize.Width - total) / 2; int yb = (footer.ClientSize.Height - _btnSave.Height) / 2;
                _btnSave.Location = new Point(Math.Max(10, startX), yb);
                _btnClose.Location = new Point(_btnSave.Right + spacing, yb);
            };
            footer.Controls.Add(_btnSave); footer.Controls.Add(_btnClose);
        }

        private void HeaderPanel_Paint(object sender, PaintEventArgs e)
        {
            using (var brush = new LinearGradientBrush(_headerPanel.ClientRectangle, Color.FromArgb(33, 150, 243), Color.FromArgb(33, 203, 243), 0f))
            { e.Graphics.FillRectangle(brush, _headerPanel.ClientRectangle); }
        }
        private void HeaderPanel_MouseDown(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) _mouseDownLocation = e.Location; }
        private void HeaderPanel_MouseMove(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Left += e.X - _mouseDownLocation.X; Top += e.Y - _mouseDownLocation.Y; } }

        private void ToggleDocStoreUi(bool show) { try { _lblDocStore.Visible = show; _txtDocStore.Visible = show; _btnDocStoreBrowse.Visible = show; } catch { } }
        private void BrowseDocStore() { try { using (var dlg = new FolderBrowserDialog()) { dlg.Description = "W�hlen Sie den Stammordner (DocStore) f�r Dokumente"; dlg.ShowNewFolderButton = true; if (!string.IsNullOrWhiteSpace(_txtDocStore.Text) && System.IO.Directory.Exists(_txtDocStore.Text)) dlg.SelectedPath = _txtDocStore.Text; if (dlg.ShowDialog(this) == DialogResult.OK) { _txtDocStore.Text = dlg.SelectedPath; } } } catch { } }
        private void ToggleTimeTrackingPwd(bool show) { try { _lblTimeTrackingPwd.Visible = show; _chkTimeTrackingPwd.Visible = show; } catch { } }

        private void LoadValues()
        {
            try
            {
                var only = IniHelper.ReadValue("Device", "OnlyNFC", AppSettings.IniPath);
                _chkOnlyNfc.Checked = !string.IsNullOrWhiteSpace(only) && (only.Equals("true", StringComparison.OrdinalIgnoreCase) || only.Equals("1") || only.Equals("yes", StringComparison.OrdinalIgnoreCase) || only.Equals("on", StringComparison.OrdinalIgnoreCase));
            }
            catch { }
            try
            {
                var hideRemote = IniHelper.ReadValue("UI", "HideRemoteButton", AppSettings.IniPath);
                _chkHideRemote.Checked = !string.IsNullOrWhiteSpace(hideRemote) && (hideRemote.Equals("true", StringComparison.OrdinalIgnoreCase) || hideRemote.Equals("1") || hideRemote.Equals("yes", StringComparison.OrdinalIgnoreCase) || hideRemote.Equals("on", StringComparison.OrdinalIgnoreCase));
            }
            catch { }
            try
            {
                var devIdRaw = IniHelper.ReadValue("Device", "ID", AppSettings.IniPath);
                if (!string.IsNullOrWhiteSpace(devIdRaw)) _txtDeviceId.Text = devIdRaw.Trim(); else _txtDeviceId.Text = AppSettings.DeviceId > 0 ? AppSettings.DeviceId.ToString() : "1";
            }
            catch { _txtDeviceId.Text = "1"; }
            try
            {
                var bu = IniHelper.ReadValue("UI", "BusyUnlockTimeoutSec", AppSettings.IniPath);
                if (!string.IsNullOrWhiteSpace(bu)) _txtBusyUnlock.Text = bu.Trim(); else _txtBusyUnlock.Text = "0";
            }
            catch { _txtBusyUnlock.Text = "0"; }
            try
            {
                var maint = IniHelper.ReadValue("Security", "MaintenancePassword", AppSettings.IniPath);
                var decMaint = DecryptSecret(maint ?? string.Empty);
                if (!string.IsNullOrEmpty(decMaint)) { _txtMaintPwd.Text = decMaint; _txtMaintPwd2.Text = decMaint; }
            }
            catch { }
            try
            {
                var adm = IniHelper.ReadValue("Security", "AdminNumericBackdoor", AppSettings.IniPath);
                var decAdm = DecryptSecret(adm ?? string.Empty);
                if (!string.IsNullOrEmpty(decAdm)) { _txtAdminBackdoor.Text = decAdm; _txtAdminBackdoor2.Text = decAdm; }
            }
            catch { }
            try
            {
                var prompt = IniHelper.ReadValue("ReceiptPrinter", "AskUser", AppSettings.IniPath);
                _chkReceiptPromptEnabled.Checked = string.IsNullOrWhiteSpace(prompt) ? true : (prompt.Equals("true", StringComparison.OrdinalIgnoreCase) || prompt.Equals("1") || prompt.Equals("yes", StringComparison.OrdinalIgnoreCase) || prompt.Equals("on", StringComparison.OrdinalIgnoreCase));
            }
            catch { _chkReceiptPromptEnabled.Checked = true; }
            try
            {
                var qr = IniHelper.ReadValue("UI", "QrCodeEnabled", AppSettings.IniPath);
                _chkQrCodeEnabled.Checked = string.IsNullOrWhiteSpace(qr) ? true : (qr.Equals("true", StringComparison.OrdinalIgnoreCase) || qr.Equals("1") || qr.Equals("yes", StringComparison.OrdinalIgnoreCase) || qr.Equals("on", StringComparison.OrdinalIgnoreCase));
            }
            catch { _chkQrCodeEnabled.Checked = true; }
            bool docsOn = false;
            try
            {
                var docs = IniHelper.ReadValue("UI", "DocumentsEnabled", AppSettings.IniPath);
                docsOn = string.IsNullOrWhiteSpace(docs) ? false : (docs.Equals("true", StringComparison.OrdinalIgnoreCase) || docs.Equals("1") || docs.Equals("yes", StringComparison.OrdinalIgnoreCase) || docs.Equals("on", StringComparison.OrdinalIgnoreCase));
                _chkDocumentsEnabled.Checked = docsOn;
            }
            catch { _chkDocumentsEnabled.Checked = false; }
            try
            {
                var ds = IniHelper.ReadValue("UI", "DocStore", AppSettings.IniPath);
                if (!string.IsNullOrWhiteSpace(ds)) _txtDocStore.Text = ds.Trim(); else _txtDocStore.Text = string.Empty;
            }
            catch { _txtDocStore.Text = string.Empty; }
            bool hoursOn = false;
            try
            {
                var hours = IniHelper.ReadValue("UI", "TimeTrackingEnabled", AppSettings.IniPath);
                hoursOn = !string.IsNullOrWhiteSpace(hours) && (hours.Equals("true", StringComparison.OrdinalIgnoreCase) || hours.Equals("1") || hours.Equals("yes", StringComparison.OrdinalIgnoreCase) || hours.Equals("on", StringComparison.OrdinalIgnoreCase));
                _chkTimeTrackingEnabled.Checked = hoursOn;
            }
            catch { _chkTimeTrackingEnabled.Checked = false; }
            try
            {
                var hpwd = IniHelper.ReadValue("UI", "TimeTrackingPasswordRequired", AppSettings.IniPath);
                bool req = !string.IsNullOrWhiteSpace(hpwd) && (hpwd.Equals("true", StringComparison.OrdinalIgnoreCase) || hpwd.Equals("1") || hpwd.Equals("yes", StringComparison.OrdinalIgnoreCase) || hpwd.Equals("on", StringComparison.OrdinalIgnoreCase));
                _chkTimeTrackingPwd.Checked = req;
            }
            catch { _chkTimeTrackingPwd.Checked = false; }
            ToggleDocStoreUi(docsOn);
            ToggleTimeTrackingPwd(hoursOn);
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

            try { IniHelper.WriteValue("Device", "OnlyNFC", _chkOnlyNfc.Checked ? "True" : "False", AppSettings.IniPath); } catch { }
            try { IniHelper.WriteValue("UI", "HideRemoteButton", _chkHideRemote.Checked ? "True" : "False", AppSettings.IniPath); } catch { }
            try { IniHelper.WriteValue("Device", "ID", newDevId.ToString(), AppSettings.IniPath); } catch { }
            try
            {
                var raw = (_txtBusyUnlock.Text ?? string.Empty).Trim(); int sec = 0; if (!string.IsNullOrEmpty(raw)) int.TryParse(raw, out sec); if (sec < 0) sec = 0; IniHelper.WriteValue("UI", "BusyUnlockTimeoutSec", sec.ToString(), AppSettings.IniPath);
            }
            catch { }
            try { IniHelper.WriteValue("Security", "MaintenancePassword", EncryptSecret((_txtMaintPwd.Text ?? string.Empty).Trim()), AppSettings.IniPath); } catch { }
            try { IniHelper.WriteValue("Security", "AdminNumericBackdoor", EncryptSecret((_txtAdminBackdoor.Text ?? string.Empty).Trim()), AppSettings.IniPath); } catch { }
            try { IniHelper.WriteValue("ReceiptPrinter", "AskUser", _chkReceiptPromptEnabled.Checked ? "True" : "False", AppSettings.IniPath); } catch { }
            try { IniHelper.WriteValue("UI", "QrCodeEnabled", _chkQrCodeEnabled.Checked ? "True" : "False", AppSettings.IniPath); } catch { }
            try { IniHelper.WriteValue("UI", "DocumentsEnabled", _chkDocumentsEnabled.Checked ? "True" : "False", AppSettings.IniPath); } catch { }
            try { IniHelper.WriteValue("UI", "DocStore", (_txtDocStore.Text ?? string.Empty).Trim(), AppSettings.IniPath); } catch { }
            try { IniHelper.WriteValue("UI", "TimeTrackingEnabled", _chkTimeTrackingEnabled.Checked ? "True" : "False", AppSettings.IniPath); } catch { }
            try { IniHelper.WriteValue("UI", "TimeTrackingPasswordRequired", _chkTimeTrackingPwd.Checked ? "True" : "False", AppSettings.IniPath); } catch { }

            try { AppLogger.Log($"Allgemeine Einstellungen gespeichert. Prompt={( _chkReceiptPromptEnabled.Checked ? "on" : "off")}, QR={( _chkQrCodeEnabled.Checked ? "on" : "off")}, Docs={( _chkDocumentsEnabled.Checked ? "on" : "off")}, DocStore='{_txtDocStore.Text}'"); } catch { }
            try { MessageBox.Show(this, "Einstellungen gespeichert.\nHinweis: Änderungen an NFC/Device-ID wirken erst nach Neustart.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information); } catch { }
        }
    }
}
