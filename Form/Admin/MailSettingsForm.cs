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
        private Button btnTest; // NEU: Verbindung prüfen

        public MailSettingsForm()
        {
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(520, 360);
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

            btnTest = new Button { Text = "Verbindung prüfen", Location = new Point(20, 300), Size = new Size(180, 30) };
            btnSave = new Button { Text = "Speichern", Location = new Point(230, 300), Size = new Size(120, 30) };
            btnCancel = new Button { Text = "Abbrechen", Location = new Point(370, 300), Size = new Size(120, 30) };

            btnTest.Click += (s, e) => TestConnection();
            btnSave.Click += (s, e) => SaveSettings();
            btnCancel.Click += (s, e) => Close();

            Controls.AddRange(new Control[] { lblFrom, txtFrom, lblDisplay, txtDisplayName, lblHost, txtHost, lblPort, nudPort, chkSsl, lblUser, txtUser, lblPass, txtPassword, btnTest, btnSave, btnCancel });

            LoadSettings();
        }

        private void LoadSettings()
        {
            try
            {
                var s = MailSettings.Load();
                txtFrom.Text = s.FromAddress ?? string.Empty;
                txtDisplayName.Text = s.FromDisplayName ?? string.Empty;
                txtHost.Text = s.SmtpHost ?? string.Empty;
                nudPort.Value = s.SmtpPort > 0 ? s.SmtpPort : 587;
                chkSsl.Checked = s.EnableSsl;
                txtUser.Text = s.Username ?? string.Empty;
                txtPassword.Text = s.Password ?? string.Empty;
            }
            catch { }
        }

        private void SaveSettings()
        {
            try
            {
                var s = new MailSettings
                {
                    FromAddress = txtFrom.Text?.Trim(),
                    FromDisplayName = txtDisplayName.Text?.Trim(),
                    SmtpHost = txtHost.Text?.Trim(),
                    SmtpPort = (int)nudPort.Value,
                    EnableSsl = chkSsl.Checked,
                    Username = txtUser.Text?.Trim(),
                    Password = txtPassword.Text
                };
                s.Save();
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

                // Hinweis: Es wird eine Testmail gesendet
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
                        msg.Body = "Dies ist eine Testnachricht zur Überprüfung der SMTP-Verbindung.\r\nZeit: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
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
