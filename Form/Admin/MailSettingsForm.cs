using System;
using System.Drawing;
using System.Net;
using System.Net.Mail;
using System.Windows.Forms;

namespace Geldautomat
{
    public class MailSettingsForm : Form
    {
        private TextBox txtFrom;
        private TextBox txtDisplayName;
        private TextBox txtHost;
        private NumericUpDown nudPort;
        private CheckBox chkSsl;
        private TextBox txtUser;
        private TextBox txtPassword;
        private Button btnSave;
        private Button btnCancel;
        private Button btnTest;
        private TextBox txtAlertEmail;
        private CheckBox chkDisableSupport; // nur sichtbar bei Entwickler-Admin (mpr)

        public MailSettingsForm()
        {
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(520, 440);
            Text = "Maileinstellungen";

            var lblFrom = new Label { Text = "Absender-Adresse", Location = new Point(20, 20), Size = new Size(200, 24) };
            txtFrom = new TextBox { Location = new Point(230, 20), Size = new Size(260, 24) };

            var lblDisplay = new Label { Text = "Absender-Name", Location = new Point(20, 60), Size = new Size(200, 24) };
            txtDisplayName = new TextBox { Location = new Point(230, 60), Size = new Size(260, 24) };

            var lblHost = new Label { Text = "SMTP-Host", Location = new Point(20, 100), Size = new Size(200, 24) };
            txtHost = new TextBox { Location = new Point(230, 100), Size = new Size(260, 24) };

            var lblPort = new Label { Text = "SMTP-Port", Location = new Point(20, 140), Size = new Size(200, 24) };
            nudPort = new NumericUpDown { Location = new Point(230, 140), Size = new Size(120, 24), Minimum = 1, Maximum = 65535, Value = 587 };

            chkSsl = new CheckBox { Text = "SSL aktivieren", Location = new Point(230, 180), Size = new Size(200, 24) };

            var lblUser = new Label { Text = "Benutzername", Location = new Point(20, 220), Size = new Size(200, 24) };
            txtUser = new TextBox { Location = new Point(230, 220), Size = new Size(260, 24) };

            var lblPass = new Label { Text = "Passwort", Location = new Point(20, 260), Size = new Size(200, 24) };
            txtPassword = new TextBox { Location = new Point(230, 260), Size = new Size(260, 24), UseSystemPasswordChar = true };

            var lblAlert = new Label { Text = "Fehler-Mail an", Location = new Point(20, 300), Size = new Size(200, 24) };
            txtAlertEmail = new TextBox { Location = new Point(230, 300), Size = new Size(260, 24) };

            chkDisableSupport = new CheckBox { Text = "Mailversand an Support unterbinden", Location = new Point(20, 330), Size = new Size(470, 24) };

            btnTest = new Button { Text = "Verbindung prüfen", Location = new Point(20, 370), Size = new Size(180, 30) };
            btnSave = new Button { Text = "Speichern", Location = new Point(230, 370), Size = new Size(120, 30) };
            btnCancel = new Button { Text = "Abbrechen", Location = new Point(370, 370), Size = new Size(120, 30) };

            btnTest.Click += (s, e) => TestConnection();
            btnSave.Click += (s, e) => SaveSettings();
            btnCancel.Click += (s, e) => Close();

            Controls.AddRange(new Control[] { lblFrom, txtFrom, lblDisplay, txtDisplayName, lblHost, txtHost, lblPort, nudPort, chkSsl, lblUser, txtUser, lblPass, txtPassword, lblAlert, txtAlertEmail, chkDisableSupport, btnTest, btnSave, btnCancel });

            LoadSettings();

            // Sichtbarkeit nur bei Entwickler-Admin (mpr)
            try
            {
                var flag = IniHelper.ReadValue("Session", "DeveloperAdmin", AppSettings.IniPath);
                bool isDev = !string.IsNullOrWhiteSpace(flag) && (flag == "1" || flag.Equals("true", StringComparison.OrdinalIgnoreCase));
                chkDisableSupport.Visible = isDev;
            }
            catch { chkDisableSupport.Visible = false; }
        }

