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
using TaMi_Einzahlautomat.UI.Layout;

namespace TaMi_Einzahlautomat
{
    public class CashboxRemoveForm : Form
    {
        private readonly NV200_SSP _ssp;
        private DataTable _kassen;
        private decimal _cashboxEuro;
        private ModernHeaderPanel _header;
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
        private int _schritt = 500; // dynamische Schrittweite in Cent (Standard 5€)
        private ComboBox _cmbStep; // Auswahl für Schrittweite
        private Label _lblStepCaption;

        private const int RowHeight = 60; // Gesamthöhe je Zeile
        private const int ControlHeight = 44; // Höhe für Buttons/Labels in der Zeile

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

        private Dictionary<int,string> _firmenNamen;          // NEU: Name je FirmenId
        private Dictionary<int,decimal> _bestandAltMap;       // NEU: Ursprungsbestand je FirmenId
        private Dictionary<int,string> _ibanMap;              // NEU: IBAN je FirmenId

        private bool _returnToCashboxConfirmed = false;
        private DenomPlan? _confirmedPlan;

        public CashboxRemoveForm(NV200_SSP ssp)
        {
            _ssp = ssp;
            _cashboxEuro = _ssp.GetCashboxSumCent() / 100m;
            _tip = new ToolTip();
            BuildModernLayout();

            // Daten/Rows erst nach dem ersten Paint laden, damit das Fenster sofort „steht“
            Shown += (s, e) =>
            {
                try { BeginInvoke((Action)(() => LoadKassenAsync())); } catch { }
            };

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
            SuspendLayout();
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1280, 1024);
            BackColor = Color.White;
            DoubleBuffered = true;
            TopMost = true;

            _header = new ModernHeaderPanel { Title = "Cashbox entfernen - Kassenaufteilung" };
            _header.CloseClicked += () => { try { Close(); } catch { } };
            Controls.Add(_header);
            _header.BringToFront();
            try { _header.ApplyRoundedRegionToForm(this); } catch { }

            // Schrittweite-Auswahl im Header (rechts)
            _lblStepCaption = new Label
            {
                Text = "Schritt:",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Segoe UI Variable", 12F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(ClientSize.Width - 360, 16),
                Size = new Size(80, 28),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                BackColor = Color.Transparent
            };
            _header.Controls.Add(_lblStepCaption);

            _cmbStep = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(ClientSize.Width - 270, 16),
                Size = new Size(150, 28),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Font = new Font("Segoe UI", 11F, FontStyle.Regular),
                BackColor = Color.White
            };
            _cmbStep.Items.AddRange(new object[] { "5 €", "10 €", "100 €", "1000 €" });
            _cmbStep.SelectedIndex = 0; // 5€
            _cmbStep.SelectedIndexChanged += (s, e) =>
            {
                var sel = Convert.ToString(_cmbStep.SelectedItem);
                switch (sel)
                {
                    case "5 €": _schritt = 500; break;
                    case "10 €": _schritt = 1000; break;
                    case "100 €": _schritt = 10000; break;
                    case "1000 €": _schritt = 100000; break;
                    default: _schritt = 500; break;
                }
                // Werte auf neue Schrittweite runden/limitieren
                if (_abzuege != null && _maxAbzug != null && _lblBestandAlt != null)
                {
                    for (int i = 0; i < _abzuege.Length; i++)
                    {
                        decimal bestandAlt;
                        decimal.TryParse(_lblBestandAlt[i].Text.Replace("€", "").Trim(), NumberStyles.Currency, CultureInfo.CurrentCulture, out bestandAlt);
                        int maxCents = (int)Math.Floor(Math.Max(0m, bestandAlt) * 100 / _schritt) * _schritt;
                        _maxAbzug[i] = Math.Max(0, maxCents);
                        // Snap aktueller Abzug auf neue Schrittweite und Kappung
                        _abzuege[i] = Math.Min(_abzuege[i], _maxAbzug[i]);
                        _abzuege[i] = (int)Math.Floor(_abzuege[i] / (decimal)_schritt) * _schritt;
                        if (_lblBetrag != null && _lblBetrag[i] != null)
                            _lblBetrag[i].Text = (_abzuege[i] / 100m).ToString("0.00") + " €";
                    }
                    ValidateSum();
                }
            };
            _header.Controls.Add(_cmbStep);

