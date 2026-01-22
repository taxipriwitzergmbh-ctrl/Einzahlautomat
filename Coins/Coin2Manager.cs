using System;
using System.Threading;
using System.Threading.Tasks;

namespace TaMi_Einzahlautomat.Coins {
    public static class Coin2Manager {
        private static readonly object _lock = new object();
        private static bool _initialized = false;
        // Update default INI path to Einzahlautomat.ini
        private static readonly string DefaultIniPath = @"C:\\ProgramData\\SuE-Software\\SuE-TaMi Client SQL\\Einzahlautomat.ini";

        public static ICoinValidator Instance {
            get;
            set;
        }
        // Reconnect/Watchdog
        private static int _pendingReconnect = 0;
        private static DateTime _lastReconnectAttemptUtc = DateTime.MinValue;
        private static volatile bool _shuttingDown = false;
        private static volatile bool _finalized = false; // NEW: block any new init/reconnect permanently once shutting down
        private static CancellationTokenSource _watchCts;
        private static Task _watchTask;
        private static CancellationTokenSource _reconnectCts; // NEW: cancel a currently running reconnect sequence
        private static Task _reconnectTask;
        private const int WatchdogIntervalMs = 3000;
        private const int MaxInitialWatchdogRuntimeSec = 60;
        private static DateTime _initStartUtc;

        private static void Log(string msg) {
            try {
                AppLogger .Log("[Coin2] " + msg);
            }

            catch {
            }
        }
        // NEW: configuration based disable (e.g. RM5 or single SmartCoin setup)
        private static bool _disabledByConfig = false;
        private static DateTime _lastNoTypeLogUtc = DateTime.MinValue; // throttle for "None" log
        private const int NoTypeRepeatMinutes = 10; // only repeat info every 10 minutes
        private static bool _loggedNoTypeOnce = false;
        // NEW: respect Admin form global disable
        private static bool IsGloballyDisabled() {
            try {
                return TaMi_Einzahlautomat.AdminCoin2Form.DeviceDisabled;
            }

            catch {
                return false;
            }
        }

        public static bool IsDisabledByConfig => _disabledByConfig;

        public static void DisableForConfig() {
            lock (_lock) {
                if (_disabledByConfig) return;
                _disabledByConfig = true;
                _shuttingDown = true; // block running loops
                StopWatchdog();
                CancelActiveReconnect();

                try {
                    if (Instance is SmartCoinV1 sc1) {
                        try {
                            sc1 .StatusChanged -= OnStatusChanged;
                        }

                        catch {
                        }

                        try {
                            sc1 .Enable(false);
                        }

                        catch {
                        }

                        try {
                            sc1 .HardDisconnect();
                        }

                        catch {
                        }

                        try {
                            sc1 .SetStatus("konfig deaktiviert");
                        }

                        catch {
                        }
                    }

                    else {
                        try {
                            Instance ?.Disconnect();
                        }

                        catch {
                        }
                    }
                }

                catch {
                }

                finally {
                    Instance = null;
                    _initialized = false;
                    _shuttingDown = false; // allow later re-enable
                    System .Threading.Interlocked.Exchange(ref _pendingReconnect, 0);
                    Log("Durch Konfiguration deaktiviert (Watchdog/Reconnect gestoppt)");
                }
            }
        }

        public static void EnableForConfig(string iniPath = null) {
            lock (_lock) {
                if (!_disabledByConfig) return; // already enabled
                _disabledByConfig = false;
                Log("Konfig-Deaktivierung aufgehoben - Re-Init wird gestartet");
                // Reset spam flags so we can log meaningful first message again if still None
                _loggedNoTypeOnce = false;
                _lastNoTypeLogUtc = DateTime.MinValue;

                try {
                    InitFromIni(iniPath);
                }

                catch (Exception ex) {
                    Log("EnableForConfig Init Fehler: " + ex.Message);
                }
            }
        }

