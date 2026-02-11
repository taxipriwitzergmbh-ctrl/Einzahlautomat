using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace TaMi_Einzahlautomat
{
    public class PaymentSettingsForm : Form
    {
        // Modern header
        private Panel headerPanel;
        private Button btnHeaderClose;
        // Minimieren-Button entfernt
        private Label lblTitle;
        private Point _mouseDownLocation;

        private CheckBox chkEnable;
        private DataGridView grid;
        private BindingList<PaymentFieldSetting> _binding;
        private Button btnClose;

        private void ApplyModernButtonStyle(Button b, Color c1, Color c2)
        {
            if (b == null) return;
            try
            {
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 0;
                b.BackColor = Color.Transparent;
                b.UseVisualStyleBackColor = false;
                b.ForeColor = Color.White;
                b.Paint -= ModernButton_Paint;
                b.Paint += ModernButton_Paint;
                b.Tag = new Tuple<Color, Color>(c1, c2);
                try { b.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, b.Width, b.Height, 14, 14)); } catch { }
                b.Resize -= ModernButton_Resize;
                b.Resize += ModernButton_Resize;
            }
            catch { }
        }

        private void ModernButton_Resize(object sender, EventArgs e)
        {
            try
            {
                var b = sender as Button;
                if (b == null) return;
                try { b.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, b.Width, b.Height, 14, 14)); } catch { }
                b.Invalidate();
            }
            catch { }
        }

        private void ModernButton_Paint(object sender, PaintEventArgs e)
        {
            var b = sender as Button;
            if (b == null) return;
            try
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var rect = b.ClientRectangle;
                rect.Width -= 1;
                rect.Height -= 1;

                var colors = b.Tag as Tuple<Color, Color>;
                var cc1 = colors != null ? colors.Item1 : Color.FromArgb(33, 150, 243);
                var cc2 = colors != null ? colors.Item2 : Color.FromArgb(13, 71, 161);

                using (var br = new LinearGradientBrush(rect, cc1, cc2, 90f))
                {
                    e.Graphics.FillRectangle(br, rect);
                }
                using (var pen = new Pen(Color.FromArgb(110, 255, 255, 255), 1f))
                {
                    e.Graphics.DrawRectangle(pen, rect);
                }

                TextRenderer.DrawText(e.Graphics, b.Text, b.Font, b.ClientRectangle, b.ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            catch { }
        }

        public PaymentSettingsForm()
        {
            Text = "Zahlungseinstellungen";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(980, 640);
            FormBorderStyle = FormBorderStyle.None;
            DoubleBuffered = true;
            MaximizeBox = false;

            // Header
            headerPanel = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(ClientSize.Width, 60),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            headerPanel.Paint += HeaderPanel_Paint;
            headerPanel.MouseDown += HeaderPanel_MouseDown;
            headerPanel.MouseMove += HeaderPanel_MouseMove;
            Controls.Add(headerPanel);

            lblTitle = new Label
            {
                Text = "Zahlungseinstellungen",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Variable", 18F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(24, 0),
                Size = new Size(600, 60),
                BackColor = Color.Transparent
            };
            headerPanel.Controls.Add(lblTitle);

            btnHeaderClose = new Button
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
            btnHeaderClose.FlatAppearance.BorderSize = 0;
            btnHeaderClose.FlatAppearance.MouseOverBackColor = Color.Transparent;
            btnHeaderClose.Click += (s, e) => Close();
            headerPanel.Controls.Add(btnHeaderClose);
            try { ApplyModernButtonStyle(btnHeaderClose, Color.FromArgb(239, 83, 80), Color.FromArgb(198, 40, 40)); } catch { }

            // Minimieren-Button entfernt

            headerPanel.Resize += (s, e) => UpdateHeaderLayout();
            UpdateHeaderLayout();
            try { Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, Width, Height, 24, 24)); } catch { }

            // Content area
            int top = headerPanel.Bottom + 16;

            chkEnable = new CheckBox { Text = "Zahlungen erstellen aktiv", Location = new Point(20, top), AutoSize = true, Font = new Font("Segoe UI Variable", 12F, FontStyle.Bold) };
            chkEnable.Checked = PaymentSettingsStore.IsEnabled();
            chkEnable.CheckedChanged += (s,e)=> PaymentSettingsStore.SetEnabled(chkEnable.Checked);
            Controls.Add(chkEnable);

            grid = new DataGridView
            {
                Location = new Point(20, top + 36),
                Size = new Size(ClientSize.Width - 40, ClientSize.Height - (top + 36) - 80),
                AutoGenerateColumns = false,
                AllowUserToAddRows = true,
                AllowUserToDeleteRows = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                EnableHeadersVisualStyles = false
            };

            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(33, 150, 243),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold)
            };
            grid.DefaultCellStyle = new DataGridViewCellStyle
            {
                Font = new Font("Segoe UI", 10f),
                SelectionBackColor = Color.FromArgb(232, 240, 254),
                SelectionForeColor = Color.Black
            };

            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(PaymentFieldSetting.Bezeichnung), HeaderText = "Bezeichnung", Width = 180 });

            // MwSt-Dropdown mit Text 19% / 7% / 0%
            var mwstOptions = new[]
            {
                new { Text = "19%", Value = MwStType.Mwst19 },
                new { Text = "7%",  Value = MwStType.Mwst7  },
                new { Text = "0%",  Value = MwStType.Mwst0  }
            };
            var colMwst = new DataGridViewComboBoxColumn
            {
                DataPropertyName = nameof(PaymentFieldSetting.MwSt),
                HeaderText = "MwSt",
                DataSource = mwstOptions,
                DisplayMember = "Text",
                ValueMember = "Value",
                Width = 70,
                FlatStyle = FlatStyle.Flat
            };
            grid.Columns.Add(colMwst);

            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(PaymentFieldSetting.Kost1), HeaderText = "Kost1", Width = 90 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(PaymentFieldSetting.Kost2), HeaderText = "Kost2", Width = 90 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(PaymentFieldSetting.Konto), HeaderText = "Konto", Width = 90 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(PaymentFieldSetting.Buchungstext), HeaderText = "Buchungstext", Width = 200 });

            // Typ-Dropdown mit Einzahlung/Auszahlung -> Werte 2 bzw. 3
            var typOptions = new[]
            {
                new { Text = "Einzahlung", Value = (byte)2 },
                new { Text = "Auszahlung", Value = (byte)3 }
            };
            var colTyp = new DataGridViewComboBoxColumn
            {
                DataPropertyName = nameof(PaymentFieldSetting.Typ),
                HeaderText = "Typ",
                DataSource = typOptions,
                DisplayMember = "Text",
                ValueMember = "Value",
                Width = 110,
                FlatStyle = FlatStyle.Flat
            };
            grid.Columns.Add(colTyp);

            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(PaymentFieldSetting.MaxBetrag), HeaderText = "Max Betrag", Width = 100, DefaultCellStyle = new DataGridViewCellStyle{ Format = "N2" }});

            _binding = PaymentSettingsStore.GetBindingList();
            grid.DataSource = _binding;

            Controls.Add(grid);

            btnClose = new Button
            {
                Text = "Schließen",
                Location = new Point(20, ClientSize.Height - 56),
                Size = new Size(140, 40),
                BackColor = Color.FromArgb(33, 150, 243),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Variable", 12F, FontStyle.Bold),
                Anchor = AnchorStyles.Left | AnchorStyles.Bottom
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s,e)=>
            {
                PaymentSettingsStore.UpdateFromBindingList(_binding);
                DialogResult = DialogResult.OK;
                Close();
            };
            try { ApplyModernButtonStyle(btnClose, Color.FromArgb(33, 150, 243), Color.FromArgb(13, 71, 161)); } catch { }
            Controls.Add(btnClose);
        }

        private void HeaderPanel_Paint(object sender, PaintEventArgs e)
        {
            try
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var r = headerPanel.ClientRectangle;
                using (var b = new LinearGradientBrush(r, Color.FromArgb(13, 71, 161), Color.FromArgb(120, 200, 255), 0f))
                {
                    e.Graphics.FillRectangle(b, r);
                }
            }
            catch
            {
                using (var b = new LinearGradientBrush(headerPanel.ClientRectangle,
                           Color.FromArgb(33, 150, 243), Color.FromArgb(33, 203, 243), 0f))
                {
                    e.Graphics.FillRectangle(b, headerPanel.ClientRectangle);
                }
            }
        }

        private void HeaderPanel_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) _mouseDownLocation = e.Location;
        }

        private void HeaderPanel_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Left += e.X - _mouseDownLocation.X;
                Top += e.Y - _mouseDownLocation.Y;
            }
        }

        private void UpdateHeaderLayout()
        {
            try
            {
                int marginRight = 12;
                int spacing = 8;
                int top = 6;
                if (btnHeaderClose != null)
                    btnHeaderClose.Location = new Point(Math.Max(0, headerPanel.ClientSize.Width - marginRight - btnHeaderClose.Width), top);
                if (lblTitle != null)
                {
                    int left = lblTitle.Left;
                    int rightLimit = btnHeaderClose.Left - spacing;
                    int newWidth = Math.Max(120, rightLimit - left);
                    lblTitle.Size = new Size(newWidth, lblTitle.Height);
                }
            }
            catch { }
        }

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);
    }
}
