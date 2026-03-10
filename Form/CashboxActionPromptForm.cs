using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Drawing.Drawing2D;
using TaMi_Einzahlautomat.UI.Layout;

namespace TaMi_Einzahlautomat
{
    public class CashboxActionPromptForm : Form
    {
        private readonly NV200_SSP _ssp;
        private readonly string _cashboxName;
        private ModernHeaderPanel _header;
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

            _header = new ModernHeaderPanel { Title = "Cashbox-Aktion" };
            _header.CloseClicked += () => { try { Close(); } catch { } };
            Controls.Add(_header);
            _header.BringToFront();
            try { _header.ApplyRoundedRegionToForm(this); } catch { }

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
                Location = new Point(140, 160),
                Size = new Size(170, 52),
                Font = new Font("Segoe UI Variable", 14F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat
            };
            btnZaehlen.FlatAppearance.BorderSize = 0;
            try
            {
                var themed = new ModernGradientButton
                {
                    Text = btnZaehlen.Text,
                    Location = btnZaehlen.Location,
                    Size = btnZaehlen.Size,
                    Font = btnZaehlen.Font,
                    GradientStart = UiTheme.PrimaryStart,
                    GradientEnd = UiTheme.PrimaryEnd
                };
                btnZaehlen = themed;
            }
            catch
            {
                btnZaehlen.BackColor = Color.FromArgb(33, 150, 243);
                btnZaehlen.ForeColor = Color.White;
            }
            btnZaehlen.Click += (s, e) =>
            {
                // CashboxCountForm ist ein UserControl -> in eigenem modernen Form hosten
                try
                {
                    var host = new Form
                    {
                        FormBorderStyle = FormBorderStyle.None,
                        StartPosition = FormStartPosition.CenterScreen,
                        ClientSize = new Size(1280, 1024),
                        BackColor = Color.White,
                        TopMost = true,
                        Text = "Cashbox zählen"
                    };
                    var header = new ModernHeaderPanel { Title = "Cashbox zählen" };
                    header.CloseClicked += () => { try { host.Close(); } catch { } };
                    host.Controls.Add(header);
                    header.BringToFront();
                    try { header.ApplyRoundedRegionToForm(host); } catch { }

                    var uc = new CashboxCountForm(_ssp) { Dock = DockStyle.Fill };
                    uc.Location = new Point(0, header.Height);
                    uc.Padding = new Padding(0, header.Height, 0, 0);
                    host.Controls.Add(uc);
                    uc.BringToFront();

                    host.Show();
                }
                catch
                {
                    // Fallback: falls Host fehlschlägt, nichts tun
                }
                Close();
            };
            Controls.Add(btnZaehlen);

            btnAnBank = new Button
            {
                Text = "An Bank buchen",
                Location = new Point(340, 160),
                Size = new Size(250, 52),
                Font = new Font("Segoe UI Variable", 14F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat
            };
            btnAnBank.FlatAppearance.BorderSize = 0;
            try
            {
                var themed = new ModernGradientButton
                {
                    Text = btnAnBank.Text,
                    Location = btnAnBank.Location,
                    Size = btnAnBank.Size,
                    Font = btnAnBank.Font,
                    GradientStart = UiTheme.SuccessStart,
                    GradientEnd = UiTheme.SuccessEnd
                };
                btnAnBank = themed;
            }
            catch
            {
                btnAnBank.BackColor = Color.FromArgb(46, 125, 50);
                btnAnBank.ForeColor = Color.White;
            }
            btnAnBank.Click += (s, e) =>
            {
                var f = new CashboxRemoveForm(_ssp);
                f.Show();
                Close();
            };
            Controls.Add(btnAnBank);

            Resize += (s, e) =>
            {
                try
                {
                    if (btnZaehlen != null) btnZaehlen.Location = new Point((ClientSize.Width / 2) - btnZaehlen.Width - 12, 160);
                    if (btnAnBank != null) btnAnBank.Location = new Point((ClientSize.Width / 2) + 12, 160);
                    if (lblText != null) lblText.Size = new Size(ClientSize.Width - 48, lblText.Height);
                }
                catch { }
            };
            try
            {
                if (btnZaehlen != null) btnZaehlen.Location = new Point((ClientSize.Width / 2) - btnZaehlen.Width - 12, 160);
                if (btnAnBank != null) btnAnBank.Location = new Point((ClientSize.Width / 2) + 12, 160);
            }
            catch { }
        }
    }
}
