using System;

using System.Threading;
using System.Net.Sockets;

using System.Diagnostics;


namespace SuE.Tools
{
    public enum ConnectionState
    {
        DISCONNECTED = 0,
        CONNECTING = 1,
        CONNECTED = 2
    }

    public class MessageClient
    {
        private const bool DEBUG_THREAD = false;
        private const bool DEBUG_SOCKET = false;
        private const bool DEBUG_RECEIVE = false;

        private const bool DEBUG_MESSAGES = false;
        private const bool DEBUG_BUFFERCONTENT = false;
        private const bool DEBUG_MESSAGEDETAILS = false;

        //Steuerzeichen
        protected internal const byte STX = 0x02;
        protected internal const byte ETX = 0x03;
        protected internal const byte ESC = 0x10;

        //Variablen
        //private TcpClient tcpClient;
        private Object socketLock;
        private Socket socket;

        private Thread _tcp_Thread;

        private string remoteHost;
        private int remotePort;

        private long statechange;
        private ConnectionState state;


        public MessageClient()
        {
            socketLock = new Object();
            _tcp_Thread = null;

            statechange = 0;
            state = ConnectionState.DISCONNECTED;

        }

        public bool IsThreadAlive()
        {
            if (_tcp_Thread == null)
                return false;

            return _tcp_Thread.IsAlive;

        }

        public System.Threading.ThreadState ThreadState()
        {
            if (_tcp_Thread == null)
                return System.Threading.ThreadState.Unstarted;

            return _tcp_Thread.ThreadState;
        }
        

        public void Connect(string HostnameOrAdress, int Port)
        {
            Debug.WriteLineIf(DEBUG_THREAD, GetType().Name + "::Connect(" + HostnameOrAdress + ":" + Port + ") enter");

            //10.04.25: Hier kann es hängen, deshalb das deaktiviert und 
            if (_tcp_Thread != null)
                _tcp_Thread.Join(5000);

            //Neu
            if (state != ConnectionState.DISCONNECTED)
                throw new Exception($"{this} is not disconnect, cant connect now");

            remoteHost = HostnameOrAdress;
            remotePort = Port;

            //Fire Event, das sollte vor dem Threadstart passieren
            ReportStateChange(ConnectionState.CONNECTING);

            //Thread starten
            _tcp_Thread = new Thread(this.Socket_ThreadProc);
            _tcp_Thread.Name = GetType().Name + ": " + remoteHost + ":" + remotePort;
            _tcp_Thread.Start();

            //TEST
            //_tcp_Thread.Abort();

            Debug.WriteLineIf(DEBUG_THREAD, GetType().Name + "::Connect(" + HostnameOrAdress + ":" + Port + ") leave");
        }

        public virtual void Disconnect()
        {
            Debug.WriteLine(GetType().Name + "::Disconnect() enter");
            statechange = Environment.TickCount;

            /*
            if (tcpClient != null)
            {
                Debug.WriteLine(GetType().Name + "::Disconnect()");
                tcpClient.Close();
                tcpClient = null;
            }
            */

            /*
            if (socket != null)
            {
                lock (socketLock)
                {
                    

                    if (socket.Connected)
                        socket.Shutdown(SocketShutdown.Both);

                    socket.Close();
                    socket.Dispose();
                    socket = null;
                }
            }
             */

            //INFO: _tcp_Thread bereinigt am Ende den socket

            if (_tcp_Thread != null)
                _tcp_Thread.Abort();

            Debug.WriteLine(GetType().Name + "::Disconnect() leave");
        }


        public long StateChange
        {
            get { return statechange; }
        }

        public ConnectionState State
        {
            get { return state; }
        }

        public string RemoteHost
        {
            get { return remoteHost; }
        }

        public int RemotePort
        {
            get { return remotePort; }
        }


