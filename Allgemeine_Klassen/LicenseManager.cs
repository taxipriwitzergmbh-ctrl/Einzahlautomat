using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TaMi_Einzahlautomat
{
    /// <summary>
    /// Lizenz-Management über LAUS Server (Lizenz- und Update-Service)
    /// Prüft alle 3 Stunden die Gültigkeit der Lizenz
    /// </summary>
    public static class LicenseManager
    {
        private static readonly string LAUS_URL = "https://extern.sue-software.de/laus/";
        private static readonly string SOFTWARE_ID = "EZATM-03D77D5CX005056A0CE43XFA46CEAA";
        private static readonly string CAN_VALUE = "EZATM";
        private static readonly string CAV_VERSION = GetApplicationVersion();

        // Offline-Gnadenfrist: 24 Stunden nach letztem erfolgreichen Online-Check
        private const int OfflineGracePeriodHours = 24;
        private static DateTime _lastSuccessfulOnlineCheckUtc = DateTime.MinValue;
        private static readonly string _persistPath = GetPersistPath();

        private static System.Threading.Timer _licenseCheckTimer;
        private static bool _isLicenseValid = false;
        private static DateTime _lastCheckTime = DateTime.MinValue;
        private static string _lastError = string.Empty;
        private static readonly object _lock = new object();

        // Lizenzinfo-Cache
        private static string _customerName = string.Empty;
        private static string _customerId = string.Empty;
        private static string _lastSystemId = string.Empty;
        private static string _lastLicenseLogValue = string.Empty;
        private static int _licenseType = -1; // -1=Fehler, 0=Demo, 1=Kauf mit WV, 2=Kauf ohne WV, 3=Miete, 4=Partner, 5=Gesperrt
        private static readonly System.Collections.Generic.HashSet<string> _licensedSmartCoinSerials = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly System.Collections.Generic.HashSet<string> _licensedNv200Serials = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool _debugMode = false;
        // Gesetzt sobald die erste Lizenzprüfung (Erfolg oder Fehler) abgeschlossen ist
        private static volatile bool _licenseInitialized = false;

        /// <summary>
        /// True sobald die erste Lizenzprüfung abgeschlossen ist und Seriennummern geladen wurden.
        /// </summary>
        public static bool IsInitialized => _licenseInitialized;

        // Merker: letzter Online-Check ist fehlgeschlagen (Netzwerkfehler), Gnadenfrist läuft
        private static volatile bool _offlineGracePeriodActive = false;

        /// <summary>
        /// True wenn der Lizenzserver aktuell nicht erreichbar ist, aber die 24h-Gnadenfrist noch läuft.
        /// </summary>
        public static bool IsOfflineGracePeriodActive => _offlineGracePeriodActive;

        /// <summary>
        /// Lizenztyp-Zahl wie vom Server geliefert (-1=Fehler, 0=Demo, 1=Kauf mit WV, 2=Kauf ohne WV, 3=Miete, 4=Partner, 5=Gesperrt)
        /// </summary>
        public static int LicenseType { get { lock (_lock) return _licenseType; } }

        /// <summary>
        /// Lizenztyp als lesbarer Klartext
        /// </summary>
        public static string LicenseTypeName
        {
            get
            {
                switch (LicenseType)
                {
                    case -1: return "Fehler";
                    case  0: return "Demo";
                    case  1: return "Kauf mit WV";
                    case  2: return "Kauf ohne WV";
                    case  3: return "Miete";
                    case  4: return "Partner";
                    case  5: return "Gesperrt";
                    default: return "Unbekannt (" + LicenseType + ")";
                }
            }
        }

        /// <summary>
        /// True wenn der Lizenztyp Demo ist (Geräte-Seriennummern werden als nicht lizenziert behandelt)
        /// </summary>
        public static bool IsDemoLicense { get { lock (_lock) return _licenseType == 0; } }

        // Gesetzt nachdem der verzögerte Flush abgeschlossen ist (dann direkt loggen)
        private static volatile bool _pendingSerialChecksClosed = false;

        // Ausstehende Serienprüfungen, die vor der Initialisierung eingingen (werden danach nachgeprüft)
        private static readonly System.Collections.Generic.List<(string deviceName, string serialNumber, bool isSmartCoin)> _pendingSerialChecks
            = new System.Collections.Generic.List<(string, string, bool)>();

        public static bool IsLicenseValid
        {
            get { lock (_lock) return _isLicenseValid; }
        }

        public static string LastError
        {
            get { lock (_lock) return _lastError; }
        }

        public static DateTime LastCheckTime
        {
            get { lock (_lock) return _lastCheckTime; }
        }

        public static string CurrentSystemId
        {
            get { lock (_lock) return _lastSystemId; }
        }

        public static string CurrentCustomerId
        {
            get { lock (_lock) return _customerId; }
        }

        public static bool IsSmartCoinSerialLicensed(string serialNumber)
        {
            lock (_lock)
            {
                return !string.IsNullOrWhiteSpace(serialNumber)
                    && _licensedSmartCoinSerials.Contains(serialNumber.Trim());
            }
        }

        public static bool IsNv200SerialLicensed(string serialNumber)
        {
            lock (_lock)
            {
                return !string.IsNullOrWhiteSpace(serialNumber)
                    && _licensedNv200Serials.Contains(serialNumber.Trim());
            }
        }

        public static void InvalidateLicenseForUnknownSmartCoin(string deviceName, string serialNumber)
        {
            string device = string.IsNullOrWhiteSpace(deviceName) ? "SmartCoin" : deviceName.Trim();
            string serial = string.IsNullOrWhiteSpace(serialNumber) ? "unbekannt" : serialNumber.Trim();
            if (!_pendingSerialChecksClosed)
            {
                // Flush noch nicht abgeschlossen – Check für später vormerken
                lock (_pendingSerialChecks) { _pendingSerialChecks.Add((device, serial, true)); }
                return;
            }
            AppLogger.Log($"LicenseManager: Lizenz ungültig – unbekannte {device} Seriennummer {serial}");
        }

        public static void InvalidateLicenseForUnknownNv200(string deviceName, string serialNumber)
        {
            string device = string.IsNullOrWhiteSpace(deviceName) ? "NV200" : deviceName.Trim();
            string serial = string.IsNullOrWhiteSpace(serialNumber) ? "unbekannt" : serialNumber.Trim();
            if (!_pendingSerialChecksClosed)
            {
                // Flush noch nicht abgeschlossen – Check für später vormerken
                lock (_pendingSerialChecks) { _pendingSerialChecks.Add((device, serial, false)); }
                return;
            }
            AppLogger.Log($"LicenseManager: Lizenz ungültig – unbekannte {device} Seriennummer {serial}");
        }

        // Ausstehende Serienprüfungen nach Initialisierung nachprüfen und ggf. loggen
        private static void FlushPendingSerialChecks()
        {
            try
            {
                (string deviceName, string serialNumber, bool isSmartCoin)[] pending;
                lock (_pendingSerialChecks)
                {
                    pending = _pendingSerialChecks.ToArray();
                    _pendingSerialChecks.Clear();
                }
                foreach (var check in pending)
                {
                    try
                    {
                        bool licensed = check.isSmartCoin
                            ? IsSmartCoinSerialLicensed(check.serialNumber)
                            : IsNv200SerialLicensed(check.serialNumber);
                        if (!licensed)
                        {
                            AppLogger.Log($"LicenseManager: Lizenz ungültig – unbekannte {check.deviceName} Seriennummer {check.serialNumber}");
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        /// <summary>
        /// Liest die aktuelle Anwendungsversion aus der Assembly
        /// </summary>
        private static string GetApplicationVersion()
        {
            try
            {
                var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                return $"{version.Major}.{version.Minor}.{version.Build}";
            }
            catch
            {
                return "1.0.0"; // Fallback
            }
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool GetVolumeInformation(
            string rootPathName,
            StringBuilder volumeNameBuffer,
            int volumeNameSize,
            out uint volumeSerialNumber,
            out uint maximumComponentLength,
            out uint fileSystemFlags,
            StringBuilder fileSystemNameBuffer,
            int fileSystemNameSize);
        
        /// <summary>
        /// Initialisiert den LicenseManager und startet die 3-Stunden-Prüfung
        /// </summary>
        public static void Initialize()
        {
            try
            {
                // Debug-Modus aus INI lesen
                try
                {
                    var debugValue = IniHelper.ReadValue("App", "Debug", AppSettings.IniPath);
                    _debugMode = !string.IsNullOrWhiteSpace(debugValue) && !debugValue.Trim().Equals("0");
                }
                catch { _debugMode = false; }

                AppLogger.BeginLicenseBlock();
                AppLogger.Log("LicenseManager: Initialisierung gestartet");

                // Sofortige erste Prüfung (synchron beim Start)
                var task = CheckLicenseAsync();
                task.Wait(TimeSpan.FromSeconds(15)); // Max 15s warten beim Start

                if (!_isLicenseValid)
                {
                    string systemIdForLog;
                    string licenseValueForLog;
                    lock (_lock)
                    {
                        systemIdForLog = _lastSystemId;
                        licenseValueForLog = _lastLicenseLogValue;
                    }

                    if (!string.IsNullOrWhiteSpace(systemIdForLog) && !string.IsNullOrWhiteSpace(licenseValueForLog))
                    {
                        AppLogger.Log($"LicenseManager: WARNUNG - Lizenz ungültig: {systemIdForLog} Lizenz={licenseValueForLog}");
                    }
                    else
                    {
                        AppLogger.Log($"LicenseManager: WARNUNG - Lizenz ungültig: {_lastError}");
                    }
                }

                // Timer für alle 3 Stunden (3 * 60 * 60 * 1000 ms)
                _licenseCheckTimer = new System.Threading.Timer(
                    callback: async _ => await CheckLicenseAsync(),
                    state: null,
                    dueTime: TimeSpan.FromHours(3),
                    period: TimeSpan.FromHours(3)
                );

                // Nächste Prüfung berechnen
                var nextCheck = DateTime.Now.AddHours(3);
                var nextCheckStr = nextCheck.ToString("HH:mm");

                if (_debugMode)
                {
                    AppLogger.Log("LicenseManager: Initialisiert – Prüfung alle 3 Stunden");
                }
                else
                {
                    AppLogger.Log($"LicenseManager: Initialisiert – Lizenz Prüfung alle 3 Stunden nächste Prüfung {nextCheckStr} Uhr");
                }

                // Geladene Seriennummern immer loggen (hilft bei Mismatch-Diagnose)
                try
                {
                    string coinSerials, nv200Serials;
                    bool isDemo;
                    lock (_lock)
                    {
                        coinSerials  = _licensedSmartCoinSerials.Count  > 0 ? string.Join(", ", _licensedSmartCoinSerials)  : "(keine)";
                        nv200Serials = _licensedNv200Serials.Count > 0 ? string.Join(", ", _licensedNv200Serials) : "(keine)";
                        isDemo = _licenseType == 0;
                    }
                    AppLogger.Log($"LicenseManager: Lizenztyp: {LicenseTypeName}");
                    if (isDemo)
                    {
                        AppLogger.Log("LicenseManager: Demo-Lizenz – SmartCoin-Serials: (Demo, keine Lizenz)");
                        AppLogger.Log("LicenseManager: Demo-Lizenz – NV200-Serials: (Demo, keine Lizenz)");
                    }
                    else
                    {
                        AppLogger.Log($"LicenseManager: Lizenzierte SmartCoin-Serials: {coinSerials}");
                        AppLogger.Log($"LicenseManager: Lizenzierte NV200-Serials: {nv200Serials}");
                    }
                }
                catch { }

                // Ausstehende Serial-Prüfungen verzögert nachholen (NV200 verbindet sich async ~1-2s später)
                System.Threading.Tasks.Task.Run(() =>
                {
                    System.Threading.Thread.Sleep(4000);
                    FlushPendingSerialChecks();
                    _pendingSerialChecksClosed = true;
                    AppLogger.Log(new string('*', 80));
                    AppLogger.EndLicenseBlock();
                });
            }
            catch (Exception ex)
            {
                AppLogger.Log($"LicenseManager Init Fehler: {ex.Message}");
                lock (_lock)
                {
                    _isLicenseValid = false;
                    _lastError = ex.Message;
                }
            }
        }
        
        /// <summary>
        /// Beendet den LicenseManager und stoppt Timer
        /// </summary>
        public static void Shutdown()
        {
            try
            {
                _licenseCheckTimer?.Dispose();
                _licenseCheckTimer = null;
                AppLogger.Log("LicenseManager: Heruntergefahren");
            }
            catch { }
        }

        // --- Offline-Persistierung: letzten erfolgreichen Check-Zeitstempel speichern ---

        private static string GetPersistPath()
        {
            try
            {
                string dir = Path.GetDirectoryName(Application.ExecutablePath);
                return Path.Combine(dir, "lc.dat");
            }
            catch { return null; }
        }

        private static void SaveLastSuccessfulCheck(DateTime utc)
        {
            try
            {
                if (string.IsNullOrEmpty(_persistPath)) return;
                // Zeitstempel als Ticks verschlüsseln und speichern
                string plain = utc.Ticks.ToString();
                string enc = TinyEncrypt(plain);
                File.WriteAllText(_persistPath, enc, Encoding.ASCII);
            }
            catch { }
        }

        private static DateTime LoadLastSuccessfulCheck()
        {
            try
            {
                if (string.IsNullOrEmpty(_persistPath) || !File.Exists(_persistPath)) return DateTime.MinValue;
                string enc = File.ReadAllText(_persistPath, Encoding.ASCII).Trim();
                if (string.IsNullOrEmpty(enc)) return DateTime.MinValue;
                string plain = TinyDecrypt(enc);
                if (long.TryParse(plain, out long ticks) && ticks > 0)
                    return new DateTime(ticks, DateTimeKind.Utc);
            }
            catch { }
            return DateTime.MinValue;
        }

        private static bool IsWithinOfflineGracePeriod()
        {
            var last = _lastSuccessfulOnlineCheckUtc;
            if (last == DateTime.MinValue) last = LoadLastSuccessfulCheck();
            if (last == DateTime.MinValue) return false;
            return (DateTime.UtcNow - last).TotalHours < OfflineGracePeriodHours;
        }

        /// <summary>
        /// Prüft die Lizenz gegen den LAUS Server
        /// </summary>
        public static async Task<bool> CheckLicenseAsync()
        {
            try
            {
                lock (_lock) { _lastCheckTime = DateTime.Now; }

                string systemId = GetSystemId();
                if (string.IsNullOrEmpty(systemId))
                {
                    lock (_lock)
                    {
                        _isLicenseValid = false;
                        _lastError = "Konnte System-ID nicht ermitteln";
                        _lastSystemId = string.Empty;
                        _lastLicenseLogValue = string.Empty;
                        _licensedSmartCoinSerials.Clear();
                        _licensedNv200Serials.Clear();
                    }
                    AppLogger.Log("LicenseManager: System-ID konnte nicht ermittelt werden");
                    return false;
                }

                lock (_lock)
                {
                    _lastSystemId = systemId;
                    _lastLicenseLogValue = string.Empty;
                }

                string encryptedSid = TinyEncrypt(systemId);
                string encryptedCan = TinyEncrypt(CAN_VALUE);
                string encryptedCav = TinyEncrypt(CAV_VERSION);

                string url = $"{LAUS_URL}?v=101&sid={encryptedSid}&can={encryptedCan}&cav={encryptedCav}";

                if (_debugMode)
                {
                    AppLogger.Log($"LicenseManager: Prüfe Lizenz... (System-ID: {systemId.Substring(0, Math.Min(20, systemId.Length))}...)");
                }

                try
                {
                    using (var client = new HttpClient())
                    {
                        client.Timeout = TimeSpan.FromSeconds(10);

                        var response = await client.PostAsync(url, null);
                        string responseText = await response.Content.ReadAsStringAsync();
                        responseText = (responseText ?? "").Trim();

                        var parsed = ParseLausResponse(responseText);

                        bool valid = false;
                        string errorMsg = string.Empty;
                        string decryptedLic = string.Empty;

                        if (parsed.ContainsKey("msg") && parsed["msg"].Equals("OK", StringComparison.OrdinalIgnoreCase))
                        {
                            if (parsed.ContainsKey("lic"))
                            {
                                decryptedLic = TinyDecrypt(parsed["lic"]);

                                var licInfo = ParseLicenseInfo(decryptedLic);

                                // Lizenztyp auslesen und speichern
                                int parsedLicType = -1;
                                if (licInfo.ContainsKey("ltyp") && int.TryParse(licInfo["ltyp"], out int lt))
                                    parsedLicType = lt;

                                bool isBlockedByType = (parsedLicType == -1 || parsedLicType == 5); // Fehler oder Gesperrt
                                bool isDemo = (parsedLicType == 0);

                                lock (_lock)
                                {
                                    _licenseType = parsedLicType;
                                    _customerName = licInfo.ContainsKey("customer") ? licInfo["customer"] : string.Empty;
                                    _customerId = licInfo.ContainsKey("customerid") ? licInfo["customerid"] : string.Empty;
                                    _lastLicenseLogValue = ExtractLicenseLogValue(decryptedLic);
                                    _licensedSmartCoinSerials.Clear();
                                    _licensedNv200Serials.Clear();

                                    // Bei Demo: Seriennummern NICHT übernehmen -> alle Geräte gelten als unlizenziert
                                    if (!isDemo)
                                    {
                                        foreach (var entry in licInfo)
                                        {
                                            if (entry.Key.StartsWith("coin", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(entry.Value))
                                                _licensedSmartCoinSerials.Add(entry.Value.Trim());
                                            if (entry.Key.StartsWith("nv200", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(entry.Value))
                                                _licensedNv200Serials.Add(entry.Value.Trim());
                                        }
                                    }
                                }

                                // Ablaufdatum auslesen und loggen
                                string ablaufStr = licInfo.ContainsKey("ablauf") ? licInfo["ablauf"] : string.Empty;

                                // Klartext-Lizenztyp immer loggen
                                string typName = LicenseTypeName;
                                string customerPart = !string.IsNullOrEmpty(_customerName) ? $"Kunde:{_customerName}" : string.Empty;
                                string idPart = !string.IsNullOrEmpty(_customerId) ? $"Knd:{_customerId}" : string.Empty;
                                string combined = string.Join(", ", new[] { customerPart, idPart }.Where(s => !string.IsNullOrEmpty(s)));

                                if (licInfo.ContainsKey("ezatm") && licInfo["ezatm"].Equals("1") && !isBlockedByType)
                                {
                                    valid = true;

                                    _lastSuccessfulOnlineCheckUtc = DateTime.UtcNow;
                                    SaveLastSuccessfulCheck(_lastSuccessfulOnlineCheckUtc);
                                    _offlineGracePeriodActive = false;

                                    string demoHint = isDemo ? " [DEMO – Geräte nicht lizenziert]" : string.Empty;
                                    AppLogger.Log($"LicenseManager: Lizenzprüfung erfolgreich – Typ: {typName}{demoHint} | {combined} | Ablauf: {ablaufStr}");

                                    if (isDemo)
                                        AppLogger.Log("LicenseManager: Demo-Lizenz aktiv – Geräte-Seriennummern werden nicht übernommen, alle Geräte gelten als unlizenziert.");
                                }
                                else
                                {
                                    // ezatm!=1 oder Typ Fehler/Gesperrt -> ungültig, sofort sperren
                                    if (isBlockedByType)
                                        errorMsg = $"Lizenztyp gesperrt: {typName}";
                                    else
                                        errorMsg = "Keine gültige EZATM-Lizenz";

                                    AppLogger.Log($"LicenseManager: Lizenz ungültig – Typ: {typName} | {combined} | Ablauf: {ablaufStr} | Grund: {errorMsg}");
                                }
                            }
                            else
                            {
                                errorMsg = "Keine Lizenzinfo im Response";
                            }
                        }
                        else
                        {
                            // Server antwortet, aber msg != OK -> sofort sperren, keine Gnadenfrist
                            errorMsg = parsed.ContainsKey("msg") ? parsed["msg"] : responseText;
                        }

                        lock (_lock)
                        {
                            _isLicenseValid = valid;
                            _lastError = valid ? string.Empty : errorMsg;
                        }
                        _licenseInitialized = true;

                        if (!valid)
                        {
                            string customerPart = !string.IsNullOrEmpty(_customerName) ? $"Kunde:{_customerName}" : string.Empty;
                            string idPart = !string.IsNullOrEmpty(_customerId) ? $"Knd:{_customerId}" : string.Empty;
                            string combined = string.Join(", ", new[] { customerPart, idPart }.Where(s => !string.IsNullOrEmpty(s)));

                            if (!string.IsNullOrEmpty(combined))
                                AppLogger.Log($"LicenseManager: Lizenz ungültig – Fehler: {errorMsg} für {combined} Einzahlautomat");
                            else
                                AppLogger.Log($"LicenseManager: Lizenz ungültig – Fehler: {errorMsg}");
                        }

                        return valid;
                    }
                }
                catch (Exception netEx)
                {
                    // Netzwerkfehler (kein Internet, Timeout, DNS etc.) -> Offline-Gnadenfrist prüfen
                    AppLogger.Log($"LicenseManager: Netzwerkfehler bei Lizenzprüfung: {netEx.Message}");

                    bool withinGrace = IsWithinOfflineGracePeriod();
                    var lastOk = _lastSuccessfulOnlineCheckUtc != DateTime.MinValue
                        ? _lastSuccessfulOnlineCheckUtc
                        : LoadLastSuccessfulCheck();

                    if (withinGrace)
                    {
                        double hoursAgo = lastOk != DateTime.MinValue
                            ? (DateTime.UtcNow - lastOk).TotalHours
                            : 0;
                        double hoursLeft = OfflineGracePeriodHours - hoursAgo;

                        AppLogger.Log($"LicenseManager: Offline-Gnadenfrist aktiv – letzter erfolgreicher Check vor {hoursAgo:F1}h, noch {hoursLeft:F1}h verfügbar. Software bleibt aktiv.");

                        _offlineGracePeriodActive = true; // Merker setzen: Gnadenfrist läuft

                        lock (_lock)
                        {
                            if (!_isLicenseValid)
                            {
                                _lastError = $"Kein Internetzugang – Gnadenfrist: noch {hoursLeft:F1}h";
                            }
                        }
                        _licenseInitialized = true;
                        return _isLicenseValid;
                    }
                    else
                    {
                        double hoursAgo = lastOk != DateTime.MinValue
                            ? (DateTime.UtcNow - lastOk).TotalHours
                            : double.MaxValue;

                        AppLogger.Log($"LicenseManager: Offline-Gnadenfrist abgelaufen (letzter Kontakt vor {hoursAgo:F1}h) – Software wird gesperrt.");

                        _offlineGracePeriodActive = false; // Gnadenfrist vorbei

                        lock (_lock)
                        {
                            _isLicenseValid = false;
                            _lastError = $"Kein Internetzugang seit mehr als {OfflineGracePeriodHours}h – Lizenz abgelaufen";
                        }
                        _licenseInitialized = true;
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                lock (_lock)
                {
                    _lastError = ex.Message;
                    _isLicenseValid = false;
                }
                AppLogger.Log($"LicenseManager: Fehler bei Lizenzprüfung: {ex.Message}");
                _licenseInitialized = true;
                return false;
            }
        }
        
        /// <summary>
        /// Erstellt die System-ID aus Software-Name + Volume-ID, MAC-Adresse und Pfad-CRC
        /// Format: EZATM-VVVVVVVVXMMMMMMMMMMMXPPPPPPPP (Hex-formatiert)
        /// </summary>
        private static string GetSystemId()
        {
            try
            {
                uint volumeId = GetDiskId();
                string mac = GetMAC();
                uint pathCrc = GetPathCRC();

                string formattedVolumeId = volumeId.ToString("X8");
                string formattedMAC = mac.Replace(":", "").Replace("-", "");
                string formattedPathCRC = pathCrc.ToString("X8");

                // System-ID mit EZATM-Präfix
                return $"{CAN_VALUE}-{formattedVolumeId}X{formattedMAC}X{formattedPathCRC}";
            }
            catch (Exception ex)
            {
                AppLogger.Log($"GetSystemId Fehler: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Parst LAUS Server Response (Format: "v=100;r=1;msg=OK;lic=...")
        /// </summary>
        private static System.Collections.Generic.Dictionary<string, string> ParseLausResponse(string response)
        {
            var result = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (string.IsNullOrWhiteSpace(response))
                    return result;

                // Aufteilen nach Semikolon
                var parts = response.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var part in parts)
                {
                    var keyValue = part.Split(new[] { '=' }, 2);
                    if (keyValue.Length == 2)
                    {
                        string key = keyValue[0].Trim();
                        string value = keyValue[1].Trim();

                        if (!string.IsNullOrEmpty(key))
                        {
                            result[key] = value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"ParseLausResponse Fehler: {ex.Message}");
            }

            return result;
        }

        private static string ExtractLicenseLogValue(string licenseInfo)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(licenseInfo))
                    return string.Empty;

                var parts = licenseInfo.Split(new[] { "::" }, StringSplitOptions.None);
                if (parts.Length > 3 && !string.IsNullOrWhiteSpace(parts[3]))
                    return parts[3].Trim();

                return licenseInfo.Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Parst die entschlüsselte Lizenzinfo
        /// Format: SYSTEMID::?::ABLAUF::fhz=0,ezatm=1::KNDNR::KUNDE::::CHECKSUM
        /// </summary>
        private static System.Collections.Generic.Dictionary<string, string> ParseLicenseInfo(string licenseInfo)
        {
            var result = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (string.IsNullOrWhiteSpace(licenseInfo))
                    return result;

                // Aufteilen nach ::
                var parts = licenseInfo.Split(new[] { "::" }, StringSplitOptions.None);

                if (parts.Length >= 6)
                {
                    // Index 1: Lizenztyp (-1=Fehler, 0=Demo, 1=Kauf mit WV, ...)
                    if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
                    {
                        result["ltyp"] = parts[1].Trim();
                    }

                    // Index 2: Ablaufdatum
                    if (parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]))
                    {
                        result["ablauf"] = parts[2].Trim();
                    }

                    // Index 3: Lizenzen (ezatm=1,coin1=...,nv200_1=...)
                    if (parts.Length > 3)
                    {
                        var licenses = parts[3].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var lic in licenses)
                        {
                            var kv = lic.Split(new[] { '=' }, 2);
                            if (kv.Length == 2)
                            {
                                result[kv[0].Trim()] = kv[1].Trim();
                            }
                        }
                    }

                    // Index 4: Kundennummer
                    if (parts.Length > 4 && !string.IsNullOrWhiteSpace(parts[4]))
                    {
                        result["customerid"] = parts[4].Trim();
                    }

                    // Index 5: Kundenname
                    if (parts.Length > 5 && !string.IsNullOrWhiteSpace(parts[5]))
                    {
                        result["customer"] = parts[5].Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"ParseLicenseInfo Fehler: {ex.Message}");
            }

            return result;
        }
        
        /// <summary>
        /// Liest die Volume Serial Number (Disk ID) der Systempartition
        /// </summary>
        private static uint GetDiskId()
        {
            try
            {
                string drive = Path.GetPathRoot(Application.StartupPath);
                
                StringBuilder volumeName = new StringBuilder(256);
                StringBuilder fileSystemName = new StringBuilder(256);
                uint serialNumber;
                uint maxComponentLength;
                uint fileSystemFlags;
                
                bool success = GetVolumeInformation(
                    drive,
                    volumeName,
                    volumeName.Capacity,
                    out serialNumber,
                    out maxComponentLength,
                    out fileSystemFlags,
                    fileSystemName,
                    fileSystemName.Capacity);
                
                return success ? serialNumber : 0;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"GetDiskId Fehler: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Liest die MAC-Adresse der ersten physischen Ethernet-Netzwerkkarte
        /// </summary>
        private static string GetMAC()
        {
            try
            {
                var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();

                // 1. Bevorzugt: Ethernet Adapter mit Status Up
                string ethernetMac = GetMacFromInterfaceType(
                    interfaces,
                    System.Net.NetworkInformation.NetworkInterfaceType.Ethernet
                );

                if (!string.IsNullOrEmpty(ethernetMac))
                    return ethernetMac;

                // 2. Fallback: WLAN Adapter mit Status Up
                string wlanMac = GetMacFromInterfaceType(
                    interfaces,
                    System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211
                );

                if (!string.IsNullOrEmpty(wlanMac))
                    return wlanMac;

                return string.Empty;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"GetMAC Fehler: {ex.Message}");
                return string.Empty;
            }
        }

        private static string GetMacFromInterfaceType(
            System.Net.NetworkInformation.NetworkInterface[] interfaces,
            System.Net.NetworkInformation.NetworkInterfaceType interfaceType)
        {
            foreach (var nic in interfaces)
            {
                try
                {
                    if (nic.NetworkInterfaceType == interfaceType &&
                        nic.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                    {
                        var mac = nic.GetPhysicalAddress().ToString();

                        if (!string.IsNullOrEmpty(mac) && mac != "000000000000")
                        {
                            return FormatMac(mac);
                        }
                    }
                }
                catch { }
            }

            return string.Empty;
        }

        private static string FormatMac(string mac)
        {
            mac = mac.Replace(":", "").Replace("-", "").ToUpperInvariant();

            if (mac.Length == 12)
            {
                var formatted = string.Empty;

                for (int i = 0; i < mac.Length; i += 2)
                {
                    if (formatted.Length > 0)
                        formatted += ":";

                    formatted += mac.Substring(i, 2);
                }

                return formatted;
            }

            return mac;
        }

        /// <summary>
        /// Erstellt CRC32-Prüfsumme über Pfad + Dateiname der EXE
        /// </summary>
        private static uint GetPathCRC()
        {
            try
            {
                string exePath = Application.ExecutablePath;
                string path = Path.GetDirectoryName(exePath);
                string fileName = Path.GetFileNameWithoutExtension(exePath);
                string fullPath = Path.Combine(path, fileName);
                
                byte[] pathBytes = Encoding.Default.GetBytes(fullPath);
                return CRC32(pathBytes);
            }
            catch (Exception ex)
            {
                AppLogger.Log($"GetPathCRC Fehler: {ex.Message}");
                return 0;
            }
        }
        
        /// <summary>
        /// Berechnet CRC32-Prüfsumme
        /// </summary>
        private static uint CRC32(byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            
            for (int i = 0; i < data.Length; i++)
            {
                crc ^= data[i];
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 1) != 0)
                        crc = (crc >> 1) ^ 0xEDB88320;
                    else
                        crc >>= 1;
                }
            }
            
            return ~crc;
        }
        
        /// <summary>
        /// Verschlüsselt Text mit tinyEncrypt-Algorithmus (kompatibel mit VB6-Version)
        /// </summary>
        private static string TinyEncrypt(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;
            
            try
            {
                const string S = "w4tykl1j8kie5kbtol3ou3qmm0fgssgj8nplxdcgcdqa0xo1cmvwo7lcmno9p8o839bcqobxo65qmfdesk2002j1xl8eseyz1tc6mylr4sb6geupohncz5zh7vz76upkrcobrqkrd95wqehrmeg5t1ch3k32cr73alnp3nj6jzfpm80sanqtml9c2ivre08cwyda3mx3fdhaj9d7e6k8w306jl7jqhh7mjiwlgj7rovi366e8rlsaywqkwhg46ybyqgofqg0wh2ongt9mwlyl24djqujlel3o254qruxxqk4bly8ozs0h4c1mulqtvxmjorhcc4aeqeh19pf2mkgfe2s2tuejppq68zj5b3ckufs0cfh9ysz4h74rr4f2fqzdoheijh0u2hkv6stthqnnd699gdqz6zt4p56s9owun35y04z1hvd4q1o86mqq3yk5lg772lkl7cjqq62f7gapstaymt0uzlo6t9odznycu5ritvumma8t2qpmpfy1rwu";
                
                byte[] K = new byte[32]
                {
                    17, 1, 7, 26, 8, 18, 10, 4,
                    23, 23, 16, 11, 10, 19, 24, 24,
                    18, 19, 8, 20, 8, 22, 6, 18,
                    11, 24, 25, 26, 12, 6, 22, 19
                };
                
                int kl = K.Length;
                int strl = input.Length;
                byte[] encrypted = new byte[strl];
                
                int j = 0;
                for (int i = 0; i < strl; i++)
                {
                    byte e = (byte)input[i];
                    encrypted[i] = (byte)(e ^ K[j]);
                    j++;
                    if (j >= kl) j = 0;
                }
                
                StringBuilder result = new StringBuilder(strl * 2);
                for (int i = 0; i < strl; i++)
                {
                    int idx = encrypted[i] * 2;
                    if (idx >= 0 && idx + 1 < S.Length)
                    {
                        result.Append(S.Substring(idx, 2));
                    }
                }
                
                return result.ToString();
            }
            catch (Exception ex)
            {
                AppLogger.Log($"TinyEncrypt Fehler: {ex.Message}");
                return string.Empty;
            }
        }
        
        /// <summary>
        /// Entschlüsselt Text mit tinyDecrypt-Algorithmus (kompatibel mit VB6-Version)
        /// </summary>
        public static string TinyDecrypt(string input)
        {
            if (string.IsNullOrEmpty(input) || input.Length % 2 != 0)
                return string.Empty;
            
            try
            {
                const string S = "w4tykl1j8kie5kbtol3ou3qmm0fgssgj8nplxdcgcdqa0xo1cmvwo7lcmno9p8o839bcqobxo65qmfdesk2002j1xl8eseyz1tc6mylr4sb6geupohncz5zh7vz76upkrcobrqkrd95wqehrmeg5t1ch3k32cr73alnp3nj6jzfpm80sanqtml9c2ivre08cwyda3mx3fdhaj9d7e6k8w306jl7jqhh7mjiwlgj7rovi366e8rlsaywqkwhg46ybyqgofqg0wh2ongt9mwlyl24djqujlel3o254qruxxqk4bly8ozs0h4c1mulqtvxmjorhcc4aeqeh19pf2mkgfe2s2tuejppq68zj5b3ckufs0cfh9ysz4h74rr4f2fqzdoheijh0u2hkv6stthqnnd699gdqz6zt4p56s9owun35y04z1hvd4q1o86mqq3yk5lg772lkl7cjqq62f7gapstaymt0uzlo6t9odznycu5ritvumma8t2qpmpfy1rwu";
                
                int len = input.Length / 2;
                byte[] decrypted = new byte[len];
                
                for (int i = 0; i < input.Length; i += 2)
                {
                    string v = input.Substring(i, 2);
                    int n = -1;
                    
                    for (int j = 0; j < S.Length; j += 2)
                    {
                        if (S.Substring(j, 2) == v)
                        {
                            n = j / 2;
                            break;
                        }
                    }
                    
                    decrypted[i / 2] = (byte)n;
                }
                
                byte[] K = new byte[32]
                {
                    17, 1, 7, 26, 8, 18, 10, 4,
                    23, 23, 16, 11, 10, 19, 24, 24,
                    18, 19, 8, 20, 8, 22, 6, 18,
                    11, 24, 25, 26, 12, 6, 22, 19
                };
                
                int kl = K.Length;
                int j2 = 0;
                char[] result = new char[len];
                
                for (int i = 0; i < len; i++)
                {
                    byte e = decrypted[i];
                    result[i] = (char)(e ^ K[j2]);
                    j2++;
                    if (j2 >= kl) j2 = 0;
                }
                
                return new string(result);
            }
            catch (Exception ex)
            {
                AppLogger.Log($"TinyDecrypt Fehler: {ex.Message}");
                return string.Empty;
            }
        }
    }
}
