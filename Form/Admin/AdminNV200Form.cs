using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.IO.Ports; // NEU f?r COM-Port Auflistung
using System.Globalization; // NEU f?r Formatierung

namespace Geldautomat
{
    public partial class AdminNV200Form : Form
    {
        private NV200_SSP _ssp;
        private Panel headerPanel;
        private Button btnClose;
        private Button btnMinimize;
        private Label lblTitle;
        private Point _mouseDownLocation;

        private Panel _panelBestand;
        private Label[] _lblBestandAnz = new Label[7];
        private Label _lblBestandSum;
        private Label _lblCashboxSumNew; // umbenannt
        private Label _lblGesamtSum; // NEU Gesamtbetrag
        private Button _btnBestandRefresh;
        private Timer _tmrBestand;

        private TextBox txtLog;

        // Verbindung/Sendebereich
        private ComboBox txtComPort; // ersetzt TextBox durch ComboBox
        private Button _btnDetectPort; // NEU: Erkennung
        private string[] _lastPorts; // NEU: letzte Ports
        private bool _detectingPort; // NEU: Erkennungsmodus aktiv
        private string[] _detectBasePorts; // Basisliste f?r Erkennung
        private Form _detectDialog; // Hinweisfenster
        private Timer _detectTimer; // Polling Timer
        private Button btnVerbinden;
        private TextBox txtSendHex;
        private Button btnSenden;
        private CheckBox chkAutoSenden;
        private Timer tmrAutoSend;
        private Button btnComPortSpeichern;
        private string iniPath = @"C:\ProgramData\SuE-Software\SuE-TaMi Client SQL\Geldautomat.ini";

        // NEU: Statusanzeige
        private Panel _panelStatus;
        private Label _lblConn;
        private Label _lblPort;
        private Label _lblLastEvt;
        private Label _lblState;
        private Timer _tmrStatus;
        private DateTime _lastEvtUtc = DateTime.MinValue;

        // NEU: Enable/Disable-Toggle
        private Button btnEnableToggle;
        private bool _enabledRequested = false;

        // Event-Verwaltung / Besitz der Instanz
        private bool _eventsAttached = false;

        private string _tempStateText = null;
        //private bool _showingBezelNote = false;

        // NEU: Button zum ?ffnen der MaxConfig und Checkbox f?r manuelles Routing
        
        private CheckBox _chkRouteAllPayout;
       // private bool _loadingRouteUi = false;

        // Feld erg?nzen:
        private Button btnPayoutStueckelung;
        
        // NEU: Deaktivieren-Checkbox + globaler Status
        private CheckBox _chkDisabled;
        public static bool DeviceDisabled = false; // true = keine Verbindungs-/Eventversuche
        
        private Label[] _lblCashboxAnz = new Label[7];
        private bool _initialPortApplied = false; // hinzugef?gt f?r EnsureSelectedPort

