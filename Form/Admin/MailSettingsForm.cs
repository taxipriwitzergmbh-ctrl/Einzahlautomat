using System;
using System.Drawing;
using System.Net;
using System.Net.Mail;
using System.Windows.Forms;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Geldautomat
{
    public class MailSettingsForm : Form
    {
        private ComboBox cboAccount;
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
            ClientSize = new Size(520, 240);
            Text = "Maileinstellungen";

            var lblAccount = new Label { Text = "Dienstkonto", Location = new Point(20, 20), Size = new Size(200, 24) };
            cboAccount = new ComboBox { Location = new Point(230, 20), Size = new Size(260, 24), DropDownStyle = ComboBoxStyle.DropDownList };

            var lblAlert = new Label { Text = "Fehler-Mail an", Location = new Point(20, 60), Size = new Size(200, 24) };
            txtAlertEmail = new TextBox { Location = new Point(230, 60), Size = new Size(260, 24) };

            chkDisableSupport = new CheckBox { Text = "Mailversand an Support unterbinden", Location = new Point(20, 100), Size = new Size(470, 24) };

            btnTest = new Button { Text = "Verbindung prüfen", Location = new Point(20, 140), Size = new Size(180, 30) };
            btnSave = new Button { Text = "Speichern", Location = new Point(230, 140), Size = new Size(120, 30) };
            btnCancel = new Button { Text = "Abbrechen", Location = new Point(370, 140), Size = new Size(120, 30) };

            btnTest.Click += async (s, e) => await TestConnectionAsync();
            btnSave.Click += (s, e) => SaveSettings();
            btnCancel.Click += (s, e) => Close();

            Controls.AddRange(new Control[] { lblAccount, cboAccount, lblAlert, txtAlertEmail, chkDisableSupport, btnTest, btnSave, btnCancel });

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
                if (_konten != null)
                {
                    foreach (var k in _konten)
                        cboAccount.Items.Add(k);
                    cboAccount.DisplayMember = nameof(DatabaseHelper.DienstkontoInfo.Name);
                }

                string ini = AppSettings.IniPath;
                // Vorauswahl aus INI (Dienstkonto-ID)
                int dkId = 0; int.TryParse(IniHelper.ReadValue("Mail", "DienstkontoID", ini), out dkId);
                if (dkId > 0 && _konten != null)
                {
                    var sel = _konten.Find(x => x.ID == dkId);
                    if (sel != null) cboAccount.SelectedItem = sel;
                }
                if (cboAccount.SelectedIndex < 0 && cboAccount.Items.Count > 0) cboAccount.SelectedIndex = 0;

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
                var sel = cboAccount.SelectedItem as DatabaseHelper.DienstkontoInfo;
                int id = sel != null ? sel.ID : 0;
                IniHelper.WriteValue("Mail", "DienstkontoID", id.ToString(), ini);
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

        private async Task TestConnectionAsync()
        {
            try
            {
                var sel = cboAccount.SelectedItem as DatabaseHelper.DienstkontoInfo;
                if (sel == null)
                {
                    MessageBox.Show(this, "Bitte ein Dienstkonto auswählen.", "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var ask = MessageBox.Show(this, "Es wird eine Testmail an die Absender-Adresse des Dienstkontos gesendet. Fortfahren?", "Verbindung prüfen", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (ask != DialogResult.Yes) return;

                Cursor prev = Cursor.Current;
                Cursor.Current = Cursors.WaitCursor;
                btnTest.Enabled = false; btnSave.Enabled = false; btnCancel.Enabled = false;
                try
                {
                    var from = new MailAddress(sel.Absender ?? sel.Benutzername ?? string.Empty, sel.Name);
                    var to = new MailAddress(sel.Absender ?? sel.Benutzername ?? string.Empty);
                    using (var msg = new MailMessage(from, to))
                    {
                        msg.Subject = "Testverbindung Geldautomat";
                        string device = (AppSettings.AutomatenName ?? string.Empty).Trim();
                        msg.Body = "Dies ist eine Testnachricht zur Überprüfung der SMTP-Verbindung.\r\nGerät: " + device + "\r\nZeit: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
                        int port = 25;
                        int.TryParse(sel.Port, out port);
                        using (var client = new SmtpClient(sel.Host, port))
                        {
                            // SSL abhängig von Typ (optional: Typ==1 -> SSL). Standard: SSL an.
                            bool ssl = true;
                            if (sel.Typ.HasValue) ssl = sel.Typ.Value != 0;
                            client.EnableSsl = ssl;
                            if (!string.IsNullOrWhiteSpace(sel.Benutzername))
                                client.Credentials = new NetworkCredential(sel.Benutzername, sel.Passwort);
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
