using System;
using System.Windows.Forms;
using System.Drawing;
using System.Runtime.InteropServices;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System.Reflection;
using System.Net;
using TaMi_Einzahlautomat.Devices;
using TaMi_Einzahlautomat.Coins;
using TaMi_Einzahlautomat; // added for types still in original namespace

namespace TaMi_Einzahlautomat
{
    internal static class Program
    {
        public static NV200_SSP NV200Instance;
        public static NV200_SSP NV2002Instance;
        public static CoinFeederController CoinFeeder;

        private static BackgroundForm _background;
        public static bool KioskModeEnabled { get; private set; }
        internal static Form BackgroundFormInstance => _background;

        private static bool _controlledShutdown = false;
        private static bool _restarting = false;
        private static readonly string GuardDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "SuE-Software", "SuE-TaMi Client SQL");
        private static readonly string RestartGuardFile = Path.Combine(GuardDir, "restart_guard.log");
        private const int MaxRestartsInWindow = 5;
        private static readonly TimeSpan RestartWindow = TimeSpan.FromMinutes(2);

        private static bool _payoutLogAttached = false;
        private static bool _shutdownInitiated = false; // NEU: um mehrfaches Auslösen zu verhindern
        private static readonly object _shutdownLock = new object();

        // NEU: zentrale Methode für Logger-Shutdown (idempotent)
        private static int _loggerShutdownCalled = 0;
        private static void ShutdownLoggerSafe(string reason)
        {
            try
            {
                if (System.Threading.Interlocked.Exchange(ref _loggerShutdownCalled, 1) == 0)
                {
                    SafeLog("Logger Shutdown gestartet: " + reason);
                    AppLogger.Shutdown();
                    SafeLog("Logger Shutdown abgeschlossen");
                }
            }
            catch { }
        }

        // --- NEU: Gebündelte Münz-Verbinderung mit 3s Debounce ---
        private static readonly object _coinBundleLock = new object();
        private static int _coin1BundleCent = 0;
        private static int _coin2BundleCent = 0;
        private static System.Timers.Timer _coin1Debounce;
        private static System.Timers.Timer _coin2Debounce;
        private const int CoinBundleDebounceMs = 3000; // fester Wert

        private static void EnsureCoinDebounceTimers()
        {
            if (_coin1Debounce == null)
            {
                _coin1Debounce = new System.Timers.Timer(CoinBundleDebounceMs);
                _coin1Debounce.AutoReset = false;
                _coin1Debounce.Elapsed += (s, e) => CommitCoinBundle(1, null);
            }
            if (_coin2Debounce == null)
            {
                _coin2Debounce = new System.Timers.Timer(CoinBundleDebounceMs);
                _coin2Debounce.AutoReset = false;
                _coin2Debounce.Elapsed += (s, e) => CommitCoinBundle(2, null);
            }
        }

        private static void StartDebounce(int device)
        {
            EnsureCoinDebounceTimers();
            if (device == 1)
            {
                try { _coin1Debounce.Stop(); _coin1Debounce.Start(); } catch { }
            }
            else
            {
                try { _coin2Debounce.Stop(); _coin2Debounce.Start(); } catch { }
            }
        }

        private static void CommitCoinBundle(int device, string reason)
        {
            try
            {
                int cent;
                lock (_coinBundleLock)
                {
                    if (device == 1)
                    {
                        cent = _coin1BundleCent;
                        _coin1BundleCent = 0;
                    }
                    else
                    {
                        cent = _coin2BundleCent;
                        _coin2BundleCent = 0;
                    }
                }
                if (cent <= 0) return;

                var euro = cent / 100m;
                if (!string.IsNullOrEmpty(reason))
                {
                    try { AppLogger.Log($"[Fallback] Münz-Commit ausgelöst ({reason}) – Summe: {euro:C2}"); } catch { }
                }
                try { AppLogger.Log($"Münzauszahlung Summe: {euro:C2}"); } catch { }
                // Hinweis: Hier könnte die PG-Reduktion als eine Buchung erfolgen.
                // Beispiel (Pseudo): DatabaseHelper.ApplyPgDelta(-euro);
            }
            catch { }
        }

