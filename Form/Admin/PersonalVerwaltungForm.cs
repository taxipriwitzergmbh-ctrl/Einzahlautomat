using System;
using System.Data;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Collections.Generic;
using TaMi_Einzahlautomat.UI.Layout;

namespace TaMi_Einzahlautomat
{
    public partial class PersonalVerwaltungForm : Form
    {
        private ModernHeaderPanel _header;
        private TextBox txtPid; private Label lblName; private Label lblVorname; private TextBox txtNfc; private TextBox txtFahrercode; private Button btnSave; private Panel numPadPanel; private Button btnClearNfc; private Button btnShowHideCode; private Button btnClearCode; private Button btnNfcUebernehmen; private int _currentPid = 0; private TextBox _lastFocusedTextBox; private Label lblStatus; private Label lblEintritt; private Label lblAustritt; private PersonalInfo _loadedPersonal;
        private static readonly DateTime PlaceholderExitDate = new DateTime(1899, 12, 30); // Platzhalter

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

        // Header & Buttons werden zentral über `ModernHeaderPanel` / `ModernGradientButton` gestaltet.

        public PersonalVerwaltungForm() { InitUi(); }

        private void InitUi()
        {
            // Fenster-Setup
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(620, 840); // Höhe erhöht
            BackColor = Color.White;
            DoubleBuffered = true;
            KeyPreview = true;
            this.KeyDown += PersonalForm_KeyDown;
            this.KeyPress += PersonalForm_KeyPress;

            _header = new ModernHeaderPanel { Title = "Personal" };
            _header.CloseClicked += () => { try { Close(); } catch { } };
            Controls.Add(_header);
            _header.BringToFront();
            try { _header.ApplyRoundedRegionToForm(this); } catch { }

            int y = 80;
            var lblPid = new Label { Text = "Personalnummer:", Location = new Point(24, y), AutoSize = true, Font = new Font("Segoe UI Variable", 12F) };
            Controls.Add(lblPid);
            txtPid = new TextBox { Location = new Point(24, y + 30), Width = 220, Font = new Font("Segoe UI Variable", 18F), TextAlign = HorizontalAlignment.Center, MaxLength = 8 };
            txtPid.GotFocus += TrackTextFocus; // Fokus-Tracking
            Controls.Add(txtPid);
            var btnLoad = new ModernGradientButton { Text = "Laden", Location = new Point(260, y + 30), Size = new Size(120, 44), Font = new Font("Segoe UI Variable", 12F, FontStyle.Bold), GradientStart = UiTheme.PrimaryStart, GradientEnd = UiTheme.PrimaryEnd };
            btnLoad.Click += async (s, e) => await LoadPersonalAsync();
            Controls.Add(btnLoad);

            y += 90;
            lblName = new Label { Text = "Name:", Location = new Point(24, y), Size = new Size(560, 30), Font = new Font("Segoe UI Variable", 12F) };
            lblVorname = new Label { Text = "Vorname:", Location = new Point(24, y + 34), Size = new Size(560, 30), Font = new Font("Segoe UI Variable", 12F) };
            Controls.Add(lblName);
            Controls.Add(lblVorname);

            lblStatus = new Label { Text = "Status:", Location = new Point(24, y + 68), Size = new Size(560, 24), Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold) };
            lblEintritt = new Label { Text = "Eintritt: -", Location = new Point(24, y + 92), Size = new Size(270, 22), Font = new Font("Segoe UI Variable", 10.5F) };
            lblAustritt = new Label { Text = "Austritt: -", Location = new Point(300, y + 92), Size = new Size(284, 22), Font = new Font("Segoe UI Variable", 10.5F) };
            Controls.Add(lblStatus); Controls.Add(lblEintritt); Controls.Add(lblAustritt);
            y += 110; // verschieben nach unten wegen neuer Labels

