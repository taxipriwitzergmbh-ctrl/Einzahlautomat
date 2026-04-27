using System;
using System.Collections.Generic;
using System.Linq;
using TaMi_Einzahlautomat.Abrechnung; // DB Regeln lokal laden statt aus externer Assembly

namespace TaMi_Einzahlautomat
{
    public class Kontierung
    {
        public int Kost1 { get; set; }
        public int Kost2 { get; set; }
        public int Konto { get; set; }
    }

    // tKontext für Regelbewertung (kann bei Bedarf erweitert werden)
    public class AccountingRuleContext
    {
        public int MandantId { get; set; }
        public int? PersId { get; set; }
        public int? FhzId { get; set; }
        public string Typ { get; set; } // z.B. "Schichtabrechnung", "Personalguthaben"
        public DateTime? StartZeit { get; set; }
        public decimal Betrag19 { get; set; }
        public decimal Betrag7 { get; set; }
        public decimal Betrag0 { get; set; }
        public string Bemerkung { get; set; }
        public PersonalInfo Personal { get; set; }
        public ShiftDetails Details { get; set; }
        public string Kennzeichen { get; set; }
    }

    public interface IAccountingRuleProvider
    {
        Kontierung GetKontierungFor(string mwstGroup, AccountingRuleContext ctx, Kontierung defaultKontierung);
        string GetBuchungstext(string key, AccountingRuleContext ctx, string defaultText);
    }

    internal sealed class DefaultIniAccountingRuleProvider : IAccountingRuleProvider
    {
        public Kontierung GetKontierungFor(string mwstGroup, AccountingRuleContext ctx, Kontierung defaultKontierung) => defaultKontierung;
        public string GetBuchungstext(string key, AccountingRuleContext ctx, string defaultText) => defaultText;
    }

    internal sealed class DbRulesAccountingRuleProvider : IAccountingRuleProvider
    {
        private readonly object _lock = new object();
        private List<AbrechnungsRegel> _rules = new List<AbrechnungsRegel>();
        private DateTime _lastLoadUtc = DateTime.MinValue;
        private readonly TimeSpan _reloadInterval = TimeSpan.FromMinutes(5);
        private bool _loadErrorLogged = false;
        

        private void EnsureLoaded()
        {
            lock (_lock)
            {
                if (_rules.Count == 0 || (DateTime.UtcNow - _lastLoadUtc) > _reloadInterval)
                {
                    try
                    {
                        _rules = System.Threading.Tasks.Task.Run(() => RulesEngine.LoadRulesAsync()).GetAwaiter().GetResult() ?? new List<AbrechnungsRegel>();
                        // Feld-Namens-Aliase angleichen: ManId / MandantId -> FirmenId
                        foreach (var r in _rules)
                        {
                            if (r?.Clauses == null) continue;
                            foreach (var c in r.Clauses)
                            {
                                try
                                {
                                    if (c == null || string.IsNullOrWhiteSpace(c.Field)) continue;
                                    var f = c.Field.Trim();
                                    if (f.Equals("ManID", StringComparison.OrdinalIgnoreCase) || f.Equals("MandantId", StringComparison.OrdinalIgnoreCase))
                                        c.Field = "FirmenId";
                                }
                                catch { }
                            }
                        }

                        // NEU: Multi-Fahrzeuglisten (z.B. "21;50;51") in Einzelregeln expandieren
                        try { ExpandMultiValueFhzClauses(); } catch { }

                        // Hinweis: Altes Feld RawResultsJson existiert nicht mehr – keine zusätzliche Mappings nötig

                        // Nach allen Anpassungen: Regeln nach Priorität sortieren
                        try
                        {
                            // Höhere Priorität zuerst auswerten (first match wins)
                            _rules = _rules
                                .OrderByDescending(r => r?.Priority ?? 0)
                                .ThenByDescending(r => r?.Id ?? 0)
                                .ToList();
                        }
                        catch { }

                        // Erfolgreiches Laden stempeln, damit kein sofortiger Reload erfolgt
                        _lastLoadUtc = DateTime.UtcNow;
                    }
                    catch (Exception ex)
                    {
                        if (!_loadErrorLogged)
                        {
                            try { AppLogger.Log("[Rules] Laden fehlgeschlagen: " + ex.Message); } catch { }
                            _loadErrorLogged = true; // nur einmal loggen
                        }
                        _rules = new List<AbrechnungsRegel>();
                        _lastLoadUtc = DateTime.UtcNow;
                    }
                }
            }
        }

