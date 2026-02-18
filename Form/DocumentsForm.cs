using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Drawing.Imaging;
using TaMi_Einzahlautomat.UI.Layout;

namespace TaMi_Einzahlautomat
{
    public class DocumentsForm : Form
    {
        private readonly PersonalInfo _personal;

        // Header
        private ModernHeaderPanel _header;
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

        public DocumentsForm(PersonalInfo personal)
        {
            _personal = personal ?? throw new ArgumentNullException(nameof(personal));
            BuildUi();
            try { AppLogger.Log($"Dokumente ge�ffnet: PID={_personal.PID}, Name={_personal.Vorname} {_personal.Name}"); } catch { }
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

            _header = new ModernHeaderPanel
            {
                Title = "Dokumente",
                ShowMinimize = false
            };
            _header.CloseClicked += () => { try { Close(); } catch { } };
            Controls.Add(_header);

            try
            {
                _header.ApplyRoundedRegionToForm(this);
                SizeChanged += (s, e) => { try { _header.ApplyRoundedRegionToForm(this); } catch { } };
            }
            catch { }

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
            _btnClosePreview = new ModernGradientButton { Text = "Schließen", Dock = DockStyle.Right, Width = 140, GradientStart = UiTheme.DangerStart, GradientEnd = UiTheme.DangerEnd, TabStop = false };
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
                        // Fallback: extern �ffnen
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
            var b = new ModernGradientButton
            {
                Text = text,
                AutoSize = true,
                Height = 40,
                Tag = targetPath,
                Font = new Font("Segoe UI Variable", 12F, FontStyle.Bold)
            };
            b.Click += (s, e) => NavigateTo((string)((Button)s).Tag);
            return b;
        }

        private Button MakeFolderButton(string text, string targetPath, bool big)
        {
            var b = new ModernGradientButton
            {
                Text = text,
                Width = big ? 260 : 200,
                Height = big ? 64 : 56,
                Margin = new Padding(8),
                Tag = targetPath,
                Font = new Font("Segoe UI Variable", big ? 16F : 14F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            };
            // neutral (unselected)
            b.GradientStart = Color.White;
            b.GradientEnd = Color.White;
            b.ForeColor = Color.FromArgb(33, 33, 33);
            b.Click += (s, e) => NavigateTo((string)((Button)s).Tag);
            return b;
        }

        private void HighlightButton(Button b, bool selected)
        {
            try
            {
                var mg = b as ModernGradientButton;
                if (mg != null)
                {
                    if (selected)
                    {
                        mg.GradientStart = UiTheme.PrimaryStart;
                        mg.GradientEnd = UiTheme.PrimaryEnd;
                        mg.ForeColor = Color.White;
                    }
                    else
                    {
                        mg.GradientStart = Color.White;
                        mg.GradientEnd = Color.White;
                        mg.ForeColor = Color.FromArgb(33, 33, 33);
                    }
                    mg.Invalidate();
                    return;
                }

                if (selected) { b.BackColor = UiTheme.PrimaryStart; b.ForeColor = Color.White; }
                else { b.BackColor = Color.White; b.ForeColor = Color.FromArgb(33, 33, 33); }
            }
            catch { }
        }

        private Control MakeDocTile(string filePath)
        {
            var pnl = new Panel { Width = 240, Height = 200, Margin = new Padding(10), BackColor = Color.White, Tag = filePath };
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

            var btnOpen = new ModernGradientButton { Text = "Öffnen", Location = new Point(12, 110), Size = new Size(104, 36), GradientStart = UiTheme.PrimaryStart, GradientEnd = UiTheme.PrimaryEnd, Tag = filePath, Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold), TabStop = false };
            btnOpen.Click += (s, e) => OpenPath((string)((Button)s).Tag);
            pnl.Controls.Add(btnOpen);
            var btnPrint = new ModernGradientButton { Text = "Drucken", Location = new Point(124, 110), Size = new Size(104, 36), GradientStart = UiTheme.SuccessStart, GradientEnd = UiTheme.SuccessEnd, Tag = filePath, Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold), TabStop = false };
            btnPrint.Click += (s, e) => PrintPath((string)((Button)s).Tag);
            try { var disableDocs = IniHelper.ReadValue("UI", "DisableDocumentsPrint", AppSettings.IniPath); if (!string.IsNullOrWhiteSpace(disableDocs) && (disableDocs.Equals("1") || disableDocs.Equals("true", StringComparison.OrdinalIgnoreCase))) btnPrint.Visible = false; } catch { }
            pnl.Controls.Add(btnPrint);

