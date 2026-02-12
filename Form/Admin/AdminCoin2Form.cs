using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Drawing.Drawing2D;
using TaMi_Einzahlautomat.Coins;
using System.IO.Ports; // NEU Ports
using System.Linq; // NEU
using TaMi_Einzahlautomat.UI.Layout;

namespace TaMi_Einzahlautomat
{
    public class AdminCoin2Form : Form
    {
        private ModernHeaderPanel _header;

        private ComboBox cmbType;
        private ComboBox txtCom; // statt TextBox
        private string[] _lastPorts; // NEU
        private Button btnDetectPort; // NEU: automatische Erkennung
        private bool _detectingPort; // NEU
        private string[] _detectBasePorts; // Basisliste
        private Form _detectDialog; // Hinweisfenster
        private Timer _detectTimer; // Polling
        private NumericUpDown nudAddr;
        private Button btnSaveIni;
        private Button btnConnectToggle;
        private Button btnEnableToggle;
        private Button btnQueryLevels;
        // btnReloadPorts entf�llt
        private TextBox txtLog;

        private ICoinValidator _coin;
        private readonly string _iniPath = @"C:\\ProgramData\\SuE-Software\\SuE-TaMi Client SQL\\Einzahlautomat.ini";
        private const string Section = "SmartCoin/2";

        private bool _enabledRequested = false;

        private Panel _panelBestand;
        private Label[] _lblBestandAnzCoins = new Label[8];
        private Label _lblBestandSumCoins;
        private Button _btnBestandRefresh;
        private Timer _tmrBestand;

        private Panel _panelStatus;
        private Label _lblScConn;
        private Label _lblScPortAddr;
        private Label _lblScLastEvt;
        private Label _lblScState;
        private Timer _tmrStatus;
        private DateTime _lastCoinEventUtc = DateTime.MinValue;

        private bool _eventsAttached = false;
        private string _statusText = null;
        private bool _didFirstReadyRefresh = false;

        // NEU: Deaktivieren-Checkbox + globaler Status
        private CheckBox _chkDisabled;
        public static bool DeviceDisabled = false;

        public AdminCoin2Form(ICoinValidator coin)
        {
            _coin = Coin2Manager.Instance ?? coin ?? CreateFromIniSection(_iniPath, Section);
            // load persisted disabled flag
            try
            {
                var dv = IniHelper.ReadValue("SmartCoin/2", "Disabled", _iniPath);
                if (!string.IsNullOrWhiteSpace(dv)) DeviceDisabled = dv.Trim().Equals("1", StringComparison.OrdinalIgnoreCase) || dv.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
            }
            catch { }
            InitializeUi();
            LoadIni();

            try { AttachCoinEvents(); } catch { }
            UpdateConnectButtonVisual();
        }

        private ICoinValidator CreateFromIniSection(string iniPath, string section)
        {
            try
            {
                var typeStr = IniHelper.ReadValue(section, "Typ", iniPath)?.Trim() ?? "None";
                var com = IniHelper.ReadValue(section, "ComPort", iniPath)?.Trim() ?? "";
                var addrStr = IniHelper.ReadValue(section, "SSPAddress", iniPath)?.Trim();

                var type = CoinValidatorFactory.ParseType(typeStr);
                var inst = CoinValidatorFactory.Create(type);
                if (inst != null)
                {
                    if (!string.IsNullOrWhiteSpace(com)) inst.ComPort = com;
                    if (int.TryParse(addrStr, out var addr) && addr > 0) inst.SspAddress = addr;
                }
                return inst;
            }
            catch { return null; }
        }

        private static bool IsReadyStatus(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            var t = s.ToLowerInvariant();
            return t.Contains("handshake ok") || t.Contains("idle") || t.Contains("bereit") || t.Contains("synchron") || t.Contains("verbunden");
        }

        private void InitializeUi()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(900, 760);
            BackColor = Color.White;
            DoubleBuffered = true;

