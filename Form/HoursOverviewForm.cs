using System;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Reflection;
using System.IO;
using System.Runtime.InteropServices;
using System.Drawing.Printing;
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using Microsoft.Win32;
using System.Collections.Generic;

namespace Geldautomat
{
    // PdfSharp font resolver: resolves Windows font registry entries to font files
    internal class PdfFontResolver : IFontResolver
    {
        private readonly Dictionary<string, string> _cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            try
            {
                // prefer exact family, then common fallbacks
                var candidates = new List<string> { familyName };
                if (!string.Equals(familyName, "Segoe UI", StringComparison.OrdinalIgnoreCase)) candidates.Add("Segoe UI");
                candidates.Add("Arial"); candidates.Add("Tahoma"); candidates.Add("Verdana"); candidates.Add("Times New Roman");

                foreach (var fam in candidates)
                {
                    var file = ResolveFontFileFromRegistry(fam);
                    if (!string.IsNullOrEmpty(file))
                    {
                        // return FontResolverInfo with the filename as the face name
                        return new FontResolverInfo(file);
                    }
                }
            }
            catch { }
            return null;
        }

        public byte[] GetFont(string faceName)
        {
            try
            {
                if (string.IsNullOrEmpty(faceName)) return null;
                string filename = faceName;
                string path = filename;
                if (!Path.IsPathRooted(path))
                {
                    var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                    path = Path.Combine(win, "Fonts", filename);
                }
                if (!File.Exists(path))
                {
                    // try to resolve via registry
                    var resolved = ResolveFontFileFromRegistry(Path.GetFileNameWithoutExtension(filename));
                    if (!string.IsNullOrEmpty(resolved))
                    {
                        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                        var full = Path.Combine(win, "Fonts", resolved);
                        if (File.Exists(full)) path = full;
                    }
                }
                if (!File.Exists(path)) return null;
                // cache
                _cache[faceName] = path;
                return File.ReadAllBytes(path);
            }
            catch { return null; }
        }

