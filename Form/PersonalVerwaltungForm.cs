using System;
using System.Data;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Drawing.Drawing2D;

namespace Geldautomat
{
    public partial class PersonalVerwaltungForm : Form
    {
        private Panel headerPanel;
        private Button btnClose;
        private Button btnMinimize;
        private Label lblTitle;
        private Point _mouseDownLocation;
        private TextBox txtPid; private Label lblName; private Label lblVorname; private TextBox txtNfc; private TextBox txtFahrercode; private Button btnSave; private Panel numPadPanel; private Button btnClearNfc; private Button btnShowHideCode; private Button btnClearCode; private Button btnNfcUebernehmen; private int _currentPid = 0; private TextBox _lastFocusedTextBox; private Label lblStatus; private Label lblEintritt; private Label lblAustritt; private PersonalInfo _loadedPersonal;
        private static readonly DateTime PlaceholderExitDate = new DateTime(1899, 12, 30); // Platzhalter

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

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

            // Header mit Farbverlauf
            headerPanel = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(ClientSize.Width, 60),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };
            headerPanel.Paint += HeaderPanel_Paint;
            headerPanel.MouseDown += HeaderPanel_MouseDown;
            headerPanel.MouseMove += HeaderPanel_MouseMove;
            Controls.Add(headerPanel);

            lblTitle = new Label
            {
                Text = "Personal",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Variable", 18F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(24, 0),
                Size = new Size(400, 60),
                BackColor = Color.Transparent
            };
            headerPanel.Controls.Add(lblTitle);

            btnClose = new Button
            {
                Text = "\u2715",
                Font = new Font("Segoe UI Symbol", 18F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(48, 48),
                Location = new Point(ClientSize.Width - 56, 6),
                TabStop = false
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 80, 80);
            btnClose.Click += (s, e) => Close();
            headerPanel.Controls.Add(btnClose);

            btnMinimize = new Button
            {
                Text = "–",
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(48, 48),
                Location = new Point(ClientSize.Width - 112, 6),
                TabStop = false
            };
            btnMinimize.FlatAppearance.BorderSize = 0;
            btnMinimize.FlatAppearance.MouseOverBackColor = Color.FromArgb(33, 150, 243, 80);
            btnMinimize.Click += (s, e) => WindowState = FormWindowState.Minimized;
            headerPanel.Controls.Add(btnMinimize);

            // Abgerundete Ecken
            try { Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, Width, Height, 24, 24)); } catch { }

            int y = 80;
            var lblPid = new Label { Text = "Personalnummer:", Location = new Point(24, y), AutoSize = true, Font = new Font("Segoe UI Variable", 12F) };
            Controls.Add(lblPid);
            txtPid = new TextBox { Location = new Point(24, y + 30), Width = 220, Font = new Font("Segoe UI Variable", 18F), TextAlign = HorizontalAlignment.Center, MaxLength = 8 };
            txtPid.GotFocus += TrackTextFocus; // Fokus-Tracking
            Controls.Add(txtPid);
            var btnLoad = new Button { Text = "Laden", Location = new Point(260, y + 30), Size = new Size(120, 44), BackColor = Color.FromArgb(33, 150, 243), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI Variable", 12F, FontStyle.Bold) };
            btnLoad.FlatAppearance.BorderSize = 0;
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
            btnNfcUebernehmen = new Button { Text = "NFC übernehmen", Location = new Point(444, y + 28), Size = new Size(140, 36), BackColor = Color.FromArgb(33, 150, 243), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI Variable", 10F, FontStyle.Bold) };
            btnNfcUebernehmen.FlatAppearance.BorderSize = 0;
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

            y += 90;
            btnSave = new Button { Text = "Speichern", Location = new Point(24, y), Size = new Size(180, 48), BackColor = Color.FromArgb(33, 150, 243), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI Variable", 14F, FontStyle.Bold) };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Click += async (s, e) => await SaveAsync();
            Controls.Add(btnSave);

            int keypadBtnH = 50; int keypadPad = 8; int keypadRows = 4; int keypadHeight = keypadRows * (keypadBtnH + keypadPad) - keypadPad;
            numPadPanel = new Panel { Location = new Point(24, y + 70), Size = new Size(560, keypadHeight + 4), AutoScroll = false, Anchor = AnchorStyles.Left | AnchorStyles.Bottom | AnchorStyles.Right };
            Controls.Add(numPadPanel);
            BuildNumPad();

            try { BeginInvoke((Action)(() => { try { txtPid.Focus(); _lastFocusedTextBox = txtPid; txtPid.SelectionStart = txtPid.TextLength; } catch { } })); } catch { }

            // CoinFeeder NFC-Integration: falls vorhanden, NFC in TextBox schreiben
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

        private void HeaderPanel_Paint(object sender, PaintEventArgs e)
        {
            using (var brush = new LinearGradientBrush(headerPanel.ClientRectangle,
                Color.FromArgb(33, 150, 243), Color.FromArgb(33, 203, 243), 0f))
            {
                e.Graphics.FillRectangle(brush, headerPanel.ClientRectangle);
            }
        }
        private void HeaderPanel_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                _mouseDownLocation = e.Location;
        }
        private void HeaderPanel_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Left += e.X - _mouseDownLocation.X;
                Top += e.Y - _mouseDownLocation.Y;
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
                var b = new Button { Text = keys[i], Size = new Size(btnW, btnH), Location = new Point(col * (btnW + pad), row * (btnH + pad)), FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI Variable", 14F, FontStyle.Bold), BackColor = Color.FromArgb(245, 247, 250), TabStop = false, Tag = keys[i] };
                b.FlatAppearance.BorderSize = 0;
                b.Click += (s, e) => OnNumPad((string)((Button)s).Tag);
                numPadPanel.Controls.Add(b);
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
                    await db.SetFahrercodeAsync(_currentPid, string.IsNullOrWhiteSpace(txtFahrercode.Text) ? null : txtFahrercode.Text.Trim());
                    await db.SetNfcAsync(_currentPid, string.IsNullOrWhiteSpace(txtNfc.Text) ? null : txtNfc.Text.Trim());
                    string warn = string.Empty;
                    if (IsCurrentlyLoginBlocked())
                    {
                        warn = "Achtung: Unter den aktuellen Bedingungen kann sich der Mitarbeiter NICHT anmelden (gesperrt oder ungültiges Eintritts/Austrittsdatum).";
                    }
                    MessageBox.Show(this, string.IsNullOrEmpty(warn) ? "Gespeichert." : ("Gespeichert.\r\n" + warn), string.IsNullOrEmpty(warn) ? "Info" : "Warnung", MessageBoxButtons.OK, string.IsNullOrEmpty(warn) ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
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
                        txtNfc.Text = val.Trim();
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
                    // Nur sinnvolle Tokens akzeptieren
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
