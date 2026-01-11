using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Drawing.Drawing2D;
using Geldautomat;
using System.IO.Ports; // Ports
using System.Linq; // Linq f�r Except/Where
using System.Globalization; // NEU f�r Summenformat

namespace Geldautomat
{
    public partial class AdminNV200_2Form : Form
    {
        private NV200_SSP _ssp;
        private Panel headerPanel;
        private Button btnClose;
        private Button btnMinimize;
        private Label lblTitle;
        private Point _mouseDownLocation;

        private Panel _panelBestand;
        private Label[] _lblBestandAnz = new Label[7];
        private Label _lblBestandSum; // payout sum label
        private Label[] _lblCashboxAnz = new Label[7];
        private Label _lblCashboxSumNew; // cashbox sum label modern
        private Label _lblGesamtSum; // total
        private Button _btnBestandRefresh;
        private Timer _tmrBestand;

        private TextBox txtLog;
        // Verbindung
        private ComboBox txtComPort; // dropdown
        private Button btnVerbinden;
        private TextBox txtSendHex;
        private Button btnSenden;
        private CheckBox chkAutoSenden;
        private Timer tmrAutoSend;
        private Button btnComPortSpeichern;
        private Button _btnDetectPort;
        private string[] _lastPorts;
        private bool _detectingPort;
        private string[] _detectBasePorts;
        private Form _detectDialog;
        private Timer _detectTimer;
        private string iniPath = @"C:\ProgramData\SuE-Software\SuE-TaMi Client SQL\Geldautomat.ini";

        private Button btnEnableToggle;
        private bool _enabledRequested = false;

        // Statusanzeige / Enable-Toggle
        private Panel _panelStatus;
        private Label _lblConn;
        private Label _lblPort;
        private Label _lblLastEvt;
        private Label _lblState;
        private Timer _tmrStatus;
        private DateTime _lastEvtUtc = System.DateTime.MinValue;

        // Events toggeln
        private bool _eventsAttached = false;

        // Routing Checkbox (Rout_X in INI)
        private CheckBox _chkRouteAllPayout;
        

        // Payout St�ckelung Panel (wie NV200/1 � Zielbest�nde als NumericUpDown)
        private Button btnPayoutStueckelung;
        private Panel _panelMaxConfig;
        private NumericUpDown[] _nudMax = new NumericUpDown[7];
        private Button _btnMaxLoad;
        private Button _btnMaxSave;
        private Button _btnMaxClose;
        private Label _lblMaxInfo;

        // NEU: Deaktivieren-Checkbox + globaler Status
        private CheckBox _chkDisabled;
        public static bool DeviceDisabled = false; // true = keine Verbindungs-/Eventversuche

        public AdminNV200_2Form(NV200_SSP ssp)
        {
            _ssp = ssp; // kann null sein, dann nur UI
            // load persisted disabled flag
            try
            {
                var dv = IniHelper.ReadValue("NV200/2", "Disabled", iniPath);
                if (!string.IsNullOrWhiteSpace(dv)) DeviceDisabled = dv.Trim().Equals("1", StringComparison.OrdinalIgnoreCase) || dv.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
            }
            catch { }
            InitializeModernAdmin();
        }

        private void InitializeModernAdmin()
        {
            // Fenster-Setup
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1280, 1024);
            BackColor = Color.White;
            DoubleBuffered = true;

            // Farbverlauf-Header
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

            // Titel
            lblTitle = new Label
            {
                Text = "Admin - NV200/2 Steuerung",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Variable", 18F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(24, 0),
                Size = new Size(400, 60),
                BackColor = Color.Transparent
            };
            headerPanel.Controls.Add(lblTitle);

            // Schlie�en-Button
            btnClose = new Button
            {
                Text = "\u2715", // Unicode-Escape f�r X
                Font = new Font("Segoe UI Symbol", 18F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(48, 48),
                Location = new Point(ClientSize.Width - 56, 6),
                TabStop = false
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 80, 80);
            btnClose.Click += (s, e) => Close();
            headerPanel.Controls.Add(btnClose);

            // Minimieren-Button
            btnMinimize = new Button
            {
                Text = "�",
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(48, 48),
                Location = new Point(ClientSize.Width - 112, 6),
                TabStop = false
            };
            btnMinimize.FlatAppearance.BorderSize = 0;
            btnMinimize.FlatAppearance.MouseOverBackColor = Color.FromArgb(33, 150, 243, 80);
            btnMinimize.Click += (s, e) => WindowState = FormWindowState.Minimized;
            headerPanel.Controls.Add(btnMinimize);

            // Abgerundete Ecken
            try { Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, Width, Height, 32, 32)); } catch { }

            // Restliches Layout
            BuildModernLayout();

            // Status-Timer
            _tmrStatus = new Timer { Interval = 1000 };
            _tmrStatus.Tick += (s, e) => UpdateStatusUi();
            _tmrStatus.Enabled = true;

            // Bestand-Timer
            StartBestandTimer(true);

            // Events der globalen NV200/2-Session anh�ngen
            AttachSspEvents();

            // Anfangswerte setzen (FIX: Ports jetzt initial laden und selektieren)
            try
            {
                LoadComPorts();
                var initialPort = _ssp?.ComPort ?? IniHelper.ReadValue("NV200/2", "ComPort", iniPath);
                EnsureSelectedPort(initialPort, initial: true);
            }
            catch { }
            UpdateEnableButtonVisual();
            UpdateStatusUi();
            RefreshBestand();

            // Routing-Checkbox initial deaktivieren (dynamisches Routing)
            try { DisableRouteCheckboxUi(); } catch { }

