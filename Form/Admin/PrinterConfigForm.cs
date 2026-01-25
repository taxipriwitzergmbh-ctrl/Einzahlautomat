using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Drawing.Printing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using TaMi_Einzahlautomat.Printing;
using System.IO.Ports;

namespace TaMi_Einzahlautomat
{
    public class PrinterConfigForm : Form
    {
        private Panel headerPanel; private Label lblTitle; private Button btnClose; private Button btnMin; private Point _dragStart;
        private TabControl _tabs;
        private Panel _customTabHeader; private Button _btnTabWin; private Button _btnTabLegacy; private Button _btnTabDocs;
        private CheckBox chkEnabled, chkAsk, chkVat, chkCut, chkAutoIfNoPrompt; private CheckBox chkUseSecondary; private ComboBox cboPrinters; private ComboBox cboPrinters2; private NumericUpDown nudChars; private Button btnSave, btnCancel, btnTest;
        private CheckBox chkL1Enabled, chkL1Cut, chkL2Enabled, chkL2Cut; private ComboBox cboL1Port, cboL2Port; private NumericUpDown nudL1Baud, nudL2Baud, nudL1Chars, nudL2Chars; private Button btnL1Test, btnL2Test;
        private ComboBox cboDocPrinters; private Button btnDocSave; private Button btnDocTest;
        private CheckBox chkDisableHoursPrint; private CheckBox chkDisableDocumentsPrint;
        private ReceiptPrinterSettings _settings;
        private CheckBox chkShowWorkTimesOnly, chkShowPauseTime, chkAutoPauseDeduction, chkShowArbeitszeit, chkMinimumPauseDeduction;
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

        public PrinterConfigForm()
        {
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.CenterScreen; DoubleBuffered = true; BackColor = Color.White; Font = new Font("Segoe UI", 10F); ClientSize = new Size(860, 620);
            BuildChrome(); BuildTabs(); ForceCustomTabHeader();
            try { lblTitle.Text += " (Tabs-Version)"; } catch { }
            try { AppLogger.Log("[PrinterConfigForm] Tabs-Version (forced custom header)"); } catch { }
            LoadSettings(); try { Region = Region.FromHrgn(CreateRoundRectRgn(0,0,Width,Height,20,20)); } catch { }
            Resize += (s,e)=> AdjustLayout();
            AdjustLayout();
        }

        private void ForceCustomTabHeader()
        {
            try
            {
                if (_tabs == null) return;
                _tabs.Appearance = TabAppearance.FlatButtons; _tabs.ItemSize = new Size(0,1); _tabs.SizeMode = TabSizeMode.Fixed;
                if (_customTabHeader != null) { try { Controls.Remove(_customTabHeader); _customTabHeader.Dispose(); } catch { } _customTabHeader=null; }
                _customTabHeader = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Color.FromArgb(245,247,250) };
                Controls.Add(_customTabHeader);
                Controls.SetChildIndex(headerPanel, 0);
                Controls.SetChildIndex(_customTabHeader, 1);
                Controls.SetChildIndex(_tabs, 2);
                _btnTabWin = MakeTabButton("Windows / Standard", new Point(10,6), ()=> { _tabs.SelectedIndex = 0; UpdateCustomTabButtons(); });
                _btnTabLegacy = MakeTabButton("Legacy / Seriell", new Point(200,6), ()=> { _tabs.SelectedIndex = 1; UpdateCustomTabButtons(); });
                _btnTabDocs = MakeTabButton("Dokumentendrucker", new Point(390,6), ()=> { _tabs.SelectedIndex = 2; UpdateCustomTabButtons(); });
                _customTabHeader.Controls.Add(_btnTabWin); _customTabHeader.Controls.Add(_btnTabLegacy); _customTabHeader.Controls.Add(_btnTabDocs);
                _tabs.SelectedIndexChanged += (s,e)=> UpdateCustomTabButtons();
                UpdateCustomTabButtons();
            }
            catch { }
        }

