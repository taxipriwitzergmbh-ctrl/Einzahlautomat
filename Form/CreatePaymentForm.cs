using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace TaMi_Einzahlautomat
{
    public class CreatePaymentForm : Form
    {
        // Modern header
        private Panel headerPanel;
        private Button btnClose;
        private Label lblTitle;
    

        private ComboBox cboFelder;
        private Button btnPickFeld;
        private Label lblPickedFeld;
        private NumericUpDown numBetrag;
        private ComboBox cboFirma;
        private Button btnPickFirma;
        private Label lblPickedFirma;
        private Button btnSave;
        private Button btnKeypad;
        private int _layoutTop;

        // Inline touch overlay for selections (no extra Form)
        private Panel _overlayPanel;
        private Label _overlayTitle;
        private TextBox _overlaySearch;
        private FlowLayoutPanel _overlayFlow;
        private Button _overlayClose;
        private Action<BigItem> _onOverlaySelect;

        // Robust tracking of selected firm
        private int? _selectedFirmaId;

        public CreatePaymentForm()
        {
            Text = "Zahlung anlegen";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(800, 520);
            FormBorderStyle = FormBorderStyle.None;
            DoubleBuffered = true;
            MaximizeBox = false;
            TopMost = true;

            // Header
            headerPanel = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(ClientSize.Width, 80),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            headerPanel.Paint += HeaderPanel_Paint;
            Controls.Add(headerPanel);

            lblTitle = new Label
            {
                Text = "Zahlung anlegen",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Variable", 24F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(24, 0),
                Size = new Size(520, 80),
                BackColor = Color.Transparent
            };
            headerPanel.Controls.Add(lblTitle);

            btnClose = new Button
            {
                Text = "\u2715",
                Font = new Font("Segoe UI Symbol", 22F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(56, 56),
                Location = new Point(ClientSize.Width - 68, 12),
                TabStop = false
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 80, 80);
            btnClose.Click += (s, e) => Close();
            headerPanel.Controls.Add(btnClose);

            headerPanel.Resize += (s, e) => UpdateHeaderLayout();
            UpdateHeaderLayout();
            try { Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, Width, Height, 24, 24)); } catch { }

            int top = headerPanel.Bottom + 24;
            _layoutTop = top;
            var lblFeld = new Label { Text = "Zahlungsfeld:", Location = new Point(24, top), AutoSize = true, Font = new Font("Segoe UI Variable", 16F, FontStyle.Bold) };
            cboFelder = new ComboBox { Location = new Point(260, top - 6), Width = 480, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 14F), IntegralHeight = false, Visible = false };
            btnPickFeld = new Button { Text = "Feld wählen", Location = new Point(260, top - 10), Size = new Size(220, 48), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(33,150,243), ForeColor = Color.White, Font = new Font("Segoe UI Variable", 16F, FontStyle.Bold) };
            btnPickFeld.FlatAppearance.BorderSize = 0;
            lblPickedFeld = new Label { Text = "Bitte Auswahl treffen", Location = new Point(btnPickFeld.Right + 12, top - 2), AutoSize = false, Size = new Size(240, 34), Font = new Font("Segoe UI", 12F, FontStyle.Regular), ForeColor = Color.DimGray, Visible = false };

            var data = PaymentSettingsStore.Load();
            var felder = data.Felder.ToList();
            // Platzhalter für Zahlungsfeld hinzufügen
            try
            {
                felder.Insert(0, new PaymentFieldSetting { Bezeichnung = "Bitte Auswahl treffen", MaxBetrag = 0m, Typ = 2, MwSt = MwStType.Mwst0 });
            }
            catch { }
            cboFelder.DataSource = felder;
            cboFelder.DisplayMember = nameof(PaymentFieldSetting.Bezeichnung);
            try { cboFelder.SelectedIndex = 0; } catch { }
            var lblBetrag = new Label { Text = "Betrag:", Location = new Point(24, top + 60), AutoSize = true, Font = new Font("Segoe UI Variable", 16F, FontStyle.Bold) };
            numBetrag = new NumericUpDown { Location = new Point(260, top + 56), Width = 320, DecimalPlaces = 2, Minimum = 0, Maximum = 1000000, Increment = 0.50m, Font = new Font("Segoe UI", 16F), ThousandsSeparator = true };
            btnKeypad = new Button { Text = "\u2328", Location = new Point(260 + 320 + 8, top + 54), Size = new Size(60, 48), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(33,150,243), ForeColor = Color.White };
            btnKeypad.FlatAppearance.BorderSize = 0;
            btnKeypad.Click += (s, e) => ShowKeypadDialog();

            var lblFirma = new Label { Text = "Firma:", Location = new Point(24, top + 120), AutoSize = true, Font = new Font("Segoe UI Variable", 16F, FontStyle.Bold) };
            cboFirma = new ComboBox { Location = new Point(260, top + 116), Width = 480, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 14F), IntegralHeight = false, Visible = false };
            btnPickFirma = new Button { Text = "Firma wählen", Location = new Point(260, top + 110), Size = new Size(220, 48), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(33,150,243), ForeColor = Color.White, Font = new Font("Segoe UI Variable", 16F, FontStyle.Bold) };
            btnPickFirma.FlatAppearance.BorderSize = 0;
            lblPickedFirma = new Label { Text = "Bitte Auswahl treffen", Location = new Point(btnPickFirma.Right + 12, top + 118), AutoSize = false, Size = new Size(240, 34), Font = new Font("Segoe UI", 12F, FontStyle.Regular), ForeColor = Color.DimGray, Visible = false };

            // Firmenliste aus DB lesen (Helper) + Platzhalter einfügen
            Load += async (s, e) =>
            {
                try
                {
                    using (var db = new DatabaseHelper())
                    {
                        var dt = await db.GetMandantenAsync(onlyForPayments: true);
                        // Optional: nach AllowedManIDs filtern
                        var allowed = ParseAllowedManIds();
                        if (allowed != null && allowed.Count > 0 && dt != null)
                        {
                            try
                            {
                                var toRemove = new System.Collections.Generic.List<DataRow>();
                                foreach (DataRow r in dt.Rows)
                                {
                                    int manId = 0;
                                    if (dt.Columns.Contains("ManID") && r["ManID"] != DBNull.Value)
                                        int.TryParse(Convert.ToString(r["ManID"]), out manId);
                                    if (!allowed.Contains(manId)) toRemove.Add(r);
                                }
                                foreach (var r in toRemove) dt.Rows.Remove(r);
                            }
                            catch { }
                        }

                        // neues DataTable mit Platzhalter an erster Stelle aufbauen
                        var table = dt.Clone();
                        if (!table.Columns.Contains("ManName")) table.Columns.Add("ManName", typeof(string));
                        if (!table.Columns.Contains("ManID")) table.Columns.Add("ManID", typeof(int));
                        try
                        {
                            var placeholder = table.NewRow();
                            placeholder["ManName"] = "Bitte Auswahl treffen";
                            placeholder["ManID"] = DBNull.Value;
                            table.Rows.Add(placeholder);
                        }
                        catch { }
                        try { foreach (DataRow r in dt.Rows) table.ImportRow(r); } catch { }

                        // Clear previous binding to avoid auto-selecting first real item
                        try { cboFirma.DataSource = null; } catch { }
                        cboFirma.DisplayMember = "ManName";
                        cboFirma.ValueMember = "ManID";
                        cboFirma.DataSource = table;

                        // Vorauswahl robust: über gebundene Items zählen und bei genau einem echten Mandanten auswählen
                        try
                        {
                            int realCount = 0; int onlyIndex = -1; object onlyId = null; string onlyName = null;
                            for (int i = 0; i < cboFirma.Items.Count; i++)
                            {
                                var drv = cboFirma.Items[i] as DataRowView;
                                if (drv == null) continue;
                                if (drv.Row == null || !drv.Row.Table.Columns.Contains("ManID")) continue;
                                if (drv.Row["ManID"] == DBNull.Value) continue; // Platzhalter
                                realCount++;
                                if (onlyIndex < 0)
                                {
                                    onlyIndex = i;
                                    onlyId = drv.Row["ManID"];
                                    if (drv.Row.Table.Columns.Contains("ManName"))
                                        onlyName = Convert.ToString(drv.Row["ManName"]) ?? null;
                                }
                                if (realCount > 1) break;
                            }
                            if (realCount == 1 && onlyIndex >= 0)
                            {
                                // Direkt setzen
                                cboFirma.SelectedIndex = onlyIndex;
                                try { if (onlyId != null) cboFirma.SelectedValue = onlyId; } catch { }
                                try { if (!string.IsNullOrWhiteSpace(onlyName) && btnPickFirma != null) btnPickFirma.Text = onlyName; } catch { }
                                _selectedFirmaId = (onlyId == null || onlyId == DBNull.Value) ? (int?)null : Convert.ToInt32(onlyId);
                                try { ValidateMaxAmount(); } catch { }

                                // Fallback: nach Binding-Cycle nochmal setzen
                                try
                                {
                                    BeginInvoke((Action)(() =>
                                    {
                                        try
                                        {
                                            if (onlyIndex >= 0) cboFirma.SelectedIndex = onlyIndex;
                                            if (onlyId != null) cboFirma.SelectedValue = onlyId;
                                            if (!string.IsNullOrWhiteSpace(onlyName) && btnPickFirma != null) btnPickFirma.Text = onlyName;
                                        }
                                        catch { }
                                        _selectedFirmaId = (onlyId == null || onlyId == DBNull.Value) ? (int?)null : Convert.ToInt32(onlyId);
                                        try { ValidateMaxAmount(); } catch { }
                                    }));
                                }
                                catch { }
                            }
                            else
                            {
                                cboFirma.SelectedIndex = 0;
                                try { cboFirma.SelectedValue = DBNull.Value; } catch { }
                                _selectedFirmaId = null;
                                try { if (btnPickFirma != null) btnPickFirma.Text = "Firma wählen"; } catch { }
                            }
                        }
                        catch { try { cboFirma.SelectedIndex = 0; _selectedFirmaId = null; } catch { } }

                        // Button-Text initial setzen
                        try
                        {
                            if (btnPickFirma != null)
                            {
                                if (cboFirma.SelectedIndex > 0 && cboFirma.SelectedItem is DataRowView drv && drv.Row.Table.Columns.Contains("ManName"))
                                    btnPickFirma.Text = Convert.ToString(drv.Row["ManName"]) ?? "Firma wählen";
                                else
                                    btnPickFirma.Text = "Firma wählen";
                            }
                        }
                        catch { }

                        // Update touch label (bleibt Hinweistext)
                        try { lblPickedFirma.Text = Convert.ToString((table.Rows[0]["ManName"])) ?? "Bitte Auswahl treffen"; } catch { }
                    }
                }
                catch { }
                PositionActionButtons();
                try { ValidateMaxAmount(); } catch { }
                try { BeginInvoke((Action)(() => ValidateMaxAmount())); } catch { }
            };

            btnSave = new Button { Text = "Zahlung anlegen", Location = new Point(ClientSize.Width - 240, ClientSize.Height - 80), Size = new Size(220, 46), BackColor = Color.FromArgb(46, 125, 50), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI Variable", 14F, FontStyle.Bold), Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Click += async (s, e) =>
            {
                var feld = cboFelder.SelectedItem as PaymentFieldSetting;
                if (feld == null || cboFelder.SelectedIndex == 0)
                {
                    MessageBox.Show(this, "Bitte Zahlungsfeld wählen.");
                    return;
                }
                var betrag = numBetrag.Value;
                if (betrag <= 0)
                {
                    MessageBox.Show(this, "Betrag muss > 0 sein.");
                    return;
                }
                if (betrag > feld.MaxBetrag)
                {
                    MessageBox.Show(this, $"Maximal erlaubter Betrag: {feld.MaxBetrag:N2}");
                    return;
                }

                // Firma muss ausgewählt sein (über robusten Marker)
                if (!_selectedFirmaId.HasValue || _selectedFirmaId.Value <= 0)
                {
                    MessageBox.Show(this, "Bitte Firma wählen.");
                    return;
                }

                decimal b19 = 0, b7 = 0, b0 = 0;
                switch (feld.MwSt)
                {
                    case MwStType.Mwst19: b19 = betrag; break;
                    case MwStType.Mwst7:  b7  = betrag; break;
                    case MwStType.Mwst0:  b0  = betrag; break;
                }

                try
                {
                    int persId = 0; string mitarbeiter = null;
                    try { persId = AbrechnungForm.CurrentPersonalId; } catch { }
                    try
                    {
                        using (var db = new DatabaseHelper())
                        {
                            var pi = await db.GetPersonalInfoAsync(persId);
                            if (pi != null)
                                mitarbeiter = ($"{pi.Vorname} {pi.Name}").Trim();
                        }
                    }
                    catch { }

                    string vorgabe = !string.IsNullOrWhiteSpace(feld.Buchungstext) ? feld.Buchungstext.Trim() : (feld.Bezeichnung ?? string.Empty).Trim();
                    string buchungstext = string.IsNullOrWhiteSpace(mitarbeiter) ? vorgabe : ($"{mitarbeiter}-{vorgabe}");

                    int firmenId = _selectedFirmaId.GetValueOrDefault(0);

                    using (var db = new DatabaseHelper())
                    {
                        byte typ = (feld.Typ == 3) ? (byte)3 : (byte)2;
                        int? k1 = ToNullableInt(feld.Kost1);
                        int? k2 = ToNullableInt(feld.Kost2);
                        int? kto = ToNullableInt(feld.Konto);
                        await db.InsertKassenbuchZahlungAsync(typ, buchungstext ?? string.Empty, b19, b7, b0,
                            persId, firmenId, k1, k2, kto, betrag);
                    }
                    MessageBox.Show(this, "Zahlung wurde angelegt.", "Erfolg", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    DialogResult = DialogResult.OK;
                    Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Fehler beim Anlegen: " + ex.Message);
                }
            };
            
            // Validierung bei Änderungen
            cboFelder.SelectedIndexChanged += (s, e) => ValidateMaxAmount();
            numBetrag.ValueChanged += (s, e) => ValidateMaxAmount();
            cboFirma.SelectedIndexChanged += (s, e) =>
            {
                try
                {
                    // Update selection marker from ComboBox
                    int? id = null;
                    var val = cboFirma.SelectedValue;
                    if (val != null && val != DBNull.Value && !string.IsNullOrWhiteSpace(Convert.ToString(val)))
                    {
                        int tmp; if (int.TryParse(Convert.ToString(val), out tmp) && tmp > 0) id = tmp;
                    }
                    else if (cboFirma.SelectedItem is DataRowView drv && drv.Row != null && drv.Row.Table.Columns.Contains("ManID") && drv.Row["ManID"] != DBNull.Value)
                    {
                        int tmp; if (int.TryParse(Convert.ToString(drv.Row["ManID"]), out tmp) && tmp > 0) id = tmp;
                    }
                    _selectedFirmaId = id;
                }
                catch { _selectedFirmaId = null; }
                ValidateMaxAmount();
            };
            // Initial prüfen
            ValidateMaxAmount();

            // Buttons nach Resize korrekt positionieren
            this.Resize += (s, e) => PositionActionButtons();

            Controls.Add(lblFeld);
            Controls.Add(cboFelder);
            Controls.Add(btnPickFeld);
            Controls.Add(lblBetrag);
            Controls.Add(numBetrag);
            Controls.Add(btnKeypad);
            Controls.Add(lblFirma);
            Controls.Add(cboFirma);
            Controls.Add(btnPickFirma);
            Controls.Add(btnSave);

            // Touch pick handlers
            btnPickFeld.Click += (s, e) => ShowFieldPicker();
            btnPickFirma.Click += (s, e) => ShowFirmaPicker();

            // Reflect selection labels on combo changes
            cboFelder.SelectedIndexChanged += (s, e) =>
            {
                try
                {
                    var feld = cboFelder.SelectedItem as PaymentFieldSetting;
                    bool hasSelection = feld != null && cboFelder.SelectedIndex > 0;
                    if (btnPickFeld != null)
                    {
                        btnPickFeld.Text = hasSelection ? (feld?.Bezeichnung ?? "Feld wählen") : "Feld wählen";
                    }
                }
                catch { }
            };
            cboFirma.SelectedIndexChanged += (s, e) =>
            {
                try
                {
                    if (btnPickFirma == null) return;
                    // Platzhalter (Index 0) -> "Firma wählen"
                    if (cboFirma.SelectedIndex <= 0)
                    {
                        btnPickFirma.Text = "Firma wählen";
                        return;
                    }

                    string name = null;
                    var drv = cboFirma.SelectedItem as DataRowView;
                    if (drv != null && drv.DataView != null && drv.DataView.Table != null && drv.DataView.Table.Columns.Contains("ManName"))
                        name = Convert.ToString(drv["ManName"]);
                    if (string.IsNullOrWhiteSpace(name))
                        name = cboFirma.Text; // fallback to ComboBox text
                    btnPickFirma.Text = string.IsNullOrWhiteSpace(name) ? "Firma wählen" : name;
                }
                catch { }
            };

            // Fenster im Vordergrund halten, falls Fokus verloren geht
            this.Deactivate += (s, e) =>
            {
                try { TopMost = true; Activate(); BringToFront(); } catch { }
            };
        }

        private System.Collections.Generic.List<int> ParseAllowedManIds()
        {
            try
            {
                var raw = AppSettings.AllowedManIdsRaw;
                if (string.IsNullOrWhiteSpace(raw)) return null;
                var parts = raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                var list = new System.Collections.Generic.List<int>();
                foreach (var p in parts)
                {
                    int v;
                    if (int.TryParse(p.Trim(), out v) && v > 0) list.Add(v);
                }
                return list;
            }
            catch { return null; }
        }

        // Ziffernfeld mit Komma – Eingabe in Euro (5 = 5,00 €)

        private int? ToNullableInt(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            int v; return int.TryParse(s.Trim(), out v) ? (int?)v : null;
        }
        private void ShowKeypadDialog()
        {
            using (var dlg = new Form { Text = "Betrag eingeben", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(320, 460), MaximizeBox = false, MinimizeBox = false })
            {
                var txt = new TextBox { Location = new Point(20, 20), Width = 260, Font = new Font("Segoe UI", 20f) };
                var info = new Label { Text = "Eingabe in Euro (Komma erlaubt)", AutoSize = true, Location = new Point(20, 60), Font = new Font("Segoe UI", 12f) };
                dlg.Controls.Add(txt);
                dlg.Controls.Add(info);

                int x0 = 20, y0 = 90, w = 80, h = 64, gap = 10;
                string[] keys = { "7","8","9","4","5","6","1","2","3",",","0","C","OK" };
                for (int i = 0; i < keys.Length; i++)
                {
                    int row = i / 3;
                    int col = i % 3;
                    var b = new Button { Text = keys[i], Size = new Size(w, h), Location = new Point(x0 + col * (w + gap), y0 + row * (h + gap)), Font = new Font("Segoe UI", 16f) };
                    dlg.Controls.Add(b);
                    b.Click += (s, e) =>
                    {
                        var t = ((Button)s).Text;
                        if (t == "C") txt.Text = string.Empty;
                        else if (t == "OK") dlg.DialogResult = DialogResult.OK;
                        else if (t == ",")
                        {
                            if (!txt.Text.Contains(",")) txt.Text += ",";
                        }
                        else txt.Text += t;
                    };
                }

                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        var input = txt.Text?.Trim();
                        if (!string.IsNullOrEmpty(input))
                        {
                            input = input.Replace('.', ',');
                            if (decimal.TryParse(input, out var euro))
                            {
                                euro = Math.Round(euro, 2);
                                if (euro < 0) euro = 0;
                                if (euro > numBetrag.Maximum) euro = numBetrag.Maximum;
                                numBetrag.Value = euro;
                            }
                        }
                    }
                    catch { }
                }
            }
        }

        private void ValidateMaxAmount()
        {
            try
            {
                var feld = cboFelder.SelectedItem as PaymentFieldSetting;
                if (feld == null) { btnSave.Enabled = false; return; }
                var betrag = numBetrag.Value;

                bool fieldOk = cboFelder.SelectedIndex > 0 && feld != null;
                bool amountOk = betrag > 0 && (fieldOk ? betrag <= feld.MaxBetrag : false);

                bool firmaOk = _selectedFirmaId.HasValue && _selectedFirmaId.Value > 0;

                btnSave.Enabled = fieldOk && amountOk && firmaOk;
            }
            catch { }
        }

        private void HeaderPanel_Paint(object sender, PaintEventArgs e)
        {
            using (var b = new LinearGradientBrush(headerPanel.ClientRectangle,
                       Color.FromArgb(33, 150, 243), Color.FromArgb(33, 203, 243), 0f))
            {
                e.Graphics.FillRectangle(b, headerPanel.ClientRectangle);
            }
        }

        private void UpdateHeaderLayout()
        {
            try
            {
                int marginRight = 12;
                int spacing = 8;
                int top = 6;
                if (btnClose != null)
                    btnClose.Location = new Point(Math.Max(0, headerPanel.ClientSize.Width - marginRight - btnClose.Width), top);
                if (lblTitle != null)
                {
                    int left = lblTitle.Left;
                    int rightLimit = (btnClose != null) ? btnClose.Left - spacing : headerPanel.ClientSize.Width - spacing;
                    int newWidth = Math.Max(120, rightLimit - left);
                    lblTitle.Size = new Size(newWidth, lblTitle.Height);
                }
            }
            catch { }
        }

        private void PositionActionButtons()
        {
            try
            {
                // Keypad rechts neben Betrag
                if (numBetrag != null && btnKeypad != null)
                {
                    btnKeypad.Location = new Point(numBetrag.Right + 8, numBetrag.Top - 2);
                }
                // Align picked labels next to buttons if visible
                if (btnPickFeld != null && lblPickedFeld != null)
                {
                    lblPickedFeld.Location = new Point(btnPickFeld.Right + 12, btnPickFeld.Top + (btnPickFeld.Height - lblPickedFeld.Height) / 2);
                }
                if (btnPickFirma != null && lblPickedFirma != null)
                {
                    lblPickedFirma.Location = new Point(btnPickFirma.Right + 12, btnPickFirma.Top + (btnPickFirma.Height - lblPickedFirma.Height) / 2);
                }
                // Save unten rechts
                if (btnSave != null)
                {
                    int margin = 20;
                    btnSave.Location = new Point(Math.Max(margin, ClientSize.Width - btnSave.Width - margin), Math.Max(_layoutTop + 220, ClientSize.Height - btnSave.Height - margin));
                }
            }
            catch { }
        }

        private void EnsureOverlay()
        {
            if (_overlayPanel != null) return;
            _overlayPanel = new Panel
            {
                Visible = false,
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(245, 245, 245)
            };
            // Header
            var header = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Color.FromArgb(33, 150, 243) };
            _overlayTitle = new Label { Text = "Auswahl", Dock = DockStyle.Left, Width = 480, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.White, Font = new Font("Segoe UI Variable", 18F, FontStyle.Bold), Padding = new Padding(16, 0, 0, 0) };
            _overlayClose = new Button { Text = "\u2715", Dock = DockStyle.Right, Width = 56, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.Transparent };
            _overlayClose.FlatAppearance.BorderSize = 0;
            _overlayClose.Click += (s, e) => HideOverlay();
            header.Controls.Add(_overlayClose);
            header.Controls.Add(_overlayTitle);

            // Search removed to fit more buttons
            _overlaySearch = null;

            // Flow container
            _overlayFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12), WrapContents = true, BackColor = Color.White };

            _overlayPanel.Controls.Add(_overlayFlow);
            _overlayPanel.Controls.Add(header);
            Controls.Add(_overlayPanel);
            _overlayPanel.BringToFront();
        }

        private System.Collections.Generic.List<BigItem> _overlayItems = new System.Collections.Generic.List<BigItem>();
        private void ShowOverlay(string title, System.Collections.Generic.List<BigItem> items, Action<BigItem> onSelect)
        {
            EnsureOverlay();
            _overlayTitle.Text = string.IsNullOrWhiteSpace(title) ? "Auswahl" : title.Trim();
            _overlayItems = items ?? new System.Collections.Generic.List<BigItem>();
            _onOverlaySelect = onSelect;
            if (_overlaySearch != null) _overlaySearch.Text = string.Empty;
            RefreshOverlayButtons();
            _overlayPanel.Visible = true;
            _overlayPanel.BringToFront();
        }

        private void HideOverlay()
        {
            if (_overlayPanel != null) _overlayPanel.Visible = false;
        }

        private void RefreshOverlayButtons()
        {
            if (_overlayFlow == null) return;
            try
            {
                _overlayFlow.SuspendLayout();
                _overlayFlow.Controls.Clear();
                var q = (_overlaySearch?.Text ?? string.Empty).Trim().ToLowerInvariant();
                System.Collections.Generic.IEnumerable<BigItem> list = _overlayItems;
                if (!string.IsNullOrEmpty(q))
                {
                    list = list.Where(i => (i.Title ?? string.Empty).ToLowerInvariant().Contains(q) || (i.Subtitle ?? string.Empty).ToLowerInvariant().Contains(q));
                }
                foreach (var it in list)
                {
                    var btn = new Button
                    {
                        Text = string.IsNullOrEmpty(it.Subtitle) ? it.Title : it.Title + "\n" + it.Subtitle,
                        Width = 320,
                        Height = 100,
                        Margin = new Padding(8),
                        TextAlign = ContentAlignment.MiddleCenter,
                        FlatStyle = FlatStyle.Flat,
                        BackColor = Color.FromArgb(33, 150, 243),
                        ForeColor = Color.White,
                        Font = new Font("Segoe UI Variable", 16F, FontStyle.Bold),
                        Tag = it
                    };
                    btn.FlatAppearance.BorderSize = 0;
                    btn.Click += (s, e) => { try { _onOverlaySelect?.Invoke((BigItem)((Button)s).Tag); } catch { } HideOverlay(); };
                    _overlayFlow.Controls.Add(btn);
                }
                _overlayFlow.ResumeLayout();
            }
            catch { }
        }

        private void ShowFieldPicker()
        {
            try
            {
                var data = PaymentSettingsStore.Load();
                var felder = data.Felder.ToList();
                // Build items for picker
                var items = felder.Select(f => new BigItem { Id = f.Bezeichnung ?? string.Empty, Title = f.Bezeichnung ?? string.Empty, Subtitle = $"Max {f.MaxBetrag:N2} €", Tag = f }).ToList();
                ShowOverlay("Zahlungsfeld wählen", items, (pickedItem) =>
                {
                    var picked = pickedItem?.Tag as PaymentFieldSetting;
                    if (picked != null)
                    {
                        for (int i = 0; i < cboFelder.Items.Count; i++)
                        {
                            var it = cboFelder.Items[i] as PaymentFieldSetting;
                            if (it != null && string.Equals(it.Bezeichnung, picked.Bezeichnung)) { cboFelder.SelectedIndex = i; break; }
                        }
                    }
                    ValidateMaxAmount();
                });
            }
            catch { }
        }

        private void ShowFirmaPicker()
        {
            try
            {
                // Extract current DataTable from combo
                DataTable table = null;
                try
                {
                    if (cboFirma.DataSource is DataTable t) table = t;
                    else if (cboFirma.DataSource is BindingSource bs && bs.DataSource is DataTable bt) table = bt;
                }
                catch { }
                if (table == null) return;
                // Nur echte Firmen (ohne Platzhalter) anzeigen
                var items = table.Rows.Cast<DataRow>()
                    .Where(r => r["ManID"] != DBNull.Value)
                    .Select(r => new BigItem
                    {
                        Id = Convert.ToString(r["ManID"] ?? ""),
                        Title = Convert.ToString(r["ManName"] ?? ""),
                        Subtitle = string.Empty,
                        Tag = r
                    })
                    .ToList();
                ShowOverlay("Firma wählen", items, (pickedItem) =>
                {
                    var idStr = pickedItem?.Id;
                    int id;
                    if (!int.TryParse(idStr, out id))
                    {
                        cboFirma.SelectedIndex = 0;
                        _selectedFirmaId = null;
                    }
                    else
                    {
                        // Wähle explizit den Index der Firma im Combo (nicht nur SelectedValue)
                        try
                        {
                            int targetIndex = -1;
                            for (int i = 0; i < cboFirma.Items.Count; i++)
                            {
                                var drv = cboFirma.Items[i] as DataRowView;
                                if (drv != null && drv.Row != null && drv.Row.Table.Columns.Contains("ManID"))
                                {
                                    var val = drv.Row["ManID"];
                                    // Vergleiche robust (string/int)
                                    if (val != DBNull.Value)
                                    {
                                        int vid;
                                        var s = Convert.ToString(val);
                                        if (int.TryParse(s, out vid) && vid == id)
                                        {
                                            targetIndex = i;
                                            break;
                                        }
                                    }
                                }
                            }
                            // Fallback: über Namen suchen
                            if (targetIndex < 0)
                            {
                                for (int i = 0; i < cboFirma.Items.Count; i++)
                                {
                                    var drv = cboFirma.Items[i] as DataRowView;
                                    if (drv != null && drv.Row != null && drv.Row.Table.Columns.Contains("ManName"))
                                    {
                                        var nm = Convert.ToString(drv.Row["ManName"]) ?? string.Empty;
                                        if (!string.IsNullOrWhiteSpace(nm) && string.Equals(nm.Trim(), (pickedItem?.Title ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
                                        {
                                            targetIndex = i;
                                            break;
                                        }
                                    }
                                }
                            }
                            // Setze nur wenn gültig (>0, nicht Platzhalter)
                            cboFirma.SelectedIndex = (targetIndex <= 0) ? Math.Max(1, targetIndex) : targetIndex;
                            _selectedFirmaId = id;
                        }
                        catch { cboFirma.SelectedValue = id; _selectedFirmaId = id; }
                        // Button-Text sofort aktualisieren
                        try { if (btnPickFirma != null) btnPickFirma.Text = string.IsNullOrWhiteSpace(pickedItem?.Title) ? "Firma wählen" : pickedItem.Title; } catch { }
                    }
                    ValidateMaxAmount();
                });
            }
            catch { }
        }

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);
    }
}
