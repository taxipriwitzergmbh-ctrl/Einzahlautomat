using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TaMi_Einzahlautomat
{
    // Simple data carrier for big button selection
    public class BigItem
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public object Tag { get; set; }
        public override string ToString() => Title;
    }

    // Touch-friendly selection dialog with large buttons
    public class BigButtonSelectionForm : Form
    {
        private readonly List<BigItem> _items;
        private readonly string _title;
        private FlowLayoutPanel _panel;
        private TextBox _txtSearch;
        private Button _btnCancel;
        public BigItem SelectedItem { get; private set; }

        public BigButtonSelectionForm(string title, IEnumerable<BigItem> items)
        {
            _title = string.IsNullOrWhiteSpace(title) ? "Auswahl" : title.Trim();
            _items = items?.ToList() ?? new List<BigItem>();
            BuildUi();
        }

        private void BuildUi()
        {
            Text = _title;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;
            ClientSize = new Size(720, 600);

            var header = new Panel { Dock = DockStyle.Top, Height = 64 };
            header.Paint += (s, e) =>
            {
                using (var b = new System.Drawing.Drawing2D.LinearGradientBrush(header.ClientRectangle, Color.FromArgb(33,150,243), Color.FromArgb(33,203,243), 0f))
                    e.Graphics.FillRectangle(b, header.ClientRectangle);
            };
            Controls.Add(header);

            var lbl = new Label { Text = _title, Dock = DockStyle.Left, Width = 480, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.White, Font = new Font("Segoe UI Variable", 18F, FontStyle.Bold), Padding = new Padding(16, 0, 0, 0) };
            header.Controls.Add(lbl);
            var btnClose = new Button { Text = "\u2715", Dock = DockStyle.Right, Width = 56, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.Transparent };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s, e) => DialogResult = DialogResult.Cancel;
            header.Controls.Add(btnClose);

            var searchPanel = new Panel { Dock = DockStyle.Top, Height = 52, Padding = new Padding(12) };
            _txtSearch = new TextBox { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 12F) };
            _txtSearch.TextChanged += (s, e) => RefreshButtons();
            searchPanel.Controls.Add(_txtSearch);
            Controls.Add(searchPanel);

            _panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12), WrapContents = true };
            Controls.Add(_panel);

            _btnCancel = new Button { Text = "Abbrechen", Height = 48, Dock = DockStyle.Bottom, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(229,57,53), ForeColor = Color.White, Font = new Font("Segoe UI Variable", 14F, FontStyle.Bold) };
            _btnCancel.FlatAppearance.BorderSize = 0;
            _btnCancel.Click += (s, e) => DialogResult = DialogResult.Cancel;
            Controls.Add(_btnCancel);

            RefreshButtons();
        }

        private void RefreshButtons()
        {
            try
            {
                _panel.SuspendLayout();
                _panel.Controls.Clear();
                var q = (_txtSearch?.Text ?? string.Empty).Trim().ToLowerInvariant();
                IEnumerable<BigItem> list = _items;
                if (!string.IsNullOrEmpty(q))
                {
                    list = list.Where(i => (i.Title ?? string.Empty).ToLowerInvariant().Contains(q) || (i.Subtitle ?? string.Empty).ToLowerInvariant().Contains(q));
                }
                foreach (var it in list)
                {
                    var btn = new Button
                    {
                        Text = string.IsNullOrEmpty(it.Subtitle) ? it.Title : it.Title + "\n" + it.Subtitle,
                        Width = 320,
                        Height = 100,
                        Margin = new Padding(8),
                        TextAlign = ContentAlignment.MiddleCenter,
                        FlatStyle = FlatStyle.Flat,
                        BackColor = Color.FromArgb(33,150,243),
                        ForeColor = Color.White,
                        Font = new Font("Segoe UI Variable", 16F, FontStyle.Bold),
                        Tag = it
                    };
                    btn.FlatAppearance.BorderSize = 0;
                    btn.Click += (s, e) => { SelectedItem = (BigItem)((Button)s).Tag; DialogResult = DialogResult.OK; };
                    _panel.Controls.Add(btn);
                }
                _panel.ResumeLayout();
            }
            catch { }
        }
    }
}