        private void AdjustLayout()
        {
            try
            {
                int top = (headerPanel?.Height ?? 0) + (_customTabHeader?.Height ?? 0);
                if (_tabs != null)
                {
                    _tabs.Location = new Point(0, top);
                    _tabs.Size = new Size(ClientSize.Width, ClientSize.Height - top);
                    _tabs.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
                }
            }
            catch { }
        }

        private Button MakeTabButton(string text, Point loc, Action onClick)
        { var b = new Button { Text=text, Location=loc, Size=new Size(180,30), FlatStyle=FlatStyle.Flat, BackColor=Color.FromArgb(220,220,220), ForeColor=Color.Black, Font=new Font("Segoe UI",9.5f,FontStyle.Bold), TabStop=false }; b.FlatAppearance.BorderSize = 0; b.Click += (s,e)=> onClick(); return b; }
        private void UpdateCustomTabButtons(){ if (_btnTabWin == null || _btnTabLegacy == null || _btnTabDocs==null || _tabs == null) return; ApplyTabButtonStyle(_btnTabWin, _tabs.SelectedIndex==0); ApplyTabButtonStyle(_btnTabLegacy, _tabs.SelectedIndex==1); ApplyTabButtonStyle(_btnTabDocs, _tabs.SelectedIndex==2); }
        private void ApplyTabButtonStyle(Button b, bool active){ if (b==null) return; b.BackColor = active ? Color.FromArgb(33,150,243) : Color.FromArgb(220,220,220); b.ForeColor = active ? Color.White : Color.Black; }

        private void BuildChrome()
        {
            headerPanel = new Panel { Dock = DockStyle.Top, Height = 62 }; headerPanel.Paint += HeaderPanel_Paint; headerPanel.MouseDown += (s,e)=> { if (e.Button==MouseButtons.Left) _dragStart=e.Location; }; headerPanel.MouseMove += (s,e)=> { if (e.Button==MouseButtons.Left) { Left += e.X - _dragStart.X; Top += e.Y - _dragStart.Y; } }; Controls.Add(headerPanel);
            lblTitle = new Label { Text="Quittungsdrucker", AutoSize=false, Location=new Point(24,0), Size=new Size(400,62), TextAlign=ContentAlignment.MiddleLeft, Font=new Font("Segoe UI Variable",19F,FontStyle.Bold), ForeColor=Color.White, BackColor=Color.Transparent }; headerPanel.Controls.Add(lblTitle);
            btnClose = new Button { Text="\u2715", Font=new Font("Segoe UI",15F,FontStyle.Bold), ForeColor=Color.White, BackColor=Color.Transparent, FlatStyle=FlatStyle.Flat, Size=new Size(50,50), Location=new Point(ClientSize.Width-56,6), TabStop=false }; btnClose.FlatAppearance.BorderSize=0; btnClose.FlatAppearance.MouseOverBackColor=Color.FromArgb(255,80,80); btnClose.Click += (s,e)=> Close(); headerPanel.Controls.Add(btnClose);
            btnMin = new Button { Text="_", Font=new Font("Segoe UI",16F,FontStyle.Bold), ForeColor=Color.White, BackColor=Color.Transparent, FlatStyle=FlatStyle.Flat, Size=new Size(50,50), Location=new Point(ClientSize.Width-112,6), TabStop=false }; btnMin.FlatAppearance.BorderSize=0; btnMin.FlatAppearance.MouseOverBackColor=Color.FromArgb(33,150,243,80); btnMin.Click += (s,e)=> WindowState=FormWindowState.Minimized; headerPanel.Controls.Add(btnMin);
        }
        private void HeaderPanel_Paint(object sender, PaintEventArgs e){ using (var lg = new LinearGradientBrush(headerPanel.ClientRectangle, Color.FromArgb(33,150,243), Color.FromArgb(33,203,243),0f)) e.Graphics.FillRectangle(lg, headerPanel.ClientRectangle); }

