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
            var cfg = ReceiptPrinterSettings.Load();

            // Automatenname
            try
            {
                var autoName = (AppSettings.AutomatenName ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(autoName)) list.Add("Automat: " + autoName);
            }
            catch { }

            DateTime? arbeitsBeginn = null;
            DateTime? arbeitsEnde = null;
            int? schichtId = null;
            int? pid = null;
            try
            {
                string mandant = null;
                string kenFromMeta = null;
                string schichtIdStr = null;
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
                                if (part.StartsWith("ID=", StringComparison.OrdinalIgnoreCase)) schichtIdStr = part.Substring(3).Trim();
                                if (part.StartsWith("PID=", StringComparison.OrdinalIgnoreCase)) { int v; if (int.TryParse(part.Substring(4).Trim(), out v)) pid = v; }
                                if (part.StartsWith("END=", StringComparison.OrdinalIgnoreCase))
                                {
                                    DateTime dt;
                                    if (DateTime.TryParse(part.Substring(4).Trim(), out dt)) arbeitsEnde = dt;
                                }
                            }
                        }
                    }
                    catch { }
                }
                if (int.TryParse(schichtIdStr, out var sid)) schichtId = sid;
                if (string.IsNullOrWhiteSpace(mandant)) mandant = kenFromMeta;
                if (string.IsNullOrWhiteSpace(mandant))
                {
                    try
                    {
                        var rx = new Regex(@"Schicht\s+(\d+),\s+(.+?)\s+vo[mn]\s+(\d{2}\.\d{2}\.\d{4}\s+\d{2}:\d{2})\s+eingezahlt", RegexOptions.IgnoreCase);
                        var m = rx.Match(buchungstext ?? string.Empty);
                        if (m.Success) { mandant = m.Groups[2].Value.Trim(); if (!schichtId.HasValue) schichtId = int.Parse(m.Groups[1].Value.Trim()); }
                    }
                    catch { }
                }

                try
                {
                    string display = null;
                    int manId = -1;
                    int sidTmp;
                    if (schichtId.HasValue)
                    {
                        using (var db = new Geldautomat.DatabaseHelper())
                        {
                            var det = db.GetShiftDetailsAsync(schichtId.Value).GetAwaiter().GetResult();
                            if (det != null)
                            {
                                arbeitsBeginn = det.StartZeit;
                                try
                                {
                                    var p = det.GetType().GetProperty("EndZeit");
                                    if (p != null)
                                    {
                                        var ev = p.GetValue(det);
                                        if (ev is DateTime dt && dt != DateTime.MinValue) arbeitsEnde = dt;
                                    }
                                }
                                catch { }
                                if (det.ManId >= 0) manId = det.ManId;
                                if (!pid.HasValue && det.PersId > 0) pid = det.PersId;
                            }
                            if (!arbeitsEnde.HasValue)
                            {
                                var end = db.GetShiftEndZeitAsync(schichtId.Value).GetAwaiter().GetResult();
                                if (end.HasValue) arbeitsEnde = end.Value;
                            }
                        }
                    }
                    if (manId >= 0)
                    {
                        using (var db = new Geldautomat.DatabaseHelper())
                        {
                            var dt = db.GetMandantenAsync(true).GetAwaiter().GetResult();
                            foreach (System.Data.DataRow r in dt.Rows)
                            {
                                try
                                {
                                    if (Convert.ToInt32(r["ManID"]) == manId) { var name = Convert.ToString(r["ManName"]); if (!string.IsNullOrWhiteSpace(name)) { display = name; break; } }
                                }
                                catch { }
                            }
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(display)) list.Add("Mandant: " + display);
                }
                catch { }

                if (!string.IsNullOrWhiteSpace(mitarbeiter))
                    list.Add("Mitarbeiter: " + mitarbeiter);

                bool isSchicht = IsSchichtTitle(title);

                if (isSchicht)
                {
                    if (!TryAddFromMetaHeader(buchungstext, list))
                    {
                        if (!TryAddFromFreeText(buchungstext, list))
                        {
                            if (!string.IsNullOrWhiteSpace(buchungstext)) list.Add(buchungstext.Trim());
                        }
                    }
                    try
                    {
                        if (!cfg.ShowWorkTimesOnly)
                        {
                            if (arbeitsBeginn.HasValue)
                                list.Add("Arbeitsbeginn: " + arbeitsBeginn.Value.ToString("HH:mm", De));
                            if (arbeitsEnde.HasValue)
                                list.Add("Arbeitsende : " + arbeitsEnde.Value.ToString("HH:mm", De));
                        }

                        TimeSpan diff = TimeSpan.Zero;
                        if (arbeitsBeginn.HasValue && arbeitsEnde.HasValue)
                        {
                            var start = arbeitsBeginn.Value;
                            var end = arbeitsEnde.Value;
                            if (end < start) end = end.AddDays(1);
                            diff = end - start;
                            if (diff.TotalMinutes < 0) diff = TimeSpan.Zero;
                        }

                        // tatsächliche Pause aus THistoryZeiterfassung innerhalb der Schicht
                        TimeSpan actualPause = TimeSpan.Zero;
                        try
                        {
                            if (pid.HasValue && arbeitsBeginn.HasValue && arbeitsEnde.HasValue)
                            {
                                using (var db = new Geldautomat.DatabaseHelper())
                                {
                                    var dtPause = db.GetZeiterfassungAsync(pid.Value, arbeitsBeginn.Value.Date, arbeitsEnde.Value.Date.AddDays(1)).GetAwaiter().GetResult();
                                    foreach (System.Data.DataRow r in dtPause.Rows)
                                    {
                                        try
                                        {
                                            int typ = r.Table.Columns.Contains("Typ") && r["Typ"] != DBNull.Value ? Convert.ToInt32(r["Typ"]) : 0;
                                            if (typ == 106) // Pause
                                            {
                                                DateTime zv = r["ZeitVon"] != DBNull.Value ? (DateTime)r["ZeitVon"] : DateTime.MinValue;
                                                DateTime zb = r["ZeitBis"] != DBNull.Value ? (DateTime)r["ZeitBis"] : DateTime.MinValue;
                                                if (zv == DateTime.MinValue || zb == DateTime.MinValue) continue;
                                                // nur Pausen innerhalb der Schichtzeit berücksichtigen
                                                if (zv >= arbeitsBeginn.Value && zv <= arbeitsEnde.Value)
                                                {
                                                    var dur = zb - zv;
                                                    if (dur.TotalMinutes >= 15) actualPause += dur;
                                                }
                                            }
                                        }
                                        catch { }
                                    }
                                }
                            }
                        }
                        catch { }

                        // Standard-Pausenregel
                        TimeSpan standardPause = TimeSpan.Zero;
                        if (diff.TotalMinutes > 0)
                        {
                            if (diff.TotalHours > 9) standardPause = TimeSpan.FromMinutes(45);
                            else if (diff.TotalHours > 6) standardPause = TimeSpan.FromMinutes(30);
                        }

                        // ausgewählte Pause abhängig von Einstellungen
                        TimeSpan usedPause = TimeSpan.Zero;
                        if (cfg.AutoPauseDeduction)
                        {
                            usedPause = standardPause;
                            if (cfg.MinimumPauseDeduction && actualPause > standardPause)
                                usedPause = actualPause;
                        }
                        else
                        {
                            usedPause = actualPause; // kein Autoabzug, nur tatsächlich erfasste Pausen abziehen
                        }

                        if (cfg.ShowPauseTime && diff.TotalMinutes > 0)
                        {
                            int ph = (int)usedPause.TotalHours; int pm = usedPause.Minutes;
                            list.Add("Pausenzeit  : " + string.Format("{0}:{1:00} h", ph, pm));
                        }

                        if (cfg.ShowArbeitszeit && diff.TotalMinutes > 0)
                        {
                            var netto = diff - usedPause;
                            if (netto.TotalMinutes < 0) netto = TimeSpan.Zero;
                            int hours = (int)netto.TotalHours;
                            int minutes = netto.Minutes;
                            list.Add("Arbeitszeit : " + string.Format("{0}:{1:00} h", hours, minutes));
                        }
                    }
                    catch { }
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(buchungstext))
                        list.Add("Text: " + buchungstext.Trim());
                }
            }
            catch { }

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
            const int labelWidth = 6;
            const int valueColumn = 28;
            string label = (mwst + "%:").PadLeft(labelWidth);
            string amount = betrag.ToString("0.00", De);
            string value = amount + " �";
            int spaces = Math.Max(1, valueColumn - label.Length - value.Length);
            return label + new string(' ', spaces) + value;
        }

        private string FormatSumLine(string label, decimal betrag)
        {
            const int valueColumn = 28;
            string left = label;
            string amount = betrag.ToString("0.00", De);
            string value = amount + " �";
            int spaces = Math.Max(1, valueColumn - left.Length - value.Length);
            return left + new string(' ', spaces) + value;
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

                string datumOnly = dat;
                try { var parts = dat.Split(' '); if (parts.Length > 0) datumOnly = parts[0]; } catch { }

                var line1 = "Schicht: " + (string.IsNullOrWhiteSpace(id) ? "unbekannt" : id) + (string.IsNullOrWhiteSpace(ken) ? "" : ", " + ken);
                var line2 = "vom " + datumOnly + " eingezahlt";
                list.Add(line1);
                list.Add(line2);
                return true;
            }
            catch { return false; }
        }

        private bool TryAddFromFreeText(string text, List<string> list)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            try
            {
                var rx = new Regex(@"Schicht\s+(\d+),\s+(.+?)\s+vo[mn]\s+(\d{2}\.\d{2}\.\d{4}\s+\d{2}:\d{2})\s+eingezahlt", RegexOptions.IgnoreCase);
                var m = rx.Match(text);
                if (m.Success)
                {
                    string id = m.Groups[1].Value.Trim();
                    string ken = m.Groups[2].Value.Trim();
                    string dat = m.Groups[3].Value.Trim();
                    string datumOnly = dat; try { var parts = dat.Split(' '); if (parts.Length > 0) datumOnly = parts[0]; } catch { }
                    list.Add("Schicht: " + id + ", " + ken);
                    list.Add("vom " + datumOnly + " eingezahlt");
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
            return title.Contains("SCHICHT") || title.Contains("NACHZAHLUNG") || title.Contains("R�CKZAHLUNG") || title.Contains("RUECKZAHLUNG");
        }
    }

    public static class ReceiptLayouts
    {
        public static IReceiptLayout Current { get; set; } = new StandardReceiptLayout();
    }
}
