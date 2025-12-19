using System;
using System.Windows.Forms;
using System.Drawing;
using System.Runtime.InteropServices;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using Geldautomat.Devices;
using Geldautomat.Coins;

namespace Geldautomat
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

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            _background = new BackgroundForm(KioskModeEnabled);
            _background.StartPosition = FormStartPosition.Manual;
            _background.Bounds = Screen.PrimaryScreen.Bounds;
            _background.Show();

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
    }
}
