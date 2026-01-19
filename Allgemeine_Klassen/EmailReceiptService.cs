using System;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.IO;
using System.Linq;
using System.Security.Cryptography; // DPAPI

namespace Geldautomat
{
    public static class EmailReceiptService
    {
        // Dienstkonto bevorzugt laden (über in INI gespeicherte DienstkontoID) und in SMTP-Konfiguration umsetzen
        private struct DienstkontoCfg { public string FromAddress; public string FromName; public string Host; public int Port; public bool EnableSsl; public string User; public string Password; }

        private static bool TryGetDienstkontoCfg(out DienstkontoCfg cfg)
        {
            cfg = new DienstkontoCfg();
            try
            {
                // Dienstkonto-ID aus INI
                int dkId = 0; int.TryParse(IniHelper.ReadValue("Mail", "DienstkontoID", AppSettings.IniPath), out dkId);
                if (dkId <= 0) return false;

                // Aus DB laden
                DatabaseHelper.DienstkontoInfo selected = null;
                try
                {
                    using (var db = new DatabaseHelper())
                    {
                        var list = db.GetDienstkontenAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                        if (list != null) selected = list.Find(x => x.ID == dkId);
                    }
                }
                catch { selected = null; }
                if (selected == null) return false;

                // Passwort entschlüsseln (TinyDecrypt)
                string plainPwd = string.IsNullOrEmpty(selected.Passwort) ? string.Empty : TinyDecrypt(selected.Passwort);
                int port = 25; if (!string.IsNullOrWhiteSpace(selected.Port)) int.TryParse(selected.Port, out port);
                bool ssl = true; if (selected.Typ.HasValue) ssl = selected.Typ.Value != 0;

                // FromName Prefer INI override
                string fromNameOverride = IniHelper.ReadValue("Mail", "DienstkontoFromName", AppSettings.IniPath);

                cfg = new DienstkontoCfg
                {
                    FromAddress = string.IsNullOrWhiteSpace(selected.Absender) ? (selected.Benutzername ?? string.Empty) : selected.Absender,
                    FromName = string.IsNullOrWhiteSpace(fromNameOverride) ? selected.Name : fromNameOverride,
                    Host = selected.Host,
                    Port = port,
                    EnableSsl = ssl,
                    User = selected.Benutzername,
                    Password = plainPwd,
                };
                // Minimal valid
                return !string.IsNullOrWhiteSpace(cfg.FromAddress) && !string.IsNullOrWhiteSpace(cfg.Host);
            }
            catch { return false; }
        }

        // Zweites Dienstkonto für Dokumente/Quittungen/Zeiterfassung
        private static bool TryGetDienstkonto2Cfg(out DienstkontoCfg cfg)
        {
            cfg = new DienstkontoCfg();
            try
            {
                int dkId = 0; int.TryParse(IniHelper.ReadValue("Mail", "Dienstkonto2ID", AppSettings.IniPath), out dkId);
                if (dkId <= 0) return false;
                DatabaseHelper.DienstkontoInfo selected = null;
                try
                {
                    using (var db = new DatabaseHelper())
                    {
                        var list = db.GetDienstkontenAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                        if (list != null) selected = list.Find(x => x.ID == dkId);
                    }
                }
                catch { selected = null; }
                if (selected == null) return false;

                string plainPwd = string.IsNullOrEmpty(selected.Passwort) ? string.Empty : TinyDecrypt(selected.Passwort);
                int port = 25; if (!string.IsNullOrWhiteSpace(selected.Port)) int.TryParse(selected.Port, out port);
                bool ssl = true; if (selected.Typ.HasValue) ssl = selected.Typ.Value != 0;
                string fromNameOverride = IniHelper.ReadValue("Mail", "Dienstkonto2FromName", AppSettings.IniPath);

                cfg = new DienstkontoCfg
                {
                    FromAddress = string.IsNullOrWhiteSpace(selected.Absender) ? (selected.Benutzername ?? string.Empty) : selected.Absender,
                    FromName = string.IsNullOrWhiteSpace(fromNameOverride) ? selected.Name : fromNameOverride,
                    Host = selected.Host,
                    Port = port,
                    EnableSsl = ssl,
                    User = selected.Benutzername,
                    Password = plainPwd,
                };
                return !string.IsNullOrWhiteSpace(cfg.FromAddress) && !string.IsNullOrWhiteSpace(cfg.Host);
            }
            catch { return false; }
        }

