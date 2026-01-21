using System;

namespace TaMi_Einzahlautomat.Printing
{
    public static class LegacyItlPrinterManager
    {
        private static LegacyItlPrinterDevice _dev1;
        private static LegacyItlPrinterDevice _dev2;
        private static LegacyItlPrinterConfig _cfg1;
        private static LegacyItlPrinterConfig _cfg2;

        public static void Configure(LegacyItlPrinterConfig c1, LegacyItlPrinterConfig c2)
        {
            _cfg1 = c1 != null ? c1.Clone() : null;
            _cfg2 = c2 != null ? c2.Clone() : null;
            Recreate();
        }

        private static void Recreate()
        {
            DisposeAll();
            if (_cfg1 != null && _cfg1.Enabled) _dev1 = new LegacyItlPrinterDevice("LEGACY1", _cfg1);
            if (_cfg2 != null && _cfg2.Enabled) _dev2 = new LegacyItlPrinterDevice("LEGACY2", _cfg2);
        }

        public static bool Print(string logicalName, string header, string[] lines)
        {
            LegacyItlPrinterDevice d = null;
            if (string.Equals(logicalName, "LEGACY1", StringComparison.OrdinalIgnoreCase)) d = _dev1;
            else if (string.Equals(logicalName, "LEGACY2", StringComparison.OrdinalIgnoreCase)) d = _dev2;
            if (d == null) return false;
            return d.PrintLines(header, lines);
        }

        public static bool Has(string logicalName)
        {
            if (string.Equals(logicalName, "LEGACY1", StringComparison.OrdinalIgnoreCase)) return _dev1 != null;
            if (string.Equals(logicalName, "LEGACY2", StringComparison.OrdinalIgnoreCase)) return _dev2 != null;
            return false;
        }

        public static void DisposeAll()
        {
            try { _dev1?.Dispose(); } catch { }
            try { _dev2?.Dispose(); } catch { }
            _dev1 = null;
            _dev2 = null;
        }
    }
}
