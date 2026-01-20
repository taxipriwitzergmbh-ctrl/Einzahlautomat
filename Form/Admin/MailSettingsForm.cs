using System;
using System.Drawing;
using System.Net;
using System.Net.Mail;
using System.Windows.Forms;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Linq;

namespace Geldautomat
{
    public class MailSettingsForm : Form
    {
        private ComboBox cboAccount;          // Dienstkonto für Fehler/Support
        private ComboBox cboAccount2;         // Dienstkonto für Dokumente/Quittungen/Zeiterfassung
        private TextBox txtFromName1;         // Absendername Dienstkonto 1
        private TextBox txtFromName2;         // Absendername Dienstkonto 2
        private Button btnSave;
        private Button btnCancel;
        private Button btnTest;
        private TextBox txtAlertEmail;
        private CheckBox chkDisableSupport; // nur sichtbar bei Entwickler-Admin (mpr)

        // Zwischenspeicher geladener Dienstkonten
        private List<DatabaseHelper.DienstkontoInfo> _konten;

        // Simple DPAPI helpers
        private static string EncryptSecret(string plain)
        {
            try
            {
                if (string.IsNullOrEmpty(plain)) return string.Empty;
                var data = Encoding.UTF8.GetBytes(plain);
                var protectedBytes = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
                return "enc:" + Convert.ToBase64String(protectedBytes);
            }
            catch { return plain ?? string.Empty; }
        }
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

        public MailSettingsForm()
        {
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(560, 320);
            Text = "Maileinstellungen";

            int xLabel = 20, xField = 230, wField = 300; int y = 20, h = 24, gap = 10;

            var lblAccount = new Label { Text = "Dienstkonto (Fehler/Support)", Location = new Point(xLabel, y), Size = new Size(200, h) };
            cboAccount = new ComboBox { Location = new Point(xField, y), Size = new Size(wField, h), DropDownStyle = ComboBoxStyle.DropDownList };
            y += h + gap;

            var lblFromName1 = new Label { Text = "Absendername (Fehler/Support)", Location = new Point(xLabel, y), Size = new Size(200, h) };
            txtFromName1 = new TextBox { Location = new Point(xField, y), Size = new Size(wField, h) };
            y += h + gap + 6;

            var lblAccount2 = new Label { Text = "Dienstkonto (Dok./Quitt./Zeit)", Location = new Point(xLabel, y), Size = new Size(200, h) };
            cboAccount2 = new ComboBox { Location = new Point(xField, y), Size = new Size(wField, h), DropDownStyle = ComboBoxStyle.DropDownList };
            y += h + gap;

            var lblFromName2 = new Label { Text = "Absendername (Dok./Quitt./Zeit)", Location = new Point(xLabel, y), Size = new Size(200, h) };
            txtFromName2 = new TextBox { Location = new Point(xField, y), Size = new Size(wField, h) };
            y += h + gap + 6;

            var lblAlert = new Label { Text = "Fehler-Mail an", Location = new Point(xLabel, y), Size = new Size(200, h) };
            txtAlertEmail = new TextBox { Location = new Point(xField, y), Size = new Size(wField, h) };
            y += h + gap;

            chkDisableSupport = new CheckBox { Text = "Mailversand an Support unterbinden", Location = new Point(xLabel, y), Size = new Size(470, h) };
            y += h + gap + 8;

            btnTest = new Button { Text = "Verbindung prüfen", Location = new Point(xLabel, y), Size = new Size(180, 30) };
            btnSave = new Button { Text = "Speichern", Location = new Point(xField, y), Size = new Size(120, 30) };
            btnCancel = new Button { Text = "Abbrechen", Location = new Point(xField + 130, y), Size = new Size(120, 30) };

            btnTest.Click += async (s, e) => await TestConnectionAsync();
            btnSave.Click += (s, e) => SaveSettings();
            btnCancel.Click += (s, e) => Close();

            Controls.AddRange(new Control[] { lblAccount, cboAccount, lblFromName1, txtFromName1, lblAccount2, cboAccount2, lblFromName2, txtFromName2, lblAlert, txtAlertEmail, chkDisableSupport, btnTest, btnSave, btnCancel });

            LoadSettingsAsync();

            // Sichtbarkeit nur bei Entwickler-Admin (mpr)
            try
            {
                var flag = IniHelper.ReadValue("Session", "DeveloperAdmin", AppSettings.IniPath);
                bool isDev = !string.IsNullOrWhiteSpace(flag) && (flag == "1" || flag.Equals("true", StringComparison.OrdinalIgnoreCase));
                chkDisableSupport.Visible = isDev;
            }
            catch { chkDisableSupport.Visible = false; }
        }