        // Provided decrypt from user
        public static string TinyDecrypt(string inp)
        {
            string s =
                "w4tykl1j8kie5kbtol3ou3qmm0fgssgj8nplxdcgcdqa0xo1cmvwo7lcmno9p8o839bcqobxo65qmfdesk2002j1xl8eseyz1tc6mylr4sb6geupohncz5zh7vz76upkrcobrqkrd95wqehrmeg5t1ch3k32cr73alnp3nj6jzfpm80sanqtml9c2ivre08cwyda3mx3fdhaj9d7e6k8w306jl7jqhh7mjiwlgj7rovi366e8rlsaywqkwhg46ybyqgofqg0wh2ongt9mwlyl24djqujlel3o254qruxxqk4bly8ozs0h4c1mulqtvxmjorhcc4aeqeh19pf2mkgfe2s2tuejppq68zj5b3ckufs0cfh9ysz4h74rr4f2fqzdoheijh0u2hkv6stthqnnd699gdqz6zt4p56s9owun35y04z1hvd4q1o86mqq3yk5lg772lkl7cjqq62f7gapstaymt0uzlo6t9odznycu5ritvumma8t2qpmpfy1rwu";

            int len = inp.Length / 2;
            byte[] temp = new byte[len];

            for (int i = 0; i < inp.Length; i += 2)
            {
                string v = inp.Substring(i, 2);
                int n = -1;

                for (int j = 0; j < s.Length; j += 2)
                {
                    if (s.Substring(j, 2) == v)
                    {
                        n = j / 2;
                        break;
                    }
                }

                temp[i / 2] = (byte)n;
            }

            byte[] K =
            {
                17, 1, 7, 26, 8, 18, 10, 4,
                23, 23, 16, 11, 10, 19, 24, 24,
                18, 19, 8, 20, 8, 22, 6, 18,
                11, 24, 25, 26, 12, 6, 22, 19
            };

            int kl = K.Length - 1;
            char[] result = new char[len];

            int keyIndex = 0;
            for (int i = 0; i < len; i++)
            {
                byte e = temp[i];
                result[i] = (char)(e ^ K[keyIndex]);

                keyIndex++;
                if (keyIndex >= kl)
                    keyIndex = 0;
            }

            return new string(result);
        }

        private static SmtpClient BuildClient(DienstkontoCfg cfg)
        {
            var client = new SmtpClient(cfg.Host, cfg.Port) { EnableSsl = cfg.EnableSsl };
            if (!string.IsNullOrWhiteSpace(cfg.User)) client.Credentials = new NetworkCredential(cfg.User, cfg.Password); else client.UseDefaultCredentials = true;
            return client;
        }

        private static MailAddress BuildFrom(DienstkontoCfg cfg)
        {
            return new MailAddress(cfg.FromAddress, cfg.FromName);
        }

        public static void SendReceiptToEmployee(PersonalInfo personal, string subjectPrefix, string body)
        {
            if (personal == null) throw new ArgumentNullException(nameof(personal));
            var email = personal.EMail;
            if (string.IsNullOrWhiteSpace(email)) throw new InvalidOperationException("Keine E-Mail-Adresse hinterlegt.");

            // Versuche Dienstkonto2, sonst INI
            DienstkontoCfg dk2;
            if (TryGetDienstkonto2Cfg(out dk2))
            {
                var mail = new MailMessage();
                mail.From = BuildFrom(dk2);
                mail.To.Add(email);
                mail.Subject = $"{subjectPrefix} Quittung";
                mail.Body = body;
                mail.IsBodyHtml = false;
                using (var client = BuildClient(dk2)) client.Send(mail);
                try { AppLogger.Log($"Mail gesendet (DK2): Quittung an '{email}' (Betreff='{mail.Subject}')"); } catch { }
                return;
            }

            var cfg = MailSettings.Load();
            if (!cfg.IsConfigured) throw new InvalidOperationException("Maileinstellungen unvollständig.");

            var mailFallback = new MailMessage();
            mailFallback.From = new MailAddress(cfg.FromAddress, cfg.FromDisplayName);
            mailFallback.To.Add(email);
            mailFallback.Subject = $"{subjectPrefix} Quittung";
            mailFallback.Body = body;
            mailFallback.IsBodyHtml = false;
            using (var client = new SmtpClient(cfg.SmtpHost, cfg.SmtpPort)) { client.EnableSsl = cfg.EnableSsl; if (!string.IsNullOrWhiteSpace(cfg.Username)) client.Credentials = new NetworkCredential(cfg.Username, cfg.Password); else client.UseDefaultCredentials = true; client.Send(mailFallback); }
            try { AppLogger.Log($"Mail gesendet (INI): Quittung an '{email}' (Betreff='{mailFallback.Subject}')"); } catch { }
        }

