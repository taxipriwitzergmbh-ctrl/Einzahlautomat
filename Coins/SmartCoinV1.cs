using System;
using System.Collections.Generic;
using System.Threading;

namespace TaMi_Einzahlautomat.Coins
{
    // Port der SmartCoinSystem.vb Kernlogik (SSP/ITLlib). Benötigt ITLlib-Referenz im Projekt.
    public class SmartCoinV1 : ICoinValidator
    {
        // Instanzbasierter Throttle für Level-Requests je Gerät (verhindert, dass Coin/2 durch Coin/1 blockiert wird)
        private readonly object _instanceLevelsLock = new object();
        private DateTime _lastInstanceLevelsRequestUtc = DateTime.MinValue;
        private static bool _kassensturzMode = false;
        private static readonly object _globalModeLock = new object();
        private static int _kassensturzCooldownMs = 5000; // 5s Block nach jeder Abfrage bei Kassensturz

        public string ComPort { get; set; } = "COM3";
        public int SspAddress { get; set; } = 16;
        public bool Connected { get; private set; }

        public event Action<int> CoinAccepted;
        public event Action<string> EventLog;

        // Event bei aktualisierten Münzständen
        public event Action<int[]> CoinLevelsUpdated;

        // Events für Münzauszahlungen
        public event Action CoinDispenseComplete;
        public event Action<string> CoinPayoutError;

        // NEU: Fortschritts-Event je tatsächlich ausgegebener Münze (Wert in Cent)
        public event Action<int> CoinDispensedDeltaCent;

        // NEU: Statusänderung (Idle, Neustart, Busy, Disabled, Jammed, ...)
        public event Action<string> StatusChanged;
        public string CurrentStatus { get { return _status; } }
        private string _status = "Unbekannt";

        private Thread _thread;
        private volatile bool _stop;
        private volatile bool _threadEnded = true;

        private readonly List<List<byte>> _sendQueue = new List<List<byte>>();
        private readonly object _queueLock = new object(); // Thread-Schutz

        private ITLlib.SSP_COMMAND_INFO _cmdInfo = new ITLlib.SSP_COMMAND_INFO();
        private ITLlib.SSP_COMMAND _cmd = new ITLlib.SSP_COMMAND();
        private ITLlib.SSPComms _comms = new ITLlib.SSPComms();
        private ITLlib.SSP_KEYS _keys = new ITLlib.SSP_KEYS();
        private bool _encryptionOk = false;

        private double _lastDispensedCount = 0;
        private int _toPay_1 = 0, _toPay_2 = 0, _toPay_5 = 0, _toPay_10 = 0, _toPay_20 = 0, _toPay_50 = 0, _toPay_100 = 0, _toPay_200 = 0;

        // Enable-Puffer, damit Enable nach Handshake automatisch angewandt wird
        private volatile bool _wantEnabled = false;

        // Vorr�tige M�nzen (Index 0..7 = {1,2,5,10,20,50,100,200} Cent). -1 = unbekannt
        private readonly int[] _coinLevels = new int[8] { -1, -1, -1, -1, -1, -1, -1, -1 };
        private readonly object _levelsLock = new object();

        // Flag: falls verschlüsselt nicht akzeptiert -> unverschlüsselt probieren
        private volatile bool _retryGetDenomUnenc = false;
        private DateTime _lastCoinCreditUtc = DateTime.MinValue; // Timestamp letzter M�nze f�r Level-Abfrage Verz�gerung

        // TEMP: Raw-Debug aktiv (bei Bedarf wieder auf false setzen)
        private bool _debugRawPoll = true; // TEMP: Raw-Debug aktiv (bei Bedarf wieder auf false setzen)
        private string HexSlice(int start, int count)
        {
            try
            {
                int len = Math.Min(count, _cmd.ResponseDataLength - start);
                if (len <= 0) return "";
                byte[] tmp = new byte[len];
                for (int k = 0; k < len; k++) tmp[k] = _cmd.ResponseData[start + k];
                return Hex(tmp, tmp.Length);
            }
            catch { return ""; }
        }

        // NEU: Zeitstempel der letzten verarbeiteten POLL-Antwort für faire Interleaving-Strategie
        private DateTime _lastPollUtc = DateTime.MinValue;
        // Minimalintervall zwischen zwei Polls auch bei fuller Queue (ms)
        private const int MaxPollGapMs = 150; // enger takten, damit einzelne M�nzen erfasst werden
        private volatile bool _requestLevelsOnNextPoll = false; // trigger from external events

        // NEU: Tracking SmartEmpty
        private volatile bool _smartEmptyInProgress = false;
        private DateTime _lastSmartEmptyActivityUtc = DateTime.MinValue; // aktualisiert bei Dispensing / Credits
        private int _smartEmptyRetryCount = 0;
        private int _smartEmptyInitialLevelSum = -1;
        private const int SmartEmptyWatchdogMs = 7000; // 7s ohne Aktivit�t => Retry
        private const int SmartEmptyMaxRetries = 2;
        private volatile bool _suspendLevelRequests = false; // während SmartEmpty keine Level-Kommandos einreihen

        // NEU: Einzahlungs-Animation Tracking
        private volatile bool _coinDepositAnimActive = false;
        private DateTime _lastDepositAnimCoinUtc = DateTime.MinValue;

        // Feld: letztes Payout-Cmd für Retry nach Re-Sync
        private List<byte> _pendingPayoutCmd;

        // NEU: Deduplizierung/Status für DEVICE_FULL (Code 207)
        private DateTime _lastDeviceFullLogUtc = DateTime.MinValue;
        private int _deviceFullRepeatCount = 0;

        private DateTime _lastDeviceFullMailUtc = DateTime.MinValue;

        public void Connect()
        {
            if (Connected) return;
            _stop = false;
            _thread = new Thread(CommLoop) { IsBackground = true, Name = "SmartCoinV1-Comm" };
            _thread.Start();
            Connected = true;
            SetStatus("Verbinde...");
            Log("Thread gestartet");
        }

        public void Disconnect()
        {
            _stop = true;
            try
            {
                if (_thread != null)
                {
                    // Bis zu ~4s (SSP Timeout 3000ms) in kleinen Intervallen warten, damit laufende IO sauber endet
                    var start = DateTime.UtcNow;
                    while (!_threadEnded && (DateTime.UtcNow - start).TotalMilliseconds < 4000)
                    {
                        if (_thread.Join(250)) break;
                    }
                    // Letzter Join-Versuch (falls Thread inzwischen fertig)
                    try { _thread.Join(100); } catch { }
                }
            }
            catch { }
            try { SafeClose(); } catch { }
            Connected = false;
            SetStatus("Getrennt");
            Log("Getrennt (Disconnect abgeschlossen)");
        }

