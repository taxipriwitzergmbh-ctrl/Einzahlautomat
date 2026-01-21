using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using ITLlib;

namespace TaMi_Einzahlautomat.Printing
{
    internal sealed class ItlTicketPrinter : IDisposable
    {
        private readonly string _comPort; private readonly byte _sspAddress; private readonly int _ticketLength; private readonly bool _rotate90; // new orientation flag
        private SSP_COMMAND _cmd = new SSP_COMMAND(); private SSP_COMMAND_INFO _cmdInfo = new SSP_COMMAND_INFO(); private SSPComms _comms = new SSPComms(); private SSP_KEYS _keys = new SSP_KEYS();
        private readonly object _sync = new object();
        private bool _opened; private bool _handshakeComplete; private bool _encryption; private System.DateTime _lastCommUtc;
        private bool _printComplete; private bool _printFailed; private string _lastStatus="";
        private const byte TemplateId = 5; private int _nextLinePos = 150; private const int LineStep = 40;
        private bool _retryAfterKeyNotSet=false; // one retry

        private const ulong FIXED_KEY_VB = 81985526925837671UL; // exakt wie im alten VB-Dienst

        private enum Cmd:byte { Reset=1, Set_Inhibits=2, Setup_Request=5, Host_Protocol_Version=6, Poll=7, Reject=8, Enable=10, Get_Serial_Number=12, Sync=17, Set_Generator=74, Set_Modulus=75, Request_Key_Exchange=76, Get_Full_Firmware=32, Print_Command=112, Printer_Config_Command=113 }
        private enum PrintSub:byte { Setup=1, Dispense_Ticket=2 }
        private enum SetupSub:byte { Add_Fixed_Text=1, Clear_Template=6 }
        private enum ConfigSub:byte { Set_Ticket_Length=2, Set_Ticket_Width=3, Set_Paper_Saving_Mode=13 }
        private enum PollResp:byte { Printing_Ticket=165, Printed_Ticket=166, Could_Not_Print_Ticket=168, No_Paper=171 }

        public ItlTicketPrinter(string comPort,int ticketLength=90,byte sspAddress=65,bool rotate90=true){ _comPort=comPort; _sspAddress=sspAddress; _ticketLength=ticketLength<=0?90:ticketLength; _rotate90=rotate90; }

        public bool Initialize(){ lock(_sync){ try { if(_opened) return true; _cmd=new SSP_COMMAND{SSPAddress=_sspAddress,ComPort=_comPort,EncryptionStatus=false,RetryLevel=2,Timeout=2500}; if(!_comms.OpenSSPComPort(_cmd)){ Log("Open COM fehlgeschlagen"); return false;} _opened=true; Log("Connected"); SendFrame(false, BuildSimple(Cmd.Sync,false)); if(!WaitHandshake(6000)){ Log("Handshake Timeout"); return false;} return true;} catch(System.Exception ex){ Log("Init Fehler: "+ex.Message); return false; } } }
        private bool WaitHandshake(int ms){ var until=System.DateTime.UtcNow.AddMilliseconds(ms); while(System.DateTime.UtcNow<until){ if(_handshakeComplete) return true; System.Threading.Thread.Sleep(120); if((System.DateTime.UtcNow-_lastCommUtc).TotalMilliseconds>500) SendFrame(false, BuildSimple(Cmd.Poll,false)); } return _handshakeComplete; }

        public bool PrintLines(System.Collections.Generic.IEnumerable<string> lines){ lock(_sync){ if(!Initialize()) return false; _printComplete=false; _printFailed=false; _nextLinePos=150; _lastStatus=""; try { ClearTemplate(TemplateId); foreach (var raw in (lines ?? System.Linq.Enumerable.Empty<string>())) { var t = Preprocess(raw); if (string.IsNullOrEmpty(t)) { _nextLinePos += LineStep; continue; } AddFixedText(t, _nextLinePos, _rotate90 ? 40 : 20); _nextLinePos += LineStep; } DispenseTicket(TemplateId); var timeout=System.DateTime.UtcNow.AddSeconds(15); while(System.DateTime.UtcNow<timeout && !_printComplete && !_printFailed){ System.Threading.Thread.Sleep(300); SendFrame(false, BuildSimple(Cmd.Poll,false)); } Log($"Print status: complete={_printComplete} failed={_printFailed} status={_lastStatus}"); return _printComplete && !_printFailed; } catch(System.Exception ex){ Log("Print Fehler: "+ex.Message); return false; } } }
        private string Preprocess(string s)
        {
            if (s == null) return string.Empty;
            // Replace characters that may not render on ticket printers
            s = s.Replace("€", " EUR");
            // Properly map German umlauts to ASCII-friendly sequences
            s = s
                .Replace("ä", "ae").Replace("Ä", "Ae")
                .Replace("ö", "oe").Replace("Ö", "Oe")
                .Replace("ü", "ue").Replace("Ü", "Ue")
                .Replace("ß", "ss");
            if (s.Length > 40) s = s.Substring(0, 40);
            return s;
        }

