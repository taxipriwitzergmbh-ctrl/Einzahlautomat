using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;

namespace TaMi_Einzahlautomat
{
    /// <summary>
    /// Pufferbasiertes File-Logging für SmartCoin-Kommunikation (Commands, Responses, Status, SmartEmpty).
    /// Hohe Poll-Frequenz -> asynchroner Flush jede Sekunde, um IO-Last zu begrenzen.
    /// </summary>
    internal static class SmartCoinCommLog
    {
        private static readonly ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();
        private static Timer _flushTimer;
        private static string _logFilePath;
        private static int _started;
        private static int _stopped;
        private static readonly object _fileLock = new object();
        private static bool _enabled = true; // Bei Bedarf über Konfiguration steuerbar
        private const int MaxFlushBatch = 500; // Schutz vor zu großen Flushes
        private const int RetentionFiles = 10; // Anzahl Log-Dateien behalten

        public static bool Enabled => _enabled;

        public static void StartSession()
        {
            if (!_enabled) return;
            if (System.Threading.Interlocked.Exchange(ref _started, 1) == 1) return;
            try
            {
                string baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "SmartCoin");
                Directory.CreateDirectory(baseDir);
                CleanupOldLogs(baseDir);
                string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                _logFilePath = Path.Combine(baseDir, $"SmartCoin_{ts}.log");
                AppendLine("--- SmartCoin Logging gestartet " + DateTime.Now.ToString("u") + " ---");
                _flushTimer = new Timer(_ => Flush(), null, 1200, 1200);
            }
            catch { }
        }

        public static void StopSession()
        {
            if (!_enabled) return;
            if (System.Threading.Interlocked.Exchange(ref _stopped, 1) == 1) return;
            try
            {
                AppendLine("--- SmartCoin Logging beendet " + DateTime.Now.ToString("u") + " ---");
                Flush();
                try { _flushTimer?.Dispose(); } catch { }
            }
            catch { }
        }

        public static void Log(string msg)
        {
            if (!_enabled) return;
            if (_logFilePath == null) return; // noch nicht gestartet
            try
            {
                var line = DateTime.Now.ToString("HH:mm:ss.fff") + " | " + msg;
                _queue.Enqueue(line);
                if (_queue.Count > 2000)
                {
                    // Backpressure: sofortiger Flush versuchen
                    Flush();
                }
            }
            catch { }
        }

        private static void AppendLine(string line)
        {
            if (_logFilePath == null) return;
            try
            {
                lock (_fileLock)
                {
                    File.AppendAllText(_logFilePath, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }
        }

        private static void Flush()
        {
            if (_logFilePath == null) return;
            try
            {
                if (_queue.IsEmpty) return;
                var sb = new StringBuilder(16 * 1024);
                int c = 0;
                while (c < MaxFlushBatch && _queue.TryDequeue(out var line))
                {
                    sb.AppendLine(line);
                    c++;
                }
                if (c == 0) return;
                AppendLine(sb.ToString().TrimEnd());
            }
            catch { }
        }

        private static void CleanupOldLogs(string dir)
        {
            try
            {
                var files = new DirectoryInfo(dir).GetFiles("SmartCoin_*.log");
                Array.Sort(files, (a,b) => b.CreationTimeUtc.CompareTo(a.CreationTimeUtc));
                for (int i = RetentionFiles; i < files.Length; i++)
                {
                    try { files[i].Delete(); } catch { }
                }
            }
            catch { }
        }
    }
}
