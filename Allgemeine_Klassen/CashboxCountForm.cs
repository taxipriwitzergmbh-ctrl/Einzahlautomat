using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Geldautomat
{
    // Von Form zu UserControl geändert!
    public class CashboxCountForm : UserControl
    {
        private readonly NV200_SSP _ssp;
        private Label[] _lblDenom;
        private Label[] _lblCount;
        private Button[] _btnMinus;
        private Button[] _btnPlus;
        private int[] _counts; // Reihenfolge: 5,10,20,50,100,200,500
        private Label _lblSum;
        private Button _btnSave;
        private const int RowH = 64;
        private Label lblHinweis;
        private Label lblCashboxQuestion;
        

        public CashboxCountForm(NV200_SSP ssp)
        {
            _ssp = ssp ?? throw new ArgumentNullException(nameof(ssp));
            this.Dock = DockStyle.Fill;
            BuildLayout();
            LoadFromSsp();
            UpdateSum();
        }

        private void BuildLayout()
        {
            BackColor = Color.White;
            DoubleBuffered = true;

            // Info-Layout: schmaler und deutlich höher
            int infoWidth = 260;          // schmaler
            int infoPad = 16;
            int infoLeftMargin = 40;      // rechter Randabstand
            int infoHinweisHeight = 100;  // höher
            int infoQuestionHeight = 80; // höher

            // Hinweisfeld oben rechts
            lblHinweis = new Label
            {
                Text = "Hauptsächlich ist der Wert wichtig, in der Cashbox ist die Stückelung unrelevant, sie müssen somit nicht die einzelnen Scheine zählen.",
                Location = new Point(this.Width - (infoWidth + infoLeftMargin), 20),
                Size = new Size(infoWidth, infoHinweisHeight),
                Font = new Font("Segoe UI", 11F, FontStyle.Regular),
                ForeColor = Color.FromArgb(33, 33, 33),
                BackColor = Color.FromArgb(255, 255, 210),
                BorderStyle = BorderStyle.FixedSingle,
                TextAlign = ContentAlignment.TopLeft,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            Controls.Add(lblHinweis);

            // Fragefeld darunter
            lblCashboxQuestion = new Label
            {
                Text = "Richtige Cashbox? Beim Entnehmen müsste oben rechts bei Status Cashbox remove stehen.",
                Location = new Point(this.Width - (infoWidth + infoLeftMargin), 20 + infoHinweisHeight + infoPad),
                Size = new Size(infoWidth, infoQuestionHeight),
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(33, 33, 33),
                BackColor = Color.FromArgb(230, 240, 255),
                BorderStyle = BorderStyle.FixedSingle,
                TextAlign = ContentAlignment.TopLeft,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            Controls.Add(lblCashboxQuestion);

            // Geldschein Reihenfolge: 5,10,20,50,100,200,500
            string[] denomTexts = { "5 €", "10 €", "20 €", "50 €", "100 €", "200 €", "500 €" };
            _lblDenom = new Label[7];
            _lblCount = new Label[7];
            _btnMinus = new Button[7];
            _btnPlus = new Button[7];
            _counts = new int[7];

            int colSpacing = 12;
            int denomWidth = 120;
            int btnWidth = 48;
            int countWidth = 100;
            int startX = 40;

            for (int i = 0; i < 7; i++)
            {
                int y = 40 + i * RowH;
                int x = startX;

                var lDenom = new Label
                {
                    Text = denomTexts[i],
                    Location = new Point(x, y + 8),
                    Size = new Size(denomWidth, 44),
                    Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                    TextAlign = ContentAlignment.MiddleLeft
                };
                Controls.Add(lDenom);
                _lblDenom[i] = lDenom;
                x += denomWidth + colSpacing;

                var bMinus = new Button
                {
                    Text = "–",
                    Location = new Point(x, y),
                    Size = new Size(btnWidth, 44),
                    Font = new Font("Segoe UI Variable", 22F, FontStyle.Bold),
                    BackColor = Color.FromArgb(229, 57, 53),
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Tag = i
                };
                bMinus.FlatAppearance.BorderSize = 0;
                bMinus.Click += (s, e) => Change(i: (int)((Button)s).Tag, delta: -1);
                Controls.Add(bMinus);
                _btnMinus[i] = bMinus;
                x += btnWidth + colSpacing;

                var lCount = new Label
                {
                    Text = "0",
                    Location = new Point(x, y),
                    Size = new Size(countWidth, 44),
                    BorderStyle = BorderStyle.FixedSingle,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI Variable", 22F, FontStyle.Bold),
                    BackColor = Color.White
                };
                Controls.Add(lCount);
                _lblCount[i] = lCount;
                x += countWidth + colSpacing;

                var bPlus = new Button
                {
                    Text = "+",
                    Location = new Point(x, y),
                    Size = new Size(btnWidth, 44),
                    Font = new Font("Segoe UI Variable", 22F, FontStyle.Bold),
                    BackColor = Color.FromArgb(33, 150, 243),
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Tag = i
                };
                bPlus.FlatAppearance.BorderSize = 0;
                bPlus.Click += (s, e) => Change(i: (int)((Button)s).Tag, delta: +1);
                Controls.Add(bPlus);
                _btnPlus[i] = bPlus;
            }

            _lblSum = new Label
            {
                Text = "Summe: 0,00 €",
                Location = new Point(startX, 40 + 7 * RowH + 20),
                Size = new Size(300, 44),
                Font = new Font("Segoe UI Variable", 20F, FontStyle.Bold),
                ForeColor = Color.FromArgb(46, 125, 50)
            };
            Controls.Add(_lblSum);

            _btnSave = new Button
            {
                Text = "Speichern",
                Size = new Size(160, 52),
                Font = new Font("Segoe UI Variable", 18F, FontStyle.Bold),
                BackColor = Color.FromArgb(46, 125, 50),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Anchor = AnchorStyles.Right | AnchorStyles.Top
            };
            _btnSave.FlatAppearance.BorderSize = 0;
            _btnSave.Click += (s, e) => SaveAndClose();
            Controls.Add(_btnSave);
            // Rechtsbündig, gleiche Zeile wie 500 €
            _btnSave.Location = new Point(this.Width - _btnSave.Width - 40, 40 + 6 * RowH);

            this.Resize += (s, e) => { AdjustLayout(); };
            AdjustLayout();
            
        }

        private void AdjustLayout()
        {
            int colSpacing = 12;
            int denomWidth = 120;
            int btnWidth = 48;
            int countWidth = 100;
            int startX = 40;

            for (int i = 0; i < 7; i++)
            {
                int y = 40 + i * RowH;
                int x = startX;
                _lblDenom[i].Location = new Point(x, y + 8);
                x += denomWidth + colSpacing;
                _btnMinus[i].Location = new Point(x, y);
                x += btnWidth + colSpacing;
                _lblCount[i].Location = new Point(x, y);
                x += countWidth + colSpacing;
                _btnPlus[i].Location = new Point(x, y);
            }

            _lblSum.Location = new Point(startX, 40 + 7 * RowH + 20);
            _btnSave.Location = new Point(this.Width - _btnSave.Width - 40, 40 + 6 * RowH);

            
        }

        private void LoadFromSsp()
        {
            _counts[0] = Math.Max(0, _ssp.Cashbox_5_euro);
            _counts[1] = Math.Max(0, _ssp.Cashbox_10_euro);
            _counts[2] = Math.Max(0, _ssp.Cashbox_20_euro);
            _counts[3] = Math.Max(0, _ssp.Cashbox_50_euro);
            _counts[4] = Math.Max(0, _ssp.Cashbox_100_euro);
            _counts[5] = Math.Max(0, _ssp.Cashbox_200_euro);
            _counts[6] = Math.Max(0, _ssp.Cashbox_500_euro);

            for (int i = 0; i < 7; i++)
                _lblCount[i].Text = _counts[i].ToString();
        }

        private void Change(int i, int delta)
        {
            _counts[i] = Math.Max(0, _counts[i] + delta);
            _lblCount[i].Text = _counts[i].ToString();
            UpdateSum();
        }

        private void UpdateSum()
        {
            int[] values = { 5, 10, 20, 50, 100, 200, 500 };
            int cents = 0;
            for (int i = 0; i < 7; i++) cents += _counts[i] * values[i] * 100;
            _lblSum.Text = $"Summe: {(cents/100m):C2}";
        }

        internal void SaveAndClose()
        {
            try
            {
                _ssp.SetCashboxCounts(
                    _counts[0], _counts[1], _counts[2], _counts[3], _counts[4], _counts[5], _counts[6]);
                this.Parent?.Controls.Remove(this); // Schließt das Control
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Fehler beim Speichern der Cashbox:\r\n{ex.Message}", "Fehler",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        
    }
}
