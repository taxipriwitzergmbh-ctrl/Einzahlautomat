using System;
using System.Drawing;
using System.Windows.Forms;
using System.Security.Cryptography; // DPAPI & AES
using System.Text; // DPAPI & AES
using TaMi_Einzahlautomat.UI.Layout;

namespace TaMi_Einzahlautomat
{
    public class GeneralSettingsForm : Form
    {
        private ModernHeaderPanel _header;

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

        private Label _lblAutoLogoff;
        private TextBox _txtAutoLogoff;

        private Label _lblAutoCloseDocsHours;
        private TextBox _txtAutoCloseDocsHours;

        private Label _lblDocsPidToken;
        private TextBox _txtDocsPidToken;

        private CheckBox _chkReceiptPromptEnabled;
        private CheckBox _chkQrCodeEnabled;
        private CheckBox _chkDocumentsEnabled;
        private Label _lblDocStore;
        private TextBox _txtDocStore;
        private Button _btnDocStoreBrowse;

        private CheckBox _chkTimeTrackingEnabled;
        private CheckBox _chkTimeTrackingPwd;
        private Label _lblTimeTrackingPwd;

        private Button _btnShowMaint;
        private Button _btnShowAdmin;

        private const string PlaceholderTag = "__enc_placeholder__";

        public GeneralSettingsForm()
        {
            BuildUi();
            LoadValues();
        }

        // AES-based obfuscation independent of user/service (on same machine), with backward-compatible decrypt
        private const string AppSecretConst = "Geldautomat.Secret.v1";
        private static void GetAesKeyIv(out byte[] key, out byte[] iv)
        {
            var material = Encoding.UTF8.GetBytes(AppSecretConst + "|" + Environment.MachineName);
            using (var sha = SHA256.Create()) key = sha.ComputeHash(material);
            using (var md5 = MD5.Create()) iv = md5.ComputeHash(Encoding.UTF8.GetBytes("IV|" + AppSecretConst + "|" + Environment.MachineName));
        }
        private static string EncryptAes(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return string.Empty;
            GetAesKeyIv(out var key, out var iv);
            using (var aes = Aes.Create())
            {
                aes.Key = key; aes.IV = iv; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
                using (var enc = aes.CreateEncryptor())
                {
                    var data = Encoding.UTF8.GetBytes(plain);
                    var cipher = enc.TransformFinalBlock(data, 0, data.Length);
                    return "enc3:" + Convert.ToBase64String(cipher);
                }
            }
        }
        private static string DecryptAes(string stored)
        {
            if (string.IsNullOrEmpty(stored) || !stored.StartsWith("enc3:", StringComparison.Ordinal)) return null;
            var b64 = stored.Substring(5);
            GetAesKeyIv(out var key, out var iv);
            using (var aes = Aes.Create())
            {
                aes.Key = key; aes.IV = iv; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
                using (var dec = aes.CreateDecryptor())
                {
                    var cipher = Convert.FromBase64String(b64);
                    var data = dec.TransformFinalBlock(cipher, 0, cipher.Length);
                    return Encoding.UTF8.GetString(data);
                }
            }
        }
        private static string EncryptSecret(string plain)
        {
            try { return EncryptAes(plain); } catch { return plain ?? string.Empty; }
        }
        private static string DecryptSecret(string stored)
        {
            try
            {
                if (string.IsNullOrEmpty(stored)) return string.Empty;
                // try new AES
                var v = DecryptAes(stored); if (v != null) return v;
                // legacy DPAPI enc:
                if (stored.StartsWith("enc:", StringComparison.Ordinal))
                {
                    var b64 = stored.Substring(4);
                    var protectedBytes = Convert.FromBase64String(b64);
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
                return stored; // plaintext
            }
            catch { return string.Empty; }
        }

        private void BuildUi()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.White;
            DoubleBuffered = true;
            Size = new Size(700, 760);

            _header = new ModernHeaderPanel { Title = "Allgemeine Einstellungen" };
            _header.CloseClicked += () => { try { Close(); } catch { } };
            Controls.Add(_header);
            _header.BringToFront();
            try { _header.ApplyRoundedRegionToForm(this); } catch { }

            var content = new Panel { Location = new Point(0, _header.Bottom), Size = new Size(ClientSize.Width, ClientSize.Height - _header.Height - 80), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom, AutoScroll = true, BackColor = Color.White };
            Controls.Add(content);

            int left = 28; int y = 20; int rowGap = 28;

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
            _btnDocStoreBrowse = new ModernGradientButton { Text = "Pfad wählen...", Location = new Point(_txtDocStore.Right + 12, y + 22), Size = new Size(140, 32), Font = new Font("Segoe UI Variable", 10.5F, FontStyle.Bold), Visible = false, GradientStart = UiTheme.PrimaryStart, GradientEnd = UiTheme.PrimaryEnd };
            _btnDocStoreBrowse.Click += (s, e) => BrowseDocStore();
            content.Controls.Add(_lblDocStore); content.Controls.Add(_txtDocStore); content.Controls.Add(_btnDocStoreBrowse);
            y += 24 + 24 + 12;

            var lblDev = new Label { Text = "Device-ID", AutoSize = true, Location = new Point(left + 26, y), Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold) };
            _txtDeviceId = new TextBox { Location = new Point(left + 26, y + 24), Width = 150, Font = new Font("Segoe UI", 11F) };
            _lblDeviceHint = new Label { Text = "(nur von SuE anzupassen)", AutoSize = true, Location = new Point(_txtDeviceId.Right + 12, y + 26), Font = new Font("Segoe UI", 9F, FontStyle.Italic), ForeColor = Color.FromArgb(140, 140, 140) };
            content.Controls.Add(lblDev); content.Controls.Add(_txtDeviceId); content.Controls.Add(_lblDeviceHint);
            y += 24 + 24 + 12;