            // NFC Label
            var lblNfc = new Label { Text = "NFC:", Location = new Point(24, y), AutoSize = true, Font = new Font("Segoe UI Variable", 12F) };
            Controls.Add(lblNfc);
            txtNfc = new TextBox { Location = new Point(24, y + 30), Width = 360, Font = new Font("Segoe UI Variable", 14F) };
            txtNfc.GotFocus += TrackTextFocus;
            Controls.Add(txtNfc);
            btnClearNfc = new Button { Text = "X", Location = new Point(392, y + 28), Size = new Size(44, 36), BackColor = Color.FromArgb(229, 57, 53), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI Variable", 12F, FontStyle.Bold) };
            btnClearNfc.FlatAppearance.BorderSize = 0;
            btnClearNfc.Click += (s, e) => txtNfc.Text = string.Empty;
            Controls.Add(btnClearNfc);

            var mgbClearNfc = new ModernGradientButton { Text = "X", Location = btnClearNfc.Location, Size = btnClearNfc.Size, Font = btnClearNfc.Font, GradientStart = UiTheme.DangerStart, GradientEnd = UiTheme.DangerEnd };
            mgbClearNfc.Click += (s, e) => txtNfc.Text = string.Empty;
            Controls.Remove(btnClearNfc);
            try { btnClearNfc.Dispose(); } catch { }
            btnClearNfc = mgbClearNfc;
            Controls.Add(btnClearNfc);

            btnNfcUebernehmen = new ModernGradientButton { Text = "NFC übernehmen", Location = new Point(444, y + 28), Size = new Size(140, 36), Font = new Font("Segoe UI Variable", 10F, FontStyle.Bold), GradientStart = UiTheme.PrimaryStart, GradientEnd = UiTheme.PrimaryEnd };
            btnNfcUebernehmen.Click += (s, e) => TryTakeLastNfc();
            Controls.Add(btnNfcUebernehmen);

            y += 80;
            var lblFcode = new Label { Text = "Fahrercode:", Location = new Point(24, y), AutoSize = true, Font = new Font("Segoe UI Variable", 12F) };
            Controls.Add(lblFcode);
            txtFahrercode = new TextBox { Location = new Point(24, y + 30), Width = 220, Font = new Font("Segoe UI Variable", 18F), TextAlign = HorizontalAlignment.Center, UseSystemPasswordChar = true, MaxLength = 8 };
            txtFahrercode.GotFocus += TrackTextFocus;
            Controls.Add(txtFahrercode);
            btnShowHideCode = new Button { Text = "👁", Location = new Point(250, y + 30), Size = new Size(44, 44), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(245, 247, 250) };
            btnShowHideCode.FlatAppearance.BorderSize = 0;
            btnShowHideCode.Click += (s, e) => { txtFahrercode.UseSystemPasswordChar = !txtFahrercode.UseSystemPasswordChar; };
            Controls.Add(btnShowHideCode);
            btnClearCode = new Button { Text = "Löschen", Location = new Point(300, y + 30), Size = new Size(120, 44), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(229, 57, 53), ForeColor = Color.White, Font = new Font("Segoe UI Variable", 12F, FontStyle.Bold) };
            btnClearCode.FlatAppearance.BorderSize = 0;
            btnClearCode.Click += (s, e) => txtFahrercode.Text = string.Empty;
            Controls.Add(btnClearCode);
            try
            {
                var mg = new ModernGradientButton { Text = btnClearCode.Text, Location = btnClearCode.Location, Size = btnClearCode.Size, Font = btnClearCode.Font, GradientStart = UiTheme.DangerStart, GradientEnd = UiTheme.DangerEnd };
                mg.Click += (s, e) => txtFahrercode.Text = string.Empty;
                Controls.Remove(btnClearCode);
                try { btnClearCode.Dispose(); } catch { }
                btnClearCode = mg;
                Controls.Add(btnClearCode);
            }
            catch { }

            y += 90;
            btnSave = new ModernGradientButton { Text = "Speichern", Location = new Point(24, y), Size = new Size(180, 48), Font = new Font("Segoe UI Variable", 14F, FontStyle.Bold), GradientStart = UiTheme.PrimaryStart, GradientEnd = UiTheme.PrimaryEnd };
            btnSave.Click += async (s, e) => await SaveAsync();
            Controls.Add(btnSave);

            int keypadBtnH = 50; int keypadPad = 8; int keypadRows = 4; int keypadHeight = keypadRows * (keypadBtnH + keypadPad) - keypadPad;
            numPadPanel = new Panel { Location = new Point(24, y + 70), Size = new Size(560, keypadHeight + 4), AutoScroll = false, Anchor = AnchorStyles.Left | AnchorStyles.Bottom | AnchorStyles.Right };
            Controls.Add(numPadPanel);
            BuildNumPad();

            try { BeginInvoke((Action)(() => { try { txtPid.Focus(); _lastFocusedTextBox = txtPid; txtPid.SelectionStart = txtPid.TextLength; } catch { } })); } catch { }

            // CoinFeeder NFC-Integration
            try
            {
                if (Program.CoinFeeder != null)
                {
                    Program.CoinFeeder.NfcReceived += OnCoinFeederNfc;
                    this.FormClosed += (s, e) => { try { Program.CoinFeeder.NfcReceived -= OnCoinFeederNfc; } catch { } };
                }
            }
            catch { }
        }

