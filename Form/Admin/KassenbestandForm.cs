using System;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing.Drawing2D;
using TaMi_Einzahlautomat.Coins; // NEU
using TaMi_Einzahlautomat.UI.Layout;

namespace TaMi_Einzahlautomat
{
    public class KassenbestandForm : Form
    {
        private ModernHeaderPanel _header;
        private ModernGradientButton _btnRefresh;
        private Label lblDiff; // NEU: Kassendifferenz
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
                // Gleiche Quelle wie AdminCoinForm: dort wird Typ in [SmartCoin/1] gespeichert.
                var t = IniHelper.ReadValue("SmartCoin/1", "Typ", AppSettings.IniPath)?.Trim() ?? string.Empty;
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

            _header = new ModernHeaderPanel { Title = "Kassenbestand je Kasse" };
            _header.CloseClicked += () => { try { Close(); } catch { } };
            Controls.Add(_header);
            _header.BringToFront();
            try { _header.ApplyRoundedRegionToForm(this); } catch { }

            // Aktualisieren-Button links neben dem Schließen-Button
            _btnRefresh = new ModernGradientButton
            {
                Text = "",
                Width = 44,
                Height = 44,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                TabStop = false
            };
            try
            {
                // orange wie früher
                _btnRefresh.GradientStart = Color.FromArgb(255, 179, 0);
                _btnRefresh.GradientEnd = Color.FromArgb(245, 124, 0);
                _btnRefresh.Font = new Font("Segoe UI Symbol", 18F, FontStyle.Bold);
            }
            catch { }

            // Symbol zeichnen (↻)
            // `ModernGradientButton` zeichnet den Text selbst in `OnPaint`, ein externer `Paint`-Handler wird nicht aufgerufen.
            // Daher nutzen wir das `Text`-Property statt `Paint`.
            try { _btnRefresh.Text = "\u21BB"; } catch { }
            _btnRefresh.Click += async (s, e) =>
            {
                try { await LoadDataAsync(); } catch { }
            };
            try { _header.Controls.Add(_btnRefresh); } catch { Controls.Add(_btnRefresh); }

            void layoutHeaderExtras()
            {
                try
                {
                    int headerH = _header != null ? _header.Height : 60;
                    int closeW = 48;
                    int marginRight = 12;
                    int gap = 12;
                    int y = (headerH - _btnRefresh.Height) / 2;

                    // refresh button sits directly left of close button
                    int x = (_header.ClientSize.Width - marginRight - closeW) - gap - _btnRefresh.Width;
                    if (x < 200) x = 200;
                    _btnRefresh.Location = new Point(x, y);
                }
                catch { }
            }
            try { _header.Resize += (s, e) => layoutHeaderExtras(); } catch { }
            try { layoutHeaderExtras(); } catch { }