        // Aufteilung Regel mit FhzId = "21;50;51" in mehrere Regeln (OR Semantik)
        private void ExpandMultiValueFhzClauses()
        {
            if (_rules == null || _rules.Count == 0) return;
            var separators = new[] { ';', ',', '|', ' ' };
            var expanded = new List<AbrechnungsRegel>();

            foreach (var r in _rules)
            {
                try
                {
                    var fhzClause = r?.Clauses?.FirstOrDefault(c => c != null && !string.IsNullOrWhiteSpace(c.Field) && c.Field.Equals("FhzId", StringComparison.OrdinalIgnoreCase));
                    if (fhzClause == null || string.IsNullOrWhiteSpace(fhzClause.Value))
                    {
                        expanded.Add(r);
                        continue;
                    }

                    var raw = fhzClause.Value.Trim();
                    if (raw.IndexOfAny(separators) < 0)
                    {
                        // Einzelwert
                        expanded.Add(r);
                        continue;
                    }

                    var parts = raw
                        .Split(separators, StringSplitOptions.RemoveEmptyEntries)
                        .Select(p => p.Trim())
                        .Where(p => p.Length > 0)
                        .Distinct()
                        .ToList();

                    if (parts.Count <= 1)
                    {
                        fhzClause.Value = parts.FirstOrDefault() ?? raw;
                        expanded.Add(r);
                        continue;
                    }

                    foreach (var p in parts)
                    {
                        var clone = r.Clone();
                        var c2 = clone.Clauses.First(c => c.Field.Equals("FhzId", StringComparison.OrdinalIgnoreCase));
                        c2.Value = p;
                        expanded.Add(clone);
                    }
                }

                catch
                {
                    // Sicherheitsnetz – originale Regel behalten
                    expanded.Add(r);
                }
            }

            _rules = expanded;
        }

        private static RulesEngine.RuleContext Map(AccountingRuleContext ctx, string mwstGroup)
        {
            var r = new RulesEngine.RuleContext
            {
                FirmenId = ctx?.MandantId ?? 0,
                PersId = ctx?.PersId,
                FhzId = ctx?.FhzId,
                Typ = ctx?.Typ ?? "Schichtabrechnung",
                Betrag19 = 0m,
                Betrag7 = 0m,
                Betrag0 = 0m
            };
            switch ((mwstGroup ?? string.Empty).Trim())
            {
                case "19":
                    var b19 = ctx?.Betrag19 ?? 0m; r.Betrag19 = b19 > 0m ? b19 : 0.01m; break;
                case "7":
                    var b7 = ctx?.Betrag7 ?? 0m; r.Betrag7 = b7 > 0m ? b7 : 0.01m; break;
                case "0":
                    var b0 = ctx?.Betrag0 ?? 0m; r.Betrag0 = b0 > 0m ? b0 : 0.01m; break;
                case "PG":
                    break;
            }
            return r;
        }

