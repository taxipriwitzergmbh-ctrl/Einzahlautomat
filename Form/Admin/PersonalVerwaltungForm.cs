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
        private Panel _contentPanel;
        private TextBox txtPid; private ComboBox cboPersonal; private Label lblName; private Label lblVorname; private TextBox txtNfc; private TextBox txtFahrercode; private Button btnSave; private Panel numPadPanel; private Button btnClearNfc; private Button btnShowHideCode; private Button btnClearCode; private Button btnNfcUebernehmen; private int _currentPid = 0; private TextBox _lastFocusedTextBox; private Label lblStatus; private Label lblEintritt; private Label lblAustritt; private PersonalInfo _loadedPersonal;
        private static readonly DateTime PlaceholderExitDate = new DateTime(1899, 12, 30); // Platzhalter

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

        // Header & Buttons werden zentral über `ModernHeaderPanel` / `ModernGradientButton` gestaltet.

        public PersonalVerwaltungForm() { InitUi(); }

        private sealed class PersonalDropdownItem
        {
            public int PID { get; set; }
            public string Name { get; set; }
            public string Vorname { get; set; }
            public override string ToString()
            {
                var fullName = ((Name ?? string.Empty) + ", " + (Vorname ?? string.Empty)).Trim(' ', ',');
                return string.IsNullOrWhiteSpace(fullName) ? PID.ToString() : $"{PID} - {fullName}";
            }
        }

        private Panel CreateSectionPanel(Point location, Size size)
        {
            return new Panel
            {
                Location = location,
                Size = size,
                BackColor = Color.FromArgb(248, 250, 252),
                BorderStyle = BorderStyle.FixedSingle
            };
        }

        private Label CreateSectionTitle(string text, Point location)
        {
            return new Label
            {
                Text = text,
                Location = location,
                AutoSize = true,
                Font = new Font("Segoe UI Variable", 12.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(33, 150, 243)
            };
        }

        private Label CreateFieldLabel(string text, Point location)
        {
            return new Label
            {
                Text = text,
                Location = location,
                AutoSize = true,
                Font = new Font("Segoe UI Variable", 11F)
            };
        }

        private void InitUi()
        {
            // Fenster-Setup
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(700, 1040);
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

            _contentPanel = new Panel
            {
                Location = new Point(0, _header.Bottom),
                Size = new Size(ClientSize.Width, ClientSize.Height - _header.Height),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                AutoScroll = true,
                BackColor = Color.White
            };
            Controls.Add(_contentPanel);

            int y = 20;
            int sectionWidth = ClientSize.Width - 48;
            int innerLeft = 18;
            int sectionGap = 18;

            var searchPanel = CreateSectionPanel(new Point(24, y), new Size(sectionWidth, 128));
            searchPanel.Controls.Add(CreateSectionTitle("Mitarbeiter suchen", new Point(innerLeft, 12)));

            var lblPid = CreateFieldLabel("Personalnummer", new Point(innerLeft, 48));
            searchPanel.Controls.Add(lblPid);
            txtPid = new TextBox { Location = new Point(innerLeft, 72), Width = 120, Font = new Font("Segoe UI Variable", 18F), TextAlign = HorizontalAlignment.Center, MaxLength = 5 };
            txtPid.GotFocus += TrackTextFocus; // Fokus-Tracking
            searchPanel.Controls.Add(txtPid);
            var btnLoad = new ModernGradientButton { Text = "⟳", Location = new Point(148, 72), Size = new Size(44, 44), Font = new Font("Segoe UI Variable", 14F, FontStyle.Bold), GradientStart = UiTheme.PrimaryStart, GradientEnd = UiTheme.PrimaryEnd };
            btnLoad.Click += async (s, e) => await LoadPersonalAsync();
            searchPanel.Controls.Add(btnLoad);

            var lblPersonalAuswahl = CreateFieldLabel("Mitarbeiter auswählen", new Point(218, 48));
            searchPanel.Controls.Add(lblPersonalAuswahl);
            cboPersonal = new ComboBox
            {
                Location = new Point(218, 74),
                Width = 396,
                Font = new Font("Segoe UI Variable", 13F),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cboPersonal.SelectedIndexChanged += async (s, e) =>
            {
                if (cboPersonal.SelectedItem is PersonalDropdownItem item && item.PID > 0 && item.PID != _currentPid)
                {
                    txtPid.Text = item.PID.ToString();
                    await LoadPersonalAsync();
                }
            };
            searchPanel.Controls.Add(cboPersonal);
            _contentPanel.Controls.Add(searchPanel);

            y = searchPanel.Bottom + sectionGap;
            var infoPanel = CreateSectionPanel(new Point(24, y), new Size(sectionWidth, 168));
            infoPanel.Controls.Add(CreateSectionTitle("Personaldaten", new Point(innerLeft, 12)));
            lblName = new Label { Text = "Name:", Location = new Point(innerLeft, 48), Size = new Size(600, 28), Font = new Font("Segoe UI Variable", 12F) };
            lblVorname = new Label { Text = "Vorname:", Location = new Point(innerLeft, 76), Size = new Size(600, 28), Font = new Font("Segoe UI Variable", 12F) };
            infoPanel.Controls.Add(lblName);
            infoPanel.Controls.Add(lblVorname);

            lblStatus = new Label { Text = "Status:", Location = new Point(innerLeft, 108), Size = new Size(600, 24), Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold) };
            lblEintritt = new Label { Text = "Eintritt: -", Location = new Point(innerLeft, 132), Size = new Size(260, 22), Font = new Font("Segoe UI Variable", 10.5F) };
            lblAustritt = new Label { Text = "Austritt: -", Location = new Point(320, 132), Size = new Size(280, 22), Font = new Font("Segoe UI Variable", 10.5F) };
            infoPanel.Controls.Add(lblStatus);
            infoPanel.Controls.Add(lblEintritt);
            infoPanel.Controls.Add(lblAustritt);
            _contentPanel.Controls.Add(infoPanel);

            y = infoPanel.Bottom + sectionGap;
            var accessPanel = CreateSectionPanel(new Point(24, y), new Size(sectionWidth, 206));
            accessPanel.Controls.Add(CreateSectionTitle("Zugangsdaten", new Point(innerLeft, 12)));

            var lblNfc = CreateFieldLabel("NFC", new Point(innerLeft, 48));
            accessPanel.Controls.Add(lblNfc);
            txtNfc = new TextBox { Location = new Point(innerLeft, 72), Width = 382, Font = new Font("Segoe UI Variable", 14F) };
            txtNfc.GotFocus += TrackTextFocus;
            accessPanel.Controls.Add(txtNfc);
            btnClearNfc = new Button { Text = "X", Location = new Point(408, 72), Size = new Size(44, 36), BackColor = Color.FromArgb(229, 57, 53), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI Variable", 12F, FontStyle.Bold) };
            btnClearNfc.FlatAppearance.BorderSize = 0;
            btnClearNfc.Click += (s, e) => txtNfc.Text = string.Empty;
            accessPanel.Controls.Add(btnClearNfc);

            var mgbClearNfc = new ModernGradientButton { Text = "X", Location = btnClearNfc.Location, Size = btnClearNfc.Size, Font = btnClearNfc.Font, GradientStart = UiTheme.DangerStart, GradientEnd = UiTheme.DangerEnd };
            mgbClearNfc.Click += (s, e) => txtNfc.Text = string.Empty;
            accessPanel.Controls.Remove(btnClearNfc);
            try { btnClearNfc.Dispose(); } catch { }
            btnClearNfc = mgbClearNfc;
            accessPanel.Controls.Add(btnClearNfc);

            btnNfcUebernehmen = new ModernGradientButton { Text = "NFC übernehmen", Location = new Point(468, 72), Size = new Size(146, 36), Font = new Font("Segoe UI Variable", 10F, FontStyle.Bold), GradientStart = UiTheme.PrimaryStart, GradientEnd = UiTheme.PrimaryEnd };
            btnNfcUebernehmen.Click += (s, e) => TryTakeLastNfc();
            accessPanel.Controls.Add(btnNfcUebernehmen);

            var lblFcode = CreateFieldLabel("Fahrercode", new Point(innerLeft, 116));
            accessPanel.Controls.Add(lblFcode);
            txtFahrercode = new TextBox { Location = new Point(innerLeft, 140), Width = 220, Font = new Font("Segoe UI Variable", 18F), TextAlign = HorizontalAlignment.Center, UseSystemPasswordChar = true, MaxLength = 8 };
            txtFahrercode.GotFocus += TrackTextFocus;
            accessPanel.Controls.Add(txtFahrercode);
            btnShowHideCode = new Button { Text = "👁", Location = new Point(244, 140), Size = new Size(44, 44), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(245, 247, 250) };
            btnShowHideCode.FlatAppearance.BorderSize = 0;
            btnShowHideCode.Click += (s, e) => { txtFahrercode.UseSystemPasswordChar = !txtFahrercode.UseSystemPasswordChar; };
            accessPanel.Controls.Add(btnShowHideCode);
            btnClearCode = new Button { Text = "Löschen", Location = new Point(298, 140), Size = new Size(130, 44), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(229, 57, 53), ForeColor = Color.White, Font = new Font("Segoe UI Variable", 12F, FontStyle.Bold) };
            btnClearCode.FlatAppearance.BorderSize = 0;
            btnClearCode.Click += (s, e) => txtFahrercode.Text = string.Empty;
            accessPanel.Controls.Add(btnClearCode);
            try
            {
                var mg = new ModernGradientButton { Text = btnClearCode.Text, Location = btnClearCode.Location, Size = btnClearCode.Size, Font = btnClearCode.Font, GradientStart = UiTheme.DangerStart, GradientEnd = UiTheme.DangerEnd };
                mg.Click += (s, e) => txtFahrercode.Text = string.Empty;
                accessPanel.Controls.Remove(btnClearCode);
                try { btnClearCode.Dispose(); } catch { }
                btnClearCode = mg;
                accessPanel.Controls.Add(btnClearCode);
            }
            catch { }
            _contentPanel.Controls.Add(accessPanel);

            y = accessPanel.Bottom + sectionGap;
            var actionPanel = CreateSectionPanel(new Point(24, y), new Size(sectionWidth, 356));
            actionPanel.Controls.Add(CreateSectionTitle("Aktionen", new Point(innerLeft, 12)));
            btnSave = new ModernGradientButton { Text = "Speichern", Location = new Point((sectionWidth - 220) / 2, 46), Size = new Size(220, 48), Font = new Font("Segoe UI Variable", 14F, FontStyle.Bold), GradientStart = UiTheme.PrimaryStart, GradientEnd = UiTheme.PrimaryEnd };
            btnSave.Click += async (s, e) => await SaveAsync();
            actionPanel.Controls.Add(btnSave);

            int keypadBtnH = 50; int keypadPad = 8; int keypadRows = 4; int keypadHeight = keypadRows * (keypadBtnH + keypadPad) - keypadPad;
            numPadPanel = new Panel { Location = new Point((sectionWidth - 376) / 2, 112), Size = new Size(376, keypadHeight + 4), AutoScroll = false, Anchor = AnchorStyles.Top, BackColor = Color.Transparent };
            actionPanel.Controls.Add(numPadPanel);
            _contentPanel.Controls.Add(actionPanel);
            BuildNumPad();

            _ = LoadPersonalDropdownAsync();

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


        private async System.Threading.Tasks.Task LoadPersonalDropdownAsync()
        {
            if (cboPersonal == null) return;

            try
            {
                using (var db = new DatabaseHelper())
                {
                    // Nutzt die zentrale Abfrage aus DatabaseHelper: TPersonal, nur nicht gesperrte Mitarbeiter.
                    var mitarbeiterList = await db.GetAktiveMitarbeiterAsync();
                    if (mitarbeiterList == null) return;

                    var items = new List<PersonalDropdownItem>();
                    foreach (var mitarbeiter in mitarbeiterList)
                    {
                        if (mitarbeiter.PID <= 0) continue;
                        items.Add(new PersonalDropdownItem
                        {
                            PID = mitarbeiter.PID,
                            Name = mitarbeiter.Name ?? string.Empty,
                            Vorname = string.Empty
                        });
                    }

                    cboPersonal.BeginUpdate();
                    cboPersonal.Items.Clear();
                    foreach (var item in items) cboPersonal.Items.Add(item);
                    cboPersonal.EndUpdate();
                }
            }
            catch
            {
                // Dropdown ist Komfortfunktion; manuelles Laden per Personalnummer bleibt weiterhin möglich.
            }
        }

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
                _currentPid = p.PID; _loadedPersonal = p; lblName.Text = $"Name: {p.Name}"; lblVorname.Text = $"Vorname: {p.Vorname}"; txtNfc.Text = p.NFC ?? string.Empty; txtFahrercode.Text = p.Fahrercode ?? string.Empty; SelectPersonalInDropdown(p.PID); UpdateStatusIndicators();
            }
        }


        private void SelectPersonalInDropdown(int pid)
        {
            if (cboPersonal == null || pid <= 0) return;
            try
            {
                for (int i = 0; i < cboPersonal.Items.Count; i++)
                {
                    if (cboPersonal.Items[i] is PersonalDropdownItem item && item.PID == pid)
                    {
                        if (cboPersonal.SelectedIndex != i) cboPersonal.SelectedIndex = i;
                        return;
                    }
                }
            }
            catch { }
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
