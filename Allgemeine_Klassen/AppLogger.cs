using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using TaMi_Einzahlautomat.Coins;
using System.Collections.Concurrent; // NEU f�r Queue

namespace TaMi_Einzahlautomat
{
    public static class AppLogger
    {
        private static readonly object _lock = new object();
        private static string _dir;
        private static string _currentFile;
        private static DateTime _currentDay;

        // Kassenbestand-Basiswert (z. B. bei Anmeldung)
        private static decimal _baselineEuro = 0m;
        private static bool _baselineSet = false;

        // Kassensturz Unterdr�ckung: Anzahl ge�ffneter Kassensturz-Fenster
        private static int _kassensturzOpenCount = 0;

        // NEU: Asynchrones Logging
        private static readonly ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();
        private static readonly AutoResetEvent _signal = new AutoResetEvent(false);
        private static Thread _writerThread;
        private static volatile bool _running;
        private const int FlushIntervalMs = 1000; // periodisches Flush
        private const int BulkThreshold = 200;    // ab dieser Anzahl sofort flush
        private static DateTime _lastFlushUtc = DateTime.UtcNow;

        private static volatile bool _shuttingDown = false; // NEU: nach Shutdown nichts mehr entgegennehmen
        private static readonly object _dedupLock = new object();
        private static string _lastLine; private static DateTime _lastLineUtc; private static int _lastRepeatCount;
        private static readonly TimeSpan DedupWindow = TimeSpan.FromSeconds(2);

        // Lizenz-Block: während der Init-Phase werden Nicht-Lizenz-Nachrichten gepuffert
        private static volatile bool _licenseBlockActive = false;
        private static readonly ConcurrentQueue<string> _licenseBuffer = new ConcurrentQueue<string>();
        public static void BeginLicenseBlock() { _licenseBlockActive = true; }
        public static void EndLicenseBlock()
        {
            _licenseBlockActive = false;
            string msg;
            while (_licenseBuffer.TryDequeue(out msg)) _queue.Enqueue(msg);
            _signal.Set();
        }

        public static bool KassensturzActive => _kassensturzOpenCount > 0;
        public static void KassensturzScopeEnter() { try { Interlocked.Increment(ref _kassensturzOpenCount); } catch { } }
        public static void KassensturzScopeExit() { try { Interlocked.Decrement(ref _kassensturzOpenCount); } catch { } }

        private static bool ShouldSuppressDuringKassensturz(string msg)
        {
            if (!KassensturzActive) return false;
            if (string.IsNullOrEmpty(msg)) return false;
            string m = msg.ToLowerInvariant();
            if (m.Contains("<--- m�nze eingezahlt") ||
                m.Contains("<--- schein eingezahlt") ||
                m.Contains("---> schein ausgezahlt") ||
                m.Contains("m�nzauszahlung summe") ||
                m.Contains("m�nzauszahlung abgeschlossen") ||
                m.Contains("auszahlung (scheine) abgeschlossen"))
                return true;
            return false;
        }

        public static void Init()
        {
            try
            {
                _dir = Path.Combine(Application.StartupPath, "Ereignisse");
                Directory.CreateDirectory(_dir);
                _currentDay = DateTime.Today;
                _currentFile = Path.Combine(_dir, $"{_currentDay:yyyy-MM-dd}.log");
                CleanupOldFiles(20);
                StartWriter();
            }
            catch { }
        }

        private static void StartWriter()
        {
            if (_running) return;
            _running = true;
            _writerThread = new Thread(WriterLoop) { IsBackground = true, Name = "AppLoggerWriter" };
            _writerThread.Start();
        }

        private static void WriterLoop()
        {
            StreamWriter sw = null;
            try
            {
                while (_running)
                {
                    // Warten auf Signal oder Timeout
                    _signal.WaitOne(FlushIntervalMs);
                    FlushInternal(ref sw, forced:false);
                }
                // Beim Stop alles flushen
                FlushInternal(ref sw, forced:true);
            }
            catch { }
            finally
            {
                try { sw?.Flush(); sw?.Dispose(); } catch { }
            }
        }

