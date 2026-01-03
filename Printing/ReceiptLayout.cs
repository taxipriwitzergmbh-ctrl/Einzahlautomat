using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Geldautomat.Printing
{
    public interface IReceiptLayout
    {
        IEnumerable<string> BuildBody(string title, string mitarbeiter, string buchungstext,
                                      decimal betrag19, decimal betrag7, decimal betrag0, bool withVatLines);
    }

    public class StandardReceiptLayout : IReceiptLayout
    {
        private static readonly CultureInfo De = new CultureInfo("de-DE");

        public IEnumerable<string> BuildBody(string title, string mitarbeiter, string buchungstext,
                                             decimal betrag19, decimal betrag7, decimal betrag0, bool withVatLines)
        {
            var list = new List<string>();

            // Automatenname
            try
            {
                var autoName = (AppSettings.AutomatenName ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(autoName)) list.Add("Automat: " + autoName);
            }
            catch { }

            // Mandantenname aus Meta oder freiem Text
            try
            {
                string mandant = null;
                string kenFromMeta = null;
                if (!string.IsNullOrWhiteSpace(buchungstext) && buchungstext.StartsWith("::SCHMETA|", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        int end = buchungstext.IndexOf("::", 2);
                        if (end > 0)
                        {
                            var header = buchungstext.Substring(2, end - 2);
                            foreach (var part in header.Split('|'))
                            {
                                if (part.StartsWith("MAN=", StringComparison.OrdinalIgnoreCase)) mandant = part.Substring(4).Trim();
                                if (part.StartsWith("KEN=", StringComparison.OrdinalIgnoreCase)) kenFromMeta = part.Substring(4).Trim();
                            }
                        }
                    }
                    catch { }
                }
                if (string.IsNullOrWhiteSpace(mandant)) mandant = kenFromMeta; // Fallback auf Kennzeichen aus Meta
                if (string.IsNullOrWhiteSpace(mandant))
                {
                    // Letzter Fallback: aus freiem Text den Kennzeichen-Teil extrahieren
                    try
                    {
                        var rx = new Regex(@"Schicht\s+(\d+),\s+(.+?)\s+vo[mn]\s+(\d{2}\.\d{2}\.\d{4}\s+\d{2}:\d{2})\s+eingezahlt", RegexOptions.IgnoreCase);
                        var m = rx.Match(buchungstext ?? string.Empty);
                        if (m.Success) mandant = m.Groups[2].Value.Trim();
                    }
                    catch { }
                }

                // Wenn MAN= numerisch ist, auf TMandanten.ManName auflösen
                try
                {
                    string display = mandant;
                    int manId;
                    if (!string.IsNullOrWhiteSpace(mandant) && int.TryParse(mandant, out manId))
                    {
                        // ID -> Name aus TMandanten
                        using (var db = new Geldautomat.DatabaseHelper())
                        {
                            var dt = db.GetMandantenAsync(true).GetAwaiter().GetResult();
                            foreach (System.Data.DataRow r in dt.Rows)
                            {
                                try
                                {
                                    if (Convert.ToInt32(r["ManID"]) == manId)
                                    {
                                        var name = Convert.ToString(r["ManName"]);
                                        if (!string.IsNullOrWhiteSpace(name)) display = name;
                                        break;
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(display)) list.Add("Mandant: " + display);
                }
                catch { if (!string.IsNullOrWhiteSpace(mandant)) list.Add("Mandant: " + mandant); }
            }
            catch { }

            if (!string.IsNullOrWhiteSpace(mitarbeiter))
                list.Add("Mitarbeiter: " + mitarbeiter);

            bool isSchicht = IsSchichtTitle(title);

            if (isSchicht)
            {
                // Versuche strukturierten Meta-Header (::SCHMETA|ID=...|DAT=...|KEN=...::)
                if (!TryAddFromMetaHeader(buchungstext, list))
                {
                    // Regex-Fallback aus freiem Text
                    if (!TryAddFromFreeText(buchungstext, list))
                    {
                        // Letzter Fallback: falls nichts erkannt – zeige den Text roh
                        if (!string.IsNullOrWhiteSpace(buchungstext))
                            list.Add(buchungstext.Trim());
                    }
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(buchungstext))
                    list.Add("Text: " + buchungstext.Trim());
            }

            if (withVatLines)
            {
                list.Add(FormatVatLine("19", betrag19));
                list.Add(FormatVatLine("7", betrag7));
                list.Add(FormatVatLine("0", betrag0));
            }

            list.Add(FormatSumLine("Summe:", betrag19 + betrag7 + betrag0));
            return list;
        }

        private string FormatVatLine(string mwst, decimal betrag)
        {
            const int amountColumn = 16;
            string label = (mwst.Length == 1 ? "  " : "") + mwst + "%:";
            string value = betrag.ToString("0.00", De) + "€";
            int spaces = Math.Max(1, amountColumn - label.Length);
            return label + new string(' ', spaces) + value;
        }

        private string FormatSumLine(string label, decimal betrag)
        {
            const int spaces = 5;
            string value = betrag.ToString("0.00", De) + "€";
            return label + new string(' ', spaces) + value;
        }

        private bool TryAddFromMetaHeader(string text, List<string> list)
        {
            if (string.IsNullOrWhiteSpace(text) || !text.StartsWith("::SCHMETA|", StringComparison.OrdinalIgnoreCase))
                return false;

            try
            {
                int end = text.IndexOf("::", 2);
                if (end <= 0) return false;
                var header = text.Substring(2, end - 2);

                string id = null;
                string dat = null;
                string ken = null;

                foreach (var part in header.Split('|'))
                {
                    if (part.StartsWith("ID=", StringComparison.OrdinalIgnoreCase))
                        id = part.Substring(3).Trim();
                    else if (part.StartsWith("DAT=", StringComparison.OrdinalIgnoreCase))
                        dat = part.Substring(4).Trim();
                    else if (part.StartsWith("KEN=", StringComparison.OrdinalIgnoreCase))
                        ken = part.Substring(4).Trim();
                }

                if (string.IsNullOrWhiteSpace(dat))
                    return false;

                var line1 = "Schicht: " + (string.IsNullOrWhiteSpace(id) ? "unbekannt" : id) +
                            (string.IsNullOrWhiteSpace(ken) ? "" : ", " + ken);
                var line2 = "vom " + dat + " eingezahlt";
                list.Add(line1);
                list.Add(line2);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryAddFromFreeText(string text, List<string> list)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            try
            {
                var rx = new Regex(@"Schicht\s+(\d+),\s+(.+?)\s+vo[mn]\s+(\d{2}\.\d{2}\.\d{4}\s+\d{2}:\d{2})\s+eingezahlt",
                    RegexOptions.IgnoreCase);
                var m = rx.Match(text);
                if (m.Success)
                {
                    string id = m.Groups[1].Value.Trim();
                    string ken = m.Groups[2].Value.Trim();
                    string dat = m.Groups[3].Value.Trim();

                    list.Add("Schicht: " + id + ", " + ken);
                    list.Add("vom " + dat + " eingezahlt");
                    return true;
                }
            }
            catch { }
            return false;
        }

        private bool IsSchichtTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return false;
            title = title.Trim().ToUpperInvariant();
            return title.Contains("SCHICHT") || title.Contains("NACHZAHLUNG") || title.Contains("RÜCKZAHLUNG") || title.Contains("RUECKZAHLUNG");
        }
    }

    public static class ReceiptLayouts
    {
        public static IReceiptLayout Current { get; set; } = new StandardReceiptLayout();
    }
}
