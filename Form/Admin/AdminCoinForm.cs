using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Drawing.Drawing2D;
using Geldautomat.Coins;
using System.IO.Ports; // NEU für Portliste
using System.Linq; // NEU für Port-Vergleich

namespace Geldautomat
{
    public class AdminCoinForm : Form
    {
        private Panel headerPanel;
        private Button btnClose;
        private Button btnMinimize;
        private Label lblTitle;
        private Point _mouseDownLocation;

        private ComboBox cmbType;
        // Entfernt doppelte TextBox-Deklaration von txtCom
        private ComboBox txtCom; // ComboBox für Portliste
        private string[] _lastPorts; // vorherige Liste zum Erkennen neuer Ports
        private Button btnDetectPort; // NEU: automatische Erkennung
        private bool _detectingPort; // Erkennungsmodus aktiv
        private string[] _detectBasePorts; // Ausgangsliste für Erkennung
        private Form _detectDialog; // Hinweisfenster
        private Timer _detectTimer; // Polling Timer
        private NumericUpDown nudAddr;
        private Button btnSaveIni;
        private Button btnConnectToggle;
        private Button btnEnableToggle;
        private Button btnQueryLevels; // NEU
        private Button btnRm5Enable;   // NEU: RM5 freigeben
        private TextBox txtLog;

        private ICoinValidator _coin = CoinManager.Instance; // nicht readonly -> Austausch erlaubt
        private readonly string _iniPath = @"C:\\ProgramData\\SuE-Software\\SuE-TaMi Client SQL\\Geldautomat.ini";

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
        private static bool IsReadyStatus(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            var t = s.ToLowerInvariant();
            return t.Contains("handshake ok") || t.Contains("idle") || t.Contains("bereit") || t.Contains("synchron") || t.Contains("verbunden");
        }

        // NEU: Deaktivieren-Checkbox + globaler Status
        private CheckBox _chkDisabled;
        public static bool DeviceDisabled = false; // true = keine Verbindungsversuche / Events

        private static bool IsRm5(string sel) => string.Equals(sel, "RM5", System.StringComparison.OrdinalIgnoreCase);
        private static bool IsNone(string sel) => string.Equals(sel, "None", System.StringComparison.OrdinalIgnoreCase);
        private static bool IsSmart(string sel) => string.Equals(sel, "SmartCoinV1", System.StringComparison.OrdinalIgnoreCase);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

        public AdminCoinForm(ICoinValidator coin)
        {
            _coin = CoinManager.Instance ?? coin;
            // load persisted disabled flag
            try
            {
                var dv = IniHelper.ReadValue("SmartCoin", "Disabled", _iniPath);
                if (!string.IsNullOrWhiteSpace(dv)) DeviceDisabled = dv.Trim().Equals("1", StringComparison.OrdinalIgnoreCase) || dv.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
            }
            catch { }
            InitializeUi();
            LoadIni();
            ApplyCoin2Mode(); // ensure correct Coin2 state after INI load
            try { AttachCoinEvents(); } catch { }
            UpdateConnectButtonVisual();
            try { UpdateStatusUi(); RefreshBestandCoins(); } catch { }
        }

        private void InitializeUi()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(900, 760);
            BackColor = Color.White;
            DoubleBuffered = true;

            headerPanel = new Panel { Location = new Point(0, 0), Size = new Size(ClientSize.Width, 60), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            headerPanel.Paint += HeaderPanel_Paint;
            headerPanel.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) _mouseDownLocation = e.Location; };
            headerPanel.MouseMove += (s, e) => { if (e.Button == MouseButtons.Left) { Left += e.X - _mouseDownLocation.X; Top += e.Y - _mouseDownLocation.Y; } };
            Controls.Add(headerPanel);

            lblTitle = new Label { Text = "Admin - Münzprüfer", AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Variable", 18F, FontStyle.Bold), ForeColor = Color.White, Location = new Point(24, 0), Size = new Size(500, 60), BackColor = Color.Transparent };
            headerPanel.Controls.Add(lblTitle);

            btnClose = new Button { Text = "\u2715", Font = new Font("Segoe UI Symbol", 18F, FontStyle.Bold), ForeColor = Color.White, BackColor = Color.Transparent, FlatStyle = FlatStyle.Flat, Size = new Size(48, 48), Location = new Point(ClientSize.Width - 56, 6), TabStop = false, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 80, 80);
            btnClose.Click += (s, e) => Close();
            headerPanel.Controls.Add(btnClose);

            btnMinimize = new Button { Text = "–", Font = new Font("Segoe UI", 18F, FontStyle.Bold), ForeColor = Color.White, BackColor = Color.Transparent, FlatStyle = FlatStyle.Flat, Size = new Size(48, 48), Location = new Point(ClientSize.Width - 112, 6), TabStop = false, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnMinimize.FlatAppearance.BorderSize = 0;
            btnMinimize.FlatAppearance.MouseOverBackColor = Color.FromArgb(33, 150, 243, 80);
            btnMinimize.Click += (s, e) => WindowState = FormWindowState.Minimized;
            headerPanel.Controls.Add(btnMinimize);

            try { Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, Width, Height, 24, 24)); } catch { }

