using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Geldautomat
{
    // Einfacher Publisher für Quittungsinhalte.
    // Optional über INI konfigurierbar:
    // [Receipt]
    // Mode=File|Http
    // SharePath=...            (für Mode=File)
    // PublicBaseUrl=https://.. (für Mode=File)
    // ApiUrl=https://...       (für Mode=Http)
    // ApiKey=...               (optional für Mode=Http)
    public static class ReceiptPublisher
    {
        // Erzeugt aus freiem Text eine öffentlich erreichbare URL (falls konfiguriert).
        // Gibt null zurück, wenn keine Veröffentlichung möglich ist.
        public static string PublishFromFreeText(string text)
        {
            try
            {
                var mode = SafeReadIni("Receipt", "Mode")?.Trim();
                if (string.Equals(mode, "File", StringComparison.OrdinalIgnoreCase))
                    return PublishToFileShare(text);
                if (string.Equals(mode, "Http", StringComparison.OrdinalIgnoreCase))
                    return PublishViaHttp(text);
            }
            catch { }
            return null;
        }

        private static string PublishToFileShare(string text)
        {
            try
            {
                var sharePath = SafeReadIni("Receipt", "SharePath");
                var publicBase = SafeReadIni("Receipt", "PublicBaseUrl");
                if (string.IsNullOrWhiteSpace(sharePath) || string.IsNullOrWhiteSpace(publicBase))
                    return null;

                Directory.CreateDirectory(sharePath);

                var fileName = "quittung_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".html";
                var full = Path.Combine(sharePath, fileName);

                var html = BuildSimpleReceiptHtml(text);
                File.WriteAllText(full, html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                if (!publicBase.EndsWith("/", StringComparison.Ordinal)) publicBase += "/";
                return publicBase + Uri.EscapeUriString(fileName);
            }
            catch
            {
                return null;
            }
        }

        private static string PublishViaHttp(string text)
        {
            try
            {
                var apiUrl = SafeReadIni("Receipt", "ApiUrl");
                if (string.IsNullOrWhiteSpace(apiUrl)) return null;

                var apiKey = SafeReadIni("Receipt", "ApiKey"); // optional
                var json = "{\"content\":\"" + JsonEscape(text ?? string.Empty) + "\"}";

                var req = (HttpWebRequest)WebRequest.Create(apiUrl);
                req.Method = "POST";
                req.ContentType = "application/json; charset=utf-8";
                req.Timeout = 15000;
                req.ReadWriteTimeout = 15000;
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    // Verschiedene Varianten zulassen
                    req.Headers["Authorization"] = "Bearer " + apiKey;
                    req.Headers["X-Api-Key"] = apiKey;
                }

                var body = Encoding.UTF8.GetBytes(json);
                using (var rs = req.GetRequestStream())
                {
                    rs.Write(body, 0, body.Length);
                }

                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    var respText = sr.ReadToEnd().Trim();
                    // 1) reine URL im Body?
                    if (respText.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        respText.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        return respText;

                    // 2) JSON mit "url":"..."
                    var m = Regex.Match(respText, "\"url\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        var url = m.Groups[1].Value;
                        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                            return url;
                    }
                }
            }
            catch { }
            return null;
        }

        private static string BuildSimpleReceiptHtml(string content)
        {
            string safe = HtmlEscape(content ?? string.Empty);
            string now = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
            return "<!DOCTYPE html><html lang=\"de\"><head><meta charset=\"utf-8\"/>" +
                   "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"/>" +
                   "<title>Quittung</title>" +
                   "<style>body{font-family:Segoe UI,Arial,sans-serif;margin:20px;}pre{white-space:pre-wrap;word-wrap:break-word;background:#f8f9fa;padding:12px;border:1px solid #dee2e6;border-radius:4px;}small{color:#666}</style>" +
                   "</head><body>" +
                   "<h1>Quittung</h1>" +
                   "<small>Erstellt am " + now + "</small>" +
                   "<pre>" + safe + "</pre>" +
                   "</body></html>";
        }

        private static string HtmlEscape(string s)
        {
            return (s ?? string.Empty)
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&#39;");
        }

        private static string JsonEscape(string s)
        {
            var sb = new StringBuilder();
            foreach (var ch in s ?? string.Empty)
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < 32) sb.Append("\\u" + ((int)ch).ToString("x4"));
                        else sb.Append(ch);
                        break;
                }
            }
            return sb.ToString();
        }

        private static string SafeReadIni(string section, string key)
        {
            try { return IniHelper.ReadValue(section, key, AppSettings.IniPath); } catch { return null; }
        }
    }
}
