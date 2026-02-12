using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TaMi_Einzahlautomat.UI.Layout
{
    internal class ModernHeaderPanel : System.Windows.Forms.Panel
    {
        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

        public string Title
        {
            get => _lblTitle?.Text;
            set { if (_lblTitle != null) _lblTitle.Text = value ?? string.Empty; }
        }

        public bool ShowMinimize { get; set; }

        private System.Windows.Forms.Label _lblTitle;
        private ModernGradientButton _btnClose;
        private ModernGradientButton _btnMin;
        private Point _mouseDown;

        public event Action CloseClicked;
        public event Action MinimizeClicked;

        public ModernHeaderPanel()
        {
            Dock = System.Windows.Forms.DockStyle.Top;
            Height = UiTheme.HeaderHeight;
            BackColor = Color.Transparent;
            DoubleBuffered = true;

            MouseDown += (s, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Left) _mouseDown = e.Location; };
            MouseMove += (s, e) =>
            {
                if (e.Button != System.Windows.Forms.MouseButtons.Left) return;
                var f = FindForm();
                if (f == null) return;
                f.Left += e.X - _mouseDown.X;
                f.Top += e.Y - _mouseDown.Y;
            };

            BuildControls();
            Resize += (s, e) => LayoutControls();
        }

        protected override void OnCreateControl()
        {
            base.OnCreateControl();
            BuildControls();
            LayoutControls();
            try
            {
                if (_btnMin != null) _btnMin.BringToFront();
                if (_btnClose != null) _btnClose.BringToFront();
            }
            catch { }
        }

        private void BuildControls()
        {
            if (_lblTitle == null)
            {
                _lblTitle = new System.Windows.Forms.Label
                {
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Font = UiTheme.HeaderFont,
                    ForeColor = Color.White,
                    Location = new Point(24, 0),
                    Size = new Size(500, Height),
                    BackColor = Color.Transparent
                };
                Controls.Add(_lblTitle);
            }

            if (_btnClose == null)
            {
                _btnClose = new ModernGradientButton
                {
                    Text = "\u2715",
                    Font = new Font("Segoe UI Symbol", 16F, FontStyle.Bold),
                    GradientStart = UiTheme.DangerStart,
                    GradientEnd = UiTheme.DangerEnd,
                    Size = new Size(48, 48),
                    TabStop = false
                };
                _btnClose.Click += (s, e) => CloseClicked?.Invoke();
                Controls.Add(_btnClose);
            }

            if (_btnMin == null)
            {
                _btnMin = new ModernGradientButton
                {
                    Text = "_",
                    Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                    GradientStart = UiTheme.SecondaryStart,
                    GradientEnd = UiTheme.SecondaryEnd,
                    Size = new Size(48, 48),
                    TabStop = false,
                    Visible = false
                };
                _btnMin.Click += (s, e) => MinimizeClicked?.Invoke();
                Controls.Add(_btnMin);
            }
        }

        private void LayoutControls()
        {
            try
            {
                var right = Width;
                if (_btnClose != null) _btnClose.Location = new Point(Math.Max(0, right - 72), 6);

                if (_btnMin != null)
                {
                    _btnMin.Visible = ShowMinimize;
                    _btnMin.Location = new Point(Math.Max(0, right - 128), 6);
                }

                if (_lblTitle != null) _lblTitle.Height = Height;
            }
            catch { }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            try
            {
                if (Visible) LayoutControls();
                if (_btnMin != null) _btnMin.BringToFront();
                if (_btnClose != null) _btnClose.BringToFront();
            }
            catch { }
        }

        protected override void OnPaint(System.Windows.Forms.PaintEventArgs e)
        {
            try
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var r = ClientRectangle;
                using (var brush = new LinearGradientBrush(r, UiTheme.HeaderGradientStart, UiTheme.HeaderGradientEnd, 0f))
                {
                    e.Graphics.FillRectangle(brush, r);
                }
            }
            catch
            {
                base.OnPaint(e);
            }
        }

        public void ApplyRoundedRegionToForm(System.Windows.Forms.Form f)
        {
            if (f == null) return;
            try
            {
                f.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, f.Width, f.Height, UiTheme.HeaderCornerRadius, UiTheme.HeaderCornerRadius));
            }
            catch { }
        }
    }
}