        private async void LoadSettingsAsync()
        {
            try
            {
                // Dienstkonten aus DB laden
                using (var db = new DatabaseHelper())
                {
                    _konten = await db.GetDienstkontenAsync().ConfigureAwait(false);
                }
                cboAccount.Items.Clear();
                cboAccount2.Items.Clear();
                if (_konten != null)
                {
                    foreach (var k in _konten)
                    {
                        cboAccount.Items.Add(k);
                        cboAccount2.Items.Add(k);
                    }
                    cboAccount.DisplayMember = nameof(DatabaseHelper.DienstkontoInfo.Name);
                    cboAccount2.DisplayMember = nameof(DatabaseHelper.DienstkontoInfo.Name);
                }

                string ini = AppSettings.IniPath;
                // Vorauswahl aus INI (Dienstkonto-IDs)
                int dk1 = 0; int.TryParse(IniHelper.ReadValue("Mail", "DienstkontoID", ini), out dk1);
                int dk2 = 0; int.TryParse(IniHelper.ReadValue("Mail", "Dienstkonto2ID", ini), out dk2);
                if (dk1 > 0 && _konten != null)
                {
                    var sel1 = _konten.Find(x => x.ID == dk1);
                    if (sel1 != null) cboAccount.SelectedItem = sel1;
                }
                if (cboAccount.SelectedIndex < 0 && cboAccount.Items.Count > 0) cboAccount.SelectedIndex = 0;
                if (dk2 > 0 && _konten != null)
                {
                    var sel2 = _konten.Find(x => x.ID == dk2);
                    if (sel2 != null) cboAccount2.SelectedItem = sel2;
                }
                if (cboAccount2.SelectedIndex < 0 && cboAccount2.Items.Count > 0) cboAccount2.SelectedIndex = 0;

                txtFromName1.Text = IniHelper.ReadValue("Mail", "DienstkontoFromName", ini) ?? string.Empty;
                txtFromName2.Text = IniHelper.ReadValue("Mail", "Dienstkonto2FromName", ini) ?? string.Empty;

                txtAlertEmail.Text = IniHelper.ReadValue("Mail", "AlertEmail", ini) ?? string.Empty;
                bool disableSupport;
                chkDisableSupport.Checked = bool.TryParse(IniHelper.ReadValue("Mail", "DisableSupportMail", ini), out disableSupport) ? disableSupport : false;
            }
            catch { }
        }

