using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Net;
using System.Net.Sockets;

namespace SuE.Tools
{
    class AsyncTcpClient
    {
        /// <summary>
        /// The default length for the read buffer.
        /// </summary>
        private const int DefaultClientReadBufferLength = 256; //32767; //4096;
        
        /// <summary>
        /// Max number of connect retries.
        /// </summary>
        private const int MaxConnectRetries = 3;
 
        /// <summary>
        /// The tcp client used for the outgoing connection.
        /// </summary>
        private readonly TcpClient client;
 
        /// <summary>
        /// The port to connect to on the remote server.
        /// </summary>
        private readonly int port;
 
        /// <summary>
        /// A reset event for use if a DNS lookup is required.
        /// </summary>
        private readonly ManualResetEvent dnsGetHostAddressesResetEvent = null;
 
        /// <summary>
        /// The length of the read buffer.
        /// </summary>
        private readonly int clientReadBufferLength;
 
        /// <summary>
        /// The addresses to try connection to.
        /// </summary>
        private IPAddress[] addresses;
 
        /// <summary>
        /// How many times to retry connection.
        /// </summary>
        private int retries;
         
        /// <summary>
        /// Occurs when the client connects to the server.
        /// </summary>
        public event EventHandler Connected;
 
        /// <summary>
        /// Occurs when the client disconnects from the server.
        /// </summary>
        public event EventHandler Disconnected;
 
        /// <summary>
        /// Occurs when data is read by the client.
        /// </summary>
        public event EventHandler<DataReadEventArgs> DataRead;
 
        /// <summary>
        /// Occurs when data is written by the client.
        /// </summary>
        public event EventHandler<DataWrittenEventArgs> DataWritten;
 
        /// <summary>
        /// Occurs when an exception is thrown during connection.
        /// </summary>
        public event EventHandler<ExceptionEventArgs> ClientConnectException;
 
        /// <summary>
        /// Occurs when an exception is thrown while reading data.
        /// </summary>
        public event EventHandler<ExceptionEventArgs> ClientReadException;
 
        /// <summary>
        /// Occurs when an exception is thrown while writing data.
        /// </summary>
        public event EventHandler<ExceptionEventArgs> ClientWriteException;
 
        /// <summary>
        /// Occurs when an exception is thrown while performing the DNS lookup.
        /// </summary>
        public event EventHandler<ExceptionEventArgs> DnsGetHostAddressesException;
 
        /// <summary>
        /// Constructor for a new client object based on a host name or server address string and a port.
        /// </summary>
        /// <param name="hostNameOrAddress">The host name or address of the server as a string.</param>
        /// <param name="port">The port on the server to connect to.</param>
        /// <param name="clientReadBufferLength">The clients read buffer length.</param>
        public AsyncTcpClient(string hostNameOrAddress, int port, int clientReadBufferLength = DefaultClientReadBufferLength)
            : this(port, clientReadBufferLength)
        {
            this.dnsGetHostAddressesResetEvent = new ManualResetEvent(false);
            Dns.BeginGetHostAddresses(hostNameOrAddress, this.DnsGetHostAddressesCallback, null);
        }
 
        /// <summary>
        /// Constructor for a new client object based on a number of IP Addresses and a port.
        /// </summary>
        /// <param name="addresses">The IP Addresses to try connecting to.</param>
        /// <param name="port">The port on the server to connect to.</param>
        /// <param name="clientReadBufferLength">The clients read buffer length.</param>
        public AsyncTcpClient(IPAddress[] addresses, int port, int clientReadBufferLength = DefaultClientReadBufferLength)
            : this(port, clientReadBufferLength)
        {
            this.addresses = addresses;
        }
 
        /// <summary>
        /// Constructor for a new client object based on a single IP Address and a port.
        /// </summary>
        /// <param name="address">The IP Address to try connecting to.</param>
        /// <param name="port">The port on the server to connect to.</param>
        /// <param name="clientReadBufferLength">The clients read buffer length.</param>
        public AsyncTcpClient(IPAddress address, int port, int clientReadBufferLength = DefaultClientReadBufferLength)
            : this(new[] {address}, port, clientReadBufferLength)
        {
        }
 
