using System;
using System.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.IO; // neu für INI-Auswertung
using System.Collections.Generic; // neu für Dictionaries
using TaMi_Einzahlautomat.Printing; // NEU

namespace TaMi_Einzahlautomat
{
    public class CashboxRemoveForm : Form
    {
        private readonly NV200_SSP _ssp;
        private DataTable _kassen;
        private decimal _cashboxEuro;
        private Panel headerPanel;
        private Label lblTitle;
        private Button btnClose;
        private FlowLayoutPanel _panel;
        private Button _btnAnBank;

        // NEU: Spaltenkopf Panel
        private Panel _columnsHeaderPanel;

        // Summary-Card unten
        private Panel _summaryCard;
        private Label _lblSumBank;
        private Label _lblSumCashbox;
        private Label _lblDiff;

        private int[] _abzuege; // in Cent
        private Label[] _lblBetrag;
        private int[] _maxAbzug;
        private int[] _firmenIds;
        private Label[] _lblBestandAlt;
        private Label[] _lblBestandNeu;
        private ToolTip _tip;

        private const int Schritt = 500; // 5€
        private const int RowHeight = 60; // Gesamthöhe je Zeile
        private const int ControlHeight = 44; // Höhe für Buttons/Labels in der Zeile

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

        private Dictionary<int,string> _firmenNamen;          // NEU: Name je FirmenId
        private Dictionary<int,decimal> _bestandAltMap;       // NEU: Ursprungsbestand je FirmenId
        private Dictionary<int,string> _ibanMap;              // NEU: IBAN je FirmenId

        public CashboxRemoveForm(NV200_SSP ssp)
        {
            _ssp = ssp;
            _cashboxEuro = _ssp.GetCashboxSumCent() / 100m;
            _tip = new ToolTip();
            BuildModernLayout();
            LoadKassenAsync();

            // Sicherstellen, dass das Fenster im Vordergrund bleibt
            this.Deactivate += (s, e) =>
            {
                try
                {
                    TopMost = true;
                    Activate();
                    BringToFront();
                }
                catch { }
            };
        }

        private void BuildModernLayout()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1280, 1024);
            BackColor = Color.White;
            DoubleBuffered = true;
            TopMost = true;

