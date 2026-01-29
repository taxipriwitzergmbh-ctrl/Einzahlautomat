using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TaMi_Einzahlautomat
{
    public class BackgroundForm : Form
    {
        private readonly bool _kioskMode;
        private PictureBox _picture;
        private Image _backgroundImage; // geladenes Hintergrundbild
        // Neuer Offset: negative Werte schieben das Bild nach oben (zeigen mehr vom unteren Bereich)
        private const int VerticalImageOffsetPx = -80; // bei Bedarf anpassen

        public BackgroundForm(bool kioskMode)
        {
            _kioskMode = kioskMode;
            InitializeBackground();
        }

        private void InitializeBackground()
        {
            AutoScaleMode = AutoScaleMode.None; // verhindert DPI-Autoscaling -> vermeidet schwarze R�nder
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowIcon = false;
            ShowInTaskbar = false; // Hintergrund nicht in Taskleiste
            BackColor = Color.Black;
            DoubleBuffered = true;
            TopMost = _kioskMode; // Im Kioskmodus immer oben (eigene Fenster bleiben per Owner dar�ber)

            _picture = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Normal, // eigenes Zeichnen (Cover)
                BackColor = Color.Black
            };
            _picture.Paint += Picture_Paint; // Cover-Rendering
            Controls.Add(_picture);

            LoadBackgroundImage();

            Shown += (s, e) =>
            {
                try { Bounds = Screen.PrimaryScreen.Bounds; } catch { }
                if (_kioskMode) HideTaskbar();
                SendToBack();
                // sicherstellen dass sofort neu gerendert wird
                try { _picture.Invalidate(); } catch { }
            };

            Resize += (s, e) => { try { _picture.Invalidate(); } catch { } }; // bei Gr��en�nderung neu zeichnen

            FormClosed += (s, e) =>
            {
                if (_kioskMode) ShowTaskbar();
                try { _backgroundImage?.Dispose(); } catch { }
            };
        }

        // Cover-Scaling: f�llt komplette Breite/H�he, schneidet ggf. �berstehende Bereiche ab (keine Balken)
        private void Picture_Paint(object sender, PaintEventArgs e)
        {
            if (_backgroundImage == null) return;
            try
            {
                var g = e.Graphics;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                float formW = _picture.Width;
                float formH = _picture.Height;
                float imgW = _backgroundImage.Width;
                float imgH = _backgroundImage.Height;
                if (formW <= 0 || formH <= 0 || imgW <= 0 || imgH <= 0) return;
                float scale = Math.Max(formW / imgW, formH / imgH); // cover
                float drawW = imgW * scale;
                float drawH = imgH * scale;
                float x = (formW - drawW) / 2f;
                float y = (formH - drawH) / 2f + VerticalImageOffsetPx; // Offset anwenden
                g.DrawImage(_backgroundImage, x, y, drawW, drawH);
            }
            catch { }
        }

        private void LoadBackgroundImage()
        {
            try
            {
                var img = TaMi_Einzahlautomat.Properties.Resources.Hintergrund;
                if (img != null)
                {
                    _backgroundImage?.Dispose();
                    _backgroundImage = new Bitmap(img); // lokale Kopie aus Embedded Resource
                }
            }
            catch { }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE: Hintergrund erh�lt keinen Fokus
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW: nicht in Alt-Tab
                cp.ExStyle |= 0x00000008; // WS_EX_TOPMOST
                return cp;
            }
        }

        protected override bool ShowWithoutActivation => true;

        // Taskleiste ein-/ausblenden im Kioskmodus
        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;

        private void HideTaskbar()
        {
            try
            {
                var taskbar = FindWindow("Shell_TrayWnd", null);
                if (taskbar != IntPtr.Zero) ShowWindow(taskbar, SW_HIDE);
                var start = FindWindow("Button", null); // �lteres Startmen�
                if (start != IntPtr.Zero) ShowWindow(start, SW_HIDE);
            }
            catch { }
        }

        private void ShowTaskbar()
        {
            try
            {
                var taskbar = FindWindow("Shell_TrayWnd", null);
                if (taskbar != IntPtr.Zero) ShowWindow(taskbar, SW_SHOW);
                var start = FindWindow("Button", null);
                if (start != IntPtr.Zero) ShowWindow(start, SW_SHOW);
            }
            catch { }
        }
    }
}
