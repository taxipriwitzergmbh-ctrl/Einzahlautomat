using System;
using System.IO.Ports;
using System.Text;

namespace Geldautomat.Printing
{
    public class LegacyItlPrinterDevice : IDisposable
    {
        private readonly object _sync = new object();
        private SerialPort _port;
        private readonly LegacyItlPrinterConfig _cfg;
        public string Name { get; private set; }

        public LegacyItlPrinterDevice(string name, LegacyItlPrinterConfig cfg)
        {
            Name = name;
            _cfg = cfg != null ? cfg.Clone() : new LegacyItlPrinterConfig();
        }

        public bool EnsureOpen()
        {
            if (_cfg == null || !_cfg.Enabled || string.IsNullOrWhiteSpace(_cfg.ComPort)) return false;
            lock (_sync)
            {
                if (_port != null && _port.IsOpen) return true;
                try
                {
                    _port = new SerialPort(_cfg.ComPort, _cfg.BaudRate, _cfg.Parity, _cfg.DataBits, _cfg.StopBits);
                    _port.Handshake = Handshake.None;
                    _port.Encoding = Encoding.GetEncoding(1252);
                    _port.NewLine = "\r\n";
                    _port.Open();
                    SendInit();
                    return true;
                }
                catch (Exception ex)
                {
                    try { AppLogger.Log("LegacyItlPrinterDevice.Open Fehler " + Name + ": " + ex.Message); } catch { }
                    SafeClose();
                    return false;
                }
            }
        }

        private void SendInit()
        {
            // ESC @ reset
            TryWrite(new byte[] { 0x1B, 0x40 });
            // Select code page 1252 (Windows Western) for Euro symbol: ESC t 16
            TryWrite(new byte[] { 0x1B, 0x74, 16 });
        }

        private void SendCut()
        {
            if (!_cfg.CutAfterPrint) return;
            // Platzhalter GS V 0 (Partial / simple) � ggf. anpassen
            TryWrite(new byte[] { 0x1D, 0x56, 0x00 });
        }

        private void TryWrite(byte[] raw)
        {
            lock (_sync)
            {
                try { if (_port != null && _port.IsOpen) _port.Write(raw, 0, raw.Length); } catch { }
            }
        }
        private void TryWriteLine(string line)
        {
            lock (_sync)
            {
                try
                {
                    if (_port != null && _port.IsOpen)
                    {
                        _port.Write(line);
                        _port.Write("\r\n");
                    }
                }
                catch { }
            }
        }

        public bool PrintLines(string header, string[] lines)
        {
            if (!EnsureOpen()) return false;
            try
            {
                if (!string.IsNullOrEmpty(header))
                {
                    // Bold ON (ESC E 1) optional
                    TryWrite(new byte[] { 0x1B, 0x45, 0x01 });
                    TryWriteLine(header);
                    // Bold OFF
                    TryWrite(new byte[] { 0x1B, 0x45, 0x00 });
                    TryWriteLine(string.Empty);
                }
                int width = _cfg.CharsPerLine > 0 ? _cfg.CharsPerLine : 40;
                if (lines != null)
                {
                    foreach (var l in lines)
                    {
                        var text = l ?? string.Empty;
                        if (text.Length > width) text = text.Substring(0, width);
                        TryWriteLine(text);
                    }
                }
                TryWriteLine(string.Empty);
                TryWriteLine(DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"));
                TryWriteLine(string.Empty);
                SendCut();
                return true;
            }
            catch (Exception ex)
            {
                try { AppLogger.Log("LegacyItlPrinterDevice.PrintLines Fehler: " + ex.Message); } catch { }
                return false;
            }
        }

        private void SafeClose()
        {
            lock (_sync)
            {
                try
                {
                    if (_port != null)
                    {
                        try { _port.Close(); } catch { }
                        try { _port.Dispose(); } catch { }
                    }
                }
                catch { }
                _port = null;
            }
        }

        public void Dispose()
        {
            SafeClose();
        }
    }
}
