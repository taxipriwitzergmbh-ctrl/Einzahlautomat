using System;
using System.Drawing;
using System.Net;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace TaMi_Einzahlautomat
{
    public class QrCodeForm : Form
    {
        private PictureBox _picture;
        private Label _lblCountdown;
        private TextBox _txtContent; // NEU: finaler QR-Text/URL sichtbar machen
        private Button _btnCopy;     // NEU: Kopieren
        private Timer _timer;
        private int _remainingSec = 30; // verlängert: 30s
        private string _qrContent;
        private string _finalContent;   // NEU: tatsächlich kodierter Inhalt

        // Fest eingebaute Quittungsseite (Default, wenn keine INI gesetzt)
        private const string FixedReceiptBaseUrl = "http://kassenautomat.priwitzer-dienstleistungsgmbh.de/Kassenautomat_Quittung.html";

        public QrCodeForm(string qrContent)
        {
            _qrContent = qrContent ?? string.Empty;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(420, 560); // größer, damit nichts abgeschnitten wird
            Text = "QR Code Quittung";
            BackColor = Color.White;

            _lblCountdown = new Label
            {
                Text = "Schließt in 30s",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 44,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold)
            };
            Controls.Add(_lblCountdown);

            _picture = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.White,
                Padding = new Padding(24), // größerer Innenabstand (Quiet Zone)
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(_picture);

            _txtContent = new TextBox
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                ReadOnly = true,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };
            Controls.Add(_txtContent);

            var pnlButtons = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 40
            };
            Controls.Add(pnlButtons);

            _btnCopy = new Button
            {
                Text = "Link kopieren",
                Dock = DockStyle.Left,
                Width = 140
            };
            _btnCopy.Click += (s, e) =>
            {
                try { if (!string.IsNullOrWhiteSpace(_finalContent)) Clipboard.SetText(_finalContent); }
                catch { }
            };
            pnlButtons.Controls.Add(_btnCopy);

            var btnClose = new Button
            {
                Text = "Schließen",
                Dock = DockStyle.Right,
                Width = 120
            };
            btnClose.Click += (s, e) => Close();
            pnlButtons.Controls.Add(btnClose);

            Load += (s, e) => LoadQr();

            _timer = new Timer { Interval = 1000 };
            _timer.Tick += (s, e) => Tick();
        }

        private void LoadQr()
        {
            try
            {
                try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12; } catch { }

                // Inhalt normalisieren (https/http-Link erzeugen)
                _finalContent = NormalizeQrContent(_qrContent);
                _txtContent.Text = _finalContent ?? string.Empty;

                // URL-kodieren
                var enc = Uri.EscapeDataString(_finalContent ?? string.Empty);

                // Provider mit explizitem Rand/Margin für bessere Erkennbarkeit
                var urls = new[]
                {
                    $"https://api.qrserver.com/v1/create-qr-code/?size=540x540&margin=24&data={enc}",
                    $"https://chart.googleapis.com/chart?chs=540x540&cht=qr&chl={enc}&chld=M|2",
                    $"https://quickchart.io/qr?text={enc}&size=540&margin=24"
                };

                byte[] img = null;
                foreach (var url in urls)
                {
                    if (TryDownload(url, 7000, out img))
                        break;
                }

                if (img != null && img.Length > 100)
                {
                    using (var ms = new MemoryStream(img))
                    using (var original = Image.FromStream(ms))
                    {
                        // Zusätzlich lokale Quiet Zone, falls Provider zu knapp ist
                        _picture.Image = AddQuietZone(original, 24);
                    }
                }
                else
                {
                    DrawErrorPlaceholder();
                }
            }
            catch
            {
                DrawErrorPlaceholder();
            }
            _timer.Start();
            UpdateCountdownLabel();
        }

        private static Image AddQuietZone(Image src, int pad)
        {
            try
            {
                int w = Math.Max(1, src.Width);
                int h = Math.Max(1, src.Height);
                var bmp = new Bitmap(w + pad * 2, h + pad * 2);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.White);
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                    g.DrawImage(src, new Rectangle(pad, pad, w, h));
                }
                return bmp;
            }
            catch { return (Image)src.Clone(); }
        }

        private bool TryDownload(string url, int timeoutMs, out byte[] data)
        {
            data = null;
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                req.UserAgent = "Geldautomat/1.0 (+QR)";
                req.Proxy = WebRequest.DefaultWebProxy;
                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    if (resp.StatusCode != HttpStatusCode.OK) return false;
                    using (var ms = new MemoryStream())
                    using (var rs = resp.GetResponseStream())
                    {
                        rs.CopyTo(ms);
                        data = ms.ToArray();
                        return data != null && data.Length > 0;
                    }
                }
            }
            catch { return false; }
        }

        private void DrawErrorPlaceholder()
        {
            try
            {
                var bmp = new Bitmap(360, 360);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.White);
                    using (var pen = new Pen(Color.Red, 4))
                    {
                        g.DrawRectangle(pen, 10, 10, bmp.Width - 20, bmp.Height - 20);
                        g.DrawLine(pen, 20, 20, bmp.Width - 20, bmp.Height - 20);
                        g.DrawLine(pen, bmp.Width - 20, 20, 20, bmp.Height - 20);
                    }
                    using (var f = new Font("Segoe UI", 10, FontStyle.Bold))
                    using (var br = new SolidBrush(Color.Black))
                    {
                        var msg = "QR konnte nicht geladen werden\n(Internet nötig)";
                        var sz = g.MeasureString(msg, f);
                        g.DrawString(msg, f, br, (bmp.Width - sz.Width) / 2, (bmp.Height - sz.Height) / 2);
                    }
                }
                _picture.Image = bmp;
            }
            catch { }
        }

        private void Tick()
        {
            _remainingSec = Math.Max(0, _remainingSec - 1);
            UpdateCountdownLabel();
            if (_remainingSec == 0)
            {
                try { _timer.Stop(); } catch { }
                Close();
            }
        }

        private void UpdateCountdownLabel()
        {
            try { _lblCountdown.Text = $"Schließt in {_remainingSec}s"; } catch { }
        }

        // QR-Inhalt in einen nutzbaren http(s)-Link transformieren
        private string NormalizeQrContent(string raw)
        {
            try
            {
                var s = (raw ?? string.Empty).Trim();

                // Bereits vollständige URL? -> direkt nutzen
                if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    return s;

                // Fester (bzw. aus INI) Quittungs-Endpunkt
                string baseUrl = null;
                try { baseUrl = IniHelper.ReadValue("Receipt", "BaseUrl", AppSettings.IniPath); } catch { }
                if (string.IsNullOrWhiteSpace(baseUrl)) baseUrl = FixedReceiptBaseUrl;

                string qp = null;
                try { qp = IniHelper.ReadValue("Receipt", "QueryParam", AppSettings.IniPath); } catch { }
                if (string.IsNullOrWhiteSpace(qp)) qp = "q";

                string tp = null;
                try { tp = IniHelper.ReadValue("Receipt", "PlainParam", AppSettings.IniPath); } catch { }
                if (string.IsNullOrWhiteSpace(tp)) tp = "t";

                string sep = baseUrl.Contains("?") ? "&" : "?";
                string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
                // Cache-Buster anhängen, um alte HTML-Versionen im Browser/Proxy zu umgehen
                string v = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                string url = baseUrl + sep + qp + "=" + Uri.EscapeDataString(b64) + "&" + tp + "=" + Uri.EscapeDataString(s) + "&v=" + v;
                return url;
            }
            catch
            {
                return raw ?? string.Empty;
            }
        }

        // Data-URL aus reinem Text erzeugen (Reserve, aktuell nicht genutzt)
        private string BuildDataUrl(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) text = "Quittung ist leer.";
            var enc = Uri.EscapeDataString(text);
            var dataUrl = "data:text/plain;charset=utf-8," + enc;
            if (dataUrl.Length > 2500)
            {
                var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
                dataUrl = "data:text/plain;charset=utf-8;base64," + b64;
            }
            return dataUrl;
        }

        private bool LooksLikeUrlWithoutScheme(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            var rx = new Regex(@"^([a-z0-9\-]+\.)+[a-z]{2,}([/:?#].*)?$", RegexOptions.IgnoreCase);
            return rx.IsMatch(s);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { _timer?.Stop(); _timer?.Dispose(); } catch { }
            base.OnFormClosed(e);
        }
    }
}
