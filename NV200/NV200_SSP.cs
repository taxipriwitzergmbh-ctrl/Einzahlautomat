using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using ITLlib;

namespace TaMi_Einzahlautomat
{
    public class NV200_SSP
    {
        private bool _logDetailedFrames = false; // detailliertes Frame-Logging (Standard: aus)
        private DateTime _lastFrameLogUtc = DateTime.MinValue; // Zeitstempel für Throttle
        public void SetDetailedFrameLogging(bool enable) { _logDetailedFrames = enable; }

        // NEW resilience / logging fields
        private int _consecutiveSendFails = 0;
        private const int MaxSendFailsBeforeReconnect = 3;
        private bool _verbosePollLogging = false;
        public void SetVerbosePollLogging(bool enable) { _verbosePollLogging = enable; }

        // Einzahlungs-Animationsfelder (Scheine)  hinzugefügt
        private volatile bool _noteDepositAnimActive = false; // läuft gerade eine Einzahlungsanzeige
        private DateTime _lastNoteDepositUtc = DateTime.MinValue; // letzter Zeitstempel einer Notenaktivität

        // Daten-Ablage: bevorzugt <AppBase>\Logs\NV200, Fallback %LocalAppData%\Geldautomat\Logs\NV200
        private string DataRootDir; // früher: readonly LocalAppData
        private string Nv200Dir
        {
            get
            {
                string folder = "Nv200_1";
                try
                {
                    var section = DetermineNvSection();
                    if (section != null && section.EndsWith("/2", StringComparison.Ordinal)) folder = "Nv200_2";
                }
                catch { }
                return Path.Combine(DataRootDir, folder);
            }
        }
        private string ProtokollFile => Path.Combine(Nv200Dir, $"NV200_Protokoll.txt");

        private Thread Thread;
        public int MAX_PayoutCount_of_5Euro = 25;
        public int MAX_PayoutCount_of_10Euro = 25;
        public int MAX_PayoutCount_of_20Euro = 25;
        public int MAX_PayoutCount_of_50Euro = 10;
        public int MAX_PayoutCount_of_100Euro = 0;
        public int MAX_PayoutCount_of_200Euro = 0;
        public int MAX_PayoutCount_of_500Euro = 0;

        public bool Akzept_5_euro = false;
        public bool Akzept_10_euro = false;
        public bool Akzept_20_euro = false;
        public bool Akzept_50_euro = false;
        public bool Akzept_100_euro = false;
        public bool Akzept_200_euro = false;
        public bool Akzept_500_euro = false;

        public int Payout_5_euro = 0;
        public int Payout_10_euro = 0;
        public int Payout_20_euro = 0;
        public int Payout_50_euro = 0;
        public int Payout_100_euro = 0;
        public int Payout_200_euro = 0;
        public int Payout_500_euro = 0;

        public int Cashbox_5_euro = 0;
        public int Cashbox_10_euro = 0;
        public int Cashbox_20_euro = 0;
        public int Cashbox_50_euro = 0;
        public int Cashbox_100_euro = 0;
        public int Cashbox_200_euro = 0;
        public int Cashbox_500_euro = 0;

        private int To_Payout_5Euro = 0;
        private int To_Payout_10Euro = 0;
        private int To_Payout_20Euro = 0;
        private int To_Payout_50Euro = 0;
        private int To_Payout_100Euro = 0;
        private int To_Payout_200Euro = 0;
        private int To_Payout_500Euro = 0;

        public int Max_Bills_in_Cashbox = 900;

        public event Action<int> Note_akzepted;
        public double Last_Dispensed_count = 0;

        public event Action<int> Wert_Dispensing;
        public event Action<int> Dispensing_Complete;
        public event Action<int> Cashbox_Replaced;
        public event Action Cashbox_Removed;
        public event Action<int, int> Jammed;
        public event Action<int, int> Halted;
        public event Action<int, int> Error_while_payout;
        public event Action<int> Note_in_Bezel;
        public event Action<int, int> Payout_Timeout;
        public event Action<int> Note_read;

        public event Action<string> Ereignis;

        // Robust event invocation (per-subscriber try/catch)
        private void SafeInvoke(Action evt)
        {
            var d = evt; if (d == null) return;
            foreach (var del in d.GetInvocationList())
            {
                try { ((Action)del).Invoke(); } catch { }
            }
        }
        private void SafeInvoke<T>(Action<T> evt, T arg)
        {
            var d = evt; if (d == null) return;
            foreach (var del in d.GetInvocationList())
            {
                try { ((Action<T>)del).Invoke(arg); } catch { }
            }
        }
        private void SafeInvoke<T1, T2>(Action<T1, T2> evt, T1 a1, T2 a2)
        {
            var d = evt; if (d == null) return;
            foreach (var del in d.GetInvocationList())
            {
                try { ((Action<T1, T2>)del).Invoke(a1, a2); } catch { }
            }
        }

        private int Channel1_Value = 0;
        private int Channel2_Value = 0;
        private int Channel3_Value = 0;
        private int Channel4_Value = 0;
        private int Channel5_Value = 0;
        private int Channel6_Value = 0;
        private int Channel7_Value = 0;
        private int Channel8_Value = 0;

        public bool Beenden = false;
        public bool Thread_beendet = true;

        public string ComPort = "Com1";
        public int SSPAdress = 0;
        public string Nv200_speicher_zusatz = "";

        public string Firmware = "-.-";
        public string SerialNumber = "-.-";

        public List<List<byte>> Sendeliste = new List<List<byte>>();
        public List<List<string>> Ereignisliste = new List<List<string>>();
        public List<List<string>> Ereignisliste_Vortag = new List<List<string>>();
        public List<List<string>> Ereignisliste_zum_Anzeigen = new List<List<string>>();

        public DateTime Last_Communicationtime;

        public string states = "not started";
        // Track whether device is logically disabled to avoid misreporting Idle when no events
        private volatile bool _isDisabled = false;
        // Track last time a non-OK response was observed
        public DateTime LastResponseNotOkUtc { get; private set; } = DateTime.MinValue;
        // Quick idle check helper for UI
        public bool IsIdleState => !string.IsNullOrEmpty(states) && states.IndexOf("Idle", StringComparison.OrdinalIgnoreCase) >= 0;

        private SSP_COMMAND_INFO CommandInfo = new SSP_COMMAND_INFO();
        private SSP_COMMAND CommandStructure = new SSP_COMMAND();
        private SSPComms Connection = new SSPComms();
        private SSP_KEYS eSSPKeys = new SSP_KEYS();
        private bool Encryption_OK = false;

        private string Last_ID = "";

        private int Current_read_Note = 0;

        // Feld in der Klasse ergänzen
        private int _lastRequestedPayoutSumCent = 0;
        private DateTime _lastAutoEnable = DateTime.MinValue;

        // Neu: Flag für optionales Auto-Enable (Standard: aus)
        private bool _autoEnableOnDisabled = false;
        public void AllowAutoEnable(bool allow) => _autoEnableOnDisabled = allow;

        // NEU: Flag für den aktiven Notenzyklus (soll während der Auszahlung nicht verändert werden)
        private volatile bool _noteCycleActive = false;

        // Drosselung für Level-Requests (Flooding vermeiden)
        private DateTime _lastLevelReqUtc = DateTime.MinValue;
        private readonly TimeSpan _minLevelInterval = TimeSpan.FromSeconds(6);

        // Felder vorhanden lassen (nicht verwendet), um Kompatibilität zu wahren
        //private bool? _route5, _route10, _route20, _route50, _route100, _route200, _route500;

        public bool is_in_use()
        {
            bool in_use = true;
            if (states.ToUpper().Contains("NEUSTART")) in_use = false;
            if (states.ToUpper().Contains("NOT STARTED")) in_use = false;
            if (states.ToUpper().Contains("IDLE")) in_use = false;
            if (states.ToUpper().Contains("CASHBOX FULL")) in_use = false;
            if (states.ToUpper().Contains("UNSAFE JAMMED")) in_use = false;
            if (states.ToUpper().Contains("SAFE JAMMED")) in_use = false;
            if (states.ToUpper().Contains("UNIT JAMMED")) in_use = false;
            if (states.ToUpper().Contains("HALTED")) in_use = false;
            return in_use;
        }

        public NV200_SSP()
        {
            Thread = new Thread(Communication);
            Protokollspeicher_Thread = new Thread(Protokoll_Vortag_speichern);

            // Bevorzugter Log-Pfad: Installationsordner\Logs
            try
            {
                DataRootDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            }
            catch
            {
                // Defensive: falls BaseDirectory scheitert, init direkt auf Fallback
                DataRootDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Geldautomat",
                    "Logs");
            }
        }

        public void Starten()
        {
            try
            {
                // Versuche bevorzugten Pfad (<AppBase>\Logs\NV200)
                Directory.CreateDirectory(DataRootDir);
                Directory.CreateDirectory(Nv200Dir);
            }
            catch (UnauthorizedAccessException)
            {
                // Fallback: %LocalAppData%\Geldautomat\Logs\NV200
                DataRootDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Geldautomat",
                    "Logs");
                Directory.CreateDirectory(DataRootDir);
                Directory.CreateDirectory(Nv200Dir);
                Ereignis_adden("Datenpfad im Installationsverzeichnis nicht beschreibbar - Fallback auf LocalAppData genutzt.");
            }
            catch (Exception ex)
            {
                Ereignis_adden("Datenpfad-Initialisierung fehlgeschlagen: " + ex.Message);
                throw;
            }

            try { Ereignis_adden("NV200 Logverzeichnis: " + Nv200Dir); } catch { }

            LoadMaxConfigFromIni();

            // Cashbox-Zähler aus Registry laden
            LoadCashboxSnapshot();

