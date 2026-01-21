using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Concurrent; // NEU
using System; // NEU für DateTime

namespace TaMi_Einzahlautomat
{
    public static class IniHelper
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetPrivateProfileString(string section, string key, string defaultValue, StringBuilder retVal, int size, string filePath);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern long WritePrivateProfileString(string section, string key, string value, string filePath);

        // NEU: einfacher Cache (Key = path|section|key)
        private struct CacheEntry { public string Value; public DateTime Utc; }
        private static readonly ConcurrentDictionary<string, CacheEntry> _cache = new ConcurrentDictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
        private static TimeSpan _defaultTtl = TimeSpan.FromMinutes(10); // Werte gelten 10 Min. (konfigurierbar)
        public static void SetDefaultTtlMinutes(int minutes) { if (minutes > 0 && minutes < 1440) _defaultTtl = TimeSpan.FromMinutes(minutes); }
        public static void ClearCache() { _cache.Clear(); }

        private static string MakeKey(string path, string section, string key) => (path ?? "") + "|" + (section ?? "") + "|" + (key ?? "");

        public static string ReadValue(string section, string key, string filePath)
        {
            try
            {
                string cacheKey = MakeKey(filePath, section, key);
                if (_cache.TryGetValue(cacheKey, out var entry))
                {
                    if ((DateTime.UtcNow - entry.Utc) < _defaultTtl)
                        return entry.Value; // Cache-Hit innerhalb TTL
                    _cache.TryRemove(cacheKey, out _); // abgelaufen -> neu lesen
                }
            }
            catch { }

            var retVal = new StringBuilder(255);
            try { GetPrivateProfileString(section, key, "", retVal, 255, filePath); } catch { }
            string val = retVal.ToString();
            try { _cache[MakeKey(filePath, section, key)] = new CacheEntry { Value = val, Utc = DateTime.UtcNow }; } catch { }
            return val;
        }

        public static void WriteValue(string section, string key, string value, string filePath)
        {
            try { WritePrivateProfileString(section, key, value, filePath); } catch { }
            // Cache sofort aktualisieren (verhindert erneutes Lesen)
            try { _cache[MakeKey(filePath, section, key)] = new CacheEntry { Value = value ?? string.Empty, Utc = DateTime.UtcNow }; } catch { }
        }

        public static KontoZuordnung GetKontoZuordnung(string section, int manId, string iniPath)
        {
            int kost1 = ReadIntMultiKey(section, manId, "Kost1", iniPath);
            int kost2 = ReadIntMultiKey(section, manId, "Kost2", iniPath);
            int konto = ReadIntMultiKey(section, manId, "Konto", iniPath);
            return new KontoZuordnung { Kost1 = kost1, Kost2 = kost2, Konto = konto };
        }

        private static int ReadIntMultiKey(string section, int manId, string keyType, string iniPath)
        {
            string[] keys = new[] {
                $"ManID {manId} {keyType}",
                $"ManID  {manId} {keyType}",
                $"ManID {keyType}",
                $"ManID  {keyType}"
            };
            foreach (var key in keys)
            {
                var raw = ReadValue(section, key, iniPath);
                if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                    return v;
            }
            return 0;
        }
    }
}