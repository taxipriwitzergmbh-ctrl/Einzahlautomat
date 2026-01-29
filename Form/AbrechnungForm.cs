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

namespace TaMi_Einzahlautomat
{
    public partial class AbrechnungForm : Form
    {
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

        private TabControl tabControl;
        private TabPage tabAbrechnen;
        private TabPage tabWechseln;

        private Label lblTitel, lblGuthaben, lblB19, lblB7, lblB0, lblSumme, lblEingezahlt, lblNoch;

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
        private const float BackgroundImageOpacity = 0.15f;
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
            BuildModernLayout();
            LoadBackgroundImage();
            ResumeLayout(true);
            EnableDoubleBufferingRecursive(this);

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
            var img = TaMi_Einzahlautomat.Properties.Resources.Hintergrund;
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
                Text = "Schicht abrechnen",
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
            btnAbmelden.Region = System.Drawing.Region.FromHrgn(CreateRoundRectRgn(0, 0, btnAbmelden.Width, btnAbmelden.Height, 14, 14));
            btnAbmelden.Click += btnAbmelden_Click;
            headerPanel.Controls.Add(btnAbmelden);

            btnAdmin = new Button
            {
                Text = "\u2699",
                Font = new Font("Segoe UI Symbol", 22F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(33, 150, 243),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(56, 56),
                Location = new Point(ClientSize.Width - 220, 2),
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
                Size = new Size(56, 56),
                Location = new Point(ClientSize.Width - 280, 2),
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

            var btnHours = new Button
            {
                Text = string.Empty,
                Font = new Font("Segoe UI Variable", 20F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(255, 143, 0),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(56, 56),
                Location = new Point(ClientSize.Width - 340, 2),
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

            try { Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, Width, Height, 24, 24)); } catch { }

            tabControl = new TabControl
            {
                Location = new Point(40, 80),
                Size = new Size(1200, 900),
                Font = new Font("Segoe UI Variable", 18F, FontStyle.Bold),
                ItemSize = new Size(300, 60),
                Alignment = TabAlignment.Top,
                Appearance = TabAppearance.Normal
            };
            Controls.Add(tabControl);

            tabAbrechnen = new TabPage("Abrechnen") { BackColor = Color.White };
            tabWechseln = new TabPage("Wechseln") { BackColor = Color.White };
            tabControl.TabPages.Add(tabAbrechnen);
            tabControl.TabPages.Add(tabWechseln);

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
                Location = new Point(650, 10),
                TabStop = false,
                Visible = false
            };
            btnSchichtAuswahl.FlatAppearance.BorderSize = 0;
            btnSchichtAuswahl.Click += BtnSchichtAuswahl_Click;
            headerPanel.Controls.Add(btnSchichtAuswahl);

            tabControl.SelectedIndexChanged += (s, e) =>
            {
                if (tabControl.SelectedTab == tabWechseln)
                {
                    SafeRefreshAvailability();
                    UpdateCoinAvailabilityLabels();
                    UpdateMaxVerfuegbar();
                }
            };
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

        private void BuildAbrechnenTab()
        {
            int y = 30;
            lblTitel = new Label
            {
                Text = $"Mitarbeiter: {_personal.Vorname} {_personal.Name}",
                Font = new Font("Segoe UI Semibold", 22F, FontStyle.Bold),
                Location = new Point(40, y),
                Size = new Size(800, 48),
                BackColor = Color.Transparent
            };
            tabAbrechnen.Controls.Add(lblTitel);

            y += 60;
            lblGuthaben = new Label
            {
                Text = "Personal-Guthaben: 0,00 € (Platzhalter)",
                Font = new Font("Segoe UI Variable", 20F),
                Location = new Point(40, y),
                Size = new Size(800, 44),
                BackColor = Color.Transparent
            };
            tabAbrechnen.Controls.Add(lblGuthaben);

            y += 60; // inkl. Leerzeile

            lblBelegInfo = new Label
            {
                Text = string.Empty,
                Font = new Font("Segoe UI Variable", 26F, FontStyle.Bold),
                Location = new Point(40, y),
                Size = new Size(1100, 70),
                BackColor = Color.Transparent,
                ForeColor = Color.FromArgb(90, 180, 255),
                AutoSize = false,
                Padding = new Padding(4),
                TextAlign = ContentAlignment.MiddleLeft
            };
            tabAbrechnen.Controls.Add(lblBelegInfo);
            y += 80;

            y += 60;
            var b19Init = _details != null ? _details.Betrag19 : 0m;
            var b7Init = _details != null ? _details.Betrag7 : 0m;
            var b0Init = _details != null ? _details.Betrag0 : 0m;
            lblB19 = new Label { Text = $"19%: {b19Init:C2}", Font = new Font("Segoe UI Variable", 20F), Location = new Point(40, y), Size = new Size(250, 44) };
            tabAbrechnen.Controls.Add(lblB19);
            lblB7 = new Label { Text = $"7%: {b7Init:C2}", Font = new Font("Segoe UI Variable", 20F), Location = new Point(320, y), Size = new Size(250, 44) };
            tabAbrechnen.Controls.Add(lblB7);
            lblB0 = new Label { Text = $"0%: {b0Init:C2}", Font = new Font("Segoe UI Variable", 20F), Location = new Point(600, y), Size = new Size(250, 44) };
            tabAbrechnen.Controls.Add(lblB0);

            y += 60;
            var sumInit = (_details != null ? _details.SummeZuZahlen : 0m);
            lblSumme = new Label { Text = $"Summe: {sumInit:C2}", Font = new Font("Segoe UI Variable", 22F, FontStyle.Bold), Location = new Point(40, y), Size = new Size(400, 48) };
            tabAbrechnen.Controls.Add(lblSumme);

            y += 86;
            lblEingezahlt = new Label { Text = $"Eingezahlt: {_eingezahltSession:C2}", Font = new Font("Segoe UI Variable", 22F, FontStyle.Bold), Location = new Point(40, y), Size = new Size(400, 48) };
            tabAbrechnen.Controls.Add(lblEingezahlt);
            var nochInit = Math.Max(0m, sumInit - _eingezahltSession);
            nochInit = Math.Max(0m, nochInit - _personalGuthaben);
            lblNoch = new Label { Text = $"Noch zu zahlen: {nochInit:C2}", Font = new Font("Segoe UI Variable", 22F, FontStyle.Bold), Location = new Point(500, y), Size = new Size(400, 48) };
            tabAbrechnen.Controls.Add(lblNoch);
            try { lblNoch.ForeColor = (nochInit > 0m) ? Color.Red : Color.Green; } catch { }

            y += 80;
            btnAbrechnen = new Button
            {
                Text = "Buchen",
                Location = new Point(40, y),
                Size = new Size(220, 60),
                Font = new Font("Segoe UI Variable", 22F, FontStyle.Bold),
                BackColor = Color.FromArgb(46, 125, 50),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnAbrechnen.FlatAppearance.BorderSize = 0;
            btnAbrechnen.Click += btnAbrechnen_Click;
            tabAbrechnen.Controls.Add(btnAbrechnen);

            btnCreatePayment = new Button
            {
                Text = "Zahlung anlegen",
                Location = new Point(280, y),
                Size = new Size(260, 60),
                Font = new Font("Segoe UI Variable", 22F, FontStyle.Bold),
                BackColor = Color.FromArgb(33, 150, 243),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Visible = PaymentSettingsStore.IsEnabled()
            };
            btnCreatePayment.FlatAppearance.BorderSize = 0;
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
            tabAbrechnen.Controls.Add(btnCreatePayment);

            // y unverändert lassen, keine zusätzlichen Buttons hier

            y += 80;
            nudManuell = new NumericUpDown { Location = new Point(40, y), Size = new Size(200, 44), DecimalPlaces = 2, Minimum = -10000, Maximum = 10000, Increment = 5, Font = new Font("Segoe UI Variable", 20F), Visible = false };
            tabAbrechnen.Controls.Add(nudManuell);
            btnManuellAdd = new Button { Text = "Manuell hinzufügen", Location = new Point(260, y), Size = new Size(280, 48), Font = new Font("Segoe UI Variable", 20F, FontStyle.Bold), BackColor = Color.FromArgb(33, 150, 243), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Visible = false };
            btnManuellAdd.FlatAppearance.BorderSize = 0;
            btnManuellAdd.Click += btnManuellAdd_Click;
            tabAbrechnen.Controls.Add(btnManuellAdd);

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
                if (btnAuszahlen != null) btnAuszahlen.Enabled = !userLocked;
                if (btnAbmelden != null)
                {
                    bool newEnabled = !userLocked;
                    bool prevEnabled = _lastAbmeldenEnabled;
                    btnAbmelden.Enabled = newEnabled;
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
                if (userLocked) { btnAuszahlen.Enabled = false; return; }
                decimal sumNotes = 0m; for (int i = 0; i < scheinWerte.Length; i++) sumNotes += scheinWerte[i] * auswahlAnzahl[i];
                decimal sumCoins = 0m; for (int i = 0; i < muenzWerte.Length; i++) sumCoins += (muenzWerte[i] * auswahlAnzahlMuenzen[i]) / 100m;
                decimal sum = sumNotes + sumCoins;
                decimal maxVerfuegbar = _eingezahltSession + _personalGuthaben;
                btnAuszahlen.Enabled = (sum > 0m) && (sum <= maxVerfuegbar);
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
                if (manualMode) { if (btnAbrechnen != null) btnAbrechnen.Enabled = enabled; return; }
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
                if (btnAbrechnen != null) btnAbrechnen.Enabled = enabled;
            }
            catch { }
        }

        private void BuildWechselnTab()
        {
            int startY = 30; int rowH = 70;
            int startXMuenzImage = 40; int startXMuenzMinus = 180; int startXMuenzCount = 240; int startXMuenzPlus = 320; int startXMuenzAvail = 400;
            Color primary = Color.FromArgb(33, 150, 243); Color danger = Color.FromArgb(229, 57, 53);
            for (int i = 0; i < muenzWerte.Length; i++)
            {
                var rowY = startY + i * rowH;
                var pb = new PictureBox { Location = new Point(startXMuenzImage, rowY), Size = new Size(60, 60), SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.None, BackColor = Color.White };
                try { pb.Image = GetCoinImageByIndex(i); } catch { }
                tabWechseln.Controls.Add(pb); picMuenzen[i] = pb;
                var bMinus = new Button { Text = "–", Location = new Point(startXMuenzMinus, rowY), Size = new Size(48, 48), Tag = i, FlatStyle = FlatStyle.Flat, BackColor = danger, ForeColor = Color.White, Font = new Font("Segoe UI Variable", 24F, FontStyle.Bold) };
                bMinus.FlatAppearance.BorderSize = 0; bMinus.Click += BtnMinusMuenzen_Click; tabWechseln.Controls.Add(bMinus); btnMinusMuenzen[i] = bMinus;
                var lbl = new Label { Text = "0", TextAlign = ContentAlignment.MiddleCenter, Location = new Point(startXMuenzCount, rowY), Size = new Size(60, 48), BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White, Font = new Font("Segoe UI Variable", 22F, FontStyle.Bold) };
                tabWechseln.Controls.Add(lbl); lblAnzahlMuenzen[i] = lbl;
                var bPlus = new Button { Text = "+", Location = new Point(startXMuenzPlus, rowY), Size = new Size(48, 48), Tag = i, FlatStyle = FlatStyle.Flat, BackColor = primary, ForeColor = Color.White, Font = new Font("Segoe UI Variable", 24F, FontStyle.Bold) };
                bPlus.FlatAppearance.BorderSize = 0; bPlus.Click += BtnPlusMuenzen_Click; tabWechseln.Controls.Add(bPlus); btnPlusMuenzen[i] = bPlus;
                var lAvail = new Label { Text = "vorrätig: 0", AutoSize = true, Location = new Point(startXMuenzAvail, rowY + 12), ForeColor = Color.DimGray, Font = new Font("Segoe UI Variable", 18F) };
                tabWechseln.Controls.Add(lAvail); lblVerfuegbarMuenzen[i] = lAvail;
            }

            int startXScheinImage = 600; int startXScheinMinus = 740; int startXScheinCount = 800; int startXScheinPlus = 880; int startXScheinAvail = 960;
            for (int i = 0; i < scheinWerte.Length; i++)
            {
                var rowY = startY + i * rowH;

                var pb = new PictureBox
                {
                    Location = new Point(startXScheinImage, rowY),
                    Size = new Size(120, 48),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BorderStyle = BorderStyle.None,
                    BackColor = Color.White
                };
                try { pb.Image = GetNoteImageByIndex(i); } catch { }
                tabWechseln.Controls.Add(pb); picScheine[i] = pb;
                var bMinus = new Button { Text = "–", Location = new Point(startXScheinMinus, rowY), Size = new Size(48, 48), Tag = i, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(229, 57, 53), ForeColor = Color.White, Font = new Font("Segoe UI Variable", 24F, FontStyle.Bold) };
                bMinus.FlatAppearance.BorderSize = 0; bMinus.Click += BtnMinus_Click; tabWechseln.Controls.Add(bMinus); btnMinus[i] = bMinus;
                var lbl = new Label { Text = "0", TextAlign = ContentAlignment.MiddleCenter, Location = new Point(startXScheinCount, rowY), Size = new Size(60, 48), BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White, Font = new Font("Segoe UI Variable", 22F, FontStyle.Bold) };
                tabWechseln.Controls.Add(lbl); lblAnzahl[i] = lbl;
                var bPlus = new Button { Text = "+", Location = new Point(startXScheinPlus, rowY), Size = new Size(48, 48), Tag = i, FlatStyle = FlatStyle.Flat, BackColor = primary, ForeColor = Color.White, Font = new Font("Segoe UI Variable", 24F, FontStyle.Bold) };
                bPlus.FlatAppearance.BorderSize = 0; bPlus.Click += BtnPlus_Click; tabWechseln.Controls.Add(bPlus); btnPlus[i] = bPlus;
                var lAvail = new Label { Text = "vorrätig: 0", AutoSize = true, Location = new Point(startXScheinAvail, rowY + 12), ForeColor = Color.DimGray, Font = new Font("Segoe UI Variable", 18F) };
                tabWechseln.Controls.Add(lAvail); lblVerfuegbar[i] = lAvail;
                if (scheinWerte[i] >= 100) { pb.Visible = false; bMinus.Visible = false; lbl.Visible = false; bPlus.Visible = false; lAvail.Visible = false; }
            }
            int labelX = 600; int labelWidth = 350; int maxVerfuegbarY = startY + Math.Max(muenzWerte.Length, scheinWerte.Length) * rowH + 10; int maxAvailX = labelX - 180;
            lblMaxVerfuegbar = new Label { Text = $"Maximal verfügbar: {(_personalGuthaben + _eingezahltSession):C2}", Font = new Font("Segoe UI Variable", 20F, FontStyle.Bold), ForeColor = Color.FromArgb(33, 150, 243), Location = new Point(maxAvailX, maxVerfuegbarY), Size = new Size(labelWidth + 180, 40), TextAlign = ContentAlignment.MiddleRight, BackColor = Color.Transparent };
            tabWechseln.Controls.Add(lblMaxVerfuegbar);
            lblSummeAuszahlung = new Label { AutoSize = false, Font = new Font("Segoe UI", 24F, FontStyle.Bold), Text = "Summe: 0,00 €", TextAlign = ContentAlignment.MiddleRight, Location = new Point(labelX, maxVerfuegbarY + 44), Size = new Size(labelWidth, 48) };
            tabWechseln.Controls.Add(lblSummeAuszahlung);
            btnAuszahlen = new Button { Text = "Auszahlen", Location = new Point(labelX + labelWidth + 30, maxVerfuegbarY + 40), Size = new Size(180, 60), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(46, 125, 50), ForeColor = Color.White, Font = new Font("Segoe UI Variable", 22F, FontStyle.Bold) };
            btnAuszahlen.FlatAppearance.BorderSize = 0; btnAuszahlen.Click += btnAuszahlen_Click; tabWechseln.Controls.Add(btnAuszahlen);
            SafeRefreshAvailability(); UpdateSummeAuszahlung(); UpdateMaxVerfuegbar();
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
                    lblB19.Text = $"19%: {raw19:C2}"; lblB7.Text = $"7%: {raw7:C2}"; lblB0.Text = $"0%: {raw0:C2}"; lblSumme.Text = $"Summe: {summe:C2}"; lblEingezahlt.Text = $"Eingezahlt: {_eingezahltSession:C2}";
                    var rest = Math.Max(0m, summe - _eingezahltSession); var noch = Math.Max(0m, rest - _personalGuthaben);
                    lblNoch.Text = $"Noch zu zahlen: {noch:C2}"; try { lblNoch.ForeColor = (noch > 0m) ? Color.Red : Color.Green; } catch { }
                }
                else
                {
                    decimal summe = -raw19 - raw7 - raw0;
                    lblB19.Text = $"19%: {-raw19:C2}"; lblB7.Text = $"7%: {-raw7:C2}"; lblB0.Text = $"0%: {-raw0:C2}"; lblSumme.Text = $"Summe: {summe:C2}"; lblEingezahlt.Text = $"Eingezahlt: {_eingezahltSession:C2}"; lblNoch.Text = "Noch zu zahlen: 0,00 €"; try { lblNoch.ForeColor = Color.Green; } catch { }
                }
                // Wichtig: nicht mit Standard-Nullwerten überschreiben, wenn eine Auszahlung geladen ist
                UpdateBuchenEnabled();
                return;
            }
            if (_details == null)
            {
                lblB19.Text = "19%: 0,00 €"; lblB7.Text = "7%: 0,00 €"; lblB0.Text = "0%: 0,00 €"; lblSumme.Text = "Summe: 0,00 €"; lblEingezahlt.Text = $"Eingezahlt: {_eingezahltSession:C2}"; lblNoch.Text = "Noch zu zahlen: 0,00 €"; UpdateBuchenEnabled(); return;
            }
            lblEingezahlt.Text = $"Eingezahlt: {_eingezahltSession:C2}"; var restStd = Math.Max(0, _details.SummeZuZahlen - _eingezahltSession); var nochStd = Math.Max(0, restStd - _personalGuthaben); lblNoch.Text = $"Noch zu zahlen: {nochStd:C2}"; try { lblNoch.ForeColor = (nochStd > 0m) ? Color.Red : Color.Green; } catch { }
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
                    new ReceiptPrinter(cfg).PrintSimpleReceipt(title, body);
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
                    Width = 560,
                    Height = 260,
                    BackColor = Color.White
                };

