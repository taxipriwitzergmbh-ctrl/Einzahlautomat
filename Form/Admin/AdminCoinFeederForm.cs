using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using TaMi_Einzahlautomat.Devices;
using System.Collections.Generic;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Linq; // sicherstellen
using TaMi_Einzahlautomat.UI.Layout;

namespace TaMi_Einzahlautomat
{
    public class AdminCoinFeederForm : Form
    {
        private readonly string _iniPath;
        private CoinFeederController _feeder;

        // UI
        private ModernHeaderPanel _header;
        private ComboBox cmbPorts;
        private ComboBox cmbType; // Typ-Auswahl: CoinFeeder oder NFC V2
       
        private Button btnDetect; // NEU Erkennung
        private bool _detectingPort; // NEU
        private string[] _detectBasePorts; // NEU
        private Form _detectDialog; // NEU
        private System.Windows.Forms.Timer _detectTimer; // NEU
        private string[] _lastPorts; // NEU
        private Button btnApply;
        private Button btnReconnect;
        private TextBox txtLog;
        private Button btnGreen;   // NV200/1 (default channel) Grün
        private Button btnGreenA;  // NV200/2 (channel 'a') Grün
        private Button btnRed;     // NV200/1 (default channel) Rot
        private Button btnRedA;    // NV200/2 (channel 'a') Rot
        private Label lblStatus;

        private Action<string> _logHandler;
        private System.Windows.Forms.Timer _stateTimer;
        private bool _nv1WasIdle = false;
        private bool _nv2WasIdle = false;

        private readonly Dictionary<string, DateTime> _lastSendTimes = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly TimeSpan _sendDebounce = TimeSpan.FromSeconds(2);
        private Point _mouseDown;

        public AdminCoinFeederForm(string iniPath)
        {
            _iniPath = iniPath;
            _feeder = Program.CoinFeeder; // globale Instanz
            BuildUi();
            LoadTypeFromIni();
            LoadPortFromIni();
            LoadPorts();
            // Sicherstellen, dass der aktuell konfigurierte/open Port selektiert ist
            EnsureSelectedPort(_feeder?.PortName);
            StartStateTimer();
            UpdateUiState();
        }

        // Hilfsmethode: aktuell verwendeten Port im Dropdown selektieren (falls nötig hinzufügen)
        private void EnsureSelectedPort(string port)
        {
            if (string.IsNullOrWhiteSpace(port) || cmbPorts == null) return;
            try
            {
                // Falls Liste leer -> Ports laden
                if (cmbPorts.Items.Count == 0)
                {
                    cmbPorts.Items.Add(port);
                    cmbPorts.SelectedItem = port;
                    return;
                }
                // Port vorhanden?
                var exists = cmbPorts.Items.Cast<object>().Any(o => string.Equals(Convert.ToString(o), port, StringComparison.OrdinalIgnoreCase));
                if (!exists)
                {
                    cmbPorts.Items.Add(port);
                }
                // Nur neu setzen wenn noch nicht selektiert
                if (!string.Equals(Convert.ToString(cmbPorts.SelectedItem), port, StringComparison.OrdinalIgnoreCase))
                {
                    cmbPorts.SelectedItem = port;
                }
            }
            catch { }
        }

