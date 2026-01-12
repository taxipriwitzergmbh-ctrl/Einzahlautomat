using System;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing.Drawing2D;
using Geldautomat.Coins; // NEU

namespace Geldautomat
{
    public class KassenbestandForm : Form
    {
        private Panel headerPanel;
        private Button btnClose;
        private Button btnRefresh; // NEU: Aktualisieren-Button
        private Label lblTitle;
        private Label lblDiff; // NEU: Kassendifferenz
        private Point _mouseDownLocation;
        private ListView lvKassenbestand;

        // NEU: Referenz auf NV200 (optional)
        private NV200_SSP _ssp;

        // NEU: Referenzen auf die Hardware-Zeilen zur sp�teren Aktualisierung
        private ListViewItem _itemSmartCoin;
        private ListViewItem _itemSmartCoin2;
        private ListViewItem _itemNv200;
        private ListViewItem _itemNv200Cashbox;
        private ListViewItem _itemNv2002;
        private ListViewItem _itemNv2002Cashbox;

        // NEU: Summenzeilen
        private ListViewItem _itemAutomatSum;
        private ListViewItem _itemKassenSum;

        // NEU: ListView-Gruppen
        private ListViewGroup _grpAutomat;
        private ListViewGroup _grpKassen;

        // NEU: Summen
        private decimal _sumAutomatEuro = 0m;
        private decimal _sumKassenEuro = 0m;

        // NEU: INI-Pfad
        private readonly string _iniPath = AppSettings.IniPath;

        // Lade-Overlay-Label (nur Symbol, kein Panel, kein Hintergrund)
        private Label loadingLabel;

        // NEU: RM5 Zeile
        private ListViewItem _itemRm5;

