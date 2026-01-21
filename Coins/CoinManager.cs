using System;

namespace TaMi_Einzahlautomat.Coins
{
    // Zentraler Singleton für das Münzgerät. Baut die Session (Connect) einmalig auf.
    public static class CoinManager
    {
        private static readonly object _lock = new object();
        private static bool _initialized = false;
        private static readonly string DefaultIniPath = @"C:\\ProgramData\\SuE-Software\\SuE-TaMi Client SQL\\Geldautomat.ini";
        public static ICoinValidator Instance { get; private set; }

        static CoinManager() { }

        public static void InitFromIni(string iniPath)
        {
            lock (_lock)
            {
                if (_initialized && Instance != null) return;
                Instance = CoinValidatorFactory.CreateFromIni(string.IsNullOrWhiteSpace(iniPath) ? DefaultIniPath : iniPath) ?? new SmartCoinV1();
                try { Instance.Connect(); } catch { }
                _initialized = true;
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
                try
                {
                    if (Instance is SmartCoinV1 sc1)
                    {
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