using System;

namespace TaMi_Einzahlautomat.Coins
{
    // Zentraler Singleton f�r das M�nzger�t. Baut die Session (Connect) einmalig auf.
    public static class CoinManager
    {
        private static readonly object _lock = new object();
        private static bool _initialized = false;
        private static readonly string DefaultIniPath = @"C:\\ProgramData\\SuE-Software\\SuE-TaMi Client SQL\\Einzahlautomat.ini";
        public static ICoinValidator Instance { get; private set; }

        static CoinManager() { }

        public static void InitFromIni(string iniPath)
        {
            lock (_lock)
            {
                if (_initialized && Instance != null) return;
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