            _header = new ModernHeaderPanel { Title = "Admin - Münzprüfer/2" };
            _header.CloseClicked += () => { try { Close(); } catch { } };
            Controls.Add(_header);
            _header.BringToFront();
            try { _header.ApplyRoundedRegionToForm(this); } catch { }

            var lblType = new Label { Text = "Version:", Location = new Point(24, 80), Size = new Size(120, 36), Font = new Font("Segoe UI Variable", 12F) };
            cmbType = new ComboBox { Location = new Point(150, 80), Size = new Size(220, 36), Font = new Font("Segoe UI Variable", 12F), DropDownStyle = ComboBoxStyle.DropDownList };
            try { cmbType.FlatStyle = FlatStyle.Flat; cmbType.BackColor = Color.FromArgb(245, 247, 250); cmbType.ForeColor = Color.FromArgb(33, 37, 41); } catch { }
            cmbType.Items.AddRange(new object[] { "None", "SmartCoinV1" });

            var lblCom = new Label { Text = "ComPort:", Location = new Point(24, 130), Size = new Size(120, 36), Font = new Font("Segoe UI Variable", 12F) };
            txtCom = new ComboBox { Location = new Point(150, 130), Size = new Size(180, 36), Font = new Font("Segoe UI Variable", 12F), DropDownStyle = ComboBoxStyle.DropDownList };
            txtCom.DropDown += (s,e)=> LoadComPorts(true); // passiver Refresh
            try { txtCom.FlatStyle = FlatStyle.Flat; txtCom.BackColor = Color.FromArgb(245, 247, 250); txtCom.ForeColor = Color.FromArgb(33, 37, 41); } catch { }
            btnDetectPort = new ModernGradientButton { Text = "Erkennung", Location = new Point(334,130), Size = new Size(100,36), GradientStart = UiTheme.SecondaryStart, GradientEnd = UiTheme.SecondaryEnd };
            btnDetectPort.Click += (s,e)=> StartDetectPortMode();

            var lblAddr = new Label { Text = "SSP Address:", Location = new Point(24, 180), Size = new Size(120, 36), Font = new Font("Segoe UI Variable", 12F) };
            nudAddr = new NumericUpDown { Location = new Point(150, 180), Size = new Size(220, 36), Font = new Font("Segoe UI Variable", 12F), Minimum = 1, Maximum = 255, Value = 16 };

            btnSaveIni = new ModernGradientButton { Text = "Speichern", Location = new Point(24, 230), Size = new Size(120, 44), GradientStart = UiTheme.SecondaryStart, GradientEnd = UiTheme.SecondaryEnd };
            btnSaveIni.Click += (s, e) => SaveIni();

            btnConnectToggle = new ModernGradientButton
            {
                Text = "Events anhängen",
                Location = new Point(160, 230),
                Size = new Size(170, 44),
                GradientStart = UiTheme.PrimaryStart,
                GradientEnd = UiTheme.PrimaryEnd
            };
            btnConnectToggle.Click += btnConnectToggle_Click;
            Controls.Add(btnConnectToggle);

            btnEnableToggle = new ModernGradientButton { Text = "Enable", Location = new Point(340, 230), Size = new Size(160, 44), GradientStart = UiTheme.SuccessStart, GradientEnd = UiTheme.SuccessEnd };
            btnEnableToggle.Click += (s, e) =>
            {
                if (_coin == null) return;
                if (!_coin.Connected) { AppendLog("Hinweis: erst verbinden."); return; }

                _enabledRequested = !_enabledRequested;
                try { _coin.Enable(_enabledRequested); } catch { }

                AppendLog(_enabledRequested ? "Enable angefordert." : "Disable angefordert.");
                UpdateEnableButtonVisual();
            };

            btnQueryLevels = new ModernGradientButton { Text = "Bestand abfragen", Location = new Point(520, 230), Size = new Size(180, 44), GradientStart = UiTheme.PrimaryStart, GradientEnd = UiTheme.PrimaryEnd };
            btnQueryLevels.Click += (s, e) => { QueryLevels(); RefreshBestandCoins(); };