        private string ResolveFontFileFromRegistry(string familyKey)
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Fonts"))
                {
                    if (key == null) return null;
                    foreach (var name in key.GetValueNames())
                    {
                        if (name.IndexOf(familyKey, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            var val = key.GetValue(name) as string;
                            if (string.IsNullOrWhiteSpace(val)) continue;
                            var file = val.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                            return file;
                        }
                    }
                }
            }
            catch { }
            return null;
        }
    }

    // Anzeige der Stunden/Zeiterfassung für einen Mitarbeiter
    public class HoursOverviewForm : Form
    {
        private readonly PersonalInfo _personal;
        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

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
        private Label lblPeriod; // shows selected month/year

        // summary
        private Panel _summaryPanel;
        private Label lblSumArbeit, lblSumShortPause, lblSumPause, lblNetto, lblSumUrlaub, lblSumKrank;
        private Label lblIstStunden;

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
            // enable double buffering on the panel to avoid rendering artifacts
            try { typeof(Panel).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_header, true, null); } catch { }
            // provide a base BackColor so the gradient has a fallback (prevents small white artifacts)
            _header.BackColor = Color.FromArgb(33, 150, 243);
            _header.Paint += (s, e) =>
            {
                using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(_header.ClientRectangle, Color.FromArgb(33, 150, 243), Color.FromArgb(33, 203, 243), 0f))
                {
                    e.Graphics.FillRectangle(br, _header.ClientRectangle);
                }
            };
            Controls.Add(_header);

            _title = new Label { Text = "Zeiterfassung", AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Variable", 22F, FontStyle.Bold), ForeColor = Color.White, Location = new Point(16, 0), Size = new Size(350, 72) };
            _title.BackColor = Color.Transparent;
            _header.Controls.Add(_title);

            _btnClose = new Button { Text = "Schließen", AutoSize = false, Size = new Size(160, 48), BackColor = Color.FromArgb(229, 57, 53), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Location = new Point(ClientSize.Width - 176, 12), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            _btnClose.FlatAppearance.BorderSize = 0; _btnClose.Click += (s, e) => Close(); _header.Controls.Add(_btnClose);

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

            _btnNext = new Button { Text = "▶", Font = new Font("Segoe UI", 18F, FontStyle.Bold), Size = new Size(48, 40), Location = new Point(788, 14), BackColor = Color.FromArgb(33, 150, 243), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            _btnNext.FlatAppearance.BorderSize = 0; _btnNext.Click += (s, e) => ShiftMonth(+1);
            _header.Controls.Add(_btnNext);

            _btnHelp = new Button { Text = "?", Font = new Font("Segoe UI", 18F, FontStyle.Bold), Size = new Size(48, 40), Location = new Point(844, 14), BackColor = Color.FromArgb(0, 133, 188), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            _btnHelp.FlatAppearance.BorderSize = 0; _btnHelp.Click += (s, e) => ShowHelp();
            _header.Controls.Add(_btnHelp);

            // Print and Email buttons for exporting the current view (use document printer and mail settings)
            var btnPrintMonth = new Button { Text = "Drucken", Font = new Font("Segoe UI", 12F, FontStyle.Bold), Size = new Size(96, 40), Location = new Point(900, 14), BackColor = Color.FromArgb(76, 175, 80), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnPrintMonth.FlatAppearance.BorderSize = 0; btnPrintMonth.Click += (s, e) => PrintMonthReport(); _header.Controls.Add(btnPrintMonth);
            var btnEmailMonth = new Button { Text = "per Mail", Font = new Font("Segoe UI", 12F, FontStyle.Bold), Size = new Size(96, 40), Location = new Point(1004, 14), BackColor = Color.FromArgb(255, 167, 38), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnEmailMonth.FlatAppearance.BorderSize = 0; btnEmailMonth.Click += (s, e) => EmailMonthReport(); _header.Controls.Add(btnEmailMonth);

            _lblInfo = new Label { Text = $"Mitarbeiter: {_personal?.Vorname} {_personal?.Name}", AutoSize = false, Location = new Point(16, _header.Bottom + 10), Size = new Size(520, 32), Font = new Font("Segoe UI", 14F, FontStyle.Bold) };
            _lblInfo.BackColor = Color.Transparent;
            Controls.Add(_lblInfo);

            // period label (month/year) to the right of employee label
            lblPeriod = new Label { Text = "", AutoSize = true, Location = new Point(16 + 540, _header.Bottom + 10), Font = new Font("Segoe UI", 14F, FontStyle.Regular) };
            lblPeriod.BackColor = Color.Transparent;
            Controls.Add(lblPeriod);

            // Summary panel directly under period: gray bar with sums listed, then table below
            _summaryPanel = new Panel { Location = new Point(16, _lblInfo.Bottom + 8), Size = new Size(ClientSize.Width - 32, 96), BackColor = Color.FromArgb(240, 240, 240), BorderStyle = BorderStyle.None };
            Controls.Add(_summaryPanel);

            lblSumArbeit = new Label { Text = "Arbeitszeit: 0:00", Font = new Font("Segoe UI", 12F, FontStyle.Bold), Location = new Point(12, 10), AutoSize = true, ForeColor = Color.FromArgb(33,37,41), BackColor = Color.Transparent };
            _summaryPanel.Controls.Add(lblSumArbeit);
            lblSumPause = new Label { Text = "Pause: 0:00", Font = new Font("Segoe UI", 12F, FontStyle.Bold), Location = new Point(240, 10), AutoSize = true, ForeColor = Color.FromArgb(33,37,41), BackColor = Color.Transparent };
            _summaryPanel.Controls.Add(lblSumPause);
            // changed label text to "Pause <15 min" as requested
            lblSumShortPause = new Label { Text = "Pause <15 min: 0:00", Font = new Font("Segoe UI", 12F, FontStyle.Bold), Location = new Point(420, 10), AutoSize = true, ForeColor = Color.FromArgb(33,37,41), BackColor = Color.Transparent };
            _summaryPanel.Controls.Add(lblSumShortPause);
            lblNetto = new Label { Text = "Nettoarbeitszeit: 0:00", Font = new Font("Segoe UI", 12F, FontStyle.Bold), Location = new Point(640, 10), AutoSize = true, ForeColor = Color.FromArgb(33,37,41), BackColor = Color.Transparent };
            _summaryPanel.Controls.Add(lblNetto);
            lblSumUrlaub = new Label { Text = "Urlaub: 0:00", Font = new Font("Segoe UI", 12F, FontStyle.Bold), Location = new Point(12, 44), AutoSize = true, ForeColor = Color.FromArgb(33,37,41), BackColor = Color.Transparent };
            _summaryPanel.Controls.Add(lblSumUrlaub);
            lblSumKrank = new Label { Text = "Krankheit: 0:00", Font = new Font("Segoe UI", 12F, FontStyle.Bold), Location = new Point(240, 44), AutoSize = true, ForeColor = Color.FromArgb(33,37,41), BackColor = Color.Transparent };
            _summaryPanel.Controls.Add(lblSumKrank);
            lblIstStunden = new Label { Text = "Ist-Stunden: 0:00", Font = new Font("Segoe UI", 12F, FontStyle.Bold), Location = new Point(640, 44), AutoSize = true, ForeColor = Color.FromArgb(33,37,41), BackColor = Color.Transparent };
            _summaryPanel.Controls.Add(lblIstStunden);

            _grid = new DataGridView
            {
                Location = new Point(16, _summaryPanel.Bottom + 8),
                Size = new Size(ClientSize.Width - 32, ClientSize.Height - (_summaryPanel.Bottom + 24)),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 40,
                Font = new Font("Segoe UI", 14F)
            };
            _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(220, 240, 255);
            _grid.DefaultCellStyle.SelectionForeColor = Color.Black;
            Controls.Add(_grid);

            try { _header.BringToFront(); _title.BringToFront(); _btnClose.BringToFront(); _cbMonth.BringToFront(); _cbYear.BringToFront(); _btnPrev.BringToFront(); _btnNext.BringToFront(); _btnHelp.BringToFront(); } catch { }
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

                    // work on a UI copy so DB result isn't modified
                    var uiDt = dt.Clone();
                    foreach (DataColumn c in uiDt.Columns)
                    {
                        try { c.AllowDBNull = true; if (c.DataType == typeof(string)) c.DefaultValue = string.Empty; else if (c.DataType == typeof(DateTime)) c.DefaultValue = DBNull.Value; else c.DefaultValue = Activator.CreateInstance(c.DataType); } catch { }
                    }
                    foreach (DataRow r in dt.Rows) uiDt.ImportRow(r);

                    if (!uiDt.Columns.Contains("Datum")) uiDt.Columns.Add(new DataColumn("Datum", typeof(DateTime)) { AllowDBNull = true, DefaultValue = DBNull.Value });

                    // populate Datum if possible
                    foreach (DataRow r in uiDt.Rows)
                    {
                        try
                        {
                            if (uiDt.Columns.Contains("ZeitVon") && r["ZeitVon"] != DBNull.Value) r["Datum"] = ((DateTime)r["ZeitVon"]).Date;
                            else if (uiDt.Columns.Contains("ZeitBis") && r["ZeitBis"] != DBNull.Value) r["Datum"] = ((DateTime)r["ZeitBis"]).Date;
                        }
                        catch { }
                    }

                    // add missing days only to UI table
                    EnsureAllDaysRows(uiDt, von, bis);

                    // add sort column so Arbeit before Pause
                    if (!uiDt.Columns.Contains("SortTyp")) uiDt.Columns.Add(new DataColumn("SortTyp", typeof(int)) { AllowDBNull = true, DefaultValue = 3 });
                    foreach (DataRow r in uiDt.Rows)
                    {
                        try
                        {
                            string t = string.Empty;
                            if (uiDt.Columns.Contains("TypText") && r["TypText"] != DBNull.Value) t = r["TypText"].ToString();
                            else if (uiDt.Columns.Contains("Typ") && r["Typ"] != DBNull.Value) t = r["Typ"].ToString();
                            var tl = t.ToLowerInvariant();
                            int sort = 3;
                            if (tl.Contains("arbeit")) sort = 0; else if (tl.Contains("pause")) sort = 1; else if (tl.Contains("frei")) sort = 2;
                            r["SortTyp"] = sort;
                        }
                        catch { r["SortTyp"] = 3; }
                    }

                    // update displayed period label
                    try { lblPeriod.Text = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(month) + " " + year.ToString(); } catch { }
                    uiDt.DefaultView.Sort = "Datum ASC, SortTyp ASC, ZeitVon ASC";
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
            // ensure columns exist on UI table
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
                    if (dt.Columns.Contains("Wt")) row["Wt"] = day.ToString("ddd");
                    if (dt.Columns.Contains("Datum")) row["Datum"] = day;
                    if (dt.Columns.Contains("ZeitVon")) row["ZeitVon"] = DBNull.Value;
                    if (dt.Columns.Contains("ZeitBis")) row["ZeitBis"] = DBNull.Value;
                    if (dt.Columns.Contains("Dauer")) row["Dauer"] = string.Empty;
                    if (dt.Columns.Contains("TypText")) row["TypText"] = "Frei";
                    if (dt.Columns.Contains("Zusatz")) row["Zusatz"] = string.Empty;

                    // ensure defaults for any non-nullable columns
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
                            catch { try { if (col.AllowDBNull) row[col] = DBNull.Value; else if (col.DataType == typeof(string)) row[col] = string.Empty; } catch { } }
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

            var altColor = Color.FromArgb(248, 251, 255);
            _grid.AlternatingRowsDefaultCellStyle.BackColor = altColor;

            // hide internal columns
            HideCol("AutoID"); HideCol("PID"); HideCol("Typ"); HideCol("FID"); HideCol("Bemerkung"); HideCol("ZeitAnlage"); HideCol("UserAnlage"); HideCol("ZeitBearbeitet"); HideCol("UserBearbeitet"); HideCol("Kennzeichen"); HideCol("SortTyp");

            // per-row visuals with clear priority: Typ -> short pause -> Sunday -> alternating
            for (int i = 0; i < _grid.Rows.Count; i++)
            {
                var r = _grid.Rows[i];
                try
                {
                    Color bgDefault = (i % 2 == 0) ? Color.White : altColor;

                    // parse date
                    DateTime d = DateTime.MinValue;
                    try { var dobj = r.Cells["Datum"].Value; if (dobj != null && dobj != DBNull.Value) DateTime.TryParse(dobj.ToString(), out d); } catch { }

                    // typ text
                    string typText = string.Empty;
                    try { if (r.Cells["TypText"].Value != null && r.Cells["TypText"].Value != DBNull.Value) typText = r.Cells["TypText"].Value.ToString(); } catch { }

                    // determine numeric typ code if present (11=Urlaub, 21=Krank)
                    int typCode = -1;
                    try { if (_grid.Columns.Contains("Typ") && r.Cells["Typ"].Value != null && r.Cells["Typ"].Value != DBNull.Value) int.TryParse(r.Cells["Typ"].Value.ToString(), out typCode); } catch { }

                    // compute duration in minutes: prefer ZeitVon/ZeitBis
                    int rowMinutes = 0;
                    try
                    {
                        if (_grid.Columns.Contains("ZeitVon") && _grid.Columns.Contains("ZeitBis") && r.Cells["ZeitVon"].Value != null && r.Cells["ZeitVon"].Value != DBNull.Value && r.Cells["ZeitBis"].Value != null && r.Cells["ZeitBis"].Value != DBNull.Value)
                        {
                            DateTime zv, zb; DateTime.TryParse(r.Cells["ZeitVon"].Value.ToString(), out zv); DateTime.TryParse(r.Cells["ZeitBis"].Value.ToString(), out zb);
                            var ts = zb - zv; rowMinutes = (int)Math.Round(ts.TotalMinutes);
                        }
                        else if (_grid.Columns.Contains("Dauer") && r.Cells["Dauer"].Value != null && r.Cells["Dauer"].Value != DBNull.Value)
                        {
                            var dstr = r.Cells["Dauer"].Value.ToString();
                            var parts = dstr.Split(':' );
                            if (parts.Length == 2 && int.TryParse(parts[0], out int h) && int.TryParse(parts[1], out int m)) rowMinutes = h * 60 + m;
                            else if (int.TryParse(dstr, out int mm)) rowMinutes = mm;
                        }
                    }
                    catch { }

                    // update displayed Dauer from computed minutes
                    try { if (_grid.Columns.Contains("Dauer") && rowMinutes > 0) r.Cells["Dauer"].Value = $"{rowMinutes/60}:{(rowMinutes%60).ToString("D2")}"; } catch { }

                    // decide background with priority
                    Color finalBg = bgDefault;
                    bool applied = false;

                    // 1) Typ codes (highest priority)
                    try
                    {
                        if (typCode == 11)
                        {
                            finalBg = Color.FromArgb(255, 255, 250, 205); // Urlaub light yellow
                            applied = true;
                            if (_grid.Columns.Contains("TypText")) r.Cells["TypText"].Value = "Urlaub";
                        }
                        else if (typCode == 21)
                        {
                            finalBg = Color.FromArgb(255, 220, 80, 80); // Krank
                            applied = true;
                            if (_grid.Columns.Contains("TypText")) r.Cells["TypText"].Value = "Krankheit";
                        }
                    }
                    catch { }

                    // 2) short pause (only if no typ color applied)
                    if (!applied)
                    {
                        try
                        {
                            if (!string.IsNullOrWhiteSpace(typText) && typText.ToLowerInvariant().Contains("pause") && rowMinutes > 0 && rowMinutes < 15)
                            {
                                finalBg = Color.FromArgb(235, 242, 255);
                                applied = true;
                                if (_grid.Columns.Contains("TypText")) r.Cells["TypText"].Value = "Pause <15 min";
                            }
                            else
                            {
                                // regular pause: hide day label to group with the work row
                                if (!string.IsNullOrWhiteSpace(typText) && typText.ToLowerInvariant().Contains("pause") && _grid.Columns.Contains("Wt")) r.Cells["Wt"].Value = string.Empty;
                            }
                        }
                        catch { }
                    }

                    // 3) Sunday highlight (only if still not applied)
                    if (!applied)
                    {
                        try { if (d != DateTime.MinValue && d.DayOfWeek == DayOfWeek.Sunday) { finalBg = Color.FromArgb(255, 255, 220, 220); applied = true; } } catch { }
                    }

                    // apply final background to row
                    try { r.DefaultCellStyle.BackColor = finalBg; } catch { }
                }
                catch { }
            }

            // update summary from bound table
            UpdateSummary(view.Table);
        }

        private void UpdateSummary(DataTable table)
        {
            try
            {
                int totalArbeitMin = 0, totalPauseMin = 0, totalNichtGewertetMin = 0, totalUrlaubMin = 0, totalKrankMin = 0;

                foreach (DataRow row in table.Rows)
                {
                    if (row.Table.Columns.Contains("Datum") && row["Datum"] == DBNull.Value) continue;

                    int minutes = 0;
                    try
                    {
                        if (row.Table.Columns.Contains("ZeitVon") && row.Table.Columns.Contains("ZeitBis") && row["ZeitVon"] != DBNull.Value && row["ZeitBis"] != DBNull.Value)
                        {
                            var zv = (DateTime)row["ZeitVon"]; var zb = (DateTime)row["ZeitBis"]; var ts = zb - zv; minutes = (int)Math.Round(ts.TotalMinutes);
                        }
                        else if (row.Table.Columns.Contains("Dauer") && row["Dauer"] != DBNull.Value)
                        {
                            var dstr = row["Dauer"].ToString();
                            var parts = dstr.Split(':' );
                            if (parts.Length == 2 && int.TryParse(parts[0], out int h) && int.TryParse(parts[1], out int m)) minutes = h * 60 + m;
                            else if (int.TryParse(dstr, out int mm)) minutes = mm;
                        }
                    }
                    catch { }

                    string typText = string.Empty;
                    if (row.Table.Columns.Contains("TypText") && row["TypText"] != DBNull.Value) typText = row["TypText"].ToString().ToLowerInvariant();

                    // also consider numeric type codes: 11 = Urlaub, 21 = Krank
                    int typCode = -1;
                    try { if (row.Table.Columns.Contains("Typ") && row["Typ"] != DBNull.Value) int.TryParse(row["Typ"].ToString(), out typCode); } catch { }

                    if (typText.Contains("arbeit")) totalArbeitMin += minutes;
                    else if (typText.Contains("pause"))
                    {
                        if (minutes > 0 && minutes < 15) totalNichtGewertetMin += minutes; else totalPauseMin += minutes;
                    }
                    else if (typText.Contains("urlaub") || typCode == 11) totalUrlaubMin += minutes;
                    else if (typText.Contains("krank") || typCode == 21) totalKrankMin += minutes;
                }

                lblSumArbeit.Text = $"Arbeitszeit: {totalArbeitMin/60}:{(totalArbeitMin%60).ToString("D2")}";
                lblSumPause.Text = $"Pause: {totalPauseMin/60}:{(totalPauseMin%60).ToString("D2")}";
                lblSumShortPause.Text = $"Pause <15 min: {totalNichtGewertetMin/60}:{(totalNichtGewertetMin%60).ToString("D2")}";
                int netto = totalArbeitMin - totalPauseMin; if (netto < 0) netto = 0;
                lblNetto.Text = $"Nettoarbeitszeit: {netto/60}:{(netto%60).ToString("D2")}";
                lblSumUrlaub.Text = $"Urlaub: {totalUrlaubMin/60}:{(totalUrlaubMin%60).ToString("D2")}";
                lblSumKrank.Text = $"Krankheit: {totalKrankMin/60}:{(totalKrankMin%60).ToString("D2")}";

                // Ist-Stunden = Netto + Urlaub + Krankheit
                int istMin = netto + totalUrlaubMin + totalKrankMin;
                lblIstStunden.Text = $"Ist-Stunden: {istMin/60}:{(istMin%60).ToString("D2")}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Fehler bei der Aktualisierung der Zusammenfassung:\r\n" + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                    c.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
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

        private void HideCol(String name)
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

        private void PrintMonthReport()
        {
            try
            {
                var table = (_grid.DataSource as DataView)?.Table;
                if (table == null) return;
                string text = GenerateReportText(table);
                string printer = string.Empty;
                try { printer = IniHelper.ReadValue("UI", "DocumentPrinter", AppSettings.IniPath) ?? string.Empty; } catch { }
                PrintText(text, printer);
            }
            catch (Exception ex) { MessageBox.Show(this, "Drucken fehlgeschlagen:\r\n" + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void EmailMonthReport()
        {
            try
            {
                var table = (_grid.DataSource as DataView)?.Table;
                if (table == null) return;
                string employeeMail = null;
                try { employeeMail = _personal?.EMail; } catch { }
                var mailCfg = MailSettings.Load();
                if (string.IsNullOrWhiteSpace(employeeMail)) { MessageBox.Show(this, "Keine Mitarbeiter-E-Mail hinterlegt.", "Mail", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                if (mailCfg == null || !mailCfg.IsConfigured) { MessageBox.Show(this, "Maileinstellungen sind nicht konfiguriert.", "Mail", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                if (!ShowMailConsentDialog(employeeMail)) return;

                // generate PDF of the current view and attach
                string tmpPdf = Path.Combine(Path.GetTempPath(), $"Zeiterfassung_{_personal.PID}_{DateTime.Now.Ticks}.pdf");
                bool pdfOk = false;
                try
                {
                    string printer = string.Empty; try { printer = IniHelper.ReadValue("UI", "DocumentPrinter", AppSettings.IniPath) ?? string.Empty; } catch { }
                    // attempt to create PDF
                    var tryOk = PrintReportToPdf(table, tmpPdf, printer);
                    // additionally verify that a non-empty file was produced
                    try
                    {
                        if (File.Exists(tmpPdf))
                        {
                            var fi = new FileInfo(tmpPdf);
                            pdfOk = tryOk && fi.Length > 0;
                            // if PrintReportToPdf returned false but file exists and has content, accept it
                            if (!pdfOk && fi.Length > 0) pdfOk = true;
                        }
                        else
                        {
                            pdfOk = false;
                        }
                    }
                    catch { pdfOk = tryOk; }
                }
                catch { pdfOk = false; }

                Cursor prev = Cursor.Current; Cursor.Current = Cursors.WaitCursor;
                try
                {
                    if (!pdfOk || !File.Exists(tmpPdf))
                    {
                        // try to surface error details from PdfSharp (if available)
                        try
                        {
                            var logPath = Path.ChangeExtension(tmpPdf, ".pdf.err.txt");
                            if (File.Exists(logPath))
                            {
                                var txt = File.ReadAllText(logPath);
                                // limit length
                                var show = txt.Length > 2000 ? txt.Substring(0, 2000) + "\r\n... (truncated)" : txt;
                                MessageBox.Show(this, "PDF-Erzeugung fehlgeschlagen. Details:\r\n\r\n" + show + "\r\n\r\n(ganzer Log: " + logPath + ")\r\nE-Mail wurde nicht gesendet.", "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                            else
                            {
                                MessageBox.Show(this, "PDF-Erzeugung fehlgeschlagen (keine Logdatei gefunden). E-Mail wurde nicht gesendet.\r\nPfad: " + tmpPdf, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                        }
                        catch
                        {
                            MessageBox.Show(this, "PDF-Erzeugung fehlgeschlagen. E-Mail wurde nicht gesendet.", "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                        try { File.Delete(tmpPdf); } catch { }
                        return;
                    }

                    using (var msg = new System.Net.Mail.MailMessage())
                    {
                        var from = new System.Net.Mail.MailAddress(mailCfg.FromAddress, mailCfg.FromDisplayName);
                        msg.From = from;
                        msg.To.Add(new System.Net.Mail.MailAddress(employeeMail));
                        msg.Subject = "Zeiterfassung - Bericht";
                        msg.Body = "Anbei die angeforderte Zeiterfassung als Anhang.";
                        msg.IsBodyHtml = false;
                        var att = new System.Net.Mail.Attachment(tmpPdf);
                        msg.Attachments.Add(att);

                        using (var client = new System.Net.Mail.SmtpClient(mailCfg.SmtpHost, mailCfg.SmtpPort))
                        {
                            client.EnableSsl = mailCfg.EnableSsl;
                            if (!string.IsNullOrWhiteSpace(mailCfg.Username)) client.Credentials = new System.Net.NetworkCredential(mailCfg.Username, mailCfg.Password);
                            else client.UseDefaultCredentials = true;
                            client.Send(msg);
                        }
                    }
                    MessageBox.Show(this, "E-Mail wurde gesendet.", "Mail", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "E-Mail Versand fehlgeschlagen:\r\n" + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally { try { Cursor.Current = prev; } catch { } try { File.Delete(tmpPdf); } catch { } }
            }
            catch (Exception ex) { MessageBox.Show(this, "Unerwarteter Fehler:\r\n" + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private string GenerateReportText(DataTable table)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Zeiterfassung für: {_personal?.Vorname} {_personal?.Name}");
                sb.AppendLine($"Zeitraum: {lblPeriod.Text}");
                sb.AppendLine(new string('-', 80));
                sb.AppendLine("Datum;Von;Bis;Dauer;Typ;Zusatz");
                foreach (DataRow row in table.Rows)
                {
                    if (row.Table.Columns.Contains("Datum") && row["Datum"] == DBNull.Value) continue;
                    string d = ""; try { if (row["Datum"] != DBNull.Value) d = ((DateTime)row["Datum"]).ToString("dd.MM.yyyy"); } catch { }
                    string zv = (row.Table.Columns.Contains("ZeitVon") && row["ZeitVon"] != DBNull.Value) ? row["ZeitVon"].ToString() : "";
                    string zb = (row.Table.Columns.Contains("ZeitBis") && row["ZeitBis"] != DBNull.Value) ? row["ZeitBis"].ToString() : "";
                    string dauer = (row.Table.Columns.Contains("Dauer") && row["Dauer"] != DBNull.Value) ? row["Dauer"].ToString() : "";
                    string typ = (row.Table.Columns.Contains("TypText") && row["TypText"] != DBNull.Value) ? row["TypText"].ToString() : "";
                    // consider numeric typ
                    try { if (string.IsNullOrWhiteSpace(typ) && row.Table.Columns.Contains("Typ") && row["Typ"] != DBNull.Value) typ = row["Typ"].ToString(); } catch { }
                    string zus = (row.Table.Columns.Contains("Zusatz") && row["Zusatz"] != DBNull.Value) ? row["Zusatz"].ToString() : "";
                    sb.AppendLine($"{d};{zv};{zb};{dauer};{typ};{zus}");
                }
                sb.AppendLine(new string('-', 80));
                return sb.ToString();
            }
            catch { return string.Empty; }
        }

        private void PrintText(string text, string printerName)
        {
            try
            {
                var pd = new PrintDocument();
                if (!string.IsNullOrWhiteSpace(printerName)) pd.PrinterSettings.PrinterName = printerName;
                pd.DocumentName = "Zeiterfassung";
                var lines = text?.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries) ?? new string[0];
                int lineIndex = 0;
                pd.PrintPage += (s, e) =>
                {
                    try
                    {
                        int left = e.MarginBounds.Left; int top = e.MarginBounds.Top;
                        var font = new Font("Segoe UI", 10);
                        float lineHeight = font.GetHeight(e.Graphics) + 2;
                        while (lineIndex < lines.Length)
                        {
                            var toPrint = lines[lineIndex];
                            e.Graphics.DrawString(toPrint, font, Brushes.Black, new RectangleF(left, top, e.MarginBounds.Width, lineHeight));
                            top += (int)lineHeight;
                            lineIndex++;
                            if (top + lineHeight > e.MarginBounds.Bottom) break;
                        }
                        e.HasMorePages = lineIndex < lines.Length;
                    }
                    catch { e.HasMorePages = false; }
                };
                pd.EndPrint += (s, e) => { try { /* cleanup if needed */ } catch { } };
                pd.Print();
            }
            catch (Exception ex) { MessageBox.Show(this, "Druckfehler:\r\n" + ex.Message, "Drucken", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private bool ShowMailConsentDialog(string email)
        {
            try
            {
                var dlg = new Form { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.CenterParent, Width = 720, Height = 380, BackColor = Color.White };
                try { dlg.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, dlg.Width, dlg.Height, 16, 16)); } catch { }
                var header = new Panel { Dock = DockStyle.Top, Height = 60 };
                header.Paint += (s, e) => { using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(header.ClientRectangle, Color.FromArgb(33, 150, 243), Color.FromArgb(33, 203, 243), 0f)) { e.Graphics.FillRectangle(brush, header.ClientRectangle); } };
                dlg.Controls.Add(header);
                var title = new Label { Text = "Dokument per E-Mail", AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Variable", 18F, FontStyle.Bold), ForeColor = Color.White, Dock = DockStyle.Fill, Padding = new Padding(16, 0, 0, 0), BackColor = Color.Transparent };
                header.Controls.Add(title);
                // Read optional top offset from INI for easier tweaking so text isn't hidden under header
                int topOffset = 0;
                try { var raw = IniHelper.ReadValue("UI", "MailConsentBodyTopOffset", AppSettings.IniPath); int v; if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out v)) topOffset = Math.Max(0, Math.Min(400, v)); } catch { }
                var body = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(20, 20 + topOffset, 20, 20) };
                dlg.Controls.Add(body);
                var lblMail = new Label { Text = "Empfänger: " + email, AutoSize = true, Font = new Font("Segoe UI", 12.5F), Dock = DockStyle.Top, Padding = new Padding(0, 0, 0, 10) };
                body.Controls.Add(lblMail);
                var info = new Label { Text = "Hinweis: Der Versand per E-Mail kann Datenschutzrisiken bergen (Weiterleitung, ungesicherte Postfächer). Ich bin einverstanden, dass mir das Dokument an die oben angezeigte Adresse zugesendet wird.", AutoSize = true, MaximumSize = new Size(640, 0), Font = new Font("Segoe UI", 11.5F), Dock = DockStyle.Top, Padding = new Padding(0, 0, 0, 10) };
                body.Controls.Add(info);
                var panelButtons = new Panel { Dock = DockStyle.Bottom, Height = 96, BackColor = Color.White };
                dlg.Controls.Add(panelButtons);
                var btnOk = new Button { Text = "Senden", Width = 180, Height = 56, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(76, 175, 80), ForeColor = Color.White, Font = new Font("Segoe UI Variable", 16F, FontStyle.Bold), TabStop = false };
                btnOk.FlatAppearance.BorderSize = 0; btnOk.Click += (s, e) => { dlg.Tag = true; dlg.Close(); };
                var btnCancel = new Button { Text = "Abbrechen", Width = 180, Height = 56, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(229, 57, 53), ForeColor = Color.White, Font = new Font("Segoe UI Variable", 16F, FontStyle.Bold), TabStop = false };
                btnCancel.FlatAppearance.BorderSize = 0; btnCancel.Click += (s, e) => { dlg.Tag = false; dlg.Close(); };
                panelButtons.Resize += (s, e) => { int spacing = 20; int total = btnOk.Width + btnCancel.Width + spacing; int startX = (panelButtons.ClientSize.Width - total) / 2; int y = (panelButtons.ClientSize.Height - btnOk.Height) / 2; btnOk.Location = new Point(Math.Max(10, startX), y); btnCancel.Location = new Point(btnOk.Right + spacing, y); };
                panelButtons.Controls.Add(btnOk); panelButtons.Controls.Add(btnCancel);
                bool result = false; try { dlg.ShowDialog(this); } catch { dlg.ShowDialog(); } try { result = (dlg.Tag is bool b) ? b : false; } catch { result = false; } try { dlg.Dispose(); } catch { }
                return result;
            }
            catch { return false; }
        }

        private string GenerateReportHtml(DataTable table)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("<!DOCTYPE html><html lang=\"de\"><head><meta charset=\"utf-8\"/><style>body{font-family:Segoe UI,Arial,sans-serif;color:#222;}table{border-collapse:collapse;width:100%;}th,td{border:1px solid #ccc;padding:6px;text-align:left;}th{background:#f0f4f8}</style></head><body>");
                sb.AppendLine($"<h2>Zeiterfassung für: {_personal?.Vorname} {_personal?.Name}</h2>");
                sb.AppendLine($"<div>{lblPeriod.Text}</div>");
                sb.AppendLine("<table>");
                sb.AppendLine("<tr><th>Tag</th><th>Datum</th><th>Von</th><th>Bis</th><th>Dauer</th><th>Typ</th><th>Fahrzeug</th></tr>");
                foreach (DataRow row in table.Rows)
                {
                    if (row.Table.Columns.Contains("Datum") && row["Datum"] == DBNull.Value) continue;
                    string wt = row.Table.Columns.Contains("Wt") && row["Wt"] != DBNull.Value ? row["Wt"].ToString() : "";
                    string d = ""; try { if (row["Datum"] != DBNull.Value) d = ((DateTime)row["Datum"]).ToString("dd.MM.yy"); } catch { }
                    string zv = row.Table.Columns.Contains("ZeitVon") && row["ZeitVon"] != DBNull.Value ? row["ZeitVon"].ToString() : "";
                    string zb = row.Table.Columns.Contains("ZeitBis") && row["ZeitBis"] != DBNull.Value ? row["ZeitBis"].ToString() : "";
                    string dauer = row.Table.Columns.Contains("Dauer") && row["Dauer"] != DBNull.Value ? row["Dauer"].ToString() : "";
                    string typ = row.Table.Columns.Contains("TypText") && row["TypText"] != DBNull.Value ? row["TypText"].ToString() : (row.Table.Columns.Contains("Typ") && row["Typ"] != DBNull.Value ? row["Typ"].ToString() : "");
                    string zus = row.Table.Columns.Contains("Zusatz") && row["Zusatz"] != DBNull.Value ? row["Zusatz"].ToString() : "";
                    sb.AppendLine($"<tr><td>{HtmlEscape(wt)}</td><td>{HtmlEscape(d)}</td><td>{HtmlEscape(zv)}</td><td>{HtmlEscape(zb)}</td><td>{HtmlEscape(dauer)}</td><td>{HtmlEscape(typ)}</td><td>{HtmlEscape(zus)}</td></tr>");
                }
                sb.AppendLine("</table>");
                sb.AppendLine("</body></html>");
                return sb.ToString();
            }
            catch { return string.Empty; }
        }

        private bool PrintReportToPdf(DataTable table, string pdfPath, string printerName)
        {
            try
            {
                // produce a nicely formatted table PDF
                var doc = new PdfDocument();
                try { if (GlobalFontSettings.FontResolver == null) GlobalFontSettings.FontResolver = new PdfFontResolver(); } catch { }
                doc.Info.Title = "Zeiterfassung";

                // choose font (no XFontStyle constants to keep compatibility)
                XFont headerFont = new XFont("Segoe UI", 16);
                XFont titleFont = new XFont("Segoe UI", 20);
                XFont font = new XFont("Segoe UI", 11);

                const double margin = 40.0;

                // layout
                var page = doc.AddPage();
                page.Size = PdfSharp.PageSize.A4;
                var gfx = XGraphics.FromPdfPage(page);

                double y = margin;
                double pageHeight = page.Height;
                double pageWidth = page.Width;
                double contentWidth = pageWidth - margin * 2;

                // title
                gfx.DrawString($"Zeiterfassung für: {_personal?.Vorname} {_personal?.Name}", titleFont, XBrushes.Black, new XRect(margin, y, contentWidth, 30), XStringFormats.TopLeft);
                y += 30 + 6;
                gfx.DrawString(lblPeriod.Text, headerFont, XBrushes.Black, new XRect(margin, y, contentWidth, 22), XStringFormats.TopLeft);
                y += 22 + 12;

                // draw separator (gray bar under month/year)
                gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(240, 240, 240)), margin, y, contentWidth, 28);
                y += 28 + 6;

                // header row
                double rowHeight = Math.Max(18, font.GetHeight() + 6);
                var penBorder = new XPen(XColors.LightGray, 0.5);
                double x = margin;

                // define column widths (matching UI)
                double colTag = 70;
                double colDatum = 180;
                double colVon = 110;
                double colBis = 110;
                double colDauer = 120;
                double colTyp = 160;
                double colZusatz = contentWidth - (colTag + colDatum + colVon + colBis + colDauer + colTyp);
                if (colZusatz < 80) { colZusatz = 80; colTyp = Math.Max(60, contentWidth - (colTag + colDatum + colVon + colBis + colDauer + colZusatz)); }

                // draw header background
                gfx.DrawRectangle(XBrushes.WhiteSmoke, x, y, contentWidth, rowHeight);

                gfx.DrawString("Tag", font, XBrushes.Black, new XRect(x + 4, y + 3, colTag - 8, rowHeight), XStringFormats.TopLeft); x += colTag;
                gfx.DrawString("Datum", font, XBrushes.Black, new XRect(x + 4, y + 3, colDatum - 8, rowHeight), XStringFormats.TopLeft); x += colDatum;
                gfx.DrawString("Von", font, XBrushes.Black, new XRect(x + 4, y + 3, colVon - 8, rowHeight), XStringFormats.TopLeft); x += colVon;
                gfx.DrawString("Bis", font, XBrushes.Black, new XRect(x + 4, y + 3, colBis - 8, rowHeight), XStringFormats.TopLeft); x += colBis;
                gfx.DrawString("Dauer", font, XBrushes.Black, new XRect(x + 4, y + 3, colDauer - 8, rowHeight), XStringFormats.TopLeft); x += colDauer;
                gfx.DrawString("Typ", font, XBrushes.Black, new XRect(x + 4, y + 3, colTyp - 8, rowHeight), XStringFormats.TopLeft); x += colTyp;
                gfx.DrawString("Fahrzeug", font, XBrushes.Black, new XRect(x + 4, y + 3, colZusatz - 8, rowHeight), XStringFormats.TopLeft);

                // draw header borders
                gfx.DrawRectangle(penBorder, margin, y, contentWidth, rowHeight);
                // vertical lines
                double vx = margin; gfx.DrawLine(penBorder, vx+colTag, y, vx+colTag, y+rowHeight); vx += colTag; gfx.DrawLine(penBorder, vx+colDatum, y, vx+colDatum, y+rowHeight); vx += colDatum; gfx.DrawLine(penBorder, vx+colVon, y, vx+colVon, y+rowHeight); vx += colVon; gfx.DrawLine(penBorder, vx+colBis, y, vx+colBis, y+rowHeight); vx += colBis; gfx.DrawLine(penBorder, vx+colDauer, y, vx+colDauer, y+rowHeight); vx += colDauer; gfx.DrawLine(penBorder, vx+colTyp, y, vx+colTyp, y+rowHeight);

                y += rowHeight;

                // compute summary totals from the same data source (prefer DataView to keep sort)
                int totalArbeitMin = 0, totalPauseMin = 0, totalNichtGewertetMin = 0, totalUrlaubMin = 0, totalKrankMin = 0;
                var dvSource = _grid.DataSource as DataView;
                IEnumerable<DataRow> rowsToIterate;
                if (dvSource != null)
                    rowsToIterate = dvSource.Cast<DataRowView>().Select(r => r.Row);
                else
                    rowsToIterate = table.Rows.Cast<DataRow>();

                foreach (var rsum in rowsToIterate)
                {
                    try
                    {
                        if (rsum.Table.Columns.Contains("Datum") && rsum["Datum"] == DBNull.Value) continue;
                        int minutes = 0;
                        try
                        {
                            if (rsum.Table.Columns.Contains("ZeitVon") && rsum.Table.Columns.Contains("ZeitBis") && rsum["ZeitVon"] != DBNull.Value && rsum["ZeitBis"] != DBNull.Value)
                            {
                                var zv = (DateTime)rsum["ZeitVon"]; var zb = (DateTime)rsum["ZeitBis"]; minutes = (int)Math.Round((zb - zv).TotalMinutes);
                            }
                            else if (rsum.Table.Columns.Contains("Dauer") && rsum["Dauer"] != DBNull.Value)
                            {
                                var dstr = rsum["Dauer"].ToString(); var parts = dstr.Split(':');
                                if (parts.Length == 2 && int.TryParse(parts[0], out int h) && int.TryParse(parts[1], out int m)) minutes = h * 60 + m; else if (int.TryParse(dstr, out int mm)) minutes = mm;
                            }
                        }
                        catch { }
                        string typText = string.Empty; if (rsum.Table.Columns.Contains("TypText") && rsum["TypText"] != DBNull.Value) typText = rsum["TypText"].ToString().ToLowerInvariant();
                        int typCode = -1; try { if (rsum.Table.Columns.Contains("Typ") && rsum["Typ"] != DBNull.Value) int.TryParse(rsum["Typ"].ToString(), out typCode); } catch { }

                        if (typText.Contains("arbeit")) totalArbeitMin += minutes;
                        else if (typText.Contains("pause")) { if (minutes > 0 && minutes < 15) totalNichtGewertetMin += minutes; else totalPauseMin += minutes; }
                        else if (typText.Contains("urlaub")) totalUrlaubMin += minutes;
                        else if (typText.Contains("krank")) totalKrankMin += minutes;
                        else if (typCode == 11) totalUrlaubMin += minutes;
                        else if (typCode == 21) totalKrankMin += minutes;
                    }
                    catch { }
                }

                // draw summary lines (no yellow band) under the gray bar
                try
                {
                    int netto = totalArbeitMin - totalPauseMin; if (netto < 0) netto = 0;
                    int istMin = netto + totalUrlaubMin + totalKrankMin;
                    double sx = margin + 8; double sy = y;
                    var boldFont = new XFont(font.FontFamily.Name, 11);
                    gfx.DrawString($"Arbeitszeit: {totalArbeitMin/60}:{(totalArbeitMin%60).ToString("D2")}", boldFont, XBrushes.Black, new XRect(sx, sy, 240, 16), XStringFormats.TopLeft);
                    gfx.DrawString($"Pause: {totalPauseMin/60}:{(totalPauseMin%60).ToString("D2")}", boldFont, XBrushes.Black, new XRect(sx + 260, sy, 200, 16), XStringFormats.TopLeft);
                    gfx.DrawString($"Pause <15 min: {totalNichtGewertetMin/60}:{(totalNichtGewertetMin%60).ToString("D2")}", boldFont, XBrushes.Black, new XRect(sx + 460, sy, 240, 16), XStringFormats.TopLeft);
                    gfx.DrawString($"Nettoarbeitszeit: {netto/60}:{(netto%60).ToString("D2")}", boldFont, XBrushes.Black, new XRect(sx + 740, sy, 260, 16), XStringFormats.TopLeft);
                    // second line
                    gfx.DrawString($"Urlaub: {totalUrlaubMin/60}:{(totalUrlaubMin%60).ToString("D2")}", font, XBrushes.Black, new XRect(sx, sy + 18, 220, 16), XStringFormats.TopLeft);
                    gfx.DrawString($"Krankheit: {totalKrankMin/60}:{(totalKrankMin%60).ToString("D2")}", font, XBrushes.Black, new XRect(sx + 260, sy + 18, 220, 16), XStringFormats.TopLeft);
                    gfx.DrawString($"Ist-Stunden: {istMin/60}:{(istMin%60).ToString("D2")}", font, XBrushes.Black, new XRect(sx + 740, sy + 18, 220, 16), XStringFormats.TopLeft);
                    y += 36;
                }
                catch { }

                // rows
                int rowIndex = 0;
                foreach (DataRow row in (dvSource != null ? dvSource.Cast<DataRowView>().Select(rv => rv.Row) : table.Rows.Cast<DataRow>()))
                 {
                     try
                     {
                        if (row.Table.Columns.Contains("Datum") && row["Datum"] == DBNull.Value) continue;
                        // prepare values
                        string wt = row.Table.Columns.Contains("Wt") && row["Wt"] != DBNull.Value ? row["Wt"].ToString() : "";
                        DateTime? date = null; try { if (row["Datum"] != DBNull.Value) date = (DateTime)row["Datum"]; } catch { }
                        string d = date.HasValue ? date.Value.ToString("dd.MM.yyyy") : string.Empty;
                        // show only time for Von/Bis (we already have the date column)
                        string zv = string.Empty; string zb = string.Empty;
                        try { if (row.Table.Columns.Contains("ZeitVon") && row["ZeitVon"] != DBNull.Value) zv = ((DateTime)row["ZeitVon"]).ToString("HH:mm"); } catch { }
                        try { if (row.Table.Columns.Contains("ZeitBis") && row["ZeitBis"] != DBNull.Value) zb = ((DateTime)row["ZeitBis"]).ToString("HH:mm"); } catch { }
                          string dauer = row.Table.Columns.Contains("Dauer") && row["Dauer"] != DBNull.Value ? row["Dauer"].ToString() : "";
                          string typ = row.Table.Columns.Contains("TypText") && row["TypText"] != DBNull.Value ? row["TypText"].ToString() : (row.Table.Columns.Contains("Typ") && row["Typ"] != DBNull.Value ? row["Typ"].ToString() : "");
                          string zus = row.Table.Columns.Contains("Zusatz") && row["Zusatz"] != DBNull.Value ? row["Zusatz"].ToString() : "";

                        // compute row minutes (prefer ZeitVon/ZeitBis)
                        int rowMinutes = 0;
                        try
                        {
                            if (row.Table.Columns.Contains("ZeitVon") && row.Table.Columns.Contains("ZeitBis") && row["ZeitVon"] != DBNull.Value && row["ZeitVon"] != DBNull.Value && row["ZeitBis"] != null && row["ZeitBis"] != DBNull.Value)
                            {
                                var zvdt = (DateTime)row["ZeitVon"]; var zbdt = (DateTime)row["ZeitBis"]; rowMinutes = (int)Math.Round((zbdt - zvdt).TotalMinutes);
                            }
                            else if (row.Table.Columns.Contains("Dauer") && row["Dauer"] != DBNull.Value)
                            {
                                var dstr = row["Dauer"].ToString(); var parts = dstr.Split(':');
                                if (parts.Length == 2 && int.TryParse(parts[0], out int h) && int.TryParse(parts[1], out int m)) rowMinutes = h * 60 + m; else if (int.TryParse(dstr, out int mm)) rowMinutes = mm;
                            }
                        }
                        catch { }

                        // determine typCode if present
                        int typCode = -1; try { if (row.Table.Columns.Contains("Typ") && row["Typ"] != DBNull.Value) int.TryParse(row["Typ"].ToString(), out typCode); } catch { }

                        // hide day label for any Pause rows (also <15min)
                        var typLower = (typ ?? string.Empty).ToLowerInvariant();
                        if (typLower.Contains("pause")) wt = string.Empty;

                        // determine background color by priority: Typ codes -> short pause -> Sunday -> alternating
                        XBrush rowBrush = null;
                        bool applied = false;
                        try
                        {
                            // 1) Typ codes (highest priority)
                            if (typCode == 11)
                            {
                                rowBrush = new XSolidBrush(XColor.FromArgb(255, 255, 250, 205)); // Urlaub light yellow
                                applied = true;
                            }
                            else if (typCode == 21)
                            {
                                rowBrush = new XSolidBrush(XColor.FromArgb(255, 220, 80, 80)); // Krank
                                applied = true;
                            }
                            // 2) short pause
                            if (!applied)
                            {
                                if (!string.IsNullOrWhiteSpace(typLower) && typLower.Contains("pause") && rowMinutes > 0 && rowMinutes < 15)
                                {
                                    rowBrush = new XSolidBrush(XColor.FromArgb(235, 242, 255));
                                    applied = true;
                                }
                            }
                            // 3) Sunday highlight
                            if (!applied)
                            {
                                if (date.HasValue && date.Value.DayOfWeek == DayOfWeek.Sunday)
                                {
                                    // light red for Sundays
                                    rowBrush = new XSolidBrush(XColor.FromArgb(255, 255, 220, 220));
                                    applied = true;
                                }
                            }
                        }
                        catch { }

                         // row height (single line)
                         double thisRowHeight = rowHeight;
                         // page break if needed
                         if (y + thisRowHeight + margin > pageHeight)
                         {
                            // new page
                            page = doc.AddPage(); page.Size = PdfSharp.PageSize.A4; gfx = XGraphics.FromPdfPage(page);
                            y = margin;

                            // draw header on new page
                            gfx.DrawString($"Zeiterfassung für: {_personal?.Vorname} {_personal?.Name}", titleFont, XBrushes.Black, new XRect(margin, y, contentWidth, 30), XStringFormats.TopLeft);
                            y += 30 + 6;
                            gfx.DrawString(lblPeriod.Text, headerFont, XBrushes.Black, new XRect(margin, y, contentWidth, 22), XStringFormats.TopLeft);
                            y += 22 + 12;
                            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(240,240,240)), margin, y, contentWidth, 28);
                            y += 28 + 6;

                            // header row
                            x = margin;
                            gfx.DrawRectangle(XBrushes.WhiteSmoke, x, y, contentWidth, rowHeight);
                            gfx.DrawString("Tag", font, XBrushes.Black, new XRect(x + 4, y + 3, colTag - 8, rowHeight), XStringFormats.TopLeft); x += colTag;
                            gfx.DrawString("Datum", font, XBrushes.Black, new XRect(x + 4, y + 3, colDatum - 8, rowHeight), XStringFormats.TopLeft); x += colDatum;
                            gfx.DrawString("Von", font, XBrushes.Black, new XRect(x + 4, y + 3, colVon - 8, rowHeight), XStringFormats.TopLeft); x += colVon;
                            gfx.DrawString("Bis", font, XBrushes.Black, new XRect(x + 4, y + 3, colBis - 8, rowHeight), XStringFormats.TopLeft); x += colBis;
                            gfx.DrawString("Dauer", font, XBrushes.Black, new XRect(x + 4, y + 3, colDauer - 8, rowHeight), XStringFormats.TopLeft); x += colDauer;
                            gfx.DrawString("Typ", font, XBrushes.Black, new XRect(x + 4, y + 3, colTyp - 8, rowHeight), XStringFormats.TopLeft); x += colTyp;
                            gfx.DrawString("Fahrzeug", font, XBrushes.Black, new XRect(x + 4, y + 3, colZusatz - 8, rowHeight), XStringFormats.TopLeft);
                            gfx.DrawRectangle(penBorder, margin, y, contentWidth, rowHeight);
                            y += rowHeight;
                        }

                        // alternating background (only if no specific color applied)
                        if (rowBrush == null)
                        {
                            if (rowIndex % 2 == 1) gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(240, 248, 255)), margin, y, contentWidth, thisRowHeight);
                        }
                        else
                        {
                            gfx.DrawRectangle(rowBrush, margin, y, contentWidth, thisRowHeight);
                        }

                         x = margin;
                         gfx.DrawString(wt, font, XBrushes.Black, new XRect(x + 4, y + 3, colTag - 8, thisRowHeight), XStringFormats.TopLeft); x += colTag;
                         gfx.DrawString(d, font, XBrushes.Black, new XRect(x + 4, y + 3, colDatum - 8, thisRowHeight), XStringFormats.TopLeft); x += colDatum;
                         gfx.DrawString(zv, font, XBrushes.Black, new XRect(x + 4, y + 3, colVon - 8, thisRowHeight), XStringFormats.TopLeft); x += colVon;
                         gfx.DrawString(zb, font, XBrushes.Black, new XRect(x + 4, y + 3, colBis - 8, thisRowHeight), XStringFormats.TopLeft); x += colBis;
                         gfx.DrawString(dauer, font, XBrushes.Black, new XRect(x + 4, y + 3, colDauer - 8, thisRowHeight), XStringFormats.TopLeft); x += colDauer;
                         gfx.DrawString(typ, font, XBrushes.Black, new XRect(x + 4, y + 3, colTyp - 8, thisRowHeight), XStringFormats.TopLeft); x += colTyp;
                         gfx.DrawString(zus, font, XBrushes.Black, new XRect(x + 4, y + 3, colZusatz - 8, thisRowHeight), XStringFormats.TopLeft);

                         // borders for row
                         gfx.DrawRectangle(penBorder, margin, y, contentWidth, thisRowHeight);

                         y += thisRowHeight;
                         rowIndex++;
                     }
                     catch { }
                 }


                // save
                try { var dir = Path.GetDirectoryName(pdfPath); if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir); } catch { }
                doc.Save(pdfPath);
                doc.Close();

                try
                {
                    var fi = new FileInfo(pdfPath);
                    return fi.Exists && fi.Length > 0;
                }
                catch { return File.Exists(pdfPath); }
            }
            catch (Exception ex)
            {
                try { var logPath = Path.ChangeExtension(pdfPath, ".pdf.err.txt"); File.WriteAllText(logPath, ex.ToString()); } catch { }
                return false;
            }
        }

        private string HtmlEscape(string s)
        {
            return (s ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}