            // NEU: Kassendifferenz rechts anzeigen
            lblDiff = new Label
            {
                Text = "Kassendifferenz:\r\n0,00 €",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Segoe UI Variable", 16F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Location = new Point(420, 0), // wird gleich dynamisch gesetzt
                Size = new Size(320, 60),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            try
            {
                _header.Controls.Add(lblDiff);

                void layoutDiffLabel()
                {
                    try
                    {
                        // `lblDiff` soll die ganze verfügbare Breite zwischen Titel links und Buttons rechts nutzen,
                        // ohne den Titel zu überdecken.
                        int leftPad = 120;
                        try
                        {
                            var titleText = _header != null ? (_header.Title ?? string.Empty) : string.Empty;
                            if (!string.IsNullOrWhiteSpace(titleText))
                            {
                                // Wenn möglich an das echte Title-Label im `ModernHeaderPanel` anlehnen,
                                // damit wir garantiert nicht in den Titelbereich hineinzeichnen.
                                int titleX = 20;
                                try
                                {
                                    foreach (Control c in _header.Controls)
                                    {
                                        var lbl = c as Label;
                                        if (lbl == null) continue;
                                        if (string.Equals(lbl.Text, titleText, StringComparison.Ordinal))
                                        {
                                            titleX = Math.Max(0, lbl.Left);
                                            break;
                                        }
                                    }
                                }
                                catch { titleX = 20; }

                                // `ModernHeaderPanel` nutzt typischerweise eine eigene (fette) Titelschrift.
                                // Daher messen wir konservativ mit Bold, damit wirklich nichts abgeschnitten wird.
                                Font titleFont = null;
                                try { titleFont = new Font(_header.Font.FontFamily, Math.Max(12f, _header.Font.Size), FontStyle.Bold); } catch { titleFont = _header.Font; }
                                int titleW = 0;
                                try { titleW = TextRenderer.MeasureText(titleText, titleFont).Width; } catch { titleW = TextRenderer.MeasureText(titleText, _header.Font).Width; }
                                try { if (titleFont != null && !ReferenceEquals(titleFont, _header.Font)) titleFont.Dispose(); } catch { }

                                // zusätzlicher Abstand zum Titel
                                leftPad = Math.Max(leftPad, titleX + titleW + 24);
                            }
                        }
                        catch { }
                        int closeW = 48;
                        int marginRight = 12;
                        int gapToClose = 12;
                        int gapBetweenButtons = 12;
                        int refreshW = _btnRefresh != null ? _btnRefresh.Width : 0;
                        int rightReserved = closeW + marginRight + (refreshW > 0 ? (gapBetweenButtons + refreshW) : 0) + gapToClose;

                        int x = leftPad;
                        // Titel soll immer vollständig bleiben -> `lblDiff` bei Bedarf schmaler machen.
                        int minDiffW = 220;
                        int wAvail = _header.ClientSize.Width - x - rightReserved;
                        int w = Math.Max(minDiffW, wAvail);
                        // Wenn nicht genug Platz da ist, lieber kleiner werden statt in den Titel zu laufen.
                        if (wAvail < minDiffW) w = Math.Max(120, wAvail);
                        lblDiff.Location = new Point(x, 0);
                        lblDiff.Size = new Size(w, _header.Height);
                        lblDiff.BringToFront();
                    }
                    catch { }
                }

                _header.Resize += (s, e) => { try { layoutDiffLabel(); } catch { } };
                _header.Layout += (s, e) => { try { layoutDiffLabel(); } catch { } };

                // initiales Layout direkt anwenden (Resize/Layout kommt teils erst später)
                try { layoutDiffLabel(); } catch { }
            }
            catch
            {
                // Fallback (sollte nicht passieren)
                Controls.Add(lblDiff);
                try { lblDiff.BringToFront(); } catch { }
            }

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

                var devState = GetDeviceState();

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
                _itemSmartCoin = null; _itemSmartCoin2 = null;
                _itemNv200 = null; _itemNv200Cashbox = null;
                _itemNv2002 = null; _itemNv2002Cashbox = null;

                AppendHardwareRows(autoName, rm5, devState);
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
            var st = GetDeviceState();

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

                    if (st == null || !st.SmartCoin1Disabled) SetValue(_itemSmartCoin, "-");
                    if (st == null || !st.SmartCoin2Disabled) SetValue(_itemSmartCoin2, "-");

                    decimal sum1 = 0m, sum2 = 0m;
                    int[] lv1 = null, lv2 = null;
                    if ((st == null || !st.SmartCoin1Disabled) && coin1 is SmartCoinV1 sc1) { lv1 = sc1.GetCoinAvailability(); }
                    if ((st == null || !st.SmartCoin2Disabled) && coin2 is SmartCoinV1 sc2v1) { lv2 = sc2v1.GetCoinAvailability(); }
                    if (lv1 != null) sum1 = SumSmartCoinEuro(lv1);
                    if (lv2 != null) sum2 = SumSmartCoinEuro(lv2);
                    if (lv1 != null) { SetValue(_itemSmartCoin, sum1.ToString("C2")); sumAutomat += sum1; }
                    if (lv2 != null) { SetValue(_itemSmartCoin2, sum2.ToString("C2")); sumAutomat += sum2; }
                }
                catch
                {
                    if (st == null || !st.SmartCoin1Disabled) SetValue(_itemSmartCoin, "-");
                    if (st == null || !st.SmartCoin2Disabled) SetValue(_itemSmartCoin2, "-");
                }
            }