            txtLog = new TextBox { Multiline = true, ScrollBars = ScrollBars.Both, ReadOnly = true, Location = new Point(24, 290), Size = new Size(520, 380), Font = new Font("Consolas", 11F) };

            Controls.Add(lblType);
            Controls.Add(cmbType);
            Controls.Add(lblCom);
            Controls.Add(txtCom);
            Controls.Add(btnDetectPort);
            Controls.Add(lblAddr);
            Controls.Add(nudAddr);
            Controls.Add(btnSaveIni);
            Controls.Add(btnConnectToggle);
            Controls.Add(btnEnableToggle);
            Controls.Add(btnQueryLevels);
            Controls.Add(txtLog);

            // NEU: Deaktiviert-Checkbox
            _chkDisabled = new CheckBox
            {
                Text = "Deaktiviert",
                Location = new Point(380, 86), // neben cmbType (150,80, width 220)
                AutoSize = true,
                Checked = DeviceDisabled
            };
            try { _chkDisabled.ForeColor = Color.FromArgb(33, 37, 41); _chkDisabled.Font = new Font("Segoe UI Variable", 10F, FontStyle.Bold); } catch { }
            _chkDisabled.CheckedChanged += (s, e) =>
            {
                DeviceDisabled = _chkDisabled.Checked;
                try { IniHelper.WriteValue("SmartCoin/2", "Disabled", DeviceDisabled ? "1" : "0", _iniPath); } catch { }
                ApplyDisabledState();
                UpdateStatusUi();
                try { AdminOverviewFormRefreshSafe(); } catch { }
            };
            Controls.Add(_chkDisabled);

            _panelStatus = new Panel
            {
                Location = new Point(560, 80),
                Size = new Size(300, 170),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                BackColor = Color.FromArgb(245, 247, 250)
            };
            Controls.Add(_panelStatus);

            var lblStatusTitle = new Label { Text = "Status SmartCoin/2", Location = new Point(10, 10), AutoSize = true, Font = new Font("Segoe UI Variable", 13F, FontStyle.Bold) };
            _panelStatus.Controls.Add(lblStatusTitle);

            var lblConn = new Label { Text = "Verbindung:", Location = new Point(20, 44), AutoSize = true, Font = new Font("Segoe UI Variable", 11F) };
            _panelStatus.Controls.Add(lblConn);
            _lblScConn = new Label { Text = "-", Location = new Point(140, 42), AutoSize = true, Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold) };
            _panelStatus.Controls.Add(_lblScConn);

