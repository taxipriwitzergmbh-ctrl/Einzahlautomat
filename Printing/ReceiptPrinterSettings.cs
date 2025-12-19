using System;
using System.IO;
using System.Xml.Serialization;

namespace Geldautomat.Printing
{
    // Einstellungen jetzt in Geldautomat.ini (Section [ReceiptPrinter])
    // Fallback: alte XML-Datei wird einmalig migriert.
    public class ReceiptPrinterSettings
    {
        private static readonly string XmlSettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "receiptprinter.settings.xml");
        private const string IniSection = "ReceiptPrinter";

        public bool Enabled { get; set; } = true;
        public bool AskUser { get; set; } = true;
        public bool PrintVatLines { get; set; } = true;
        public string PrinterName { get; set; } = string.Empty;
        public int CharsPerLine { get; set; } = 40;
        public bool CutAfterPrint { get; set; } = false;
        public bool AutoPrintIfNoPrompt { get; set; } = true;
        public string SecondaryPrinterName { get; set; } = string.Empty;
        public bool UseSecondaryOnFailure { get; set; } = true;

        // Legacy (serielle) Drucker Konfiguration
        public bool Legacy1Enabled { get; set; }
        public string Legacy1ComPort { get; set; } = string.Empty;
        public int Legacy1Baud { get; set; } = 9600;
        public int Legacy1Chars { get; set; } = 40;
        public bool Legacy1Cut { get; set; } = true;

        public bool Legacy2Enabled { get; set; }
        public string Legacy2ComPort { get; set; } = string.Empty;
        public int Legacy2Baud { get; set; } = 9600;
        public int Legacy2Chars { get; set; } = 40;
        public bool Legacy2Cut { get; set; } = true;

