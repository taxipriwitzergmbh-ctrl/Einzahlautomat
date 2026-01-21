using System;
using System.Drawing;
using System.Windows.Forms;
using System.Linq; // hinzugefügt für Enumerable

namespace TaMi_Einzahlautomat
{
    public class KassensturzNV200 : Form
    {
        private NV200_SSP _ssp;
        private TabControl tabControl;
        private TabPage tabCashbox;
        private TabPage tabPayout;
        private CashboxCountForm cashboxForm;
        private Panel payoutPanel;
        private Label[] lblLinksBestand;
        private Label[] lblRechtsBestand;
        private Label lblLinksSumme;
        private Label lblRechtsSumme;
        private Label lblDifferenz; // nur noch eine Differenz-Anzeige (initial - live)
        private Button btnAlleAuszahlen;
        private Button btnInCashboxFahren;
        private Button btnAlleEingezahlt;
        private int[] initialPayout;
        private decimal initialPayoutSum;
        private int[] livePayout;
        private decimal livePayoutSum;

        private Panel headerPanel;
        private Label lblTitle;
        private Label _lblHeaderStatus; // Statusanzeige im Header
        private Button btnClose;
        private Point _mouseDownLocation;
        private Timer _tmrPayout;

        // Event-Handler als Felder, damit sie entfernt werden k?nnen
        private Action<int> _onNoteAccepted;
        private Action<int> _onDispensingComplete;
        private Action<int> _onWertDispensing;
        private Action<int> _onCashboxReplaced;
        private Action _onCashboxRemoved;
        private Action<string> _onSspEvent;

        // Status-Update
        private Timer _tmrStatus;

        private const string SnapshotSection = "KassensturzNV200";
        private bool _snapshotLoaded = false;
        private Label _lblSnapshotInfo; // Hinweislabel
        private string _snapshotKey; // dynamischer Key (pro Ger?t)

        // Original MAX-Werte sichern um w?hrend des Kassensturzes alles in den Payout zu routen
        private int _origMax5, _origMax10, _origMax20, _origMax50, _origMax100, _origMax200, _origMax500;

        // Merker für Startlog (nur einmal)
        private bool _startLogged = false;

        // Laufzeitdarstellung während Auszahlung
        private bool _dispensingActive = false;
        private int[] _dispenseViewCounts = null; // gespiegelt/erwartete Payout-Bestände (während Auszahlung)

