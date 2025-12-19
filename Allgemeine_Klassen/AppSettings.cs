using System;
using System.IO;

namespace Geldautomat
{
    public static class AppSettings
    {
        public static string AutomatenName { get; set; } = ""; // Alt (wird später entfernt)
        public static int DeviceId { get; set; } = 0;            // Neu
        public static string IniPath { get; set; } = @"C:\ProgramData\SuE-Software\SuE-TaMi Client SQL\Geldautomat.ini";
        public static string AllowedManIdsRaw { get; set; } = null; // from TKassenbuchDevice (e.g. "1;4")

        // ALT: liest den Namen (Bestand bis Umstellung abgeschlossen ist)
        public static string LoadAutomatenNameFromIni()
        {
            try
            {
                if (!File.Exists(IniPath)) return "";
                string section = null;
                foreach (var raw in File.ReadAllLines(IniPath))
                {
                    var line = (raw ?? "").Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                    if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2).Trim(); continue; }
                    if (!string.Equals(section, "Device", StringComparison.OrdinalIgnoreCase)) continue;
                    int eq = line.IndexOf('='); if (eq <= 0) continue;
                    var key = line.Substring(0, eq).Trim();
                    var value = line.Substring(eq + 1).Trim();
                    if (string.Equals(key, "Name", StringComparison.OrdinalIgnoreCase)) return value;
                }
            }
            catch { }
            return "";
        }

        // NEU: liest Device-ID aus der INI ([Device] ID=123). Gibt 0 zurück falls nicht gefunden/ungültig.
        public static int LoadDeviceIdFromIni()
        {
            try
            {
                if (!File.Exists(IniPath)) return 0;
                string section = null;
                foreach (var raw in File.ReadAllLines(IniPath))
                {
                    var line = (raw ?? "").Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                    if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2).Trim(); continue; }
                    if (!string.Equals(section, "Device", StringComparison.OrdinalIgnoreCase)) continue;
                    int eq = line.IndexOf('='); if (eq <= 0) continue;
                    var key = line.Substring(0, eq).Trim();
                    var value = line.Substring(eq + 1).Trim();
                    if (string.Equals(key, "ID", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(value, out var id) && id >= 0) return id;
                        return 0; // ungültig -> 0
                    }
                }
            }
            catch { }
            return 0;
        }
    }
}