using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using TaMi_Einzahlautomat.UI.Layout;

namespace TaMi_Einzahlautomat
{
    public class PaymentSettingsForm : Form
    {
        private ModernHeaderPanel _header;

        private CheckBox chkEnable;
        private DataGridView grid;
        private BindingList<PaymentFieldSetting> _binding;
        private Button btnClose;

        // Header & Buttons werden zentral über `ModernHeaderPanel` / `ModernGradientButton` gestaltet.

        public PaymentSettingsForm()
        {
            Text = "Zahlungseinstellungen";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(980, 640);
            FormBorderStyle = FormBorderStyle.None;
            DoubleBuffered = true;
            MaximizeBox = false;

            _header = new ModernHeaderPanel { Title = "Zahlungseinstellungen" };
            _header.CloseClicked += () => { try { Close(); } catch { } };
            Controls.Add(_header);
            _header.BringToFront();
            try { _header.ApplyRoundedRegionToForm(this); } catch { }

            // Content area
            int top = _header.Bottom + 16;

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

            btnClose = new ModernGradientButton
            {
                Text = "Schließen",
                Location = new Point(20, ClientSize.Height - 56),
                Size = new Size(140, 40),
                Font = new Font("Segoe UI Variable", 12F, FontStyle.Bold),
                Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
                GradientStart = UiTheme.PrimaryStart,
                GradientEnd = UiTheme.PrimaryEnd
            };
            btnClose.Click += (s, e) =>
            {
                grid.EndEdit();

                foreach (var item in _binding)
                {
                    if (item == null)
                        continue;

                    if (string.IsNullOrWhiteSpace(item.Kost1))
                        item.Kost1 = "0";

                    if (string.IsNullOrWhiteSpace(item.Kost2))
                        item.Kost2 = "0";

                    if (string.IsNullOrWhiteSpace(item.Konto))
                        item.Konto = "0";
                }

                PaymentSettingsStore.UpdateFromBindingList(_binding);
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(btnClose);
        }
    }
}
