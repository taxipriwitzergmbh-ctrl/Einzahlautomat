using System.Collections.Generic;
using System;
using System.IO.Ports;
using System.Text;
using System.Linq;

namespace TaMi_Einzahlautomat.Devices
{
    public enum CoinFeederProtocolMode
    {
        Auto,
        ASCII,
        HEX
    }

    public class CoinFeederController : IDisposable
    {
        private SerialPort _port;
        // RX parsing state for NFC (decimal bytes separated by '$', frames end with CR/LF)
        private readonly object _rxLock = new object();
        private System.Text.StringBuilder _currentNumber = new System.Text.StringBuilder();
        private readonly List<int> _numberBuffer = new List<int>();
        // Typical UID here is 7 bytes; accept 6 as minimum to be tolerant for split frames
        private const int MinNfcTokenCount = 6;

        // New: mark if the current incoming frame contained a numeric token > 255 (likely telemetry like "2011")
        private bool _currentFrameHasLargeNumber = false;

        // Special handling for 2011 header frames used by some devices: "2011$<len>$<b1>$<b2>..."
        private bool _currentFrameHas2011Header = false;
        private int _expected2011Length = -1;

        // Debounce last emitted NFC token to avoid chatter from device
        private string _lastEmittedNfcToken = null;
        private DateTime _lastEmittedNfcTime = DateTime.MinValue;
        private static readonly TimeSpan NfcEmitDebounce = TimeSpan.FromSeconds(2);

        // Zentral: ignorierte NFC Tokens (aus INI)
        private readonly List<string> _ignoredNfcTokens = new List<string>();

        public string PortName { get; private set; }
        public int BaudRate { get; private set; } = 9600;
        public Parity Parity { get; private set; } = Parity.None;
        public int DataBits { get; private set; } = 8;
        public StopBits StopBits { get; private set; } = StopBits.One;
        public bool Dtr { get; private set; } = false; // default: disabled
        public bool Rts { get; private set; } = false; // default: disabled
        public bool IsOpen => _port != null && _port.IsOpen;

        // Default: Auto erkennt anhand des Templates, ASCII wenn nicht HEX.
        public CoinFeederProtocolMode Mode { get; set; } = CoinFeederProtocolMode.Auto;
        // Default Terminator: LF only. Important: do NOT append CR.
        public string Terminator { get; set; } = "\n";
        // If true, and template ends with '$', append Terminator
        public bool AppendTerminatorAfterDollar { get; set; } = true;

        // Allow disabling NFC parsing/invocation at runtime (useful for diagnostics/logging)
        public bool NfcEnabled { get; set; } = true;

        public event Action<string> EventLog;
        // Fired when an NFC token is decoded from incoming serial data (hex string, uppercase)
        public event Action<string> NfcReceived;

        public CoinFeederController() { LoadIgnoredNfcTokens(); }

        public void Configure(string portName, int baud = 9600, Parity parity = Parity.None, int dataBits = 8, StopBits stopBits = StopBits.One)
        {
            PortName = portName;
            BaudRate = baud;
            Parity = parity;
            DataBits = dataBits;
            StopBits = stopBits;
        }

        public void ConfigureAdvanced(string portName, int baud, Parity parity, int dataBits, StopBits stopBits, bool dtr, bool rts)
        {
            PortName = portName;
            BaudRate = baud;
            Parity = parity;
            DataBits = dataBits;
            StopBits = stopBits;
            Dtr = dtr;
            Rts = rts;
        }

        public void Open()
        {
            if (IsOpen) return;
            if (string.IsNullOrWhiteSpace(PortName)) throw new InvalidOperationException("PortName not set");
            _port = new SerialPort(PortName, BaudRate, Parity, DataBits, StopBits)
            {
                NewLine = "\n",
                Encoding = Encoding.ASCII,
                ReadTimeout = 500,
                WriteTimeout = 500,
                Handshake = Handshake.None,
                DtrEnable = Dtr,
                RtsEnable = Rts
            };
            _port.DataReceived += Port_DataReceived;
            _port.Open();
            Log($"[INFO] Port geöffnet: {PortName} @ {BaudRate}bps {Parity}/{DataBits}/{StopBits} DTR={(Dtr ? 1 : 0)} RTS={(Rts ? 1 : 0)}");
        }

