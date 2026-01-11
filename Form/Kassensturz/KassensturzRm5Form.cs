using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Geldautomat.Coins;

namespace Geldautomat
{
    public class KassensturzRm5Form : Form
    {
        private readonly Rm5CctalkValidator _rm5;
        private int[] _preDialogLevels; // Original Levels beim Öffnen
        private Panel headerPanel;
        private Label lblTitle;
        private Button btnClose;
        private Point _mouseDownLocation;
        private Label[] lblStartBestand;
        private Label lblStartSumme;
        private Label[] lblAktBestand;
        private Label lblAktSumme;
        private Label lblDifferenz;
        private Button btnEntleeren;
        private Button btnAlleEingezahlt;
        private Timer _tmrLevels;
        private bool _emptyStarted = false;
        private int[] _initialLevels;
        private decimal _initialSum;
        private int[] _currentLevels;
        private decimal _currentSum;
        private static readonly int[] NominalCent = { 1,2,5,10,20,50,100,200 };
        private static readonly decimal[] NominalEuro = { 0.01m,0.02m,0.05m,0.10m,0.20m,0.50m,1m,2m };
        private const string SnapshotSection = "RM5Kassensturz";
        private const string SnapshotKey = "SavedLevels";
        private bool _snapshotLoaded = false;
        private Label _lblSnapshotInfo;

        // Neuer Ablaufstatus für 255er Entleerung
        private enum DrainStage { None, FirstRunning, SecondRunning }
        private DrainStage _stage = DrainStage.None;
        private bool[] _secondRunNeeded = new bool[8];
        private int[] _firstRunLevels; // Level direkt vor Start der ersten 255er Runde (für Anzeige / Logik)

        private DateTime _drainStartUtc = DateTime.MinValue; // Safety timeout
        // Konfigurierbare Zeitwerte (nicht const wegen Edit&Continue)
        private int _drainSafetySecondsCfg = 25;            // verkürzt, früheres erzwungenes Finalize
        private int _earlyAllZeroGraceMsCfg = 4000;         // nach 4s mit allen Hopper=0 früh abschließen
        private Timer _tmrSafety;                            // background safety timer

        private bool _uiZeroLocked = false; // Anzeige auf 0 gesperrt nach Start

        // NEU: Einzel-Hopper-Entleerung UI/State
        private Button[] _btnEmptyByCoin = new Button[8];
        private bool _singleDrainInProgress = false;
        private int _singleDrainIdx = -1;
        private int _singleDrainInitialLevel = -1;            // initialer Level vor Start
        private bool _singleSecondRunPending = false;         // zweiter Lauf nötig?
        private bool _singleSecondRunLaunched = false;        // zweiter Lauf bereits gestartet?

        // NEU: Manuelle Anpassung per Tastatur (Strg+M, +/-)
        private bool _manualAdjustMode = false;
        private int _manualAdjustIdx = 3; // 10c als Start
        private Label _lblAdjustHint;

        // Touch: manueller Modus per Button +/- je Münzsorte
        private Button _btnManualAdjust;
        private Button[] _btnMinus = new Button[8];
        private Button[] _btnPlus = new Button[8];

        // Konstruktor
        public KassensturzRm5Form(Rm5CctalkValidator rm5)
        {
            _rm5 = rm5;
            try { if (_rm5 != null) _rm5.SuppressUiNotifications = true; } catch { }
            try { _preDialogLevels = _rm5?.GetCoinAvailability(); } catch { }
            SafeCaptureInitialLevels();
            BuildLayout();
            ShowInitial();
            StartAutoRefresh();
            AttachRm5Events();
            // Tastatursteuerung (für Pfeiltasten zuverlässiger über ProcessCmdKey)
            try { KeyPreview = true; this.KeyDown += KassensturzRm5Form_KeyDown; } catch { }
        }