        // Header wird zentral über `ModernHeaderPanel` gezeichnet.

        private void BuildNumPad()
        {
            numPadPanel.Controls.Clear();
            string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "<", "0", "OK" };
            int btnW = 120, btnH = 50, pad = 8;
            int rows = (int)Math.Ceiling(keys.Length / 3.0);
            // Panelgröße ggf. anpassen falls Layout später geändert wird
            int neededHeight = rows * (btnH + pad) - pad;
            if (numPadPanel.Height < neededHeight + 4)
            {
                try { numPadPanel.Height = neededHeight + 4; } catch { }
            }

            for (int i = 0; i < keys.Length; i++)
            {
                int row = i / 3, col = i % 3;
                var b = new ModernGradientButton { Text = keys[i], Size = new Size(btnW, btnH), Location = new Point(col * (btnW + pad), row * (btnH + pad)), Font = new Font("Segoe UI Variable", 14F, FontStyle.Bold), TabStop = false, Tag = keys[i] };
                b.Click += (s, e) => OnNumPad((string)((Button)s).Tag);
                numPadPanel.Controls.Add(b);

                try
                {
                    var key = keys[i];
                    var mgb = b as ModernGradientButton;
                    if (mgb != null)
                    {
                        if (key == "OK") { mgb.GradientStart = UiTheme.SuccessStart; mgb.GradientEnd = UiTheme.SuccessEnd; }
                        else if (key == "<") { mgb.GradientStart = UiTheme.SecondaryStart; mgb.GradientEnd = UiTheme.SecondaryEnd; }
                        else { mgb.GradientStart = UiTheme.PrimaryStart; mgb.GradientEnd = UiTheme.PrimaryEnd; }
                    }
                }
                catch { }
            }
        }

        private void TrackTextFocus(object sender, System.EventArgs e)
        { _lastFocusedTextBox = sender as TextBox; }

        private void OnNumPad(string key)
        {
            TextBox target = null;
            if (txtPid != null && txtPid.Focused) target = txtPid;
            else if (txtNfc != null && txtNfc.Focused) target = txtNfc;
            else if (txtFahrercode != null && txtFahrercode.Focused) target = txtFahrercode;

            // Falls durch Button-Klick kein TextBox Fokus mehr: letzte gemerkte verwenden
            if (target == null) target = _lastFocusedTextBox;
            // Fallback falls noch null: Personalnummer
            if (target == null) target = txtPid ?? txtNfc ?? txtFahrercode;
            if (target == null) return;

            // Letzte Fokus-Box aktualisieren (für nachfolgenden Key)
            _lastFocusedTextBox = target;

            if (key == "OK")
            {
                if (target == txtPid)
                {
                    _ = LoadPersonalAsync();
                }
                return;
            }
            if (key == "<")
            {
                if (target.TextLength > 0) target.Text = target.Text.Substring(0, target.TextLength - 1);
                try { target.Focus(); target.SelectionStart = target.Text.Length; } catch { }
                return;
            }
            if (key.Length == 1 && char.IsDigit(key[0]) && (target.MaxLength == 0 || target.TextLength < target.MaxLength))
            {
                target.Text += key;
                try { target.Focus(); target.SelectionStart = target.Text.Length; } catch { }
            }
        }

        private void UpdateStatusIndicators()
        {
            if (_loadedPersonal == null) { lblStatus.Text = "Status:"; lblEintritt.Text = "Eintritt: -"; lblAustritt.Text = "Austritt: -"; return; }
            bool blocked = _loadedPersonal.Gesperrt; var today = DateTime.Today;
            bool futureEntry = _loadedPersonal.Eintrittsdatum.HasValue && _loadedPersonal.Eintrittsdatum.Value.Date > today;
            bool exitIsPlaceholder = _loadedPersonal.Austrittsdatum.HasValue && _loadedPersonal.Austrittsdatum.Value.Date == PlaceholderExitDate.Date;
            bool hasRealExit = _loadedPersonal.Austrittsdatum.HasValue && !exitIsPlaceholder;
            bool pastExit = hasRealExit && _loadedPersonal.Austrittsdatum.Value.Date < today;
            lblEintritt.Text = "Eintritt: " + (_loadedPersonal.Eintrittsdatum.HasValue ? _loadedPersonal.Eintrittsdatum.Value.ToString("dd.MM.yyyy") : "-");
            lblAustritt.Text = "Austritt: " + (hasRealExit ? _loadedPersonal.Austrittsdatum.Value.ToString("dd.MM.yyyy") : "");
            lblStatus.Text = blocked ? "Status: GESPERRT" : "Status: Aktiv";
            lblStatus.ForeColor = blocked ? Color.Red : Color.FromArgb(33, 150, 243);
            lblEintritt.ForeColor = futureEntry ? Color.Red : Color.Black;
            lblAustritt.ForeColor = pastExit ? Color.Red : Color.Black;
        }

