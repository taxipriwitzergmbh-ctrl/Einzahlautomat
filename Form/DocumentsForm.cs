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

        // Preview overlay
        private Panel _previewOverlay;
        private Panel _previewHeader;
        private Button _btnClosePreview;
        private WebBrowser _pdfViewer;
        private Panel _imgScrollPanel;
        private PictureBox _imgViewer;
        private Timer _previewTimer;
        private string _currentPreviewPath;

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
            var btnHeaderClose = new Button { Text = "\u2715", Font = new Font("Segue UI Symbol", 18F, FontStyle.Bold), ForeColor = Color.White, BackColor = Color.Transparent, FlatStyle = FlatStyle.Flat, Size = new Size(48, 48), Location = new Point(ClientSize.Width - 56, 8), Anchor = AnchorStyles.Top | AnchorStyles.Right };
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

            // Preview overlay (initial hidden)
            _previewOverlay = new Panel { Visible = false, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            Controls.Add(_previewOverlay);
            _previewOverlay.BringToFront();

            _previewHeader = new Panel { Height = 48, Dock = DockStyle.Top };
            _previewHeader.Paint += (s, e) =>
            {
                using (var brush = new LinearGradientBrush(_previewHeader.ClientRectangle, Color.FromArgb(33, 150, 243), Color.FromArgb(33, 203, 243), 0f))
                { e.Graphics.FillRectangle(brush, _previewHeader.ClientRectangle); }
            };
            _previewOverlay.Controls.Add(_previewHeader);
            _btnClosePreview = new Button { Text = "Schließen", Dock = DockStyle.Right, Width = 120, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(229,57,53), ForeColor = Color.White };
            _btnClosePreview.FlatAppearance.BorderSize = 0;
            _btnClosePreview.Click += (s, e) => HidePreview();
            _previewHeader.Controls.Add(_btnClosePreview);

            _pdfViewer = new WebBrowser { Dock = DockStyle.Fill, AllowWebBrowserDrop = false, IsWebBrowserContextMenuEnabled = false, ScriptErrorsSuppressed = true, Visible = false, AllowNavigation = true };
            _pdfViewer.DocumentCompleted += (s, e) => { try { _previewTimer?.Stop(); } catch { } };
            _previewOverlay.Controls.Add(_pdfViewer);

            _imgScrollPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Visible = false, BackColor = Color.Black };
            _previewOverlay.Controls.Add(_imgScrollPanel);
            _imgViewer = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Location = new Point(0, 0) };
            _imgScrollPanel.Controls.Add(_imgViewer);

            _previewTimer = new Timer { Interval = 600 };
            _previewTimer.Tick += (s, e) =>
            {
                try
                {
                    _previewTimer.Stop();
                    bool loaded = false;
                    try { loaded = _pdfViewer.ReadyState == WebBrowserReadyState.Complete && _pdfViewer.Document != null; } catch { loaded = false; }
                    if (!loaded && !string.IsNullOrEmpty(_currentPreviewPath))
                    {
                        // Fallback: extern öffnen
                        HidePreview();
                        OpenExternal(_currentPreviewPath);
                    }
                }
                catch { }
            };

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

                _previewOverlay.Location = new Point(margin, _header.Bottom + 6);
                _previewOverlay.Size = new Size(width, ClientSize.Height - _header.Bottom - 12);
                if (_previewOverlay.Visible) _previewOverlay.BringToFront();
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
                _breadcrumbPanel.Controls.Add(new Label { Text = "Aktueller Pfad:", AutoSize = true, Padding = new Padding(6, 10, 12, 0), Font = new Font("Segoe UI", 11F, FontStyle.Bold), ForeColor = Color.FromArgb(33,33,33) });
                string rel = MakeRelativePath(_currentPath, _root);
                var parts = string.IsNullOrEmpty(rel) ? new string[0] : rel.Split(new[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
                var btnRoot = MakeNavButton(Path.GetFileName(_root).Length > 0 ? Path.GetFileName(_root) : _root, _root);
                _breadcrumbPanel.Controls.Add(btnRoot);
                string running = _root;
                foreach (var p in parts)
                {
                    running = Path.Combine(running, p);
                    _breadcrumbPanel.Controls.Add(new Label { Text = "/", AutoSize = true, Padding = new Padding(12, 6, 12, 0), Font = new Font("Segoe UI", 16F, FontStyle.Bold), ForeColor = Color.Gray });
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

                // Auf Root-Ebene (depth==0) keine Unterordner-Buttons anzeigen, um Duplikate zu vermeiden
                if (depth == 0)
                {
                    return;
                }

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

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            var lblIcon = new Label { Text = (ext == ".pdf" ? "PDF" : (ext == ".png" || ext == ".jpg" || ext == ".jpeg" ? "IMG" : "TIF")), AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Location = new Point(12, 12), Size = new Size(56, 56), BackColor = Color.FromArgb(240, 240, 240), ForeColor = Color.FromArgb(66, 66, 66), Font = new Font("Segoe UI", 14F, FontStyle.Bold) };
            pnl.Controls.Add(lblIcon);

            var title = ExtractYearMonthTitle(filePath);
            var lblTitle = new Label { Text = title, AutoSize = false, Location = new Point(80, 12), Size = new Size(148, 32), Font = new Font("Segoe UI", 14.5F, FontStyle.Bold) };
            pnl.Controls.Add(lblTitle);

            var fileName = Path.GetFileName(filePath);
            var lblName = new Label { Text = Truncate(fileName, 28), AutoSize = false, Location = new Point(80, 46), Size = new Size(148, 22), Font = new Font("Segoe UI", 10.5F), ForeColor = Color.DimGray };
            pnl.Controls.Add(lblName);

            var dt = SafeGetWriteTime(filePath);
            var lblDate = new Label { Text = dt.ToString("dd.MM.yyyy HH:mm"), AutoSize = false, Location = new Point(12, 80), Size = new Size(216, 22), Font = new Font("Segoe UI", 10F), ForeColor = Color.Gray };
            pnl.Controls.Add(lblDate);

            var btnOpen = new Button { Text = "Öffnen", Location = new Point(12, 110), Size = new Size(104, 36), BackColor = Color.FromArgb(33, 150, 243), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Tag = filePath, Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold) };
            btnOpen.FlatAppearance.BorderSize = 0; btnOpen.Click += (s, e) => OpenPath((string)((Button)s).Tag);
            pnl.Controls.Add(btnOpen);
            var btnPrint = new Button { Text = "Drucken", Location = new Point(124, 110), Size = new Size(104, 36), BackColor = Color.FromArgb(76, 175, 80), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Tag = filePath, Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold) };
            btnPrint.FlatAppearance.BorderSize = 0; btnPrint.Click += (s, e) => PrintPath((string)((Button)s).Tag);
            pnl.Controls.Add(btnPrint);

            pnl.Cursor = Cursors.Hand;
            pnl.Click += (s, e) => OpenPath((string)pnl.Tag);
            foreach (Control c in pnl.Controls) c.Click += (s, e) => { };

            return pnl;
        }

        private string ExtractYearMonthTitle(string filePath)
        {
            try
            {
                var name = Path.GetFileNameWithoutExtension(filePath);
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
                HidePreview();
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
                    .Where(p => p.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
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

        private static DateTime SafeGetWriteTime(string f)
        {
            try { return File.GetLastWriteTime(f); } catch { return DateTime.MinValue; }
        }

        private bool IsImage(string path)
        {
            var ext = Path.GetExtension(path)?.ToLowerInvariant();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".tif" || ext == ".tiff";
        }

        private bool IsPdf(string path)
        {
            var ext = Path.GetExtension(path)?.ToLowerInvariant();
            return ext == ".pdf";
        }

        private void ShowPreview(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                _pdfViewer.Visible = false;
                _imgScrollPanel.Visible = false;

                if (IsPdf(path))
                {
                    _pdfViewer.Visible = true;
                    try
                    {
                        _currentPreviewPath = path;
                        // Render via HTML embed to improve compatibility
                        var fileUri = new Uri(path).AbsoluteUri;
                        _pdfViewer.DocumentText = $"<html><body style='margin:0;padding:0;background:#fff;'><embed src='{fileUri}' type='application/pdf' width='100%' height='100%'/></body></html>";
                        _previewTimer.Stop();
                        _previewTimer.Start();
                    }
                    catch { _pdfViewer.Visible = false; }
                }
                else if (IsImage(path))
                {
                    _imgScrollPanel.Visible = true;
                    try
                    {
                        // Dispose previous image to avoid file locks
                        if (_imgViewer.Image != null)
                        {
                            var old = _imgViewer.Image; _imgViewer.Image = null; try { old.Dispose(); } catch { }
                        }
                        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            _imgViewer.Image = Image.FromStream(fs);
                        }
                        // Fit image within scroll panel by setting size to panel size; Zoom handles aspect
                        _imgViewer.Dock = DockStyle.Fill;
                    }
                    catch { _imgScrollPanel.Visible = false; }
                }
                else
                {
                    // unsupported -> fallback external
                    OpenExternal(path);
                    return;
                }

                _previewOverlay.Visible = true;
                _previewOverlay.BringToFront();
            }
            catch { }
        }

        private void HidePreview()
        {
            try
            {
                _previewOverlay.Visible = false;
                try { _previewTimer?.Stop(); } catch { }
                try
                {
                    if (_imgViewer.Image != null)
                    {
                        var old = _imgViewer.Image; _imgViewer.Image = null; old.Dispose();
                    }
                }
                catch { }
            }
            catch { }
        }

        private void OpenExternal(string path)
        {
            try
            {
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

        private void OpenPath(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                if (IsPdf(path) || IsImage(path))
                {
                    ShowPreview(path);
                    return;
                }
                OpenExternal(path);
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
                var printer = string.Empty;
                try { printer = IniHelper.ReadValue("UI", "DocumentPrinter", AppSettings.IniPath) ?? string.Empty; } catch { }
                ProcessStartInfo psi;
                if (!string.IsNullOrWhiteSpace(printer))
                {
                    psi = new ProcessStartInfo
                    {
                        FileName = path,
                        Verb = "printto",
                        Arguments = '"' + printer + '"',
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        UseShellExecute = true
                    };
                }
                else
                {
                    psi = new ProcessStartInfo(path)
                    {
                        Verb = "print",
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        UseShellExecute = true
                    };
                }
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