        public bool SendPacket(MessagePacket p)
        {
            byte[] packet = p.getEscaped();

            if (socket == null || state != ConnectionState.CONNECTED)
                return false;


                try
                {
                int bytesSend;

                    lock (socketLock)
                    {
                        bytesSend = socket.Send(packet, 0, packet.Length, SocketFlags.None);
                    }

                    if (bytesSend != packet.Length)
                    {
                        ExceptionReporter.DoReportError(null, "packet.Length=" + packet.Length + " bytesSend=" + bytesSend, "MessageClient.SendPacket");
                        System.Windows.Forms.MessageBox.Show("packet.Length=" + packet.Length + " bytesSend=" + bytesSend, "MessageClient.SendPacket");
                    }

                    Debug.WriteLineIf(DEBUG_MESSAGES, GetType().Name + ": " + remoteHost + ":" + remotePort + " SendPacket Size=" + p.WritePos + " Data=" + p.ToString());
                    OnMessageSend(p);
                    return true;
                }
                catch (Exception)
                {
                    Debug.WriteLineIf(DEBUG_MESSAGES, GetType().Name + ": " + remoteHost + ":" + remotePort + " FAILED SendPacket Size=" + p.WritePos + " Data=" + p.ToString());
                }

            return false;
        }

        //Löst den OnConnectionState aus
        private void ReportStateChange(ConnectionState newState)
        {
            //10.04.25: In einigen Fällen gibt es hiermit ein DeadLock. Erstmal deaktiviert, es wurden keine negativen Auswirkungen festgestellt
            //lock (socketLock)
            //{
                statechange = Environment.TickCount;
                ConnectionState stateLast = state;
                state = newState;

                OnConnectionState(state, stateLast);
            //}
            

        }

