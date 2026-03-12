using System;
using System.Threading;
using System.Threading.Tasks;

namespace TaMi_Einzahlautomat.Coins
{
    // Zentraler Singleton f�r das M�nzger�t. Baut die Session (Connect) einmalig auf.
    public static class CoinManager
    {
        private static readonly object _lock = new object();
        private static bool _initialized = false;
        private static readonly string DefaultIniPath = @"C:\\ProgramData\\SuE-Software\\SuE-TaMi Client SQL\\Einzahlautomat.ini";
        public static ICoinValidator Instance { get; private set; }

        // Reconnect/Watchdog (align Coin/1 with Coin/2 robustness)
        private static int _pendingReconnect = 0;
        private static DateTime _lastReconnectAttemptUtc = DateTime.MinValue;
        private static volatile bool _shuttingDown = false;
        private static volatile bool _finalized = false;
        private static CancellationTokenSource _watchCts;
        private static Task _watchTask;
        private static CancellationTokenSource _reconnectCts;
        private static Task _reconnectTask;
        private const int WatchdogIntervalMs = 3000;
        private const int MaxInitialWatchdogRuntimeSec = 60;
        private static DateTime _initStartUtc;

        static CoinManager() { }

        private static void Log(string msg)
        {
            try { AppLogger.Log("[Coin1] " + msg); } catch { }
        }

        private static void StartWatchdog()
        {
            try { _watchCts?.Cancel(); } catch { }
            _watchCts = new CancellationTokenSource();
            var ct = _watchCts.Token;
            _watchTask = Task.Run(async () =>
            {
                Log("Watchdog gestartet");
                while (!ct.IsCancellationRequested && !_shuttingDown && !_finalized)
                {
                    try
                    {
                        var inst = Instance as SmartCoinV1;
                        if (inst != null)
                        {
                            string status = inst.CurrentStatus ?? string.Empty;
                            bool unhealthy = status.IndexOf("port nicht verf", StringComparison.OrdinalIgnoreCase) >= 0
                                             || status.IndexOf("verbindung unterbrochen", StringComparison.OrdinalIgnoreCase) >= 0
                                             || (inst.Connected && status.IndexOf("neustart", StringComparison.OrdinalIgnoreCase) >= 0);
                            if (unhealthy || !inst.Connected)
                            {
                                if ((DateTime.UtcNow - _initStartUtc).TotalSeconds <= MaxInitialWatchdogRuntimeSec)
                                    OnStatusChanged(string.IsNullOrWhiteSpace(status) ? "verbindung unterbrochen (watchdog)" : status);
                            }
                        }
                    }
                    catch (Exception ex) { Log("Watchdog Fehler: " + ex.Message); }

                    try { await Task.Delay(WatchdogIntervalMs, ct).ConfigureAwait(false); }
                    catch { }
                }
                Log("Watchdog beendet");
            }, ct);
        }

        private static void StopWatchdog()
        {
            try { _watchCts?.Cancel(); } catch { }
            try { _watchTask?.Wait(1000); } catch { }
            _watchCts = null;
            _watchTask = null;
        }

        private static void CancelActiveReconnect()
        {
            try { _reconnectCts?.Cancel(); } catch { }
            try { _reconnectTask?.Wait(500); } catch { }
            _reconnectTask = null;
            _reconnectCts = null;
            Interlocked.Exchange(ref _pendingReconnect, 0);
        }