        public static void SendReceiptToEmployeeWithAttachment(PersonalInfo personal, string subjectPrefix, string body, string attachmentPlainText, string attachmentFileName = "Quittung.html")
        {
            if (personal == null) throw new ArgumentNullException(nameof(personal));
            var email = personal.EMail;
            if (string.IsNullOrWhiteSpace(email)) throw new InvalidOperationException("Keine E-Mail-Adresse hinterlegt.");

            DienstkontoCfg dk2;
            if (TryGetDienstkonto2Cfg(out dk2))
            {
                var mail = new MailMessage();
                mail.From = BuildFrom(dk2);
                mail.To.Add(email);
                mail.Subject = $"{subjectPrefix} Quittung";
                mail.Body = body;
                mail.IsBodyHtml = false;
                var html = BuildSimpleReceiptHtml(attachmentPlainText ?? string.Empty);
                var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(html);
                var ms = new MemoryStream(bytes);
                var attachment = new Attachment(ms, attachmentFileName, "text/html");
                mail.Attachments.Add(attachment);
                using (var client = BuildClient(dk2)) client.Send(mail);
                try { AppLogger.Log($"Mail gesendet (DK2): Quittung+Anhang ('{attachmentFileName}') an '{email}' (Betreff='{mail.Subject}')"); } catch { }
                return;
            }

            var cfg = MailSettings.Load();
            if (!cfg.IsConfigured) throw new InvalidOperationException("Maileinstellungen unvollständig.");

            var mailFallback = new MailMessage();
            mailFallback.From = new MailAddress(cfg.FromAddress, cfg.FromDisplayName);
            mailFallback.To.Add(email);
            mailFallback.Subject = $"{subjectPrefix} Quittung";
            mailFallback.Body = body;
            mailFallback.IsBodyHtml = false;
            var html2 = BuildSimpleReceiptHtml(attachmentPlainText ?? string.Empty);
            var bytes2 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(html2);
            var ms2 = new MemoryStream(bytes2);
            var attachment2 = new Attachment(ms2, attachmentFileName, "text/html");
            mailFallback.Attachments.Add(attachment2);
            using (var client = new SmtpClient(cfg.SmtpHost, cfg.SmtpPort)) { client.EnableSsl = cfg.EnableSsl; if (!string.IsNullOrWhiteSpace(cfg.Username)) client.Credentials = new NetworkCredential(cfg.Username, cfg.Password); else client.UseDefaultCredentials = true; client.Send(mailFallback); }
            try { AppLogger.Log($"Mail gesendet (INI): Quittung+Anhang ('{attachmentFileName}') an '{email}' (Betreff='{mailFallback.Subject}')"); } catch { }
        }

        // Support-/Fehler-Meldung versenden – mehrere Empfänger via ';' erlaubt; Support kann per INI deaktiviert werden
        public static void SendSupportAlert(string subjectSuffix, string message)
        {
            // Versuche Dienstkonto1 zu verwenden, sonst Fallback auf alte INI-Mailsettings
            DienstkontoCfg dk;
            bool useDienstkonto = TryGetDienstkontoCfg(out dk);

            MailSettings cfg = null;
            if (!useDienstkonto)
            {
                cfg = MailSettings.Load();
                if (!cfg.IsConfigured) return;
            }

            string device = (AppSettings.AutomatenName ?? string.Empty).Trim();
            string subject = string.IsNullOrWhiteSpace(device) ? $"Automat – Meldung {subjectSuffix}" : $"{device} – Meldung {subjectSuffix}";
            string body = (message ?? string.Empty);

            var mail = new MailMessage();
            if (useDienstkonto)
                mail.From = BuildFrom(dk);
            else
                mail.From = new MailAddress(cfg.FromAddress, cfg.FromDisplayName);

            var toLog = new System.Collections.Generic.List<string>();
            // Ziel 1: konfigurierte Fehler-Mails (mehrere via ';')
            string alert = IniHelper.ReadValue("Mail", "AlertEmail", AppSettings.IniPath);
            if (string.IsNullOrWhiteSpace(alert) && cfg != null) alert = cfg.AlertEmail;
            if (!string.IsNullOrWhiteSpace(alert))
            {
                var recipients = alert.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                                       .Select(s => s.Trim())
                                       .Where(s => !string.IsNullOrWhiteSpace(s));
                foreach (var r in recipients) { try { mail.To.Add(r); toLog.Add(r); } catch { } }
            }
            // Ziel 2: optional Support, falls nicht deaktiviert
            bool disableSupport = false;
            try { bool tmp; disableSupport = bool.TryParse(IniHelper.ReadValue("Mail", "DisableSupportMail", AppSettings.IniPath), out tmp) ? tmp : false; } catch { }
            if (!disableSupport)
            {
                try { mail.To.Add("support@priwitzer-dienstleistungsgmbh.de"); toLog.Add("support@priwitzer-dienstleistungsgmbh.de"); } catch { }
            }

            mail.Subject = subject;
            mail.Body = body;
            mail.IsBodyHtml = false;

            if (useDienstkonto)
            {
                using (var client = BuildClient(dk)) client.Send(mail);
            }
            else
            {
                using (var client = new SmtpClient(cfg.SmtpHost, cfg.SmtpPort))
                {
                    client.EnableSsl = cfg.EnableSsl;
                    if (!string.IsNullOrWhiteSpace(cfg.Username)) client.Credentials = new NetworkCredential(cfg.Username, cfg.Password); else client.UseDefaultCredentials = true;
                    client.Send(mail);
                }
            }
            try { AppLogger.Log($"Support-Fehlermail gesendet (Betreff='{subject}') an: " + string.Join(", ", toLog.ToArray())); } catch { }
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
    }