        private void Socket_ThreadProc()
        {
            try
            {
                Debug.WriteLineIf(DEBUG_THREAD, GetType().Name + ": " + remoteHost + ":" + remotePort + " tcp_ThreadProc enter."); // ThreadID=" + _tcp_Thread.ManagedThreadId); Kann hier schon sein das der Thread abgebrochen ist
                
                const int BUFFERSIZE = 32768;
                byte[] buffer = new byte[BUFFERSIZE];
                int bufferpos = 0;
                int buffermax = BUFFERSIZE;
                int bytesRead;

                Debug.WriteLineIf(DEBUG_RECEIVE, GetType().Name + ": " + remoteHost + ":" + remotePort + " begin connect");

                //Connect Socket
                lock (socketLock)
                {
                    socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                    try
                    {
                        socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.TypeOfService, 0x14); //0x14 = IPTOS_RELIABILITY | IPTOS_LOWDELAY
                    }
                    catch (ThreadAbortException e)
                    {
                        throw e;
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLineIf(DEBUG_SOCKET, GetType().Name + ": SetSocketOption FAILED: " + e.Message);
                    }

                    socket.ReceiveTimeout = 250;
                    socket.Connect(remoteHost, remotePort);
                }

                Debug.WriteLineIf(DEBUG_SOCKET, GetType().Name + ": " + remoteHost + ":" + remotePort + " connect done");

                ReportStateChange(ConnectionState.CONNECTED);
                
                //Read Endless
                while (true)
                {
                    Debug.WriteLineIf(DEBUG_THREAD, GetType().Name + ": " + remoteHost + ":" + remotePort + " tcp_ThreadProc alive. ThreadID=" + _tcp_Thread.ManagedThreadId);

                    //Versuche vom Socket zu lesen
                    try
                    {
                        //if (stream.DataAvailable) { bytesRead = stream.Read(buffer, bufferpos, (buffermax - bufferpos)); }
                        //bytesRead = stream.Read(buffer, bufferpos, (buffermax - bufferpos));
                        bytesRead = socket.Receive(buffer, bufferpos, (buffermax - bufferpos), SocketFlags.None, out SocketError socketError);

                        //Socket must have been closed by Remote Peer
                        if ((socketError == SocketError.Success) && (bytesRead == 0))
                        {
                            //System.Windows.Forms.MessageBox.Show("close from remote peer detected");
                            bytesRead = -1;
                        }

                        if (socketError != SocketError.Success && socketError != SocketError.TimedOut)
                        {
                            Debug.WriteLineIf(DEBUG_SOCKET, GetType().Name + ": " + remoteHost + ":" + remotePort + " SocketError: " + socketError);
                            bytesRead = -1;
                        }


                    }
                    catch (ThreadAbortException)
                    {
                        bytesRead = -1;
                    }

                    catch (SocketException ex)
                    {
                        Debug.WriteLineIf(DEBUG_SOCKET, GetType().Name + ": Receive FAILED: " + ex.Message);

                        if (ex.SocketErrorCode == SocketError.WouldBlock ||
                            ex.SocketErrorCode == SocketError.IOPending ||
                            ex.SocketErrorCode == SocketError.NoBufferSpaceAvailable)
                        {
                            // socket buffer is probably empty, wait and try again
                            Thread.Sleep(50);
                            bytesRead = 0;
                        }
                        else
                        {
                            bytesRead = -1;
                        }
                    }

                    catch (Exception)
                    {
                        bytesRead = -1;
                    }

                    //Socket Fehler
                    if (bytesRead == -1)
                    {
                        break; //exit while loop
                    }

                    //Wurden Daten gelesen verarbeite diese
                    else if (bytesRead > 0)
                    {
                        bufferpos += bytesRead;
                        Debug.WriteLineIf(DEBUG_RECEIVE, "MessageClient: " + remoteHost + ":" + remotePort + " Read=" + bytesRead + " BufferSize=" + bufferpos + "/" + buffermax);
                        Debug.WriteLineIf(DEBUG_BUFFERCONTENT, "MessageClient: " + remoteHost + ":" + remotePort + " BUFFER SIZE=" + bufferpos + " DATA=" + BitConverter.ToString(buffer, 0, bufferpos));

                        //Vergrößere buffer, falls notwendig
                        if (buffermax <= bufferpos + bytesRead)
                        {
                            buffermax += BUFFERSIZE;
                            Debug.WriteLineIf(DEBUG_BUFFERCONTENT, "MessageClient: " + remoteHost + ":" + remotePort + " must increase buffer to " + buffermax);

                            byte[] bufferNew = new byte[buffermax];
                            Buffer.BlockCopy(buffer, 0, bufferNew, 0, buffer.Length);
                            buffer = bufferNew;
                        }

                        //Durchsuche den aktuellen Puffer nach einem kompletten Datenpaket       
                        int nPosSTX;
                        int nPosETX;
                        int nPosLastETX = 0;

                        int packetsize;
                        byte[] packet;

                        do
                        {
                            packetsize = FetchPacketFromBuffer(buffer, nPosLastETX, bufferpos, out nPosSTX, out nPosETX, out packet);
                            if (packetsize == 0) break;

                            Debug.WriteLineIf(DEBUG_BUFFERCONTENT, "MessageClient: " + remoteHost + ":" + remotePort + " PACKET SIZE=" + packetsize + " DATA=" + BitConverter.ToString(packet));
                            Debug.WriteLineIf(DEBUG_RECEIVE, "MessageClient: " + remoteHost + ":" + remotePort + " PACKET nPosSTX=" + nPosSTX + " nPosETX=" + nPosETX + " bufferPos: " + bufferpos);

                            //OnMessageRecv
                            MessagePacket messagepacket = new MessagePacket(packet, 0, packetsize);
                            OnMessageRecv(messagepacket);

                            nPosLastETX = nPosETX + 1;
                        }
                        while (packetsize != 0);

                        //Kürze den Buffer wenn mindestens ein Packet ausgeschnitten wurde
                        if (nPosLastETX > 0)
                        {
                            Buffer.BlockCopy(buffer, nPosLastETX, buffer, 0, (bufferpos - nPosLastETX));
                            bufferpos -= nPosLastETX;

                            Debug.WriteLineIf(DEBUG_RECEIVE, "MessageClient: " + remoteHost + ":" + remotePort + " RESIZE bufferpos=" + bufferpos + " nPosLastETX=" + nPosLastETX);
                        }

                    } //if (bytesRead > 0)

                } //while (true)



            }
            //Socket Fehler
            catch (SocketException e)
            {
                //No Connection
                Debug.WriteLineIf(DEBUG_SOCKET, GetType().Name + ": " + remoteHost + ":" + remotePort + " FAILED: " + e.Message);
            }
            //Thread.Abort wurde aufgerufen
            catch (ThreadAbortException e)
            {
                //Disconnect was called or Application aborted
                Debug.WriteLineIf(DEBUG_THREAD, GetType().Name + ": " + remoteHost + ":" + remotePort + " tcp_ThreadProc ThreadAbortException: " + e);
            }
            catch (Exception e)
            {
                ExceptionReporter.DoReportError(e);
            }

