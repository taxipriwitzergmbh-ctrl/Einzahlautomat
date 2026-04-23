using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text;
using System.Threading;

namespace TaMi_Einzahlautomat.Coins
{
    public class Rm5CctalkValidator : ICoinValidator
    {
        // File logging additions
        private readonly object _fileLogLock = new object();
        private string _logFilePath;
        private void InitFileLog()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_logFilePath))
                {
                    var baseDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SuE-Software", "SuE-TaMi Client SQL", "Logs");
                    System.IO.Directory.CreateDirectory(baseDir);
                    _logFilePath = System.IO.Path.Combine(baseDir, "rm5_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log");
                }
                // Nur LF als Zeilenende
                System.IO.File.AppendAllText(_logFilePath, "==== RM5 Session Start " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ====\n");
            }
            catch { }
        }

        #region Public Properties / Interface
        public string ComPort { get; set; } = "COM6";
        public int SspAddress { get; set; } = 2; // RM5 (Sorter / Validator) Standard-Adresse
        public bool Connected { get; private set; }
        #endregion

        #region Events
        public event Action<int> CoinAccepted;                 // Wert in Cent
        public event Action<string> EventLog;                  // Logausgaben
        public event Action<int[]> CoinLevelsUpdated;          // Aktueller Bestand (Index {1,2,5,10,20,50,100,200})
        public event Action<int> CoinDispensedDeltaCent;       // Jede best�tigte ausgezahlte M�nze (Wert in Cent)
        public event Action CoinDispenseComplete;              // Alle Hopper ohne Rest
        public event Action<int,int> CoinPayoutError;          // (hopperIdx, remaining) � Abort wie in VB, Bestand nicht reduziert
        #endregion

        #region Konstanten / Mapping
        private static readonly int[] NominalIndexValues = { 1, 2, 5, 10, 20, 50, 100, 200 };
        private const byte HostAddr = 1; // Master

        // ccTalk Commands
        private const byte CMD_READ_BUFFERED_CREDIT = 229;  // Read buffered credit events
        private const byte CMD_MODIFY_INHIBIT = 228;        // Master inhibit on/off
        private const byte CMD_SET_INHIBIT_MASK = 231;      // Accepted channels mask
        private const byte CMD_SET_SORTER_PATH = 210;       // (hier als "Request sorter path" gem�� Alt-Log, nur 1 Byte: Coin)
        private const byte CMD_REQ_SORTER_PATH = 209;       // (hier ebenfalls Abfrage � Reihenfolge aus Alt-Log 210 dann 209)
        private const byte CMD_SIMPLE_POLL = 254;
        private const byte CMD_REQ_SERIAL = 242;            // Hopper Seriennummer anfordern
        private const byte CMD_HOPPER_DISPENSE = 167;       // Hopper dispense
        private const byte CMD_HOPPER_POLL = 168;           // Hopper counter poll
        private const byte CMD_HOPPER_ENABLE = 218;         // (aus Alt-Implementierung � Hopper aktivieren / PIN senden Variante)
        private const byte CMD_HOPPER_PIN = 164;            // (vermutlich PIN / Security Command � Reihenfolge 218 -> 164)
        private const byte CMD_DEVICE_RESET = 1; // VB: Reset header = 1

        private const int MAX_HOPPERS = 5; // 10c,20c,50c,100c,200c
        #endregion

        #region INI / Persistenz
        private const string IniSection = "RM5";
        private const string IniKeyLevels = "LevelsV2"; // Version
        private string _iniPath => AppSettings.IniPath;
        private DateTime _lastSaveUtc = DateTime.MinValue;
        private const int MinSaveIntervalMs = 2500;
        #endregion

        #region Interne Strukturen
        private class HopperState
        {
            public int Address;
            public byte[] Serial = new byte[3];
            public bool SerialValid;
            public int PaidLast = -1;
            public int PaidTotal = -1;
            public int ToPayout = 0;
            public int RequestWithoutChange = 0;
            public bool AwaitingPoll = false;
            public DateTime LastPollUtc = DateTime.MinValue;
            public bool DispenseQueued = false;
            public byte LastSentHeader = 0; // F�r Parser Entscheidung Serial/Poll (jetzt tats�chliche gesendete Frames)
            public bool ExpectSerial = false; // Nach CMD_REQ_SERIAL
            public int SerialRetry = 0;
            // NEU: Verz�gerten Post-Dispense Poll
            public bool PostDispensePollPending = false;
            public DateTime PostDispensePollDueUtc = DateTime.MinValue;
        }

        private readonly HopperState[] _hoppers = new HopperState[MAX_HOPPERS];
        private readonly int[] _coinLevels = { -1, -1, -1, 0, 0, 0, 0, 0 }; // Indizes 3..7 relevant
        private readonly object _levelsLock = new object();

        // Anpassung: wir erlauben optional Kanal 6 (10ct). Default Maske jetzt 6 Kan�le (0x3F)
        private const byte InhibitMaskLowDefault = 0x3F; // Bits 1..6 -> Kan�le 1..6
        private const byte InhibitMaskHighDefault = 0x00;

        // Default Kanalzuordnung (Index = Kanalnummer)
        // Neu: Channel 1 = 10ct (vorher 0). Channel 6 deaktiviert (0) um doppelte 10ct-Zuordnung zu vermeiden.
        // Format: index0 unused, ch1=10, ch2=200, ch3=50, ch4=20, ch5=100, ch6=0
        // Adjust channel mapping to VB logic: 1=200c,2=50c,3=20c,4=100c,5=10c (updated after field test where ch4 was 1�)
        private int[] _validatorChannelCent = { 0, 200, 50, 20, 100, 10 }; // (fallback) channel index -> cent
        private readonly int[] _codeValueMap = { 0, 200, 100, 50, 20, 10, 5, 2, 1 }; // event code 1..8 -> cent (VB mapping)

        // Sorter Paths (nur f�r Logging / sp�tere Nutzung)
        // Index: coin number (1..5)
        // coin1 = 2� , coin2 = 1� , coin3 = 50c , coin4 = 20c , coin5 = 10c
        // Migration Hinweis: fr�here Defaults waren coin1->3, coin3->2; jetzt getauscht (coin1->2, coin3->3)
        // Aktuelle Default-Zuordnung nach Feldtests: 2� Pfad 2, 1� Pfad 5, 50c Pfad 3, 20c Pfad 4, 10c Pfad 6
        private byte[] _sorterPaths = { 0, 2, 5, 3, 4, 6 }; // coin1->2, coin2->5, coin3->3, coin4->4, coin5->6
        // Verification runtime data
        private readonly byte[] _actualSorterPaths = new byte[6]; // last confirmed path from device (via 210 response)
        private readonly int[] _sorterPathSetAttempts = new int[6];
        private const int MaxSorterPathRetries = 3;
        private readonly int[] _coinNumberToCent = { 0, 200, 100, 50, 20, 10 }; // coin index -> cent

        private readonly HashSet<int> _processedCreditEvents = new HashSet<int>();
        private readonly HashSet<int> _creditCodes = new HashSet<int> { 1, 2, 3, 4, 5 };
        private int _wrapEpoch = 0; // NEU: z�hlt EventCounter Wraps (255->0)
        private const int MaxProcessedKeys = 4096; // Begrenzung
        private int _lastProcessedEventCounter = -1; // bleibt erhalten (war schon vorhanden, hier konsolidiert)

        private SerialPort _sp;
        private Thread _thread;
        private volatile bool _stop;

        // Timings
        private const int LoopSleepMs = 40;
        private const int CreditPollIntervalMs = 180;
        private const int SimplePollIntervalMs = 1000;
        private const int HopperPollIntervalMs = 300;
        private const int HopperPollReplyTimeoutMs = 1500;
        private const int HopperNoChangeWarn = 15;
        private const int HopperNoChangeAbort = 40;
        private const int HopperCounterMax = 0x1000000;
        private const int PostDispensePollDelayMs = 160; // NEU: Wartezeit bis erster Poll nach DISPENSE

        private DateTime _lastCreditPollUtc = DateTime.MinValue;
        private DateTime _lastSimplePollUtc = DateTime.MinValue;

        private readonly Queue<(byte dest, byte header, byte[] data)> _sendQueue = new Queue<(byte, byte, byte[])>();
        private readonly object _queueLock = new object();
        private readonly object _sendLock = new object();

        // Entfernt doppelte Deklaration _lastProcessedEventCounter (war hier nochmals vorhanden)
        private volatile bool _initializing = false; // W�hrend Startup-Sequenz keine zyklischen Polls
        #endregion

        #region �ffentliche API
        public void SetChannelValue(int channel, int cent)
        {
            if (channel < 1) return;
            if (channel >= _validatorChannelCent.Length)
                Array.Resize(ref _validatorChannelCent, channel + 1);
            _validatorChannelCent[channel] = cent;
            Log($"Channel {channel} -> {cent}ct");
        }

        // Suppress in-app UI notifications (BusyAnimation popups) while a modal flow is active
        public bool SuppressUiNotifications { get; set; }
        public void AddCreditCode(int code)
        { if (code >= 0 && code < 256 && _creditCodes.Add(code)) Log($"CreditCode {code} hinzugef�gt"); }
        public int[] GetCoinAvailability()
        { lock (_levelsLock) { var c = new int[_coinLevels.Length]; Array.Copy(_coinLevels, c, c.Length); return c; } }
        public void RequestCoinLevels() { try { CoinLevelsUpdated?.Invoke(GetCoinAvailability()); } catch { } }

        // NEU: Alle M�nzbest�nde dauerhaft auf 0 setzen (Kassensturz endg�ltig)
        public void ResetAllCoinLevelsToZero(bool persist = true)
        {
            lock (_levelsLock)
            {
                for (int i = 0; i < _coinLevels.Length; i++) _coinLevels[i] = 0;
            }
            try { CoinLevelsUpdated?.Invoke(GetCoinAvailability()); } catch { }
            if (persist) { try { PersistLevels(); } catch { } }
            Log("Alle RM5 M�nzlevel auf 0 gesetzt (ResetAllCoinLevelsToZero)");
        }

        // NEU: kompletten Satz Level setzen (f�r Revert beim Kassensturz-Dialog)
        public void SetAllCoinLevels(int[] levels, bool persist = true)
        {
            if (levels == null || levels.Length < _coinLevels.Length) return;
            lock (_levelsLock)
            {
                for (int i = 0; i < _coinLevels.Length && i < levels.Length; i++)
                    _coinLevels[i] = Math.Max(0, levels[i]);
            }
            try { CoinLevelsUpdated?.Invoke(GetCoinAvailability()); } catch { }
            if (persist) { try { PersistLevels(); } catch { } }
            Log("RM5 Level �bernommen (SetAllCoinLevels)");
        }

        private void PersistLevels()
        {
            try
            {
                var sb = new StringBuilder();
                lock (_levelsLock)
                {
                    for (int i = 0; i < _coinLevels.Length; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append(_coinLevels[i]);
                    }
                }
                IniHelper.WriteValue(IniSection, IniKeyLevels, sb.ToString(), _iniPath);
            }
            catch { }
        }

        private void RestoreLevels()
        {
            try
            {
                string line = IniHelper.ReadValue(IniSection, IniKeyLevels, _iniPath);
                if (string.IsNullOrWhiteSpace(line)) return;
                var parts = line.Split(',');
                lock (_levelsLock)
                {
                    for (int i = 0; i < _coinLevels.Length && i < parts.Length; i++)
                    {
                        if (int.TryParse(parts[i], out var v) && v >= 0) _coinLevels[i] = v;
                    }
                }
            }
            catch { }
        }
        #endregion

        #region Connect / Disconnect
        public void Connect()
        {
            if (Connected) return;
            try
            {
                InitFileLog();
                InitHoppersDefault();
                RestoreLevels();
                LoadHopperSettings();
                LoadChannelMappingFromIni(); // NEU: Mapping laden

                _sp = new SerialPort(ComPort, 9600, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = 200,
                    WriteTimeout = 200,
                    Handshake = Handshake.None
                };
                _sp.Open();
                Connected = true;
                Log($"Port er�ffnet ({ComPort}) ValidatorAddr={SspAddress}");

                BuildStartupQueue(); // exakte vorgegebene Reihenfolge
                _initializing = true;

                _stop = false;
                _thread = new Thread(CommLoop) { IsBackground = true, Name = "RM5-ccTalk" };
                _thread.Start();
                try { CoinLevelsUpdated?.Invoke(GetCoinAvailability()); } catch { }
            }
            catch (Exception ex)
            {
                Log("Connect Fehler: " + ex.Message);
                try { _sp?.Dispose(); } catch { }
                _sp = null; Connected = false;
            }
        }

        public void Disconnect()
        {
            _stop = true;
            try { _thread?.Join(400); } catch { }
            try { PersistLevels(); } catch { }
            try { _sp?.Close(); } catch { }
            try { _sp?.Dispose(); } catch { }
            _sp = null; lock (_queueLock) _sendQueue.Clear(); Connected = false; Log("Getrennt");
        }
        public void Dispose() { try { Disconnect(); } catch { } }
        #endregion

        #region Startup Queue Aufbau
        private void BuildStartupQueue()
        {
            foreach (var h in _hoppers)
            {
                if (h.Address <= 0) continue;
                Enqueue(h.Address, CMD_HOPPER_ENABLE, new byte[] { (byte)'0', (byte)'0', (byte)'0', (byte)'0' });
                Enqueue(h.Address, CMD_HOPPER_PIN, new byte[] { 0xA5 }); // 165 dez
                Enqueue(h.Address, CMD_REQ_SERIAL, null);
            }
            Enqueue(SspAddress, CMD_DEVICE_RESET, null);
            Enqueue(SspAddress, CMD_MODIFY_INHIBIT, new byte[] { 0x01 });
            for (byte coin = 1; coin <= 5; coin++)
            {
                byte path = (coin < _sorterPaths.Length) ? _sorterPaths[coin] : (byte)2;
                Log($"Set SorterPath coin={coin} (nominal={(coin==1?200:(coin==2?100:(coin==3?50:(coin==4?20:10))))}ct) -> path={path}");
                Enqueue(SspAddress, CMD_SET_SORTER_PATH, new byte[] { coin, path });
                Log($"Req SorterPath coin={coin}");
                Enqueue(SspAddress, CMD_REQ_SORTER_PATH, new byte[] { coin });
            }
            Enqueue(SspAddress, CMD_SET_INHIBIT_MASK, new byte[] { 0x1F, 0x00 });
            Enqueue(SspAddress, CMD_READ_BUFFERED_CREDIT, null);
        }
        #endregion

        #region Enable / Disable
        public void Enable(bool enable)
        {
            if (!Connected) return;
            if (enable)
            {
                Enqueue(SspAddress, CMD_MODIFY_INHIBIT, new byte[] { 0x01 });
                Enqueue(SspAddress, CMD_SET_INHIBIT_MASK, new byte[] { InhibitMaskLowDefault, InhibitMaskHighDefault });
            }
            else
            {
                Enqueue(SspAddress, CMD_SET_INHIBIT_MASK, new byte[] { 0x00, 0x00 });
            }
        }
        #endregion

        #region Auszahlung
        public void PayoutCoins(int[] countsByIndex)
        {
            if (countsByIndex == null || countsByIndex.Length < 8) return;
            try { if (!SuppressUiNotifications) BusyAnimationManager.Begin("Münzauszahlung läuft"); } catch { }
            var lv = GetCoinAvailability();
            var sb = new StringBuilder(); sb.Append("PayoutCoins Request=[");
            for (int i = 0; i < 8; i++) { if (i > 0) sb.Append(','); sb.Append(countsByIndex[i]); }
            sb.Append("] Levels=["); for (int i = 0; i < 8; i++) { if (i > 0) sb.Append(','); sb.Append(lv[i]); } sb.Append("]"); Log(sb.ToString());

            for (int h = 0; h < MAX_HOPPERS; h++)
            {
                int idx = 3 + h; int want = countsByIndex[idx]; if (want <= 0) continue;
                lock (_levelsLock)
                {
                    int current = _coinLevels[idx]; if (current < 0) continue; if (current < want) want = current; if (want <= 0) continue;
                }
                _hoppers[h].ToPayout += want; _hoppers[h].DispenseQueued = false;
                if (_hoppers[h].SerialValid)
                {
                    QueueHopperDispense(h);
                }
                else
                {
                    Log($"Hopper {h} Serial fehlt -> erneut anfordern für Auszahlung");
                    Enqueue(_hoppers[h].Address, CMD_REQ_SERIAL, null);
                }
            }
        }
        private void QueueHopperDispense(int h)
        {
            var hs = _hoppers[h]; if (hs.ToPayout <= 0) return; if (!hs.SerialValid) { Log($"Hopper {h} Dispense verschoben � Serial fehlt"); return; } if (hs.DispenseQueued) return;
            int remaining = hs.ToPayout;
            while (remaining > 0)
            { byte take = (byte)Math.Min(255, remaining); byte[] data = { hs.Serial[0], hs.Serial[1], hs.Serial[2], take }; Enqueue(hs.Address, CMD_HOPPER_DISPENSE, data); Log($"Hopper {h} DISPENSE addr={hs.Address} take={take} remainingAfter={remaining - take}"); remaining -= take; }
            // Sofortigen Poll NICHT mehr direkt senden -> verz�gert planen
            hs.PostDispensePollPending = true;
            hs.PostDispensePollDueUtc = DateTime.UtcNow.AddMilliseconds(PostDispensePollDelayMs);
            hs.DispenseQueued = true;
            _hoppers[h] = hs;
        }
        #endregion

        #region Thread Loop
        private void CommLoop()
        {
            var readBuffer = new byte[512];
            while (!_stop)
            {
                try
                {
                    if (_sp == null || !_sp.IsOpen) { Thread.Sleep(200); continue; }

                    (byte dest, byte header, byte[] data) item = default;
                    lock (_queueLock) { if (_sendQueue.Count > 0) item = _sendQueue.Dequeue(); }
                    if (item.dest != 0)
                    {
                        SendFrame(item.dest, item.header, item.data); Thread.Sleep(25); Drain(readBuffer);
                    }
                    else
                    {
                        if (_initializing) _initializing = false;
                        if (!_initializing)
                        {
                            var now = DateTime.UtcNow;

                            // Hopper Poll Timeout / Retry
                            for (int h = 0; h < MAX_HOPPERS; h++)
                            {
                                var hs = _hoppers[h]; if (hs.Address <= 0) continue;
                                if (hs.AwaitingPoll && (now - hs.LastPollUtc).TotalMilliseconds > HopperPollReplyTimeoutMs)
                                {
                                    hs.AwaitingPoll = false; // retry freigeben
                                    Log($"Hopper {h} Poll timeout � retry");
                                    _hoppers[h] = hs;
                                }
                            }

                            if (_sendQueue.Count == 0 && (now - _lastCreditPollUtc).TotalMilliseconds >= CreditPollIntervalMs)
                            { Enqueue(SspAddress, CMD_READ_BUFFERED_CREDIT, null); _lastCreditPollUtc = now; }
                            if (_sendQueue.Count == 0 && (now - _lastSimplePollUtc).TotalMilliseconds >= SimplePollIntervalMs)
                            { Enqueue(SspAddress, CMD_SIMPLE_POLL, null); _lastSimplePollUtc = now; }

                            // Verz�gerten Post-Dispense Poll ausf�hren
                            if (_sendQueue.Count == 0)
                            {
                                for (int h = 0; h < MAX_HOPPERS; h++)
                                {
                                    var hs = _hoppers[h]; if (hs.Address <= 0) continue; if (!hs.SerialValid) continue;
                                    if (hs.PostDispensePollPending && DateTime.UtcNow >= hs.PostDispensePollDueUtc && !hs.AwaitingPoll)
                                    {
                                        Enqueue(hs.Address, CMD_HOPPER_POLL, null);
                                        hs.PostDispensePollPending = false; hs.AwaitingPoll = true; hs.LastPollUtc = DateTime.UtcNow;
                                        _hoppers[h] = hs; break; // nur einen pro Schleife einreihen
                                    }
                                }
                            }

                            // Hopper polls (regul�r)
                            if (_sendQueue.Count == 0)
                            {
                                for (int h = 0; h < MAX_HOPPERS; h++)
                                {
                                    var hs = _hoppers[h]; if (hs.Address <= 0) continue; bool payoutActive = hs.ToPayout > 0; bool needBaseline = (hs.PaidLast < 0 && payoutActive);
                                    if (!hs.SerialValid) continue;
                                    var now2 = DateTime.UtcNow;
                                    if ((needBaseline || payoutActive) && !hs.AwaitingPoll && !hs.PostDispensePollPending && (now2 - hs.LastPollUtc).TotalMilliseconds >= HopperPollIntervalMs)
                                    { Enqueue(hs.Address, CMD_HOPPER_POLL, null); hs.LastPollUtc = now2; hs.AwaitingPoll = true; _hoppers[h] = hs; break; }
                                }
                            }
                        }
                        Drain(readBuffer); Thread.Sleep(LoopSleepMs);
                    }
                }
                catch (Exception ex) { Log("CommLoop Fehler: " + ex.Message); }
            }
        }
        #endregion

        #region Senden / Empfang
        private void Enqueue(int dest, byte header, byte[] data)
        {
            lock (_queueLock) _sendQueue.Enqueue(((byte)dest, header, data ?? Array.Empty<byte>()));
        }
        private void SendFrame(int dest, byte header, byte[] data)
        {
            try
            {
                lock (_sendLock)
                {
                    if (_sp == null || !_sp.IsOpen) return;
                    data = data ?? Array.Empty<byte>(); byte len = (byte)data.Length; byte[] frame = new byte[5 + len];
                    frame[0] = (byte)dest; frame[1] = len; frame[2] = HostAddr; frame[3] = header;
                    for (int i = 0; i < len; i++) frame[4 + i] = data[i];
                    int sum = 0; for (int i = 0; i < frame.Length - 1; i++) sum += frame[i];
                    frame[frame.Length - 1] = (byte)((0x100 - (sum & 0xFF)) & 0xFF);
                    _sp.Write(frame, 0, frame.Length); LogFrame("S", frame);

                    int hIdx = GetHopperIndexByAddress(dest);
                    if (hIdx >= 0)
                    {
                        var hs = _hoppers[hIdx];
                        hs.LastSentHeader = header;
                        if (header == CMD_REQ_SERIAL)
                        {
                            hs.ExpectSerial = true; hs.AwaitingPoll = false;
                        }
                        else if (header == CMD_HOPPER_POLL)
                        {
                            hs.AwaitingPoll = true; hs.LastPollUtc = DateTime.UtcNow;
                        }
                        _hoppers[hIdx] = hs;
                    }
                }
            }
            catch (Exception ex) { Log("Send Fehler: " + ex.Message); }
        }
        private void Drain(byte[] tmpBuf)
        {
            if (_sp == null) return;
            try
            {
                int avail = _sp.BytesToRead; if (avail <= 0) return; if (avail > tmpBuf.Length) avail = tmpBuf.Length;
                int r = _sp.Read(tmpBuf, 0, avail); if (r <= 0) return; int i = 0;
                while (i + 4 < r)
                { byte len = tmpBuf[i + 1]; int chk = i + 4 + len; if (chk >= r) break; int fl = chk - i + 1; var frame = new byte[fl]; Array.Copy(tmpBuf, i, frame, 0, fl); LogFrame("R", frame); if (ValidateChecksum(frame)) ParseFrame(frame); i += fl; }
            }
            catch (TimeoutException) { }
            catch (Exception ex) { Log("Drain Fehler: " + ex.Message); }
        }
        #endregion

        #region Frame Parsing
        private void ParseFrame(byte[] frame)
        {
            if (frame.Length < 5) return; byte len = frame[1]; byte src = frame[2]; byte header = frame[3];

            // Sorter path SET response: header=210, len=2, data: coin, path
            if (header == CMD_SET_SORTER_PATH && len == 2 && frame.Length >= 7)
            {
                byte coin = frame[4]; byte path = frame[5];
                if (coin >= 1 && coin <= 5)
                {
                    _actualSorterPaths[coin] = path;
                    byte expected = (coin < _sorterPaths.Length) ? _sorterPaths[coin] : (byte)0;
                    if (expected == path)
                    {
                        Log($"SorterPath ok coin={coin} path={path}");
                    }
                    else
                    {
                        int att = ++_sorterPathSetAttempts[coin];
                        Log($"SorterPath MISMATCH coin={coin} got={path} expected={expected} attempt={att}/{MaxSorterPathRetries}");
                        if (att <= MaxSorterPathRetries)
                        {
                            // Retry: send again set + request
                            Enqueue(SspAddress, CMD_SET_SORTER_PATH, new byte[] { coin, expected });
                            Enqueue(SspAddress, CMD_REQ_SORTER_PATH, new byte[] { coin });
                        }
                        else
                        {
                            Log($"SorterPath coin={coin} giving up after {att} attempts");
                        }
                    }
                }
                return; // handled
            }
            // Sorter path REQUEST response (header 209) � some firmware may echo coin only (len=1); optional extra logging
            if (header == CMD_REQ_SORTER_PATH && len == 1 && frame.Length >= 6)
            {
                byte coin = frame[4];
                if (coin >= 1 && coin <= 5)
                {
                    byte actual = _actualSorterPaths[coin];
                    byte expected = (coin < _sorterPaths.Length) ? _sorterPaths[coin] : (byte)0;
                    if (actual != 0 && actual == expected)
                        Log($"SorterPath confirm coin={coin} path={actual}");
                    else if (actual != 0)
                        Log($"SorterPath confirm coin={coin} path={actual} (expected {expected})");
                }
                // continue (no return) to allow other parsing if needed
            }

            int hopperIdx = GetHopperIndexByAddress(src);
            if (hopperIdx >= 0 && header == 0 && len == 3)
            {
                var hs = _hoppers[hopperIdx];
                // Entscheidung ob Serial oder Poll anhand Erwartung / LastSentHeader (jetzt korrekt, weil erst beim Senden gesetzt)
                bool isSerialCandidate = hs.ExpectSerial && !hs.SerialValid && hs.LastSentHeader == CMD_REQ_SERIAL;
                bool isPollCandidate = hs.AwaitingPoll && hs.LastSentHeader == CMD_HOPPER_POLL;

                if (isSerialCandidate)
                {
                    if (frame.Length >= 8)
                    {
                        hs.Serial[0] = frame[4]; hs.Serial[1] = frame[5]; hs.Serial[2] = frame[6];
                        // Treat FF FF FF (and 00 00 00) as valid placeholder serials to avoid blocking
                        bool isAllZero = (hs.Serial[0] == 0 && hs.Serial[1] == 0 && hs.Serial[2] == 0);
                        bool isAllFF = (hs.Serial[0] == 0xFF && hs.Serial[1] == 0xFF && hs.Serial[2] == 0xFF);
                        if (isAllZero || isAllFF)
                        {
                            hs.SerialValid = true; hs.ExpectSerial = false; hs.SerialRetry = 0;
                            Log($"Hopper {hopperIdx} Platzhalter-Serial empfangen ({(isAllFF ? "FF FF FF" : "00 00 00")}) � akzeptiert");
                            // Proceed with baseline poll
                            Enqueue(hs.Address, CMD_HOPPER_POLL, null);
                            // Start pending payout if any
                            if (hs.ToPayout > 0 && !hs.DispenseQueued)
                            {
                                Log($"Hopper {hopperIdx} QueueHopperDispense nach Platzhalter-Serial (pending ToPayout={hs.ToPayout})");
                                _hoppers[hopperIdx] = hs; // store before queuing
                                QueueHopperDispense(hopperIdx);
                                hs = _hoppers[hopperIdx];
                            }
                        }
                        else
                        {
                            hs.SerialValid = true; hs.ExpectSerial = false; hs.SerialRetry = 0;
                            Log($"Hopper {hopperIdx} Serial={hs.Serial[0]}-{hs.Serial[1]}-{hs.Serial[2]}");
                            // Nach g�ltiger Serial Baseline-POLL einreihen (Fix: jetzt erster Poll wirklich nach Serial)
                            Enqueue(hs.Address, CMD_HOPPER_POLL, null);
                            // NEU: Falls vor Serial bereits ForcePayoutRaw / PayoutCoins ToPayout gesetzt hat -> jetzt Auszahlung starten
                            if (hs.ToPayout > 0 && !hs.DispenseQueued)
                            {
                                Log($"Hopper {hopperIdx} QueueHopperDispense nach Serial (pending ToPayout={hs.ToPayout})");
                                _hoppers[hopperIdx] = hs; // erst speichern
                                QueueHopperDispense(hopperIdx);
                                hs = _hoppers[hopperIdx]; // zur�cklesen
                            }
                        }
                        _hoppers[hopperIdx] = hs;
                    }
                    return;
                }
                if (isPollCandidate)
                {
                    hs.AwaitingPoll = false; _hoppers[hopperIdx] = hs; if (frame.Length >= 8) ProcessHopperPoll(hopperIdx, frame); return;
                }
                // Fallback: Wenn neither noch � ignorieren
            }
            else if (header == 0 && len > 3)
            { ParseCreditEvents(frame); return; }
        }
        private void ProcessHopperPoll(int hopperIdx, byte[] frame)
        {
            var hs = _hoppers[hopperIdx];
            int b0 = frame[4];
            int b1 = frame[5];
            int b2 = frame[6];
            int total = b0 + 256 * b1 + 256 * 256 * b2;
            int last = hs.PaidLast;

            if (last < 0)
            {
                hs.PaidLast = total;
                hs.PaidTotal = total;
                hs.RequestWithoutChange = 0;
                Log($"Hopper {hopperIdx} BaselineCounter={total}");
                _hoppers[hopperIdx] = hs;
                return;
            }

            int diff = total - last;

            // Rollover pr�fen (24-bit Counter)
            if (diff < 0 && total < last)
            {
                int rollover = (HopperCounterMax - last) + total;
                if (rollover > 0 && rollover < 20000)
                {
                    diff = rollover;
                    Log($"Hopper {hopperIdx} ROLLOVER diff={diff}");
                }
                else
                {
                    // Baseline-Reset Heuristik: starker R�cksprung aber kein echter Rollover -> wie VB ignorieren & Baseline neu
                    int jump = last - total;
                    if (jump > 100 && jump < 1000000)
                    {
                        Log($"Hopper {hopperIdx} Counter sprang r�ckw�rts (last={last} now={total} jump={jump}) � Baseline neu ohne Fortschritt");
                        hs.PaidLast = total;
                        hs.PaidTotal = total;
                        hs.RequestWithoutChange = 0; // nicht als Stillstand werten
                        _hoppers[hopperIdx] = hs;
                        return;
                    }
                    // Sonst diff bleibt negativ (kein Fortschritt) -> wie kein diff behandeln
                    diff = 0;
                }
            }

            // Baselines aktualisieren (auch wenn diff==0, damit k�nftige Spr�nge korrekt bewertet werden)
            hs.PaidLast = total;
            hs.PaidTotal = total;

            if (diff > 0)
            {
                // Realer Fortschritt
                hs.RequestWithoutChange = 0;
                int coinIndex = 3 + hopperIdx; // Mapping zu NominalIndexValues (10c beginnt bei Index 3)
                if (coinIndex >= 0 && coinIndex < NominalIndexValues.Length)
                {
                    int valueCent = NominalIndexValues[coinIndex];
                    int effective = diff;
                    if (hs.ToPayout > 0 && effective > hs.ToPayout)
                        effective = hs.ToPayout; // Sicherheit: nicht mehr abbuchen als angefordert

                    if (effective > 0)
                    {
                        // Bestand wie im VB-Modell nur bei best�tigter Auszahlung reduzieren
                        lock (_levelsLock)
                        {
                            if (_coinLevels[coinIndex] < 0) _coinLevels[coinIndex] = 0;
                            _coinLevels[coinIndex] = Math.Max(0, _coinLevels[coinIndex] - effective);
                        }
                        try { CoinLevelsUpdated?.Invoke(GetCoinAvailability()); } catch { }

                        for (int k = 0; k < effective; k++)
                        {
                            try { CoinDispensedDeltaCent?.Invoke(valueCent); } catch { }
                        }

                        hs.ToPayout = Math.Max(0, hs.ToPayout - effective);
                        if (hs.ToPayout == 0)
                        {
                            Log($"Hopper {hopperIdx} Auszahlung abgeschlossen");
                            hs.DispenseQueued = false;
                        }
                        SaveLevelsThrottled();
                    }
                }
            }
            else
            {
                // Kein Fortschritt � nur z�hlen wenn noch Coins angefordert sind
                if (hs.ToPayout > 0)
                {
                    hs.RequestWithoutChange++;
                    if (hs.RequestWithoutChange == HopperNoChangeWarn)
                        Log($"WARN Hopper {hopperIdx}: {HopperNoChangeWarn} Polls ohne Fortschritt (remaining={hs.ToPayout})");

                    if (hs.RequestWithoutChange >= HopperNoChangeAbort)
                    {
                        int remaining = hs.ToPayout;
                        Log($"ABORT Hopper {hopperIdx}: keine �nderung nach {hs.RequestWithoutChange} Polls � Rest {remaining} verworfen (VB-Verhalten)");
                        hs.ToPayout = 0; // Rest NICHT abbuchen / Bestand unver�ndert
                        hs.DispenseQueued = false;
                        hs.RequestWithoutChange = 0;
                        try { if (!SuppressUiNotifications) BusyAnimationManager.End("RM5 Fehler"); } catch { }
                        try { CoinPayoutError?.Invoke(hopperIdx, remaining); } catch { }
                    }
                }
            }

            _hoppers[hopperIdx] = hs;
            CheckDispenseComplete();
        }
        private void ParseCreditEvents(byte[] frame)
        {
            if (frame.Length < 5) return;
            byte eventCounter = frame[4];
            int len = frame[1];
            int bytesPairs = len - 1;
            if (bytesPairs < 2) return;
            int events = bytesPairs / 2;

            if (_lastProcessedEventCounter < 0)
            {
                _lastProcessedEventCounter = eventCounter;
                Log($"Init EC={eventCounter}");
                return;
            }

            // Wrap-Erkennung: alter hoch (>=200), neuer klein (<=15) -> plausibler 255->0 �bergang
            if (eventCounter <= 15 && _lastProcessedEventCounter >= 200)
            {
                _wrapEpoch++;
                _processedCreditEvents.Clear(); // Duplikat-Cache leeren, damit nach Wrap alles wieder gez�hlt wird
                Log($"EC wrap erkannt -> epoch={_wrapEpoch} (alt={_lastProcessedEventCounter} neu={eventCounter})");
            }

            for (int i = 0; i < events; i++)
            {
                int code = frame[5 + i * 2];
                int channel = frame[5 + i * 2 + 1];
                if (channel < 0) channel = 0;

                int derivedEc = (eventCounter - i) & 0xFF; // r�ckw�rts abz�hlen wie Ger�t liefert

                // Eindeutiger Key unter Einbeug Epoch + Counter + Code + Channel (untere 4 Bit)
                int key = (_wrapEpoch << 20) | (derivedEc << 12) | ((code & 0xFF) << 4) | (channel & 0x0F);
                if (_processedCreditEvents.Contains(key))
                    continue; // echtes Duplikat innerhalb derselben Epoche
                _processedCreditEvents.Add(key);
                if (_processedCreditEvents.Count > MaxProcessedKeys)
                    _processedCreditEvents.Clear(); // Speicher begrenzen / alte Events freigeben

                int cent = 0;
                if (code >= 1 && code < _codeValueMap.Length)
                    cent = _codeValueMap[code];
                if (cent == 0 && channel > 0 && channel < _validatorChannelCent.Length)
                    cent = _validatorChannelCent[channel];

                if (_creditCodes.Contains(code))
                {
                    if (cent > 0)
                    {
                        try { CoinAccepted?.Invoke(cent); } catch { }
                        AddCoinToLevels(cent);
                        // kompakteres Log
                        Log($"Credit epoch={_wrapEpoch} ec={derivedEc} code={code} ch={channel} {cent}ct");
                    }
                    else
                    {
                        Log($"Credit unmapped code={code} ch={channel} ec={derivedEc}");
                    }
                }
                // sonst: ignorierte Codes nicht loggen um Spam zu vermeiden
            }

            _lastProcessedEventCounter = eventCounter;
        }
        #endregion

        #region Levels & Speicherung
        private void AddCoinToLevels(int cent)
        {
            int idx = NominalIndexOf(cent); if (idx < 0) return;
            lock (_levelsLock)
            {
                if (_coinLevels[idx] < 0) _coinLevels[idx] = 0;
                _coinLevels[idx]++;
            }
            try { CoinLevelsUpdated?.Invoke(GetCoinAvailability()); } catch { }
            // Persist immediately to avoid missing last change when no further events occur
            try { PersistLevels(); } catch { }
        }
        private void SaveLevelsThrottled()
        {
            var now = DateTime.UtcNow;
            if ((now - _lastSaveUtc).TotalMilliseconds < MinSaveIntervalMs) return;
            _lastSaveUtc = now;
            try { PersistLevels(); } catch { }
        }
        #endregion

        #region Helper / Mapping / Logging
        private int NominalIndexOf(int cent) { for (int i = 0; i < NominalIndexValues.Length; i++) if (NominalIndexValues[i] == cent) return i; return -1; }
        private int GetHopperIndexByAddress(int addr) { for (int i = 0; i < MAX_HOPPERS; i++) if (_hoppers[i].Address == addr) return i; return -1; }
        private bool ValidateChecksum(byte[] frame) { int sum = 0; for (int i = 0; i < frame.Length; i++) sum += frame[i]; return (sum & 0xFF) == 0; }
        private void Log(string s) { try { EventLog?.Invoke("[RM5] " + s); } catch { } try { if (!string.IsNullOrEmpty(_logFilePath)) { lock (_fileLogLock) System.IO.File.AppendAllText(_logFilePath, "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + s + "\n"); } } catch { } }
        private void LogFrame(string dir, byte[] fr) { if (fr == null) return; var sb = new StringBuilder(); sb.Append(dir).Append(": "); foreach (var b in fr) sb.Append(b).Append(' '); Log(sb.ToString().TrimEnd()); }
        private void CheckDispenseComplete()
        {
            for (int h = 0; h < MAX_HOPPERS; h++) if (_hoppers[h].ToPayout > 0) return;
            try { if (!SuppressUiNotifications) BusyAnimationManager.End("RM5 fertig"); } catch { }
            try { PersistLevels(); } catch { }
            try { CoinDispenseComplete?.Invoke(); } catch { }
        }
        private void InitHoppersDefault() { int[] defaultAddr = { 3, 7, 4, 5, 6 }; for (int i = 0; i < MAX_HOPPERS; i++) _hoppers[i] = new HopperState { Address = defaultAddr[i] }; }
        private void LoadHopperSettings()
        {
            try
            {
                string addrRaw = IniHelper.ReadValue("Hoppersettings", "Adresse", _iniPath);
                if (!string.IsNullOrWhiteSpace(addrRaw))
                {
                    var parts = addrRaw.Replace(',', ';').Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= MAX_HOPPERS)
                        for (int i = 0; i < MAX_HOPPERS; i++) if (int.TryParse(parts[i].Trim(), out var a) && a > 0) _hoppers[i].Address = a;
                }
                string sorterRaw = IniHelper.ReadValue("Hoppersettings", "SorterPaths", _iniPath);
                if (!string.IsNullOrWhiteSpace(sorterRaw))
                {
                    var parts = sorterRaw.Replace(',', ';').Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 5)
                    {
                        var tmp = new byte[6]; tmp[0] = 0; bool ok = true;
                        for (int i = 1; i <= 5; i++) if (!byte.TryParse(parts[i - 1].Trim(), out tmp[i])) { ok = false; break; }
                        if (ok) { _sorterPaths = tmp; }
                    }
                }
                // Migration: alte Default-Kombination (coin1=3, coin3=2) automatisch auf neue (coin1=2, coin3=3) anpassen
                if (_sorterPaths.Length >= 6 && _sorterPaths[1] == 3 && _sorterPaths[3] == 2)
                {
                    byte old1 = _sorterPaths[1]; byte old3 = _sorterPaths[3];
                    _sorterPaths[1] = 2; _sorterPaths[3] = 3;
                    Log($"SorterPath Migration: coin1 {old1}->2, coin3 {old3}->3 (auto)");
                    try { IniHelper.WriteValue("Hoppersettings", "SorterPaths", string.Join(",", _sorterPaths[1], _sorterPaths[2], _sorterPaths[3], _sorterPaths[4], _sorterPaths[5]), _iniPath); } catch { }
                }
                Log($"HopperSettings: Adr={string.Join("|", Array.ConvertAll(_hoppers, h => h.Address.ToString()))} Sorter={string.Join("|", _sorterPaths)}");
            }
            catch (Exception ex) { Log("HopperSettings Fehler: " + ex.Message); }
        }
        private void LoadChannelMappingFromIni()
        {
            try
            {
                string raw = IniHelper.ReadValue("RM5", "ChannelValues", _iniPath);
                if (string.IsNullOrWhiteSpace(raw)) return;
                var parts = raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) return;
                var list = new List<int>();
                for (int i = 0; i < parts.Length; i++)
                {
                    if (int.TryParse(parts[i].Trim(), out var v) && v >= 0) list.Add(v); else list.Add(0);
                }
                if (list.Count >= 2) _validatorChannelCent = list.ToArray();
                Log($"ChannelMapping geladen: {string.Join("|", _validatorChannelCent)}");
            }
            catch (Exception ex) { Log("ChannelMapping Fehler: " + ex.Message); }
        }
        #endregion

        private byte CalcInhibitLowMask()
        {
            int maxChannel = (_validatorChannelCent?.Length ?? 0) - 1;
            if (maxChannel >= 6) return 0x3F;
            if (maxChannel >= 5) return 0x1F;
            byte mask = 0;
            for (int ch = 1; ch <= maxChannel; ch++) mask |= (byte)(1 << (ch - 1));
            return mask;
        }

        public void AcceptAllChannels() { if (!Connected) return; Enqueue(SspAddress, CMD_SET_INHIBIT_MASK, new byte[] { CalcInhibitLowMask(), InhibitMaskHighDefault }); }
        public void ForceEnableAll() { if (!Connected) return; Enqueue(SspAddress, CMD_MODIFY_INHIBIT, new byte[] { 0x01 }); AcceptAllChannels(); }

        public void ForcePayoutRaw(int[] countsByIndex)
        {
            if (countsByIndex == null || countsByIndex.Length < 8) return;
            try { if (!SuppressUiNotifications) BusyAnimationManager.Begin("Münzauszahlung läuft"); } catch { }
            Log("ForcePayoutRaw gestartet: " + string.Join(",", countsByIndex));
            for (int h = 0; h < MAX_HOPPERS; h++)
            {
                int idx = 3 + h; // Hopper beginnt bei 10ct Index 3
                int want = countsByIndex[idx];
                if (want <= 0) continue;
                // IGNORIERT interne _coinLevels (kann 0 sein obwohl physisch M�nzen vorhanden)
                _hoppers[h].ToPayout += want;
                _hoppers[h].DispenseQueued = false; // neu planen
                if (_hoppers[h].SerialValid)
                {
                    QueueHopperDispense(h);
                }
                else
                {
                    Log($"ForcePayoutRaw: Hopper {h} Serial fehlt -> anfordern");
                    Enqueue(_hoppers[h].Address, CMD_REQ_SERIAL, null);
                }
            }
        }
    }
}