            var btnMail = new ModernGradientButton { Text = "per Mail", Location = new Point(12, 152), Size = new Size(216, 36), GradientStart = Color.FromArgb(255, 167, 38), GradientEnd = Color.FromArgb(245, 124, 0), Tag = filePath, Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold), TabStop = false };
            btnMail.Click += (s, e) => SendDocumentByEmail((string)((Button)s).Tag);
            pnl.Controls.Add(btnMail);

            pnl.Cursor = Cursors.Hand;
            pnl.Click += (s, e) => OpenPath((string)pnl.Tag);
            foreach (Control c in pnl.Controls) c.Click += (s, e) => { };

            return pnl;
        }

        private void SendDocumentByEmail(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
                string employeeMail = null;
                try { employeeMail = _personal?.EMail; } catch { employeeMail = null; }
                var mailCfg = MailSettings.Load();
                if (string.IsNullOrWhiteSpace(employeeMail)) { MessageBox.Show(this, "Keine Mitarbeiter-E-Mail hinterlegt.", "Mail", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                if (mailCfg == null || !mailCfg.IsConfigured) { MessageBox.Show(this, "Maileinstellungen sind nicht konfiguriert.", "Mail", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                if (!ShowMailConsentDialog(employeeMail)) return;

                try { AppLogger.Log($"Dokumente: E-Mail-Versand gestartet � Datei='{Path.GetFileName(path)}' an '{employeeMail}'"); } catch { }
                Cursor prev = Cursor.Current; Cursor.Current = Cursors.WaitCursor;
                try
                {
                    using (var msg = new System.Net.Mail.MailMessage())
                    {
                        var from = new System.Net.Mail.MailAddress(mailCfg.FromAddress, mailCfg.FromDisplayName);
                        msg.From = from;
                        msg.To.Add(new System.Net.Mail.MailAddress(employeeMail));
                        msg.Subject = "Dokument vom Geldautomat";
                        msg.Body = "Sie erhalten das angeforderte Dokument als Anhang. Bitte gehen Sie sorgsam mit personenbezogenen Daten um.";
                        msg.IsBodyHtml = false;
                        var att = new System.Net.Mail.Attachment(path);
                        msg.Attachments.Add(att);

                        using (var client = new System.Net.Mail.SmtpClient(mailCfg.SmtpHost, mailCfg.SmtpPort))
                        {
                            client.EnableSsl = mailCfg.EnableSsl;
                            if (!string.IsNullOrWhiteSpace(mailCfg.Username)) client.Credentials = new System.Net.NetworkCredential(mailCfg.Username, mailCfg.Password);
                            else client.UseDefaultCredentials = true;
                            client.Send(msg);
                        }
                    }
                    try { AppLogger.Log($"Dokumente: E-Mail gesendet � Datei='{Path.GetFileName(path)}' an '{employeeMail}'"); } catch { }
                    MessageBox.Show(this, "E-Mail wurde gesendet.", "Mail", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "E-Mail Versand fehlgeschlagen:\r\n" + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally { Cursor.Current = prev; }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Unerwarteter Fehler:\r\n" + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private bool ShowMailConsentDialog(string email)
        {
            try
            {
                var dlg = new Form
                {
                    FormBorderStyle = FormBorderStyle.None,
                    StartPosition = FormStartPosition.CenterParent,
                    Width = 720,
                    Height = 380,
                    BackColor = Color.White
                };
                try
                {
                    using (var gp = new GraphicsPath())
                    {
                        int radius = 16;
                        var rect = new Rectangle(0, 0, dlg.Width, dlg.Height);
                        int d = radius * 2;
                        gp.AddArc(rect.X, rect.Y, d, d, 180, 90);
                        gp.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
                        gp.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                        gp.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
                        gp.CloseFigure();
                        dlg.Region = new Region(gp);
                    }
                }
                catch { }

                // Subtle border around the dialog
                try
                {
                    dlg.Padding = new Padding(1);
                    dlg.Paint += (s, e) =>
                    {
                        var rect = dlg.ClientRectangle;
                        using (var pen = new Pen(Color.FromArgb(210, 210, 210)))
                        {
                            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                            e.Graphics.DrawRectangle(pen, new Rectangle(0, 0, rect.Width - 1, rect.Height - 1));
                        }
                    };
                }
                catch { }

                // Header
                var header = new Panel { Dock = DockStyle.Top, Height = 60 };
                header.Paint += (s, e) =>
                {
                    using (var brush = new LinearGradientBrush(header.ClientRectangle, Color.FromArgb(33, 150, 243), Color.FromArgb(33, 203, 243), 0f))
                    {
                        e.Graphics.FillRectangle(brush, header.ClientRectangle);
                    }
                };
                dlg.Controls.Add(header);
                var title = new Label
                {
                    Text = "Dokument per E-Mail",
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Font = new Font("Segoe UI Variable", 18F, FontStyle.Bold),
                    ForeColor = Color.White,
                    Dock = DockStyle.Fill,
                    Padding = new Padding(16, 0, 0, 0),
                    BackColor = Color.Transparent
                };
                header.Controls.Add(title);

                // Read optional top offset from INI for easier tweaking (pixels)
                int topOffset = 40;
                try
                {
                    var raw = IniHelper.ReadValue("UI", "MailConsentBodyTopOffset", AppSettings.IniPath);
                    int v; if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out v)) topOffset = Math.Max(0, Math.Min(400, v));
                }
                catch { }

                // Body under header, ensures content starts below header
                var body = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(20, 20 + topOffset, 20, 20), AutoScroll = true };
                dlg.Controls.Add(body);

                var lblMail = new Label { Text = "Empf�nger: " + email, AutoSize = true, Font = new Font("Segoe UI", 12.5F), Dock = DockStyle.Top, Padding = new Padding(0, 0, 0, 10) };
                body.Controls.Add(lblMail);

                int calcWidth() { return Math.Max(320, body.ClientSize.Width - body.Padding.Horizontal); }
                var info = new Label
                {
                    Text = "Hinweis: Der Versand per E-Mail kann Datenschutzrisiken bergen (Weiterleitung, ungesicherte Postf�cher). Ich bin einverstanden, dass mir das Dokument an die oben angezeigte Adresse zugesendet wird.",
                    AutoSize = true,
                    MaximumSize = new Size(640, 0),
                    Font = new Font("Segoe UI", 11.5F),
                    Dock = DockStyle.Top,
                    Padding = new Padding(0, 0, 0, 10)
                };
                body.Controls.Add(info);

                body.Resize += (s, e) =>
                {
                    try { info.MaximumSize = new Size(calcWidth(), 0); } catch { }
                };
                try { info.MaximumSize = new Size(calcWidth(), 0); } catch { }

                var spacer = new Panel { Dock = DockStyle.Top, Height = 6 }; body.Controls.Add(spacer);

                // Buttons bottom, large like previous design
                var panelButtons = new Panel { Dock = DockStyle.Bottom, Height = 96, BackColor = Color.White };
                dlg.Controls.Add(panelButtons);

                var btnOk = new Button
                {
                    Text = "Senden",
                    Width = 180,
                    Height = 56,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(76, 175, 80),
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI Variable", 16F, FontStyle.Bold),
                    TabStop = false
                };
                btnOk.FlatAppearance.BorderSize = 0;
                try
                {
                    using (var gp = new GraphicsPath())
                    {
                        int radius = 12;
                        var rect = new Rectangle(0, 0, btnOk.Width, btnOk.Height);
                        int d = radius * 2;
                        gp.AddArc(rect.X, rect.Y, d, d, 180, 90);
                        gp.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
                        gp.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                        gp.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
                        gp.CloseFigure();
                        btnOk.Region = new Region(gp);
                    }
                }
                catch { }

                var btnCancel = new Button
                {
                    Text = "Abbrechen",
                    Width = 180,
                    Height = 56,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(229, 57, 53),
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI Variable", 16F, FontStyle.Bold),
                    TabStop = false
                };
                btnCancel.FlatAppearance.BorderSize = 0;
                try
                {
                    using (var gp = new GraphicsPath())
                    {
                        int radius = 12;
                        var rect = new Rectangle(0, 0, btnCancel.Width, btnCancel.Height);
                        int d = radius * 2;
                        gp.AddArc(rect.X, rect.Y, d, d, 180, 90);
                        gp.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
                        gp.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                        gp.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
                        gp.CloseFigure();
                        btnCancel.Region = new Region(gp);
                    }
                }
                catch { }

                btnOk.Click += (s, e) => { dlg.Tag = true; dlg.Close(); };
                btnCancel.Click += (s, e) => { dlg.Tag = false; dlg.Close(); };

                panelButtons.Resize += (s, e) =>
                {
                    int spacing = 20;
                    int total = btnOk.Width + btnCancel.Width + spacing;
                    int startX = (panelButtons.ClientSize.Width - total) / 2;
                    int y = (panelButtons.ClientSize.Height - btnOk.Height) / 2;
                    btnOk.Location = new Point(Math.Max(10, startX), y);
                    btnCancel.Location = new Point(btnOk.Right + spacing, y);
                };
                panelButtons.Controls.Add(btnOk);
                panelButtons.Controls.Add(btnCancel);

                bool result = false; try { dlg.ShowDialog(this); } catch { dlg.ShowDialog(); }
                try { result = (dlg.Tag is bool b) ? b : false; } catch { result = false; }
                try { dlg.Dispose(); } catch { }
                return result;
            }
            catch { return false; }
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
            return s.Substring(0, Math.Max(0, max - 1)) + "�";
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
                if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) return; // nicht �ber Root
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
                MessageBox.Show(this, "�ffnen fehlgeschlagen: " + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenPath(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                try { AppLogger.Log($"Dokument ge�ffnet: '{Path.GetFileName(path)}'"); } catch { }
                if (IsPdf(path) || IsImage(path))
                {
                    ShowPreview(path);
                    return;
                }
                OpenExternal(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "�ffnen fehlgeschlagen: " + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void PrintPath(string path
        ){
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
                string printer = string.Empty;
                try { printer = IniHelper.ReadValue("UI", "DocumentPrinter", AppSettings.IniPath) ?? string.Empty; } catch { }

                // Validate explicit printer once (optional)
                if (!string.IsNullOrWhiteSpace(printer))
                {
                    try
                    {
                        var ps = new PrinterSettings { PrinterName = printer };
                        if (!ps.IsValid)
                        {
                            MessageBox.Show(this, "Der konfigurierte Drucker ist ung�ltig oder nicht erreichbar: " + printer, "Drucker", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }
                    }
                    catch { }
                }

                if (IsImage(path))
                {
                    var ok = PrintImageFile(path, printer);
                    if (ok) TryNotifySpoolQueued();
                    else MessageBox.Show(this, "Bild konnte nicht gedruckt werden.", "Druckfehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (IsPdf(path))
                {
                    // Require SumatraPDF and use it exclusively for PDF
                    bool started = TrySumatraSilentPrint(path, printer);
                    if (started)
                    {
                        TryNotifySpoolQueued();
                        return;
                    }
                    // Sumatra not found or failed -> show prompt and do not claim success
                    ShowSumatraDownloadPrompt();
                    return;
                }

                // Unknown types are not supported
                MessageBox.Show(this, "Dieser Dateityp wird zum Drucken nicht unterst�tzt.", "Druckfehler", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Drucken fehlgeschlagen: " + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ShowSumatraDownloadPrompt()
        {
            try
            {
                var dlg = new Form
                {
                    FormBorderStyle = FormBorderStyle.None,
                    StartPosition = FormStartPosition.CenterParent,
                    Width = 560,
                    Height = 220,
                    BackColor = Color.White
                };
                try
                {
                    using (var gp = new GraphicsPath())
                    {
                        int radius = 14;
                        var rect = new Rectangle(0, 0, dlg.Width, dlg.Height);
                        int d = radius * 2;
                        gp.AddArc(rect.X, rect.Y, d, d, 180, 90);
                        gp.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
                        gp.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                        gp.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
                        gp.CloseFigure();
                        dlg.Region = new Region(gp);
                    }
                }
                catch { }

                var header = new Panel { Dock = DockStyle.Top, Height = 56 };
                header.Paint += (s, e) =>
                {
                    using (var brush = new LinearGradientBrush(header.ClientRectangle, Color.FromArgb(33, 150, 243), Color.FromArgb(33, 203, 243), 0f))
                    { e.Graphics.FillRectangle(brush, header.ClientRectangle); }
                };
                dlg.Controls.Add(header);

                var title = new Label
                {
                    Text = "PDF-Drucker ben�tigt",
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Font = new Font("Segoe UI Variable", 18F, FontStyle.Bold),
                    ForeColor = Color.White,
                    Location = new Point(16, 0),
                    Size = new Size(420, 56)
                };
                header.Controls.Add(title);

                var body = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
                dlg.Controls.Add(body);
                var info = new Label
                {
                    Text = "F�r den direkten PDF-Druck wird SumatraPDF ben�tigt.\r\nKlicken Sie auf 'Herunterladen', um die offizielle Download-Seite zu �ffnen.",
                    AutoSize = false,
                    Location = new Point(16, 20),
                    Size = new Size(520, 60),
                    Font = new Font("Segoe UI", 11.5F)
                };
                body.Controls.Add(info);

                var panelButtons = new Panel { Dock = DockStyle.Bottom, Height = 84 };
                body.Controls.Add(panelButtons);

                var btnDownload = new Button
                {
                    Text = "Herunterladen",
                    Width = 150,
                    Height = 44,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(76, 175, 80),
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI Variable", 14F, FontStyle.Bold),
                    TabStop = false
                };
                btnDownload.FlatAppearance.BorderSize = 0;
                try
                {
                    using (var gp = new GraphicsPath())
                    {
                        int radius = 12;
                        var rect = new Rectangle(0, 0, btnDownload.Width, btnDownload.Height);
                        int d = radius * 2;
                        gp.AddArc(rect.X, rect.Y, d, d, 180, 90);
                        gp.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
                        gp.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                        gp.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
                        gp.CloseFigure();
                        btnDownload.Region = new Region(gp);
                    }
                }
                catch { }
                btnDownload.Click += (s, e) =>
                {
                    try
                    {
                        // Official download page
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "https://www.sumatrapdfreader.org/free-pdf-reader.html",
                            UseShellExecute = true
                        });
                    }
                    catch { }
                    try { dlg.Close(); } catch { }
                };

                var btnClose = new Button
                {
                    Text = "Schlie�en",
                    Width = 120,
                    Height = 44,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(229, 57, 53),
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI Variable", 14F, FontStyle.Bold),
                    TabStop = false
                };
                btnClose.FlatAppearance.BorderSize = 0;
                try
                {
                    using (var gp = new GraphicsPath())
                    {
                        int radius = 12;
                        var rect = new Rectangle(0, 0, btnClose.Width, btnClose.Height);
                        int d = radius * 2;
                        gp.AddArc(rect.X, rect.Y, d, d, 180, 90);
                        gp.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
                        gp.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                        gp.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
                        gp.CloseFigure();
                        btnClose.Region = new Region(gp);
                    }
                }
                catch { }
                btnClose.Click += (s, e) => { try { dlg.Close(); } catch { } };

                int spacing = 16;
                int totalWidth = btnDownload.Width + btnClose.Width + spacing;
                int startX = (dlg.ClientSize.Width - totalWidth) / 2;
                int y = 20;
                btnDownload.Location = new Point(startX, y);
                btnClose.Location = new Point(btnDownload.Right + spacing, y);
                panelButtons.Controls.Add(btnDownload);
                panelButtons.Controls.Add(btnClose);

                try { dlg.ShowDialog(this); } catch { dlg.ShowDialog(); }
            }
            catch { }
        }

        private static bool PrintImageFile(string path, string printer)
        {
            try
            {
                byte[] data = File.ReadAllBytes(path);
                var ms = new MemoryStream(data);
                var img = Image.FromStream(ms);
                int frameCount = 1;
                try { var dimCheck = new FrameDimension(img.FrameDimensionsList[0]); frameCount = img.GetFrameCount(dimCheck); } catch { frameCount = 1; }

                var pd = new PrintDocument();
                if (!string.IsNullOrWhiteSpace(printer)) pd.PrinterSettings.PrinterName = printer; else { try { pd.PrinterSettings.PrinterName = new PrinterSettings().PrinterName; } catch { } }
                if (string.IsNullOrWhiteSpace(pd.PrinterSettings.PrinterName)) { img.Dispose(); ms.Dispose(); return false; }
                if (!pd.PrinterSettings.IsValid) { img.Dispose(); ms.Dispose(); return false; }
                pd.DocumentName = Path.GetFileName(path);
                int pageIndex = 0;
                pd.PrintPage += (s, e) =>
                {
                    try
                    {
                        if (frameCount > 1)
                        {
                            var dim = new FrameDimension(img.FrameDimensionsList[0]);
                            img.SelectActiveFrame(dim, pageIndex);
                        }
                        Rectangle bounds = e.MarginBounds;
                        if ((img.Width > img.Height) && bounds.Height > bounds.Width)
                        {
                            var tmp = bounds; bounds = new Rectangle(tmp.Left, tmp.Top, tmp.Height, tmp.Width);
                        }
                        float ratio = Math.Min((float)bounds.Width / img.Width, (float)bounds.Height / img.Height);
                        int w = (int)(img.Width * ratio); int h = (int)(img.Height * ratio);
                        int x = bounds.Left + (bounds.Width - w) / 2; int y = bounds.Top + (bounds.Height - h) / 2;
                        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        e.Graphics.DrawImage(img, new Rectangle(x, y, w, h));
                        pageIndex++;
                        e.HasMorePages = pageIndex < frameCount;
                    }
                    catch { e.HasMorePages = false; }
                };
                pd.EndPrint += (s, e) => { try { img.Dispose(); } catch { } try { ms.Dispose(); } catch { } };
                pd.Print();
                return true;
            }
            catch { return false; }
        }

        private bool TryShellPrint(string path, string printer)
        {
            try
            {
                Process proc;
                if (string.IsNullOrWhiteSpace(printer))
                {
                    var psi = new ProcessStartInfo(path)
                    {
                        Verb = "print",
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        UseShellExecute = true
                    };
                    proc = Process.Start(psi);
                }
                else
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = path,
                        Verb = "printto",
                        Arguments = '"' + printer + '"',
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        UseShellExecute = true
                    };
                    proc = Process.Start(psi);
                }
                bool started = proc != null;
                try { System.Threading.Thread.Sleep(400); } catch { }
                try { proc?.Dispose(); } catch { }
                return started;
            }
            catch { return false; }
        }

        private bool TryAcrobatSilentPrint(string path, string printer)
        {
            try
            {
                var acroPath = FindAcrobatPath();
                if (string.IsNullOrEmpty(acroPath) || !File.Exists(acroPath)) return false;
                var args = string.IsNullOrWhiteSpace(printer)
                    ? $"/p \"{path}\""
                    : $"/t \"{path}\" \"{printer}\"";
                var psi = new ProcessStartInfo
                {
                    FileName = acroPath,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                var p = Process.Start(psi);
                bool started = p != null;
                try { System.Threading.Thread.Sleep(600); } catch { }
                try { p?.Dispose(); } catch { }
                return started;
            }
            catch { return false; }
        }

        private string FindAcrobatPath()
        {
            try
            {
                string[] candidates = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Adobe", "Acrobat Reader DC", "Reader", "AcroRd32.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Adobe", "Acrobat Reader DC", "Reader", "AcroRd32.exe")
                };
                foreach (var c in candidates) { if (File.Exists(c)) return c; }
                try
                {
                    using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\App Paths\\AcroRd32.exe"))
                    {
                        var v = k?.GetValue(null) as string; if (!string.IsNullOrWhiteSpace(v) && File.Exists(v)) return v;
                    }
                }
                catch { }
            }
            catch { }
            return null;
        }

        private bool TrySumatraSilentPrint(string path, string printer)
        {
            try
            {
                string baseDir = Application.StartupPath;
                // Prefer Sumatra in subfolder 'PDF' next to Geldautomat.exe
                string preferred = Path.Combine(baseDir, "PDF", "SumatraPDF.exe");
                string[] candidates = new[]
                {
                    preferred,
                    Path.Combine(baseDir, "SumatraPDF.exe"),
                    Path.Combine(baseDir, "Ressourcen", "SumatraPDF.exe")
                };
                string exe = candidates.FirstOrDefault(File.Exists);
                if (string.IsNullOrEmpty(exe)) return false;

                string absFile = Path.GetFullPath(path);
                string args = string.IsNullOrWhiteSpace(printer)
                    ? $"-silent -print-to-default \"{absFile}\""
                    : $"-silent -print-to \"{printer}\" \"{absFile}\"";
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetDirectoryName(exe) ?? baseDir
                };
                var p = Process.Start(psi);
                bool started = p != null;
                try { System.Threading.Thread.Sleep(600); } catch { }
                try { p?.Dispose(); } catch { }
                return started;
            }
            catch { return false; }
        }

        private void TryNotifySpoolQueued()
        {
            try { MessageBox.Show(this, "Druckauftrag gesendet.", "Drucken", MessageBoxButtons.OK, MessageBoxIcon.Information); } catch { }
        }
    }
}