        private static void AttachCoinPayoutLogging()
        {
            if (_payoutLogAttached) return;
            try
            {
                EnsureCoinDebounceTimers();
                try { DeviceLogger.InitSmartCoins(); } catch { }
                var c1 = CoinManager.Instance as SmartCoinV1;
                if (c1 != null)
                {
                    // Auszahlungen (Delta-Events) – sammeln und nach 3s bundeln
                    c1.CoinDispensedDeltaCent += (cent) =>
                    {
                        try { AppLogger.Log($"(Coin/1) ---> Münze ausgezahlt: {(cent / 100m):C2}"); } catch { }
                        try { DeviceLogger.LogSmartCoin("SmartCoin1", $"DISP_DELTA {cent} ct"); } catch { }
                        lock (_coinBundleLock) { _coin1BundleCent += cent; }
                        StartDebounce(1);
                    };

                    // Einzahlungen (Akzeptierte Münzen)
                    c1.CoinAccepted += (cent) =>
                    {
                        try { AppLogger.Log($"(Coin/1) <--- Münze eingezahlt: {(cent / 100m):C2}"); } catch { }
                        try { DeviceLogger.LogSmartCoin("SmartCoin1", $"COIN_ACCEPTED {cent} ct"); } catch { }
                    };
                }
                else
                {
                    // RM5 als primäres Gerät
                    var rm5 = CoinManager.Instance as Rm5CctalkValidator;
                    if (rm5 != null)
                    {
                        rm5.CoinDispensedDeltaCent += (cent) =>
                        {
                            try { AppLogger.Log($"(RM5) ---> Münze ausgezahlt: {(cent / 100m):C2}"); } catch { }
                            lock (_coinBundleLock) { _coin1BundleCent += cent; }
                            StartDebounce(1);
                        };
                        // NEU: Einzahlungen RM5/1
                        rm5.CoinAccepted += (cent) =>
                        {
                            try { AppLogger.Log($"(RM5) <--- Münze eingezahlt: {(cent / 100m):C2}"); } catch { }
                        };
                    }
                }
            }
            catch { }
            try
            {
                EnsureCoinDebounceTimers();
                var c2 = Coin2Manager.Instance as SmartCoinV1;
                if (c2 != null)
                {
                    // Auszahlungen (Delta-Events) – sammeln und nach 3s bundeln
                    c2.CoinDispensedDeltaCent += (cent) =>
                    {
                        try { AppLogger.Log($"(Coin/2) ---> Münze ausgezahlt: {(cent / 100m):C2}"); } catch { }
                        try { DeviceLogger.LogSmartCoin("SmartCoin2", $"DISP_DELTA {cent} ct"); } catch { }
                        lock (_coinBundleLock) { _coin2BundleCent += cent; }
                        StartDebounce(2);
                    };
                    // Einzahlungen (Akzeptierte Münzen)
                    c2.CoinAccepted += (cent) =>
                    {
                        try { AppLogger.Log($"(Coin/2) <--- Münze eingezahlt: {(cent / 100m):C2}"); } catch { }
                        try { DeviceLogger.LogSmartCoin("SmartCoin2", $"COIN_ACCEPTED {cent} ct"); } catch { }
                    };
                }

            }
            catch { }
            _payoutLogAttached = true;
        }

