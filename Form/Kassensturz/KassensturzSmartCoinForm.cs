using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using TaMi_Einzahlautomat.Coins;
using System.Linq;

namespace TaMi_Einzahlautomat
{
    public class KassensturzSmartCoinForm : Form
    {
        private Label[] lblLinksBestand;
        private Label lblLinksSumme;
        private Label[] lblRechtsBestand;
        private Label lblRechtsSumme;
        private Label lblDifferenz;
        private Button btnEntleeren;
        private Button btnAlleEingezahlt;
        private SmartCoinV1 smartCoin;
        private int[] startBestand;
        private int[] aktuellerBestand;
        private decimal startSumme;
        private decimal aktuellSumme;
        private int[] initialBestand;
        private decimal initialSumme;
        private Panel headerPanel;
        private Label lblTitle;
        private Button btnClose;
        private Point _mouseDownLocation;
        private bool entleert = false;
        private Timer _tmrBestand;
        private bool _refreshing = false;
        private readonly object _refreshLock = new object();
        

        private const string SnapshotSection = "SmartCoinKassensturz";
        private const string SnapshotKey = "SavedLevels"; // Format: v1;tsTicks;lv0,lv1,...,lv7
        private bool _snapshotLoaded = false; // zeigt ob persistenter Snapshot aktiv ist

        private Label _lblSnapshotInfo; // Hinweis für aktiven Snapshot

        // Startlog Marker
        private bool _startLogged = false;

        public KassensturzSmartCoinForm(SmartCoinV1 smartCoin)
        {
            this.smartCoin = smartCoin;
            SafeInit();
        }

        protected override void OnShown(System.EventArgs e)
        {
            base.OnShown(e);
            try
            {
                // Kassensturz-Modus aktivieren: nach jeder Levels-Abfrage 5s Pause erzwingen
                try { SmartCoinV1.EnableKassensturzMode(true, 5000); } catch { }
                AppLogger.KassensturzScopeEnter();
                if (!_startLogged)
                {
                    var lv = smartCoin?.GetCoinAvailability();
                    startBestand = lv != null ? (int[])lv.Clone() : new int[8];
                    startSumme = BerechneSumme(startBestand);
                    AppLogger.Log($"[Kassensturz/SmartCoin] Start Münz-Bestand: {FormatCoins(startBestand)} = {startSumme:0.00} €");
                    _startLogged = true;
                }
            }
            catch { }
        }

        private string FormatCoins(int[] lv)
        {
            if (lv == null || lv.Length < 8) return "-";
            decimal[] euro = {0.01m,0.02m,0.05m,0.10m,0.20m,0.50m,1m,2m};
            try { return string.Join(",", Enumerable.Range(0,8).Select(i => euro[i].ToString("0.00") + "€=" + lv[i])); } catch { return "-"; }
        }

        private void SafeInit()
        {
            if (smartCoin == null)
            {
                BuildFallbackLayout("SmartCoin Gerät nicht verfügbar.");
                return;
            }

            if (LoadSnapshotIfExists())
            {
                _snapshotLoaded = true;
                initialBestand = (int[])startBestand.Clone();
                initialSumme = BerechneSumme(initialBestand);
            }
            else
            {
                int versuche = 0;
                do
                {
                    try { smartCoin.RequestCoinLevels(); } catch { }
                    System.Threading.Thread.Sleep(120);
                    try { initialBestand = smartCoin.GetCoinAvailability(); } catch { initialBestand = null; }
                    versuche++;
                } while (AlleUnbekannt(initialBestand) && versuche < 5);
                initialSumme = BerechneSumme(initialBestand);
            }

            InitializeLayout();
            ZeigeInitialBestand();
            // Initialer Refresh NUR starten wenn Handle bereits existiert – sonst per Load-Event nachholen
            if (IsHandleCreated)
            {
                try { BeginInvoke((Action)(() => RefreshBestandCoins())); } catch { }
            }
            else
            {
                try
                {
                    Load += (s, e) => { try { RefreshBestandCoins(); } catch { } };
                }
                catch { }
            }
            try { if (smartCoin != null) smartCoin.CoinLevelsUpdated += SmartCoin_CoinLevelsUpdated; } catch { }
            _tmrBestand = new Timer { Interval = 3000 };
            _tmrBestand.Tick += async (s, e) => await RefreshBestandCoinsAsync();
            _tmrBestand.Enabled = true;

            if (_snapshotLoaded) ShowSnapshotInfo();
        }