            Protokoll_laden();
            Thread.IsBackground = true;
            Thread.Start();
            Protokollspeicher_Thread.IsBackground = true;
            Protokollspeicher_Thread.Start();
            states = "started";
        }

        private void Protokoll_laden()
        {
            if (File.Exists(ProtokollFile))
            {
                using (StreamReader Datei = new StreamReader(ProtokollFile))
                {
                    while (!Datei.EndOfStream)
                    {
                        string Readstring = Datei.ReadLine();
                        string[] ReadArray = Readstring.Split('-');
                        if (ReadArray.Length == 3)
                        {
                            var element1 = new List<string> { ReadArray[0], ReadArray[1], ReadArray[2] };
                            if (DateTime.TryParse(element1[1], out DateTime dt))
                            {
                                if (dt.Date == DateTime.Now.Date)
                                {
                                    Ereignisliste.Add(element1);
                                }
                            }
                        }
                    }
                }
            }
        }

        // Protokoll_speichern thread-sicher machen
        private void Protokoll_speichern()
        {
            try
            {
                lock (_logLock)
                {
                    using (var Datei = new StreamWriter(ProtokollFile, false))
                    {
                        int counter = 0;
                        while (counter < Ereignisliste.Count)
                        {
                            var element1 = Ereignisliste[counter];
                            try
                            {
                                Datei.WriteLine(element1[0] + "-" + element1[1] + "-" + element1[2]);
                            }
                            catch { }
                            counter++;
                        }
                    }
                }
            }
            catch { /* IO-Fehler ignorieren */ }
        }

        // NEU: nur eine einzelne Zeile anhängen (Performance-Optimierung)
        private void Protokoll_Append(List<string> element)
        {
            try
            {
                if (element == null || element.Count < 3) return;
                lock (_logLock)
                {
                    using (var sw = new StreamWriter(ProtokollFile, true))
                    {
                        sw.WriteLine(element[0] + "-" + element[1] + "-" + element[2]);
                    }
                }
            }
            catch { }
        }

        private string Get_New_ID()
        {
            string New_ID_1 = DateTime.Now.ToString("ddMMyyyyHHmmssfff");
            while (New_ID_1 == Last_ID)
            {
                Thread.Sleep(50);
                New_ID_1 = DateTime.Now.ToString("ddMMyyyyHHmmssfff");
            }
            Last_ID = New_ID_1;
            return New_ID_1;
        }

        private Thread Protokollspeicher_Thread;
        private bool Vortag_speichern = false;

        private void Protokoll_Vortag_speichern()
        {
            while (!Beenden)
            {
                if (Vortag_speichern)
                {
                    string dateStr = DateTime.Now.AddDays(-1).ToString("dd_MM_yyyy");
                    string path = Path.Combine(Nv200Dir, $"NV200{Nv200_speicher_zusatz}_Protokoll_{dateStr}.txt");
                    if (!File.Exists(path))
                    {
                        using (StreamWriter Datei = new StreamWriter(path))
                        {
                            int counter = 0;
                            while (counter < Ereignisliste_Vortag.Count)
                            {
                                var element1 = Ereignisliste_Vortag[counter];
                                try
                                {
                                    if (DateTime.TryParse(element1[1], out DateTime datum))
                                    {
                                        if (datum < DateTime.Parse(DateTime.Now.ToString("dd.MM.yyyy") + " 00:00:00"))
                                        {
                                            try { Datei.WriteLine(element1[0] + "-" + element1[1] + "-" + element1[2]); } catch { }
                                            Ereignisliste_Vortag.RemoveAt(counter);
                                            counter--;
                                        }
                                    }
                                }
                                catch { }
                                counter++;
                            }
                        }
                        SafeInvoke(Ereignis, "End Saving Protokoll Vortag");
                    }
                    Vortag_speichern = false;
                }
                Thread.Sleep(500);
            }
        }

        public void Ereignis_adden(string x)
        {
            // Spam-Filter: rohe Frame-Ausgaben (" Send:") nur wenn detailliertes Logging aktiv
            if (!_logDetailedFrames && x.IndexOf(" Send:", StringComparison.OrdinalIgnoreCase) >= 0)
                return;

            // Prefix mit NV200-Section (NV200/1 oder NV200/2) hinzufügen, falls noch nicht vorhanden
            try
            {
                if (!x.StartsWith("[NV200/", StringComparison.OrdinalIgnoreCase))
                {
                    string section = "?";
                    try { section = DetermineNvSection(); } catch { }
                    x = $"[{section}] {x}";
                }
            }
            catch { }

            var Element = new List<string>
            {
                Get_New_ID(),
                DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"),
                x
            };
            Ereignisliste.Add(Element);
            Ereignisliste_zum_Anzeigen.Add(Element);
            if (Ereignisliste_zum_Anzeigen.Count > 100)
                Ereignisliste_zum_Anzeigen.RemoveAt(0);

            // Tageswechsel: nur einmal zwischen 00:00:00 und 00:00:05
            if (DateTime.Now.TimeOfDay.Hours < 1 && DateTime.Now.TimeOfDay.Minutes < 1 && DateTime.Now.TimeOfDay.Seconds < 5)
            {
                SafeInvoke(Ereignis, "Start Saving Protokoll");
                while (Ereignisliste.Count > 0)
                {
                    Ereignisliste_Vortag.Add(Ereignisliste[0]);
                    Ereignisliste.RemoveAt(0);
                }
                SafeInvoke(Ereignis, "End Copy Protokoll to Vortagsliste");
                Vortag_speichern = true;
                // Neue Tagesdatei leeren (voller Rewrite der – jetzt leeren – aktuellen Liste)
                try { lock (_logLock) { File.WriteAllText(ProtokollFile, string.Empty); } } catch { }
                while (DateTime.Now.TimeOfDay.Seconds < 5)
                    Thread.Sleep(100);
            }

            // sofort an UI
            SafeInvoke(Ereignis, x);

            // Nur anhängen statt komplettes Umschreiben
            Protokoll_Append(Element);
        }

        public int Noch_Verfuegbar()
        {
            int Neu = 0;
            Neu = To_Payout_5Euro * 500;
            Neu += To_Payout_10Euro * 1000;
            Neu += To_Payout_20Euro * 2000;
            Neu += To_Payout_50Euro * 5000;
            Neu += To_Payout_100Euro * 10000;
            Neu += To_Payout_200Euro * 20000;
            Neu += To_Payout_500Euro * 50000;

            Payout_5_euro += To_Payout_5Euro;
            Payout_10_euro += To_Payout_10Euro;
            Payout_20_euro += To_Payout_20Euro;
            Payout_50_euro += To_Payout_50Euro;
            Payout_100_euro += To_Payout_100Euro;
            Payout_200_euro += To_Payout_200Euro;
            Payout_500_euro += To_Payout_500Euro;

            To_Payout_5Euro = 0;
            To_Payout_10Euro = 0;
            To_Payout_20Euro = 0;
            To_Payout_50Euro = 0;
            To_Payout_100Euro = 0;
            To_Payout_200Euro = 0;
            To_Payout_500Euro = 0;
            return Neu;
        }

        private bool neustart = false;

        private void Communication()
        {
            Ereignis_adden("Thread gestartet");
            Thread_beendet = false;
            while (true)
            {
                string state = "";
                try
                {
                    if (_consecutiveSendFails >= MaxSendFailsBeforeReconnect)
                    {
                        Ereignis_adden($"Erzwinge Neustart nach {_consecutiveSendFails} Send-Fehlern");
                        _consecutiveSendFails = 0; // reset counter on forced restart
                    }
                    states = "Neustart"; // only logged at real reconnect attempt
                    Ereignis_adden(states);
                    try { Connection.CloseComPort(); } catch { Ereignis_adden("Failed CloseComport vor Neustart"); }
                    CommandStructure.RetryLevel = 2;
                    CommandStructure.Timeout = 3000;
                    CommandStructure.SSPAddress = (byte)SSPAdress;
                    CommandStructure.ComPort = ComPort;
                    CommandStructure.EncryptionStatus = false;
                    Last_Communicationtime = DateTime.Now;

                    if (Connection.OpenSSPComPort(CommandStructure))
                    {
                        Ereignis_adden("Connected");
                        Encryption_OK = false;
                        sync();
                        state = "";
                        while (true)
                        {
                            if (Sendeliste.Count == 0) Poll();
                            var Element = Sendeliste[0];
                            Sendeliste.RemoveAt(0);
                            if (Element.Count > 2)
                            {
                                CommandStructure.EncryptionStatus = Element[0] != 0;
                                CommandStructure.CommandDataLength = (byte)Element[1];
                                for (int n = 0; n < Element[1]; n++) CommandStructure.CommandData[n] = Element[n + 2];
                                bool sendOk = Connection.SSPSendCommand(CommandStructure, CommandInfo);
                                if (!sendOk)
                                {
                                    _consecutiveSendFails++;
                                    if (_consecutiveSendFails < MaxSendFailsBeforeReconnect)
                                    {
                                        // Attempt lightweight recovery: re-sync once, clear encryption, requeue command
                                        Ereignis_adden($"Send Command verlieh false (Versuch {_consecutiveSendFails}/{MaxSendFailsBeforeReconnect - 1})  versuche Sync statt sofortigem Neustart");
                                        Encryption_OK = false;
                                        sync();
                                        // Requeue original element (front) to retry after sync
                                        Sendeliste.Insert(0, Element);
                                        Thread.Sleep(150);
                                        continue; // stay in inner loop
                                    }
                                    // Too many failures -> break to outer reconnect
                                    Ereignis_adden("Send Command verlieh false - überschreitet Toleranz, Neustart erforderlich");
                                    state = AppendState(state, ";SendFailMax");
                                    Sendeliste.Clear();
                                    Encryption_OK = false;
                                    try { Connection.CloseComPort(); } catch { }
                                    break; // inner loop
                                }
                                else
                                {
                                    _consecutiveSendFails = 0; // reset on success
                                    Last_Communicationtime = DateTime.Now;
                                    if (CommandStructure.ResponseDataLength < 1)
                                    {
                                        if (!states.Contains("Response is shorter than 1 Byte"))
                                        {
                                            Ereignis_adden("Response is shorter than 1 Byte");
                                            state = AppendState(state, ";Resp<1");
                                        }
                                    }
                                    else
                                    {
                                        if (CommandStructure.ResponseData[0] != CCommands.SSP_RESPONSE_OK)
                                        {
                                            if (CommandStructure.ResponseData[0] == CCommands.SSP_RESPONSE_KEY_NOT_SET)
                                            {
                                                Ereignis_adden("Key not set");
                                                if (!neustart)
                                                {
                                                    neustart = true;
                                                    Sendeliste.Clear();
                                                    Encryption_OK = false;
                                                    sync();
                                                }
                                            }

                                            // Special handling for payout still in place
                                            if (Element[2] == CCommands.SSP_CMD_PAYOUT_BY_DENOMINATION && CommandStructure.ResponseData[0] == 245)
                                            {
                                                if (_isDispensing)
                                                {
                                                    Ereignis_adden("245 während Dispensing ignoriert (toleriert)");
                                                    continue;
                                                }
                                                Ereignis_adden("Response not ok (PBD): " + (CommandStructure.ResponseDataLength >= 2 ? CommandStructure.ResponseData[1].ToString() : "?"));
                                                if (CommandStructure.ResponseDataLength >= 2 && CommandStructure.ResponseData[1] == 1)
                                                {
                                                    Ereignis_adden("Fallback auf PAYOUT_AMOUNT (Summe)");
                                                    PayoutAmountFromQueuedDenoms();
                                                }
                                                Payout_angleichen();
                                                SafeInvoke(Error_while_payout, 0, 0);
                                                continue;
                                            }

                                            // NEW: mark non-OK timestamp for watchdogs
                                            try { LastResponseNotOkUtc = DateTime.UtcNow; } catch { }
                                            // NEW: treat single non-OK while idle as warning only (do not modify state drastically)
                                            if (Element[2] == CCommands.SSP_CMD_POLL)
                                            {
                                                Ereignis_adden($"Warn: Poll Response not OK ({CommandStructure.ResponseData[0]})  wird ignoriert (Transient)");
                                                // Skip fatal handling and continue polling
                                                Thread.Sleep(120);
                                                continue;
                                            }

                                            // General error handling
                                            state = AppendState(state, $";RespNotOK({CommandStructure.ResponseData[0]}) Cmd:{Element[2]}");
                                            Ereignis_adden(state);
                                        }
                                        else
                                        {
                                            try
                                            {
                                                if (Element[2] == CCommands.SSP_CMD_SYNC && Element[0] == 1) Encryption_OK = true;
                                                // successful non-POLL responses may clear the non-OK marker; POLL handled separately in Auswertung (Idle)
                                                try { if (Element[2] != CCommands.SSP_CMD_POLL) LastResponseNotOkUtc = DateTime.MinValue; } catch { }
                                                Communication_Auswertung(ref state, Element);
                                            }
                                            catch (Exception ex)
                                            {
                                                Ereignis_adden(ex.Message);
                                            }
                                        }
                                    }
                                }
                                if (Element[2] == CCommands.SSP_CMD_POLL) Thread.Sleep(200);
                                // NEU: Rohframe-Logging nur wenn aktiviert + gedrosselt
                                if (_logDetailedFrames)
                                {
                                    if (_frameLogIntervalMs <= 0 || (DateTime.UtcNow - _lastFrameLogUtc).TotalMilliseconds >= _frameLogIntervalMs)
                                    {
                                        _lastFrameLogUtc = DateTime.UtcNow;
                                        string _bytes = " Send:";
                                        for (int n = 0; n < Element.Count; n++) _bytes += " - " + Element[n];
                                        _bytes += " - Response:";
                                        for (int n = 0; n < CommandStructure.ResponseDataLength; n++) _bytes += " - " + CommandStructure.ResponseData[n];
                                        Ereignis_adden(_bytes);
                                    }
                                }
                            }
                            else
                            {
                                Ereignis_adden("Command zu kurz");
                            }
                            if (Beenden || ComPort != CommandStructure.ComPort)
                            {
                                Connection.CloseComPort();
                                break;
                            }
                            states = state;
                        }
                    }
                    else
                    {
                        // Fix: Zugriff auf CommandStructure.ComPort statt fehlerhaftem Member
                        state = AppendState(state, ";Open SSPComPort (" + CommandStructure.ComPort + ") failed");
                        Ereignis_adden(state);
                    }
                    Thread.Sleep(1000);
                    if (Beenden) break;
                }
                catch (ObjectDisposedException ex)
                {
                    Ereignis_adden(ex.Message);
                }
                catch (Exception exi)
                {
                    Ereignis_adden(exi.Message);
                }
                states = state;
                try { bool isIdle = !string.IsNullOrWhiteSpace(state) && state.ToLowerInvariant().Contains("idle"); SyncCoinFeederByIdle(isIdle); } catch { }
            }
            Protokoll_speichern();
            Thread_beendet = true;
        }

        private static string AppendState(string current, string add)
        {
            if (string.IsNullOrEmpty(current)) return add; if (current.Contains(add)) return current; return current + add;
        }

        public void Communication_Auswertung(ref string state, List<byte> sendElement)
        {
            // Handshake/Key-Exchange
            if (CommandStructure.CommandData[0] == CCommands.SSP_CMD_SYNC)
            {
                Ereignis_adden("Syncronisiert");
                if (!Encryption_OK)
                {
                    Connection.InitiateSSPHostKeys(eSSPKeys, CommandStructure);
                    Set_up_Generator(eSSPKeys);
                    neustart = false;
                }
                return;
            }

            if (CommandStructure.CommandData[0] == CCommands.SSP_CMD_SET_GENERATOR)
            {
                Ereignis_adden("Set Generator");
                Set_up_Modulus(eSSPKeys);
                return;
            }

            if (CommandStructure.CommandData[0] == CCommands.SSP_CMD_SET_MODULUS)
            {
                Ereignis_adden("Set Modulus");
                Do_Key_Exchange(eSSPKeys);
                return;
            }

            if (CommandStructure.CommandData[0] == CCommands.SSP_CMD_REQUEST_KEY_EXCHANGE)
            {
                var array = new byte[8];
                for (int i = 0; i < 8; i++)
                    array[i] = CommandStructure.ResponseData[i + 1];
                eSSPKeys.SlaveInterKey = BitConverter.ToUInt64(array, 0);
                Connection.CreateSSPHostEncryptionKey(eSSPKeys);
                CommandStructure.Key.FixedKey = 81985526925837671UL; // 0x0123456789ABCDEF
                CommandStructure.Key.VariableKey = eSSPKeys.KeyHost;
                Ereignis_adden("Key Request: Keys are set");

                SetProtocolVersion(8);
                SetValueReportingType(1);   // Werte statt Kanalnummern
                SetInhibit(true);           // explizit sperren
                GetSerialNumber();
                Set_Routing();              // initiale Routen setzen
                //ApplyManualRouteOverrides(); // deaktiviert  dynamisches Routing
                ConfigureBezel(255, 0, 0);  // rot

                // NEU: Nach erfolgreichem Handshake automatischer vollständiger Freigabe
                if (_autoEnableOnDisabled)
                {
                    Freigeben();
                    Ereignis_adden("Auto-Freigeben nach Reset/Handshake ausgeführt.");
                }

                return;
            }

            // Firmware
            if (CommandStructure.CommandData[0] == CCommands.SSP_CMD_GET_FIRMWARE_VERSION)
            {
                Firmware = "";
                for (int n = 1; n < CommandStructure.ResponseDataLength; n++)
                    Firmware += Convert.ToChar(CommandStructure.ResponseData[n]);
                Ereignis_adden("Firmware: " + Firmware);
                return;
            }

            // Seriennummer (4 Bytes als uint32, Big-Endian)
            if (sendElement[2] == CCommands.SSP_CMD_GET_SERIAL_NUMBER)
            {
                if (CommandStructure.ResponseDataLength >= 5) // Status + 4 Bytes
                {
                    // Big-Endian: höchstwertiges Byte zuerst
                    uint serialNum = (uint)(
                        (CommandStructure.ResponseData[1] << 24) |
                        (CommandStructure.ResponseData[2] << 16) |
                        (CommandStructure.ResponseData[3] << 8) |
                        CommandStructure.ResponseData[4]
                    );
                    SerialNumber = serialNum.ToString();
                    Ereignis_adden("Seriennummer: " + SerialNumber);
                }
                else
                {
                    SerialNumber = "error";
                    Ereignis_adden("Seriennummer: Fehler - ungültige Antwortlänge");
                }
                return;
            }

            // SETUP_REQUEST -> Channel-Values (für Note_read Mapping)
            if (sendElement[2] == CCommands.SSP_CMD_SETUP_REQUEST)
            {
                try
                {
                    int channelcount = CommandStructure.ResponseData[12];
                    int multiplier = CommandStructure.ResponseData[11]
                                   + (CommandStructure.ResponseData[10] << 8)
                                   + (CommandStructure.ResponseData[9] << 16);

                    Ereignis_adden("Anzahl Channels: " + channelcount);

                    for (int counter = 1; counter <= channelcount; counter++)
                    {
                        int chVal = CommandStructure.ResponseData[12 + counter] * multiplier; // meist 5/10/20/50/100/200/500
                        switch (counter)
                        {
                            case 1: Channel1_Value = chVal; break;
                            case 2: Channel2_Value = chVal; break;
                            case 3: Channel3_Value = chVal; break;
                            case 4: Channel4_Value = chVal; break;
                            case 5: Channel5_Value = chVal; break;
                            case 6: Channel6_Value = chVal; break;
                            case 7: Channel7_Value = chVal; break;
                            case 8: Channel8_Value = chVal; break;
                        }
                        Ereignis_adden($"Channel {counter} Wert: {chVal}");
                    }
                }
                catch (Exception ex)
                {
                    Ereignis_adden("SETUP_REQUEST parse error: " + ex.Message);
                }
                return;
            }

            // GET_DENOMINATION_LEVEL -> nur Payout-Level übernehmen, Cashbox nicht anfassen
            if (sendElement[2] == CCommands.SSP_CMD_GET_DENOMINATION_LEVEL && CommandStructure.ResponseDataLength >= 2)
            {
                int wert = sendElement[3]
                         + (sendElement[4] << 8)
                         + (sendElement[5] << 16)
                         + (sendElement[6] << 24);
                int level = CommandStructure.ResponseData[1]; // Count im Payout-Store

                switch (wert)
                {
                    case 500:
                        Payout_5_euro = level;
                        break;
                    case 1000:
                        Payout_10_euro = level;
                        break;
                    case 2000:
                        Payout_20_euro = level;
                        break;
                    case 5000:
                        Payout_50_euro = level;
                        break;
                    case 10000:
                        Payout_100_euro = level;
                        break;
                    case 20000:
                        Payout_200_euro = level;
                        break;
                    case 50000:
                        Payout_500_euro = level;
                        break;
                }

                // Dynamisches Routing unmittelbar für diese Denomination anpassen
                try
                {
                    bool toPayout = ShouldStackToPayout(wert);
                    ChangeNoteRoute(wert, toPayout);
                }
                catch { }

                return;
            }

            // POLL -> Einzahlungen/Events
            if (CommandStructure.CommandData[0] == CCommands.SSP_CMD_POLL)
            {
                try
                {
                    if (CommandStructure.ResponseDataLength == 1)
                    {
                        // Idle – ggf. Animation beenden falls keine Note mehr aktiv
                        if (_noteDepositAnimActive && (DateTime.UtcNow - _lastNoteDepositUtc).TotalMilliseconds > 2500)
                        {
                            _noteDepositAnimActive = false;
                            try { BusyAnimationManager.End("note deposit idle"); } catch { }
                        }
                        _noteCycleActive = false; Current_read_Note = -1;
                        if (_isDisabled) { state = "Disabled"; SyncCoinFeederByIdle(false); }
                        else { state = "Idle"; SyncCoinFeederByIdle(true); }
                        try { LastResponseNotOkUtc = DateTime.MinValue; } catch { }

                        // Nach einem SmartEmpty/EmptyAll: Bestände konsolidieren
                        try
                        {
                            if (_emptyingActive)
                            {
                                TransferPayoutToCashbox();
                                _emptyingActive = false;
                            }
                        }
                        catch { }

                        return;
                    }

                    for (int i = 1; i < CommandStructure.ResponseDataLength; i++)
                    {
                        byte code = CommandStructure.ResponseData[i];
                        if (_verbosePollLogging) Ereignis_adden($"POLL-Event-Code: {code} (0x{code:X2})");
                        switch (code)
                        {
                            case CCommands.SSP_POLL_SLAVE_RESET:
                                Ereignis_adden("Unit reset");
                                break;
                            case CCommands.SSP_POLL_DISABLED:
                                state = ";Disabled";
                                _isDisabled = true;
                                if (CommandStructure.ResponseDataLength == 2) Ereignis_adden("Unit disabled...");
                                if (_autoEnableOnDisabled)
                                {
                                    // nach Disabled-Gerätestatus automatisch vollständig freigeben
                                    // NEU: Während einer Auszahlung kein Auto-Re-Enable ausführen
                                    if (_isDispensing)
                                    {
                                        Ereignis_adden("Auto-Re-Enable übersprungen (während Dispensing)");
                                    }
                                    else
                                    {
                                        Freigeben();
                                        Ereignis_adden("Auto-Re-Enable (erlaubt) ausgeführt.");
                                    }
                                }
                                try { SyncCoinFeederByIdle(false); } catch { }
                                break;
                            case CCommands.SSP_POLL_READ_NOTE:
                                if (i + 1 < CommandStructure.ResponseDataLength)
                                {
                                    byte channel = CommandStructure.ResponseData[i + 1];
                                    if (channel > 0)
                                    {
                                        _noteCycleActive = true;
                                        _isDisabled = false; // Gerät ist aktiv
                                        Current_read_Note = channel;
                                        int euro = ChannelValueByChannel(channel);
                                        if (euro > 0)
                                        {
                                            // euro kommt üblicherweise als 5/10/20/50/100/200/500 (Euro). Falls Firmware Cent liefert (>=1000), nicht erneut *100.
                                            int denomCent = euro >= 1000 ? euro : euro * 100;
                                            bool toPayout = ShouldStackToPayout(denomCent);
                                            ChangeNoteRoute(denomCent, toPayout);
                                            Ereignis_adden($"Pre-Stack routing {denomCent / 100}€ -> {(toPayout ? "PAYOUT" : "CASHBOX")} (PayoutCount={GetPayoutCountFor(denomCent)}, Max={GetMaxFor(denomCent)})");
                                            try { Ereignis_adden($"Note in escrow, channel {channel}, {(denomCent/100m):0.00} EUR"); } catch { Ereignis_adden($"Note in escrow, channel {channel}, {euro} (raw)"); }
                                        }
                                        if (euro > 0) SafeInvoke(Note_read, euro);
                                        state = "read note";
                                        try { SyncCoinFeederByIdle(false); } catch { }
                                        // Start Einzahlungsanimation
                                        try
                                        {
                                            _lastNoteDepositUtc = DateTime.UtcNow;
                                            if (!_noteDepositAnimActive && !BusyAnimationManager.IsActive)
                                            {
                                                BusyAnimationManager.Begin("Einzahlung läuft");
                                                _noteDepositAnimActive = true;
                                            }
                                        }
                                        catch { }
                                    }
                                    else
                                    {
                                        Ereignis_adden("Reading note");
                                        state = "Reading note";
                                        try { SyncCoinFeederByIdle(false); } catch { }
                                    }
                                    i += 1;
                                }
                                break;
                            case CCommands.SSP_POLL_NOTE_STACKING:
                                Ereignis_adden("Stacking note");
                                _isDisabled = false;
                                state = ";Stacking note";
                                try { SyncCoinFeederByIdle(false); } catch { }
                                try { _lastNoteDepositUtc = DateTime.UtcNow; } catch { }
                                break;
                            case CCommands.SSP_POLL_NOTE_STACKED:
                                Ereignis_adden("Note stacked");
                                try
                                {
                                    int euro = ChannelValueByChannel(Current_read_Note);
                                    if (euro > 0)
                                    {
                                        // Standard: euro ist eine Euro-Nennwertzahl (z. B. 100). Nur bei Cent-Werten (>=1000) umrechnen.
                                        decimal amount = euro >= 1000 ? euro / 100m : euro;
                                        AppLogger.Log($"({DetermineNvSection()}) <--- Schein eingezahlt: {amount:0.00} € (Route: CASHBOX)");
                                    }
                                }
                                catch { }
                                // tatsächliche Route: CASHBOX
                                Note_stored_or_Stacked(Current_read_Note, storedInPayout: false);
                                _noteCycleActive = false; // Cycle Ende
                                Current_read_Note = 0;
                                try { SyncCoinFeederByIdle(false); } catch { }
                                try { _lastNoteDepositUtc = DateTime.UtcNow; } catch { }
                                break;
                            case CCommands.SSP_POLL_NOTE_STORED_IN_PAYOUT:
                                Ereignis_adden("Note stored");
                                try
                                {
                                    int euro = ChannelValueByChannel(Current_read_Note);
                                    if (euro > 0)
                                    {
                                        decimal amount = euro >= 1000 ? euro / 100m : euro;
                                        AppLogger.Log($"({DetermineNvSection()}) <--- Schein eingezahlt: {amount:0.00} € (Route: PAYOUT)");
                                    }
                                }
                                catch { }
                                // tatsächliche Route: PAYOUT
                                Note_stored_or_Stacked(Current_read_Note, storedInPayout: true);
                                _noteCycleActive = false; // Cycle Ende
                                Current_read_Note = 0;
                                try { SyncCoinFeederByIdle(false); } catch { }
                                try { _lastNoteDepositUtc = DateTime.UtcNow; } catch { }
                                break;
                            case CCommands.SSP_POLL_NOTE_HELD_IN_BEZEL:
                                Ereignis_adden("Note in bezel...");
                                state = ";Note in Bezel";
                                try { SyncCoinFeederByIdle(false); } catch { }
                                if (!states.Contains("Note in Bezel") && i + 4 < CommandStructure.ResponseDataLength)
                                {
                                    int val = CommandStructure.ResponseData[i + 1]
                                            + (CommandStructure.ResponseData[i + 2] << 8)
                                            + (CommandStructure.ResponseData[i + 3] << 16)
                                            + (CommandStructure.ResponseData[i + 4] << 24);
                                    SafeInvoke(Note_in_Bezel, val);
                                }
                                i += 7;
                                break;
                            case CCommands.SSP_POLL_CASHBOX_REMOVED:
                                Ereignis_adden("Cashbox removed");
                                state = "Cashbox removed";
                                SafeInvoke(Cashbox_Removed);
                                try { SyncCoinFeederByIdle(false); } catch { }
                                break;
                            case CCommands.SSP_POLL_CASHBOX_REPLACED:
                                Ereignis_adden("Cashbox replaced");
                                state = "Cashbox replaced";
                                // Dialog mit Auswahl anzeigen (UI-Thread beachten!)
                                System.Windows.Forms.Application.OpenForms[0]?.BeginInvoke((Action)(() =>
                                {
                                    var prompt = new TaMi_Einzahlautomat.CashboxActionPromptForm(this, "Cashbox");
                                    prompt.Show();
                                }));
                                // NEU: Event auslösen, damit UIs (z.B. KassensturzNV200) sofort aktualisieren
                                SafeInvoke(Cashbox_Replaced, 0);
                                try { SyncCoinFeederByIdle(false); } catch { }
                                break;
                            case CCommands.SSP_POLL_DISPENSING:
                                Ereignis_adden("Dispensing...");
                                _isDispensing = true; // Auszahlung läuft
                                try { SyncCoinFeederByIdle(false); } catch { }
                                if (i + 5 < CommandStructure.ResponseDataLength)
                                {
                                    // Laut VB-Implementierung ist der ausgezahlte Gesamtwert ab i+2..i+5 zu lesen
                                    int dispensed = CommandStructure.ResponseData[i + 2]
                                                 + (CommandStructure.ResponseData[i + 3] << 8)
                                                 + (CommandStructure.ResponseData[i + 4] << 16)
                                                 + (CommandStructure.ResponseData[i + 5] << 24);
                                    int last = (int)Last_Dispensed_count;
                                    int delta = dispensed - last;
                                    if (delta != 0)
                                    {
                                        // Den Delta-Betrag melden (Cent)
                                        SafeInvoke(Wert_Dispensing, delta);

                                        // Pro Schein loggen (Delta ist der Wert in Cent)
                                        try
                                        {
                                            AppLogger.Log($"({DetermineNvSection()}) ---> Schein ausgezahlt: {(delta / 100.0):0.00} €");
                                        }
                                        catch { }

                                        // Interne To_Payout_* Zähler reduzieren, damit Noch_Verfuegbar stimmt
                                        switch (delta)
                                        {
                                            case 500: if (To_Payout_5Euro > 0) To_Payout_5Euro--; break;
                                            case 1000: if (To_Payout_10Euro > 0) To_Payout_10Euro--; break;
                                            case 2000: if (To_Payout_20Euro > 0) To_Payout_20Euro--; break;
                                            case 5000: if (To_Payout_50Euro > 0) To_Payout_50Euro--; break;
                                            case 10000: if (To_Payout_100Euro > 0) To_Payout_100Euro--; break;
                                            case 20000: if (To_Payout_200Euro > 0) To_Payout_200Euro--; break;
                                            case 50000: if (To_Payout_500Euro > 0) To_Payout_500Euro--; break;
                                        }

                                        // NEU: Eingezahlt sofort um den ausgezahlten Betrag verringernmpr

                                        try
                                        {
                                            Eingezahlt = Math.Max(0, Eingezahlt - delta);
                                            Ereignis_adden($"Eingezahlt minus Auszahlung: {(Eingezahlt / 100.0):0.00} €");
                                        }
                                        catch { }

                                        Last_Dispensed_count = dispensed;
                                    }
                                    // i um 4 erhöhen, da 4 Payload-Bytes (nach code und count) gelesen wurden
                                    i += 4;
                                }
                                else
                                {
                                    SafeInvoke(Wert_Dispensing, 0);
                                }
                                break;
                            case CCommands.SSP_POLL_DISPENSED:
                                Ereignis_adden("Dispense complete");
                                _isDispensing = false; // Auszahlung beendet
                                try { SyncCoinFeederByIdle(false); } catch { }
                                {
                                    int paid = 0;
                                    if (i + 5 < CommandStructure.ResponseDataLength)
                                    {
                                        // wie oben: kumulative Summe an Position i+2..i+5
                                        paid = CommandStructure.ResponseData[i + 2]
                                             + (CommandStructure.ResponseData[i + 3] << 8)
                                             + (CommandStructure.ResponseData[i + 4] << 16)
                                             + (CommandStructure.ResponseData[i + 5] << 24);
                                        i += 4;
                                    }

                                    // NEU: Fallback - falls im DISPENSING kein Delta erkannt wurde
                                    if (paid > 0 && Last_Dispensed_count == 0)
                                    {
                                        try { AppLogger.Log($"({DetermineNvSection()}) ---> Schein ausgezahlt: {(paid / 100.0):0.00} €"); } catch { }
                                        try
                                        {
                                            Eingezahlt = Math.Max(0, Eingezahlt - paid);
                                            Ereignis_adden($"Eingezahlt minus Auszahlung: {(Eingezahlt / 100.0):0.00} €");
                                        }
                                        catch { }
                                        Last_Dispensed_count = paid;
                                    }
                                    int neu = Noch_Verfuegbar();
                                    try
                                    {
                                        int total = paid > 0 ? paid : neu;
                                        AppLogger.Log($"({DetermineNvSection()}) Auszahlung abgeschlossen: {(total / 100.0):0.00} €");
                                    }
                                    catch { }
                                    SafeInvoke(Dispensing_Complete, paid > 0 ? paid : neu);

                                    // NEU: direkt nach Abschluss die Payout-Level synchronisieren
                                    try { Payout_angleichen(); } catch { }
                                }
                                break;
                            case CCommands.SSP_POLL_INCOMPLETE_PAYOUT:
                                Ereignis_adden("Incomplete payout");
                                SafeInvoke(Error_while_payout, 0, Noch_Verfuegbar());
                                try { SyncCoinFeederByIdle(false); } catch { }
                                // Auszahlung gilt als beendet/unterbrochen
                                _isDispensing = false;
                                // Variable L 3angeloesung analog VB: i += ((ResponseData(i+1) * 7) + 1)
                                try { int setCount = CommandStructure.ResponseData[i + 1]; i += (setCount * 7) + 1; }
                                catch { }
                                break;
                            case CCommands.SSP_POLL_TIME_OUT:
                                Ereignis_adden("Payout timeout");
                                SafeInvoke(Payout_Timeout, 0, Noch_Verfuegbar());
                                try { SyncCoinFeederByIdle(false); } catch { }
                                _isDispensing = false;
                                try { int setCount = CommandStructure.ResponseData[i + 1]; i += (setCount * 7) + 1; }
                                catch { }
                                break;
                            case CCommands.SSP_POLL_JAMMED:
                                Ereignis_adden("Payout jammed");
                                SafeInvoke(Jammed, 0, Noch_Verfuegbar());
                                try { SyncCoinFeederByIdle(false); } catch { }
                                _isDispensing = false;
                                try { int setCount = CommandStructure.ResponseData[i + 1]; i += (setCount * 7) + 1; }
                                catch { }
                                break;
                            case CCommands.SSP_POLL_HALTED:
                                Ereignis_adden("Payout halted");
                                SafeInvoke(Halted, 0, Noch_Verfuegbar());
                                try { SyncCoinFeederByIdle(false); } catch { }
                                _isDispensing = false;
                                try { int setCount = CommandStructure.ResponseData[i + 1]; i += (setCount * 7) + 1; }
                                catch { }
                                break;
                            default:
                                // andere Events ignorieren/loggen
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Ereignis_adden("POLL parse error: " + ex.Message);
                }
            }
        }

        // Hilfsfunktion: Kanal -> Euro
        private int ChannelValueByChannel(int channel)
        {
            switch (channel)
            {
                case 1: return Channel1_Value;
                case 2: return Channel2_Value;
                case 3: return Channel3_Value;
                case 4: return Channel4_Value;
                case 5: return Channel5_Value;
                case 6: return Channel6_Value;
                case 7: return Channel7_Value;
                case 8: return Channel8_Value;
                default: return 0;
            }
        }


        // Neu: Note_stored_or_Stacked - zählt strikt nach tatsächlicher Route (Payout/Cashbox) und normalisiert Euro/Cent
        private void Note_stored_or_Stacked(int noteChannel, bool storedInPayout)
        {
            int raw = ChannelValueByChannel(noteChannel); // kann 5/10/€ oder 500/1000/€ (Cent) sein
            if (raw <= 0) { Current_read_Note = -1; return; }

            // Wenn kein Mitarbeiter eingeloggt ist, Schein rejecten!
            if (!MitarbeiterEingeloggt)
            {
                Ereignis_adden("Einzahlung abgelehnt: Kein Mitarbeiter eingeloggt!");
                RejectNote();
                Current_read_Note = -1;
                return;
            }

            // Normalisieren
            int denomCent = raw >= 1000 ? raw : raw * 100;
            int denomEuro = raw >= 1000 ? (raw / 100) : raw;

            // Für jeden Wert: exakt dort zählen, wo der Schein gelandet ist
            void incByRoute(ref int payout, ref int cashbox)
            {
                Poll();

                if (storedInPayout)
                {
                    payout = Math.Max(0, payout) + 1;
                }
                else
                {
                    cashbox = Math.Max(0, cashbox) + 1;
                    SaveCashboxSnapshot();
                }

                // zukünftiges Routing dynamisch anpassen
                bool toPayoutNext = ShouldStackToPayout(denomCent);
                ChangeNoteRoute(denomCent, toPayoutNext);
                Ereignis_adden($"{denomEuro} Euro akzepted ({(storedInPayout ? "PAYOUT" : "CASHBOX")})");
                SafeInvoke(Note_akzepted, denomEuro);

                Eingezahlt += denomEuro * 100;
                Ereignis_adden($"Eingezahlt gesamt: {Eingezahlt / 100.0:0.00} €");
            }

            switch (denomEuro)
            {
                case 5:   incByRoute(ref Payout_5_euro,   ref Cashbox_5_euro);   break;
                case 10:  incByRoute(ref Payout_10_euro,  ref Cashbox_10_euro);  break;
                case 20:  incByRoute(ref Payout_20_euro,  ref Cashbox_20_euro);  break;
                case 50:  incByRoute(ref Payout_50_euro,  ref Cashbox_50_euro);  break;
                case 100: incByRoute(ref Payout_100_euro, ref Cashbox_100_euro); break;
                case 200: incByRoute(ref Payout_200_euro, ref Cashbox_200_euro); break;
                case 500: incByRoute(ref Payout_500_euro, ref Cashbox_500_euro); break;
                default:
                    Ereignis_adden($"Warnung: Unbekannter Nennwert (raw={raw}) € Zähler unverändert.");
                    break;
            }

            Current_read_Note = -1;
        }

        // Neu: Rejektions-Code abfragen
        public void QueryRejection()
        {
            var element = new List<byte>();
            element.Add(1); // Encryption
            element.Add(1); // Data length
            element.Add(CCommands.SSP_CMD_LAST_REJECT_CODE);
            Sendeliste.Add(element);
        }

        // Rohdaten in die interne Sendeliste legen.
        // Achtung: Das Format muss [EncFlag][DataLen][Cmd][Payload...] sein.
        public void SendRaw(byte[] data)
        {
            if (data == null || data.Length < 3)
            {
                SafeInvoke(Ereignis, "SendRaw: Daten zu kurz (<3 Bytes).");
                return;
            }
            Sendeliste.Add(new List<byte>(data));
        }

        // Routing einer Denomination setzen (in Payout oder Cashbox).
        public void ChangeNoteRoute(int note, bool stack_to_Payout)
        {
            // Während einer Auszahlung keine Route-änderungen senden, um Busy/245 zu vermeiden
            if (_isDispensing)
            {
                if (_verbosePollLogging) try { Ereignis_adden("Skip route change (dispensing)"); } catch { }
                return;
            }
            var element = new List<byte>();
            element.Add(1); // Encryption
            element.Add(9); // Data length
            element.Add(CCommands.SSP_CMD_SET_DENOMINATION_ROUTE);
            element.Add(stack_to_Payout ? (byte)0 : (byte)1); // 0 => payout, 1 => cashbox
            byte[] b = BitConverter.GetBytes(note);
            element.Add(b[0]);
            element.Add(b[1]);
            element.Add(b[2]);
            element.Add(b[3]);
            element.Add(69); // 'E'
            element.Add(85); // 'U'
            element.Add(82); // 'R'
            Sendeliste.Add(element);

            try { Ereignis_adden($"Set route {note / 100}€ -> {(stack_to_Payout ? "PAYOUT" : "CASHBOX")}"); } catch { }
        }
        
        // replace auto-property with backing field to allow logout action
        private bool _mitarbeiterEingeloggt = false;
        public bool MitarbeiterEingeloggt
        {
            get => _mitarbeiterEingeloggt;
            set
            {
                // if transitioning from true -> false, ensure coinfeeder LEDs are turned off
                if (_mitarbeiterEingeloggt && !value)
                {
                    try { TurnOffCoinFeederBoth(); } catch { }
                    // also allow SyncCoinFeederByIdle to resend an off command if needed
                    try { _lastIdleState = null; } catch { }
                }
                _mitarbeiterEingeloggt = value;
            }
        }

        public int Eingezahlt { get; private set; } = 0;
        // NEU: Schein zurückgeben (Reject)
        private void RejectNote() 
        {
            var element = new List<byte>();
            element.Add(1); // Encryption
            element.Add(1); // Data length
            element.Add(CCommands.SSP_CMD_REJECT_BANKNOTE);
            Sendeliste.Add(element);
        }

        // Optional: falls noch nicht vorhanden - für automatische Routenwahl.
        public void Set_Routing()
        {
            // Während einer Auszahlung keine Routing-Massenupdates senden
            if (_isDispensing)
            {
                if (_verbosePollLogging) try { Ereignis_adden("Skip Set_Routing (dispensing)"); } catch { }
                return;
            }
            ChangeNoteRoute(500,   Payout_5_euro   < MAX_PayoutCount_of_5Euro);
            ChangeNoteRoute(1000,  Payout_10_euro  < MAX_PayoutCount_of_10Euro);
            ChangeNoteRoute(2000,  Payout_20_euro  < MAX_PayoutCount_of_20Euro);
            ChangeNoteRoute(5000,  Payout_50_euro  < MAX_PayoutCount_of_50Euro);
            ChangeNoteRoute(10000, Payout_100_euro < MAX_PayoutCount_of_100Euro);
            ChangeNoteRoute(20000, Payout_200_euro < MAX_PayoutCount_of_200Euro);
            ChangeNoteRoute(50000, Payout_500_euro < MAX_PayoutCount_of_500Euro);
        }

        // Beleuchtung (Bezel) konfigurieren and synchronize CoinFeeder LEDs
        public void ConfigureBezel(byte red, byte green, byte blue)
        {
            var element = new List<byte>();
            element.Add(1); // Encryption
            element.Add(5); // Data length
            element.Add(CCommands.SSP_CMD_CONFIGURE_BEZEL);
            element.Add(red);
            element.Add(green);
            element.Add(blue);
            element.Add(0);
            Sendeliste.Add(element);

            try
            {
                var sig = string.Format("{0},{1},{2}", red, green, blue);
                if (sig == _lastBezelSig) return;
                _lastBezelSig = sig;

                // Keep bezel configuration for the NV200 device only.
                // Do NOT send CoinFeeder commands from here - CoinFeeder must be driven solely by SyncCoinFeederByIdle
                // so remove feeder.SendTemplate calls that previously caused multiple sends from different code paths.
            }
            catch { }
        }

        // Gerät deaktivieren
        public void Disable_Device()
        {
            var element = new List<byte>();
            element.Add(1); // Encryption
            element.Add(1);
            element.Add(CCommands.SSP_CMD_DISABLE);
            Sendeliste.Add(element);

            // clear last idle state so SyncCoinFeederByIdle will send an update (turn off LEDs)
            try { _lastIdleState = null; } catch { }
            _isDisabled = true;
        }

        // Handshake-Schritt: Host->Request Key Exchange (8 Byte)
        public void Do_Key_Exchange(SSP_KEYS keys)
        {
            var element = new List<byte>();
            element.Add(0); // No encryption
            element.Add(9); // Data length
            element.Add(CCommands.SSP_CMD_REQUEST_KEY_EXCHANGE);
            byte[] array = BitConverter.GetBytes(keys.HostInter);
            for (int i = 0; i < 8; i++) element.Add(array[i]);
            Sendeliste.Add(element);
        }

        // Handshake-Schritt: Generator setzen
        public void Set_up_Generator(SSP_KEYS keys)
        {
            var element = new List<byte>();
            element.Add(0); // No encryption
            element.Add(9); // Data length
            element.Add(CCommands.SSP_CMD_SET_GENERATOR);
            byte[] array = BitConverter.GetBytes(keys.Generator);
            for (int i = 0; i < 8; i++) element.Add(array[i]);
            Sendeliste.Add(element);
        }

        // Handshake-Schritt: Modulus setzen
        public void Set_up_Modulus(SSP_KEYS keys)
        {
            var element = new List<byte>();
            element.Add(0); // No encryption
            element.Add(9); // Data length
            element.Add(CCommands.SSP_CMD_SET_MODULUS);
            byte[] array = BitConverter.GetBytes(keys.Modulus);
            for (int i = 0; i < 8; i++) element.Add(array[i]);
            Sendeliste.Add(element);
        }

        // Poll-Befehl
        public void Poll()
        {
            var element = new List<byte>();
            element.Add(0); // No encryption
            element.Add(1); // Data length
            element.Add(CCommands.SSP_CMD_POLL);
            Sendeliste.Add(element);
        }

        // Sync-Befehl
        public void sync()
        {
            var element = new List<byte>();
            element.Add(0); // No encryption
            element.Add(1); // Data length
            element.Add(CCommands.SSP_CMD_SYNC);
            Sendeliste.Add(element);
        }

        // Setup-Request
        public void PayoutSetupRequest()
        {
            var element = new List<byte>();
            element.Add(1); // Encryption
            element.Add(1); // Data length
            element.Add(CCommands.SSP_CMD_SETUP_REQUEST);
            Sendeliste.Add(element);
        }

        // Validator aktivieren
        public void Enable_Device()
        {
            var element = new List<byte>();
            element.Add(1); // Encryption
            element.Add(1);
            element.Add(CCommands.SSP_CMD_ENABLE);
            Sendeliste.Add(element);
            _isDisabled = false;
        }

        // Payout-Gerät aktivieren
        public void Enable_Payout_Device()
        {
            var element = new List<byte>();
            element.Add(1); // Encryption
            element.Add(1);
            element.Add(CCommands.SSP_CMD_ENABLE_PAYOUT_DEVICE);
            Sendeliste.Add(element);
            _isDisabled = false;
        }

        // Seriennummer anfordern
        public void GetSerialNumber()
        {
            var element = new List<byte>();
            element.Add(1); // Encryption
            element.Add(1);
            element.Add(CCommands.SSP_CMD_GET_SERIAL_NUMBER);
            Sendeliste.Add(element);
        }

        // Protokollversion setzen
        public void SetProtocolVersion(byte pVersion)
        {
            var element = new List<byte>();
            element.Add(1); // Encryption
            element.Add(2); // Data length
            element.Add(CCommands.SSP_CMD_HOST_PROTOCOL_VERSION);
            element.Add(pVersion);
            Sendeliste.Add(element);
        }

        // Inhibits setzen (hier: alle Kanäle erlauben; bei Bedarf feiner steuern)
        public void SetInhibits()
        {
            SetChannelInhibits(0xFF, 0xFF);
        }

        // Gerät gezielt inhibitieren oder freigeben (alle Channels)
        public void SetInhibit(bool inhibit)
        {
            // true => sperren (0x00/0x00), false => erlauben (0xFF/0xFF)
            SetChannelInhibits(inhibit ? (byte)0x00 : (byte)0xFF, inhibit ? (byte)0x00 : (byte)0xFF);
        }

        // Hilfsmethode für Channel-Inhibits (0x00/0x00 = gesperrt, 0xFF/0xFF = freigegeben)
        private void SetChannelInhibits(byte low, byte high)
        {
            var element = new List<byte>();
            element.Add(1); // Encryption
            element.Add(3); // Data length
            element.Add(CCommands.SSP_CMD_SET_CHANNEL_INHIBITS);
            element.Add(low);   // Low 8 channels
            element.Add(high);  // High 8 channels
            Sendeliste.Add(element);

            try { Ereignis_adden($"SetChannelInhibits: low=0x{low:X2}, high=0x{high:X2}"); } catch { }
        }

        // Komplette Freigabe-Sequenz wie im VB-Code
        public void Freigeben()
        {
            PayoutSetupRequest();
            SetValueReportingType(1); // WICHTIG: auf Value stellen
            SetInhibits();
            GetSerialNumber();
            Enable_Device();
            Enable_Payout_Device();
            Set_Routing();
            //ApplyManualRouteOverrides(); // deaktiviert - dynamisches Routing
            // NEU: Nach erfolgreicher Freigabe LED auf grün
            ConfigureBezel(0, 255, 0);
        }

        // Bestände angleichen (falls noch nicht vorhanden)
        public void Payout_angleichen()
        {
            // Während Banknote verarbeitet wird oder Auszahlung läuft: keine Maintenance-Kommandos senden
            if (_noteCycleActive || _isDispensing)
            {
                try { Ereignis_adden("Skip angleichen: busy (note/dispense)"); } catch { }
                return;
            }

            var now = DateTime.UtcNow;
            if (_lastLevelReqUtc != DateTime.MinValue && (now - _lastLevelReqUtc) < _minLevelInterval)
            {
                // Drosseln, um Bus-Flooding zu vermeiden
                return;
            }
            _lastLevelReqUtc = now;

            CheckNoteLevel(500);
            CheckNoteLevel(1000);
            CheckNoteLevel(2000);
            CheckNoteLevel(5000);
            CheckNoteLevel(10000);
            CheckNoteLevel(20000);
            CheckNoteLevel(50000);
        }

        // Level einer Denomination anfragen (falls noch nicht vorhanden)
        public void CheckNoteLevel(int note)
        {
            var element = new List<byte>();
            element.Add(1); // Encryption
            element.Add(8); // Data length
            element.Add(CCommands.SSP_CMD_GET_DENOMINATION_LEVEL);
            byte[] b = BitConverter.GetBytes(note);
            element.Add(b[0]);
            element.Add(b[1]);
            element.Add(b[2]);
            element.Add(b[3]);
            element.Add(69); // 'E'
            element.Add(85); // 'U'
            element.Add(82); // 'R'
            Sendeliste.Add(element);
        }

        // PAYOUT_BY_DENOMINATION im VB-Format senden: Count(2) + Value(4) + 'EUR' + Flag(88)
        public void PayoutByDenomination(int[] counts)
        {
            // Sicherstellen, dass das Gerät aktiv ist
            Enable_Device();
            Enable_Payout_Device();
            Thread.Sleep(200); // kurze Wartezeit

            Ereignis_adden("DEBUG: PayoutByDenomination aufgerufen");
            if (counts == null || counts.Length < 7)
            {
                Ereignis_adden("PayoutByDenomination: ungültige Zähl-Liste.");
                return;
            }

            Ereignis_adden($"DEBUG: counts = {string.Join(",", counts)}");

            // To_Payout_* setzen
            To_Payout_5Euro = counts[0];
            To_Payout_10Euro = counts[1];
            To_Payout_20Euro = counts[2];
            To_Payout_50Euro = counts[3];
            To_Payout_100Euro = counts[4];
            To_Payout_200Euro = counts[5];
            To_Payout_500Euro = counts[6];

            // Summe in Cent merken (für Fallback)
            _lastRequestedPayoutSumCent =
                  counts[0]*500  + counts[1]*1000 + counts[2]*2000 +
                  counts[3]*5000 + counts[4]*10000+ counts[5]*20000+
                  counts[6]*50000;

            // Denoms sammeln
            var denoms = new List<(int valueCent, ushort count)>
            {
                (500,  (ushort)Math.Min(65535, Math.Max(0, counts[0]))),
                (1000, (ushort)Math.Min(65535, Math.Max(0, counts[1]))),
                (2000, (ushort)Math.Min(65535, Math.Max(0, counts[2]))),
                (5000, (ushort)Math.Min(65535, Math.Max(0, counts[3]))),
                (10000,(ushort)Math.Min(65535, Math.Max(0, counts[4]))),
                (20000,(ushort)Math.Min(65535, Math.Max(0, counts[5]))),
                (50000,(ushort)Math.Min(65535, Math.Max(0, counts[6]))),
            };
            denoms = denoms.FindAll(d => d.count > 0);

            if (denoms.Count == 0)
            {
                Ereignis_adden("PayoutByDenomination: keine Stückzahlen > 0 gewählt.");
                return;
            }

            // Länge: 1(cmd) + 1(versch. Scheine) + N*(2(count)+4(value)+3('EUR')) + 1(flag)
            int len = 3 + denoms.Count * 9;

            var element = new List<byte>();
            element.Add(1);                 // Encryption
            element.Add((byte)len);         // Data length
            element.Add(CCommands.SSP_CMD_PAYOUT_BY_DENOMINATION);
            element.Add((byte)denoms.Count);

            foreach (var d in denoms)
            {
                // Count 2-Byte little-endian
                element.Add((byte)(d.count & 0xFF));
                element.Add((byte)((d.count >> 8) & 0xFF));

                // Value 4-Byte little-endian
                var v = BitConverter.GetBytes(d.valueCent);
                element.Add(v[0]); element.Add(v[1]); element.Add(v[2]); element.Add(v[3]);

                // 'E','U','R'
                element.Add(69); element.Add(85); element.Add(82);
            }

            element.Add(88); // Real Payout (VB nutzt 88; 25 wäre Test)

            Ereignis_adden($"Queue PAYOUT_BY_DENOMINATION: 5x{counts[0]},10x{counts[1]},20x{counts[2]},50x{counts[3]},100x{counts[4]},200x{counts[5]},500x{counts[6]} (Len={len})");

            // Debug-Bytes loggen
            var dbg = "PBD bytes: ";
            for (int i = 0; i < element.Count; i++) dbg += element[i] + (i + 1 < element.Count ? "-" : "");
            Ereignis_adden(dbg);

            // Für Dispensing-Delta wie im VB
            Last_Dispensed_count = 0;

            Sendeliste.Add(element);
            Ereignis_adden($"Starte Auszahlung (Denoms: {denoms.Count}).");
        }

        // Optional: Fallback auf PAYOUT_AMOUNT (0x33) - zahlt nur die Summe aus.
        public void PayoutAmountFromQueuedDenoms()
        {
            Enable_Device();
            Enable_Payout_Device();
            Thread.Sleep(200); // kurze Wartezeit

            // Primär die zuletzt angeforderte Summe verwenden,
            // da To_Payout_* im Fehlerfall evtl. schon 0 sind.
            int sum = _lastRequestedPayoutSumCent;

            if (sum <= 0)
            {
                // Fallback: aus To_Payout_* berechnen, falls vorhanden
                sum = To_Payout_5Euro * 500
                    + To_Payout_10Euro * 1000
                    + To_Payout_20Euro * 2000
                    + To_Payout_50Euro * 5000
                    + To_Payout_100Euro * 10000
                    + To_Payout_200Euro * 20000
                    + To_Payout_500Euro * 50000;
            }

            if (sum <= 0)
            {
                Ereignis_adden("PayoutAmount: Summe = 0, Abbruch.");
                return;
            }

            var v = BitConverter.GetBytes(sum);
            var element = new List<byte>();
            element.Add(1);
            element.Add(1 + 4 + 3 + 1);
            element.Add(CCommands.SSP_CMD_PAYOUT_AMOUNT);
            element.Add(v[0]); element.Add(v[1]); element.Add(v[2]); element.Add(v[3]);
            element.Add(69); element.Add(85); element.Add(82);
            element.Add(0); // Test=0

            Ereignis_adden($"Queue PAYOUT_AMOUNT: {sum/100.0:0.00} EUR");
            Sendeliste.Add(element);
        }

        // Wertemodus setzen: 1 = Value (anstelle von Channel)
        public void SetValueReportingType(byte type)
        {
            var element = new List<byte>();
            element.Add(1); // Encryption
            element.Add(2); // Data length
            element.Add(CCommands.SSP_CMD_SET_VALUE_REPORTING_TYPE);
            element.Add(type); // 1 = Value, 0 = Channel
            Sendeliste.Add(element);
        }

        // Start SMART_EMPTY: moves all payout notes into cashbox, keeping counts
        public void SmartEmptyToCashbox()
        {
            // nicht während Auszahlung starten
            if (_isDispensing) { try { Ereignis_adden("Skip SMART_EMPTY: dispensing active"); } catch { } return; }
            var element = new List<byte>();
            element.Add(1); // Encryption
            element.Add(1); // Data length
            element.Add(CCommands.SSP_CMD_SMART_EMPTY);
            Sendeliste.Add(element);
            try { Ereignis_adden("SMART_EMPTY angefordert (Payout -> Cashbox)"); } catch { }
            _emptyingActive = true; // mark emptying cycle active
        }

        // Optional: EMPTY_ALL (klassisch), falls Firmware unterstützt
        public void EmptyAllToCashbox()
        {
            if (_isDispensing) { try { Ereignis_adden("Skip EmptyAll: dispensing active"); } catch { } return; }
            var element = new List<byte>();
            element.Add(1);
            element.Add(1);
            element.Add(CCommands.SSP_CMD_EMPTY_ALL);
            Sendeliste.Add(element);
            try { Ereignis_adden("EMPTY_ALL angefordert (Payout -> Cashbox)"); } catch { }
            _emptyingActive = true; // mark emptying cycle active
        }

        // Thread-sicheres Logging: Feld hinzufügen
        private readonly object _logLock = new object();

        // Field to avoid repeated CoinFeeder sends for same bezel color
        private string _lastBezelSig = null;

        // Intervall-Steuerung für detailliertes Rohframe-Logging (0 = jede Antwort, >0 ms = Drossel)
        private int _frameLogIntervalMs = 500;
        public void StartLiveFrameLogging() { _frameLogIntervalMs = 0; _logDetailedFrames = true; }
        public void EndLiveFrameLogging() { _frameLogIntervalMs = 500; _logDetailedFrames = false; }

        // 1. Feld ergänzen:
        private bool _isDispensing = false;
        // Track emptying state (SMART_EMPTY / EMPTY_ALL)
        private bool _emptyingActive = false;

        // Field to remember last Idle state to avoid duplicate feeder sends
        private bool? _lastIdleState = null;

        // Send coinfeeder command based on Idle/non-Idle state
        private void SyncCoinFeederByIdle(bool isIdle)
        {
            // LED-Steuerung wurde in den zentralen CoinFeederCoordinator verlagert.
            // Diese Methode ist absichtlich leer, um doppelte Kommandos zu vermeiden.
            try { _lastIdleState = isIdle; } catch { }
        }

        private void TurnOffCoinFeederBoth()
        {
            // Keine direkten CoinFeeder-Kommandos mehr aus dem NV200-Treiber senden.
        }

        private void TransferPayoutToCashbox()
        {
            // Move all payout counts into cashbox counts and clear payout, then persist snapshot
            try
            {
                int moved5 = Payout_5_euro;
                int moved10 = Payout_10_euro;
                int moved20 = Payout_20_euro;
                int moved50 = Payout_50_euro;
                int moved100 = Payout_100_euro;
                int moved200 = Payout_200_euro;
                int moved500 = Payout_500_euro;

                if (moved5 + moved10 + moved20 + moved50 + moved100 + moved200 + moved500 == 0)
                {
                    Ereignis_adden("SMART_EMPTY abgeschlossen: Keine Payout-Scheine zu übertragen.");
                    return;
                }

                // Detailed log per note moved
                try
                {
                    string section = DetermineNvSection();
                    string ts() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    void logMove(decimal eur)
                    {
                        AppLogger.Log($"{ts()} | ({section}) Payout ---> Cashbox Schein {eur:0.00} €");
                    }
                    for (int i = 0; i < moved5; i++)   logMove(5m);
                    for (int i = 0; i < moved10; i++)  logMove(10m);
                    for (int i = 0; i < moved20; i++)  logMove(20m);
                    for (int i = 0; i < moved50; i++)  logMove(50m);
                    for (int i = 0; i < moved100; i++) logMove(100m);
                    for (int i = 0; i < moved200; i++) logMove(200m);
                    for (int i = 0; i < moved500; i++) logMove(500m);
                }
                catch { }

                Cashbox_5_euro   = Math.Max(0, Cashbox_5_euro)   + Math.Max(0, moved5);
                Cashbox_10_euro  = Math.Max(0, Cashbox_10_euro)  + Math.Max(0, moved10);
                Cashbox_20_euro  = Math.Max(0, Cashbox_20_euro)  + Math.Max(0, moved20);
                Cashbox_50_euro  = Math.Max(0, Cashbox_50_euro)  + Math.Max(0, moved50);
                Cashbox_100_euro = Math.Max(0, Cashbox_100_euro) + Math.Max(0, moved100);
                Cashbox_200_euro = Math.Max(0, Cashbox_200_euro) + Math.Max(0, moved200);
                Cashbox_500_euro = Math.Max(0, Cashbox_500_euro) + Math.Max(0, moved500);

                Payout_5_euro = 0;
                Payout_10_euro = 0;
                Payout_20_euro = 0;
                Payout_50_euro = 0;
                Payout_100_euro = 0;
                Payout_200_euro = 0;
                Payout_500_euro = 0;

                SaveCashboxSnapshot();
                Ereignis_adden($"SMART_EMPTY abgeschlossen: Cashbox-Bestand aktualisiert (neu: {GetCashboxSumCent()/100.0:0.00} €)");
            }
            catch (Exception ex)
            {
                try { Ereignis_adden("TransferPayoutToCashbox Fehler: " + ex.Message); } catch { }
            }
        }

        // Persistenz (Registry) für Cashbox-Zähler
        private readonly object _persistLock = new object();
        private string CashboxRegPath => @"Software\Geldautomat\NV200\" + (ComPort ?? "Unknown");

        private void CheckCashboxOverLimitAndAlert()
        {
            try
            {
                int nowCount = 0;
                try
                {
                    nowCount =
                        Math.Max(0, Cashbox_5_euro) +
                        Math.Max(0, Cashbox_10_euro) +
                        Math.Max(0, Cashbox_20_euro) +
                        Math.Max(0, Cashbox_50_euro) +
                        Math.Max(0, Cashbox_100_euro) +
                        Math.Max(0, Cashbox_200_euro) +
                        Math.Max(0, Cashbox_500_euro);
                }
                catch { nowCount = 0; }

                // Default: 1000er Cashbox => Warnung ab 900 Scheinen
                int limitCount = 900;
                try
                {
                    var sec = DetermineNvSection();
                    string typ = null;
                    try { typ = IniHelper.ReadValue(sec, "CashboxType", AppSettings.IniPath); } catch { typ = null; }
                    // Allowed: "500" or "1000"
                    if (!string.IsNullOrWhiteSpace(typ))
                    {
                        var t = typ.Trim();
                        if (t == "500") limitCount = 450;
                        else if (t == "1000") limitCount = 900;
                    }
                }
                catch { limitCount = 900; }

                int prevCount = -1;
                bool prevValid = false;

                lock (_persistLock)
                {
                    using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(CashboxRegPath))
                    {
                        if (key != null)
                        {
                            try
                            {
                                prevCount = Convert.ToInt32(key.GetValue("CashboxSumCount", -1));
                                prevValid = prevCount >= 0;
                                key.SetValue("CashboxSumCount", nowCount, Microsoft.Win32.RegistryValueKind.DWord);
                            }
                            catch { prevValid = false; }
                        }
                    }
                }

                if (!prevValid) return;

                if (prevCount <= limitCount && nowCount > limitCount)
                {
                    int cashboxIndex = 1;
                    try
                    {
                        var sec = DetermineNvSection();
                        if (!string.IsNullOrWhiteSpace(sec) && sec.EndsWith("/2", StringComparison.Ordinal)) cashboxIndex = 2;
                    }
                    catch { cashboxIndex = 1; }

                    string device = (AppSettings.AutomatenName ?? string.Empty).Trim();
                    string body =
                        "Hinweis: Cashbox-Füllstand überschritten.\r\n\r\n" +
                        "Gerät: " + device + "\r\n" +
                        "Cashbox: " + cashboxIndex + "\r\n" +
                        "Anzahl Scheine: " + nowCount + "\r\n" +
                        "Limit: " + limitCount + "\r\n\r\n" +
                        "Bitte Cashbox " + cashboxIndex + " leeren.";

                    EmailReceiptService.SendAlertOnly("Cashbox leeren", body);
                }
            }
            catch { }
        }

        private void SaveCashboxSnapshot()
        {
            try
            {
                lock (_persistLock)
                {
                    using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(CashboxRegPath))
                    {
                        if (key == null) return;
                        key.SetValue("Cashbox_5",   Cashbox_5_euro,   Microsoft.Win32.RegistryValueKind.DWord);
                        key.SetValue("Cashbox_10",  Cashbox_10_euro,  Microsoft.Win32.RegistryValueKind.DWord);
                        key.SetValue("Cashbox_20",  Cashbox_20_euro,  Microsoft.Win32.RegistryValueKind.DWord);
                        key.SetValue("Cashbox_50",  Cashbox_50_euro,  Microsoft.Win32.RegistryValueKind.DWord);
                        key.SetValue("Cashbox_100", Cashbox_100_euro, Microsoft.Win32.RegistryValueKind.DWord);
                        key.SetValue("Cashbox_200", Cashbox_200_euro, Microsoft.Win32.RegistryValueKind.DWord);
                        key.SetValue("Cashbox_500", Cashbox_500_euro, Microsoft.Win32.RegistryValueKind.DWord);
                        key.SetValue("UpdatedUtc",  DateTime.UtcNow.Ticks, Microsoft.Win32.RegistryValueKind.QWord);
                    }
                }

                try { CheckCashboxOverLimitAndAlert(); } catch { }
            }
            catch { /* robust gegen Ausnahmen */ }
        }

        private void LoadCashboxSnapshot()
        {
            try
            {
                lock (_persistLock)
                {
                    using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(CashboxRegPath, false))
                    {
                        if (key == null) return;
                        Cashbox_5_euro   = Convert.ToInt32(key.GetValue("Cashbox_5",   Cashbox_5_euro));
                        Cashbox_10_euro  = Convert.ToInt32(key.GetValue("Cashbox_10",  Cashbox_10_euro));
                        Cashbox_20_euro  = Convert.ToInt32(key.GetValue("Cashbox_20",  Cashbox_20_euro));
                        Cashbox_50_euro  = Convert.ToInt32(key.GetValue("Cashbox_50",  Cashbox_50_euro));
                        Cashbox_100_euro = Convert.ToInt32(key.GetValue("Cashbox_100", Cashbox_100_euro));
                        Cashbox_200_euro = Convert.ToInt32(key.GetValue("Cashbox_200", Cashbox_200_euro));
                        Cashbox_500_euro = Convert.ToInt32(key.GetValue("Cashbox_500", Cashbox_500_euro));
                    }
                }
                try { Ereignis_adden($"Cashbox aus Registry geladen: {GetCashboxSumCent()/100.0:0.00} €"); } catch { }
            }
            catch { /* falls Registry nicht verfügbar, weiter */ }
        }

        // Summenhelfer (Snapshots der aktuellen Bestände)
        public int GetPayoutSumCent()
        {
            int p5   = System.Threading.Volatile.Read(ref Payout_5_euro);
            int p10  = System.Threading.Volatile.Read(ref Payout_10_euro);
            int p20  = System.Threading.Volatile.Read(ref Payout_20_euro);
            int p50  = System.Threading.Volatile.Read(ref Payout_50_euro);
            int p100 = System.Threading.Volatile.Read(ref Payout_100_euro);
            int p200 = System.Threading.Volatile.Read(ref Payout_200_euro);
            int p500 = System.Threading.Volatile.Read(ref Payout_500_euro);

            return p5 * 500
                 + p10 * 1000
                 + p20 * 2000
                 + p50 * 5000
                 + p100 * 10000
                 + p200 * 20000
                 + p500 * 50000;
        }

        public int GetCashboxSumCent()
        {
            int c5   = System.Threading.Volatile.Read(ref Cashbox_5_euro);
            int c10  = System.Threading.Volatile.Read(ref Cashbox_10_euro);
            int c20  = System.Threading.Volatile.Read(ref Cashbox_20_euro);
            int c50  = System.Threading.Volatile.Read(ref Cashbox_50_euro);
            int c100 = System.Threading.Volatile.Read(ref Cashbox_100_euro);
            int c200 = System.Threading.Volatile.Read(ref Cashbox_200_euro);
            int c500 = System.Threading.Volatile.Read(ref Cashbox_500_euro);

            return c5 * 500
                 + c10 * 1000
                 + c20 * 2000
                 + c50 * 5000
                 + c100 * 10000
                 + c200 * 20000
                 + c500 * 50000;
        }

        public int GetTotalNotesSumCent() => GetPayoutSumCent() + GetCashboxSumCent();
        public decimal GetTotalNotesSumEuro() => GetTotalNotesSumCent() / 100m;

        // Entscheidet, ob eine Note in den Payout-Store geroutet werden soll
        private bool ShouldStackToPayout(int denomCent)
        {
            // Nur dynamisch anhand MAX-Levels und aktuellem Payout-Bestand entscheiden
            switch (denomCent)
            {
                case 500:   return Payout_5_euro   < MAX_PayoutCount_of_5Euro;
                case 1000:  return Payout_10_euro  < MAX_PayoutCount_of_10Euro;
                case 2000:  return Payout_20_euro  < MAX_PayoutCount_of_20Euro;
                case 5000:  return Payout_50_euro  < MAX_PayoutCount_of_50Euro;
                case 10000: return Payout_100_euro < MAX_PayoutCount_of_100Euro;
                case 20000: return Payout_200_euro < MAX_PayoutCount_of_200Euro;
                case 50000: return Payout_500_euro < MAX_PayoutCount_of_500Euro;
                default:    return true;
            }
        }

        private int GetPayoutCountFor(int denomCent)
        {
            switch (denomCent)
            {
                case 500:   return Payout_5_euro;
                case 1000:  return Payout_10_euro;
                case 2000:  return Payout_20_euro;
                case 5000:  return Payout_50_euro;
                case 10000: return Payout_100_euro;
                case 20000: return Payout_200_euro;
                case 50000: return Payout_500_euro;
                default:    return -1;
            }
        }

        private int GetMaxFor(int denomCent)
        {
            switch (denomCent)
            {
                case 500:   return MAX_PayoutCount_of_5Euro;
                case 1000:  return MAX_PayoutCount_of_10Euro;
                case 2000:  return MAX_PayoutCount_of_20Euro;
                case 5000:  return MAX_PayoutCount_of_50Euro;
                case 10000: return MAX_PayoutCount_of_100Euro;
                case 20000: return MAX_PayoutCount_of_200Euro;
                case 50000: return MAX_PayoutCount_of_500Euro;
                default:    return -1;
            }
        }

        // Stub, falls die INI-Variante (IniHelper) hier nicht vorhanden ist
        public void LoadMaxConfigFromIni()
        {
            try
            {
                string section = DetermineNvSection();

                int ReadInt(string key, int def)
                {
                    try
                    {
                        string s = IniHelper.ReadValue(section, key, AppSettings.IniPath);
                        if (int.TryParse(s, out var v)) return v;
                    }
                    catch { }
                    return def;
                }

                // MAX-Konfig: mehrere mögliche Schlüsselnamen unterstützen
                int v5   = ReadInt("MAX_5",   MAX_PayoutCount_of_5Euro);
                int v10  = ReadInt("MAX_10",  MAX_PayoutCount_of_10Euro);
                int v20  = ReadInt("MAX_20",  MAX_PayoutCount_of_20Euro);
                int v50  = ReadInt("MAX_50",  MAX_PayoutCount_of_50Euro);
                int v100 = ReadInt("MAX_100", MAX_PayoutCount_of_100Euro);
                int v200 = ReadInt("MAX_200", MAX_PayoutCount_of_200Euro);
                int v500 = ReadInt("MAX_500", MAX_PayoutCount_of_500Euro);

                // Fallback-Schlüssel (falls andere Namen genutzt werden)
                v5   = ReadInt("MAX_PayoutCount_of_5Euro",   v5);
                v10  = ReadInt("MAX_PayoutCount_of_10Euro",  v10);
                v20  = ReadInt("MAX_PayoutCount_of_20Euro",  v20);
                v50  = ReadInt("MAX_PayoutCount_of_50Euro",  v50);
                v100 = ReadInt("MAX_PayoutCount_of_100Euro", v100);
                v200 = ReadInt("MAX_PayoutCount_of_200Euro", v200);
                v500 = ReadInt("MAX_PayoutCount_of_500Euro", v500);

                MAX_PayoutCount_of_5Euro   = Math.Max(0, v5);
                MAX_PayoutCount_of_10Euro  = Math.Max(0, v10);
                MAX_PayoutCount_of_20Euro  = Math.Max(0, v20);
                MAX_PayoutCount_of_50Euro  = Math.Max(0, v50);
                MAX_PayoutCount_of_100Euro = Math.Max(0, v100);
                MAX_PayoutCount_of_200Euro = Math.Max(0, v200);
                MAX_PayoutCount_of_500Euro = Math.Max(0, v500);

                try
                {
                    Ereignis_adden($"INI geladen ({section}): MAX=5:{MAX_PayoutCount_of_5Euro},10:{MAX_PayoutCount_of_10Euro},20:{MAX_PayoutCount_of_20Euro},50:{MAX_PayoutCount_of_50Euro},100:{MAX_PayoutCount_of_100Euro},200:{MAX_PayoutCount_of_200Euro},500:{MAX_PayoutCount_of_500Euro}");
                }
                catch { }
            }
            catch { }
        }

        // Welche NV200/Section? -> anhand COM-Port bestimmen
        private string DetermineNvSection()
        {
            try
            {
                string s1 = IniHelper.ReadValue("NV200/1", "ComPort", AppSettings.IniPath);
                string s2 = IniHelper.ReadValue("NV200/2", "ComPort", AppSettings.IniPath);
                if (!string.IsNullOrWhiteSpace(ComPort))
                {
                    if (!string.IsNullOrWhiteSpace(s1) && string.Equals(s1, ComPort, StringComparison.OrdinalIgnoreCase)) return "NV200/1";
                    if (!string.IsNullOrWhiteSpace(s2) && string.Equals(s2, ComPort, StringComparison.OrdinalIgnoreCase)) return "NV200/2";
                }
            }
            catch { }
            return "NV200/1"; // Default
        }

        // Ordnername für Logs dynamisch aus NV200-Section ableiten
        private string DetermineNvFolderName()
        {
            try
            {
                var section = DetermineNvSection(); // "NV200/1" oder "NV200/2"
                if (section.EndsWith("/2", StringComparison.Ordinal)) return "Nv200_2";
                return "Nv200_1"; // Default
            }
            catch
            {
                return "Nv200_1";
            }
        }

        public void SetCashboxCounts(int c5, int c10, int c20, int c50, int c100, int c200, int c500)
        {
            Cashbox_5_euro   = Math.Max(0, c5);
            Cashbox_10_euro  = Math.Max(0, c10);
            Cashbox_20_euro  = Math.Max(0, c20);
            Cashbox_50_euro  = Math.Max(0, c50);
            Cashbox_100_euro = Math.Max(0, c100);
            Cashbox_200_euro = Math.Max(0, c200);
            Cashbox_500_euro = Math.Max(0, c500);
            SaveCashboxSnapshot();
            Ereignis_adden($"Cashbox gezällt: {GetCashboxSumCent() / 100.0:0.00} €");
}

        // Graceful stop (called on shutdown)
        public void Stopp()
        {
            try { Disable_Device(); } catch { }
            try { _lastIdleState = null; } catch { }
            try { TurnOffCoinFeederBoth(); } catch { }
            try { Thread.Sleep(300); } catch { }
            Beenden = true;
        }

        // NEU: öffentliche Hilfsfunktion für Admin-Form: Log-Dateipfad liefern
        public string GetProtocolLogFilePath()
        {
            try { return ProtokollFile; } catch { return null; }
        }

        // NEU: vollständigen Log-Snapshot liefern (begrenzt). Wenn fromFile=true, direkt aus Datei tailen.
        public List<string> GetFullLogSnapshot(int maxLines = 5000, bool fromFile = true)
        {
            var list = new List<string>();
            try
            {
                if (fromFile && File.Exists(ProtokollFile))
                {
                    // Effizient nur letzte maxLines laden
                    var all = new List<string>();
                    using (var fs = new FileStream(ProtokollFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var sr = new StreamReader(fs))
                    {
                        string line;
                        while ((line = sr.ReadLine()) != null) all.Add(line);
                    }
                    int skip = Math.Max(0, all.Count - maxLines);
                    for (int i = skip; i < all.Count; i++) list.Add(all[i]);
                }
                else
                {
                    // Aus interner Liste
                    int skip = Math.Max(0, Ereignisliste.Count - maxLines);
                    for (int i = skip; i < Ereignisliste.Count; i++)
                    {
                        var el = Ereignisliste[i];
                        try
                        {
                            if (el.Count >= 3)
                                list.Add(el[0] + "-" + el[1] + "-" + el[2]);
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return list;
        }
    }

    // Hilfsmethode, da ITLlib.SSP_COMMAND.ResponseData als Indexer genutzt wird.
    internal static class SSPCommandExtensions
    {
        public static byte ResponseData(this SSP_COMMAND cmd, int index)
        {
            return cmd.ResponseData[index];
        }
    }

}