            // NEU: Spaltenübschriften Panel
            _columnsHeaderPanel = new Panel
            {
                Location = new Point(40, 80),
                Size = new Size(1200, 40),
                BackColor = Color.White,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            TryEnableDoubleBuffer(_columnsHeaderPanel);
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
            // Fix: Spalten etwas nach links rücken, damit "nachher" nicht abgeschnitten wird
            AddHeaderLabel(_columnsHeaderPanel, "Kasse", new Rectangle(0, 5, 560, 30), ContentAlignment.MiddleLeft);
            AddHeaderLabel(_columnsHeaderPanel, "vorher", new Rectangle(580, 5, 120, 30), ContentAlignment.MiddleRight);
            AddHeaderLabel(_columnsHeaderPanel, "an Bank", new Rectangle(710, 5, 250, 30), ContentAlignment.MiddleCenter);
            AddHeaderLabel(_columnsHeaderPanel, "nachher", new Rectangle(970, 5, 120, 30), ContentAlignment.MiddleRight);

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
            TryEnableDoubleBuffer(_panel);
            Controls.Add(_panel);

            // Summary Card
            _summaryCard = new Panel
            {
                Location = new Point(40, ClientSize.Height - 150),
                Size = new Size(460, 120),
                BackColor = Color.White,
                Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
            };
            TryEnableDoubleBuffer(_summaryCard);
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

                if (_lblStepCaption != null) _lblStepCaption.Location = new Point(ClientSize.Width - 360, 16);
                if (_cmbStep != null) _cmbStep.Location = new Point(ClientSize.Width - 270, 16);
            };