        public KassensturzNV200(NV200_SSP ssp)
        {
            _ssp = ssp;
            try { string cp = _ssp?.ComPort ?? "Unknown"; _snapshotKey = "Snapshot_" + cp.Trim(); } catch { _snapshotKey = "Snapshot_Unknown"; }
            LoadSnapshotIfExists();
            InitializeLayout();
            _tmrPayout = new Timer { Interval = 3000 }; _tmrPayout.Tick += (s, e) => RefreshPayout(); _tmrPayout.Enabled = true;
            _tmrStatus = new Timer { Interval = 1000 }; _tmrStatus.Tick += (s, e) => UpdateStatusLabel(); _tmrStatus.Start(); UpdateStatusLabel();
            if (_ssp != null)
            {
                _onNoteAccepted = (wert) => { try { BeginInvoke((Action)RefreshPayout); } catch { } };
                _onDispensingComplete = (wert) =>
                {
                    try
                    {
                        BeginInvoke((Action)(() =>
                        {
                            _dispensingActive = false;
                            _dispenseViewCounts = null;
                            if (btnAlleAuszahlen != null) btnAlleAuszahlen.Enabled = true;
                            RefreshPayout();
                        }));
                    }
                    catch { }
                };
                _onWertDispensing = (deltaCent) =>
                {
                    try { BeginInvoke((Action)(() => OnWertDispensing(deltaCent))); } catch { }
                };
                _onCashboxReplaced = (wert) => { try { BeginInvoke((Action)RefreshPayout); } catch { } };
                _onCashboxRemoved = () => { };
                _ssp.Note_akzepted += _onNoteAccepted;
                _ssp.Dispensing_Complete += _onDispensingComplete;
                try { _ssp.Wert_Dispensing += _onWertDispensing; } catch { }
                _ssp.Cashbox_Replaced += _onCashboxReplaced;
                _ssp.Cashbox_Removed += _onCashboxRemoved;
                _onSspEvent = (msg) => { try { BeginInvoke((Action)UpdateStatusLabel); } catch { } }; try { _ssp.Ereignis += _onSspEvent; } catch { }
                ElevateMaxCounts();
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try
            {
                AppLogger.KassensturzScopeEnter();
                if (!_startLogged)
                {
                    initialPayout = GetPayoutCounts();
                    initialPayoutSum = BerechneSumme(initialPayout);
                    AppLogger.Log($"[Kassensturz/NV200] Start Payout-Bestand: {FormatCounts(initialPayout)} = {initialPayoutSum:0.00} €");
                    _startLogged = true;
                }
            }
            catch { }
        }

        private string FormatCounts(int[] c)
        {
            if (c == null || c.Length < 7) return "-";
            int[] w = {5,10,20,50,100,200,500};
            try { return string.Join(",", Enumerable.Range(0,7).Select(i => w[i] + "€=" + c[i])); } catch { return "-"; }
        }

        private void ElevateMaxCounts()
        {
            if (_ssp == null) return;
            _origMax5 = _ssp.MAX_PayoutCount_of_5Euro; _origMax10 = _ssp.MAX_PayoutCount_of_10Euro; _origMax20 = _ssp.MAX_PayoutCount_of_20Euro; _origMax50 = _ssp.MAX_PayoutCount_of_50Euro; _origMax100 = _ssp.MAX_PayoutCount_of_100Euro; _origMax200 = _ssp.MAX_PayoutCount_of_200Euro; _origMax500 = _ssp.MAX_PayoutCount_of_500Euro;
            int huge = 1_000_000; _ssp.MAX_PayoutCount_of_5Euro = huge; _ssp.MAX_PayoutCount_of_10Euro = huge; _ssp.MAX_PayoutCount_of_20Euro = huge; _ssp.MAX_PayoutCount_of_50Euro = huge; _ssp.MAX_PayoutCount_of_100Euro = huge; _ssp.MAX_PayoutCount_of_200Euro = huge; _ssp.MAX_PayoutCount_of_500Euro = huge; try { _ssp.Set_Routing(); } catch { }
        }
        private void RestoreMaxCounts()
        { if (_ssp == null) return; _ssp.MAX_PayoutCount_of_5Euro = _origMax5; _ssp.MAX_PayoutCount_of_10Euro = _origMax10; _ssp.MAX_PayoutCount_of_20Euro = _origMax20; _ssp.MAX_PayoutCount_of_50Euro = _origMax50; _ssp.MAX_PayoutCount_of_100Euro = _origMax100; _ssp.MAX_PayoutCount_of_200Euro = _origMax200; _ssp.MAX_PayoutCount_of_500Euro = _origMax500; try { _ssp.Set_Routing(); } catch { } }

        private void InitializeLayout()
        {
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.CenterScreen; ClientSize = new Size(800, 600); BackColor = Color.White; DoubleBuffered = true;
            headerPanel = new Panel { Location = new Point(0, 0), Size = new Size(ClientSize.Width, 60), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, BackColor = Color.FromArgb(33, 150, 243) }; headerPanel.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) _mouseDownLocation = e.Location; }; headerPanel.MouseMove += (s, e) => { if (e.Button == MouseButtons.Left) { Left += e.X - _mouseDownLocation.X; Top += e.Y - _mouseDownLocation.Y; } }; Controls.Add(headerPanel); headerPanel.BringToFront();
            lblTitle = new Label { Text = "Kassensturz NV200", AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Variable", 18F, FontStyle.Bold), ForeColor = Color.White, Location = new Point(24, 0), Size = new Size(360, 60), BackColor = Color.Transparent }; headerPanel.Controls.Add(lblTitle);
            _lblHeaderStatus = new Label { Text = "Status: -", AutoSize = false, TextAlign = ContentAlignment.MiddleRight, Font = new Font("Segoe UI", 12F, FontStyle.Bold), ForeColor = Color.White, BackColor = Color.Transparent, Location = new Point(ClientSize.Width - 320, 0), Size = new Size(220, 60), Anchor = AnchorStyles.Top | AnchorStyles.Right }; headerPanel.Controls.Add(_lblHeaderStatus);
            btnClose = new Button { Text = "\u2715", Font = new Font("Segoe UI Symbol", 18F, FontStyle.Bold), ForeColor = Color.White, BackColor = Color.Transparent, FlatStyle = FlatStyle.Flat, Size = new Size(48, 48), Location = new Point(ClientSize.Width - 56, 6), TabStop = false, Anchor = AnchorStyles.Top | AnchorStyles.Right }; btnClose.FlatAppearance.BorderSize = 0; btnClose.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 80, 80); btnClose.Click += (s, e) => Close(); headerPanel.Controls.Add(btnClose);
            _lblSnapshotInfo = new Label { Text = string.Empty, Location = new Point(20, 60), Size = new Size(ClientSize.Width - 40, 20), ForeColor = Color.DarkOrange, Font = new Font("Segoe UI", 9F, FontStyle.Bold), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Visible = _snapshotLoaded }; Controls.Add(_lblSnapshotInfo);
            tabControl = new TabControl { Location = new Point(0, 80), Size = new Size(ClientSize.Width, ClientSize.Height - 80), Font = new Font("Segoe UI", 12F, FontStyle.Bold), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right }; Controls.Add(tabControl);
            tabCashbox = new TabPage("Cashbox"); cashboxForm = new CashboxCountForm(_ssp) { Dock = DockStyle.Fill }; tabCashbox.Controls.Add(cashboxForm); tabControl.TabPages.Add(tabCashbox);
            tabPayout = new TabPage("Payout"); payoutPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White }; tabPayout.Controls.Add(payoutPanel); tabControl.TabPages.Add(tabPayout); BuildPayoutPanel(); tabControl.SelectedIndexChanged += (s, e) => { if (tabControl.SelectedTab == tabPayout) RefreshPayout(); };
            if (_snapshotLoaded) ShowSnapshotInfo();
        }

        private void ShowSnapshotInfo() { if (_lblSnapshotInfo == null) return; _lblSnapshotInfo.Text = "Aktiver Kassensturz-Snapshot – 'Payout leeren' speichert keinen neuen Bestand (Fortführen) oder mit 'Neuer Start' überschreiben."; _lblSnapshotInfo.Visible = true; }
        private void HideSnapshotInfo() { if (_lblSnapshotInfo == null) return; _lblSnapshotInfo.Visible = false; _lblSnapshotInfo.Text = string.Empty; }

        private void BuildPayoutPanel()
        {
            payoutPanel.Controls.Clear();
            var lblLinksTitel = new Label { Text = "Bestand vor", Location = new Point(40, 40), Size = new Size(220, 30), Font = new Font("Segoe UI", 13F, FontStyle.Bold) }; payoutPanel.Controls.Add(lblLinksTitel);
            string[] denomText = { "5 €", "10 €", "20 €", "50 €", "100 €", "200 €", "500 €" };
            lblLinksBestand = new Label[7]; lblRechtsBestand = new Label[7];
            for (int i = 0; i < denomText.Length; i++) { lblLinksBestand[i] = new Label { Text = $"{denomText[i],4}: -", Location = new Point(60, 80 + i * 32), Size = new Size(140, 28), Font = new Font("Segoe UI", 12F) }; payoutPanel.Controls.Add(lblLinksBestand[i]); }
            lblLinksSumme = new Label { Text = "Summe: -", Location = new Point(60, 80 + denomText.Length * 32), Size = new Size(200, 32), Font = new Font("Segoe UI", 13F, FontStyle.Bold) }; payoutPanel.Controls.Add(lblLinksSumme);
            var lblRechtsTitel = new Label { Text = "Aktueller Bestand:", Location = new Point(560, 40), Size = new Size(200, 30), Font = new Font("Segoe UI", 13F, FontStyle.Bold) }; payoutPanel.Controls.Add(lblRechtsTitel);
            for (int i = 0; i < denomText.Length; i++) { lblRechtsBestand[i] = new Label { Text = $"{denomText[i],4}: -", Location = new Point(580, 80 + i * 32), Size = new Size(140, 28), Font = new Font("Segoe UI", 12F) }; payoutPanel.Controls.Add(lblRechtsBestand[i]); }
            lblRechtsSumme = new Label { Text = "Summe: -", Location = new Point(580, 80 + denomText.Length * 32), Size = new Size(200, 32), Font = new Font("Segoe UI", 13F, FontStyle.Bold) }; payoutPanel.Controls.Add(lblRechtsSumme);
            btnAlleAuszahlen = new Button { Text = "Payout leeren\r\n(alle Scheine)", Size = new Size(240, 80), Font = new Font("Segoe UI Variable", 15F, FontStyle.Bold), BackColor = Color.FromArgb(33, 150, 243), ForeColor = Color.White, FlatStyle = FlatStyle.Flat }; btnAlleAuszahlen.FlatAppearance.BorderSize = 0; btnAlleAuszahlen.Click += BtnPayoutLeeren_Click; payoutPanel.Controls.Add(btnAlleAuszahlen);
            btnInCashboxFahren = new Button { Text = "In Cashbox fahren\r\n(Payout -> Cashbox)", Size = new Size(240, 64), Font = new Font("Segoe UI Variable", 12F, FontStyle.Bold), BackColor = Color.FromArgb(3, 155, 229), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnInCashboxFahren.FlatAppearance.BorderSize = 0;
            btnInCashboxFahren.Click += BtnInCashboxFahren_Click;
            payoutPanel.Controls.Add(btnInCashboxFahren);
            lblDifferenz = new Label { Text = "Differenz: -", AutoSize = false, Size = new Size(400, 40), Font = new Font("Segoe UI", 15F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter }; payoutPanel.Controls.Add(lblDifferenz);
            btnAlleEingezahlt = new Button { Text = "Alle Scheine eingezahlt", Size = new Size(240, 48), Font = new Font("Segoe UI Variable", 12F, FontStyle.Bold), BackColor = Color.FromArgb(46, 125, 50), ForeColor = Color.White, FlatStyle = FlatStyle.Flat }; btnAlleEingezahlt.FlatAppearance.BorderSize = 0; btnAlleEingezahlt.Click += BtnAlleEingezahlt_Click; payoutPanel.Controls.Add(btnAlleEingezahlt);
            payoutPanel.Resize += (s, e) => LayoutPayoutDynamic();
            LayoutPayoutDynamic();
        }

        private void LayoutPayoutDynamic()
        {
            if (payoutPanel == null) return;
            int centerX = payoutPanel.Width / 2;
            if (btnAlleAuszahlen != null) btnAlleAuszahlen.Location = new Point(centerX - btnAlleAuszahlen.Width / 2, 160);
            if (btnInCashboxFahren != null) btnInCashboxFahren.Location = new Point(centerX - btnInCashboxFahren.Width / 2, 160 + (btnAlleAuszahlen?.Height ?? 80) + 10);
            int bottomMargin = 20;
            if (btnAlleEingezahlt != null) btnAlleEingezahlt.Location = new Point(centerX - btnAlleEingezahlt.Width / 2, payoutPanel.Height - btnAlleEingezahlt.Height - bottomMargin);
            if (lblDifferenz != null) lblDifferenz.Location = new Point(centerX - lblDifferenz.Width / 2, btnAlleEingezahlt.Top - lblDifferenz.Height - 8);
        }

        private void BtnInCashboxFahren_Click(object sender, System.EventArgs e)
        {
            if (_ssp == null) return;
            try { AppLogger.KassensturzScopeEnter(); } catch { }

            var confirm = MessageBox.Show(this,
                "Alle Scheine im Payout in die Cashbox fahren?",
                "Bestätigen",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes) return;

            try
            {
                _ssp.SmartEmptyToCashbox();
            }
            catch (Exception ex)
            {
                try { AppLogger.Log("[Kassensturz/NV200] SmartEmpty Fehler: " + ex.Message); } catch { }
            }

            // nach kurzer Zeit Levels abfragen
            var t = new Timer { Interval = 1500 };
            t.Tick += (s, ev) =>
            {
                t.Stop();
                try { _ssp.Payout_angleichen(); } catch { }
                RefreshPayout();
                t.Dispose();
                try { AppLogger.KassensturzScopeExit(); } catch { }
            };
            t.Start();
        }

        private void BtnAlleEingezahlt_Click(object sender, System.EventArgs e)
        { ClearSnapshot(); _snapshotLoaded = false; HideSnapshotInfo(); initialPayout = GetPayoutCounts(); initialPayoutSum = BerechneSumme(initialPayout); RefreshPayout(); }

        private void BtnPayoutLeeren_Click(object sender, System.EventArgs e)
        {
            if (_ssp == null) return;
            // Kassensturz aktiv: Personalguthaben darf sich nicht ändern
            try { AppLogger.KassensturzScopeEnter(); } catch { }

            // Snapshot / Startlogik unverändert
            if (_snapshotLoaded)
            {
                var res = MessageBox.Show(this,
                    "Es läuft noch ein aktiver Kassensturz.\nJa = fortfahren | Nein = Neuer Start | Abbrechen = Abbruch",
                    "Aktiver Kassensturz",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button1);

                if (res == DialogResult.Cancel) return;
                if (res == DialogResult.No)
                {
                    initialPayout = GetPayoutCounts();
                    initialPayoutSum = BerechneSumme(initialPayout);
                    SaveSnapshot(initialPayout);
                    ShowSnapshotInfo();
                }
                // Ja = fortfahren -> initialPayout bleibt der alte Snapshot
            }
            else
            {
                initialPayout = GetPayoutCounts();
                initialPayoutSum = BerechneSumme(initialPayout);
                SaveSnapshot(initialPayout);
                _snapshotLoaded = true;
                ShowSnapshotInfo();
            }

            // WICHTIG: MessageBox jetzt VOR dem Start der Auszahlung (nicht blockierend während Events)
            MessageBox.Show(this,
                "Der Payout-Bereich wird geleert! Alle Scheine werden jetzt ausgezahlt.",
                "Info",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            // Auszahlung starten (UI jetzt frei für Events/Timer)
            int[] payoutCounts = GetPayoutCounts();
            try
            {
                _ssp.PayoutByDenomination(payoutCounts);
            }
            catch (Exception ex)
            {
                try { AppLogger.Log("[Kassensturz/NV200] Fehler bei PayoutByDenomination: " + ex.Message); } catch { }
            }

            // Sofort ein erstes Refresh anstoßen (falls interne Werte schon 0 gesetzt wurden)
            RefreshPayout();

            // Fallback: nach 2s erneut aktualisieren
            try
            {
                // Live-Tracking aktivieren und Button sperren
                _dispensingActive = true;
                _dispenseViewCounts = GetPayoutCounts();
                if (btnAlleAuszahlen != null) btnAlleAuszahlen.Enabled = false;
            }
            catch { }
            RefreshPayout();
            var t = new Timer { Interval = 2000 };
            t.Tick += (s2, ev) =>
            {
                t.Stop();
                try { _ssp.Payout_angleichen(); } catch { }
                RefreshPayout();
                t.Dispose();
                // Kassensturz beenden
                try { AppLogger.KassensturzScopeExit(); } catch { }
            };
            t.Start();
        }

        private void UpdateStatusLabel() { try { if (_lblHeaderStatus != null) { string s = _ssp?.states ?? "-"; _lblHeaderStatus.Text = $"Status: {s}"; } } catch { } }

        private void RefreshPayout()
        {
            if (initialPayout == null) { initialPayout = GetPayoutCounts(); initialPayoutSum = BerechneSumme(initialPayout); }
            // Während einer Auszahlung keine Level-Requests senden (würden ohnehin übersprungen)
            if (_ssp != null && !_dispensingActive) { try { _ssp.Payout_angleichen(); } catch { } }

            int[] countsToShow = _dispensingActive && _dispenseViewCounts != null ? _dispenseViewCounts : GetPayoutCounts();
            livePayout = countsToShow;
            livePayoutSum = BerechneSumme(countsToShow);
            int[] werte = { 5, 10, 20, 50, 100, 200, 500 };
            for (int i = 0; i < 7; i++)
            {
                int vL = initialPayout != null && i < initialPayout.Length ? initialPayout[i] : -1;
                int vR = countsToShow != null && i < countsToShow.Length ? countsToShow[i] : -1;
                if (lblLinksBestand != null && i < lblLinksBestand.Length) lblLinksBestand[i].Text = $"{werte[i]} €: {(vL >= 0 ? vL.ToString() : "?")}";
                if (lblRechtsBestand != null && i < lblRechtsBestand.Length) lblRechtsBestand[i].Text = $"{werte[i]} €: {(vR >= 0 ? vR.ToString() : "?")}";
            }
            if (lblLinksSumme != null) lblLinksSumme.Text = $"Summe: {initialPayoutSum:C2}";
            if (lblRechtsSumme != null) lblRechtsSumme.Text = $"Summe: {livePayoutSum:C2}";
            if (lblDifferenz != null) lblDifferenz.Text = $"Differenz: {(initialPayoutSum - livePayoutSum):C2}"; // Entnommener Betrag positiv
        }

        private void OnWertDispensing(int deltaCent)
        {
            if (_dispenseViewCounts == null) _dispenseViewCounts = GetPayoutCounts();
            _dispensingActive = true;
            int idx = IndexFromCent(deltaCent);
            if (idx >= 0 && _dispenseViewCounts != null && idx < _dispenseViewCounts.Length)
            {
                if (_dispenseViewCounts[idx] > 0) _dispenseViewCounts[idx]--;
            }
            RefreshPayout();
        }

        private int IndexFromCent(int cent)
        {
            switch (cent)
            {
                case 500: return 0;
                case 1000: return 1;
                case 2000: return 2;
                case 5000: return 3;
                case 10000: return 4;
                case 20000: return 5;
                case 50000: return 6;
                default: return -1;
            }
        }

        private int[] GetPayoutCounts() => new int[] { _ssp?.Payout_5_euro ?? 0, _ssp?.Payout_10_euro ?? 0, _ssp?.Payout_20_euro ?? 0, _ssp?.Payout_50_euro ?? 0, _ssp?.Payout_100_euro ?? 0, _ssp?.Payout_200_euro ?? 0, _ssp?.Payout_500_euro ?? 0 };
        private decimal BerechneSumme(int[] arr) { if (arr == null) return 0m; int[] werte = { 5, 10, 20, 50, 100, 200, 500 }; decimal sum = 0m; for (int i = 0; i < 7; i++) { int v = arr != null && i < arr.Length ? arr[i] : 0; if (v > 0) sum += v * werte[i]; } return sum; }
        private void SaveSnapshot(int[] counts) { if (counts == null || counts.Length < 7) return; try { string payload = string.Join(",", counts); string line = $"v1;{System.DateTime.UtcNow.Ticks};{payload}"; IniHelper.WriteValue(SnapshotSection, _snapshotKey, line, AppSettings.IniPath); try { AppLogger.Log("[NV200] Snapshot gespeichert: " + line); } catch { } } catch { } }
        private void ClearSnapshot() { try { IniHelper.WriteValue(SnapshotSection, _snapshotKey, string.Empty, AppSettings.IniPath); } catch { } try { AppLogger.Log("[NV200] Snapshot gelöscht"); } catch { } }
        private void LoadSnapshotIfExists() { try { string line = IniHelper.ReadValue(SnapshotSection, _snapshotKey, AppSettings.IniPath); if (string.IsNullOrWhiteSpace(line)) return; var parts = line.Split(';'); if (parts.Length < 3) return; var csv = parts[2]; var arr = csv.Split(','); if (arr.Length < 7) return; initialPayout = new int[7]; for (int i = 0; i < 7; i++) { int v; if (!int.TryParse(arr[i], out v)) v = 0; initialPayout[i] = v; } initialPayoutSum = BerechneSumme(initialPayout); _snapshotLoaded = true; } catch { } }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try
            {
                // Endbestand loggen (nur wenn Start vorhanden)
                if (_startLogged)
                {
                    livePayout = GetPayoutCounts();
                    livePayoutSum = BerechneSumme(livePayout);
                    decimal diff = initialPayoutSum - livePayoutSum; // entnommen
                    AppLogger.Log($"[Kassensturz/NV200] Ende Payout-Bestand: {FormatCounts(livePayout)} = {livePayoutSum:0.00} € | Entnommen: {diff:0.00} €");
                }
            }
            catch { }
            try { AppLogger.KassensturzScopeExit(); } catch { }

            if (_tmrPayout != null) { _tmrPayout.Stop(); _tmrPayout.Dispose(); }
            if (_tmrStatus != null) { _tmrStatus.Stop(); _tmrStatus.Dispose(); }
            if (_ssp != null)
            {
                if (_onNoteAccepted != null) _ssp.Note_akzepted -= _onNoteAccepted;
                if (_onDispensingComplete != null) _ssp.Dispensing_Complete -= _onDispensingComplete;
                if (_onWertDispensing != null) { try { _ssp.Wert_Dispensing -= _onWertDispensing; } catch { } }
                if (_onCashboxReplaced != null) _ssp.Cashbox_Replaced -= _onCashboxReplaced;
                if (_onCashboxRemoved != null) _ssp.Cashbox_Removed -= _onCashboxRemoved;
                if (_onSspEvent != null) { try { _ssp.Ereignis -= _onSspEvent; } catch { } }
                RestoreMaxCounts();
            }
            base.OnFormClosed(e);
        }
    }
}
