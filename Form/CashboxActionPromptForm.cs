using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Drawing.Drawing2D;

namespace TaMi_Einzahlautomat
{
    public class CashboxActionPromptForm : Form
    {
        private readonly NV200_SSP _ssp;
        private readonly string _cashboxName;
        private Panel headerPanel;
        private Label lblTitle;
        private Button btnClose;
        private Label lblText;
        private Button btnZaehlen;
        private Button btnAnBank;

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

        public CashboxActionPromptForm(NV200_SSP ssp, string cashboxName)
        {
            _ssp = ssp ?? throw new ArgumentNullException(nameof(ssp));
            _cashboxName = string.IsNullOrWhiteSpace(cashboxName) ? "Cashbox 1" : cashboxName;
            BuildLayout();
        }

        private void BuildLayout()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(720, 260);
            BackColor = Color.White;
            TopMost = true;
            DoubleBuffered = true;

            headerPanel = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(ClientSize.Width, 56),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            headerPanel.Paint += (s, e) =>
            {
                using (var brush = new LinearGradientBrush(headerPanel.ClientRectangle,
                    Color.FromArgb(33, 150, 243), Color.FromArgb(33, 203, 243), 0f))
                {
                    e.Graphics.FillRectangle(brush, headerPanel.ClientRectangle);
                }
            };
            Controls.Add(headerPanel);

            lblTitle = new Label
            {
                Text = "Cashbox-Aktion",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Variable", 18F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(20, 0),
                Size = new Size(520, 56)
            };
            headerPanel.Controls.Add(lblTitle);

            btnClose = new Button
            {
                Text = "\u2715",
                Font = new Font("Segoe UI Symbol", 14F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(40, 36),
                Location = new Point(ClientSize.Width - 50, 10),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 80, 80);
            btnClose.Click += (s, e) => Close();
            headerPanel.Controls.Add(btnClose);

            try { Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, Width, Height, 18, 18)); } catch { }

            lblText = new Label
            {
                Text = $"Die {_cashboxName} wurde entfernt.\r\nMöchten Sie diese zählen oder entleeren und an Bank buchen?",
                AutoSize = false,
                Location = new Point(24, 76),
                Size = new Size(672, 60),
                Font = new Font("Segoe UI", 14F, FontStyle.Regular)
            };
            Controls.Add(lblText);

            btnZaehlen = new Button
            {
                Text = "Zählen",
                Location = new Point(160, 160),
                Size = new Size(150, 48),
                Font = new Font("Segoe UI Variable", 14F, FontStyle.Bold),
                BackColor = Color.FromArgb(33, 150, 243),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnZaehlen.FlatAppearance.BorderSize = 0;
            btnZaehlen.Click += (s, e) =>
            {
                var f = new CashboxCountForm(_ssp);
                f.Show();
                Close();
            };
            Controls.Add(btnZaehlen);

            btnAnBank = new Button
            {
                Text = "An Bank buchen",
                Location = new Point(360, 160),
                Size = new Size(220, 48),
                Font = new Font("Segoe UI Variable", 14F, FontStyle.Bold),
                BackColor = Color.FromArgb(46, 125, 50),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnAnBank.FlatAppearance.BorderSize = 0;
            btnAnBank.Click += (s, e) =>
            {
                var f = new CashboxRemoveForm(_ssp);
                f.Show();
                Close();
            };
            Controls.Add(btnAnBank);
        }
    }
}