        public Kontierung GetKontierungFor(string mwstGroup, AccountingRuleContext ctx, Kontierung @default)
        {
            EnsureLoaded();
            var rctx = Map(ctx, mwstGroup);
            int? k1 = null, k2 = null, kto = null; string txt = null;
            try
            {
                RulesEngine.ApplyForEdit(_rules, rctx, ref k1, ref k2, ref kto, ref txt);
            }
            catch (Exception ex)
            {
                try { AppLogger.Log("[Rules] ApplyForEdit Fehler (Var1): " + ex.Message); } catch { }
            }

            // Zweiter Versuch mit Overload (falls nichts gesetzt)
            if (k1 == null && k2 == null && kto == null)
            {
                try
                {
                    RulesEngine.ApplyForEdit(_rules, rctx, rctx.Betrag19, rctx.Betrag7, rctx.Betrag0, ref k1, ref k2, ref kto, ref txt);
                    if (k1 != null || k2 != null || kto != null)
                    {
                        try { AppLogger.Log($"[Rules][Retry] Gruppe={mwstGroup} Treffer nach Overload: k1={k1},k2={k2},kto={kto}"); } catch { }
                    }
                }
                catch (Exception ex2)
                {
                    try { AppLogger.Log("[Rules] ApplyForEdit Fehler (Var2): " + ex2.Message); } catch { }
                }
            }

            var result = new Kontierung
            {
                Kost1 = k1 ?? @default?.Kost1 ?? 0,
                Kost2 = k2 ?? @default?.Kost2 ?? 0,
                Konto = kto ?? @default?.Konto ?? 0
            };

            try
            {
                // Betrag für Logzwecke ermitteln (je nach MwSt.-Gruppe)
                decimal betrag = 0m;
                var grp = (mwstGroup ?? string.Empty).Trim();
                switch (grp)
                {
                    case "19": betrag = ctx?.Betrag19 ?? 0m; break;
                    case "7":  betrag = ctx?.Betrag7  ?? 0m; break;
                    case "0":  betrag = ctx?.Betrag0  ?? 0m; break;
                    case "PG":
                        // Bei PG gibt es oft keine Einzelbetragsfelder im Kontext – Summe aller bekannten Beträge nutzen (falls gesetzt)
                        betrag = (ctx?.Betrag19 ?? 0m) + (ctx?.Betrag7 ?? 0m) + (ctx?.Betrag0 ?? 0m);
                        break;
                    default:
                        betrag = (ctx?.Betrag19 ?? 0m) + (ctx?.Betrag7 ?? 0m) + (ctx?.Betrag0 ?? 0m);
                        break;
                }

                var de = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
                string betragStr = betrag.ToString("0.00", de) + " €";

                // Ergebnis-Logging unterdrücken für Personalguthaben (PG)
                if (!string.Equals(mwstGroup, "PG", StringComparison.OrdinalIgnoreCase))
                {
                    AppLogger.Log($"[Rules][Result] Gruppe={mwstGroup} FirmenId={rctx.FirmenId} PersId={rctx.PersId} FhzId={rctx.FhzId} -> k1={result.Kost1},k2={result.Kost2},kto={result.Konto} (Default: {(@default?.Kost1 ?? 0)}/{(@default?.Kost2 ?? 0)}/{(@default?.Konto ?? 0)}) Betrag={betragStr}");
                }
            }
            catch { }

            if (result.Kost1 == 0 && result.Kost2 == 0 && result.Konto == 0)
            {
                try
                {
                    string explain = _rules.Count > 0 ? Explain(mwstGroup, ctx) : "keine Regeln geladen";
                    AppLogger.Log($"[Rules][Diag] Gruppe={mwstGroup} -> alle 0. Explain:\n{explain}");
                }
                catch { }
            }

            return result;
        }

        public string GetBuchungstext(string key, AccountingRuleContext ctx, string defaultText)
        {
            EnsureLoaded();
            var rctx = new RulesEngine.RuleContext
            {
                FirmenId = ctx?.MandantId ?? 0,
                PersId = ctx?.PersId,
                FhzId = ctx?.FhzId,
                Typ = ctx?.Typ ?? "Schichtabrechnung",
                Betrag19 = ctx?.Betrag19 ?? 0m,
                Betrag7 = ctx?.Betrag7 ?? 0m,
                Betrag0 = ctx?.Betrag0 ?? 0m
            };
            int? k1 = null, k2 = null, kto = null; string txt = null;
            try { RulesEngine.ApplyForEdit(_rules, rctx, ref k1, ref k2, ref kto, ref txt); } catch { }
            return string.IsNullOrWhiteSpace(txt) ? defaultText : txt;
        }

        internal string Explain(string mwstGroup, AccountingRuleContext ctx)
        {
            EnsureLoaded();
            try
            {
                var mapped = Map(ctx, mwstGroup); // preserves existing matching behaviour
                var raw = RulesEngine.ExplainRuleMatching(_rules ?? new List<AbrechnungsRegel>(), mapped);
                // Minimal-invasiv: Wenn Originalbetrag negativ war, ersetze die durch Map() auf 0,01 gesetzte Anzeige
                // nur im Log (raw) – Logic (mapped) bleibt unverändert.
                try
                {
                    if (ctx != null && !string.IsNullOrWhiteSpace(raw))
                    {
                        var de = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
                        if (string.Equals(mwstGroup, "19") && ctx.Betrag19 < 0)
                        {
                            string repl = ctx.Betrag19.ToString("0.00", de);
                            raw = raw.Replace("B19=0,01", "B19=" + repl).Replace("B19=0.01", "B19=" + repl);
                        }
                        else if (string.Equals(mwstGroup, "7") && ctx.Betrag7 < 0)
                        {
                            string repl = ctx.Betrag7.ToString("0.00", de);
                            raw = raw.Replace("B7=0,01", "B7=" + repl).Replace("B7=0.01", "B7=" + repl);
                        }
                        else if (string.Equals(mwstGroup, "0") && ctx.Betrag0 < 0)
                        {
                            string repl = ctx.Betrag0.ToString("0.00", de);
                            raw = raw.Replace("B0=0,01", "B0=" + repl).Replace("B0=0.01", "B0=" + repl);
                        }
                    }
                }
                catch { }
                return FilterExplainOutput(raw);
            }
            catch { return "no explain"; }
        }