        private void Port_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                int toRead = _port.BytesToRead;
                if (toRead <= 0) return;
                var buffer = new byte[toRead];
                int read = _port.Read(buffer, 0, buffer.Length);
                if (read > 0)
                {
                    string hex = BitConverter.ToString(buffer, 0, read).Replace('-', ' ');

                    string ascii = Encoding.ASCII.GetString(buffer, 0, read);
                    Log($"RX [{DateTime.Now:HH:mm:ss}] HEX: {hex}");
                    Log($"RX [{DateTime.Now:HH:mm:ss}] ASCII: {Sanitize(ascii)}");

                    // Try to parse NFC-style decimal byte tokens separated by '$'
                    ParseAsciiForNfc(ascii);
                }
            }
            catch (Exception ex)
            {
                Log($"[ERR] RX: {ex.Message}");
            }
        }

        private bool FrameLooksLikeTelemetry(List<int> tokens)
        {
            if (tokens == null || tokens.Count == 0) return false;
            // Check whole buffer for NV200-like status anywhere: common pattern 100,0,0,0,1,0
            if (tokens.Count >= 6)
            {
                // If any contiguous subsequence of length >=6 matches status heuristic, treat frame as telemetry
                for (int i = 0; i + 6 <= tokens.Count; i++)
                {
                    var window = tokens.GetRange(i, 6).ToArray();
                    if (IsLikelyNv200StatusReport(window)) return true;
                }

                // also heuristic: if buffer mostly zeros and small values and contains 100 or 1 as anchor
                int zeroCount = 0;
                int smallCount = 0;
                foreach (var b in tokens)
                {
                    if (b == 0) zeroCount++;
                    if (b <= 200) smallCount++;
                }
                if (zeroCount >= tokens.Count - 2 && (tokens.Contains(1) || tokens.Contains(10) || tokens.Contains(100)))
                    return true;
            }
            return false;
        }

        // Explizite RM5-Blacklist (Großschreibung, ohne Trennzeichen)
        private static readonly HashSet<string> _rm5ExplicitBlacklist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "640101002E01",
            "640101004C01",
            "640101006A01",
            "640101008801",
            "64010100A601",
            "64010100C401",
            "64010100E201",
        };

        // Token-String prüfen (nur 6-Byte-Frames), plus Muster 64 01 01 00 ?? 01
        private static bool IsExplicitRm5Blacklisted(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;
            var t = token.Replace(" ", string.Empty).Replace("-", string.Empty).ToUpperInvariant();
            if (t.Length != 12) return false; // genau 6 Bytes
            if (_rm5ExplicitBlacklist.Contains(t)) return true;
            if (t.StartsWith("64010100", StringComparison.Ordinal) && t.EndsWith("01", StringComparison.Ordinal))
                return true;
            return false;
        }

        private void ParseAsciiForNfc(string ascii)
        {
            if (string.IsNullOrEmpty(ascii)) return;
            lock (_rxLock)
            {
                foreach (char ch in ascii)
                {
                    if (char.IsDigit(ch))
                    {
                        if (_currentNumber.Length < 6) // prevent runaway numbers
                            _currentNumber.Append(ch);
                        continue;
                    }
                    if (ch == '$')
                    {
                        if (_currentNumber.Length > 0)
                        {
                            if (int.TryParse(_currentNumber.ToString(), out int v))
                            {
                                // Special-case: 2011 is a known header in KassensystemPRO frames
                                if (v > 255)
                                {
                                    if (v == 2011)
                                    {
                                        // mark header - do NOT treat as general telemetry
                                        _currentFrameHas2011Header = true;
                                        _expected2011Length = -1; // reset expected length until we read next token
                                    }
                                    else
                                    {
                                        _currentFrameHasLargeNumber = true;
                                    }
                                }
                                else
                                {
                                    // If we have seen 2011 header and no length yet, the next token is the length
                                    if (_currentFrameHas2011Header && _expected2011Length == -1)
                                    {
                                        _expected2011Length = v;
                                    }
                                    else
                                    {
                                        _numberBuffer.Add(v & 0xFF);
                                    }
                                }
                            }
                            _currentNumber.Clear();
                        }
                        // Direkt nach Separator prüfen, ob 2011-UID vollständig ist und sofort emittieren
                        if (_currentFrameHas2011Header && _expected2011Length > 0 && _numberBuffer.Count >= _expected2011Length)
                        {
                            var uidBytes = _numberBuffer.GetRange(0, _expected2011Length).ToArray();
                            var sbEmit = new System.Text.StringBuilder(uidBytes.Length * 2);
                            foreach (var b in uidBytes) sbEmit.AppendFormat("{0:X2}", b);
                            string tokenEmit = sbEmit.ToString();

                            if (IsExplicitRm5Blacklisted(tokenEmit))
                            {
                                Log($"NFC: RM5 status frame suppressed ({tokenEmit})");
                                _numberBuffer.RemoveRange(0, _expected2011Length);
                                _currentFrameHas2011Header = false; _expected2011Length = -1; _currentFrameHasLargeNumber = false;
                            }
                            else if (IsIgnoredNfcToken(tokenEmit))
                            {
                                _numberBuffer.RemoveRange(0, _expected2011Length);
                                _currentFrameHas2011Header = false; _expected2011Length = -1; _currentFrameHasLargeNumber = false;
                            }
                            else
                            {
                                var nowEmit = DateTime.UtcNow;
                                if (!(string.Equals(_lastEmittedNfcToken, tokenEmit, StringComparison.OrdinalIgnoreCase) && (nowEmit - _lastEmittedNfcTime) < NfcEmitDebounce))
                                {
                                    try { if (NfcEnabled) NfcReceived?.Invoke(tokenEmit); } catch { }
                                    try { if (NfcEnabled) EventLog?.Invoke($"NFC: {tokenEmit}"); } catch { }
                                    _lastEmittedNfcToken = tokenEmit;
                                    _lastEmittedNfcTime = nowEmit;
                                }
                                _numberBuffer.RemoveRange(0, _expected2011Length);
                                _currentFrameHas2011Header = false; _expected2011Length = -1; _currentFrameHasLargeNumber = false;
                            }
                        }
                        // $ is a separator only
                        continue;
                    }
                    if (ch == '\r' || ch == '\n')
                    {
                        // end of frame - push current number if present
                        if (_currentNumber.Length > 0)
                        {
                            if (int.TryParse(_currentNumber.ToString(), out int v))
                            {
                                if (v > 255)
                                {
                                    if (v == 2011)
                                    {
                                        _currentFrameHas2011Header = true;
                                        _expected2011Length = -1;
                                    }
                                    else
                                    {
                                        _currentFrameHasLargeNumber = true;
                                    }
                                }
                                else
                                {
                                    if (_currentFrameHas2011Header && _expected2011Length == -1)
                                        _expected2011Length = v;
                                    else
                                        _numberBuffer.Add(v & 0xFF);
                                }
                            }
                            _currentNumber.Clear();
                        }

                        // If this frame contains a 2011 header, try to extract UID from it
                        if (_currentFrameHas2011Header)
                        {
                            if (_expected2011Length > 0 && _numberBuffer.Count >= _expected2011Length)
                            {
                                // take first expected2011Length bytes as UID
                                var uidBytes = _numberBuffer.GetRange(0, _expected2011Length).ToArray();
                                var sb = new StringBuilder(uidBytes.Length * 2);
                                foreach (var b in uidBytes) sb.AppendFormat("{0:X2}", b);
                                string token = sb.ToString();

                                // Explizit RM5-Status unterdrücken
                                if (IsExplicitRm5Blacklisted(token))
                                {
                                    Log($"NFC: RM5 status frame suppressed ({token})");
                                    _numberBuffer.RemoveRange(0, _expected2011Length);
                                    _currentFrameHas2011Header = false; _expected2011Length = -1; _currentFrameHasLargeNumber = false;
                                    continue;
                                }

                                // zentral: ignorierte Tokens nicht melden
                                if (IsIgnoredNfcToken(token))
                                {
                                    
                                    _numberBuffer.RemoveRange(0, _expected2011Length);
                                    _currentFrameHas2011Header = false; _expected2011Length = -1; _currentFrameHasLargeNumber = false;
                                    continue;
                                }

                                // emit token
                                var nowEmit = DateTime.UtcNow;
                                if (!(string.Equals(_lastEmittedNfcToken, token, StringComparison.OrdinalIgnoreCase) && (nowEmit - _lastEmittedNfcTime) < NfcEmitDebounce))
                                {
                                    try { if (NfcEnabled) NfcReceived?.Invoke(token); } catch { }
                                    try { if (NfcEnabled) EventLog?.Invoke($"NFC: {token}"); } catch { }
                                    _lastEmittedNfcToken = token;
                                    _lastEmittedNfcTime = nowEmit;
                                }

                                // remove consumed tokens
                                _numberBuffer.RemoveRange(0, _expected2011Length);

                                // reset header flags after successful extraction
                                _currentFrameHas2011Header = false;
                                _expected2011Length = -1;
                                _currentFrameHasLargeNumber = false;
                            }

                            // not enough data yet -> wait for next frame
                            continue;
                        }

                        // If frame had an out-of-range numeric token (but not 2011), treat whole frame as telemetry/status and skip NFC detection
                        if (_currentFrameHasLargeNumber)
                        {
                            Log("RX: telemetry/status frame detected (large token), skipping NFC parse for this frame");
                            _numberBuffer.Clear();
                            _currentFrameHasLargeNumber = false;
                            _currentFrameHas2011Header = false;
                            _expected2011Length = -1;
                            continue;
                        }

                        // Stronger telemetry detection
                        if (FrameLooksLikeTelemetry(_numberBuffer))
                        {
                            Log("RX: telemetry/status frame detected (pattern), skipping NFC parse for this frame");
                            _numberBuffer.Clear();
                            continue;
                        }

                        // Nur NFC-Erkennung zulassen, wenn der Frame mit 2011$ begonnen hat
                        if (!_currentFrameHas2011Header)
                        {
                            // Kein 2011-Header -> als Telemetrie behandeln und verwerfen
                            _numberBuffer.Clear();
                            continue;
                        }

                        // Wenn wir genug Tokens haben, versuche eine UID zu finden (präferiert 7 Bytes)
                        if (_numberBuffer.Count >= MinNfcTokenCount)
                        {
                            byte[] uid = null;
                            int uidLenFound = 0;
                            int startIndex = -1;

                            // prefer 7, then 6
                            for (int want = 7; want >= MinNfcTokenCount; want--)
                            {
                                for (int i = 0; i + want <= _numberBuffer.Count; i++)
                                {
                                    var window = _numberBuffer.GetRange(i, want).ToArray();
                                    // skip status-like windows or all-zero
                                    if (IsLikelyNv200StatusReport(window)) continue;
                                    bool allZero = true; foreach (var b in window) if (b != 0) { allZero = false; break; }
                                    if (allZero) continue;

                                    uid = new byte[want];
                                    for (int k = 0; k < want; k++) uid[k] = (byte)window[k];
                                    uidLenFound = want;
                                    startIndex = i;
                                    break;
                                }
                                if (uid != null) break;
                            }

                            if (uid != null && uidLenFound > 0)
                            {
                                var sb = new StringBuilder(uid.Length * 2);
                                foreach (var b in uid) sb.AppendFormat("{0:X2}", b);
                                string token = sb.ToString();

                                // RM5-Status (Heuristik) ODER explizit gemeldete Sequenzen unterdrücken
                                if (IsLikelyRm5StatusToken(uid) || IsExplicitRm5Blacklisted(token))
                                {
                                    Log($"NFC: RM5 status frame suppressed ({token})");
                                    int removeCountSupp = startIndex + uidLenFound;
                                    if (removeCountSupp > 0) _numberBuffer.RemoveRange(0, removeCountSupp);
                                    continue; // do not treat as NFC
                                }

                                // zentral: ignorierte Tokens nicht melden
                                if (IsIgnoredNfcToken(token))
                                {
                                    
                                    int removeCountIgn = startIndex + uidLenFound;
                                    if (removeCountIgn > 0) _numberBuffer.RemoveRange(0, removeCountIgn);
                                    continue;
                                }

                                // ignore trivial all-zero tokens
                                if (token.Trim('0').Length == 0)
                                {
                                    int removeCount0 = startIndex + uidLenFound;
                                    if (removeCount0 > 0) _numberBuffer.RemoveRange(0, removeCount0);
                                }
                                else
                                {
                                    // Debounce emitted tokens
                                    var now = DateTime.UtcNow;
                                    if (string.Equals(_lastEmittedNfcToken, token, StringComparison.OrdinalIgnoreCase) && (now - _lastEmittedNfcTime) < NfcEmitDebounce)
                                    {
                                        Log($"NFC: duplicate token '{token}' suppressed");
                                    }
                                    else
                                    {
                                        try { if (NfcEnabled) NfcReceived?.Invoke(token); } catch { }
                                        try { if (NfcEnabled) EventLog?.Invoke($"NFC: {token}"); } catch { }

                                        _lastEmittedNfcToken = token;
                                        _lastEmittedNfcTime = now;
                                    }

                                    // remove consumed tokens up to end of window
                                    int removeCount = startIndex + uidLenFound;
                                    if (removeCount > 0)
                                    {
                                        _numberBuffer.RemoveRange(0, removeCount);
                                    }
                                }
                            }
                            else
                            {
                                // No suitable window found; cap buffer and keep for next fragments
                                const int MaxBufferedTokens = 64;
                                if (_numberBuffer.Count > MaxBufferedTokens)
                                {
                                    int remove = _numberBuffer.Count - MaxBufferedTokens;
                                    _numberBuffer.RemoveRange(0, remove);
                                }
                            }
                        }

                        continue;
                    }
                    // any other char: treat like separator
                    if (_currentNumber.Length > 0)
                    {
                        if (int.TryParse(_currentNumber.ToString(), out int v))
                        {
                            if (v > 255)
                            {
                                if (v == 2011)
                                {
                                    _currentFrameHas2011Header = true;
                                    _expected2011Length = -1;
                                }
                                else
                                {
                                    _currentFrameHasLargeNumber = true;
                                }
                            }
                            else
                            {
                                if (_currentFrameHas2011Header && _expected2011Length == -1)
                                    _expected2011Length = v;
                                else
                                    _numberBuffer.Add(v & 0xFF);
                            }
                        }
                        _currentNumber.Clear();
                    }
                }
            }
        }

        public void Close()
        {
            try
            {
                if (_port != null)
                {
                    _port.DataReceived -= Port_DataReceived;
                    if (_port.IsOpen) _port.Close();
                    Log("[INFO] Port geschlossen");
                }
            }
            catch { }
        }

        public void Dispose()
        {
            try { Close(); } catch { }
            try { _port?.Dispose(); } catch { }
        }

        // Utilities
        private static byte[] ParseHex(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return new byte[0];
            hex = hex.Replace(" ", string.Empty).Replace("-", string.Empty);
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                hex = hex.Substring(2);
            int len = hex.Length;
            if (len % 2 != 0) throw new ArgumentException("Hex string length must be even.");
            var data = new byte[len / 2];
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return data;
        }

        private static bool LooksLikeHex(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            var t = s.Trim();
            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t.Substring(2);
            // allow spaces or dashes as separators
            t = t.Replace(" ", string.Empty).Replace("-", string.Empty);
            if (t.Length < 2 || (t.Length % 2) != 0) return false;
            for (int i = 0; i < t.Length; i++)
            {
                char c = t[i];
                bool isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!isHex) return false;
            }
            return true;
        }

        private static string Unescape(string s)
        {
            return s?.Replace("\\r", "\r").Replace("\\n", "\n") ?? string.Empty;
        }

        private static string Sanitize(string s)
        {
            if (s == null) return string.Empty;
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                if (char.IsControl(ch) && ch != '\r' && ch != '\n')
                    sb.Append('.');
                else
                    sb.Append(ch);
            }
            return sb.ToString();
        }

        private bool ShouldAppendTerminator(string template, string term)
        {
            if (string.IsNullOrEmpty(term)) return false;
            if (string.IsNullOrEmpty(template)) return true;
            // Wenn das Template bereits mit Zeilenende endet, nichts anhängen
            if (template.EndsWith("\n") || template.EndsWith("\r")) return false;
            // Viele Geräte nutzen '$' als Terminator -> optional trotzdem Terminator anhängen
            if (template.EndsWith("$")) return AppendTerminatorAfterDollar;
            return true;
        }

        public void SendTemplate(string template)
        {
            if (!IsOpen) Open();
            try
            {
                if (Mode == CoinFeederProtocolMode.HEX || (Mode == CoinFeederProtocolMode.Auto && LooksLikeHex(template)))
                {
                    var bytes = ParseHex(template);
                    _port.Write(bytes, 0, bytes.Length);
                    Log($"TX [{DateTime.Now:HH:mm:ss}] HEX: {BitConverter.ToString(bytes).Replace('-', ' ')}");
                }
                else
                {
                    string term = Unescape(Terminator);
                    var msg = template;
                    if (ShouldAppendTerminator(template, term)) msg += term;
                    // WRITE EXACTLY msg (do not append extra CR/LF). Terminator must be LF only.
                    _port.Write(msg);
                    var msgBytes = Encoding.ASCII.GetBytes(msg);

                    Log($"TX [{DateTime.Now:HH:mm:ss}] ASCII: {Sanitize(msg)}");
                    Log($"TX [{DateTime.Now:HH:mm:ss}] ASCII HEX: {BitConverter.ToString(msgBytes).Replace('-', ' ')}");
                }
            }
            catch (Exception ex)
            {
                Log($"[ERR] TX: {ex.Message}");
                throw;
            }
        }

        // Convenience methods for LEDs using provided templates
        public void SendLed1Red(string tpl) => SendTemplate(tpl);
        public void SendLed1Green(string tpl) => SendTemplate(tpl);
        public void SendLed1Off(string tpl) => SendTemplate(tpl);
        public void SendLed2Red(string tpl) => SendTemplate(tpl);
        public void SendLed2Green(string tpl) => SendTemplate(tpl);
        public void SendLed2Off(string tpl) => SendTemplate(tpl);

        private void Log(string msg)
        {
            try { EventLog?.Invoke(msg); } catch { }
        }

        // Heuristic to detect NV200 status frames so they are not treated as NFC tokens.
        private bool IsLikelyNv200StatusReport(int[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return false;
            // Common observed pattern: 100$0$0$0$1$0 (length >=6)
            if (bytes.Length >= 6 && bytes[0] == 100 && bytes[1] == 0 && bytes[2] == 0 && bytes[3] == 0 && bytes[4] == 1)
                return true;

            // Another heuristic: if all values are small (<= 200) and first token is 1 or 10 or 100 and sequence contains many zeros,
            // treat as status telemetry rather than NFC UID.
            if (bytes.Length >= 6)
            {
                int zeroCount = 0;
                foreach (var b in bytes) if (b == 0) zeroCount++;
                if (zeroCount >= bytes.Length - 2 && (bytes[0] == 1 || bytes[0] == 10 || bytes[0] == 100))
                    return true;
            }

            return false;
        }

        // === RM5 status token suppression (angepasst: Endbyte 0x00 ODER 0x01) ===
        // RM5 (cctalk) kann kurze, sich wiederholende Statusframes schicken, die wir nicht als NFC behandeln wollen.
        private static bool IsLikelyRm5StatusToken(byte[] bytes)
        {
            if (bytes == null) return false;
            if (bytes.Length != 6 && bytes.Length != 7) return false;
            if (bytes[0] != 0x64) return false; // leading 100 decimal
            if (!(bytes[1] == 0x01 && (bytes[2] == 0x00 || bytes[2] == 0x01))) return false;
            byte last = bytes[bytes.Length - 1];
            if (!(last == 0x00 || last == 0x01)) return false;

            // Mehrheit der Mittelbytes sehr klein (0x00/0x01) -> reduziert False Positives
            int middleSmall = 0;
            for (int i = 1; i < bytes.Length - 1; i++)
                if (bytes[i] == 0x00 || bytes[i] == 0x01) middleSmall++;
            return middleSmall >= (bytes.Length - 2) / 2; // majority small
        }

        // Zentral: Ignorierliste laden und prüfen
        private void LoadIgnoredNfcTokens()
        {
            try
            {
                _ignoredNfcTokens.Clear();
                var v = IniHelper.ReadValue("Device", "IgnoredNfcTokens", AppSettings.IniPath);
                if (!string.IsNullOrWhiteSpace(v))
                {
                    var parts = v.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                                 .Select(p => p.Trim())
                                 .Where(p => p.Length > 0)
                                 .Distinct(StringComparer.OrdinalIgnoreCase)
                                 .ToList();
                    if (parts.Count > 0)
                    {
                        _ignoredNfcTokens.AddRange(parts);
                    }
                }
                // Historischer Default sicherstellen
                if (!_ignoredNfcTokens.Any(t => string.Equals(t, "640001000100", StringComparison.OrdinalIgnoreCase)))
                {
                    _ignoredNfcTokens.Add("640001000100");
                }
                try { EventLog?.Invoke("CoinFeeder: Ignored NFC tokens geladen: " + string.Join(", ", _ignoredNfcTokens.ToArray())); } catch { }
            }
            catch { }
        }

        private bool IsIgnoredNfcToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;
            try
            {
                foreach (var t in _ignoredNfcTokens)
                {
                    if (string.Equals(token, t, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch { }
            return false;
        }
    }
}