        private void KassensturzRm5Form_KeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (e.Control && e.KeyCode == Keys.M)
                {
                    ToggleManualAdjust();
                    e.Handled = true;
                }
                else if (_manualAdjustMode && e.KeyCode == Keys.Escape)
                {
                    ToggleManualAdjust(false); e.Handled = true;
                }
            }
            catch { }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Abfangen der Pfeiltasten/Plus/Minus auch wenn ein Button den Fokus hat
            if (_manualAdjustMode)
            {
                if (keyData == Keys.Up)
                { _manualAdjustIdx = Math.Max(3, _manualAdjustIdx - 1); UpdateManualAdjustHighlight(); return true; }
                if (keyData == Keys.Down)
                { _manualAdjustIdx = Math.Min(7, _manualAdjustIdx + 1); UpdateManualAdjustHighlight(); return true; }
                if (keyData == Keys.Add || keyData == Keys.Oemplus)
                { AdjustSelectedLevel(+1); return true; }
                if (keyData == Keys.Subtract || keyData == Keys.OemMinus)
                { AdjustSelectedLevel(-1); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void StartSafetyTimer()
        {
            if (_tmrSafety == null)
            {
                _tmrSafety = new Timer { Interval = 3000 };
                _tmrSafety.Tick += (s, e) => SafetyTick();
            }
            _tmrSafety.Start();
        }
        private async void SafetyTick()
        {
            if (!_emptyStarted) return;
            if (_stage == DrainStage.None) return;
            if (_drainStartUtc == DateTime.MinValue) return;

            if (_currentLevels != null && _currentLevels.Length >= 8)
            {
                bool allZero = true;
                for (int i = 3; i < 8; i++) if (_currentLevels[i] > 0) { allZero = false; break; }
                if (allZero && (DateTime.UtcNow - _drainStartUtc).TotalMilliseconds > _earlyAllZeroGraceMsCfg)
                { try { AppLogger.Log("[RM5] Alle Hopper melden 0 – frühzeitiges Finalize"); } catch { } FinalizeDrain(); return; }
            }

            if ((DateTime.UtcNow - _drainStartUtc).TotalSeconds < _drainSafetySecondsCfg) return;
            try { AppLogger.Log("[RM5] Safety timeout – force finalize Kassensturz"); } catch { }
            try { _rm5?.RequestCoinLevels(); } catch { }
            await Task.Delay(250);
            try { _currentLevels = _rm5?.GetCoinAvailability(); } catch { }
            FinalizeDrain();
        }

        private void AttachRm5Events()
        {
            if (_rm5 == null) return;
            try { _rm5.CoinDispenseComplete += Rm5OnDispenseComplete; } catch { }
            try { _rm5.CoinLevelsUpdated += Rm5OnLevelsUpdated; } catch { }
            try
            {
                _rm5.CoinPayoutError += (hopper, remaining) =>
                {
                    try { BeginInvoke((Action)(() => { try { AppLogger.Log($"[RM5] Hopper {hopper} PayoutError Rest={remaining} -> prüfe Abschluss"); } catch { } })); } catch { }
                };
            }
            catch { }
        }
        private void DetachRm5Events()
        {
            if (_rm5 == null) return;
            try { _rm5.CoinDispenseComplete -= Rm5OnDispenseComplete; } catch { }
            try { _rm5.CoinLevelsUpdated -= Rm5OnLevelsUpdated; } catch { }
        }
        private void Rm5OnLevelsUpdated(int[] lv)
        {
            try
            {
                if (lv == null || lv.Length < 8) return;
                if (_uiZeroLocked) return;
                BeginInvoke((Action)(() =>
                {
                    _currentLevels = (int[])lv.Clone();
                    _currentSum = ComputeSum(_currentLevels);
                    for (int i = 0; i < 8; i++)
                        lblAktBestand[i].Text = string.Format("{0,4:N2} €: {1}", NominalEuro[i], _currentLevels[i] >= 0 ? _currentLevels[i].ToString() : "?");
                    lblAktSumme.Text = $"Summe: {_currentSum:C2}";
                    UpdateDifferenz();

                    // Re-enable single-drain button, aber nur wenn kein zweiter Lauf aussteht
                    if (_singleDrainInProgress && _singleDrainIdx >= 0 && _singleDrainIdx < 8)
                    {
                        try
                        {
                            if (_currentLevels[_singleDrainIdx] <= 0 && !_singleSecondRunPending && !_singleSecondRunLaunched)
                            {
                                _singleDrainInProgress = false;
                                int idx = _singleDrainIdx;
                                _singleDrainIdx = -1;
                                if (btnEntleeren != null) btnEntleeren.Enabled = true;
                                var btn = _btnEmptyByCoin[idx];
                                if (btn != null) btn.Enabled = true;
                            }
                        }
                        catch { }
                    }
                }));
            }
            catch { }
        }
        private void Rm5OnDispenseComplete()
        {
            try
            {
                BeginInvoke((Action)(async () =>
                {
                    if (!_emptyStarted)
                    {
                        // Einzelhopper-Entleerung oder sonstige Aktion
                        try { _rm5?.RequestCoinLevels(); } catch { }
                        await Task.Delay(300);
                        int[] lv = null; try { lv = _rm5?.GetCoinAvailability(); } catch { }
                        if (lv != null && lv.Length >= 8)
                        {
                            _currentLevels = (int[])lv.Clone();
                            _currentSum = ComputeSum(_currentLevels);
                            for (int i = 0; i < 8; i++)
                                lblAktBestand[i].Text = string.Format("{0,4:N2} €: {1}", NominalEuro[i], _currentLevels[i]);
                            lblAktSumme.Text = $"Summe: {_currentSum:C2}";
                            UpdateDifferenz();
                        }

                        // Falls Single-Drain aktiv: zweiten Lauf ggf. automatisch starten
                        if (_singleDrainInProgress && _singleDrainIdx >= 0)
                        {
                            if (_singleSecondRunPending && !_singleSecondRunLaunched)
                            {
                                try
                                {
                                    var req2 = new int[8]; req2[_singleDrainIdx] = 255;
                                    _rm5?.ForcePayoutRaw(req2);
                                    _singleSecondRunLaunched = true;
                                    try { AppLogger.Log($"[RM5] Einzel-Entleerung: zweiter Lauf gestartet Hopper {_singleDrainIdx}"); } catch { }
                                    try { _rm5?.RequestCoinLevels(); } catch { }
                                    return; // auf nächsten Complete warten
                                }
                                catch (Exception ex)
                                {
                                    try { AppLogger.Log("[RM5] Einzel-Entleerung: zweiter Lauf Fehler: " + ex.Message); } catch { }
                                }
                            }

                            // Finalisieren der Einzel-Entleerung: Hopper-Level persistierend auf 0 setzen
                            var doneIdx = _singleDrainIdx;
                            try
                            {
                                int[] cur = null; try { cur = _rm5?.GetCoinAvailability(); } catch { cur = null; }
                                if (cur != null && cur.Length >= 8)
                                {
                                    cur[doneIdx] = 0;
                                    try { _rm5?.SetAllCoinLevels(cur, true); } catch { }
                                    try { AppLogger.Log($"[RM5] Einzel-Entleerung: Hopper {doneIdx} Level persistierend auf 0 gesetzt"); } catch { }
                                }
                            }
                            catch { }

                            // Status zurücksetzen und UI freigeben
                            _singleDrainInProgress = false;
                            _singleDrainIdx = -1;
                            _singleSecondRunPending = false;
                            _singleSecondRunLaunched = false;
                            if (btnEntleeren != null) btnEntleeren.Enabled = true;
                            var b = _btnEmptyByCoin[doneIdx];
                            if (b != null) b.Enabled = true;

                            // Refresh Levels zur Anzeige
                            try { _rm5?.RequestCoinLevels(); } catch { }
                            return;
                        }
                        return;
                    }

                    int[] lv2 = null;
                    try { _rm5?.RequestCoinLevels(); } catch { }
                    await Task.Delay(300);
                    try { lv2 = _rm5?.GetCoinAvailability(); } catch { }
                    if (lv2 != null && lv2.Length >= 8)
                    {
                        _currentLevels = (int[])lv2.Clone();
                        _currentSum = ComputeSum(_currentLevels);
                        for (int i = 0; i < 8; i++)
                            lblAktBestand[i].Text = string.Format("{0,4:N2} €: {1}", NominalEuro[i], _currentLevels[i]);
                        lblAktSumme.Text = $"Summe: {_currentSum:C2}";
                        UpdateDifferenz();
                    }

                    if (_stage == DrainStage.FirstRunning)
                    {
                        bool needSecond = _secondRunNeeded.Any(b => b);
                        if (needSecond)
                        {
                            var req = new int[8]; bool hasAny = false;
                            for (int i = 3; i < 8; i++)
                            {
                                if (_secondRunNeeded[i] && _currentLevels != null && _currentLevels[i] > 0)
                                { req[i] = 255; hasAny = true; }
                            }
                            if (hasAny)
                            {
                                try
                                {
                                    _stage = DrainStage.SecondRunning;
                                    _rm5.ForcePayoutRaw(req); // zweite Runde ebenfalls roh erzwingen
                                    _drainStartUtc = DateTime.UtcNow;
                                    return;
                                }
                                catch (Exception ex)
                                { try { AppLogger.Log("[RM5] Zweiter Batch Fehler (ForcePayoutRaw): " + ex.Message); } catch { } }
                            }
                        }
                        FinalizeDrain();
                        return;
                    }
                    else if (_stage == DrainStage.SecondRunning)
                    {
                        FinalizeDrain();
                        return;
                    }
                }));
            }
            catch { }
        }

        private void FinalizeDrain()
        {
            _stage = DrainStage.None;
            _emptyStarted = false;
            _uiZeroLocked = false;
            if (_tmrSafety != null) { try { _tmrSafety.Stop(); } catch { } }
            try { _rm5?.ResetAllCoinLevelsToZero(true); } catch { }
            try { AppLogger.Log("[RM5] ResetAllCoinLevelsToZero ausgeführt (Finalize)"); } catch { }

            // Physikalische / persistente Bestände jetzt auf 0 setzen
            try { _rm5?.ResetAllCoinLevelsToZero(true); } catch { }
            try { AppLogger.Log("[RM5] ResetAllCoinLevelsToZero ausgeführt (Finalize)"); } catch { }

            // Anzeige rechts auf 0 setzen (aktueller Bestand)
            _currentLevels = new int[8];
            _currentSum = 0m;
            for (int i = 0; i < 8; i++)
                lblAktBestand[i].Text = $"{NominalEuro[i],4:N2} €: 0";
            lblAktSumme.Text = "Summe: 0,00 €";
            UpdateDifferenz();
            btnEntleeren.Enabled = true;
            try { AppLogger.Log("[RM5] Kassensturz abgeschlossen (finalize) – Referenzbestand erhalten"); } catch { }
        }

        private void SafeCaptureInitialLevels()
        {
            if (LoadSnapshotIfExists())
            {
                _snapshotLoaded = true;
                _initialSum = ComputeSum(_initialLevels);
                return;
            }
            int[] lv = null; int tries = 0;
            do
            {
                try { _rm5?.RequestCoinLevels(); } catch { }
                System.Threading.Thread.Sleep(200);
                try { lv = _rm5?.GetCoinAvailability(); } catch { lv = null; }
                tries++;
            } while ((lv == null || lv.Length < 8 || lv.All(v => v <= 0)) && tries < 5);
            _initialLevels = (lv != null && lv.Length >= 8) ? (int[])lv.Clone() : new int[8];
            _initialSum = ComputeSum(_initialLevels);
        }

        private void BuildLayout()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(800, 600);
            BackColor = Color.White;
            DoubleBuffered = true;

            headerPanel = new Panel { Location = new Point(0,0), Size = new Size(ClientSize.Width,60), Anchor = AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right, BackColor = Color.FromArgb(33,150,243) };
            headerPanel.MouseDown += (s,e)=> { if (e.Button==MouseButtons.Left) _mouseDownLocation = e.Location; };
            headerPanel.MouseMove += (s,e)=> { if (e.Button==MouseButtons.Left) { Left += e.X - _mouseDownLocation.X; Top += e.Y - _mouseDownLocation.Y; } };
            Controls.Add(headerPanel);

            lblTitle = new Label { Text = "Kassensturz RM5", AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Variable",18F,FontStyle.Bold), ForeColor = Color.White, Location = new Point(24,0), Size = new Size(400,60), BackColor = Color.Transparent };
            headerPanel.Controls.Add(lblTitle);

            btnClose = new Button { Text = "\u2715", Font = new Font("Segoe UI Symbol",18F,FontStyle.Bold), ForeColor = Color.White, BackColor = Color.Transparent, FlatStyle = FlatStyle.Flat, Size = new Size(48,48), Location = new Point(ClientSize.Width - 56, 6), TabStop = false };
            btnClose.FlatAppearance.BorderSize = 0; btnClose.FlatAppearance.MouseOverBackColor = Color.FromArgb(255,80,80); btnClose.Click += (s,e)=> Close(); headerPanel.Controls.Add(btnClose);

            _lblSnapshotInfo = new Label { Text = string.Empty, Location = new Point(40,60), Size = new Size(720,20), ForeColor = Color.DarkOrange, Font = new Font("Segoe UI",9.5f,FontStyle.Bold), Visible = false }; Controls.Add(_lblSnapshotInfo);

            var lblLinksTitel = new Label { Text = "Bestand vor Entleerung:", Location = new Point(40,80), Size = new Size(260,30), Font = new Font("Segoe UI",13F,FontStyle.Bold) }; Controls.Add(lblLinksTitel);
            lblStartBestand = new Label[8]; for (int i=0;i<8;i++){ var l = new Label { Text = $"{NominalEuro[i],4:N2} €: -", Location = new Point(60,120 + i*32), Size = new Size(150,28), Font = new Font("Segoe UI",12F) }; Controls.Add(l); lblStartBestand[i]=l; }
            lblStartSumme = new Label { Text = "Summe: -", Location = new Point(60,390), Size = new Size(180,32), Font = new Font("Segoe UI",13F,FontStyle.Bold) }; Controls.Add(lblStartSumme);

            var lblRechtsTitel = new Label { Text = "Aktueller Bestand:", Location = new Point(560,80), Size = new Size(200,30), Font = new Font("Segoe UI",13F,FontStyle.Bold) }; Controls.Add(lblRechtsTitel);
            lblAktBestand = new Label[8]; for (int i=0;i<8;i++){ var l = new Label { Text = $"{NominalEuro[i],4:N2} €: -", Location = new Point(580,120 + i*32), Size = new Size(150,28), Font = new Font("Segoe UI",12F) }; Controls.Add(l); lblAktBestand[i]=l; }
            lblAktSumme = new Label { Text = "Summe: -", Location = new Point(580,390), Size = new Size(180,32), Font = new Font("Segoe UI",13F,FontStyle.Bold) }; Controls.Add(lblAktSumme);

            btnEntleeren = new Button { Text = "Münzen entleeren", Location = new Point(300,180), Size = new Size(200,60), Font = new Font("Segoe UI Variable",15F,FontStyle.Bold), BackColor = Color.FromArgb(33,150,243), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnEntleeren.FlatAppearance.BorderSize = 0; btnEntleeren.Click += BtnEntleeren_Click; Controls.Add(btnEntleeren);

            // NEU: Einzel-Entleerung je Münzsorte (10c..2€)
            var lblEinzeln = new Label { Text = "Einzeln entleeren:", Location = new Point(300,260), Size = new Size(200,24), Font = new Font("Segoe UI",11F,FontStyle.Bold) };
            Controls.Add(lblEinzeln);
            int yBtn = 290;
            for (int i = 3; i < 8; i++)
            {
                var b = new Button
                {
                    Text = $"{NominalEuro[i]:0.00} € entleeren",
                    Location = new Point(300, yBtn),
                    Size = new Size(200, 32),
                    Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                    BackColor = Color.FromArgb(96, 125, 139),
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Tag = i
                };
                b.FlatAppearance.BorderSize = 0;
                b.Click += BtnEmptySingle_Click;
                Controls.Add(b);
                _btnEmptyByCoin[i] = b;
                yBtn += 36;
            }

            // Differenz weiter nach oben schieben, um Platz zu schaffen
            lblDifferenz = new Label { Text = "Differenz: -", Location = new Point(300,460), Size = new Size(200,40), Font = new Font("Segoe UI",15F,FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter }; Controls.Add(lblDifferenz);

            // Touch: Button zum Umschalten in den manuellen Anpassungsmodus – ganz oben über 'Münzen entleeren' platzieren
            _btnManualAdjust = new Button { Text = "Manuell anpassen", Location = new Point(300, 130), Size = new Size(200, 40), Font = new Font("Segoe UI Variable", 12F, FontStyle.Bold), BackColor = Color.FromArgb(96, 125, 139), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            _btnManualAdjust.FlatAppearance.BorderSize = 0; _btnManualAdjust.Click += (s, e) => ToggleManualAdjust(); Controls.Add(_btnManualAdjust);

            btnAlleEingezahlt = new Button { Text = "Alle Münzen eingezahlt", Location = new Point(270,550), Size = new Size(260,40), Font = new Font("Segoe UI Variable",12F,FontStyle.Bold), BackColor = Color.FromArgb(46,125,50), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnAlleEingezahlt.FlatAppearance.BorderSize = 0; btnAlleEingezahlt.Click += BtnAlleEingezahlt_Click; Controls.Add(btnAlleEingezahlt);

            this.Paint += (s,e)=>{ using(var pen=new Pen(Color.FromArgb(120,120,120),2)){ e.Graphics.DrawRectangle(pen,1,1,ClientSize.Width-3,ClientSize.Height-3);} };
            if (_snapshotLoaded) ShowSnapshotInfo();
        }

        private void ShowSnapshotInfo(){ if (_lblSnapshotInfo!=null){ _lblSnapshotInfo.Text = "Aktiver RM5 Snapshot – 'Münzen entleeren' setzt keinen neuen Startbestand. Für Neustart 'Alle Münzen eingezahlt' wählen."; _lblSnapshotInfo.Visible = true; } }
        private void HideSnapshotInfo(){ if (_lblSnapshotInfo!=null){ _lblSnapshotInfo.Visible=false; _lblSnapshotInfo.Text=string.Empty; } }
        private void ShowInitial(){ for (int i=0;i<8;i++){ int v = (_initialLevels!=null && i<_initialLevels.Length)?_initialLevels[i]:-1; lblStartBestand[i].Text = $"{NominalEuro[i],4:N2} €: {(v>=0? v.ToString():"?")}"; } lblStartSumme.Text = $"Summe: {_initialSum:C2}"; UpdateDifferenz(); }
        private void StartAutoRefresh(){ _tmrLevels = new Timer { Interval = 3000 }; _tmrLevels.Tick += async (s,e)=> await RefreshLevelsAsync(); _tmrLevels.Enabled = true; _ = RefreshLevelsAsync(); }
        private async Task RefreshLevelsAsync(){ if (_rm5==null) return; if(_stage!=DrainStage.None) return; // Während Abfluss UI nicht dauernd überschreiben
            try { _rm5.RequestCoinLevels(); } catch { } await Task.Delay(250); int[] lv=null; try { lv=_rm5.GetCoinAvailability(); } catch { } if (lv!=null && lv.Length>=8){ _currentLevels=(int[])lv.Clone(); _currentSum=ComputeSum(_currentLevels); for(int i=0;i<8;i++){ lblAktBestand[i].Text=$"{NominalEuro[i],4:N2} €: {(_currentLevels[i]>=0?_currentLevels[i].ToString():"?" )}"; } lblAktSumme.Text=$"Summe: {_currentSum:C2}"; if(!_snapshotLoaded && !_emptyStarted){ _initialLevels=(int[])_currentLevels.Clone(); _initialSum=_currentSum; ShowInitial(); } UpdateDifferenz(); } }
        private decimal ComputeSum(int[] lv){ if (lv==null||lv.Length<8) return 0m; long cent=0; for(int i=0;i<8;i++) if (lv[i]>0) cent += (long)lv[i]*NominalCent[i]; return cent/100m; }
        private void UpdateDifferenz(){ decimal diff=_currentSum - _initialSum; lblDifferenz.Text=$"Differenz: {diff:C2}"; }

        private async void BtnEntleeren_Click(object sender, EventArgs e)
        {
            if (_emptyStarted)
            {
                if (MessageBox.Show(this, "Erneut entleeren?", "Bestätigung", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            }
            if (!_snapshotLoaded)
            {
                SaveSnapshot(_initialLevels ?? new int[8]);
                _snapshotLoaded = true; ShowSnapshotInfo();
            }
            try
            {
                int[] lv = null; try { _rm5.RequestCoinLevels(); } catch { } await Task.Delay(300); try { lv = _rm5.GetCoinAvailability(); } catch { lv = null; }
                if (lv==null || lv.Length<8){ MessageBox.Show(this,"Aktuelle RM5 Levels unbekannt.","Hinweis",MessageBoxButtons.OK,MessageBoxIcon.Information); return; }

                _firstRunLevels = (int[])lv.Clone();
                for (int i = 0; i < 8; i++)
                    _secondRunNeeded[i] = (i >= 3) && _firstRunLevels[i] > 200; // Nur echte Hopper (10c..2€) prüfen

                var req = new int[8];
                // ERSTE RUNDE: immer 255 pro Hopper (10c..2€) anfordern – ForcePayoutRaw ignoriert interne Levels
                for(int i=0;i<8;i++)
                {
                    if (i >= 3) req[i] = 255; else req[i] = 0;
                }

                try { _rm5.ForcePayoutRaw(req); }
                catch (Exception ex){ MessageBox.Show(this,"Fehler beim Start der Entleerung: "+ex.Message,"Fehler",MessageBoxButtons.OK,MessageBoxIcon.Error); return; }

                // UI sofort auf 0 setzen & sperren
                _uiZeroLocked = true;
                _currentLevels = new int[8];
                _currentSum = 0m;
                for (int i = 0; i < 8; i++)
                    lblAktBestand[i].Text = $"{NominalEuro[i],4:N2} €: 0";
                lblAktSumme.Text = "Summe: 0,00 €";
                UpdateDifferenz();

                _emptyStarted = true; btnEntleeren.Enabled = false; _stage = DrainStage.FirstRunning; _drainStartUtc = DateTime.UtcNow; StartSafetyTimer();
                try { AppLogger.Log("[RM5] Kassensturz gestartet – ForcePayoutRaw Batch 255 (UI sofort auf 0)"); } catch { }
            }
            catch (Exception ex){ MessageBox.Show(this,"Entleerung fehlgeschlagen: "+ex.Message,"Fehler",MessageBoxButtons.OK,MessageBoxIcon.Error); }
        }

        private void BtnEmptySingle_Click(object sender, EventArgs e)
        {
            if (!(sender is Button b) || b.Tag == null) return;
            int idx = (int)b.Tag;
            if (idx < 3 || idx > 7)
            {
                MessageBox.Show(this, "RM5 unterstützt Einzelentleerung für 10c..2€.", "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (_emptyStarted || _stage != DrainStage.None)
            {
                MessageBox.Show(this, "Globaler Kassensturz läuft. Bitte warten.", "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (_singleDrainInProgress)
            {
                MessageBox.Show(this, "Es läuft bereits eine Einzel-Entleerung.", "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                // WICHTIG: vor Einzel-Entleerung Snapshot anlegen, damit linker Referenzbestand erhalten bleibt
                if (!_snapshotLoaded)
                {
                    try { SaveSnapshot(_initialLevels ?? new int[8]); _snapshotLoaded = true; ShowSnapshotInfo(); } catch { }
                }

                // Initialen Level optional erfassen (nur für 2. Lauf-Entscheidung), aber nicht blockieren
                int level = -1;
                try { var lv = _rm5?.GetCoinAvailability(); if (lv != null && lv.Length > idx) level = lv[idx]; } catch { }

                _singleDrainInProgress = true;
                _singleDrainIdx = idx;
                _singleDrainInitialLevel = level;
                _singleSecondRunPending = level > 200; // wenn vorher >200, zweiten Lauf vormerken
                _singleSecondRunLaunched = false;

                b.Enabled = false;
                if (btnEntleeren != null) btnEntleeren.Enabled = false; // während Einzelentleerung globalen Start sperren

                var req = new int[8]; req[idx] = 255; // IMMER 255 senden, egal wie der Bestand ist
                try { _rm5?.ForcePayoutRaw(req); }
                catch (Exception ex) { _singleDrainInProgress = false; _singleDrainIdx = -1; _singleSecondRunPending = false; _singleSecondRunLaunched = false; b.Enabled = true; if (btnEntleeren!=null) btnEntleeren.Enabled = true; MessageBox.Show(this, "Start fehlgeschlagen: " + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }

                // Nach Start Levels anfordern, UI aktualisiert sich im Event
                try { _rm5?.RequestCoinLevels(); } catch { }
            }
            catch (Exception ex)
            {
                _singleDrainInProgress = false; _singleDrainIdx = -1; _singleSecondRunPending = false; _singleSecondRunLaunched = false;
                try { b.Enabled = true; } catch { }
                if (btnEntleeren != null) btnEntleeren.Enabled = true;
                MessageBox.Show(this, "Einzel-Entleerung fehlgeschlagen: " + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnAlleEingezahlt_Click(object sender, EventArgs e)
        {
            // Benutzer bestätigt, dass alle vorhandenen Münzen als eingezahlt gelten
            try
            {
                // Aktuelle Levels ermitteln (rechter Bestand)
                int[] cur = null;
                try { cur = (_currentLevels != null && _currentLevels.Length >= 8) ? (int[])_currentLevels.Clone() : null; } catch { cur = null; }
                if (cur == null)
                {
                    try { _rm5?.RequestCoinLevels(); } catch { }
                    System.Threading.Thread.Sleep(300);
                    try { cur = _rm5?.GetCoinAvailability(); } catch { cur = null; }
                }
                if (cur == null || cur.Length < 8)
                {
                    MessageBox.Show(this, "Aktueller RM5-Bestand ist unbekannt. Bitte erneut versuchen.", "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // WICHTIG: Beim RM5 den rechten (aktuellen) Bestand als Geräte-/Referenzbestand übernehmen
                try { _rm5?.SetAllCoinLevels(cur, true); } catch { }
                try { AppLogger.Log("[RM5] Manuell: Alle Münzen eingezahlt -> SetAllCoinLevels(current, persist)"); } catch { }

                // Reset / Neubeginn: Snapshot aufheben und linken Referenzbestand auf aktuellen setzen
                ClearSnapshot();
                _snapshotLoaded = false;
                _emptyStarted = false;
                _stage = DrainStage.None;
                Array.Clear(_secondRunNeeded, 0, _secondRunNeeded.Length);
                _uiZeroLocked = false;

                _initialLevels = (int[])cur.Clone();
                _initialSum = ComputeSum(_initialLevels);

                // UI aktualisieren: Links = Referenz (neu), Rechts = aktuell (unverändert)
                ShowInitial();
                HideSnapshotInfo();
                btnEntleeren.Enabled = true;

                // Rechts (aktuelle Anzeige) mit cur synchronisieren
                _currentLevels = (int[])cur.Clone();
                _currentSum = ComputeSum(_currentLevels);
                for (int i = 0; i < 8; i++)
                    lblAktBestand[i].Text = $"{NominalEuro[i],4:N2} €: {_currentLevels[i]}";
                lblAktSumme.Text = $"Summe: {_currentSum:C2}";
                UpdateDifferenz(); // sollte jetzt 0,00 € zeigen
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Fehler beim Übernehmen des aktuellen Bestands: " + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SaveSnapshot(int[] levels){ if (levels==null||levels.Length<8) return; try{ string payload=string.Join(",",levels.Select(v=> v<0? -1: v)); string line=$"v1;{DateTime.UtcNow.Ticks};{payload}"; IniHelper.WriteValue(SnapshotSection, SnapshotKey, line, AppSettings.IniPath); try{ AppLogger.Log("[RM5] Snapshot gespeichert: "+line);}catch{} }catch{} }
        private bool LoadSnapshotIfExists(){ try{ string line=IniHelper.ReadValue(SnapshotSection, SnapshotKey, AppSettings.IniPath); if(string.IsNullOrWhiteSpace(line)) return false; var parts=line.Split(';'); if(parts.Length<3) return false; var lvCsv=string.Join(";",parts.Skip(2)); var lvParts=lvCsv.Split(','); if(lvParts.Length<8) return false; _initialLevels=new int[8]; for(int i=0;i<8;i++){ if(!int.TryParse(lvParts[i],out var v)) v=-1; _initialLevels[i]=v; } try{ AppLogger.Log("[RM5] Snapshot geladen: "+line);}catch{} return true; }catch{ return false; } }
        private void ClearSnapshot(){ try{ IniHelper.WriteValue(SnapshotSection, SnapshotKey, string.Empty, AppSettings.IniPath);}catch{} try{ AppLogger.Log("[RM5] Snapshot gelöscht"); }catch{} }

        // Close-Prompt: Speichern oder verwerfen
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel) return;
            try
            {
                // Aktuelle Levels holen
                int[] cur = null; try { cur = _rm5?.GetCoinAvailability(); } catch { }
                bool allZero = cur != null && cur.Length >= 8 && cur.Skip(3).All(v => v == 0); // echte Hopper 10c..2€ prüfen
                // Wenn bereits alle 0 UND differenz angezeigt – trotzdem fragen ob behalten
                if (_preDialogLevels != null && cur != null)
                {
                    bool changed = false;
                    for (int i = 0; i < Math.Min(_preDialogLevels.Length, cur.Length); i++)
                    {
                        if (_preDialogLevels[i] != cur[i]) { changed = true; break; }
                    }
                    if (changed)
                    {
                        var dr = MessageBox.Show(this,
                            "Neue Münzbestände speichern?\r\n\rJa = neue (aktuellen) Stand dauerhaft übernehmen.\rNein = ursprüngliche Werte wiederherstellen.",
                            "RM5 Kassensturz", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                        if (dr == DialogResult.Cancel)
                        {
                            e.Cancel = true; return;
                        }
                        if (dr == DialogResult.Yes)
                        {
                            try { _rm5?.SetAllCoinLevels(cur, true); } catch { }
                            try { AppLogger.Log("[RM5] Kassensturz: neue Levels gespeichert beim Schließen"); } catch { }
                        }
                        else if (dr == DialogResult.No)
                        {
                            try { _rm5?.SetAllCoinLevels(_preDialogLevels, true); } catch { }
                            try { AppLogger.Log("[RM5] Kassensturz: ursprüngliche Levels wiederhergestellt beim Schließen"); } catch { }
                        }
                    }
                }
            }
            catch { }
        }

        protected override void OnFormClosed(FormClosedEventArgs e){ try{ _tmrLevels?.Stop(); }catch{} try{ _tmrLevels?.Dispose(); }catch{} if(_tmrSafety!=null){ try{ _tmrSafety.Stop(); }catch{} try{ _tmrSafety.Dispose(); }catch{} } DetachRm5Events(); try{ if(_rm5!=null) _rm5.SuppressUiNotifications=false; }catch{} base.OnFormClosed(e); }

        private void EnsureAdjustHint()
        {
            if (_lblAdjustHint != null) return;
            _lblAdjustHint = new Label
            {
                Text = "Manuell (Strg+M): +/- ändern, Pfeile wechseln, Esc beendet",
                AutoSize = false,
                Width = 520,
                Height = 24,
                Left = 260,
                Top = 80,
                ForeColor = Color.FromArgb(255, 143, 0),
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };
            try { Controls.Add(_lblAdjustHint); _lblAdjustHint.BringToFront(); } catch { }
        }

        private void UpdateManualAdjustHighlight()
        {
            try
            {
                for (int i = 0; i < 8; i++)
                {
                    if (lblAktBestand == null || i >= lblAktBestand.Length || lblAktBestand[i] == null) continue;
                    if (_manualAdjustMode && i == _manualAdjustIdx)
                    {
                        lblAktBestand[i].BackColor = Color.FromArgb(255, 249, 196); // hellgelb
                        lblAktBestand[i].Font = new Font(lblAktBestand[i].Font, FontStyle.Bold);
                    }
                    else
                    {
                        lblAktBestand[i].BackColor = Color.Transparent;
                        lblAktBestand[i].Font = new Font("Segoe UI", 12f, FontStyle.Regular);
                    }
                }
            }
            catch { }
        }

        private void AdjustSelectedLevel(int delta)
        {
            try
            {
                if (_emptyStarted || _singleDrainInProgress || _uiZeroLocked) return; // während Entleerung gesperrt
                int idx = _manualAdjustIdx;
                if (idx < 3 || idx > 7) return; // nur echte Hopper

                int[] cur = null;
                try { cur = _rm5?.GetCoinAvailability(); } catch { cur = null; }
                if (cur == null || cur.Length < 8)
                    cur = (_currentLevels != null && _currentLevels.Length >= 8) ? (int[])_currentLevels.Clone() : new int[8];

                int oldVal = cur[idx] < 0 ? 0 : cur[idx];
                int newVal = Math.Max(0, oldVal + delta);
                if (newVal == oldVal) return;
                cur[idx] = newVal;

                try { _rm5?.SetAllCoinLevels(cur, true); } catch { }

                _currentLevels = (int[])cur.Clone();
                _currentSum = ComputeSum(_currentLevels);
                if (lblAktBestand != null && lblAktBestand.Length > idx && lblAktBestand[idx] != null)
                    lblAktBestand[idx].Text = string.Format("{0,4:N2} €: {1}", NominalEuro[idx], newVal);
                if (lblAktSumme != null) lblAktSumme.Text = $"Summe: {_currentSum:C2}";
                UpdateDifferenz();

                try { _rm5?.RequestCoinLevels(); } catch { }
            }
            catch { }
        }

        // Touch-Helfer: Umschalten der manuellen Anpassung inkl. +/- Buttons
        private void ToggleManualAdjust() { ToggleManualAdjust(null); }
        private void ToggleManualAdjust(bool on) { ToggleManualAdjust((bool?)on); }
        private void ToggleManualAdjust(bool? on)
        {
            bool target = on ?? !_manualAdjustMode;
            if (target && (_emptyStarted || _singleDrainInProgress || _uiZeroLocked))
            {
                try { MessageBox.Show(this, "Während einer Entleerung sind manuelle Anpassungen gesperrt.", "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information); } catch { }
                return;
            }

            _manualAdjustMode = target;
            if (_manualAdjustMode && (_manualAdjustIdx < 3 || _manualAdjustIdx > 7)) _manualAdjustIdx = 3;

            EnsureAdjustHint();
            if (_lblAdjustHint != null) _lblAdjustHint.Visible = _manualAdjustMode;
            EnsureAdjustButtons();
            SetAdjustButtonsVisible(_manualAdjustMode);
            UpdateManualAdjustHighlight();
        }

        private void EnsureAdjustButtons()
        {
            try
            {
                for (int i = 3; i < 8; i++)
                {
                    int y = (lblAktBestand != null && lblAktBestand.Length > i && lblAktBestand[i] != null) ? lblAktBestand[i].Location.Y : (120 + i * 32);

                    if (_btnMinus[i] == null)
                    {
                        var btn = new Button
                        {
                            Text = "–",
                            Location = new Point(540, y),
                            Size = new Size(32, 28),
                            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                            BackColor = Color.FromArgb(239, 83, 80),
                            ForeColor = Color.White,
                            FlatStyle = FlatStyle.Flat,
                            Tag = i
                        };
                        btn.FlatAppearance.BorderSize = 0;
                        btn.Click += (s, e) => { try { var idx = (int)((Button)s).Tag; _manualAdjustIdx = idx; AdjustSelectedLevel(-1); } catch { } };
                        Controls.Add(btn);
                        _btnMinus[i] = btn; _btnMinus[i].Visible = false;
                    }

                    if (_btnPlus[i] == null)
                    {
                        var btn = new Button
                        {
                            Text = "+",
                            Location = new Point(740, y),
                            Size = new Size(32, 28),
                            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                            BackColor = Color.FromArgb(46, 125, 50),
                            ForeColor = Color.White,
                            FlatStyle = FlatStyle.Flat,
                            Tag = i
                        };
                        btn.FlatAppearance.BorderSize = 0;
                        btn.Click += (s, e) => { try { var idx = (int)((Button)s).Tag; _manualAdjustIdx = idx; AdjustSelectedLevel(+1); } catch { } };
                        Controls.Add(btn);
                        _btnPlus[i] = btn; _btnPlus[i].Visible = false;
                    }
                }
            }
            catch { }
        }

        private void SetAdjustButtonsVisible(bool vis)
        {
            try
            {
                for (int i = 3; i < 8; i++)
                {
                    if (_btnMinus[i] != null) _btnMinus[i].Visible = vis;
                    if (_btnPlus[i] != null) _btnPlus[i].Visible = vis;
                }
            }
            catch { }
        }
    }
}
