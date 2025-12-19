using System;
using System.IO;
using System.Text;
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
        private ComboBox _cmbFiles; // NEU: Dateiauswahl
        private string _logFilePath;
        private string _logDir;

        public LogViewForm()
        {
            Text = "Log anzeigen";
            StartPosition = FormStartPosition.CenterParent;
            Size = new System.Drawing.Size(900, 700);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            // Container Panel oben für Auswahl + Reload
            var topPanel = new Panel { Dock = DockStyle.Top, Height = 50, Padding = new Padding(6) };
            Controls.Add(topPanel);

            _cmbFiles = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 380,
                Left = 6,
                Top = 12,
                Font = new System.Drawing.Font("Segoe UI", 10F)
            };
            _cmbFiles.SelectedIndexChanged += (s, e) => OnSelectedFileChanged();
            topPanel.Controls.Add(_cmbFiles);

            btnReload = new Button
            {
                Text = "Aktualisieren",
                Left = _cmbFiles.Right + 10,
                Top = 10,
                Width = 120,
                Height = 30
            };
            btnReload.Click += (s, e) => LoadLog();
            topPanel.Controls.Add(btnReload);

            btnClose = new Button
            {
                Text = "Schließen",
                Dock = DockStyle.Bottom,
                Height = 36
            };
            btnClose.Click += (s, e) => Close();
            Controls.Add(btnClose);

            txtLog = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                Dock = DockStyle.Fill,
                Font = new System.Drawing.Font("Consolas", 10F),
                WordWrap = false
            };
            Controls.Add(txtLog);

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
                    .OrderByDescending(fi => fi.Name) // yyyy-MM-dd.log sortierbar
                    .Take(30)
                    .ToList();
                foreach (var fi in files)
                    _cmbFiles.Items.Add(fi.Name);
                if (files.Count > 0)
                {
                    // heutige bevorzugt selektieren, sonst erste
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
            }
            catch (Exception ex)
            {
                txtLog.Text = $"Fehler beim Laden der Logdatei:\r\n{ex.Message}";
            }
        }
    }
}