        private bool IsCurrentlyLoginBlocked()
        {
            if (_loadedPersonal == null) return true; var today = DateTime.Today; if (_loadedPersonal.Gesperrt) return true; if (_loadedPersonal.Eintrittsdatum.HasValue && _loadedPersonal.Eintrittsdatum.Value.Date > today) return true; if (_loadedPersonal.Austrittsdatum.HasValue && _loadedPersonal.Austrittsdatum.Value.Date != PlaceholderExitDate.Date && _loadedPersonal.Austrittsdatum.Value.Date < today) return true; return false;
        }

        private async System.Threading.Tasks.Task LoadPersonalAsync()
        {
            if (!int.TryParse(txtPid.Text.Trim(), out var pid) || pid <= 0) { MessageBox.Show(this, "Bitte gültige Personalnummer eingeben.", "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            using (var db = new DatabaseHelper())
            {
                var p = await db.GetPersonalInfoAsync(pid); if (p == null) { MessageBox.Show(this, "Personalnummer nicht gefunden.", "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                if (p.Austrittsdatum.HasValue && p.Austrittsdatum.Value.Date == PlaceholderExitDate.Date) p.Austrittsdatum = null; // Platzhalter entfernen
                _currentPid = p.PID; _loadedPersonal = p; lblName.Text = $"Name: {p.Name}"; lblVorname.Text = $"Vorname: {p.Vorname}"; txtNfc.Text = p.NFC ?? string.Empty; txtFahrercode.Text = p.Fahrercode ?? string.Empty; UpdateStatusIndicators();
            }
        }

        private async System.Threading.Tasks.Task SaveAsync()
        {
            if (_currentPid <= 0)
            {
                MessageBox.Show(this, "Bitte zuerst Personal laden.", "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var db = new DatabaseHelper())
            {
                try
                {
                    // Leere Eingaben als leere Strings speichern (statt NULL)
                    string fahrercode = string.IsNullOrWhiteSpace(txtFahrercode.Text) ? string.Empty : txtFahrercode.Text.Trim();
                    string nfc        = string.IsNullOrWhiteSpace(txtNfc.Text)        ? string.Empty : txtNfc.Text.Trim();

                    await db.SetFahrercodeAsync(_currentPid, fahrercode);
                    await db.SetNfcAsync(_currentPid, nfc);

                    string warn = string.Empty;
                    if (IsCurrentlyLoginBlocked())
                    {
                        warn = "Achtung: Unter den aktuellen Bedingungen kann sich der Mitarbeiter NICHT anmelden (gesperrt oder ungültiges Eintritts/Austrittsdatum).";
                    }

                    MessageBox.Show(this,
                        string.IsNullOrEmpty(warn) ? "Gespeichert." : ("Gespeichert.\r\n" + warn),
                        string.IsNullOrEmpty(warn) ? "Info" : "Warnung",
                        MessageBoxButtons.OK,
                        string.IsNullOrEmpty(warn) ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Fehler beim Speichern: " + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void TryTakeLastNfc()
        {
            try
            {
                if (Program.CoinFeeder == null) return;
                var prop = Program.CoinFeeder.GetType().GetField("_lastEmittedNfcToken", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (prop != null)
                {
                    var val = prop.GetValue(Program.CoinFeeder) as string;
                    if (!string.IsNullOrWhiteSpace(val) && val.Trim('0').Length > 0)
                    {
                        txtNfc.Text = val.Trim();
                    }
                }
            }
            catch { }
        }

        private void OnCoinFeederNfc(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return;
            try
            {
                BeginInvoke((Action)(() =>
                {
                    if (token.Trim('0').Length == 0) return;
                    txtNfc.Text = token.Trim();
                }));
            }
            catch { }
        }

        private void PersonalForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Return)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
        }

        private void PersonalForm_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == '\r' || e.KeyChar == '\n')
            {
                e.Handled = true; // Verhindert Default-Button/Close
            }
        }
    }
}
