using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Threading;
using System.Linq; // NEU für OrderBy
using System.Runtime.InteropServices;
using System.Drawing;

namespace TaMi_Einzahlautomat
{
    public class LogViewForm : Form
    {
        private Panel _headerPanel;
        private Label _lblTitle;
        private Button _btnHeaderClose;
        private Point _mouseDownLocation;

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

        private void ApplyModernButtonStyle(Button b, System.Drawing.Color c1, System.Drawing.Color c2)
        {
            if (b == null) return;
            try
            {
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 0;
                b.BackColor = System.Drawing.Color.Transparent;
                b.UseVisualStyleBackColor = false;
                b.ForeColor = System.Drawing.Color.White;
                b.Paint -= ModernButton_Paint;
                b.Paint += ModernButton_Paint;
                b.Tag = new Tuple<System.Drawing.Color, System.Drawing.Color>(c1, c2);
                try { b.Region = System.Drawing.Region.FromHrgn(CreateRoundRectRgn(0, 0, b.Width, b.Height, 14, 14)); } catch { }
                b.Resize -= ModernButton_Resize;
                b.Resize += ModernButton_Resize;
            }
            catch { }
        }

        private void ModernButton_Resize(object sender, EventArgs e)
        {
            try
            {
                var b = sender as Button;
                if (b == null) return;
                try { b.Region = System.Drawing.Region.FromHrgn(CreateRoundRectRgn(0, 0, b.Width, b.Height, 14, 14)); } catch { }
                b.Invalidate();
            }
            catch { }
        }

        private void ModernButton_Paint(object sender, PaintEventArgs e)
        {
            var b = sender as Button;
            if (b == null) return;
            try
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var rect = b.ClientRectangle;
                rect.Width -= 1;
                rect.Height -= 1;

                var colors = b.Tag as Tuple<System.Drawing.Color, System.Drawing.Color>;
                var cc1 = colors != null ? colors.Item1 : System.Drawing.Color.FromArgb(33, 150, 243);
                var cc2 = colors != null ? colors.Item2 : System.Drawing.Color.FromArgb(13, 71, 161);

                using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(rect, cc1, cc2, 90f))
                {
                    e.Graphics.FillRectangle(br, rect);
                }
                using (var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(110, 255, 255, 255), 1f))
                {
                    e.Graphics.DrawRectangle(pen, rect);
                }

                TextRenderer.DrawText(e.Graphics, b.Text, b.Font, b.ClientRectangle, b.ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            catch { }
        }
        private TextBox txtLog;
        private Button btnReload;
        private Button btnClose;
        private Button _btnDiff;   // DIF-Suche
        private ComboBox _cmbFiles; // Dateiauswahl
        private TextBox _txtSearch; // Sucheingabe
        private Button _btnFind;    // Suchen
        private Button _btnNext;    // Weiter
        private CheckBox _chkCase;  // Groß/Klein beachten
        private int _lastFindIndex = -1;
        private string _lastFindTerm = null;
        private int _lastDiffIndex = -1; // Letzte DIF-Position

        private string _logFilePath;
        private string _logDir;

