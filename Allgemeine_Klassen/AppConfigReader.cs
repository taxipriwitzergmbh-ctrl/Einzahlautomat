using System;
using System.IO;

namespace TaMi_Einzahlautomat
{
    internal static class AppConfigReader
    {
        // Pfad zur zentralen INI
        private static readonly string IniPath =
            @"C:\ProgramData\SuE-Software\SuE-TaMi Client SQL\Einzahlautomat.ini";

        public static bool TryGetNv200ComFromIni(out string comPort)
        {
            comPort = null;
            try
            {
                if (!File.Exists(IniPath))
                    return false;

                string currentSection = null;
                foreach (var rawLine in File.ReadAllLines(IniPath))
                {
                    var line = rawLine?.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith(";") || line.StartsWith("#"))
                        continue;

                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        currentSection = line.Substring(1, line.Length - 2).Trim();
                        continue;
                    }

                    if (!string.Equals(currentSection, "NV200/1", StringComparison.OrdinalIgnoreCase))
                        continue;

                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;

                    var key = line.Substring(0, eq).Trim();
                    var val = line.Substring(eq + 1).Trim();

                    if (key.Equals("COM", StringComparison.OrdinalIgnoreCase))
                    {
                        // Erlaubt "4" oder "COM4"
                        if (!string.IsNullOrEmpty(val))
                        {
                            if (val.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                                comPort = val.ToUpperInvariant();
                            else
                                comPort = "COM" + val;
                            return true;
                        }
                    }
                }
            }
            catch
            {
                // Fehler beim Lesen ignorieren
            }
            return false;
        }
    }
}