        /// <summary>
        /// Private constructor for a new client object.
        /// </summary>
        /// <param name="port">The port on the server to connect to.</param>
        /// <param name="clientReadBufferLength">The clients read buffer length.</param>
        private AsyncTcpClient(int port, int clientReadBufferLength)
        {
            this.client = new TcpClient();
            this.port = port;
            this.clientReadBufferLength = clientReadBufferLength;
            this.retries = 0;
        }
 
        /// <summary>
        /// Starts an asynchronous connection to the remote server.
        /// </summary>
        public void Connect()
        {
            if (this.dnsGetHostAddressesResetEvent != null)
                this.dnsGetHostAddressesResetEvent.WaitOne();

            this.retries = 0;
            this.client.BeginConnect(this.addresses, this.port, this.ClientConnectCallback, null);
        }

        public void Close()
        {
         this.client.Close();

        }
 
        /// <summary>
        /// Writes a string to the server using a given encoding.
        /// </summary>
        /// <param name="value">The string to write.</param>
        /// <param name="encoding">The encoding to use.</param>
        /// <returns>A Guid that can be used to match the data written to the confirmation event.</returns>
        public Guid Write(string value, Encoding encoding)
        {
            byte[] buffer = encoding.GetBytes(value);
            return this.Write(buffer);
        }
 
        /// <summary>
        /// Writes a byte array to the server.
        /// </summary>
        /// <param name="buffer">The byte array to write.</param>
        /// <returns>A Guid that can be used to match the data written to the confirmation event.</returns>
        public Guid Write(byte[] buffer)
        {
            Guid guid = Guid.NewGuid();
            NetworkStream networkStream = this.client.GetStream();
            networkStream.BeginWrite(buffer, 0, buffer.Length, this.ClientWriteCallback, guid);
            return guid;
        }

        /// <summary>
        /// Writes a byte array to the server.
        /// </summary>
        /// <param name="buffer">The byte array to write.</param>
        /// <returns>A Guid that can be used to match the data written to the confirmation event.</returns>
        public Guid Write(byte[] buffer, int offset, int size)
        {
            Guid guid = Guid.NewGuid();
            NetworkStream networkStream = this.client.GetStream();
            networkStream.BeginWrite(buffer, offset, size, this.ClientWriteCallback, guid);
            return guid;
        }

 
        /// <summary>
        /// Callback from the asynchronous DNS lookup.
        /// </summary>
        /// <param name="asyncResult">The result of the async operation.</param>
        private void DnsGetHostAddressesCallback(IAsyncResult asyncResult)
        {
            try
            {
                this.addresses = Dns.EndGetHostAddresses(asyncResult);
                this.dnsGetHostAddressesResetEvent.Set();
            }
            catch (Exception ex)
            {
                if (this.DnsGetHostAddressesException != null)
                    this.DnsGetHostAddressesException(this, new ExceptionEventArgs(ex));
            }
        }
 
        /// <summary>
        /// Callback from the asynchronous Connect method.
        /// </summary>
        /// <param name="asyncResult">The result of the async operation.</param>
        private void ClientConnectCallback(IAsyncResult asyncResult)
        {
            try
            {
                this.client.EndConnect(asyncResult);
                if (this.Connected != null)
                    this.Connected(this, new EventArgs());
            }
            catch (Exception ex)
            {
                // implement simple retry logic using 'retries'
                retries++;
                if (retries < MaxConnectRetries)
                {
                    try
                    {
                        this.client.BeginConnect(this.addresses, this.port, this.ClientConnectCallback, null);
                        return;
                    }
                    catch (Exception inner)
                    {
                        // fall through and notify using original exception if reconnect also fails synchronously
                        ex = inner;
                    }
                }

                if (this.ClientConnectException != null)
                    this.ClientConnectException(this, new ExceptionEventArgs(ex));
                return;
            }
 
            try
            {
                NetworkStream networkStream = this.client.GetStream();
                byte[] buffer = new byte[this.clientReadBufferLength];
                networkStream.BeginRead(buffer, 0, buffer.Length, this.ClientReadCallback, buffer);
            }
            catch (Exception ex)
            {
                if (this.ClientReadException != null)
                    this.ClientReadException(this, new ExceptionEventArgs(ex));
            }
        }
 