        // Pr�ft ob in INI der RM5 als aktiver M�nzpr�fer konfiguriert ist
        private bool IsRm5Active()
        {
            try
            {
                var t = IniHelper.ReadValue("SmartCoin", "Typ", AppSettings.IniPath)?.Trim() ?? string.Empty;
                return t.Equals("RM5", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public KassenbestandForm()
        {
            InitializeLayout();
        }

        // NEU: �berladener Ctor, um NV200-Instanz zu �bergeben
        public KassenbestandForm(NV200_SSP ssp)
        {
            _ssp = ssp;
            InitializeLayout();
        }

        private void InitializeLayout()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(800, 600);
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
                Text = "Kassenbestand je Kasse",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Variable", 18F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(24, 0),
                Size = new Size(420, 60),
                BackColor = Color.Transparent
            };
            headerPanel.Controls.Add(lblTitle);

            // NEU: Kassendifferenz rechts anzeigen
            lblDiff = new Label
            {
                Text = "Kassendifferenz: 0,00 �",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Segoe UI Variable", 16F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Location = new Point(420, 0), // weiter nach links
                Size = new Size(220, 60), // schmaler, damit Platz f�r Buttons
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            headerPanel.Controls.Add(lblDiff);

            btnClose = new Button
            {
                Text = "\u2715",
                Font = new Font("Segoe UI Symbol", 18F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(48, 48),
                Location = new Point(ClientSize.Width - 56, 6),
                TabStop = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 80, 80);
            btnClose.Click += (s, e) => Close();
            headerPanel.Controls.Add(btnClose);

            // NEU: Aktualisieren-Button
            btnRefresh = new Button
            {
                Text = "\u21BB", // Unicode-Symbol ?
                Font = new Font("Segoe UI Symbol", 22F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(33, 150, 243),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(48, 48),
                Location = new Point(ClientSize.Width - 112, 6),
                TabStop = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnRefresh.FlatAppearance.BorderSize = 0;
            btnRefresh.FlatAppearance.MouseOverBackColor = Color.FromArgb(33, 203, 243);
            btnRefresh.Click += async (s, e) =>
            {
                btnRefresh.Enabled = false;
                loadingLabel.Visible = true;
                CenterLoadingLabel();
                await LoadDataAsync();
                loadingLabel.Visible = false;
                btnRefresh.Enabled = true;
            };
            headerPanel.Controls.Add(btnRefresh); // Button zuletzt hinzuf�gen, damit er oben liegt
    
            try { Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, Width, Height, 24, 24)); } catch { }

            lvKassenbestand = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                Location = new Point(20, 80),
                Size = new Size(760, 500),
                Font = new Font("Segoe UI Variable", 12F, FontStyle.Regular),
                ShowGroups = true // NEU: Gruppenanzeige
            };
            lvKassenbestand.Columns.Add("Kasse", 280, HorizontalAlignment.Left);
            lvKassenbestand.Columns.Add("Kassenbestand", 160, HorizontalAlignment.Right);
            lvKassenbestand.Columns.Add("Letzter Eintrag (Zeit)", 160, HorizontalAlignment.Left);
            lvKassenbestand.Columns.Add("Automatenname", 140, HorizontalAlignment.Left);
            Controls.Add(lvKassenbestand);

            // NEU: Gruppen anlegen
            _grpAutomat = new ListViewGroup("Automatenbestand", HorizontalAlignment.Left);
            _grpKassen = new ListViewGroup("Kassenbestand", HorizontalAlignment.Left);
            lvKassenbestand.Groups.Add(_grpAutomat);
            lvKassenbestand.Groups.Add(_grpKassen);

            // Lade-Label (nur Symbol, kein Panel)
            loadingLabel = new Label
            {
                Text = "??",
                Font = new Font("Segoe UI Emoji", 48F, FontStyle.Bold),
                ForeColor = Color.DodgerBlue,
                AutoSize = true,
                BackColor = Color.Transparent,
                Visible = false
            };
            Controls.Add(loadingLabel);
            this.Resize += (s, e) => CenterLoadingLabel();
            CenterLoadingLabel();

            Shown += async (s, e) => await LoadDataAsync();
        }

        private void CenterLoadingLabel()
        {
            if (loadingLabel != null)
                loadingLabel.Location = new Point((ClientSize.Width - loadingLabel.Width) / 2, (ClientSize.Height - loadingLabel.Height) / 2);
        }

        private async Task LoadDataAsync()
        {
            loadingLabel.Visible = true;
            CenterLoadingLabel();
            Cursor.Current = Cursors.WaitCursor;
            try
            {
                bool rm5 = IsRm5Active();
                if (string.IsNullOrWhiteSpace(AppSettings.AutomatenName))
                {
                    AppSettings.AutomatenName = AppSettings.LoadAutomatenNameFromIni() ?? "";
                }
                var autoName = AppSettings.AutomatenName ?? "";

                decimal sumKassen = 0m;
                var newKassenItems = new System.Collections.Generic.List<ListViewItem>();

                using (var db = new DatabaseHelper())
                {
                    var dt = await db.GetLatestKassenbestaendeAsync(autoName);
                    foreach (DataRow row in dt.Rows)
                    {
                        string kassenName = row["ManName"] as string ?? $"ID {row["FirmenId"]}";
                        decimal bestand = row["Kassenbestand"] == DBNull.Value ? 0m : Convert.ToDecimal(row["Kassenbestand"]);
                        DateTime ts = row["ErfasstAm"] == DBNull.Value ? DateTime.MinValue : Convert.ToDateTime(row["ErfasstAm"]);
                        var item = new ListViewItem(kassenName);
                        item.SubItems.Add(bestand.ToString("C2"));
                        item.SubItems.Add(ts == DateTime.MinValue ? "" : ts.ToString("dd.MM.yyyy HH:mm"));
                        item.SubItems.Add(autoName);
                        item.Group = _grpKassen;
                        newKassenItems.Add(item);
                        sumKassen += bestand;
                    }
                    if (dt.Rows.Count == 0)
                    {
                        var empty = new ListViewItem(new[] { "-", "Keine Daten", "", autoName }) { ForeColor = Color.DimGray };
                        empty.Group = _grpKassen;
                        newKassenItems.Add(empty);
                    }
                }

                // Jetzt ListView atomar neu bef�llen
                lvKassenbestand.BeginUpdate();
                lvKassenbestand.Items.Clear();
                _itemAutomatSum = null; _itemKassenSum = null; _itemRm5 = null;
                AppendHardwareRows(autoName, rm5);
                foreach (var item in newKassenItems) lvKassenbestand.Items.Add(item);
                lvKassenbestand.EndUpdate();

                // Summen jetzt einf�gen
                _sumKassenEuro = sumKassen;
                // Automatenbestand (Hardware) async aktualisieren
                await RefreshHardwareAsync(autoName, rm5);
                // Summenzeilen nach Hardware-Update immer am Ende einf�gen
                UpsertAutomatSumItem(autoName);
                UpsertKassenSumItem(autoName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Fehler beim Laden des Kassenbestands:\r\n{ex.Message}", "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                loadingLabel.Visible = false;
                Cursor.Current = Cursors.Default;
            }
        }

        // Sendet nur Abfragen (kein Enable/Freigeben) und aktualisiert die vier Zeilen
        private async Task RefreshHardwareAsync(string autoName)
        { await RefreshHardwareAsync(autoName, IsRm5Active()); }
        private async Task RefreshHardwareAsync(string autoName, bool rm5)
        {
            try
            {
                if (!rm5)
                {
                    try { if (_ssp != null) _ssp.Payout_angleichen(); } catch { }
                    try { Program.NV200Instance?.Payout_angleichen(); } catch { }
                    try { Program.NV2002Instance?.Payout_angleichen(); } catch { }
                    try
                    {
                        var coin1 = CoinManager.Instance;
                        if (coin1 is SmartCoinV1 s1) s1.RequestCoinLevels();
                        var coin2 = Coin2Manager.Instance;
                        if (coin2 is SmartCoinV1 s1b) s1b.RequestCoinLevels();
                    }
                    catch { }
                }
                else
                {
                    // RM5 aktiv -> Levels anfragen
                    try
                    {
                        if (CoinManager.Instance is Rm5CctalkValidator rm5Inst)
                        {
                            rm5Inst.RequestCoinLevels();
                        }
                    }
                    catch { }
                }
                await Task.Delay(400);
                UpdateHardwareRowValues(autoName, rm5);
                await Task.Delay(700);
                UpdateHardwareRowValues(autoName, rm5);
            }
            catch { }
        }

        private void UpdateHardwareRowValues(string autoName)
        { UpdateHardwareRowValues(autoName, IsRm5Active()); }
        private void UpdateHardwareRowValues(string autoName, bool rm5)
        {
            // INI-Ports lesen
            string nv1Port = IniHelper.ReadValue("NV200/1", "ComPort", _iniPath);
            string nv2Port = IniHelper.ReadValue("NV200/2", "ComPort", _iniPath);
            decimal sumAutomat = 0m;
            if (rm5)
            {
                // RM5 aktiv: SmartCoin-Zeilen ausblenden, RM5 Zeile bef�llen
                if (_itemSmartCoin != null) _itemSmartCoin.ForeColor = Color.LightGray;
                if (_itemSmartCoin2 != null) _itemSmartCoin2.ForeColor = Color.LightGray;
                if (_itemRm5 != null)
                {
                    try
                    {
                        if (CoinManager.Instance is Rm5CctalkValidator rm5Inst)
                        {
                            var lv = rm5Inst.GetCoinAvailability();
                            decimal sumCent = 0m;
                            int[] vals = {1,2,5,10,20,50,100,200};
                            for (int i = 0; i < 8 && i < lv.Length; i++)
                            {
                                int c = lv[i]; if (c > 0 && lv[i] >= 0) sumCent += c * vals[i];
                            }
                            var euro = (sumCent / 100m).ToString("C2");
                            SetValue(_itemRm5, euro);
                            sumAutomat += (sumCent / 100m);
                        }
                        else SetValue(_itemRm5, "-");
                    }
                    catch { SetValue(_itemRm5, "-"); }
                }
            }
            else
            {
                // SmartCoin(e)
                try
                {
                    var coin1 = CoinManager.Instance;
                    var coin2 = Coin2Manager.Instance;
                    SetValue(_itemSmartCoin, "-");
                    SetValue(_itemSmartCoin2, "-");
                    decimal sum1 = 0m, sum2 = 0m;
                    int[] lv1 = null, lv2 = null;
                    if (coin1 is SmartCoinV1 sc1) { lv1 = sc1.GetCoinAvailability(); }
                    if (coin2 is SmartCoinV1 sc2v1) { lv2 = sc2v1.GetCoinAvailability(); }
                    if (lv1 != null) sum1 = SumSmartCoinEuro(lv1);
                    if (lv2 != null) sum2 = SumSmartCoinEuro(lv2);
                    if (lv1 != null) { SetValue(_itemSmartCoin, sum1.ToString("C2")); sumAutomat += sum1; }
                    if (lv2 != null) { SetValue(_itemSmartCoin2, sum2.ToString("C2")); sumAutomat += sum2; }
                }
                catch
                {
                    SetValue(_itemSmartCoin, "-");
                    SetValue(_itemSmartCoin2, "-");
                }
            }

            // NV200 Ger�te unver�ndert (immer ber�cksichtigen)
            try
            {
                SetValue(_itemNv200, "-"); SetValue(_itemNv2002, "-"); SetValue(_itemNv200Cashbox, "-"); SetValue(_itemNv2002Cashbox, "-");
                void FillNv(NV200_SSP dev, string expectedPort, ListViewItem payoutItem, ListViewItem cashboxItem)
                {
                    try
                    {
                        if (dev == null || string.IsNullOrWhiteSpace(expectedPort)) return;
                        if (!string.Equals(dev.ComPort, expectedPort, StringComparison.OrdinalIgnoreCase)) return;
                        decimal nvSum = 5m * dev.Payout_5_euro + 10m * dev.Payout_10_euro + 20m * dev.Payout_20_euro + 50m * dev.Payout_50_euro + 100m * dev.Payout_100_euro + 200m * dev.Payout_200_euro + 500m * dev.Payout_500_euro;
                        decimal cashboxEuro = dev.GetCashboxSumCent() / 100m;
                        SetValue(payoutItem, nvSum.ToString("C2"));
                        SetValue(cashboxItem, cashboxEuro.ToString("C2"));
                        sumAutomat += nvSum + cashboxEuro;
                    }
                    catch { }
                }
                FillNv(Program.NV200Instance, nv1Port, _itemNv200, _itemNv200Cashbox);
                FillNv(Program.NV2002Instance, nv2Port, _itemNv2002, _itemNv2002Cashbox);
            }
            catch { }
            _sumAutomatEuro = sumAutomat;
            UpsertAutomatSumItem(autoName);
            UpdateDiffLabel();
        }

        private decimal ReadCurrencyOrZero(ListViewItem item)
        {
            try
            {
                if (item == null || item.SubItems.Count < 2) return 0m;
                var s = item.SubItems[1].Text?.Trim();
                if (string.IsNullOrEmpty(s) || s == "-") return 0m;
                if (decimal.TryParse(s, NumberStyles.Currency, CultureInfo.CurrentCulture, out var v))
                    return v;
            }
            catch { }
            return 0m;
        }

        private void SetValue(ListViewItem item, string text)
        {
            if (item != null && item.SubItems.Count >= 2)
                item.SubItems[1].Text = text;
        }

        private void UpsertAutomatSumItem(string autoName)
        {
            if (_itemAutomatSum == null)
            {
                _itemAutomatSum = new ListViewItem("Summe Automatenbestand");
                _itemAutomatSum.SubItems.Add(_sumAutomatEuro.ToString("C2"));
                _itemAutomatSum.SubItems.Add("");
                _itemAutomatSum.SubItems.Add(autoName);
                _itemAutomatSum.Group = _grpAutomat;
                _itemAutomatSum.ForeColor = Color.Black;
                _itemAutomatSum.Font = new Font(lvKassenbestand.Font, FontStyle.Bold);
                lvKassenbestand.Items.Add(_itemAutomatSum);
            }
            else
            {
                _itemAutomatSum.SubItems[1].Text = _sumAutomatEuro.ToString("C2");
            }
        }

        private void UpsertKassenSumItem(string autoName)
        {
            if (_itemKassenSum == null)
            {
                _itemKassenSum = new ListViewItem("Summe Kassenbestand");
                _itemKassenSum.SubItems.Add(_sumKassenEuro.ToString("C2"));
                _itemKassenSum.SubItems.Add("");
                _itemKassenSum.SubItems.Add(autoName);
                _itemKassenSum.Group = _grpKassen;
                _itemKassenSum.ForeColor = Color.Black;
                _itemKassenSum.Font = new Font(lvKassenbestand.Font, FontStyle.Bold);
                lvKassenbestand.Items.Add(_itemKassenSum);
            }
            else
            {
                _itemKassenSum.SubItems[1].Text = _sumKassenEuro.ToString("C2");
            }
            UpdateDiffLabel();
        }

        private void UpdateDiffLabel()
        {
            decimal diff = _sumAutomatEuro - _sumKassenEuro;
            lblDiff.Text = $"Kassendifferenz: {diff:C2}";
            lblDiff.ForeColor = diff == 0m ? Color.White : Color.Yellow;
            try { KassenSummary.Update(_sumAutomatEuro, _sumKassenEuro); } catch { }
        }

        // Sechs Zeilen hinzuf�gen und referenzieren
        private void AppendHardwareRows(string autoName)
        { AppendHardwareRows(autoName, IsRm5Active()); }
        private void AppendHardwareRows(string autoName, bool rm5)
        {
            if (rm5)
            {
                _itemRm5 = new ListViewItem("RM5 M�nzenbestand");
                _itemRm5.SubItems.Add("-");
                _itemRm5.SubItems.Add("");
                _itemRm5.SubItems.Add(autoName);
                _itemRm5.ForeColor = Color.FromArgb(255,143,0);
                _itemRm5.Group = _grpAutomat;
                lvKassenbestand.Items.Add(_itemRm5);
            }
            else
            {
                // SmartCoin 1
                _itemSmartCoin = new ListViewItem("SmartCoin");
                _itemSmartCoin.SubItems.Add("-");
                _itemSmartCoin.SubItems.Add("");
                _itemSmartCoin.SubItems.Add(autoName);
                _itemSmartCoin.ForeColor = Color.FromArgb(25, 118, 210);
                _itemSmartCoin.Group = _grpAutomat;
                lvKassenbestand.Items.Add(_itemSmartCoin);
                // SmartCoin 2
                _itemSmartCoin2 = new ListViewItem("SmartCoin 2");
                _itemSmartCoin2.SubItems.Add("-");
                _itemSmartCoin2.SubItems.Add("");
                _itemSmartCoin2.SubItems.Add(autoName);
                _itemSmartCoin2.ForeColor = Color.FromArgb(25, 118, 210);
                _itemSmartCoin2.Group = _grpAutomat;
                lvKassenbestand.Items.Add(_itemSmartCoin2);
            }
            // NV200 u.a. immer
            _itemNv200 = new ListViewItem("NV200"); _itemNv200.SubItems.Add("-"); _itemNv200.SubItems.Add(""); _itemNv200.SubItems.Add(autoName); _itemNv200.ForeColor = Color.FromArgb(46,125,50); _itemNv200.Group = _grpAutomat; lvKassenbestand.Items.Add(_itemNv200);
            _itemNv200Cashbox = new ListViewItem("NV200 Cashbox"); _itemNv200Cashbox.SubItems.Add("-"); _itemNv200Cashbox.SubItems.Add(""); _itemNv200Cashbox.SubItems.Add(autoName); _itemNv200Cashbox.ForeColor = Color.FromArgb(46,125,50); _itemNv200Cashbox.Group = _grpAutomat; lvKassenbestand.Items.Add(_itemNv200Cashbox);
            _itemNv2002 = new ListViewItem("NV200/2"); _itemNv2002.SubItems.Add("-"); _itemNv2002.SubItems.Add(""); _itemNv2002.SubItems.Add(autoName); _itemNv2002.ForeColor = Color.FromArgb(46,125,50); _itemNv2002.Group = _grpAutomat; lvKassenbestand.Items.Add(_itemNv2002);
            _itemNv2002Cashbox = new ListViewItem("NV200/2 Cashbox"); _itemNv2002Cashbox.SubItems.Add("-"); _itemNv2002Cashbox.SubItems.Add(""); _itemNv2002Cashbox.SubItems.Add(autoName); _itemNv2002Cashbox.ForeColor = Color.FromArgb(46,125,50); _itemNv2002Cashbox.Group = _grpAutomat; lvKassenbestand.Items.Add(_itemNv2002Cashbox);
        }

        // M�nzsumme in Euro aus Level-Array
        // Index 0..7 = {1,2,5,10,20,50,100,200} Cent; -1 = unbekannt
        private decimal SumSmartCoinEuro(int[] lv)
        {
            if (lv == null || lv.Length < 8) return 0m;
            int safe(int i) => lv[i] < 0 ? 0 : lv[i];
            decimal sumCent =
                  1m   * safe(0)
                + 2m   * safe(1)
                + 5m   * safe(2)
                + 10m  * safe(3)
                + 20m  * safe(4)
                + 50m  * safe(5)
                + 100m * safe(6)
                + 200m * safe(7);
            return sumCent / 100m;
        }

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

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
    }
}