        // ==== Handshake Reaction (VB Stil) =====
        private void ProcessResponse(byte[] sent){ try { if(_cmd.ResponseDataLength<1) { Log($"Resp leer cmd={sent[2]} enc={(sent[0])}" ); return;} byte resp=_cmd.ResponseData[0]; byte cmdCode=sent[2]; Log($"Resp cmd={cmdCode} enc={(sent[0])} code={resp} bytes=[{DumpResp()}]"); if(cmdCode==(byte)Cmd.Poll && resp==240){ for(int i=1;i<_cmd.ResponseDataLength;i++){ var ev=_cmd.ResponseData[i]; if(ev==(byte)PollResp.Printing_Ticket) _lastStatus="Printing"; else if(ev==(byte)PollResp.Printed_Ticket){ _lastStatus="Print complete"; _printComplete=true;} else if(ev==(byte)PollResp.Could_Not_Print_Ticket || ev==(byte)PollResp.No_Paper){ _printFailed=true; _lastStatus="Print fail: "+ev; } } }
            if(cmdCode==(byte)Cmd.Sync && sent[0]==0 && !_encryption){ Log("Syncronisiert (unencrypted)"); _comms.InitiateSSPHostKeys(_keys,_cmd); SendFrame(false, BuildSimple(Cmd.Set_Generator,false,System.BitConverter.GetBytes(_keys.Generator))); return; }
            if(cmdCode==(byte)Cmd.Set_Generator){ Log("Set Generator OK"); SendFrame(false, BuildSimple(Cmd.Set_Modulus,false,System.BitConverter.GetBytes(_keys.Modulus))); return; }
            if(cmdCode==(byte)Cmd.Set_Modulus){ Log("Set Modulus OK"); SendFrame(false, BuildSimple(Cmd.Request_Key_Exchange,false,System.BitConverter.GetBytes(_keys.HostInter))); return; }
            if(cmdCode==(byte)Cmd.Request_Key_Exchange){ if(resp==240 && _cmd.ResponseDataLength>=9){ var tmp=new byte[8]; for(int i=0;i<8;i++) tmp[i]=_cmd.ResponseData[i+1]; _keys.SlaveInterKey=System.BitConverter.ToUInt64(tmp,0); _comms.CreateSSPHostEncryptionKey(_keys); _cmd.Key.FixedKey=FIXED_KEY_VB; // wichtig: VB Wert
_cmd.Key.VariableKey=_keys.KeyHost; Log("Key Request OK -> Keys gesetzt (FixedKey VB)"); SendFrame(false, BuildSimple(Cmd.Enable,false)); Thread.Sleep(60); SetPaperSavingMode(0); Thread.Sleep(60); SetTicketLength(_ticketLength); Thread.Sleep(60); SetTicketWidth(_rotate90? _ticketLength : 150); // heuristik
Thread.Sleep(80); SendFrame(true, BuildSimple(Cmd.Sync,true)); return; } }
            if(cmdCode==(byte)Cmd.Sync && sent[0]==1){ // encrypted sync
                // Einige Ger�te liefern nur 0xF0 oder manchmal 0xF1 � beides akzeptieren
                if(resp==240){ _encryption=true; if(!_handshakeComplete){ _handshakeComplete=true; Log("Encryption aktiv / Handshake abgeschlossen"); ClearTemplate(TemplateId); RequestFirmware(); } return; }
                if(resp==250){ Log("Key_Not_Set bei encrypted Sync"); if(!_retryAfterKeyNotSet){ _retryAfterKeyNotSet=true; _encryption=false; _handshakeComplete=false; Log("Starte erneuten Versuch mit neuem Sync"); SendFrame(false, BuildSimple(Cmd.Sync,false)); return; } else { Log("Retry bereits durchgef�hrt � Abbruch"); } }
            }
        } catch(System.Exception ex){ Log("ProcessResponse Fehler: "+ex.Message); } }

        // ==== Low level send =====
        private void SendFrame(bool encrypted, byte[] frame){ if(frame==null||frame.Length<3||!_opened) return; try { _cmd.EncryptionStatus=encrypted; _cmd.CommandDataLength=frame[1]; for(int i=0;i<frame[1];i++) _cmd.CommandData[i]=frame[i+2]; if(!_comms.SSPSendCommand(_cmd,_cmdInfo)){ Log("SendCommand failed cmd="+frame[2]+" enc="+encrypted); return;} _lastCommUtc=System.DateTime.UtcNow; Log($"Sent cmd={frame[2]} enc={(encrypted?1:0)} respLen={_cmd.ResponseDataLength}"); ProcessResponse(frame); } catch(System.Exception ex){ Log("SendFrame Fehler: "+ex.Message); } }
        private byte[] BuildSimple(Cmd c,bool encrypted,byte[] payload=null){ var list=new List<byte>(); list.Add(encrypted?(byte)1:(byte)0); int len=1+(payload?.Length??0); list.Add((byte)len); list.Add((byte)c); if(payload!=null) list.AddRange(payload); return list.ToArray(); }