            _lblBusyUnlock = new Label { Text = "Timeout Freigabe (Sek.) bei Gerätefehler", AutoSize = true, Location = new Point(left + 26, y), Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold) };
            _txtBusyUnlock = new TextBox { Location = new Point(left + 26, y + 24), Width = 150, Font = new Font("Segoe UI", 11F) };
            content.Controls.Add(_lblBusyUnlock); content.Controls.Add(_txtBusyUnlock);
            y += 24 + 24 + 12;

            _lblAutoLogoff = new Label { Text = "Auto-Logoff nach (Sek.)", AutoSize = true, Location = new Point(left + 26, y), Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold) };
            _txtAutoLogoff = new TextBox { Location = new Point(left + 26, y + 24), Width = 150, Font = new Font("Segoe UI", 11F) };
            content.Controls.Add(_lblAutoLogoff); content.Controls.Add(_txtAutoLogoff);
            y += 24 + 24 + 12;

            _lblAutoCloseDocsHours = new Label { Text = "Auto-Close Dokumente/Zeiterfassung (Sek.)", AutoSize = true, Location = new Point(left + 26, y), Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold) };
            _txtAutoCloseDocsHours = new TextBox { Location = new Point(left + 26, y + 24), Width = 150, Font = new Font("Segoe UI", 11F) };
            content.Controls.Add(_lblAutoCloseDocsHours); content.Controls.Add(_txtAutoCloseDocsHours);
            y += 24 + 24 + 12;

            _lblDocsPidToken = new Label { Text = "Dokumente: Personalnummer-Token von rechts (1=letztes, 2=vorletztes)", AutoSize = true, Location = new Point(left + 26, y), Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold) };
            _txtDocsPidToken = new TextBox { Location = new Point(left + 26, y + 24), Width = 150, Font = new Font("Segoe UI", 11F) };
            var lblDocsPidHint = new Label { Text = "Beispiel: LOBN_..._10001_00000 => Wert=2", AutoSize = true, Location = new Point(left + 190, y + 26), Font = new Font("Segoe UI", 9F, FontStyle.Italic), ForeColor = Color.FromArgb(140, 140, 140) };
            content.Controls.Add(_lblDocsPidToken); content.Controls.Add(_txtDocsPidToken); content.Controls.Add(lblDocsPidHint);
            y += 24 + 24 + 12;

            var lblMaint = new Label { Text = "Wartungscode (Maintenance)", AutoSize = true, Location = new Point(left + 26, y), Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold) };
            content.Controls.Add(lblMaint);
            y += 26;
            _txtMaintPwd = new TextBox { Location = new Point(left + 26, y), Width = 240, UseSystemPasswordChar = true, Font = new Font("Segoe UI", 11F) };
            var lblMaint2 = new Label { Text = "Wiederholen", AutoSize = true, Location = new Point(left + 280, y - 18), Font = new Font("Segoe UI", 9.5f), ForeColor = Color.DimGray };
            _txtMaintPwd2 = new TextBox { Location = new Point(left + 280, y), Width = 240, UseSystemPasswordChar = true, Font = new Font("Segoe UI", 11F) };
            content.Controls.Add(_txtMaintPwd); content.Controls.Add(lblMaint2); content.Controls.Add(_txtMaintPwd2);
            _btnShowMaint = new Button { Text = "👁", Width = 30, Height = _txtMaintPwd.Height, FlatStyle = FlatStyle.Flat, BackColor = Color.White, TabStop = false };
            _btnShowMaint.FlatAppearance.BorderSize = 0; _btnShowMaint.Location = new Point(left, _txtMaintPwd.Top - 2); _btnShowMaint.Click += (s, e) => ToggleReveal(_txtMaintPwd, _txtMaintPwd2);
            content.Controls.Add(_btnShowMaint);
            y += 24 + 20;

            var lblAdmin = new Label { Text = "Admin-Backdoor (numerisch)", AutoSize = true, Location = new Point(left + 26, y), Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold) };
            content.Controls.Add(lblAdmin);
            y += 26;
            _txtAdminBackdoor = new TextBox { Location = new Point(left + 26, y), Width = 240, UseSystemPasswordChar = true, Font = new Font("Segoe UI", 11F) };
            var lblAdmin2 = new Label { Text = "Wiederholen", AutoSize = true, Location = new Point(left + 280, y - 18), Font = new Font("Segoe UI", 9.5f), ForeColor = Color.DimGray };
            _txtAdminBackdoor2 = new TextBox { Location = new Point(left + 280, y), Width = 240, UseSystemPasswordChar = true, Font = new Font("Segoe UI", 11F) };
            content.Controls.Add(_txtAdminBackdoor); content.Controls.Add(lblAdmin2); content.Controls.Add(_txtAdminBackdoor2);
            _btnShowAdmin = new Button { Text = "👁", Width = 30, Height = _txtAdminBackdoor.Height, FlatStyle = FlatStyle.Flat, BackColor = Color.White, TabStop = false };
            _btnShowAdmin.FlatAppearance.BorderSize = 0; _btnShowAdmin.Location = new Point(left, _txtAdminBackdoor.Top - 2); _btnShowAdmin.Click += (s, e) => ToggleReveal(_txtAdminBackdoor, _txtAdminBackdoor2);
            content.Controls.Add(_btnShowAdmin);
            y += 24 + 20;

            var lblHours = new Label { Text = "Zeiterfassung anzeigen", AutoSize = true, Location = new Point(left + 26, y), Font = new Font("Segoe UI Variable", 12F, FontStyle.Bold) };
            _chkTimeTrackingEnabled = new CheckBox { Location = new Point(left, y - 2), AutoSize = true };
            content.Controls.Add(lblHours); content.Controls.Add(_chkTimeTrackingEnabled);
            y += rowGap;

            _lblTimeTrackingPwd = new Label { Text = "Passwortabfrage für Zeiterfassung", AutoSize = true, Location = new Point(left + 26, y), Font = new Font("Segoe UI Variable", 12F, FontStyle.Bold), Visible = false };
            _chkTimeTrackingPwd = new CheckBox { Location = new Point(left, y - 2), AutoSize = true, Visible = false };
            content.Controls.Add(_lblTimeTrackingPwd); content.Controls.Add(_chkTimeTrackingPwd);
            _chkTimeTrackingEnabled.CheckedChanged += (s, e) => { ToggleTimeTrackingPwd(_chkTimeTrackingEnabled.Checked); };

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 80, BackColor = Color.White };
            Controls.Add(footer);
            _btnSave = new ModernGradientButton { Text = "Speichern", Size = new Size(180, 48), Font = new Font("Segoe UI Variable", 12F, FontStyle.Bold), GradientStart = UiTheme.PrimaryStart, GradientEnd = UiTheme.PrimaryEnd };
            _btnSave.Click += (s, e) => SaveValues();
            _btnClose = new ModernGradientButton { Text = "Schließen", Size = new Size(180, 48), Font = new Font("Segoe UI Variable", 12F, FontStyle.Bold), GradientStart = UiTheme.SecondaryStart, GradientEnd = UiTheme.SecondaryEnd };
            _btnClose.Click += (s, e) => Close();
            footer.Resize += (s, e) => { int spacing = 16; int total = _btnSave.Width + _btnClose.Width + spacing; int startX = (footer.ClientSize.Width - total) / 2; int yb = (footer.ClientSize.Height - _btnSave.Height) / 2; _btnSave.Location = new Point(Math.Max(10, startX), yb); _btnClose.Location = new Point(_btnSave.Right + spacing, yb); };
            footer.Controls.Add(_btnSave); footer.Controls.Add(_btnClose);
        }

        private void ToggleDocStoreUi(bool show) { try { _lblDocStore.Visible = show; _txtDocStore.Visible = show; _btnDocStoreBrowse.Visible = show; } catch { } }
        private void BrowseDocStore() { try { using (var dlg = new FolderBrowserDialog()) { dlg.Description = "Wählen Sie den Stammordner (DocStore) für Dokumente"; dlg.ShowNewFolderButton = true; if (!string.IsNullOrWhiteSpace(_txtDocStore.Text) && System.IO.Directory.Exists(_txtDocStore.Text)) dlg.SelectedPath = _txtDocStore.Text; if (dlg.ShowDialog(this) == DialogResult.OK) { _txtDocStore.Text = dlg.SelectedPath; } } } catch { } }
        private void ToggleTimeTrackingPwd(bool show) { try { _lblTimeTrackingPwd.Visible = show; _chkTimeTrackingPwd.Visible = show; } catch { } }

        private void ToggleReveal(TextBox a, TextBox b)
        {
            if (a == null || b == null) return;
            bool isPlaceholder = (a.Tag as string) == PlaceholderTag || (b.Tag as string) == PlaceholderTag;
            if (isPlaceholder) return;
            try
            {
                bool masked = a.UseSystemPasswordChar || a.PasswordChar != '\0';
                a.UseSystemPasswordChar = !masked; a.PasswordChar = '\0';
                b.UseSystemPasswordChar = !masked; b.PasswordChar = '\0';
            }
            catch { }
        }

        private void LoadValues()
        {
            string ini = AppSettings.IniPath;
            try { var only = IniHelper.ReadValue("Device", "OnlyNFC", ini); _chkOnlyNfc.Checked = !string.IsNullOrWhiteSpace(only) && (only.Equals("true", StringComparison.OrdinalIgnoreCase) || only.Equals("1") || only.Equals("yes", StringComparison.OrdinalIgnoreCase) || only.Equals("on", StringComparison.OrdinalIgnoreCase)); } catch { }
            try { var hideRemote = IniHelper.ReadValue("UI", "HideRemoteButton", ini); _chkHideRemote.Checked = !string.IsNullOrWhiteSpace(hideRemote) && (hideRemote.Equals("true", StringComparison.OrdinalIgnoreCase) || hideRemote.Equals("1") || hideRemote.Equals("yes", StringComparison.OrdinalIgnoreCase) || hideRemote.Equals("on", StringComparison.OrdinalIgnoreCase)); } catch { }
            try { var devIdRaw = IniHelper.ReadValue("Device", "ID", ini); if (!string.IsNullOrWhiteSpace(devIdRaw)) _txtDeviceId.Text = devIdRaw.Trim(); else _txtDeviceId.Text = AppSettings.DeviceId > 0 ? AppSettings.DeviceId.ToString() : "1"; } catch { _txtDeviceId.Text = "1"; }
            try { var bu = IniHelper.ReadValue("UI", "BusyUnlockTimeoutSec", ini); _txtBusyUnlock.Text = string.IsNullOrWhiteSpace(bu) ? "0" : bu.Trim(); } catch { _txtBusyUnlock.Text = "0"; }
            try { var al = IniHelper.ReadValue("UI", "AutoLogoffTimeoutSec", ini); _txtAutoLogoff.Text = string.IsNullOrWhiteSpace(al) ? "0" : al.Trim(); } catch { _txtAutoLogoff.Text = "0"; }
            try { var ac = IniHelper.ReadValue("UI", "AutoCloseDocsHoursSec", ini); _txtAutoCloseDocsHours.Text = string.IsNullOrWhiteSpace(ac) ? "0" : ac.Trim(); } catch { try { _txtAutoCloseDocsHours.Text = "0"; } catch { } }
            try { var raw = IniHelper.ReadValue("UI", "DocumentsPidTokenFromRight", ini); _txtDocsPidToken.Text = string.IsNullOrWhiteSpace(raw) ? "1" : raw.Trim(); } catch { try { _txtDocsPidToken.Text = "1"; } catch { } }
            try { var maint = IniHelper.ReadValue("Security", "MaintenancePassword", ini); var dec = DecryptSecret(maint ?? string.Empty); if (!string.IsNullOrEmpty(dec)) { _txtMaintPwd.Text = dec; _txtMaintPwd2.Text = dec; } else if (!string.IsNullOrEmpty(maint)) { _txtMaintPwd.Text = new string('•', 8); _txtMaintPwd2.Text = new string('•', 8); _txtMaintPwd.Tag = PlaceholderTag; _txtMaintPwd2.Tag = PlaceholderTag; } } catch { }
            try { var adm = IniHelper.ReadValue("Security", "AdminNumericBackdoor", ini); var dec = DecryptSecret(adm ?? string.Empty); if (!string.IsNullOrEmpty(dec)) { _txtAdminBackdoor.Text = dec; _txtAdminBackdoor2.Text = dec; } else if (!string.IsNullOrEmpty(adm)) { _txtAdminBackdoor.Text = new string('•', 8); _txtAdminBackdoor2.Text = new string('•', 8); _txtAdminBackdoor.Tag = PlaceholderTag; _txtAdminBackdoor2.Tag = PlaceholderTag; } } catch { }
            try { var prompt = IniHelper.ReadValue("ReceiptPrinter", "AskUser", ini); _chkReceiptPromptEnabled.Checked = string.IsNullOrWhiteSpace(prompt) ? true : (prompt.Equals("true", StringComparison.OrdinalIgnoreCase) || prompt.Equals("1") || prompt.Equals("yes", StringComparison.OrdinalIgnoreCase) || prompt.Equals("on", StringComparison.OrdinalIgnoreCase)); } catch { _chkReceiptPromptEnabled.Checked = true; }
            try { var qr = IniHelper.ReadValue("UI", "QrCodeEnabled", ini); _chkQrCodeEnabled.Checked = string.IsNullOrWhiteSpace(qr) ? true : (qr.Equals("true", StringComparison.OrdinalIgnoreCase) || qr.Equals("1") || qr.Equals("yes", StringComparison.OrdinalIgnoreCase) || qr.Equals("on", StringComparison.OrdinalIgnoreCase)); } catch { _chkQrCodeEnabled.Checked = true; }
            bool docsOn = false; try { var docs = IniHelper.ReadValue("UI", "DocumentsEnabled", ini); docsOn = !string.IsNullOrWhiteSpace(docs) && (docs.Equals("true", StringComparison.OrdinalIgnoreCase) || docs.Equals("1") || docs.Equals("yes", StringComparison.OrdinalIgnoreCase) || docs.Equals("on", StringComparison.OrdinalIgnoreCase)); _chkDocumentsEnabled.Checked = docsOn; } catch { _chkDocumentsEnabled.Checked = false; }
            try { var ds = IniHelper.ReadValue("UI", "DocStore", ini); _txtDocStore.Text = string.IsNullOrWhiteSpace(ds) ? string.Empty : ds.Trim(); } catch { _txtDocStore.Text = string.Empty; }
            bool hoursOn = false; try { var hours = IniHelper.ReadValue("UI", "TimeTrackingEnabled", ini); hoursOn = !string.IsNullOrWhiteSpace(hours) && (hours.Equals("true", StringComparison.OrdinalIgnoreCase) || hours.Equals("1") || hours.Equals("yes", StringComparison.OrdinalIgnoreCase) || hours.Equals("on", StringComparison.OrdinalIgnoreCase)); _chkTimeTrackingEnabled.Checked = hoursOn; } catch { _chkTimeTrackingEnabled.Checked = false; }
            try { var hpwd = IniHelper.ReadValue("UI", "TimeTrackingPasswordRequired", ini); bool req = !string.IsNullOrWhiteSpace(hpwd) && (hpwd.Equals("true", StringComparison.OrdinalIgnoreCase) || hpwd.Equals("1") || hpwd.Equals("yes", StringComparison.OrdinalIgnoreCase) || hpwd.Equals("on", StringComparison.OrdinalIgnoreCase)); _chkTimeTrackingPwd.Checked = req; } catch { _chkTimeTrackingPwd.Checked = false; }
            ToggleDocStoreUi(docsOn); ToggleTimeTrackingPwd(hoursOn);
        }

        private void SaveValues()
        {
            // Validate repeat
            try { var m1 = (_txtMaintPwd.Text ?? string.Empty).Trim(); var m2 = (_txtMaintPwd2.Text ?? string.Empty).Trim(); if (!string.Equals(m1, m2, StringComparison.Ordinal)) { MessageBox.Show(this, "Wartungscode stimmt nicht überein.", "Eingabe prüfen", MessageBoxButtons.OK, MessageBoxIcon.Warning); _txtMaintPwd2.Focus(); return; } } catch { }
            try { var a1 = (_txtAdminBackdoor.Text ?? string.Empty).Trim(); var a2 = (_txtAdminBackdoor2.Text ?? string.Empty).Trim(); if (!string.Equals(a1, a2, StringComparison.Ordinal)) { MessageBox.Show(this, "Admin-Backdoor stimmt nicht überein.", "Eingabe prüfen", MessageBoxButtons.OK, MessageBoxIcon.Warning); _txtAdminBackdoor2.Focus(); return; } } catch { }

            int newDevId = AppSettings.DeviceId; try { var raw = (_txtDeviceId.Text ?? string.Empty).Trim(); if (!string.IsNullOrEmpty(raw)) { if (!int.TryParse(raw, out newDevId) || newDevId <= 0) { MessageBox.Show(this, "Ungültige Device-ID. Bitte eine positive Zahl eingeben.", "Eingabe prüfen", MessageBoxButtons.OK, MessageBoxIcon.Warning); _txtDeviceId.Focus(); return; } } } catch { }

            string ini = AppSettings.IniPath;
            try { IniHelper.WriteValue("Device", "OnlyNFC", _chkOnlyNfc.Checked ? "True" : "False", ini); } catch { }
            try { IniHelper.WriteValue("UI", "HideRemoteButton", _chkHideRemote.Checked ? "True" : "False", ini); } catch { }
            try { IniHelper.WriteValue("Device", "ID", newDevId.ToString(), ini); } catch { }
            try { var raw = (_txtBusyUnlock.Text ?? string.Empty).Trim(); int sec = 0; if (!string.IsNullOrEmpty(raw)) int.TryParse(raw, out sec); if (sec < 0) sec = 0; IniHelper.WriteValue("UI", "BusyUnlockTimeoutSec", sec.ToString(), ini); } catch { }
            try { var raw = (_txtAutoLogoff.Text ?? string.Empty).Trim(); int sec = 0; if (!string.IsNullOrEmpty(raw)) int.TryParse(raw, out sec); if (sec < 0) sec = 0; IniHelper.WriteValue("UI", "AutoLogoffTimeoutSec", sec.ToString(), ini); } catch { }
            try { var raw = (_txtAutoCloseDocsHours.Text ?? string.Empty).Trim(); int sec = 0; if (!string.IsNullOrEmpty(raw)) int.TryParse(raw, out sec); if (sec < 0) sec = 0; IniHelper.WriteValue("UI", "AutoCloseDocsHoursSec", sec.ToString(), ini); } catch { }
            try { var raw = (_txtDocsPidToken.Text ?? string.Empty).Trim(); int v = 1; if (!string.IsNullOrEmpty(raw)) int.TryParse(raw, out v); if (v < 1) v = 1; if (v > 20) v = 20; IniHelper.WriteValue("UI", "DocumentsPidTokenFromRight", v.ToString(), ini); } catch { }
            try { IniHelper.WriteValue("Security", "MaintenancePassword", EncryptSecret((_txtMaintPwd.Text ?? string.Empty).Trim()), ini); } catch { }
            try { IniHelper.WriteValue("Security", "AdminNumericBackdoor", EncryptSecret((_txtAdminBackdoor.Text ?? string.Empty).Trim()), ini); } catch { }
            try { IniHelper.WriteValue("ReceiptPrinter", "AskUser", _chkReceiptPromptEnabled.Checked ? "True" : "False", ini); } catch { }
            try { IniHelper.WriteValue("UI", "QrCodeEnabled", _chkQrCodeEnabled.Checked ? "True" : "False", ini); } catch { }
            try { IniHelper.WriteValue("UI", "DocumentsEnabled", _chkDocumentsEnabled.Checked ? "True" : "False", ini); } catch { }
            try { IniHelper.WriteValue("UI", "DocStore", (_txtDocStore.Text ?? string.Empty).Trim(), ini); } catch { }
            try { IniHelper.WriteValue("UI", "TimeTrackingEnabled", _chkTimeTrackingEnabled.Checked ? "True" : "False", ini); } catch { }
            try { IniHelper.WriteValue("UI", "TimeTrackingPasswordRequired", _chkTimeTrackingPwd.Checked ? "True" : "False", ini); } catch { }

            try { AppLogger.Log("Allgemeine Einstellungen gespeichert."); } catch { }
            try { MessageBox.Show(this, "Einstellungen gespeichert.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information); } catch { }
        }
    }
}