        private SmartCoinV1 GetSmartCoinInstance()
        {
            // Hole die Singleton-Instanz, wie sie im AdminCoinForm verwendet wird
            if (CoinManager.Instance is SmartCoinV1 sc1)
                return sc1;
            // Fallback: eigene Instanz, falls CoinManager.Instance nicht verfügbar
            return smartCoin;
        }

        private bool AlleUnbekannt(int[] arr)
        {
            if (arr == null) return true;
            foreach (var v in arr) if (v > 0) return false;
            return true;
        }

        private decimal BerechneSumme(int[] bestand)
        {
            if (bestand == null) return 0m;
            int[] centWerte = { 1, 2, 5, 10, 20, 50, 100, 200 };
            decimal summe = 0m;
            for (int i = 0; i < 8; i++)
            {
                int v = bestand != null && i < bestand.Length ? bestand[i] : -1;
                if (v > 0) summe += v * centWerte[i] / 100m;
            }
            return summe;
        }

        private void ZeigeInitialBestand()
        {
            decimal[] euroWerte = { 0.01m, 0.02m, 0.05m, 0.10m, 0.20m, 0.50m, 1.00m, 2.00m };
            for (int i = 0; i < 8; i++)
            {
                int v = initialBestand != null && i < initialBestand.Length ? initialBestand[i] : -1;
                lblLinksBestand[i].Text = $"{euroWerte[i],4:N2} €: {(v >= 0 ? v.ToString() : "?")}";
            }
            lblLinksSumme.Text = $"Summe: {initialSumme:C2}";
        }

        private void InitializeLayout()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(800, 600);
            BackColor = Color.White;
            DoubleBuffered = true;

