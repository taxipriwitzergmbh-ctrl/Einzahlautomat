using System;
using System.Data;
using System.Drawing;
using System.Windows.Forms;
using System.Drawing.Drawing2D;

namespace Geldautomat
{
    public partial class ShiftSelectionForm : Form
    {
        public int? SelectedSchichtId { get; private set; }

        private DataGridView dgvShifts;
        private Button btnOk;
        private Button btnCancel;
        private Panel headerPanel;
        private Label lblTitle;
        private Button btnClose;
        private Point _mouseDownLocation;

        public ShiftSelectionForm(DataTable shifts)
        {
            // Modernes Fenster-Setup
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(800, 500);
            BackColor = Color.White;
            DoubleBuffered = true;

            // Farbverlauf-Header
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

            // Titel
            lblTitle = new Label
            {
                Text = "Schicht auswählen",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Variable", 20F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(24, 0),
                Size = new Size(400, 60),
                BackColor = Color.Transparent
            };
            headerPanel.Controls.Add(lblTitle);

            // Schließen-Button
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

            // DataGridView
            dgvShifts = new DataGridView
            {
                DataSource = shifts,
                Location = new Point(30, 80),
                Size = new Size(720, 320),
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI Variable", 14F),
                RowHeadersVisible = false,
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Color.FromArgb(33, 150, 243),
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI Variable", 14F, FontStyle.Bold),
                    Alignment = DataGridViewContentAlignment.MiddleLeft
                },
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Font = new Font("Segoe UI Variable", 14F),
                    SelectionBackColor = Color.FromArgb(232, 240, 254),
                    SelectionForeColor = Color.Black
                }
            };
            // Spaltenüberschriften anpassen
            if (dgvShifts.Columns.Contains("SchichtId"))
                dgvShifts.Columns["SchichtId"].HeaderText = "Schicht-ID";
            if (dgvShifts.Columns.Contains("StartZeit"))
                dgvShifts.Columns["StartZeit"].HeaderText = "Startdatum";
            if (dgvShifts.Columns.Contains("Kennzeichen"))
                dgvShifts.Columns["Kennzeichen"].HeaderText = "Kennzeichen";
            if (dgvShifts.Columns.Contains("EinnahmenBar"))
                dgvShifts.Columns["EinnahmenBar"].HeaderText = "Einnahmen bar (€)";

            dgvShifts.DoubleClick += dgvShifts_DoubleClick;
            Controls.Add(dgvShifts);

            // OK-Button
            btnOk = new Button
            {
                Text = "OK",
                Font = new Font("Segoe UI Variable", 16F, FontStyle.Bold),
                BackColor = Color.FromArgb(33, 150, 243),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(160, 48),
                Location = new Point(400, 420)
            };
            btnOk.FlatAppearance.BorderSize = 0;
            btnOk.Click += btnOk_Click;
            Controls.Add(btnOk);

            // Cancel-Button
            btnCancel = new Button
            {
                Text = "Abbrechen",
                Font = new Font("Segoe UI Variable", 16F, FontStyle.Bold),
                BackColor = Color.FromArgb(229, 57, 53),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(160, 48),
                Location = new Point(580, 420)
            };
            btnCancel.FlatAppearance.BorderSize = 0;
            btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(btnCancel);

            // Abgerundete Ecken
            try { Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, Width, Height, 24, 24)); } catch { }
        }

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

        [System.Runtime.InteropServices.DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

        private void btnOk_Click(object sender, EventArgs e)
        {
            SelectCurrent();
        }

        private void dgvShifts_DoubleClick(object sender, EventArgs e)
        {
            SelectCurrent();
        }

        private void SelectCurrent()
        {
            if (dgvShifts.CurrentRow != null)
            {
                var row = ((DataRowView)dgvShifts.CurrentRow.DataBoundItem).Row;
                SelectedSchichtId = row.Field<int>("SchichtId");
                DialogResult = DialogResult.OK;
                Close();
            }
        }
    }
}