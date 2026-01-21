using System;
using System.Drawing;
using System.Windows.Forms;
using System.Threading.Tasks;
using TaMi_Einzahlautomat.Coins;

namespace TaMi_Einzahlautomat
{
    public class KassensturzForm : Form
    {
        private Label lblKassendifferenz;
        private Label lblHinweis;
        private Button btnNV200_1;
        private Button btnNV200_2;
        private Button btnSmartcoin1;
        private Button btnSmartcoin2;
        private Button btnRm5; // NEU: RM5 Button
        private Panel headerPanel;
        private Label lblTitle;
        private Button btnClose;
        private Point _mouseDownLocation;

        public KassensturzForm()
        {
            InitializeLayout();
            LoadKassendifferenzAsync();
        }

        private void InitializeLayout()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(500, 640); // Fenster noch größer
            BackColor = Color.White;
            DoubleBuffered = true;

            // Prüfen, ob NV200/2 global deaktiviert ist (Flag oder INI)
            bool nv2Disabled = false;
            try
            {
                nv2Disabled = AdminNV200_2Form.DeviceDisabled;
                if (!nv2Disabled)
                {
                    var dv = IniHelper.ReadValue("NV200/2", "Disabled", AppSettings.IniPath);
                    if (!string.IsNullOrWhiteSpace(dv))
                        nv2Disabled = dv.Trim().Equals("1", StringComparison.OrdinalIgnoreCase) || dv.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { }

            // Header (wie in den anderen Fenstern ganz oben und zuerst hinzugefügt)
            headerPanel = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(ClientSize.Width, 60),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };
            headerPanel.BackColor = Color.FromArgb(33, 150, 243);
            headerPanel.MouseDown += HeaderPanel_MouseDown;
            headerPanel.MouseMove += HeaderPanel_MouseMove;
            Controls.Add(headerPanel);
            headerPanel.BringToFront();

            lblTitle = new Label
            {
                Text = "Kassensturz",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Variable", 18F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(24, 0),
                Size = new Size(300, 60),
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

            // Kassendifferenz und Hinweisfeld unterhalb des Headers
            lblKassendifferenz = new Label
            {
                Text = "Kassendifferenz: ...",
                Font = new Font("Segoe UI Variable", 15F, FontStyle.Bold),
                ForeColor = Color.FromArgb(33, 150, 243),
                Location = new Point(20, 70),
                Size = new Size(460, 32),
                TextAlign = ContentAlignment.MiddleLeft
            };
            Controls.Add(lblKassendifferenz);

            lblHinweis = new Label
            {
                Text = "Beim Kassensturz werden die einzelnen Komponenten entleert, wieder befüllt und gezählt. So kann der tatsächliche Bestand exakt ermittelt werden. Durch Drücken der Buttons werden die jeweiligen Geräte entleert, neu befüllt und gezählt.",
                Font = new Font("Segoe UI", 11F, FontStyle.Regular),
                ForeColor = Color.DimGray,
                Location = new Point(20, 108),
                Size = new Size(460, 120),
                TextAlign = ContentAlignment.TopLeft,
                AutoSize = false
            };
            Controls.Add(lblHinweis);

            // Buttons Basis-Layout
            int btnWidth = 350;
            int btnHeight = 60;
            int startX = (ClientSize.Width - btnWidth) / 2;
            int startY = 250;
            int spacing = 20;

            btnNV200_1 = new Button
            {
                Text = "NV200/1",
                Size = new Size(btnWidth, btnHeight),
                Location = new Point(startX, startY),
                Font = new Font("Segoe UI Variable", 16F, FontStyle.Bold),
                BackColor = Color.FromArgb(33, 150, 243),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnNV200_1.FlatAppearance.BorderSize = 0;
            btnNV200_1.Click += BtnNV200_1_Click;
            Controls.Add(btnNV200_1);

            btnNV200_2 = new Button
            {
                Text = "NV200/2",
                Size = new Size(btnWidth, btnHeight),
                Location = new Point(startX, startY + (btnHeight + spacing)),
                Font = new Font("Segoe UI Variable", 16F, FontStyle.Bold),
                BackColor = Color.FromArgb(33, 150, 243),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnNV200_2.FlatAppearance.BorderSize = 0;
            btnNV200_2.Click += BtnNV200_2_Click;
            Controls.Add(btnNV200_2);

            // SmartCoin Buttons standardmäßig anlegen
            btnSmartcoin1 = new Button
            {
                Text = "Smartcoin 1",
                Size = new Size(btnWidth, btnHeight),
                Location = new Point(startX, startY + 2 * (btnHeight + spacing)),
                Font = new Font("Segoe UI Variable", 16F, FontStyle.Bold),
                BackColor = Color.FromArgb(33, 150, 243),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnSmartcoin1.FlatAppearance.BorderSize = 0;
            btnSmartcoin1.Click += BtnSmartcoin1_Click;
            Controls.Add(btnSmartcoin1);

            btnSmartcoin2 = new Button
            {
                Text = "Smartcoin 2",
                Size = new Size(btnWidth, btnHeight),
                Location = new Point(startX, startY + 3 * (btnHeight + spacing)),
                Font = new Font("Segoe UI Variable", 16F, FontStyle.Bold),
                BackColor = Color.FromArgb(33, 150, 243),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnSmartcoin2.FlatAppearance.BorderSize = 0;
            btnSmartcoin2.Click += BtnSmartcoin2_Click;
            Controls.Add(btnSmartcoin2);

            // RM5 Erkennung und UI-Anpassung
            try
            {
                bool rm5 = IsRm5Active();
                if (rm5)
                {
                    // SmartCoin Buttons ausblenden
                    btnSmartcoin1.Visible = false;
                    btnSmartcoin2.Visible = false;

                    btnRm5 = new Button
                    {
                        Text = "Münzen RM5",
                        Size = new Size(btnWidth, btnHeight),
                        Location = new Point(startX, startY + 2 * (btnHeight + spacing)), // Position von Smartcoin 1
                        Font = new Font("Segoe UI Variable", 16F, FontStyle.Bold),
                        BackColor = Color.FromArgb(33, 150, 243),
                        ForeColor = Color.White,
                        FlatStyle = FlatStyle.Flat
                    };
                    btnRm5.FlatAppearance.BorderSize = 0;
                    btnRm5.Click += BtnRm5_Click;
                    Controls.Add(btnRm5);
                }
            }
            catch { }

            // NV200/2 Button ggf. ausblenden und nachfolgende Buttons nach oben schieben
            if (nv2Disabled)
            {
                if (btnNV200_2 != null) btnNV200_2.Visible = false;
                int delta = btnHeight + spacing;
                if (btnRm5 != null && btnRm5.Visible)
                {
                    btnRm5.Location = new Point(btnRm5.Location.X, btnRm5.Location.Y - delta);
                }
                else
                {
                    btnSmartcoin1.Location = new Point(btnSmartcoin1.Location.X, btnSmartcoin1.Location.Y - delta);
                    btnSmartcoin2.Location = new Point(btnSmartcoin2.Location.X, btnSmartcoin2.Location.Y - delta);
                }
            }

            // Rahmen
            this.Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(120, 120, 120), 2))
                {
                    e.Graphics.DrawRectangle(pen, 1, 1, this.ClientSize.Width - 3, this.ClientSize.Height - 3);
                }
            };
        }

        private bool IsRm5Active()
        {
            try
            {
                // Erst aktives Gerät prüfen
                if (CoinManager.Instance != null && CoinManager.Instance is Rm5CctalkValidator) return true;
                // INI lesen
                string t = IniHelper.ReadValue("SmartCoin", "Typ", AppSettings.IniPath);
                if (!string.IsNullOrWhiteSpace(t) && t.Trim().Equals("RM5", StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { }
            return false;
        }

        private void BtnRm5_Click(object sender, EventArgs e)
        {
            try
            {
                if (CoinManager.Instance == null) CoinManager.InitFromIni(AppSettings.IniPath);
                var rm5 = CoinManager.Instance as Rm5CctalkValidator;
                if (rm5 == null)
                {
                    MessageBox.Show(this, "RM5 nicht verfügbar.", "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (!rm5.Connected) { try { rm5.Connect(); } catch { } }
                // Dialog anzeigen
                using (var dlg = new KassensturzRm5Form(rm5)) { dlg.ShowDialog(this); }
            }
            catch (Exception ex)
            {
                try { AppLogger.Log("[Kassensturz] RM5 Fehler: " + ex.Message); } catch { }
                MessageBox.Show(this, "Fehler RM5: " + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void LoadKassendifferenzAsync()
        {
            try
            {
                var snapshot = KassenSummary.Difference;
                if (snapshot.HasValue)
                {
                    SetDiff(snapshot.Value);
                    return;
                }
                decimal diff = await GetKassendifferenzAsync();
                SetDiff(diff);
            }
            catch
            {
                lblKassendifferenz.Text = "Kassendifferenz: Fehler";
                lblKassendifferenz.ForeColor = Color.OrangeRed;
            }
        }

        private void SetDiff(decimal diff)
        {
            lblKassendifferenz.Text = $"Kassendifferenz: {diff:C2}";
            lblKassendifferenz.ForeColor = diff == 0m ? Color.FromArgb(46, 125, 50) : Color.OrangeRed;
        }

        // NEU: Hilfsroutine – versucht bis zu 'tries' mal frische Level zu holen
        private async Task<int[]> TryGetFreshLevelsAsync(SmartCoinV1 sc, int tries = 4, int delayMs = 180)
        {
            if (sc == null) return null;
            try { if (!sc.Connected) return null; } catch { return null; }
            int[] last = null;
            for (int i = 0; i < tries; i++)
            {
                try { sc.RequestCoinLevels(); } catch { }
                await Task.Delay(delayMs);
                try { last = sc.GetCoinAvailability(); } catch { last = null; }
                if (last != null && last.Length >= 8)
                {
                    bool anyKnown = false;
                    for (int k = 0; k < 8; k++) if (last[k] >= 0) { anyKnown = true; break; }
                    if (anyKnown) return last;
                }
            }
            return null; // keine verlässlichen frischen Daten
        }

        private decimal SumCoinsEuro(int[] lv)
        {
            if (lv == null || lv.Length < 8) return 0m;
            int safe(int i) => lv[i] < 0 ? 0 : lv[i];
            long cent = 0; int[] vals = { 1,2,5,10,20,50,100,200 };
            for (int i = 0; i < 8; i++) cent += (long)safe(i) * vals[i];
            return cent / 100m;
        }

        private async Task<decimal> GetKassendifferenzAsync()
        {
            var sn = KassenSummary.Difference; if (sn.HasValue) return sn.Value;

            decimal sumAutomat = 0m; decimal sumKassen = 0m;
            string autoName = AppSettings.AutomatenName; if (string.IsNullOrWhiteSpace(autoName)) autoName = AppSettings.LoadAutomatenNameFromIni() ?? "";

            // DB Kassenbestände laden (inkl. Personalguthaben – kein Filter mehr)
            using (var db = new DatabaseHelper())
            {
                var dt = await db.GetLatestKassenbestaendeAsync(autoName);
                foreach (System.Data.DataRow row in dt.Rows)
                {
                    decimal bestand = row["Kassenbestand"] == DBNull.Value ? 0m : Convert.ToDecimal(row["Kassenbestand"]);
                    sumKassen += bestand;
                }
            }

            // COINS (SmartCoin oder RM5 Fallback) --------------------------------------------
            SmartCoinV1 sc1 = null, sc2 = null;
            try { if (CoinManager.Instance == null) CoinManager.InitFromIni(AppSettings.IniPath); } catch { }
            try { sc1 = CoinManager.Instance as SmartCoinV1; } catch { }
            try { if (Coin2Manager.Instance == null) Coin2Manager.InitFromIni(AppSettings.IniPath); } catch { }
            try { sc2 = Coin2Manager.Instance as SmartCoinV1; } catch { }

            int[] lv1 = null; int[] lv2 = null;
            try { lv1 = await TryGetFreshLevelsAsync(sc1); } catch { }
            try { lv2 = await TryGetFreshLevelsAsync(sc2); } catch { }

            // Fallback: Wenn kein SmartCoin-Level, aber RM5 aktiv -> RM5 Levels holen
            if (lv1 == null || lv1.Length < 8)
            {
                try
                {
                    if (CoinManager.Instance is Rm5CctalkValidator rm5a)
                    {
                        lv1 = rm5a.GetCoinAvailability();
                    }
                }
                catch { }
            }
            if (lv2 == null || lv2.Length < 8)
            {
                try
                {
                    if (Coin2Manager.Instance is Rm5CctalkValidator rm5b)
                    {
                        lv2 = rm5b.GetCoinAvailability();
                    }
                }
                catch { }
            }

            // Münzen addieren (wenn irgendein Level bekannt ist)
            if (lv1 != null) sumAutomat += SumCoinsEuro(lv1);
            if (lv2 != null) sumAutomat += SumCoinsEuro(lv2);

            // NV200/1 + NV200/2 --------------------------------
            try
            {
                var nv1 = Program.NV200Instance;
                if (nv1 != null)
                {
                    try { nv1.Payout_angleichen(); } catch { }
                    sumAutomat += 5m * nv1.Payout_5_euro + 10m * nv1.Payout_10_euro + 20m * nv1.Payout_20_euro + 50m * nv1.Payout_50_euro + 100m * nv1.Payout_100_euro + 200m * nv1.Payout_200_euro + 500m * nv1.Payout_500_euro;
                    try { sumAutomat += nv1.GetCashboxSumCent() / 100m; } catch { }
                }
            }
            catch { }
            try
            {
                var nv2 = Program.NV2002Instance;
                if (nv2 != null)
                {
                    try { nv2.Payout_angleichen(); } catch { }
                    sumAutomat += 5m * nv2.Payout_5_euro + 10m * nv2.Payout_10_euro + 20m * nv2.Payout_20_euro + 50m * nv2.Payout_50_euro + 100m * nv2.Payout_100_euro + 200m * nv2.Payout_200_euro + 500m * nv2.Payout_500_euro;
                    try { sumAutomat += nv2.GetCashboxSumCent() / 100m; } catch { }
                }
            }
            catch { }

            decimal diffFinal = sumAutomat - sumKassen;
            try { KassenSummary.Update(sumAutomat, sumKassen); } catch { }
            return diffFinal;
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

        private void BtnSmartcoin1_Click(object sender, EventArgs e)
        {
            try
            {
                // Nachinitialisieren falls nicht geschehen (verhindert Null-Referenzen)
                if (CoinManager.Instance == null) CoinManager.InitFromIni(AppSettings.IniPath);
                var smartCoin = CoinManager.Instance as SmartCoinV1;
                if (smartCoin == null)
                {
                    MessageBox.Show(this, "SmartCoin 1 nicht verfügbar oder falscher Gerätetyp.", "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                try { if (!smartCoin.Connected) smartCoin.Connect(); } catch { }
                using (var dlg = new KassensturzSmartCoinForm(smartCoin)) { dlg.ShowDialog(this); }
            }
            catch (TypeLoadException tle)
            {
                try { AppLogger.Log("[Kassensturz] SmartCoin1 TypeLoadException: " + tle.Message); } catch { }
                MessageBox.Show(this, "SmartCoin Bibliothek (ITLlib) fehlt oder ist inkompatibel.\r\n" + tle.Message, "SmartCoin Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                try { AppLogger.Log("[Kassensturz] SmartCoin1 Fehler: " + ex.Message); } catch { }
                MessageBox.Show(this, "Fehler beim Öffnen SmartCoin1: " + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnSmartcoin2_Click(object sender, EventArgs e)
        {
            try
            {
                // Zweite Instanz initialisieren
                if (Coin2Manager.Instance == null) Coin2Manager.InitFromIni(AppSettings.IniPath);
                var smartCoin2 = Coin2Manager.Instance as SmartCoinV1;
                if (smartCoin2 == null)
                {
                    MessageBox.Show(this, "SmartCoin 2 nicht verfügbar oder falscher Gerätetyp.", "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                try { if (!smartCoin2.Connected) smartCoin2.Connect(); } catch { }
                using (var dlg = new KassensturzSmartCoinForm(smartCoin2)) { dlg.ShowDialog(this); }
            }
            catch (TypeLoadException tle)
            {
                try { AppLogger.Log("[Kassensturz] SmartCoin2 TypeLoadException: " + tle.Message); } catch { }
                MessageBox.Show(this, "SmartCoin2 Bibliothek (ITLlib) fehlt oder ist inkompatibel.\r\n" + tle.Message, "SmartCoin2 Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                try { AppLogger.Log("[Kassensturz] SmartCoin2 Fehler: " + ex.Message); } catch { }
                MessageBox.Show(this, "Fehler beim Öffnen SmartCoin2: " + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnNV200_1_Click(object sender, EventArgs e)
        {
            try
            {
                var nv200 = Program.NV200Instance;
                if (nv200 == null) { MessageBox.Show(this, "Kein NV200-Gerät verbunden.", "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
                using (var dlg = new KassensturzNV200(nv200)) { dlg.ShowDialog(this); }
            }
            catch (Exception ex)
            {
                try { AppLogger.Log("[Kassensturz] NV200/1 Fehler: " + ex.Message); } catch { }
                MessageBox.Show(this, "Fehler beim Öffnen NV200/1: " + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnNV200_2_Click(object sender, EventArgs e)
        {
            try
            {
                var nv200 = Program.NV2002Instance;
                if (nv200 == null) { MessageBox.Show(this, "Kein NV200/2-Gerät verbunden.", "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
                using (var dlg = new KassensturzNV200(nv200)) { dlg.ShowDialog(this); }
            }
            catch (Exception ex)
            {
                try { AppLogger.Log("[Kassensturz] NV200/2 Fehler: " + ex.Message); } catch { }
                MessageBox.Show(this, "Fehler beim Öffnen NV200/2: " + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