            // Header
            headerPanel = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(ClientSize.Width, 60),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(33, 150, 243)
            };
            headerPanel.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) _mouseDownLocation = e.Location; };
            headerPanel.MouseMove += (s, e) => { if (e.Button == MouseButtons.Left) { Left += e.X - _mouseDownLocation.X; Top += e.Y - _mouseDownLocation.Y; } };
            Controls.Add(headerPanel);

            lblTitle = new Label
            {
                Text = "Kassensturz SmartCoin",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Variable", 18F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(24, 0),
                Size = new Size(400, 60),
                BackColor = Color.Transparent
            };
            headerPanel.Controls.Add(lblTitle);

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

            // Linke Seite: Startbestand
            var lblLinksTitel = new Label
            {
                Text = "Bestand vor Entleerung:",
                Location = new Point(40, 80),
                Size = new Size(220, 30),
                Font = new Font("Segoe UI", 13F, FontStyle.Bold)
            };
            Controls.Add(lblLinksTitel);

            string[] denomText = { "1 c", "2 c", "5 c", "10 c", "20 c", "50 c", "1 €", "2 €" };
            lblLinksBestand = new Label[8];
            for (int i = 0; i < denomText.Length; i++)
            {
                var lbl = new Label
                {
                    Text = $"{denomText[i],4}: -",
                    Location = new Point(60, 120 + i * 32),
                    Size = new Size(120, 28),
                    Font = new Font("Segoe UI", 12F)
                };
                Controls.Add(lbl);
                lblLinksBestand[i] = lbl;
            }
            lblLinksSumme = new Label
            {
                Text = "Summe: -",
                Location = new Point(60, 390),
                Size = new Size(180, 32),
                Font = new Font("Segoe UI", 13F, FontStyle.Bold)
            };
            Controls.Add(lblLinksSumme);

            // Rechte Seite: Aktueller Bestand
            var lblRechtsTitel = new Label
            {
                Text = "Aktueller Bestand:",
                Location = new Point(580, 80),
                Size = new Size(180, 30),
                Font = new Font("Segoe UI", 13F, FontStyle.Bold)
            };
            Controls.Add(lblRechtsTitel);

            lblRechtsBestand = new Label[8];
            for (int i = 0; i < denomText.Length; i++)
            {
                var lbl = new Label
                {
                    Text = $"{denomText[i],4}: -",
                    Location = new Point(600, 120 + i * 32),
                    Size = new Size(120, 28),
                    Font = new Font("Segoe UI", 12F)
                };
                Controls.Add(lbl);
                lblRechtsBestand[i] = lbl;
            }
            lblRechtsSumme = new Label
            {
                Text = "Summe: -",
                Location = new Point(600, 390),
                Size = new Size(180, 32),
                Font = new Font("Segoe UI", 13F, FontStyle.Bold)
            };
            Controls.Add(lblRechtsSumme);

            // Button Münzen entleeren
            btnEntleeren = new Button
            {
                Text = "Münzen entleeren",
                Location = new Point(300, 180),
                Size = new Size(200, 60),
                Font = new Font("Segoe UI Variable", 15F, FontStyle.Bold),
                BackColor = Color.FromArgb(33, 150, 243),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnEntleeren.FlatAppearance.BorderSize = 0;
            btnEntleeren.Click += BtnEntleeren_Click;
            Controls.Add(btnEntleeren);

            // Differenz
            lblDifferenz = new Label
            {
                Text = "Differenz: -",
                Location = new Point(300, 500),
                Size = new Size(320, 40), // verbreitert (vorher 200)
                Font = new Font("Segoe UI", 15F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = false
            };
            Controls.Add(lblDifferenz);

            // Button Alle Münzen eingezahlt
            btnAlleEingezahlt = new Button
            {
                Text = "Alle Münzen eingezahlt",
                Location = new Point(300, 550),
                Size = new Size(200, 40),
                Font = new Font("Segoe UI Variable", 12F, FontStyle.Bold),
                BackColor = Color.FromArgb(46, 125, 50),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnAlleEingezahlt.FlatAppearance.BorderSize = 0;
            btnAlleEingezahlt.Click += BtnAlleEingezahlt_Click; // NEU
            Controls.Add(btnAlleEingezahlt);

            // Hinweis-Label (links unter Titel)
            _lblSnapshotInfo = new Label
            {
                Text = string.Empty,
                Location = new Point(40, 60),
                Size = new Size(720, 20),
                ForeColor = Color.DarkOrange,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Visible = false
            };
            Controls.Add(_lblSnapshotInfo);

            // Rahmen
            this.Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(120, 120, 120), 2))
                {
                    e.Graphics.DrawRectangle(pen, 1, 1, this.ClientSize.Width - 3, this.ClientSize.Height - 3);
                }
            };
        }

        private void ShowSnapshotInfo()
        {
            if (_lblSnapshotInfo == null) return;
            _lblSnapshotInfo.Text = "Aktiver Kassensturz-Snapshot vorhanden – 'Münzen entleeren' speichert keinen neuen Bestand (Fortführen) oder 'Neuer Start' wählen.";
            _lblSnapshotInfo.Visible = true;
        }
        private void HideSnapshotInfo()
        {
            if (_lblSnapshotInfo == null) return; _lblSnapshotInfo.Visible = false; _lblSnapshotInfo.Text = string.Empty;
        }

        private void LoadStartBestand()
        {
            startBestand = smartCoin.GetCoinAvailability();
            startSumme = 0m;
            int[] centWerte = { 1, 2, 5, 10, 20, 50, 100, 200 };
            for (int i = 0; i < 8; i++)
            {
                int v = startBestand != null && i < startBestand.Length ? startBestand[i] : -1;
                lblLinksBestand[i].Text = $"{centWerte[i],4}: {(v >= 0 ? v.ToString() : "?")}";
                if (v > 0) startSumme += v * centWerte[i] / 100m;
            }
            lblLinksSumme.Text = $"Summe: {startSumme:C2}";
        }

        private void LoadAktuellerBestand()
        {   // legacy call wird auf neue Async-Methode gemappt
            RefreshBestandCoins();
        }

        // Public/Legacy Wrapper – startet asynchronen Refresh ohne Blockade
        private void RefreshBestandCoins()
        {
            _ = RefreshBestandCoinsAsync();
        }

        private async Task RefreshBestandCoinsAsync()
        {
            if (smartCoin == null) return;
            if (_refreshing) return;
            lock (_refreshLock)
            {
                if (_refreshing) return;
                _refreshing = true;
            }
            try
            {
                await Task.Run(() =>
                {
                    try { smartCoin.RequestCoinLevels(); } catch { }
                });
                await Task.Delay(300);
                int[] levels = null;
                try { levels = smartCoin.GetCoinAvailability(); } catch { }
                UpdateCoinsUiFromLevels(levels);

                // NEU: Wenn kein Snapshot aktiv ist (nach 'Alle Münzen eingezahlt'),
                // wird der linke Start-Bestand automatisch auf den aktuellen Stand gesetzt.
                if (!_snapshotLoaded && levels != null && levels.Length >= 8)
                {
                    initialBestand = (int[])levels.Clone();
                    initialSumme = BerechneSumme(initialBestand);
                    ZeigeInitialBestand(); // aktualisiere linke Anzeige
                }
            }
            catch { }
            finally { _refreshing = false; }
        }

        private void UpdateCoinsUiFromLevels(int[] lv)
        {
            decimal summe = 0m;
            decimal[] euroWerte = { 0.01m, 0.02m, 0.05m, 0.10m, 0.20m, 0.50m, 1.00m, 2.00m };
            for (int i = 0; i < 8; i++)
            {
                int v = (lv != null && i < lv.Length) ? lv[i] : -2; // -2 = keine neuen Daten
                if (lblRechtsBestand[i] != null)
                {
                    if (v == -2)
                    {
                        // Keine Änderung, Wert bleibt wie er ist
                    }
                    else if (v < 0)
                    {
                        lblRechtsBestand[i].Text = $"{euroWerte[i],4:N2} €: ?";
                    }
                    else
                    {
                        lblRechtsBestand[i].Text = $"{euroWerte[i],4:N2} €: {v}";
                        summe += v * euroWerte[i];
                    }
                }
            }
            aktuellSumme = summe;
            lblRechtsSumme.Text = $"Summe: {aktuellSumme:C2}";
            UpdateDifferenz();
        }

        private void UpdateDifferenz()
        {
            decimal diff = aktuellSumme - initialSumme;
            string text = "Differenz: " + diff.ToString("N2") + " €"; // N2 + Euro-Zeichen für konsistente Gruppierung
            if (lblDifferenz != null)
            {
                lblDifferenz.Text = text;
                // Dynamisch verbreitern falls nötig
                try
                {
                    var sz = TextRenderer.MeasureText(text, lblDifferenz.Font);
                    int needed = sz.Width + 20; // etwas Puffer
                    if (needed > lblDifferenz.Width)
                    {
                        int max = ClientSize.Width - lblDifferenz.Left - 20;
                        lblDifferenz.Width = Math.Min(needed, max);
                    }
                }
                catch { }
            }
        }

        private async void BtnEntleeren_Click(object sender, EventArgs e)
        {
            // Wenn bereits Snapshot aktiv: Nachfrage ob fortführen oder neu starten
            if (_snapshotLoaded)
            {
                var res = MessageBox.Show(this, "Es läuft noch ein aktueller Kassensturz.\nWählen Sie 'Ja' um fortzufahren (alter Bestand bleibt) oder 'Nein' für einen neuen Start (Bestand wird neu gespeichert).", "Aktiver Kassensturz", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);
                if (res == DialogResult.Cancel) return; // Abbruch
                if (res == DialogResult.No)
                {
                    // Neuer Start: aktuellen Live-Bestand erfassen und als neuen Snapshot setzen
                    try
                    {
                        var live = smartCoin?.GetCoinAvailability();
                        if (live == null) live = new int[8];
                        initialBestand = (int[])live.Clone();
                        initialSumme = BerechneSumme(initialBestand);
                        ZeigeInitialBestand();
                        SaveSnapshot(live); // überschreibt alten
                        ShowSnapshotInfo();
                        entleert = false; // neuen Vorgang beginnen
                    }
                    catch { }
                }
                // Bei Ja (fortführen) einfach weiter ohne neuen Snapshot
            }
            else
            {
                // Kein Snapshot aktiv -> vor Entleerung speichern
                try
                {
                    startBestand = initialBestand ?? smartCoin?.GetCoinAvailability();
                    if (startBestand == null) startBestand = new int[8];
                    SaveSnapshot(startBestand);
                    _snapshotLoaded = true;
                    ShowSnapshotInfo();
                }
                catch { }
            }

            if (entleert)
            {
                // Bereits entleert, aber evtl. erneut entleeren erlaubt – Nachfrage ob wirklich nochmal
                var again = MessageBox.Show(this, "SmartEmpty wurde bereits ausgeführt. Nochmals entleeren?", "Bestätigung", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (again != DialogResult.Yes) return;
            }

            var result = MessageBox.Show(
                "Achten Sie darauf, dass der SmartCoin die Münzen beim Kassensturz nach unten entleert und nicht in den normalen Auswurf. Haben Sie einen Eimer untergestellt?",
                "Hinweis",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information
            );
            if (result != DialogResult.Yes) return;

            await CountdownAndSmartEmpty();
        }

        private async Task CountdownAndSmartEmpty()
        {
            using (var countdownForm = new Form())
            {
                countdownForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                countdownForm.StartPosition = FormStartPosition.CenterParent;
                countdownForm.ClientSize = new Size(300, 120);
                countdownForm.Text = "Achtung";
                var lbl = new Label { Text = string.Empty, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 24F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
                countdownForm.Controls.Add(lbl);
                countdownForm.Show(this); // Modalität vermeiden, UI bleibt reaktionsfähig
                for (int i = 3; i >= 1; i--)
                {
                    lbl.Text = i.ToString();
                    await Task.Delay(800);
                }
                lbl.Text = "Start!";
                await Task.Delay(500);
                countdownForm.Close();
            }

            try
            {
                btnEntleeren.Enabled = false;
                _tmrBestand.Enabled = false; // während SmartEmpty keine Poll-Requests
                await Task.Run(() =>
                {
                    try { smartCoin?.SmartEmpty(); } catch { }
                });
                entleert = true;
            }
            finally
            {
                await Task.Delay(1500); // etwas warten bis Gerät Levels aktualisiert
                _tmrBestand.Enabled = true;
                RefreshBestandCoins();
                btnEntleeren.Enabled = true;
            }
        }

        private void SmartCoin_CoinLevelsUpdated(int[] lvls)
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => UpdateCoinsUiFromLevels(lvls)));
            }
            else
            {
                UpdateCoinsUiFromLevels(lvls);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try
            {
                if (_startLogged)
                {
                    var end = smartCoin?.GetCoinAvailability();
                    aktuellerBestand = end != null ? (int[])end.Clone() : new int[8];
                    aktuellSumme = BerechneSumme(aktuellerBestand);
                    decimal diff = aktuellSumme - startSumme;
                    AppLogger.Log($"[Kassensturz/SmartCoin] Ende Münz-Bestand: {FormatCoins(aktuellerBestand)} = {aktuellSumme:0.00} € | Differenz: {diff:0.00} €");
                }
            }
            catch { }
            try { AppLogger.KassensturzScopeExit(); } catch { }

            if (this.smartCoin != null)
            {
                try { this.smartCoin.CoinLevelsUpdated -= SmartCoin_CoinLevelsUpdated; } catch { }
            }
            if (_tmrBestand != null)

            // Kassensturz-Modus wieder deaktivieren
            try { SmartCoinV1.EnableKassensturzMode(false); } catch { }
            {
                try { _tmrBestand.Stop(); } catch { }
                try { _tmrBestand.Dispose(); } catch { }
                _tmrBestand = null;
            }
            base.OnFormClosed(e);
        }

        public KassensturzSmartCoinForm() : this(GetSmartCoinInstanceStatic())
        {
        }
        private static SmartCoinV1 GetSmartCoinInstanceStatic()
        {
            if (CoinManager.Instance is SmartCoinV1 sc1)
                return sc1;
            return new SmartCoinV1();
        }

        private void BtnAlleEingezahlt_Click(object sender, EventArgs e)
        {
            try { ClearSnapshot(); } catch { }
            try
            {
                // Sofort auf Live setzen
                var live = smartCoin?.GetCoinAvailability();
                if (live == null) live = new int[8];
                initialBestand = (int[])live.Clone();
                initialSumme = BerechneSumme(initialBestand);
                ZeigeInitialBestand();
                entleert = false; _snapshotLoaded = false;
                HideSnapshotInfo();
                lblDifferenz.Text = "Differenz: 0,00 €";
            }
            catch { }
        }

        // Persistenz --------------------------------------------------
        private void SaveSnapshot(int[] levels)
        {
            if (levels == null || levels.Length < 8) return;
            try
            {
                string payload = string.Join(",", levels.Select(v => v < 0 ? -1 : v));
                string line = $"v1;{DateTime.UtcNow.Ticks};{payload}";
                IniHelper.WriteValue(SnapshotSection, SnapshotKey, line, AppSettings.IniPath);
                try { AppLogger.Log("[SmartCoin] Snapshot gespeichert: " + line); } catch { }
            }
            catch { }
        }

        private bool LoadSnapshotIfExists()
        {
            try
            {
                string line = IniHelper.ReadValue(SnapshotSection, SnapshotKey, AppSettings.IniPath);
                if (string.IsNullOrWhiteSpace(line)) return false;
                var parts = line.Split(';');
                if (parts.Length < 3) return false;
                // parts[0] = version, parts[1] = ticks, parts[2] = csv
                var csv = string.Join(";", parts, 2, parts.Length - 2); // falls Semikolons in Zukunft
                var lvStr = csv.Split(',');
                if (lvStr.Length < 8) return false;
                startBestand = new int[8];
                for (int i = 0; i < 8; i++)
                {
                    int v; if (!int.TryParse(lvStr[i], out v)) v = -1; startBestand[i] = v;
                }
                startSumme = BerechneSumme(startBestand);
                try { AppLogger.Log("[SmartCoin] Snapshot geladen: " + line); } catch { }
                return true;
            }
            catch { return false; }
        }

        private void ClearSnapshot()
        {
            try { IniHelper.WriteValue(SnapshotSection, SnapshotKey, string.Empty, AppSettings.IniPath); } catch { }
            try { AppLogger.Log("[SmartCoin] Snapshot gelöscht"); } catch { }
        }

        // Fallback Oberfläche bei fehlender SmartCoin-Initialisierung
        private void BuildFallbackLayout(string reason)
        {
            try { Controls.Clear(); } catch { }
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(520, 220);
            Text = "SmartCoin nicht verfügbar";
            var lbl = new Label
            {
                Text = "SmartCoin Modul konnte nicht initialisiert werden.\r\n" + (reason ?? "Unbekannter Fehler"),
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.DarkRed
            };
            Controls.Add(lbl);
            var btn = new Button { Text = "Schließen", Dock = DockStyle.Bottom, Height = 44, BackColor = Color.FromArgb(33, 150, 243), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btn.FlatAppearance.BorderSize = 0; btn.Click += (s, e) => Close();
            Controls.Add(btn);
        }
    }
}
