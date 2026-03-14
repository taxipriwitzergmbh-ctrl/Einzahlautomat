using System;
using System.Collections.Generic; // Am Anfang ergänzen
using System.Data;                // Für DataTable
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq; // neu für LINQ
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.IO;
using System.Threading.Tasks;
using TaMi_Einzahlautomat.Coins;
using System.Reflection; // für DoubleBuffered-Reflektion
using TaMi_Einzahlautomat.Printing;
using TaMi_Einzahlautomat.Devices; // for CoinFeederProtocolMode
using System.Drawing.Imaging; // NEU für ColorMatrix (transparenter Hintergrund)
using TaMi_Einzahlautomat.UI.Layout;

namespace TaMi_Einzahlautomat
{
    public partial class AbrechnungForm : Form
    {
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            try
            {
                if (keyData == (Keys.Control | Keys.M))
                {
                    try
                    {
                        if (nudManuell != null && btnManuellAdd != null)
                        {
                            bool vis = !nudManuell.Visible;
                            nudManuell.Visible = vis;
                            btnManuellAdd.Visible = vis;
                            try { UpdateBuchenEnabled(); } catch { }
                        }
                    }
                    catch { }
                    return true;
                }
                if (keyData == (Keys.Control | Keys.A))
                {
                    try { if (btnAdmin != null) btnAdmin.Visible = !btnAdmin.Visible; } catch { }
                    return true;
                }
            }
            catch { }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        private sealed class AbrechnenSurfacePanel : Panel
        {
            protected override void OnPaintBackground(PaintEventArgs e)
            {
                try
                {
                    if (FindForm() is AbrechnungForm f)
                    {
                        var s = e.Graphics.Save();
                        try
                        {
                            // map panel coords to form coords
                            var p = PointToScreen(Point.Empty);
                            var pf = f.PointToClient(p);
                            e.Graphics.TranslateTransform(-pf.X, -pf.Y);
                            f.InvokePaintBackground(f, new PaintEventArgs(e.Graphics, f.ClientRectangle));
                        }
                        finally { e.Graphics.Restore(s); }
                        return;
                    }
                }
                catch { }
                base.OnPaintBackground(e);
            }
        }

        private Panel _cardAbrechnen;
        private Region _cardAbrechnenRegion;
        private Label _abrechnenCardTitle;
        private Panel _abrechnenInnerPanel;
        private Region _abrechnenInnerRegion;
        private int _abrechnenCardShadowSize = 10;
        private bool _abrechnenTabPaintAttached = false;

        private void EnsureAbrechnenTabPainting()
        {
            if (_abrechnenTabPaintAttached) return;
            _abrechnenTabPaintAttached = true;
            try
            {
                tabAbrechnen.Paint += (s, e) =>
                {
                    try
                    {
                        // Keine zusätzliche Fläche malen: das Form zeichnet bereits das Hintergrundbild.
                        // Die Card zeichnet ihren eigenen semi-transparenten Hintergrund.
                    }
                    catch { }
                };
            }
            catch { }
        }

