using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Drawing.Drawing2D;

namespace TaMi_Einzahlautomat
{
    // Moderne, flache Bottom-Bar mit sanfter Accent-Linie + Shimmer Progress (indeterminate)
    public sealed class ProcessingAnimation : IDisposable
    {
        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

        private Form _form;
        private System.Windows.Forms.Timer _textTimer;
        private System.Windows.Forms.Timer _animTimer;
        private string _baseText;
        private int _dotCount;
        private int _disposed;
        private float _shimmerX; // Animationsoffset
        private DateTime _lastFrame = DateTime.UtcNow;
        private const float ShimmerSpeed = 320f; // px / Sekunde
        private const int TargetHeightMin = 76;
        private const int TargetHeightMax = 160;
        private static readonly Color Accent = Color.FromArgb(0, 122, 255); // Modernes Blau
        private static readonly Color AccentDim = Color.FromArgb(0, 90, 210);
        private static readonly Color BackgroundTop = Color.FromArgb(235, 18, 22, 30);
        private static readonly Color BackgroundBottom = Color.FromArgb(235, 12, 14, 20);
        private static readonly Color BorderLine = Color.FromArgb(40, 255, 255, 255);

        private ProcessingAnimation(Form f) { _form = f; }

        public static ProcessingAnimation Show(Form owner, string text = "Bitte warten …")
        {
            Rectangle screenBounds;
            try { screenBounds = owner != null ? owner.Bounds : Screen.PrimaryScreen.WorkingArea; } catch { screenBounds = Screen.PrimaryScreen.WorkingArea; }

            float dpiY = 96f;
            try { using (var g = Graphics.FromHwnd(IntPtr.Zero)) dpiY = g.DpiY; } catch { }
            int targetHeight = (int)Math.Round((3.2f / 2.54f) * dpiY); // ~3.2 cm
            if (targetHeight < TargetHeightMin) targetHeight = TargetHeightMin;
            if (targetHeight > TargetHeightMax) targetHeight = TargetHeightMax;

            var f = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                Size = new Size(screenBounds.Width, targetHeight),
                BackColor = Color.Black,
                ShowInTaskbar = false,
                TopMost = owner != null && owner.TopMost,
                Font = new Font("Segoe UI", 11f, FontStyle.Regular)
            };
            f.Location = new Point(screenBounds.Left, screenBounds.Bottom - targetHeight);
            try { f.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, f.Width, f.Height, 16, 16)); } catch { }

            var panel = new ShimmerPanel { Dock = DockStyle.Fill };
            f.Controls.Add(panel);

            // Textbereich (links ausgerichtet)
            var lbl = new Label
            {
                Text = text,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Location = new Point(32, 0),
                Size = new Size(f.ClientSize.Width - 64, f.ClientSize.Height - 20),
                Font = new Font("Segoe UI Semibold", 18f, FontStyle.Regular),
                ForeColor = Color.FromArgb(235, 240, 245),
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
            };
            panel.Controls.Add(lbl);

            var anim = new ProcessingAnimation(f) { _baseText = text };
            f.Tag = anim;
            panel.Tag = anim; // Panel braucht Zugriff

            anim._textTimer = new System.Windows.Forms.Timer { Interval = 700 };
            anim._textTimer.Tick += (s, e) =>
            {
                try { anim._dotCount = (anim._dotCount + 1) % 4; lbl.Text = anim._baseText + new string('.', anim._dotCount); } catch { }
            };
            try { anim._textTimer.Start(); } catch { }

            anim._animTimer = new System.Windows.Forms.Timer { Interval = 22 }; // ~45 FPS für smooth aber moderat
            anim._animTimer.Tick += (s, e) => { try { anim.Step(panel); } catch { } };
            try { anim._animTimer.Start(); } catch { }

            panel.Paint += (s, e) => anim.Paint(e.Graphics, panel.ClientRectangle, lbl); // custom draw

            try { if (owner != null) f.Show(owner); else f.Show(); } catch { }
            return anim;
        }

        private void Step(Control surface)
        {
            var now = DateTime.UtcNow;
            float dt = (float)(now - _lastFrame).TotalSeconds;
            if (dt <= 0f || dt > 0.5f) dt = 0.016f;
            _lastFrame = now;
            _shimmerX += ShimmerSpeed * dt;
            if (_shimmerX > surface.Width * 2) _shimmerX = 0; // loop
            surface.Invalidate();
        }

        private void Paint(Graphics g, Rectangle bounds, Label lbl)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            // Hintergrund Verlauf
            using (var lg = new LinearGradientBrush(bounds, BackgroundTop, BackgroundBottom, LinearGradientMode.Vertical))
            { g.FillRectangle(lg, bounds); }

            // Obere feine Linie (dezent)
            using (var penTop = new Pen(BorderLine, 1f)) g.DrawLine(penTop, bounds.Left + 8, bounds.Top + 1, bounds.Right - 8, bounds.Top + 1);

            // Accent-Linie unter dem Textbereich (statisch) -> baseline
            int progressBaselineY = bounds.Bottom - 18; // etwas über unterem Rand
            using (var penBase = new Pen(Color.FromArgb(60, Accent), 3f))
                g.DrawLine(penBase, 32, progressBaselineY, bounds.Right - 32, progressBaselineY);

            // Shimmer / Indeterminate Segment (ein breiter softer sweep)
            float sweepWidth = Math.Max(140f, bounds.Width * 0.18f);
            float x = (_shimmerX % (bounds.Width + sweepWidth)) - sweepWidth; // kann negativ sein beim Übergang
            var shimmerRect = new RectangleF(x, progressBaselineY - 4, sweepWidth, 8);

            if (shimmerRect.Right > 32 && shimmerRect.Left < bounds.Right - 32)
            {
                // Begrenzen auf Nutzbereich
                float clipL = 32; float clipR = bounds.Right - 32;
                var clipped = RectangleF.Intersect(shimmerRect, new RectangleF(clipL, shimmerRect.Y, clipR - clipL, shimmerRect.Height));
                if (clipped.Width > 1)
                {
                    using (var path = new GraphicsPath())
                    {
                        path.AddRectangle(clipped);
                        using (var br = new LinearGradientBrush(clipped,
                            Color.FromArgb(35, AccentDim),
                            Color.FromArgb(175, Accent), 0f))
                        {
                            br.WrapMode = WrapMode.TileFlipXY;
                            g.FillPath(br, path);
                        }
                        // Soft edges durch overlay gradient
                        using (var edgeFade = new LinearGradientBrush(clipped,
                            Color.FromArgb(0, Accent), Color.FromArgb(140, Accent), LinearGradientMode.Horizontal))
                        { g.FillPath(edgeFade, path); }
                    }
                }
            }

            // Dezenter Glow über Baseline (leichtes Highlight unter Text – nur falls Platz)
            var glowRect = new RectangleF(32, progressBaselineY - 10, bounds.Width - 64, 20);
            using (var lgGlow = new LinearGradientBrush(glowRect, Color.FromArgb(28, Accent), Color.FromArgb(0, Accent), LinearGradientMode.Vertical))
            { g.FillRectangle(lgGlow, glowRect); }

            // Optional rechter Statusplatzhalter (kann später genutzt werden)
            // (absichtlich leer gelassen – reserved area)
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            var f = _form; _form = null;
            try { _textTimer?.Stop(); _textTimer?.Dispose(); } catch { }
            try { _animTimer?.Stop(); _animTimer?.Dispose(); } catch { }
            _textTimer = null; _animTimer = null;
            try
            {
                if (f != null && !f.IsDisposed)
                {
                    Action c = () => { try { if (!f.IsDisposed) f.Close(); } catch { } };
                    if (f.InvokeRequired) { try { f.BeginInvoke(c); } catch { } } else c();
                }
            }
            catch { }
        }

        private class ShimmerPanel : Panel
        {
            public ShimmerPanel() { this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true); }
            protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x02000000; return cp; } }
        }
    }
}