        private void LoadSettings()
        {
            try
            {
                string ini = AppSettings.IniPath;
                txtFrom.Text = IniHelper.ReadValue("Mail", "FromAddress", ini) ?? string.Empty;
                txtDisplayName.Text = IniHelper.ReadValue("Mail", "FromDisplayName", ini) ?? string.Empty;
                txtHost.Text = IniHelper.ReadValue("Mail", "SmtpHost", ini) ?? string.Empty;
                int port; txtAlertEmail.Text = IniHelper.ReadValue("Mail", "AlertEmail", ini) ?? string.Empty;
                if (int.TryParse(IniHelper.ReadValue("Mail", "SmtpPort", ini), out port)) nudPort.Value = Math.Max(1, Math.Min(65535, port)); else nudPort.Value = 587;
                bool ssl; chkSsl.Checked = bool.TryParse(IniHelper.ReadValue("Mail", "EnableSsl", ini), out ssl) ? ssl : true;
                txtUser.Text = IniHelper.ReadValue("Mail", "Username", ini) ?? string.Empty;
                txtPassword.Text = IniHelper.ReadValue("Mail", "Password", ini) ?? string.Empty;
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
                IniHelper.WriteValue("Mail", "FromAddress", txtFrom.Text?.Trim() ?? string.Empty, ini);
                IniHelper.WriteValue("Mail", "FromDisplayName", txtDisplayName.Text?.Trim() ?? string.Empty, ini);
                IniHelper.WriteValue("Mail", "SmtpHost", txtHost.Text?.Trim() ?? string.Empty, ini);
                IniHelper.WriteValue("Mail", "SmtpPort", ((int)nudPort.Value).ToString(), ini);
                IniHelper.WriteValue("Mail", "EnableSsl", chkSsl.Checked ? "True" : "False", ini);
                IniHelper.WriteValue("Mail", "Username", txtUser.Text?.Trim() ?? string.Empty, ini);
                IniHelper.WriteValue("Mail", "Password", txtPassword.Text ?? string.Empty, ini);
                IniHelper.WriteValue("Mail", "AlertEmail", txtAlertEmail.Text?.Trim() ?? string.Empty, ini);
                // nur persistieren, wenn Checkbox sichtbar (Entwickler-Admin), sonst unverändert lassen
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

        private void TestConnection()
        {
            try
            {
                string fromAddr = txtFrom.Text?.Trim();
                string host = txtHost.Text?.Trim();
                int port = (int)nudPort.Value;
                bool ssl = chkSsl.Checked;
                string user = txtUser.Text?.Trim();
                string pwd = txtPassword.Text;

                if (string.IsNullOrWhiteSpace(host) || port <= 0)
                {
                    MessageBox.Show(this, "Bitte SMTP-Host und Port angeben.", "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (string.IsNullOrWhiteSpace(fromAddr))
                {
                    MessageBox.Show(this, "Bitte Absender-Adresse angeben.", "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var ask = MessageBox.Show(this, "Es wird eine Testmail an die Absender-Adresse gesendet. Fortfahren?", "Verbindung prüfen", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (ask != DialogResult.Yes) return;

                Cursor prev = Cursor.Current;
                Cursor.Current = Cursors.WaitCursor;
                btnTest.Enabled = false; btnSave.Enabled = false; btnCancel.Enabled = false;
                try
                {
                    var from = new MailAddress(fromAddr, string.IsNullOrWhiteSpace(txtDisplayName.Text) ? null : txtDisplayName.Text.Trim());
                    var to = new MailAddress(fromAddr);
                    using (var msg = new MailMessage(from, to))
                    {
                        msg.Subject = "Testverbindung Geldautomat";
                        string device = (AppSettings.AutomatenName ?? string.Empty).Trim();
                        msg.Body = "Dies ist eine Testnachricht zur Überprüfung der SMTP-Verbindung.\r\nGerät: " + device + "\r\nZeit: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
                        using (var client = new SmtpClient(host, port))
                        {
                            client.EnableSsl = ssl;
                            if (!string.IsNullOrWhiteSpace(user))
                                client.Credentials = new NetworkCredential(user, pwd);
                            else
                                client.UseDefaultCredentials = true;
                            client.Send(msg);
                        }
                    }
                    MessageBox.Show(this, "Verbindung OK – Testmail wurde gesendet.", "Erfolg", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Verbindung fehlgeschlagen:\r\n" + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    Cursor.Current = prev;
                    btnTest.Enabled = true; btnSave.Enabled = true; btnCancel.Enabled = true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Unerwarteter Fehler:\r\n" + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