        private static void FlushInternal(ref StreamWriter sw, bool forced)
        {
            try
            {
                int queued = _queue.Count;
                if (queued == 0 && !forced) return;
                if (!forced)
                {
                    var due = (DateTime.UtcNow - _lastFlushUtc).TotalMilliseconds >= FlushIntervalMs;
                    var bulk = queued >= BulkThreshold;
                    if (!due && !bulk) return; // noch warten
                }

                // Day-Rotation pr�fen
                if (DateTime.Today != _currentDay)
                {
                    _currentDay = DateTime.Today;
                    _currentFile = Path.Combine(_dir, $"{_currentDay:yyyy-MM-dd}.log");
                    CleanupOldFiles(20);
                    try { sw?.Flush(); sw?.Dispose(); } catch { }
                    sw = null;
                }

                if (sw == null)
                {
                    sw = new StreamWriter(new FileStream(_currentFile, FileMode.Append, FileAccess.Write, FileShare.Read), Encoding.UTF8);
                }

                // Drain Queue in Bulk
                var sb = new StringBuilder();
                string line;
                while (_queue.TryDequeue(out line))
                {
                    sb.AppendLine(line);
                }
                sw.Write(sb.ToString());
                sw.Flush();
                _lastFlushUtc = DateTime.UtcNow;
            }
            catch { }
        }

        public static void Shutdown()
        {
            try
            {
                _shuttingDown = true; // weitere Log-Aufrufe ignorieren
                _running = false;
                _signal.Set();
                if (_writerThread != null && _writerThread.IsAlive)
                    _writerThread.Join(3000);
            }
            catch { }
        }