        private static void OnStatusChanged(string status)
        {
            if (_shuttingDown || _finalized) return;
            if (string.IsNullOrWhiteSpace(status)) return;
            var s = status.ToLowerInvariant();

            if (s.Contains("port nicht verf") || s.Contains("verbindung unterbrochen") || s.Contains("neustart"))
            {
                if (Interlocked.CompareExchange(ref _pendingReconnect, 1, 0) != 0) return;

                if ((DateTime.UtcNow - _lastReconnectAttemptUtc).TotalSeconds < 5)
                {
                    Interlocked.Exchange(ref _pendingReconnect, 0);
                    return;
                }

                _lastReconnectAttemptUtc = DateTime.UtcNow;
                CancelActiveReconnect();
                _reconnectCts = new CancellationTokenSource();
                var token = _reconnectCts.Token;
                _reconnectTask = Task.Run(async () =>
                {
                    try
                    {
                        var inst = Instance as SmartCoinV1;
                        if (inst == null) return;
                        await Task.Delay(800, token).ConfigureAwait(false);
                        if (token.IsCancellationRequested || _shuttingDown || _finalized) return;
                        try { inst.HardDisconnect(); Log("Auto-Reconnect: HardDisconnect"); } catch { }
                        await Task.Delay(700, token).ConfigureAwait(false);
                        if (token.IsCancellationRequested || _shuttingDown || _finalized) return;
                        try { inst.Connect(); Log("Auto-Reconnect: Connect"); } catch (Exception ex) { Log("Auto-Reconnect Connect-Fehler: " + ex.Message); }
                        await Task.Delay(800, token).ConfigureAwait(false);
                        if (token.IsCancellationRequested || _shuttingDown || _finalized) return;
                        try { inst.Enable(true); Log("Auto-Reconnect: Enable"); } catch { }
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _pendingReconnect, 0);
                    }
                }, token);
            }
        }

        public static void InitFromIni(string iniPath)
        {
            lock (_lock)
            {
                if (_initialized && Instance != null) return;
                _shuttingDown = false;
                _initStartUtc = DateTime.UtcNow;
                var path = string.IsNullOrWhiteSpace(iniPath) ? DefaultIniPath : iniPath;

                // SmartCoin/1 analog zu SmartCoin/2 initialisieren, damit der ComPort nicht durch Defaults (z.B. COM3)
                // oder durch einen anderen/generischen INI-Abschnitt unbeabsichtigt zurückgesetzt wird.
                var typeStr = IniHelper.ReadValue("SmartCoin/1", "Typ", path)?.Trim() ?? "";
                var com = IniHelper.ReadValue("SmartCoin/1", "ComPort", path)?.Trim() ?? "";
                var addrStr = IniHelper.ReadValue("SmartCoin/1", "SSPAddress", path)?.Trim();

                var type = CoinValidatorFactory.ParseType(typeStr);
                var inst = CoinValidatorFactory.Create(type);
                if (inst == null) inst = new SmartCoinV1();

                if (!string.IsNullOrWhiteSpace(com)) inst.ComPort = com;
                if (type == CoinValidatorType.Rm5Cctalk) inst.SspAddress = 2;
                else if (int.TryParse(addrStr, out var addr) && addr > 0) inst.SspAddress = addr;

                Instance = inst;
                if (Instance is SmartCoinV1 scInst)
                {
                    try { scInst.StatusChanged += OnStatusChanged; } catch { }
                }
                try { Instance.Connect(); } catch { }
                _initialized = true;
                try { StartWatchdog(); } catch { }
            }
        }

        public static void Enable(bool enable)
        {
            try { Instance?.Enable(enable); } catch { }
        }

        public static void Shutdown()
        {
            lock (_lock)
            {
                _shuttingDown = true;
                _finalized = true;
                StopWatchdog();
                CancelActiveReconnect();
                try
                {
                    if (Instance is SmartCoinV1 sc1)
                    {
                        try { sc1.StatusChanged -= OnStatusChanged; } catch { }
                        try { sc1.Enable(false); } catch { }
                        try { sc1.HardDisconnect(); } catch { }
                        try { sc1.SetStatus("getrennt"); } catch { }
                    }
                    else
                    {
                        try { Instance?.Disconnect(); } catch { }
                    }
                }
                catch { }
                finally
                {
                    Instance = null;
                    _initialized = false;
                }
            }
        }
    }
}