            finally
            {
                //Socket schließen
                lock (socketLock)
                {
                    //TODO: Hier kann socket = null schon wieder sein! GRRRRR
                    if (socket != null)
                    {
                        Debug.WriteLineIf(DEBUG_THREAD, GetType().Name + ": " + remoteHost + ":" + remotePort + " tcp_ThreadProc socket closing");

                        try
                        {
                            if (socket.Connected)
                                socket.Shutdown(SocketShutdown.Both);

                            socket.Close();
                            socket.Dispose();
                        }
                        catch (SocketException e)
                        {
                            Debug.WriteLineIf(DEBUG_THREAD, GetType().Name + ": " + remoteHost + ":" + remotePort + " tcp_ThreadProc socket closing error: " + e.Message);
                        }

                        Debug.WriteLineIf(DEBUG_THREAD, GetType().Name + ": " + remoteHost + ":" + remotePort + " tcp_ThreadProc socket closed");
                        socket = null;
                    }
                } //lock (socketLock)
                
                //Fire Event
                ReportStateChange(ConnectionState.DISCONNECTED);

                Debug.WriteLineIf(DEBUG_THREAD, GetType().Name + ": " + remoteHost + ":" + remotePort + " tcp_ThreadProc Thread leave ThreadID=" + _tcp_Thread.ManagedThreadId);
            } //finally

        }

        #region "Internal Functions"

        //Finde STX und ETX Steuerzeichen im Buffer und schneidet, sofern vorhanden, ein entsprechendes Paket aus.
        //Neu 24.04.12 1.14d ETX muss immer hinter STX liegen.
        private int FetchPacketFromBuffer(byte[] buffer, int bufferoffset, int buffersize, out int posstx, out int posetx, out byte[] packet)
        {
            int nPosSTX = -1;
            int nPosETX = -1;
            int nCntESC = 0;

            bool packetskipbyte = false;
            byte[] packetbuffer = new byte[buffersize - bufferoffset];
            int packetbuffersize = 0;


            for (int i = bufferoffset; i < buffersize; i++)
            {
                packetskipbyte = false;

                switch (buffer[i])
                {
                    case ESC: nCntESC++; packetskipbyte = ((nCntESC % 2) != 0); break;
                    case STX: if ((nPosSTX == -1) && ((nCntESC % 2) == 0)) { nPosSTX = i; packetskipbyte = true; } nCntESC = 0; break;
                    case ETX: if ((nPosETX == -1) && (nPosSTX != -1) && ((nCntESC % 2) == 0)) { nPosETX = i; packetskipbyte = true; } nCntESC = 0; break;

                    default: nCntESC = 0; break;
                }

                //INFO: Wenn wir ein PACKET_STX und PACKET_ETX gefunden haben müssen wir diese Schleife verlassen
                if ((nPosETX != -1) && (nPosSTX != -1)) { break; }

                //Das aktuelle byte gehört zum packet
                if (packetskipbyte == false)
                {
                    packetbuffer[packetbuffersize] = buffer[i];
                    packetbuffersize++;
                }

            } //for (int i = 0; i < _buffersize; i++)


            posstx = nPosSTX;
            posetx = nPosETX;

            if ((nPosETX > -1) && (nPosSTX > -1))
            {
                packet = new byte[packetbuffersize];
                Buffer.BlockCopy(packetbuffer, 0, packet, 0, packetbuffersize);
                return packetbuffersize;
            }
            else
            {
                packet = null;
                return 0;
            }
        }

        #endregion

        #region "Callback Function Templates"

        public virtual void OnConnectionState(ConnectionState statenew, ConnectionState stateold)
        {
            //
        }

        public virtual void OnMessageRecv(MessagePacket p)
        {
            //
        }

        public virtual void OnMessageSend(MessagePacket p)
        {
            //
        }

        #endregion

    } //class
    
}
