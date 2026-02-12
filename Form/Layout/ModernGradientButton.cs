using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TaMi_Einzahlautomat.UI.Layout
{
    internal class ModernGradientButton : System.Windows.Forms.Button
    {
        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

        public Color GradientStart { get; set; } = UiTheme.PrimaryStart;
        public Color GradientEnd { get; set; } = UiTheme.PrimaryEnd;
        public int CornerRadius { get; set; } = UiTheme.ButtonCornerRadius;

        public ModernGradientButton()
        {
            FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.White;
            UseVisualStyleBackColor = false;
            ForeColor = Color.White;

            Resize += (s, e) => UpdateRegion();
        }

        protected override void OnCreateControl()
        {
            base.OnCreateControl();
            UpdateRegion();
        }

        private void UpdateRegion()
        {
            try
            {
                Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, Width, Height, CornerRadius, CornerRadius));
            }
            catch { }
        }

        protected override void OnPaint(System.Windows.Forms.PaintEventArgs e)
        {
            try
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

                var rect = ClientRectangle;
                rect.Width -= 1;
                rect.Height -= 1;

                using (var br = new LinearGradientBrush(rect, GradientStart, GradientEnd, 90f))
                {
                    e.Graphics.FillRectangle(br, rect);
                }

                using (var pen = new Pen(Color.FromArgb(110, 255, 255, 255), 1f))
                {
                    e.Graphics.DrawRectangle(pen, rect);
                }

                System.Windows.Forms.TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
                    System.Windows.Forms.TextFormatFlags.HorizontalCenter | System.Windows.Forms.TextFormatFlags.VerticalCenter);
            }
            catch
            {
                base.OnPaint(e);
            }
        }
    }
}
