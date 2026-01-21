using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Threading;
using System.Linq; // NEU für OrderBy
using System.Runtime.InteropServices;

namespace TaMi_Einzahlautomat
{
    public class LogViewForm : Form
    {
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
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
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

            // Log-Textbox zuerst hinzufügen (damit Dock Fill nicht vom Header überlagert wird)
            txtLog = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                Dock = DockStyle.Fill,
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
            Controls.Add(btnClose);

            // Moderner Header: TableLayoutPanel mit flexibler Suche-Spalte
            var top = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 52,
                Padding = new Padding(6),
                ColumnCount = 7,
                RowCount = 1
            };
            top.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260F)); // Datei-Auswahl
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));       // Reload
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));       // DIF
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));  // Suche
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));       // Suchen
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));       // Weiter
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));       // Aa
            Controls.Add(top);

            _cmbFiles = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                IntegralHeight = false,
                MaxDropDownItems = 12,
                Dock = DockStyle.Fill
            };
            _cmbFiles.DropDownHeight = 260;
            _cmbFiles.SelectedIndexChanged += (s, e) => OnSelectedFileChanged();
            _cmbFiles.Margin = new Padding(0, 6, 8, 6);
            top.Controls.Add(_cmbFiles, 0, 0);

            btnReload = new Button { Text = "Aktualisieren", AutoSize = true };
            btnReload.Margin = new Padding(0, 6, 6, 6);
            btnReload.Click += (s, e) => LoadLog();
            top.Controls.Add(btnReload, 1, 0);

            _btnDiff = new Button { Text = "DIF", AutoSize = true };
            _btnDiff.Margin = new Padding(0, 6, 6, 6);
            _btnDiff.Click += (s, e) => JumpToNextDiff();
            top.Controls.Add(_btnDiff, 2, 0);

            _txtSearch = new TextBox { Dock = DockStyle.Fill };
            _txtSearch.Margin = new Padding(0, 6, 6, 6);
            _txtSearch.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.Handled = true; e.SuppressKeyPress = true; DoFind(true); } };
            top.Controls.Add(_txtSearch, 3, 0);
            SetCueBanner(_txtSearch, "Suchen … (F3=Weiter, Strg+F=Fokus)");

            _btnFind = new Button { Text = "Suchen", AutoSize = true };
            _btnFind.Margin = new Padding(0, 6, 6, 6);
            _btnFind.Click += (s, e) => DoFind(true);
            top.Controls.Add(_btnFind, 4, 0);

            _btnNext = new Button { Text = "Weiter", AutoSize = true };
            _btnNext.Margin = new Padding(0, 6, 6, 6);
            _btnNext.Click += (s, e) => DoFind(false);
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