        [STAThread]
        private static void Main()
        {
            // Ensure PdfSharp files are organized into a subfolder and can be loaded from there
            try
            {
                var baseDir = Application.StartupPath;
                var pdfSharpDir = Path.Combine(baseDir, "PDFSharp");
                try { Directory.CreateDirectory(pdfSharpDir); } catch { }

                // Move known PdfSharp/MigraDoc related files to the PDFSharp directory
                string[] patterns = new[]
                {
                    "PdfSharp*.dll",
                    "MigraDoc*.dll",
                    "SharpZipLib*.dll",
                    "System.Drawing.Common*.dll", // sometimes required by PdfSharp
                    "fonts.json", // common resource files if present
                    "*.ttf"
                };
                foreach (var pat in patterns)
                {
                    try
                    {
                        var files = Directory.GetFiles(baseDir, pat, SearchOption.TopDirectoryOnly);
                        foreach (var f in files)
                        {
                            var name = Path.GetFileName(f);
                            var dest = Path.Combine(pdfSharpDir, name);
                            if (!string.Equals(f, dest, StringComparison.OrdinalIgnoreCase))
                            {
                                try
                                {
                                    if (File.Exists(dest)) File.Delete(dest);
                                }
                                catch { }
                                try { File.Move(f, dest); } catch { }
                            }
                        }
                    }
                    catch { }
                }

                // Probe assemblies from PDFSharp folder
                AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
                {
                    try
                    {
                        var asmName = new AssemblyName(e.Name).Name + ".dll";
                        var candidate = Path.Combine(pdfSharpDir, asmName);
                        if (File.Exists(candidate)) return Assembly.LoadFrom(candidate);
                    }
                    catch { }
                    return null;
                };
            }
            catch { }

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Exception ex = e.ExceptionObject as Exception;
                SafeLog("UnhandledException: " + (ex != null ? ex.ToString() : e.ExceptionObject));
                TryScheduleRestart("UnhandledException");
            };
            Application.ThreadException += (s, e) =>
            {
                SafeLog("ThreadException: " + e.Exception);
                TryScheduleRestart("ThreadException");
            };

           

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            AppSettings.AutomatenName = AppSettings.LoadAutomatenNameFromIni();
            AppSettings.DeviceId = AppSettings.LoadDeviceIdFromIni(); // NEU
            if (AppSettings.DeviceId <= 0)
            {
                try { AppLogger.Init(); SafeLog("Abbruch: Keine gültige Device-ID in INI (Abschnitt [Device] ID=<zahl>) gefunden."); } catch { }
                MessageBox.Show("Fehler: Keine gültige Device-ID gefunden.\r\nBitte tragen Sie in der INI unter [Device] eine ID=<Zahl> ein und starten Sie neu.",
                                "Geräte-ID fehlt", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ShutdownLoggerSafe("DeviceId fehlt");
                return;
            }

            // TKassenbuchDevice laden und ggf. Start verhindern
            try
            {
                using (var db = new DatabaseHelper())
                {
                    var devInfo = db.GetKassenbuchDeviceInfoAsync(AppSettings.DeviceId).GetAwaiter().GetResult();
                    if (devInfo != null)
                    {
                        // AutomatenName aus DB bevorzugen
                        if (!string.IsNullOrWhiteSpace(devInfo.AutomatenName))
                        {
                            AppSettings.AutomatenName = devInfo.AutomatenName;
                        }
                        // AllowedManIDs (semicolon separated) -> store raw
                        if (devInfo.AllowedManID.HasValue)
                        {
                            AppSettings.AllowedManIdsRaw = devInfo.AllowedManID.Value == 0 ? null : devInfo.AllowedManID.Value.ToString();
                        }
                        if (devInfo.Gesperrt)
                        {
                            try { AppLogger.Init(); SafeLog($"Abbruch: Gerät gesperrt (DeviceID={AppSettings.DeviceId})."); } catch { }
                            MessageBox.Show($"Dieser Automat ist gesperrt (DeviceID={AppSettings.DeviceId}). Bitte wenden Sie sich an den Administrator.",
                                            "Automat gesperrt", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            ShutdownLoggerSafe("Device gesperrt");
                            return;
                        }
                        // AllowedManID wird später verwendet
                    }
                }
            }
            catch { }

            NV200Instance = new NV200_SSP();
            NV2002Instance = new NV200_SSP();
            CoinFeeder = new CoinFeederController();

            try { CoinManager.InitFromIni(AppSettings.IniPath); } catch { }
            try { Coin2Manager.InitFromIni(AppSettings.IniPath); } catch { }

            try { AttachCoinPayoutLogging(); } catch { }

            AppLogger.Init();
            try { AppLogger.LogStartBanner(); } catch { }
            SafeLog("Programmstart");

            // Check for available update on startup (non-blocking)
            try { Task.Run(() => PromptUpdateIfAvailable()); } catch { }

            // Daily background update check (configurable time in INI: [App] DailyUpdateCheckTime, default 12:00)
            try { StartDailyUpdateCheck(); } catch { }

            try { CoinFeederCoordinator.Start(); } catch { }

            try
            {
                string feederCom = IniHelper.ReadValue("CoinFeeder", "ComPort", AppSettings.IniPath);
                if (!string.IsNullOrWhiteSpace(feederCom))
                {
                    CoinFeeder.Configure(feederCom);
                    CoinFeeder.Mode = CoinFeederProtocolMode.ASCII;
                    CoinFeeder.Terminator = "\n";
                    CoinFeeder.AppendTerminatorAfterDollar = true;
                    CoinFeeder.Open();
                    SafeLog($"CoinFeeder verbunden auf {feederCom}");
                }
                else SafeLog("CoinFeeder: Kein COM-Port in INI gefunden.");
            }
            catch (Exception ex) { SafeLog("CoinFeeder Startfehler: " + ex.Message); }

            try
            {
                string com2 = IniHelper.ReadValue("NV200/2", "ComPort", AppSettings.IniPath);
                if (!string.IsNullOrWhiteSpace(com2))
                {
                    NV2002Instance.ComPort = com2;
                    NV2002Instance.SSPAdress = 0;
                    NV2002Instance.Starten();
                    try { NV2002Instance.Disable_Device(); } catch { }
                    try { NV2002Instance.SetInhibit(true); } catch { }
                    try { NV2002Instance.MitarbeiterEingeloggt = false; } catch { }
                    try { NV2002Instance.ConfigureBezel(255, 0, 0); } catch { }
                    SafeLog($"NV200/2 verbunden auf {com2}");
                }
                else SafeLog("NV200/2: Kein COM-Port in INI gefunden.");
            }
            catch (Exception ex) { SafeLog("NV200/2 Startfehler: " + ex.Message); }

            KioskModeEnabled = ReadKioskFlagFromIni();

            Application.ApplicationExit += (s, e) => { ShutdownLoggerSafe("ApplicationExit"); };

            _background = new BackgroundForm(KioskModeEnabled);
            _background.StartPosition = FormStartPosition.Manual;
            _background.Bounds = Screen.PrimaryScreen.Bounds;
            _background.Show();

            // Nach einem Update beim ersten Start die Release Notes anzeigen
            try { Task.Run(() => ShowReleaseNotesAsync(_background, forceShow: false)); } catch { }

            var login = new LoginForm(NV200Instance);
            CenterOverBackground(login);
            login.StartPosition = FormStartPosition.Manual;
            login.TopMost = KioskModeEnabled;
            login.Show(_background);

            login.FormClosing += (sender, args) =>
            {
                if (!_shutdownInitiated)
                {
                    args.Cancel = true;
                    BeginDelayedShutdown(false);
                }
            };

            if (_background != null)
            {
                _background.FormClosing += (sender, args) =>
                {
                    if (!_shutdownInitiated)
                    {
                        args.Cancel = true;
                        BeginDelayedShutdown(false);
                    }
                };
            }

            Application.Run(login);

            if (!_controlledShutdown && !_shutdownInitiated)
            {
                SafeLog("Unerwartetes Ende ohne kontrolliertes Beenden – Neustart wird versucht.");
                ShutdownLoggerSafe("UnexpectedExit");
                TryScheduleRestart("UnexpectedExit");
            }
            else
            {
                ShutdownLoggerSafe("NormalExit");
            }
        }

        private static void BeginDelayedShutdown(bool controlled)
        {
            lock (_shutdownLock)
            {
                if (_shutdownInitiated) return;
                _shutdownInitiated = true;
                if (controlled) _controlledShutdown = true;
            }

            // NEU: Coin2-Watchdog sofort ruhig stellen
            try { Coin2Manager.DisableForConfig(); } catch { }

            SafeLog("Verzögerter Shutdown gestartet – Geräte werden geschlossen...");
            try
            {
                // UI Hinweis
                try
                {
                    if (_background != null && !_background.IsDisposed)
                    {
                        _background.Invoke(new Action(() =>
                        {
                            _background.Text = "Beenden... (Geräte werden getrennt)";
                        }));
                    }
                }
                catch { }
            }
            catch { }

            Task.Run(async () =>
            {
                try
                {
                    // Reihenfolge: deaktivieren -> Shutdown
                    try { CoinManager.Enable(false); } catch { }
                    try { Coin2Manager.Enable(false); } catch { }

                    // Vor dem Shutdown eventuelle offene Münz-Bundles noch committen (Fallback)
                    try { CommitCoinBundle(1, "Shutdown"); CommitCoinBundle(2, "Shutdown"); } catch { }

                    try { CoinManager.Shutdown(); SafeLog("CoinManager heruntergefahren"); } catch { }
                    try { Coin2Manager.Shutdown(); SafeLog("Coin2Manager heruntergefahren"); } catch { }

                    // NV200 Geräte nur sperren (vollständiges Stopp kann sofort Port freigeben)
                    try
                    {
                        if (NV200Instance != null)
                        {
                            try { NV200Instance.Disable_Device(); } catch { }
                            try { NV200Instance.SetInhibit(true); } catch { }
                            try { NV200Instance.MitarbeiterEingeloggt = false; } catch { }
                            try { NV200Instance.Stopp(); } catch { }
                        }
                    }
                    catch { }
                    try
                    {
                        if (NV2002Instance != null)
                        {
                            try { NV2002Instance.Disable_Device(); } catch { }
                            try { NV2002Instance.SetInhibit(true); } catch { }
                            try { NV2002Instance.MitarbeiterEingeloggt = false; } catch { }
                            try { NV2002Instance.Stopp(); } catch { }
                        }
                    }
                    catch { }

                    try { CoinFeederCoordinator.Stop(); } catch { }
                    try { CoinFeeder?.Close(); } catch { }
                }
                finally
                {
                    SafeLog("Geräte getrennt – Warte 5 Sekunden vor Prozessende...");
                    try
                    {
                        if (_background != null && !_background.IsDisposed)
                        {
                            _background.Invoke(new Action(() => _background.Text = "Beenden in 5s ..."));
                        }
                    }
                    catch { }
                    try { await Task.Delay(5000); } catch { }
                    SafeLog("Beenden jetzt.");
                    try { AppLogger.LogEndBanner(); } catch { }
                    ShutdownLoggerSafe("DelayedShutdown");
                    try { ShowTaskbar(); } catch { }
                    try { Environment.Exit(0); } catch { }
                }
            });
        }

        public static void RequestControlledShutdown()
        {
            BeginDelayedShutdown(true);
        }

        private static void TryScheduleRestart(string reason)
        {
            if (_controlledShutdown) return;
            if (_restarting) return;
            _restarting = true;

            if (!CanRestartNow())
            {
                SafeLog("Restart unterdrückt (zu viele in kurzer Zeit). Grund: " + reason);
                ShutdownLoggerSafe("RestartSuppressed");
                return;
            }

            SafeLog("Starte Neustart (Grund: " + reason + ")");
            try { RecordRestart(); } catch { }

            try
            {
                var exe = Application.ExecutablePath;
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(exe)
                });
            }
            catch (Exception ex)
            {
                SafeLog("Neustart fehlgeschlagen: " + ex.Message);
            }