        private void BuildTabs(){ _tabs = new TabControl { Dock = DockStyle.None, Appearance = TabAppearance.Normal, Font = new Font("Segoe UI", 10F, FontStyle.Bold) }; Controls.Add(_tabs); Controls.SetChildIndex(_tabs, 2); var tabWin = new TabPage("Windows / Standard") { BackColor = Color.White }; var tabLegacy = new TabPage("Legacy / Seriell") { BackColor = Color.White }; var tabDocs = new TabPage("Dokumentendrucker") { BackColor = Color.White }; _tabs.TabPages.Add(tabWin); _tabs.TabPages.Add(tabLegacy); _tabs.TabPages.Add(tabDocs); BuildWindowsTab(tabWin); BuildLegacyTab(tabLegacy); BuildDocumentsTab(tabDocs); }

        private void BuildWindowsTab(TabPage parent){ var pnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24,12,24,12), AutoScroll = true, BackColor = Color.White }; parent.Controls.Add(pnl); int y=8; chkEnabled = MakeCheck(pnl, "Druck aktivieren", ref y); chkAsk = MakeCheck(pnl, "Vor Druck fragen", ref y); chkVat = MakeCheck(pnl, "MwSt.-Zeilen drucken", ref y); chkCut = MakeCheck(pnl, "Nach Druck schneiden (ger e4tespez.)", ref y); chkAutoIfNoPrompt = MakeCheck(pnl, "Automatisch drucken wenn keine Nachfrage", ref y); chkShowWorkTimesOnly = MakeCheck(pnl, "Arbeitszeiten einblenden (Anfang/Ende)", ref y); chkShowPauseTime = MakeCheck(pnl, "Pausenzeiten anzeigen", ref y); chkAutoPauseDeduction = MakeCheck(pnl, "Automatischer Pausenabzug (30/45 Min)", ref y); chkMinimumPauseDeduction = MakeCheck(pnl, "Mindest-Pause abziehen (wenn größer als Vorgabe)", ref y); chkShowArbeitszeit = MakeCheck(pnl, "Arbeitszeit anzeigen", ref y); MakeLabel(pnl, "Prim e4rer Drucker:", ref y); cboPrinters = new ComboBox { Location=new Point(260,y-30), Size=new Size(320,28), DropDownStyle=ComboBoxStyle.DropDownList }; pnl.Controls.Add(cboPrinters); y+=10; chkUseSecondary = MakeCheck(pnl, "Sekund e4ren Drucker bei Fehler verwenden", ref y); MakeLabel(pnl, "Sekund e4rer Drucker:", ref y); cboPrinters2 = new ComboBox { Location=new Point(260,y-30), Size=new Size(320,28), DropDownStyle=ComboBoxStyle.DropDownList }; pnl.Controls.Add(cboPrinters2); y+=10; MakeLabel(pnl, "Zeichen / Zeile:", ref y); nudChars = new NumericUpDown { Location=new Point(260,y-30), Minimum=10, Maximum=120, Value=40, Size=new Size(80,28) }; pnl.Controls.Add(nudChars); y+=20; int btnY = y+10; btnSave = MakeActionButton(pnl, "Speichern", new Point(260,btnY), Color.FromArgb(46,125,50), (s,e)=>SaveAndClose()); btnTest = MakeActionButton(pnl, "Testdruck", new Point(380,btnY), Color.FromArgb(33,150,243), (s,e)=>DoTestPrint()); btnCancel = MakeActionButton(pnl, "Abbrechen", new Point(500,btnY), Color.FromArgb(120,120,120), (s,e)=>Close()); }

        private void BuildLegacyTab(TabPage parent){ var pnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24,12,24,12), AutoScroll = true, BackColor = Color.White }; parent.Controls.Add(pnl); int y=8; var sep1 = new Label { Text="Legacy Drucker 1 (LOGISCHE ID: LEGACY1)", AutoSize=false, Font=new Font("Segoe UI",11F,FontStyle.Bold), ForeColor=Color.FromArgb(33,70,90), Location=new Point(8,y), Size=new Size(420,26)}; pnl.Controls.Add(sep1); y+=34; chkL1Enabled = MakeCheck(pnl, "Aktiviert", ref y); MakeLabel(pnl, "COM-Port:", ref y); cboL1Port = new ComboBox { Location=new Point(260,y-30), Size=new Size(160,26), DropDownStyle=ComboBoxStyle.DropDownList }; pnl.Controls.Add(cboL1Port); y+=10; MakeLabel(pnl, "Baudrate:", ref y); nudL1Baud = new NumericUpDown { Location=new Point(260,y-30), Minimum=1200, Maximum=115200, Increment=300, Value=9600, Size=new Size(120,26)}; pnl.Controls.Add(nudL1Baud); y+=10; MakeLabel(pnl, "Zeichen / Zeile:", ref y); nudL1Chars = new NumericUpDown { Location=new Point(260,y-30), Minimum=10, Maximum=80, Value=40, Size=new Size(80,26)}; pnl.Controls.Add(nudL1Chars); y+=10; chkL1Cut = MakeCheck(pnl, "Cut nach Druck", ref y); btnL1Test = MakeActionButton(pnl, "Test LEGACY1", new Point(260,y), Color.FromArgb(33,150,243), (s,e)=>DoLegacyTest(1)); y+=60; var sep2 = new Label { Text="Legacy Drucker 2 (LOGISCHE ID: LEGACY2)", AutoSize=false, Font=new Font("Segoe UI",11F,FontStyle.Bold), ForeColor=Color.FromArgb(33,70,90), Location=new Point(8,y), Size=new Size(420,26)}; pnl.Controls.Add(sep2); y+=34; chkL2Enabled = MakeCheck(pnl, "Aktiviert", ref y); MakeLabel(pnl, "COM-Port:", ref y); cboL2Port = new ComboBox { Location=new Point(260,y-30), Size=new Size(160,26), DropDownStyle=ComboBoxStyle.DropDownList }; pnl.Controls.Add(cboL2Port); y+=10; MakeLabel(pnl, "Baudrate:", ref y); nudL2Baud = new NumericUpDown { Location=new Point(260,y-30), Minimum=1200, Maximum=115200, Increment=300, Value=9600, Size=new Size(120,26)}; pnl.Controls.Add(nudL2Baud); y+=10; MakeLabel(pnl, "Zeichen / Zeile:", ref y); nudL2Chars = new NumericUpDown { Location=new Point(260,y-30), Minimum=10, Maximum=80, Value=40, Size=new Size(80,26)}; pnl.Controls.Add(nudL2Chars); y+=10; chkL2Cut = MakeCheck(pnl, "Cut nach Druck", ref y); btnL2Test = MakeActionButton(pnl, "Test LEGACY2", new Point(260,y), Color.FromArgb(33,150,243), (s,e)=>DoLegacyTest(2)); y+=60; var lblInfo = new Label { Text="Hinweis: Um einen Legacy Drucker zu verwenden, w�hlen Sie im Windows-Tab\nals Druckernamen 'LEGACY1' oder 'LEGACY2'.", AutoSize=false, Font=new Font("Segoe UI",9F,FontStyle.Italic), ForeColor=Color.DimGray, Location=new Point(8,y), Size=new Size(600,40)}; pnl.Controls.Add(lblInfo); y+=50; var btnSave2 = MakeActionButton(pnl, "Speichern", new Point(260,y), Color.FromArgb(46,125,50), (s,e)=>SaveAndClose()); }

        private void BuildDocumentsTab(TabPage parent)
        {
            var pnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 12, 24, 12), AutoScroll = true, BackColor = Color.White };
            parent.Controls.Add(pnl);
            int y = 8;
            MakeLabel(pnl, "Dokumentendrucker einstellen:", ref y);
            y += 10;
            MakeLabel(pnl, "Drucker:", ref y);
            cboDocPrinters = new ComboBox { Location = new Point(260, y - 30), Size = new Size(320, 28), DropDownStyle = ComboBoxStyle.DropDownList };
            pnl.Controls.Add(cboDocPrinters);
            y += 10;
            chkDisableHoursPrint = MakeCheck(pnl, "Stundenausdruck deaktivieren", ref y);
            chkDisableDocumentsPrint = MakeCheck(pnl, "Dokumentenausdruck deaktivieren", ref y);
            y += 10;
            btnDocSave = MakeActionButton(pnl, "Speichern", new Point(260, y), Color.FromArgb(46, 125, 50), (s, e) => SaveDocPrinter());
            y += 50;
            btnDocTest = MakeActionButton(pnl, "Testdruck", new Point(260, y), Color.FromArgb(33, 150, 243), (s, e) => TestDocPrinter());
        }

        private Label MakeLabel(Control parent, string text, ref int y){ var lbl = new Label { Text=text, Location=new Point(20,y), Size=new Size(230,30), Font=new Font("Segoe UI",10.5F,FontStyle.Bold), ForeColor=Color.FromArgb(33,70,90)}; parent.Controls.Add(lbl); y+=40; return lbl; }
        private CheckBox MakeCheck(Control parent, string text, ref int y){ var cb = new CheckBox { Text=text, Location=new Point(20,y), AutoSize=true, Font=new Font("Segoe UI",10F)}; parent.Controls.Add(cb); y+=34; return cb; }
        private Button MakeActionButton(Control parent, string text, Point loc, Color back, EventHandler click){ var b = new Button { Text=text, Location=loc, Size=new Size(140,40), BackColor=back, ForeColor=Color.White, FlatStyle=FlatStyle.Flat, Font=new Font("Segoe UI Variable",10F,FontStyle.Bold)}; b.FlatAppearance.BorderSize=0; b.FlatAppearance.MouseOverBackColor=ControlPaint.Light(back,.15f); b.Click+=click; try { b.Region = Region.FromHrgn(CreateRoundRectRgn(0,0,b.Width,b.Height,14,14)); } catch { } parent.Controls.Add(b); return b; }

        private void LoadSettings(){ try { _settings = ReceiptPrinterSettings.Load(); } catch { _settings = new ReceiptPrinterSettings(); } cboPrinters.Items.Clear(); cboPrinters2.Items.Clear(); foreach (string pr in PrinterSettings.InstalledPrinters){ cboPrinters.Items.Add(pr); cboPrinters2.Items.Add(pr);} if (!cboPrinters.Items.Contains("LEGACY1")) cboPrinters.Items.Add("LEGACY1"); if (!cboPrinters.Items.Contains("LEGACY2")) cboPrinters.Items.Add("LEGACY2"); if (!cboPrinters2.Items.Contains("LEGACY1")) cboPrinters2.Items.Add("LEGACY1"); if (!cboPrinters2.Items.Contains("LEGACY2")) cboPrinters2.Items.Add("LEGACY2"); chkEnabled.Checked = _settings.Enabled; chkAsk.Checked = _settings.AskUser; chkVat.Checked = _settings.PrintVatLines; chkCut.Checked = _settings.CutAfterPrint; chkAutoIfNoPrompt.Checked = _settings.AutoPrintIfNoPrompt; chkUseSecondary.Checked = _settings.UseSecondaryOnFailure; if (chkShowWorkTimesOnly != null) chkShowWorkTimesOnly.Checked = !_settings.ShowWorkTimesOnly; if (chkShowPauseTime != null) chkShowPauseTime.Checked = _settings.ShowPauseTime; if (chkAutoPauseDeduction != null) chkAutoPauseDeduction.Checked = _settings.AutoPauseDeduction; if (chkShowArbeitszeit != null) chkShowArbeitszeit.Checked = _settings.ShowArbeitszeit; if (chkMinimumPauseDeduction != null) chkMinimumPauseDeduction.Checked = _settings.MinimumPauseDeduction; nudChars.Value = Math.Max(nudChars.Minimum, Math.Min(nudChars.Maximum, _settings.CharsPerLine)); if (!string.IsNullOrWhiteSpace(_settings.PrinterName) && cboPrinters.Items.Contains(_settings.PrinterName)) cboPrinters.SelectedItem=_settings.PrinterName; else if (cboPrinters.Items.Count>0) cboPrinters.SelectedIndex=0; if (!string.IsNullOrWhiteSpace(_settings.SecondaryPrinterName) && cboPrinters2.Items.Contains(_settings.SecondaryPrinterName)) cboPrinters2.SelectedItem=_settings.SecondaryPrinterName; else if (cboPrinters2.Items.Count>0) cboPrinters2.SelectedIndex=0; RefreshSerialPortLists(); chkL1Enabled.Checked=_settings.Legacy1Enabled; chkL1Cut.Checked=_settings.Legacy1Cut; nudL1Baud.Value=ClampNumeric(nudL1Baud,_settings.Legacy1Baud); nudL1Chars.Value=ClampNumeric(nudL1Chars,_settings.Legacy1Chars); if (!string.IsNullOrWhiteSpace(_settings.Legacy1ComPort)&&cboL1Port.Items.Contains(_settings.Legacy1ComPort)) cboL1Port.SelectedItem=_settings.Legacy1ComPort; chkL2Enabled.Checked=_settings.Legacy2Enabled; chkL2Cut.Checked=_settings.Legacy2Cut; nudL2Baud.Value=ClampNumeric(nudL2Baud,_settings.Legacy2Baud); nudL2Chars.Value=ClampNumeric(nudL2Chars,_settings.Legacy2Chars); if (!string.IsNullOrWhiteSpace(_settings.Legacy2ComPort)&&cboL2Port.Items.Contains(_settings.Legacy2ComPort)) cboL2Port.SelectedItem=_settings.Legacy2ComPort; UpdateCustomTabButtons(); AdjustLayout(); try { cboDocPrinters.Items.Clear(); foreach (string pr in PrinterSettings.InstalledPrinters) cboDocPrinters.Items.Add(pr); string docPrn = IniHelper.ReadValue("UI", "DocumentPrinter", AppSettings.IniPath); if (!string.IsNullOrWhiteSpace(docPrn) && cboDocPrinters.Items.Contains(docPrn)) cboDocPrinters.SelectedItem = docPrn; else if (cboDocPrinters.Items.Count > 0) cboDocPrinters.SelectedIndex = 0; 
                var dHours = IniHelper.ReadValue("UI", "DisableHoursPrint", AppSettings.IniPath);
                var dDocs = IniHelper.ReadValue("UI", "DisableDocumentsPrint", AppSettings.IniPath);
                chkDisableHoursPrint.Checked = !string.IsNullOrWhiteSpace(dHours) && (dHours.Equals("1") || dHours.Equals("true", StringComparison.OrdinalIgnoreCase));
                chkDisableDocumentsPrint.Checked = !string.IsNullOrWhiteSpace(dDocs) && (dDocs.Equals("1") || dDocs.Equals("true", StringComparison.OrdinalIgnoreCase));
            } catch { } }

        private decimal ClampNumeric(NumericUpDown nud, int val){ if (val < nud.Minimum) val = (int)nud.Minimum; if (val > nud.Maximum) val = (int)nud.Maximum; return val; }
        private void RefreshSerialPortLists(){ string[] ports; try { ports = SerialPort.GetPortNames(); } catch { ports = new string[0]; } Array.Sort(ports,StringComparer.OrdinalIgnoreCase); cboL1Port.Items.Clear(); cboL2Port.Items.Clear(); foreach(var p in ports){ cboL1Port.Items.Add(p); cboL2Port.Items.Add(p);} }

        private void SaveAndClose(){ try { _settings.Enabled=chkEnabled.Checked; _settings.AskUser=chkAsk.Checked; _settings.PrintVatLines=chkVat.Checked; _settings.CutAfterPrint=chkCut.Checked; _settings.AutoPrintIfNoPrompt=chkAutoIfNoPrompt.Checked; _settings.CharsPerLine=(int)nudChars.Value; _settings.PrinterName=cboPrinters.SelectedItem as string ?? string.Empty; _settings.SecondaryPrinterName=cboPrinters2.SelectedItem as string ?? string.Empty; _settings.UseSecondaryOnFailure=chkUseSecondary.Checked; _settings.ShowWorkTimesOnly = !(chkShowWorkTimesOnly != null && chkShowWorkTimesOnly.Checked); _settings.ShowPauseTime = chkShowPauseTime != null && chkShowPauseTime.Checked; _settings.AutoPauseDeduction = chkAutoPauseDeduction != null && chkAutoPauseDeduction.Checked; _settings.ShowArbeitszeit = chkShowArbeitszeit != null && chkShowArbeitszeit.Checked; _settings.MinimumPauseDeduction = chkMinimumPauseDeduction != null && chkMinimumPauseDeduction.Checked; _settings.Legacy1Enabled=chkL1Enabled.Checked; _settings.Legacy1ComPort=cboL1Port.SelectedItem as string ?? string.Empty; _settings.Legacy1Baud=(int)nudL1Baud.Value; _settings.Legacy1Chars=(int)nudL1Chars.Value; _settings.Legacy1Cut=chkL1Cut.Checked; _settings.Legacy2Enabled=chkL2Enabled.Checked; _settings.Legacy2ComPort=cboL2Port.SelectedItem as string ?? string.Empty; _settings.Legacy2Baud=(int)nudL2Baud.Value; _settings.Legacy2Chars=(int)nudL2Chars.Value; _settings.Legacy2Cut=chkL2Cut.Checked; _settings.Save(); 
            IniHelper.WriteValue("UI", "DisableHoursPrint", chkDisableHoursPrint.Checked ? "1" : "0", AppSettings.IniPath);
            IniHelper.WriteValue("UI", "DisableDocumentsPrint", chkDisableDocumentsPrint.Checked ? "1" : "0", AppSettings.IniPath);
            DialogResult=DialogResult.OK; Close(); } catch (Exception ex) { MessageBox.Show(this, "Fehler beim Speichern:\r\n"+ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error); } }

        private ReceiptPrinterSettings BuildCurrentSettings(){ return new ReceiptPrinterSettings { Enabled = chkEnabled.Checked, AskUser = chkAsk.Checked, PrintVatLines = chkVat.Checked, CutAfterPrint = chkCut.Checked, AutoPrintIfNoPrompt = chkAutoIfNoPrompt.Checked, CharsPerLine = (int)nudChars.Value, PrinterName = cboPrinters.SelectedItem as string ?? string.Empty, SecondaryPrinterName = cboPrinters2.SelectedItem as string ?? string.Empty, UseSecondaryOnFailure = chkUseSecondary.Checked, ShowWorkTimesOnly = !(chkShowWorkTimesOnly != null && chkShowWorkTimesOnly.Checked), ShowPauseTime = chkShowPauseTime != null && chkShowPauseTime.Checked, AutoPauseDeduction = chkAutoPauseDeduction != null && chkAutoPauseDeduction.Checked, ShowArbeitszeit = chkShowArbeitszeit != null && chkShowArbeitszeit.Checked, Legacy1Enabled = chkL1Enabled.Checked, Legacy1ComPort = cboL1Port.SelectedItem as string ?? string.Empty, Legacy1Baud = (int)nudL1Baud.Value, Legacy1Chars = (int)nudL1Chars.Value, Legacy1Cut = chkL1Cut.Checked, Legacy2Enabled = chkL2Enabled.Checked, Legacy2ComPort = cboL2Port.SelectedItem as string ?? string.Empty, Legacy2Baud = (int)nudL2Baud.Value, Legacy2Chars = (int)nudL2Chars.Value, Legacy2Cut = chkL2Cut.Checked, MinimumPauseDeduction = chkMinimumPauseDeduction != null && chkMinimumPauseDeduction.Checked }; }

        private void DoTestPrint(){ try { var cfg = BuildCurrentSettings(); var lines = new[]{"Testdruck","Prim�r: "+(cfg.PrinterName??"(Standard)"),"Sekundär: "+(string.IsNullOrWhiteSpace(cfg.SecondaryPrinterName)?"(keiner)":cfg.SecondaryPrinterName),"Fallback aktiv: "+(cfg.UseSecondaryOnFailure?"Ja":"Nein"),"Zeit: "+DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"),"Zeichen/Zeile: "+cfg.CharsPerLine,"MwSt.-Zeilen: "+(cfg.PrintVatLines?"Ja":"Nein")}; new ReceiptPrinter(cfg).PrintSimpleReceipt("TEST", lines); MessageBox.Show(this,"Testdruck gesendet (unsaved settings).","Info",MessageBoxButtons.OK,MessageBoxIcon.Information);} catch(Exception ex){ MessageBox.Show(this,"Testdruck fehlgeschlagen:\r\n"+ex.Message,"Fehler",MessageBoxButtons.OK,MessageBoxIcon.Error);} }
        private void DoLegacyTest(int id){ try { var cfg = BuildCurrentSettings(); string logical = id==1?"LEGACY1":"LEGACY2"; cfg.PrinterName = logical; cfg.SecondaryPrinterName = string.Empty; var lines = new[]{"Legacy Testdruck","Logischer Name: "+logical,DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss")}; new ReceiptPrinter(cfg).PrintSimpleReceipt(logical, lines); MessageBox.Show(this,"Legacy Testdruck gesendet (unsaved settings).","Info",MessageBoxButtons.OK,MessageBoxIcon.Information);} catch(Exception ex){ MessageBox.Show(this,"Legacy Testdruck fehlgeschlagen:\r\n"+ex.Message,"Fehler",MessageBoxButtons.OK,MessageBoxIcon.Error);} }

        private void SaveDocPrinter(){ try { var name = cboDocPrinters.SelectedItem as string ?? string.Empty; IniHelper.WriteValue("UI", "DocumentPrinter", name, AppSettings.IniPath); 
            IniHelper.WriteValue("UI", "DisableHoursPrint", chkDisableHoursPrint.Checked ? "1" : "0", AppSettings.IniPath);
            IniHelper.WriteValue("UI", "DisableDocumentsPrint", chkDisableDocumentsPrint.Checked ? "1" : "0", AppSettings.IniPath);
            MessageBox.Show(this, "Dokumentendrucker gespeichert.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information); } catch (Exception ex) { MessageBox.Show(this, "Speichern fehlgeschlagen:\r\n" + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error); } }
        private void TestDocPrinter(){ try { var name = cboDocPrinters.SelectedItem as string ?? string.Empty; if (string.IsNullOrWhiteSpace(name)) { MessageBox.Show(this, "Bitte Drucker ausw�hlen.", "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information); return; } using (var pd = new PrintDocument()) { pd.PrinterSettings = new PrinterSettings { PrinterName = name }; if (!pd.PrinterSettings.IsValid) { MessageBox.Show(this, "Drucker ist ung�ltig oder nicht erreichbar.", "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error); return; } pd.DocumentName = "Geldautomat - Testdruck"; pd.PrintPage += (s, ev) => { var g = ev.Graphics; using (var font = new Font("Segoe UI", 16f, FontStyle.Bold)) using (var brush = new SolidBrush(Color.Black)) { g.DrawString("Testdruck Dokumentendrucker", font, brush, new PointF(40, 60)); } using (var font2 = new Font("Segoe UI", 10f)) { g.DrawString(DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"), font2, Brushes.Black, new PointF(40, 100)); g.DrawString("Quelle: Einstellungen > Dokumentendrucker", font2, Brushes.Black, new PointF(40, 120)); } ev.HasMorePages = false; }; pd.Print(); } MessageBox.Show(this, "Testdruck gesendet.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information); } catch (Exception ex) { MessageBox.Show(this, "Testdruck fehlgeschlagen:\r\n" + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error); } }
    }
}
