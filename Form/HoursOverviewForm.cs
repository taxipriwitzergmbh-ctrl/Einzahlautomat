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
        private Button _btnHelp;
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
            ClientSize = new Size(1280, 1024);

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

            _btnClose = new Button { Text = "Schließen", AutoSize = false, Size = new Size(160, 48), BackColor = Color.FromArgb(229, 57, 53), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Location = new Point(ClientSize.Width - 176, 12), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            _btnClose.FlatAppearance.BorderSize = 0; _btnClose.Click += (s, e) => Close(); _header.Controls.Add(_btnClose);

            // Monat/Jahr Auswahl (touch-freundlich)
            _cbMonth = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 16F), Location = new Point(420, 14), Size = new Size(230, 40) };
            _cbMonth.Items.AddRange(new object[] { "Januar","Februar","März","April","Mai","Juni","Juli","August","September","Oktober","November","Dezember" });
            _cbMonth.SelectedIndexChanged += (s, e) => Reload();
            _header.Controls.Add(_cbMonth);
            _cbYear = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 16F), Location = new Point(660, 14), Size = new Size(120, 40) };
            int yNow = DateTime.Now.Year; for (int y = yNow - 5; y <= yNow + 1; y++) _cbYear.Items.Add(y);
            _cbYear.SelectedIndexChanged += (s, e) => Reload();
            _header.Controls.Add(_cbYear);

            _btnPrev = new Button { Text = "◀", Font = new Font("Segoe UI", 18F, FontStyle.Bold), Size = new Size(48, 40), Location = new Point(368, 14), BackColor = Color.FromArgb(33, 150, 243), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            _btnPrev.FlatAppearance.BorderSize = 0; _btnPrev.Click += (s, e) => ShiftMonth(-1);
            _header.Controls.Add(_btnPrev);

            // fixed _btnNext initialization (removed malformed duplicate/new.Size)
            _btnNext = new Button { Text = "▶", Font = new Font("Segoe UI", 18F, FontStyle.Bold), Size = new Size(48, 40), Location = new Point(788, 14), BackColor = Color.FromArgb(33, 150, 243), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            _btnNext.FlatAppearance.BorderSize = 0; _btnNext.Click += (s, e) => ShiftMonth(+1);
            _header.Controls.Add(_btnNext);

            _btnHelp = new Button { Text = "?", Font = new Font("Segoe UI", 18F, FontStyle.Bold), Size = new Size(48, 40), Location = new Point(844, 14), BackColor = Color.FromArgb(0, 133, 188), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            _btnHelp.FlatAppearance.BorderSize = 0; _btnHelp.Click += (s, e) => ShowHelp();
            _header.Controls.Add(_btnHelp);

            _lblInfo = new Label { Text = $"Mitarbeiter: {_personal?.Vorname} {_personal?.Name}", AutoSize = false, Location = new Point(16, _header.Bottom + 10), Size = new Size(ClientSize.Width - 32, 32), Font = new Font("Segoe UI", 14F, FontStyle.Bold) };
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
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None, // disable fill to honor explicit widths
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 40,
                Font = new Font("Segoe UI", 14F)
            };
            _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(220, 240, 255);
            _grid.DefaultCellStyle.SelectionForeColor = Color.Black;
            Controls.Add(_grid);
        }

        private void ShowHelp()
        {
            try
            {
                MessageBox.Show(this, "Wählen Sie oben Monat und Jahr. Mit ◀/▶ wechseln Sie die Monate. Die Tabelle zeigt Wochentag, Datum, Anfang/Ende, Dauer, Typ und Fahrzeug.", "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch { }
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

                    // Create a UI copy of the table so we don't modify the original DB result
                    var uiDt = dt.Clone();
                    // Relax nullability and set sane defaults for UI table
                    foreach (DataColumn col in uiDt.Columns)
                    {
                        try
                        {
                            col.AllowDBNull = true;
                            if (col.DataType == typeof(string)) col.DefaultValue = string.Empty;
                            else if (col.DataType == typeof(DateTime)) col.DefaultValue = DBNull.Value;
                            else col.DefaultValue = Activator.CreateInstance(col.DataType);
                        }
                        catch { }
                    }

                    // Import rows from DB result
                    foreach (DataRow r in dt.Rows) uiDt.ImportRow(r);

                    // Ensure Datum column exists in UI table
                    if (!uiDt.Columns.Contains("Datum")) uiDt.Columns.Add(new DataColumn("Datum", typeof(DateTime)) { AllowDBNull = true, DefaultValue = DBNull.Value });

                    // Fill Datum from ZeitVon/ZeitBis where available
                    foreach (DataRow r in uiDt.Rows)
                    {
                        try
                        {
                            var v = uiDt.Columns.Contains("ZeitVon") && r["ZeitVon"] != DBNull.Value ? (DateTime)r["ZeitVon"] : DateTime.MinValue;
                            if (v != DateTime.MinValue) r["Datum"] = v.Date;
                            else if (uiDt.Columns.Contains("ZeitBis") && r["ZeitBis"] != DBNull.Value) r["Datum"] = ((DateTime)r["ZeitBis"]).Date;
                        }
                        catch { }
                    }

                    // Insert missing days (TypText="Frei") before binding — only in UI table
                    EnsureAllDaysRows(uiDt, von, bis);

                    // sort on view and bind
                    uiDt.DefaultView.Sort = "Datum ASC, ZeitVon ASC";
                    BindGrid(uiDt.DefaultView);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Fehler beim Laden der Zeiterfassung:\r\n" + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void EnsureAllDaysRows(DataTable dt, DateTime start, DateTime end)
        {
            // Ensure needed columns with permissive null rules for UI table
            if (!dt.Columns.Contains("Wt")) dt.Columns.Add(new DataColumn("Wt", typeof(string)) { AllowDBNull = true, DefaultValue = string.Empty });
            if (!dt.Columns.Contains("TypText")) dt.Columns.Add(new DataColumn("TypText", typeof(string)) { AllowDBNull = true, DefaultValue = string.Empty });
            if (!dt.Columns.Contains("Zusatz")) dt.Columns.Add(new DataColumn("Zusatz", typeof(string)) { AllowDBNull = true, DefaultValue = string.Empty });
            if (!dt.Columns.Contains("ZeitVon")) dt.Columns.Add(new DataColumn("ZeitVon", typeof(DateTime)) { AllowDBNull = true, DefaultValue = DBNull.Value });
            if (!dt.Columns.Contains("ZeitBis")) dt.Columns.Add(new DataColumn("ZeitBis", typeof(DateTime)) { AllowDBNull = true, DefaultValue = DBNull.Value });
            if (!dt.Columns.Contains("Dauer")) dt.Columns.Add(new DataColumn("Dauer", typeof(string)) { AllowDBNull = true, DefaultValue = string.Empty });
            if (!dt.Columns.Contains("Datum")) dt.Columns.Add(new DataColumn("Datum", typeof(DateTime)) { AllowDBNull = true, DefaultValue = DBNull.Value });

            var existingDates = dt.AsEnumerable()
                                  .Where(r => r.Table.Columns.Contains("Datum") && r["Datum"] != DBNull.Value)
                                  .Select(r => ((DateTime)r["Datum"]).Date)
                                  .Distinct()
                                  .ToHashSet();

            for (var day = start.Date; day < end.Date; day = day.AddDays(1))
            {
                if (!existingDates.Contains(day))
                {
                    var row = dt.NewRow();

                    // set obvious values
                    if (dt.Columns.Contains("Wt")) row["Wt"] = day.ToString("ddd");
                    if (dt.Columns.Contains("Datum")) row["Datum"] = day;
                    if (dt.Columns.Contains("ZeitVon")) row["ZeitVon"] = DBNull.Value;
                    if (dt.Columns.Contains("ZeitBis")) row["ZeitBis"] = DBNull.Value;
                    if (dt.Columns.Contains("Dauer")) row["Dauer"] = string.Empty;
                    if (dt.Columns.Contains("TypText")) row["TypText"] = "Frei";
                    if (dt.Columns.Contains("Zusatz")) row["Zusatz"] = string.Empty;

                    // ensure all non-set columns have a valid default to avoid non-nullable errors
                    foreach (DataColumn col in dt.Columns)
                    {
                        if (row.IsNull(col))
                        {
                            try
                            {
                                if (col.AllowDBNull) { row[col] = DBNull.Value; continue; }
                                if (col.DataType == typeof(string)) row[col] = string.Empty;
                                else if (col.DataType == typeof(DateTime)) row[col] = DateTime.MinValue;
                                else row[col] = Activator.CreateInstance(col.DataType);
                            }
                            catch
                            {
                                try { if (col.AllowDBNull) row[col] = DBNull.Value; else if (col.DataType == typeof(string)) row[col] = string.Empty; } catch { }
                            }
                        }
                    }

                    dt.Rows.Add(row);
                }
            }
        }

        private void BindGrid(DataView view)
        {
            _grid.Columns.Clear();
            _grid.DataSource = view;

            // headers and explicit widths
            SetCol("Wt", "Tag", 70, DataGridViewContentAlignment.MiddleCenter);
            SetDateCol("Datum", "Datum", 180, showTimeOnly: false, dateOnly: true);
            SetDateCol("ZeitVon", "Von", 110, showTimeOnly: true);
            SetDateCol("ZeitBis", "Bis", 110, showTimeOnly: true);
            SetCol("Dauer", "Dauer", 120, DataGridViewContentAlignment.MiddleCenter);
            SetCol("TypText", "Typ", 160, DataGridViewContentAlignment.MiddleLeft);
            if (view.Table.Columns.Contains("Zusatz")) SetCol("Zusatz", "Fahrzeug", 240, DataGridViewContentAlignment.MiddleLeft);

            // order
            int order = 0;
            SetDisplayIndex("Wt", order++);
            SetDisplayIndex("Datum", order++);
            SetDisplayIndex("ZeitVon", order++);
            SetDisplayIndex("ZeitBis", order++);
            SetDisplayIndex("Dauer", order++);
            SetDisplayIndex("TypText", order++);
            SetDisplayIndex("Zusatz", order++);

            _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 251, 255);

            // Sundays highlight
            foreach (DataGridViewRow r in _grid.Rows)
            {
                try
                {
                    var datumObj = r.Cells["Datum"].Value;
                    DateTime d;
                    if (datumObj != null && datumObj != DBNull.Value && DateTime.TryParse(datumObj.ToString(), out d))
                    {
                        if (d.DayOfWeek == DayOfWeek.Sunday)
                        {
                            r.DefaultCellStyle.BackColor = Color.FromArgb(40, 255, 128, 128);
                        }
                    }
                }
                catch { }
            }

            HideCol("AutoID"); // remove AutoID as requested
            HideCol("PID");    // remove PID as requested
            HideCol("Typ"); HideCol("FID"); HideCol("Bemerkung"); HideCol("ZeitAnlage"); HideCol("UserAnlage"); HideCol("ZeitBearbeitet"); HideCol("UserBearbeitet"); HideCol("Kennzeichen");

            // Short pause highlight (Typ==106 and <20 min)
            bool hasTypCol = _grid.Columns.Contains("Typ");
            foreach (DataGridViewRow r in _grid.Rows)
            {
                try
                {
                    int typ = 0;
                    if (hasTypCol && r.Cells["Typ"].Value != null && r.Cells["Typ"].Value != DBNull.Value)
                        try { typ = Convert.ToInt32(r.Cells["Typ"].Value); } catch { typ = 0; }
                    DateTime vonVal = DateTime.MinValue, bisVal = DateTime.MinValue;
                    if (r.Cells["ZeitVon"].Value != null && r.Cells["ZeitVon"].Value != DBNull.Value) try { vonVal = Convert.ToDateTime(r.Cells["ZeitVon"].Value); } catch { }
                    if (r.Cells["ZeitBis"].Value != null && r.Cells["ZeitBis"].Value != DBNull.Value) try { bisVal = Convert.ToDateTime(r.Cells["ZeitBis"].Value); } catch { }
                    if (bisVal > vonVal)
                    {
                        var diff = bisVal - vonVal;
                        if (typ == 106 && diff.TotalMinutes < 20)
                            r.DefaultCellStyle.BackColor = Color.FromArgb(235, 242, 255);
                    }
                }
                catch { }
            }
        }

        private void SetDisplayIndex(string name, int index)
        {
            try
            {
                if (_grid.Columns.Contains(name))
                {
                    var c = _grid.Columns[name];
                    c.Visible = true;
                    c.DisplayIndex = index;
                    c.AutoSizeMode = DataGridViewAutoSizeColumnMode.None; // ensure fixed width
                }
            }
            catch { }
        }

        private void SetCol(string name, string header, int width, DataGridViewContentAlignment align)
        {
            if (!_grid.Columns.Contains(name)) return;
            var c = _grid.Columns[name];
            c.HeaderText = header; c.Width = width; c.FillWeight = width; c.DefaultCellStyle.Alignment = align; c.ReadOnly = true; c.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        }

        private void SetDateCol(string name, string header, int width, bool showTimeOnly = false, bool dateOnly = false)
        {
            if (!_grid.Columns.Contains(name)) return;
            var c = _grid.Columns[name];
            c.HeaderText = header; c.Width = width; c.FillWeight = width; c.ReadOnly = true; c.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            if (dateOnly) c.DefaultCellStyle.Format = "dd.MM.yy";
            else c.DefaultCellStyle.Format = showTimeOnly ? "HH:mm" : "dd.MM.yy HH:mm";
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