        // ==== High level printer commands =====
        private void ClearTemplate(byte template){ if(!_handshakeComplete) return; var arr=new byte[]{1,4,(byte)Cmd.Print_Command,(byte)PrintSub.Setup,(byte)SetupSub.Clear_Template,template}; SendFrame(true,arr); }
        private void AddFixedText(string text,int logicalLinePos,int basePos){ if(!_handshakeComplete) return; // rotation aware placement
int xLow=_rotate90? logicalLinePos : basePos; int yLow=_rotate90? basePos : logicalLinePos; var element=new List<byte>{(byte)(_encryption?1:0),0,(byte)Cmd.Print_Command,(byte)PrintSub.Setup,(byte)SetupSub.Add_Fixed_Text,TemplateId,1,(byte)(_rotate90?1:0)}; byte xHigh=0; while(xLow>255){ xHigh++; xLow-=256; } element.Add((byte)xLow); element.Add(xHigh); byte yHigh=0; while(yLow>255){ yHigh++; yLow-=256; } element.Add((byte)yLow); element.Add(yHigh); var bytes=Encoding.Unicode.GetBytes(text); element.AddRange(bytes); element[1]=(byte)(10+bytes.Length); SendFrame(_encryption,element.ToArray()); }
        private void DispenseTicket(byte template){ if(!_handshakeComplete) return; var arr=new byte[]{(byte)(_encryption?1:0),3,(byte)Cmd.Print_Command,(byte)PrintSub.Dispense_Ticket,template}; SendFrame(_encryption,arr); }
        private void SetTicketLength(int length){ byte low=(byte)(length & 0xFF); byte high=(byte)((length>>8)&0xFF); var arr=new byte[]{0,4,(byte)Cmd.Printer_Config_Command,(byte)ConfigSub.Set_Ticket_Length,low,high}; SendFrame(false,arr); }
        private void SetTicketWidth(int width){ byte low=(byte)(width & 0xFF); byte high=(byte)((width>>8)&0xFF); var arr=new byte[]{0,4,(byte)Cmd.Printer_Config_Command,(byte)ConfigSub.Set_Ticket_Width,low,high}; SendFrame(false,arr); }
        private void SetPaperSavingMode(int mode){ var arr=new byte[]{0,3,(byte)Cmd.Printer_Config_Command,(byte)ConfigSub.Set_Paper_Saving_Mode,(byte)mode}; SendFrame(false,arr); }
        private void RequestFirmware(){ SendFrame(false, BuildSimple(Cmd.Get_Full_Firmware,false)); }

        private string DumpResp(){ try { var parts=new List<string>(); for(int i=0;i<_cmd.ResponseDataLength;i++) parts.Add(_cmd.ResponseData[i].ToString()); return string.Join(",",parts); } catch { return "?"; } }
        private void Log(string msg){
            try
            {
                if (ShouldLog(msg))
                    AppLogger.Log("[ItlTicketPrinter] "+msg);
            }
            catch { }
        }

        private static bool? _debugFlag; // Lazy Cache
        private static DateTime _debugLastCheckUtc = DateTime.MinValue;
        private static readonly TimeSpan _debugReloadInterval = TimeSpan.FromSeconds(30); // periodisch neu einlesen (falls INI ge�ndert)
        private static bool IsDebugEnabled()
        {
            if (_debugFlag == null || (DateTime.UtcNow - _debugLastCheckUtc) > _debugReloadInterval)
            {
                _debugLastCheckUtc = DateTime.UtcNow;
                try
                {
                    string v = IniHelper.ReadValue("General", "Debug", AppSettings.IniPath);
                    _debugFlag = (!string.IsNullOrEmpty(v)) && (v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1" || v.Equals("yes", StringComparison.OrdinalIgnoreCase));
                }
                catch { _debugFlag = false; }
            }
            return _debugFlag == true;
        }

        // Entscheidet ob geloggt wird: Immer Fehler/Fail, sonst nur bei Debug Flag
        private static bool ShouldLog(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return false;
            // Schl�sselw�rter f�r Fehler immer loggen
            if (msg.IndexOf("Fehler", StringComparison.OrdinalIgnoreCase) >= 0 ||
                msg.IndexOf("fail", StringComparison.OrdinalIgnoreCase) >= 0 ||
                msg.IndexOf("fehlgeschlagen", StringComparison.OrdinalIgnoreCase) >= 0 ||
                msg.IndexOf("Timeout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                msg.IndexOf("Abbruch", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return IsDebugEnabled();
        }
        public void Dispose(){ lock(_sync){ try { _comms.CloseComPort(); } catch { } _opened=false; } }
    }
}
