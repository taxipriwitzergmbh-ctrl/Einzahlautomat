using System;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Geldautomat
{
    // Anzeige der Stunden/Zeiterfassung für einen Mitarbeiter
    public class HoursOverviewForm : Form
    {
        private readonly PersonalInfo _personal;
        private Panel _header;
        private Label _title;
        private Button _btnClose;
        private ComboBox _cbMonth;
        private ComboBox _cbYear;
        private Button _btnPrev;
        private Button _btnNext;
        private DataGridView _grid;
        private Label _lblInfo;

        public HoursOverviewForm(PersonalInfo personal)
        {
            _personal = personal ?? throw new ArgumentNullException(nameof(personal));
            BuildUi();
            LoadCurrentMonth();
        }

        private void BuildUi()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;
            DoubleBuffered = true;
            ClientSize = new Size(900, 700);

            _header = new Panel { Dock = DockStyle.Top, Height = 72 };
            _header.Paint += (s, e) =>
            {
                using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(_header.ClientRectangle, Color.FromArgb(33, 150, 243), Color.FromArgb(33, 203, 243), 0f))
                {
                    e.Graphics.FillRectangle(br, _header.ClientRectangle);
                }
            };
            Controls.Add(_header);

            _title = new Label { Text = "Zeiterfassung", AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Variable", 22F, FontStyle.Bold), ForeColor = Color.White, Location = new Point(16, 0), Size = new Size(350, 72) };
            _header.Controls.Add(_title);

            _btnClose = new Button { Text = "Schließen", AutoSize = false, Size = new Size(140, 48), BackColor = Color.FromArgb(229, 57, 53), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Location = new Point(ClientSize.Width - 156, 12), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            _btnClose.FlatAppearance.BorderSize = 0; _btnClose.Click += (s, e) => Close(); _header.Controls.Add(_btnClose);

            // Monat/Jahr Auswahl (touch-freundlich)
            _cbMonth = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 14F), Location = new Point(380, 18), Size = new Size(160, 36) };
            _cbMonth.Items.AddRange(new object[] { "Januar","Februar","März","April","Mai","Juni","Juli","August","September","Oktober","November","Dezember" });
            _cbMonth.SelectedIndexChanged += (s, e) => Reload();
            _header.Controls.Add(_cbMonth);
            _cbYear = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 14F), Location = new Point(550, 18), Size = new Size(110, 36) };
            int yNow = DateTime.Now.Year; for (int y = yNow - 5; y <= yNow + 1; y++) _cbYear.Items.Add(y);
            _cbYear.SelectedIndexChanged += (s, e) => Reload();
            _header.Controls.Add(_cbYear);

            _btnPrev = new Button { Text = "?", Font = new Font("Segoe UI", 16F, FontStyle.Bold), Size = new Size(48, 36), Location = new Point(320, 18), BackColor = Color.FromArgb(33, 150, 243), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            _btnPrev.FlatAppearance.BorderSize = 0; _btnPrev.Click += (s, e) => ShiftMonth(-1);
            _header.Controls.Add(_btnPrev);
            _btnNext = new Button { Text = "?", Font = new Font("Segoe UI", 16F, FontStyle.Bold), Size = new Size(48, 36), Location = new Point(670, 18), BackColor = Color.FromArgb(33, 150, 243), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            _btnNext.FlatAppearance.BorderSize = 0; _btnNext.Click += (s, e) => ShiftMonth(+1);
            _header.Controls.Add(_btnNext);

            _lblInfo = new Label { Text = $"Mitarbeiter: {_personal?.Vorname} {_personal?.Name}", AutoSize = false, Location = new Point(16, _header.Bottom + 10), Size = new Size(ClientSize.Width - 32, 32), Font = new Font("Segoe UI", 12F, FontStyle.Bold) };
            Controls.Add(_lblInfo);

            _grid = new DataGridView
            {
                Location = new Point(16, _lblInfo.Bottom + 8),
                Size = new Size(ClientSize.Width - 32, ClientSize.Height - (_lblInfo.Bottom + 24)),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 36,
                Font = new Font("Segoe UI", 12F)
            };
            _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(220, 240, 255);
            _grid.DefaultCellStyle.SelectionForeColor = Color.Black;
            Controls.Add(_grid);
        }

        private void LoadCurrentMonth()
        {
            var now = DateTime.Now;
            _cbMonth.SelectedIndex = now.Month - 1;
            int idxY = -1; for (int i = 0; i < _cbYear.Items.Count; i++) { if ((int)_cbYear.Items[i] == now.Year) { idxY = i; break; } }
            if (idxY >= 0) _cbYear.SelectedIndex = idxY; else { _cbYear.Items.Add(now.Year); _cbYear.SelectedIndex = _cbYear.Items.Count - 1; }
            Reload();
        }

        private async void Reload()
        {
            try
            {
                if (_cbMonth.SelectedIndex < 0 || _cbYear.SelectedIndex < 0) return;
                int month = _cbMonth.SelectedIndex + 1;
                int year = (int)_cbYear.SelectedItem;
                DateTime von = new DateTime(year, month, 1, 0, 0, 0);
                DateTime bis = von.AddMonths(1);
                using (var db = new DatabaseHelper())
                {
                    var dt = await db.GetZeiterfassungAsync(_personal.PID, von, bis);
                    BindGrid(dt);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Fehler beim Laden der Zeiterfassung:\r\n" + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BindGrid(DataTable dt)
        {
            _grid.Columns.Clear();
            _grid.DataSource = dt;
            // Column setup similar to screenshot
            SetCol("TypText", "Typ", 110, DataGridViewContentAlignment.MiddleLeft);
            SetCol("Wt", "Wt", 60, DataGridViewContentAlignment.MiddleCenter);
            SetDateCol("ZeitVon", "Von", 180);
            SetDateCol("ZeitBis", "Bis", 140, showTimeOnly: true);
            SetCol("Dauer", "Dauer", 100, DataGridViewContentAlignment.MiddleCenter);
            // Zusatz: Kennzeichen oder Fhz
            if (dt.Columns.Contains("Zusatz")) SetCol("Zusatz", "Zusatz", 160, DataGridViewContentAlignment.MiddleLeft);

            // Hide technical columns
            HideCol("AutoID"); HideCol("Typ"); HideCol("PID"); HideCol("FID"); HideCol("Bemerkung"); HideCol("ZeitAnlage"); HideCol("UserAnlage"); HideCol("ZeitBearbeitet"); HideCol("UserBearbeitet"); HideCol("Kennzeichen");

            // Alternating row style
            _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 251, 255);

            // Highlight short pause rows (example: Typ == 2 and duration < 00:20)
            foreach (DataGridViewRow r in _grid.Rows)
            {
                try
                {
                    int typ = r.Cells["Typ"].Value == null ? 0 : Convert.ToInt32(r.Cells["Typ"].Value);
                    var vonObj = r.Cells["ZeitVon"].Value as DateTime?;
                    var bisObj = r.Cells["ZeitBis"].Value as DateTime?;
                    if (vonObj.HasValue && bisObj.HasValue && bisObj.Value > vonObj.Value)
                    {
                        var diff = bisObj.Value - vonObj.Value;
                        if (typ == 2 && diff.TotalMinutes < 20)
                        {
                            r.DefaultCellStyle.BackColor = Color.FromArgb(235, 242, 255);
                        }
                    }
                }
                catch { }
            }
        }

        private void SetCol(string name, string header, int width, DataGridViewContentAlignment align)
        {
            if (!_grid.Columns.Contains(name)) return;
            var c = _grid.Columns[name];
            c.HeaderText = header; c.Width = width; c.FillWeight = width; c.DefaultCellStyle.Alignment = align; c.ReadOnly = true;
        }

        private void SetDateCol(string name, string header, int width, bool showTimeOnly = false)
        {
            if (!_grid.Columns.Contains(name)) return;
            var c = _grid.Columns[name];
            c.HeaderText = header; c.Width = width; c.FillWeight = width; c.ReadOnly = true;
            c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            c.DefaultCellStyle.Format = showTimeOnly ? "HH:mm" : "dd.MM.yy HH:mm";
        }

        private void HideCol(string name)
        {
            if (_grid.Columns.Contains(name)) _grid.Columns[name].Visible = false;
        }

        private void ShiftMonth(int delta)
        {
            if (_cbMonth.SelectedIndex < 0 || _cbYear.SelectedIndex < 0) return;
            int m = _cbMonth.SelectedIndex + 1; int y = (int)_cbYear.SelectedItem;
            var cur = new DateTime(y, m, 1);
            var next = cur.AddMonths(delta);
            _cbMonth.SelectedIndex = next.Month - 1;
            int idxY = -1; for (int i = 0; i < _cbYear.Items.Count; i++) { if ((int)_cbYear.Items[i] == next.Year) { idxY = i; break; } }
            if (idxY >= 0) _cbYear.SelectedIndex = idxY; else { _cbYear.Items.Add(next.Year); _cbYear.SelectedIndex = _cbYear.Items.Count - 1; }
        }
    }
}
