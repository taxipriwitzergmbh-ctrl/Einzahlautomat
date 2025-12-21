using System;
using System.Drawing;
using System.Windows.Forms;

namespace Geldautomat
{
    // Platzhalter: Anzeige der Stunden/Zeiterfassung
    public class HoursOverviewForm : Form
    {
        private readonly PersonalInfo _personal;
        private Panel _header;
        private Label _title;
        private Button _btnClose;

        public HoursOverviewForm(PersonalInfo personal)
        {
            _personal = personal;
            BuildUi();
        }

        private void BuildUi()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;
            DoubleBuffered = true;
            ClientSize = new Size(800, 600);

            _header = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Color.FromArgb(33, 150, 243) };
            Controls.Add(_header);

            _title = new Label { Text = "Zeiterfassung", Dock = DockStyle.Left, Width = 400, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Variable", 20F, FontStyle.Bold), ForeColor = Color.White, Padding = new Padding(16, 0, 0, 0) };
            _header.Controls.Add(_title);

            _btnClose = new Button { Text = "Schließen", Dock = DockStyle.Right, Width = 140, BackColor = Color.FromArgb(229, 57, 53), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            _btnClose.FlatAppearance.BorderSize = 0; _btnClose.Click += (s, e) => Close(); _header.Controls.Add(_btnClose);

            var lblInfo = new Label { Text = $"Mitarbeiter: {_personal?.Vorname} {_personal?.Name}", AutoSize = false, Location = new Point(24, _header.Bottom + 16), Size = new Size(ClientSize.Width - 48, 30), Font = new Font("Segoe UI", 12F, FontStyle.Bold) };
            Controls.Add(lblInfo);

            var lblPlaceholder = new Label { Text = "Hier folgt die Anzeige der Stunden.", AutoSize = false, Location = new Point(24, lblInfo.Bottom + 12), Size = new Size(ClientSize.Width - 48, 30), Font = new Font("Segoe UI", 12F) };
            Controls.Add(lblPlaceholder);
        }
    }
}