            // Header
            headerPanel = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(ClientSize.Width, 60),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };
            headerPanel.Paint += HeaderPanel_Paint;
            Controls.Add(headerPanel);

            lblTitle = new Label
            {
                Text = "Cashbox entfernen – Kassenaufteilung",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Variable", 22F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(24, 0),
                Size = new Size(800, 60),
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
                TabStop = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 80, 80);
            btnClose.Click += (s, e) => Close();
            headerPanel.Controls.Add(btnClose);

            try { Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, Width, Height, 24, 24)); } catch { }

            // NEU: Spaltenüberschriften Panel
            _columnsHeaderPanel = new Panel
            {
                Location = new Point(40, 80),
                Size = new Size(1200, 40),
                BackColor = Color.White,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            _columnsHeaderPanel.Paint += (s, e) =>
            {
                // Untere Trennlinie
                using (var pen = new Pen(Color.Gainsboro, 1))
                {
                    e.Graphics.DrawLine(pen, 0, _columnsHeaderPanel.Height - 1, _columnsHeaderPanel.Width, _columnsHeaderPanel.Height - 1);
                }
            };
            Controls.Add(_columnsHeaderPanel);

            // Positions konsistent zu den Zeilen (siehe LoadKassenAsync)
            AddHeaderLabel(_columnsHeaderPanel, "Kasse", new Rectangle(0, 5, 620, 30), ContentAlignment.MiddleLeft);
            AddHeaderLabel(_columnsHeaderPanel, "vorher", new Rectangle(640, 5, 140, 30), ContentAlignment.MiddleRight);
            AddHeaderLabel(_columnsHeaderPanel, "an Bank", new Rectangle(790, 5, 228, 30), ContentAlignment.MiddleCenter);
            AddHeaderLabel(_columnsHeaderPanel, "nachher", new Rectangle(1030, 5, 140, 30), ContentAlignment.MiddleRight);

            // Rows panel (verschoben nach unten)
            _panel = new FlowLayoutPanel
            {
                Location = new Point(40, 80 + _columnsHeaderPanel.Height),
                Size = new Size(1200, 760 - _columnsHeaderPanel.Height),
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.White,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };
            Controls.Add(_panel);

            // Summary Card
            _summaryCard = new Panel
            {
                Location = new Point(40, ClientSize.Height - 150),
                Size = new Size(460, 120),
                BackColor = Color.White,
                Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
            };
            _summaryCard.Paint += (s, e) =>
            {
                var r = _summaryCard.ClientRectangle;
                r.Width -= 1; r.Height -= 1;
                using (var p = new Pen(Color.Gainsboro)) e.Graphics.DrawRectangle(p, r);
            };
            Controls.Add(_summaryCard);

            var lbl1 = new Label { Text = "an Bank", Font = new Font("Segoe UI Variable", 12F), Location = new Point(12, 10), Size = new Size(160, 24) };
            _summaryCard.Controls.Add(lbl1);
            _lblSumBank = new Label
            {
                Text = "0,00 €",
                Font = new Font("Segoe UI Variable", 18F, FontStyle.Bold),
                ForeColor = Color.FromArgb(46, 125, 50),
                Location = new Point(180, 6),
                Size = new Size(260, 32),
                TextAlign = ContentAlignment.MiddleRight
            };
            _summaryCard.Controls.Add(_lblSumBank);

            var lbl2 = new Label { Text = "Cashbox", Font = new Font("Segoe UI Variable", 12F), Location = new Point(12, 44), Size = new Size(160, 24) };
            _summaryCard.Controls.Add(lbl2);
            _lblSumCashbox = new Label
            {
                Text = _cashboxEuro.ToString("C2"),
                Font = new Font("Segoe UI Variable", 16F, FontStyle.Bold),
                ForeColor = Color.FromArgb(25, 118, 210),
                Location = new Point(180, 42),
                Size = new Size(260, 28),
                TextAlign = ContentAlignment.MiddleRight
            };
            _summaryCard.Controls.Add(_lblSumCashbox);

            var sep = new Panel { Location = new Point(12, 74), Size = new Size(428, 1), BackColor = Color.Gainsboro };
            _summaryCard.Controls.Add(sep);

            _lblDiff = new Label
            {
                Text = "+ 0,00 €",
                Font = new Font("Segoe UI Variable", 14F, FontStyle.Bold),
                Location = new Point(12, 82),
                Size = new Size(428, 28),
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Color.OrangeRed
            };
            _summaryCard.Controls.Add(_lblDiff);

            // Action button
            _btnAnBank = new Button
            {
                Text = "an Bank",
                Location = new Point(ClientSize.Width - 230, ClientSize.Height - 124),
                Size = new Size(180, 60),
                Font = new Font("Segoe UI Variable", 22F, FontStyle.Bold),
                BackColor = Color.FromArgb(46, 125, 50),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Enabled = false,
                Anchor = AnchorStyles.Right | AnchorStyles.Bottom
            };
            _btnAnBank.FlatAppearance.BorderSize = 0;
            _btnAnBank.Click += BtnAnBank_Click;
            Controls.Add(_btnAnBank);

            Shown += (s, e) => { Activate(); BringToFront(); TopMost = true; };
            Resize += (s, e) =>
            {
                _summaryCard.Location = new Point(40, ClientSize.Height - 150);
                _btnAnBank.Location = new Point(ClientSize.Width - 230, ClientSize.Height - 124);
                _columnsHeaderPanel.Width = ClientSize.Width - 80; // 40 left + 40 right margin
                _panel.Width = ClientSize.Width - 80;
                _panel.Height = ClientSize.Height - 80 - _columnsHeaderPanel.Height - 260; // adjust remaining height
            };
        }

        // NEU: Hilfsmethode für Header-Labels
        private void AddHeaderLabel(Panel parent, string text, Rectangle bounds, ContentAlignment align)
        {
            var lbl = new Label
            {
                Text = text,
                Font = new Font("Segoe UI Variable", 13F, FontStyle.Bold),
                AutoSize = false,
                Location = bounds.Location,
                Size = bounds.Size,
                TextAlign = align,
                ForeColor = Color.FromArgb(45, 45, 45),
                BackColor = Color.White
            };
            parent.Controls.Add(lbl);
        }

        private async void LoadKassenAsync()
        {
            using (var db = new DatabaseHelper())
            {
                _kassen = await db.GetLatestKassenbestaendeAsync(AppSettings.AutomatenName);
            }

            var rows = _kassen.Rows.Cast<DataRow>()
                          .Where(r => Convert.ToInt32(r["FirmenId"]) != -1)
                          .ToList();
            int n = rows.Count;
            if (n == 0) return;

            _firmenNamen    = rows.ToDictionary(r => Convert.ToInt32(r["FirmenId"]), r => r["ManName"].ToString());
            _bestandAltMap  = rows.ToDictionary(r => Convert.ToInt32(r["FirmenId"]), r => Convert.ToDecimal(r["Kassenbestand"]));
            _ibanMap        = rows.ToDictionary(r => Convert.ToInt32(r["FirmenId"]), r => (r.Table.Columns.Contains("Kto1IBAN") && r["Kto1IBAN"] != DBNull.Value) ? Convert.ToString(r["Kto1IBAN"]).Trim() : string.Empty);

            decimal[] bastaende = rows.Select(r => Convert.ToDecimal(r["Kassenbestand"])) .ToArray();
            decimal sumBestandPos = bastaende.Where(b => b > 0m).Sum();
            int cashboxCents = (int)Math.Round(_cashboxEuro * 100);

            _abzuege      = new int[n];
            _maxAbzug     = new int[n];
            _firmenIds    = new int[n];
            _lblBetrag    = new Label[n];
            _lblBestandAlt= new Label[n];
            _lblBestandNeu= new Label[n];

            // Maximaler Abzug je Kasse (Bestand, abgerundet auf 5€)
            for (int i = 0; i < n; i++)
            {
                int maxCents = (int)Math.Floor(Math.Max(0m, bastaende[i]) * 100 / Schritt) * Schritt;
                _maxAbzug[i] = Math.Max(maxCents, 0);
                _firmenIds[i] = Convert.ToInt32(rows[i]["FirmenId"]);
            }

            // 1) Proportionale Initialverteilung (nur positive Bestände gewichten)
            int rest = cashboxCents;
            for (int i = 0; i < n; i++)
            {
                int anteil = 0;
                if (sumBestandPos > 0m && bastaende[i] > 0m)
                {
                    anteil = (int)Math.Floor((bastaende[i] / sumBestandPos) * cashboxCents / Schritt) * Schritt;
                }
                anteil = Math.Min(anteil, _maxAbzug[i]);
                _abzuege[i] = anteil;
                rest -= anteil;
            }
            // 2. Rest in 5€-Schritten an Kassen mit Luft verteilen
            bool changed = true;
            while (rest >= Schritt && changed)
            {
                changed = false;
                for (int i = 0; i < n && rest >= Schritt; i++)
                {
                    if (_abzuege[i] + Schritt <= _maxAbzug[i])
                    {
                        _abzuege[i] += Schritt;
                        rest -= Schritt;
                        changed = true;
                    }
                }
            }

            _panel.Controls.Clear();

            for (int i = 0; i < n; i++)
            {
                string kassenName = rows[i]["ManName"].ToString();
                decimal bestand = bastaende[i];

                var panelRow = new Panel
                {
                    Width = 1180,
                    Height = RowHeight,
                    BackColor = Color.White
                };

                int y = (RowHeight - ControlHeight) / 2; // vertikal mittig

                // Kassename (breit, mit Ellipsis + Tooltip)
                var lblKasse = new Label
                {
                    AutoSize = false,
                    UseCompatibleTextRendering = true,
                    Text = kassenName,
                    AutoEllipsis = true,
                    Width = 620, // breiter, damit nichts abgeschnitten wird
                    Height = ControlHeight,
                    Location = new Point(0, y),
                    Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                    TextAlign = ContentAlignment.MiddleLeft
                };
                _tip.SetToolTip(lblKasse, kassenName);
                panelRow.Controls.Add(lblKasse);

                // Bestand alt
                var lblBestandAlt = new Label
                {
                    AutoSize = false,
                    Text = bestand.ToString("C2"),
                    Width = 140,
                    Height = ControlHeight,
                    Location = new Point(640, y + 3),
                    Font = new Font("Segoe UI", 15F, FontStyle.Regular),
                    TextAlign = ContentAlignment.MiddleRight,
                    ForeColor = Color.DimGray
                };
                panelRow.Controls.Add(lblBestandAlt);
                _lblBestandAlt[i] = lblBestandAlt;

                // Minus-Button
                var btnMinus = new Button
                {
                    Text = "–",
                    Font = new Font("Segoe UI Variable", 22F, FontStyle.Bold),
                    Size = new Size(48, ControlHeight),
                    Location = new Point(790, y),
                    BackColor = Color.FromArgb(229, 57, 53),
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Tag = i
                };
                btnMinus.FlatAppearance.BorderSize = 0;
                btnMinus.FlatAppearance.MouseOverBackColor = Color.FromArgb(211, 47, 47);
                btnMinus.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, btnMinus.Width, btnMinus.Height, 12, 12));
                btnMinus.Click += BtnMinus_Click;
                panelRow.Controls.Add(btnMinus);

                // Betrag (Label-Box)
                var lblBetrag = new Label
                {
                    AutoSize = false,
                    Text = (_abzuege[i] / 100m).ToString("0.00") + " €",
                    Width = 120,
                    Height = ControlHeight,
                    Location = new Point(845, y),
                    Font = new Font("Segoe UI Variable", 22F, FontStyle.Bold),
                    TextAlign = ContentAlignment.MiddleCenter,
                    BorderStyle = BorderStyle.FixedSingle,
                    BackColor = Color.White
                };
                panelRow.Controls.Add(lblBetrag);
                _lblBetrag[i] = lblBetrag;

                // Plus-Button
                var btnPlus = new Button
                {
                    Text = "+",
                    Font = new Font("Segoe UI Variable", 22F, FontStyle.Bold),
                    Size = new Size(48, ControlHeight),
                    Location = new Point(970, y),
                    BackColor = Color.FromArgb(33, 150, 243),
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Tag = i
                };
                btnPlus.FlatAppearance.BorderSize = 0;
                btnPlus.FlatAppearance.MouseOverBackColor = Color.FromArgb(33, 203, 243);
                btnPlus.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, btnPlus.Width, btnPlus.Height, 12, 12));
                btnPlus.Click += BtnPlus_Click;
                panelRow.Controls.Add(btnPlus);

                // nach Abzug
                var lblBestandNeu = new Label
                {
                    AutoSize = false,
                    Text = (bestand - _abzuege[i] / 100m).ToString("C2"),
                    Width = 140,
                    Height = ControlHeight,
                    Location = new Point(1030, y + 3),
                    Font = new Font("Segoe UI", 15F, FontStyle.Regular),
                    TextAlign = ContentAlignment.MiddleRight,
                    ForeColor = Color.FromArgb(46, 125, 50)
                };
                panelRow.Controls.Add(lblBestandNeu);
                _lblBestandNeu[i] = lblBestandNeu;

                _panel.Controls.Add(panelRow);
            }

            ValidateSum();
        }

        private void BtnMinus_Click(object sender, EventArgs e)
        {
            int idx = (int)((Button)sender).Tag;
            if (_abzuege[idx] > 0)
            {
                _abzuege[idx] -= Schritt;
                if (_abzuege[idx] < 0) _abzuege[idx] = 0;
                _lblBetrag[idx].Text = (_abzuege[idx] / 100m).ToString("0.00") + " €";
                ValidateSum();
            }
        }

        private void BtnPlus_Click(object sender, EventArgs e)
        {
            int idx = (int)((Button)sender).Tag;
            if (_abzuege[idx] + Schritt <= _maxAbzug[idx])
            {
                _abzuege[idx] += Schritt;
                _lblBetrag[idx].Text = (_abzuege[idx] / 100m).ToString("0.00") + " €";
                ValidateSum();
            }
        }

        private void ValidateSum()
        {
            decimal sum = _abzuege.Sum() / 100m;
            for (int i = 0; i < _lblBestandAlt.Length; i++)
            {
                decimal bestandAlt = 0m;
                decimal.TryParse(_lblBestandAlt[i].Text.Replace("€", "").Trim(), NumberStyles.Currency, CultureInfo.CurrentCulture, out bestandAlt);
                decimal neu = Math.Max(0m, bestandAlt - _abzuege[i] / 100m);
                _lblBestandNeu[i].Text = neu.ToString("C2");
            }
            decimal diff = _cashboxEuro - sum; // Erwartet: 0,00

            _lblSumBank.Text = sum.ToString("C2");
            _lblSumCashbox.Text = _cashboxEuro.ToString("C2");
            _lblDiff.Text = (diff > 0 ? "+ " : diff < 0 ? "- " : "± ") + Math.Abs(diff).ToString("C2");
            _lblDiff.ForeColor = Math.Abs(diff) < 0.01m ? Color.FromArgb(46, 125, 50) : Color.OrangeRed;

            _btnAnBank.Enabled = Math.Abs(diff) < 0.01m && sum > 0m;
        }

        private async void BtnAnBank_Click(object sender, EventArgs e)
        {
            using (var db = new DatabaseHelper())
            {
                for (int i = 0; i < _abzuege.Length; i++)
                {
                    if (_abzuege[i] <= 0) continue;

                    decimal betragGesamt = -_abzuege[i] / 100m;
                    var kont = LoadAnBankKontierung(_firmenIds[i]);

                    var entry = new KassenbuchEntry
                    {
                        FirmenId       = _firmenIds[i],
                        AutomatenName  = AppSettings.AutomatenName,
                        Typ            = "Auszahlung",
                        Buchungstext   = "an Bank",
                        Betrag19       = 0m,
                        Betrag7        = 0m,
                        Betrag0        = betragGesamt,
                        BetragGesamt   = betragGesamt,
                        Kost1          = kont.Kost1,
                        Kost2          = kont.Kost2,
                        Konto          = kont.Konto
                    };
                    await db.InsertKassenbuchAsync(entry);
                }
            }

            // Quittung drucken (vor Reset damit Originalsumme vorhanden, aber wir nutzen bereits _cashboxEuro)
            try { PrintReceipt(); } catch { /* Druckfehler ignorieren */ }

            // Cashbox-Bestand zurücksetzen
            _ssp.SetCashboxCounts(0, 0, 0, 0, 0, 0, 0);

            MessageBox.Show("Buchungen erfolgreich erstellt und Cashbox zurückgesetzt.", "Fertig",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }

        // NEU: Quittungsdruck (Layout erweitert: Name, IBAN, Betragszeile + Leerzeile) + Logging
        private void PrintReceipt()
        {
            var cfg = ReceiptPrinterSettings.Load();
            if (!cfg.Enabled) return;

            if (cfg.AskUser)
            {
                if (MessageBox.Show(this, "Quittung drucken?", "Quittung",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
            }

            int width = Math.Max(42, cfg.CharsPerLine); // etwas breiter wirken
            string sep = new string('=', Math.Min(width, 64));
            string dash = new string('-', Math.Min(width, 64));

            // Titel block – optisch größer (Leerzeilen, Rahmen, zentriert)
            string title = "AUSZAHLUNG AN BANK";
            string centeredTitle = Center(title, width).ToUpperInvariant();

            decimal sumBank = _abzuege.Sum() / 100m;

            var lines = new List<string>
            {
                sep,
                Center("QUITTUNG", width),
                centeredTitle,
                sep,
                "",
                Center($"Automat: {AppSettings.AutomatenName}", width),
                Center($"Cashbox Gesamt: {_cashboxEuro:N2} EUR", width),
                Center($"Summe an Bank : {sumBank:N2} EUR", width),
                "",
                dash,
                Center("VERTEILUNG", width),
                dash,
            };

            var logLines = new List<string>(lines); // Für AppLogger identisch

            for (int i = 0; i < _abzuege.Length; i++)
            {
                if (_abzuege[i] <= 0) continue;

                int mandantId = _firmenIds[i];
                string name = (_firmenNamen != null && _firmenNamen.ContainsKey(mandantId)) ? _firmenNamen[mandantId] : ("ID " + mandantId);
                decimal alt = (_bestandAltMap != null && _bestandAltMap.ContainsKey(mandantId)) ? _bestandAltMap[mandantId] : 0m;
                decimal abz = _abzuege[i] / 100m;
                decimal neu = Math.Max(0m, alt - abz);
                string iban = (_ibanMap != null && _ibanMap.ContainsKey(mandantId) && !string.IsNullOrWhiteSpace(_ibanMap[mandantId])) ? _ibanMap[mandantId] : "(keine IBAN)";

                // Namezeile etwas größer wirken lassen (Leerzeile davor und danach)
                lines.Add(Center(TrimName(name, width), width));
                lines.Add(Center("IBAN: " + iban, width));

                string left = ("-" + abz.ToString("N2") + " EUR").PadLeft(12);
                string right = $"Alt: {alt,7:N2}  Neu: {neu,7:N2}";
                string combined = left + new string(' ', Math.Max(2, width - left.Length - right.Length)) + right;
                lines.Add(combined);
                lines.Add("");

                logLines.Add(lines[lines.Count - 4]); // Name
                logLines.Add(lines[lines.Count - 3]); // IBAN
                logLines.Add(lines[lines.Count - 2]); // Betragszeile
                logLines.Add(lines[lines.Count - 1]); // Leerzeile
            }

            lines.Add(dash);
            decimal diff = _cashboxEuro - sumBank;
            lines.Add(Center($"Differenz (soll 0,00): {diff:N2} EUR", width));
            lines.Add(sep);

            logLines.Add(lines[lines.Count - 3]); // separator (dash)
            logLines.Add(lines[lines.Count - 2]); // diff line
            logLines.Add(lines[lines.Count - 1]); // end sep

            try
            {
                var printer = new ReceiptPrinter(cfg);
                printer.PrintSimpleReceipt("AUSZAHLUNG", lines);
            }
            catch { }

            try
            {
                AppLogger.Log("Cashbox Auszahlung an Bank:");
                foreach (var l in logLines)
                    AppLogger.Log(l);
            }
            catch { }
        }

        private static string Center(string text, int width)
        {
            if (string.IsNullOrEmpty(text)) return new string(' ', Math.Max(0, width));
            text = text.Trim();
            if (text.Length >= width) return text;
            int pad = (width - text.Length) / 2;
            return new string(' ', Math.Max(0, pad)) + text + new string(' ', Math.Max(0, width - text.Length - pad));
        }

        private string TrimName(string name, int max)
        {
            if (string.IsNullOrEmpty(name)) return "";
            name = name.Trim();
            return name.Length <= max ? name : (name.Substring(0, max - 1) + "…");
        }

        private static Dictionary<int, Kontierung> _anBankPerMandant;
        private static Kontierung _anBankDefault;
        private static DateTime _anBankLastLoadUtc = DateTime.MinValue;
        private static readonly TimeSpan _anBankReload = TimeSpan.FromMinutes(5);

        private static Kontierung LoadAnBankKontierung(int mandantId)
        {
            try
            {
                if (_anBankPerMandant == null || (DateTime.UtcNow - _anBankLastLoadUtc) > _anBankReload)
                {
                    _anBankPerMandant = new Dictionary<int, Kontierung>();
                    _anBankDefault = new Kontierung();
                    _anBankLastLoadUtc = DateTime.UtcNow;
                    string ini = AppSettings.IniPath;
                    if (!File.Exists(ini)) return new Kontierung();
                    var lines = File.ReadAllLines(ini);
                    bool inSection = false;
                    foreach (var raw in lines)
                    {
                        var line = raw.Trim();
                        if (line.Length == 0) continue;
                        if (line.StartsWith("#") || line.StartsWith(";") || line.StartsWith("//")) continue;
                        if (line.StartsWith("["))
                        {
                            inSection = line.Equals("[an Bank]", StringComparison.OrdinalIgnoreCase);
                            continue;
                        }
                        if (!inSection) continue;
                        // Ende bei nächster Section
                        if (line.StartsWith("[")) break;

                        // Tokens (Whitespace getrennt)
                        var tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (tokens.Length == 0) continue;
                        int? currentMandant = null; // null = nicht gefunden
                        for (int t = 0; t < tokens.Length; t++)
                        {
                            var tok = tokens[t];
                            if (tok.StartsWith("ManID", StringComparison.OrdinalIgnoreCase))
                            {
                                var parts = tok.Split('=');
                                if (parts.Length == 2 && int.TryParse(parts[1], out var mid))
                                {
                                    currentMandant = mid;
                                }
                                else
                                {
                                    // Zeile mit Default (kein '=' hinter ManID)
                                    currentMandant = -1; // -1 als Marker für Default
                                }
                            }
                        }
                        if (currentMandant == null) continue; // keine ManID Angabe

                        // Ziel-Kontierung bestimmen (Default oder spezifisch)
                        Kontierung target;
                        if (currentMandant == -1)
                        {
                            target = _anBankDefault;
                        }
                        else
                        {
                            if (!_anBankPerMandant.TryGetValue(currentMandant.Value, out target))
                            {
                                target = new Kontierung();
                                _anBankPerMandant[currentMandant.Value] = target;
                            }
                        }

                        // Werte extrahieren
                        foreach (var tok in tokens)
                        {
                            var kv = tok.Split('=');
                            if (kv.Length != 2) continue;
                            if (kv[0].Equals("Kost1", StringComparison.OrdinalIgnoreCase) && int.TryParse(kv[1], out var k1)) target.Kost1 = k1;
                            else if (kv[0].Equals("Kost2", StringComparison.OrdinalIgnoreCase) && int.TryParse(kv[1], out var k2)) target.Kost2 = k2;
                            else if (kv[0].Equals("Konto", StringComparison.OrdinalIgnoreCase) && int.TryParse(kv[1], out var kto)) target.Konto = kto;
                        }
                    }
                }
                // Ergebnis: spezifisch oder Default
                if (_anBankPerMandant != null && _anBankPerMandant.TryGetValue(mandantId, out var spec))
                {
                    // fehlende Felder aus Default ergänzen
                    return new Kontierung
                    {
                        Kost1 = spec.Kost1 != 0 ? spec.Kost1 : (_anBankDefault?.Kost1 ?? 0),
                        Kost2 = spec.Kost2 != 0 ? spec.Kost2 : (_anBankDefault?.Kost2 ?? 0),
                        Konto = spec.Konto != 0 ? spec.Konto : (_anBankDefault?.Konto ?? 0)
                    };
                }
                return new Kontierung
                {
                    Kost1 = _anBankDefault?.Kost1 ?? 0,
                    Kost2 = _anBankDefault?.Kost2 ?? 0,
                    Konto = _anBankDefault?.Konto ?? 0
                };
            }
            catch { return new Kontierung(); }
        }

        private void HeaderPanel_Paint(object sender, PaintEventArgs e)
        {
            using (var brush = new LinearGradientBrush(headerPanel.ClientRectangle,
                Color.FromArgb(33, 150, 243), Color.FromArgb(33, 203, 243), 0f))
            {
                e.Graphics.FillRectangle(brush, headerPanel.ClientRectangle);
            }
        }
    }
}