            var lblType = new Label { Text = "Version:", Location = new Point(24, 80), Size = new Size(120, 36), Font = new Font("Segoe UI Variable", 12F) };
            cmbType = new ComboBox { Location = new Point(150, 80), Size = new Size(220, 36), Font = new Font("Segoe UI Variable", 12F), DropDownStyle = ComboBoxStyle.DropDownList };
            cmbType.Items.AddRange(new object[] { "None", "SmartCoinV1", "RM5" });
            cmbType.SelectedIndexChanged += (s, e) => OnTypeChanged();

            var lblCom = new Label { Text = "ComPort:", Location = new Point(24, 130), Size = new Size(120, 36), Font = new Font("Segoe UI Variable", 12F) };
            txtCom = new ComboBox { Location = new Point(150, 130), Size = new Size(180, 36), Font = new Font("Segoe UI Variable", 12F), DropDownStyle = ComboBoxStyle.DropDownList };
            txtCom.DropDown += (s,e)=> LoadComPorts(true); // passive Refresh beim Öffnen
            // ReloadPorts Button entfernt
            btnDetectPort = new Button { Text = "Erkennung", Location = new Point(334,130), Size = new Size(100,36), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(245,247,250) };
            btnDetectPort.FlatAppearance.BorderSize = 0;
            btnDetectPort.Click += (s,e)=> StartDetectPortMode();

            var lblAddr = new Label { Text = "SSP Address:", Location = new Point(24, 180), Size = new Size(120, 36), Font = new Font("Segoe UI Variable", 12F) };
            nudAddr = new NumericUpDown { Location = new Point(150, 180), Size = new Size(220, 36), Font = new Font("Segoe UI Variable", 12F), Minimum = 1, Maximum = 255, Value = 16 };

            btnSaveIni = new Button { Text = "Speichern", Location = new Point(24, 230), Size = new Size(120, 44), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(33, 150, 243), ForeColor = Color.White };
            btnSaveIni.FlatAppearance.BorderSize = 0;
            btnSaveIni.Click += (s, e) => SaveIni();

            btnConnectToggle = new Button
            {
                Text = "Events anhängen",
                Location = new Point(160, 230),
                Size = new Size(170, 44),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(33, 150, 243),
                ForeColor = Color.White
            };
            btnConnectToggle.FlatAppearance.BorderSize = 0;
            btnConnectToggle.Click += btnConnectToggle_Click;
            Controls.Add(btnConnectToggle);

            btnEnableToggle = new Button { Text = "Enable", Location = new Point(340, 230), Size = new Size(160, 44), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(46, 125, 50), ForeColor = Color.White };
            btnEnableToggle.FlatAppearance.BorderSize = 0;
            btnEnableToggle.Click += (s, e) =>
            {
                if (_coin == null) return;
                if (!_coin.Connected) { AppendLog("Hinweis: erst verbinden."); return; }
                _enabledRequested = !_enabledRequested;
                try { _coin.Enable(_enabledRequested); } catch { }
                AppendLog(_enabledRequested ? "Enable angefordert." : "Disable angefordert.");
                UpdateEnableButtonVisual();
            };

            btnQueryLevels = new Button { Text = "Bestand abfragen", Location = new Point(520, 230), Size = new Size(180, 44), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(33, 150, 243), ForeColor = Color.White };
            btnQueryLevels.FlatAppearance.BorderSize = 0;
            btnQueryLevels.Click += (s, e) => { QueryLevels(); RefreshBestandCoins(); };

            btnRm5Enable = new Button { Text = "RM5 freigeben", Location = new Point(710, 230), Size = new Size(160, 44), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(255,143,0), ForeColor = Color.White, Visible = false };
            btnRm5Enable.FlatAppearance.BorderSize = 0;
            btnRm5Enable.Click += (s, e) => Rm5Enable();

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
            Controls.Add(btnRm5Enable);
            Controls.Add(txtLog);

