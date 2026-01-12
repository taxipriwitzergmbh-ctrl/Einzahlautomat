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
        public static void SendReceiptToEmployee(PersonalInfo personal, string subjectPrefix, string body)
        {
            if (personal == null) throw new ArgumentNullException(nameof(personal));
            var email = personal.EMail;
            if (string.IsNullOrWhiteSpace(email)) throw new InvalidOperationException("Keine E-Mail-Adresse hinterlegt.");

            var cfg = MailSettings.Load();
            if (!cfg.IsConfigured) throw new InvalidOperationException("Maileinstellungen unvollständig.");

            var mail = new MailMessage();
            mail.From = new MailAddress(cfg.FromAddress, cfg.FromDisplayName);
            mail.To.Add(email);
            mail.Subject = $"{subjectPrefix} Quittung";
            mail.Body = body;
            mail.IsBodyHtml = false;

            using (var client = new SmtpClient(cfg.SmtpHost, cfg.SmtpPort))
            {
                client.EnableSsl = cfg.EnableSsl;
                if (!string.IsNullOrWhiteSpace(cfg.Username)) client.Credentials = new NetworkCredential(cfg.Username, cfg.Password); else client.UseDefaultCredentials = true;
                client.Send(mail);
            }
            try { AppLogger.Log($"Mail gesendet: Quittung an '{email}' (Betreff='{mail.Subject}')"); } catch { }
        }

        public static void SendReceiptToEmployeeWithAttachment(PersonalInfo personal, string subjectPrefix, string body, string attachmentPlainText, string attachmentFileName = "Quittung.html")
        {
            if (personal == null) throw new ArgumentNullException(nameof(personal));
            var email = personal.EMail;
            if (string.IsNullOrWhiteSpace(email)) throw new InvalidOperationException("Keine E-Mail-Adresse hinterlegt.");

            var cfg = MailSettings.Load();
            if (!cfg.IsConfigured) throw new InvalidOperationException("Maileinstellungen unvollständig.");

            var mail = new MailMessage();
            mail.From = new MailAddress(cfg.FromAddress, cfg.FromDisplayName);
            mail.To.Add(email);
            mail.Subject = $"{subjectPrefix} Quittung";
            mail.Body = body;
            mail.IsBodyHtml = false;

            var html = BuildSimpleReceiptHtml(attachmentPlainText ?? string.Empty);
            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(html);
            var ms = new MemoryStream(bytes);
            var attachment = new Attachment(ms, attachmentFileName, "text/html");
            mail.Attachments.Add(attachment);

            using (var client = new SmtpClient(cfg.SmtpHost, cfg.SmtpPort))
            {
                client.EnableSsl = cfg.EnableSsl;
                if (!string.IsNullOrWhiteSpace(cfg.Username)) client.Credentials = new NetworkCredential(cfg.Username, cfg.Password); else client.UseDefaultCredentials = true;
                client.Send(mail);
            }
            try { AppLogger.Log($"Mail gesendet: Quittung+Anhang ('{attachmentFileName}') an '{email}' (Betreff='{mail.Subject}')"); } catch { }
        }

        // Support-/Fehler-Meldung versenden � mehrere Empf�nger via ';' erlaubt; Support kann per INI deaktiviert werden
        public static void SendSupportAlert(string subjectSuffix, string message)
        {
            var cfg = MailSettings.Load();
            if (!cfg.IsConfigured) return;

            string device = (AppSettings.AutomatenName ?? string.Empty).Trim();
            string subject = string.IsNullOrWhiteSpace(device) ? $"Automat � Meldung {subjectSuffix}" : $"{device} � Meldung {subjectSuffix}";
            string body = (message ?? string.Empty);

            var mail = new MailMessage();
            mail.From = new MailAddress(cfg.FromAddress, cfg.FromDisplayName);

            var toLog = new System.Collections.Generic.List<string>();
            // Ziel 1: konfigurierte Fehler-Mails (mehrere via ';')
            if (!string.IsNullOrWhiteSpace(cfg.AlertEmail))
            {
                var recipients = cfg.AlertEmail.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                                               .Select(s => s.Trim())
                                               .Where(s => !string.IsNullOrWhiteSpace(s));
                foreach (var r in recipients) { try { mail.To.Add(r); toLog.Add(r); } catch { } }
            }
            // Ziel 2: optional Support, falls nicht deaktiviert
            if (!cfg.DisableSupportMail)
            {
                try { mail.To.Add("support@priwitzer-dienstleistungsgmbh.de"); toLog.Add("support@priwitzer-dienstleistungsgmbh.de"); } catch { }
            }

            mail.Subject = subject;
            mail.Body = body;
            mail.IsBodyHtml = false;

            using (var client = new SmtpClient(cfg.SmtpHost, cfg.SmtpPort))
            {
                client.EnableSsl = cfg.EnableSsl;
                if (!string.IsNullOrWhiteSpace(cfg.Username)) client.Credentials = new NetworkCredential(cfg.Username, cfg.Password); else client.UseDefaultCredentials = true;
                client.Send(mail);
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
        public string AlertEmail { get; set; } // mehrere Empf�nger per ';'
        public bool DisableSupportMail { get; set; } // NEU: Support-Mail unterdr�cken

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
