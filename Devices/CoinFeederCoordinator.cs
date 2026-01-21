using System;
using System.Threading;
using TaMi_Einzahlautomat.Coins; // RM5 detection

namespace TaMi_Einzahlautomat.Devices
{
    internal static class CoinFeederCoordinator
    {
        private static readonly object _lock = new object();
        private static Timer _timer;
        private static string _lastCmdA; // physical channel 'a' (now mapped to NV200/2)
        private static string _lastCmdB; // physical default channel (now mapped to NV200/1)
        private static DateTime _lastSendA = DateTime.MinValue;
        private static DateTime _lastSendB = DateTime.MinValue;
        // legacy debounce (retained for potential future use)
        private static readonly TimeSpan _sendDebounce = TimeSpan.FromMilliseconds(400);
        private static bool _running = false;

        // NEW: Track last login state to send 2001$/2002$ only on transitions
        private static bool _hasLastLoginState = false;
        private static bool _lastLoginState = false; // true = logged in -> 2001$, false = logged out -> 2002$

        public static bool IsRunning { get { lock (_lock) { return _running && _timer != null; } } }

        // Only resend identical command after refresh interval (keep LEDs alive)
        private static readonly TimeSpan _refreshInterval = TimeSpan.FromMinutes(2);

        // Dynamic interval (slower with RM5)
        private static int _intervalMs = 1500; // default

        // Backoff for reopen attempts
        private static DateTime _lastOpenAttemptUtc = DateTime.MinValue;
        private static DateTime _lastOpenFailedUtc = DateTime.MinValue;
        private static int _consecutiveOpenFails = 0;
        private static readonly TimeSpan ShortBackoff = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan LongBackoff = TimeSpan.FromMinutes(1);
        private const int FailThresholdForLongBackoff = 3;

        public static void Start()
        {
            lock (_lock)
            {
                if (_running) return;
                _intervalMs = DetermineIntervalMs();
                if (_intervalMs <= 0)
                {
                    try { AppLogger.Log("CoinFeederCoordinator: deaktiviert (Intervall <=0)"); } catch { }
                    return; // disabled by configuration
                }
                _running = true;
                _timer = new Timer(Tick, null, 1000, _intervalMs);
                try { AppLogger.Log($"CoinFeederCoordinator gestartet (Intervall {_intervalMs}ms, Mapping: NV200/1->default, NV200/2->'a')"); } catch { }
            }
        }

        public static void Stop()
        {
            lock (_lock)
            {
                _running = false;
                try { _timer?.Dispose(); } catch { }
                _timer = null;
                try { AppLogger.Log("CoinFeederCoordinator gestoppt"); } catch { }
            }
        }

        private static int DetermineIntervalMs()
        {
            try
            {
                string overrideStr = IniHelper.ReadValue("CoinFeeder", "CoordinatorIntervalMs", AppSettings.IniPath);
                if (!string.IsNullOrWhiteSpace(overrideStr) && int.TryParse(overrideStr, out int cfg) && cfg >= 0)
                {
                    return cfg; // 0 => disabled
                }
            }
            catch { }

            bool rm5Present = false;
            try
            {
                var c1 = CoinManager.Instance;
                var c2 = Coin2Manager.Instance;
                if (c1 is Rm5CctalkValidator || c2 is Rm5CctalkValidator) rm5Present = true;
            }
            catch { }

            if (rm5Present)
            {
                try
                {
                    var dis = IniHelper.ReadValue("CoinFeeder", "DisableCoordinatorForRm5", AppSettings.IniPath);
                    if (!string.IsNullOrWhiteSpace(dis) && (dis == "1" || dis.Equals("true", StringComparison.OrdinalIgnoreCase)))
                        return 0;
                }
                catch { }
                return 7000; // slow cadence with RM5
            }

            return 1500; // standard
        }

