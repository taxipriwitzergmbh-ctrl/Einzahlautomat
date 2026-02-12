using System;
using System.Drawing;
using System.Windows.Forms;
using TaMi_Einzahlautomat.UI.Layout;

namespace TaMi_Einzahlautomat
{
    public partial class AdminNV200Form : Form
    {
        private Panel _panelMaxConfig;
        private NumericUpDown[] _nudMax = new NumericUpDown[7];
        private Button _btnMaxLoad;
        private Button _btnMaxSave;
        private Button _btnMaxClose;
        private Label _lblMaxInfo;

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            try
            {
                InitMaxConfigUi();
                LoadMaxFromIniToUi();
            }
            catch { /* UI darf nicht crashen */ }
        }

        private void InitMaxConfigUi()
        {
            if (_panelMaxConfig != null) return;

            // Panel rechts, wie Status-/Bestandsbereich, initial verborgen
            _panelMaxConfig = new Panel
            {
                Left = 850,                 // rechte Spalte
                Top = 700,                  // unterhalb Bestand/Buttons
                Width = 380,
                Height = 300,
                BackColor = Color.FromArgb(245, 247, 250),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Visible = false
            };
            Controls.Add(_panelMaxConfig);

            // Header
            var lblHeader = new Label
            {
                Text = "Payout-Zielbest�nde",
                Left = 10,
                Top = 10,
                AutoSize = true,
                Font = new Font("Segoe UI Variable", 13F, FontStyle.Bold),
                ForeColor = Color.FromArgb(33, 37, 41)
            };
            _panelMaxConfig.Controls.Add(lblHeader);

            // Close-Button (�)
            _btnMaxClose = new Button
            {
                Text = "�",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = Color.FromArgb(66, 66, 66),
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(32, 28),
                Left = _panelMaxConfig.Width - 42,
                Top = 8,
                TabStop = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            _btnMaxClose.FlatAppearance.BorderSize = 0;
            _btnMaxClose.FlatAppearance.MouseOverBackColor = Color.FromArgb(230, 230, 230);
            _btnMaxClose.Click += (s, e) =>
            {
                _panelMaxConfig.Visible = false;
                
            };
            _panelMaxConfig.Controls.Add(_btnMaxClose);

            // Trennerlinie
            var sep = new Panel
            {
                Left = 10,
                Top = 42,
                Width = _panelMaxConfig.Width - 20,
                Height = 1,
                BackColor = Color.FromArgb(220, 225, 230),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            _panelMaxConfig.Controls.Add(sep);

            // Labels + NumericUpDowns in einer �Grid�-Anordnung
            var labels = new[] { "5 �", "10 �", "20 �", "50 �", "100 �", "200 �", "500 �" };
            int baseLeft = 20;
            int baseTop = 56;
            int rowH = 30;

            for (int i = 0; i < 7; i++)
            {
                var lbl = new Label
                {
                    Left = baseLeft,
                    Top = baseTop + i * rowH,
                    Width = 80,
                    Text = labels[i],
                    Font = new Font("Segoe UI Variable", 11F, FontStyle.Regular),
                    ForeColor = Color.FromArgb(33, 37, 41)
                };
                _panelMaxConfig.Controls.Add(lbl);

                var nud = new NumericUpDown
                {
                    Left = baseLeft + 90,
                    Top = baseTop + i * rowH - 2,
                    Width = 100,
                    Minimum = 0,
                    Maximum = 65535,
                    Font = new Font("Segoe UI Variable", 11F, FontStyle.Bold),
                    TextAlign = HorizontalAlignment.Right
                };
                _panelMaxConfig.Controls.Add(nud);
                _nudMax[i] = nud;
            }

            // Buttons: Laden/Speichern
            _btnMaxLoad = new ModernGradientButton
            {
                Text = "Laden",
                Left = baseLeft + 210,
                Top = baseTop,
                Width = 130,
                Height = 30,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            ((ModernGradientButton)_btnMaxLoad).GradientStart = UiTheme.PrimaryStart;
            ((ModernGradientButton)_btnMaxLoad).GradientEnd = UiTheme.PrimaryEnd;
            _btnMaxLoad.Click += (s, e) => LoadMaxFromIniToUi();
            _panelMaxConfig.Controls.Add(_btnMaxLoad);

            _btnMaxSave = new ModernGradientButton
            {
                Text = "Speichern",
                Left = baseLeft + 210,
                Top = baseTop + 36,
                Width = 130,
                Height = 30,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            ((ModernGradientButton)_btnMaxSave).GradientStart = UiTheme.SuccessStart;
            ((ModernGradientButton)_btnMaxSave).GradientEnd = UiTheme.SuccessEnd;
            _btnMaxSave.Click += (s, e) => SaveMaxFromUiToIniAndApply();
            _panelMaxConfig.Controls.Add(_btnMaxSave);

            // Info
            _lblMaxInfo = new Label
            {
                Left = baseLeft + 210,
                Top = baseTop + 76,
                Width = 150,
                Height = 80,
                ForeColor = Color.DimGray,
                Font = new Font("Segoe UI Variable", 9.5F, FontStyle.Regular),
                Text = "Hinweis:\r\n- Nach Speichern werden\r\n  Routen neu gesetzt."
            };
            _panelMaxConfig.Controls.Add(_lblMaxInfo);

            // Voreinstellung aus der aktuellen Session anzeigen
            try
            {
                _nudMax[0].Value = Clamp(_ssp.MAX_PayoutCount_of_5Euro);
                _nudMax[1].Value = Clamp(_ssp.MAX_PayoutCount_of_10Euro);
                _nudMax[2].Value = Clamp(_ssp.MAX_PayoutCount_of_20Euro);
                _nudMax[3].Value = Clamp(_ssp.MAX_PayoutCount_of_50Euro);
                _nudMax[4].Value = Clamp(_ssp.MAX_PayoutCount_of_100Euro);
                _nudMax[5].Value = Clamp(_ssp.MAX_PayoutCount_of_200Euro);
                _nudMax[6].Value = Clamp(_ssp.MAX_PayoutCount_of_500Euro);
            }
            catch { }
        }

        private static decimal Clamp(int v)
        {
            if (v < 0) return 0;
            if (v > 65535) return 65535;
            return v;
        }

        private string DetermineNvSection()
        {
            try
            {
                string port = _ssp?.ComPort ?? "";
                string p1 = IniHelper.ReadValue("NV200/1", "ComPort", AppSettings.IniPath);
                string p2 = IniHelper.ReadValue("NV200/2", "ComPort", AppSettings.IniPath);

                if (!string.IsNullOrWhiteSpace(port) && !string.IsNullOrWhiteSpace(p1) &&
                    string.Equals(port, p1, StringComparison.OrdinalIgnoreCase))
                    return "NV200/1";

                if (!string.IsNullOrWhiteSpace(port) && !string.IsNullOrWhiteSpace(p2) &&
                    string.Equals(port, p2, StringComparison.OrdinalIgnoreCase))
                    return "NV200/2";
            }
            catch { }
            return "NV200";
        }

        private void LoadMaxFromIniToUi()
        {
            string section = DetermineNvSection();

            int ReadMax(string key, int current)
            {
                try
                {
                    var s = IniHelper.ReadValue(section, key, AppSettings.IniPath);
                    if (int.TryParse(s, out var v)) return v;
                }
                catch { }
                return current;
            }

            try
            {
                _nudMax[0].Value = Clamp(ReadMax("Max_5", _ssp.MAX_PayoutCount_of_5Euro));
                _nudMax[1].Value = Clamp(ReadMax("Max_10", _ssp.MAX_PayoutCount_of_10Euro));
                _nudMax[2].Value = Clamp(ReadMax("Max_20", _ssp.MAX_PayoutCount_of_20Euro));
                _nudMax[3].Value = Clamp(ReadMax("Max_50", _ssp.MAX_PayoutCount_of_50Euro));
                _nudMax[4].Value = Clamp(ReadMax("Max_100", _ssp.MAX_PayoutCount_of_100Euro));
                _nudMax[5].Value = Clamp(ReadMax("Max_200", _ssp.MAX_PayoutCount_of_200Euro));
                _nudMax[6].Value = Clamp(ReadMax("Max_500", _ssp.MAX_PayoutCount_of_500Euro));
            }
            catch { }
        }

        private void SaveMaxFromUiToIniAndApply()
        {
            string section = DetermineNvSection();

            void WriteMax(string key, decimal value)
            {
                try { IniHelper.WriteValue(section, key, ((int)value).ToString(), AppSettings.IniPath); } catch { }
            }

            try
            {
                // 1) In INI schreiben
                WriteMax("Max_5", _nudMax[0].Value);
                WriteMax("Max_10", _nudMax[1].Value);
                WriteMax("Max_20", _nudMax[2].Value);
                WriteMax("Max_50", _nudMax[3].Value);
                WriteMax("Max_100", _nudMax[4].Value);
                WriteMax("Max_200", _nudMax[5].Value);
                WriteMax("Max_500", _nudMax[6].Value);

                // 2) In laufender Session anwenden
                _ssp.MAX_PayoutCount_of_5Euro = (int)_nudMax[0].Value;
                _ssp.MAX_PayoutCount_of_10Euro = (int)_nudMax[1].Value;
                _ssp.MAX_PayoutCount_of_20Euro = (int)_nudMax[2].Value;
                _ssp.MAX_PayoutCount_of_50Euro = (int)_nudMax[3].Value;
                _ssp.MAX_PayoutCount_of_100Euro = (int)_nudMax[4].Value;
                _ssp.MAX_PayoutCount_of_200Euro = (int)_nudMax[5].Value;
                _ssp.MAX_PayoutCount_of_500Euro = (int)_nudMax[6].Value;

                // 3) Routen neu setzen (Mehrmengen gehen in Cashbox)
                _ssp.Set_Routing();
                _ssp.Ereignis_adden($"Max-Zielbest�nde aktualisiert ({section}) und Routen neu gesetzt.");
                MessageBox.Show(this, "Einstellungen gespeichert und angewendet.", "OK", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Fehler beim Speichern:\r\n" + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}