        public AdminNV200Form(NV200_SSP ssp)
        {
            _ssp = ssp ?? throw new ArgumentNullException(nameof(ssp));
            // LOAD persisted disabled flag
            try
            {
                var dv = IniHelper.ReadValue("NV200/1", "Disabled", iniPath);
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
                Text = "Admin - NV200 Steuerung",
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
                Text = "\u2715",
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

            // Events aus globaler Session anh�ngen (ohne Start/Stop)
            AttachSspEvents();

            // Anfangswerte setzen
            txtComPort.Text = _ssp?.ComPort ?? "";
            UpdateEnableButtonVisual();
            UpdateStatusUi();
            RefreshBestand();
            try { DisableRouteCheckboxUi(); } catch { }
            EnsureSelectedPort(_ssp?.ComPort, initial: true); // nur initial
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

            // --- Verbindungsbereich (nur Events anh�ngen/l�sen, keine zweite Session) ---
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
            txtComPort.DropDown += (s,e)=> LoadComPorts(true); // passiver Refresh
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

            // NEU: Freigeben/Sperren (analog AdminCoinForm)
            btnEnableToggle = new Button
            {
                Location = new Point(570, yStart - 4),
                Size = new Size(120, 24),
                FlatStyle = FlatStyle.Flat
            };
            btnEnableToggle.FlatAppearance.BorderSize = 0;
            btnEnableToggle.Click += BtnEnableToggle_Click;
            Controls.Add(btnEnableToggle);

            // NEU: Deaktiviert-Checkbox (verhindert Verbindung)
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
                try { IniHelper.WriteValue("NV200/1", "Disabled", DeviceDisabled ? "1" : "0", iniPath); } catch { }
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
                Text = "Status NV200",
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

            // Spalten-Header
            var hdrPayout = new Label
            {
                Text = "Payout",
                Location = new Point(120, 30),
                AutoSize = true,
                Font = new Font("Segoe UI Variable", 10F, FontStyle.Bold)
            };
            _panelBestand.Controls.Add(hdrPayout);

            var hdrCashbox = new Label
            {
                Text = "Cashbox",
                Location = new Point(260, 30),
                AutoSize = true,
                Font = new Font("Segoe UI Variable", 10F, FontStyle.Bold)
            };
            _panelBestand.Controls.Add(hdrCashbox);

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

            // Summen
            // Summen Bereich neu gestalten: Trennerlinie unter letzter Wertezeile
            var sepLine = new Panel
            {
                Location = new Point(20, 50 + werte.Length * 38 - 4),
                Size = new Size(_panelBestand.Width - 40, 2),
                BackColor = Color.Black,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            _panelBestand.Controls.Add(sepLine);

            int baseSumY = 50 + werte.Length * 38 + 6; // unterhalb der letzten Zeile

            // Payout Summe
            _lblBestandSum = new Label
            {
                Text = "0,00 �",
                Location = new Point(110, baseSumY),
                Size = new Size(110, 24),
                Font = new Font("Segoe UI Variable", 13F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            };
            _panelBestand.Controls.Add(_lblBestandSum);

            // Cashbox Summe
            _lblCashboxSumNew = new Label
            {
                Text = "0,00 �",
                Location = new Point(250, baseSumY),
                Size = new Size(130, 24),
                Font = new Font("Segoe UI Variable", 13F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            };
            _panelBestand.Controls.Add(_lblCashboxSumNew);

            // Gesamt (zentriert zwischen beiden Spalten)
            _lblGesamtSum = new Label
            {
                Text = "Gesamt: 0,00 �",
                Location = new Point((_panelBestand.Width - 200) / 2, baseSumY + 30),
                Size = new Size(200, 24),
                Font = new Font("Segoe UI Variable", 12.5F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            };
            _panelBestand.Controls.Add(_lblGesamtSum);

            // Aktualisieren-Button eine Zeile tiefer und mittig
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

            // COM-Port aus INI laden
            string iniCom = IniHelper.ReadValue("NV200/1", "ComPort", iniPath);
            LoadComPorts();
            EnsureSelectedPort(_ssp?.ComPort ?? iniCom, initial: true);
            // NEU: Button "Payout St�ckelung" als Feld anlegen und Click-Handler zuweisen
            btnPayoutStueckelung = new Button
            {
                Text = "Payout St�ckelung",
                Location = new Point(850, yStart + 610),
                Size = new Size(220, 32),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnPayoutStueckelung.Click += (s, e) => ToggleMaxConfigPanel();
            Controls.Add(btnPayoutStueckelung);

            // NEU: Checkbox f�r manuelles Routing � deaktiviert
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

            // Sicherstellen, dass Checkbox vor dem Log liegt (anklickbar)
            _chkRouteAllPayout.BringToFront();
            txtLog.SendToBack();
        }

        // Alt: ShowMaxConfigPanel() -> Neu: ToggleMaxConfigPanel()
        private void ToggleMaxConfigPanel()
        {
            try
            {
                InitMaxConfigUi(); // aus Partial
                if (_panelMaxConfig == null) return;

                bool newVisible = !_panelMaxConfig.Visible;
                _panelMaxConfig.Visible = newVisible;

                if (newVisible)
                {
                    _panelMaxConfig.BringToFront();
                    ScrollControlIntoView(_panelMaxConfig);
                    btnPayoutStueckelung.Text = "Payout St�ckelung ausblenden";
                }
                else
                {
                    btnPayoutStueckelung.Text = "Payout St�ckelung";
                }
            }
            catch (Exception ex)
            {
                AppendLog("MaxConfig konnte nicht ge�ffnet/geschlossen werden: " + ex.Message);
            }
        }

        private void RefreshBestand()
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
            _lblCashboxSumNew.Text = sumCbox.ToString("N2", de) + " �";
            if (_lblGesamtSum != null) _lblGesamtSum.Text = "Gesamt: " + (sumEuro + sumCbox).ToString("N2", de) + " �";
        }

        // --- Fehlende Hilfsmethoden (stubs / einfache Implementierungen) ---
        private void LoadComPorts(bool passive = false)
        {
            try
            {
                var prev = _lastPorts ?? Array.Empty<string>();
                var current = txtComPort.SelectedItem as string;
                var ports = SerialPort.GetPortNames();
                Array.Sort(ports, StringComparer.OrdinalIgnoreCase);
                _lastPorts = ports;
                var newPorts = ports.Where(p => Array.IndexOf(prev, p) < 0).ToList();
                txtComPort.Items.Clear();
                txtComPort.Items.AddRange(ports);
                if (_detectingPort && _detectBasePorts != null)
                {
                    var added = ports.Where(p => Array.IndexOf(_detectBasePorts, p) < 0).ToList();
                    if (added.Count > 0) { SelectDetectedPort(added[0]); return; }
                }
                else if (!passive)
                {
                    if (string.IsNullOrWhiteSpace(current) && newPorts.Count > 0) txtComPort.SelectedItem = newPorts[0];
                    else if (!string.IsNullOrWhiteSpace(current))
                    {
                        int idx = Array.IndexOf(ports, current);
                        txtComPort.SelectedIndex = idx >= 0 ? idx : -1;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(current))
                {
                    int idx = Array.IndexOf(ports, current);
                    txtComPort.SelectedIndex = idx >= 0 ? idx : -1;
                }
                if (txtComPort.Items.Count == 0) txtComPort.SelectedIndex = -1;
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

                // Dialog mit zweistufigem Ablauf: Trennen -> Weiter -> 4s Countdown -> Detection
                _detectDialog = new Form
                {
                    Text = "COM-Port Erkennung",
                    Size = new Size(460, 180),
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    StartPosition = FormStartPosition.CenterScreen,
                    ControlBox = false,
                    TopMost = true,
                    BackColor = Color.White
                };
                var lbl = new Label
                {
                    Text = "Ist das entsprechende Gerät getrennt?\r\nBitte Gerät JETZT trennen und danach auf \"Weiter\" klicken.",
                    AutoSize = false,
                    Location = new Point(12, 12),
                    Size = new Size(420, 56),
                    Font = new Font("Segoe UI Variable", 11F),
                    ForeColor = Color.FromArgb(33, 37, 41)
                };
                _detectDialog.Controls.Add(lbl);
                var lblCountdown = new Label
                {
                    Text = string.Empty,
                    AutoSize = false,
                    Location = new Point(12, 72),
                    Size = new Size(420, 24),
                    ForeColor = Color.DimGray,
                    Font = new Font("Segoe UI Variable", 10F)
                };
                _detectDialog.Controls.Add(lblCountdown);
                var btnContinue = new Button { Text = "Weiter", Location = new Point(260, 110), Size = new Size(90, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(33,150,243), ForeColor = Color.White };
                btnContinue.FlatAppearance.BorderSize = 0;
                var btnCancel = new Button { Text = "Abbrechen", Location = new Point(356, 110), Size = new Size(90, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(245,247,250), ForeColor = Color.FromArgb(33,37,41) };
                btnCancel.FlatAppearance.BorderSize = 0;
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
                                try { lblCountdown.Text = $"Countdown: {countdown} s"; } catch { }
                            }
                            else
                            {
                                try { preTimer.Stop(); preTimer.Dispose(); } catch { }
                                // Jetzt Basisliste aufnehmen und Detection starten
                                try
                                {
                                    _detectBasePorts = SerialPort.GetPortNames();
                                    Array.Sort(_detectBasePorts, StringComparer.OrdinalIgnoreCase);
                                }
                                catch { _detectBasePorts = Array.Empty<string>(); }
                                lbl.Text = "Jetzt bitte das Gerät einstecken.\r\nFenster schließt automatisch bei Erkennung.";
                                lblCountdown.Text = string.Empty;
                                // Start Polling Timer
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
        private void SelectDetectedPort(string port)
        {
            try
            {
                if (!txtComPort.Items.Cast<object>().Any(o => string.Equals(Convert.ToString(o), port, StringComparison.OrdinalIgnoreCase)))
                {
                    txtComPort.Items.Add(port);
                }
                txtComPort.SelectedItem = port;
                try { IniHelper.WriteValue("NV200/1", "ComPort", port, iniPath); } catch { }
                try { if (_ssp != null) _ssp.ComPort = port; } catch { }
                try { AppendLog("COM-Port erkannt und übernommen: " + port); } catch { }
            }
            catch { }
            StopDetectPortMode(false);
        }
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
        private void btnVerbinden_Click(object sender, EventArgs e)
        {
            if (_ssp == null) { AppendLog("Keine NV200-Instanz verf�gbar."); return; }
            if (_eventsAttached) { DetachSspEvents(); AppendLog("Events gel�st."); } else { AttachSspEvents(); AppendLog("Events angeh�ngt."); }
            UpdateStatusUi();
        }
        private void BtnComPortSpeichern_Click(object sender, EventArgs e)
        {
            var sel = txtComPort.SelectedItem as string; if (string.IsNullOrWhiteSpace(sel)) { MessageBox.Show("Kein Port ausgew�hlt."); return; }
            IniHelper.WriteValue("NV200/1", "ComPort", sel, iniPath); AppendLog("COM-Port gespeichert: " + sel); MessageBox.Show("COM-Port gespeichert.");
        }
        private void BtnEnableToggle_Click(object sender, EventArgs e)
        {
            if (_ssp == null) { AppendLog("Keine NV200-Instanz verf�gbar."); return; }
            _enabledRequested = !_enabledRequested;
            try
            {
                if (_enabledRequested) { _ssp.Freigeben(); _ssp.ConfigureBezel(0,255,0); AppendLog("NV200 freigegeben."); }
                else { _ssp.Disable_Device(); _ssp.SetInhibit(true); _ssp.ConfigureBezel(255,0,0); AppendLog("NV200 gesperrt."); }
            }
            catch (Exception ex) { AppendLog("Fehler EnableToggle: " + ex.Message); }
            UpdateEnableButtonVisual(); UpdateStatusUi();
        }
        private void UpdateEnableButtonVisual()
        {
            if (btnEnableToggle == null) return; btnEnableToggle.Text = _enabledRequested ? "Sperren" : "Freigeben"; btnEnableToggle.BackColor = _enabledRequested ? Color.FromArgb(183,28,28) : Color.FromArgb(46,125,50); btnEnableToggle.ForeColor = Color.White;
        }
        private void ApplyDisabledState()
        {
            if (DeviceDisabled)
            {
                try { _tmrBestand?.Stop(); } catch { }
                try { _ssp?.Disable_Device(); _ssp?.SetInhibit(true); _ssp?.ConfigureBezel(255,0,0); } catch { }
                try { DetachSspEvents(); } catch { }
            }
            else
            {
                try { StartBestandTimer(true); } catch { }
                try { AttachSspEvents(); } catch { }
                try { _ssp?.SetInhibit(false); } catch { }
            }
        }
        private void AdminOverviewFormRefreshSafe()
        {
            try
            {
                foreach (Form f in Application.OpenForms) if (f is AdminOverviewForm ov) { var mi = typeof(AdminOverviewForm).GetMethod("UpdateDeviceStatus", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public); mi?.Invoke(ov,null); break; }
            }
            catch { }
        }
        private void UpdateStatusUi()
        {
            try
            {
                if (DeviceDisabled)
                {
                    _lblConn.Text = "Deaktiviert"; _lblConn.ForeColor = Color.Gray; _lblPort.Text = "-"; _lblLastEvt.Text = "-"; _lblState.Text = "disabled"; _lblState.ForeColor = Color.Gray; return;
                }
                bool commAlive = false; if (_ssp != null) { var diff = DateTime.Now - _ssp.Last_Communicationtime; commAlive = diff.TotalSeconds < 3.0; }
                _lblConn.Text = commAlive ? "Verbunden" : "Keine Aktivit�t"; _lblConn.ForeColor = commAlive ? Color.FromArgb(0,128,0) : Color.FromArgb(183,28,28);
                _lblPort.Text = $"{(_ssp?.ComPort ?? "-")}, Addr {(_ssp!=null ? _ssp.SSPAdress.ToString() : "-")}"; EnsureSelectedPort(_ssp?.ComPort,false);
                if (_lastEvtUtc == DateTime.MinValue) _lblLastEvt.Text = "�"; else { var ago = DateTime.UtcNow - _lastEvtUtc; _lblLastEvt.Text = $"{_lastEvtUtc.ToLocalTime():HH:mm:ss} ({Math.Max(0,(int)ago.TotalSeconds)} s)"; }
                var s = _tempStateText ?? (_ssp?.states ?? "-"); _lblState.Text = s; var t = (s??"").ToLowerInvariant(); Color c = Color.SteelBlue; if (t.Contains("idle")||t.Contains("bereit")||t.Contains("started")||t.Contains("connected")||t.Contains("synchron")) c=Color.FromArgb(0,128,0); else if (t.Contains("dispens")||t.Contains("stack")||t.Contains("reading")||t.Contains("read note")) c=Color.FromArgb(255,140,0); else if (t.Contains("disabled")||t.Contains("jammed")||t.Contains("halted")||t.Contains("failed")||t.Contains("timeout")||t.Contains("open sspcomport")||t.Contains("neustart")) c=Color.FromArgb(183,28,28); _lblState.ForeColor = c;
            }
            catch { }
        }
        private void btnSenden_Click(object sender, EventArgs e)
        {
            if (_ssp == null) { AppendLog("Nicht verbunden."); return; }
            string hex = (txtSendHex?.Text ?? "").Trim().Replace(" ", ""); if (hex.Length==0){ AppendLog("Kein Hex-String."); return; }
            try
            {
                if (hex.Length %2!=0) throw new FormatException("Ungerade Hex-L�nge");
                byte[] data = new byte[hex.Length/2]; for(int i=0;i<data.Length;i++) data[i]=Convert.ToByte(hex.Substring(i*2,2),16);
                _ssp.SendRaw(data); AppendLog("TX: "+ NV200SerialPort.ToHex(data));
            }
            catch(Exception ex){ AppendLog("Fehler beim Senden: "+ex.Message); }
        }
        private void chkAutoSenden_CheckedChanged(object sender, EventArgs e){ if (tmrAutoSend!=null && chkAutoSenden!=null){ tmrAutoSend.Enabled = chkAutoSenden.Checked; AppendLog("AutoSenden: "+(chkAutoSenden.Checked?"aktiv":"inaktiv")); }}
        private void tmrAutoSend_Tick(object sender, EventArgs e){ btnSenden_Click(sender,e); }
        private void StartBestandTimer(bool start){ if (_tmrBestand==null) return; _tmrBestand.Enabled = start; if (start) RefreshBestand(); }
        private void AttachSspEvents(){ if (_ssp==null||_eventsAttached) return; _ssp.Ereignis += SspOnEreignis; _eventsAttached = true; if (btnVerbinden!=null) btnVerbinden.Text="Events l�sen"; }
        private void DetachSspEvents(){ if (_ssp==null||!_eventsAttached) return; try{ _ssp.Ereignis -= SspOnEreignis; }catch{} _eventsAttached=false; if (btnVerbinden!=null) btnVerbinden.Text="Events anh�ngen"; }
        private void SspOnEreignis(string x){ _lastEvtUtc = DateTime.UtcNow; AppendLog(x); }
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
            }
            catch { }
        }

        private void DisableRouteCheckboxUi()
        {
            if (_chkRouteAllPayout != null)
            {
                _chkRouteAllPayout.Enabled = false;
                _chkRouteAllPayout.Text = "Manuelles Routing deaktiviert (automatisch)";
                _chkRouteAllPayout.CheckState = CheckState.Indeterminate;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try { LoadInitialNvLog(); } catch { }
            try { _ssp?.StartLiveFrameLogging(); AppendLog("Live-Frame-Logging aktiv (Form ge�ffnet)"); } catch { }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { _ssp?.EndLiveFrameLogging(); AppendLog("Live-Frame-Logging beendet (Form geschlossen)"); } catch { }
            base.OnFormClosed(e);
        }

        private void LoadInitialNvLog(int maxLines = 1000)
        {
            if (_ssp == null || txtLog == null) return;
            var lines = _ssp.GetFullLogSnapshot(maxLines, true);
            if (lines == null || lines.Count == 0) return;
            var sb = new StringBuilder();
            foreach (var raw in lines)
            {
                // Erwartetes Format: ID-DateTime-Message (ID ohne Bindestriche, DateTime dd.MM.yyyy HH:mm:ss, Message kann Bindestriche enthalten)
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
    }
}