using System;
using System.Drawing;
using System.Windows.Forms;

namespace Geldautomat
{
    public class CoinLogForm : Form
    {
        private TextBox txtLogView;
        public CoinLogForm(string logText)
        {
            Text = "Münzprotokoll";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(700, 500);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            txtLogView = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 11F),
                Text = logText
            };
            Controls.Add(txtLogView);
        }
    }
}
