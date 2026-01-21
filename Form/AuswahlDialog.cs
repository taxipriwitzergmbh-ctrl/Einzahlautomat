using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Threading.Tasks;

namespace TaMi_Einzahlautomat
{
    public class AuswahlDialog : Form
    {
        public AuswahlItem SelectedItem { get; private set; }
        private ListBox listBox;
        private Button btnOk;
        private Button btnCancel;
        private Label lblTitle;
        private Panel headerPanel;

        public AuswahlDialog(List<AuswahlItem> items)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            Width = 800; // Breiter gemacht
            Height = 420;
            BackColor = Color.White;
            DoubleBuffered = true;

            headerPanel = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(Width, 60),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };
            headerPanel.Paint += (s, e) =>
            {
                using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(headerPanel.ClientRectangle,
                    Color.FromArgb(33, 150, 243), Color.FromArgb(33, 203, 243), 0f))
                {
                    e.Graphics.FillRectangle(brush, headerPanel.ClientRectangle);
                }
            };
            Controls.Add(headerPanel);

            lblTitle = new Label
            {
                Text = "Schicht oder Zahlung auswählen",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Variable", 20F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(24, 0),
                Size = new Size(Width - 48, 60),
                BackColor = Color.Transparent
            };
            headerPanel.Controls.Add(lblTitle);

            listBox = new ListBox
            {
                Location = new Point(40, 80),
                Size = new Size(Width - 80, 220),
                Font = new Font("Segoe UI Variable", 16F),
                BorderStyle = BorderStyle.FixedSingle
            };

            // Neue Logik: AnzeigeText für Schichten (Nachzahlung/Rückzahlung) direkt berechnen
            LoadAnzeigeTextAsync(items);

            Controls.Add(listBox);

            btnOk = new Button
            {
                Text = "OK",
                Font = new Font("Segoe UI Variable", 16F, FontStyle.Bold),
                BackColor = Color.FromArgb(46, 125, 50),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(140, 48),
                Location = new Point(Width - 320, Height - 90)
            };
            btnOk.FlatAppearance.BorderSize = 0;
            btnOk.Click += (s, e) =>
            {
                if (listBox.SelectedIndex >= 0)
                {
                    SelectedItem = items[listBox.SelectedIndex];
                    DialogResult = DialogResult.OK;
                }
            };
            Controls.Add(btnOk);

            btnCancel = new Button
            {
                Text = "Abbrechen",
                Font = new Font("Segoe UI Variable", 16F, FontStyle.Bold),
                BackColor = Color.FromArgb(229, 57, 53),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(140, 48),
                Location = new Point(Width - 160, Height - 90)
            };
            btnCancel.FlatAppearance.BorderSize = 0;
            btnCancel.Click += (s, e) => DialogResult = DialogResult.Cancel;
            Controls.Add(btnCancel);

            try
            {
                Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, Width, Height, 24, 24));
            }
            catch { }

            headerPanel.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    _mouseDownLocation = e.Location;
            };
            headerPanel.MouseMove += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    Left += e.X - _mouseDownLocation.X;
                    Top += e.Y - _mouseDownLocation.Y;
                }
            };
        }

        // AnzeigeText für Schichten (Nachzahlung/Rückzahlung) direkt berechnen und Betrag anhängen
        private async void LoadAnzeigeTextAsync(List<AuswahlItem> items)
        {
            listBox.Items.Clear();
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.Schicht != null)
                {
                    string kennzeichen = "";
                    if (item.Schicht.FhzId > 0)
                    {
                        using (var db = new DatabaseHelper())
                        {
                            try
                            {
                                var fahrzeug = await db.GetFahrzeugInfoAsync(item.Schicht.FhzId);
                                if (fahrzeug != null)
                                    kennzeichen = fahrzeug.Kennzeichen;
                            }
                            catch { }
                        }
                    }
                    string datum = item.Schicht.StartZeit.ToString("dd.MM.yyyy HH:mm");
                    decimal einzahlungFahrer = item.Schicht.EinzahlungBisher;
                    decimal einnahmenBar = item.Schicht.Betrag19 + item.Schicht.Betrag7 + item.Schicht.Betrag0 + einzahlungFahrer;
                    decimal diff = einnahmenBar - einzahlungFahrer;
                    string betragStr = $" ({diff:C2})";
                    string text;
                    if (einzahlungFahrer != 0)
                    {
                        if (diff > 0)
                            text = $"Nachzahlung Schicht \"{kennzeichen}\" vom {datum}{betragStr}";
                        else if (diff < 0)
                            text = $"Rückzahlung Schicht \"{kennzeichen}\" vom {datum}{betragStr}";
                        else
                            text = $"Schicht \"{kennzeichen}\" vom {datum}{betragStr}";
                    }
                    else
                    {
                        text = $"Schicht \"{kennzeichen}\" vom {datum}{betragStr}";
                    }
                    listBox.Items.Add(text);
                }
                else if (item.Auszahlung != null)
                {
                    listBox.Items.Add(item.AnzeigeText);
                }
            }
        }

        private Point _mouseDownLocation;

        [System.Runtime.InteropServices.DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);
    }
}