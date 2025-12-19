using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace Geldautomat
{
    public class DocumentsForm : Form
    {
        private readonly PersonalInfo _personal;

        // Header
        private Panel _header;
        private Label _title;
        private Label _lblInfo;

        // Navigation UI
        private FlowLayoutPanel _breadcrumbPanel;
        private FlowLayoutPanel _rootFoldersPanel;   // Oberste Ebene (z.B. Lohnabrechnungen / Allgemeine Dateien)
        private FlowLayoutPanel _subFoldersPanel;    // Unterordner des aktuellen Pfads
        private FlowLayoutPanel _docsPanel;          // Dokument-Kacheln

        private string _root;
        private string _currentPath;

        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_RESTORE = 9;
        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

        public DocumentsForm(PersonalInfo personal)
        {
            _personal = personal ?? throw new ArgumentNullException(nameof(personal));
            BuildUi();
            _root = GetDocStoreRoot();
            if (string.IsNullOrWhiteSpace(_root))
            {
                MessageBox.Show(this, "DocStore-Pfad ist nicht konfiguriert.", "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _root = GetDocumentsRootFallback();
            }
            if (string.IsNullOrWhiteSpace(_root)) _root = Application.StartupPath;
            _currentPath = _root;
            RefreshAllUi();
        }

        private void BuildUi()
        {
            Text = "Dokumente";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1280, 1024); // wie AbrechnungForm
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.White;
            DoubleBuffered = true;

            // Header modern
            _header = new Panel { Location = new Point(0, 0), Size = new Size(ClientSize.Width, 64), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            _header.Paint += (s, e) =>
            {
                using (var brush = new LinearGradientBrush(_header.ClientRectangle, Color.FromArgb(33, 150, 243), Color.FromArgb(33, 203, 243), 0f))
                { e.Graphics.FillRectangle(brush, _header.ClientRectangle); }
            };
            Controls.Add(_header);
            _title = new Label { Text = "Dokumente", AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Variable", 22F, FontStyle.Bold), ForeColor = Color.White, Location = new Point(20, 0), Size = new Size(700, 64), BackColor = Color.Transparent };
            _header.Controls.Add(_title);
            var btnHeaderClose = new Button { Text = "\u2715", Font = new Font("Segoe UI Symbol", 18F, FontStyle.Bold), ForeColor = Color.White, BackColor = Color.Transparent, FlatStyle = FlatStyle.Flat, Size = new Size(48, 48), Location = new Point(ClientSize.Width - 56, 8), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnHeaderClose.FlatAppearance.BorderSize = 0; btnHeaderClose.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 80, 80); btnHeaderClose.Click += (s, e) => Close();
            _header.Controls.Add(btnHeaderClose);

            try { Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, Width, Height, 20, 20)); } catch { }

            _lblInfo = new Label { Text = $"Mitarbeiter: {_personal.Vorname} {_personal.Name}", AutoSize = true, Font = new Font("Segoe UI", 12F, FontStyle.Bold) };
            Controls.Add(_lblInfo);

            // Breadcrumb
            _breadcrumbPanel = new FlowLayoutPanel { AutoScroll = true, WrapContents = false, BackColor = Color.White };
            Controls.Add(_breadcrumbPanel);

            // Top-Level Folder Buttons (z.B. Lohnabrechnungen / Allgemeine Dateien)
            _rootFoldersPanel = new FlowLayoutPanel { AutoScroll = true, WrapContents = true, BackColor = Color.White };
            Controls.Add(_rootFoldersPanel);

            // Sub-Folder Buttons
            _subFoldersPanel = new FlowLayoutPanel { AutoScroll = true, WrapContents = true, BackColor = Color.White };
            Controls.Add(_subFoldersPanel);

            // Document Tiles area
            _docsPanel = new FlowLayoutPanel { AutoScroll = true, WrapContents = true };
            Controls.Add(_docsPanel);

            // initial layout
            Relayout();
            this.Resize += (s, e) => Relayout();
        }

        private void Relayout()
        {
            try
            {
                int margin = 20;
                int x = margin;
                int y = _header.Bottom + 10;
                int width = ClientSize.Width - margin * 2;

                _lblInfo.Location = new Point(x, y);
                _lblInfo.Size = new Size(width, _lblInfo.Height);

                y = _lblInfo.Bottom + 10;
                _breadcrumbPanel.Location = new Point(x, y);
                _breadcrumbPanel.Size = new Size(width, 44);

                y = _breadcrumbPanel.Bottom + 8;
                _rootFoldersPanel.Location = new Point(x, y);
                _rootFoldersPanel.Size = new Size(width, 90);

                y = _rootFoldersPanel.Bottom + 8;
                _subFoldersPanel.Location = new Point(x, y);
                _subFoldersPanel.Size = new Size(width, 120);

                y = _subFoldersPanel.Bottom + 8;
                _docsPanel.Location = new Point(x, y);
                _docsPanel.Size = new Size(width, ClientSize.Height - y - margin);

                _breadcrumbPanel.BringToFront();
                _rootFoldersPanel.BringToFront();
                _subFoldersPanel.BringToFront();
                _docsPanel.BringToFront();
            }
            catch { }
        }

        private void RefreshAllUi()
        {
            RefreshBreadcrumb();
            BuildRootFolderButtons();
            BuildSubFolderButtons();
            BuildDocumentTiles();
            Relayout();
        }

        private void RefreshBreadcrumb()
        {
            try
            {
                _breadcrumbPanel.Controls.Clear();
                string rel = MakeRelativePath(_currentPath, _root);
                var parts = string.IsNullOrEmpty(rel) ? new string[0] : rel.Split(new[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
                var btnRoot = MakeNavButton(Path.GetFileName(_root).Length > 0 ? Path.GetFileName(_root) : _root, _root);
                _breadcrumbPanel.Controls.Add(btnRoot);
                string running = _root;
                foreach (var p in parts)
                {
                    running = Path.Combine(running, p);
                    _breadcrumbPanel.Controls.Add(new Label { Text = "/", AutoSize = true, Padding = new Padding(6, 10, 6, 0), ForeColor = Color.Gray });
                    _breadcrumbPanel.Controls.Add(MakeNavButton(p, running));
                }
            }
            catch { }
        }

        private void BuildRootFolderButtons()
        {
            try
            {
                _rootFoldersPanel.Controls.Clear();
                if (!Directory.Exists(_root)) return;
                var roots = SafeGetDirectories(_root).OrderBy(d => d).ToArray();
                if (roots.Length == 0)
                {
                    _rootFoldersPanel.Controls.Add(new Label { Text = "Keine Ordner im Stamm vorhanden.", AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(8) });
                }
                foreach (var dir in roots)
                {
                    var di = new DirectoryInfo(dir);
                    var b = MakeFolderButton(di.Name, dir, big: true);
                    // Hervorheben, wenn aktueller Pfad darunter liegt
                    try
                    {
                        string full = Path.GetFullPath(_currentPath);
                        string dfull = Path.GetFullPath(dir);
                        if (full.StartsWith(dfull, StringComparison.OrdinalIgnoreCase)) HighlightButton(b, true);
                    }
                    catch { }
                    _rootFoldersPanel.Controls.Add(b);
                }
                _rootFoldersPanel.Refresh();
                _rootFoldersPanel.PerformLayout();
            }
            catch { }
        }

        private void BuildSubFolderButtons()
        {
            try
            {
                _subFoldersPanel.Controls.Clear();
                if (!Directory.Exists(_currentPath)) return;

                // Ermitteln, ob wir in einer Kategorie (depth=1) oder tiefer (>=2) sind
                int depth = 0;
                try
                {
                    var rel = MakeRelativePath(_currentPath, _root);
                    if (!string.IsNullOrEmpty(rel)) depth = rel.Split(new[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries).Length;
                }
                catch { depth = 0; }

                string listBase = _currentPath;
                if (depth >= 2)
                {
                    try
                    {
                        var parent = Directory.GetParent(_currentPath);
                        if (parent != null) listBase = parent.FullName;
                    }
                    catch { listBase = _currentPath; }
                }

                var subs = SafeGetDirectories(listBase).OrderBy(d => d).ToArray();
                if (subs.Length == 0)
                {
                    _subFoldersPanel.Controls.Add(new Label { Text = "Keine Unterordner.", AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(8) });
                }
                foreach (var dir in subs)
                {
                    var di = new DirectoryInfo(dir);
                    var b = MakeFolderButton(di.Name, dir, big: false);
                    // Mark current selection when showing siblings
                    try
                    {
                        string curFull = Path.GetFullPath(_currentPath);
                        string bFull = Path.GetFullPath(dir);
                        if (curFull.Equals(bFull, StringComparison.OrdinalIgnoreCase)) HighlightButton(b, true);
                    }
                    catch { }
                    _subFoldersPanel.Controls.Add(b);
                }
                _subFoldersPanel.Refresh();
                _subFoldersPanel.PerformLayout();
            }
            catch { }
        }

        private void BuildDocumentTiles()
        {
            try
            {
                _docsPanel.SuspendLayout();
                _docsPanel.Controls.Clear();

                // Tiefe relativ zum Root bestimmen: Root -> 0, Kategorie -> 1, Unterordner -> >=2
                int depth = 0;
                try
                {
                    var rel = MakeRelativePath(_currentPath, _root);
                    if (!string.IsNullOrEmpty(rel)) depth = rel.Split(new[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries).Length;
                }
                catch { depth = 0; }

                // Erst ab Unterordner-Ebene (>=2) Dateien anzeigen
                if (depth >= 2)
                {
                    var files = GetFilesForCurrentFolder();
                    foreach (var f in files.OrderByDescending(p => SafeGetWriteTime(p)))
                    {
                        _docsPanel.Controls.Add(MakeDocTile(f));
                    }
                }
            }
            catch { }
            finally { try { _docsPanel.ResumeLayout(); } catch { } }
        }

        private Button MakeNavButton(string text, string targetPath)
        {
            var b = new Button
            {
                Text = text,
                AutoSize = true,
                Height = 40,
                BackColor = Color.FromArgb(33, 150, 243),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Tag = targetPath,
                Font = new Font("Segoe UI Variable", 12F, FontStyle.Bold)
            };
            b.FlatAppearance.BorderSize = 0;
            b.Click += (s, e) => NavigateTo((string)((Button)s).Tag);
            return b;
        }

        private Button MakeFolderButton(string text, string targetPath, bool big)
        {
            var b = new Button
            {
                Text = text,
                Width = big ? 260 : 200,
                Height = big ? 64 : 56,
                Margin = new Padding(8),
                BackColor = Color.White,
                ForeColor = Color.FromArgb(33, 33, 33),
                FlatStyle = FlatStyle.Flat,
                Tag = targetPath,
                Font = new Font("Segoe UI Variable", big ? 16F : 14F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
            b.FlatAppearance.BorderSize = 1;
            b.Click += (s, e) => NavigateTo((string)((Button)s).Tag);
            return b;
        }

        private void HighlightButton(Button b, bool selected)
        {
            try
            {
                if (selected)
                {
                    b.BackColor = Color.FromArgb(33, 150, 243);
                    b.ForeColor = Color.White;
                    b.FlatAppearance.BorderColor = Color.FromArgb(33, 150, 243);
                }
                else
                {
                    b.BackColor = Color.White;
                    b.ForeColor = Color.FromArgb(33, 33, 33);
                    b.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
                }
            }
            catch { }
        }

        private Control MakeDocTile(string filePath)
        {
            var pnl = new Panel { Width = 240, Height = 160, Margin = new Padding(10), BackColor = Color.White, Tag = filePath };
            pnl.Paint += (s, e) => { try { e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; using (var pen = new Pen(Color.FromArgb(210, 210, 210))) e.Graphics.DrawRectangle(pen, new Rectangle(0, 0, pnl.Width - 1, pnl.Height - 1)); } catch { } };

            // Dokument-Icon (einfaches Rechteck mit „PDF/TIF“)
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            var lblIcon = new Label { Text = ext == ".pdf" ? "PDF" : "TIF", AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Location = new Point(12, 12), Size = new Size(56, 56), BackColor = Color.FromArgb(240, 240, 240), ForeColor = Color.FromArgb(66, 66, 66), Font = new Font("Segoe UI", 14F, FontStyle.Bold) };
            pnl.Controls.Add(lblIcon);

            // Titel (Jahr/Monat)
            var title = ExtractYearMonthTitle(filePath);
            var lblTitle = new Label { Text = title, AutoSize = false, Location = new Point(80, 12), Size = new Size(148, 32), Font = new Font("Segoe UI", 14.5F, FontStyle.Bold) };
            pnl.Controls.Add(lblTitle);

            // Untertitel (Dateiname verkürzt)
            var fileName = Path.GetFileName(filePath);
            var lblName = new Label { Text = Truncate(fileName, 28), AutoSize = false, Location = new Point(80, 46), Size = new Size(148, 22), Font = new Font("Segoe UI", 10.5F), ForeColor = Color.DimGray };
            pnl.Controls.Add(lblName);

            // Zeitstempel
            var dt = SafeGetWriteTime(filePath);
            var lblDate = new Label { Text = dt.ToString("dd.MM.yyyy HH:mm"), AutoSize = false, Location = new Point(12, 80), Size = new Size(216, 22), Font = new Font("Segoe UI", 10F), ForeColor = Color.Gray };
            pnl.Controls.Add(lblDate);

            // Buttons: Öffnen (links) | Drucken (rechts)
            var btnOpen = new Button { Text = "Öffnen", Location = new Point(12, 110), Size = new Size(104, 36), BackColor = Color.FromArgb(33, 150, 243), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Tag = filePath, Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold) };
            btnOpen.FlatAppearance.BorderSize = 0; btnOpen.Click += (s, e) => OpenPath((string)((Button)s).Tag);
            pnl.Controls.Add(btnOpen);
            var btnPrint = new Button { Text = "Drucken", Location = new Point(124, 110), Size = new Size(104, 36), BackColor = Color.FromArgb(76, 175, 80), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Tag = filePath, Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold) };
            btnPrint.FlatAppearance.BorderSize = 0; btnPrint.Click += (s, e) => PrintPath((string)((Button)s).Tag);
            pnl.Controls.Add(btnPrint);

            // Kachelklick öffnet ebenfalls
            pnl.Cursor = Cursors.Hand;
            pnl.Click += (s, e) => OpenPath((string)pnl.Tag);
            foreach (Control c in pnl.Controls) c.Click += (s, e) => { /* absorb child clicks if needed */ };

            return pnl;
        }

        private string ExtractYearMonthTitle(string filePath)
        {
            try
            {
                var name = Path.GetFileNameWithoutExtension(filePath);
                // DATEV: lobn_YYYYMM_...
                var parts = name.Split('_');
                if (parts.Length >= 2 && parts[0].Equals("lobn", StringComparison.OrdinalIgnoreCase) && parts[1].Length == 6)
                {
                    string y = parts[1].Substring(0, 4);
                    string m = parts[1].Substring(4, 2);
                    return $"{m}/{y}";
                }
            }
            catch { }
            var dt = SafeGetWriteTime(filePath);
            return dt.ToString("MM/yyyy");
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
            return s.Substring(0, Math.Max(0, max - 1)) + "…";
        }

        private string GetDocStoreRoot()
        {
            try
            {
                var root = IniHelper.ReadValue("UI", "DocStore", AppSettings.IniPath);
                if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root)) return root.Trim();
            }
            catch { }
            return null;
        }

        private string GetDocumentsRootFallback()
        {
            try
            {
                var alt = Path.Combine(Application.StartupPath, "Ressourcen", "Dokumente");
                if (Directory.Exists(alt)) return alt;
            }
            catch { }
            return null;
        }

        private void NavigateTo(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                string full = Path.GetFullPath(path);
                string rootFull = Path.GetFullPath(_root);
                if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) return; // nicht über Root
                if (!Directory.Exists(full)) return;
                _currentPath = full;
                RefreshAllUi();
            }
            catch { }
        }

        private static string MakeRelativePath(string path, string root)
        {
            try
            {
                var full = Path.GetFullPath(path);
                var r = Path.GetFullPath(root);
                if (full.StartsWith(r, StringComparison.OrdinalIgnoreCase))
                {
                    var rel = full.Substring(r.Length).TrimStart(Path.DirectorySeparatorChar);
                    return rel;
                }
            }
            catch { }
            return string.Empty;
        }

        private static string[] SafeGetDirectories(string basePath)
        {
            try { return Directory.GetDirectories(basePath); } catch { return new string[0]; }
        }

        private string[] GetFilesForCurrentFolder()
        {
            var basePath = _currentPath;
            if (string.IsNullOrWhiteSpace(basePath) || !Directory.Exists(basePath)) return new string[0];
            try
            {
                var files = Directory.EnumerateFiles(basePath, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(p => p.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                int pid = _personal != null ? _personal.PID : 0;
                files = files.Where(f => IsFileForEmployeeByLastToken(f, pid)).ToArray();
                return files;
            }
            catch { return new string[0]; }
        }

        private static bool IsFileForEmployeeByLastToken(string filePath, int pid)
        {
            try
            {
                var name = Path.GetFileNameWithoutExtension(filePath);
                var parts = (name ?? string.Empty).Split('_');
                if (parts.Length == 0) return false;
                string last = parts[parts.Length - 1];
                if (last == null) return false;
                last = last.Trim();
                // führende Nullen entfernen
                last = last.TrimStart('0');
                if (last.Length == 0) last = "0";
                int filePid;
                if (!int.TryParse(last, out filePid)) return false;
                return filePid == pid;
            }
            catch { return false; }
        }

        private bool IsPathUnderLohnabrechnungen(string path)
        {
            try
            {
                var candidate = Path.GetFullPath(path);
                var target = Path.Combine(_root, "Lohnabrechnungen");
                target = Path.GetFullPath(target);
                return candidate.StartsWith(target, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static bool IsDatevPayslipForEmployee(string filePath, string pidStr)
        {
            try
            {
                var name = Path.GetFileNameWithoutExtension(filePath);
                var parts = name.Split('_');
                if (parts.Length >= 5)
                {
                    var personalNr = parts[parts.Length - 1];
                    if (!string.IsNullOrEmpty(pidStr) && string.Equals(personalNr, pidStr, StringComparison.OrdinalIgnoreCase)) return true;
                }
                var nn = NormalizeText(name);
                if (!string.IsNullOrEmpty(pidStr) && nn.Contains(pidStr)) return true;
            }
            catch { }
            return false;
        }

        private static string NormalizeText(string s)
        {
            s = s ?? string.Empty;
            s = s.Trim().ToLowerInvariant();
            s = s.Replace(" ", "");
            s = s.Replace("-", "");
            s = s.Replace("_", "");
            s = s.Replace(".", "");
            return s;
        }

        private static DateTime SafeGetWriteTime(string f)
        {
            try { return File.GetLastWriteTime(f); } catch { return DateTime.MinValue; }
        }

        private void OpenPath(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                var psi = new ProcessStartInfo(path) { UseShellExecute = true };
                var proc = Process.Start(psi);
                try
                {
                    System.Threading.Thread.Sleep(300);
                    if (proc != null)
                    {
                        proc.Refresh();
                        var h = proc.MainWindowHandle;
                        if (h != IntPtr.Zero)
                        {
                            ShowWindow(h, SW_RESTORE);
                            SetForegroundWindow(h);
                        }
                    }
                }
                catch { }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Öffnen fehlgeschlagen: " + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void PrintPath(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                var psi = new ProcessStartInfo(path)
                {
                    Verb = "print",
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = true
                };
                Process.Start(psi);
                try { MessageBox.Show(this, "Druckauftrag gesendet.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information); } catch { }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Drucken fehlgeschlagen: " + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