        public static ReceiptPrinterSettings Load()
        {
            var s = new ReceiptPrinterSettings();
            try
            {
                var ini = AppSettings.IniPath;
                if (File.Exists(ini))
                {
                    // Wenn Section existiert (Enabled key gesetzt), aus INI lesen
                    var enabledRaw = IniHelper.ReadValue(IniSection, nameof(Enabled), ini);
                    if (!string.IsNullOrEmpty(enabledRaw))
                    {
                        s.Enabled = ParseBool(enabledRaw, true);
                        s.AskUser = ParseBool(IniHelper.ReadValue(IniSection, nameof(AskUser), ini), true);
                        s.PrintVatLines = ParseBool(IniHelper.ReadValue(IniSection, nameof(PrintVatLines), ini), true);
                        s.PrinterName = IniHelper.ReadValue(IniSection, nameof(PrinterName), ini) ?? string.Empty;
                        s.CharsPerLine = ParseInt(IniHelper.ReadValue(IniSection, nameof(CharsPerLine), ini), 40, 10, 120);
                        s.CutAfterPrint = ParseBool(IniHelper.ReadValue(IniSection, nameof(CutAfterPrint), ini), false);
                        s.AutoPrintIfNoPrompt = ParseBool(IniHelper.ReadValue(IniSection, nameof(AutoPrintIfNoPrompt), ini), true);
                        s.SecondaryPrinterName = IniHelper.ReadValue(IniSection, nameof(SecondaryPrinterName), ini) ?? string.Empty;
                        s.UseSecondaryOnFailure = ParseBool(IniHelper.ReadValue(IniSection, nameof(UseSecondaryOnFailure), ini), true);

                        // Legacy Felder
                        s.Legacy1Enabled = ParseBool(IniHelper.ReadValue(IniSection, nameof(Legacy1Enabled), ini), false);
                        s.Legacy1ComPort = IniHelper.ReadValue(IniSection, nameof(Legacy1ComPort), ini) ?? string.Empty;
                        s.Legacy1Baud = ParseInt(IniHelper.ReadValue(IniSection, nameof(Legacy1Baud), ini), 9600, 1200, 115200);
                        s.Legacy1Chars = ParseInt(IniHelper.ReadValue(IniSection, nameof(Legacy1Chars), ini), 40, 10, 80);
                        s.Legacy1Cut = ParseBool(IniHelper.ReadValue(IniSection, nameof(Legacy1Cut), ini), true);
                        s.Legacy2Enabled = ParseBool(IniHelper.ReadValue(IniSection, nameof(Legacy2Enabled), ini), false);
                        s.Legacy2ComPort = IniHelper.ReadValue(IniSection, nameof(Legacy2ComPort), ini) ?? string.Empty;
                        s.Legacy2Baud = ParseInt(IniHelper.ReadValue(IniSection, nameof(Legacy2Baud), ini), 9600, 1200, 115200);
                        s.Legacy2Chars = ParseInt(IniHelper.ReadValue(IniSection, nameof(Legacy2Chars), ini), 40, 10, 80);
                        s.Legacy2Cut = ParseBool(IniHelper.ReadValue(IniSection, nameof(Legacy2Cut), ini), true);
                        return s;
                    }
                }

                // Falls keine INI-Werte vorhanden: Alte XML migrieren falls vorhanden
                if (File.Exists(XmlSettingsPath))
                {
                    try
                    {
                        var ser = new XmlSerializer(typeof(ReceiptPrinterSettings));
                        using (var fs = File.OpenRead(XmlSettingsPath))
                        {
                            var old = (ReceiptPrinterSettings)ser.Deserialize(fs);
                            if (old != null)
                            {
                                s = old;
                                // Direkt in INI persistieren (Migration)
                                s.Save();
                                return s;
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return s; // Defaults
        }

        public void Save()
        {
            try
            {
                var ini = AppSettings.IniPath;
                // Sicherstellen, dass Verzeichnis existiert
                try
                {
                    var dir = Path.GetDirectoryName(ini);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                }
                catch { }

                IniHelper.WriteValue(IniSection, nameof(Enabled), Enabled ? "1" : "0", ini);
                IniHelper.WriteValue(IniSection, nameof(AskUser), AskUser ? "1" : "0", ini);
                IniHelper.WriteValue(IniSection, nameof(PrintVatLines), PrintVatLines ? "1" : "0", ini);
                IniHelper.WriteValue(IniSection, nameof(PrinterName), PrinterName ?? string.Empty, ini);
                IniHelper.WriteValue(IniSection, nameof(CharsPerLine), CharsPerLine.ToString(), ini);
                IniHelper.WriteValue(IniSection, nameof(CutAfterPrint), CutAfterPrint ? "1" : "0", ini);
                IniHelper.WriteValue(IniSection, nameof(AutoPrintIfNoPrompt), AutoPrintIfNoPrompt ? "1" : "0", ini);
                IniHelper.WriteValue(IniSection, nameof(SecondaryPrinterName), SecondaryPrinterName ?? string.Empty, ini);
                IniHelper.WriteValue(IniSection, nameof(UseSecondaryOnFailure), UseSecondaryOnFailure ? "1" : "0", ini);

                // Legacy Felder speichern
                IniHelper.WriteValue(IniSection, nameof(Legacy1Enabled), Legacy1Enabled ? "1" : "0", ini);
                IniHelper.WriteValue(IniSection, nameof(Legacy1ComPort), Legacy1ComPort ?? string.Empty, ini);
                IniHelper.WriteValue(IniSection, nameof(Legacy1Baud), Legacy1Baud.ToString(), ini);
                IniHelper.WriteValue(IniSection, nameof(Legacy1Chars), Legacy1Chars.ToString(), ini);
                IniHelper.WriteValue(IniSection, nameof(Legacy1Cut), Legacy1Cut ? "1" : "0", ini);
                IniHelper.WriteValue(IniSection, nameof(Legacy2Enabled), Legacy2Enabled ? "1" : "0", ini);
                IniHelper.WriteValue(IniSection, nameof(Legacy2ComPort), Legacy2ComPort ?? string.Empty, ini);
                IniHelper.WriteValue(IniSection, nameof(Legacy2Baud), Legacy2Baud.ToString(), ini);
                IniHelper.WriteValue(IniSection, nameof(Legacy2Chars), Legacy2Chars.ToString(), ini);
                IniHelper.WriteValue(IniSection, nameof(Legacy2Cut), Legacy2Cut ? "1" : "0", ini);
            }
            catch { }
        }

        private static bool ParseBool(string raw, bool def)
        {
            if (string.IsNullOrWhiteSpace(raw)) return def;
            raw = raw.Trim().ToLowerInvariant();
            if (raw == "1" || raw == "true" || raw == "yes" || raw == "ja" || raw == "on") return true;
            if (raw == "0" || raw == "false" || raw == "no" || raw == "nein" || raw == "off") return false;
            return def;
        }

        private static int ParseInt(string raw, int def, int min, int max)
        {
            if (int.TryParse(raw, out var v))
            {
                if (v < min) v = min; if (v > max) v = max;
                return v;
            }
            return def;
        }
    }
}