        // Cue Banner für TextBox (Platzhalter)
        private const uint EM_SETCUEBANNER = 0x1501;
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, string lParam);
        private static void SetCueBanner(TextBox box, string text)
        {
            if (box == null) return;
            if (box.IsHandleCreated)
                SendMessage(box.Handle, EM_SETCUEBANNER, (IntPtr)1, text);
            else
                box.HandleCreated += (s, e) => SendMessage(box.Handle, EM_SETCUEBANNER, (IntPtr)1, text);
        }

        public LogViewForm()
        {
            Text = "Log anzeigen";
            StartPosition = FormStartPosition.CenterParent;
            Size = new System.Drawing.Size(1100, 720);
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = System.Drawing.Color.White;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new System.Drawing.Font("Segoe UI", 9F);
            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.F) { _txtSearch?.Focus(); _txtSearch?.SelectAll(); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.F3) { DoFind(false); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.F5) { LoadLog(); e.SuppressKeyPress = true; }
                else if (e.Alt && e.KeyCode == Keys.D) { JumpToNextDiff(); e.SuppressKeyPress = true; }
            };

            // Header
            _headerPanel = new Panel { Dock = DockStyle.Top, Height = 60 };
            _headerPanel.Paint += (s, e) =>
            {
                try
                {
                    e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    var r = _headerPanel.ClientRectangle;
                    using (var b = new System.Drawing.Drawing2D.LinearGradientBrush(r, System.Drawing.Color.FromArgb(13, 71, 161), System.Drawing.Color.FromArgb(120, 200, 255), 0f))
                        e.Graphics.FillRectangle(b, r);
                }
                catch
                {
                    using (var b = new System.Drawing.Drawing2D.LinearGradientBrush(_headerPanel.ClientRectangle, System.Drawing.Color.FromArgb(33, 150, 243), System.Drawing.Color.FromArgb(33, 203, 243), 0f))
                        e.Graphics.FillRectangle(b, _headerPanel.ClientRectangle);
                }
            };
            _headerPanel.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) _mouseDownLocation = new Point(e.X, e.Y); };
            _headerPanel.MouseMove += (s, e) => { if (e.Button == MouseButtons.Left) { Left += e.X - _mouseDownLocation.X; Top += e.Y - _mouseDownLocation.Y; } };
            Controls.Add(_headerPanel);

            _lblTitle = new Label { Text = "Log anzeigen", AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Font = new System.Drawing.Font("Segoe UI Variable", 18F, System.Drawing.FontStyle.Bold), ForeColor = System.Drawing.Color.White, Location = new System.Drawing.Point(24, 0), Size = new System.Drawing.Size(420, 60), BackColor = System.Drawing.Color.Transparent };
            _headerPanel.Controls.Add(_lblTitle);

            _btnHeaderClose = new Button { Text = "\u2715", Font = new System.Drawing.Font("Segoe UI Symbol", 18F, System.Drawing.FontStyle.Bold), ForeColor = System.Drawing.Color.White, BackColor = System.Drawing.Color.Transparent, FlatStyle = FlatStyle.Flat, Size = new System.Drawing.Size(48, 48), Location = new System.Drawing.Point(Width - 72, 6), Anchor = AnchorStyles.Top | AnchorStyles.Right, TabStop = false };
            _btnHeaderClose.FlatAppearance.BorderSize = 0;
            _btnHeaderClose.FlatAppearance.MouseOverBackColor = System.Drawing.Color.Transparent;
            _btnHeaderClose.Click += (s, e) => Close();
            _headerPanel.Controls.Add(_btnHeaderClose);
            try { ApplyModernButtonStyle(_btnHeaderClose, System.Drawing.Color.FromArgb(239, 83, 80), System.Drawing.Color.FromArgb(198, 40, 40)); } catch { }

            try { _headerPanel.Controls.SetChildIndex(_btnHeaderClose, 0); } catch { }

            try { Region = System.Drawing.Region.FromHrgn(CreateRoundRectRgn(0, 0, Width, Height, 24, 24)); } catch { }

            // Log-Textbox zuerst hinzufügen (damit Dock Fill nicht überlagert wird)
            txtLog = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                Dock = DockStyle.Bottom,
                Height = 520,
                Font = new System.Drawing.Font("Consolas", 10F),
                WordWrap = false,
                HideSelection = false,
                BackColor = System.Drawing.SystemColors.Window
            };
            Controls.Add(txtLog);

            // Unterer Close-Button
            btnClose = new Button
            {
                Text = "Schließen",
                Dock = DockStyle.Bottom,
                Height = 36
            };
            btnClose.Click += (s, e) => Close();
            try { ApplyModernButtonStyle(btnClose, System.Drawing.Color.FromArgb(96, 125, 139), System.Drawing.Color.FromArgb(55, 71, 79)); } catch { }
            Controls.Add(btnClose);

            // Toolbar: TableLayoutPanel im Header-Bereich (oberhalb der Log-Anzeige)
            var top = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                Padding = new Padding(6),
                ColumnCount = 7,
                RowCount = 1,
                BackColor = System.Drawing.Color.Transparent
            };
            top.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260F)); // Datei-Auswahl
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));       // Reload
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));       // DIF
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));  // Suche
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));       // Suchen
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));       // Weiter
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));       // Aa
            _headerPanel.Height = 60 + top.Height;
            _headerPanel.Controls.Add(top);

            _cmbFiles = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                IntegralHeight = false,
                MaxDropDownItems = 12,
                Dock = DockStyle.Fill
            };
            try { _cmbFiles.FlatStyle = FlatStyle.Flat; _cmbFiles.BackColor = System.Drawing.Color.FromArgb(245, 247, 250); _cmbFiles.ForeColor = System.Drawing.Color.FromArgb(33, 37, 41); } catch { }
            _cmbFiles.DropDownHeight = 260;
            _cmbFiles.SelectedIndexChanged += (s, e) => OnSelectedFileChanged();
            _cmbFiles.Margin = new Padding(0, 6, 8, 6);
            top.Controls.Add(_cmbFiles, 0, 0);

            btnReload = new Button { Text = "Aktualisieren", AutoSize = true };
            btnReload.Margin = new Padding(0, 6, 6, 6);
            btnReload.Click += (s, e) => LoadLog();
            try { ApplyModernButtonStyle(btnReload, System.Drawing.Color.FromArgb(33, 150, 243), System.Drawing.Color.FromArgb(13, 71, 161)); } catch { }
            top.Controls.Add(btnReload, 1, 0);

            _btnDiff = new Button { Text = "DIF", AutoSize = true };
            _btnDiff.Margin = new Padding(0, 6, 6, 6);
            _btnDiff.Click += (s, e) => JumpToNextDiff();
            try { ApplyModernButtonStyle(_btnDiff, System.Drawing.Color.FromArgb(96, 125, 139), System.Drawing.Color.FromArgb(55, 71, 79)); } catch { }
            top.Controls.Add(_btnDiff, 2, 0);

            _txtSearch = new TextBox { Dock = DockStyle.Fill };
            _txtSearch.Margin = new Padding(0, 6, 6, 6);
            _txtSearch.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.Handled = true; e.SuppressKeyPress = true; DoFind(true); } };
            try { _txtSearch.BackColor = System.Drawing.Color.FromArgb(245, 247, 250); _txtSearch.ForeColor = System.Drawing.Color.FromArgb(33, 37, 41); } catch { }
            top.Controls.Add(_txtSearch, 3, 0);
            SetCueBanner(_txtSearch, "Suchen … (F3=Weiter, Strg+F=Fokus)");

            _btnFind = new Button { Text = "Suchen", AutoSize = true };
            _btnFind.Margin = new Padding(0, 6, 6, 6);
            _btnFind.Click += (s, e) => DoFind(true);
            try { ApplyModernButtonStyle(_btnFind, System.Drawing.Color.FromArgb(33, 150, 243), System.Drawing.Color.FromArgb(13, 71, 161)); } catch { }
            top.Controls.Add(_btnFind, 4, 0);

            _btnNext = new Button { Text = "Weiter", AutoSize = true };
            _btnNext.Margin = new Padding(0, 6, 6, 6);
            _btnNext.Click += (s, e) => DoFind(false);
            try { ApplyModernButtonStyle(_btnNext, System.Drawing.Color.FromArgb(33, 150, 243), System.Drawing.Color.FromArgb(13, 71, 161)); } catch { }
            top.Controls.Add(_btnNext, 5, 0);

            _chkCase = new CheckBox { Text = "Aa", AutoSize = true };
            _chkCase.Margin = new Padding(0, 10, 0, 6);
            _chkCase.CheckedChanged += (s, e) => { _lastFindIndex = -1; _lastFindTerm = null; };
            top.Controls.Add(_chkCase, 6, 0);

            // Tooltips für bessere UX
            var tips = new ToolTip();
            tips.SetToolTip(_cmbFiles, "Logdatei auswählen");
            tips.SetToolTip(btnReload, "Log neu laden (F5)");
            tips.SetToolTip(_btnDiff, "Zur nächsten Kassendifferenz ≠ 0 springen (Alt+D)");
            tips.SetToolTip(_txtSearch, "Suchbegriff eingeben");
            tips.SetToolTip(_btnFind, "Suchen starten");
            tips.SetToolTip(_btnNext, "Nächsten Treffer suchen (F3)");
            tips.SetToolTip(_chkCase, "Groß-/Kleinschreibung beachten");

            // Log-Verzeichnis und Dateiliste bestimmen
            _logDir = Path.Combine(Application.StartupPath, "Ereignisse");
            PopulateFileList();
        }

        private void PopulateFileList()
        {
            _cmbFiles.Items.Clear();
            try
            {
                if (!Directory.Exists(_logDir)) Directory.CreateDirectory(_logDir);
                var files = Directory.GetFiles(_logDir, "*.log")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(fi => fi.Name)
                    .Take(30)
                    .ToList();
                foreach (var fi in files)
                    _cmbFiles.Items.Add(fi.Name);
                if (files.Count > 0)
                {
                    string todayName = DateTime.Today.ToString("yyyy-MM-dd") + ".log";
                    int idx = _cmbFiles.Items.IndexOf(todayName);
                    _cmbFiles.SelectedIndex = idx >= 0 ? idx : 0;
                }
                else
                {
                    txtLog.Text = "Keine Logdateien gefunden.";
                }
            }
            catch (Exception ex)
            {
                txtLog.Text = "Fehler beim Auflisten der Logdateien:\r\n" + ex.Message;
            }
        }

        private void OnSelectedFileChanged()
        {
            try
            {
                var sel = _cmbFiles.SelectedItem as string;
                if (string.IsNullOrWhiteSpace(sel)) return;
                _logFilePath = Path.Combine(_logDir, sel);
                LoadLog();
            }
            catch { }
        }

        private void LoadLog()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_logFilePath))
                {
                    txtLog.Text = "Keine Datei gewählt.";
                    return;
                }
                if (!File.Exists(_logFilePath))
                {
                    txtLog.Text = $"Logdatei nicht gefunden:\r\n{_logFilePath}";
                    return;
                }
                const int maxAttempts = 3;
                int attempt = 0;
                Exception lastEx = null;
                while (attempt < maxAttempts)
                {
                    try
                    {
                        using (var fs = new FileStream(_logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var sr = new StreamReader(fs, Encoding.UTF8, true))
                        {
                            txtLog.Text = sr.ReadToEnd();
                            lastEx = null;
                            break;
                        }
                    }
                    catch (IOException ex)
                    {
                        lastEx = ex;
                        attempt++;
                        Thread.Sleep(80);
                    }
                }
                if (lastEx != null)
                    txtLog.Text = $"Fehler beim Laden der Logdatei:\r\n{lastEx.Message}";

                // Suche-States zurücksetzen
                _lastFindIndex = -1;
                _lastDiffIndex = -1;

                if (!string.IsNullOrWhiteSpace(_lastFindTerm))
                {
                    txtLog.SelectionStart = 0;
                    txtLog.SelectionLength = 0;
                    DoFind(true);
                }
            }
            catch (Exception ex)
            {
                txtLog.Text = $"Fehler beim Laden der Logdatei:\r\n{ex.Message}";
            }
        }

        private void DoFind(bool first)
        {
            try
            {
                string term = (_txtSearch.Text ?? string.Empty);
                if (string.IsNullOrEmpty(term)) return;

                var comparison = _chkCase.Checked ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

                if (first || !string.Equals(term, _lastFindTerm, StringComparison.Ordinal))
                {
                    _lastFindIndex = -1;
                    _lastFindTerm = term;
                }

                int startIdx = _lastFindIndex < 0 ? 0 : _lastFindIndex + Math.Max(1, term.Length);
                if (startIdx > txtLog.TextLength) startIdx = 0;

                int idx = txtLog.Text.IndexOf(term, startIdx, comparison);
                if (idx < 0 && startIdx > 0)
                {
                    idx = txtLog.Text.IndexOf(term, 0, comparison);
                }

                if (idx >= 0)
                {
                    _lastFindIndex = idx;
                    _lastFindTerm = term;
                    txtLog.SelectionStart = idx;
                    txtLog.SelectionLength = term.Length;
                    txtLog.ScrollToCaret();
                    txtLog.Focus();
                }
                else
                {
                    System.Media.SystemSounds.Beep.Play();
                }
            }
            catch { }
        }

        // Springt zum nächsten Eintrag mit "Kassendifferenz != 0"
        private void JumpToNextDiff()
        {
            try
            {
                string text = txtLog.Text;
                if (string.IsNullOrEmpty(text)) { System.Media.SystemSounds.Beep.Play(); return; }

                int start = _lastDiffIndex < 0 ? 0 : Math.Min(_lastDiffIndex + 1, text.Length - 1);

                // Suche nach "Kassendifferenz" und einer Zahl in derselben Zeile
                int idx = text.IndexOf("Kassendifferenz", start, StringComparison.OrdinalIgnoreCase);
                while (idx >= 0)
                {
                    // Zeilengrenzen
                    int lineStart = text.LastIndexOf('\n', idx);
                    lineStart = lineStart < 0 ? 0 : lineStart + 1;
                    int lineEnd = text.IndexOf('\n', idx);
                    if (lineEnd < 0) lineEnd = text.Length;
                    string line = text.Substring(lineStart, lineEnd - lineStart);

                    // Zahl extrahieren (deutsch/englisch, mit Tausendern)
                    decimal val = 0m;
                    bool hasNumber = false;
                    foreach (Match m in Regex.Matches(line, @"[+-]?\d{1,3}(?:[.\s]\d{3})*(?:[,\.]\d+)?|[+-]?\d+(?:[,\.]\d+)?"))
                    {
                        string raw = m.Value.Replace("€", string.Empty).Replace(" ", string.Empty);
                        string norm = raw.Replace(".", string.Empty).Replace(",", ".");
                        decimal temp;
                        if (decimal.TryParse(norm, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out temp))
                        {
                            val = temp;
                            hasNumber = true;
                            break;
                        }
                    }

                    if (hasNumber && val != 0m)
                    {
                        _lastDiffIndex = idx;
                        txtLog.SelectionStart = lineStart;
                        txtLog.SelectionLength = Math.Max(1, lineEnd - lineStart);
                        txtLog.ScrollToCaret();
                        txtLog.Focus();
                        return;
                    }

                    // nächstes Vorkommen suchen
                    idx = text.IndexOf("Kassendifferenz", idx + 1, StringComparison.OrdinalIgnoreCase);
                }

                // Wrap-Around, wenn wir nicht von 0 gestartet sind
                if (start > 0)
                {
                    _lastDiffIndex = -1;
                    JumpToNextDiff();
                }
                else
                {
                    System.Media.SystemSounds.Beep.Play();
                }
            }
            catch { }
        }
    }
}