            ShutdownLoggerSafe("RestartExit");
            try { Environment.Exit(1); } catch { }
        }

        private static bool CanRestartNow()
        {
            try
            {
                Directory.CreateDirectory(GuardDir);
                if (!File.Exists(RestartGuardFile)) return true;

                var lines = File.ReadAllLines(RestartGuardFile);
                var now = DateTime.UtcNow;
                var recent = new List<DateTime>();
                foreach (var l in lines)
                {
                    DateTime dt;
                    if (DateTime.TryParse(l, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out dt))
                    {
                        if ((now - dt) <= RestartWindow) recent.Add(dt);
                    }
                }
                return recent.Count < MaxRestartsInWindow;
            }
            catch { return true; }
        }

        private static void RecordRestart()
        {
            try
            {
                Directory.CreateDirectory(GuardDir);
                File.AppendAllText(RestartGuardFile, DateTime.UtcNow.ToString("o") + Environment.NewLine);
            }
            catch { }
        }

        private static void SafeLog(string msg)
        {
            try { AppLogger.Log(msg); } catch { }
        }

        // Public entry points to trigger update check
        public static void CheckForUpdateNow()
        {
            try { Task.Run(() => PromptUpdateIfAvailable()); } catch { }
        }

        public static void CheckForUpdateNow(Form owner)
        {
            // Manuell (Button): Up-to-date Hinweis anzeigen
            try { Task.Run(() => PromptUpdateIfAvailableWithOwner(owner, showUpToDateMessage: true)); } catch { }
        }

        private static bool ReadKioskFlagFromIni()
        {
            try
            {
                var dbg = IniHelper.ReadValue("Device", "Debug", AppSettings.IniPath);
                if (string.IsNullOrWhiteSpace(dbg)) return false;
                var v = dbg.Trim();
                bool isFalse = v.Equals("false", StringComparison.OrdinalIgnoreCase) || v == "0" || v.Equals("no", StringComparison.OrdinalIgnoreCase) || v.Equals("off", StringComparison.OrdinalIgnoreCase);
                return isFalse;
            }
            catch { return false; }
        }

        private static void CenterOverBackground(Form child)
        {
            try
            {
                var screen = Screen.FromControl(_background);
                var bounds = screen.WorkingArea;
                child.Location = new Point(
                    bounds.Left + (bounds.Width - child.Width) / 2,
                    bounds.Top + (bounds.Height - child.Height) / 2);
            }
            catch { }
        }

        public static void ExitApplication()
        {
            BeginDelayedShutdown(false);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_SHOW = 5;
        public static void ShowTaskbar()
        {
            try
            {
                var taskbar = FindWindow("Shell_TrayWnd", null);
                if (taskbar != IntPtr.Zero) ShowWindow(taskbar, SW_SHOW);
                var start = FindWindow("Button", null);
                if (start != IntPtr.Zero) ShowWindow(start, SW_SHOW);
            }
            catch { }
        }

        private const string UpdateMsiUrl = "http://kassenautomat.priwitzer-dienstleistungsgmbh.de/Update_Einzahlautomat/Einzahlautomat_Setup.msi";
        private const string ReleaseNotesUrl = "http://kassenautomat.priwitzer-dienstleistungsgmbh.de/Update_Einzahlautomat/releasenotes.txt";

        private const string AppIniSection = "App";
        private const string LastSeenVersionKey = "LastSeenVersion";
        private const string LastUpdateAlertVersionKey = "LastUpdateAlertVersion";
        private const string DailyUpdateCheckTimeKey = "DailyUpdateCheckTime";
        private static System.Windows.Forms.Timer _dailyUpdateCheckTimer;

        private static (int hh, int mm) ReadDailyUpdateCheckTime()
        {
            int hh = 12;
            int mm = 0;
            try
            {
                var t = IniHelper.ReadValue(AppIniSection, DailyUpdateCheckTimeKey, AppSettings.IniPath);
                if (!string.IsNullOrWhiteSpace(t))
                {
                    var parts = t.Trim().Split(':');
                    if (parts.Length >= 2)
                    {
                        int.TryParse(parts[0], out hh);
                        int.TryParse(parts[1], out mm);
                    }
                }
            }
            catch { hh = 12; mm = 0; }

            if (hh < 0 || hh > 23) hh = 12;
            if (mm < 0 || mm > 59) mm = 0;
            return (hh, mm);
        }

        public static void ShowUpdateHints(Form owner)
        {
            try { Task.Run(() => ShowReleaseNotesAsync(owner, forceShow: true)); } catch { }
        }

        private static Version GetCurrentAppVersion()
        {
            try
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return v ?? new Version(0, 0, 0, 0);
            }
            catch { return new Version(0, 0, 0, 0); }
        }

        private static Version ReadLastSeenVersion()
        {
            try
            {
                var s = IniHelper.ReadValue(AppIniSection, LastSeenVersionKey, AppSettings.IniPath);
                if (string.IsNullOrWhiteSpace(s)) return null;
                Version v;
                return Version.TryParse(s.Trim(), out v) ? v : null;
            }
            catch { return null; }
        }

        private static Version ReadLastUpdateAlertVersion()
        {
            try
            {
                var s = IniHelper.ReadValue(AppIniSection, LastUpdateAlertVersionKey, AppSettings.IniPath);
                if (string.IsNullOrWhiteSpace(s)) return null;
                Version v;
                return Version.TryParse(s.Trim(), out v) ? v : null;
            }
            catch { return null; }
        }

        private static void WriteLastUpdateAlertVersion(Version v)
        {
            if (v == null) return;
            try { IniHelper.WriteValue(AppIniSection, LastUpdateAlertVersionKey, v.ToString(), AppSettings.IniPath); } catch { }
        }

        private static void StartDailyUpdateCheck()
        {
            try
            {
                if (_dailyUpdateCheckTimer != null) return;

                var (hh, mm) = ReadDailyUpdateCheckTime();
                var now = DateTime.Now;
                var next = new DateTime(now.Year, now.Month, now.Day, hh, mm, 0);
                if (next <= now) next = next.AddDays(1);
                var dueMs = Math.Max(1000, (int)Math.Min(int.MaxValue, (next - now).TotalMilliseconds));

                _dailyUpdateCheckTimer = new System.Windows.Forms.Timer();
                _dailyUpdateCheckTimer.Interval = dueMs;
                _dailyUpdateCheckTimer.Tick += (s, e) =>
                {
                    try
                    {
                        // After the first tick, run every 24h
                        try { _dailyUpdateCheckTimer.Interval = 24 * 60 * 60 * 1000; } catch { }
                        try { Task.Run(() => CheckForUpdateAndAlertAsync()); } catch { }
                    }
                    catch { }
                };
                _dailyUpdateCheckTimer.Start();
            }
            catch { }
        }

        private static async Task CheckForUpdateAndAlertAsync()
        {
            try
            {
                // Download MSI to temp to read ProductVersion
                string tempMsi = Path.Combine(Path.GetTempPath(), "Einzahlautomat_Setup.msi");
                try
                {
                    using (var wc = new WebClient())
                    {
                        wc.Proxy = WebRequest.DefaultWebProxy;
                        await wc.DownloadFileTaskAsync(UpdateMsiUrl, tempMsi).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    SafeLog("Daily update check: MSI download failed: " + ex.Message);
                    return;
                }

                Version localVer;
                if (!Version.TryParse(Application.ProductVersion, out localVer)) localVer = new Version(0, 0, 0, 0);
                Version remoteVer = GetMsiProductVersion(tempMsi) ?? new Version(0, 0, 0, 0);
                if (remoteVer <= localVer) return;

                // Alert only once per remote version
                var lastAlert = ReadLastUpdateAlertVersion();
                if (lastAlert != null && remoteVer <= lastAlert) return;

                string device = (AppSettings.AutomatenName ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(device)) device = "Automat";
                string msg = "Es ist ein Update verfügbar.\r\n" +
                             "Installierte Version: " + localVer + "\r\n" +
                             "Verfügbare Version: " + remoteVer + "\r\n" +
                             "Download: " + UpdateMsiUrl + "\r\n" +
                             "Zeitpunkt: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");

                try
                {
                    EmailReceiptService.SendAlertOnly("Update verfügbar (" + remoteVer + ")", msg);
                    WriteLastUpdateAlertVersion(remoteVer);
                }
                catch { }
            }
            catch (Exception ex)
            {
                SafeLog("Daily update check failed: " + ex.Message);
            }
        }

        private static void WriteLastSeenVersion(Version v)
        {
            if (v == null) return;
            try { IniHelper.WriteValue(AppIniSection, LastSeenVersionKey, v.ToString(), AppSettings.IniPath); } catch { }
        }

        private static async Task<string> TryDownloadReleaseNotesAsync()
        {
            try
            {
                using (var wc = new WebClient())
                {
                    wc.Proxy = WebRequest.DefaultWebProxy;
                    wc.Encoding = System.Text.Encoding.UTF8;
                    var txt = await wc.DownloadStringTaskAsync(ReleaseNotesUrl).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(txt)) return null;
                    return txt;
                }
            }
            catch (Exception ex)
            {
                SafeLog("ReleaseNotes Download fehlgeschlagen: " + ex.Message);
                return null;
            }
        }

        private static void ShowReleaseNotesOnUi(Form owner, string notesText)
        {
            var ui = owner ?? _background;
            if (ui == null || ui.IsDisposed) return;
            try
            {
                ui.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var title = "Updatehinweise";
                        if (!string.IsNullOrWhiteSpace(Application.ProductVersion))
                        {
                            title += " (" + Application.ProductVersion + ")";
                        }
                        MessageBox.Show(ui, notesText ?? "Keine Updatehinweise verfügbar.", title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch { }
                }));
            }
            catch { }
        }

        private static async Task ShowReleaseNotesAsync(Form owner, bool forceShow)
        {
            try
            {
                var current = GetCurrentAppVersion();
                var last = ReadLastSeenVersion();

                bool shouldShow = forceShow;
                if (!shouldShow)
                {
                    if (last == null) shouldShow = true;
                    else if (current > last) shouldShow = true;
                }

                if (!shouldShow) return;

                var notes = await TryDownloadReleaseNotesAsync().ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(notes))
                {
                    notes = "Keine Updatehinweise gefunden (releasenotes.txt).";
                }

                ShowReleaseNotesOnUi(owner, notes);

                if (!forceShow)
                {
                    WriteLastSeenVersion(current);
                }
            }
            catch { }
        }
        private static async Task PromptUpdateIfAvailable()
        {
            // Automatischer/Background-Check (z.B. beim Start): keine "Up-to-date" Meldung anzeigen
            await PromptUpdateIfAvailableWithOwner(_background, showUpToDateMessage: false).ConfigureAwait(false);
        }

        private static async Task PromptUpdateIfAvailableWithOwner(Form owner, bool showUpToDateMessage)
        {
            try
            {
                // Download MSI to temp to read ProductVersion
                string tempMsi = Path.Combine(Path.GetTempPath(), "Einzahlautomat_Setup.msi");
                try
                {
                    using (var wc = new WebClient())
                    {
                        wc.Proxy = WebRequest.DefaultWebProxy;
                        await wc.DownloadFileTaskAsync(UpdateMsiUrl, tempMsi).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    SafeLog("Update-Download für Versionsprüfung fehlgeschlagen: " + ex.Message);
                    return;
                }

                var localVerStr = Application.ProductVersion;
                Version localVer;
                if (!Version.TryParse(localVerStr, out localVer)) localVer = new Version(0, 0, 0, 0);

                 Version remoteVer = GetMsiProductVersion(tempMsi) ?? new Version(0, 0, 0, 0);
                 if (remoteVer <= localVer)
                 {
                     // Aktuell (oder neuer)
                     if (showUpToDateMessage)
                     {
                         var uiUpToDate = owner ?? _background;
                         if (uiUpToDate != null && !uiUpToDate.IsDisposed)
                         {
                             try
                             {
                                 uiUpToDate.BeginInvoke(new Action(() =>
                                 {
                                     try
                                     {
                                         MessageBox.Show(
                                             uiUpToDate,
                                             "Sie haben die aktuelle Version " + localVer + ".\r\nBesser als das wird’s heute nicht.",
                                             "Update",
                                             MessageBoxButtons.OK,
                                             MessageBoxIcon.Information);
                                     }
                                     catch { }
                                 }));
                             }
                             catch { }
                         }
                     }
                     return; // not newer
                 }

                // Ask user on UI thread
                var ui = owner ?? _background;
                if (ui != null && !ui.IsDisposed)
                {
                    ui.BeginInvoke(new Action(async () =>
                    {
                        var res = MessageBox.Show(
                            ui,
                            "Es ist eine neuere Version verfügbar (" + remoteVer + "). Möchten Sie das Update jetzt installieren?",
                            "Update verfügbar",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question);
                        if (res != DialogResult.Yes) return;

                        // MSI already downloaded to tempMsi

                        try
                        {
                            // Start MSI installer; let it handle elevation if needed
                            var psi = new ProcessStartInfo
                            {
                                FileName = "msiexec.exe",
                                Arguments = "/i \"" + tempMsi + "\"",
                                UseShellExecute = true,
                                Verb = "runas"
                            };
                            Process.Start(psi);
                        }
                        catch (Exception ex)
                        {
                            SafeLog("Update-Start fehlgeschlagen: " + ex.Message);
                            MessageBox.Show(ui, "Update konnte nicht gestartet werden:\r\n" + ex.Message, "Update", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }

                        // Close the app to allow installer to update files
                        try { RequestControlledShutdown(); } catch { }
                    }));
                }
            }
            catch (Exception ex)
            {
                SafeLog("Update-Prüfung fehlgeschlagen: " + ex.Message);
            }
        }

        private static Version GetMsiProductVersion(string msiPath)
        {
            try
            {
                var progId = Type.GetTypeFromProgID("WindowsInstaller.Installer");
                if (progId == null) return null;
                object installerObj = Activator.CreateInstance(progId);
                var installerType = installerObj.GetType();
                var db = installerType.InvokeMember("OpenDatabase", System.Reflection.BindingFlags.InvokeMethod, null, installerObj, new object[] { msiPath, 0 });
                var dbType = db.GetType();
                var view = dbType.InvokeMember("OpenView", System.Reflection.BindingFlags.InvokeMethod, null, db, new object[] { "SELECT `Value` FROM `Property` WHERE `Property`='ProductVersion'" });
                var viewType = view.GetType();
                viewType.InvokeMember("Execute", System.Reflection.BindingFlags.InvokeMethod, null, view, new object[] { null });
                var rec = viewType.InvokeMember("Fetch", System.Reflection.BindingFlags.InvokeMethod, null, view, null);
                if (rec == null) return null;
                var recType = rec.GetType();
                var val = recType.InvokeMember("StringData", System.Reflection.BindingFlags.GetProperty, null, rec, new object[] { 1 }) as string;
                Version v;
                if (Version.TryParse(val, out v)) return v;
                return null;
            }
            catch { return null; }
        }
    }
}