        // NEU: entfernt Zeilen mit ') no match '
        private static string FilterExplainOutput(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;
            try
            {
                var sep = new[] { "\r\n", "\n" };
                var lines = raw.Split(sep, StringSplitOptions.None);
                // Nur erste nicht-leere Zeile (Context) behalten
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    return line.TrimEnd();
                }
                return string.Empty;
            }
            catch { return raw; }
        }
    }

    public static class AccountingRules
    {
        private static IAccountingRuleProvider _provider = new DbRulesAccountingRuleProvider();
        public static void SetProvider(IAccountingRuleProvider provider) => _provider = provider ?? new DefaultIniAccountingRuleProvider();

        private const string SectionPG = "Standart Konten Personalguthaben";
        private const string Section0 = "Standart Konten 0%";
        private const string Section7 = "Standart Konten 7%";
        private const string Section19 = "Standart Konten 19%";

        private static AccountingRuleContext BuildSchichtCtx(int mandantId, int? persId, int? fhzId, decimal b19, decimal b7, decimal b0) => new AccountingRuleContext { MandantId = mandantId, PersId = persId, FhzId = fhzId, Typ = "Schichtabrechnung", Betrag19 = b19, Betrag7 = b7, Betrag0 = b0 };

        public static Kontierung GetPersonalguthabenKontierung(int mandantId)
        {
            try
            {
                var z = IniHelper.GetKontoZuordnung(SectionPG, mandantId, AppSettings.IniPath);
                var @default = new Kontierung { Kost1 = z.Kost1, Kost2 = z.Kost2, Konto = z.Konto };
                var ctx = new AccountingRuleContext { MandantId = mandantId, Typ = "Personalguthaben" };
                return _provider.GetKontierungFor("PG", ctx, @default) ?? @default;
            }
            catch { return new Kontierung(); }
        }

        public static Kontierung GetKontierung19(int mandantId)
        { try { var z = IniHelper.GetKontoZuordnung(Section19, mandantId, AppSettings.IniPath); var d = new Kontierung { Kost1 = z.Kost1, Kost2 = z.Kost2, Konto = z.Konto }; var ctx = new AccountingRuleContext { MandantId = mandantId, Typ = "Schichtabrechnung" }; return _provider.GetKontierungFor("19", ctx, d) ?? d; } catch { return new Kontierung(); } }
        public static Kontierung GetKontierung7(int mandantId)
        { try { var z = IniHelper.GetKontoZuordnung(Section7, mandantId, AppSettings.IniPath); var d = new Kontierung { Kost1 = z.Kost1, Kost2 = z.Kost2, Konto = z.Konto }; var ctx = new AccountingRuleContext { MandantId = mandantId, Typ = "Schichtabrechnung" }; return _provider.GetKontierungFor("7", ctx, d) ?? d; } catch { return new Kontierung(); } }
        public static Kontierung GetKontierung0(int mandantId)
        { try { var z = IniHelper.GetKontoZuordnung(Section0, mandantId, AppSettings.IniPath); var d = new Kontierung { Kost1 = z.Kost1, Kost2 = z.Kost2, Konto = z.Konto }; var ctx = new AccountingRuleContext { MandantId = mandantId, Typ = "Schichtabrechnung" }; return _provider.GetKontierungFor("0", ctx, d) ?? d; } catch { return new Kontierung(); } }

        public static Kontierung GetKontierung19(int mandantId, int? persId, int? fhzId, decimal betrag19)
        { try { var z = IniHelper.GetKontoZuordnung(Section19, mandantId, AppSettings.IniPath); var d = new Kontierung { Kost1 = z.Kost1, Kost2 = z.Kost2, Konto = z.Konto }; var ctx = BuildSchichtCtx(mandantId, persId, fhzId, betrag19, 0m, 0m); return _provider.GetKontierungFor("19", ctx, d) ?? d; } catch { return new Kontierung(); } }
        public static Kontierung GetKontierung7(int mandantId, int? persId, int? fhzId, decimal betrag7)
        { try { var z = IniHelper.GetKontoZuordnung(Section7, mandantId, AppSettings.IniPath); var d = new Kontierung { Kost1 = z.Kost1, Kost2 = z.Kost2, Konto = z.Konto }; var ctx = BuildSchichtCtx(mandantId, persId, fhzId, 0m, betrag7, 0m); return _provider.GetKontierungFor("7", ctx, d) ?? d; } catch { return new Kontierung(); } }
        public static Kontierung GetKontierung0(int mandantId, int? persId, int? fhzId, decimal betrag0)
        { try { var z = IniHelper.GetKontoZuordnung(Section0, mandantId, AppSettings.IniPath); var d = new Kontierung { Kost1 = z.Kost1, Kost2 = z.Kost2, Konto = z.Konto }; var ctx = BuildSchichtCtx(mandantId, persId, fhzId, 0m, 0m, betrag0); return _provider.GetKontierungFor("0", ctx, d) ?? d; } catch { return new Kontierung(); } }

        public static Kontierung GetKontierung19(ShiftDetails details) => details == null ? new Kontierung() : GetKontierung19(details.ManId, details.PersId, details.FhzId, details.Betrag19);
        public static Kontierung GetKontierung7(ShiftDetails details) => details == null ? new Kontierung() : GetKontierung7(details.ManId, details.PersId, details.FhzId, details.Betrag7);
        public static Kontierung GetKontierung0(ShiftDetails details) => details == null ? new Kontierung() : GetKontierung0(details.ManId, details.PersId, details.FhzId, details.Betrag0);

        public static string ComposeSchichtBuchungstext(PersonalInfo personal, ShiftDetails details, string kennzeichen)
        {
            if (personal == null || details == null) return string.Empty;
            string datum = details.StartZeit.ToString("dd.MM.yyyy HH:mm");
            string kennTeil = string.IsNullOrWhiteSpace(kennzeichen) ? string.Empty : ", " + kennzeichen;
            var def = $"{personal.Vorname} {personal.Name} Schicht {details.SchichtId}{kennTeil} von {datum} eingezahlt";
            var ctx = new AccountingRuleContext { MandantId = details.ManId, PersId = details.PersId, FhzId = details.FhzId, Typ = "Schichtabrechnung", StartZeit = details.StartZeit, Betrag19 = details.Betrag19, Betrag7 = details.Betrag7, Betrag0 = details.Betrag0, Personal = personal, Details = details, Kennzeichen = kennzeichen };
            return _provider.GetBuchungstext("Schicht", ctx, def) ?? def;
        }

        public static string ComposePgEinzahlungText(PersonalInfo personal)
        { var def = personal == null ? "Personalguthaben-Ausgleich Einzahlung" : $"Personalguthaben-Ausgleich Einzahlung ({personal.Vorname} {personal.Name})"; var ctx = new AccountingRuleContext { MandantId = 0, Typ = "Personalguthaben", Personal = personal }; return _provider.GetBuchungstext("PG_Einzahlung", ctx, def) ?? def; }
        public static string ComposePgAuszahlungText(PersonalInfo personal)
        { var def = personal == null ? "Personalguthaben-Auszahlung" : $"Personalguthaben-Ausgleich Auszahlung ({personal.Vorname} {personal.Name})"; var ctx = new AccountingRuleContext { MandantId = 0, Typ = "Personalguthaben", Personal = personal }; return _provider.GetBuchungstext("PG_Auszahlung", ctx, def) ?? def; }
        public static string ComposePgEinbuchungText(PersonalInfo personal)
        { var def = personal == null ? "Personalguthaben eingebucht" : $"Personalguthaben eingebucht von {personal.Vorname} {personal.Name}"; var ctx = new AccountingRuleContext { MandantId = 0, Typ = "Personalguthaben", Personal = personal }; return _provider.GetBuchungstext("PG_Einbuchung", ctx, def) ?? def; }

        public static string ExplainKontierung(string mwstGroup, ShiftDetails details)
        {
            try { var db = _provider as DbRulesAccountingRuleProvider; if (db == null || details == null) return "no db-provider"; var ctx = BuildSchichtCtx(details.ManId, details.PersId, details.FhzId, details.Betrag19, details.Betrag7, details.Betrag0); return db.Explain(mwstGroup, ctx); } catch { return "explain failed"; }
        }

        // Hilfsfunktion: Regel-Typ-String in Regelcode normalisieren (z. B. "Trinkgeld" -> "6")
        private static string NormalizeTypForRules(string typ)
        {
            if (string.IsNullOrWhiteSpace(typ)) return null;
            var t = typ.Trim();
            // numerische Werte direkt verwenden
            byte num;
            if (byte.TryParse(t, out num)) return num.ToString();
            // bekannte Aliasnamen abbilden
            switch (t.ToLowerInvariant())
            {
                case "anfangsbestand": return "1";
                case "einzahlung": return "2";
                case "auszahlung": return "3";
                case "schichtabrechnung": return "4";
                case "personalguthaben": return "5";
                case "trinkgeld": return "6";
                default: return t; // als freier String belassen
            }
        }

        // Allgemeiner Zugriff für fremde Typen (z. B. "Trinkgeld")
        // Nutzt dieselben INI-Defaults wie für Schichtabrechnungen, setzt aber den Kontext-Typ entsprechend.
        public static Kontierung GetKontierungForTyp(string mwstGroup, int mandantId, int? persId, int? fhzId, decimal betrag, string typ)
        {
            try
            {
                string PickSection(string grp)
                {
                    switch ((grp ?? string.Empty).Trim())
                    {
                        case "19": return Section19;
                        case "7": return Section7;
                        case "0": return Section0;
                        default: return Section0;
                    }
                }

                string grp0 = (mwstGroup ?? "0").Trim();
                string section = PickSection(grp0);

                decimal b19 = 0m, b7 = 0m, b0 = 0m;
                switch (grp0)
                {
                    case "19": b19 = betrag; break;
                    case "7":  b7  = betrag; break;
                    case "0":  b0  = betrag; break;
                    default:     b0  = betrag; break;
                }

                var z = IniHelper.GetKontoZuordnung(section, mandantId, AppSettings.IniPath);
                var @default = new Kontierung { Kost1 = z.Kost1, Kost2 = z.Kost2, Konto = z.Konto };

                var ctx = new AccountingRuleContext
                {
                    MandantId = mandantId,
                    PersId = persId,
                    FhzId = fhzId,
                    Typ = NormalizeTypForRules(typ) ?? "Schichtabrechnung",
                    Betrag19 = b19,
                    Betrag7 = b7,
                    Betrag0 = b0
                };

                var res = _provider.GetKontierungFor(grp0, ctx, @default) ?? @default;

                // Fallback über andere Gruppen, falls nichts (bzw. nur Default) gefunden wurde
                if (res.Kost1 == @default.Kost1 && res.Kost2 == @default.Kost2 && res.Konto == @default.Konto)
                {
                    // Probier-Reihenfolge: wenn 0 -> 7 -> 19, wenn 7 -> 0 -> 19, wenn 19 -> 7 -> 0
                    var order = new List<string>();
                    if (grp0 == "0") order.AddRange(new[] { "7", "19" });
                    else if (grp0 == "7") order.AddRange(new[] { "0", "19" });
                    else order.AddRange(new[] { "7", "0" });

                    foreach (var alt in order)
                    {
                        try
                        {
                            string sectionAlt = PickSection(alt);
                            var zAlt = IniHelper.GetKontoZuordnung(sectionAlt, mandantId, AppSettings.IniPath);
                            var defAlt = new Kontierung { Kost1 = zAlt.Kost1, Kost2 = zAlt.Kost2, Konto = zAlt.Konto };
                            var ctxAlt = new AccountingRuleContext
                            {
                                MandantId = mandantId,
                                PersId = persId,
                                FhzId = fhzId,
                                Typ = ctx.Typ,
                                Betrag19 = 0m,
                                Betrag7 = 0m,
                                Betrag0 = 0m
                            };
                            if (alt == "19") ctxAlt.Betrag19 = Math.Abs(betrag);
                            else if (alt == "7") ctxAlt.Betrag7 = Math.Abs(betrag);
                            else ctxAlt.Betrag0 = Math.Abs(betrag);

                            var r2 = _provider.GetKontierungFor(alt, ctxAlt, defAlt) ?? defAlt;
                            if (!(r2.Kost1 == defAlt.Kost1 && r2.Kost2 == defAlt.Kost2 && r2.Konto == defAlt.Konto))
                            {
                                try { AppLogger.Log($"[Rules][CrossGroup] Typ={ctx.Typ} initial={grp0} -> matched in {alt}: k1={r2.Kost1},k2={r2.Kost2},kto={r2.Konto}"); } catch { }
                                return r2;
                            }
                        }
                        catch { }
                    }
                }

                return res;
            }
            catch { return new Kontierung(); }
        }
    }
}