            ResumeLayout(performLayout: true);
        }

        private static void TryEnableDoubleBuffer(Control c)
        {
            if (c == null) return;
            try
            {
                var pi = typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                pi?.SetValue(c, true, null);
            }
            catch { }
        }

        private struct DenomPlan
        {
            public int ReturnCents;
            public int NewBankCents;
            public int[] RemainingCounts; // 5,10,20,50,100,200,500
            public int[] ReturnCounts;    // 5,10,20,50,100,200,500
        }

        private DenomPlan BuildReturnPlan(int diffCents)
        {
            // diffCents = Betrag, der im Automaten/Cashbox verbleiben soll (positiv)
            var plan = new DenomPlan
            {
                ReturnCents = 0,
                NewBankCents = 0,
                RemainingCounts = new int[7],
                ReturnCounts = new int[7]
            };

            int[] values = { 5, 10, 20, 50, 100, 200, 500 };
            int[] avail =
            {
                Math.Max(0, _ssp.Cashbox_5_euro),
                Math.Max(0, _ssp.Cashbox_10_euro),
                Math.Max(0, _ssp.Cashbox_20_euro),
                Math.Max(0, _ssp.Cashbox_50_euro),
                Math.Max(0, _ssp.Cashbox_100_euro),
                Math.Max(0, _ssp.Cashbox_200_euro),
                Math.Max(0, _ssp.Cashbox_500_euro)
            };

            int cashboxCents = (int)Math.Round(_cashboxEuro * 100m);
            int bankCents = (int)Math.Round((_abzuege != null ? _abzuege.Sum() : 0) * 1m);

            int bestRemainder = int.MaxValue;
            int[] bestRemain = null;
            int[] bestReturn = null;

            // Wir suchen die nächsthöhere darstellbare Verbleib-Stückelung >= diffCents
            // Ziel: minimaler Überschuss (Remainder), muss in 5€-Schritten sein (wie Cashbox-Scheine)
            for (int target = diffCents; target <= diffCents + 10000; target += 500) // +100€ Puffer
            {
                int[] remain = new int[7];
                int[] ret = new int[7];
                Array.Copy(avail, remain, 7);
                int remaining = target;

                // greedy groß->klein
                for (int i = 6; i >= 0; i--)
                {
                    int valC = values[i] * 100;
                    int need = remaining / valC;
                    if (need <= 0) continue;
                    int take = Math.Min(need, remain[i]);
                    if (take <= 0) continue;
                    ret[i] = take;
                    remain[i] -= take;
                    remaining -= take * valC;
                }

                if (remaining == 0)
                {
                    int remainder = target - diffCents;
                    if (remainder < bestRemainder)
                    {
                        bestRemainder = remainder;
                        bestRemain = remain;
                        bestReturn = ret;
                        if (bestRemainder == 0) break;
                    }
                }
            }

            if (bestRemain == null || bestReturn == null)
            {
                // Fallback: nichts sinnvoll planbar
                Array.Copy(avail, plan.RemainingCounts, 7);
                plan.ReturnCents = 0;
                plan.NewBankCents = bankCents;
                return plan;
            }

            int returnCents = 0;
            for (int i = 0; i < 7; i++) returnCents += bestReturn[i] * values[i] * 100;

            // ReturnCounts/ReturnCents = Stückelung/Summe die in der Cashbox verbleiben soll
            // RemainingCounts = verbleibende Scheine nach Entnahme zur Bank
            plan.ReturnCents = returnCents;
            plan.NewBankCents = Math.Max(0, cashboxCents - returnCents);
            plan.ReturnCounts = bestReturn;
            plan.RemainingCounts = bestRemain;
            return plan;
        }

        private string FormatPlanCounts(int[] counts)
        {
            if (counts == null || counts.Length < 7) return "";
            int[] values = { 5, 10, 20, 50, 100, 200, 500 };
            var parts = new List<string>();
            for (int i = 6; i >= 0; i--)
            {
                if (counts[i] > 0) parts.Add(counts[i] + "x" + values[i] + "€");
            }
            return parts.Count == 0 ? "(keine)" : string.Join(" ", parts.ToArray());
        }

        // NEU: Hilfsmethode f�r Header-Labels
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
            if (_panel == null) return;

            // Optionaler „Loading“-Hinweis, damit keine „leeren Rahmen“ sichtbar bleiben
            Label loading = null;
            try
            {
                if (_panel.Controls.Count == 0)
                {
                    loading = new Label
                    {
                        Text = "Lade Kassen...",
                        AutoSize = false,
                        Width = _panel.Width - 25,
                        Height = 60,
                        Font = new Font("Segoe UI Variable", 18F, FontStyle.Bold),
                        ForeColor = Color.DimGray,
                        TextAlign = ContentAlignment.MiddleCenter
                    };
                    _panel.Controls.Add(loading);
                }
            }
            catch { }

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

            // Maximaler Abzug je Kasse (Bestand, abgerundet auf aktuelle Schrittweite)
            for (int i = 0; i < n; i++)
            {
                int maxCents = (int)Math.Floor(Math.Max(0m, bastaende[i]) * 100 / _schritt) * _schritt;
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
                    anteil = (int)Math.Floor((bastaende[i] / sumBestandPos) * cashboxCents / _schritt) * _schritt;
                }
                anteil = Math.Min(anteil, _maxAbzug[i]);
                _abzuege[i] = anteil;
                rest -= anteil;
            }
            // 2. Rest in Schrittweite an Kassen mit Luft verteilen
            bool changed = true;
            while (rest >= _schritt && changed)
            {
                changed = false;
                for (int i = 0; i < n && rest >= _schritt; i++)
                {
                    if (_abzuege[i] + _schritt <= _maxAbzug[i])
                    {
                        _abzuege[i] += _schritt;
                        rest -= _schritt;
                        changed = true;
                    }
                }
            }

            try
            {
                _panel.SuspendLayout();
                _panel.Controls.Clear();
            }
            catch { }

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
                    Width = 560,
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
                    Width = 120,
                    Height = ControlHeight,
                    Location = new Point(580, y + 3),
                    Font = new Font("Segoe UI", 15F, FontStyle.Regular),
                    TextAlign = ContentAlignment.MiddleRight,
                    ForeColor = Color.DimGray
                };
                panelRow.Controls.Add(lblBestandAlt);
                _lblBestandAlt[i] = lblBestandAlt;

                // Minus-Button
                var btnMinus = new Button
                {
                    Text = "-",
                    Font = new Font("Segoe UI Variable", 22F, FontStyle.Bold),
                    Size = new Size(48, ControlHeight),
                    Location = new Point(710, y),
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
                    Location = new Point(760, y),
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
                    Location = new Point(890, y),
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

                // Zero-Button (setzt direkt auf 0)
                var btnZero = new Button
                {
                    Text = "0",
                    Font = new Font("Segoe UI Variable", 22F, FontStyle.Bold),
                    Size = new Size(48, ControlHeight),
                    Location = new Point(940, y),
                    BackColor = Color.FromArgb(120, 120, 120),
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Tag = i
                };
                btnZero.FlatAppearance.BorderSize = 0;
                btnZero.FlatAppearance.MouseOverBackColor = Color.FromArgb(100, 100, 100);
                btnZero.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, btnZero.Width, btnZero.Height, 12, 12));
                btnZero.Click += BtnZero_Click;
                panelRow.Controls.Add(btnZero);

                // nach Abzug
                var lblBestandNeu = new Label
                {
                    AutoSize = false,
                    Text = (bestand - _abzuege[i] / 100m).ToString("C2"),
                    Width = 120,
                    Height = ControlHeight,
                    Location = new Point(970, y + 3),
                    Font = new Font("Segoe UI", 15F, FontStyle.Regular),
                    TextAlign = ContentAlignment.MiddleRight,
                    ForeColor = Color.FromArgb(46, 125, 50)
                };
                panelRow.Controls.Add(lblBestandNeu);
                _lblBestandNeu[i] = lblBestandNeu;

                _panel.Controls.Add(panelRow);
            }

            try { _panel.ResumeLayout(performLayout: true); } catch { }

            ValidateSum();
        }

        private void BtnMinus_Click(object sender, EventArgs e)
        {
            int idx = (int)((Button)sender).Tag;
            if (_abzuege[idx] > 0)
            {
                _abzuege[idx] -= _schritt;
                if (_abzuege[idx] < 0) _abzuege[idx] = 0;
                _lblBetrag[idx].Text = (_abzuege[idx] / 100m).ToString("0.00") + " €";
                ValidateSum();
            }
        }

        private void BtnPlus_Click(object sender, EventArgs e)
        {
            int idx = (int)((Button)sender).Tag;
            if (_abzuege[idx] + _schritt <= _maxAbzug[idx])
            {
                _abzuege[idx] += _schritt;
                _lblBetrag[idx].Text = (_abzuege[idx] / 100m).ToString("0.00") + " €";
                ValidateSum();
            }
        }

        private void BtnZero_Click(object sender, EventArgs e)
        {
            int idx = (int)((Button)sender).Tag;
            _abzuege[idx] = 0;
            _lblBetrag[idx].Text = "0.00 €";
            ValidateSum();
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
            _lblDiff.Text = (diff > 0 ? "+ " : diff < 0 ? "- " : "€ ") + Math.Abs(diff).ToString("C2");
            _lblDiff.ForeColor = Math.Abs(diff) < 0.01m ? Color.FromArgb(46, 125, 50) : Color.OrangeRed;

            bool reachedMax = false;
            try
            {
                if (_abzuege != null && _maxAbzug != null)
                {
                    reachedMax = true;
                    for (int i = 0; i < _abzuege.Length; i++)
                    {
                        if (_abzuege[i] + _schritt <= _maxAbzug[i]) { reachedMax = false; break; }
                    }
                }
            }
            catch { reachedMax = false; }

            // Button aktiv wenn:
            // 1) diff==0 (perfekt), oder
            // 2) diff>0 und Rücklage bestätigt, oder
            // 3) diff>0 und wir sind bereits am Maximum (Rest ist nicht ausbuchbar)
            _btnAnBank.Enabled = (sum > 0m) &&
                                ((Math.Abs(diff) < 0.01m) ||
                                 (diff > 0.01m && (_returnToCashboxConfirmed || reachedMax)));
        }

        private async void BtnAnBank_Click(object sender, EventArgs e)
        {
            // Sonderfall: Cashbox > Summe an Bank (z.B. Personalguthaben bleibt im Automat)
            decimal sumBankUi = _abzuege.Sum() / 100m;
            decimal diffEuro = _cashboxEuro - sumBankUi;
            if (diffEuro > 0.01m && !_returnToCashboxConfirmed)
            {
                int diffCents = (int)Math.Round(diffEuro * 100m);
                var plan = BuildReturnPlan(diffCents);
                if (plan.ReturnCents <= 0)
                {
                    MessageBox.Show(this,
                        "Hinweis: In der Cashbox ist mehr Geld als an Bank gebucht werden kann.\r\n" +
                        "Es konnte keine sinnvolle Stückelung zur Rücklage vorgeschlagen werden.",
                        "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                decimal retEuro = plan.ReturnCents / 100m;
                decimal newBankEuro = plan.NewBankCents / 100m;

                string msg =
                    "Hinweis: Die Cashbox-Summe ist größer als die Summe 'an Bank'.\r\n\r\n" +
                    "Das deutet darauf hin, dass ein Teil (z.B. Personalguthaben) im Automaten verbleiben soll.\r\n\r\n" +
                    "In der Cashbox muss folgende Stückelung verbleiben:\r\n" +
                    FormatPlanCounts(plan.ReturnCounts) + "\r\n\r\n" +
                    "Diese Scheine werden an die Bank gebracht (Rest-Cashbox nach Entnahme):\r\n" +
                    FormatPlanCounts(plan.RemainingCounts) + "\r\n\r\n" +
                    $"Verbleibend in Cashbox: {retEuro:0.00} €\r\n" +
                    $"Summe an Bank: {newBankEuro:0.00} €\r\n\r\n" +
                    "Haben Sie das Geld zurückgelegt?";

                if (MessageBox.Show(this, msg, "Bestätigung erforderlich", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;

                // Bank-Buchungen reduzieren: nur um den Überschuss, falls wegen fehlender Stückelung mehr als diffCents im Automaten bleiben muss
                int reduce = Math.Max(0, plan.ReturnCents - diffCents);
                if (reduce > 0)
                {
                    int bestIdx = -1;
                    int bestVal = 0;
                    for (int i = 0; i < _abzuege.Length; i++)
                    {
                        if (_abzuege[i] > bestVal)
                        {
                            bestVal = _abzuege[i];
                            bestIdx = i;
                        }
                    }
                    if (bestIdx >= 0)
                    {
                        _abzuege[bestIdx] = Math.Max(0, _abzuege[bestIdx] - reduce);
                        _lblBetrag[bestIdx].Text = (_abzuege[bestIdx] / 100m).ToString("0.00") + " €";
                    }
                }

                // Cashbox verbleibende Stückelung merken (wird beim Abschluss gesetzt)
                _returnToCashboxConfirmed = true;
                _confirmedPlan = plan;
                ValidateSum();
            }

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

            // Cashbox-Bestand setzen: Standard leer, bei bestätigter Rücklage auf verbleibende Stückelung setzen
            try
            {
                if (_returnToCashboxConfirmed)
                {
                    // Verwende exakt den vom Benutzer bestätigten Plan, damit keine andere Stückelung/ Summe entsteht
                    var plan2 = _confirmedPlan ?? BuildReturnPlan(0);
                    _ssp.SetCashboxCounts(
                        plan2.ReturnCounts[0], plan2.ReturnCounts[1], plan2.ReturnCounts[2], plan2.ReturnCounts[3], plan2.ReturnCounts[4], plan2.ReturnCounts[5], plan2.ReturnCounts[6]);
                }
                else
                {
                    _ssp.SetCashboxCounts(0, 0, 0, 0, 0, 0, 0);
                }
            }
            catch
            {
                try { _ssp.SetCashboxCounts(0, 0, 0, 0, 0, 0, 0); } catch { }
            }

            MessageBox.Show("Buchungen erfolgreich erstellt und Cashbox aktualisiert.", "Fertig",
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

            // Titel block   optisch gr  r (Leerzeilen, Rahmen, zentriert)
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

            var logLines = new List<string>(lines); // F r AppLogger identisch

            for (int i = 0; i < _abzuege.Length; i++)
            {
                if (_abzuege[i] <= 0) continue;

                int mandantId = _firmenIds[i];
                string name = (_firmenNamen != null && _firmenNamen.ContainsKey(mandantId)) ? _firmenNamen[mandantId] : ("ID " + mandantId);
                decimal alt = (_bestandAltMap != null && _bestandAltMap.ContainsKey(mandantId)) ? _bestandAltMap[mandantId] : 0m;
                decimal abz = _abzuege[i] / 100m;
                decimal neu = Math.Max(0m, alt - abz);
                string iban = (_ibanMap != null && _ibanMap.ContainsKey(mandantId) && !string.IsNullOrWhiteSpace(_ibanMap[mandantId])) ? _ibanMap[mandantId] : "(keine IBAN)";

                // Namezeile etwas gr  r wirken lassen (Leerzeile davor und danach)
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
            return name.Length <= max ? name : (name.Substring(0, max - 1) + "€");
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

        // Header wird zentral über `ModernHeaderPanel` gezeichnet.
    }
}