            var lblPortAddr = new Label { Text = "Port/Addr:", Location = new Point(20, 70), AutoSize = true, Font = new Font("Segoe UI Variable", 11F) };
            _panelStatus.Controls.Add(lblPortAddr);
            _lblScPortAddr = new Label { Text = "-", Location = new Point(140, 68), AutoSize = true, Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold) };
            _panelStatus.Controls.Add(_lblScPortAddr);

            _lblScLastEvt = new Label { Text = "-", Location = new Point(20, 96), AutoSize = true, Font = new Font("Segoe UI Variable", 10F) };
            _panelStatus.Controls.Add(_lblScLastEvt);

            var lblState = new Label { Text = "Zustand:", Location = new Point(20, 118), AutoSize = true, Font = new Font("Segoe UI Variable", 11F) };
            _panelStatus.Controls.Add(lblState);
            _lblScState = new Label { Text = "-", Location = new Point(140, 116), AutoSize = true, Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold) };
            _panelStatus.Controls.Add(_lblScState);

            _panelBestand = new Panel
            {
                Location = new Point(560, 290),
                Size = new Size(300, 460), // mehr H�he
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                BackColor = Color.FromArgb(245, 247, 250)
            };
            Controls.Add(_panelBestand);

            var lblTitel = new Label { Text = "Kassenbestand (Münzen, Stück):", Location = new Point(10, 10), AutoSize = true, Font = new Font("Segoe UI Variable", 13F, FontStyle.Bold) };
            _panelBestand.Controls.Add(lblTitel);

            string[] denomText = { "1 c", "2 c", "5 c", "10 c", "20 c", "50 c", "1 €", "2 €" };
            for (int i = 0; i < denomText.Length; i++)
            {
                var ldenom = new Label { Text = $"{denomText[i],4}:", Location = new Point(20, 50 + i * 38), AutoSize = true, Font = new Font("Segoe UI Variable", 12F) };
                _panelBestand.Controls.Add(ldenom);

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
                _lblBestandAnzCoins[i] = lval;
            }

            // Summe und Refresh unter die letzte M�nz-Zeile verschieben
            int lastRowY = 50 + (denomText.Length - 1) * 38; // y der 2� Zeile
            int sumY = lastRowY + 38; // eine Zeile darunter

            _lblBestandSumCoins = new Label { Text = "Gesamt: 0,00 €", Location = new Point(20, sumY), AutoSize = true, Font = new Font("Segoe UI Variable", 13F, FontStyle.Bold) };
            _panelBestand.Controls.Add(_lblBestandSumCoins);

            // Refresh-Button jetzt eine weitere Zeile unter der Summe platzieren
            _btnBestandRefresh = new ModernGradientButton { Text = "Aktualisieren", Location = new Point(20, sumY + 34), Size = new Size(110, 38), GradientStart = UiTheme.PrimaryStart, GradientEnd = UiTheme.PrimaryEnd, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
            _btnBestandRefresh.Click += (s, e) => RefreshBestandCoins();
            _panelBestand.Controls.Add(_btnBestandRefresh);

            _tmrBestand = new Timer { Interval = 3000 };
            _tmrBestand.Tick += (s, e) => RefreshBestandCoins();
            _tmrBestand.Enabled = true;

            _tmrStatus = new Timer { Interval = 1000 };
            _tmrStatus.Tick += (s, e) => UpdateStatusUi();
            _tmrStatus.Enabled = true;

            // 1) Ports zuerst laden
            LoadComPorts();
            // 2) Aktuellen Port aus Objekt oder INI selektieren (falls nicht vorhanden hinzuf�gen)
            SelectOrAddInitialPort();
            // 3) Adresse setzen, falls vorhanden
            if (_coin != null && _coin.SspAddress > 0) nudAddr.Value = _coin.SspAddress;

            UpdateEnableButtonVisual();
            UpdateStatusUi();
            ApplyDisabledState();
        }

        // Hilfsmethode: aktuellen Port ausw�hlen oder hinzuf�gen
        private void SelectOrAddInitialPort()
        {
            try
            {
                string initialPort = _coin?.ComPort;
                if (string.IsNullOrWhiteSpace(initialPort))
                    initialPort = IniHelper.ReadValue(Section, "ComPort", _iniPath);
                if (string.IsNullOrWhiteSpace(initialPort)) return;
                int idx = txtCom.Items.IndexOf(initialPort);
                if (idx >= 0)
                    txtCom.SelectedIndex = idx;
                else
                {
                    txtCom.Items.Add(initialPort);
                    txtCom.SelectedItem = initialPort;
                }
            }
            catch { }
        }

        private void LoadIni()
        {
            var typeStr = IniHelper.ReadValue(Section, "Typ", _iniPath) ?? "None";
            var com = IniHelper.ReadValue(Section, "ComPort", _iniPath) ?? "";
            var addr = IniHelper.ReadValue(Section, "SSPAddress", _iniPath);

            cmbType.SelectedItem = typeStr.Equals("V1", StringComparison.OrdinalIgnoreCase) ? "SmartCoinV1"
                                 : "None";
            // COM-Port Auswahl nur setzen, falls noch keine Selektion vorhanden (SelectOrAddInitialPort erledigt die Hauptarbeit)
            if (txtCom.SelectedIndex < 0 && !string.IsNullOrWhiteSpace(com))
            {
                int idx = txtCom.Items.IndexOf(com);
                if (idx >= 0) txtCom.SelectedIndex = idx; else { txtCom.Items.Add(com); txtCom.SelectedItem = com; }
            }
            if (int.TryParse(addr, out var a) && a > 0) nudAddr.Value = a;
        }

        private void SaveIni()
        {
            var sel = (cmbType.SelectedItem as string) ?? "None";
            IniHelper.WriteValue(Section, "Typ", sel == "SmartCoinV1" ? "V1" : "None", _iniPath);
            if (!string.IsNullOrWhiteSpace(txtCom.Text))
                IniHelper.WriteValue(Section, "ComPort", txtCom.Text.Trim(), _iniPath);
            IniHelper.WriteValue(Section, "SSPAddress", ((int)nudAddr.Value).ToString(), _iniPath);

            if (_coin != null)
            {
                _coin.ComPort = txtCom.Text?.Trim();
                _coin.SspAddress = (int)nudAddr.Value;
            }

            AppendLog($"INI gespeichert: Typ={sel}, COM={txtCom.Text}, Addr={nudAddr.Value}");
            MessageBox.Show(this, "Einstellungen gespeichert.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void QueryLevels()
        {
            if (DeviceDisabled) { AppendLog("Gerät deaktiviert - keine Abfrage."); return; }
            if (_coin == null) { AppendLog("Kein Münzprüfer-Objekt vorhanden."); return; }
            if (!_coin.Connected) { AppendLog("Hinweis: erst verbinden."); return; }
            if (!IsReadyStatus(_statusText))
            {
                var sc1 = _coin as SmartCoinV1;
                if (sc1 != null && !string.IsNullOrWhiteSpace(sc1.CurrentStatus))
                {
                    _statusText = sc1.CurrentStatus;
                }
                if (!IsReadyStatus(_statusText)) { AppendLog("Hinweis: wartet auf Handshake/Idle."); return; }
            }

            try
            {
                if (_coin is SmartCoinV1 sc1)
                {
                    AppendLog("GET_DENOMINATION_LEVEL anfordern (V1)...");
                    sc1.RequestCoinLevels();
                    var lv = sc1.GetCoinAvailability();
                    AppendLog("Lokale Level (Cache): " + FormatLevels(lv));
                }
                else
                {
                    AppendLog("Dieses Münzgerät unterstützt die Level-Abfrage hier nicht.");
                }
            }
            catch (Exception ex)
            {
                AppendLog("Fehler bei Level-Abfrage: " + ex.Message);
            }
        }

        private void RefreshBestandCoins()
        {
            try
            {
                if (DeviceDisabled) return;
                if (_coin == null || !_coin.Connected) return;
                if (!_eventsAttached) return;
                if (!IsReadyStatus(_statusText))
                {
                    var sc1 = _coin as SmartCoinV1;
                    if (sc1 != null && !string.IsNullOrWhiteSpace(sc1.CurrentStatus))
                    {
                        _statusText = sc1.CurrentStatus;
                    }
                    if (!IsReadyStatus(_statusText)) return;
                }

                int[] lv = null;
                if (_coin is SmartCoinV1 sc1a) { sc1a.RequestCoinLevels(); lv = sc1a.GetCoinAvailability(); }

                UpdateCoinsUiFromLevels(lv);
                _didFirstReadyRefresh = true;
            }
            catch { }
        }

        private void UpdateCoinsUiFromLevels(int[] lv)
        {
            decimal sumCent = 0m;
            for (int i = 0; i < 8; i++)
            {
                int v = (lv != null && i < lv.Length) ? lv[i] : -1;
                if (_lblBestandAnzCoins[i] != null)
                    _lblBestandAnzCoins[i].Text = v < 0 ? "?" : v.ToString();
                sumCent += Math.Max(0, v) * new[] { 1, 2, 5, 10, 20, 50, 100, 200 }[i];
            }
            if (_lblBestandSumCoins != null)
                _lblBestandSumCoins.Text = $"Gesamt: {(sumCent / 100m):C2}";
        }

        private string FormatLevels(int[] lv)
        {
            if (lv == null || lv.Length < 8) return "(keine Daten)";
            Func<int, string> Safe = i => lv[i] < 0 ? "?" : lv[i].ToString();
            return $"1c={Safe(0)}, 2c={Safe(1)}, 5c={Safe(2)}, 10c={Safe(3)}, 20c={Safe(4)}, 50c={Safe(5)}, 1€={Safe(6)}, 2€={Safe(7)}";
        }

        private void UpdateEnableButtonVisual()
        {
            btnEnableToggle.Text = _enabledRequested ? "Disable" : "Enable";
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
            }
        }

        // NEU: verf�gbare COM-Ports laden (mit Detection + passive Option)
        private void LoadComPorts(bool passive = false)
        {
            try
            {
                var prev = _lastPorts ?? Array.Empty<string>();
                var currentSelection = (txtCom.Text ?? string.Empty).Trim();
                var ports = SerialPort.GetPortNames();
                Array.Sort(ports, StringComparer.OrdinalIgnoreCase);
                _lastPorts = ports.ToArray();
                var newPorts = ports.Where(p => !prev.Contains(p, StringComparer.OrdinalIgnoreCase)).ToList();

                txtCom.Items.Clear();
                txtCom.Items.AddRange(ports);

                if (_detectingPort && _detectBasePorts != null)
                {
                    var added = ports.Where(p => !_detectBasePorts.Contains(p, StringComparer.OrdinalIgnoreCase)).ToList();
                    if (added.Count > 0)
                    {
                        SelectDetectedPort(added[0]);
                        return;
                    }
                }
                else if (!passive)
                {
                    if (string.IsNullOrWhiteSpace(currentSelection) && newPorts.Count > 0)
                    {
                        txtCom.SelectedItem = newPorts[0];
                    }
                    else if (!string.IsNullOrWhiteSpace(currentSelection))
                    {
                        int idx = Array.IndexOf(ports, currentSelection);
                        if (idx >= 0) txtCom.SelectedIndex = idx; else txtCom.SelectedIndex = -1;
                    }
                }
                else // passiver Refresh
                {
                    if (!string.IsNullOrWhiteSpace(currentSelection))
                    {
                        int idx = Array.IndexOf(ports, currentSelection);
                        if (idx >= 0) txtCom.SelectedIndex = idx; else txtCom.SelectedIndex = -1; // Port weg -> leer
                    }
                }
                if (txtCom.Items.Count == 0) txtCom.SelectedIndex = -1;
                EnsureSelectedPort(_coin?.ComPort, initial: false); // nur hinzuf�gen, keine Selektion
            }
            catch { }
        }

        private void StartDetectPortMode()
        {
            if (_detectingPort) { StopDetectPortMode(true); return; }
            try
            {
                _detectingPort = true;
                btnDetectPort.Text = "Stop";
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
                var lblCountdown = new Label { Text = string.Empty, AutoSize = false, Location = new Point(12, 72), Size = new Size(420, 24), ForeColor = Color.DimGray, Font = new Font("Segoe UI Variable", 10F) };
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
                // Sicherstellen, dass der erkannte Port im Dropdown vorhanden ist
                if (!txtCom.Items.Cast<object>().Any(o => string.Equals(Convert.ToString(o), port, StringComparison.OrdinalIgnoreCase)))
                {
                    txtCom.Items.Add(port);
                }
                txtCom.SelectedItem = port;
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
            if (btnDetectPort != null) btnDetectPort.Text = "Erkennung";
            AppendLog(cancelled ? "COM-Port Erkennung abgebrochen." : "Neuer COM-Port erkannt: " + (txtCom.SelectedItem ?? "?"));
        }

        private void UpdateStatusUi()
        {
            try
            {
                if (DeviceDisabled)
                {
                    if (_lblScConn != null) { _lblScConn.Text = "Deaktiviert"; _lblScConn.ForeColor = Color.Gray; }
                    if (_lblScPortAddr != null) _lblScPortAddr.Text = "-";
                    if (_lblScLastEvt != null) _lblScLastEvt.Text = "-";
                    if (_lblScState != null) { _lblScState.Text = "disabled"; _lblScState.ForeColor = Color.Gray; }
                    return;
                }
                bool conn = _coin != null && _coin.Connected;
                if (_lblScConn != null)
                {
                    _lblScConn.Text = conn ? "Verbunden (Thread)" : "Getrennt";
                    _lblScConn.ForeColor = conn ? Color.FromArgb(0, 128, 0) : Color.FromArgb(183, 28, 28);
                }
                if (_lblScPortAddr != null) _lblScPortAddr.Text = $"{(_coin?.ComPort ?? "-")}, Addr {(_coin != null ? _coin.SspAddress.ToString() : "-")}";
                EnsureSelectedPort(_coin?.ComPort, initial: false);
                
                // Hole den aktuellen Status auch ohne StatusChanged-Event
                var sc1 = _coin as SmartCoinV1;
                if (sc1 != null && !string.IsNullOrWhiteSpace(sc1.CurrentStatus))
                {
                    _statusText = sc1.CurrentStatus;
                }

                if (_lblScState != null)
                    _lblScState.Text = string.IsNullOrWhiteSpace(_statusText) ? "-" : _statusText;

                if (!_didFirstReadyRefresh && IsReadyStatus(_statusText) && _eventsAttached)
                {
                    // beim ersten Mal nach Ready gleich Best�nde holen
                    RefreshBestandCoins();
                }

                if (_lblScLastEvt != null)
                {
                    if (_lastCoinEventUtc == DateTime.MinValue)
                        _lblScLastEvt.Text = "Letzte Aktivität: -";
                    else
                    {
                        var ago = DateTime.UtcNow - _lastCoinEventUtc;
                        _lblScLastEvt.Text = $"Letzte Aktivität: {_lastCoinEventUtc.ToLocalTime():HH:mm:ss} ({Math.Max(0, (int)ago.TotalSeconds)} s)";
                    }
                }
            }
            catch { }
        }

        private void AppendLog(string s)
        {
            if (InvokeRequired) { BeginInvoke((Action)(() => AppendLog(s))); return; }
            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {s}\r\n");
        }

        // Header wird zentral über `ModernHeaderPanel` gezeichnet.

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { DetachCoinEvents(); } catch { }
            // WICHTIG: Erkennungsmodus sauber beenden, damit kein Timer auf disposed Controls feuert
            try { _detectTimer?.Stop(); } catch { }
            _detectTimer = null;
            try { _detectDialog?.Close(); } catch { }
            try { _detectDialog?.Dispose(); } catch { }
            _detectDialog = null;
            base.OnFormClosed(e);
        }

        private void AttachCoinEvents()
        {
            if (DeviceDisabled) return;
            if (_coin == null || _eventsAttached) return;

            try
            {
                if (_coin is SmartCoinV1 sc1)
                {
                    sc1.EventLog += CoinOnEventLog;
                    sc1.CoinAccepted += CoinOnCoinAccepted;
                    sc1.CoinLevelsUpdated += CoinOnCoinLevelsUpdated;
                    sc1.StatusChanged += CoinOnStatusChanged;

                    // sofort Status holen
                    if (!string.IsNullOrWhiteSpace(sc1.CurrentStatus))
                    {
                        _statusText = sc1.CurrentStatus;
                    }
                }
                _eventsAttached = true;
            }
            catch { }
            UpdateConnectButtonVisual();
        }

        private void DetachCoinEvents()
        {
            if (_coin == null || !_eventsAttached) return;

            try
            {
                if (_coin is SmartCoinV1 sc1)
                {
                    sc1.EventLog -= CoinOnEventLog;
                    sc1.CoinAccepted -= CoinOnCoinAccepted;
                    sc1.CoinLevelsUpdated -= CoinOnCoinLevelsUpdated;
                    sc1.StatusChanged -= CoinOnStatusChanged;
                }
            }
            catch { }
            _eventsAttached = false;
            UpdateConnectButtonVisual();
        }

        private void btnConnectToggle_Click(object sender, EventArgs e)
        {
            if (DeviceDisabled) { AppendLog("Gerät deaktiviert – keine Events."); return; }
            if (_coin == null)
            {
                AppendLog("Kein Münzprüfer-Objekt vorhanden.");
                return;
            }

            if (_eventsAttached)
            {
                DetachCoinEvents();
                AppendLog("Events gelöst.");
            }
            else
            {
                AttachCoinEvents();
                AppendLog("Events angehängt.");
            }
        }

        private void CoinOnEventLog(string s)
        {
            _lastCoinEventUtc = DateTime.UtcNow;
            AppendLog(s);
        }

        private void CoinOnCoinAccepted(int c)
        {
            AppendLog($"CoinAccepted: {c} ct");
        }

        private void CoinOnCoinLevelsUpdated(int[] lvls)
        {
            AppendLog("CoinLevelsUpdated: " + FormatLevels(lvls));
            BeginInvoke((Action)(() => UpdateCoinsUiFromLevels(lvls)));
        }

        private void CoinOnStatusChanged(string s)
        {
            _statusText = s;
            try { BeginInvoke((Action)(() => _lblScState.Text = string.IsNullOrWhiteSpace(s) ? "-" : s)); } catch { }
            if (IsReadyStatus(s))
            {
                try { BeginInvoke((Action)RefreshBestandCoins); } catch { }
            }
        }

        private void UpdateConnectButtonVisual()
        {
            if (btnConnectToggle == null) return;
            btnConnectToggle.Text = _eventsAttached ? "Events lösen" : "Events anhängen";

            var mgb = btnConnectToggle as ModernGradientButton;
            if (mgb != null)
            {
                if (DeviceDisabled)
                {
                    mgb.GradientStart = Color.Gray;
                    mgb.GradientEnd = ControlPaint.Dark(Color.Gray);
                }
                else if (_eventsAttached)
                {
                    mgb.GradientStart = UiTheme.DangerStart;
                    mgb.GradientEnd = UiTheme.DangerEnd;
                }
                else
                {
                    mgb.GradientStart = UiTheme.PrimaryStart;
                    mgb.GradientEnd = UiTheme.PrimaryEnd;
                }
                try { mgb.Invalidate(); } catch { }
            }
            else
            {
                btnConnectToggle.BackColor = DeviceDisabled ? Color.Gray : (_eventsAttached ? Color.FromArgb(183, 28, 28) : Color.FromArgb(33, 150, 243));
            }
        }

        private void ApplyDisabledState()
        {
            try
            {
                if (DeviceDisabled)
                {
                    try { _tmrBestand?.Stop(); } catch { }
                    try { _tmrStatus?.Stop(); } catch { }
                    try { if (_coin != null && _coin.Connected) _coin.Enable(false); } catch { }
                    try { DetachCoinEvents(); } catch { }
                }
                else
                {
                    try { _tmrBestand?.Start(); } catch { }
                    try { _tmrStatus?.Start(); } catch { }
                    try { AttachCoinEvents(); } catch { }
                }
                UpdateConnectButtonVisual();
            }
            catch { }
        }

        private void AdminOverviewFormRefreshSafe()
        {
            try
            {
                foreach (Form f in Application.OpenForms)
                {
                    if (f is AdminOverviewForm ov)
                    {
                        var mi = typeof(AdminOverviewForm).GetMethod("UpdateDeviceStatus", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                        mi?.Invoke(ov, null);
                        break;
                    }
                }
            }
            catch { }
        }

        // Helfer: aktuellen Port sicher im Dropdown selektieren (ggf. hinzuf�gen)
        private bool _initialPortApplied = false; // NEU
        private void EnsureSelectedPort(string port, bool initial = false)
        {
            if (string.IsNullOrWhiteSpace(port) || txtCom == null) return;
            try
            {
                bool exists = txtCom.Items.Cast<object>().Any(o => string.Equals(Convert.ToString(o), port, StringComparison.OrdinalIgnoreCase));
                if (!exists) txtCom.Items.Add(port);
                if (initial && !_initialPortApplied)
                {
                    txtCom.SelectedItem = port;
                    _initialPortApplied = true;
                }
            }
            catch { }
        }
        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);
    }
}