using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using TaMi_Einzahlautomat.UI.Layout;

namespace TaMi_Einzahlautomat
{
    public sealed class NotizenConfirmForm : Form
    {
        public sealed class NoteItem
        {
            public int AutoId;
            public string Text;
            public DateTime? GueltigBis;
            public int RelId;
            public int Flags;
        }

        private readonly List<NoteItem> _items;
        private readonly Func<int, int, System.Threading.Tasks.Task> _setFlagAsync;

        private ModernHeaderPanel _header;
        private Label _lblCounter;
        private TextBox _txt;
        private ModernGradientButton _btnOk;
        private int _idx;

        public NotizenConfirmForm(IEnumerable<NoteItem> items, Func<int, int, System.Threading.Tasks.Task> setFlagAsync)
        {
            _items = (items ?? Enumerable.Empty<NoteItem>()).ToList();
            _setFlagAsync = setFlagAsync;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(900, 520);
            BackColor = Color.White;
            DoubleBuffered = true;

            BuildUi();
            ShowCurrent();
        }

        private void BuildUi()
        {
            _header = new ModernHeaderPanel
            {
                Title = "Notizen bestätigen",
                ShowMinimize = false
            };
            _header.CloseClicked += () => { try { Close(); } catch { } };
            Controls.Add(_header);

            try
            {
                _lblCounter = new Label
                {
                    AutoSize = false,
                    Size = new Size(140, _header.Height),
                    TextAlign = ContentAlignment.MiddleRight,
                    Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold),
                    ForeColor = Color.White,
                    BackColor = Color.Transparent
                };
                _header.Controls.Add(_lblCounter);
                _header.Controls.SetChildIndex(_lblCounter, 0);
                _header.Resize += (s, e) =>
                {
                    try
                    {
                        int rightPad = 74;
                        _lblCounter.Location = new Point(Math.Max(0, _header.Width - rightPad - _lblCounter.Width - 10), 0);
                        _lblCounter.Height = _header.Height;
                    }
                    catch { }
                };
                try
                {
                    int rightPad = 74;
                    _lblCounter.Location = new Point(Math.Max(0, _header.Width - rightPad - _lblCounter.Width - 10), 0);
                    _lblCounter.Height = _header.Height;
                }
                catch { }
            }
            catch { }

            try
            {
                _header.ApplyRoundedRegionToForm(this);
                SizeChanged += (s, e) => { try { _header.ApplyRoundedRegionToForm(this); } catch { } };
            }
            catch { }

            var panelBody = new Panel
            {
                Location = new Point(24, _header.Bottom + 20),
                Size = new Size(ClientSize.Width - 48, ClientSize.Height - _header.Bottom - 40),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.Transparent
            };
            Controls.Add(panelBody);

            panelBody.Paint += (s, e) =>
            {
                try
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    var r = panelBody.ClientRectangle;
                    r.Width -= 1;
                    r.Height -= 1;
                    using (var br = new LinearGradientBrush(r, Color.FromArgb(210, 245, 250, 255), Color.FromArgb(185, 230, 240, 255), 90f))
                        e.Graphics.FillRectangle(br, r);
                    using (var pen = new Pen(Color.FromArgb(90, 180, 200, 230), 1f))
                        e.Graphics.DrawRectangle(pen, r);
                }
                catch { }
            };

            _txt = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                Location = new Point(22, 20),
                Size = new Size(panelBody.Width - 44, panelBody.Height - 120),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Font = new Font("Segoe UI Variable", 16F, FontStyle.Regular),
                // TextBox does not support transparency reliably in WinForms.
                BackColor = Color.White,
                ScrollBars = ScrollBars.Vertical
            };
            panelBody.Controls.Add(_txt);

            _btnOk = new ModernGradientButton
            {
                Text = "Gelesen und verstanden",
                GradientStart = UiTheme.SuccessStart,
                GradientEnd = UiTheme.SuccessEnd,
                Font = new Font("Segoe UI Variable", 16F, FontStyle.Bold),
                Size = new Size(320, 60),
                Anchor = AnchorStyles.Bottom,
                TabStop = false
            };
            panelBody.Controls.Add(_btnOk);
            _btnOk.Click += async (s, e) => await OnOkAsync();

            panelBody.SizeChanged += (s, e) =>
            {
                try { _btnOk.Location = new Point((panelBody.Width - _btnOk.Width) / 2, panelBody.Height - _btnOk.Height - 20); } catch { }
            };
            try { _btnOk.Location = new Point((panelBody.Width - _btnOk.Width) / 2, panelBody.Height - _btnOk.Height - 20); } catch { }
        }

        private void ShowCurrent()
        {
            if (_items == null || _items.Count == 0 || _idx < 0 || _idx >= _items.Count)
            {
                try { Close(); } catch { }
                return;
            }

            var it = _items[_idx];
            try { _txt.Text = (it.Text ?? string.Empty).Trim(); } catch { }
            try { _lblCounter.Text = string.Format("{0}/{1}", _idx + 1, _items.Count); } catch { }
        }

        private async System.Threading.Tasks.Task OnOkAsync()
        {
            try
            {
                if (_items == null || _items.Count == 0) { try { Close(); } catch { } return; }
                if (_idx < 0 || _idx >= _items.Count) { try { Close(); } catch { } return; }

                var it = _items[_idx];

                try
                {
                    if (_setFlagAsync != null)
                    {
                        int newFlags = 0;
                        try { newFlags = (it.Flags | 4); } catch { newFlags = 4; }
                        await _setFlagAsync(it.AutoId, newFlags);
                        it.Flags = newFlags;
                    }
                }
                catch { }

                _idx++;
                if (_idx >= _items.Count)
                {
                    try { Close(); } catch { }
                    return;
                }

                ShowCurrent();
            }
            catch
            {
                try { Close(); } catch { }
            }
        }
    }
}