        /// <summary>
        /// Callback from the asynchronous Read method.
        /// </summary>
        /// <param name="asyncResult">The result of the async operation.</param>
        private void ClientReadCallback(IAsyncResult asyncResult)
        {
            //MOD: MCSchermer: Der Socket ist bereits geschlossen, es muss keine Ausnahme mehr ausgelöst werden.
            if (this.client.Connected == false)
            {
                if (this.ClientReadException != null)
                    this.ClientReadException(this, new ExceptionEventArgs(null));

                return;
            }


            try
            {
                NetworkStream networkStream = this.client.GetStream();
                int read = networkStream.EndRead(asyncResult);
 
                if (read == 0)
                {
                    this.client.Close();

                    if (this.Disconnected != null)
                        this.Disconnected(this, new EventArgs());

                    return;
                }
 
                byte[] buffer = asyncResult.AsyncState as byte[];
                if (buffer != null)
                {
                    byte[] data = new byte[read];
                    Buffer.BlockCopy(buffer, 0, data, 0, read);
                    networkStream.BeginRead(buffer, 0, buffer.Length, this.ClientReadCallback, buffer);
                    if (this.DataRead != null)
                        this.DataRead(this, new DataReadEventArgs(data));
                }
            }
            catch (Exception ex)
            {
                if (this.ClientReadException != null)
                    this.ClientReadException(this, new ExceptionEventArgs(ex));
            }
        }
 
        /// <summary>
        /// Callback from the asynchronous write callback.
        /// </summary>
        /// <param name="asyncResult">The result of the async operation.</param>
        private void ClientWriteCallback(IAsyncResult asyncResult)
        {
            try
            {
                NetworkStream networkStream = this.client.GetStream();
                networkStream.EndWrite(asyncResult);
                Guid guid = (Guid)asyncResult.AsyncState;
                if (this.DataWritten != null)
                    this.DataWritten(this, new DataWrittenEventArgs(guid));
            }
            catch (Exception ex)
            {
                if (this.ClientWriteException != null)
                    this.ClientWriteException(this, new ExceptionEventArgs(ex));
            }
        }
    }
 
    /// <summary>
    /// Provides data for an exception occuring event.
    /// </summary>
    public class ExceptionEventArgs : EventArgs
    {
        /// <summary>
        /// Constructor for a new Exception Event Args object.
        /// </summary>
        /// <param name="ex">The exception that was thrown.</param>
        public ExceptionEventArgs(Exception ex)
        {
            this.Exception = ex;
        }
 
        public Exception Exception { get; private set; }
    }
 
    /// <summary>
    /// Provides data for a data read event.
    /// </summary>
    public class DataReadEventArgs : EventArgs
    {
        /// <summary>
        /// Constructor for a new Data Read Event Args object.
        /// </summary>
        /// <param name="data">The data that was read from the remote host.</param>
        public DataReadEventArgs(byte[] data)
        {
            this.Data = data;
        }
 
        /// <summary>
        /// Gets the data that has been read.
        /// </summary>
        public byte[] Data { get; private set; }
    }
 
    /// <summary>
    /// Provides data for a data write event.
    /// </summary>
    public class DataWrittenEventArgs : EventArgs
    {
        /// <summary>
        /// Constructor for a Data Written Event Args object.
        /// </summary>
        /// <param name="guid">The guid of the data written.</param>
        public DataWrittenEventArgs(Guid guid)
        {
            this.Guid = guid;
        }
 
        /// <summary>
        /// Gets the Guid used to match the data written to the confirmation event.
        /// </summary>
        public Guid Guid { get; private set; }
    }

}
