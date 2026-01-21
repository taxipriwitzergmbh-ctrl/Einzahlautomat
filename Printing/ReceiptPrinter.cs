using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;

namespace TaMi_Einzahlautomat.Printing
{
    public class ReceiptPrinter
    {
        private readonly ReceiptPrinterSettings _cfg;
        private IList<string> _linesToPrint;
        private static readonly string[] KnownTypes = new[] { "SCHICHTABRECHNUNG","NACHZAHLUNG","RÜCKZAHLUNG","RUECKZAHLUNG","EINZAHLUNG","AUSZAHLUNG" };
        public ReceiptPrinter(ReceiptPrinterSettings cfg){ _cfg = cfg ?? new ReceiptPrinterSettings(); }

        public void PrintSimpleReceipt(string title, IEnumerable<string> bodyLines)
        {
            try
            {
                var bodyList = (bodyLines ?? Enumerable.Empty<string>()).Where(l=>l!=null).Select(l=>l.TrimEnd()).ToList();
                if (string.IsNullOrWhiteSpace(title)) { var detected = DetectTitleFromBody(bodyList); if (!string.IsNullOrWhiteSpace(detected)) title = detected; }
                var list = new List<string>();
                list.Add("==== " + (string.IsNullOrWhiteSpace(title)?"QUITTUNG":title.Trim()) + " ====");
                list.Add(DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"));
                if (bodyList.Count>0) list.AddRange(bodyList);
                list.Add(new string('=',24));
                _linesToPrint = WrapLines(list, Math.Max(10,_cfg.CharsPerLine));

                // Konfiguration für Legacy seriell immer setzen
                LegacyItlPrinterManager.Configure(
                    new LegacyItlPrinterConfig { Enabled=_cfg.Legacy1Enabled, ComPort=_cfg.Legacy1ComPort, BaudRate=_cfg.Legacy1Baud, CharsPerLine=_cfg.Legacy1Chars, CutAfterPrint=_cfg.Legacy1Cut },
                    new LegacyItlPrinterConfig { Enabled=_cfg.Legacy2Enabled, ComPort=_cfg.Legacy2ComPort, BaudRate=_cfg.Legacy2Baud, CharsPerLine=_cfg.Legacy2Chars, CutAfterPrint=_cfg.Legacy2Cut });

                // 1) ITL Ticket Printer Versuch für LEGACY Namen (SSP Ticketdrucker) -> COM-Port aus Legacy1/2 Einstellungen
                if (TryItlTicketPrint(_cfg.PrinterName, title)) return;
                if (!IsLegacyName(_cfg.PrinterName))
                {
                    if (TryWindowsPrint(_cfg.PrinterName)) return; // normaler Windows Drucker
                }
                else
                {
                    // Fallback: ALT seriell (ESC/POS) nur wenn ITL Ticket nicht ging
                    if (LegacyItlPrinterManager.Print(_cfg.PrinterName, title, _linesToPrint.ToArray())) return;
                }

                // Sekundär
                if (_cfg.UseSecondaryOnFailure && !string.IsNullOrWhiteSpace(_cfg.SecondaryPrinterName))
                {
                    if (TryItlTicketPrint(_cfg.SecondaryPrinterName, title)) return;
                    if (!IsLegacyName(_cfg.SecondaryPrinterName))
                    {
                        if (TryWindowsPrint(_cfg.SecondaryPrinterName)) return;
                    }
                    else if (LegacyItlPrinterManager.Print(_cfg.SecondaryPrinterName, title, _linesToPrint.ToArray())) return;
                }
                SafeWriteFallback(_linesToPrint ?? new List<string>{"PRINT ERROR"});
            }
            catch (Exception)
            { try { SafeWriteFallback(_linesToPrint ?? new List<string>{"PRINT ERROR"}); } catch { } }
        }

        private bool TryItlTicketPrint(string logicalName, string header)
        {
            try
            {
                if (!IsLegacyName(logicalName)) return false; // nur LEGACY1/2 als logischer Name
                // Mapping: LEGACY1 -> Legacy1 Einstellungen; LEGACY2 -> Legacy2 Einstellungen (COM-Port erforderlich)
                string com = null; bool enabled=false; int length=90;
                if (string.Equals(logicalName,"LEGACY1",StringComparison.OrdinalIgnoreCase)) { enabled = _cfg.Legacy1Enabled; com = _cfg.Legacy1ComPort; }
                else if (string.Equals(logicalName,"LEGACY2",StringComparison.OrdinalIgnoreCase)) { enabled = _cfg.Legacy2Enabled; com = _cfg.Legacy2ComPort; }
                if (!enabled || string.IsNullOrWhiteSpace(com)) return false;
                // Ticketlänge heuristisch aus CharsPerLine ableiten (oder Standard 90)
                try { length = Math.Max(60, Math.Min(200, 20 + (_linesToPrint?.Count ?? 6)*10)); } catch { }
                using (var pr = new ItlTicketPrinter(com, length))
                {
                    if (!pr.Initialize()) return false;
                    var lines = new List<string>();
                    lines.Add(header);
                    lines.AddRange(_linesToPrint.Skip(1)); // erste Zeile (==== HEADER ===) ersetzen: schon header
                    return pr.PrintLines(lines);
                }
            }
            catch (Exception ex)
            {
                try { AppLogger.Log("[ReceiptPrinter] ITL Ticket Versuch fehlgeschlagen: "+ex.Message); } catch { }
                return false;
            }
        }

        private bool IsLegacyName(string name){ if (string.IsNullOrWhiteSpace(name)) return false; var n=name.Trim().ToUpperInvariant(); return n=="LEGACY1"|| n=="LEGACY2"; }
        private bool TryWindowsPrint(string printerName){ try { var doc=new PrintDocument(); if(!string.IsNullOrWhiteSpace(printerName)) doc.PrinterSettings.PrinterName=printerName; doc.PrintPage += Doc_PrintPage; if(!doc.PrinterSettings.IsValid) throw new InvalidOperationException("Printer invalid: "+printerName); doc.Print(); return true;} catch(Exception ex){ try { var lines=new List<string>(_linesToPrint??new List<string>()); lines.Add("-- Fehler: "+ex.Message); SafeWriteFallback(lines);} catch { } return false; } }
        private string DetectTitleFromBody(IList<string> body){ if(body==null) return null; foreach(var line in body){ if(string.IsNullOrWhiteSpace(line)) continue; var upper=line.Trim().ToUpperInvariant(); foreach(var k in KnownTypes){ if(upper==k || upper.StartsWith(k+" ") || upper.Contains(" "+k+" ") || upper.EndsWith(" "+k) || upper.StartsWith(k+":") || upper.StartsWith(k+";")) return k=="RUECKZAHLUNG"?"RÜCKZAHLUNG":k; } } return null; }
        private void Doc_PrintPage(object s, PrintPageEventArgs e){ using(var font=new Font("Consolas",9f)){ float y=0; float h=font.GetHeight(e.Graphics)+2; foreach(var line in _linesToPrint){ e.Graphics.DrawString(line,font,Brushes.Black,0,y); y+=h; } } e.HasMorePages=false; }
        private List<string> WrapLines(IEnumerable<string> lines, int width){ var result=new List<string>(); foreach(var l in lines){ if(l.Length<=width){ result.Add(l); continue;} int idx=0; while(idx<l.Length){ int take=Math.Min(width,l.Length-idx); result.Add(l.Substring(idx,take)); idx+=take; } } return result; }
        private void SafeWriteFallback(IEnumerable<string> lines){ try { var path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"receipt_fallback_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".txt"); File.WriteAllLines(path, lines?? new[]{"(leer)"}); } catch { } }
    }
}