            // NV200
            try
            {
                if (st == null || !st.Nv2001Disabled) { SetValue(_itemNv200, "-"); SetValue(_itemNv200Cashbox, "-"); }
                if (st == null || !st.Nv2002Disabled) { SetValue(_itemNv2002, "-"); SetValue(_itemNv2002Cashbox, "-"); }

                void FillNv(NV200_SSP dev, string expectedPort, ListViewItem payoutItem, ListViewItem cashboxItem)
                {
                    try
                    {
                        if (dev == null || string.IsNullOrWhiteSpace(expectedPort)) return;
                        if (payoutItem == null || cashboxItem == null) return;
                        if (!string.Equals(dev.ComPort, expectedPort, StringComparison.OrdinalIgnoreCase)) return;
                        decimal nvSum = 5m * dev.Payout_5_euro + 10m * dev.Payout_10_euro + 20m * dev.Payout_20_euro + 50m * dev.Payout_50_euro + 100m * dev.Payout_100_euro + 200m * dev.Payout_200_euro + 500m * dev.Payout_500_euro;
                        decimal cashboxEuro = dev.GetCashboxSumCent() / 100m;
                        SetValue(payoutItem, nvSum.ToString("C2"));
                        SetValue(cashboxItem, cashboxEuro.ToString("C2"));
                        sumAutomat += nvSum + cashboxEuro;
                    }
                    catch { }
                }

                if (st == null || !st.Nv2001Disabled)
                    FillNv(Program.NV200Instance, nv1Port, _itemNv200, _itemNv200Cashbox);

                if (st == null || !st.Nv2002Disabled)
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
            lblDiff.Text = $"Kassendifferenz:\r\n{diff:C2}";
            lblDiff.ForeColor = diff == 0m ? Color.White : Color.Yellow;
            try { KassenSummary.Update(_sumAutomatEuro, _sumKassenEuro); } catch { }
        }

        private sealed class DeviceState
        {
            public bool SmartCoin1Disabled;
            public bool SmartCoin2Disabled;
            public bool Nv2001Disabled;
            public bool Nv2002Disabled;
        }