        public static void Log(string message)
        {
            try
            {
                if (_shuttingDown) return; // nach Shutdown keine neuen Eintr�ge mehr
                // Filter f�r Ger�teraum / Rauschen
                if (message != null)
                {
                    string msg = message.ToLowerInvariant();
                    string trimmed = msg.TrimStart();
                    if (trimmed.StartsWith("rx ") || trimmed.StartsWith("tx ") || trimmed.StartsWith("rx[") || trimmed.StartsWith("tx[") || trimmed.StartsWith("rx:") || trimmed.StartsWith("tx:") ||
                        msg.Contains("coinfeeder") || msg.Contains("sending template:") || msg.Contains("auto: nv200") || msg.Contains("state timer started") ||
                        msg.Contains("send suppressed (debounce):") || msg.Contains("nv200/1 state:") || msg.Contains("nv200/2 state:") || msg.Contains("ensurefeederopen") ||
                        msg.Contains("coinfeeder isopen=") || msg.Contains("opening coinfeeder on") || msg.Contains("auto-send nv200") ||
                        msg.Contains("status frame suppressed") || msg.Contains("rm5 status frame suppressed")) return;
                }
                if (ShouldSuppressDuringKassensturz(message)) return;
                if (_dir == null) Init();

                string original = message ?? string.Empty;
                bool isBlank = string.IsNullOrWhiteSpace(original);
                bool isHyphenLine = !isBlank && original.Trim().Length > 0 && original.Trim().All(c => c == '-');
                string line = (isBlank || isHyphenLine) ? original : $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | {message}";

                // NEU: einfache Deduplizierung (gleiche Zeile mehrfach in kurzer Zeit)
                bool suppress = false; string toEnqueue = line;
                lock (_dedupLock)
                {
                    if (!string.IsNullOrEmpty(line) && !_shuttingDown)
                    {
                        if (string.Equals(line, _lastLine, StringComparison.Ordinal))
                        {
                            if ((DateTime.UtcNow - _lastLineUtc) <= DedupWindow)
                            {
                                _lastRepeatCount++;
                                suppress = true; // nicht jede Wiederholung loggen
                            }
                            else
                            {
                                // Fenster abgelaufen: ggf. letzte Wiederholungen ausgeben
                                if (_lastRepeatCount > 0)
                                {
                                    _queue.Enqueue($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | (wiederholt {_lastRepeatCount}x): {_lastLine}");
                                    _lastRepeatCount = 0;
                                }
                            }
                        }
                        else
                        {
                            if (_lastRepeatCount > 0 && !string.IsNullOrEmpty(_lastLine))
                            {
                                _queue.Enqueue($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | (wiederholt {_lastRepeatCount}x): {_lastLine}");
                                _lastRepeatCount = 0;
                            }
                            _lastLine = line;
                        }
                        _lastLineUtc = DateTime.UtcNow;
                    }
                }
                if (suppress) { _signal.Set(); return; }

                // Während Lizenz-Block: Nicht-Lizenz-Nachrichten puffern
                if (_licenseBlockActive)
                {
                    bool isLicenseMsg = original.IndexOf("LicenseManager:", StringComparison.OrdinalIgnoreCase) >= 0
                                    || (original.Length > 0 && original.Trim().All(c => c == '*'));
                    if (!isLicenseMsg)
                    {
                        _licenseBuffer.Enqueue(toEnqueue);
                        return;
                    }
                }

                _queue.Enqueue(toEnqueue);
                _signal.Set(); // weckt Writer
            }
            catch { }
        }

        public static void LogStartBanner()
        {
            try
            {
                if (_shuttingDown) return;
                // f�nf Leerzeilen
                for (int i = 0; i < 5; i++)
                    _queue.Enqueue(string.Empty);

                // zentrierte Trennlinie mit START (breite 80)
                const int width = 80;
                string label = " START ";
                int hyphens = Math.Max(0, width - label.Length);
                int left = hyphens / 2;
                int right = hyphens - left;
                string line = new string('-', left) + label + new string('-', right);
                _queue.Enqueue(line);
                _signal.Set();
            }
            catch { }
        }

        public static void LogEndBanner()
        {
            try
            {
                if (_shuttingDown) return;
                for (int i = 0; i < 5; i++)
                    _queue.Enqueue(string.Empty);
                const int width = 80;
                string label = " END ";
                int hyphens = Math.Max(0, width - label.Length);
                int left = hyphens / 2;
                int right = hyphens - left;
                string line = new string('-', left) + label + new string('-', right);
                _queue.Enqueue(line);
                _signal.Set();
            }
            catch { }
        }

        public static void LogSeparator() { Log(new string('_', 74)); }

        private static void CleanupOldFiles(int daysToKeep)
        {
            try
            {
                var files = Directory.GetFiles(_dir, "*.log");
                var toDelete = files.Select(f => new FileInfo(f)).OrderByDescending(fi => fi.Name).Skip(daysToKeep).ToList();
                foreach (var fi in toDelete) { try { fi.Delete(); } catch { } }
            }
            catch { }
        }

        private static bool IsRm5Levels(int[] lv)
        { if (lv == null || lv.Length < 8) return false; return lv[0] < 0 && lv[1] < 0 && lv[2] < 0 && (lv[3] >= 0 || lv[4] >= 0 || lv[5] >= 0 || lv[6] >= 0 || lv[7] >= 0); }
        private static string FormatCoinsLine(int[] lv)
        {
            if (lv == null || lv.Length < 8) return "(keine Daten)";
            string safe(int i) => lv[i] < 0 ? "?" : lv[i].ToString();
            if (IsRm5Levels(lv)) return $"10c={safe(3)},20c={safe(4)},50c={safe(5)},1{EuroSign}={safe(6)},2{EuroSign}={safe(7)}"; // RM5
            return $"1c={safe(0)},2c={safe(1)},5c={safe(2)},10c={safe(3)},20c={safe(4)},50c={safe(5)},1{EuroSign}={safe(6)},2{EuroSign}={safe(7)}";
        }

        // Einzel-Snapshot
        public static void LogKassenbestandSnapshot(NV200_SSP ssp, int[] coinLevels)
        {
            try
            {
                string coinsLine = FormatCoinsLine(coinLevels);
                decimal sum = 0m;
                if (ssp != null)
                {
                    long payoutCent = (long)ssp.Payout_5_euro * 500 + (long)ssp.Payout_10_euro * 1000 + (long)ssp.Payout_20_euro * 2000 + (long)ssp.Payout_50_euro * 5000 + (long)ssp.Payout_100_euro * 10000 + (long)ssp.Payout_200_euro * 20000 + (long)ssp.Payout_500_euro * 50000;
                    long cashboxCent = (long)ssp.Cashbox_5_euro * 500 + (long)ssp.Cashbox_10_euro * 1000 + (long)ssp.Cashbox_20_euro * 2000 + (long)ssp.Cashbox_50_euro * 5000 + (long)ssp.Cashbox_100_euro * 10000 + (long)ssp.Cashbox_200_euro * 20000 + (long)ssp.Cashbox_500_euro * 50000;
                    long coinsCent = 0;
                    if (coinLevels != null && coinLevels.Length >= 8)
                    {
                        int[] vals = { 1,2,5,10,20,50,100,200 };
                        for (int i = 0; i < 8; i++) { int c = coinLevels[i] < 0 ? 0 : coinLevels[i]; coinsCent += (long)c * vals[i]; }
                    }
                    sum = Math.Round((coinsCent + payoutCent + cashboxCent) / 100m, 2);
                }
                var msg = new StringBuilder();
                msg.AppendLine("Bestand |");
                msg.AppendLine($"Coins: {coinsLine}");
                msg.AppendLine($"Payout: {FormatPayoutCounts(ssp)}");
                msg.AppendLine($"Cashbox: {FormatCashboxCounts(ssp)}");
                msg.Append($"Summe: {sum:0.00} �");
                Log(msg.ToString());
            }
            catch { }
        }

        public static decimal ComputeKassenbestandEuro(NV200_SSP ssp, int[] coinLevels)
        {
            try
            {
                long coinsCent = 0;
                if (coinLevels != null && coinLevels.Length >= 8)
                {
                    int[] vals = { 1,2,5,10,20,50,100,200 };
                    for (int i = 0; i < 8; i++) { int c = coinLevels[i] < 0 ? 0 : coinLevels[i]; coinsCent += (long)c * vals[i]; }
                }
                long payoutCent = 0, cashboxCent = 0;
                if (ssp != null)
                {
                    payoutCent = (long)ssp.Payout_5_euro * 500 + (long)ssp.Payout_10_euro * 1000 + (long)ssp.Payout_20_euro * 2000 + (long)ssp.Payout_50_euro * 5000 + (long)ssp.Payout_100_euro * 10000 + (long)ssp.Payout_200_euro * 20000 + (long)ssp.Payout_500_euro * 50000;
                    cashboxCent = (long)ssp.Cashbox_5_euro * 500 + (long)ssp.Cashbox_10_euro * 1000 + (long)ssp.Cashbox_20_euro * 2000 + (long)ssp.Cashbox_50_euro * 5000 + (long)ssp.Cashbox_100_euro * 10000 + (long)ssp.Cashbox_200_euro * 20000 + (long)ssp.Cashbox_500_euro * 50000;
                }
                return Math.Round((coinsCent + payoutCent + cashboxCent) / 100m, 2);
            }
            catch { return 0m; }
        }

        public static void SetKassenbestandBaseline(decimal euro, string reason = null)
        {
            if (_baselineSet) return;
            _baselineEuro = Math.Round(euro, 2);
            _baselineSet = true;
            Log($"Baseline gesetzt: {_baselineEuro:0.00} €{(string.IsNullOrEmpty(reason) ? string.Empty : " (" + reason + ")")}");

        }

        public static void SetKassenbestandBaselineFromSnapshot(NV200_SSP ssp, int[] coinLevels, string reason = null)
        {
            if (_baselineSet) return;
            bool allKnown = coinLevels != null && coinLevels.Length >= 8 && coinLevels.All(v => v >= 0);
            if (!allKnown) return;

            var euro = ComputeKassenbestandEuro(ssp, coinLevels);
            SetKassenbestandBaseline(euro, reason);
        }

        public static void LogKassenDifferenzFromBaseline(NV200_SSP ssp, int[] coinLevels, string label)
        {
            try
            {
                var now = ComputeKassenbestandEuro(ssp, coinLevels);
                if (_baselineSet)
                {
                    var diff = Math.Round(now - _baselineEuro, 2);
                    Log($"Kassendifferenz {label}: {diff:0.00} � (jetzt {now:0.00} �, Basis {_baselineEuro:0.00} �)");
                }
                else Log($"Kassendifferenz {label}: Baseline nicht gesetzt (jetzt {now:0.00} �)");
            }
            catch { }
        }

        private static string FormatCoinLevels(int[] lv) => FormatCoinsLine(lv);
        private const string EuroSign = "\u20AC";

        private static string FormatPayoutCounts(NV200_SSP s)
        { return s == null ? "-" : $"5{EuroSign}={s.Payout_5_euro},10{EuroSign}={s.Payout_10_euro},20{EuroSign}={s.Payout_20_euro},50{EuroSign}={s.Payout_50_euro},100{EuroSign}={s.Payout_100_euro},200{EuroSign}={s.Payout_200_euro},500{EuroSign}={s.Payout_500_euro}"; }
        private static string FormatCashboxCounts(NV200_SSP s)
        { return s == null ? "-" : $"5{EuroSign}={s.Cashbox_5_euro},10{EuroSign}={s.Cashbox_10_euro},20{EuroSign}={s.Cashbox_20_euro},50{EuroSign}={s.Cashbox_50_euro},100{EuroSign}={s.Cashbox_100_euro},200{EuroSign}={s.Cashbox_200_euro},500{EuroSign}={s.Cashbox_500_euro}"; }
        public static bool BaselineIsSet => _baselineSet;

        public static void LogKassenbestandSnapshotCombined(NV200_SSP nv1, int[] coin1, NV200_SSP nv2, int[] coin2, bool setBaselineIfNotSet = false, string baselineReason = null)
        {
            try
            {
                // Ja: das kann direkt hier im `AppLogger` passieren (statt neuer Form/Helper).
                // Wir ersetzen lediglich die alte Ausgabe (Zeilen ~380-413) durch die neue Blöcke-Ansicht.

                var sb = new StringBuilder();
                const int frameWidth = 49;

                // Header-Zeile soll im Log den Zeitstempel tragen (AppLogger.Log() setzt Zeitstempel pro Zeile).
                // Die Rahmenlinie soll direkt NACH der Header-Zeile kommen.
                sb.AppendLine("Bestand (kombiniert) |");
                sb.AppendLine(new string('~', frameWidth));

                bool nv2001Disabled = false;
                bool nv2002Disabled = false;
                bool smartCoin1Disabled = false;
                bool smartCoin2Disabled = false;
                bool rm5Active = false;

                try { nv2001Disabled = AdminNV200Form.DeviceDisabled; } catch { }
                try { nv2002Disabled = AdminNV200_2Form.DeviceDisabled; } catch { }
                try { smartCoin2Disabled = AdminCoin2Form.DeviceDisabled; } catch { }

                try
                {
                    if (!nv2001Disabled)
                    {
                        var dv = IniHelper.ReadValue("NV200/1", "Disabled", AppSettings.IniPath);
                        if (!string.IsNullOrWhiteSpace(dv))
                            nv2001Disabled = dv.Trim().Equals("1", StringComparison.OrdinalIgnoreCase) || dv.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                    }
                }
                catch { }

                try
                {
                    if (!nv2002Disabled)
                    {
                        var dv = IniHelper.ReadValue("NV200/2", "Disabled", AppSettings.IniPath);
                        if (!string.IsNullOrWhiteSpace(dv))
                            nv2002Disabled = dv.Trim().Equals("1", StringComparison.OrdinalIgnoreCase) || dv.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                    }
                }
                catch { }

                try
                {
                    var dv = IniHelper.ReadValue("SmartCoin/1", "Disabled", AppSettings.IniPath);
                    if (!string.IsNullOrWhiteSpace(dv))
                        smartCoin1Disabled = dv.Trim().Equals("1", StringComparison.OrdinalIgnoreCase) || dv.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                }
                catch { }

                try
                {
                    if (!smartCoin2Disabled)
                    {
                        var dv = IniHelper.ReadValue("SmartCoin/2", "Disabled", AppSettings.IniPath);
                        if (!string.IsNullOrWhiteSpace(dv))
                            smartCoin2Disabled = dv.Trim().Equals("1", StringComparison.OrdinalIgnoreCase) || dv.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                    }
                }
                catch { }

                try
                {
                    rm5Active = (CoinManager.Instance is Rm5CctalkValidator);
                    if (!rm5Active)
                    {
                        var t = IniHelper.ReadValue("SmartCoin/1", "Typ", AppSettings.IniPath);
                        rm5Active = !string.IsNullOrWhiteSpace(t) && t.Trim().Equals("RM5", StringComparison.OrdinalIgnoreCase);
                    }
                }
                catch { rm5Active = false; }

                decimal sumSmartcoins = 0m;
                decimal sumNv200 = 0m;

                // ---- SmartCoin / RM5 ----
                if (rm5Active)
                {
                    if (coin1 == null || coin1.Length < 8)
                    {
                        try { var c = CoinManager.Instance as Rm5CctalkValidator; if (c != null) coin1 = c.GetCoinAvailability(); } catch { }
                    }

                    var eur = SumCoinsEuro(coin1);
                    if (eur > 0m || (coin1 != null && coin1.Length >= 8))
                    {
                        sb.AppendLine("-- RM5 --");
                        sb.AppendLine($"Summe={eur:0.00}{EuroSign}");
                        sb.AppendLine("Coins: " + FormatCoinsLine(coin1));
                        sb.AppendLine();
                        sumSmartcoins += eur;
                    }
                }
                else
                {
                    if (!smartCoin1Disabled)
                    {
                        if (coin1 == null || coin1.Length < 8)
                        {
                            try { var c = CoinManager.Instance; if (c is SmartCoinV1 sc1) coin1 = sc1.GetCoinAvailability(); } catch { }
                        }
                        var eur1 = SumCoinsEuro(coin1);
                        if (eur1 > 0m || (coin1 != null && coin1.Length >= 8))
                        {
                            sb.AppendLine("-- SmartCoin 1 --");
                            sb.AppendLine($"Summe={eur1:0.00}{EuroSign}");
                            sb.AppendLine("Coins 1: " + FormatCoinsLine(coin1));
                            sumSmartcoins += eur1;
                        }
                    }

                    if (!smartCoin2Disabled)
                    {
                        if (coin2 == null || coin2.Length < 8)
                        {
                            try { var c2 = Coin2Manager.Instance; if (c2 is SmartCoinV1 sc2) coin2 = sc2.GetCoinAvailability(); } catch { }
                        }
                        var eur2 = SumCoinsEuro(coin2);
                        if (eur2 > 0m || (coin2 != null && coin2.Length >= 8))
                        {
                            sb.AppendLine("-- SmartCoin 2 --");
                            sb.AppendLine($"Summe={eur2:0.00}{EuroSign}");
                            sb.AppendLine("Coins 2: " + FormatCoinsLine(coin2));
                            sumSmartcoins += eur2;
                        }
                    }

                    if (sumSmartcoins > 0m)
                    {
                        sb.AppendLine($"Summe Smartcoins: {sumSmartcoins:0.00} {EuroSign}");
                        sb.AppendLine();
                    }
                }

                // ---- NV200 ----
                if (!nv2001Disabled)
                {
                    var sum1 = SumNv200Euro(nv1);
                    sb.AppendLine("-- NV200/1 --");
                    sb.AppendLine($"Summe={sum1:0.00}{EuroSign}");
                    sb.AppendLine("Payout: " + FormatPayoutCounts(nv1));
                    sb.AppendLine("Cashbox: " + FormatCashboxCounts(nv1));
                    sumNv200 += sum1;
                }

                if (!nv2002Disabled)
                {
                    var sum2 = SumNv200Euro(nv2);
                    sb.AppendLine("-- NV200/2 --");
                    sb.AppendLine($"Summe={sum2:0.00}{EuroSign}");
                    sb.AppendLine("Payout: " + FormatPayoutCounts(nv2));
                    sb.AppendLine("Cashbox: " + FormatCashboxCounts(nv2));
                    sumNv200 += sum2;
                }

                if (sumNv200 > 0m)
                {
                    sb.AppendLine($"Summe NV200: {sumNv200:0.00} {EuroSign}");
                    sb.AppendLine();
                }

                var gesamt = sumSmartcoins + sumNv200;
                sb.AppendLine($"Summe Gesamt: {gesamt:0.00} {EuroSign}");
                sb.AppendLine(new string('~', frameWidth));

                Log(sb.ToString());
                if (setBaselineIfNotSet && !_baselineSet) SetKassenbestandBaseline(gesamt, baselineReason);
            }
            catch { }
        }

        private static decimal SumCoinsEuro(int[] coinLevels)
        {
            try
            {
                if (coinLevels == null || coinLevels.Length < 8) return 0m;
                long cent = 0;
                int[] vals = { 1, 2, 5, 10, 20, 50, 100, 200 };
                for (int i = 0; i < 8; i++)
                {
                    int c = coinLevels[i];
                    if (c < 0) c = 0;
                    cent += (long)c * vals[i];
                }
                return Math.Round(cent / 100m, 2);
            }
            catch { return 0m; }
        }

        private static decimal SumNv200Euro(NV200_SSP ssp)
        {
            try
            {
                if (ssp == null) return 0m;
                long payoutCent = (long)ssp.Payout_5_euro * 500 + (long)ssp.Payout_10_euro * 1000 + (long)ssp.Payout_20_euro * 2000 + (long)ssp.Payout_50_euro * 5000 + (long)ssp.Payout_100_euro * 10000 + (long)ssp.Payout_200_euro * 20000 + (long)ssp.Payout_500_euro * 50000;
                long cashboxCent = (long)ssp.Cashbox_5_euro * 500 + (long)ssp.Cashbox_10_euro * 1000 + (long)ssp.Cashbox_20_euro * 2000 + (long)ssp.Cashbox_50_euro * 5000 + (long)ssp.Cashbox_100_euro * 10000 + (long)ssp.Cashbox_200_euro * 20000 + (long)ssp.Cashbox_500_euro * 50000;
                return Math.Round((payoutCent + cashboxCent) / 100m, 2);
            }
            catch { return 0m; }
        }
    }
}