        private static void Tick(object state)
        {
            try
            {
                var feeder = Program.CoinFeeder;
                if (feeder == null) return;

                EnsureFeederOpenSafe();
                if (!feeder.IsOpen) return;

                var nv1 = Program.NV200Instance; // default channel (no 'a')
                var nv2 = Program.NV2002Instance; // channel 'a'

                var s1 = nv1?.states ?? string.Empty;
                var s2 = nv2?.states ?? string.Empty;

                bool ready1 = IsReadyState(s1);
                bool ready2 = IsReadyState(s2);

                // NEW: Send 2001$ / 2002$ on login-state change when RM5 is present
                bool rm5PresentNow = false;
                try
                {
                    var c1 = CoinManager.Instance;
                    var c2 = Coin2Manager.Instance;
                    if (c1 is Rm5CctalkValidator || c2 is Rm5CctalkValidator) rm5PresentNow = true;
                }
                catch { }

                if (rm5PresentNow)
                {
                    bool loggedIn = false;
                    try { loggedIn = (nv1?.MitarbeiterEingeloggt == true) || (nv2?.MitarbeiterEingeloggt == true); } catch { }

                    if (!_hasLastLoginState || loggedIn != _lastLoginState)
                    {
                        SafeSend(loggedIn ? "2001$" : "2002$");
                        try { AppLogger.Log("CoinFeederCoordinator: " + (loggedIn ? "2001$ (Freigabe)" : "2002$ (Sperre)")); } catch { }
                        _lastLoginState = loggedIn;
                        _hasLastLoginState = true;
                    }
                }

                // Map: ready -> green (2005/2005a), busy/error -> red (2004/2004a)
                string cmdA = null; // NV200/2
                string cmdB = null; // NV200/1

                if (nv2 != null)
                    cmdA = ready2 ? "2005a$" : "2004a$";
                if (nv1 != null)
                    cmdB = ready1 ? "2005$" : "2004$";

                var now = DateTime.UtcNow;
                bool anySent = false;

                if (!string.IsNullOrEmpty(cmdA))
                {
                    if (!string.Equals(_lastCmdA, cmdA, StringComparison.OrdinalIgnoreCase) || (now - _lastSendA) >= _refreshInterval)
                    {
                        SafeSend(cmdA);
                        _lastCmdA = cmdA;
                        _lastSendA = now;
                        anySent = true;
                    }
                }
                if (!string.IsNullOrEmpty(cmdB))
                {
                    if (!string.Equals(_lastCmdB, cmdB, StringComparison.OrdinalIgnoreCase) || (now - _lastSendB) >= _refreshInterval)
                    {
                        SafeSend(cmdB);
                        _lastCmdB = cmdB;
                        _lastSendB = now;
                        anySent = true;
                    }
                }

                if (anySent)
                {
                    try { AppLogger.Log($"CoinFeederCoordinator: LED update (NV200/2->A={_lastCmdA}, NV200/1->Default={_lastCmdB})"); } catch { }
                }
            }
            catch (Exception ex)
            {
                try { AppLogger.Log("CoinFeederCoordinator Tick error: " + ex.Message); } catch { }
            }
        }

        private static bool IsReadyState(string state)
        {
            try
            {
                var t = (state ?? string.Empty).ToLowerInvariant();
                if (t.Length == 0) return true; // unknown -> assume ready to avoid forcing red on both
                if (t.Contains("idle") || t.Contains("bereit") || t.Contains("started") || t.Contains("connected") || t.Contains("synchron"))
                    return true;
                if (t.Contains("disabled") || t.Contains("jammed") || t.Contains("halted") || t.Contains("failed") || t.Contains("timeout") || t.Contains("open sspcomport") || t.Contains("neustart"))
                    return false;
                if (t.Contains("dispens") || t.Contains("stack") || t.Contains("reading") || t.Contains("read note") || t.Contains("bez"))
                    return false;
                return true; // default to ready
            }
            catch { return true; }
        }

        private static void EnsureFeederOpenSafe()
        {
            try
            {
                var feeder = Program.CoinFeeder;
                if (feeder == null) return;
                if (feeder.IsOpen) return;

                var now = DateTime.UtcNow;
                TimeSpan needWait = _consecutiveOpenFails >= FailThresholdForLongBackoff ? LongBackoff : ShortBackoff;
                if ((now - _lastOpenAttemptUtc) < needWait)
                    return;

                _lastOpenAttemptUtc = now;
                string sel = !string.IsNullOrWhiteSpace(feeder.PortName) ? feeder.PortName : IniHelper.ReadValue("CoinFeeder", "ComPort", AppSettings.IniPath);
                if (string.IsNullOrWhiteSpace(sel)) return;

                if (!string.Equals(feeder.PortName, sel, StringComparison.OrdinalIgnoreCase))
                {
                    try { feeder.Close(); } catch { }
                    feeder.Configure(sel);
                }
                feeder.Mode = CoinFeederProtocolMode.ASCII;
                feeder.Terminator = "\n"; // unified terminator
                feeder.AppendTerminatorAfterDollar = true;
                try
                {
                    feeder.Open();
                    _consecutiveOpenFails = 0;
                    try { AppLogger.Log("CoinFeederCoordinator: Port geöffnet (Reopen) auf " + sel); } catch { }
                }
                catch (Exception openEx)
                {
                    _consecutiveOpenFails++;
                    _lastOpenFailedUtc = now;
                    try { AppLogger.Log($"CoinFeederCoordinator: Reopen fehlgeschlagen ({_consecutiveOpenFails}): " + openEx.Message); } catch { }
                }
            }
            catch (Exception ex)
            {
                try { AppLogger.Log("EnsureFeederOpenSafe failed: " + ex.Message); } catch { }
            }
        }

        private static void SafeSend(string template)
        {
            try
            {
                var feeder = Program.CoinFeeder;
                if (feeder == null || !feeder.IsOpen) return;
                feeder.SendTemplate(template);
            }
            catch (Exception ex)
            {
                try { AppLogger.Log("CoinFeeder send failed: " + ex.Message); } catch { }
            }
        }
    }
}
