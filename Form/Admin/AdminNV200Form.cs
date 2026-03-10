using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.IO.Ports; // NEU f?r COM-Port Auflistung
using System.Globalization; // NEU f?r Formatierung
using TaMi_Einzahlautomat.UI.Layout;

namespace TaMi_Einzahlautomat
{
    public partial class AdminNV200Form : Form
    {
        private NV200_SSP _ssp;
        private ModernHeaderPanel _header;

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
        private string iniPath = @"C:\ProgramData\SuE-Software\SuE-TaMi Client SQL\Einzahlautomat.ini";

        // NEU: Statusanzeige
        private Panel _panelStatus;
        private Label _lblConn;
        private Label _lblPort;
        private Label _lblLastEvt;
        private Label _lblState;
        private Label _lblCashboxTypeStatus;
        private Timer _tmrStatus;
        private DateTime _lastEvtUtc = DateTime.MinValue;

        // NEU: Enable/Disable-Toggle
        private Button btnEnableToggle;
        private bool _enabledRequested = false;

        // Event-Verwaltung / Besitz der Instanz
        private bool _eventsAttached = false;

        private ComboBox cboCashboxType;

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

            _header = new ModernHeaderPanel { Title = "Admin - NV200 Steuerung" };
            _header.CloseClicked += () => { try { Close(); } catch { } };
            Controls.Add(_header);
            _header.BringToFront();
            try { _header.ApplyRoundedRegionToForm(this); } catch { }

            // Restliches Layout
            BuildModernLayout();

            // Status-Timer
            _tmrStatus = new Timer { Interval = 1000 };
            _tmrStatus.Tick += (s, e) => UpdateStatusUi();
            _tmrStatus.Enabled = true;

            // Bestand-Timer
            StartBestandTimer(true);

            // Events aus globaler Session anhängen (ohne Start/Stop)
            AttachSspEvents();

            // Anfangswerte setzen
            txtComPort.Text = _ssp?.ComPort ?? "";
            try { if (_lblCashboxTypeStatus != null && cboCashboxType != null) _lblCashboxTypeStatus.Text = cboCashboxType.Text; } catch { }
            UpdateEnableButtonVisual();
            UpdateStatusUi();
            RefreshBestand();
            try { DisableRouteCheckboxUi(); } catch { }
            EnsureSelectedPort(_ssp?.ComPort, initial: true); // nur initial
            ApplyDisabledState();
        }

        // Header wird zentral über `ModernHeaderPanel` gezeichnet.

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