        #region Modern UI

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED für flackerfrei
                return cp;
            }
        }

        private void BuildUi()
        {
            SuspendLayout();

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;
            DoubleBuffered = true;
            ClientSize = new Size(880, 620);

            // Rounded corners übernimmt `_header.ApplyRoundedRegionToForm(this)`

            _header = new ModernHeaderPanel { Title = "CoinFeeder – Verwaltung" };
            _header.CloseClicked += () => { try { Close(); } catch { } };
            Controls.Add(_header);
            _header.BringToFront();
            try { _header.ApplyRoundedRegionToForm(this); } catch { }

            // Port-Auswahl
            var lblPort = new Label
            {
                Text = "Port:",
                Location = new Point(24, 80),
                AutoSize = true,
                Font = new Font("Segoe UI Variable", 12f, FontStyle.Bold)
            };
            Controls.Add(lblPort);

            cmbPorts = new ComboBox
            {
                Location = new Point(80, 76),
                Size = new Size(160, 36),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI Variable", 12f)
            };
            try { cmbPorts.FlatStyle = FlatStyle.Flat; cmbPorts.BackColor = Color.FromArgb(245, 247, 250); cmbPorts.ForeColor = Color.FromArgb(33, 37, 41); } catch { }
            cmbPorts.DropDown += (s,e)=> LoadPorts(true);
            Controls.Add(cmbPorts);

            // Typ-Auswahl (CoinFeeder / NFC V2)
            var lblType = new Label
            {
                Text = "Typ:",
                Location = new Point(24, 120),
                AutoSize = true,
                Font = new Font("Segoe UI Variable", 12f, FontStyle.Bold)
            };
            Controls.Add(lblType);

            cmbType = new ComboBox
            {
                Location = new Point(80, 116),
                Size = new Size(160, 36),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI Variable", 12f)
            };
            try { cmbType.FlatStyle = FlatStyle.Flat; cmbType.BackColor = Color.FromArgb(245, 247, 250); cmbType.ForeColor = Color.FromArgb(33, 37, 41); } catch { }
            cmbType.Items.AddRange(new object[] { "CoinFeeder", "NFC V2" });
            cmbType.SelectedIndexChanged += (s, e) => { SaveTypeToIni(); UpdateUiState(); };
            Controls.Add(cmbType);

            btnDetect = new ModernGradientButton { Text = "Erkennung", Location = new Point(250, 76), Size = new Size(120, 36), GradientStart = UiTheme.SecondaryStart, GradientEnd = UiTheme.SecondaryEnd };
            btnDetect.Click += (s,e)=> StartDetectPortMode();
            Controls.Add(btnDetect);

            btnApply = new ModernGradientButton { Text = "Übernehmen", Location = new Point(380, 76), Size = new Size(140, 36), GradientStart = UiTheme.SuccessStart, GradientEnd = UiTheme.SuccessEnd };
            btnApply.Click += (s, e) => ApplyPortChange();
            Controls.Add(btnApply);

            btnReconnect = new ModernGradientButton { Text = "Reopen", Location = new Point(530, 76), Size = new Size(110, 36), GradientStart = UiTheme.PrimaryStart, GradientEnd = UiTheme.PrimaryEnd };
            btnReconnect.Click += (s, e) => ReopenCurrentPort();
            Controls.Add(btnReconnect);

            lblStatus = new Label
            {
                Text = "Status: -",
                Location = new Point(660, 80),
                Size = new Size(300, 52),
                Font = new Font("Segoe UI Variable", 11f, FontStyle.Italic),
                ForeColor = Color.DimGray,
                AutoSize = false
            };
            Controls.Add(lblStatus);

            // Log
            txtLog = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Location = new Point(24, 170),
                Size = new Size(560, 460),
                Font = new Font("Consolas", 10f),
                BackColor = Color.White
            };
            Controls.Add(txtLog);

            _logHandler = (line) =>
            {
                try
                {
                    if (txtLog.InvokeRequired)
                        txtLog.BeginInvoke((Action)(() => AppendLog(line)));
                    else
                        AppendLog(line);
                }
                catch { }
            };
            if (_feeder != null)
                _feeder.EventLog += _logHandler;

            // LED Buttons – Mapping angepasst:
            // NV200/1 -> default channel commands (ohne 'a')
            // NV200/2 -> channel 'a'
            int bx = 610;
            int by = 210;
            int bw = 220;
            int bh = 58;
            int pad = 16;

            btnGreen = new ModernGradientButton { Text = "NV200/1 Grün (2005$)", Location = new Point(bx, by), Size = new Size(bw, bh), GradientStart = UiTheme.SuccessStart, GradientEnd = UiTheme.SuccessEnd };
            btnGreen.Click += (s, e) => SafeSend("2005$");
            Controls.Add(btnGreen);

            btnGreenA = new ModernGradientButton { Text = "NV200/2 Grün (2005a$)", Location = new Point(bx, by + (bh + pad)), Size = new Size(bw, bh), GradientStart = UiTheme.SuccessStart, GradientEnd = UiTheme.SuccessEnd };
            btnGreenA.Click += (s, e) => SafeSend("2005a$");
            Controls.Add(btnGreenA);

            btnRed = new ModernGradientButton { Text = "NV200/1 Rot (2004$)", Location = new Point(bx, by + 2 * (bh + pad)), Size = new Size(bw, bh), GradientStart = UiTheme.DangerStart, GradientEnd = UiTheme.DangerEnd };
            btnRed.Click += (s, e) => SafeSend("2004$");
            Controls.Add(btnRed);

            btnRedA = new ModernGradientButton { Text = "NV200/2 Rot (2004a$)", Location = new Point(bx, by + 3 * (bh + pad)), Size = new Size(bw, bh), GradientStart = UiTheme.DangerStart, GradientEnd = UiTheme.DangerEnd };
            btnRedA.Click += (s, e) => SafeSend("2004a$");
            Controls.Add(btnRedA);

            // Hinweislabel entfernt (LF (\n) Anzeige)

            FormClosed += OnFormClosed;

            ResumeLayout(false);
        }

        private void AppendLog(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
        }

        #endregion

        #region Port Handling

        private void LoadPorts(bool passive = false)
        {
            try
            {
                var previous = _lastPorts ?? Array.Empty<string>();
                var current = cmbPorts.SelectedItem as string;
                var ports = SerialPort.GetPortNames();
                Array.Sort(ports, StringComparer.OrdinalIgnoreCase);
                _lastPorts = (string[])ports.Clone();

                var newPorts = ports.Where(p => !previous.Contains(p, StringComparer.OrdinalIgnoreCase)).ToList();

                cmbPorts.Items.Clear();
                cmbPorts.Items.AddRange(ports);

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
                    if (string.IsNullOrWhiteSpace(current) && newPorts.Count > 0)
                        cmbPorts.SelectedItem = newPorts[0];
                    else if (!string.IsNullOrWhiteSpace(current))
                    {
                        int idx = Array.IndexOf(ports, current);
                        if (idx >= 0) cmbPorts.SelectedIndex = idx; else cmbPorts.SelectedIndex = -1;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(current))
                {
                    int idx = Array.IndexOf(ports, current);
                    if (idx >= 0) cmbPorts.SelectedIndex = idx; else cmbPorts.SelectedIndex = -1;
                }
                if (cmbPorts.Items.Count == 0) cmbPorts.SelectedIndex = -1;

                // Nach Aktualisierung sicherstellen, dass der tatsächliche Port (konfiguriert/feeder.PortName) sichtbar ist.
                EnsureSelectedPort(_feeder?.PortName);
            }
            catch (Exception ex)
            {
                AppendLog("LoadPorts Fehler: " + ex.Message);
            }
        }

        private void LoadPortFromIni()
        {
            try
            {
                var configured = IniHelper.ReadValue("CoinFeeder", "ComPort", _iniPath);
                if (!string.IsNullOrWhiteSpace(configured) && _feeder != null && !_feeder.IsOpen)
                {
                    // Statt _feeder.PortName = configured; (nicht erlaubt, da private set) -> Configure aufrufen
                    try
                    {
                        _feeder.Configure(configured);
                        AppendLog($"Konfiguration vorbereitet (Port={configured})");
                    }
                    catch (Exception ex)
                    {
                        AppendLog("Configure fehlgeschlagen: " + ex.Message);
                    }
                }
                // Direkt sicherstellen, dass Dropdown den konfigurierten Port anzeigt
                EnsureSelectedPort(_feeder?.PortName ?? configured);
            }
            catch { }
        }

        private void ApplyPortChange()
        {
            try
            {
                var sel = cmbPorts.SelectedItem as string;
                if (string.IsNullOrWhiteSpace(sel))
                {
                    MessageBox.Show(this, "Bitte zuerst einen Port auswählen.", "Hinweis",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                IniHelper.WriteValue("CoinFeeder", "ComPort", sel, _iniPath);
                AppendLog($"INI gespeichert: [CoinFeeder] ComPort={sel}");

                if (_feeder != null)
                {
                    bool reopen = !_feeder.IsOpen || !string.Equals(_feeder.PortName, sel, StringComparison.OrdinalIgnoreCase);
                    if (reopen)
                    {
                        try { if (_feeder.IsOpen) _feeder.Close(); } catch { }
                        _feeder.Configure(sel);
                        _feeder.Open();
                        AppendLog($"Port gewechselt und geöffnet: {sel} (IsOpen={_feeder.IsOpen})");
                    }
                }
                UpdateUiState();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Portwechsel fehlgeschlagen:\r\n" + ex.Message, "Fehler",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                AppendLog("ApplyPortChange Fehler: " + ex.Message);
            }
        }

        private void ReopenCurrentPort()
        {
            try
            {
                var sel = cmbPorts.SelectedItem as string;
                if (string.IsNullOrWhiteSpace(sel))
                {
                    MessageBox.Show(this, "Kein Port ausgewählt.", "Hinweis",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (_feeder == null)
                {
                    MessageBox.Show(this, "Feeder-Instanz fehlt.", "Fehler",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                try { if (_feeder.IsOpen) _feeder.Close(); } catch { }
                _feeder.Configure(sel);
                _feeder.Open();
                AppendLog($"Reopen auf {sel} (IsOpen={_feeder.IsOpen})");
                UpdateUiState();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Reopen fehlgeschlagen:\r\n" + ex.Message, "Fehler",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                AppendLog("Reopen Fehler: " + ex.Message);
            }
        }

        private void UpdateUiState()
        {
            try
            {
                bool open = _feeder != null && _feeder.IsOpen;
                bool isNfc = IsNfcMode();
                btnGreen.Enabled = open;
                btnGreenA.Enabled = open;
                btnRed.Enabled = open;
                btnRedA.Enabled = open;
                btnReconnect.Enabled = (cmbPorts.SelectedItem != null);
                lblStatus.Text = $"Port: {_feeder?.PortName ?? "-"}{Environment.NewLine}Open: {(open ? "Ja" : "Nein")}";
                lblStatus.ForeColor = open ? Color.FromArgb(0, 128, 0) : Color.FromArgb(183, 28, 28);

                // Im NFC-Modus keine LED-Steuerung
                if (isNfc)
                {
                    btnGreen.Visible = false; btnGreenA.Visible = false; btnRed.Visible = false; btnRedA.Visible = false;
                }
                else
                {
                    btnGreen.Visible = true; btnGreenA.Visible = true; btnRed.Visible = true; btnRedA.Visible = true;
                }
            }
            catch { }
        }

        #endregion

        #region State / Auto-Logic

        private void StartStateTimer()
        {
            _stateTimer = new System.Windows.Forms.Timer { Interval = 1500 }; // etwas langsamer
            _stateTimer.Tick += (s, e) => StateTimerTick();
            _stateTimer.Start();
            AppendLog("State-Timer gestartet");
        }

        private void StateTimerTick()
        {
            try
            {
                if (IsNfcMode()) { UpdateUiState(); return; } // Keine Auto-LED-Logik im NFC-Modus
                var nv1 = Program.NV200Instance; // default channel
                var nv2 = Program.NV2002Instance; // channel 'a'
                string s1 = nv1?.states ?? string.Empty;
                string s2 = nv2?.states ?? string.Empty;

                bool idle1 = !string.IsNullOrWhiteSpace(s1) && s1.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0;
                bool idle2 = !string.IsNullOrWhiteSpace(s2) && s2.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0;

                // NV200/1 (default channel)
                if (idle1 && !_nv1WasIdle)
                {
                    try
                    {
                        EnsureFeederOpen();
                        if (nv1 != null && nv1.MitarbeiterEingeloggt)
                        {
                            AppendLog("Auto: NV200/1 idle -> 2005$");
                            SendSimple("2005$");
                        }
                        else
                        {
                            AppendLog("Auto: NV200/1 idle ohne Login -> 2004$");
                            SendSimple("2004$");
                        }
                    }
                    catch (Exception ex) { AppendLog("Auto NV200/1 Fehler: " + ex.Message); }
                    _nv1WasIdle = true;
                }
                else if (!idle1 && _nv1WasIdle)
                    _nv1WasIdle = false;

                // NV200/2 (channel 'a')
                if (idle2 && !_nv2WasIdle)
                {
                    try
                    {
                        EnsureFeederOpen();
                        if (nv2 != null && nv2.MitarbeiterEingeloggt)
                        {
                            AppendLog("Auto: NV200/2 idle -> 2005a$");
                            SendSimple("2005a$");
                        }
                        else
                        {
                            AppendLog("Auto: NV200/2 idle ohne Login -> 2004a$");
                            SendSimple("2004a$");
                        }
                    }
                    catch (Exception ex) { AppendLog("Auto NV200/2 Fehler: " + ex.Message); }
                    _nv2WasIdle = true;
                }
                else if (!idle2 && _nv2WasIdle)
                    _nv2WasIdle = false;

                UpdateUiState();
            }
            catch (Exception ex) { AppendLog("StateTimerTick Fehler: " + ex.Message); }
        }

        private void EnsureFeederOpen()
        {
            if (_feeder == null)
                throw new InvalidOperationException("CoinFeederController fehlt.");

            var sel = !string.IsNullOrWhiteSpace(_feeder.PortName)
                            ? _feeder.PortName
                            : (IniHelper.ReadValue("CoinFeeder", "ComPort", _iniPath) ?? string.Empty);

            if (string.IsNullOrWhiteSpace(sel))
                throw new InvalidOperationException("Kein COM-Port konfiguriert (INI [CoinFeeder] ComPort).");

            if (!_feeder.IsOpen)
            {
                if (!string.Equals(_feeder.PortName, sel, StringComparison.OrdinalIgnoreCase))
                {
                    try { if (_feeder.IsOpen) _feeder.Close(); } catch { }
                    _feeder.Configure(sel);
                }

                AppendLog($"Opening CoinFeeder auf {sel}...");
                _feeder.Open();
                AppendLog($"IsOpen={_feeder.IsOpen}");
            }
        }

        #endregion

        #region Sending

        private void SafeSend(string tpl)
        {
            try { SendSimple(tpl); }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                AppendLog("Send Fehler: " + ex.Message);
            }
        }

        private void SendSimple(string template)
        {
            if (_feeder == null)
                throw new InvalidOperationException("CoinFeeder nicht initialisiert.");

            if (IsNfcMode())
            {
                AppendLog("NFC V2 Modus aktiv – Senden deaktiviert.");
                return;
            }

            try
            {
                DateTime now = DateTime.UtcNow;
                if (_lastSendTimes.TryGetValue(template, out DateTime last) && (now - last) < _sendDebounce)
                {
                    AppendLog($"Debounce: {template} verworfen");
                    return;
                }
                _lastSendTimes[template] = now;
            }
            catch { }

            EnsureFeederOpen();

            _feeder.Mode = CoinFeederProtocolMode.ASCII;
            _feeder.Terminator = "\n"; // unified LF only
            _feeder.AppendTerminatorAfterDollar = true;

            AppendLog($"Sende: {template} (Port {_feeder.PortName}, Open={_feeder.IsOpen})");
            SendMulti(template);
        }

        private void SendMulti(string template)
        {
            var parts = (template ?? string.Empty)
                .Replace("\r", "\n")
                .Split(new[] { '\n', ';' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var p in parts)
            {
                var cmd = p.Trim();
                if (cmd.Length == 0) continue;
                try { _feeder.SendTemplate(cmd); }
                catch (Exception ex) { AppendLog("Teilkommando Fehler: " + ex.Message); }
                Thread.Sleep(60);
            }
        }

        #endregion

        #region Cleanup

        private void OnFormClosed(object sender, FormClosedEventArgs e)
        {
            try
            {
                if (_stateTimer != null)
                {
                    _stateTimer.Stop();
                    _stateTimer.Dispose();
                    _stateTimer = null;
                }
            }
            catch { }

            try { if (_feeder != null && _logHandler != null) _feeder.EventLog -= _logHandler; } catch { }
        }

        #endregion

        private void StartDetectPortMode()
        {
            if (_detectingPort) { StopDetectPortMode(true); return; }
            try
            {
                _detectingPort = true;
                btnDetect.Text = "Stop";

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
                var preTimer = new System.Windows.Forms.Timer { Interval = 1000 };
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
                                try
                                {
                                    _detectBasePorts = SerialPort.GetPortNames();
                                    Array.Sort(_detectBasePorts, StringComparer.OrdinalIgnoreCase);
                                }
                                catch { _detectBasePorts = Array.Empty<string>(); }
                                lbl.Text = "Jetzt bitte das Gerät einstecken.\r\nFenster schließt automatisch bei Erkennung.";
                                lblCountdown.Text = string.Empty;
                                _detectTimer = new System.Windows.Forms.Timer { Interval = 700 };
                                _detectTimer.Tick += (ts2, te2) => LoadPorts();
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
                if (!cmbPorts.Items.Cast<object>().Any(o => string.Equals(Convert.ToString(o), port, StringComparison.OrdinalIgnoreCase)))
                {
                    cmbPorts.Items.Add(port);
                }
                cmbPorts.SelectedItem = port;

                // Automatisch übernehmen: INI speichern und Feeder konfigurieren/öffnen
                try { IniHelper.WriteValue("CoinFeeder", "ComPort", port, _iniPath); } catch { }
                try
                {
                    if (_feeder != null)
                    {
                        bool needOpen = !_feeder.IsOpen || !string.Equals(_feeder.PortName, port, StringComparison.OrdinalIgnoreCase);
                        if (needOpen)
                        {
                            try { if (_feeder.IsOpen) _feeder.Close(); } catch { }
                            _feeder.Configure(port);
                            _feeder.Open();
                        }
                    }
                }
                catch { }
                UpdateUiState();
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
            btnDetect.Text = "Erkennung";
            AppendLog(cancelled ? "COM-Port Erkennung abgebrochen." : "Neuer COM-Port erkannt: " + (cmbPorts.SelectedItem ?? "?"));
        }

        // --- Hilfsmethoden: Typ laden/speichern und prüfen ---
        private void LoadTypeFromIni()
        {
            try
            {
                var t = IniHelper.ReadValue("CoinFeeder", "Type", _iniPath);
                if (string.Equals(t, "NFC", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "NFC V2", StringComparison.OrdinalIgnoreCase))
                    cmbType.SelectedItem = "NFC V2";
                else
                    cmbType.SelectedItem = "CoinFeeder";
            }
            catch { cmbType.SelectedIndex = 0; }
        }

        private void SaveTypeToIni()
        {
            try
            {
                var val = (cmbType.SelectedItem as string) ?? "CoinFeeder";
                IniHelper.WriteValue("CoinFeeder", "Type", val, _iniPath);
            }
            catch { }
        }

        private bool IsNfcMode()
        {
            try { return string.Equals(Convert.ToString(cmbType.SelectedItem), "NFC V2", StringComparison.OrdinalIgnoreCase); } catch { return false; }
        }

        // --- NFC Frame Decoder: 2011$<count>$<b1>$<b2>$... ---
        private string DecodeNfcFrame(string line)
        {
            try
            {
                var raw = (line ?? string.Empty).Trim();
                var parts = raw.Split(new[] { '$' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && parts[0] == "2011")
                {
                    int count = 0; int.TryParse(parts[1], out count);
                    var values = new List<int>();
                    for (int i = 2; i < parts.Length; i++) { if (int.TryParse(parts[i], out var v)) values.Add(v); }
                    if (count > 0 && values.Count >= count)
                    {
                        return $"NFC V2: Bits={count}, Werte=[{string.Join(",", values.Take(count))}]";
                    }
                    return $"NFC V2: Rohdaten=[{string.Join(",", values)}]";
                }
            }
            catch { }
            return line;
        }
    }
}
