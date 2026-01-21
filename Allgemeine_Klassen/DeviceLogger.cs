using System;
using System.IO;
using System.Text;

namespace TaMi_Einzahlautomat
{
    // Dedizierte Dateien unter Logs\SmartCoin1 und Logs\SmartCoin2 mit 7 Tagen Aufbewahrung
    public static class DeviceLogger
    {
        private static readonly object _lock = new object();
        private static string _baseDir;
        private static bool _initialized;

        private static void EnsureBaseDir()
        {
            if (_initialized) return;
            lock (_lock)
            {
                if (_initialized) return;
                try
                {
                    _baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                    Directory.CreateDirectory(_baseDir);
                }
                catch
                {
                    try
                    {
                        _baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Geldautomat", "Logs");
                        Directory.CreateDirectory(_baseDir);
                    }
                    catch { }
                }
                _initialized = true;
            }
        }

        private static string EnsureFolder(string deviceFolder)
        {
            EnsureBaseDir();
            if (string.IsNullOrWhiteSpace(_baseDir)) return null;
            try
            {
                var dir = Path.Combine(_baseDir, deviceFolder);
                Directory.CreateDirectory(dir);
                return dir;
            }
            catch { return null; }
        }

        private static void CleanupOldFiles(string dir, int daysToKeep)
        {
            try
            {
                var limit = DateTime.Today.AddDays(-daysToKeep);
                foreach (var f in Directory.EnumerateFiles(dir, "*.log"))
                {
                    try
                    {
                        var fi = new FileInfo(f);
                        var name = Path.GetFileNameWithoutExtension(f);
                        DateTime day;
                        if (!DateTime.TryParse(name, out day)) day = fi.LastWriteTime.Date;
                        if (day < limit) fi.Delete();
                    }
                    catch { }
                }
            }
            catch { }
        }

        public static void InitSmartCoins()
        {
            try
            {
                var sc1 = EnsureFolder("SmartCoin1");
                if (!string.IsNullOrEmpty(sc1)) CleanupOldFiles(sc1, 7);
            }
            catch { }
            try
            {
                var sc2 = EnsureFolder("SmartCoin2");
                if (!string.IsNullOrEmpty(sc2)) CleanupOldFiles(sc2, 7);
            }
            catch { }
        }

        public static void LogSmartCoin(string deviceFolder, string message)
        {
            if (string.IsNullOrWhiteSpace(deviceFolder)) return;
            var dir = EnsureFolder(deviceFolder);
            if (string.IsNullOrEmpty(dir)) return;
            var file = Path.Combine(dir, DateTime.Today.ToString("yyyy-MM-dd") + ".log");
            var line = (message ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(line))
                line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " | " + line;

            try
            {
                lock (_lock)
                {
                    File.AppendAllText(file, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }

            try { CleanupOldFiles(dir, 7); } catch { }
        }
    }
}