        public static void InitFromIni(string iniPath = null) {
            lock (_lock) {
                if (_disabledByConfig) {
                    // completely disabled: do nothing
                    return;
                }
                // also respect global UI-disabled flag
                if (IsGloballyDisabled()) return;

                if (_finalized) { /* nach Programmende keine Logs mehr erzeugen */
                    return;
                }

                if (_initialized && Instance != null) return;
                _shuttingDown = false;
                _initStartUtc = DateTime.UtcNow;
                // Use Einzahlautomat.ini by default
                var path = string.IsNullOrWhiteSpace(iniPath) ? DefaultIniPath : iniPath;

                try {
                    var typeStr = IniHelper.ReadValue("SmartCoin/2", "Typ", path)?.Trim() ?? "";

                    if (string.IsNullOrWhiteSpace(typeStr) || typeStr.Equals("None", StringComparison.OrdinalIgnoreCase)) {
                        // Mark as config-disabled so we stop repeated init attempts and logs
                        _disabledByConfig = true;
                        bool shouldLog = false;

                        if (!_loggedNoTypeOnce) {
                            shouldLog = true;
                            _loggedNoTypeOnce = true;
                        }

                        else if ((DateTime.UtcNow - _lastNoTypeLogUtc).TotalMinutes >= NoTypeRepeatMinutes) {
                            shouldLog = true; // periodic reminder
                        }

                        if (shouldLog) {
                            _lastNoTypeLogUtc = DateTime.UtcNow;
                            Log("[SmartCoin/2] Typ leer oder 'None' - zweite Instanz deaktiviert");
                        }

                        _initialized = false;
                        Instance = null;
                        return;
                    }

                    var com = IniHelper.ReadValue("SmartCoin/2", "ComPort", path)?.Trim() ?? "";
                    var addrStr = IniHelper.ReadValue("SmartCoin/2", "SSPAddress", path)?.Trim();
                    Log($"INI SmartCoin/2: Typ='{typeStr}', ComPort='', SSPAddress='{addrStr}'");
                    var type = CoinValidatorFactory.ParseType(typeStr);
                    var inst = CoinValidatorFactory.Create(type);

                    if (inst == null) {
                        Log("Kein gültiger Typ - Fallback auf SmartCoinV1");
                        inst = new SmartCoinV1();
                    }

                    if (!string.IsNullOrWhiteSpace(com)) inst.ComPort = com;
                    else Log("WARN: Kein ComPort in [SmartCoin/2] gefunden.");
                    if (int.TryParse(addrStr, out var addr) && addr > 0) inst.SspAddress = addr;
                    else Log("INFO: Keine/ungültige SSPAddress - Standard wird verwendet.");
                    if (inst is SmartCoinV1 sc1) sc1.StatusChanged += OnStatusChanged;

                    try {
                        inst .Connect();
                        Log("Connect() auf Gerät 2 initiiert.");
                    }

                    catch (Exception ex) {
                        Log("Connect-Fehler initial: " + ex.Message);
                    }

                    Instance = inst;
                    _disabledByConfig = false; // active now
                    StartWatchdog();
                }

                catch (Exception ex) {
                    Log("InitFromIni Fehler: " + ex.Message);
                }

                finally {
                    _initialized = Instance != null;
                }
            }
        }

        private static void StartWatchdog() {
            try {
                _watchCts ?.Cancel();
            }

            catch {
            }

            _watchCts = new CancellationTokenSource();
            var ct = _watchCts.Token;
            _watchTask = Task.Run(async () => {
                Log("Watchdog gestartet");
                while (!ct.IsCancellationRequested && !_shuttingDown && !_finalized)
                {
                    if (_disabledByConfig) break; // stop if disabled
                    if (IsGloballyDisabled()) break; // stop if globally disabled by UI
                    try
                    {
                        var inst = Instance as SmartCoinV1;
                        if (inst != null)
                        {
                            string status = inst.CurrentStatus ?? string.Empty;
                            bool unhealthy = status.IndexOf("port nicht verf", System.StringComparison.OrdinalIgnoreCase) >= 0
                                             || status.IndexOf("verbindung unterbrochen", System.StringComparison.OrdinalIgnoreCase) >= 0
                                             || (inst.Connected && status.IndexOf("neustart", System.StringComparison.OrdinalIgnoreCase) >= 0);
                            if (unhealthy || !inst.Connected)
                            {
                                if ((DateTime.UtcNow - _initStartUtc).TotalSeconds <= MaxInitialWatchdogRuntimeSec)
                                    OnStatusChanged(string.IsNullOrWhiteSpace(status) ? "verbindung unterbrochen (watchdog)" : status);
                            }
                        }
                    }
                    catch (Exception ex) { Log("Watchdog Fehler: " + ex.Message); }
                    await Task.Delay(WatchdogIntervalMs, ct).ConfigureAwait(false);
                }
                Log("Watchdog beendet");
            }, ct)
            ;
        }