    public class MailSettings
    {
        public string FromAddress { get; set; }
        public string FromDisplayName { get; set; }
        public string SmtpHost { get; set; }
        public int SmtpPort { get; set; } = 587;
        public bool EnableSsl { get; set; } = true;
        public string Username { get; set; }
        public string Password { get; set; }
        public string AlertEmail { get; set; } // mehrere Empfänger per ';'
        public bool DisableSupportMail { get; set; } // NEU: Support-Mail unterdrücken

        public bool IsConfigured => !string.IsNullOrWhiteSpace(FromAddress) && !string.IsNullOrWhiteSpace(SmtpHost);

        private const string IniSection = "Mail";

        private static string DecryptSecret(string stored)
        {
            try
            {
                if (string.IsNullOrEmpty(stored)) return string.Empty;
                if (!stored.StartsWith("enc:", StringComparison.Ordinal)) return stored; // backward plaintext
                var b64 = stored.Substring(4);
                var protectedBytes = Convert.FromBase64String(b64);
                var unprotected = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(unprotected);
            }
            catch { return string.Empty; }
        }

        public static MailSettings Load()
        {
            var s = new MailSettings();
            try
            {
                s.FromAddress = IniHelper.ReadValue(IniSection, "FromAddress", AppSettings.IniPath);
                s.FromDisplayName = IniHelper.ReadValue(IniSection, "FromDisplayName", AppSettings.IniPath);
                s.SmtpHost = IniHelper.ReadValue(IniSection, "SmtpHost", AppSettings.IniPath);
                int port; if (int.TryParse(IniHelper.ReadValue(IniSection, "SmtpPort", AppSettings.IniPath), out port)) s.SmtpPort = port;
                bool ssl; if (bool.TryParse(IniHelper.ReadValue(IniSection, "EnableSsl", AppSettings.IniPath), out ssl)) s.EnableSsl = ssl;
                s.Username = IniHelper.ReadValue(IniSection, "Username", AppSettings.IniPath);
                var storedPwd = IniHelper.ReadValue(IniSection, "Password", AppSettings.IniPath);
                s.Password = DecryptSecret(storedPwd ?? string.Empty);
                s.AlertEmail = IniHelper.ReadValue(IniSection, "AlertEmail", AppSettings.IniPath);
                bool disableSupport; s.DisableSupportMail = bool.TryParse(IniHelper.ReadValue(IniSection, "DisableSupportMail", AppSettings.IniPath), out disableSupport) ? disableSupport : false;
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try { IniHelper.WriteValue(IniSection, "FromAddress", FromAddress ?? string.Empty, AppSettings.IniPath); } catch { }
            try { IniHelper.WriteValue(IniSection, "FromDisplayName", FromDisplayName ?? string.Empty, AppSettings.IniPath); } catch { }
            try { IniHelper.WriteValue(IniSection, "SmtpHost", SmtpHost ?? string.Empty, AppSettings.IniPath); } catch { }
            try { IniHelper.WriteValue(IniSection, "SmtpPort", SmtpPort.ToString(), AppSettings.IniPath); } catch { }
            try { IniHelper.WriteValue(IniSection, "EnableSsl", EnableSsl ? "True" : "False", AppSettings.IniPath); } catch { }
            try { IniHelper.WriteValue(IniSection, "Username", Username ?? string.Empty, AppSettings.IniPath); } catch { }
            try { IniHelper.WriteValue(IniSection, "Password", Password ?? string.Empty, AppSettings.IniPath); } catch { }
            try { IniHelper.WriteValue(IniSection, "AlertEmail", AlertEmail ?? string.Empty, AppSettings.IniPath); } catch { }
            try { IniHelper.WriteValue(IniSection, "DisableSupportMail", DisableSupportMail ? "True" : "False", AppSettings.IniPath); } catch { }
        }
    }
}
