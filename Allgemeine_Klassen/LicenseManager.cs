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
        private static readonly string CAV_VERSION = "1.0.0";

        private static System.Threading.Timer _licenseCheckTimer;
        private static bool _isLicenseValid = false;
        private static DateTime _lastCheckTime = DateTime.MinValue;
        private static string _lastError = string.Empty;
        private static readonly object _lock = new object();

        // Lizenzinfo-Cache
        private static string _customerName = string.Empty;
        private static string _customerId = string.Empty;
        private static bool _debugMode = false;

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

                AppLogger.Log("LicenseManager: Initialisierung gestartet");

                // Sofortige erste Prüfung (synchron beim Start)
                var task = CheckLicenseAsync();
                task.Wait(TimeSpan.FromSeconds(15)); // Max 15s warten beim Start

                if (!_isLicenseValid)
                {
                    AppLogger.Log($"LicenseManager: WARNUNG - Lizenz ungültig: {_lastError}");
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
                    }
                    AppLogger.Log("LicenseManager: System-ID konnte nicht ermittelt werden");
                    return false;
                }

                string encryptedSid = TinyEncrypt(systemId);
                string encryptedCan = TinyEncrypt(CAN_VALUE);
                string encryptedCav = TinyEncrypt(CAV_VERSION);

                string url = $"{LAUS_URL}?v=101&sid={encryptedSid}&can={encryptedCan}&cav={encryptedCav}";

                // System-ID nur im Debug-Modus loggen
                if (_debugMode)
                {
                    AppLogger.Log($"LicenseManager: Prüfe Lizenz... (System-ID: {systemId.Substring(0, Math.Min(20, systemId.Length))}...)");
                }

                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);

                    var response = await client.PostAsync(url, null);
                    string responseText = await response.Content.ReadAsStringAsync();
                    responseText = (responseText ?? "").Trim();

                    // LAUS Server Antwort parsen: "v=100;r=1;msg=OK;lic=..."
                    var parsed = ParseLausResponse(responseText);

                    bool valid = false;
                    string errorMsg = string.Empty;
                    string decryptedLic = string.Empty;

                    if (parsed.ContainsKey("msg") && parsed["msg"].Equals("OK", StringComparison.OrdinalIgnoreCase))
                    {
                        // msg=OK -> Lizenz potentiell gültig, jetzt lic= prüfen
                        if (parsed.ContainsKey("lic"))
                        {
                            decryptedLic = TinyDecrypt(parsed["lic"]);

                            // Lizenzinfo parsen und ezatm=1 prüfen
                            var licInfo = ParseLicenseInfo(decryptedLic);

                            // Kundendaten speichern (für Fehlerfall)
                            lock (_lock)
                            {
                                _customerName = licInfo.ContainsKey("customer") ? licInfo["customer"] : string.Empty;
                                _customerId = licInfo.ContainsKey("customerid") ? licInfo["customerid"] : string.Empty;
                            }

                            if (licInfo.ContainsKey("ezatm") && licInfo["ezatm"].Equals("1"))
                            {
                                // ezatm=1 -> Lizenz gültig
                                valid = true;

                                // Logging je nach Debug-Modus
                                if (_debugMode)
                                {
                                    AppLogger.Log($"LicenseManager: Lizenzinfo entschlüsselt: {decryptedLic}");
                                    AppLogger.Log("LicenseManager: Lizenzprüfung erfolgreich (OK)");
                                }
                                else
                                {
                                    string customerPart = !string.IsNullOrEmpty(_customerName) ? $"Kunde:{_customerName}" : string.Empty;
                                    string idPart = !string.IsNullOrEmpty(_customerId) ? $"Knd:{_customerId}" : string.Empty;
                                    string combined = string.Join(", ", new[] { customerPart, idPart }.Where(s => !string.IsNullOrEmpty(s)));
                                    AppLogger.Log($"LicenseManager: Lizenzprüfung erfolgreich (OK) {combined} Einzahlautomat");
                                }
                            }
                            else
                            {
                                // ezatm != 1 -> ungültige Lizenz
                                errorMsg = "Keine gültige EZATM-Lizenz";
                                if (_debugMode)
                                {
                                    AppLogger.Log($"LicenseManager: Lizenzinfo entschlüsselt: {decryptedLic}");
                                }
                            }
                        }
                        else
                        {
                            errorMsg = "Keine Lizenzinfo im Response";
                        }
                    }
                    else
                    {
                        // msg != OK oder nicht vorhanden -> ungültig
                        errorMsg = parsed.ContainsKey("msg") ? parsed["msg"] : responseText;
                    }

                    lock (_lock)
                    {
                        _isLicenseValid = valid;
                        _lastError = valid ? string.Empty : errorMsg;
                    }

                    if (!valid)
                    {
                        // Bei ungültiger Lizenz auch Kundeninfos anzeigen
                        string customerPart = !string.IsNullOrEmpty(_customerName) ? $"Kunde:{_customerName}" : string.Empty;
                        string idPart = !string.IsNullOrEmpty(_customerId) ? $"Knd:{_customerId}" : string.Empty;
                        string combined = string.Join(", ", new[] { customerPart, idPart }.Where(s => !string.IsNullOrEmpty(s)));

                        if (!string.IsNullOrEmpty(combined))
                        {
                            AppLogger.Log($"LicenseManager: Lizenz ungültig – Fehler: {errorMsg} für {combined} Einzahlautomat");
                        }
                        else
                        {
                            AppLogger.Log($"LicenseManager: Lizenz ungültig – Fehler: {errorMsg}");
                        }
                    }

                    return valid;
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
                    // Index 3: Lizenzen (fhz=0,ezatm=1)
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
                // Alternative zu WMI: NetworkInterface aus System.Net.NetworkInformation
                var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
                foreach (var nic in interfaces)
                {
                    try
                    {
                        // Nur physische Ethernet-Adapter (nicht virtuelle, Loopback, etc.)
                        if (nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Ethernet &&
                            nic.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                        {
                            var mac = nic.GetPhysicalAddress().ToString();
                            if (!string.IsNullOrEmpty(mac) && mac != "000000000000")
                            {
                                // MAC-Adresse formatieren mit Doppelpunkt (wie WMI: "AA:BB:CC:DD:EE:FF")
                                if (mac.Length == 12)
                                {
                                    var formatted = string.Empty;
                                    for (int i = 0; i < mac.Length; i += 2)
                                    {
                                        if (formatted.Length > 0) formatted += ":";
                                        formatted += mac.Substring(i, 2);
                                    }
                                    return formatted.ToUpperInvariant();
                                }
                                return mac.ToUpperInvariant();
                            }
                        }
                    }
                    catch { }
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"GetMAC Fehler: {ex.Message}");
                return string.Empty;
            }
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