        private void SaveSettings()
        {
            try
            {
                string ini = AppSettings.IniPath;
                var sel1 = cboAccount.SelectedItem as DatabaseHelper.DienstkontoInfo;
                var sel2 = cboAccount2.SelectedItem as DatabaseHelper.DienstkontoInfo;
                int id1 = sel1 != null ? sel1.ID : 0;
                int id2 = sel2 != null ? sel2.ID : 0;
                IniHelper.WriteValue("Mail", "DienstkontoID", id1.ToString(), ini);
                IniHelper.WriteValue("Mail", "Dienstkonto2ID", id2.ToString(), ini);
                IniHelper.WriteValue("Mail", "DienstkontoFromName", (txtFromName1.Text ?? string.Empty).Trim(), ini);
                IniHelper.WriteValue("Mail", "Dienstkonto2FromName", (txtFromName2.Text ?? string.Empty).Trim(), ini);
                IniHelper.WriteValue("Mail", "AlertEmail", txtAlertEmail.Text?.Trim() ?? string.Empty, ini);
                if (chkDisableSupport.Visible)
                    IniHelper.WriteValue("Mail", "DisableSupportMail", chkDisableSupport.Checked ? "True" : "False", ini);
                MessageBox.Show(this, "Maileinstellungen gespeichert.", "Mail", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Speichern fehlgeschlagen:\r\n" + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string BuildDetailedError(Exception ex)
        {
            var sb = new StringBuilder();
            int depth = 0;
            Exception cur = ex;
            while (cur != null && depth < 6)
            {
                sb.AppendLine((depth == 0 ? "Fehler:" : "Inner:") + " " + cur.GetType().FullName + ": " + cur.Message);
                if (cur is SmtpException smt) sb.AppendLine("SmtpStatusCode: " + smt.StatusCode);
                if (cur is SocketException sox) sb.AppendLine("SocketError: " + sox.SocketErrorCode + " (" + sox.ErrorCode + ")");
                cur = cur.InnerException; depth++;
            }
            sb.AppendLine();
            sb.AppendLine("StackTrace:");
            sb.AppendLine(ex.ToString());
            return sb.ToString().TrimEnd();
        }

        private static string ResolveHostInfo(string host)
        {
            try
            {
                var ips = Dns.GetHostAddresses(host).Select(ip => ip.ToString()).ToArray();
                return string.Join(", ", ips);
            }
            catch { return "(DNS-Auflösung fehlgeschlagen)"; }
        }

        private static (bool ok, string note) TryTcpConnect(string host, int port, int timeoutMs)
        {
            try
            {
                using (var tcp = new TcpClient())
                {
                    var task = tcp.ConnectAsync(host, port);
                    if (!task.Wait(timeoutMs)) return (false, "TCP Connect Timeout");
                    if (tcp.Connected) return (true, "TCP Connected");
                    return (false, "TCP Not connected");
                }
            }
            catch (Exception ex) { return (false, ex.GetType().Name + ": " + ex.Message); }
        }

        private async Task TestConnectionAsync()
        {
            try
            {
                var sel = cboAccount.SelectedItem as DatabaseHelper.DienstkontoInfo;
                if (sel == null) { MessageBox.Show(this, "Bitte ein Dienstkonto auswählen.", "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                var ask = MessageBox.Show(this, "Es wird eine Testmail an die Absender-Adresse des Dienstkontos gesendet. Fortfahren?", "Verbindung prüfen", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (ask != DialogResult.Yes) return;

                Cursor prev = Cursor.Current; Cursor.Current = Cursors.WaitCursor; btnTest.Enabled = false; btnSave.Enabled = false; btnCancel.Enabled = false;
                try
                {
                    try { ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12; } catch { }

                    // Einige Provider (z.B. Strato) verlangen, dass der From/Envelope-Absender exakt dem Login entspricht
                    var loginAddr = sel.Benutzername ?? string.Empty;
                    var fromAddr = !string.IsNullOrWhiteSpace(loginAddr) ? loginAddr : (sel.Absender ?? string.Empty);
                    var displayName = (txtFromName1.Text ?? string.Empty).Trim();
                    var from = new MailAddress(fromAddr, string.IsNullOrWhiteSpace(displayName) ? sel.Name : displayName, Encoding.UTF8);
                    var to = new MailAddress(fromAddr);
                    int port = 25; int.TryParse(sel.Port, out port);

                    // Preflight diagnostics
                    var dnsInfo = ResolveHostInfo(sel.Host);
                    var tcp = TryTcpConnect(sel.Host, port, 7000);

                    using (var msg = new MailMessage())
                    {
                        // Strikter Aufbau: Absender/Empfänger
                        msg.From = from;
                        msg.To.Add(to);
                        // Kein Sender setzen – manche Server (Strato) lehnen ab, wenn Sender != From
                        // msg.Sender = from;
                        msg.Subject = "Testverbindung Geldautomat";
                        msg.SubjectEncoding = Encoding.UTF8;
                        string device = (AppSettings.AutomatenName ?? string.Empty).Trim();
                        msg.Body = "Dies ist eine Testnachricht zur Überprüfung der SMTP-Verbindung.\r\nGerät: " + device + "\r\nZeit: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
                        msg.BodyEncoding = Encoding.UTF8;

                        string decryptedPwd = EmailReceiptService.TinyDecrypt(sel.Passwort ?? string.Empty);

                        bool TrySend(int usePort, out Exception error)
                        {
                            error = null;
                            try
                            {
                                using (var client = new SmtpClient(sel.Host, usePort))
                                {
                                    client.DeliveryMethod = SmtpDeliveryMethod.Network;
                                    client.Timeout = 30000;
                                    // Immer explizite Credentials verwenden
                                    client.UseDefaultCredentials = false;
                                    client.Credentials = new NetworkCredential(loginAddr, decryptedPwd);

                                    // STARTTLS für 587, Implicit SSL für 465 – SmtpClient verwaltet das intern über EnableSsl
                                    client.EnableSsl = (usePort == 465 || usePort == 587);

                                    client.Send(msg);
                                    return true;
                                }
                            }
                            catch (Exception ex)
                            {
                                error = ex; return false;
                            }
                        }

                        // Bevorzugt 587 (STARTTLS), danach 465 (Implicit SSL), zuletzt konfigurierter Port
                        Exception lastError = null;
                        int[] portsToTry = new int[]
                        {
                            587,
                            465,
                            port
                        };
                        foreach (var p in portsToTry.Distinct())
                        {
                            if (TrySend(p, out lastError))
                            {
                                MessageBox.Show(this, "Verbindung OK – Testmail wurde gesendet.\r\nDNS: " + dnsInfo + "\r\nTCP: " + tcp.note + "\r\nPort verwendet: " + p, "Erfolg", MessageBoxButtons.OK, MessageBoxIcon.Information);
                                lastError = null; break;
                            }
                        }

                        if (lastError != null)
                        {
                            var details = BuildDetailedError(lastError);
                            MessageBox.Show(this,
                                "Verbindung fehlgeschlagen:\r\n" + details +
                                "\r\rVerwendete Zugangsdaten:" +
                                "\rSMTP-Host: " + sel.Host +
                                "\rDNS-IP(s): " + dnsInfo +
                                "\rTCP-Check: " + tcp.note +
                                "\rPort (versucht): " + string.Join(", ", portsToTry.Distinct()) +
                                "\rSSL: auto (587 STARTTLS, 465 Implicit)" +
                                "\rBenutzer: " + (loginAddr ?? string.Empty) +
                                "\rFrom: " + fromAddr +
                                "\rPasswort (entschlüsselt): " + (string.IsNullOrEmpty(decryptedPwd) ? "(leer)" : decryptedPwd),
                                "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            try { AppLogger.Log("Mail-Test fehlgeschlagen: " + lastError.ToString()); } catch { }
                        }
                    }
                }
                finally { Cursor.Current = prev; btnTest.Enabled = true; btnSave.Enabled = true; btnCancel.Enabled = true; }
            }
            catch (Exception ex) { MessageBox.Show(this, "Unerwarteter Fehler:\r\n" + ex.ToString(), "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }
}