        private void BuildModernLayout()
        {
            int yStart = 70; // Abstand unterhalb des Headers

            // --- Verbindungsbereich (nur Events anhängen/lösen, keine zweite Session) ---
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
            try
            {
                txtComPort.FlatStyle = FlatStyle.Flat;
                txtComPort.BackColor = Color.FromArgb(245, 247, 250);
                txtComPort.ForeColor = Color.FromArgb(33, 37, 41);
            }
            catch { }
            txtComPort.DropDown += (s,e)=> LoadComPorts(true); // passiver Refresh
            Controls.Add(txtComPort);

            _btnDetectPort = new ModernGradientButton
            {
                Text = "Erkennung",
                Location = new Point(225, yStart - 4),
                Size = new Size(90, 24),
                GradientStart = UiTheme.SecondaryStart,
                GradientEnd = UiTheme.SecondaryEnd
            };
            _btnDetectPort.Click += (s,e)=> StartDetectPortMode();
            Controls.Add(_btnDetectPort);

            btnVerbinden = new ModernGradientButton
            {
                Location = new Point(320, yStart - 4),
                Size = new Size(140, 24),
                Text = "Events anhängen",
                GradientStart = UiTheme.PrimaryStart,
                GradientEnd = UiTheme.PrimaryEnd
            };
            btnVerbinden.Click += btnVerbinden_Click;
            Controls.Add(btnVerbinden);

            btnComPortSpeichern = new ModernGradientButton
            {
                Location = new Point(465, yStart - 4),
                Size = new Size(100, 24),
                Text = "Speichern",
                GradientStart = UiTheme.SecondaryStart,
                GradientEnd = UiTheme.SecondaryEnd
            };
            btnComPortSpeichern.Click += BtnComPortSpeichern_Click;
            Controls.Add(btnComPortSpeichern);

            // Bestands-Panel muss existieren bevor Controls hinzugefügt werden
            _panelBestand = new Panel
            {
                Location = new Point(850, yStart + 180),
                Size = new Size(380, 420),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                BackColor = Color.FromArgb(245, 247, 250)
            };
            Controls.Add(_panelBestand);

            cboCashboxType = new ComboBox
            {
                Location = new Point(140, 138),
                Size = new Size(220, 26),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            try
            {
                cboCashboxType.FlatStyle = FlatStyle.Flat;
                cboCashboxType.BackColor = Color.FromArgb(245, 247, 250);
                cboCashboxType.ForeColor = Color.FromArgb(33, 37, 41);
            }
            catch { }
            cboCashboxType.Items.AddRange(new object[] { "1000 Note", "500 Note" });
            try
            {
                string v = IniHelper.ReadValue("NV200/1", "CashboxType", iniPath);
                if (v != null && v.Trim() == "500") cboCashboxType.SelectedIndex = 1;
                else cboCashboxType.SelectedIndex = 0;
            }
            catch { cboCashboxType.SelectedIndex = 0; }
            cboCashboxType.SelectedIndexChanged += (s, e) =>
            {
                try
                {
                    string saveVal = cboCashboxType.SelectedIndex == 1 ? "500" : "1000";
                    IniHelper.WriteValue("NV200/1", "CashboxType", saveVal, iniPath);
                }
                catch { }
                try { if (_lblCashboxTypeStatus != null) _lblCashboxTypeStatus.Text = cboCashboxType.Text; } catch { }
            };
            // wird unten dem Status-Panel hinzugefügt

            // NEU: Freigeben/Sperren (analog AdminCoinForm)
            btnEnableToggle = new ModernGradientButton
            {
                Location = new Point(570, yStart - 4),
                Size = new Size(120, 24),
                GradientStart = UiTheme.SuccessStart,
                GradientEnd = UiTheme.SuccessEnd
            };
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
            try
            {
                _chkDisabled.ForeColor = Color.FromArgb(33, 37, 41);
                _chkDisabled.Font = new Font("Segoe UI Variable", 10F, FontStyle.Bold);
            }
            catch { }
            _chkDisabled.CheckedChanged += (s, e) =>
            {
                DeviceDisabled = _chkDisabled.Checked;
                try { IniHelper.WriteValue("NV200/1", "Disabled", DeviceDisabled ? "1" : "0", iniPath); } catch { }
                ApplyDisabledState();
                UpdateStatusUi();
                try { AdminOverviewFormRefreshSafe(); } catch { }
            };
            Controls.Add(_chkDisabled);

            // --- Sende-Bereich entfernt (Senden/Hex + Auto-Senden) ---

            // --- Log-Feld ---
            txtLog = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Location = new Point(24, yStart + 34),
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
                Size = new Size(380, 178),
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

            // Cashbox-Typ unter Zustand im Status-Panel
            var lCbt = new Label { Text = "Cashbox-Typ:", Location = new Point(20, 142), AutoSize = true, Font = new Font("Segoe UI Variable", 11F) };
            _panelStatus.Controls.Add(lCbt);
            _lblCashboxTypeStatus = new Label
            {
                Text = "-",
                Location = new Point(140, 140),
                AutoSize = true,
                Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold)
            };
            _panelStatus.Controls.Add(_lblCashboxTypeStatus);

            // ComboBox optisch passend im Status-Panel platzieren
            cboCashboxType.Location = new Point(140, 138);
            cboCashboxType.Size = new Size(220, 26);
            _panelStatus.Controls.Add(cboCashboxType);

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
                    Text = $"{werte[i],3} €:",
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
                Text = "0,00 €",
                Location = new Point(110, baseSumY),
                Size = new Size(110, 24),
                Font = new Font("Segoe UI Variable", 13F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            };
            _panelBestand.Controls.Add(_lblBestandSum);

            // Cashbox Summe
            _lblCashboxSumNew = new Label
            {
                Text = "0,00 €",
                Location = new Point(250, baseSumY),
                Size = new Size(130, 24),
                Font = new Font("Segoe UI Variable", 13F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            };
            _panelBestand.Controls.Add(_lblCashboxSumNew);

            // Gesamt (zentriert zwischen beiden Spalten)
            _lblGesamtSum = new Label
            {
                Text = "Gesamt: 0,00 €",
                Location = new Point((_panelBestand.Width - 200) / 2, baseSumY + 30),
                Size = new Size(200, 24),
                Font = new Font("Segoe UI Variable", 12.5F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            };
            _panelBestand.Controls.Add(_lblGesamtSum);

            // Aktualisieren-Button eine Zeile tiefer und mittig
            _btnBestandRefresh = new ModernGradientButton
            {
                Text = "Aktualisieren",
                Size = new Size(140, 32),
                Location = new Point((_panelBestand.Width - 140) / 2, baseSumY + 60),
                Anchor = AnchorStyles.Top,
                GradientStart = UiTheme.PrimaryStart,
                GradientEnd = UiTheme.PrimaryEnd
            };
            _panelBestand.Controls.Add(_btnBestandRefresh);
            _btnBestandRefresh.Click += (s, e) => RefreshBestand();

            _tmrBestand = new Timer { Interval = 3000 };
            _tmrBestand.Tick += (s, e) => RefreshBestand();

            // COM-Port aus INI laden
            string iniCom = IniHelper.ReadValue("NV200/1", "ComPort", iniPath);
            LoadComPorts();
            EnsureSelectedPort(_ssp?.ComPort ?? iniCom, initial: true);
            // NEU: Button "Payout Stückelung" als Feld anlegen und Click-Handler zuweisen
            btnPayoutStueckelung = new ModernGradientButton
            {
                Text = "Payout Stückelung",
                Location = new Point(850, yStart + 610),
                Size = new Size(220, 32),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                GradientStart = UiTheme.SecondaryStart,
                GradientEnd = UiTheme.SecondaryEnd
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
                    btnPayoutStueckelung.Text = "Payout Stückelung ausblenden";
                }
                else
                {
                    btnPayoutStueckelung.Text = "Payout Stückelung";
                }
            }
            catch (Exception ex)
            {
                AppendLog("MaxConfig konnte nicht geöffnet/geschlossen werden: " + ex.Message);
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
            _lblBestandSum.Text = sumEuro.ToString("N2", de) + " €";
            int[] cbox = { _ssp.Cashbox_5_euro, _ssp.Cashbox_10_euro, _ssp.Cashbox_20_euro, _ssp.Cashbox_50_euro, _ssp.Cashbox_100_euro, _ssp.Cashbox_200_euro, _ssp.Cashbox_500_euro };
            for (int i = 0; i < cbox.Length && i < _lblCashboxAnz.Length; i++) _lblCashboxAnz[i].Text = cbox[i].ToString();
            decimal sumCbox = 5m*cbox[0] + 10m*cbox[1] + 20m*cbox[2] + 50m*cbox[3] + 100m*cbox[4] + 200m*cbox[5] + 500m*cbox[6];
            _lblCashboxSumNew.Text = sumCbox.ToString("N2", de) + " €";
            if (_lblGesamtSum != null) _lblGesamtSum.Text = "Gesamt: " + (sumEuro + sumCbox).ToString("N2", de) + " €";
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
            if (_ssp == null) { AppendLog("Keine NV200-Instanz verfügbar."); return; }
            if (_eventsAttached) { DetachSspEvents(); AppendLog("Events gelöst."); } else { AttachSspEvents(); AppendLog("Events angehängt."); }
            UpdateStatusUi();
        }
        private void BtnComPortSpeichern_Click(object sender, EventArgs e)
        {
            var sel = txtComPort.SelectedItem as string; if (string.IsNullOrWhiteSpace(sel)) { MessageBox.Show("Kein Port ausgewählt."); return; }
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
            if (btnEnableToggle == null) return;
            btnEnableToggle.Text = _enabledRequested ? "Sperren" : "Freigeben";

            var mgb = btnEnableToggle as ModernGradientButton;
            if (mgb != null)
            {
                if (_enabledRequested)
                {
                    mgb.GradientStart = UiTheme.DangerStart;
                    mgb.GradientEnd = UiTheme.DangerEnd;
                }
                else
                {
                    mgb.GradientStart = UiTheme.SuccessStart;
                    mgb.GradientEnd = UiTheme.SuccessEnd;
                }
                try { mgb.Invalidate(); } catch { }
            }
            else
            {
                btnEnableToggle.BackColor = _enabledRequested ? Color.FromArgb(183, 28, 28) : Color.FromArgb(46, 125, 50);
                btnEnableToggle.ForeColor = Color.White;
            }
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
                _lblConn.Text = commAlive ? "Verbunden" : "Keine Aktivität"; _lblConn.ForeColor = commAlive ? Color.FromArgb(0,128,0) : Color.FromArgb(183,28,28);
                _lblPort.Text = $"{(_ssp?.ComPort ?? "-")}, Addr {(_ssp!=null ? _ssp.SSPAdress.ToString() : "-")}"; EnsureSelectedPort(_ssp?.ComPort,false);
                if (_lastEvtUtc == DateTime.MinValue) _lblLastEvt.Text = "-"; else { var ago = DateTime.UtcNow - _lastEvtUtc; _lblLastEvt.Text = $"{_lastEvtUtc.ToLocalTime():HH:mm:ss} ({Math.Max(0,(int)ago.TotalSeconds)} s)"; }
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
        private void AttachSspEvents()
        {
            if (_ssp == null || _eventsAttached) return;
            _ssp.Ereignis += SspOnEreignis;
            _eventsAttached = true;
            if (btnVerbinden != null)
            {
                btnVerbinden.Text = "Events lösen";
                var b = btnVerbinden as ModernGradientButton;
                if (b != null)
                {
                    b.GradientStart = UiTheme.DangerStart;
                    b.GradientEnd = UiTheme.DangerEnd;
                    try { b.Invalidate(); } catch { }
                }
            }
        }

        private void DetachSspEvents()
        {
            if (_ssp == null || !_eventsAttached) return;
            try { _ssp.Ereignis -= SspOnEreignis; } catch { }
            _eventsAttached = false;
            if (btnVerbinden != null)
            {
                btnVerbinden.Text = "Events anhängen";
                var b = btnVerbinden as ModernGradientButton;
                if (b != null)
                {
                    b.GradientStart = UiTheme.PrimaryStart;
                    b.GradientEnd = UiTheme.PrimaryEnd;
                    try { b.Invalidate(); } catch { }
                }
            }
        }
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
            try { _ssp?.StartLiveFrameLogging(); AppendLog("Live-Frame-Logging aktiv (Form geöffnet)"); } catch { }
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