        private DeviceState GetDeviceState()
        {
            var st = new DeviceState();
            try
            {
                var dv = IniHelper.ReadValue("SmartCoin/1", "Disabled", _iniPath);
                if (!string.IsNullOrWhiteSpace(dv)) st.SmartCoin1Disabled = dv.Trim().Equals("1", StringComparison.OrdinalIgnoreCase) || dv.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
            }
            catch { }

            try
            {
                st.SmartCoin2Disabled = AdminCoin2Form.DeviceDisabled;
            }
            catch { }
            try
            {
                if (!st.SmartCoin2Disabled)
                {
                    var dv = IniHelper.ReadValue("SmartCoin/2", "Disabled", _iniPath);
                    if (!string.IsNullOrWhiteSpace(dv)) st.SmartCoin2Disabled = dv.Trim().Equals("1", StringComparison.OrdinalIgnoreCase) || dv.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { }

            try
            {
                st.Nv2001Disabled = AdminNV200Form.DeviceDisabled;
            }
            catch { }
            try
            {
                if (!st.Nv2001Disabled)
                {
                    var dv = IniHelper.ReadValue("NV200/1", "Disabled", _iniPath);
                    if (!string.IsNullOrWhiteSpace(dv)) st.Nv2001Disabled = dv.Trim().Equals("1", StringComparison.OrdinalIgnoreCase) || dv.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { }

            try
            {
                st.Nv2002Disabled = AdminNV200_2Form.DeviceDisabled;
            }
            catch { }
            try
            {
                if (!st.Nv2002Disabled)
                {
                    var dv = IniHelper.ReadValue("NV200/2", "Disabled", _iniPath);
                    if (!string.IsNullOrWhiteSpace(dv)) st.Nv2002Disabled = dv.Trim().Equals("1", StringComparison.OrdinalIgnoreCase) || dv.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { }

            return st;
        }

        // Sechs Zeilen hinzuf�gen und referenzieren
        private void AppendHardwareRows(string autoName)
        { AppendHardwareRows(autoName, IsRm5Active(), GetDeviceState()); }
        private void AppendHardwareRows(string autoName, bool rm5)
        { AppendHardwareRows(autoName, rm5, GetDeviceState()); }
        private void AppendHardwareRows(string autoName, bool rm5, DeviceState st)
        {
            if (rm5)
            {
                _itemRm5 = new ListViewItem("RM5 Münzenbestand");
                _itemRm5.SubItems.Add("-");
                _itemRm5.SubItems.Add("");
                _itemRm5.SubItems.Add(autoName);
                _itemRm5.ForeColor = Color.FromArgb(255, 143, 0);
                _itemRm5.Group = _grpAutomat;
                lvKassenbestand.Items.Add(_itemRm5);
            }
            else
            {
                if (st == null || !st.SmartCoin1Disabled)
                {
                    // SmartCoin 1
                    _itemSmartCoin = new ListViewItem("SmartCoin");
                    _itemSmartCoin.SubItems.Add("-");
                    _itemSmartCoin.SubItems.Add("");
                    _itemSmartCoin.SubItems.Add(autoName);
                    _itemSmartCoin.ForeColor = Color.FromArgb(25, 118, 210);
                    _itemSmartCoin.Group = _grpAutomat;
                    lvKassenbestand.Items.Add(_itemSmartCoin);
                }

                if (st == null || !st.SmartCoin2Disabled)
                {
                    // SmartCoin 2
                    _itemSmartCoin2 = new ListViewItem("SmartCoin 2");
                    _itemSmartCoin2.SubItems.Add("-");
                    _itemSmartCoin2.SubItems.Add("");
                    _itemSmartCoin2.SubItems.Add(autoName);
                    _itemSmartCoin2.ForeColor = Color.FromArgb(25, 118, 210);
                    _itemSmartCoin2.Group = _grpAutomat;
                    lvKassenbestand.Items.Add(_itemSmartCoin2);
                }
            }

            if (st == null || !st.Nv2001Disabled)
            {
                _itemNv200 = new ListViewItem("NV200");
                _itemNv200.SubItems.Add("-");
                _itemNv200.SubItems.Add("");
                _itemNv200.SubItems.Add(autoName);
                _itemNv200.ForeColor = Color.FromArgb(46, 125, 50);
                _itemNv200.Group = _grpAutomat;
                lvKassenbestand.Items.Add(_itemNv200);

                _itemNv200Cashbox = new ListViewItem("NV200 Cashbox");
                _itemNv200Cashbox.SubItems.Add("-");
                _itemNv200Cashbox.SubItems.Add("");
                _itemNv200Cashbox.SubItems.Add(autoName);
                _itemNv200Cashbox.ForeColor = Color.FromArgb(46, 125, 50);
                _itemNv200Cashbox.Group = _grpAutomat;
                lvKassenbestand.Items.Add(_itemNv200Cashbox);
            }

            if (st == null || !st.Nv2002Disabled)
            {
                _itemNv2002 = new ListViewItem("NV200/2");
                _itemNv2002.SubItems.Add("-");
                _itemNv2002.SubItems.Add("");
                _itemNv2002.SubItems.Add(autoName);
                _itemNv2002.ForeColor = Color.FromArgb(46, 125, 50);
                _itemNv2002.Group = _grpAutomat;
                lvKassenbestand.Items.Add(_itemNv2002);

                _itemNv2002Cashbox = new ListViewItem("NV200/2 Cashbox");
                _itemNv2002Cashbox.SubItems.Add("-");
                _itemNv2002Cashbox.SubItems.Add("");
                _itemNv2002Cashbox.SubItems.Add(autoName);
                _itemNv2002Cashbox.ForeColor = Color.FromArgb(46, 125, 50);
                _itemNv2002Cashbox.Group = _grpAutomat;
                lvKassenbestand.Items.Add(_itemNv2002Cashbox);
            }
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

        // Header wird zentral über `ModernHeaderPanel` gezeichnet.
    }
}