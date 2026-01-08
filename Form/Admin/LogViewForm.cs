using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Threading;
using System.Linq; // NEU für OrderBy

namespace Geldautomat
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

        public LogViewForm()
        {
            Text = "Log anzeigen";
            StartPosition = FormStartPosition.CenterParent;
            Size = new System.Drawing.Size(1100, 720); // etwas breiter
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            // Log-Textbox zuerst hinzufügen (damit Dock Fill nicht vom Header überlagert wird)
            txtLog = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                Dock = DockStyle.Fill,
                Font = new System.Drawing.Font("Consolas", 10F),
                WordWrap = false,
                HideSelection = false
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

            // Oberer Header (schmaler)
            var topPanel = new Panel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(6) };
            Controls.Add(topPanel);

            _cmbFiles = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 240, // kleineres Auswahlfeld
                Left = 6,
                Top = 10,
                Font = new System.Drawing.Font("Segoe UI", 9.5F),
                IntegralHeight = false
            };
            _cmbFiles.MaxDropDownItems = 12;
            _cmbFiles.DropDownHeight = 260;
            _cmbFiles.SelectedIndexChanged += (s, e) => OnSelectedFileChanged();
            topPanel.Controls.Add(_cmbFiles);

            btnReload = new Button
            {
                Text = "Aktualisieren",
                Left = _cmbFiles.Right + 8,
                Top = 8,
                Width = 110,
                Height = 30
            };
            btnReload.Click += (s, e) => LoadLog();
            topPanel.Controls.Add(btnReload);

            // DIF-Button
            _btnDiff = new Button
            {
                Text = "DIF",
                Left = btnReload.Right + 6,
                Top = 8,
                Width = 56,
                Height = 30
            };
            _btnDiff.Click += (s, e) => JumpToNextDiff();
            topPanel.Controls.Add(_btnDiff);

            _txtSearch = new TextBox
            {
                Left = _btnDiff.Right + 8,
                Top = 10,
                Width = 200
            };
            _txtSearch.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.Handled = true; e.SuppressKeyPress = true; DoFind(true); } };
            topPanel.Controls.Add(_txtSearch);

            _btnFind = new Button
            {
                Text = "Suchen",
                Left = _txtSearch.Right + 6,
                Top = 8,
                Width = 72,
                Height = 30
            };
            _btnFind.Click += (s, e) => DoFind(true);
            topPanel.Controls.Add(_btnFind);

            _btnNext = new Button
            {
                Text = "Weiter",
                Left = _btnFind.Right + 6,
                Top = 8,
                Width = 64,
                Height = 30
            };
            _btnNext.Click += (s, e) => DoFind(false);
            topPanel.Controls.Add(_btnNext);

            _chkCase = new CheckBox
            {
                Text = "Aa",
                Left = _btnNext.Right + 6,
                Top = 13,
                Width = 36
            };
            _chkCase.CheckedChanged += (s, e) => { _lastFindIndex = -1; _lastFindTerm = null; };
            topPanel.Controls.Add(_chkCase);

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