                try { dlg.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, dlg.Width, dlg.Height, 16, 16)); } catch { }

                var header = new Panel { Dock = DockStyle.Top, Height = 64 };
                header.Paint += (s, e) =>
                {
                    using (var brush = new LinearGradientBrush(header.ClientRectangle, Color.FromArgb(33, 150, 243), Color.FromArgb(33, 203, 243), 0f))
                    { e.Graphics.FillRectangle(brush, header.ClientRectangle); }
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

                var lbl = new Label
                {
                    Text = "Möchten Sie eine Quittung, wenn ja wie?",
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Dock = DockStyle.Top,
                    Height = 64,
                    Font = new Font("Segoe UI Variable", 16F)
                };
                body.Controls.Add(lbl);

                // push the label two line-heights down
                try
                {
                    int lineH = TextRenderer.MeasureText("A", lbl.Font).Height;
                    body.Padding = new Padding(0, lineH * 2, 0, 0);
                }
                catch { }

                var panelButtons = new Panel { Dock = DockStyle.Bottom, Height = 92, BackColor = Color.White };
                body.Controls.Add(panelButtons);

                Func<string, Button> makeBtn = (text) =>
                {
                    var b = new Button
                    {
                        Text = text,
                        Width = 120,
                        Height = 48,
                        FlatStyle = FlatStyle.Flat,
                        BackColor = Color.FromArgb(33, 150, 243),
                        ForeColor = Color.White,
                        Font = new Font("Segoe UI Variable", 14F, FontStyle.Bold),
                        TabStop = false
                    };
                    b.FlatAppearance.BorderSize = 0;
                    try { b.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, b.Width, b.Height, 12, 12)); } catch { }
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

                // Layout
                int spacing = 16;
                int totalWidth = btnMail.Width + btnPrint.Width + btnQr.Width + btnNo.Width + spacing * 3;
                int startX = (dlg.ClientSize.Width - totalWidth) / 2;
                int y = 20;
                btnMail.Location = new Point(startX, y);
                btnPrint.Location = new Point(btnMail.Right + spacing, y);
                btnQr.Location = new Point(btnPrint.Right + spacing, y);
                btnNo.Location = new Point(btnQr.Right + spacing, y);
                panelButtons.Controls.AddRange(new Control[] { btnMail, btnPrint, btnQr, btnNo });


                // Shadow (optional, no-op if not supported)
                try
                {
                    dlg.Padding = new Padding(1);
                    dlg.Paint += (s, e) =>
                    {
                        var rect = dlg.ClientRectangle;
                        using (var pen = new Pen(Color.FromArgb(220, 220, 220)))
                        {
                            e.Graphics.DrawRectangle(pen, new Rectangle(0, 0, rect.Width - 1, rect.Height - 1));
                        }
                    };
                }
                catch { }

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
            using (var brush = new LinearGradientBrush(headerPanel.ClientRectangle, Color.FromArgb(33, 150, 243), Color.FromArgb(33, 203, 243), 0f)) e.Graphics.FillRectangle(brush, headerPanel.ClientRectangle);
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
        }

        private void AttachCoinEvents()
        {
            if (_coinEventsAttached) return;
            if (_coin is SmartCoinV1 sc1)
            {
                sc1.CoinLevelsUpdated += OnCoinLevelsUpdated; sc1.CoinAccepted += CoinOnAccepted; sc1.EventLog += CoinOnLog; sc1.CoinDispensedDeltaCent += OnCoinDispensedDelta; sc1.CoinDispenseComplete += OnCoinDispenseComplete; try { sc1.CoinPayoutError += OnCoinPayoutError; } catch { }
                _coinEventsAttached = true; try { sc1.RequestCoinLevels(); } catch { }
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
            try { BeginInvoke((Action)(() => { AddEingezahlt(cent / 100m); })); } catch { }
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
            try { BeginInvoke((Action)(() => { AddEingezahlt(cent / 100m); try { SmartCoinV1.ScheduleLevelsGlobal(); } catch { } })); } catch { }
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
                                lblEingezahlt.Text = $"Eingezahlt: {_eingezahltSession:C2}";
                            }
                            else
                            {
                                decimal gut = -(-Convert.ToDecimal(_currentAuszahlungRow["Betrag19"]) + -Convert.ToDecimal(_currentAuszahlungRow["Betrag7"]) + -Convert.ToDecimal(_currentAuszahlungRow["Betrag0"])); _eingezahltSession += gut; lblEingezahlt.Text = $"Eingezahlt: {_eingezahltSession:C2}";
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
                if (_eingezahltSession > 0m)
                {
                    int manId = _details?.ManId ?? (_currentAuszahlungRow != null && _currentAuszahlungRow.Table.Columns.Contains("FirmenID") && _currentAuszahlungRow["FirmenID"] != DBNull.Value ? Convert.ToInt32(_currentAuszahlungRow["FirmenID"]) : 0);
                    int schichtId = _details?.SchichtId ?? 0;
                    using (var db = new DatabaseHelper())
                    {
                        decimal alterSaldo = await db.GetLastPersonalGuthabenSaldoAsync(_personal.PID); decimal betrag = Math.Round(_eingezahltSession, 2); decimal neuerSaldo = alterSaldo + betrag; var zpg = AccountingRules.GetPersonalguthabenKontierung(manId);
                        var entry = new KassenbuchEntry { PersId = _personal.PID, SchichtId = schichtId, Typ = "Personalguthaben", Buchungstext = AccountingRules.ComposePgEinbuchungText(_personal), Kost1 = zpg.Kost1, Kost2 = zpg.Kost2, Konto = zpg.Konto, Betrag19 = 0m, Betrag7 = 0m, Betrag0 = betrag, SaldoPersonalguthaben = neuerSaldo, FirmenId = -1, AutomatenName = AppSettings.AutomatenName, Kassenbestand = GetCurrentKassenbestandEuro() };
                        await db.InsertKassenbuchAsync(entry); _personalGuthaben = neuerSaldo; _eingezahltSession = 0m; lblGuthaben.Text = $"Personal-Guthaben: {_personalGuthaben:C2}"; lblEingezahlt.Text = $"Eingezahlt: {_eingezahltSession:C2}"; UpdateMaxVerfuegbar();
                    }
                }
                else if (_eingezahltSession < 0m)
                {
                    // Negative manuelle Eingabe: Personalguthaben bei Abmeldung reduzieren
                    int manId = _details?.ManId ?? (_currentAuszahlungRow != null && _currentAuszahlungRow.Table.Columns.Contains("FirmenID") && _currentAuszahlungRow["FirmenID"] != DBNull.Value ? Convert.ToInt32(_currentAuszahlungRow["FirmenID"]) : 0);
                    int schichtId = _details?.SchichtId ?? 0;
                    using (var db = new DatabaseHelper())
                    {
                        decimal alterSaldo = await db.GetLastPersonalGuthabenSaldoAsync(_personal.PID);
                        decimal betrag = Math.Abs(Math.Round(_eingezahltSession, 2)); // zu reduzierender Betrag
                        decimal neuerSaldo = Math.Round(alterSaldo - betrag, 2);
                        var zpg = AccountingRules.GetPersonalguthabenKontierung(manId);
                        var entry = new KassenbuchEntry
                        {
                            PersId = _personal.PID,
                            SchichtId = schichtId,
                            Typ = "Personalguthaben",
                            Buchungstext = AccountingRules.ComposePgEinzahlungText(_personal),
                            Kost1 = zpg.Kost1,
                            Kost2 = zpg.Kost2,
                            Konto = zpg.Konto,
                            Betrag19 = 0m,
                            Betrag7 = 0m,
                            Betrag0 = -betrag, // Abzug
                            SaldoPersonalguthaben = neuerSaldo,
                            FirmenId = -1,
                            AutomatenName = AppSettings.AutomatenName,
                            Kassenbestand = GetCurrentKassenbestandEuro()
                        };
                        await db.InsertKassenbuchAsync(entry);
                        _personalGuthaben = neuerSaldo;
                        _eingezahltSession = 0m;
                        lblGuthaben.Text = $"Personal-Guthaben: {_personalGuthaben:C2}";
                        lblEingezahlt.Text = $"Eingezahlt: {_eingezahltSession:C2}";
                        UpdateMaxVerfuegbar();
                    }
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
                        if (lblSummeAuszahlung != null) lblSummeAuszahlung.Text = $"Summe: {(_geplanteAuszahlung + sumCoins):C2}"; return;
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
            int[] avail = GetCurrentAvailability(); for (int i = 0; i < 7; i++) if (lblVerfuegbar[i] != null) lblVerfuegbar[i].Text = $"vorrätig: {avail[i]}";
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
            decimal summe = sumScheine + sumMuenzen; if (lblSummeAuszahlung != null) lblSummeAuszahlung.Text = $"Summe: {summe:C2}"; UpdateAuszahlenEnabled();
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
                decimal restScheine = Math.Max(0m, Math.Round(_geplanteAuszahlung - (_notesDispensedCent / 100m), 2)); decimal restMuenzen = Math.Max(0m, Math.Round(_geplanteMuenzAuszahlung, 2)); decimal restSum = restScheine + restMuenzen; if (_payoutInProgress && lblSummeAuszahlung != null) lblSummeAuszahlung.Text = $"Summe: {restSum:C2}";
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
            if (!_payoutInProgress && lblSummeAuszahlung != null) lblSummeAuszahlung.Text = "Summe: 0,00 €"; _geplanteAuszahlung = 0m; _notesDispensedCent = 0;
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
            if (!_payoutInProgress && lblSummeAuszahlung != null) lblSummeAuszahlung.Text = "Summe: 0,00 €"; AppLogger.Log("Münzauszahlung abgeschlossen");
        }

        private int GetAvailByIndex(int idx) { var avail = GetCurrentAvailability(); return (idx >= 0 && idx < avail.Length) ? avail[idx] : 0; }

        private async void BtnSchichtAuswahl_Click(object sender, EventArgs e)
        {
            await LadeAlleAbrechenbarenItemsAsync(); if (_auswahlItems.Count <= 1) return; using (var dlg = new AuswahlDialog(_auswahlItems)) { if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedItem != null) { if (dlg.SelectedItem.Schicht != null) LadeSchicht(dlg.SelectedItem.Schicht); else if (dlg.SelectedItem.Auszahlung != null) LadeAuszahlung(dlg.SelectedItem.Auszahlung); } }
        }

        private async void LadeSchicht(ShiftDetails details)
        {
            _details = details; lblTitel.Text = $"Mitarbeiter: {_personal.Vorname} {_personal.Name}"; lblB19.Text = $"19%: {_details.Betrag19:C2}"; lblB7.Text = $"7%: {_details.Betrag7:C2}"; lblB0.Text = $"0%: {_details.Betrag0:C2}"; lblSumme.Text = $"Summe: {_details.SummeZuZahlen:C2}"; lblEingezahlt.Text = $"Eingezahlt: {_eingezahltSession:C2}";
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
                lblB19.Text = $"19%: {raw19:C2}"; lblB7.Text = $"7%: {raw7:C2}"; lblB0.Text = $"0%: {raw0:C2}"; lblSumme.Text = $"Summe: {summe:C2}"; lblEingezahlt.Text = $"Eingezahlt: {_eingezahltSession:C2}";
                var rest = Math.Max(0m, summe - _eingezahltSession); var noch = Math.Max(0m, rest - _personalGuthaben);
                lblNoch.Text = $"Noch zu zahlen: {noch:C2}"; try { lblNoch.ForeColor = (noch > 0m) ? Color.Red : Color.Green; } catch { }
                UpdateBuchenEnabled();
            }
            else
            {
                decimal summe = -raw19 - raw7 - raw0;
                lblB19.Text = $"19%: {-raw19:C2}"; lblB7.Text = $"7%: {-raw7:C2}"; lblB0.Text = $"0%: {-raw0:C2}"; lblSumme.Text = $"Summe: {summe:C2}"; lblEingezahlt.Text = $"Eingezahlt: {_eingezahltSession:C2}"; lblNoch.Text = "Noch zu zahlen: 0,00 €"; try { lblNoch.ForeColor = Color.Green; } catch { }
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

                private void UpdateMaxVerfuegbar() { if (lblMaxVerfuegbar != null) lblMaxVerfuegbar.Text = $"Maximal verfügbar: {(_personalGuthaben + _eingezahltSession):C2}"; }
                private void UpdateCoinAvailabilityLabels() { for (int i = 0; i < 8; i++) { if (lblVerfuegbarMuenzen[i] == null) continue; int a = _coinAvail[i] >= 0 ? _coinAvail[i] : 0; int b = _coin2Avail[i] >= 0 ? _coin2Avail[i] : 0; bool known = false; int total = 0; if (a > 0) { total += a; known = true; } if (b > 0) { total += b; known = true; } lblVerfuegbarMuenzen[i].Text = known ? $"vorrätig: {total}" : "vorrätig: ?"; } }
                private int GetCombinedCoinAvail(int idx) { try { int a = (idx >= 0 && idx < _coinAvail.Length) ? _coinAvail[idx] : -1; int b = (idx >= 0 && idx < _coin2Avail.Length) ? _coin2Avail[idx] : -1; if (a < 0 && b < 0) return -1; int sum = 0; if (a > 0) sum += a; if (b > 0) sum += b; return sum; } catch { return -1; } }
                private void OnCoinDispensedDelta(int cent)
                {
                    if (AppLogger.KassensturzActive) return; if (cent <= 0) return; try { BeginInvoke((Action)(() => { _coinsDispensedCentTotal += cent; decimal delta = Math.Round(cent / 100m, 2); if (delta <= 0m) return; decimal remain = delta; if (_eingezahltSession > 0m) { var take = Math.Min(_eingezahltSession, remain); _eingezahltSession = Math.Round(_eingezahltSession - take, 2); remain = Math.Round(remain - take, 2); } if (remain > 0m && _personalGuthaben > 0m) { var takeG = Math.Min(_personalGuthaben, remain); _personalGuthaben = Math.Round(_personalGuthaben - takeG, 2); remain = Math.Round(remain - takeG, 2); _consumedGuthabenCoins = Math.Round(_consumedGuthabenCoins + takeG, 2); } if (remain > 0m) AppLogger.Log($"WARN: Delta {remain:0.00} € nicht gedeckt."); _geplanteMuenzAuszahlung = Math.Max(0m, Math.Round(_geplanteMuenzAuszahlung - delta, 2)); try { SmartCoinV1.ScheduleLevelsGlobal(); } catch { } decimal restScheine = Math.Max(0m, Math.Round(_geplanteAuszahlung - (_notesDispensedCent / 100m), 2)); decimal restMuenzen = Math.Max(0m, Math.Round(_geplanteMuenzAuszahlung, 2)); decimal restSum = restScheine + restMuenzen; if (_payoutInProgress && lblSummeAuszahlung != null) lblSummeAuszahlung.Text = $"Summe: {restSum:C2}"; lblGuthaben.Text = $"Personal-Guthaben: {_personalGuthaben:C2}"; lblEingezahlt.Text = $"Eingezahlt: {_eingezahltSession:C2}"; UpdateAbrechnenSummaries(); UpdateMaxVerfuegbar(); })); } catch { }
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
                        btnSchichtAuswahl.Visible = _auswahlItems != null && _auswahlItems.Count > 1; if (_auswahlItems == null || _auswahlItems.Count == 0) { _details = null; _currentAuszahlungRow = null; lblBelegInfo.Text = string.Empty; lblB19.Text = "19%: 0,00 €"; lblB7.Text = "7%: 0,00 €"; lblB0.Text = "0%: 0,00 €"; lblSumme.Text = "Summe: 0,00 €"; lblNoch.Text = "Noch zu zahlen: 0,00 €"; UpdateBuchenEnabled(); return; }
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
