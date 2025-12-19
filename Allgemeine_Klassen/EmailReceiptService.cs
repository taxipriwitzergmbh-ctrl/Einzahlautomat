using System;
using System.Net;
using System.Net.Mail;
using System.Reflection;

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
                if (!string.IsNullOrWhiteSpace(cfg.Username))
                {
                    client.Credentials = new NetworkCredential(cfg.Username, cfg.Password);
                }
                else
                {
                    client.UseDefaultCredentials = true;
                }
                client.Send(mail);
            }
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

        public bool IsConfigured => !string.IsNullOrWhiteSpace(FromAddress) && !string.IsNullOrWhiteSpace(SmtpHost);

        private const string IniSection = "Mail";

        public static MailSettings Load()
        {
            var s = new MailSettings();
            try
            {
                s.FromAddress = IniHelper.ReadValue(IniSection, "FromAddress", AppSettings.IniPath);
                s.FromDisplayName = IniHelper.ReadValue(IniSection, "FromDisplayName", AppSettings.IniPath);
                s.SmtpHost = IniHelper.ReadValue(IniSection, "SmtpHost", AppSettings.IniPath);
                int port;
                if (int.TryParse(IniHelper.ReadValue(IniSection, "SmtpPort", AppSettings.IniPath), out port)) s.SmtpPort = port;
                bool ssl;
                if (bool.TryParse(IniHelper.ReadValue(IniSection, "EnableSsl", AppSettings.IniPath), out ssl)) s.EnableSsl = ssl;
                s.Username = IniHelper.ReadValue(IniSection, "Username", AppSettings.IniPath);
                s.Password = IniHelper.ReadValue(IniSection, "Password", AppSettings.IniPath);
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                IniHelper.WriteValue(IniSection, "FromAddress", FromAddress ?? string.Empty, AppSettings.IniPath);
                IniHelper.WriteValue(IniSection, "FromDisplayName", FromDisplayName ?? string.Empty, AppSettings.IniPath);
                IniHelper.WriteValue(IniSection, "SmtpHost", SmtpHost ?? string.Empty, AppSettings.IniPath);
                IniHelper.WriteValue(IniSection, "SmtpPort", SmtpPort.ToString(), AppSettings.IniPath);
                IniHelper.WriteValue(IniSection, "EnableSsl", EnableSsl ? "true" : "false", AppSettings.IniPath);
                IniHelper.WriteValue(IniSection, "Username", Username ?? string.Empty, AppSettings.IniPath);
                IniHelper.WriteValue(IniSection, "Password", Password ?? string.Empty, AppSettings.IniPath);
            }
            catch { }
        }
    }
}