        public void Enable(bool enable)
        {
            if (!Connected) return;

            try { BusyAnimationManager.EndForce(); } catch { }
            _wantEnabled = enable; // Wunsch merken

            if (enable)
            {
                if (_encryptionOk) EnqueueEnableSequence();
                else Log("Enable vorgemerkt (wird nach Handshake ausgef�hrt)");
            }
            else
            {
                if (_encryptionOk) EnqueueDisableSequence();
                else Log("Disable vorgemerkt (wird nach Handshake ausgef�hrt)");
            }
        }

        public void Dispose()
        {
            try { Disconnect(); } catch { }
        }

        private void CommLoop()
        {
            _threadEnded = false;
            while (!_stop)
            {
                try
                {
                    // Init Session
                    _cmd.RetryLevel = 2;
                    _cmd.Timeout = 3000;
                    _cmd.SSPAddress = (byte)SspAddress; // Cast auf byte zwingend!
                    _cmd.ComPort = ComPort;
                    _cmd.EncryptionStatus = false;

                    if (_comms.OpenSSPComPort(_cmd))
                    {
                        _encryptionOk = false;
                        Log($"Connected (Port={_cmd.ComPort}, Addr={_cmd.SSPAddress}, Enc={_cmd.EncryptionStatus})");
                        SetStatus("Verbunden");
                        try
                        {
                            // Einige Ger�te ben�tigen nach Neustart einen expliziten RESET, wie im KassensystemPRO.
                            // Sende vor dem Handshake einmal RESET, danach wie gewohnt SYNC/Key-Exchange.
                            Enqueue((byte)0, (byte)1, (byte)CCommands.SSP_CMD_RESET);
                            Log("RESET enqueued (Startup)");
                            SetStatus("Reset...");
                        }
                        catch { }
                        Sync(); // unverschl�sselt

                        while (!_stop)
                        {
                            // SmartEmpty Watchdog pr�fen
                            if (_smartEmptyInProgress)
                            {
                                if (_lastSmartEmptyActivityUtc != DateTime.MinValue)
                                {
                                    var idleMs = (int)(DateTime.UtcNow - _lastSmartEmptyActivityUtc).TotalMilliseconds;
                                    if (idleMs > SmartEmptyWatchdogMs && _smartEmptyRetryCount < SmartEmptyMaxRetries)
                                    {
                                        int currentSum = CurrentLevelSum();
                                        if (currentSum > 0)
                                        {
                                            Log("SmartEmpty Watchdog: keine Aktivit�t seit " + idleMs + "ms, LevelsSum=" + currentSum + ", Retry " + (_smartEmptyRetryCount + 1));
                                            _smartEmptyRetryCount++;
                                            // Queue leeren und SmartEmpty erneut schicken
                                            ClearQueue();
                                            Enqueue(1, 1, (byte)CCommands.SSP_CMD_SMART_EMPTY);
                                            _lastSmartEmptyActivityUtc = DateTime.UtcNow; // reset
                                        }
                                    }
                                    else if (idleMs > SmartEmptyWatchdogMs && _smartEmptyRetryCount >= SmartEmptyMaxRetries)
                                    {
                                        int remain = CurrentLevelSum();
                                        if (remain > 0)
                                        {
                                            Log("SmartEmpty Watchdog: Abbruch nach max. Retries, verbleibende M�nzen (Summe)=" + remain);
                                            _smartEmptyInProgress = false;
                                            _suspendLevelRequests = false;
                                            Enable(true);
                                            try { CoinPayoutError?.Invoke("SmartEmpty unvollst�ndig � bitte erneut versuchen."); } catch { }
                                            ScheduleLevelsRequest();
                                        }
                                    }
                                }
                            }

                            // Einzahlungsanimation ggf. automatisch beenden (kein Coin seit >2.5s & keine Auszahlung aktiv)
                            try
                            {
                                if (_coinDepositAnimActive && !_smartEmptyInProgress && (DateTime.UtcNow - _lastDepositAnimCoinUtc).TotalMilliseconds > 2500)
                                {
                                    _coinDepositAnimActive = false;
                                    try { BusyAnimationManager.End("deposit coins done"); } catch { }
                                }
                            }
                            catch { }

                            // If a level request is scheduled (e.g., on movement), enqueue once (nur nach Handshake)
                            if (_requestLevelsOnNextPoll && !_suspendLevelRequests && _encryptionOk)
                            {
                                try
                                {
                                    AlignPayoutLevels(false);
                                }
                                catch { }
                                finally { _requestLevelsOnNextPoll = false; }
                            }

                            // Interleaving-Strategie wie in KassensystemPRO: wenn nichts ansteht -> einfach einen POLL einreihen
                            if (!HasQueuedItems())
                            {
                                Poll();
                            }
                            else
                            {
                                // Wenn l�nger kein Poll verarbeitet wurde, schiebe einen Poll vorn ein (ohne Pause)
                                if ((DateTime.UtcNow - _lastPollUtc).TotalMilliseconds > MaxPollGapMs)
                                {
                                    EnqueueFront((byte)0, (byte)1, (byte)CCommands.SSP_CMD_POLL);
                                }
                            }

                            var elem = Dequeue();
                            if (elem.Count <= 2)
                            {
                                Log("Command zu kurz");
                                continue;
                            }

                            _cmd.EncryptionStatus = elem[0] == (byte)1;
                            _cmd.CommandDataLength = elem[1];

                            for (int n = 0; n < elem[1]; n++)
                                _cmd.CommandData[n] = elem[n + 2];

                            bool ok = _comms.SSPSendCommand(_cmd, _cmdInfo);
                            if (!ok)
                            {
                                Log("Send Command verlief false");
                                ClearQueue();
                                _encryptionOk = false;
                                SafeClose();
                                SetStatus("Verbindung unterbrochen – Neuaufbau");
                                try { BusyAnimationManager.End("abort comm"); } catch { }
                                break; // re-open
                            }
                            else
                            {
                                if (_cmd.ResponseDataLength < 1)
                                {
                                    Log("Response is shorter than 1 Byte");
                                }
                                else
                                {
                                    byte rsp = _cmd.ResponseData[0];
                                    byte sentCmd = _cmd.CommandData[0];

                                    if (rsp != CCommands.SSP_RESPONSE_OK)
                                    {
                                        if (rsp == CCommands.SSP_RESPONSE_KEY_NOT_SET)
                                        {
                                            Log($"Resp not OK ({rsp}) for cmd=0x{sentCmd:X2} -> Re-Sync");
                                            ClearQueue();
                                            _encryptionOk = false;
                                            SetStatus("Handshake neu");
                                            Sync();
                                        }
                                        else
                                        {
                                            Log($"Resp not OK ({rsp}) for cmd=0x{sentCmd:X2}");
                                            if (sentCmd == (byte)CCommands.SSP_CMD_GET_DENOMINATION_LEVEL && !_retryGetDenomUnenc)
                                            {
                                                _retryGetDenomUnenc = true;
                                                Log("Retry GET_DENOMINATION_LEVEL unverschlüsselt...");
                                                AlignPayoutLevels(false);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        try { HandleResponse(elem); }
                                        catch (Exception exi) { Log("HandleResponse: " + exi.Message); }
                                    }

                                    // Letzten Poll-Zeitstempel pflegen
                                    if (sentCmd == (byte)CCommands.SSP_CMD_POLL)
                                    {
                                        _lastPollUtc = DateTime.UtcNow;
                                    }
                                }
                            }

                            if (elem[2] == (byte)CCommands.SSP_CMD_POLL)
                            {
                                // Wie KassensystemPRO: bei Idle 100ms, sonst 25ms
                                var st = _status ?? string.Empty;
                                bool idle = st.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0
                                            || st.IndexOf("bereit", StringComparison.OrdinalIgnoreCase) >= 0;
                                Thread.Sleep(idle ? 100 : 25);
                            }
                            else
                            {
                                Thread.Sleep(25);
                            }
                        }
                    }
                    else
                    {
                        Log($"Open SSPComPort ({_cmd.ComPort}) failed");
                        SetStatus("Port nicht verfügbar");
                        try { BusyAnimationManager.End("abort port"); } catch { }
                    }

                    if (_stop) break;
                    Thread.Sleep(1000);
                }
                catch (ObjectDisposedException ode)
                {
                    Log(ode.Message);
                }
                catch (Exception ex)
                {
                    Log(ex.Message);
                }
            }

            _threadEnded = true;
        }

        // Helfer f�rs Hex-Loggen
        private string Hex(byte[] data, int len)
        {
            if (data == null || len <= 0) return "";
            len = Math.Min(len, data.Length);
            char[] c = new char[len * 3 - 1];
            int idx = 0;
            for (int i = 0; i < len; i++)
            {
                byte b = data[i];
                c[idx++] = GetHex(((b >> 4) & 0xF));
                c[idx++] = GetHex(b & 0xF);
                if (i < len - 1) c[idx++] = '-';
            }
            return new string(c);
        }
        private char GetHex(int v) => (char)(v < 10 ? '0' + v : 'A' + (v - 10));

        // NEU: Status setzen (nur bei �nderung)
        internal void SetStatus(string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            if (_status == s) return;
            _status = s;
            try { StatusChanged?.Invoke(s); } catch { }
            Log("Status: " + s);
        }

        // Request-Logging + korrektes Payload (Len=8: 1 cmd + 7 payload)
        private void RequestDenomLevel(int cent, bool encrypted)
        {
            var b = BitConverter.GetBytes(cent); // 4 Bytes LE
            byte enc = (byte)(encrypted ? 1 : 0);

            var elem = NewCmd(enc, (byte)8, (byte)CCommands.SSP_CMD_GET_DENOMINATION_LEVEL);
            elem.Add(b[0]); elem.Add(b[1]); elem.Add(b[2]); elem.Add(b[3]);
            elem.Add((byte)'E'); elem.Add((byte)'U'); elem.Add((byte)'R');

            lock (_queueLock) { _sendQueue.Add(elem); }
            Log($"REQ Level {cent} ct {(encrypted ? "[enc]" : "[plain]")}");
        }

        // Map coin raw value (possibly 32-bit) to valid denomination or -1
        private int MapCoinValue(int raw)
        {
            switch (raw)
            {
                case 1: case 2: case 5: case 10: case 20: case 50: case 100: case 200: return raw;
            }
            int low = raw & 0xFF;
            switch (low)
            {
                case 1: case 2: case 5: case 10: case 20: case 50: case 100: case 200: return low;
            }
            return -1;
        }

        // Antwort-Parsing
        private void HandleResponse(List<byte> sent)
        {
            byte cmd = _cmd.CommandData[0];

            if (cmd == (byte)CCommands.SSP_CMD_SYNC)
            {
                Log("Synchronisiert");
                SetStatus("Synchronisiert");
                if (!_encryptionOk)
                {
                    _comms.InitiateSSPHostKeys(_keys, _cmd);
                    SetGenerator(_keys);
                    return;
                }
            }

            if (cmd == (byte)CCommands.SSP_CMD_SET_GENERATOR)
            {
                Log("Set Generator");
                SetStatus("Handshake...");
                SetModulus(_keys);
                return;
            }

            if (cmd == (byte)CCommands.SSP_CMD_SET_MODULUS)
            {
                Log("Set Modulus");
                SetStatus("Handshake...");
                DoKeyExchange(_keys);
                return;
            }

            if (cmd == (byte)CCommands.SSP_CMD_REQUEST_KEY_EXCHANGE)
            {
                var arr = new byte[8];
                for (int i = 0; i < 8; i++) arr[i] = _cmd.ResponseData[i + 1];
                _keys.SlaveInterKey = BitConverter.ToUInt64(arr, 0);
                _comms.CreateSSPHostEncryptionKey(_keys);
                _cmd.Key.FixedKey = 81985526925837671;
                _cmd.Key.VariableKey = _keys.KeyHost;
                Log("Key Request: Keys are set");

                // Setze Host-Protokoll-Version wie in KassensystemPRO (v7)
                try
                {
                    SetProtocolVersion(7);
                    Log("Host Protocol Version 7 requested");
                }
                catch { }

                _encryptionOk = true;
                SetStatus("Handshake OK");
                if (_wantEnabled) EnqueueEnableSequence();

                // NEU: einmaligen Retry eines zuvor abgewiesenen Payouts
                try
                {
                    List<byte> retry = null;
                    lock (_queueLock)
                    {
                        if (_pendingPayoutCmd != null)
                        {
                            retry = new List<byte>(_pendingPayoutCmd);
                            _pendingPayoutCmd = null; // nur einmal retry
                        }
                    }
                    if (retry != null)
                    {
                        lock (_queueLock) { _sendQueue.Insert(0, retry); }
                        Log("Retry Payout after re-sync enqueued (front).");
                    }
                }
                catch { }

                return;
            }

            // GET_DENOMINATION_LEVEL
            if (cmd == (byte)CCommands.SSP_CMD_GET_DENOMINATION_LEVEL)
            {
                try
                {
                    Log($"GET_DENOM resp len={_cmd.ResponseDataLength} data={Hex(_cmd.ResponseData, _cmd.ResponseDataLength)}");

                    int valueCent = 0;
                    if (sent != null && sent.Count >= 7)
                    {
                        valueCent = sent[3]
                                  | (sent[4] << 8)
                                  | (sent[5] << 16)
                                  | (sent[6] << 24);
                    }

                    int count = -1;
                    if (_cmd.ResponseDataLength >= 3)
                    {
                        count = _cmd.ResponseData[1] | (_cmd.ResponseData[2] << 8);
                    }
                    if (_cmd.ResponseDataLength >= 5)
                    {
                        int c32 = _cmd.ResponseData[1]
                                  | (_cmd.ResponseData[2] << 8)
                                  | (_cmd.ResponseData[3] << 16)
                                  | (_cmd.ResponseData[4] << 24);
                        if (c32 >= 0) count = c32;
                    }

                    int idx = IndexFromCent(valueCent);
                    if (idx >= 0 && count >= 0)
                    {
                        lock (_levelsLock) { _coinLevels[idx] = count; }
                        _retryGetDenomUnenc = false;
                        Log($"Level {valueCent} ct: {count}");

                        if (valueCent == 200)
                        {
                            CoinLevelsUpdatedSafe();
                            Log("CoinLevelsUpdated: " + FormatLevels(_coinLevels));
                        }
                    }
                    else
                    {
                        Log($"GET_DENOM parse unplausibel (val={valueCent}, count={count})");
                    }
                }
                catch (Exception ex)
                {
                    Log("Parse GET_DENOMINATION_LEVEL failed: " + ex.Message);
                }
                return;
            }

            if (cmd == (byte)CCommands.SSP_CMD_POLL)
            {
                if (_cmd.ResponseDataLength == 1)
                {
                    SetStatus("Idle");
                    return; // Idle
                }

                for (int i = 1; i < _cmd.ResponseDataLength; i++)
                {
                    byte code = _cmd.ResponseData[i];
                    switch (code)
                    {
                        case CCommands.SSP_POLL_SLAVE_RESET:
                            Log("Unit reset");
                            SetStatus("Neustart");
                            break;

                        case CCommands.SSP_POLL_DISABLED:
                            Log("Unit disabled");
                            SetStatus("Disabled");
                            if (_wantEnabled && _encryptionOk) EnqueueEnableSequence();
                            break;

                        case CCommands.SSP_POLL_COIN_CREDIT:
                            {
                                // Während Einwurf als Status melden (für Admin-UI)
                                SetStatus("Einwurf");
                                if (_debugRawPoll) Log("COIN_BLOCK(SHORT) RAW=" + HexSlice(i, 8));
                                if (i + 1 < _cmd.ResponseDataLength)
                                {
                                    int v = _cmd.ResponseData[i + 1];
                                    if (IsValidDenom(v)) { RaiseCoin(v); _lastCoinCreditUtc = DateTime.UtcNow; }
                                    else if (_debugRawPoll) Log("Ignored invalid short coin byte=" + v);
                                }
                                i += 7;
                                break;
                            }
                        case 0xC1:
                            {
                                // Extended header marker � actual coin credit follows in 0xBF fragment(s)
                                SetStatus("Einwurf");
                                if (_debugRawPoll) Log("COIN_EXT_HDR RAW=" + HexSlice(i, 10));
                                // WICHTIG: Hier NICHTs verbrauchen � 0xBF wird im n�chsten Switch-Zweig geparst
                                break;
                            }
                        case 191:
                            {
                                // KassensystemPRO-kompatibel: 0xBF trägt die Extended-Coin-Infos.
                                // Verwende Low-Byte f�r Einzelm�nzen; falls unplausibel, nimm v32 (aggregierter Cent-Wert).
                                if (_debugRawPoll) Log("FRAG_191 RAW=" + HexSlice(i, 9));

                                if (i + 2 < _cmd.ResponseDataLength)
                                {
                                    int low = _cmd.ResponseData[i + 2];
                                    int v32 = 0;
                                    if (i + 5 < _cmd.ResponseDataLength)
                                    {
                                        v32 = _cmd.ResponseData[i + 2]
                                              | (_cmd.ResponseData[i + 3] << 8)
                                              | (_cmd.ResponseData[i + 4] << 16)
                                              | (_cmd.ResponseData[i + 5] << 24);
                                    }

                                    if (IsValidDenom(low))
                                    {
                                        RaiseCoin(low);
                                        _lastCoinCreditUtc = DateTime.UtcNow;
                                    }
                                    else if (v32 > 0)
                                    {
                                        // Aggregierte Summe (z. B. 150, 250, 300 ct) – als ein Credit verbuchen
                                        RaiseCoin(v32);
                                        _lastCoinCreditUtc = DateTime.UtcNow;
                                    }
                                    else if (_debugRawPoll)
                                    {
                                        Log($"FRAG_191 Low-Byte unplausibel: {low}");
                                    }
                                }
                                // Blockl�nge: BF + 8 Datenbytes (1 Count + 4 Value + 3 'EUR')
                                // For-Loop erh�ht zus�tzlich um +1 => hier i += 8
                                i += 8;
                                break;
                            }

                        case CCommands.SSP_POLL_DISPENSING:
                            Log("Dispensing coin(s)");
                            SetStatus("Busy (Dispensing)");
                            _lastSmartEmptyActivityUtc = DateTime.UtcNow; // Aktivit�t
                            int disp = ReadInt32(i + 2);

                            // BUGFIX 2: Z�hler-Reset / Wrap erkennen (zweite Auszahlung hatte keine Einzel-Events)
                            if (disp < _lastDispensedCount)
                            {
                                Log("Dispense counter reset detected (DISPENSING) old=" + _lastDispensedCount + " new=" + disp + ". Reset baseline.");
                                _lastDispensedCount = 0; // Baseline neu setzen
                            }

                            int diff = (int)(disp - _lastDispensedCount);

                            if (diff > 0)
                            {
                                if (_smartEmptyInProgress)
                                {
                                    _lastDispensedCount = disp; // Nur fortschreiben
                                }
                                else
                                {
                                    if (!EmitPerCoinDeltas(diff))
                                    {
                                        try { CoinDispensedDeltaCent?.Invoke(diff); } catch { }
                                    }
                                    _lastDispensedCount = disp;
                                }
                            }

                            i += ((_cmd.ResponseData[i + 1] * 7) + 1);
                            break;

                        case CCommands.SSP_POLL_DISPENSED:
                            Log("Dispensed Coin(s)");
                            _lastSmartEmptyActivityUtc = DateTime.UtcNow; // Aktivit�t
                            try
                            {
                                int totalDisp = ReadInt32(i + 2);

                                // BUGFIX 2: Reset auch hier erkennen
                                if (totalDisp < _lastDispensedCount)
                                {
                                    Log("Dispense counter reset detected (DISPENSED) old=" + _lastDispensedCount + " new=" + totalDisp + ". Reset baseline.");
                                    _lastDispensedCount = 0;
                                }

                                int tail = (int)(totalDisp - _lastDispensedCount);
                                if (tail > 0)
                                {
                                    if (_smartEmptyInProgress)
                                    {
                                        _lastDispensedCount = totalDisp;
                                    }
                                    else
                                    {
                                        if (!EmitPerCoinDeltas(tail))
                                        {
                                            try { CoinDispensedDeltaCent?.Invoke(tail); } catch { }
                                        }
                                        _lastDispensedCount = totalDisp;
                                    }
                                }
                            }
                            catch { }

                            if (!_smartEmptyInProgress)
                            {
                                _toPay_1 = _toPay_2 = _toPay_5 = _toPay_10 = _toPay_20 = _toPay_50 = _toPay_100 = _toPay_200 = 0;
                                Enable(true);
                                SetStatus("Bereit");
                                try { CoinDispenseComplete?.Invoke(); } catch { }
                                try { BusyAnimationManager.End("Coins fertig"); } catch { }
                            }
                            i += ((_cmd.ResponseData[i + 1] * 7) + 1);
                            break;
                        case CCommands.SSP_POLL_ERROR_DURING_PAYOUT:
                        case CCommands.SSP_POLL_COIN_MECH_ERROR:
                        case CCommands.SSP_POLL_COIN_MECH_JAMMED:
                        case CCommands.SSP_POLL_HALTED:
                        case CCommands.SSP_POLL_INCOMPLETE_PAYOUT:
                            {
                                string errName;
                                switch (code)
                                {
                                    case CCommands.SSP_POLL_ERROR_DURING_PAYOUT: errName = "ERROR_DURING_PAYOUT"; break;
                                    case CCommands.SSP_POLL_COIN_MECH_ERROR: errName = "COIN_MECH_ERROR"; break;
                                    case CCommands.SSP_POLL_COIN_MECH_JAMMED: errName = "COIN_MECH_JAMMED"; break;
                                    case CCommands.SSP_POLL_HALTED: errName = "HALTED"; break;
                                    case CCommands.SSP_POLL_INCOMPLETE_PAYOUT: errName = "INCOMPLETE_PAYOUT"; break;
                                    default: errName = "UNBEKANNT"; break;
                                }
                                Log("Fehlercode: " + errName);
                                SetStatus("St�rung: " + errName);
                                _smartEmptyInProgress = false; _suspendLevelRequests = false; _smartEmptyRetryCount = 0; _smartEmptyInitialLevelSum = -1; _toPay_1 = _toPay_2 = _toPay_5 = _toPay_10 = _toPay_20 = _toPay_50 = _toPay_100 = _toPay_200 = 0;
                                // Animation VOR MessageBox schlie�en
                                try { BusyAnimationManager.End("Coins Fehler"); } catch { }
                                try { CoinPayoutError?.Invoke("Fehler / Abbruch: " + errName); } catch { }
                                Enable(true); if (_encryptionOk) ScheduleLevelsRequest();
                                break;
                            }

                        case CCommands.SSP_POLL_JAMMED:
                            Log("Unit jammed...");
                            SetStatus("St�rung (Jammed)");
                            _smartEmptyInProgress = false; _suspendLevelRequests = false;
                            try { BusyAnimationManager.End("Coins Jammed"); } catch { }
                            RequestCoinLevels();
                            i += ((_cmd.ResponseData[i + 1] * 7) + 1);
                            try { CoinPayoutError?.Invoke("M�nzger�t blockiert (JAMMED)."); } catch { }
                            break;

                        case CCommands.SSP_POLL_TIME_OUT:
                            Log("Timed out searching for a coin");
                            SetStatus("Timeout");
                            _smartEmptyInProgress = false; _suspendLevelRequests = false;
                            try { BusyAnimationManager.End("Coins Timeout"); } catch { }
                            i += ((_cmd.ResponseData[i + 1] * 7) + 1);
                            try { CoinPayoutError?.Invoke("M�nzauszahlung zeit�berschritten."); } catch { }
                            break;

                        case CCommands.SSP_POLL_DEVICE_FULL:
                            {
                                // SmartCoin meldet vollen Hopper/Stack � korrekt behandeln statt UNSUPPORTED
                                SetStatus("Maximaler Füllstand erreicht");
                                var now = DateTime.UtcNow;
                                if ((now - _lastDeviceFullLogUtc).TotalSeconds >= 5)
                                {
                                    Log("Device FULL (Maximaler Füllstand erreicht)");
                                    _lastDeviceFullLogUtc = now;
                                    _deviceFullRepeatCount = 0;
                                }
                                else
                                {
                                    _deviceFullRepeatCount++;
                                    if (_deviceFullRepeatCount % 10 == 0) Log("Device FULL besteht weiterhin...");
                                }
                                // Animationsabbruch � es k�nnen keine weiteren M�nzen angenommen werden
                                try { BusyAnimationManager.End("coins full"); } catch { }
                                // WICHTIG: Einige Geräte gehen bei FULL in einen deaktivierten Zustand über und
                                // verarbeiten danach keine Auszahlungsbefehle mehr, bis ENABLE erneut gesendet wurde.
                                // Damit Wechselgeld-Auszahlungen weiterhin funktionieren, explizit wieder aktivieren.
                                try { Enable(true); } catch { }
                                // Levels aktualisieren, damit UI den tatsächlichen Füllstand sieht
                                try { ScheduleLevelsRequest(); } catch { }
                                break;
                            }

                        // NEU: Zust�nde beim SmartEmpty
                        case CCommands.SSP_POLL_SMART_EMPTYING:
                        {
                            Log("SMART Emptying...");
                            SetStatus("Smart Emptying");
                            _lastSmartEmptyActivityUtc = DateTime.UtcNow; // Aktivit�t markieren -> Watchdog ruhigstellen
                            // Datenblock �berspringen wie bei anderen Events mit Payload
                            i += ((_cmd.ResponseData[i + 1] * 7) + 1);
                            break;
                        }

                        case CCommands.SSP_POLL_SMART_EMPTIED:
                        {
                            Log("SMART Emptied");
                            // SmartEmpty sauber beenden
                            _smartEmptyInProgress = false;
                            _suspendLevelRequests = false;
                            _smartEmptyRetryCount = 0;
                            _smartEmptyInitialLevelSum = -1;

                            // Ger�t wieder freigeben und Levels aktualisieren
                            Enable(true);
                            SetStatus("Bereit");
                            try { BusyAnimationManager.End("Coins fertig"); } catch { }
                            try { ScheduleLevelsRequest(); } catch { }

                            i += ((_cmd.ResponseData[i + 1] * 7) + 1);
                            break;
                        }

                        // Optional (einige Ger�te benutzen EMPTYING/EMPTIED anstelle SMART_*):
                        case CCommands.SSP_POLL_EMPTYING:
                        {
                            Log("Emptying...");
                            SetStatus("Emptying");
                            _lastSmartEmptyActivityUtc = DateTime.UtcNow;
                            break;
                        }

                        case CCommands.SSP_POLL_EMPTIED:
                        {
                            Log("Emptied");
                            _smartEmptyInProgress = false;
                            _suspendLevelRequests = false;
                            _smartEmptyRetryCount = 0;
                            _smartEmptyInitialLevelSum = -1;

                            Enable(true);
                            SetStatus("Bereit");
                            try { BusyAnimationManager.End("Coins fertig"); } catch { }
                            try { ScheduleLevelsRequest(); } catch { }
                            break;
                        }

                        default:
                            if (code == 207) { /* bereits separat behandelt */ }
                            else if (_debugRawPoll) Log("UNSUPPORTED code=" + code + " RAW=" + HexSlice(i, 10));
                            else Log("Unsupported poll response: " + code);
                            break;
                    }
                }
            }

            if (cmd == (byte)0xB5) // GET_ALL_LEVELS f�r SCS (selten)
            {
                try
                {
                    Log($"GET_ALL_LEVELS resp len={_cmd.ResponseDataLength} data={Hex(_cmd.ResponseData, _cmd.ResponseDataLength)}");
                    int p = 1;
                    bool any = false;

                    while (p + 6 < _cmd.ResponseDataLength)
                    {
                        int valueCent = _cmd.ResponseData[p]
                                        | (_cmd.ResponseData[p + 1] << 8)
                                        | (_cmd.ResponseData[p + 2] << 16)
                                        | (_cmd.ResponseData[p + 3] << 24);
                        p += 4;

                        if (p + 2 >= _cmd.ResponseDataLength) break;
                        byte c1 = _cmd.ResponseData[p], c2 = _cmd.ResponseData[p + 1], c3 = _cmd.ResponseData[p + 2];
                        p += 3;

                        int count = -1;
                        if (p + 1 < _cmd.ResponseDataLength)
                        {
                            count = _cmd.ResponseData[p] | (_cmd.ResponseData[p + 1] << 8);
                            p += 2;
                        }
                        else if (p + 3 < _cmd.ResponseDataLength)
                        {
                            int c32 = _cmd.ResponseData[p]
                                      | (_cmd.ResponseData[p + 1] << 8)
                                      | (_cmd.ResponseData[p + 2] << 16)
                                      | (_cmd.ResponseData[p + 3] << 24);
                            count = c32;
                            p += 4;
                        }
                        else
                        {
                            break;
                        }

                        int idx = IndexFromCent(valueCent);
                        if (idx >= 0 && count >= 0)
                        {
                            lock (_levelsLock) { _coinLevels[idx] = count; }
                            any = true;
                            Log($"ALL_LEVELS: {valueCent}ct ({(char)c1}{(char)c2}{(char)c3}) = {count}");
                        }
                        else
                        {
                            Log($"ALL_LEVELS: unbekannt/�bersprungen val={valueCent}, count={count}");
                        }
                    }

                    if (any)
                    {
                        _retryGetDenomUnenc = false;
                        CoinLevelsUpdatedSafe();
                    }
                    else
                    {
                        Log("GET_ALL_LEVELS enthielt keine verwertbaren Eintr�ge.");
                    }
                }
                catch (Exception ex)
                {
                    Log("Parse GET_ALL_LEVELS failed: " + ex.Message);
                }
                return;
            }
        }

        private bool IsValidDenom(int v)
        {
            switch (v)
            {
                case 1: case 2: case 5: case 10: case 20: case 50: case 100: case 200: return true;
                default: return false;
            }
        }

        public void ScheduleLevelsRequest()
        {
            if (_suspendLevelRequests) return; // während SmartEmpty unterdrücken
            _requestLevelsOnNextPoll = true;
        }

        // Zerlege eine aggregierte Dispense-Summe in Einzelm�nzen und feuere je M�nze ein Delta-Event
        private bool EmitPerCoinDeltas(int diff)
        {
            if (_smartEmptyInProgress) return false; // keine Einzel-Events w�hrend SmartEmpty
            if (diff <= 0) return false;
            int before = diff;

            Action<int> raise = (val) => { try { CoinDispensedDeltaCent?.Invoke(val); } catch { } };

            // Hilfs-Lokalfunktion: solange m�glich von einem Nominalwert abziehen
            void Consume(ref int remaining, ref int counter, int value)
            {
                while (remaining >= value && counter > 0)
                {
                    counter--; remaining -= value; raise(value);
                }
            }

            // Erst anhand der geplanten St�ckzahlen (_toPay_*) abbauen (gro� -> klein)
            Consume(ref diff, ref _toPay_200, 200);
            Consume(ref diff, ref _toPay_100, 100);
            Consume(ref diff, ref _toPay_50, 50);
            Consume(ref diff, ref _toPay_20, 20);
            Consume(ref diff, ref _toPay_10, 10);
            Consume(ref diff, ref _toPay_5, 5);
            Consume(ref diff, ref _toPay_2, 2);
            // In der Praxis kann vom Gerät gelegentlich eine ungerade Differenz (1ct) gemeldet werden
            // � obwohl 1ct regul�r nicht ausgezahlt wird. Um Kassen-/Guthaben-Anzeigen konsistent zu halten,
            // verbuchen wir auch diese 1ct als Delta-Event.
            Consume(ref diff, ref _toPay_1, 1);

            // Falls noch Rest bleibt (z. B. weil _toPay_* nicht exakt passten), greedy ohne Counter aufl�sen
            int[] vals = new[] { 200, 100, 50, 20, 10, 5, 2, 1 };
            foreach (var v in vals)
            {
                while (diff >= v)
                {
                    diff -= v; raise(v);
                }
            }

            // Am Ende sollte diff == 0 sein
            return before != 0;
        }

        // Reihenfolge: zuerst Global Inhibit, dann ENABLE/DISABLE
        private void EnqueueEnableSequence()
        {
            Enqueue((byte)1, (byte)2, (byte)CCommands.SSP_CMD_SET_COIN_MECH_GLOBAL_INHIBIT, (byte)1);
            Enqueue((byte)1, (byte)1, (byte)CCommands.SSP_CMD_ENABLE);
        }

        private void EnqueueDisableSequence()
        {
            Enqueue((byte)1, (byte)2, (byte)CCommands.SSP_CMD_SET_COIN_MECH_GLOBAL_INHIBIT, (byte)0);
            Enqueue((byte)1, (byte)1, (byte)CCommands.SSP_CMD_DISABLE);
        }

        private void RaiseCoin(int cent)
        {
            try { CoinAccepted?.Invoke(cent); } catch { }
            Log($"CoinAccepted {cent} ct");
            // Einzahlungsanimation starten/halten
            try
            {
                _lastDepositAnimCoinUtc = DateTime.UtcNow;
                if (!_coinDepositAnimActive)
                {
                    if (!BusyAnimationManager.IsActive)
                    {
                        BusyAnimationManager.Begin("Einzahlung l�uft");
                        _coinDepositAnimActive = true;
                    }
                }
            }
            catch { }
        }

        private int ReadInt32(int startIndex)
        {
            return _cmd.ResponseData[startIndex]
                   + (_cmd.ResponseData[startIndex + 1] << 8)
                   + (_cmd.ResponseData[startIndex + 2] << 16)
                   + (_cmd.ResponseData[startIndex + 3] << 24);
        }

        private void Poll()
        {
            Enqueue((byte)0, (byte)1, (byte)CCommands.SSP_CMD_POLL);
        }

        private void Sync()
        {
            Enqueue((byte)0, (byte)1, (byte)CCommands.SSP_CMD_SYNC);
        }

        private void SetGenerator(ITLlib.SSP_KEYS keys)
        {
            var arr = BitConverter.GetBytes(keys.Generator);
            var elem = NewCmd((byte)0, (byte)9, (byte)CCommands.SSP_CMD_SET_GENERATOR);
            for (int i = 0; i < 8; i++) elem.Add(arr[i]);
            lock (_queueLock) { _sendQueue.Insert(0, elem); } // Handshake priorisieren
        }

        private void SetModulus(ITLlib.SSP_KEYS keys)
        {
            var arr = BitConverter.GetBytes(keys.Modulus);
            var elem = NewCmd((byte)0, (byte)9, (byte)CCommands.SSP_CMD_SET_MODULUS);
            for (int i = 0; i < 8; i++) elem.Add(arr[i]);
            lock (_queueLock) { _sendQueue.Insert(0, elem); } // Handshake priorisieren
        }

        private void DoKeyExchange(ITLlib.SSP_KEYS keys)
        {
            var arr = BitConverter.GetBytes(keys.HostInter);
            var elem = NewCmd((byte)0, (byte)9, (byte)CCommands.SSP_CMD_REQUEST_KEY_EXCHANGE);
            for (int i = 0; i < 8; i++) elem.Add(arr[i]);
            lock (_queueLock) { _sendQueue.Insert(0, elem); } // Handshake priorisieren
        }

        private void SetProtocolVersion(byte v)
        {
            Enqueue((byte)1, (byte)2, (byte)CCommands.SSP_CMD_HOST_PROTOCOL_VERSION, v);
        }

        // M�nzauszahlung nach St�ckzahlen (Index: {1,2,5,10,20,50,100,200} Cent)
        public void PayoutCoins(int[] countsByIndex)
        {
            if (countsByIndex == null || countsByIndex.Length < 8)
            {
                Log("PayoutCoins: ung�ltige St�ckzahlliste.");
                return;
            }
            try { BusyAnimationManager.Begin("M�nzauszahlung l�uft"); } catch { }

            int[] valOrder = new[] { 1, 2, 5, 10, 20, 50, 100, 200 };

            var denoms = new List<(int valueCent, ushort count)>();
            for (int i = 0; i < 8; i++)
            {
                int v = valOrder[i];
                int c = Math.Max(0, countsByIndex[i]);
                if (c <= 0) continue;

                denoms.Add((v, (ushort)Math.Min(65535, c)));

                // interne ToPay-Z�hler grob pflegen
                switch (v)
                {
                    case 1: _toPay_1 += c; break;
                    case 2: _toPay_2 += c; break;
                    case 5: _toPay_5 += c; break;
                    case 10: _toPay_10 += c; break;
                    case 20: _toPay_20 += c; break;
                    case 50: _toPay_50 += c; break;
                    case 100: _toPay_100 += c; break;
                    case 200: _toPay_200 += c; break;
                }
            }

            if (denoms.Count == 0)
            {
                Log("PayoutCoins: keine unterst�tzten M�nzen > 0 ausgew�hlt.");
                return;
            }

            // PAYOUT_BY_DENOMINATION: 1(cmd) + 1(count) + N*(2+4+3) + 1(flag)
            int len = 3 + denoms.Count * 9;

            var elem = new List<byte>();
            elem.Add(1);                 // Encryption
            elem.Add((byte)len);         // Data length
            elem.Add((byte)CCommands.SSP_CMD_PAYOUT_BY_DENOMINATION);
            elem.Add((byte)denoms.Count);

            foreach (var d in denoms)
            {
                // Count (2 LE)
                elem.Add((byte)(d.count & 0xFF));
                elem.Add((byte)((d.count >> 8) & 0xFF));

                // Value (4 LE) � Cent
                var v = BitConverter.GetBytes(d.valueCent);
                elem.Add(v[0]); elem.Add(v[1]); elem.Add(v[2]); elem.Add(v[3]);

                // Currency 'EUR'
                elem.Add(69); elem.Add(85); elem.Add(82);
            }

            elem.Add(88); // real payout

            lock (_queueLock)
            {
                _sendQueue.Add(elem);
                // Kopie f�r evtl. Retry nach KEY_NOT_SET
                _pendingPayoutCmd = new List<byte>(elem);
            }
            Log("PayoutCoins enqueued.");
        }

        // M�nz-Level anfragen (wird vom UI zyklisch genutzt)
        public void RequestCoinLevels()
        {
            // W�hrend sehr kurzer Zeit nach Einwurf keine Levelabfragen (Vermeidet Event-Zerhackung bei vielen Polls)
            if ((DateTime.UtcNow - _lastCoinCreditUtc).TotalMilliseconds < 400)
            {
                return; // kurz aussetzen
            }
            // Instanzbasiertes Gate je Ger�t (separat f�r Coin/1 und Coin/2)
            var now = DateTime.UtcNow;
            lock (_instanceLevelsLock)
            {
                int minMs = _kassensturzMode ? _kassensturzCooldownMs : 1200;
                if ((now - _lastInstanceLevelsRequestUtc).TotalMilliseconds < minMs)
                {
                    return; // geblockt f�r dieses Ger�t
                }
                _lastInstanceLevelsRequestUtc = now; // Timestamp pro Ger�t
            }
            AlignPayoutLevels(false);
        }

        // GET_ALL_LEVELS � optional
        private void RequestAllLevels(bool encrypted)
        {
            byte enc = (byte)(encrypted ? 1 : 0);
            var elem = NewCmd(enc, (byte)1, (byte)0xB5);
            lock (_queueLock) { _sendQueue.Add(elem); }
            Log($"REQ GET_ALL_LEVELS {(encrypted ? "[enc]" : "[plain]")}");
        }

        // Verf�gbarkeiten kopiert zur�ckgeben
        public int[] GetCoinAvailability()
        {
            lock (_levelsLock)
            {
                var copy = new int[8];
                Array.Copy(_coinLevels, copy, 8);
                return copy;
            }
        }

        private void AlignPayoutLevels(bool encrypted)
        {
            RequestDenomLevel(1, encrypted);
            RequestDenomLevel(2, encrypted);
            RequestDenomLevel(5, encrypted);
            RequestDenomLevel(10, encrypted);
            RequestDenomLevel(20, encrypted);
            RequestDenomLevel(50, encrypted);
            RequestDenomLevel(100, encrypted);
            RequestDenomLevel(200, encrypted);
        }

        private void Enqueue(byte enc, byte len, byte cmd, params byte[] rest)
        {
            var elem = NewCmd(enc, len, cmd);
            foreach (var r in rest) elem.Add(r);
            lock (_queueLock) { _sendQueue.Add(elem); }
        }

        // NEU: Front-Queueing f�r priorisierte Polls
        private void EnqueueFront(byte enc, byte len, byte cmd, params byte[] rest)
        {
            var elem = NewCmd(enc, len, cmd);
            foreach (var r in rest) elem.Add(r);
            lock (_queueLock) { _sendQueue.Insert(0, elem); }
        }

        private List<byte> NewCmd(byte enc, byte len, byte cmd)
        {
            return new List<byte> { enc, len, cmd };
        }

        private List<byte> Dequeue()
        {
            lock (_queueLock)
            {
                if (_sendQueue.Count == 0) return new List<byte>();
                var e = _sendQueue[0];
                _sendQueue.RemoveAt(0);
                return e;
            }
        }

        private bool HasQueuedItems()
        {
            lock (_queueLock) { return _sendQueue.Count > 0; }
        }

        private void ClearQueue()
        {
            lock (_queueLock) { _sendQueue.Clear(); }
        }

        private void SafeClose()
        {
            try { _comms.CloseComPort(); } catch { }
        }

        private void Log(string s)
        {
            // Bei laufendem SmartEmpty verbale Roh-Poll Logs etwas drosseln
            if (_smartEmptyInProgress && _debugRawPoll && s.IndexOf("RAW=", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // �berspringe detaillierte RAW-Eintr�ge um UI (EventLog) nicht zu �berlasten
                return;
            }
            try { EventLog?.Invoke("[V1] " + s); } catch { }

            // Zus�tzlich in dedizierte SmartCoin-Logdatei schreiben
            try
            {
                string folder = null;
                try
                {
                    if (object.ReferenceEquals(this, CoinManager.Instance)) folder = "SmartCoin1";
                    else if (object.ReferenceEquals(this, Coin2Manager.Instance)) folder = "SmartCoin2";
                }
                catch { }
                if (string.IsNullOrEmpty(folder))
                {
                    // Fallback anhand COM-Port/INI
                    try
                    {
                        var sc1 = IniHelper.ReadValue("SmartCoin/1", "ComPort", AppSettings.IniPath);
                        var sc2 = IniHelper.ReadValue("SmartCoin/2", "ComPort", AppSettings.IniPath);
                        if (!string.IsNullOrWhiteSpace(ComPort))
                        {
                            if (!string.IsNullOrWhiteSpace(sc1) && string.Equals(sc1, ComPort, StringComparison.OrdinalIgnoreCase)) folder = "SmartCoin1";
                            else if (!string.IsNullOrWhiteSpace(sc2) && string.Equals(sc2, ComPort, StringComparison.OrdinalIgnoreCase)) folder = "SmartCoin2";
                        }
                    }
                    catch { }
                }
                if (!string.IsNullOrEmpty(folder))
                {
                    DeviceLogger.LogSmartCoin(folder, s);
                }
            }
            catch { }
        }

        private void CoinLevelsUpdatedSafe()
        {
            try { CoinLevelsUpdated?.Invoke(GetCoinAvailability()); } catch { }
        }

        private int IndexFromCent(int cent)
        {
            switch (cent)
            {
                case 1: return 0;
                case 2: return 1;
                case 5: return 2;
                case 10: return 3;
                case 20: return 4;
                case 50: return 5;
                case 100: return 6;
                case 200: return 7;
                default: return -1;
            }
        }

        private string FormatLevels(int[] lv)
        {
            if (lv == null || lv.Length < 8) return "(keine Daten)";
            string safe(int i) => lv[i] < 0 ? "?" : lv[i].ToString();
            return $"1c={safe(0)}, 2c={safe(1)}, 5c={safe(2)}, 10c={safe(3)}, 20c={safe(4)}, 50c={safe(5)}, 1€={safe(6)}, 2€={safe(7)}";
        }

        /// <summary>
        /// Führt einen Smart-Empty-Befehl aus (alle Münzen werden ausgezahlt, wie Kassensturz in KassensystemPRO)
        /// </summary>
        public void SmartEmpty()
        {
            // Vorbereitungen: Queue leeren & Level-Requests aussetzen
            ClearQueue();
            _suspendLevelRequests = true;
            _smartEmptyRetryCount = 0;
            _smartEmptyInitialLevelSum = CurrentLevelSum();
            _lastSmartEmptyActivityUtc = DateTime.UtcNow;
            // 1 = Encryption, 1 = Length, 1 = Command
            Enqueue(1, 1, (byte)CCommands.SSP_CMD_SMART_EMPTY);
            _smartEmptyInProgress = true;
            Log("SmartEmpty-Befehl (SSP_CMD_SMART_EMPTY) enqueued. StartLevelSum=" + _smartEmptyInitialLevelSum);
        }

        private int CurrentLevelSum()
        {
            lock (_levelsLock)
            {
                int s = 0;
                int[] vals = { 1, 2, 5, 10, 20, 50, 100, 200 };
                for (int i = 0; i < 8; i++)
                {
                    int c = _coinLevels[i];
                    if (c > 0) s += c * vals[i];
                }
                return s; // in Cent
            }
        }

        // Wiederhergestellt: HardDisconnect f�r Manager
        public void HardDisconnect()
        {
            try { Log("HardDisconnect init"); } catch { }
            _stop = true;
            try { SafeClose(); } catch { }
            try
            {
                if (_thread != null)
                {
                    var start = DateTime.UtcNow;
                    while (!_threadEnded && (DateTime.UtcNow - start).TotalMilliseconds < 2000)
                    {
                        if (_thread.Join(100)) break;
                    }
                }
            }
            catch { }
            lock (_queueLock) { _sendQueue.Clear(); }
            Connected = false;
            SetStatus("Getrennt");
            try { Log("HardDisconnect done"); } catch { }
            try { Thread.Sleep(100); } catch { }
            try { BusyAnimationManager.EndForce(); } catch { }
        }

        // -- ZENTRALE WRAPPER: einheitliche Level-Abfragen �ber eine Stelle --
        public static void RequestLevelsGlobal()
        {
            try
            {
                // Kein globales Gate mehr: jede Instanz filtert selbst per Ger�t
                try
                {
                    var inst = CoinManager.Instance;
                    if (inst is SmartCoinV1 sc && sc.Connected) sc.RequestCoinLevels();
                    else if (inst is Rm5CctalkValidator rm) rm.RequestCoinLevels();
                }
                catch { }

                try
                {
                    var inst2 = Coin2Manager.Instance;
                    if (inst2 is SmartCoinV1 sc2 && sc2.Connected) sc2.RequestCoinLevels();
                    else if (inst2 is Rm5CctalkValidator rm2) rm2.RequestCoinLevels();
                }
                catch { }
            }
            catch { }
        }

        public static void ScheduleLevelsGlobal()
        {
            try { (CoinManager.Instance as SmartCoinV1)?.ScheduleLevelsRequest(); } catch { }
            try { (Coin2Manager.Instance as SmartCoinV1)?.ScheduleLevelsRequest(); } catch { }
        }

        // Kassensturz-Mode einschalten/abschalten; setzt globales 5s-Gate f�r Level-Requests
        public static void EnableKassensturzMode(bool enabled, int cooldownMs = 5000)
        {
            lock (_globalModeLock)
            {
                _kassensturzMode = enabled;
                if (cooldownMs > 0) _kassensturzCooldownMs = cooldownMs;
            }
        }
    }
}