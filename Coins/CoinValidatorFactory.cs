using System;

namespace Geldautomat.Coins
{
    public static class CoinValidatorFactory
    {
        private const string Section = "SmartCoin";
        private const string KeyType = "Typ";
        private const string KeyCom = "ComPort";
        private const string KeyAddr = "SSPAddress";

        public static ICoinValidator CreateFromIni(string iniPath)
        {
            var typeStr = IniHelper.ReadValue(Section, KeyType, iniPath)?.Trim();
            var com = IniHelper.ReadValue(Section, KeyCom, iniPath)?.Trim();
            var addrStr = IniHelper.ReadValue(Section, KeyAddr, iniPath)?.Trim();

            var type = ParseType(typeStr);
            var inst = Create(type);
            if (inst != null)
            {
                if (!string.IsNullOrWhiteSpace(com)) inst.ComPort = com;
                if (type == CoinValidatorType.Rm5Cctalk)
                {
                    // RM5: Adresse immer auf 2 setzen (INI ignorieren – oft falscher Altwert 16)
                    inst.SspAddress = 2;
                }
                else if (int.TryParse(addrStr, out var addr) && addr > 0)
                {
                    inst.SspAddress = addr;
                }
            }
            return inst;
        }

        public static ICoinValidator Create(CoinValidatorType type)
        {
            switch (type)
            {
                case CoinValidatorType.SmartCoinV1: return new SmartCoinV1();
                case CoinValidatorType.SmartCoinV2: return new SmartCoinV1(); // Alias
                case CoinValidatorType.RtCoinSystem: return new SmartCoinV1(); // Platzhalter gleiche Klasse
                case CoinValidatorType.Rm5Cctalk: return new Rm5CctalkValidator { SspAddress = 2 }; // Default 2
                default: return null;
            }
        }

        public static CoinValidatorType ParseType(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return CoinValidatorType.None;
            s = s.Trim().ToLowerInvariant();
            if (s == "v1" || s.Contains("smartcoinv1")) return CoinValidatorType.SmartCoinV1;
            if (s == "v2" || s.Contains("smartcoinv2")) return CoinValidatorType.SmartCoinV2;
            if (s == "rt" || s.Contains("rtcoinsystem")) return CoinValidatorType.RtCoinSystem;
            if (s == "rm5" || s.Contains("rm5cctalk")) return CoinValidatorType.Rm5Cctalk;
            if (s.Contains("smartcoinsystem") || s == "smartcoin") return CoinValidatorType.SmartCoinV1; // historisch
            return CoinValidatorType.None;
        }

        public static void SaveToIni(string iniPath, CoinValidatorType type, string comPort, int? sspAddress = null)
        {
            IniHelper.WriteValue(Section, KeyType, TypeToString(type), iniPath);
            if (!string.IsNullOrWhiteSpace(comPort)) IniHelper.WriteValue(Section, KeyCom, comPort, iniPath);
            if (type != CoinValidatorType.Rm5Cctalk && sspAddress.HasValue && sspAddress.Value > 0)
                IniHelper.WriteValue(Section, KeyAddr, sspAddress.Value.ToString(), iniPath);
            // Für RM5 Adresse bewusst nicht schreiben (fix 2)
        }

        private static string TypeToString(CoinValidatorType t)
        {
            switch (t)
            {
                case CoinValidatorType.SmartCoinV1: return "V1";
                case CoinValidatorType.SmartCoinV2: return "V2";  //Gibt es wohl auch nicht.
                case CoinValidatorType.RtCoinSystem: return "RT"; //Gibt es nicht mehr im Kundenkreis
                case CoinValidatorType.Rm5Cctalk: return "RM5";
                default: return "None";
            }
        }
    }
}