            // NEU: Deaktiviert-Checkbox (oben rechts neben Buttons)
            _chkDisabled = new CheckBox
            {
                Text = "Deaktiviert",
                Location = new Point(380, 86), // neben Versions-Auswahl (cmbType at 150,80 size 220)
                AutoSize = true,
                Checked = DeviceDisabled
            };
            _chkDisabled.CheckedChanged += (s, e) =>
            {
                DeviceDisabled = _chkDisabled.Checked;
                try { IniHelper.WriteValue("SmartCoin", "Disabled", DeviceDisabled ? "1" : "0", _iniPath); } catch { }
                ApplyDisabledState();
                UpdateStatusUi();
                try { AdminOverviewFormRefreshSafe(); } catch { }
            };
            Controls.Add(_chkDisabled);

            _panelStatus = new Panel { Location = new Point(560, 80), Size = new Size(300, 170), Anchor = AnchorStyles.Top | AnchorStyles.Right, BackColor = Color.FromArgb(245, 247, 250) };
            Controls.Add(_panelStatus);

            var lblStatusTitle = new Label { Text = "Status", Location = new Point(10, 10), AutoSize = true, Font = new Font("Segoe UI Variable", 13F, FontStyle.Bold) };
            _panelStatus.Controls.Add(lblStatusTitle);
            var lblConn = new Label { Text = "Verbindung:", Location = new Point(20, 44), AutoSize = true, Font = new Font("Segoe UI Variable", 11F) };
            _panelStatus.Controls.Add(lblConn);
            _lblScConn = new Label { Text = "-", Location = new Point(140, 42), AutoSize = true, Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold) };
            _panelStatus.Controls.Add(_lblScConn);
            var lblPortAddr = new Label { Text = "Port/Addr:", Location = new Point(20, 70), AutoSize = true, Font = new Font("Segoe UI Variable", 11F) };
            _panelStatus.Controls.Add(lblPortAddr);
            _lblScPortAddr = new Label { Text = "-", Location = new Point(140, 68), AutoSize = true, Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold) };
            _panelStatus.Controls.Add(_lblScPortAddr);
            var lblLastEvt = new Label { Text = "Letzte Aktivität:", Location = new Point(20, 96), AutoSize = true, Font = new Font("Segoe UI Variable", 11F) };
            _panelStatus.Controls.Add(lblLastEvt);
            _lblScLastEvt = new Label { Text = "-", Location = new Point(140, 94), AutoSize = true, Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold) };
            _panelStatus.Controls.Add(_lblScLastEvt);
            var lblState = new Label { Text = "Zustand:", Location = new Point(20, 118), AutoSize = true, Font = new Font("Segoe UI Variable", 11F) };
            _panelStatus.Controls.Add(lblState);
            _lblScState = new Label { Text = "-", Location = new Point(140, 116), AutoSize = true, Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold) };
            _panelStatus.Controls.Add(_lblScState);

            _panelBestand = new Panel { Location = new Point(560, 290), Size = new Size(300, 460), Anchor = AnchorStyles.Top | AnchorStyles.Right, BackColor = Color.FromArgb(245, 247, 250) };
            Controls.Add(_panelBestand);
            var lblTitel = new Label { Text = "Kassenbestand (Münzen, Stück):", Location = new Point(10, 10), AutoSize = true, Font = new Font("Segoe UI Variable", 13F, FontStyle.Bold) };
            _panelBestand.Controls.Add(lblTitel);
            string[] denomText = { "1 c", "2 c", "5 c", "10 c", "20 c", "50 c", "1 €", "2 €" };
            for (int i = 0; i < denomText.Length; i++)
            {
                var ldenom = new Label { Text = $"{denomText[i],4}:", Location = new Point(20, 50 + i * 38), AutoSize = true, Font = new Font("Segoe UI Variable", 12F) };
                _panelBestand.Controls.Add(ldenom);
                var lval = new Label { Text = "0", Location = new Point(120, 50 + i * 38), Size = new Size(60, 32), TextAlign = ContentAlignment.MiddleRight, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI Variable", 12F, FontStyle.Bold), BackColor = Color.White };
                _panelBestand.Controls.Add(lval);
                _lblBestandAnzCoins[i] = lval;
            }
            // Reposition sum + refresh below last row (avoids overlap with 2€ row)
            int lastRowYCoins = 50 + (denomText.Length - 1) * 38; // y of last denomination line
            int sumYCoins = lastRowYCoins + 38; // one line below
            _lblBestandSumCoins = new Label { Text = "Gesamt: 0,00 €", Location = new Point(20, sumYCoins), AutoSize = true, Font = new Font("Segoe UI Variable", 13F, FontStyle.Bold) };
            _panelBestand.Controls.Add(_lblBestandSumCoins);
            _btnBestandRefresh = new Button { Text = "Aktualisieren", Location = new Point(20, sumYCoins + 34), Size = new Size(110, 38), Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
            _btnBestandRefresh.Click += (s, e) => RefreshBestandCoins();
            _panelBestand.Controls.Add(_btnBestandRefresh);

            _tmrBestand = new Timer { Interval = 3000 };
            _tmrBestand.Tick += (s, e) => RefreshBestandCoins();
            _tmrBestand.Enabled = true;
            _tmrStatus = new Timer { Interval = 1000 };
            _tmrStatus.Tick += (s, e) => UpdateStatusUi();
            _tmrStatus.Enabled = true;

            if (_coin != null)
            {
                txtCom.Text = _coin.ComPort;
                nudAddr.Value = _coin.SspAddress;
            }
            LoadComPorts();
            EnsureSelectedPort(_coin?.ComPort, initial: true); // nur initial setzen
            UpdateEnableButtonVisual();
            UpdateStatusUi();
            RefreshBestandCoins();
            ApplyDisabledState();
        }

        private void OnTypeChanged()
        {
            var sel = cmbType.SelectedItem as string ?? "None";
            bool rm5 = IsRm5(sel);
            nudAddr.Enabled = !rm5;
            // Panel für RM5 NICHT ausblenden, wir zeigen künstlichen Bestand
            _panelBestand.Visible = true;
            btnQueryLevels.Enabled = true;
            btnRm5Enable.Visible = rm5;
            ReplaceInstance(sel);
            ApplyCoin2Mode(); // adjust Coin2 based on new selection
            UpdateStatusUi();
            RefreshBestandCoins();
        }

        private void ApplyCoin2Mode()
        {
            try
            {
                var sel = cmbType.SelectedItem as string ?? "None";
                if (IsRm5(sel) || IsNone(sel))
                {
                    // deactivate second coin device completely
                    Coin2Manager.DisableForConfig();
                    AppendLog("Coin2 deaktiviert (Konfiguration: " + sel + ")");
                }
                else if (IsSmart(sel))
                {
                    // Only enable if INI has a valid type (not None)
                    var typeStr = IniHelper.ReadValue("SmartCoin/2", "Typ", _iniPath)?.Trim() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(typeStr) && !typeStr.Equals("None", System.StringComparison.OrdinalIgnoreCase))
                    {
                        if (Coin2Manager.IsDisabledByConfig)
                        {
                            Coin2Manager.EnableForConfig(_iniPath);
                            AppendLog("Coin2 aktiviert (SmartCoin/2)");
                        }
                    }
                    else
                    {
                        Coin2Manager.DisableForConfig();
                        AppendLog("Coin2 deaktiviert (kein SmartCoin/2 Typ in INI)");
                    }
                }
            }
            catch { }
        }

        private void ReplaceInstance(string sel)
        {
            CoinValidatorType desired = CoinValidatorType.None;
            if (sel == "SmartCoinV1") desired = CoinValidatorType.SmartCoinV1;
            else if (sel == "RM5") desired = CoinValidatorType.Rm5Cctalk;

            bool already =
                (desired == CoinValidatorType.Rm5Cctalk && _coin != null && _coin.GetType().Name == "Rm5CctalkValidator") ||
                ((desired == CoinValidatorType.SmartCoinV1) && _coin is SmartCoinV1) ||
                (desired == CoinValidatorType.None && _coin == null);
            if (already) return;

            try { DetachCoinEvents(); } catch { }
            try
            {
                _coin = CoinValidatorFactory.Create(desired);
                if (_coin != null)
                {
                    _coin.ComPort = txtCom.Text?.Trim();
                    if (!(desired == CoinValidatorType.Rm5Cctalk)) _coin.SspAddress = (int)nudAddr.Value;
                }
            }
            catch (Exception ex) { AppendLog("Instanzwechsel Fehler: " + ex.Message); }
            try { AttachCoinEvents(); } catch { }
        }

        private void LoadIni()
        {
            var typeStr = IniHelper.ReadValue("SmartCoin", "Typ", _iniPath) ?? "None";
            var com = IniHelper.ReadValue("SmartCoin", "ComPort", _iniPath) ?? string.Empty;
            var addr = IniHelper.ReadValue("SmartCoin", "SSPAddress", _iniPath);

            cmbType.SelectedItem =
                typeStr.Equals("V1", StringComparison.OrdinalIgnoreCase) ? "SmartCoinV1" :
                typeStr.Equals("RM5", StringComparison.OrdinalIgnoreCase) ? "RM5" : "None";

            // Immer überschreiben (vorher wurde COM3 aus vorhandener Instanz nicht ersetzt)
            txtCom.Text = com;
            if (int.TryParse(addr, out var a) && a > 0) nudAddr.Value = a;
            OnTypeChanged();
        }

        private void SaveIni()
        {
            var sel = (cmbType.SelectedItem as string) ?? "None";
            CoinValidatorType t = CoinValidatorType.None;
            if (sel == "SmartCoinV1") t = CoinValidatorType.SmartCoinV1;
            else if (sel == "RM5") t = CoinValidatorType.Rm5Cctalk;

            CoinValidatorFactory.SaveToIni(_iniPath, t, txtCom.Text?.Trim(), IsRm5(sel) ? (int?)null : (int)nudAddr.Value);
            if (_coin != null)
            {
                _coin.ComPort = txtCom.Text?.Trim();
                if (!IsRm5(sel)) _coin.SspAddress = (int)nudAddr.Value;
            }
            AppendLog($"INI gespeichert: Typ={sel}, COM={txtCom.Text}, Addr={(IsRm5(sel) ? "-" : nudAddr.Value.ToString())}");
            MessageBox.Show(this, "Einstellungen gespeichert.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void Rm5Enable()
        {
            if (!IsRm5(cmbType.SelectedItem as string)) { AppendLog("RM5 nicht ausgewählt."); return; }

            if (_coin == null || _coin.GetType().Name != "Rm5CctalkValidator") { AppendLog("Instanz ist kein Rm5CctalkValidator."); return; }
            try
            {
                Rm5CctalkValidator rm5 = (Rm5CctalkValidator)_coin;

                if (!(bool)rm5.Connected)
                {
                    rm5.ComPort = txtCom.Text?.Trim();
                    rm5.Connect();

                    AppendLog("RM5 verbunden.");
                }

                rm5.ForceEnableAll();
                rm5.AcceptAllChannels();
                
                _enabledRequested = true; 
                UpdateEnableButtonVisual();
                
                AppendLog("RM5 Kanäle 1-5 freigegeben.");
            }
            catch (Exception ex) { AppendLog("RM5 Freigabe Fehler: " + ex.Message); }
        }

        private void ToggleConnect()
        {
            if (DeviceDisabled)
            {
                AppendLog("Gerät deaktiviert – keine Verbindung.");
                return;
            }
            if (_coin == null)
            {
                MessageBox.Show(this, "Kein M�nzpr�fer-Objekt vorhanden.", "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                if (_coin.Connected)
                {
                    _coin.Disconnect();
                    _enabledRequested = false;
                    UpdateEnableButtonVisual();
                    AppendLog("Getrennt.");
                }
                else
                {
                    _coin.ComPort = txtCom.Text?.Trim();
                    if (!IsRm5(cmbType.SelectedItem as string)) _coin.SspAddress = (int)nudAddr.Value;
                    _coin.Connect();
                    _enabledRequested = true;
                    _coin.Enable(true);
                    UpdateEnableButtonVisual();
                    AppendLog("Verbunden und enabled.");
                    RefreshBestandCoins();
                }
            }
            catch (System.Exception ex)
            {
                AppendLog("Fehler: " + ex.Message);
                MessageBox.Show(this, "Verbindung fehlgeschlagen:\r\n" + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { UpdateStatusUi(); }
        }

        private void RefreshBestandCoins()
        {
            try
            {
                if (DeviceDisabled) return;
                if (_coin == null || !_coin.Connected) return;
                if (!_eventsAttached) return;

                if (_coin is SmartCoinV1 sc1)
                {
                    if (!string.IsNullOrWhiteSpace(sc1.CurrentStatus)) _statusText = sc1.CurrentStatus;
                    if (!IsReadyStatus(_statusText)) return;
                    sc1.RequestCoinLevels();
                    var lv = sc1.GetCoinAvailability();
                    UpdateCoinsUiFromLevels(lv);
                    _didFirstReadyRefresh = true;
                }
                else if (_coin is Rm5CctalkValidator rm5)
                {
                    rm5.RequestCoinLevels();
                    var lv = rm5.GetCoinAvailability();
                    UpdateCoinsUiFromLevels(lv);
                }
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
                if (v > 0) sumCent += v * new[] { 1, 2, 5, 10, 20, 50, 100, 200 }[i];
            }
            if (_lblBestandSumCoins != null)
                _lblBestandSumCoins.Text = $"Gesamt: {(sumCent / 100m):C2}";
        }

        private void QueryLevels()
        {
            if (DeviceDisabled) { AppendLog("Gerät deaktiviert – keine Abfrage."); return; }
            if (_coin == null) { AppendLog("Kein M�nzpr�fer-Objekt vorhanden."); return; }
            if (!_coin.Connected) { AppendLog("Hinweis: erst verbinden."); return; }
            try
            {
                if (_coin is SmartCoinV1 sc1)
                {
                    AppendLog("GET_DENOMINATION_LEVEL anfordern (V1)...");
                    sc1.RequestCoinLevels();
                    UpdateStatusText(sc1.CurrentStatus);
                    var lv = sc1.GetCoinAvailability();
                    AppendLog("Lokale Level (Cache): " + FormatLevels(lv));
                }
                else if (_coin is Rm5CctalkValidator rm5)
                {
                    rm5.RequestCoinLevels();
                    var lv = rm5.GetCoinAvailability();
                    AppendLog("RM5 Levels: " + FormatLevels(lv));
                }
                else
                    AppendLog("Dieses Münzgerät unterstützt die Level-Abfrage nicht.");
            }
            catch (System.Exception ex) { AppendLog("Fehler bei Level-Abfrage: " + ex.Message); }
        }

        private string FormatLevels(int[] lv)
        {
            if (lv == null || lv.Length < 8) return "(keine Daten)";
            string safe(int i) => lv[i] < 0 ? "?" : lv[i].ToString();
            return $"1c={safe(0)}, 2c={safe(1)}, 5c={safe(2)}, 10c={safe(3)}, 20c={safe(4)}, 50c={safe(5)}, 1€={safe(6)}, 2€={safe(7)}";
        }

        private void UpdateEnableButtonVisual()
        {
            btnEnableToggle.Text = _enabledRequested ? "Disable" : "Enable";
            btnEnableToggle.BackColor = _enabledRequested ? Color.FromArgb(183, 28, 28) : Color.FromArgb(46, 125, 50);
        }

        // NEU: verfügbare COM-Ports laden
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
                    if (added.Count > 0) { SelectDetectedPort(added[0]); return; }
                }
                else if (!passive)
                {
                    if (string.IsNullOrWhiteSpace(currentSelection) && newPorts.Count > 0) txtCom.SelectedItem = newPorts[0];
                    else if (!string.IsNullOrWhiteSpace(currentSelection))
                    {
                        int idx = Array.IndexOf(ports, currentSelection);
                        if (idx >= 0) txtCom.SelectedIndex = idx; else txtCom.SelectedIndex = -1;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(currentSelection))
                {
                    int idx = Array.IndexOf(ports, currentSelection);
                    if (idx >= 0) txtCom.SelectedIndex = idx; else txtCom.SelectedIndex = -1;
                }
                if (txtCom.Items.Count == 0) txtCom.SelectedIndex = -1;
                EnsureSelectedPort(_coin?.ComPort, initial: false); // nur hinzufügen
            }
            catch { }
        }

        // NEU: Erkennungsmodus starten
        private void StartDetectPortMode()
        {
            if (_detectingPort)
            {
                StopDetectPortMode(true);
                return;
            }
            try
            {
                _detectBasePorts = SerialPort.GetPortNames();
                Array.Sort(_detectBasePorts, StringComparer.OrdinalIgnoreCase);
                _detectingPort = true;
                btnDetectPort.Text = "Stop";
                // Dialog erstellen
                _detectDialog = new Form
                {
                    Text = "COM-Port Erkennung",
                    Size = new Size(420,140),
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    StartPosition = FormStartPosition.CenterParent,
                    ControlBox = false,
                    TopMost = true
                };
                var lbl = new Label
                {
                    Text = "Bitte stecken Sie jetzt den gewünschten USB / COM Adapter ein...\r\nDas Fenster schließt sich automatisch, sobald ein neuer Port erkannt wurde.",
                    AutoSize = false,
                    Location = new Point(12,12),
                    Size = new Size(380,60)
                };
                _detectDialog.Controls.Add(lbl);
                var btnCancel = new Button { Text = "Abbrechen", Location = new Point(300,78), Size = new Size(90,28) };
                btnCancel.Click += (s,e)=> StopDetectPortMode(true);
                _detectDialog.Controls.Add(btnCancel);
                _detectDialog.Show(this);
                // Timer starten
                _detectTimer = new Timer { Interval = 700 };
                _detectTimer.Tick += (s,e)=> LoadComPorts();
                _detectTimer.Start();
            }
            catch { StopDetectPortMode(true); }
        }

        private void SelectDetectedPort(string port)
        {
            try
            {
                if (!txtCom.Items.Cast<object>().Any(o => string.Equals(Convert.ToString(o), port, StringComparison.OrdinalIgnoreCase)))
                {
                    txtCom.Items.Add(port);
                }
                txtCom.SelectedItem = port;
                try { IniHelper.WriteValue("SmartCoin", "ComPort", port, _iniPath); } catch { }
                try { if (_coin != null) _coin.ComPort = port; } catch { }
                AppendLog("COM-Port erkannt und übernommen: " + port);
            }
            catch { }
            StopDetectPortMode(false);
        }

        private void StopDetectPortMode(bool cancelled)
        {
            try { _detectTimer?.Stop(); } catch { }
            try { _detectTimer = null; } catch { }
            if (_detectDialog != null)
            {
                try { _detectDialog.Close(); } catch { }
                try { _detectDialog.Dispose(); } catch { }
                _detectDialog = null;
            }
            _detectingPort = false;
            btnDetectPort.Text = "Erkennung";
            if (cancelled) AppendLog("COM-Port Erkennung abgebrochen.");
            else AppendLog("Neuer COM-Port erkannt und ausgewählt: " + (txtCom.SelectedItem ?? "?"));
        }

        private void UpdateStatusText(string status)
        {
            if (_lblScState == null) return;
            var s = status ?? "-";
            _lblScState.Text = string.IsNullOrWhiteSpace(s) ? "-" : s;
            var t = (s ?? "").ToLowerInvariant();
            Color c = Color.SteelBlue;
            if (t.Contains("idle") || t.Contains("bereit") || t.Contains("synchron") || t.Contains("verbunden") || t.Contains("handshake ok")) c = Color.FromArgb(0, 128, 0);
            else if (t.Contains("busy") || t.Contains("dispens") || t.Contains("einwurf")) c = Color.FromArgb(255, 140, 0);
            else if (t.Contains("disabled") || t.Contains("störung") || t.Contains("jammed") || t.Contains("timeout") || t.Contains("getrennt") || t.Contains("port nicht verfügbar")) c = Color.FromArgb(183, 28, 28);
            _lblScState.ForeColor = c;
        }

        private void UpdateStatusUi()
        {
            try
            {
                if (DeviceDisabled)
                {
                    _lblScConn.Text = "Deaktiviert";
                    _lblScConn.ForeColor = Color.Gray;
                    _lblScPortAddr.Text = "-";
                    _lblScLastEvt.Text = "-";
                    _lblScState.Text = "disabled";
                    _lblScState.ForeColor = Color.Gray;
                    return;
                }
                bool conn = _coin != null && _coin.Connected;
                _lblScConn.Text = conn ? "Verbunden" : "Getrennt";
                _lblScConn.ForeColor = conn ? Color.FromArgb(0, 128, 0) : Color.FromArgb(183, 28, 28);
                if (_coin != null && _coin.GetType().Name == "Rm5CctalkValidator")
                    _lblScPortAddr.Text = _coin.ComPort + ", RM5";
                else
                    _lblScPortAddr.Text = _coin == null ? "-" : ($"{_coin.ComPort}, Addr {_coin.SspAddress}");
                EnsureSelectedPort(_coin?.ComPort, initial: false);
                if (_lastCoinEventUtc == DateTime.MinValue)
                    _lblScLastEvt.Text = "-";
                else
                {
                    var ago = DateTime.UtcNow - _lastCoinEventUtc;
                    _lblScLastEvt.Text = $"{_lastCoinEventUtc.ToLocalTime():HH:mm:ss} ({Math.Max(0, (int)ago.TotalSeconds)} s)";
                }

                if (_coin is SmartCoinV1 sc1 && !string.IsNullOrWhiteSpace(sc1.CurrentStatus))
                {
                    _statusText = sc1.CurrentStatus; UpdateStatusText(_statusText);
                }
                else if (_coin is Rm5CctalkValidator rm5)
                {
                    _statusText = conn ? "RM5 verbunden" : "RM5 getrennt"; UpdateStatusText(_statusText);
                }

                if (!_didFirstReadyRefresh && _eventsAttached) RefreshBestandCoins();
            }
            catch { }
        }

        // Helfer: aktuellen COM-Port sicher im Dropdown selektieren (ggf. hinzufügen)
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

        private void AppendLog(string s)
        {
            if (InvokeRequired) { BeginInvoke((System.Action)(() => AppendLog(s))); return; }
            txtLog.AppendText($"[{System.DateTime.Now:HH:mm:ss}] {s}\r\n");
        }

        private void HeaderPanel_Paint(object sender, PaintEventArgs e)
        {
            using (var brush = new LinearGradientBrush(headerPanel.ClientRectangle, Color.FromArgb(33, 150, 243), Color.FromArgb(33, 203, 243), 0f))
            { e.Graphics.FillRectangle(brush, headerPanel.ClientRectangle); }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { DetachCoinEvents(); } catch { }
            base.OnFormClosed(e);
        }

        private void AttachCoinEvents()
        {
            if (DeviceDisabled) return;
            if (_coin == null || _eventsAttached) return;
            _coin.EventLog += CoinOnEventLog;
            _coin.CoinAccepted += CoinOnCoinAccepted;
            if (_coin is SmartCoinV1 sc1)
            {
                sc1.CoinLevelsUpdated += CoinOnCoinLevelsUpdated;
                sc1.StatusChanged += CoinOnStatusChanged;
                if (!string.IsNullOrWhiteSpace(sc1.CurrentStatus)) { _statusText = sc1.CurrentStatus; UpdateStatusText(_statusText); }
            }
            else if (_coin is Rm5CctalkValidator rm5)
            {
                rm5.CoinLevelsUpdated += CoinOnCoinLevelsUpdated;
                // RM5 hat kein StatusChanged, StatusText direkt setzen
                _statusText = rm5.Connected ? "RM5 verbunden" : "RM5 getrennt";
                UpdateStatusText(_statusText);
            }
            _eventsAttached = true;
            UpdateConnectButtonVisual();
        }
        private void DetachCoinEvents()
        {
            if (_coin == null || !_eventsAttached) return;
            _coin.EventLog -= CoinOnEventLog;
            _coin.CoinAccepted -= CoinOnCoinAccepted;
            if (_coin is SmartCoinV1 sc1)
            {
                sc1.CoinLevelsUpdated -= CoinOnCoinLevelsUpdated;
                sc1.StatusChanged -= CoinOnStatusChanged;
            }
            else if (_coin is Rm5CctalkValidator rm5)
            {
                rm5.CoinLevelsUpdated -= CoinOnCoinLevelsUpdated;
            }
            _eventsAttached = false;
            UpdateConnectButtonVisual();
        }

        private void btnConnectToggle_Click(object sender, System.EventArgs e)
        {
            if (DeviceDisabled) { AppendLog("Gerät deaktiviert – keine Events."); return; }
            if (_coin == null) { AppendLog("Kein M�nzpr�fer-Objekt vorhanden."); return; }
            if (_eventsAttached)
            {
                DetachCoinEvents();
                AppendLog("Events gelöst.");
            }
            else
            {
                AttachCoinEvents();
                AppendLog("Events angehängt.");
                if (IsReadyStatus(_statusText)) RefreshBestandCoins();
            }
        }

        private void CoinOnEventLog(string s)
        {
            _lastCoinEventUtc = System.DateTime.UtcNow;
            AppendLog(s);
        }

        private void CoinOnCoinAccepted(int c)
        {
            AppendLog($"CoinAccepted: {c} ct");
        }

        private void CoinOnCoinLevelsUpdated(int[] lvls)
        {
            AppendLog("CoinLevelsUpdated: " + FormatLevels(lvls));
            try { BeginInvoke((System.Action)(() => UpdateCoinsUiFromLevels(lvls))); } catch { }
        }

        private void CoinOnStatusChanged(string s)
        {
            try
            {
                _statusText = s;
                BeginInvoke((System.Action)(() => UpdateStatusText(s)));
                if (IsReadyStatus(s) && !_didFirstReadyRefresh && _eventsAttached) BeginInvoke((System.Action)(RefreshBestandCoins));
            }
            catch { }
        }

        private void UpdateConnectButtonVisual()
        {
            if (btnConnectToggle == null) return;
            btnConnectToggle.Text = _eventsAttached ? "Events lösen" : "Events anhängen";
            btnConnectToggle.BackColor = DeviceDisabled ? Color.Gray : (_eventsAttached ? Color.FromArgb(183, 28, 28) : Color.FromArgb(33, 150, 243));
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
    }
}