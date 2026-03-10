using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Threading.Tasks;
using TaMi_Einzahlautomat.UI.Layout;

namespace TaMi_Einzahlautomat
{
    public class AuswahlDialog : Form
    {
        public AuswahlItem SelectedItem { get; private set; }
        private ListBox listBox;
        private Button btnOk;
        private Button btnCancel;
        private ModernHeaderPanel _header;

        public AuswahlDialog(List<AuswahlItem> items)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(800, 420);
            BackColor = Color.White;
            DoubleBuffered = true;

            _header = new ModernHeaderPanel { Title = "Auswahl" };
            _header.CloseClicked += () => { try { DialogResult = DialogResult.Cancel; } catch { } };
            Controls.Add(_header);
            _header.BringToFront();
            try { _header.ApplyRoundedRegionToForm(this); } catch { }

            listBox = new ListBox
            {
                Location = new Point(40, 80),
                Size = new Size(ClientSize.Width - 80, 220),
                Font = new Font("Segoe UI Variable", 16F),
                BorderStyle = BorderStyle.FixedSingle
            };

            // Neue Logik: AnzeigeText f�r Schichten (Nachzahlung/R�ckzahlung) direkt berechnen
            LoadAnzeigeTextAsync(items);

            Controls.Add(listBox);

            btnOk = new Button
            {
                Text = "OK",
                Font = new Font("Segoe UI Variable", 16F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(140, 48),
                Location = new Point(ClientSize.Width - 320, ClientSize.Height - 90)
            };
            btnOk.FlatAppearance.BorderSize = 0;
            try
            {
                var themed = new ModernGradientButton
                {
                    Text = btnOk.Text,
                    Font = btnOk.Font,
                    Size = btnOk.Size,
                    Location = btnOk.Location,
                    GradientStart = UiTheme.SuccessStart,
                    GradientEnd = UiTheme.SuccessEnd
                };
                btnOk = themed;
            }
            catch
            {
                btnOk.BackColor = Color.FromArgb(46, 125, 50);
                btnOk.ForeColor = Color.White;
            }
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
                FlatStyle = FlatStyle.Flat,
                Size = new Size(140, 48),
                Location = new Point(ClientSize.Width - 160, ClientSize.Height - 90)
            };
            btnCancel.FlatAppearance.BorderSize = 0;
            try
            {
                var themed = new ModernGradientButton
                {
                    Text = btnCancel.Text,
                    Font = btnCancel.Font,
                    Size = btnCancel.Size,
                    Location = btnCancel.Location,
                    GradientStart = UiTheme.DangerStart,
                    GradientEnd = UiTheme.DangerEnd
                };
                btnCancel = themed;
            }
            catch
            {
                btnCancel.BackColor = Color.FromArgb(229, 57, 53);
                btnCancel.ForeColor = Color.White;
            }
            btnCancel.Click += (s, e) => DialogResult = DialogResult.Cancel;
            Controls.Add(btnCancel);

            Resize += (s, e) => ApplyLayout();
            ApplyLayout();
        }

        private void ApplyLayout()
        {
            try
            {
                if (listBox != null)
                {
                    listBox.Location = new Point(40, 80);
                    listBox.Size = new Size(ClientSize.Width - 80, Math.Max(120, ClientSize.Height - 200));
                }
                if (btnOk != null) btnOk.Location = new Point(ClientSize.Width - 320, ClientSize.Height - 90);
                if (btnCancel != null) btnCancel.Location = new Point(ClientSize.Width - 160, ClientSize.Height - 90);
            }
            catch { }
        }

        // AnzeigeText f�r Schichten (Nachzahlung/R�ckzahlung) direkt berechnen und Betrag anh�ngen
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
                            text = $"R�ckzahlung Schicht \"{kennzeichen}\" vom {datum}{betragStr}";
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