        private static void StopWatchdog() {
            try {
                _watchCts ?.Cancel();
            }

            catch {
            }

            try {
                _watchTask ?.Wait(1000);
            }

            catch {
            }

            _watchCts = null;
            _watchTask = null;
        }

        private static void CancelActiveReconnect() {
            try {
                _reconnectCts ?.Cancel();
            }

            catch {
            }

            try {
                _reconnectTask ?.Wait(500);
            }

            catch {
            }

            _reconnectTask = null;
            _reconnectCts = null;
            System .Threading.Interlocked.Exchange(ref _pendingReconnect, 0);
        }

        private static void OnStatusChanged(string status) {
            if (_disabledByConfig) return; // ignore when disabled by config
            if (IsGloballyDisabled()) return; // ignore when globally disabled via UI
            if (_shuttingDown || _finalized) return;
            if (string.IsNullOrWhiteSpace(status)) return;
            var s = status.ToLowerInvariant();

            if (s.Contains("port nicht verf") || s.Contains("verbindung unterbrochen") || s.Contains("neustart")) {
                if (System.Threading.Interlocked.CompareExchange(ref _pendingReconnect, 1, 0) != 0) return;

                if ((DateTime.UtcNow - _lastReconnectAttemptUtc).TotalSeconds < 5) {
                    System .Threading.Interlocked.Exchange(ref _pendingReconnect, 0);
                    return;
                }

                _lastReconnectAttemptUtc = DateTime.UtcNow;
                CancelActiveReconnect(); // sicherstellen nur EIN Task aktiv
                _reconnectCts = new CancellationTokenSource();
                var token = _reconnectCts.Token;
                _reconnectTask = Task.Run(async () => {
                    try
                    {
                        var inst = Instance as SmartCoinV1;
                        if (inst == null) return;
                        await Task.Delay(800, token).ConfigureAwait(false);
                        if (token.IsCancellationRequested || _shuttingDown || _finalized || _disabledByConfig || IsGloballyDisabled()) return;
                        try { inst.HardDisconnect(); Log("Auto-Reconnect: HardDisconnect"); } catch { }
                        await Task.Delay(700, token).ConfigureAwait(false);
                        if (token.IsCancellationRequested || _shuttingDown || _finalized || _disabledByConfig || IsGloballyDisabled()) return;
                        try { inst.Connect(); Log("Auto-Reconnect: Connect"); } catch (System.Exception ex) { Log("Auto-Reconnect Connect-Fehler: " + ex.Message); }
                        await Task.Delay(800, token).ConfigureAwait(false);
                        if (token.IsCancellationRequested || _shuttingDown || _finalized || _disabledByConfig || IsGloballyDisabled()) return;
                        try { inst.Enable(true); Log("Auto-Reconnect: Enable"); } catch { }
                    }
                    finally
                    {
                        System.Threading.Interlocked.Exchange(ref _pendingReconnect, 0);
                    }
                }, token)
                ;
            }
        }

        public static void Enable(bool enable) {
            try {
                if (!_shuttingDown && !_finalized && !_disabledByConfig && !IsGloballyDisabled()) Instance?.Enable(enable);
            }

            catch {
            }
        }

        public static void EnsureReconnect() {
            try {
                if (_shuttingDown || _finalized || _disabledByConfig || IsGloballyDisabled()) return;
                if (Instance is SmartCoinV1 sc && !sc.Connected) OnStatusChanged("verbindung unterbrochen (manual ensure)");
            }

            catch {
            }
        }

        public static void Shutdown() {
            lock (_lock) {
                _shuttingDown = true; // block future tasks
                _finalized = true; // block future init
                StopWatchdog();
                CancelActiveReconnect();
                // Detach & disconnect
                var inst = Instance as SmartCoinV1;

                if (inst != null) {
                    try {
                        inst .StatusChanged -= OnStatusChanged;
                    }

                    catch {
                    }

                    try {
                        inst .Enable(false);
                    }

                    catch {
                    }

                    try {
                        inst .HardDisconnect();
                    }

                    catch {
                    }

                    try {
                        inst .SetStatus("getrennt");
                    }

                    catch {
                    }
                }

                else {
                    try {
                        Instance ?.Disconnect();
                    }

                    catch {
                    }
                }

                Instance = null;
                _initialized = false;
                System .Threading.Interlocked.Exchange(ref _pendingReconnect, 0);
            }
        }
    }
}
