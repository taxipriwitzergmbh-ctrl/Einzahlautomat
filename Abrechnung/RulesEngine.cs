using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Geldautomat.Abrechnung
{
    public sealed class AbrechnungsRegel
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string JoinKind { get; set; }
        public bool IsDefault { get; set; }
        public int Priority { get; set; }
        public int? ManId { get; set; }
        public int? PersId { get; set; }
        public string FhzIds { get; set; }
        public int? ResultKost1 { get; set; }
        public int? ResultKost2 { get; set; }
        public int? ResultKonto { get; set; }
        public string ResultBuchungstext { get; set; }
        public bool IsActive { get; set; } = true;
        public List<AbrechnungsClause> Clauses { get; set; } = new List<AbrechnungsClause>();

        // Deep clone to support rule expansion without referencing external implementations
        public AbrechnungsRegel Clone()
        {
            var copy = (AbrechnungsRegel)this.MemberwiseClone();
            if (this.Clauses != null)
            {
                copy.Clauses = new List<AbrechnungsClause>(this.Clauses.Count);
                foreach (var c in this.Clauses)
                {
                    if (c == null) { copy.Clauses.Add(null); continue; }
                    copy.Clauses.Add(new AbrechnungsClause
                    {
                        Id = c.Id,
                        RuleId = c.RuleId,
                        GroupId = c.GroupId,
                        Field = c.Field,
                        Operator = c.Operator,
                        Value = c.Value,
                    });
                }
            }
            else
            {
                copy.Clauses = new List<AbrechnungsClause>();
            }
            return copy;
        }
    }

    public sealed class AbrechnungsClause
    {
        public int Id { get; set; }
        public int RuleId { get; set; }
        public int GroupId { get; set; }
        public string Field { get; set; }
        public string Operator { get; set; }
        public string Value { get; set; }
    }

    public static class RulesEngine
    {
        public sealed class RuleContext
        {
            public int FirmenId { get; set; }
            public int? PersId { get; set; }
            public int? FhzId { get; set; }
            public string Typ { get; set; }
            public decimal Betrag19 { get; set; }
            public decimal Betrag7 { get; set; }
            public decimal Betrag0 { get; set; }
        }

        private static bool TryParseInt(string s, out int v) { return int.TryParse((s ?? string.Empty).Trim(), out v); }
        private static bool TryParseDecimal(string s, out decimal v) { return decimal.TryParse((s ?? string.Empty).Trim(), out v); }

        private static bool EvalClause(AbrechnungsClause c, RuleContext ctx)
        {
            if (c == null) return true;
            var op = (c.Operator ?? string.Empty).Trim().ToUpperInvariant();
            var fld = (c.Field ?? string.Empty).Trim();
            var val = c.Value ?? string.Empty;

            Func<int?> getInt = () =>
            {
                switch (fld.ToLowerInvariant())
                {
                    case "manid": return ctx.FirmenId;
                    case "firmenid": return ctx.FirmenId;
                    case "persid": return ctx.PersId;
                    case "fhzid": return ctx.FhzId;
                }
                return null;
            };
            Func<decimal?> getDec = () =>
            {
                switch (fld.ToLowerInvariant())
                {
                    case "betrag19": return ctx.Betrag19;
                    case "betrag7": return ctx.Betrag7;
                    case "betrag0": return ctx.Betrag0;
                }
                return null;
            };
            Func<string> getStr = () =>
            {
                switch (fld.ToLowerInvariant())
                {
                    case "typ": return ctx.Typ ?? string.Empty;
                }
                return null;
            };

            if (op == "IN" || op == "NOT IN")
            {
                var items = (val ?? string.Empty).Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
                var ii = new HashSet<int>();
                foreach (var s in items) { if (TryParseInt(s, out var iv)) ii.Add(iv); }
                bool contains;
                if (ii.Count > 0)
                {
                    var iv = getInt();
                    contains = iv.HasValue && ii.Contains(iv.Value);
                }
                else
                {
                    var sv = getStr();
                    contains = sv != null && items.Contains(sv, StringComparer.OrdinalIgnoreCase);
                }
                return op == "IN" ? contains : !contains;
            }

            if (op == "IS NULL" || op == "IS NOT NULL")
            {
                bool isNull;
                var iv = getInt();
                if (iv.HasValue) isNull = false; else
                {
                    var dv = getDec(); if (dv.HasValue) isNull = false; else
                    {
                        var sv = getStr(); isNull = string.IsNullOrEmpty(sv);
                    }
                }
                return op == "IS NULL" ? isNull : !isNull;
            }

            var leftInt = getInt();
            if (leftInt.HasValue)
            {
                if (op == "=" || op == "!=")
                {
                    var parts = (val ?? string.Empty).Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
                    var ints = new HashSet<int>(); foreach (var s in parts) { if (TryParseInt(s, out var iv)) ints.Add(iv); }
                    if (ints.Count > 0)
                    {
                        bool contains = ints.Contains(leftInt.Value);
                        return op == "=" ? contains : !contains;
                    }
                }
                if (!TryParseInt(val, out var rightInt)) rightInt = 0;
                switch (op)
                {
                    case "=": return leftInt.Value == rightInt;
                    case "!=": return leftInt.Value != rightInt;
                    case ">": return leftInt.Value > rightInt;
                    case ">=": return leftInt.Value >= rightInt;
                    case "<": return leftInt.Value < rightInt;
                    case "<=": return leftInt.Value <= rightInt;
                }
            }

            var leftDec = getDec();
            if (leftDec.HasValue)
            {
                if (!TryParseDecimal(val, out var rightDec)) rightDec = 0m;
                switch (op)
                {
                    case "=": return leftDec.Value == rightDec;
                    case "!=": return leftDec.Value != rightDec;
                    case ">": return leftDec.Value > rightDec;
                    case ">=": return leftDec.Value >= rightDec;
                    case "<": return leftDec.Value < rightDec;
                    case "<=": return leftDec.Value <= rightDec;
                }
            }

            var leftStr = getStr();
            if (leftStr != null)
            {
                if (op == "=" || op == "!=")
                {
                    var parts = (val ?? string.Empty).Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 1)
                    {
                        bool contains = parts.Any(p => string.Equals(leftStr, p, StringComparison.OrdinalIgnoreCase));
                        return op == "=" ? contains : !contains;
                    }
                }
                switch (op)
                {
                    case "=": return string.Equals(leftStr, val, StringComparison.OrdinalIgnoreCase);
                    case "!=": return !string.Equals(leftStr, val, StringComparison.OrdinalIgnoreCase);
                }
            }

            return false;
        }

        private static string SanitizeWhere(string where)
        {
            if (string.IsNullOrWhiteSpace(where)) return string.Empty;
            var w = where;
            try { w = Regex.Replace(w, @"\[[^\]]*\]", " "); } catch { }
            w = w.Replace("ISNULL(Betrag19,0)", "Betrag19");
            w = w.Replace("ISNULL(Betrag7,0)", "Betrag7");
            w = w.Replace("ISNULL(Betrag0,0)", "Betrag0");
            w = Regex.Replace(w, @"\s+", " ").Trim();
            return w;
        }

        private static bool MatchesRule(AbrechnungsRegel r, RuleContext ctx)
        {
            if (r.Clauses != null && r.Clauses.Count > 0)
            {
                var safeClauses = r.Clauses.Where(c => c != null).ToList();
                if (safeClauses.Count == 0) return true;
                var groups = safeClauses.GroupBy(c => c.GroupId);
                bool anyGroupTrue = false;
                foreach (var g in groups)
                {
                    bool allTrue = true;
                    foreach (var c in g)
                    {
                        if (!EvalClause(c, ctx)) { allTrue = false; break; }
                    }
                    if (allTrue) { anyGroupTrue = true; break; }
                }
                if (!anyGroupTrue) return false;
            }
            return true;
        }

        public static string ExplainRuleMatching(IEnumerable<AbrechnungsRegel> rules, RuleContext ctx)
        {
            var sb = new StringBuilder();
            try
            {
                sb.AppendLine($"Context: ManId={ctx?.FirmenId}, PersId={ctx?.PersId}, FhzId={ctx?.FhzId}, Typ='{ctx?.Typ}', B19={ctx?.Betrag19}, B7={ctx?.Betrag7}, B0={ctx?.Betrag0}");
                if (rules == null) { sb.AppendLine("No rules loaded."); return sb.ToString(); }
                int idx = 0;
                foreach (var r in rules)
                {
                    idx++;
                    if (r == null) { sb.AppendLine($"[{idx}] <null> rule"); continue; }
                    if (!r.IsActive) { sb.AppendLine($"[{idx}] {r.Name} (Id={r.Id}) -> inactive"); continue; }
                    bool match = MatchesRule(r, ctx);
                    sb.AppendLine(match ? $"[{idx}] MATCH {r.Name} (Id={r.Id})" : $"[{idx}] no match {r.Name} (Id={r.Id})");
                }
            }
            catch (Exception ex) { sb.AppendLine("Explain failed: " + ex.Message); }
            return sb.ToString();
        }

        public static async Task<List<AbrechnungsRegel>> LoadRulesAsync()
        {
            try
            {
                using (var db = new Geldautomat.DatabaseHelper())
                {
                    var dt = await db.LoadAbrechnungsRegelnAsync();
                    var list = new List<AbrechnungsRegel>();
                    if (dt != null)
                    {
                        foreach (DataRow r in dt.Rows)
                        {
                            if (r == null) continue;
                            var model = new AbrechnungsRegel
                            {
                                Id = Convert.ToInt32(r["Id"]),
                                Name = r["Name"] as string,
                                JoinKind = r["JoinKind"] as string,
                                IsDefault = r.Table.Columns.Contains("IsDefault") && r["IsDefault"] != DBNull.Value && Convert.ToBoolean(r["IsDefault"]),
                                Priority = r["Priority"] == DBNull.Value ? 100 : Convert.ToInt32(r["Priority"]),
                                ManId = null,
                                FhzIds = null,
                                PersId = null,
                                ResultKost1 = r["ResultKost1"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["ResultKost1"]),
                                ResultKost2 = r["ResultKost2"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["ResultKost2"]),
                                ResultKonto = r["ResultKonto"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["ResultKonto"]),
                                ResultBuchungstext = r["ResultText"] as string,
                                IsActive = r.Table.Columns.Contains("IsActive") && r["IsActive"] != DBNull.Value ? Convert.ToBoolean(r["IsActive"]) : true,
                                Clauses = new List<AbrechnungsClause>()
                            };
                            if (!model.IsDefault && r.Table.Columns.Contains("Fallback") && r["Fallback"] != DBNull.Value)
                            {
                                try
                                {
                                    if (r["Fallback"] is bool fbBool)
                                        model.IsDefault = fbBool;
                                    else
                                    {
                                        var s = Convert.ToString(r["Fallback"])?.Trim();
                                        if (!string.IsNullOrEmpty(s))
                                        {
                                            model.IsDefault = s.Equals("ja", StringComparison.OrdinalIgnoreCase)
                                                             || s.Equals("yes", StringComparison.OrdinalIgnoreCase)
                                                             || s.Equals("true", StringComparison.OrdinalIgnoreCase)
                                                             || s.Equals("1");
                                        }
                                    }
                                }
                                catch { }
                            }
                            list.Add(model);
                        }
                    }

                    await TryLoadClausesAsync(list, db);

                    return list
                        .Where(r => r != null)
                        .OrderBy(r => r.IsDefault ? 1 : 0)
                        .ThenBy(r => r.Priority)
                        .ThenBy(r => r.Id)
                        .ToList();
                }
            }
            catch (Exception)
            {
                return new List<AbrechnungsRegel>();
            }
        }

        private static async Task TryLoadClausesAsync(List<AbrechnungsRegel> rules, Geldautomat.DatabaseHelper db)
        {
            try
            {
                var dt = await db.LoadAbrechnungsClausesAsync();
                if (dt == null) return;
                var lookup = rules.ToDictionary(r => r.Id, r => r);
                foreach (DataRow r in dt.Rows)
                {
                    if (r == null) continue;
                    if (!dt.Columns.Contains("RuleId")) continue;
                    int ruleId = Convert.ToInt32(r["RuleId"]);
                    if (!lookup.TryGetValue(ruleId, out var rule)) continue;
                    if (rule.Clauses == null) rule.Clauses = new List<AbrechnungsClause>();
                    var clause = new AbrechnungsClause();
                    try { if (dt.Columns.Contains("Id") && r["Id"] != DBNull.Value) clause.Id = Convert.ToInt32(r["Id"]); } catch { }
                    clause.RuleId = ruleId;
                    try { clause.GroupId = (dt.Columns.Contains("GroupId") && r["GroupId"] != DBNull.Value) ? Convert.ToInt32(r["GroupId"]) : 0; } catch { clause.GroupId = 0; }
                    try { clause.Field = dt.Columns.Contains("Field") ? (r["Field"] as string) : null; } catch { clause.Field = null; }
                    try { clause.Operator = dt.Columns.Contains("Operator") ? (r["Operator"] as string) : null; } catch { clause.Operator = null; }
                    try { clause.Value = dt.Columns.Contains("Value") ? (r["Value"] as string) : null; } catch { clause.Value = null; }
                    rule.Clauses.Add(clause);
                }
            }
            catch { }
        }

        public static void ApplyForEdit(IEnumerable<AbrechnungsRegel> rules, RuleContext ctx, decimal v19, decimal v7, decimal v0,
            ref int? kost1, ref int? kost2, ref int? konto, ref string buchungstext)
        {
            if (ctx == null) ctx = new RuleContext();
            ctx.Betrag19 = v19; ctx.Betrag7 = v7; ctx.Betrag0 = v0;
            ApplyForEdit(rules, ctx, ref kost1, ref kost2, ref konto, ref buchungstext);
        }

        public static void ApplyForEdit(IEnumerable<AbrechnungsRegel> rules, RuleContext ctx,
            ref int? kost1, ref int? kost2, ref int? konto, ref string buchungstext)
        {
            if (rules == null) return;
            int? specK1 = null, specK2 = null, specKto = null; string specTxt = null;
            int? defK1 = null, defK2 = null, defKto = null; string defTxt = null;

            foreach (var r in rules)
            {
                if (r == null || !r.IsActive) continue;
                if (!MatchesRule(r, ctx)) continue;

                if (r.IsDefault)
                {
                    if (!defK1.HasValue && r.ResultKost1.HasValue) defK1 = r.ResultKost1;
                    if (!defK2.HasValue && r.ResultKost2.HasValue) defK2 = r.ResultKost2;
                    if (!defKto.HasValue && r.ResultKonto.HasValue) defKto = r.ResultKonto;
                    if (string.IsNullOrWhiteSpace(defTxt) && !string.IsNullOrWhiteSpace(r.ResultBuchungstext)) defTxt = r.ResultBuchungstext;
                }
                else
                {
                    if (!specK1.HasValue && r.ResultKost1.HasValue) specK1 = r.ResultKost1;
                    if (!specK2.HasValue && r.ResultKost2.HasValue) specK2 = r.ResultKost2;
                    if (!specKto.HasValue && r.ResultKonto.HasValue) specKto = r.ResultKonto;
                    if (string.IsNullOrWhiteSpace(specTxt) && !string.IsNullOrWhiteSpace(r.ResultBuchungstext)) specTxt = r.ResultBuchungstext;
                }
            }

            if (!kost1.HasValue) kost1 = specK1.HasValue ? specK1 : defK1;
            if (!kost2.HasValue) kost2 = specK2.HasValue ? specK2 : defK2;
            if (!konto.HasValue) konto = specKto.HasValue ? specKto : defKto;
            if (string.IsNullOrWhiteSpace(buchungstext)) buchungstext = !string.IsNullOrWhiteSpace(specTxt) ? specTxt : defTxt;
        }
    }
}