        private void ApplyModernButtonStyle(Button b, Color c1, Color c2)
        {
            if (b == null) return;
            try
            {
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 0;
                b.BackColor = Color.Transparent;
                b.UseVisualStyleBackColor = false;
                b.ForeColor = Color.White;
                b.Paint -= ModernButton_Paint;
                b.Paint += ModernButton_Paint;
                b.Tag = new Tuple<Color, Color>(c1, c2);
                try { b.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, b.Width, b.Height, 12, 12)); } catch { }
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
                try { b.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, b.Width, b.Height, 12, 12)); } catch { }
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
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var rect = b.ClientRectangle;
                rect.Width -= 1;
                rect.Height -= 1;

                var colors = b.Tag as Tuple<Color, Color>;
                var c1 = colors != null ? colors.Item1 : Color.FromArgb(46, 125, 50);
                var c2 = colors != null ? colors.Item2 : Color.FromArgb(27, 94, 32);

                using (var br = new LinearGradientBrush(rect, c1, c2, 90f))
                {
                    e.Graphics.FillRectangle(br, rect);
                }
                using (var pen = new Pen(Color.FromArgb(110, 255, 255, 255), 1f))
                {
                    e.Graphics.DrawRectangle(pen, rect);
                }
                TextRenderer.DrawText(e.Graphics, b.Text, b.Font, b.ClientRectangle, b.ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            catch { }
        }

        // Global zugängliche aktuelle Personal-ID (für andere Dialoge wie CreatePaymentForm)
        public static int CurrentPersonalId { get; private set; }
        // Singleton-Helper: nur eine Instanz zulassen
        public static AbrechnungForm FindOpenInstance()
        {
            try
            {
                foreach (Form f in Application.OpenForms)
                {
                    if (f is AbrechnungForm af)
                        return af;
                }
            }
            catch { }
            return null;
        }

        public static AbrechnungForm ShowOrActivate(PersonalInfo personal, ShiftDetails details, NV200_SSP ssp, bool isAdmin = false)
        {
            var existing = FindOpenInstance();
            if (existing != null)
            {
                try
                {
                    if (existing.WindowState == FormWindowState.Minimized)
                        existing.WindowState = FormWindowState.Normal;
                    existing.BringToFront();
                    existing.Activate();
                    existing.Focus();
                }
                catch { }
                return existing;
            }

            var frm = new AbrechnungForm(personal, details, ssp, isAdmin);
            try
            {
                // Im Kioskmodus: Owner auf Hintergrund setzen und TopMost aktivieren
                if (Program.KioskModeEnabled && Program.BackgroundFormInstance != null)
                {
                    frm.StartPosition = FormStartPosition.Manual;
                    // zentrieren relativ zum Background
                    try
                    {
                        var bg = Program.BackgroundFormInstance.Bounds;
                        frm.Location = new Point(
                            bg.Left + (bg.Width - frm.Width) / 2,
                            bg.Top + (bg.Height - frm.Height) / 2);
                    }
                    catch { }
                    frm.TopMost = true; // darüber halten
                    frm.Show(Program.BackgroundFormInstance); // Owner setzen
                }
                else
                {
                    frm.Show();
                }
            }
            catch { }
            return frm;
        }

        // NEU: Überladung mit vorab geladenem Personalguthaben (verhindert doppelte DB-Abfrage)
        public static AbrechnungForm ShowOrActivate(PersonalInfo personal, ShiftDetails details, NV200_SSP ssp, decimal preloadedGuthaben, bool isAdmin = false)
        {
            var existing = FindOpenInstance();
            if (existing != null)
            {
                try
                {
                    if (existing.WindowState == FormWindowState.Minimized)
                        existing.WindowState = FormWindowState.Normal;
                    existing.BringToFront();
                    existing.Activate();
                    existing.Focus();
                }
                catch { }
                return existing;
            }
            var frm = new AbrechnungForm(personal, details, ssp, preloadedGuthaben, isAdmin);
            try
            {
                if (Program.KioskModeEnabled && Program.BackgroundFormInstance != null)
                {
                    frm.StartPosition = FormStartPosition.Manual;
                    // zentrieren relativ zum Background
                    try
                    {
                        var bg = Program.BackgroundFormInstance.Bounds;
                        frm.Location = new Point(
                            bg.Left + (bg.Width - frm.Width) / 2,
                            bg.Top + (bg.Height - frm.Height) / 2);
                    }
                    catch { }
                    frm.TopMost = true;
                    frm.Show(Program.BackgroundFormInstance);
                }
                else
                {
                    frm.Show();
                }
            }
            catch { }
            return frm;
        }

        private ShiftDetails _details;
        private decimal _eingezahltSession;
        private NV200_SSP _ssp;
        private ICoinValidator _coin; // via CoinManager
        private ICoinValidator _coin2 = null; // zweites Gerät
        private bool _coin2EventsAttached = false;

        private bool _eventsAttached = false;
        private Button btnAdmin;
        private Button btnDocuments; // NEU: Dokumente
        private int[] scheinWerte = { 5, 10, 20, 50, 100, 200, 500 };
        private int[] auswahlAnzahl = new int[7];
        private string[] scheinBilder = { "5euro.jpg", "10euro.jpg", "20euro.jpg", "50euro.jpg", "100euro.jpg", "200euro.jpg", "500euro.jpg" };

        private PictureBox[] picScheine = new PictureBox[7];
        private Button[] btnPlus = new Button[7];
        private Button[] btnMinus = new Button[7];
        private Label[] lblAnzahl = new Label[7];
        private Label[] lblVerfuegbar = new Label[7];
        private Label lblSummeAuszahlung;
        private Button btnAuszahlen;
        private Timer _tmrAvail;

        private Panel headerPanel;
        private Label lblTitle;
        private Point _mouseDownLocation;

        // zentraler UI-Style (einmalig) für diese Form
        private bool _uiStyleApplied = false;

        private TabControl tabControl;
        private TabPage tabAbrechnen;
        private TabPage tabWechseln;

        private Panel _abrechnenSurface;
        private Panel _wechselnSurface;
        private sealed class BackgroundInheritingTabControl : TabControl
        {
            private const int TCM_ADJUSTRECT = 0x1328;

            [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
            private struct RECT
            {
                public int Left;
                public int Top;
                public int Right;
                public int Bottom;
            }

            protected override CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    // remove the default client edge border (the visible rectangle around tab pages)
                    const int WS_EX_CLIENTEDGE = 0x00000200;
                    cp.ExStyle &= ~WS_EX_CLIENTEDGE;
                    // also remove the normal border which can still draw a 1px frame
                    const int WS_BORDER = 0x00800000;
                    cp.Style &= ~WS_BORDER;
                    return cp;
                }
            }

            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);

                try
                {
                    if (m.Msg == TCM_ADJUSTRECT && m.LParam != IntPtr.Zero)
                    {
                        // Expand the display rectangle slightly so the native control doesn't paint
                        // an inset frame around the tab pages.
                        var rc = (RECT)System.Runtime.InteropServices.Marshal.PtrToStructure(m.LParam, typeof(RECT));
                        rc.Left -= 4;
                        rc.Top -= 2;
                        rc.Right += 4;
                        rc.Bottom += 4;
                        System.Runtime.InteropServices.Marshal.StructureToPtr(rc, m.LParam, true);
                    }
                }
                catch { }
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                try
                {
                    if (FindForm() is AbrechnungForm f)
                    {
                        var s = e.Graphics.Save();
                        try
                        {
                            var p = PointToScreen(Point.Empty);
                            var pf = f.PointToClient(p);
                            e.Graphics.TranslateTransform(-pf.X, -pf.Y);
                            f.InvokePaintBackground(f, new PaintEventArgs(e.Graphics, f.ClientRectangle));
                        }
                        finally { e.Graphics.Restore(s); }
                        return;
                    }
                }
                catch { }
                base.OnPaintBackground(e);
            }
        }

        private void ApplyCentralDesign()
        {
            if (_uiStyleApplied) return;
            _uiStyleApplied = true;

            // Header nach zentralem Theme (Gradient via `ModernHeaderPanel`/`UiTheme`)
            try
            {
                if (headerPanel != null && !(headerPanel is ModernHeaderPanel))
                {
                    var old = headerPanel;
                    var modern = new ModernHeaderPanel
                    {
                        Location = old.Location,
                        Size = old.Size,
                        Anchor = old.Anchor
                    };

                    // Titel IN den Header übernehmen (falls vorhanden)
                    try { modern.Title = lblTitle != null ? (lblTitle.Text ?? string.Empty) : string.Empty; } catch { }

                    // Close/Minimize an Form binden
                    try
                    {
                        modern.CloseClicked += () => { try { Close(); } catch { } };
                        modern.MinimizeClicked += () => { try { WindowState = FormWindowState.Minimized; } catch { } };
                    }
                    catch { }

                    // Vorhandene Header-Controls übernehmen (Buttons etc.)
                    try
                    {
                        var toMove = new Control[old.Controls.Count];
                        old.Controls.CopyTo(toMove, 0);
                        old.Controls.Clear();
                        modern.Controls.AddRange(toMove);
                    }
                    catch { }

                    // alte Title-Label entfernen, da `ModernHeaderPanel` eigenes Title-Label hat
                    try
                    {
                        if (lblTitle != null)
                        {
                            try { modern.Controls.Remove(lblTitle); } catch { }
                            try { lblTitle.Dispose(); } catch { }
                            lblTitle = null;
                        }
                    }
                    catch { }

                    int idx = Controls.GetChildIndex(old);
                    Controls.Remove(old);
                    try { old.Dispose(); } catch { }
                    Controls.Add(modern);
                    Controls.SetChildIndex(modern, idx);
                    headerPanel = modern;

                    // In diesem Screen kein "X" im Header: Abmelden-Button übernimmt die Funktion.
                    try
                    {
                        for (int i = modern.Controls.Count - 1; i >= 0; i--)
                        {
                            var b = modern.Controls[i] as ModernGradientButton;
                            if (b == null) continue;
                            if (string.Equals(b.Text, "\u2715", StringComparison.Ordinal) || string.Equals(b.Text, "✕", StringComparison.Ordinal))
                            {
                                modern.Controls.RemoveAt(i);
                                try { b.Dispose(); } catch { }
                                break;
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            // Buttons nach Theme einfärben
            try
            {
                if (btnAbmelden != null)
                {
                    ApplyModernButtonStyle(btnAbmelden, UiTheme.DangerStart, UiTheme.DangerEnd);
                    btnAbmelden.FlatAppearance.MouseOverBackColor = Color.Transparent;
                }

                if (btnAdmin != null)
                {
                    ApplyModernButtonStyle(btnAdmin, UiTheme.PrimaryStart, UiTheme.PrimaryEnd);
                    btnAdmin.FlatAppearance.MouseOverBackColor = Color.Transparent;
                }

                if (btnDocuments != null)
                {
                    ApplyModernButtonStyle(btnDocuments, UiTheme.SuccessStart, UiTheme.SuccessEnd);
                    btnDocuments.FlatAppearance.MouseOverBackColor = Color.Transparent;
                }
            }
            catch { }
        }

        private Label lblTitel, lblGuthaben, lblB19, lblB7, lblB0, lblSumme, lblEingezahlt, lblNoch;

        // Abrechnen-Tab: ausgerichtete Summen (Text links / Betrag rechts)
        private Panel pnlSummeRow;
        private Panel pnlEingezahltRow;
        private Panel pnlNochRow;
        private Label lblSummeText, lblEingezahltText, lblNochText;
        private Label lblSummeValue, lblEingezahltValue, lblNochValue;
        private Panel pnlSummeSeparator;

        private Button btnAbrechnen, btnAbmelden, btnManuellAdd;
        private Button btnCreatePayment;
        private NumericUpDown nudManuell;

        private readonly PersonalInfo _personal;

        private Button btnSchichtAuswahl;
        private decimal _personalGuthaben = 0m;

        // Laufender Auszahlstatus (reduziert – ungenutzte Felder entfernt)
        private decimal _geplanteAuszahlung = 0m;
        private bool _currentZahlungIstEinzahlung = false;
        private Label lblMaxVerfuegbar;

        // Anzahl aktiver Scheingeräte während Auszahlung
        private int _activeNotePayoutDevices = 0;

        // Münzen
        private int[] muenzWerte = { 1, 2, 5, 10, 20, 50, 100, 200 }; // Cent
        private int[] auswahlAnzahlMuenzen = new int[8];
        private string[] muenzBilder = { "1cent.jpg", "2cent.jpg", "5cent.jpg", "10cent.jpg", "20cent.jpg", "50cent.jpg", "1euro.jpg", "2euro.jpg" };
        private PictureBox[] picMuenzen = new PictureBox[8];
        private Button[] btnPlusMuenzen = new Button[8];
        private Button[] btnMinusMuenzen = new Button[8];
        private Label[] lblAnzahlMuenzen = new Label[8];
        private Label[] lblVerfuegbarMuenzen = new Label[8];

        private bool _coinEventsAttached = false;
        private int[] _coinAvail = new int[8] { -1, -1, -1, -1, -1, -1, -1, -1 };
        private int[] _coin2Avail = new int[8] { -1, -1, -1, -1, -1, -1, -1, -1 };
        private DateTime _lastCoinLevelsRequestUtc = DateTime.MinValue;
        private DateTime _lastPayoutLevelsScheduleUtc = DateTime.MinValue;

        // Payout Münzen (reduziert – ungenutzte Felder entfernt)
        private decimal _geplanteMuenzAuszahlung = 0m;

        private bool _notesPayoutInProgress = false;
        private bool _coinsPayoutInProgress = false;
        private bool _payoutInProgress = false;
        private bool _bookingInProgress = false;
        private int _busyUnlockTimeoutSec = 0;
        private DateTime _busyLockSince = DateTime.MinValue;
        private Timer _tmrBusyUnlock;

        private int _notesDispensedCent = 0;
        private int _coinsDispensedCentTotal = 0;
        private decimal _consumedGuthabenNotes = 0m;
        private decimal _consumedGuthabenCoins = 0m;

        private readonly bool _isAdmin;
        private int _activeCoinPayoutDevices = 0;

        private Image _bgImage;
        private const float BackgroundImageOpacity = 1.0f;
        private bool _lastAbmeldenEnabled = true;

        public AbrechnungForm(PersonalInfo personal, ShiftDetails details, NV200_SSP ssp) : this(personal, details, ssp, false) { }

        public AbrechnungForm(PersonalInfo personal, ShiftDetails details, NV200_SSP ssp, bool isAdmin)
        {
            _personal = personal ?? throw new ArgumentNullException(nameof(personal));
            _details = details;
            _ssp = ssp;
            _coin = CoinManager.Instance;
            _isAdmin = isAdmin;
            try { CurrentPersonalId = _personal?.PID ?? 0; } catch { CurrentPersonalId = 0; }
            KeyPreview = true;
            try { this.KeyDown += AbrechnungForm_KeyDown; } catch { }
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            UpdateStyles();
            SuspendLayout();
            LoadBackgroundImage();
            BuildModernLayout();
            ResumeLayout(true);
            EnableDoubleBufferingRecursive(this);
            try { Invalidate(true); Update(); } catch { }

            // NEW: If preloaded details do not match AllowedManIds, ignore them to avoid showing disallowed shift values
            try
            {
                var allowed = ParseAllowedManIds();
                if (_details != null && allowed != null && allowed.Count > 0)
                {
                    if (!allowed.Contains(_details.ManId))
                    {
                        _details = null;
                    }
                }
            }
            catch { }

            try
            {
                var bu = IniHelper.ReadValue("UI", "BusyUnlockTimeoutSec", AppSettings.IniPath);
                if (!string.IsNullOrWhiteSpace(bu)) int.TryParse(bu, out _busyUnlockTimeoutSec);
                if (_busyUnlockTimeoutSec < 0) _busyUnlockTimeoutSec = 0;
            }
            catch { _busyUnlockTimeoutSec = 0; }

            try
            {
                _tmrBusyUnlock = new Timer { Interval = 1000 };
                _tmrBusyUnlock.Tick += (s, e) => BusyUnlockWatchdog();
                _tmrBusyUnlock.Start();
            }
            catch { }
        }

        public AbrechnungForm(PersonalInfo personal, ShiftDetails details, NV200_SSP ssp, decimal preloadedGuthaben, bool isAdmin)
            : this(personal, details, ssp, isAdmin)
        {
            try
            {
                _personalGuthaben = preloadedGuthaben;
                if (lblGuthaben != null)
                    lblGuthaben.Text = $"Personal-Guthaben: {_personalGuthaben:C2}";
            }
            catch { }
        }

        private void LoadBackgroundImage()
        {
            // Prefer a dedicated background for this form if available.
            Image img = null;
            try
            {
                // Resolve dynamically so this still compiles even if the resource is not yet present.
                img = TaMi_Einzahlautomat.Properties.Resources.ResourceManager.GetObject("Hintergrund_Abrechnen") as Image;
            }
            catch { img = null; }

            if (img == null)
            {
                try { img = TaMi_Einzahlautomat.Properties.Resources.Hintergrund; } catch { img = null; }
            }
            if (img == null)
            {
                try { AppLogger.Log("Resources.Hintergrund ist null – Hintergrund wird nicht angezeigt."); } catch { }
                return;
            }
            try
            {
                _bgImage?.Dispose();
                _bgImage = new Bitmap(img);
            }
            catch (Exception ex)
            {
                try { AppLogger.Log("Fehler beim Laden des Hintergrundbildes: " + ex.Message); } catch { }
                // Fallback: direkt als Form-Hintergrund setzen
                try { this.BackgroundImage = img; this.BackgroundImageLayout = ImageLayout.Stretch; } catch { }
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            if (_bgImage == null) return;
            try
            {
                var g = e.Graphics;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                float formW = ClientSize.Width;
                float formH = ClientSize.Height;
                if (formW <= 0 || formH <= 0) return;
                float imgW = _bgImage.Width;
                float imgH = _bgImage.Height;
                if (imgW <= 0 || imgH <= 0) return;
                float scale = Math.Max(formW / imgW, formH / imgH);
                float drawW = imgW * scale;
                float drawH = imgH * scale;
                float x = (formW - drawW) / 2f;
                float y = (formH - drawH) / 2f - 40f;
                using (var ia = new ImageAttributes())
                {
                    var cm = new ColorMatrix();
                    cm.Matrix33 = BackgroundImageOpacity;
                    ia.SetColorMatrix(cm, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                    var dest = new Rectangle((int)x, (int)y, (int)drawW, (int)drawH);
                    g.DrawImage(_bgImage, dest, 0, 0, _bgImage.Width, _bgImage.Height, GraphicsUnit.Pixel, ia);
                }
            }
            catch { }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { _bgImage?.Dispose(); } catch { }
            try
            {
                if (_tmrAvail != null)
                {
                    _tmrAvail.Enabled = false;
                    _tmrAvail.Stop();
                    _tmrAvail.Tick -= (s, ev) => SafeRefreshAvailability();
                    _tmrAvail.Dispose();
                    _tmrAvail = null;
                }
            }
            catch { }

            base.OnFormClosed(e);

            try { _coin?.Enable(false); } catch { }
            try { DetachCoinEvents(); } catch { }
            try { CurrentPersonalId = 0; } catch { }
            try { _coin2?.Enable(false); } catch { }
            try { DetachCoin2Events(); } catch { }

            try
            {
                if (_ssp != null)
                {
                    try { _ssp.Disable_Device(); } catch { }
                    try { _ssp.SetInhibit(true); } catch { }
                    try { _ssp.MitarbeiterEingeloggt = false; } catch { }
                    try { _ssp.ConfigureBezel(255, 0, 0); } catch { }
                    try { _ssp.AllowAutoEnable(false); } catch { }
                }
            }
            catch { }

            try
            {
                var ssp2 = Program.NV2002Instance;
                if (ssp2 != null)
                {
                    try { ssp2.Disable_Device(); } catch { }
                    try { ssp2.SetInhibit(true); } catch { }
                    try { ssp2.MitarbeiterEingeloggt = false; } catch { }
                    try { ssp2.ConfigureBezel(255, 0, 0); } catch { }
                    try { ssp2.AllowAutoEnable(false); } catch { }
                }
            }
            catch { }

            try { DetachSspEvents(); } catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                if (_tmrAvail != null)
                {
                    _tmrAvail.Enabled = false;
                    _tmrAvail.Stop();
                }
            }
            catch { }
            base.OnFormClosing(e);
        }

        private void BuildModernLayout()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1280, 1024);
            BackColor = Color.White;
            DoubleBuffered = true;
            try { SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true); } catch { }
            try { UpdateStyles(); } catch { }

            headerPanel = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(ClientSize.Width, 60),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };
            headerPanel.Paint += HeaderPanel_Paint;
            headerPanel.MouseDown += HeaderPanel_MouseDown;
            headerPanel.MouseMove += HeaderPanel_MouseMove;
            Controls.Add(headerPanel);

            lblTitle = new Label
            {
                Text = "Einzahlautomat",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Variable", 22F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(24, 0),
                Size = new Size(600, 60),
                BackColor = Color.Transparent
            };
            headerPanel.Controls.Add(lblTitle);

            btnAbmelden = new Button
            {
                Text = "Abmelden",
                Font = new Font("Segoe UI Variable", 14F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(229, 57, 53),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(140, 44),
                Location = new Point(ClientSize.Width - 160, 8),
                TabStop = false
            };
            btnAbmelden.FlatAppearance.BorderSize = 0;
            btnAbmelden.FlatAppearance.MouseOverBackColor = Color.FromArgb(211, 47, 47);
            try { btnAbmelden.Region = System.Drawing.Region.FromHrgn(CreateRoundRectRgn(0, 0, btnAbmelden.Width, btnAbmelden.Height, 14, 14)); } catch { }
            btnAbmelden.Click += btnAbmelden_Click;
            headerPanel.Controls.Add(btnAbmelden);

            btnAdmin = new Button
            {
                Text = "\u2699",
                Font = new Font("Segoe UI Symbol", 22F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(33, 150, 243),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(56, 44),
                Location = new Point(ClientSize.Width - 220, 8),
                TabStop = false,
                Visible = _isAdmin
            };
            btnAdmin.FlatAppearance.BorderSize = 0;
            btnAdmin.FlatAppearance.MouseOverBackColor = Color.FromArgb(33, 203, 243);
            btnAdmin.Click += (s, e) =>
            {
                try
                {
                    foreach (Form f in Application.OpenForms)
                    {
                        if (f is AdminOverviewForm existing)
                        {
                            try
                            {
                                if (existing.WindowState == FormWindowState.Minimized)
                                    existing.WindowState = FormWindowState.Normal;
                                existing.TopMost = true;
                                existing.BringToFront();
                                existing.Activate();
                                existing.Focus();
                                if (Program.KioskModeEnabled && TopMost)
                                {
                                    TopMost = false;
                                    existing.FormClosed += (s2, e2) => { try { TopMost = true; Activate(); } catch { } };
                                }
                            }
                            catch { }
                            return;
                        }
                    }
                }
                catch { }

                var frmAdmin = new AdminOverviewForm(_ssp);
                try
                {
                    if (Program.KioskModeEnabled)
                    {
                        bool prevTopMost = TopMost;
                        try { TopMost = false; } catch { }
                        frmAdmin.TopMost = true;
                        frmAdmin.FormClosed += (s3, e3) => { try { TopMost = prevTopMost; Activate(); BringToFront(); } catch { } };
                    }
                }
                catch { }
                frmAdmin.StartPosition = FormStartPosition.CenterScreen;
                frmAdmin.Show();
                try { frmAdmin.BringToFront(); frmAdmin.Activate(); } catch { }
            };
            headerPanel.Controls.Add(btnAdmin);

            // NEU: Dokumente-Button links neben Admin
            bool docsEnabled = false;
            try
            {
                var docs = IniHelper.ReadValue("UI", "DocumentsEnabled", AppSettings.IniPath);
                docsEnabled = !string.IsNullOrWhiteSpace(docs) &&
                    (docs.Equals("true", StringComparison.OrdinalIgnoreCase) || docs.Equals("1") || docs.Equals("yes", StringComparison.OrdinalIgnoreCase) || docs.Equals("on", StringComparison.OrdinalIgnoreCase));
            }
            catch { }

            btnDocuments = new Button
            {
                Text = string.Empty,
                Font = new Font("Segoe UI Symbol", 20F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(76, 175, 80),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(56, 44),
                Location = new Point(ClientSize.Width - 280, 8),
                TabStop = false,
                Visible = docsEnabled
            };
            btnDocuments.FlatAppearance.BorderSize = 0;
            btnDocuments.FlatAppearance.MouseOverBackColor = Color.FromArgb(96, 195, 100);
            btnDocuments.Paint += (s, pe) =>
            {
                try
                {
                    var g = pe.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    var btn = (Button)s;
                    var r = btn.ClientRectangle;
                    int margin = 12;
                    int fold = 8;
                    var page = new Rectangle(r.Left + margin, r.Top + 8, r.Width - margin * 2, r.Height - 16);
                    using (var pen = new Pen(Color.White, 2f))
                    {
                        // page outline
                        g.DrawRectangle(pen, page);
                        // folded corner (top-right)
                        g.DrawLine(pen, page.Right - fold, page.Top, page.Right, page.Top + fold);
                        g.DrawLine(pen, page.Right - fold, page.Top, page.Right - fold, page.Top + fold);
                        // text lines
                        int tx = page.Left + 4;
                        int rx = page.Right - 4;
                        int y1 = page.Top + 8;
                        int y2 = y1 + 6;
                        int y3 = y2 + 6;
                        g.DrawLine(pen, tx, y1, rx - fold, y1);
                        g.DrawLine(pen, tx, y2, rx - 6, y2);
                        g.DrawLine(pen, tx, y3, rx - 10, y3);
                    }
                }
                catch { }
            };
            btnDocuments.Click += (s, e) =>
            {
                try
                {
                    Action<PersonalInfo> openDocs = (p) =>
                    {
                        var frmDocs = new DocumentsForm(p);
                        frmDocs.StartPosition = FormStartPosition.CenterScreen;
                        if (Program.KioskModeEnabled)
                        {
                            bool prevTopMost = TopMost;
                            try { TopMost = false; } catch { }
                            frmDocs.TopMost = true;
                            frmDocs.FormClosed += (s3, e3) => { try { TopMost = prevTopMost; Activate(); BringToFront(); } catch { } };
                        }
                        frmDocs.Show();
                        try { frmDocs.BringToFront(); frmDocs.Activate(); } catch { }
                    };

                    var auth = new AuthForm(_personal.PID, _personal, _ssp, openDocs);
                    auth.StartPosition = FormStartPosition.CenterParent;
                    if (Program.KioskModeEnabled)
                    {
                        bool prevTopMost = TopMost;
                        try { TopMost = false; } catch { }
                        auth.TopMost = true;
                        auth.FormClosed += (s3, e3) => { try { TopMost = prevTopMost; Activate(); BringToFront(); } catch { } };
                    }
                    auth.Show(this);
                }
                catch { }
            };
            headerPanel.Controls.Add(btnDocuments);

            // NEU: Stunden-Button links neben Dokumente
            bool hoursEnabled = false; bool hoursPwdRequired = false;
            try
            {
                var hours = IniHelper.ReadValue("UI", "TimeTrackingEnabled", AppSettings.IniPath);
                hoursEnabled = !string.IsNullOrWhiteSpace(hours) && (hours.Equals("true", StringComparison.OrdinalIgnoreCase) || hours.Equals("1") || hours.Equals("yes", StringComparison.OrdinalIgnoreCase) || hours.Equals("on", StringComparison.OrdinalIgnoreCase));
                var hpwd = IniHelper.ReadValue("UI", "TimeTrackingPasswordRequired", AppSettings.IniPath);
                hoursPwdRequired = !string.IsNullOrWhiteSpace(hpwd) && (hpwd.Equals("true", StringComparison.OrdinalIgnoreCase) || hpwd.Equals("1") || hpwd.Equals("yes", StringComparison.OrdinalIgnoreCase) || hpwd.Equals("on", StringComparison.OrdinalIgnoreCase));
            }
            catch { }

            // Personal-Flag: Zeiterfassungsansicht komplett ausblenden
            // Regel: Wenn `KEINE_ZEITERFASSUNGS_ANSICHT` gesetzt ist -> Button ausblenden,
            // Ausnahme/Override: wenn zusätzlich `ZEITERFASSUNG_EINZAHLAUTOMAT` gesetzt ist -> trotzdem anzeigen.
            try
            {
                if (_personal != null)
                {
                    var flags = (SuE.TaMi.PersonalFlags)_personal.Flags;
                    bool hide = (((int)flags & (int)SuE.TaMi.PersonalFlags.PERSONAL_FLAG_KEINE_ZEITERFASSUNGS_ANSICHT) == (int)SuE.TaMi.PersonalFlags.PERSONAL_FLAG_KEINE_ZEITERFASSUNGS_ANSICHT);
                    bool forceShow = (((int)flags & (int)SuE.TaMi.PersonalFlags.PERSONAL_FLAG_ZEITERFASSUNG_EINZAHLAUTOMAT) == (int)SuE.TaMi.PersonalFlags.PERSONAL_FLAG_ZEITERFASSUNG_EINZAHLAUTOMAT);
                    if (hide && !forceShow)
                        hoursEnabled = false;
                }
            }
            catch { }

            var btnHours = new Button
            {
                Text = string.Empty,
                Font = new Font("Segoe UI Variable", 20F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(255, 143, 0),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(56, 44),
                Location = new Point(ClientSize.Width - 340, 8),
                TabStop = false,
                Visible = hoursEnabled
            };
            btnHours.FlatAppearance.BorderSize = 0;
            btnHours.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 183, 77);
            btnHours.Paint += (s, pe) =>
            {
                try
                {
                    var g = pe.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
                    var r = ((Button)s).ClientRectangle;
                    int cx = r.Left + r.Width / 2; int cy = r.Top + r.Height / 2;
                    int radius = Math.Min(r.Width, r.Height) / 2 - 12;
                    using (var pen = new Pen(Color.White, 3f))
                    {
                        g.DrawEllipse(pen, cx - radius, cy - radius, radius * 2, radius * 2);
                        // clock hands
                        g.DrawLine(pen, cx, cy, cx, cy - radius + 6); // hour up
                        g.DrawLine(pen, cx, cy, cx + radius - 8, cy); // minute right
                    }
                }
                catch { }
            };
            btnHours.Click += (s, e) =>
            {
                try
                {
                    Action<PersonalInfo> openHours = (p) =>
                    {
                        var frmHours = new HoursOverviewForm(p);
                        frmHours.StartPosition = FormStartPosition.CenterScreen;
                        if (Program.KioskModeEnabled)
                        {
                            bool prevTopMost = TopMost; try { TopMost = false; } catch { }
                            frmHours.TopMost = true;
                            frmHours.FormClosed += (s3, e3) => { try { TopMost = prevTopMost; Activate(); BringToFront(); } catch { } };
                        }
                        frmHours.Show();
                        try { frmHours.BringToFront(); frmHours.Activate(); } catch { }
                    };

                    if (hoursPwdRequired)
                    {
                        var auth = new AuthForm(_personal.PID, _personal, _ssp, openHours);
                        auth.StartPosition = FormStartPosition.CenterParent;
                        if (Program.KioskModeEnabled)
                        {
                            bool prevTopMost = TopMost; try { TopMost = false; } catch { }
                            auth.TopMost = true;
                            auth.FormClosed += (s4, e4) => { try { TopMost = prevTopMost; Activate(); BringToFront(); } catch { } };
                        }
                        auth.Show(this);
                    }
                    else
                    {
                        openHours(_personal);
                    }
                }
                catch { }
            };
            headerPanel.Controls.Add(btnHours);

            // Zentralen Style anwenden (Header + Button-Theme)
            try { ApplyCentralDesign(); } catch { }

            // Header modernisieren (Gradient + moderne Buttons wie in Vorlage)
            try
            {
                // After applying ModernButtonStyle to header icon buttons, re-attach icon painting.
                // Otherwise the ModernButton_Paint handler would hide these symbols.
                PaintEventHandler docsIconPaint = null;
                PaintEventHandler hoursIconPaint = null;
                try
                {
                    docsIconPaint = (s, pe) =>
                    {
                        try
                        {
                            var g = pe.Graphics;
                            g.SmoothingMode = SmoothingMode.AntiAlias;
                            var btn = (Button)s;
                            var r = btn.ClientRectangle;
                            int margin = 12;
                            int fold = 6;
                            var page = new Rectangle(r.Left + margin, r.Top + 8, r.Width - margin * 2, r.Height - 16);
                            using (var pen = new Pen(Color.White, 2f))
                            {
                                g.DrawRectangle(pen, page);
                                g.DrawLine(pen, page.Right - fold, page.Top, page.Right, page.Top + fold);
                                g.DrawLine(pen, page.Right - fold, page.Top, page.Right - fold, page.Top + fold);

                                int tx = page.Left + 4;
                                int rx = page.Right - 4;
                                int y1 = page.Top + 6;
                                int y2 = y1 + 6;
                                int y3 = y2 + 6;
                                g.DrawLine(pen, tx, y1, rx - fold, y1);
                                g.DrawLine(pen, tx, y2, rx - 6, y2);
                                g.DrawLine(pen, tx, y3, rx - 10, y3);
                            }
                        }
                        catch { }
                    };

                    hoursIconPaint = (s, pe) =>
                    {
                        try
                        {
                            var g = pe.Graphics;
                            g.SmoothingMode = SmoothingMode.AntiAlias;
                            var r = ((Button)s).ClientRectangle;
                            int cx = r.Left + r.Width / 2;
                            int cy = r.Top + r.Height / 2;
                            int radius = Math.Min(r.Width, r.Height) / 2 - 12;
                            using (var pen = new Pen(Color.White, 3f))
                            {
                                g.DrawEllipse(pen, cx - radius, cy - radius, radius * 2, radius * 2);
                                g.DrawLine(pen, cx, cy, cx, cy - radius + 6);
                                g.DrawLine(pen, cx, cy, cx + radius - 8, cy);
                            }
                        }
                        catch { }
                    };
                }
                catch { }

                Action layoutHeaderButtons = () =>
                {
                    try
                    {
                        int right = ClientSize.Width - 20;
                        int gap = 10;
                        int yAbmelden = 8;
                        int yIcon = 8;
                        int headerH = 60;
                        try
                        {
                            // Align all header buttons vertically centered
                            headerH = headerPanel != null ? headerPanel.Height : 60;
                            yAbmelden = (headerH - (btnAbmelden != null ? btnAbmelden.Height : 44)) / 2;
                            yIcon = (headerH - 44) / 2;
                        }
                        catch { yAbmelden = 8; yIcon = 8; }
                        if (btnAbmelden != null)
                        {
                            btnAbmelden.Location = new Point(right - btnAbmelden.Width, yAbmelden);
                            right = btnAbmelden.Left - gap;
                        }
                        if (btnAdmin != null && btnAdmin.Visible)
                        {
                            btnAdmin.Location = new Point(right - btnAdmin.Width, yIcon);
                            right = btnAdmin.Left - gap;
                        }
                        if (btnDocuments != null && btnDocuments.Visible)
                        {
                            btnDocuments.Location = new Point(right - btnDocuments.Width, yIcon);
                            right = btnDocuments.Left - gap;
                        }
                        if (btnHours != null && btnHours.Visible)
                        {
                            btnHours.Location = new Point(right - btnHours.Width, yIcon);
                            right = btnHours.Left - gap;
                        }

                        // Schicht-Auswahl links neben den Icon-Buttons (bzw. vor Abmelden)
                        if (btnSchichtAuswahl != null && btnSchichtAuswahl.Visible)
                        {
                            int y = (headerH - btnSchichtAuswahl.Height) / 2;
                            btnSchichtAuswahl.Location = new Point(Math.Max(20, right - btnSchichtAuswahl.Width), y);
                            right = btnSchichtAuswahl.Left - gap;
                        }
                    }
                    catch { }
                };

                // Modern button styles (gradient + rounded + border)
                ApplyModernButtonStyle(btnAbmelden, Color.FromArgb(239, 83, 80), Color.FromArgb(198, 40, 40));
                btnAbmelden.FlatAppearance.MouseOverBackColor = Color.Transparent;

                ApplyModernButtonStyle(btnAdmin, Color.FromArgb(33, 150, 243), Color.FromArgb(13, 71, 161));
                btnAdmin.FlatAppearance.MouseOverBackColor = Color.Transparent;
                try { btnAdmin.Font = new Font("Segoe UI Symbol", 18F, FontStyle.Bold); } catch { }

                ApplyModernButtonStyle(btnDocuments, Color.FromArgb(76, 175, 80), Color.FromArgb(27, 94, 32));
                btnDocuments.FlatAppearance.MouseOverBackColor = Color.Transparent;
                try
                {
                    if (docsIconPaint != null)
                    {
                        btnDocuments.Paint -= docsIconPaint;
                        btnDocuments.Paint += docsIconPaint;
                    }
                }
                catch { }

                ApplyModernButtonStyle(btnHours, Color.FromArgb(255, 179, 0), Color.FromArgb(245, 124, 0));
                btnHours.FlatAppearance.MouseOverBackColor = Color.Transparent;
                try
                {
                    if (hoursIconPaint != null)
                    {
                        btnHours.Paint -= hoursIconPaint;
                        btnHours.Paint += hoursIconPaint;
                    }
                }
                catch { }

                layoutHeaderButtons();
                SizeChanged += (s, e) => layoutHeaderButtons();
                try
                {
                    headerPanel.Layout += (s, e) =>
                    {
                        try { layoutHeaderButtons(); } catch { }
                    };
                }
                catch { }
            }
            catch { }

            try { Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, Width, Height, 24, 24)); } catch { }

            tabControl = new BackgroundInheritingTabControl
            {
                Location = new Point(40, 80),
                Size = new Size(1200, 900),
                Font = new Font("Segoe UI Variable", 18F, FontStyle.Bold),
                ItemSize = new Size(300, 60),
                Alignment = TabAlignment.Top,
                Appearance = TabAppearance.Normal
            };
            try { tabControl.BackColor = Color.Transparent; } catch { }
            Controls.Add(tabControl);

            tabAbrechnen = new TabPage("Abrechnen") { BackColor = Color.Transparent };
            tabWechseln = new TabPage("Wechseln") { BackColor = Color.Transparent };
            try { EnableDoubleBufferingRecursive(tabAbrechnen); } catch { }
            try { EnableDoubleBufferingRecursive(tabWechseln); } catch { }
            try
            {
                tabAbrechnen.UseVisualStyleBackColor = false;
                tabWechseln.UseVisualStyleBackColor = false;
            }
            catch { }
            tabControl.TabPages.Add(tabAbrechnen);
            tabControl.TabPages.Add(tabWechseln);

            // Ensure Abrechnen background uses the form's background (not TabPage default white)
            try
            {
                tabAbrechnen.Controls.Clear();
                _abrechnenSurface = new AbrechnenSurfacePanel
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.Transparent
                };
                tabAbrechnen.Controls.Add(_abrechnenSurface);
            }
            catch { }

            // Wechseln-Tab ebenfalls über eine Surface laufen lassen, damit das Form-Background durchscheint
            try
            {
                tabWechseln.Controls.Clear();
                _wechselnSurface = new AbrechnenSurfacePanel
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.Transparent
                };
                tabWechseln.Controls.Add(_wechselnSurface);
            }
            catch { }

            tabAbrechnen.SuspendLayout();
            BuildAbrechnenTab();
            tabAbrechnen.ResumeLayout(false);

            tabWechseln.SuspendLayout();
            BuildWechselnTab();
            tabWechseln.ResumeLayout(false);

            _tmrAvail = new Timer { Interval = 3000 };
            _tmrAvail.Tick += (s, e) => SafeRefreshAvailability();
            _tmrAvail.Enabled = true;

            btnSchichtAuswahl = new Button
            {
                Text = "Schicht auswählen",
                Font = new Font("Segoe UI Variable", 14F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(33, 150, 243),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(220, 40),
                Location = new Point(20, 10),
                TabStop = false,
                Visible = false
            };
            btnSchichtAuswahl.FlatAppearance.BorderSize = 0;
            try
            {
                ApplyModernButtonStyle(btnSchichtAuswahl, UiTheme.PrimaryStart, UiTheme.PrimaryEnd);
                btnSchichtAuswahl.FlatAppearance.MouseOverBackColor = Color.Transparent;
            }
            catch { }
            btnSchichtAuswahl.Click += BtnSchichtAuswahl_Click;
            headerPanel.Controls.Add(btnSchichtAuswahl);

            // initial einordnen
            try { headerPanel.PerformLayout(); } catch { }

            tabControl.SelectedIndexChanged += (s, e) =>
            {
                if (tabControl.SelectedTab == tabWechseln)
                {
                    try { tabWechseln.SuspendLayout(); } catch { }
                    SafeRefreshAvailability();
                    UpdateCoinAvailabilityLabels();
                    UpdateMaxVerfuegbar();
                    try { tabWechseln.ResumeLayout(true); tabWechseln.Invalidate(true); } catch { }
                }
            };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                try
                {
                    // Reduces flicker when switching tabs by compositing the whole form.
                    // (Tradeoff: can feel a bit less snappy on very old GPUs, but fixes the partial redraw.)
                    cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED
                }
                catch { }
                return cp;
            }
        }

        private void EnableDoubleBufferingRecursive(Control root)
        {
            if (root == null) return;
            try
            {
                var prop = typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
                prop?.SetValue(root, true, null);
            }
            catch { }
            foreach (Control child in root.Controls) EnableDoubleBufferingRecursive(child);
        }

        private Label lblBelegInfo;
        private Label lblNotizenInfo;
        private Timer _notesScrollTimer;
        private int _notesScrollOffsetPx;
        private int _notesScrollVelocityPx;
        private int _notesScrollPauseMs;
        private int _notesScrollPauseRemainingMs;
        private int _notesScrollLastTextHash;
        private Bitmap _notesScrollBitmap;
        private int _notesScrollBitmapTextHash;
        private int _notesScrollBitmapWidth;
        private float _notesScrollBitmapFontSize;
        private List<string> _notesScrollLines;
        private int _notesScrollFirstLine;
        private int _notesScrollLineOffsetPx;
        private int _notesScrollLineHeightPx;

        private void ResetNotesScrollBitmap()
        {
            try { _notesScrollBitmap?.Dispose(); } catch { }
            _notesScrollBitmap = null;
            _notesScrollBitmapTextHash = 0;
            _notesScrollBitmapWidth = 0;
            _notesScrollBitmapFontSize = 0f;
            _notesScrollLines = null;
            _notesScrollFirstLine = 0;
            _notesScrollLineOffsetPx = 0;
            _notesScrollLineHeightPx = 0;
        }

        private void EnsureNotesScrollLines(string txt, int wrapWidth)
        {
            if (lblNotizenInfo == null) { ResetNotesScrollBitmap(); return; }
            if (string.IsNullOrEmpty(txt)) { ResetNotesScrollBitmap(); return; }
            if (wrapWidth <= 0) wrapWidth = 1;

            int h = txt.GetHashCode();
            float fs = 0f;
            string fn = string.Empty;
            FontStyle fsty = FontStyle.Regular;
            try
            {
                if (lblNotizenInfo.Font != null)
                {
                    fs = lblNotizenInfo.Font.Size;
                    fn = lblNotizenInfo.Font.Name ?? string.Empty;
                    fsty = lblNotizenInfo.Font.Style;
                }
            }
            catch { fs = 0f; fn = string.Empty; fsty = FontStyle.Regular; }

            int fontKey = 0;
            try { unchecked { fontKey = (fn.GetHashCode() * 397) ^ (int)fsty; } } catch { fontKey = (int)fsty; }
            int combinedKey = 0;
            try { unchecked { combinedKey = (h * 397) ^ fontKey; } } catch { combinedKey = h; }

            if (_notesScrollLines != null && _notesScrollBitmapTextHash == combinedKey && _notesScrollBitmapWidth == wrapWidth && Math.Abs(_notesScrollBitmapFontSize - fs) < 0.01f)
                return;

            _notesScrollBitmapTextHash = combinedKey;
            _notesScrollBitmapWidth = wrapWidth;
            _notesScrollBitmapFontSize = fs;
            _notesScrollFirstLine = 0;
            _notesScrollLineOffsetPx = 0;

            // line height (TextRenderer uses slightly different metrics than GDI+)
            try { _notesScrollLineHeightPx = TextRenderer.MeasureText("Ag", lblNotizenInfo.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Height; }
            catch { _notesScrollLineHeightPx = 20; }
            if (_notesScrollLineHeightPx < 12) _notesScrollLineHeightPx = 12;

            // Word-wrap into draw-lines using TextRenderer
            var result = new List<string>();
            try
            {
                var paragraphs = (txt ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n").Split(new[] { '\n' }, StringSplitOptions.None);
                foreach (var p in paragraphs)
                {
                    var line = (p ?? string.Empty);
                    if (line.Length == 0) { result.Add(string.Empty); continue; }

                    // simple greedy wrap: split by spaces
                    var words = line.Split(new[] { ' ' }, StringSplitOptions.None);
                    string cur = string.Empty;
                    for (int i = 0; i < words.Length; i++)
                    {
                        var w = words[i] ?? string.Empty;
                        var test = string.IsNullOrEmpty(cur) ? w : (cur + " " + w);
                        int wpx = 0;
                        try { wpx = TextRenderer.MeasureText(test, lblNotizenInfo.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width; }
                        catch { wpx = wrapWidth + 1; }

                        if (wpx > wrapWidth && !string.IsNullOrEmpty(cur))
                        {
                            result.Add(cur);
                            cur = w;
                        }
                        else
                        {
                            cur = test;
                        }
                    }
                    if (!string.IsNullOrEmpty(cur)) result.Add(cur);
                }
            }
            catch
            {
                result.Clear();
                result.Add(txt ?? string.Empty);
            }

            _notesScrollLines = result;
        }

        private void EnsureNotesScrollBitmap(string txt, int wrapWidth)
        {
            if (string.IsNullOrEmpty(txt) || lblNotizenInfo == null) { ResetNotesScrollBitmap(); return; }
            if (wrapWidth <= 0) wrapWidth = 1;
            int h = txt.GetHashCode();
            float fs = 0f;
            try { fs = lblNotizenInfo.Font != null ? lblNotizenInfo.Font.Size : 0f; } catch { fs = 0f; }

            if (_notesScrollBitmap != null && _notesScrollBitmapTextHash == h && _notesScrollBitmapWidth == wrapWidth && Math.Abs(_notesScrollBitmapFontSize - fs) < 0.01f)
                return;

            ResetNotesScrollBitmap();
            _notesScrollBitmapTextHash = h;
            _notesScrollBitmapWidth = wrapWidth;
            _notesScrollBitmapFontSize = fs;

            try
            {
                // measure required height (with unlimited height but fixed width)
                int measuredH = 0;
                using (var g = lblNotizenInfo.CreateGraphics())
                using (var fmt = new StringFormat())
                {
                    fmt.Alignment = StringAlignment.Near;
                    fmt.LineAlignment = StringAlignment.Near;
                    fmt.Trimming = StringTrimming.None;
                    fmt.FormatFlags &= ~StringFormatFlags.NoWrap;
                    fmt.FormatFlags &= ~StringFormatFlags.NoClip;
                    var sz = g.MeasureString(txt, lblNotizenInfo.Font, wrapWidth, fmt);
                    measuredH = (int)Math.Ceiling(sz.Height);
                }
                if (measuredH < 1) measuredH = 1;

                // extra padding so last line never clips
                int padBottom = 12;
                int bmpH = measuredH + padBottom;
                var bmp = new Bitmap(Math.Max(1, wrapWidth), Math.Max(1, bmpH), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using (var g2 = Graphics.FromImage(bmp))
                using (var fmt2 = new StringFormat())
                {
                    g2.Clear(Color.Transparent);
                    g2.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                    fmt2.Alignment = StringAlignment.Near;
                    fmt2.LineAlignment = StringAlignment.Near;
                    fmt2.Trimming = StringTrimming.None;
                    fmt2.FormatFlags &= ~StringFormatFlags.NoWrap;
                    fmt2.FormatFlags &= ~StringFormatFlags.NoClip;
                    var rect = new RectangleF(0, 0, wrapWidth, bmpH);
                    using (var br = new SolidBrush(lblNotizenInfo.ForeColor))
                        g2.DrawString(txt, lblNotizenInfo.Font, br, rect, fmt2);
                }
                _notesScrollBitmap = bmp;
            }
            catch
            {
                ResetNotesScrollBitmap();
            }
        }

        private void BuildAbrechnenTab()
        {
            // Glas-/Frosted-Look: TabPage selbst transparent halten und Overlay zeichnen
            tabAbrechnen.BackColor = Color.Transparent;
            EnsureAbrechnenTabPainting();

            var host = (Control)(_abrechnenSurface ?? tabAbrechnen);

            Color surface = Color.FromArgb(120, 245, 250, 255);
            Color surface2 = Color.FromArgb(80, 230, 240, 255);
            Color border = Color.FromArgb(90, 180, 200, 230);
            Color text = Color.FromArgb(15, 23, 42);
            Color subText = Color.FromArgb(51, 65, 85);
            Color primary = Color.FromArgb(0, 122, 204);
            Color danger = Color.FromArgb(211, 47, 47);
            Color success = Color.FromArgb(46, 125, 50);

            var fontCardTitle = new Font("Segoe UI Variable", 28F, FontStyle.Bold);
            var fontBody = new Font("Segoe UI Variable", 16F, FontStyle.Regular);
            // Mitarbeiter-Zeile: nur leicht größer als Personal-Guthaben
            var fontTitle = new Font("Segoe UI Variable", 18F, FontStyle.Bold);
            var fontStrong = new Font("Segoe UI Variable", 18F, FontStyle.Bold);
            var fontAmounts = new Font("Segoe UI Variable", 18F, FontStyle.Bold);

            if (_cardAbrechnen != null)
            {
                try { _cardAbrechnenRegion?.Dispose(); } catch { }
                try { _cardAbrechnen.Dispose(); } catch { }
                _cardAbrechnen = null;
            }

            try { _abrechnenInnerRegion?.Dispose(); } catch { }
            _abrechnenInnerRegion = null;
            _abrechnenInnerPanel = null;
            _abrechnenCardTitle = null;

            // Neu aufbauen -> vorherige Shadow-Panels im Tab entfernen
            try
            {
                for (int i = host.Controls.Count - 1; i >= 0; i--)
                {
                    var c = host.Controls[i];
                    if (c is Panel p && p.Tag is string t && t == "abrechnen-shadow")
                    {
                        host.Controls.RemoveAt(i);
                        try { c.Dispose(); } catch { }
                    }
                }
            }
            catch { }

            int pad = 26;

            // Schatten unter der Card (separates Panel, damit Card selbst clicking etc. nicht beeinflusst)
            Panel shadowPanel = null;
            try
            {
                // No outer margin: card should touch the tab client area (no visible gap).
                int outerMargin = 0;
                shadowPanel = new Panel
                {
                    Tag = "abrechnen-shadow",
                    Location = new Point(outerMargin - _abrechnenCardShadowSize, outerMargin - _abrechnenCardShadowSize),
                    Size = new Size(host.ClientSize.Width - (outerMargin * 2) + _abrechnenCardShadowSize * 2, host.ClientSize.Height - (outerMargin * 2) + _abrechnenCardShadowSize * 2),
                    Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                    BackColor = Color.Transparent
                };
                shadowPanel.Paint += (s, e) =>
                {
                    try
                    {
                        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        var r = shadowPanel.ClientRectangle;
                        r.Inflate(-2, -2);

                        // einfache weiche Schatten-Illusion über mehrere Rechtecke
                        for (int k = _abrechnenCardShadowSize; k >= 1; k--)
                        {
                            int alpha = (int)(14f * (k / (float)_abrechnenCardShadowSize));
                            using (var pen = new Pen(Color.FromArgb(alpha, 10, 20, 40), 2f))
                            {
                                var rr = new Rectangle(r.X + (k - 1), r.Y + (k - 1), r.Width - (k - 1) * 2, r.Height - (k - 1) * 2);
                                e.Graphics.DrawRectangle(pen, rr);
                            }
                        }
                    }
                    catch { }
                };

                host.Controls.Add(shadowPanel);
                try { shadowPanel.SendToBack(); } catch { }
            }
            catch { shadowPanel = null; }

            _cardAbrechnen = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(host.ClientSize.Width, host.ClientSize.Height),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.Transparent
            };
            _cardAbrechnen.Paint += (s, e) =>
            {
                try
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

                    // Hintergrund (leichter Glas-Verlauf)
                    var rFill = _cardAbrechnen.ClientRectangle;
                    using (var brush = new LinearGradientBrush(rFill, surface, surface2, 90f))
                    {
                        e.Graphics.FillRectangle(brush, rFill);
                    }

                    // feine Glanzkante oben
                    try
                    {
                        var top = new Rectangle(rFill.Left, rFill.Top, rFill.Width, Math.Max(1, rFill.Height / 3));
                        using (var gloss = new LinearGradientBrush(top, Color.FromArgb(70, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                        {
                            e.Graphics.FillRectangle(gloss, top);
                        }
                    }
                    catch { }

                    // Border
                    using (var pen = new Pen(border, 1f))
                    {
                        var r = _cardAbrechnen.ClientRectangle;
                        r.Width -= 1; r.Height -= 1;
                        e.Graphics.DrawRectangle(pen, r);
                    }
                }
                catch { }
            };
            try
            {
                var rgn = Region.FromHrgn(CreateRoundRectRgn(0, 0, _cardAbrechnen.Width, _cardAbrechnen.Height, 18, 18));
                _cardAbrechnenRegion = rgn;
                _cardAbrechnen.Region = rgn;
            }
            catch { }
            host.Controls.Add(_cardAbrechnen);
            try { _cardAbrechnen.BringToFront(); } catch { }

            host.SizeChanged += (s, e) =>
            {
                try
                {
                    if (_cardAbrechnen == null) return;
                    _cardAbrechnenRegion?.Dispose();
                    _cardAbrechnenRegion = null;
                    var rgn = Region.FromHrgn(CreateRoundRectRgn(0, 0, _cardAbrechnen.Width, _cardAbrechnen.Height, 18, 18));
                    _cardAbrechnenRegion = rgn;
                    _cardAbrechnen.Region = rgn;

                    try
                    {
                        if (_abrechnenInnerPanel != null)
                        {
                            _abrechnenInnerRegion?.Dispose();
                            _abrechnenInnerRegion = null;
                            var irgn = Region.FromHrgn(CreateRoundRectRgn(0, 0, _abrechnenInnerPanel.Width, _abrechnenInnerPanel.Height, 16, 16));
                            _abrechnenInnerRegion = irgn;
                            _abrechnenInnerPanel.Region = irgn;
                        }
                    }
                    catch { }

                    // Shadow panel an Card-Size anpassen
                    try
                    {
                        if (shadowPanel != null)
                        {
                            shadowPanel.Location = new Point(_cardAbrechnen.Left - _abrechnenCardShadowSize, _cardAbrechnen.Top - _abrechnenCardShadowSize);
                            shadowPanel.Size = new Size(_cardAbrechnen.Width + _abrechnenCardShadowSize * 2, _cardAbrechnen.Height + _abrechnenCardShadowSize * 2);
                            shadowPanel.Invalidate();
                        }
                    }
                    catch { }
                }
                catch { }
            };
            // Inneres Glas-Panel (wie Screenshot)
            int innerPanelTop = 190;
            _abrechnenInnerPanel = new Panel
            {
                Location = new Point(pad, innerPanelTop),
                Size = new Size(_cardAbrechnen.Width - pad * 2, _cardAbrechnen.Height - innerPanelTop - 70),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(100, 255, 255, 255)
            };
            _abrechnenInnerPanel.Paint += (s, e) =>
            {
                try
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    var rFill = _abrechnenInnerPanel.ClientRectangle;
                    using (var br = new LinearGradientBrush(rFill, Color.FromArgb(100, 255, 255, 255), Color.FromArgb(85, 255, 255, 255), 90f))
                    {
                        e.Graphics.FillRectangle(br, rFill);
                    }
                    using (var pen = new Pen(Color.FromArgb(70, 210, 225, 245), 1f))
                    {
                        var r = _abrechnenInnerPanel.ClientRectangle;
                        r.Width -= 1; r.Height -= 1;
                        e.Graphics.DrawRectangle(pen, r);
                    }
                }
                catch { }
            };
            try
            {
                var irgn = Region.FromHrgn(CreateRoundRectRgn(0, 0, _abrechnenInnerPanel.Width, _abrechnenInnerPanel.Height, 16, 16));
                _abrechnenInnerRegion = irgn;
                _abrechnenInnerPanel.Region = irgn;
            }
            catch { }
            _cardAbrechnen.Controls.Add(_abrechnenInnerPanel);

            _cardAbrechnen.SizeChanged += (s, e) =>
            {
                try
                {
                    if (_abrechnenInnerPanel == null) return;
                    _abrechnenInnerPanel.Width = Math.Max(1, _cardAbrechnen.Width - pad * 2);
                    _abrechnenInnerPanel.Height = Math.Max(1, _cardAbrechnen.Height - innerPanelTop - 70);
                    _abrechnenInnerPanel.Left = pad;
                    _abrechnenInnerPanel.Top = innerPanelTop;
                    _abrechnenInnerPanel.Invalidate();
                }
                catch { }
            };

            _abrechnenCardTitle = new Label
            {
                Text = "Abrechnungen",
                Font = fontCardTitle,
                ForeColor = text,
                Location = new Point(pad + 8, 24),
                Size = new Size(_cardAbrechnen.Width - pad * 2, 48),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.Transparent
            };
            _cardAbrechnen.Controls.Add(_abrechnenCardTitle);

            lblTitel = new Label
            {
                Text = $"Mitarbeiter: {_personal.Vorname} {_personal.Name}",
                Font = fontTitle,
                ForeColor = text,
                Location = new Point(pad + 8, 76),
                Size = new Size(_cardAbrechnen.Width - pad * 2, 36),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.Transparent
            };
            _cardAbrechnen.Controls.Add(lblTitel);

            lblGuthaben = new Label
            {
                Text = "Personal-Guthaben: 0,00 €",
                Font = fontBody,
                ForeColor = subText,
                Location = new Point(pad + 8, 114),
                Size = new Size(_cardAbrechnen.Width - pad * 2, 30),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.Transparent
            };
            _cardAbrechnen.Controls.Add(lblGuthaben);

            int y = 16;

            lblBelegInfo = new Label
            {
                Text = string.Empty,
                Font = new Font("Segoe UI Variable", 20F, FontStyle.Bold),
                Location = new Point(pad, y),
                Size = new Size(_abrechnenInnerPanel.Width - pad * 2, 56),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.Transparent,
                ForeColor = primary,
                AutoSize = false,
                Padding = new Padding(12, 10, 12, 10),
                TextAlign = ContentAlignment.TopLeft
            };
            try { lblBelegInfo.AutoEllipsis = true; } catch { }
            // Glasiger Banner-Hintergrund (eigene Paint-Proc, damit Transparenz sauber ist)
            try
            {
                lblBelegInfo.Paint += (s, pe) =>
                {
                    try
                    {
                        var g = pe.Graphics;
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        var r = lblBelegInfo.ClientRectangle;
                        r.Width -= 1; r.Height -= 1;
                        using (var br = new LinearGradientBrush(r, Color.FromArgb(120, 255, 255, 255), Color.FromArgb(75, 255, 255, 255), 90f))
                        {
                            g.FillRectangle(br, r);
                        }
                        using (var pen = new Pen(Color.FromArgb(90, 200, 220, 245), 1f))
                        {
                            g.DrawRectangle(pen, r);
                        }
                        TextRenderer.DrawText(g, lblBelegInfo.Text ?? string.Empty, lblBelegInfo.Font, Rectangle.Inflate(lblBelegInfo.ClientRectangle, -12, -10), lblBelegInfo.ForeColor, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
                    }
                    catch { }
                };
            }
            catch { }
            _cardAbrechnen.Controls.Add(lblBelegInfo);

            // in das innere Panel verschieben
            try
            {
                _cardAbrechnen.Controls.Remove(lblBelegInfo);
                _abrechnenInnerPanel.Controls.Add(lblBelegInfo);
            }
            catch { }

            // Hinweisfeld für Mitarbeiter-Notizen (TNotizen RelTyp=12)
            try
            {
                int notesRightAlignOffsetPx = 38; // ~1cm bei 96dpi
                Func<int> computeNotesLeft = () =>
                {
                    try
                    {
                        int leftClear = pad + 8;
                        int leftLimit = leftClear;
                        try
                        {
                            int wTitle = (_abrechnenCardTitle != null) ? TextRenderer.MeasureText(_abrechnenCardTitle.Text ?? string.Empty, _abrechnenCardTitle.Font).Width : 0;
                            int wEmp = (lblTitel != null) ? TextRenderer.MeasureText(lblTitel.Text ?? string.Empty, lblTitel.Font).Width : 0;
                            int wBal = (lblGuthaben != null) ? TextRenderer.MeasureText(lblGuthaben.Text ?? string.Empty, lblGuthaben.Font).Width : 0;
                            int max = Math.Max(wTitle, Math.Max(wEmp, wBal));
                            leftLimit = pad + 8 + max + 24;
                        }
                        catch { }
                        return Math.Max(pad, leftLimit + notesRightAlignOffsetPx);
                    }
                    catch { return pad; }
                };

                Func<int> computeNotesWidth = () =>
                {
                    try
                    {
                        // Dynamisch: rechts so breit wie möglich, aber Titel/Mitarbeiter/Guthaben nicht überdecken
                        int rightSpace = Math.Max(260, _cardAbrechnen.Width - (pad * 2));
                        int leftClear = pad + 8;
                        int leftLimit = leftClear;
                        try
                        {
                            // rechts neben den linken Texten starten (maximale Textbreite der 3 Labels)
                            int wTitle = (_abrechnenCardTitle != null) ? TextRenderer.MeasureText(_abrechnenCardTitle.Text ?? string.Empty, _abrechnenCardTitle.Font).Width : 0;
                            int wEmp = (lblTitel != null) ? TextRenderer.MeasureText(lblTitel.Text ?? string.Empty, lblTitel.Font).Width : 0;
                            int wBal = (lblGuthaben != null) ? TextRenderer.MeasureText(lblGuthaben.Text ?? string.Empty, lblGuthaben.Font).Width : 0;
                            int max = Math.Max(wTitle, Math.Max(wEmp, wBal));
                            leftLimit = pad + 8 + max + 24; // Gap
                        }
                        catch { }

                        int maxWidth = (_cardAbrechnen.Width - pad) - leftLimit;
                        if (maxWidth < 260) maxWidth = 260;
                        if (maxWidth > 720) maxWidth = 720;

                        // Je länger der Name, desto eher etwas breiter (aber innerhalb maxWidth)
                        int extra = 0;
                        try
                        {
                            string nm = (lblTitel != null ? (lblTitel.Text ?? string.Empty) : string.Empty);
                            extra = Math.Min(180, Math.Max(0, nm.Length - 18) * 8);
                        }
                        catch { extra = 0; }

                        int w = 520 + extra;
                        // breiter nach links zulassen
                        if (w < maxWidth) w = maxWidth;
                        if (w > maxWidth) w = maxWidth;
                        if (w < 360) w = 360;
                        if (w > rightSpace) w = rightSpace;
                        return w;
                    }
                    catch { return 520; }
                };

                // Notizen sollen den gleichen Textstil wie "Personal-Guthaben" haben (aber explizit nicht Bold)
                Font notesFont = null;
                try
                {
                    if (lblGuthaben != null && lblGuthaben.Font != null)
                    {
                        var f = lblGuthaben.Font;
                        notesFont = new Font(f.FontFamily, f.Size, FontStyle.Regular);
                    }
                }
                catch { notesFont = null; }
                if (notesFont == null) notesFont = new Font("Segoe UI Variable", 16F, FontStyle.Regular);

                lblNotizenInfo = new Label
                {
                    Text = "Hinweisfeld wird geladen...",
                    Font = notesFont,
                    // oben rechts im Card-Header (neben Titel/Mitarbeiter/Guthaben)
                    Size = new Size(computeNotesWidth(), 130),
                    Location = new Point(computeNotesLeft(), 24),
                    Anchor = AnchorStyles.Top | AnchorStyles.Right,
                    BackColor = Color.FromArgb(0, 255, 255, 255),
                    ForeColor = Color.FromArgb(15, 23, 42),
                    AutoSize = false,
                    Padding = new Padding(12, 10, 12, 10),
                    TextAlign = ContentAlignment.TopLeft,
                    Visible = false
                };
                // Standard-Text-Rendering des Labels unterdrücken (wir zeichnen selbst)
                try { lblNotizenInfo.Tag = lblNotizenInfo.Text; lblNotizenInfo.Text = string.Empty; } catch { }
                lblNotizenInfo.Paint += (s, pe) =>
                {
                    try
                    {
                        var g = pe.Graphics;
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        var r = lblNotizenInfo.ClientRectangle;
                        // moderner Card-Look: Soft-Shadow + leichter Verlauf + Akzent
                        if (r.Width > 2 && r.Height > 2)
                        {
                            // Soft shadow (subtil)
                            try
                            {
                                for (int k = 8; k >= 1; k--)
                                {
                                    int a = (int)(14f * (k / 8f));
                                    using (var penS = new Pen(Color.FromArgb(a, 0, 0, 0), 2f))
                                    {
                                        var rr = new Rectangle(r.X + (k - 1), r.Y + (k - 1), r.Width - (k - 1) * 2, r.Height - (k - 1) * 2);
                                        g.DrawRectangle(penS, rr);
                                    }
                                }
                            }
                            catch { }

                            var body = new Rectangle(r.Left, r.Top, r.Width - 1, r.Height - 1);
                            using (var br = new LinearGradientBrush(body, Color.FromArgb(110, 255, 255, 255), Color.FromArgb(40, 235, 245, 255), 90f))
                            { g.FillRectangle(br, body); }

                            // Accent bar + small highlight
                            using (var accent = new LinearGradientBrush(new Rectangle(body.Left, body.Top, 8, body.Height),
                                Color.FromArgb(255, 33, 150, 243), Color.FromArgb(200, 13, 71, 161), 90f))
                            { g.FillRectangle(accent, new Rectangle(body.Left, body.Top, 8, body.Height)); }
                            using (var hi = new SolidBrush(Color.FromArgb(70, 255, 255, 255)))
                            { g.FillRectangle(hi, new Rectangle(body.Left + 8, body.Top, body.Width - 8, Math.Max(1, body.Height / 5))); }

                            using (var pen = new Pen(Color.FromArgb(140, 180, 200, 230), 1f))
                            { g.DrawRectangle(pen, body); }
                            // inner contour
                            try
                            {
                                using (var pen2 = new Pen(Color.FromArgb(70, 255, 255, 255), 1f))
                                { g.DrawRectangle(pen2, new Rectangle(body.Left + 1, body.Top + 1, body.Width - 2, body.Height - 2)); }
                            }
                            catch { }
                        }
                        var txt = lblNotizenInfo.Tag as string;
                        var viewRect = Rectangle.Inflate(lblNotizenInfo.ClientRectangle, -16, -10);
                        if (viewRect.Width <= 1 || viewRect.Height <= 1) return;

                        // Draw directly using TextRenderer to match Label rendering (ClearType, no "bitmap look").
                        EnsureNotesScrollLines(txt ?? string.Empty, viewRect.Width);
                        if (_notesScrollLines == null || _notesScrollLines.Count == 0) return;

                        var st2 = g.Save();
                        try
                        {
                            g.SetClip(viewRect);
                            int y0 = viewRect.Top - _notesScrollLineOffsetPx;
                            int yy = y0;
                            int idx = _notesScrollFirstLine;
                            int drawn = 0;
                            int maxDraw = Math.Max(10, (viewRect.Height / Math.Max(1, _notesScrollLineHeightPx)) + 4);
                            while (drawn < maxDraw)
                            {
                                if (idx >= _notesScrollLines.Count) idx = 0;
                                var line = _notesScrollLines[idx] ?? string.Empty;
                                int indentPx = 0;
                                if (line.Length > 0 && line[0] == '\t')
                                {
                                    indentPx = 28; // entspricht optisch ~3 Leerzeichen bei dieser Fontgröße
                                    line = line.Substring(1);
                                }
                                var rLine = new Rectangle(viewRect.Left + indentPx, yy, Math.Max(1, viewRect.Width - indentPx), _notesScrollLineHeightPx);
                                TextRenderer.DrawText(g, line, lblNotizenInfo.Font, rLine, lblNotizenInfo.ForeColor,
                                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding);
                                yy += _notesScrollLineHeightPx;
                                idx++;
                                drawn++;
                                if (yy > viewRect.Bottom + _notesScrollLineHeightPx) break;
                            }
                        }
                        finally { try { g.Restore(st2); } catch { } }

                        // leichte Rundung (visuell) über Region
                        try
                        {
                            if (lblNotizenInfo.Region == null)
                                lblNotizenInfo.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, lblNotizenInfo.Width, lblNotizenInfo.Height, 18, 18));
                        }
                        catch { }
                    }
                    catch { }
                };
                _cardAbrechnen.Controls.Add(lblNotizenInfo);

                // Scroll-Setup (nur wenn nötig aktiv)
                try
                {
                    _notesScrollVelocityPx = 1;
                    _notesScrollPauseMs = 900;
                    _notesScrollPauseRemainingMs = _notesScrollPauseMs;
                    _notesScrollOffsetPx = 0;
                    if (_notesScrollTimer == null)
                    {
                        _notesScrollTimer = new Timer { Interval = 35 };
                        _notesScrollTimer.Tick += (s, e) =>
                        {
                            try
                            {
                                if (lblNotizenInfo == null || !lblNotizenInfo.Visible) { _notesScrollTimer.Stop(); return; }
                                var txt = lblNotizenInfo.Tag as string;
                                if (string.IsNullOrWhiteSpace(txt)) { _notesScrollTimer.Stop(); return; }

                                // Hash change -> reset
                                int h = txt.GetHashCode();
                                if (_notesScrollLastTextHash != h)
                                {
                                    _notesScrollLastTextHash = h;
                                    _notesScrollOffsetPx = 0;
                                    _notesScrollPauseRemainingMs = _notesScrollPauseMs;
                                }

                                var rc = Rectangle.Inflate(lblNotizenInfo.ClientRectangle, -16, -10);
                                if (rc.Width <= 1 || rc.Height <= 1) return;
                                EnsureNotesScrollLines(txt ?? string.Empty, rc.Width);
                                if (_notesScrollLines == null || _notesScrollLines.Count == 0) return;

                                if (_notesScrollPauseRemainingMs > 0)
                                {
                                    _notesScrollPauseRemainingMs -= _notesScrollTimer.Interval;
                                    return;
                                }

                                _notesScrollLineOffsetPx += Math.Max(1, _notesScrollVelocityPx);
                                int lh = Math.Max(1, _notesScrollLineHeightPx);
                                if (_notesScrollLineOffsetPx >= lh)
                                {
                                    _notesScrollLineOffsetPx = 0;
                                    _notesScrollFirstLine++;
                                    if (_notesScrollFirstLine >= _notesScrollLines.Count) _notesScrollFirstLine = 0;
                                }

                                try { lblNotizenInfo.Invalidate(); } catch { }
                            }
                            catch { }
                        };
                    }
                }
                catch { }

                // bei Größenänderung oben rechts halten
                _cardAbrechnen.SizeChanged += (s, e) =>
                {
                    try
                    {
                        if (lblNotizenInfo == null) return;
                        int w = computeNotesWidth();
                        int h = 130;
                        int left = computeNotesLeft();
                        int maxW = Math.Max(260, (_cardAbrechnen.Width - pad) - left);
                        lblNotizenInfo.Size = new Size(Math.Min(w, maxW), h);
                        lblNotizenInfo.Left = left;
                        lblNotizenInfo.Top = 24;
                        try { ResetNotesScrollBitmap(); } catch { }
                        try { lblNotizenInfo.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, lblNotizenInfo.Width, lblNotizenInfo.Height, 16, 16)); } catch { }
                    }
                    catch { }
                };
            }
            catch { }

            // Mehr Abstand zwischen Banner(n) und MwSt-Zeile
            y += 86;

            var b19Init = _details != null ? _details.Betrag19 : 0m;
            var b7Init = _details != null ? _details.Betrag7 : 0m;
            var b0Init = _details != null ? _details.Betrag0 : 0m;

            // MwSt-Zeile: 7%-€ an der gleichen X-Position wie die Summenwerte ausrichten.
            int mwstGap = 6;
            int amountW = 170;
            int gap2 = 6;
            int labelW = 240;
            try
            {
                int w1 = TextRenderer.MeasureText("Noch zu zahlen:", fontStrong).Width;
                int w2 = TextRenderer.MeasureText("Eingezahlt:", fontStrong).Width;
                int w3 = TextRenderer.MeasureText("Summe:", fontStrong).Width;
                labelW = Math.Max(140, Math.Max(w1, Math.Max(w2, w3)) + 8);
            }
            catch { labelW = 240; }

            int amountLeftX = pad + labelW + gap2;
            int euroX = amountLeftX + amountW - TextRenderer.MeasureText("€", fontAmounts).Width;

            lblB19 = new Label { Text = $"19%: {b19Init:C2}", Font = fontBody, ForeColor = text, Location = new Point(pad, y), AutoSize = true, BackColor = Color.Transparent };
            _abrechnenInnerPanel.Controls.Add(lblB19);

            lblB7 = new Label { Text = $"7%: {b7Init:C2}", Font = fontBody, ForeColor = text, AutoSize = true, BackColor = Color.Transparent };
            _abrechnenInnerPanel.Controls.Add(lblB7);

            // shift lblB7 so that its '€' aligns with euroX
            int idxEuro7 = lblB7.Text != null ? lblB7.Text.IndexOf('€') : -1;
            int euroOffset7 = 0;
            try
            {
                if (idxEuro7 > 0)
                {
                    euroOffset7 = TextRenderer.MeasureText(lblB7.Text.Substring(0, idxEuro7), lblB7.Font).Width;
                }
            }
            catch { euroOffset7 = 0; }

            int b7AlignOffset = 4; // kleines Stück nach rechts
            int b7Left = euroX - euroOffset7 + b7AlignOffset;
            int minB7Left = lblB19.Right + mwstGap;
            if (b7Left < minB7Left) b7Left = minB7Left;
            lblB7.Location = new Point(b7Left, y);

            int gap19to7 = lblB7.Left - lblB19.Right;

            lblB0 = new Label { Text = $"0%: {b0Init:C2}", Font = fontBody, ForeColor = text, AutoSize = true, BackColor = Color.Transparent };
            _abrechnenInnerPanel.Controls.Add(lblB0);
            lblB0.Location = new Point(lblB7.Right + gap19to7, y);

            var sepMwst = new Panel
            {
                BackColor = Color.FromArgb(229, 231, 235),
                Location = new Point(pad, y + 34),
                Size = new Size(_abrechnenInnerPanel.Width - pad * 2, 1),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            _abrechnenInnerPanel.Controls.Add(sepMwst);

            y += 52;
            var sumInit = (_details != null ? _details.SummeZuZahlen : 0m);

            // zweispaltiges Layout, damit Eurozeichen fluchten
            // (amountW/gap2/labelW sind oben bereits bestimmt)
            int rowW = labelW + gap2 + amountW;

            // (Separator für Amount-Spalte nicht mehr nötig, Linien kommen als Vollbreite)
            pnlSummeSeparator = null;

            pnlSummeRow = new Panel
            {
                Location = new Point(pad, y),
                Size = new Size(rowW, 40),
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            lblSummeText = new Label
            {
                Text = "Summe:",
                Font = fontStrong,
                ForeColor = text,
                Location = new Point(0, 0),
                Size = new Size(labelW, 40),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            lblSummeValue = new Label
            {
                Text = sumInit.ToString("C2"),
                Font = fontAmounts,
                ForeColor = (sumInit < 0m) ? danger : text,
                Location = new Point(labelW + gap2, 0),
                Size = new Size(amountW, 40),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleRight
            };
            pnlSummeRow.Controls.Add(lblSummeText);
            pnlSummeRow.Controls.Add(lblSummeValue);
            _abrechnenInnerPanel.Controls.Add(pnlSummeRow);
            lblSumme = lblSummeValue;

            y += 46;
            pnlEingezahltRow = new Panel
            {
                Location = new Point(pad, y),
                Size = new Size(rowW, 40),
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            lblEingezahltText = new Label
            {
                Text = "Eingezahlt:",
                Font = fontStrong,
                ForeColor = text,
                Location = new Point(0, 0),
                Size = new Size(labelW, 40),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            lblEingezahltValue = new Label
            {
                Text = _eingezahltSession.ToString("C2"),
                Font = fontAmounts,
                ForeColor = text,
                Location = new Point(labelW + gap2, 0),
                Size = new Size(amountW, 40),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleRight
            };
            pnlEingezahltRow.Controls.Add(lblEingezahltText);
            pnlEingezahltRow.Controls.Add(lblEingezahltValue);
            _abrechnenInnerPanel.Controls.Add(pnlEingezahltRow);
            lblEingezahlt = lblEingezahltValue;

            // gleicher Abstand wie nach "Summe"
            y += 62;
            var nochInit = Math.Max(0m, sumInit - _eingezahltSession);
            nochInit = Math.Max(0m, nochInit - _personalGuthaben);
            pnlNochRow = new Panel
            {
                Location = new Point(pad, y),
                Size = new Size(rowW, 40),
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            lblNochText = new Label
            {
                Text = "Noch zu zahlen:",
                Font = fontStrong,
                ForeColor = text,
                Location = new Point(0, 0),
                Size = new Size(labelW, 40),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            lblNochValue = new Label
            {
                Text = nochInit.ToString("C2"),
                Font = fontAmounts,
                ForeColor = (nochInit > 0m) ? danger : success,
                Location = new Point(labelW + gap2, 0),
                Size = new Size(amountW, 40),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleRight
            };
            pnlNochRow.Controls.Add(lblNochText);
            pnlNochRow.Controls.Add(lblNochValue);
            _abrechnenInnerPanel.Controls.Add(pnlNochRow);
            lblNoch = lblNochValue;
            try { lblNoch.ForeColor = (nochInit > 0m) ? danger : success; } catch { }

            // Strich auch über "Noch zu zahlen" (durchgehend)
            try
            {
                var pnlNochSeparator = new Panel
                {
                    BackColor = Color.FromArgb(229, 231, 235),
                    Location = new Point(pad, y - 10),
                    Size = new Size(_abrechnenInnerPanel.Width - pad * 2, 1),
                    Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
                };
                _abrechnenInnerPanel.Controls.Add(pnlNochSeparator);
                _abrechnenInnerPanel.SizeChanged += (s, e) =>
                {
                    try
                    {
                        pnlNochSeparator.Top = pnlNochRow.Top - 10;
                        pnlNochSeparator.Left = pad;
                        pnlNochSeparator.Width = _abrechnenInnerPanel.Width - pad * 2;
                    }
                    catch { }
                };
            }
            catch { }

            // Bei Resize im Abrechnen-Tab die Betrags-Spalte + Separator sauber ausrichten
            try
            {
                _abrechnenInnerPanel.SizeChanged += (s, e) =>
                {
                    try
                    {
                        if (pnlSummeRow == null || pnlEingezahltRow == null || pnlNochRow == null) return;

                        int newAmountW = 170;
                        int newGap2 = 10;
                        int newLabelW = 240;
                        try
                        {
                            int w1 = TextRenderer.MeasureText("Noch zu zahlen:", fontStrong).Width;
                            int w2 = TextRenderer.MeasureText("Eingezahlt:", fontStrong).Width;
                            int w3 = TextRenderer.MeasureText("Summe:", fontStrong).Width;
                            newLabelW = Math.Max(140, Math.Max(w1, Math.Max(w2, w3)) + 8);
                        }
                        catch { newLabelW = 240; }

                        int newRowW = newLabelW + newGap2 + newAmountW;
                        pnlSummeRow.Width = newRowW;
                        pnlEingezahltRow.Width = newRowW;
                        pnlNochRow.Width = newRowW;

                        if (lblSummeText != null) lblSummeText.Width = newLabelW;
                        if (lblEingezahltText != null) lblEingezahltText.Width = newLabelW;
                        if (lblNochText != null) lblNochText.Width = newLabelW;

                        int xVal = newLabelW + newGap2;
                        if (lblSummeValue != null) { lblSummeValue.Left = xVal; lblSummeValue.Width = newAmountW; }
                        if (lblEingezahltValue != null) { lblEingezahltValue.Left = xVal; lblEingezahltValue.Width = newAmountW; }
                        if (lblNochValue != null) { lblNochValue.Left = xVal; lblNochValue.Width = newAmountW; }

                        // Anpassung MwSt-Zeile: 7%-€ an Betrags-Spalte ausrichten
                        try
                        {
                            int mwstGap2 = 6;
                            int b7AlignOffset2 = 4; // kleines Stück nach rechts
                            int newAmountW2 = 170;
                            int newGap22 = 10;
                            int newLabelW2 = 240;
                            try
                            {
                                int w1 = TextRenderer.MeasureText("Noch zu zahlen:", fontStrong).Width;
                                int w2 = TextRenderer.MeasureText("Eingezahlt:", fontStrong).Width;
                                int w3 = TextRenderer.MeasureText("Summe:", fontStrong).Width;
                                newLabelW2 = Math.Max(140, Math.Max(w1, Math.Max(w2, w3)) + 8);
                            }
                            catch { newLabelW2 = 240; }

                            int amountLeftX2 = pad + newLabelW2 + newGap22;
                            int euroX2 = amountLeftX2 + newAmountW2 - TextRenderer.MeasureText("€", fontAmounts).Width;

                            if (lblB19 != null)
                            {
                                lblB19.AutoSize = true;
                                lblB19.Left = pad;
                            }
                            if (lblB7 != null)
                            {
                                lblB7.AutoSize = true;
                                int idxEuro7_2 = lblB7.Text != null ? lblB7.Text.IndexOf('€') : -1;
                                int euroOffset7_2 = 0;
                                try
                                {
                                    if (idxEuro7_2 > 0)
                                    {
                                        euroOffset7_2 = TextRenderer.MeasureText(lblB7.Text.Substring(0, idxEuro7_2), lblB7.Font).Width;
                                    }
                                }
                                catch { euroOffset7_2 = 0; }

                                int b7Left2 = euroX2 - euroOffset7_2 + b7AlignOffset2;
                                int minB7Left2 = (lblB19 != null ? lblB19.Right : pad) + mwstGap2;
                                if (b7Left2 < minB7Left2) b7Left2 = minB7Left2;
                                lblB7.Left = b7Left2;
                            }
                            if (lblB0 != null)
                            {
                                lblB0.AutoSize = true;
                                int gap19to7_2 = 0;
                                try { if (lblB19 != null && lblB7 != null) gap19to7_2 = lblB7.Left - lblB19.Right; } catch { gap19to7_2 = mwstGap2; }
                                if (gap19to7_2 <= 0) gap19to7_2 = mwstGap2;
                                lblB0.Left = (lblB7 != null ? lblB7.Right : (lblB19 != null ? lblB19.Right : pad)) + gap19to7_2;
                            }
                        }
                        catch { }
                    }
                    catch { }
                };
            }
            catch { }

            // Buttons
            y += 92;
            btnAbrechnen = new Button
            {
                Text = "Buchen",
                Location = new Point(pad, y),
                Size = new Size(200, 56),
                Font = new Font("Segoe UI Variable", 20F, FontStyle.Bold),
                BackColor = Color.FromArgb(160, 160, 160),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Enabled = false
            };
            ApplyModernButtonStyle(btnAbrechnen, Color.FromArgb(160, 160, 160), Color.FromArgb(120, 120, 120));
            btnAbrechnen.Click += btnAbrechnen_Click;
            _abrechnenInnerPanel.Controls.Add(btnAbrechnen);

            btnCreatePayment = new Button
            {
                Text = "Zahlung",
                // Neben "Buchen" mit identischer Höhe (wie im Screenshot)
                Location = new Point(pad + 240, y),
                Size = new Size(200, 56),
                Font = new Font("Segoe UI Variable", 20F, FontStyle.Bold),
                BackColor = Color.FromArgb(33, 150, 243),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Visible = PaymentSettingsStore.IsEnabled()
            };
            ApplyModernButtonStyle(btnCreatePayment, Color.FromArgb(33, 150, 243), Color.FromArgb(0, 122, 204));
            btnCreatePayment.Click += async (s, e) =>
            {
                using (var dlg = new CreatePaymentForm())
                {
                    dlg.StartPosition = FormStartPosition.CenterParent;
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        try { await LadeAlleAbrechenbarenItemsAsync(); SelectNextItemOrClear(); UpdateAbrechnenSummaries(); } catch { }
                    }
                }
            };
            _abrechnenInnerPanel.Controls.Add(btnCreatePayment);

            // Manuell UI nach oben rechts neben Zahlung (standardmäßig ausgeblendet; per Strg+M)
            int manW = 180;
            int manH = 46;
            int manGap = 12;
            int yMan = y + (btnCreatePayment.Height - manH) / 2;
            nudManuell = new NumericUpDown
            {
                Location = new Point(btnCreatePayment.Right + manGap, yMan),
                Size = new Size(manW, manH),
                DecimalPlaces = 2,
                Minimum = -10000,
                Maximum = 10000,
                Increment = 5,
                Font = new Font("Segoe UI Variable", 16F),
                Visible = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            _abrechnenInnerPanel.Controls.Add(nudManuell);

            btnManuellAdd = new Button
            {
                Text = "Manuell hinzufügen",
                Location = new Point(nudManuell.Right + manGap, yMan),
                Size = new Size(260, manH),
                Font = new Font("Segoe UI Variable", 16F, FontStyle.Bold),
                BackColor = Color.FromArgb(33, 150, 243),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Visible = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnManuellAdd.FlatAppearance.BorderSize = 0;
            try { btnManuellAdd.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, btnManuellAdd.Width, btnManuellAdd.Height, 12, 12)); } catch { }
            btnManuellAdd.Click += btnManuellAdd_Click;
            _abrechnenInnerPanel.Controls.Add(btnManuellAdd);

            // beim Resize rechts neben Zahlung halten
            try
            {
                _abrechnenInnerPanel.SizeChanged += (s, e) =>
                {
                    try
                    {
                        if (btnCreatePayment == null || nudManuell == null || btnManuellAdd == null) return;
                        int y2 = btnCreatePayment.Top + (btnCreatePayment.Height - manH) / 2;
                        nudManuell.Top = y2;
                        btnManuellAdd.Top = y2;
                        nudManuell.Left = btnCreatePayment.Right + manGap;
                        btnManuellAdd.Left = nudManuell.Right + manGap;
                    }
                    catch { }
                };
            }
            catch { }

            // y unverändert lassen, keine zusätzlichen Buttons hier

            y += 78;

            if (_ssp != null && !string.IsNullOrWhiteSpace(_ssp.ComPort))
                UpdateAbrechnenSummaries();
        }

        private void UpdateBusyUI()
        {
            _payoutInProgress = _notesPayoutInProgress || _coinsPayoutInProgress;
            bool overrideUnlock = IsAnyDeviceStuckAndOthersIdle();
            bool userLocked = (_payoutInProgress || _bookingInProgress || AdminMode.IsOpen || AnyCoinEinwurf()) && !overrideUnlock;
            if (userLocked && _busyLockSince == DateTime.MinValue) _busyLockSince = DateTime.UtcNow; else if (!userLocked) _busyLockSince = DateTime.MinValue;
            try
            {
                // Nur Busy-Lock setzen; die finale Enabled-Entscheidung + Optik macht UpdateAuszahlenEnabled()
                if (btnAuszahlen != null) btnAuszahlen.Enabled = !userLocked;
                if (btnAbmelden != null)
                {
                    bool newEnabled = !userLocked;
                    bool prevEnabled = _lastAbmeldenEnabled;
                    btnAbmelden.Enabled = newEnabled;

                    // Optik: bei Disabled grau zeichnen (ModernButton_Paint nutzt b.Tag als Gradient-Farben)
                    try
                    {
                        if (newEnabled)
                        {
                            btnAbmelden.Tag = new Tuple<Color, Color>(UiTheme.DangerStart, UiTheme.DangerEnd);
                            btnAbmelden.ForeColor = Color.White;
                        }
                        else
                        {
                            var g1 = Color.FromArgb(170, 170, 170);
                            var g2 = Color.FromArgb(130, 130, 130);
                            btnAbmelden.Tag = new Tuple<Color, Color>(g1, g2);
                            btnAbmelden.ForeColor = Color.White;
                        }
                        btnAbmelden.Invalidate();
                    }
                    catch { }

                    if (newEnabled && !prevEnabled)
                    {
                        try { BusyAnimationManager.End("abmelden enabled"); } catch { }
                        try { if (BusyAnimationManager.IsActive) BusyAnimationManager.EndForce(); } catch { }
                    }
                    _lastAbmeldenEnabled = newEnabled;
                }
                if (btnSchichtAuswahl != null) btnSchichtAuswahl.Enabled = !userLocked;
                UpdatePlusMinusEnabled();
                UpdateBuchenEnabled();
                UpdateAuszahlenEnabled();
            }
            catch { }
        }

        private bool IsAnyDeviceStuckAndOthersIdle()
        {
            try
            {
                if (AdminMode.IsOpen) return false;
                var nv1 = _ssp; var nv2 = Program.NV2002Instance;
                bool nv1NonOkLong = false, nv2NonOkLong = false;
                bool nv1Idle = false, nv2Idle = false;
                try { if (nv1 != null) { nv1Idle = nv1.IsIdleState; nv1NonOkLong = nv1.LastResponseNotOkUtc != DateTime.MinValue && (DateTime.UtcNow - nv1.LastResponseNotOkUtc).TotalSeconds >= 5; } } catch { }
                try { if (nv2 != null) { nv2Idle = nv2.IsIdleState; nv2NonOkLong = nv2.LastResponseNotOkUtc != DateTime.MinValue && (DateTime.UtcNow - nv2.LastResponseNotOkUtc).TotalSeconds >= 5; } } catch { }
                bool coinsIdle = false;
                try { coinsIdle = (!_coinsPayoutInProgress && !AnyCoinEinwurf()); } catch { coinsIdle = true; }
                // at least one NV stuck and all other devices idle
                if (nv1NonOkLong && (nv2 == null || nv2Idle) && coinsIdle) return true;
                if (nv2NonOkLong && (nv1 == null || nv1Idle) && coinsIdle) return true;
                return false;
            }
            catch { return false; }
        }

        private void BusyUnlockWatchdog()
        {
            try
            {
                // Rule 1: Configured timeout-based unlock
                if (_busyUnlockTimeoutSec > 0)
                {
                    bool userLocked = (_notesPayoutInProgress || _coinsPayoutInProgress || _bookingInProgress) && !AdminMode.IsOpen;
                    if (userLocked && _busyLockSince != DateTime.MinValue)
                    {
                        var elapsed = (DateTime.UtcNow - _busyLockSince).TotalSeconds;
                        if (elapsed >= _busyUnlockTimeoutSec)
                        {
                            _notesPayoutInProgress = false;
                            _coinsPayoutInProgress = false;
                            _payoutInProgress = false;
                            _bookingInProgress = false;
                            _activeNotePayoutDevices = 0;
                            _activeCoinPayoutDevices = 0;
                            _busyLockSince = DateTime.MinValue;
                            UpdateBusyUI();
                            try { MessageBox.Show(this, "Timeout erreicht – Bedienung wieder freigegeben.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information); } catch { }
                        }
                    }
                }

                // Rule 2: Cooperative unlock when one device stuck non-OK >5s but others are idle
                if (!AdminMode.IsOpen)
                {
                    var nv1 = _ssp;
                    var nv2 = Program.NV2002Instance;
                    bool nv1NonOkLong = false, nv2NonOkLong = false;
                    bool nv1Idle = false, nv2Idle = false;
                    try { if (nv1 != null) { nv1Idle = nv1.IsIdleState; nv1NonOkLong = nv1.LastResponseNotOkUtc != DateTime.MinValue && (DateTime.UtcNow - nv1.LastResponseNotOkUtc).TotalSeconds >= 5; } } catch { }
                    try { if (nv2 != null) { nv2Idle = nv2.IsIdleState; nv2NonOkLong = nv2.LastResponseNotOkUtc != DateTime.MinValue && (DateTime.UtcNow - nv2.LastResponseNotOkUtc).TotalSeconds >= 5; } } catch { }

                    bool coinsIdle = true;
                    try
                    {
                        // Treat coin validators as idle when not dispensing and not indicating Einwurf
                        coinsIdle = !_coinsPayoutInProgress && !AnyCoinEinwurf();
                    }
                    catch { }

                    bool anyNvStuck = nv1NonOkLong || nv2NonOkLong;
                    bool othersIdle = coinsIdle && ((nv1NonOkLong && (nv2 == null || nv2Idle)) || (nv2NonOkLong && (nv1 == null || nv1Idle)) || (!nv1NonOkLong && !nv2NonOkLong));

                    if (anyNvStuck && othersIdle)
                    {
                        _notesPayoutInProgress = false;
                        _payoutInProgress = false;
                        _activeNotePayoutDevices = 0;
                        // Do not change coin payout flags here; allow coin operations
                        UpdateBusyUI();
                    }
                }
            }
            catch { }
        }

        private void UpdateAuszahlenEnabled()
        {
            try
            {
                if (btnAuszahlen == null) return;
                bool userLocked = _payoutInProgress || _bookingInProgress || AdminMode.IsOpen || AnyCoinEinwurf();
                if (userLocked)
                {
                    btnAuszahlen.Enabled = false;
                }
                else
                {
                    decimal sumNotes = 0m; for (int i = 0; i < scheinWerte.Length; i++) sumNotes += scheinWerte[i] * auswahlAnzahl[i];
                    decimal sumCoins = 0m; for (int i = 0; i < muenzWerte.Length; i++) sumCoins += (muenzWerte[i] * auswahlAnzahlMuenzen[i]) / 100m;
                    decimal sum = sumNotes + sumCoins;
                    decimal maxVerfuegbar = _eingezahltSession + _personalGuthaben;
                    btnAuszahlen.Enabled = (sum > 0m) && (sum <= maxVerfuegbar);
                }

                // Optik passend zur finalen Enabled-Logik
                try
                {
                    if (btnAuszahlen.Enabled)
                    {
                        btnAuszahlen.Tag = new Tuple<Color, Color>(Color.FromArgb(46, 125, 50), Color.FromArgb(27, 94, 32));
                        btnAuszahlen.ForeColor = Color.White;
                    }
                    else
                    {
                        var g1 = Color.FromArgb(170, 170, 170);
                        var g2 = Color.FromArgb(130, 130, 130);
                        btnAuszahlen.Tag = new Tuple<Color, Color>(g1, g2);
                        btnAuszahlen.ForeColor = Color.White;
                    }
                    btnAuszahlen.Invalidate();
                }
                catch { }
            }
            catch { }
        }

        private void UpdateBuchenEnabled()
        {
            try
            {
                bool enabled = true;
                if (btnAbmelden != null && !btnAbmelden.Enabled) enabled = false;
                bool manualMode = (nudManuell != null && nudManuell.Visible) || (btnManuellAdd != null && btnManuellAdd.Visible);
                if (manualMode)
                {
                    if (btnAbrechnen != null)
                    {
                        btnAbrechnen.Enabled = enabled;
                        try { btnAbrechnen.BackColor = enabled ? Color.FromArgb(46, 125, 50) : Color.FromArgb(160, 160, 160); btnAbrechnen.ForeColor = Color.White; } catch { }
                    }
                    return;
                }
                if (enabled)
                {
                    decimal sum = 0m; decimal noch = 0m;
                    if (_currentAuszahlungRow != null)
                    {
#if DEBUG
                        // im Debugger: immer aktivieren
                        enabled = true;
#else
                        decimal raw19 = Math.Abs(Convert.ToDecimal(_currentAuszahlungRow["Betrag19"]));
                        decimal raw7 = Math.Abs(Convert.ToDecimal(_currentAuszahlungRow["Betrag7"]));
                        decimal raw0 = Math.Abs(Convert.ToDecimal(_currentAuszahlungRow["Betrag0"]));
                        if (_currentZahlungIstEinzahlung)
                        {
                            sum = raw19 + raw7 + raw0;
                            var rest = Math.Max(0m, sum - _eingezahltSession);
                            noch = Math.Max(0m, rest - _personalGuthaben);
                        }
                        else
                        {
                            sum = -(raw19 + raw7 + raw0);
                            noch = 0m;
                        }
#endif
                    }
                    else if (_details != null)
                    {
                        decimal offen19 = _details.Betrag19;
                        decimal offen7 = _details.Betrag7;
                        decimal offen0 = _details.Betrag0;
                        sum = offen19 + offen7 + offen0;
                        var rest = Math.Max(0m, sum - _eingezahltSession);
                        noch = Math.Max(0m, rest - _personalGuthaben);
                    }
                    if (sum == 0m) enabled = false;
                    if (noch != 0m) enabled = false;
                }
                if (btnAbrechnen != null)
                {
                    btnAbrechnen.Enabled = enabled;
                    try
                    {
                        if (enabled)
                        {
                            btnAbrechnen.BackColor = Color.FromArgb(46, 125, 50);
                            btnAbrechnen.Tag = new Tuple<Color, Color>(Color.FromArgb(46, 125, 50), Color.FromArgb(27, 94, 32));
                        }
                        else
                        {
                            btnAbrechnen.BackColor = Color.FromArgb(160, 160, 160);
                            btnAbrechnen.Tag = new Tuple<Color, Color>(Color.FromArgb(160, 160, 160), Color.FromArgb(120, 120, 120));
                        }
                        btnAbrechnen.ForeColor = Color.White;
                        btnAbrechnen.Invalidate();
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void BuildWechselnTab()
        {
            tabWechseln.BackColor = Color.Transparent;

            var host = (Control)(_wechselnSurface ?? tabWechseln);
            // Build UI into the surface so the form background can shine through.
            // The TabPage itself stays transparent; padding must be applied to the host.
            try { tabWechseln.Padding = new Padding(0); } catch { }
            try
            {
                if (!ReferenceEquals(host, tabWechseln))
                {
                    tabWechseln.Controls.Clear();
                    host.Dock = DockStyle.Fill;
                    tabWechseln.Controls.Add(host);
                }
            }
            catch { }

            try { host.Padding = new Padding(18); } catch { }
            try { host.BackColor = Color.Transparent; } catch { }
            try { host.Controls.Clear(); } catch { }

            // Background glass layer (like Abrechnen): fills the whole tab client area so there is
            // no "empty" transparent zone near the tab header / former border area.
            Panel glassLayer = null;
            try
            {
                glassLayer = new Panel
                {
                    Location = new Point(0, 0),
                    Size = new Size(host.ClientSize.Width, host.ClientSize.Height),
                    Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                    BackColor = Color.Transparent
                };

                glassLayer.Paint += (s, e) =>
                {
                    try
                    {
                        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        var r = glassLayer.ClientRectangle;
                        if (r.Width <= 1 || r.Height <= 1) return;

                        var fill = Color.FromArgb(70, 255, 255, 255);
                        var fill2 = Color.FromArgb(35, 255, 255, 255);
                        var glassBorder = Color.FromArgb(90, 180, 200, 230);

                        using (var br = new LinearGradientBrush(r, fill, fill2, 90f))
                        {
                            e.Graphics.FillRectangle(br, r);
                        }

                        using (var pen = new Pen(glassBorder, 1f))
                        {
                            var rr2 = r;
                            rr2.Width -= 1;
                            rr2.Height -= 1;
                            e.Graphics.DrawRectangle(pen, rr2);
                        }
                    }
                    catch { }
                };

                try
                {
                    glassLayer.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, glassLayer.Width, glassLayer.Height, 18, 18));
                }
                catch { }

                host.Controls.Add(glassLayer);
                try { glassLayer.SendToBack(); } catch { }
                try { glassLayer.Enabled = false; } catch { }

                host.SizeChanged += (s, e) =>
                {
                    try
                    {
                        if (glassLayer == null) return;
                        try { glassLayer.Region?.Dispose(); } catch { }
                        try { glassLayer.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, glassLayer.Width, glassLayer.Height, 18, 18)); } catch { }
                        glassLayer.Invalidate();
                    }
                    catch { }
                };
            }
            catch { }

            Color primary = Color.FromArgb(0, 122, 204);
            Color danger = Color.FromArgb(211, 47, 47);
            Color success = Color.FromArgb(46, 125, 50);
            // Less transparent surfaces so cards/footer stand out more
            Color surface = Color.FromArgb(210, 245, 250, 255);
            Color surface2 = Color.FromArgb(185, 230, 240, 255);
            Color innerSurface = Color.FromArgb(175, 255, 255, 255);
            Color border = Color.FromArgb(90, 180, 200, 230);
            Color subText = Color.FromArgb(55, 65, 81);

            var fontTitle = new Font("Segoe UI Variable", 18F, FontStyle.Bold);
            var fontRow = new Font("Segoe UI Variable", 18F, FontStyle.Regular);
            var fontCount = new Font("Segoe UI Variable", 20F, FontStyle.Bold);
            var fontBtn = new Font("Segoe UI Variable", 16F, FontStyle.Bold);

            Func<Control, int, int, int, int, int, int, Region> rr = (ctrl, x, y, w, h, r1, r2) =>
            {
                try { return Region.FromHrgn(CreateRoundRectRgn(x, y, x + w, y + h, r1, r2)); } catch { return null; }
            };

            // --- Layout: zwei Cards (Münzen / Scheine) + Footer ---
            int cardY = 18;
            int cardH = 650;
            int gap = 14;
            int cardW = (host.ClientSize.Width - host.Padding.Left - host.Padding.Right - gap) / 2;
            if (cardW < 520) cardW = 520;
            int leftX = host.Padding.Left;
            int rightX = leftX + cardW + gap;

            Panel MakeCard(string title, int x)
            {
                var card = new Panel
                {
                    Location = new Point(x, cardY),
                    Size = new Size(cardW, cardH),
                    BackColor = Color.Transparent
                };
                try { card.Region = rr(card, 0, 0, card.Width, card.Height, 18, 18); } catch { }
                card.Paint += (s, e) =>
                {
                    try
                    {
                        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        var rFill = card.ClientRectangle;
                        using (var br = new LinearGradientBrush(rFill, surface, surface2, 90f))
                        {
                            e.Graphics.FillRectangle(br, rFill);
                        }
                        try
                        {
                            var top = new Rectangle(rFill.Left, rFill.Top, rFill.Width, Math.Max(1, rFill.Height / 3));
                            using (var gloss = new LinearGradientBrush(top, Color.FromArgb(70, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                            {
                                e.Graphics.FillRectangle(gloss, top);
                            }
                        }
                        catch { }
                        using (var pen = new Pen(border, 1f))
                        {
                            var r = card.ClientRectangle;
                            r.Width -= 1; r.Height -= 1;
                            e.Graphics.DrawRectangle(pen, r);
                        }
                    }
                    catch { }
                };

                var lbl = new Label
                {
                    Text = title,
                    Font = fontTitle,
                    ForeColor = Color.FromArgb(17, 24, 39),
                    AutoSize = false,
                    Location = new Point(18, 14),
                    Size = new Size(card.Width - 36, 34),
                    BackColor = Color.Transparent
                };
                card.Controls.Add(lbl);

                var sep = new Panel
                {
                    BackColor = border,
                    Location = new Point(18, 56),
                    Size = new Size(card.Width - 36, 1)
                };
                card.Controls.Add(sep);

                host.Controls.Add(card);
                return card;
            }

            Button MakeIconButton(string text, Color back, Color fore)
            {
                var b = new Button
                {
                    Text = text,
                    Size = new Size(44, 44),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = back,
                    ForeColor = fore,
                    Font = new Font("Segoe UI Variable", 18F, FontStyle.Bold),
                    TabStop = false
                };
                b.FlatAppearance.BorderSize = 0;
                b.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(back);
                b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(back);
                try { b.Region = rr(b, 0, 0, b.Width, b.Height, 12, 12); } catch { }
                return b;
            }

            Label MakeCountLabel()
            {
                var lbl = new Label
                {
                    Text = "0",
                    TextAlign = ContentAlignment.MiddleCenter,
                    Size = new Size(70, 44),
                    BorderStyle = BorderStyle.FixedSingle,
                    BackColor = Color.FromArgb(248, 250, 252),
                    ForeColor = Color.FromArgb(17, 24, 39),
                    Font = fontCount
                };
                return lbl;
            }

            var cardCoins = MakeCard("Münzen", leftX);
            var cardNotes = MakeCard("Scheine", rightX);
            try { cardCoins.BringToFront(); } catch { }
            try { cardNotes.BringToFront(); } catch { }

            // Tabellenkopf in Cards
            void AddHeaderRow(Panel card)
            {
                var y = 68;
                var h1 = new Label { Text = "Wert", Font = new Font(fontRow, FontStyle.Bold), ForeColor = subText, AutoSize = false, Location = new Point(18, y), Size = new Size(120, 32), BackColor = Color.Transparent };
                var h2 = new Label { Text = "Anzahl", Font = new Font(fontRow, FontStyle.Bold), ForeColor = subText, AutoSize = false, Location = new Point(170, y), Size = new Size(160, 32), BackColor = Color.Transparent };
                var h3 = new Label { Text = "Vorrätig", Font = new Font(fontRow, FontStyle.Bold), ForeColor = subText, AutoSize = false, Location = new Point(card.Width - 170, y), Size = new Size(150, 32), TextAlign = ContentAlignment.MiddleRight, BackColor = Color.Transparent };
                card.Controls.Add(h1);
                card.Controls.Add(h2);
                card.Controls.Add(h3);
            }
            AddHeaderRow(cardCoins);
            AddHeaderRow(cardNotes);

            // Rows
            int startY = 106;
            int rowH = 62;

            // Coins
            for (int i = 0; i < muenzWerte.Length; i++)
            {
                int rowY = startY + i * rowH;

                var pb = new PictureBox
                {
                    Location = new Point(18, rowY + 6),
                    Size = new Size(50, 50),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BorderStyle = BorderStyle.None,
                    BackColor = Color.Transparent
                };
                try { pb.Image = GetCoinImageByIndex(i); } catch { }
                cardCoins.Controls.Add(pb);
                picMuenzen[i] = pb;

                var bMinus = MakeIconButton("–", danger, Color.White);
                bMinus.Tag = i;
                bMinus.Location = new Point(170, rowY + 9);
                bMinus.Click += BtnMinusMuenzen_Click;
                cardCoins.Controls.Add(bMinus);
                btnMinusMuenzen[i] = bMinus;

                var lbl = MakeCountLabel();
                lbl.Location = new Point(220, rowY + 9);
                cardCoins.Controls.Add(lbl);
                lblAnzahlMuenzen[i] = lbl;

                var bPlus = MakeIconButton("+", primary, Color.White);
                bPlus.Tag = i;
                bPlus.Location = new Point(296, rowY + 9);
                bPlus.Click += BtnPlusMuenzen_Click;
                cardCoins.Controls.Add(bPlus);
                btnPlusMuenzen[i] = bPlus;

                var lAvail = new Label
                {
                    Text = "0",
                    AutoSize = false,
                    Location = new Point(cardCoins.Width - 200, rowY + 14),
                    Size = new Size(180, 30),
                    TextAlign = ContentAlignment.MiddleRight,
                    ForeColor = subText,
                    Font = fontRow,
                    BackColor = Color.Transparent
                };
                cardCoins.Controls.Add(lAvail);
                lblVerfuegbarMuenzen[i] = lAvail;

                // subtle row separator
                try
                {
                    if (i < muenzWerte.Length - 1)
                    {
                        var sep = new Panel { BackColor = Color.FromArgb(242, 244, 247), Location = new Point(18, rowY + rowH - 2), Size = new Size(cardCoins.Width - 36, 1) };
                        cardCoins.Controls.Add(sep);
                    }
                }
                catch { }
            }

            // Notes
            for (int i = 0; i < scheinWerte.Length; i++)
            {
                int rowY = startY + i * rowH;

                var pb = new PictureBox
                {
                    Location = new Point(18, rowY + 10),
                    Size = new Size(110, 42),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BorderStyle = BorderStyle.None,
                    BackColor = Color.Transparent
                };
                try { pb.Image = GetNoteImageByIndex(i); } catch { }
                cardNotes.Controls.Add(pb);
                picScheine[i] = pb;

                var bMinus = MakeIconButton("–", danger, Color.White);
                bMinus.Tag = i;
                bMinus.Location = new Point(170, rowY + 9);
                bMinus.Click += BtnMinus_Click;
                cardNotes.Controls.Add(bMinus);
                btnMinus[i] = bMinus;

                var lbl = MakeCountLabel();
                lbl.Location = new Point(220, rowY + 9);
                cardNotes.Controls.Add(lbl);
                lblAnzahl[i] = lbl;

                var bPlus = MakeIconButton("+", primary, Color.White);
                bPlus.Tag = i;
                bPlus.Location = new Point(296, rowY + 9);
                bPlus.Click += BtnPlus_Click;
                cardNotes.Controls.Add(bPlus);
                btnPlus[i] = bPlus;

                var lAvail = new Label
                {
                    Text = "0",
                    AutoSize = false,
                    Location = new Point(cardNotes.Width - 200, rowY + 14),
                    Size = new Size(180, 30),
                    TextAlign = ContentAlignment.MiddleRight,
                    ForeColor = subText,
                    Font = fontRow,
                    BackColor = Color.Transparent
                };
                cardNotes.Controls.Add(lAvail);
                lblVerfuegbar[i] = lAvail;

                if (scheinWerte[i] >= 100)
                {
                    pb.Visible = false;
                    bMinus.Visible = false;
                    lbl.Visible = false;
                    bPlus.Visible = false;
                    lAvail.Visible = false;
                }
                else
                {
                    try
                    {
                        if (i < scheinWerte.Length - 1)
                        {
                            var sep = new Panel { BackColor = Color.FromArgb(242, 244, 247), Location = new Point(18, rowY + rowH - 2), Size = new Size(cardNotes.Width - 36, 1) };
                            cardNotes.Controls.Add(sep);
                        }
                    }
                    catch { }
                }
            }

            // Footer actions area
            int footerY = cardY + cardH + 14;
            int footerH = 120;
            var footer = new Panel
            {
                Location = new Point(cardCoins.Left, footerY),
                // Span under both cards but DO NOT include the gap twice.
                // Use the visible card edges as reference.
                Size = new Size(cardNotes.Right - cardCoins.Right, footerH),
                BackColor = Color.Transparent
            };
            try
            {
                // Ensure pixel-perfect right alignment with the Scheine-card
                footer.Left = cardCoins.Left;
                footer.Width = cardNotes.Right - cardCoins.Right;
            }
            catch { }
            try { footer.Region = rr(footer, 0, 0, footer.Width, footer.Height, 18, 18); } catch { }
            footer.Paint += (s, e) =>
            {
                try
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    var rFill = footer.ClientRectangle;
                    using (var br = new LinearGradientBrush(rFill, innerSurface, Color.FromArgb(50, 255, 255, 255), 90f))
                    {
                        e.Graphics.FillRectangle(br, rFill);
                    }
                    using (var pen = new Pen(border, 1f))
                    {
                        var r = footer.ClientRectangle;
                        r.Width -= 1; r.Height -= 1;
                        e.Graphics.DrawRectangle(pen, r);
                    }
                }
                catch { }
            };
            host.Controls.Add(footer);
            try { footer.BringToFront(); } catch { }

            // Reposition on resize (surface can resize independently of TabPage)
            try
            {
                host.SizeChanged += (s, e) =>
                {
                    try
                    {
                        int newCardW = (host.ClientSize.Width - host.Padding.Left - host.Padding.Right - gap) / 2;
                        if (newCardW < 520) newCardW = 520;
                        cardCoins.Width = newCardW;
                        cardNotes.Width = newCardW;
                        cardNotes.Left = host.Padding.Left + newCardW + gap;
                        footer.Left = cardCoins.Left;
                        footer.Width = cardNotes.Right - footer.Left - 60;

                        // keep separators aligned with card widths
                        foreach (Control c in cardCoins.Controls)
                        {
                            if (c is Panel p && p.Height == 1) p.Width = cardCoins.Width - 36;
                        }
                        foreach (Control c in cardNotes.Controls)
                        {
                            if (c is Panel p && p.Height == 1) p.Width = cardNotes.Width - 36;
                        }
                    }
                    catch { }
                };
            }
            catch { }

            // Footer: Rechts Button, links daneben groß die Summe, darüber kleiner Maximal verfügbar
            int innerPad = 18;
            int buttonW = 180;
            int buttonH = 52;
            int buttonX = footer.Width - innerPad - buttonW;

            int infoRight = buttonX - 16; // Abstand zum Button
            int infoX = innerPad;
            int infoW = Math.Max(260, infoRight - infoX);

            var lblMaxText = new Label
            {
                Text = "Maximal verfügbar",
                Font = new Font("Segoe UI Variable", 16F, FontStyle.Bold),
                ForeColor = primary,
                Location = new Point(infoX, 16),
                Size = new Size(Math.Max(160, infoW - 220), 28),
                TextAlign = ContentAlignment.MiddleRight,
                BackColor = Color.Transparent
            };
            footer.Controls.Add(lblMaxText);

            lblMaxVerfuegbar = new Label
            {
                Text = $"{(_personalGuthaben + _eingezahltSession):C2}",
                Font = new Font("Segoe UI Variable", 16F, FontStyle.Bold),
                ForeColor = primary,
                Location = new Point(infoX + Math.Max(160, infoW - 220), 16),
                Size = new Size(220, 28),
                TextAlign = ContentAlignment.MiddleRight,
                BackColor = Color.Transparent
            };
            footer.Controls.Add(lblMaxVerfuegbar);

            var lblSumText = new Label
            {
                Text = "Summe",
                Font = new Font("Segoe UI Variable", 22F, FontStyle.Bold),
                ForeColor = Color.FromArgb(17, 24, 39),
                Location = new Point(infoX, 56),
                Size = new Size(Math.Max(160, infoW - 220), 44),
                TextAlign = ContentAlignment.MiddleRight,
                BackColor = Color.Transparent
            };
            footer.Controls.Add(lblSumText);

            lblSummeAuszahlung = new Label
            {
                AutoSize = false,
                Font = new Font("Segoe UI Variable", 22F, FontStyle.Bold),
                Text = "0,00 €",
                ForeColor = Color.FromArgb(17, 24, 39),
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(infoX + Math.Max(160, infoW - 220), 56),
                Size = new Size(220, 44),
                BackColor = Color.Transparent
            };
            footer.Controls.Add(lblSummeAuszahlung);

            btnAuszahlen = new Button
            {
                Text = "Auszahlen",
                Location = new Point(buttonX, 50),
                Size = new Size(buttonW, buttonH),
                FlatStyle = FlatStyle.Flat,
                BackColor = success,
                ForeColor = Color.White,
                Font = new Font("Segoe UI Variable", 18F, FontStyle.Bold),
                TabStop = false
            };
            btnAuszahlen.FlatAppearance.BorderSize = 0;
            btnAuszahlen.FlatAppearance.MouseOverBackColor = Color.FromArgb(56, 142, 60);
            btnAuszahlen.FlatAppearance.MouseDownBackColor = Color.FromArgb(27, 94, 32);
            try
            {
                ApplyModernButtonStyle(btnAuszahlen, Color.FromArgb(46, 125, 50), Color.FromArgb(27, 94, 32));
                btnAuszahlen.FlatAppearance.MouseOverBackColor = Color.Transparent;
                btnAuszahlen.Tag = new Tuple<Color, Color>(Color.FromArgb(46, 125, 50), Color.FromArgb(27, 94, 32));
                btnAuszahlen.ForeColor = Color.White;
            }
            catch { }
            try { btnAuszahlen.Region = rr(btnAuszahlen, 0, 0, btnAuszahlen.Width, btnAuszahlen.Height, 14, 14); } catch { }
            btnAuszahlen.Click += btnAuszahlen_Click;
            footer.Controls.Add(btnAuszahlen);

            // Keep footer visuals + inner layout aligned on resize
            try
            {
                host.SizeChanged += (s, e) =>
                {
                    try
                    {
                        // update rounded region to match new width/height
                        try { footer.Region?.Dispose(); } catch { }
                        try { footer.Region = rr(footer, 0, 0, footer.Width, footer.Height, 18, 18); } catch { }

                        int bx = footer.Width - innerPad - buttonW;
                        if (btnAuszahlen != null) btnAuszahlen.Left = bx;

                        int infoR = bx - 16;
                        int iw = Math.Max(260, infoR - infoX);
                        int leftW = Math.Max(160, iw - 220);

                        if (lblMaxText != null) lblMaxText.Width = leftW;
                        if (lblMaxVerfuegbar != null) lblMaxVerfuegbar.Left = infoX + leftW;

                        if (lblSumText != null) lblSumText.Width = leftW;
                        if (lblSummeAuszahlung != null) lblSummeAuszahlung.Left = infoX + leftW;
                    }
                    catch { }
                };
            }
            catch { }

            SafeRefreshAvailability();
            UpdateSummeAuszahlung();
            UpdateMaxVerfuegbar();
        }

        private Image GetCoinImageByIndex(int i)
        {
            try
            {
                switch (muenzWerte[i])
                {
                    case 1: return TaMi_Einzahlautomat.Properties.Resources._1cent ?? TaMi_Einzahlautomat.Properties.Resources._1euro; // fallback
                    case 2: return TaMi_Einzahlautomat.Properties.Resources._2cent ?? TaMi_Einzahlautomat.Properties.Resources._2cent1;
                    case 5: return TaMi_Einzahlautomat.Properties.Resources._5cent ?? TaMi_Einzahlautomat.Properties.Resources._5cent1;
                    case 10: return TaMi_Einzahlautomat.Properties.Resources._10cent;
                    case 20: return TaMi_Einzahlautomat.Properties.Resources._20cent;
                    case 50: return TaMi_Einzahlautomat.Properties.Resources._50cent;
                    case 100: return TaMi_Einzahlautomat.Properties.Resources._1euro;
                    case 200: return TaMi_Einzahlautomat.Properties.Resources._2euro;
                }
            }
            catch { }
            return null;
        }

        private Image GetNoteImageByIndex(int i)
        {
            try
            {
                switch (scheinWerte[i])
                {
                    case 5: return TaMi_Einzahlautomat.Properties.Resources._5euro;
                    case 10: return TaMi_Einzahlautomat.Properties.Resources._10euro;
                    case 20: return TaMi_Einzahlautomat.Properties.Resources._20euro;
                    case 50: return TaMi_Einzahlautomat.Properties.Resources._50euro;
                    case 100: return TaMi_Einzahlautomat.Properties.Resources._100euro;
                    case 200: return null; // not present
                    case 500: return null; // not present
                }
            }
            catch { }
            return null;
        }

        private void UpdateAbrechnenSummaries()
        {
            if (_currentAuszahlungRow != null)
            {
                decimal raw19 = Math.Abs(Convert.ToDecimal(_currentAuszahlungRow["Betrag19"]));
                decimal raw7 = Math.Abs(Convert.ToDecimal(_currentAuszahlungRow["Betrag7"]));
                decimal raw0 = Math.Abs(Convert.ToDecimal(_currentAuszahlungRow["Betrag0"]));
                if (_currentZahlungIstEinzahlung)
                {
                    decimal summe = raw19 + raw7 + raw0;
                    lblB19.Text = $"19%: {raw19:C2}"; lblB7.Text = $"  7%:   {raw7:C2}"; lblB0.Text = $"0%: {raw0:C2}";
                    if (lblSumme != null) lblSumme.Text = summe.ToString("C2");
                    if (lblEingezahlt != null) lblEingezahlt.Text = _eingezahltSession.ToString("C2");
                    var rest = Math.Max(0m, summe - _eingezahltSession); var noch = Math.Max(0m, rest - _personalGuthaben);
                    if (lblNoch != null) lblNoch.Text = noch.ToString("C2");
                    try { if (lblNoch != null) lblNoch.ForeColor = (noch > 0m) ? Color.Red : Color.Green; } catch { }
                }
                else
                {
                    decimal summe = -raw19 - raw7 - raw0;
                    lblB19.Text = $"19%: {-raw19:C2}"; lblB7.Text = $"  7%:   {-raw7:C2}"; lblB0.Text = $"0%: {-raw0:C2}";
                    if (lblSumme != null) lblSumme.Text = summe.ToString("C2");
                    if (lblEingezahlt != null) lblEingezahlt.Text = _eingezahltSession.ToString("C2");
                    if (lblNoch != null) lblNoch.Text = 0m.ToString("C2");
                    try { if (lblNoch != null) lblNoch.ForeColor = Color.Green; } catch { }
                }
                // Wichtig: nicht mit Standard-Nullwerten überschreiben, wenn eine Auszahlung geladen ist
                UpdateBuchenEnabled();
                return;
            }
            if (_details == null)
            {
                lblB19.Text = "19%: 0,00 €"; lblB7.Text = "7%:   0,00 €"; lblB0.Text = "0%: 0,00 €";
                if (lblSumme != null) lblSumme.Text = 0m.ToString("C2");
                if (lblEingezahlt != null) lblEingezahlt.Text = _eingezahltSession.ToString("C2");
                if (lblNoch != null) lblNoch.Text = 0m.ToString("C2");
                try { if (lblNoch != null) lblNoch.ForeColor = Color.Green; } catch { }
                UpdateBuchenEnabled();
                return;
            }
            if (lblSumme != null) lblSumme.Text = _details.SummeZuZahlen.ToString("C2");
            if (lblEingezahlt != null) lblEingezahlt.Text = _eingezahltSession.ToString("C2");
            var restStd = Math.Max(0, _details.SummeZuZahlen - _eingezahltSession);
            var nochStd = Math.Max(0, restStd - _personalGuthaben);
            if (lblNoch != null) lblNoch.Text = nochStd.ToString("C2");
            try { if (lblNoch != null) lblNoch.ForeColor = (nochStd > 0m) ? Color.Red : Color.Green; } catch { }
            UpdateBuchenEnabled();
        }

        private void TryPrintReceipt(string vorgang, decimal b19, decimal b7, decimal b0, string buchungstext)
        {
            try
            {
                var cfg = ReceiptPrinterSettings.Load();
                // Entferne das frühe Return: auch bei deaktiviertem Drucker sollen Mail/QR angeboten werden.

                DialogResult choice = DialogResult.None;
                if (cfg.AskUser)
                {
                    choice = ShowReceiptChoiceDialog();
                }
                else
                {
                    // Wenn keine Nachfrage: nur automatisch drucken, wenn Druck aktiviert ist und Auto-Print gesetzt ist
                    choice = (cfg.Enabled && cfg.AutoPrintIfNoPrompt) ? DialogResult.Yes : DialogResult.No;
                }

                if (choice == DialogResult.Cancel)
                {
                    // Mail
                    try
                    {
                        string titleM = string.IsNullOrWhiteSpace(vorgang) ? "QUITTUNG" : vorgang.ToUpperInvariant();
                        string mitarbeiterM = _personal.Vorname + " " + _personal.Name;
                        var bodyLinesM = ReceiptLayouts.Current.BuildBody(titleM, mitarbeiterM, buchungstext, b19, b7, b0, true);
                        string bodyM = string.Join("\r\n", bodyLinesM ?? new string[0]);
                        // NEU: HTML-Anhang wie QR-Quittung
                        string attachmentText = string.Join("\n", bodyLinesM ?? new string[0]);
                        EmailReceiptService.SendReceiptToEmployeeWithAttachment(_personal, vorgang, bodyM, attachmentText);
                        MessageBox.Show(this, "Quittung per E-Mail gesendet.", "Mail", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, "E-Mail Versand fehlgeschlagen:\r\n" + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    return;
                }
                if (choice == DialogResult.Ignore)
                {
                    // QR-Code anzeigen (20s Auto-Close)
                    try
                    {
                        string titleQ = string.IsNullOrWhiteSpace(vorgang) ? "QUITTUNG" : vorgang.ToUpperInvariant();
                        string mitarbeiterQ = _personal.Vorname + " " + _personal.Name;
                        var bodyLinesQ = ReceiptLayouts.Current.BuildBody(titleQ, mitarbeiterQ, buchungstext, b19, b7, b0, true);
                        string qrText = string.Join("\n", bodyLinesQ ?? new string[0]);
                        var frm = new QrCodeForm(qrText);
                        try { frm.Show(this); frm.BringToFront(); frm.Activate(); } catch { frm.Show(); }
                    }
                    catch { }
                    return;
                }
                if (choice == DialogResult.Yes)
                {
                    // Drucken nur ausführen, wenn Druck aktiviert ist
                    if (!cfg.Enabled)
                    {
                        MessageBox.Show(this, "Drucken ist deaktiviert.", "Quittung", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    string title = string.IsNullOrWhiteSpace(vorgang) ? "QUITTUNG" : vorgang.ToUpperInvariant();
                    string mitarbeiter = _personal.Vorname + " " + _personal.Name;
                    var body = ReceiptLayouts.Current.BuildBody(title, mitarbeiter, buchungstext, b19, b7, b0, cfg.PrintVatLines);
                    if (cfg.LegacyDontWait)
                    {
                        try
                        {
                            Task.Run(() =>
                            {
                                try { new ReceiptPrinter(cfg).PrintSimpleReceipt(title, body); } catch { }
                            });
                        }
                        catch { }
                    }
                    else
                    {
                        new ReceiptPrinter(cfg).PrintSimpleReceipt(title, body);
                    }
                }
                // choice == No -> nichts tun
            }
            catch { }
        }

        private DialogResult ShowReceiptChoiceDialog()
        {
            try
            {
                var dlg = new Form
                {
                    FormBorderStyle = FormBorderStyle.None,
                    StartPosition = FormStartPosition.CenterParent,
                    Width = 720,
                    Height = 320,
                    BackColor = Color.White
                };

                try { dlg.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, dlg.Width, dlg.Height, 16, 16)); } catch { }

                // Modern card look
                try
                {
                    dlg.Padding = new Padding(1);
                    dlg.Paint += (s, e) =>
                    {
                        try
                        {
                            var rect = dlg.ClientRectangle;
                            using (var pen = new Pen(Color.FromArgb(200, 210, 225, 245)))
                            {
                                e.Graphics.DrawRectangle(pen, new Rectangle(0, 0, rect.Width - 1, rect.Height - 1));
                            }
                        }
                        catch { }
                    };
                }
                catch { }

                var header = new Panel { Dock = DockStyle.Top, Height = 64 };
                header.Paint += (s, e) =>
                {
                    try
                    {
                        // Match app header style
                        using (var brush = new LinearGradientBrush(header.ClientRectangle, Color.FromArgb(13, 71, 161), Color.FromArgb(120, 200, 255), 0f))
                        {
                            e.Graphics.FillRectangle(brush, header.ClientRectangle);
                        }
                    }
                    catch
                    {
                        using (var brush = new LinearGradientBrush(header.ClientRectangle, Color.FromArgb(33, 150, 243), Color.FromArgb(33, 203, 243), 0f))
                        { e.Graphics.FillRectangle(brush, header.ClientRectangle); }
                    }
                };
                dlg.Controls.Add(header);

                var lblTitle = new Label
                {
                    Text = "Quittung",
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Font = new Font("Segoe UI Variable", 20F, FontStyle.Bold),
                    ForeColor = Color.White,
                    Location = new Point(20, 0),
                    Size = new Size(400, 64),
                    BackColor = Color.Transparent
                };
                header.Controls.Add(lblTitle);

                var body = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
                dlg.Controls.Add(body);

                // Frosted/Glass inner surface like other screens
                Panel surface = null;
                try
                {
                    surface = new Panel
                    {
                        Dock = DockStyle.Fill,
                        BackColor = Color.Transparent,
                        Padding = new Padding(18)
                    };
                    surface.Paint += (s, e) =>
                    {
                        try
                        {
                            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                            var r = surface.ClientRectangle;
                            if (r.Width <= 1 || r.Height <= 1) return;
                            using (var br = new LinearGradientBrush(r, Color.FromArgb(210, 245, 250, 255), Color.FromArgb(185, 230, 240, 255), 90f))
                            {
                                e.Graphics.FillRectangle(br, r);
                            }
                            using (var pen = new Pen(Color.FromArgb(140, 180, 200, 230), 1f))
                            {
                                var rr = r; rr.Width -= 1; rr.Height -= 1;
                                e.Graphics.DrawRectangle(pen, rr);
                            }
                        }
                        catch { }
                    };
                    body.Controls.Add(surface);
                }
                catch { surface = null; }

                var host = (Control)(surface ?? body);

                var lbl = new Label
                {
                    Text = "Möchten Sie eine Quittung, wenn ja wie?",
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Dock = DockStyle.Top,
                    Height = 76,
                    Font = new Font("Segoe UI Variable", 18F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(15, 23, 42),
                    BackColor = Color.Transparent
                };
                host.Controls.Add(lbl);

                // push the label two line-heights down
                try
                {
                    int lineH = TextRenderer.MeasureText("A", lbl.Font).Height;
                    host.Padding = new Padding(host.Padding.Left, host.Padding.Top + lineH, host.Padding.Right, host.Padding.Bottom);
                }
                catch { }

                var panelButtons = new Panel { Dock = DockStyle.Bottom, Height = 120, BackColor = Color.Transparent };
                host.Controls.Add(panelButtons);

                Func<string, Button> makeBtn = (text) =>
                {
                    var b = new Button
                    {
                        Text = text,
                        Width = 150,
                        Height = 56,
                        FlatStyle = FlatStyle.Flat,
                        BackColor = Color.FromArgb(33, 150, 243),
                        ForeColor = Color.White,
                        Font = new Font("Segoe UI Variable", 16F, FontStyle.Bold),
                        TabStop = false
                    };
                    b.FlatAppearance.BorderSize = 0;
                    try { b.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, b.Width, b.Height, 12, 12)); } catch { }

                    // Modern gradient style like the rest of the app
                    try
                    {
                        ApplyModernButtonStyle(b, Color.FromArgb(33, 150, 243), Color.FromArgb(13, 71, 161));
                        b.FlatAppearance.MouseOverBackColor = Color.Transparent;
                    }
                    catch { }
                    return b;
                };

                var btnMail = makeBtn("per Mail");
                var btnPrint = makeBtn("Drucken");
                var btnQr = makeBtn("QR-Code");
                var btnNo = makeBtn("Nein");

                // Disable mail when no employee mail or no mail settings configured
                try
                {
                    bool hasEmployeeMail = _personal != null && !string.IsNullOrWhiteSpace(_personal.EMail);
                    var mailCfg = MailSettings.Load();
                    bool mailConfigured = mailCfg != null && mailCfg.IsConfigured;
                    bool mailAvailable = hasEmployeeMail && mailConfigured;
                    btnMail.Enabled = mailAvailable;
                    if (!mailAvailable)
                    {
                        btnMail.BackColor = Color.LightGray;
                        btnMail.ForeColor = Color.WhiteSmoke;
                        btnMail.FlatAppearance.MouseOverBackColor = Color.LightGray;
                        btnMail.Cursor = Cursors.No;
                        string reason = !hasEmployeeMail ? "Keine Mitarbeiter-E-Mail hinterlegt." : "Maileinstellungen unvollständig.";
                        try { new ToolTip().SetToolTip(btnMail, reason); } catch { }
                    }
                }
                catch { }

                // Disable print when printer disabled in settings
                try
                {
                    var prnCfg = ReceiptPrinterSettings.Load();
                    bool printEnabled = prnCfg != null && prnCfg.Enabled;
                    btnPrint.Enabled = printEnabled;
                    if (!printEnabled)
                    {
                        btnPrint.BackColor = Color.LightGray;
                        btnPrint.ForeColor = Color.WhiteSmoke;
                        btnPrint.FlatAppearance.MouseOverBackColor = Color.LightGray;
                        btnPrint.Cursor = Cursors.No;
                        try { new ToolTip().SetToolTip(btnPrint, "Drucken ist deaktiviert."); } catch { }
                    }
                }
                catch { }

                // NEU: QR-Code nach UI-Setting deaktivieren
                try
                {
                    var qr = IniHelper.ReadValue("UI", "QrCodeEnabled", AppSettings.IniPath);
                    bool qrEnabled = string.IsNullOrWhiteSpace(qr) ? true :
                        (qr.Equals("true", StringComparison.OrdinalIgnoreCase) || qr.Equals("1") || qr.Equals("yes", StringComparison.OrdinalIgnoreCase) || qr.Equals("on", StringComparison.OrdinalIgnoreCase));
                    btnQr.Enabled = qrEnabled;
                    if (!qrEnabled)
                    {
                        btnQr.BackColor = Color.LightGray;
                        btnQr.ForeColor = Color.WhiteSmoke;
                        btnQr.FlatAppearance.MouseOverBackColor = Color.LightGray;
                        btnQr.Cursor = Cursors.No;
                        try { new ToolTip().SetToolTip(btnQr, "QR-Code ist deaktiviert."); } catch { }
                      }
                }
                catch { }

                btnNo.BackColor = Color.FromArgb(229, 57, 53);
                btnNo.FlatAppearance.MouseOverBackColor = Color.FromArgb(211, 47, 47);
                try
                {
                    ApplyModernButtonStyle(btnNo, Color.FromArgb(239, 83, 80), Color.FromArgb(198, 40, 40));
                    btnNo.FlatAppearance.MouseOverBackColor = Color.Transparent;
                }
                catch { }

                // Layout
                int spacing = 16;
                int totalWidth = btnMail.Width + btnPrint.Width + btnQr.Width + btnNo.Width + spacing * 3;
                int startX = (dlg.ClientSize.Width - totalWidth) / 2;
                int y = (panelButtons.Height - btnMail.Height) / 2;
                btnMail.Location = new Point(startX, y);
                btnPrint.Location = new Point(btnMail.Right + spacing, y);
                btnQr.Location = new Point(btnPrint.Right + spacing, y);
                btnNo.Location = new Point(btnQr.Right + spacing, y);
                panelButtons.Controls.AddRange(new Control[] { btnMail, btnPrint, btnQr, btnNo });

                DialogResult result = DialogResult.None;
                btnMail.Click += (s, e) => { result = DialogResult.Cancel; dlg.Close(); };
                btnPrint.Click += (s, e) => { result = DialogResult.Yes; dlg.Close(); };
                btnQr.Click += (s, e) => { result = DialogResult.Ignore; dlg.Close(); };
                btnNo.Click += (s, e) => { result = DialogResult.No; dlg.Close(); };

                try { dlg.ShowDialog(this); } catch { dlg.ShowDialog(); }
                try { dlg.Dispose(); } catch { }
                return result;
            }
            catch { return DialogResult.No; }
        }

        private void HeaderPanel_Paint(object sender, PaintEventArgs e)
        {
            try
            {
                // Wenn `headerPanel` ein `ModernHeaderPanel` ist, übernimmt dieses selbst das Painting.
                if (headerPanel is ModernHeaderPanel) return;

                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var r = headerPanel.ClientRectangle;

                using (var brush = new LinearGradientBrush(r, Color.FromArgb(13, 71, 161), Color.FromArgb(120, 200, 255), 0f))
                    e.Graphics.FillRectangle(brush, r);
            }
            catch
            {
                using (var brush = new LinearGradientBrush(headerPanel.ClientRectangle, Color.FromArgb(33, 150, 243), Color.FromArgb(33, 203, 243), 0f))
                    e.Graphics.FillRectangle(brush, headerPanel.ClientRectangle);
            }
        }
        private void HeaderPanel_MouseDown(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) _mouseDownLocation = e.Location; }
        private void HeaderPanel_MouseMove(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Left += e.X - _mouseDownLocation.X; Top += e.Y - _mouseDownLocation.Y; } }
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try { EnableAllDevicesOnOpen(); } catch { }
            try
            {
                await LadeAlleAbrechenbarenItemsAsync(); btnSchichtAuswahl.Visible = _auswahlItems.Count > 1;
                AuswahlItem oldestSchicht = null; AuswahlItem oldestAuszahlung = null; DateTime? minSchicht = null; DateTime? minAuszahlung = null;
                foreach (var item in _auswahlItems)
                {
                    if (item.Schicht != null)
                    {
                        var start = item.Schicht.StartZeit; if (!minSchicht.HasValue || start < minSchicht.Value) { minSchicht = start; oldestSchicht = item; }
                    }
                    else if (item.Auszahlung != null)
                    {
                        DateTime? erfasst = (item.Auszahlung.Table.Columns.Contains("ErfasstAm") && item.Auszahlung["ErfasstAm"] != DBNull.Value) ? Convert.ToDateTime(item.Auszahlung["ErfasstAm"]) : DateTime.MinValue;
                        if (!minAuszahlung.HasValue || erfasst < minAuszahlung.Value) { minAuszahlung = erfasst; oldestAuszahlung = item; }
                    }
                }
                if (oldestSchicht != null) LadeSchicht(oldestSchicht.Schicht); else if (oldestAuszahlung != null) LadeAuszahlung(oldestAuszahlung.Auszahlung);
            }
            catch (Exception ex) { MessageBox.Show(this, $"NV200: Verbindung beim Öffnen fehlgeschlagen:\r\n{ex.Message}", "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            {
                using (var db = new DatabaseHelper()) { _personalGuthaben = await db.GetLastPersonalGuthabenSaldoAsync(_personal.PID); }
            }
            try { lblGuthaben.Text = $"Personal-Guthaben: {_personalGuthaben:C2}"; } catch { }
            UpdateAbrechnenSummaries();
            try { if (btnCreatePayment != null) btnCreatePayment.Visible = PaymentSettingsStore.IsEnabled(); } catch { }

            // Notizen (Mitarbeiterinfo) laden und anzeigen
            try
            {
                using (var db = new DatabaseHelper())
                {
                    var notes = await db.GetActiveMitarbeiterNotizenAsync(_personal.PID);
                    if (lblNotizenInfo != null)
                    {
                        if (notes != null && notes.Count > 0)
                        {
                            int noteIdx = 0;
                            Func<string, string> fmt = (n) =>
                            {
                                noteIdx++;
                                var raw = (n ?? string.Empty);
                                var t = raw.Replace("\r\n", "\n").Replace("\r", "\n");
                                var lines = t.Split(new[] { '\n' }, StringSplitOptions.None);
                                if (lines.Length <= 1) return noteIdx.ToString() + ". " + (lines[0] ?? string.Empty).Trim();
                                // Folgezeilen einrücken: nicht über Spaces (werden von TextRenderer optisch gekürzt),
                                // sondern über ein Prefix, das wir beim Zeichnen als echten X-Offset interpretieren.
                                const string indentPrefix = "\t";
                                return noteIdx.ToString() + ". " + (lines[0] ?? string.Empty).Trim() + "\r\n" + string.Join("\r\n", lines.Skip(1).Select(l => indentPrefix + (l ?? string.Empty).Trim()));
                            };
                            string text = string.Join("\r\n\r\n", notes.Select(fmt));
                            // nach der letzten Notiz zwei Leerzeilen für sauberen Abschluss / Scroll-Loop
                            text += "\r\n\r\n";
                            if (text.Length > 900) text = text.Substring(0, 900) + "…";
                            try { lblNotizenInfo.Tag = text; lblNotizenInfo.Text = string.Empty; } catch { }
                            lblNotizenInfo.Visible = !string.IsNullOrWhiteSpace(text);
                            try
                            {
                                _notesScrollLastTextHash = 0;
                                _notesScrollOffsetPx = 0;
                                _notesScrollFirstLine = 0;
                                _notesScrollLineOffsetPx = 0;
                                _notesScrollPauseRemainingMs = _notesScrollPauseMs;
                                if (lblNotizenInfo.Visible && _notesScrollTimer != null) _notesScrollTimer.Start();
                            }
                            catch { }
                            try { lblNotizenInfo.BringToFront(); } catch { }
                            try { lblNotizenInfo.Invalidate(); } catch { }
                        }
                        else
                        {
                            // ausblenden wenn nichts da
                            lblNotizenInfo.Visible = false;
                            try { lblNotizenInfo.Tag = string.Empty; lblNotizenInfo.Text = string.Empty; } catch { }
                            try { _notesScrollOffsetPx = 0; if (_notesScrollTimer != null) _notesScrollTimer.Stop(); } catch { }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                try
                {
                    if (lblNotizenInfo != null)
                    {
                        lblNotizenInfo.Visible = true;
                        try { lblNotizenInfo.Tag = "Fehler beim Laden der Notizen: " + ex.Message; lblNotizenInfo.Text = string.Empty; } catch { }
                        try { _notesScrollLastTextHash = 0; _notesScrollOffsetPx = 0; if (_notesScrollTimer != null) _notesScrollTimer.Start(); } catch { }
                        lblNotizenInfo.Invalidate();
                    }
                }
                catch { }
            }
        }

        private void AttachCoinEvents()
        {
            if (_coinEventsAttached) return;
            if (_coin is SmartCoinV1 sc1)
            {
                sc1.CoinLevelsUpdated += OnCoinLevelsUpdated; sc1.CoinAccepted += CoinOnAccepted; sc1.EventLog += CoinOnLog; sc1.CoinDispensedDeltaCent += OnCoinDispensedDelta; sc1.CoinDispenseComplete += OnCoinDispenseComplete; try { sc1.CoinPayoutError += OnCoinPayoutError; } catch { }
                _coinEventsAttached = true;
                try { RequestCoinLevels(); } catch { }
            }
            else if (_coin is Rm5CctalkValidator rm5)
            {
                rm5.CoinAccepted += CoinOnAccepted; rm5.EventLog += CoinOnLog; try { rm5.CoinLevelsUpdated += OnCoinLevelsUpdated; } catch { }
                try { rm5.CoinDispensedDeltaCent += OnCoinDispensedDelta; } catch { }
                try { rm5.CoinDispenseComplete += OnCoinDispenseComplete; } catch { }
                try
                {
                    rm5.CoinPayoutError += (hopperIdx, remaining) =>
                    {
                        try
                        {
                            BeginInvoke((Action)(() =>
                            {
                                AppLogger.Log($"RM5 Payout ERROR Hopper {hopperIdx} – {remaining} Münzen nicht ausgezahlt");
                                if (AdminMode.IsOpen) return;
                                MessageBox.Show(this, $"Münzauszahlung fehlgeschlagen (Hopper {hopperIdx}). {remaining} Münzen nicht ausgezahlt.", "Münzauszahlung", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            }));
                        }
                        catch { }
                    };
                }
                catch { }
                _coinEventsAttached = true; try { rm5.RequestCoinLevels(); } catch { }
            }
        }

        private void DetachCoinEvents()
        {
            if (!_coinEventsAttached) return;
            if (_coin is SmartCoinV1 sc1)
            {
                sc1.CoinLevelsUpdated -= OnCoinLevelsUpdated; sc1.CoinAccepted -= CoinOnAccepted; sc1.EventLog -= CoinOnLog; sc1.CoinDispensedDeltaCent -= OnCoinDispensedDelta; sc1.CoinDispenseComplete -= OnCoinDispenseComplete; try { sc1.CoinPayoutError -= OnCoinPayoutError; } catch { }
            }
            else if (_coin is Rm5CctalkValidator rm5)
            {
                rm5.CoinAccepted -= CoinOnAccepted; rm5.EventLog -= CoinOnLog; try { rm5.CoinLevelsUpdated -= OnCoinLevelsUpdated; } catch { }
                try { rm5.CoinDispensedDeltaCent -= OnCoinDispensedDelta; } catch { }
                try { rm5.CoinDispenseComplete -= OnCoinDispenseComplete; } catch { }
            }
            _coinEventsAttached = false;
        }

        // Request coin levels throttled
        private void RequestCoinLevels()
        {
            var now = DateTime.UtcNow;
            if ((now - _lastCoinLevelsRequestUtc).TotalMilliseconds < 1200) return;
            _lastCoinLevelsRequestUtc = now;
            try { SmartCoinV1.RequestLevelsGlobal(); } catch { }
        }

        private void OnCoinLevelsUpdated(int[] levels)
        {
            if (levels == null || levels.Length < 8) return;
            try
            {
                BeginInvoke((Action)(() =>
                {
                    for (int i = 0; i < 8; i++) _coinAvail[i] = levels[i];
                    UpdateCoinAvailabilityLabels(); UpdatePlusMinusEnabled();
                }));
            }
            catch { }
        }

        private void OnCoin2LevelsUpdated(int[] levels)
        {
    if (levels == null || levels.Length < 8) return;
            try
            {
                BeginInvoke((Action)(() =>
                {
                    for (int i = 0; i < 8; i++) _coin2Avail[i] = levels[i];
                    UpdateCoinAvailabilityLabels(); UpdatePlusMinusEnabled();
                }));
            }
            catch { }
        }

        private void CoinOnAccepted(int cent)
        {
            if (cent <= 0) return;
            try
            {
                BeginInvoke((Action)(() =>
                {
                    try { UpdateBusyUI(); } catch { }
                    AddEingezahlt(cent / 100m);
                    try { SmartCoinV1.ScheduleLevelsGlobal(); } catch { }
                }));
            }
            catch { }
        }
        private void CoinOnLog(string msg) { try { BeginInvoke((Action)(() => Text = $"Schicht abrechnen – Coin: {msg}")); } catch { } }

        private void AttachCoin2Events()
        {
            if (_coin2EventsAttached || _coin2 == null) return;
            try
            {
                if (_coin2 is SmartCoinV1 sc1)
                {
                    sc1.CoinAccepted += Coin2OnAccepted; sc1.EventLog += Coin2OnLog; sc1.CoinLevelsUpdated += OnCoin2LevelsUpdated; try { sc1.CoinDispensedDeltaCent += OnCoinDispensedDelta; } catch { }
                    try { sc1.CoinDispenseComplete += OnCoinDispenseComplete; } catch { }
                    try { sc1.CoinPayoutError += OnCoinPayoutError; } catch { }
                }
                _coin2EventsAttached = true;
                try { RequestCoinLevels(); } catch { }
            }
            catch { }
        }
        private void DetachCoin2Events()
        {
            if (!_coin2EventsAttached || _coin2 == null) return;
            try
            {
                if (_coin2 is SmartCoinV1 sc1)
                {
                    sc1.CoinAccepted -= Coin2OnAccepted; sc1.EventLog -= Coin2OnLog; sc1.CoinLevelsUpdated -= OnCoin2LevelsUpdated; try { sc1.CoinDispensedDeltaCent -= OnCoinDispensedDelta; } catch { }
                    try { sc1.CoinDispenseComplete -= OnCoinDispenseComplete; } catch { }
                    try { sc1.CoinPayoutError -= OnCoinPayoutError; } catch { }
                }
            }
            catch { }
            _coin2EventsAttached = false;
        }
        private void OnCoinPayoutError(string message)
        {
            try { BeginInvoke((Action)(() => { _activeCoinPayoutDevices = 0; _coinsPayoutInProgress = false; UpdateBusyUI(); if (!AdminMode.IsOpen && !string.IsNullOrWhiteSpace(message)) MessageBox.Show(this, message, "Münzauszahlung", MessageBoxButtons.OK, MessageBoxIcon.Warning); })); } catch { }
        }
        private void Coin2OnAccepted(int cent)
        {
            if (cent <= 0) return;
            try
            {
                BeginInvoke((Action)(() =>
                {
                    try { UpdateBusyUI(); } catch { }
                    AddEingezahlt(cent / 100m);
                    try { SmartCoinV1.ScheduleLevelsGlobal(); } catch { }
                }));
            }
            catch { }
        }
        private void Coin2OnLog(string msg) { try { BeginInvoke((Action)(() => Text = $"Schicht abrechnen – Coin/2: {msg}")); } catch { } }

        private async void btnAbrechnen_Click(object sender, EventArgs e)
        {
            if (_bookingInProgress) return;
            _bookingInProgress = true; UpdateBusyUI();
            try
            {
                if (_currentAuszahlungRow != null)
                {
                    try
                    {
                        decimal raw19 = Math.Abs(Convert.ToDecimal(_currentAuszahlungRow["Betrag19"]));
                        decimal raw7 = Math.Abs(Convert.ToDecimal(_currentAuszahlungRow["Betrag7"]));
                        decimal raw0 = Math.Abs(Convert.ToDecimal(_currentAuszahlungRow["Betrag0"]));
                        decimal sumPos = raw19 + raw7 + raw0;
                        using (var db = new DatabaseHelper())
                        {
                            bool einzahlung = _currentZahlungIstEinzahlung;
                            decimal betrag19 = (einzahlung ? 1 : -1) * Convert.ToDecimal(_currentAuszahlungRow["Betrag19"]);
                            decimal betrag7 = (einzahlung ? 1 : -1) * Convert.ToDecimal(_currentAuszahlungRow["Betrag7"]);
                            decimal betrag0 = (einzahlung ? 1 : -1) * Convert.ToDecimal(_currentAuszahlungRow["Betrag0"]);
                            try { AppLogger.Log($"[Buchen] {(einzahlung ? "Einzahlung" : "Auszahlung")} Gesamt={(betrag19 + betrag7 + betrag0):0.00} €"); } catch { }
                            decimal aktuellerBestand = GetCurrentKassenbestandEuro();
                            if (aktuellerBestand + (betrag19 + betrag7 + betrag0) < 0m) { MessageBox.Show(this, "Abrechnen nicht möglich: Zu wenig Geld in der Kasse.", "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                            string typ = einzahlung ? "Einzahlung" : "Auszahlung";
                            try
                            {
                                if (!einzahlung && _currentAuszahlungRow.Table.Columns.Contains("Typ"))
                                {
                                    var t = Convert.ToString(_currentAuszahlungRow["Typ"]);
                                    if (!string.IsNullOrWhiteSpace(t) && t.Trim().Equals("Trinkgeld", StringComparison.OrdinalIgnoreCase)) typ = "Trinkgeld";
                                }
                            }
                            catch { }
                            string rawTxt = _currentAuszahlungRow["Buchungstext"]?.ToString(); string baseTxt = string.IsNullOrWhiteSpace(rawTxt) ? typ : rawTxt.Trim(); string personPrefix = $"{_personal.Vorname} {_personal.Name}".Trim(); string buchungstext = baseTxt.StartsWith(personPrefix, StringComparison.OrdinalIgnoreCase) ? baseTxt : $"{personPrefix} {baseTxt}";
                            int firmenId = _currentAuszahlungRow.Table.Columns.Contains("FirmenID") && _currentAuszahlungRow["FirmenID"] != DBNull.Value ? Convert.ToInt32(_currentAuszahlungRow["FirmenID"]) : -1;
                            int kost1 = _currentAuszahlungRow.Table.Columns.Contains("Kost1") && _currentAuszahlungRow["Kost1"] != DBNull.Value && !string.IsNullOrWhiteSpace(_currentAuszahlungRow["Kost1"].ToString()) ? Convert.ToInt32(_currentAuszahlungRow["Kost1"]) : 0;
                            int kost2 = _currentAuszahlungRow.Table.Columns.Contains("Kost2") && _currentAuszahlungRow["Kost2"] != DBNull.Value && !string.IsNullOrWhiteSpace(_currentAuszahlungRow["Kost2"].ToString()) ? Convert.ToInt32(_currentAuszahlungRow["Kost2"]) : 0;
                            int konto = _currentAuszahlungRow.Table.Columns.Contains("Konto") && _currentAuszahlungRow["Konto"] != DBNull.Value && !string.IsNullOrWhiteSpace(_currentAuszahlungRow["Konto"].ToString()) ? Convert.ToInt32(_currentAuszahlungRow["Konto"]) : 0;

                            // Wenn keine Vorgabe vorhanden (alle drei 0), Kontierung per Regeln bestimmen
                            if (kost1 == 0 && kost2 == 0 && konto == 0)
                            {
                                try
                                {
                                    // MwSt.-Gruppe anhand der Beträge wählen: Priorität 19 -> 7 -> 0
                                    decimal abs19 = Math.Abs(betrag19);
                                    decimal abs7 = Math.Abs(betrag7);
                                    decimal abs0 = Math.Abs(betrag0);
                                    string grp = abs19 > 0m ? "19" : (abs7 > 0m ? "7" : "0");
                                    decimal betrag = abs19 > 0m ? abs19 : (abs7 > 0m ? abs7 : abs0);
                                    int? fhzId = null;
                                    try { fhzId = _details?.FhzId > 0 ? (int?)_details.FhzId : null; } catch { fhzId = null; }
                                    var res = AccountingRules.GetKontierungForTyp(grp, Math.Max(0, firmenId), _personal?.PID, fhzId, betrag, typ);
                                    if (res != null)
                                    {
                                        kost1 = res.Kost1;
                                        kost2 = res.Kost2;
                                        konto = res.Konto;
                                        try { AppLogger.Log($"[Buchen] Regeln angewendet: grp={grp} -> k1={kost1},k2={kost2},kto={konto}"); } catch { }
                                    }
                                }
                                catch { }
                            }

                            if (einzahlung)
                            {
                                decimal totalVerfuegbar = Math.Round(_eingezahltSession + _personalGuthaben, 2);
                                if (sumPos > totalVerfuegbar) { MessageBox.Show(this, "Nicht genügend eingezahlt/Personalguthaben.", "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                            }
                            var entry = new KassenbuchEntry { PersId = _personal.PID, SchichtId = 0, Typ = typ, Buchungstext = buchungstext, Kost1 = kost1, Kost2 = kost2, Konto = konto, Betrag19 = betrag19, Betrag7 = betrag7, Betrag0 = betrag0, FirmenId = _currentAuszahlungRow["FirmenID"] != DBNull.Value ? Convert.ToInt32(_currentAuszahlungRow["FirmenID"]) : 0, AutomatenName = AppSettings.AutomatenName, Kassenbestand = GetCurrentKassenbestandEuro() };
                            await db.InsertKassenbuchAsync(entry);
                            if (einzahlung)
                            {
                                decimal vonEingezahlt = Math.Min(_eingezahltSession, sumPos); decimal vonGuthaben = Math.Max(0m, sumPos - vonEingezahlt); _eingezahltSession = Math.Round(_eingezahltSession - vonEingezahlt, 2);
                                if (vonGuthaben > 0m)
                                {
                                    int manId = _details?.ManId ?? (firmenId > 0 ? firmenId : 0); var zpg = AccountingRules.GetPersonalguthabenKontierung(manId); decimal neuerSaldo = Math.Round(_personalGuthaben - vonGuthaben, 2);
                                    var pgEntry = new KassenbuchEntry { PersId = _personal.PID, SchichtId = 0, Typ = "Personalguthaben", Buchungstext = AccountingRules.ComposePgEinzahlungText(_personal), Kost1 = zpg.Kost1, Kost2 = zpg.Kost2, Konto = zpg.Konto, Betrag19 = 0m, Betrag7 = 0m, Betrag0 = -Math.Round(vonGuthaben, 2), SaldoPersonalguthaben = neuerSaldo, FirmenId = -1, AutomatenName = AppSettings.AutomatenName, Kassenbestand = GetCurrentKassenbestandEuro() };
                                    await db.InsertKassenbuchAsync(pgEntry); _personalGuthaben = neuerSaldo; lblGuthaben.Text = $"Personal-Guthaben: {_personalGuthaben:C2}";
                                }
                                    if (lblEingezahlt != null) lblEingezahlt.Text = _eingezahltSession.ToString("C2");
                            }
                            else
                            {
                                decimal gut = -(-Convert.ToDecimal(_currentAuszahlungRow["Betrag19"]) + -Convert.ToDecimal(_currentAuszahlungRow["Betrag7"]) + -Convert.ToDecimal(_currentAuszahlungRow["Betrag0"])); _eingezahltSession += gut; if (lblEingezahlt != null) lblEingezahlt.Text = _eingezahltSession.ToString("C2");
                            }
                            int belegnummer = Convert.ToInt32(_currentAuszahlungRow["Belegnummer"]); await db.MarkZahlungAlsVerbuchtAsync(belegnummer); TryPrintReceipt(typ, betrag19, betrag7, betrag0, buchungstext);
                        }
                        MessageBox.Show(this, "Zahlung wurde erfolgreich abgerechnet.", "Erfolg", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        _currentAuszahlungRow = null; _currentZahlungIstEinzahlung = false; await LadeAlleAbrechenbarenItemsAsync(); SelectNextItemOrClear(); UpdateAbrechnenSummaries(); UpdateMaxVerfuegbar(); return;
                    }
                    catch (Exception ex)
                    {
                        if (ex is InvalidOperationException iox && string.Equals(iox.Message, "NEGATIVE_KASSENBESTAND", StringComparison.Ordinal)) { MessageBox.Show(this, "Abrechnen nicht möglich: Zu wenig Geld.", "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                        MessageBox.Show(this, $"Fehler beim Abrechnen:\r\n{ex}", "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error); return;
                    }
                }
                if (_details != null)
                {
                    try
                    {
                        decimal offen19 = _details.Betrag19; decimal offen7 = _details.Betrag7; decimal offen0 = _details.Betrag0; decimal summe = offen19 + offen7 + offen0;
                        decimal aktuellerBestand = GetCurrentKassenbestandEuro(); if (aktuellerBestand + summe < 0m) { MessageBox.Show(this, "Diese Abrechnung würde den Kassenbestand negativ machen.", "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                        using (var db = new DatabaseHelper())
                        {
                            await db.UpdateSchichtEinzahlungAsync(_details.SchichtId, _personal.PID, _details.EinzahlungBisher + summe, offen19, offen7, offen0);
                            decimal vonEingezahlt = Math.Min(_eingezahltSession, summe); decimal vonGuthaben = Math.Max(0m, summe - vonEingezahlt); _eingezahltSession = Math.Round(_eingezahltSession - vonEingezahlt, 2);
                            if (vonGuthaben > 0m)
                            {
                                var zpg = AccountingRules.GetPersonalguthabenKontierung(_details.ManId); decimal neuerSaldo = Math.Round(_personalGuthaben - vonGuthaben, 2);
                                var pgEntry = new KassenbuchEntry { PersId = _personal.PID, SchichtId = 0, Typ = "Personalguthaben", Buchungstext = AccountingRules.ComposePgEinzahlungText(_personal), Kost1 = zpg.Kost1, Kost2 = zpg.Kost2, Konto = zpg.Konto, Betrag19 = 0m, Betrag7 = 0m, Betrag0 = -Math.Round(vonGuthaben, 2), SaldoPersonalguthaben = neuerSaldo, FirmenId = -1, AutomatenName = AppSettings.AutomatenName, Kassenbestand = GetCurrentKassenbestandEuro() };
                                await db.InsertKassenbuchAsync(pgEntry); _personalGuthaben = neuerSaldo; lblGuthaben.Text = $"Personal-Guthaben: {_personalGuthaben:C2}";
                            }
                            string kennzeichen = ""; // ensure declared before use
                            try { if (_details.FhzId > 0) { var fahrzeug = await db.GetFahrzeugInfoAsync(_details.FhzId); if (fahrzeug != null) kennzeichen = fahrzeug.Kennzeichen; } } catch { }
                            string buchungstextSchicht = AccountingRules.ComposeSchichtBuchungstext(_personal, _details, kennzeichen);
                            decimal diffSum = _details.Betrag19 + _details.Betrag7 + _details.Betrag0; bool istNachzahlung = _details.EinzahlungBisher != 0m && diffSum > 0m; bool istRueckzahlung = _details.EinzahlungBisher != 0m && diffSum < 0m;
                            if (istNachzahlung || istRueckzahlung)
                            {
                                string marker = istNachzahlung ? "Nachzahlung" : "Rückzahlung"; int pos = buchungstextSchicht.IndexOf("Schicht", StringComparison.OrdinalIgnoreCase);
                                if (pos > 0) buchungstextSchicht = buchungstextSchicht.Substring(0, pos).TrimEnd() + " " + marker + " " + buchungstextSchicht.Substring(pos);
                                else
                                {
                                    string namePrefix = $"{_personal.Vorname} {_personal.Name}";
                                    if (buchungstextSchicht.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase)) buchungstextSchicht = namePrefix + " " + marker + " " + buchungstextSchicht.Substring(namePrefix.Length).TrimStart(); else buchungstextSchicht += " " + marker;
                                }
                                }
                            
                            if (offen19 != 0m)
                            {
                                var z19 = AccountingRules.GetKontierung19(_details); if (z19.Kost1 == 0 && z19.Kost2 == 0 && z19.Konto == 0) { var fb = ApplyRuleFallback("19", _details); if (fb.Kost1 != 0 || fb.Kost2 != 0 || fb.Konto != 0) z19 = fb; }
                                var entry19 = new KassenbuchEntry { PersId = _personal.PID, SchichtId = _details.SchichtId, Typ = "Schichtabrechnung", Buchungstext = buchungstextSchicht, Kost1 = z19.Kost1, Kost2 = z19.Kost2, Konto = z19.Konto, Betrag19 = offen19, Betrag7 = 0m, Betrag0 = 0m, FirmenId = _details.ManId, AutomatenName = AppSettings.AutomatenName, Kassenbestand = GetCurrentKassenbestandEuro() };
                                await db.InsertKassenbuchAsync(entry19);
                            }
                            if (offen7 != 0m)
                            {
                                var z7 = AccountingRules.GetKontierung7(_details); if (z7.Kost1 == 0 && z7.Kost2 == 0 && z7.Konto == 0) { var fb = ApplyRuleFallback("7", _details); if (fb.Kost1 != 0 || fb.Kost2 != 0 || fb.Konto != 0) z7 = fb; }
                                var entry7 = new KassenbuchEntry { PersId = _personal.PID, SchichtId = _details.SchichtId, Typ = "Schichtabrechnung", Buchungstext = buchungstextSchicht, Kost1 = z7.Kost1, Kost2 = z7.Kost2, Konto = z7.Konto, Betrag19 = 0m, Betrag7 = offen7, Betrag0 = 0m, FirmenId = _details.ManId, AutomatenName = AppSettings.AutomatenName, Kassenbestand = GetCurrentKassenbestandEuro() };
                                await db.InsertKassenbuchAsync(entry7);
                            }
                            if (offen0 != 0m)
                            {
                                var z0 = AccountingRules.GetKontierung0(_details); if (z0.Kost1 == 0 && z0.Kost2 == 0 && z0.Konto == 0) { var fb = ApplyRuleFallback("0", _details); if (fb.Kost1 != 0 || fb.Kost2 != 0 || fb.Konto != 0) z0 = fb; }
                                var entry0 = new KassenbuchEntry { PersId = _personal.PID, SchichtId = _details.SchichtId, Typ = "Schichtabrechnung", Buchungstext = buchungstextSchicht, Kost1 = z0.Kost1, Kost2 = z0.Kost2, Konto = z0.Konto, Betrag19 = 0m, Betrag7 = 0m, Betrag0 = offen0, FirmenId = _details.ManId, AutomatenName = AppSettings.AutomatenName, Kassenbestand = GetCurrentKassenbestandEuro() };
                                await db.InsertKassenbuchAsync(entry0);
                            }
                            try
                            {
                                string receiptType = istNachzahlung ? "NACHZAHLUNG" : (istRueckzahlung ? "RÜCKZAHLUNG" : "SCHICHTABRECHNUNG"); decimal r19 = offen19; decimal r7 = offen7; decimal r0 = offen0; decimal total = r19 + r7 + r0; if (total < 0) { r19 = -r19; r7 = -r7; r0 = -r0; }
                                var meta = $"::SCHMETA|ID={_details.SchichtId}|DAT={_details.StartZeit:dd.MM.yyyy HH:mm}"; if (!string.IsNullOrWhiteSpace(kennzeichen)) meta += $"|KEN={kennzeichen}"; meta += "::"; TryPrintReceipt(receiptType, r19, r7, r0, meta);
                            }
                            catch { }
                        }
                        if (_eingezahltSession > 0m) { await CreditLeftoverToPersonalguthabenAsync(_details.ManId, _details.SchichtId); }
                        MessageBox.Show(this, "Schicht wurde erfolgreich abgerechnet.", "Erfolg", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        _details = null; await LadeAlleAbrechenbarenItemsAsync(); SelectNextItemOrClear(); UpdateAbrechnenSummaries(); UpdateMaxVerfuegbar(); return;
                    }
                    catch (Exception ex) { MessageBox.Show(this, $"Fehler beim Abrechnen der Schicht:\r\n{ex}", "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
                }
            }
            finally { _bookingInProgress = false; UpdateBusyUI(); }
        }

        private async void btnAbmelden_Click(object sender, EventArgs e) { await AbmeldenCoreAsync(false); }
        public Task ForceAbmeldenFromAdminAsync() => AbmeldenCoreAsync(true);
        private async Task AbmeldenCoreAsync(bool ignoreAdminMode)
        {
            try
            {
                if (!ignoreAdminMode && _auswahlItems != null && _auswahlItems.Any(ai => ai.Schicht != null))
                {
                    var res = MessageBox.Show(this, "Es gibt noch offene Schichten. Trotzdem abmelden?", "Offene Schichten", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2); if (res != DialogResult.Yes) return;
                }
            }
            catch { }
            try
            {
                try { if (_tmrAvail != null) { _tmrAvail.Enabled = false; _tmrAvail.Stop(); } } catch { }
                if (AdminMode.IsOpen && !ignoreAdminMode) { MessageBox.Show(this, "Im Admin-Modus ist Abmelden deaktiviert.", "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                // Personalguthaben beim Abmelden immer als Delta zum letzten DB-Saldo buchen,
                // damit bereits reduzierte Guthaben (durch Auszahlungen) nicht verloren gehen.
                int manId = _details?.ManId ?? (_currentAuszahlungRow != null && _currentAuszahlungRow.Table.Columns.Contains("FirmenID") && _currentAuszahlungRow["FirmenID"] != DBNull.Value ? Convert.ToInt32(_currentAuszahlungRow["FirmenID"]) : 0);
                int schichtId = _details?.SchichtId ?? 0;
                using (var db = new DatabaseHelper())
                {
                    decimal alterSaldo = await db.GetLastPersonalGuthabenSaldoAsync(_personal.PID);
                    decimal neuerSaldo = Math.Round(_personalGuthaben, 2);

                    // Zusätzliches Session-Eingezahlt (z.B. durch Münz-/Schein-Einwurf ohne sofortige PG-Buchung)
                    // wird dem gewünschten Guthaben-Saldo zugeschlagen.
                    if (_eingezahltSession != 0m)
                    {
                        neuerSaldo = Math.Round(neuerSaldo + Math.Round(_eingezahltSession, 2), 2);
                    }

                    decimal delta = Math.Round(neuerSaldo - alterSaldo, 2);
                    if (delta != 0m)
                    {
                        var zpg = AccountingRules.GetPersonalguthabenKontierung(manId);
                        string buchungstext = delta > 0m ? AccountingRules.ComposePgEinbuchungText(_personal) : AccountingRules.ComposePgEinzahlungText(_personal);
                        var entry = new KassenbuchEntry
                        {
                            PersId = _personal.PID,
                            SchichtId = schichtId,
                            Typ = "Personalguthaben",
                            Buchungstext = buchungstext,
                            Kost1 = zpg.Kost1,
                            Kost2 = zpg.Kost2,
                            Konto = zpg.Konto,
                            Betrag19 = 0m,
                            Betrag7 = 0m,
                            Betrag0 = delta,
                            SaldoPersonalguthaben = neuerSaldo,
                            FirmenId = -1,
                            AutomatenName = AppSettings.AutomatenName,
                            Kassenbestand = GetCurrentKassenbestandEuro()
                        };
                        await db.InsertKassenbuchAsync(entry);
                    }

                    _personalGuthaben = neuerSaldo;
                    _eingezahltSession = 0m;
                    lblGuthaben.Text = $"Personal-Guthaben: {_personalGuthaben:C2}";
                    if (lblEingezahlt != null) lblEingezahlt.Text = _eingezahltSession.ToString("C2");
                    UpdateMaxVerfuegbar();
                }
            }
            catch (Exception ex) { if (!ignoreAdminMode) MessageBox.Show(this, $"Fehler beim Abmelden:\r\n{ex.Message}", "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally
            {
                try
                {
                    var nv1 = _ssp; var nv2 = Program.NV2002Instance;
                    try { nv1?.Payout_angleichen(); nv2?.Payout_angleichen(); await Task.Delay(200); nv1?.Payout_angleichen(); nv2?.Payout_angleichen(); } catch { }
                    try { RequestCoinLevels(); await Task.Delay(700); RequestCoinLevels(); await Task.Delay(600); } catch { }
                    int[] lv1 = null; int[] lv2 = null;
                    try { if (_coin is SmartCoinV1 sc1) lv1 = sc1.GetCoinAvailability(); else if (_coin is Rm5CctalkValidator rm5a) lv1 = rm5a.GetCoinAvailability(); } catch { }
                    try { if (_coin2 == null) { try { Coin2Manager.InitFromIni(AppSettings.IniPath); } catch { } _coin2 = Coin2Manager.Instance; } if (_coin2 is SmartCoinV1 sc2) lv2 = sc2.GetCoinAvailability(); else if (_coin2 is Rm5CctalkValidator rm5b) lv2 = rm5b.GetCoinAvailability(); } catch { }

                    // Persist RM5 levels explicitly on logout to avoid losing last payouts
                    try
                    {
                        if (_coin is Rm5CctalkValidator rm5Main)
                        {
                            var cur = lv1 ?? rm5Main.GetCoinAvailability();
                            if (cur != null && cur.Length >= 8) rm5Main.SetAllCoinLevels(cur, true);
                        }
                        if (_coin2 is Rm5CctalkValidator rm5Second)
                        {
                            var cur2 = lv2 ?? rm5Second.GetCoinAvailability();
                            if (cur2 != null && cur2.Length >= 8) rm5Second.SetAllCoinLevels(cur2, true);
                        }
                    }
                    catch { }

                    try { AppLogger.LogKassenbestandSnapshotCombined(nv1, lv1, nv2, lv2, false, "Abmeldung"); } catch { }
                    decimal automatSum = 0m; try { automatSum += AppLogger.ComputeKassenbestandEuro(nv1, lv1); } catch { }
                    try { automatSum += AppLogger.ComputeKassenbestandEuro(nv2, lv2); } catch { }
                    automatSum = Math.Round(automatSum, 2);
                    decimal kassenSum = 0m; string autoName = AppSettings.AutomatenName; if (string.IsNullOrWhiteSpace(autoName)) autoName = AppSettings.LoadAutomatenNameFromIni();
                    try { using (var db = new DatabaseHelper()) { var dt = await db.GetLatestKassenbestaendeAsync(autoName ?? string.Empty); foreach (DataRow r in dt.Rows) if (r["Kassenbestand"] != DBNull.Value) kassenSum += Convert.ToDecimal(r["Kassenbestand"]); } kassenSum = Math.Round(kassenSum, 2); } catch { }
                // Zusätzlicher Logeintrag: Kassenbestand zwischen Summe Gesamt und Kassendifferenz
                try { AppLogger.Log($"Kassenbestand: {kassenSum:0.00} €"); } catch { }
                    try {
                        decimal diff = Math.Round(automatSum - kassenSum, 2);
                        AppLogger.Log($"Kassendifferenz bei Abmeldung: {diff:0.00} €");
                        try { KassenSummary.Update(automatSum, kassenSum); } catch { }
                    } catch { }
                }
                catch { }
                try { _coin?.Enable(false); } catch { }
                try { _coin2?.Enable(false); } catch { }
                try { AppLogger.Log(""); AppLogger.Log(new string('-', 80)); AppLogger.Log(""); } catch { }
                Close();
            }
        }

        private decimal GetCurrentKassenbestandEuro()
        {
            try { int[] lv = null; if (_coin is SmartCoinV1 sc1) lv = sc1.GetCoinAvailability(); else if (_coin is Rm5CctalkValidator rm5) lv = rm5.GetCoinAvailability(); return AppLogger.ComputeKassenbestandEuro(_ssp, lv); } catch { return 0m; }
        }

        private void AttachSspEvents()
        {
            if (_ssp == null || _eventsAttached) return;
            _ssp.Note_akzepted += SspOnNoteAccepted; _ssp.Ereignis += SspOnEreignis; _ssp.Wert_Dispensing += SspOnWertDispensing; _ssp.Dispensing_Complete += SspOnDispensingComplete; _ssp.Error_while_payout += SspOnPayoutError;
            try
            {
                var ssp2 = Program.NV2002Instance; if (ssp2 != null)
                {
                    ssp2.Note_akzepted += SspOnNoteAccepted; ssp2.Wert_Dispensing += SspOnWertDispensing; ssp2.Dispensing_Complete += SspOnDispensingComplete; ssp2.Error_while_payout += SspOnPayoutError; ssp2.Ereignis += SspOnEreignis; ssp2.Note_in_Bezel += (wert) => Console.WriteLine($"(NV200/2) Schein zur Entnahme bereit: {wert} €");
                }
            }
            catch { }
            _ssp.Note_in_Bezel += (wert) => { Console.WriteLine($"Schein zur Entnahme bereit: {wert} €"); };
            _eventsAttached = true;
        }

        private void DetachSspEvents()
        {
            if (_ssp == null || !_eventsAttached) return;
            _ssp.Error_while_payout -= SspOnPayoutError; _ssp.Note_akzepted -= SspOnNoteAccepted; _ssp.Ereignis -= SspOnEreignis; _ssp.Wert_Dispensing -= SspOnWertDispensing; _ssp.Dispensing_Complete -= SspOnDispensingComplete; _ssp.Error_while_payout -= SspOnPayoutError;
            try
            {
                var ssp2 = Program.NV2002Instance; if (ssp2 != null)
                {
                    ssp2.Note_akzepted -= SspOnNoteAccepted; ssp2.Wert_Dispensing -= SspOnWertDispensing; ssp2.Dispensing_Complete -= SspOnDispensingComplete; ssp2.Error_while_payout -= SspOnPayoutError; ssp2.Ereignis -= SspOnEreignis;
                }
            }
            catch { }
            _eventsAttached = false;
        }

        private void btnManuellAdd_Click(object sender, EventArgs e)
        {
            if (AdminMode.IsOpen) { MessageBox.Show(this, "Im Admin-Modus deaktiviert.", "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            AddEingezahlt(nudManuell.Value);
        }

        private void AddEingezahlt(decimal betrag)
        {
            if (betrag == 0) return; if (AdminMode.IsOpen) return; if (AppLogger.KassensturzActive) return; _eingezahltSession += betrag; UpdateAbrechnenSummaries(); UpdateMaxVerfuegbar();
        }

        private void BtnMinus_Click(object sender, EventArgs e)
        {
            int idx = (int)((Button)sender).Tag; if (auswahlAnzahl[idx] > 0) auswahlAnzahl[idx]--; lblAnzahl[idx].Text = auswahlAnzahl[idx].ToString(); UpdateSummeAuszahlung(); UpdatePlusMinusEnabled();
        }
        private void BtnPlus_Click(object sender, EventArgs e)
        {
            int idx = (int)((Button)sender).Tag;
            int avail = GetAvailByIndex(idx);
            if (auswahlAnzahl[idx] < avail)
            {
                auswahlAnzahl[idx]++;
                lblAnzahl[idx].Text = auswahlAnzahl[idx].ToString();
                UpdateSummeAuszahlung();
            }
            UpdatePlusMinusEnabled();
        }
        private void BtnMinusMuenzen_Click(object sender, EventArgs e)
        {
            int idx = (int)((Button)sender).Tag; if (auswahlAnzahlMuenzen[idx] > 0) auswahlAnzahlMuenzen[idx]--; lblAnzahlMuenzen[idx].Text = auswahlAnzahlMuenzen[idx].ToString(); UpdateSummeAuszahlung(); UpdatePlusMinusEnabled();
        }
        private void BtnPlusMuenzen_Click(object sender, EventArgs e)
        {
            int idx = (int)((Button)sender).Tag;
            int avail = GetCombinedCoinAvail(idx);
            bool canIncrease = avail < 0 || auswahlAnzahlMuenzen[idx] < avail;
            if (canIncrease)
            {
                auswahlAnzahlMuenzen[idx]++;
                lblAnzahlMuenzen[idx].Text = auswahlAnzahlMuenzen[idx].ToString();
                UpdateSummeAuszahlung();
            }
            UpdatePlusMinusEnabled();
        }

        private void btnAuszahlen_Click(object sender, EventArgs e)
        {
            if (AdminMode.IsOpen) { MessageBox.Show(this, "Im Admin-Modus deaktiviert.", "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (_payoutInProgress) { MessageBox.Show(this, "Es läuft bereits eine Auszahlung.", "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            decimal sumNotes = 0m; for (int i = 0; i < scheinWerte.Length; i++) sumNotes += scheinWerte[i] * auswahlAnzahl[i];
            decimal sumCoins = 0m; for (int i = 0; i < muenzWerte.Length; i++) sumCoins += (muenzWerte[i] * auswahlAnzahlMuenzen[i]) / 100m;
            if (sumNotes <= 0m && sumCoins <= 0m) { MessageBox.Show(this, "Bitte Auswahl treffen.", "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            decimal maxVerfuegbar = _eingezahltSession + _personalGuthaben;
            decimal gesamt = sumNotes + sumCoins;
            if (sumNotes > 0m)
            {
                var ssp1 = _ssp; var ssp2 = Program.NV2002Instance; if (ssp1 == null && ssp2 == null) { MessageBox.Show(this, "Kein NV200 verbunden.", "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                int[] avail1 = { Math.Max(0, ssp1?.Payout_5_euro ?? 0), Math.Max(0, ssp1?.Payout_10_euro ?? 0), Math.Max(0, ssp1?.Payout_20_euro ?? 0), Math.Max(0, ssp1?.Payout_50_euro ?? 0), Math.Max(0, ssp1?.Payout_100_euro ?? 0), Math.Max(0, ssp1?.Payout_200_euro ?? 0), Math.Max(0, ssp1?.Payout_500_euro ?? 0) };
                int[] avail2 = { Math.Max(0, ssp2?.Payout_5_euro ?? 0), Math.Max(0, ssp2?.Payout_10_euro ?? 0), Math.Max(0, ssp2?.Payout_20_euro ?? 0), Math.Max(0, ssp2?.Payout_50_euro ?? 0), Math.Max(0, ssp2?.Payout_100_euro ?? 0), Math.Max(0, ssp2?.Payout_200_euro ?? 0), Math.Max(0, ssp2?.Payout_500_euro ?? 0) };
                var req1 = new int[7]; var req2 = new int[7];
                for (int i = 0; i < scheinWerte.Length; i++)
                {
                    int need = auswahlAnzahl[i]; int a1 = avail1[i]; int a2 = avail2[i]; if (!TryBalancedSplit(need, a1, a2, out int take1, out int take2)) { MessageBox.Show(this, "Nicht genügend Scheine verfügbar.", "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                    req1[i] = take1; req2[i] = take2;
                }
                _notesPayoutInProgress = true; _activeNotePayoutDevices = 0; UpdateBusyUI();
                try { ssp1?.SetInhibit(false); } catch { }
                try { ssp2?.SetInhibit(false); } catch { }
                try
                {
                    if (req1.Sum() > 0 && ssp1 != null) { _activeNotePayoutDevices++; ssp1.PayoutByDenomination(req1); }
                    if (req2.Sum() > 0 && ssp2 != null) { _activeNotePayoutDevices++; ssp2.PayoutByDenomination(req2); }
                    if (_activeNotePayoutDevices == 0) { _notesPayoutInProgress = false; UpdateBusyUI(); MessageBox.Show(this, "Keine Scheine auszuzahlen.", "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                }
                catch (Exception ex)
                {
                    _activeNotePayoutDevices = 0; _notesPayoutInProgress = false; UpdateBusyUI(); MessageBox.Show(this, $"Auszahlung (Scheine) Fehler:\r\n{ex.Message}", "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error); return;
                }
                _geplanteAuszahlung = 0m; for (int i = 0; i < scheinWerte.Length; i++) _geplanteAuszahlung += scheinWerte[i] * auswahlAnzahl[i];
            }
            if (sumCoins > 0m)
            {
                if (_coin == null) { MessageBox.Show(this, "Münzgerät nicht verbunden.", "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                try
                {
                    bool isRm5 = _coin is Rm5CctalkValidator; var sc1 = _coin as SmartCoinV1; if (sc1 == null && !isRm5) { MessageBox.Show(this, "Gerät unterstützt keine Münzauszahlung.", "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                    _coin.Enable(true); if (_coin2 != null && !isRm5) { try { _coin2.Enable(true); } catch { } }
                    _coinsPayoutInProgress = true; UpdateBusyUI();
                    if (isRm5)
                    {
                        try { _activeCoinPayoutDevices = 1; _coin.PayoutCoins(auswahlAnzahlMuenzen.ToArray()); } catch (Exception ex) { MessageBox.Show(this, "RM5 Auszahlung Fehler: " + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error); _coinsPayoutInProgress = false; UpdateBusyUI(); return; }
                        if (lblSummeAuszahlung != null) lblSummeAuszahlung.Text = $"{(_geplanteAuszahlung + sumCoins):C2}"; return;
                    }
                    int[] reqMain = new int[8]; int[] reqSecond = new int[8]; bool haveSecond = _coin2 != null && _coin2.Connected;
                    for (int i = 0; i < auswahlAnzahlMuenzen.Length; i++)
                    {
                        int need = auswahlAnzahlMuenzen[i]; if (need <= 0) { reqMain[i] = 0; reqSecond[i] = 0; continue; }
                        if (!haveSecond) { reqMain[i] = need; reqSecond[i] = 0; continue; }
                        int a1 = _coinAvail[i] >= 0 ? _coinAvail[i] : 0; int a2 = _coin2Avail[i] >= 0 ? _coin2Avail[i] : 0; if (a1 + a2 < need) { MessageBox.Show(this, $"Nicht genügend Münzen ({muenzWerte[i]} ct).", "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Warning); _coinsPayoutInProgress = false; UpdateBusyUI(); return; }
                        if (a1 > 40 && a2 > 40)
                        {
                            int take1 = need / 2 + (need % 2); int take2 = need - take1; if (take1 > a1) { int deficit = take1 - a1; take1 = a1; take2 += deficit; }
                            if (take2 > a2) { int deficit = take2 - a2; take2 = a2; take1 += deficit; }
                            reqMain[i] = take1; reqSecond[i] = take2;
                        }
                        else
                        {
                            if (a1 >= a2) { int take1 = Math.Min(need, a1); int rest = need - take1; int take2 = Math.Min(rest, a2); reqMain[i] = take1; reqSecond[i] = take2; }
                            else { int take2 = Math.Min(need, a2); int rest = need - take2; int take1 = Math.Min(rest, a1); reqMain[i] = take1; reqSecond[i] = take2; }
                        }
                    }
                    if (sc1 != null) sc1.PayoutCoins(reqMain); if (haveSecond && reqSecond.Sum() > 0) { var secV1 = _coin2 as SmartCoinV1; secV1?.PayoutCoins(reqSecond); }
                    _activeCoinPayoutDevices = 0; if (reqMain.Sum() > 0) _activeCoinPayoutDevices++; if (reqSecond.Sum() > 0) _activeCoinPayoutDevices++;
                    _geplanteMuenzAuszahlung = sumCoins;
                }
                catch (Exception ex)
                {
                    _geplanteMuenzAuszahlung = 0m; _activeCoinPayoutDevices = 0; _coinsPayoutInProgress = false; UpdateBusyUI(); MessageBox.Show(this, $"Münzauszahlung Fehler:\r\n{ex.Message}", "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void SafeRefreshAvailability()
        {
            if (_ssp != null) { try { _ssp.Payout_angleichen(); } catch { } }
            try { var ssp2 = Program.NV2002Instance; ssp2?.Payout_angleichen(); } catch { }
            int[] avail = GetCurrentAvailability(); for (int i = 0; i < 7; i++) if (lblVerfuegbar[i] != null) lblVerfuegbar[i].Text = $"{avail[i]}";
            UpdateCoinAvailabilityLabels(); UpdatePlusMinusEnabled(); UpdateBusyUI();
        }

        private int[] GetCurrentAvailability()
        {
            var ssp1 = _ssp; var ssp2 = Program.NV2002Instance; return new int[] { Math.Max(0, (ssp1?.Payout_5_euro ?? 0) + (ssp2?.Payout_5_euro ?? 0)), Math.Max(0, (ssp1?.Payout_10_euro ?? 0) + (ssp2?.Payout_10_euro ?? 0)), Math.Max(0, (ssp1?.Payout_20_euro ?? 0) + (ssp2?.Payout_20_euro ?? 0)), Math.Max(0, (ssp1?.Payout_50_euro ?? 0) + (ssp2?.Payout_50_euro ?? 0)), Math.Max(0, (ssp1?.Payout_100_euro ?? 0) + (ssp2?.Payout_100_euro ?? 0)), Math.Max(0, (ssp1?.Payout_200_euro ?? 0) + (ssp2?.Payout_200_euro ?? 0)), Math.Max(0, (ssp1?.Payout_500_euro ?? 0) + (ssp2?.Payout_500_euro ?? 0)) };
        }

        private void UpdateSummeAuszahlung()
        {
            decimal sumScheine = 0m; for (int i = 0; i < scheinWerte.Length; i++) sumScheine += scheinWerte[i] * auswahlAnzahl[i];
            decimal sumMuenzen = 0m; for (int i = 0; i < muenzWerte.Length; i++) sumMuenzen += (muenzWerte[i] * auswahlAnzahlMuenzen[i]) / 100m;
            decimal summe = sumScheine + sumMuenzen; if (lblSummeAuszahlung != null) lblSummeAuszahlung.Text = $"{summe:C2}"; UpdateAuszahlenEnabled();
        }

        private void UpdatePlusMinusEnabled()
        {
            int[] avail = GetCurrentAvailability();
            for (int i = 0; i < 7; i++)
            {
                if (btnPlus[i] != null) btnPlus[i].Enabled = !_payoutInProgress && !AdminMode.IsOpen && (auswahlAnzahl[i] < avail[i]);
                if (btnMinus[i] != null) btnMinus[i].Enabled = !_payoutInProgress && !AdminMode.IsOpen && (auswahlAnzahl[i] > 0);
            }
            for (int i = 0; i < btnPlusMuenzen.Length; i++)
            {
                int availCoinsCombined = GetCombinedCoinAvail(i); bool canIncrease = availCoinsCombined < 0 || auswahlAnzahlMuenzen[i] < availCoinsCombined; if (btnPlusMuenzen[i] != null) btnPlusMuenzen[i].Enabled = !_payoutInProgress && !AdminMode.IsOpen && canIncrease;
                if (btnMinusMuenzen[i] != null) btnMinusMuenzen[i].Enabled = !_payoutInProgress && !AdminMode.IsOpen && auswahlAnzahlMuenzen[i] > 0;
            }
        }

        private void SspOnNoteAccepted(int wert)
        {
            if (wert <= 0) return;
            BeginInvoke((Action)(() => { if (!AdminMode.IsOpen) { AddEingezahlt(wert); } SafeRefreshAvailability(); }));
        }
        private void SspOnEreignis(string text) { try { BeginInvoke((Action)(() => Text = $"Schicht abrechnen – NV200: {text}")); } catch { } }
        private void SspOnWertDispensing(int cent)
        {
            if (AppLogger.KassensturzActive) return; BeginInvoke((Action)(() =>
            {
                _notesDispensedCent += cent; decimal delta = Math.Round(cent / 100m, 2); if (delta <= 0m) return; decimal remain = delta;
                if (_eingezahltSession > 0m) { var take = Math.Min(_eingezahltSession, remain); _eingezahltSession = Math.Round(_eingezahltSession - take, 2); remain = Math.Round(remain - take, 2); }
                if (remain > 0m && _personalGuthaben > 0m) { var takeG = Math.Min(_personalGuthaben, remain); _personalGuthaben = Math.Round(_personalGuthaben - takeG, 2); remain = Math.Round(remain - takeG, 2); _consumedGuthabenNotes = Math.Round(_consumedGuthabenNotes + takeG, 2); }
                decimal restScheine = Math.Max(0m, Math.Round(_geplanteAuszahlung - (_notesDispensedCent / 100m), 2)); decimal restMuenzen = Math.Max(0m, Math.Round(_geplanteMuenzAuszahlung, 2)); decimal restSum = restScheine + restMuenzen; if (_payoutInProgress && lblSummeAuszahlung != null) lblSummeAuszahlung.Text = $"{restSum:C2}";
                lblGuthaben.Text = $"Personal-Guthaben: {_personalGuthaben:C2}"; lblEingezahlt.Text = $"Eingezahlt: {_eingezahltSession:C2}"; UpdateAbrechnenSummaries(); UpdateMaxVerfuegbar();
            }));
        }
        private async void SspOnDispensingComplete(int cent)
        {
            if (InvokeRequired) { BeginInvoke((Action)(() => SspOnDispensingComplete(cent))); return; }
            if (_activeNotePayoutDevices > 0) _activeNotePayoutDevices = Math.Max(0, _activeNotePayoutDevices - 1); if (_activeNotePayoutDevices > 0) return;
            try
            {
                for (int i = 0; i < auswahlAnzahl.Length; i++) { auswahlAnzahl[i] = 0; if (lblAnzahl[i] != null) lblAnzahl[i].Text = "0"; }
                lblGuthaben.Text = $"Personal-Guthaben: {_personalGuthaben:C2}"; UpdateAbrechnenSummaries(); SafeRefreshAvailability(); UpdateMaxVerfuegbar();
            }
            finally
            {
                _notesPayoutInProgress = false; UpdateBusyUI(); try { _ssp?.SetInhibit(false); } catch { }
                try { Program.NV2002Instance?.SetInhibit(false); } catch { }
            }
            if (!_coinsPayoutInProgress && !AppLogger.KassensturzActive)
            {
                decimal sumPg = Math.Round(_consumedGuthabenNotes + _consumedGuthabenCoins, 2); if (sumPg > 0m)
                {
                    try
                    {
                        using (var db = new DatabaseHelper())
                        {
                            int manId = _details?.ManId ?? 0; int schichtId = _details?.SchichtId ?? 0; var zpg = AccountingRules.GetPersonalguthabenKontierung(manId);
                            var entry = new KassenbuchEntry { PersId = _personal.PID, SchichtId = schichtId, Typ = "Personalguthaben", Buchungstext = $"Personalguthaben-Auszahlung ({_personal.Vorname} {_personal.Name})", Kost1 = zpg.Kost1, Kost2 = zpg.Kost2, Konto = zpg.Konto, Betrag19 = 0m, Betrag7 = 0m, Betrag0 = -sumPg, SaldoPersonalguthaben = _personalGuthaben, FirmenId = -1, AutomatenName = AppSettings.AutomatenName, Kassenbestand = GetCurrentKassenbestandEuro() };
                            await db.InsertKassenbuchAsync(entry);
                        }
                    }
                    catch { }
                    finally { _consumedGuthabenNotes = 0m; _consumedGuthabenCoins = 0m; }
                }
            }
            if (!_payoutInProgress && lblSummeAuszahlung != null) lblSummeAuszahlung.Text = "0,00 €"; _geplanteAuszahlung = 0m; _notesDispensedCent = 0;
            try { _lastPayoutLevelsScheduleUtc = DateTime.MinValue; } catch { }
        }

        private async void OnCoinDispenseComplete()
        {
            if (InvokeRequired) { BeginInvoke((Action)(() => OnCoinDispenseComplete())); return; }
            if (_activeCoinPayoutDevices > 0) _activeCoinPayoutDevices = Math.Max(0, _activeCoinPayoutDevices - 1); if (_activeCoinPayoutDevices > 0) return;
            try
            {
                decimal coinsTotal = Math.Round(_coinsDispensedCentTotal / 100m, 2); if (coinsTotal > 0m) { AppLogger.Log($"Münzauszahlung Summe: {coinsTotal:0.00} € (PG: {_consumedGuthabenCoins:0.00} €)"); }
                for (int i = 0; i < auswahlAnzahlMuenzen.Length; i++) { auswahlAnzahlMuenzen[i] = 0; if (lblAnzahlMuenzen[i] != null) lblAnzahlMuenzen[i].Text = "0"; }
                _geplanteMuenzAuszahlung = 0m; lblGuthaben.Text = $"Personal-Guthaben: {_personalGuthaben:C2}"; UpdateAbrechnenSummaries(); SafeRefreshAvailability(); UpdateMaxVerfuegbar();
            }
            finally
            {
                _coinsDispensedCentTotal = 0; _coinsPayoutInProgress = false; UpdateBusyUI(); try { _ssp?.SetInhibit(false); } catch { }
                try { Program.NV2002Instance?.SetInhibit(false); } catch { }
            }
            if (!_notesPayoutInProgress)
            {
                decimal sumPg = Math.Round(_consumedGuthabenNotes + _consumedGuthabenCoins, 2); if (sumPg > 0m)
                {
                    try
                    {
                        using (var db = new DatabaseHelper())
                        {
                            int manId = _details?.ManId ?? 0; int schichtId = _details?.SchichtId ?? 0; var zpg = AccountingRules.GetPersonalguthabenKontierung(manId);
                            var entry = new KassenbuchEntry { PersId = _personal.PID, SchichtId = schichtId, Typ = "Personalguthaben", Buchungstext = $"Personalguthaben-Auszahlung ({_personal.Vorname} {_personal.Name})", Kost1 = zpg.Kost1, Kost2 = zpg.Kost2, Konto = zpg.Konto, Betrag19 = 0m, Betrag7 = 0m, Betrag0 = -sumPg, SaldoPersonalguthaben = _personalGuthaben, FirmenId = -1, AutomatenName = AppSettings.AutomatenName, Kassenbestand = GetCurrentKassenbestandEuro() };
                            await db.InsertKassenbuchAsync(entry);
                        }
                    }
                    catch { }
                    finally { _consumedGuthabenNotes = 0m; _consumedGuthabenCoins = 0m; }
                }
            }
            if (!_payoutInProgress && lblSummeAuszahlung != null) lblSummeAuszahlung.Text = "0,00 €"; AppLogger.Log("Münzauszahlung abgeschlossen");
            try { _lastPayoutLevelsScheduleUtc = DateTime.MinValue; } catch { }
        }

        private int GetAvailByIndex(int idx) { var avail = GetCurrentAvailability(); return (idx >= 0 && idx < avail.Length) ? avail[idx] : 0; }

        private async void BtnSchichtAuswahl_Click(object sender, EventArgs e)
        {
            await LadeAlleAbrechenbarenItemsAsync(); if (_auswahlItems.Count <= 1) return; using (var dlg = new AuswahlDialog(_auswahlItems)) { if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedItem != null) { if (dlg.SelectedItem.Schicht != null) LadeSchicht(dlg.SelectedItem.Schicht); else if (dlg.SelectedItem.Auszahlung != null) LadeAuszahlung(dlg.SelectedItem.Auszahlung); } }
        }

        private async void LadeSchicht(ShiftDetails details)
        {
            _details = details; lblTitel.Text = $"Mitarbeiter: {_personal.Vorname} {_personal.Name}"; lblB19.Text = $"19%: {_details.Betrag19:C2}"; lblB7.Text = $"7%: {_details.Betrag7:C2}"; lblB0.Text = $"0%: {_details.Betrag0:C2}";
            if (lblSumme != null) lblSumme.Text = _details.SummeZuZahlen.ToString("C2");
            if (lblEingezahlt != null) lblEingezahlt.Text = _eingezahltSession.ToString("C2");
            string kennzeichen = "";
            using (var db = new DatabaseHelper())
            {
                if (_details.FhzId > 0) { var fahrzeug = await db.GetFahrzeugInfoAsync(_details.FhzId); if (fahrzeug != null) kennzeichen = fahrzeug.Kennzeichen; }
                _personalGuthaben = await db.GetLastPersonalGuthabenSaldoAsync(_personal.PID); lblGuthaben.Text = $"Personal-Guthaben: {_personalGuthaben:C2}"; UpdateAbrechnenSummaries();
            }
            string datum = _details.StartZeit.ToString("dd.MM.yyyy HH:mm");
            string infoText;
            if (_details.EinzahlungBisher != 0)
            {
                decimal einnahmenBar = _details.Betrag19 + _details.Betrag7 + _details.Betrag0 + _details.EinzahlungBisher; decimal diff = einnahmenBar - _details.EinzahlungBisher; if (diff > 0) infoText = $"Nachzahlung Schicht \"{kennzeichen}\" vom {datum}"; else if (diff < 0) infoText = $"Rückzahlung Schicht \"{kennzeichen}\" vom {datum}"; else infoText = $"Schicht \"{kennzeichen}\" vom {datum}";
        }
            else infoText = $"Schicht \"{kennzeichen}\" vom {datum}";
            lblBelegInfo.Text = infoText;
            for (int i = 0; i < auswahlAnzahl.Length; i++) { auswahlAnzahl[i] = 0; if (lblAnzahl[i] != null) lblAnzahl[i].Text = "0"; }
            UpdateSummeAuszahlung(); SafeRefreshAvailability(); UpdateMaxVerfuegbar();
        }

        private void EnableAllDevicesOnOpen()
        {
            try { AttachSspEvents(); } catch { }

            // NV200/1
            try
        {
                if (_ssp != null)
            {
                    _ssp.AllowAutoEnable(true);
                    _ssp.SetInhibit(false);
                    _ssp.Freigeben();
                    try { _ssp.ConfigureBezel(0, 255, 0); } catch { }
                    try { _ssp.MitarbeiterEingeloggt = true; } catch { } // added
            }
        }
            catch { }

            // NV200/2
            try
        {
                var ssp2 = Program.NV2002Instance;
                if (ssp2 != null)
            {
                    ssp2.AllowAutoEnable(true);
                    ssp2.SetInhibit(false);
                    ssp2.Freigeben();
                    try { ssp2.ConfigureBezel(0, 255, 0); } catch { }
                    try { ssp2.MitarbeiterEingeloggt = true; } catch { }
                }
                }
            catch { }

            // SmartCoin/1 (Singleton)
            try
                {
                AttachCoinEvents();
                if (_coin != null)
                    {
                    if (!_coin.Connected)
                        {
                        try { _coin.Connect(); } catch { }
                        }
                    _coin.Enable(true);
                    }
                }
            catch { }

            // SmartCoin/2
            try
            {
                _coin2 = Coin2Manager.Instance;
                if (_coin2 != null)
                {
                    if (!_coin2.Connected)
                    {
                        try { _coin2.Connect(); } catch { }
                    }
                    AttachCoin2Events();
                    try { _coin2.Enable(true); } catch { }
                }
            }
            catch { }

            // UI/Bestände aktualisieren
            try
                {
                SafeRefreshAvailability();
                RequestCoinLevels();
                UpdatePlusMinusEnabled();
            }
            catch { }
        }


        private void LadeAuszahlung(DataRow row)
        {
            string typ = row.Table.Columns.Contains("Typ") ? (row["Typ"]?.ToString() ?? "Auszahlung") : "Auszahlung"; _currentAuszahlungRow = row; _currentZahlungIstEinzahlung = typ.Equals("Einzahlung", StringComparison.OrdinalIgnoreCase); _details = null;
            lblTitel.Text = $"Mitarbeiter: {_personal.Vorname} {_personal.Name}"; lblGuthaben.Text = $"Personal-Guthaben: {_personalGuthaben:C2}"; lblBelegInfo.Text = $"{typ}: {row["Buchungstext"]}";
            decimal raw19 = Math.Abs(Convert.ToDecimal(row["Betrag19"])); decimal raw7 = Math.Abs(Convert.ToDecimal(row["Betrag7"])); decimal raw0 = Math.Abs(Convert.ToDecimal(row["Betrag0"]));
            if (_currentZahlungIstEinzahlung)
            {
                decimal summe = raw19 + raw7 + raw0;
                lblB19.Text = $"19%:   {raw19:C2}"; lblB7.Text = $"7%: {raw7:C2}"; lblB0.Text = $"0%: {raw0:C2}";
                if (lblSumme != null) lblSumme.Text = summe.ToString("C2");
                if (lblEingezahlt != null) lblEingezahlt.Text = _eingezahltSession.ToString("C2");
                var rest = Math.Max(0m, summe - _eingezahltSession); var noch = Math.Max(0m, rest - _personalGuthaben);
                if (lblNoch != null) lblNoch.Text = noch.ToString("C2");
                try { if (lblNoch != null) lblNoch.ForeColor = (noch > 0m) ? Color.Red : Color.Green; } catch { }
                UpdateBuchenEnabled();
            }
            else
            {
                decimal summe = -raw19 - raw7 - raw0;
                lblB19.Text = $"19%:   {-raw19:C2}"; lblB7.Text = $"7%: {-raw7:C2}"; lblB0.Text = $"0%: {-raw0:C2}";
                if (lblSumme != null) lblSumme.Text = summe.ToString("C2");
                if (lblEingezahlt != null) lblEingezahlt.Text = _eingezahltSession.ToString("C2");
                if (lblNoch != null) lblNoch.Text = 0m.ToString("C2");
                try { if (lblNoch != null) lblNoch.ForeColor = Color.Green; } catch { }
                UpdateBuchenEnabled();
            }
        }

        private DataRow _currentAuszahlungRow = null;
        private List<AuswahlItem> _auswahlItems = new List<AuswahlItem>();

        private async Task LadeAlleAbrechenbarenItemsAsync()
        {
            _auswahlItems.Clear(); using (var db = new DatabaseHelper())
            {
                var dtSchichten = await db.GetOpenShiftsListAsync(_personal.PID);
                var allowed = ParseAllowedManIds();
                foreach (DataRow row in dtSchichten.Rows)
                {
                    int schichtId = Convert.ToInt32(row["SchichtId"]);
                    var details = await db.GetShiftDetailsAsync(schichtId);
                    if (details == null) continue;
                    if (allowed != null && allowed.Count > 0 && !allowed.Contains(details.ManId)) continue;
                    _auswahlItems.Add(new AuswahlItem { AnzeigeText = $"Schicht {details.SchichtId} vom {details.StartZeit:dd.MM.yyyy HH:mm}", Schicht = details });
                }
                var dtAuszahlungen = await db.GetOffeneAuszahlungenAsync(_personal.PID);
                foreach (DataRow row in dtAuszahlungen.Rows)
                {
                    if (allowed != null && allowed.Count > 0)
                    {
                        int manId = 0;
                        if (row.Table.Columns.Contains("FirmenID") && row["FirmenID"] != DBNull.Value)
                        {
                            int.TryParse(Convert.ToString(row["FirmenID"]), out manId);
                        }
                        if (!allowed.Contains(manId)) continue;
                    }
                    string belegnr = row["Belegnummer"].ToString();
                    string text = row["Buchungstext"]?.ToString() ?? "Zahlung";
                    string typ = row.Table.Columns.Contains("Typ") ? (row["Typ"]?.ToString() ?? "Zahlung") : "Zahlung";
                    // Betrag anzeigen statt Datum/Uhrzeit
                    string betragPart = string.Empty;
                    try
                    {
                        if (row.Table.Columns.Contains("BetragGesamt") && row["BetragGesamt"] != DBNull.Value)
                        {
                            var de = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
                            decimal betrag = Convert.ToDecimal(row["BetragGesamt"]);
                            betragPart = " (" + betrag.ToString("C2", de) + ")";
                        }
                    }
                    catch { }
                    _auswahlItems.Add(new AuswahlItem { AnzeigeText = $"{typ} {belegnr} ({text}){betragPart}", Auszahlung = row });
                }
            }
        }

        private List<int> ParseAllowedManIds()
        {
            try
            {
                var raw = AppSettings.AllowedManIdsRaw;
                if (string.IsNullOrWhiteSpace(raw)) return null;
                var parts = raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                var list = new List<int>();
                foreach (var p in parts)
                {
                    int v;
                    if (int.TryParse(p.Trim(), out v))
                    {
                        if (v > 0) list.Add(v);
                    }
                }
                return list;
            }
            catch { return null; }
        }

                private void UpdateMaxVerfuegbar() { if (lblMaxVerfuegbar != null) lblMaxVerfuegbar.Text = $"{(_personalGuthaben + _eingezahltSession):C2}"; }
                private void UpdateCoinAvailabilityLabels() { for (int i = 0; i < 8; i++) { if (lblVerfuegbarMuenzen[i] == null) continue; int a = _coinAvail[i] >= 0 ? _coinAvail[i] : 0; int b = _coin2Avail[i] >= 0 ? _coin2Avail[i] : 0; bool known = false; int total = 0; if (a > 0) { total += a; known = true; } if (b > 0) { total += b; known = true; } lblVerfuegbarMuenzen[i].Text = known ? $"{total}" : "-"; } }
                private int GetCombinedCoinAvail(int idx) { try { int a = (idx >= 0 && idx < _coinAvail.Length) ? _coinAvail[idx] : -1; int b = (idx >= 0 && idx < _coin2Avail.Length) ? _coin2Avail[idx] : -1; if (a < 0 && b < 0) return -1; int sum = 0; if (a > 0) sum += a; if (b > 0) sum += b; return sum; } catch { return -1; } }
                private void OnCoinDispensedDelta(int cent)
                {
                    if (AppLogger.KassensturzActive) return; if (cent <= 0) return; try { BeginInvoke((Action)(() => { _coinsDispensedCentTotal += cent; decimal delta = Math.Round(cent / 100m, 2); if (delta <= 0m) return; decimal remain = delta; if (_eingezahltSession > 0m) { var take = Math.Min(_eingezahltSession, remain); _eingezahltSession = Math.Round(_eingezahltSession - take, 2); remain = Math.Round(remain - take, 2); } if (remain > 0m && _personalGuthaben > 0m) { var takeG = Math.Min(_personalGuthaben, remain); _personalGuthaben = Math.Round(_personalGuthaben - takeG, 2); remain = Math.Round(remain - takeG, 2); _consumedGuthabenCoins = Math.Round(_consumedGuthabenCoins + takeG, 2); } if (remain > 0m) AppLogger.Log($"WARN: Delta {remain:0.00} € nicht gedeckt."); _geplanteMuenzAuszahlung = Math.Max(0m, Math.Round(_geplanteMuenzAuszahlung - delta, 2)); decimal restScheine = Math.Max(0m, Math.Round(_geplanteAuszahlung - (_notesDispensedCent / 100m), 2)); decimal restMuenzen = Math.Max(0m, Math.Round(_geplanteMuenzAuszahlung, 2)); decimal restSum = restScheine + restMuenzen; if (_payoutInProgress && lblSummeAuszahlung != null) lblSummeAuszahlung.Text = $"{restSum:C2}"; if (restSum <= 0m) { try { _lastPayoutLevelsScheduleUtc = DateTime.MinValue; } catch { } } else { try { var now = DateTime.UtcNow; if ((now - _lastPayoutLevelsScheduleUtc).TotalMilliseconds >= 1200) { _lastPayoutLevelsScheduleUtc = now; SmartCoinV1.ScheduleLevelsGlobal(); } } catch { } } lblGuthaben.Text = $"Personal-Guthaben: {_personalGuthaben:C2}"; lblEingezahlt.Text = $"Eingezahlt: {_eingezahltSession:C2}"; UpdateAbrechnenSummaries(); UpdateMaxVerfuegbar(); })); } catch { }
                }
                private void SspOnPayoutError(int wert, int neu)
                {
                    if (InvokeRequired) { BeginInvoke((Action)(() => SspOnPayoutError(wert, neu))); return; }
                    _activeNotePayoutDevices = 0; _notesPayoutInProgress = false; UpdateBusyUI(); MessageBox.Show(this, "Fehler bei der Scheinauszahlung.", "Payout-Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                private bool TryBalancedSplit(int need, int avail1, int avail2, out int take1, out int take2)
                {
                    take1 = 0; take2 = 0; if (need <= 0) return true; avail1 = Math.Max(0, avail1); avail2 = Math.Max(0, avail2); if (avail1 + avail2 < need) return false; if (avail1 >= avail2) { take1 = need / 2 + (need % 2); take2 = need - take1; if (take1 > avail1) { int deficit = take1 - avail1; take1 = avail1; take2 += deficit; } if (take2 > avail2) { int deficit = take2 - avail2; take2 = avail2; take1 += deficit; } } else { take2 = need / 2 + (need % 2); take1 = need - take2; if (take2 > avail2) { int deficit = take2 - avail2; take2 = avail2; take1 += deficit; } if (take1 > avail1) { int deficit = take1 - avail1; take1 = avail1; take2 += deficit; } }
                    return take1 >= 0 && take2 >= 0 && take1 <= avail1 && take2 <= avail2 && take1 + take2 == need;
                }

                private void AbrechnungForm_KeyDown(object sender, KeyEventArgs e)
                {
                    try
                    {
                        if (e.Control && e.KeyCode == Keys.M)
                        {
                            if (nudManuell != null && btnManuellAdd != null) { bool vis = !nudManuell.Visible; nudManuell.Visible = vis; btnManuellAdd.Visible = vis; try { UpdateBuchenEnabled(); } catch { } }
                            e.Handled = true;
                        }
                        if (e.Control && e.KeyCode == Keys.A)
                        {
                            if (btnAdmin != null) btnAdmin.Visible = !btnAdmin.Visible; e.Handled = true;
                        }
                    }
                    catch { }
                }
                private bool AnyCoinEinwurf() { try { var s1 = (_coin as SmartCoinV1)?.CurrentStatus; if (!string.IsNullOrEmpty(s1) && s1.ToLowerInvariant().Contains("einwurf")) return true; } catch { } try { var s2 = (_coin2 as SmartCoinV1)?.CurrentStatus; if (!string.IsNullOrEmpty(s2) && s2.ToLowerInvariant().Contains("einwurf")) return true; } catch { } return false; }
                private Kontierung ApplyRuleFallback(string mwstGroup, ShiftDetails d) { return new Kontierung(); }
                private Task CreditLeftoverToPersonalguthabenAsync(int manId, int schichtId) { return Task.CompletedTask; }
                private void SelectNextItemOrClear()
                {
                    try
                    {
                        btnSchichtAuswahl.Visible = _auswahlItems != null && _auswahlItems.Count > 1; if (_auswahlItems == null || _auswahlItems.Count == 0) { _details = null; _currentAuszahlungRow = null; lblBelegInfo.Text = string.Empty; lblB19.Text = "19%: 0,00 €"; lblB7.Text = "7%: 0,00 €"; lblB0.Text = "0%: 0,00 €"; if (lblSumme != null) lblSumme.Text = 0m.ToString("C2"); if (lblNoch != null) lblNoch.Text = 0m.ToString("C2"); try { if (lblNoch != null) lblNoch.ForeColor = Color.Green; } catch { } UpdateBuchenEnabled(); return; }
                        var nextShift = _auswahlItems.Where(a => a.Schicht != null).OrderBy(a => a.Schicht.StartZeit).FirstOrDefault();
                        if (nextShift != null)
                        {
                            LadeSchicht(nextShift.Schicht);
                            return;
                        }
                        DataRow nextPay = null; DateTime? min = null;
                        foreach (var ai in _auswahlItems.Where(a => a.Auszahlung != null))
                        {
                            var row = ai.Auszahlung;
                            DateTime t = (row.Table.Columns.Contains("ErfasstAm") && row["ErfasstAm"] != DBNull.Value) ? Convert.ToDateTime(row["ErfasstAm"]) : DateTime.MinValue;
                            if (!min.HasValue || t < min.Value) { min = t; nextPay = row; }
                        }
                        if (nextPay != null)
                        {
                            // WICHTIG: korrekt laden, damit Typ (Einzahlung/Auszahlung) gesetzt wird
                            LadeAuszahlung(nextPay);
                            return;
                        }
                        lblBelegInfo.Text = string.Empty;
                        UpdateBuchenEnabled();
                    }
                    catch { try { lblBelegInfo.Text = string.Empty; } catch { } }
                }
            }
        }