            ApplyDisabledState();
        }

        private void HeaderPanel_Paint(object sender, PaintEventArgs e)
        {
            using (var brush = new LinearGradientBrush(headerPanel.ClientRectangle,
                Color.FromArgb(33, 150, 243), Color.FromArgb(33, 203, 243), 0f))
            {
                e.Graphics.FillRectangle(brush, headerPanel.ClientRectangle);
            }
        }

        private void HeaderPanel_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                _mouseDownLocation = e.Location;
        }

        private void HeaderPanel_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Left += e.X - _mouseDownLocation.X;
                Top += e.Y - _mouseDownLocation.Y;
            }
        }

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

        private void BuildModernLayout()
        {
            int yStart = 70; // Abstand unterhalb des Headers

            // --- Verbindungs-/Events-Bereich ---
            var lblComPort = new Label
            {
                Text = "COM-Port:",
                Location = new Point(24, yStart),
                Size = new Size(80, 20)
            };
            Controls.Add(lblComPort);

            txtComPort = new ComboBox
            {
                Location = new Point(110, yStart - 4),
                Size = new Size(110, 26),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            txtComPort.DropDown += (s,e)=> LoadComPorts(true);
            Controls.Add(txtComPort);

            _btnDetectPort = new Button
            {
                Text = "Erkennung",
                Location = new Point(225, yStart - 4),
                Size = new Size(90, 24),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(245,247,250)
            };
            _btnDetectPort.FlatAppearance.BorderSize = 0;
            _btnDetectPort.Click += (s,e)=> StartDetectPortMode();
            Controls.Add(_btnDetectPort);

            btnVerbinden = new Button
            {
                Location = new Point(320, yStart - 4),
                Size = new Size(140, 24),
                Text = "Events anh�ngen"
            };
            btnVerbinden.Click += btnVerbinden_Click;
            Controls.Add(btnVerbinden);

            btnComPortSpeichern = new Button
            {
                Location = new Point(465, yStart - 4),
                Size = new Size(100, 24),
                Text = "Speichern"
            };
            btnComPortSpeichern.Click += BtnComPortSpeichern_Click;
            Controls.Add(btnComPortSpeichern);

            // Enable/Sperren
            btnEnableToggle = new Button
            {
                Location = new Point(570, yStart - 4),
                Size = new Size(120, 24),
                FlatStyle = FlatStyle.Flat
            };
            btnEnableToggle.FlatAppearance.BorderSize = 0;
            btnEnableToggle.Click += BtnEnableToggle_Click;
            Controls.Add(btnEnableToggle);

            // NEU: Deaktiviert-Checkbox
            _chkDisabled = new CheckBox
            {
                Text = "Deaktiviert",
                Location = new Point(700, yStart - 2),
                AutoSize = true,
                Checked = DeviceDisabled
            };
            _chkDisabled.CheckedChanged += (s, e) =>
            {
                DeviceDisabled = _chkDisabled.Checked;
                try { IniHelper.WriteValue("NV200/2", "Disabled", DeviceDisabled ? "1" : "0", iniPath); } catch { }
                ApplyDisabledState();
                UpdateStatusUi();
                try { AdminOverviewFormRefreshSafe(); } catch { }
            };
            Controls.Add(_chkDisabled);

            // --- Sende-Bereich ---
            var lblSend = new Label
            {
                Text = "Senden (Hex):",
                Location = new Point(24, yStart + 34),
                Size = new Size(100, 20)
            };
            Controls.Add(lblSend);

            txtSendHex = new TextBox
            {
                Location = new Point(130, yStart + 32),
                Size = new Size(240, 20),
                Text = "7F"
            };
            Controls.Add(txtSendHex);

            btnSenden = new Button
            {
                Location = new Point(380, yStart + 30),
                Size = new Size(80, 23),
                Text = "Senden"
            };
            btnSenden.Click += btnSenden_Click;
            Controls.Add(btnSenden);

            chkAutoSenden = new CheckBox
            {
                Location = new Point(470, yStart + 34),
                Size = new Size(180, 20),
                Text = "Auto Senden (alle 500ms)"
            };
            chkAutoSenden.CheckedChanged += chkAutoSenden_CheckedChanged;
            Controls.Add(chkAutoSenden);

            tmrAutoSend = new Timer { Interval = 500 };
            tmrAutoSend.Tick += tmrAutoSend_Tick;

            // --- Log-Feld ---
            txtLog = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Location = new Point(24, yStart + 70),
                Size = new Size(800, 850),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left,
                Font = new Font("Consolas", 11F),
                BackColor = Color.White,
                ForeColor = Color.FromArgb(33, 37, 41),
                Visible = true
            };
            Controls.Add(txtLog);

            // --- Status-Panel ---
            _panelStatus = new Panel
            {
                Location = new Point(850, yStart),
                Size = new Size(380, 160),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                BackColor = Color.FromArgb(245, 247, 250)
            };
            Controls.Add(_panelStatus);

            var lblStatusTitle = new Label
            {
                Text = "Status NV200/2",
                Location = new Point(10, 10),
                AutoSize = true,
                Font = new Font("Segoe UI Variable", 13F, FontStyle.Bold)
            };
            _panelStatus.Controls.Add(lblStatusTitle);

            var lConn = new Label { Text = "Verbindung:", Location = new Point(20, 44), AutoSize = true, Font = new Font("Segoe UI Variable", 11F) };
            _panelStatus.Controls.Add(lConn);
            _lblConn = new Label { Text = "-", Location = new Point(140, 42), AutoSize = true, Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold) };
            _panelStatus.Controls.Add(_lblConn);

            var lPort = new Label { Text = "Port/Addr:", Location = new Point(20, 70), AutoSize = true, Font = new Font("Segoe UI Variable", 11F) };
            _panelStatus.Controls.Add(lPort);
            _lblPort = new Label { Text = "-", Location = new Point(140, 68), AutoSize = true, Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold) };
            _panelStatus.Controls.Add(_lblPort);

            var lLast = new Label { Text = "Letzte Aktivit�t:", Location = new Point(20, 96), AutoSize = true, Font = new Font("Segoe UI Variable", 11F) };
            _panelStatus.Controls.Add(lLast);
            _lblLastEvt = new Label { Text = "-", Location = new Point(140, 94), AutoSize = true, Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold) };
            _panelStatus.Controls.Add(_lblLastEvt);

            var lState = new Label { Text = "Zustand:", Location = new Point(20, 118), AutoSize = true, Font = new Font("Segoe UI Variable", 11F) };
            _panelStatus.Controls.Add(lState);
            _lblState = new Label { Text = "-", Location = new Point(140, 116), AutoSize = true, Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold) };
            _panelStatus.Controls.Add(_lblState);

            // --- Bestands-Panel ---
            _panelBestand = new Panel
            {
                Location = new Point(850, yStart + 180),
                Size = new Size(380, 420),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                BackColor = Color.FromArgb(245, 247, 250)
            };
            Controls.Add(_panelBestand);

            var lblTitel = new Label
            {
                Text = "Bestand:",
                Location = new Point(10, 10),
                AutoSize = true,
                Font = new Font("Segoe UI Variable", 13F, FontStyle.Bold)
            };
            _panelBestand.Controls.Add(lblTitel);

            int[] werte = { 5, 10, 20, 50, 100, 200, 500 };
            for (int i = 0; i < werte.Length; i++)
            {
                var ldenom = new Label
                {
                    Text = $"{werte[i],3} �:",
                    Location = new Point(30, 50 + i * 38),
                    AutoSize = true,
                    Font = new Font("Segoe UI Variable", 12F)
                };
                _panelBestand.Controls.Add(ldenom);

                // Payout-Spalte
                var lval = new Label
                {
                    Text = "0",
                    Location = new Point(120, 50 + i * 38),
                    Size = new Size(60, 32),
                    TextAlign = ContentAlignment.MiddleRight,
                    BorderStyle = BorderStyle.FixedSingle,
                    Font = new Font("Segoe UI Variable", 12F, FontStyle.Bold),
                    BackColor = Color.White
                };
                _panelBestand.Controls.Add(lval);
                _lblBestandAnz[i] = lval;

                // Cashbox-Spalte
                var lcb = new Label
                {
                    Text = "0",
                    Location = new Point(260, 50 + i * 38),
                    Size = new Size(60, 32),
                    TextAlign = ContentAlignment.MiddleRight,
                    BorderStyle = BorderStyle.FixedSingle,
                    Font = new Font("Segoe UI Variable", 12F, FontStyle.Bold),
                    BackColor = Color.White
                };
                _panelBestand.Controls.Add(lcb);
                _lblCashboxAnz[i] = lcb;
            }

            // Summen Bereich modernisieren: Trennerlinie
            var sepLine = new Panel
            {
                Location = new Point(20, 50 + werte.Length * 38 - 4),
                Size = new Size(_panelBestand.Width - 40, 2),
                BackColor = Color.Black,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            _panelBestand.Controls.Add(sepLine);

            int baseSumY = 50 + werte.Length * 38 + 6;

            _lblBestandSum = new Label
            {
                Text = "0,00 �",
                Location = new Point(110, baseSumY),
                Size = new Size(110, 24),
                Font = new Font("Segoe UI Variable", 13F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            };
            _panelBestand.Controls.Add(_lblBestandSum);

            _lblCashboxSumNew = new Label
            {
                Text = "0,00 �",
                Location = new Point(250, baseSumY),
                Size = new Size(130, 24),
                Font = new Font("Segoe UI Variable", 13F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            };
            _panelBestand.Controls.Add(_lblCashboxSumNew);

            _lblGesamtSum = new Label
            {
                Text = "Gesamt: 0,00 �",
                Location = new Point((_panelBestand.Width - 200) / 2, baseSumY + 30),
                Size = new Size(200, 24),
                Font = new Font("Segoe UI Variable", 12.5F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            };
            _panelBestand.Controls.Add(_lblGesamtSum);

            _btnBestandRefresh = new Button
            {
                Text = "Aktualisieren",
                Size = new Size(140, 32),
                Location = new Point((_panelBestand.Width - 140) / 2, baseSumY + 60),
                Anchor = AnchorStyles.Top
            };
            _panelBestand.Controls.Add(_btnBestandRefresh);
            _btnBestandRefresh.Click += (s, e) => RefreshBestand();

            _tmrBestand = new Timer { Interval = 3000 };
            _tmrBestand.Tick += (s, e) => RefreshBestand();

            // --- Admin-Extras rechts unten ---
            // Payout St�ckelung Button -> echtes Toggle-Panel
            btnPayoutStueckelung = new Button
            {
                Text = "Payout St�ckelung",
                Location = new Point(850, yStart + 610),
                Size = new Size(220, 32),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnPayoutStueckelung.Click += (s, e) => ToggleMaxConfigPanel();
            Controls.Add(btnPayoutStueckelung);

            // Manuelles Routing (CheckBox) � deaktiviert, da Routing dynamisch erfolgt
            _chkRouteAllPayout = new CheckBox
            {
                Text = "Manuelles Routing deaktiviert (automatisch)",
                AutoSize = false,
                Location = new Point(850, yStart + 662),
                Size = new Size(360, 38),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                UseCompatibleTextRendering = true,
                CheckAlign = ContentAlignment.TopLeft,
                TextAlign = ContentAlignment.TopLeft,
                ThreeState = true,
                Enabled = false
            };
            Controls.Add(_chkRouteAllPayout);
            _chkRouteAllPayout.BringToFront();
            txtLog.SendToBack();
        }

        // Toggle Payout St�ckelung Panel (Zielbest�nde UI)
        private void ToggleMaxConfigPanel()
        {
            try
            {
                InitMaxConfigUi();
                if (_panelMaxConfig == null) return;

                bool newVisible = !_panelMaxConfig.Visible;
                _panelMaxConfig.Visible = newVisible;
                if (newVisible)
                {
                    _panelMaxConfig.BringToFront();
                    ScrollControlIntoView(_panelMaxConfig);
                    btnPayoutStueckelung.Text = "Payout St�ckelung ausblenden";
                    LoadMaxFromIniToUi();
                }
                else
                {
                    btnPayoutStueckelung.Text = "Payout St�ckelung";
                }
            }
            catch (Exception ex)
            {
                AppendLog("Payout St�ckelung konnte nicht ge�ffnet/geschlossen werden: " + ex.Message);
            }
        }

        private void InitMaxConfigUi()
        {
            if (_panelMaxConfig != null) return;

            _panelMaxConfig = new Panel
            {
                Location = new Point(850, 70 + 710), // unterhalb Checkbox
                Size = new Size(380, 300),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                BackColor = Color.FromArgb(245, 247, 250),
                Visible = false
            };
            Controls.Add(_panelMaxConfig);

            var lbl = new Label
            {
                Text = "Payout-Zielbest�nde",
                Location = new Point(10, 10),
                AutoSize = true,
                Font = new Font("Segoe UI Variable", 13F, FontStyle.Bold)
            };
            _panelMaxConfig.Controls.Add(lbl);

            _btnMaxClose = new Button
            {
                Text = "�",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = Color.FromArgb(66, 66, 66),
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(32, 28),
                Left = _panelMaxConfig.Width - 42,
                Top = 8,
                TabStop = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            _btnMaxClose.FlatAppearance.BorderSize = 0;
            _btnMaxClose.FlatAppearance.MouseOverBackColor = Color.FromArgb(230, 230, 230);
            _btnMaxClose.Click += (s, e) => { _panelMaxConfig.Visible = false; btnPayoutStueckelung.Text = "Payout St�ckelung"; };
            _panelMaxConfig.Controls.Add(_btnMaxClose);

            var sep = new Panel { Left = 10, Top = 42, Width = _panelMaxConfig.Width - 20, Height = 1, BackColor = Color.FromArgb(220, 225, 230), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            _panelMaxConfig.Controls.Add(sep);

            var labels = new[] { "5 �", "10 �", "20 �", "50 �", "100 �", "200 �", "500 �" };
            int baseLeft = 20;
            int baseTop = 56;
            int rowH = 30;

            for (int i = 0; i < 7; i++)
            {
                var l = new Label
                {
                    Left = baseLeft,
                    Top = baseTop + i * rowH,
                    Width = 80,
                    Text = labels[i],
                    Font = new Font("Segoe UI Variable", 11F, FontStyle.Regular),
                    ForeColor = Color.FromArgb(33, 37, 41)
                };
                _panelMaxConfig.Controls.Add(l);

                var nud = new NumericUpDown
                {
                    Left = baseLeft + 90,
                    Top = baseTop + i * rowH - 2,
                    Width = 100,
                    Minimum = 0,
                    Maximum = 65535,
                    Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold),
                    TextAlign = HorizontalAlignment.Right
                };
                _panelMaxConfig.Controls.Add(nud);
                _nudMax[i] = nud;
            }

            _btnMaxLoad = new Button
            {
                Text = "Laden",
                Left = baseLeft + 210,
                Top = baseTop,
                Width = 130,
                Height = 30,
                FlatStyle = FlatStyle.Flat,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                BackColor = Color.FromArgb(33, 150, 243),
                ForeColor = Color.White
            };
            _btnMaxLoad.FlatAppearance.BorderSize = 0;
            _btnMaxLoad.FlatAppearance.MouseOverBackColor = Color.FromArgb(30, 136, 229);
            _btnMaxLoad.Click += (s, e) => LoadMaxFromIniToUi();
            _panelMaxConfig.Controls.Add(_btnMaxLoad);

            _btnMaxSave = new Button
            {
                Text = "Speichern",
                Left = baseLeft + 210,
                Top = baseTop + 36,
                Width = 130,
                Height = 30,
                FlatStyle = FlatStyle.Flat,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                BackColor = Color.FromArgb(46, 125, 50),
                ForeColor = Color.White
            };
            _btnMaxSave.FlatAppearance.BorderSize = 0;
            _btnMaxSave.FlatAppearance.MouseOverBackColor = Color.FromArgb(27, 94, 32);
            _btnMaxSave.Click += (s, e) => SaveMaxFromUiToIniAndApply();
            _panelMaxConfig.Controls.Add(_btnMaxSave);

            _lblMaxInfo = new Label
            {
                Left = baseLeft + 210,
                Top = baseTop + 76,
                Width = 150,
                Height = 80,
                ForeColor = Color.DimGray,
                Font = new Font("Segoe UI Variable", 9.5F, FontStyle.Regular),
                Text = "Hinweis:\r\n- Nach Speichern werden\r\n  Routen neu gesetzt."
            };
            _panelMaxConfig.Controls.Add(_lblMaxInfo);

            // Vorbelegung aus Session
            try
            {
                _nudMax[0].Value = Clamp(_ssp?.MAX_PayoutCount_of_5Euro ?? 0);
                _nudMax[1].Value = Clamp(_ssp?.MAX_PayoutCount_of_10Euro ?? 0);
                _nudMax[2].Value = Clamp(_ssp?.MAX_PayoutCount_of_20Euro ?? 0);
                _nudMax[3].Value = Clamp(_ssp?.MAX_PayoutCount_of_50Euro ?? 0);
                _nudMax[4].Value = Clamp(_ssp?.MAX_PayoutCount_of_100Euro ?? 0);
                _nudMax[5].Value = Clamp(_ssp?.MAX_PayoutCount_of_200Euro ?? 0);
                _nudMax[6].Value = Clamp(_ssp?.MAX_PayoutCount_of_500Euro ?? 0);
            }
            catch { }
        }

        private static decimal Clamp(int v)
        {
            if (v < 0) return 0;
            if (v > 65535) return 65535;
            return v;
        }

        private void LoadMaxFromIniToUi()
        {
            string section = DetermineNv2Section();

            int ReadMax(string key, int current)
            {
                try
                {
                    var s = IniHelper.ReadValue(section, key, AppSettings.IniPath);
                    if (int.TryParse(s, out var v)) return v;
                }
                catch { }
                return current;
            }

            try
            {
                _nudMax[0].Value = Clamp(ReadMax("Max_5", _ssp?.MAX_PayoutCount_of_5Euro ?? 0));
                _nudMax[1].Value = Clamp(ReadMax("Max_10", _ssp?.MAX_PayoutCount_of_10Euro ?? 0));
                _nudMax[2].Value = Clamp(ReadMax("Max_20", _ssp?.MAX_PayoutCount_of_20Euro ?? 0));
                _nudMax[3].Value = Clamp(ReadMax("Max_50", _ssp?.MAX_PayoutCount_of_50Euro ?? 0));
                _nudMax[4].Value = Clamp(ReadMax("Max_100", _ssp?.MAX_PayoutCount_of_100Euro ?? 0));
                _nudMax[5].Value = Clamp(ReadMax("Max_200", _ssp?.MAX_PayoutCount_of_200Euro ?? 0));
                _nudMax[6].Value = Clamp(ReadMax("Max_500", _ssp?.MAX_PayoutCount_of_500Euro ?? 0));
            }
            catch { }
        }

        private void SaveMaxFromUiToIniAndApply()
        {
            string section = DetermineNv2Section();

            void WriteMax(string key, decimal value)
            {
                try { IniHelper.WriteValue(section, key, ((int)value).ToString(), AppSettings.IniPath); } catch { }
            }

            try
            {
                // 1) In INI schreiben (NV200/2)
                WriteMax("Max_5", _nudMax[0].Value);
                WriteMax("Max_10", _nudMax[1].Value);
                WriteMax("Max_20", _nudMax[2].Value);
                WriteMax("Max_50", _nudMax[3].Value);
                WriteMax("Max_100", _nudMax[4].Value);
                WriteMax("Max_200", _nudMax[5].Value);
                WriteMax("Max_500", _nudMax[6].Value);

                // 2) In laufender NV200/2-Session anwenden
                if (_ssp != null)
                {
                    _ssp.MAX_PayoutCount_of_5Euro = (int)_nudMax[0].Value;
                    _ssp.MAX_PayoutCount_of_10Euro = (int)_nudMax[1].Value;
                    _ssp.MAX_PayoutCount_of_20Euro = (int)_nudMax[2].Value;
                    _ssp.MAX_PayoutCount_of_50Euro = (int)_nudMax[3].Value;
                    _ssp.MAX_PayoutCount_of_100Euro = (int)_nudMax[4].Value;
                    _ssp.MAX_PayoutCount_of_200Euro = (int)_nudMax[5].Value;
                    _ssp.MAX_PayoutCount_of_500Euro = (int)_nudMax[6].Value;

                    // Routing rein dynamisch nach MAX setzen
                    _ssp.Set_Routing();
                    AppendLog($"Max-Zielbest�nde aktualisiert ({section}) und Routen neu gesetzt.");
                }

                MessageBox.Show(this, "Einstellungen gespeichert und angewendet.", "OK", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Fehler beim Speichern:\r\n" + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RefreshBestand()
        {
            try
            {
                if (_ssp == null) return;
                _ssp.Payout_angleichen();
                int[] counts = { _ssp.Payout_5_euro, _ssp.Payout_10_euro, _ssp.Payout_20_euro, _ssp.Payout_50_euro, _ssp.Payout_100_euro, _ssp.Payout_200_euro, _ssp.Payout_500_euro };
                for (int i = 0; i < counts.Length && i < _lblBestandAnz.Length; i++) _lblBestandAnz[i].Text = counts[i].ToString();
                decimal sumEuro = 5m*counts[0] + 10m*counts[1] + 20m*counts[2] + 50m*counts[3] + 100m*counts[4] + 200m*counts[5] + 500m*counts[6];
                var de = CultureInfo.GetCultureInfo("de-DE");
                _lblBestandSum.Text = sumEuro.ToString("N2", de) + " �";
                int[] cbox = { _ssp.Cashbox_5_euro, _ssp.Cashbox_10_euro, _ssp.Cashbox_20_euro, _ssp.Cashbox_50_euro, _ssp.Cashbox_100_euro, _ssp.Cashbox_200_euro, _ssp.Cashbox_500_euro };
                for (int i = 0; i < cbox.Length && i < _lblCashboxAnz.Length; i++) _lblCashboxAnz[i].Text = cbox[i].ToString();
                decimal sumCbox = 5m*cbox[0] + 10m*cbox[1] + 20m*cbox[2] + 50m*cbox[3] + 100m*cbox[4] + 200m*cbox[5] + 500m*cbox[6];
                if (_lblCashboxSumNew != null) _lblCashboxSumNew.Text = sumCbox.ToString("N2", de) + " �";
                if (_lblGesamtSum != null) _lblGesamtSum.Text = "Gesamt: " + (sumEuro + sumCbox).ToString("N2", de) + " �";
            }
            catch { }
        }

        private void StartBestandTimer(bool start)
        {
            if (_tmrBestand == null) return;
            _tmrBestand.Enabled = start;
            if (start) RefreshBestand();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try { LoadInitialNvLog(); } catch { }
            try { _ssp?.StartLiveFrameLogging(); AppendLog("Live-Frame-Logging aktiv (Form ge�ffnet)"); } catch { }
        }

        // Erweitern: Live-Logging beim Schlie�en stoppen
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { _ssp?.EndLiveFrameLogging(); AppendLog("Live-Frame-Logging beendet (Form geschlossen)"); } catch { }
            try { _detectTimer?.Stop(); } catch { }
            try { _detectDialog?.Close(); } catch { }
            try { _detectDialog?.Dispose(); } catch { }
            _detectDialog = null;
            try { _tmrBestand?.Stop(); } catch { }
            try { _tmrStatus?.Stop(); } catch { }
            try { tmrAutoSend?.Stop(); } catch { }
            DetachSspEvents();
            base.OnFormClosed(e);
        }

        private void LoadInitialNvLog(int maxLines = 1000)
        {
            if (_ssp == null || txtLog == null) return;
            var lines = _ssp.GetFullLogSnapshot(maxLines, true);
            if (lines == null || lines.Count == 0) return;
            var sb = new System.Text.StringBuilder();
            foreach (var raw in lines)
            {
                try
                {
                    int firstDash = raw.IndexOf('-');
                    if (firstDash < 0) { sb.AppendLine(raw); continue; }
                    int secondDash = raw.IndexOf('-', firstDash + 1);
                    if (secondDash < 0) { sb.AppendLine(raw); continue; }
                    string dtPart = raw.Substring(firstDash + 1, secondDash - firstDash - 1).Trim();
                    string msgPart = raw.Substring(secondDash + 1).Trim();
                    DateTime dt;
                    if (DateTime.TryParse(dtPart, out dt))
                        sb.AppendLine(dt.ToString("HH:mm:ss.fff") + " - " + msgPart);
                    else
                        sb.AppendLine(dtPart + " - " + msgPart);
                }
                catch { sb.AppendLine(raw); }
            }
            txtLog.Clear();
            txtLog.Text = sb.ToString();
        }

        private void AppendLog(string text)
        {
            if (txtLog == null) return;
            if (txtLog.IsDisposed) return;
            if (txtLog.InvokeRequired)
            {
                try { txtLog.BeginInvoke((Action)(() => AppendLog(text))); } catch { }
                return;
            }
            try
            {
                txtLog.AppendText($"{DateTime.Now:HH:mm:ss.fff} - {text}{Environment.NewLine}");
                txtLog.SelectionStart = txtLog.TextLength;
                txtLog.ScrollToCaret();
            }
            catch { }
        }

        private void BtnEnableToggle_Click(object sender, EventArgs e)
        {
            if (_ssp == null)
            {
                AppendLog("Keine NV200/2-Instanz verf�gbar.");
                return;
            }

            try
            {
                _enabledRequested = !_enabledRequested;

                if (_enabledRequested)
                {
                    _ssp.Freigeben();
                    _ssp.ConfigureBezel(0, 255, 0);
                    AppendLog("NV200/2 freigegeben (Enable + Inhibits + Routing).");
                }
                else
                {
                    _ssp.Disable_Device();
                    _ssp.SetInhibit(true);
                    _ssp.ConfigureBezel(255, 0, 0);
                    AppendLog("NV200/2 gesperrt (Disable + Inhibit).");
                }

                UpdateEnableButtonVisual();
                UpdateStatusUi();
            }
            catch (Exception ex)
            {
                AppendLog("Fehler bei Freigabe/Sperre: " + ex.Message);
            }
        }

        private void UpdateEnableButtonVisual()
        {
            if (btnEnableToggle == null) return;
            btnEnableToggle.Text = _enabledRequested ? "Sperren" : "Freigeben";
            btnEnableToggle.BackColor = _enabledRequested ? Color.FromArgb(183, 28, 28) : Color.FromArgb(46, 125, 50);
            btnEnableToggle.ForeColor = Color.White;
        }

        private void AttachSspEvents()
        {
            if (_ssp == null || _eventsAttached) return;

            _ssp.Ereignis += SspOnEreignis;
            _ssp.Note_read += (wert) => AppendLog($"Note_read: {wert} �");
            _ssp.Note_akzepted += (wert) =>
            {
                AppendLog($"Note_accepted: {wert} �");
                try { BeginInvoke((Action)RefreshBestand); } catch { }
            };
            _ssp.Note_in_Bezel += (wert) =>
            {
                AppendLog($"Schein zur Entnahme bereit: {wert} �");
                _lblState.Text = $"Schein im Ausgabeschacht: {wert} �";
                _lblState.ForeColor = Color.FromArgb(255, 140, 0);
            };
            _ssp.Wert_Dispensing += (wert) => AppendLog($"Dispensing: {wert}");
            _ssp.Dispensing_Complete += (wert) =>
            {
                AppendLog($"Dispense complete: {wert}");
                try { BeginInvoke((Action)RefreshBestand); } catch { }
            };
            _ssp.Cashbox_Replaced += (wert) =>
            {
                AppendLog($"Cashbox replaced, amount: {wert}");
                try { BeginInvoke((Action)RefreshBestand); } catch { }
            };
            _ssp.Cashbox_Removed += () => AppendLog("Cashbox removed");
            _ssp.Jammed += (wert, neu) => AppendLog($"Jammed: wert={wert}, available={neu}");
            _ssp.Halted += (wert, neu) => AppendLog($"Halted: wert={wert}, available={neu}");
            _ssp.Error_while_payout += (wert, neu) => AppendLog($"Payout error: wert={wert}, available={neu}");
            _ssp.Payout_Timeout += (wert, neu) => AppendLog($"Payout timeout: wert={wert}, available={neu}");

            _eventsAttached = true;
            if (btnVerbinden != null) btnVerbinden.Text = "Events l�sen";
        }

        private void DetachSspEvents()
        {
            if (_ssp == null || !_eventsAttached) return;

            try { _ssp.Ereignis -= SspOnEreignis; } catch { }
            _eventsAttached = false;
            if (btnVerbinden != null) btnVerbinden.Text = "Events anh�ngen";
        }

        private void btnVerbinden_Click(object sender, EventArgs e)
        {
            if (_ssp == null)
            {
                AppendLog("Keine NV200/2-Instanz verf�gbar.");
                return;
            }

            // Nur Events toggeln � keine zweite Session!
            if (_eventsAttached)
            {
                DetachSspEvents();
                AppendLog("Events gel�st.");
            }
            else
            {
                AttachSspEvents();
                AppendLog("Events angeh�ngt.");
            }

            UpdateStatusUi();
        }

        private void btnSenden_Click(object sender, EventArgs e)
        {
            if (_ssp == null)
            {
                AppendLog("Nicht verbunden.");
                return;
            }

            string hex = (txtSendHex?.Text ?? "").Trim().Replace(" ", "");
            if (hex.Length == 0)
            {
                AppendLog("Kein Hex-String eingegeben.");
                return;
            }

            try
            {
                if (hex.Length % 2 != 0)
                    throw new FormatException("Ungerade Hex-L�nge.");

                byte[] data = new byte[hex.Length / 2];
                for (int i = 0; i < data.Length; i++)
                    data[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);

                _ssp.SendRaw(data);
                AppendLog("TX: " + NV200SerialPort.ToHex(data));
            }
            catch (Exception ex)
            {
                AppendLog("Fehler beim Senden: " + ex.Message);
            }
        }

        private void chkAutoSenden_CheckedChanged(object sender, EventArgs e)
        {
            if (tmrAutoSend != null && chkAutoSenden != null)
            {
                tmrAutoSend.Enabled = chkAutoSenden.Checked;
                AppendLog("AutoSenden: " + (chkAutoSenden.Checked ? "aktiviert" : "deaktiviert"));
            }
        }

        private void tmrAutoSend_Tick(object sender, EventArgs e)
        {
            btnSenden_Click(sender, e);
        }

        private void SspOnEreignis(string x)
        {
            _lastEvtUtc = DateTime.UtcNow;
            if (!txtLog.IsHandleCreated)
                return;
            if (InvokeRequired)
                BeginInvoke((Action)(() => AppendLog(x)));
            else
                AppendLog(x);
        }

        private void BtnComPortSpeichern_Click(object sender, EventArgs e)
        {
            var sel = txtComPort.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(sel)) { MessageBox.Show("Kein Port ausgew�hlt."); return; }
            IniHelper.WriteValue("NV200/2", "ComPort", sel, iniPath);
            AppendLog("COM-Port gespeichert: " + sel);
            MessageBox.Show("COM-Port gespeichert.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void UpdateStatusUi()
        {
            try
            {
                if (DeviceDisabled)
                {
                    _lblConn.Text = "Deaktiviert";
                    _lblConn.ForeColor = Color.Gray;
                    _lblPort.Text = "-";
                    _lblLastEvt.Text = "-";
                    _lblState.Text = "disabled";
                    _lblState.ForeColor = Color.Gray;
                    return;
                }
                bool commAlive = false;
                if (_ssp != null)
                {
                    var diff = DateTime.Now - _ssp.Last_Communicationtime;
                    commAlive = diff.TotalSeconds < 3.0;
                }
                _lblConn.Text = commAlive ? "Verbunden" : "Keine Aktivit�t";
                _lblConn.ForeColor = commAlive ? Color.FromArgb(0, 128, 0) : Color.FromArgb(183, 28, 28);
                _lblPort.Text = $"{(_ssp?.ComPort ?? "-")}, Addr {(_ssp != null ? _ssp.SSPAdress.ToString() : "-")}_";
                EnsureSelectedPort(_ssp?.ComPort, initial: false); // nur hinzuf�gen
                if (_lastEvtUtc == DateTime.MinValue)
                    _lblLastEvt.Text = "�";
                else
                {
                    var ago = DateTime.UtcNow - _lastEvtUtc;
                    _lblLastEvt.Text = $"{_lastEvtUtc.ToLocalTime():HH:mm:ss} ({Math.Max(0, (int)ago.TotalSeconds)} s)";
                }

                var s = _ssp?.states ?? "-";
                _lblState.Text = s;

                var t = (s ?? "").ToLowerInvariant();
                Color c = Color.SteelBlue;
                if (t.Contains("idle") || t.Contains(" bereit") || t.Contains("started") || t.Contains("connected") || t.Contains("syncronisiert") || t.Contains("synchronisiert"))
                    c = Color.FromArgb(0, 128, 0);
                else if (t.Contains("dispens") || t.Contains("stack") || t.Contains("reading") || t.Contains("read note"))
                    c = Color.FromArgb(255, 140, 0);
                else if (t.Contains("disabled") || t.Contains("jammed") || t.Contains("halted") || t.Contains("failed") || t.Contains("timeout") || t.Contains("open sspcomport") || t.Contains("neustart"))
                    c = Color.FromArgb(183, 28, 28);

                _lblState.ForeColor = c;
            }
            catch { }
        }

        // Deaktiviert: Rout_X aus INI lesen/schreiben
        private void DisableRouteCheckboxUi()
        {
            if (_chkRouteAllPayout != null)
            {
                _chkRouteAllPayout.Enabled = false;
                _chkRouteAllPayout.Text = "Manuelles Routing deaktiviert (automatisch)";
                _chkRouteAllPayout.CheckState = CheckState.Indeterminate;
            }
        }

        private string DetermineNv2Section()
        {
            try
            {
                string s1 = IniHelper.ReadValue("NV200/1", "ComPort", AppSettings.IniPath);
                string s2 = IniHelper.ReadValue("NV200/2", "ComPort", AppSettings.IniPath);
                string cp = _ssp?.ComPort ?? txtComPort?.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(cp))
                {
                    if (!string.IsNullOrWhiteSpace(s2) && string.Equals(s2, cp, System.StringComparison.OrdinalIgnoreCase)) return "NV200/2";
                    if (!string.IsNullOrWhiteSpace(s1) && string.Equals(s1, cp, System.StringComparison.OrdinalIgnoreCase)) return "NV200/1";
                }
            }
            catch { }
            return "NV200/2"; // Default f�r dieses Formular
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            UpdateStatusUi();
        }

        private void LoadComPorts(bool passive = false)
        {
            try
            {
                var prev = _lastPorts ?? Array.Empty<string>();
                var current = txtComPort.SelectedItem as string;
                var ports = SerialPort.GetPortNames();
                Array.Sort(ports, StringComparer.OrdinalIgnoreCase);
                _lastPorts = ports;
                var newPorts = ports.Except(prev ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase).ToList();
                txtComPort.Items.Clear();
                txtComPort.Items.AddRange(ports);
                if (_detectingPort && _detectBasePorts != null)
                {
                    var added = ports.Where(p => !_detectBasePorts.Contains(p, StringComparer.OrdinalIgnoreCase)).ToList();
                    if (added.Count > 0) { SelectDetectedPort(added[0]); return; }
                }
                else if (!passive)
                {
                    if (string.IsNullOrWhiteSpace(current) && newPorts.Count > 0)
                        txtComPort.SelectedItem = newPorts[0];
                    else if (!string.IsNullOrWhiteSpace(current))
                    {
                        int idx = Array.IndexOf(ports, current);
                        if (idx >= 0) txtComPort.SelectedIndex = idx; else txtComPort.SelectedIndex = -1;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(current))
                {
                    int idx = Array.IndexOf(ports, current);
                    if (idx >= 0) txtComPort.SelectedIndex = idx; else txtComPort.SelectedIndex = -1;
                }
                if (txtComPort.Items.Count == 0) txtComPort.SelectedIndex = -1;
            }
            catch { }
        }

        // Helfer: tats�chlichen Port sicher im Dropdown selektieren (ggf. hinzuf�gen)
        private bool _initialPortApplied = false; // NEU
        private void EnsureSelectedPort(string port, bool initial = false)
        {
            if (string.IsNullOrWhiteSpace(port) || txtComPort == null) return;
            try
            {
                bool exists = txtComPort.Items.Cast<object>().Any(o => string.Equals(Convert.ToString(o), port, StringComparison.OrdinalIgnoreCase));
                if (!exists) txtComPort.Items.Add(port);
                if (initial && !_initialPortApplied)
                {
                    txtComPort.SelectedItem = port;
                    _initialPortApplied = true;
                }
            }
            catch { }
        }

        private void StartDetectPortMode()
        {
            if (_detectingPort) { StopDetectPortMode(true); return; }
            try
            {
                _detectingPort = true;
                _btnDetectPort.Text = "Stop";

                _detectDialog = new Form
                {
                    Text = "COM-Port Erkennung",
                    Size = new Size(460, 180),
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    StartPosition = FormStartPosition.CenterParent,
                    ControlBox = false,
                    TopMost = true
                };
                var lbl = new Label
                {
                    Text = "Ist das entsprechende Gerät getrennt?\r\nBitte Gerät JETZT trennen und danach auf \"Weiter\" klicken.",
                    AutoSize = false,
                    Location = new Point(12, 12),
                    Size = new Size(420, 56)
                };
                _detectDialog.Controls.Add(lbl);
                var lblCountdown = new Label { Text = string.Empty, AutoSize = false, Location = new Point(12, 72), Size = new Size(420, 24), ForeColor = Color.DimGray };
                _detectDialog.Controls.Add(lblCountdown);
                var btnContinue = new Button { Text = "Weiter", Location = new Point(260, 110), Size = new Size(90, 28) };
                var btnCancel = new Button { Text = "Abbrechen", Location = new Point(356, 110), Size = new Size(90, 28) };
                _detectDialog.Controls.Add(btnContinue);
                _detectDialog.Controls.Add(btnCancel);
                btnCancel.Click += (s, e) => { StopDetectPortMode(true); };

                int countdown = 4;
                Timer preTimer = new Timer { Interval = 1000 };
                btnContinue.Click += (s, e) =>
                {
                    try
                    {
                        btnContinue.Enabled = false;
                        lbl.Text = "Bitte warten... (Erkennung startet gleich)";
                        lblCountdown.Text = $"Countdown: {countdown} s";
                        preTimer.Tick += (ts, te) =>
                        {
                            countdown--;
                            if (countdown > 0)
                            {
                                lblCountdown.Text = $"Countdown: {countdown} s";
                            }
                            else
                            {
                                preTimer.Stop(); preTimer.Dispose();
                                _detectBasePorts = SerialPort.GetPortNames();
                                Array.Sort(_detectBasePorts, StringComparer.OrdinalIgnoreCase);
                                lbl.Text = "Jetzt bitte das Gerät einstecken.\r\nFenster schließt automatisch bei Erkennung.";
                                lblCountdown.Text = string.Empty;
                                _detectTimer = new Timer { Interval = 700 };
                                _detectTimer.Tick += (ts2, te2) => LoadComPorts();
                                _detectTimer.Start();
                            }
                        };
                        preTimer.Start();
                    }
                    catch { }
                };

                _detectDialog.Show(this);
            }
            catch { StopDetectPortMode(true); }
        }

        private void SelectDetectedPort(string port)
        {
            try
            {
                if (!txtComPort.Items.Cast<object>().Any(o => string.Equals(Convert.ToString(o), port, StringComparison.OrdinalIgnoreCase)))
                {
                    txtComPort.Items.Add(port);
                }
                txtComPort.SelectedItem = port;
                try { IniHelper.WriteValue("NV200/2", "ComPort", port, iniPath); } catch { }
                try { if (_ssp != null) _ssp.ComPort = port; } catch { }
                AppendLog("COM-Port erkannt und übernommen: " + port);
            }
            catch { }
            StopDetectPortMode(false);
        }

        private void StopDetectPortMode(bool cancelled)
        {
            try { _detectTimer?.Stop(); } catch { }
            _detectTimer = null;
            if (_detectDialog != null)
            {
                try { _detectDialog.Close(); } catch { }
                try { _detectDialog.Dispose(); } catch { }
                _detectDialog = null;
            }
            _detectingPort = false;
            if (_btnDetectPort != null) _btnDetectPort.Text = "Erkennung";
            AppendLog(cancelled ? "COM-Port Erkennung abgebrochen." : "Neuer COM-Port erkannt: " + (txtComPort.SelectedItem ?? "?"));
        }

        // Hilfsmethode: globalen Overview aktualisieren (wie in NV200/1)
        private void AdminOverviewFormRefreshSafe()
        {
            try
            {
                foreach (Form f in Application.OpenForms)
                {
                    if (f is AdminOverviewForm ov)
                    {
                        var mi = typeof(AdminOverviewForm).GetMethod("UpdateDeviceStatus", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                        try { mi?.Invoke(ov, null); } catch { }
                        break;
                    }
                }
            }
            catch { }
        }

        // Fehlenden DeviceDisabled-Umschalter erg�nzen
        private void ApplyDisabledState()
        {
            if (DeviceDisabled)
            {
                try { _tmrBestand?.Stop(); } catch { }
                try { _ssp?.Disable_Device(); _ssp?.SetInhibit(true); _ssp?.ConfigureBezel(255, 0, 0); } catch { }
                try { DetachSspEvents(); } catch { }
            }
            else
            {
                try { StartBestandTimer(true); } catch { }
                try { AttachSspEvents(); } catch { }
                try { _ssp?.SetInhibit(false); } catch { }
            }
        }
    }
}
// Globale Korrektur: verbliebene _sip Verweise ersetzt durch _ssp
// Diese �nderung wurde automatisch durchgef�hrt. Bitte pr�fen Sie dennoch die Logik und den Fluss des Programms.