using System;
using System.IO.Ports;
using System.Text;

namespace Geldautomat
{
    // Grundlage für die NV200-Anbindung via COM.
    // Ohne konkrete Protokollkommandos (z. B. SSP-Poll) antwortet das Gerät i. d. R. nicht.
    public class NV200SerialPort : IDisposable
    {
        private readonly SerialPort _port;

        public bool IsOpen => _port.IsOpen;
        public string PortName => _port.PortName;

        public NV200SerialPort(string portName, int baudRate = 9600, Parity parity = Parity.None, int dataBits = 8, StopBits stopBits = StopBits.One)
        {
            _port = new SerialPort(portName, baudRate, parity, dataBits, stopBits)
            {
                Handshake = Handshake.None,
                ReadTimeout = 1000,
                WriteTimeout = 1000,
                ReceivedBytesThreshold = 1,
                DtrEnable = true, // Viele NV200-Adapter benötigen DTR/RTS
                RtsEnable = true
            };
            _port.DataReceived += PortOnDataReceived;
            _port.ErrorReceived += PortOnErrorReceived;
        }

        public void Open()
        {
            if (!_port.IsOpen)
                _port.Open();
        }

        public void Close()
        {
            if (_port.IsOpen)
                _port.Close();
        }

        public void SendRaw(byte[] data)
        {
            if (!_port.IsOpen) throw new InvalidOperationException("Port ist nicht geöffnet.");
            _port.Write(data, 0, data.Length);
        }

        public event EventHandler<byte[]> DataReceivedRaw;
        public event EventHandler<string> PortError;

        private void PortOnDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                // Alles verfügbare lesen
                int total = _port.BytesToRead;
                if (total <= 0) return;

                byte[] buffer = new byte[total];
                int offset = 0;
                while (_port.BytesToRead > 0 && offset < buffer.Length)
                {
                    int read = _port.Read(buffer, offset, buffer.Length - offset);
                    if (read <= 0) break;
                    offset += read;
                }

                if (offset > 0)
                {
                    if (offset != buffer.Length)
                    {
                        var trimmed = new byte[offset];
                        Buffer.BlockCopy(buffer, 0, trimmed, 0, offset);
                        DataReceivedRaw?.Invoke(this, trimmed);
                    }
                    else
                    {
                        DataReceivedRaw?.Invoke(this, buffer);
                    }
                }
            }
            catch (Exception ex)
            {
                PortError?.Invoke(this, ex.Message);
            }
        }

        private void PortOnErrorReceived(object sender, SerialErrorReceivedEventArgs e)
        {
            try
            {
                PortError?.Invoke(this, $"SerialError: {e.EventType}");
            }
            catch { }
        }

        public static string ToHex(byte[] data)
        {
            if (data == null) return string.Empty;
            var sb = new StringBuilder(data.Length * 3);
            foreach (var b in data)
                sb.AppendFormat("{0:X2} ", b);
            return sb.ToString().TrimEnd();
        }

        public void Dispose()
        {
            try
            {
                if (_port != null)
                {
                    _port.DataReceived -= PortOnDataReceived;
                    _port.ErrorReceived -= PortOnErrorReceived;
                    if (_port.IsOpen) _port.Close();
                    _port.Dispose();
                }
            }
            catch
            {
                // Ignorieren beim Dispose
            }
        }
    }
}