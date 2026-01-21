using SuE.Tools;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SuE.TaMi
{
    public class TaMiClient : MessageClient
    {
        public static char SEP_CHAR_COL = '|';
        public static char SEP_CHAR_ROW = '¦';

        private const bool DEBUG_SENDRECV = false;
        private const bool DEBUG_MESSAGES = true;
        private const bool DEBUG_MESSAGEDETAILS = false;

        private const ushort IP_PROTO_VERS = 0x82; //Version 1.18
        
        public delegate void EnabledChange(TaMiClient tamiClient, bool Enabled);
        public event EnabledChange OnEnabledChange;

        /// <summary>
        /// Occurs when the connection to the server changed.
        /// </summary>
        public event EventHandler<ConnectionStateChangeEventArgs> ConnectionStateChange;

        /// <summary>
        /// Occurs when the server sends a LOGINMSG to this client.
        /// </summary>
        public event EventHandler<LoginMessageEventArgs> LoginMessage;

        /// <summary>
        /// Occurs when the server sends a CONFIGMSG to this client.
        /// </summary>
        public event EventHandler<ConfigMessageEventArgs> ConfigMessage;

        /// <summary>
        /// Occurs when the server sends a VEHICLEMANMSG to this client.
        /// </summary>
        public event EventHandler<VehicleManagerMessageEventArgs> VehicleManagerMessage;

        /// <summary>
        /// Occurs when the server sends a ZONEMANMSG to this client.
        /// </summary>
        public event EventHandler<ZoneManagerMessageEventArgs> ZoneManagerMessage;

        /// <summary>
        /// Occurs when the server sends a JOBMSG of type GETLIST to this client.
        /// </summary>
        public event EventHandler<JobGetListMessageEventArgs> JobGetListMessage;

        /// <summary>
        /// Occurs when the server sends a JOBMSG of type JOBSYNC to this client.
        /// </summary>
        public event EventHandler<JobSyncMessageEventArgs> JobSyncMessage;

        /// <summary>
        /// Occurs when the server sends a JOBREPMSG of type DELSUBITEMS to this client.
        /// </summary>
        public event EventHandler<JobGetListMessageEventArgs> JobsRemovedMessage;


        private string      mId;
        private bool        mEnabled;

        string              mRemoteHost;
        int                 mRemotePort;
        
        private int         mUserId = -1;
        private string      mPassword = "SuE";
        private string      mSessionId = "";
        private int         mLogInResult = -1; //Not logged in

        private int         mAppRights1 = 0;
        private int         mAppRights2 = 0;
        private string      mUsername1 = string.Empty;
        private string      mUsername2 = string.Empty;
        
        private byte        mLastSendMsgId = Byte.MaxValue;

        private int         mLastMessageRecvTicks;
        private byte        mLastProccessedMsgId;
        

        private string      mDatabaseServer;
        private string      mDatabaseName;
        private string      mDatabaseUser;
        private string      mDatabasePass;
        
        private string      mRegName1;
        private string      mRegName2;
        private string      mRegSerial;
        private int         mRegFhz;
        private int         mRegCustomerId;

        private Config          mConfig;
        private Zonen           mZonen;
        private FahrzeugGruppen mFahrzeugGruppen;
        private Fahrzeuge       mFahrzeuge;
        private PersonalMan     mPersonal;
        private Jobs            mJobs;

        private Tarife          mTarife;


        public TaMiClient(string id, string host, int port, string sessionId)
        {
            this.Init(id, host, 63500, sessionId, string.Empty, string.Empty, string.Empty, string.Empty);
        }

        public TaMiClient(string id, string host, int port, string sessionId, string databaseserver, string databasename, string databaseuser, string databasepass) 
        {
            this.Init(id, host, port, sessionId, databaseserver, databasename, databaseuser, databasepass);
        }

        #region "Functions"

        private void Init(string id, string host, int port, string sessionId, string databaseserver, string databasename, string databaseuser, string databasepass)
        {
            mId = id;
            mEnabled = false;

            mRemoteHost = host;
            mRemotePort = port;

            mSessionId = sessionId;

            mDatabaseServer = databaseserver;
            mDatabaseName = databasename;
            mDatabaseUser = databaseuser;
            mDatabasePass = databasepass;

            //Thread thread = Thread.CurrentThread;
            //TaMiTools.NUMBER_DECIMAL_SEP = thread.CurrentCulture.NumberFormat.NumberDecimalSeparator;
            //TaMiTools.NUMBER_THOUSAND_SEP = thread.CurrentCulture.NumberFormat.NumberGroupSeparator;

            mRegName1 = "";
            mRegName2 = "";
            mRegSerial = "";
            mRegFhz = 0;
            mRegCustomerId = 0;

            mLastMessageRecvTicks = Environment.TickCount & Int32.MaxValue;
            mLastProccessedMsgId = 0;

            mConfig = new Config();
            mConfig.TaMiClient = this;
            mConfig.Application = ConfigTag.APP_DISPO;

            mZonen = new Zonen();
            mZonen.TaMiClient = this;

            mFahrzeugGruppen = new FahrzeugGruppen();
            mFahrzeugGruppen.TaMiClient = this;

            mFahrzeuge = new Fahrzeuge();
            mFahrzeuge.TaMiClient = this;

            mPersonal = new PersonalMan();
            mPersonal.TaMiClient = this;

            mJobs = new Jobs();
            mJobs.TaMiClient = this;

            mTarife = new Tarife();
            mTarife.TaMiClient = this;
        }

        private byte GetNewMsgID()
        {
            if (mLastSendMsgId == Byte.MaxValue) mLastSendMsgId = 1; else mLastSendMsgId++;
            return mLastSendMsgId;
        }

        public bool HasAppRight(AppRights1 right)
        {
            return (mAppRights1 & (int)right) == (int)right;
        }

        public bool HasAppRight(AppRights2 right)
        {
            return (mAppRights2 & (int)right) == (int)right;
        }

        public void Connect(int userid, string password)
        {
            mLastMessageRecvTicks = Environment.TickCount & Int32.MaxValue;

            mUserId = userid;
            mPassword = password;

            base.Connect(mRemoteHost, mRemotePort);
        }


        public new bool SendPacket(MessagePacket packet)
        {
            if (!mEnabled)
                return false;

            if (DEBUG_SENDRECV || true)
            {
                TAC tac   = (TAC)packet.getByteB();
                byte msgid = packet.getByteB();

                Debug.WriteLine($"TaMiClient: {this} SEND {tac} MsgId: {msgid}");
            }

            return base.SendPacket(packet);
        }

        //Login
        public ResultCodes Login(AppId appid, string sessionid, NotifyFlags notifymask, int userid, string password)
        {
            byte msgId = SendLoginREQ(appid, sessionid, notifymask, userid, password);

            if (msgId == 0)
                return ResultCodes.UNKNOWN;

            if (WaitForMsgId(msgId))
            {
                return (ResultCodes)mLogInResult;
            }
            else
            {
                return ResultCodes.TIMEOUT;
            }
        }

        //LoginAsync
        public async Task<ResultCodes> LoginAsync(AppId appid, string sessionid, NotifyFlags notifymask, int userid, string password)
        {
            byte msgId = SendLoginREQ(appid, sessionid, notifymask, userid, password);

            if (msgId == 0)
                return ResultCodes.UNKNOWN;

            int checkfrequencyMs = 25;
            int timeoutMs = 8000;
            int waitTime = 0;

            while (msgId != mLastProccessedMsgId)
            {
                await Task.Delay(checkfrequencyMs);
                waitTime += checkfrequencyMs;

                if (waitTime >= timeoutMs)
                    return ResultCodes.TIMEOUT;

                else if (State != ConnectionState.CONNECTED)
                    return ResultCodes.TIMEOUT;
            }

            return (ResultCodes)mLogInResult;
        }


        public byte SendLoginREQ(AppId appid, string sessionid, NotifyFlags notifymask, int userid, string password)
        {
            //VernamCode Password
            string szPassword = "";
            byte[] KEY = Encoding.UTF8.GetBytes("hiJ2%6K12nmhr4O22p=7/");
            for (int i = 0; i < password.Length; i++)
                { szPassword += (char)(((byte)password.ToCharArray(i, 1)[0]) ^ KEY[i]); }

            //Datenpaket erstellen
            byte msgid = GetNewMsgID();
            MessagePacket packet = new MessagePacket();

            packet.putByte((byte)TAC.LOGINREQ);
            packet.putByte(msgid);
            packet.putShort(IP_PROTO_VERS);
            packet.putByte((byte)appid);
            packet.putInt(0); //Flags
            packet.putInt((int)notifymask);
            packet.putInt(userid);
            packet.putShort(szPassword.Length);
            packet.putString(szPassword);
            packet.putShort(sessionid.Length); //SessionId
            packet.putString(sessionid); //SessionId String

            if (SendPacket(packet))
                return msgid;
            else
                return 0;
        }

        public byte SendKeepAliveREQ()
        {
            //Datenpaket erstellen
            byte msgid = GetNewMsgID();
            MessagePacket packet = new MessagePacket( );
            packet.putByte((byte)TAC.KEEPALIVEREQ);
            
            SendPacket(packet);

            //Erstmal so, damit bei langen Übertragungszeiten nicht permanent neue KeepAliveREQ an den Server gesendetet werden
            mLastMessageRecvTicks = Environment.TickCount & Int32.MaxValue;

            return msgid;
        }

        public byte SendReqFhzPosStatusUpdate(Fahrzeug fahrzeug)
        {
            //Datenpaket erstellen
            byte msgid = GetNewMsgID();

            MessagePacket packet = new MessagePacket();

            packet.putByte((byte)TAC.VEHICLEMANREQ);
            packet.putByte(msgid);
            packet.putByte((byte)VehicleManAction.REQPOSUPDATE);
            packet.putShort((short)0);  //Flags
            packet.putInt(fahrzeug.Id); //FhzId

            SendPacket(packet);
            return msgid;
        }

        //Zum debuggen Fhz Position verändern.
        public byte SendSetFhzPosStatus(Fahrzeug fahrzeug, byte newStatus, double newLat, double newLng)
        {
            //Datenpaket erstellen
            byte msgid = GetNewMsgID();

            MessagePacket packet = new MessagePacket();

            packet.putByte((byte)TAC.VEHICLEMANREQ);
            packet.putByte(msgid);
            packet.putByte((byte)VehicleManAction.SETPOSSTATUS);
            packet.putShort((short)0);  //Flags
            packet.putInt(fahrzeug.Id); //FhzId
            packet.putByte(newStatus);  //Neuer StatusId
            packet.putDouble(newLat);   //Lat Neu
            packet.putDouble(newLng);   //Lng Neu

            SendPacket(packet);
            return msgid;
        }

        //Fahrzeugliste vom Server abfragen
        public byte SendGetFahrzeugList(VehicleManAction action)
        {
            //if (mMsgIDFahrzeugSyncStatic > 0) { Log.v(LOG_TAG, "sendGetFahrzeugListReq but waiting for MsgID=" + mMsgIDFahrzeugSyncStatic); return -1; }

            //Datenpaket erstellen
            byte msgid = GetNewMsgID();

            MessagePacket packet = new MessagePacket();

            packet.putByte((byte)TAC.VEHICLEMANREQ);
            packet.putByte(msgid);
            packet.putByte((byte)action);
            packet.putByte(1);  //Version
            packet.putShort((short)SyncFlags.FULLSYNC); //Flags
            packet.putString("19700101000000"); //p.putString(Util.Calendar2String(mJobsOffen.getLastSync(), true));

            //Log.v(LOG_TAG, "VEHICLEMANREQ MsgID=" + msgid + " Action=" + action);
            //if (mMsgSocket.addPacket(p)) ;
            //{
            //    mMsgIDFahrzeugSyncStatic = msgid;
            //}

            SendPacket(packet);
            return msgid;
        }

        public bool GetFahrzeugList(VehicleManAction action)
        {
            return WaitForMsgId(SendGetFahrzeugList(action));
        }

        //Personalliste vom Server anfragen
        public byte SendGetPersonalList(PersonalManAction action)
        {
            //if (mMsgIDFahrzeugSyncStatic > 0) { Log.v(LOG_TAG, "sendGetFahrzeugListReq but waiting for MsgID=" + mMsgIDFahrzeugSyncStatic); return -1; }

            //Datenpaket erstellen
            byte msgid = GetNewMsgID();

            MessagePacket packet = new MessagePacket();

            packet.putByte((byte)TAC.PERSONALMANREQ);
            packet.putByte(msgid);
            packet.putByte((byte)action);
            packet.putByte(1);  //Version
            packet.putShort((short)SyncFlags.FULLSYNC); //Flags
            packet.putString("19700101000000"); //p.putString(Util.Calendar2String(mJobsOffen.getLastSync(), true));

            //Log.v(LOG_TAG, "VEHICLEMANREQ MsgID=" + msgid + " Action=" + action);
            //if (mMsgSocket.addPacket(p)) ;
            //{
            //    mMsgIDFahrzeugSyncStatic = msgid;
            //}

            SendPacket(packet);
            return msgid;
        }

        public bool GetPersonalList(PersonalManAction action)
        {
            return WaitForMsgId(SendGetPersonalList(action));
        }

        //Zonenliste abrufen
        public byte SendGetZoneList(ZoneManAction action)
        {
            //if (mMsgIDFahrzeugSyncStatic > 0) { Log.v(LOG_TAG, "sendGetFahrzeugListReq but waiting for MsgID=" + mMsgIDFahrzeugSyncStatic); return -1; }

            //Datenpaket erstellen
            byte msgid = GetNewMsgID();

            MessagePacket packet = new MessagePacket();

            packet.putByte((byte)TAC.ZONEMANREQ);
            packet.putByte(msgid);
            packet.putByte((byte)action);
            packet.putByte(1);  //Version
            packet.putShort((short)SyncFlags.FULLSYNC); //Flags
            packet.putString("19700101000000"); //p.putString(Util.Calendar2String(mJobsOffen.getLastSync(), true));

            //Log.v(LOG_TAG, "VEHICLEMANREQ MsgID=" + msgid + " Action=" + action);
            //if (mMsgSocket.addPacket(p)) ;
            //{
            //    mMsgIDFahrzeugSyncStatic = msgid;
            //}

            SendPacket(packet);
            return msgid;
        }

        public bool GetZoneList(ZoneManAction action)
        {
            return WaitForMsgId(SendGetZoneList(action));
        }


        //Jobliste vom Server abrufen
        public byte SendGetJobList()
        {
            //if (mMsgIDFahrzeugSyncStatic > 0) { Log.v(LOG_TAG, "sendGetFahrzeugListReq but waiting for MsgID=" + mMsgIDFahrzeugSyncStatic); return -1; }

            //Datenpaket erstellen
            byte msgid = GetNewMsgID();

            MessagePacket packet = new MessagePacket();

            packet.putByte((byte)TAC.JOBREQ);
            packet.putByte(msgid);
            packet.putByte((byte)JobAction.GETLIST);
            packet.putByte(1);  //Version
            packet.putShort((short)SyncFlags.FULLSYNC); //Flags
            packet.putString("19700101000000"); //p.putString(Util.Calendar2String(mJobsOffen.getLastSync(), true));
            packet.putByte(0); //Alle Jobs

            //Log.v(LOG_TAG, "JOBREQ MsgID=" + msgid + " Action=" + action);
            //if (mMsgSocket.addPacket(p)) ;
            //{
            //    mMsgIDJobSyncStatic = msgid;
            //}

            SendPacket(packet);
            return msgid;
        }

        public bool GetJobList()
        {
            return WaitForMsgId(SendGetJobList());
        }



        public byte SendUpdateJob(Job job, bool updateBearbeiter)
        {
            if (job == null) 
                throw new ArgumentNullException("job");

            //Bearbeiter aktualisieren
            if (updateBearbeiter)
            {
                job.ZeitBearbeitet = DateTime.Now;
                if (mUserId != 0)
                    job.UserBearbeitet = mUserId;
            }

            //Datenpaket erstellen
            byte msgid = GetNewMsgID();
            MessagePacket packet = new MessagePacket();
            packet.putByte((byte)TAC.JOBREQ);
            packet.putByte(msgid);
            packet.putByte((byte)JobAction.SET);
            packet.putString(job.SerializeStatic());

            SendPacket(packet);
            return msgid;
        }

        public byte SendDispatchJobReq(Job job, Fahrzeug fhz, int flags)
        {
            if (job == null) 
                throw new ArgumentNullException("job");

            if (fhz == null) 
                throw new ArgumentNullException("fhz");

            //Datenpaket erstellen
            byte msgid = GetNewMsgID();

            MessagePacket packet = new MessagePacket();
            packet.putByte((byte)TAC.JOBREQ);
            packet.putByte(msgid);
            packet.putByte((byte)JobAction.DISPATCH);
            packet.putString(job.JobId + "|" + fhz.Id + "|" + flags);

            SendPacket(packet);  
            return msgid;
        }

        //Geht alle vermittelten Aufträge durch und ordnet diese den Fahrzeugen zu
        public List<Fahrzeug> RefreshJobCountForAllFahrzeuge()
        {
            List<Fahrzeug> fahrzeugeChanged = new List<Fahrzeug>();

            //Bei allen Fahrzeugen, die Anzahl der vermittelten Aufträge erstmal auf 0 setzen.
            foreach (Fahrzeug fhz in mFahrzeuge)
            {
                if (fhz.ClearDispatchedJobs())
                    fahrzeugeChanged.Add(fhz);
            }

            //Gehe alle Aufträge durch und ordne die Aufträge im JobStatus.VERMITTELT dem jeweiligen Fahrzeug zu
            foreach (Job job in mJobs)
            {
                if (job.Status == JobStatus.VERMITTELT)
                {
                    Fahrzeug fhz = mFahrzeuge[job.FahrzeugId];
                    if (fhz != null)
                    {
                        fhz.AddDispatchedJob(job);

                        if (!fahrzeugeChanged.Contains(fhz))  
                            fahrzeugeChanged.Add(fhz);
                    }
                }
            }

            return fahrzeugeChanged;
        }


        //Prüfe, wenn das Fahrzeug existiert und gültig ist, welche Optionen vom Fahrzeug des Auftrags nicht erfüllt werden
        public string[] GetMissingOptions(Job job)
        {
            if (job == null)
            {
                string[] emptyResult = { string.Empty, string.Empty };
                return emptyResult;
            }

            if (job.FahrzeugId == 0)
            {
                string[] emptyResult = { string.Empty, job.Optionen };
                return emptyResult;
            }

            Fahrzeug fahrzeug = this.Fahrzeuge[job.FahrzeugId];
            if (fahrzeug == null)
            {
                string[] emptyResult = { string.Empty, job.Optionen };
                return emptyResult;
            }

            return Optionen.SplitMissingOptions(job.Optionen, fahrzeug.Optionen);
        }

        public override string ToString()
        {
            return "TaMiClient: " + mId;
        }

        #endregion

        #region "Internal Functions"

        /// <summary>
        /// Blocks while waiting for MsgId from Server or timeout.
        /// </summary>
        private bool WaitForMsgId(byte msgId, int checkfrequencyMs = 25, int timeoutMs = 8000)
        {
            int waitTime = 0;

            if (msgId == 0)
                throw new Exception("cant wait for msgId 0");

            if (DEBUG_MESSAGES)
                Debug.WriteLine($"TaMiClient: {this}: WaitForMsgId  {msgId}");

            while (msgId != mLastProccessedMsgId)
            { 
                Thread.Sleep(checkfrequencyMs); //Task.Delay(checkfrequencyMs);
                waitTime += checkfrequencyMs;

                if (waitTime >= timeoutMs)
                {
                    if (DEBUG_MESSAGES) { Debug.WriteLine($"TaMiClient: {this}: WaitForMsgId  {msgId} TIMEOUT AFTER: {timeoutMs}"); }

                    return false;
                }
                else if (State != ConnectionState.CONNECTED)
                {
                    if (DEBUG_MESSAGES) { Debug.WriteLine($"TaMiClient: {this}: WaitForMsgId  {msgId} DISCONNECTED AFTER: {timeoutMs}"); }
                    return false;
                }

            }

            if (DEBUG_MESSAGES)
                Debug.WriteLine($"TaMiClient: {this}: WaitForMsgId  {msgId} success after {waitTime} ms.");

            return true;
        }

        #endregion

        #region "Propertys"

        public string Id
        {
            get { return mId; }
        }

        public bool Enabled
        {
            get { return mEnabled; }
            set
            {
                if (mEnabled != value)
                {
                    mEnabled = value;
                    if (OnEnabledChange != null)
                        OnEnabledChange(this, value);

                    if (!mEnabled) this.Disconnect();
                }

            }
        }

        //Gibt die Zeit in ms seit dem letzten empfangenen Datenpaket vom Server
        public int LastMessageAge
        {
            get
            {
                int current = Environment.TickCount & Int32.MaxValue;
                return Math.Abs(current - mLastMessageRecvTicks);
            }
        }

        new public string RemoteHost
        {
            get { return mRemoteHost; }
        }

        new public int RemotePort
        {
            get { return mRemotePort; }
        }

        public int UserId
        {
            get { return mUserId; }
        }

        public string Username1
        {
            get { return mUsername1; }
        }

        public string Username2
        {
            get { return mUsername2; }
        }

        public string SessionId
        {
            get { return mSessionId; }
        }

        public string DatabaseServer
        {
            get { return mDatabaseServer; }
        }

        public string DatabaseName
        {
            get { return mDatabaseName; }
        }

        public string DatabaseUser
        {
            get { return mDatabaseUser; }
        }

        public string DatabasePass
        {
            get { return mDatabasePass; }
        }

        public string DatabaseConnectionStr
        {
            get
            {
                return string.Format("Server={0};UID={1};Pwd={2};Database={3}", mDatabaseServer, mDatabaseUser, mDatabasePass, mDatabaseName);
            }
        }

        //Stellt eine Verbindung zur TaMi Datenbank her
        public SqlConnection OpenTaMiDB(bool showerror)
        {
            if (mDatabaseServer == null || mDatabaseServer.Length == 0) return null;
            if (mDatabaseName == null   || mDatabaseName.Length == 0) return null;

            return this.OpenSQLConnection(this.DatabaseConnectionStr, showerror);
        }

        //Stellt eine Verbindung zur FleetMap Datenbank her
        public SqlConnection OpenFleetMapDB(bool showerror)
        {
            if (mDatabaseServer == null || mDatabaseServer.Length == 0) return null;
            //if (mDatabaseName == null || mDatabaseName.Length == 0) return null;

            string connectionString;

            //Special: Bei SuE ist die FleetMap Datenbank immer auf dem Server
            if (mRegCustomerId == 1 && mDatabaseServer.Equals("(local)\\SQLExpress"))
                connectionString = "Server=192.168.255.1;UID=FleetMap;Pwd=fleetmap;Database=SuE-FleetMap";
            
            else
                connectionString = string.Format("Server={0};UID=FleetMap;Pwd=fleetmap;Database=SuE-FleetMap", mDatabaseServer);

            return this.OpenSQLConnection(connectionString, showerror);
        }

        public string GetFleetMapConnString()
        {
            if (mDatabaseServer == null || mDatabaseServer.Length == 0) return null;

            string dbName = "SuE-FleetMap";
            int pDelim = mDatabaseName.LastIndexOf("-");
            
            if (pDelim > 3) //Nicht SuE-TaMi, sondern SuE-TaMi-XXXX
            {
                dbName += mDatabaseName.Substring(pDelim);
            }

            //Special: Bei SuE PC-Stefan ist die FleetMap Datenbank immer auf dem Server
            if (mRegCustomerId == 1 && (mDatabaseServer.Equals("(local)\\SQLExpress") || mDatabaseServer.StartsWith("192.168.255.12"))  )
                return string.Format("Server=192.168.255.2;UID=FleetMap;Pwd=fleetmap;Database={0}", dbName);

            else
                return string.Format("Server={0};UID=FleetMap;Pwd=fleetmap;Database={1}", mDatabaseServer, dbName);
        }


        //Stellt eine Verbindung zu einer SQLDatenbank her
        private SqlConnection OpenSQLConnection(string connectionString, bool showerror)
        {
            if (connectionString == null || connectionString.Length == 0) 
                return null;

            string errorMessage = "";

            SqlConnection conn = null;

        RetryDB:
            try
            {
                conn = new SqlConnection();
                conn.ConnectionString = connectionString;
                conn.Open();
            }
            catch (SqlException e)
            {
                errorMessage = "Cn: " + connectionString.RemoveBetween(0, "Pwd=", ";") +
                               "\n\nSqlException Code: " + e.Number +
                               "\nMessage: " + e.Message;
                conn = null;
            }
            catch (Exception e)
            {
                errorMessage = "Cn: " + connectionString.RemoveBetween(0, "Pwd=", ";") +
                               "\n\nException: " + e.Message + 
                               "\nDetails: " + e.ToString();
                conn = null;
            }

            //Fehlermeldung anzeigen
            if (showerror && string.IsNullOrEmpty(errorMessage) == false)
            {
                if (System.Windows.Forms.MessageBox.Show("Datenbankverbindung konnte nicht hergestellt werden.\n\n" + errorMessage, "TaMiClient.OpenSQLConnection", System.Windows.Forms.MessageBoxButtons.RetryCancel, System.Windows.Forms.MessageBoxIcon.Warning) == System.Windows.Forms.DialogResult.Retry)
                {
                    goto RetryDB;
                }
            }

            return conn;
        }


        public SqlDataReader QueryTaMiDB(string sqlCommand, bool showerror)
        {
            SqlConnection connTaMi = this.OpenTaMiDB(showerror);
            if (connTaMi == null) return null;

            SqlCommand cmd = new SqlCommand(sqlCommand, connTaMi);
            SqlDataReader rdr = cmd.ExecuteReader(System.Data.CommandBehavior.CloseConnection);

            return rdr;
        }


        public int RegCustomerId
        {
            get { return mRegCustomerId; }
        }

        public Config Config
        {
            get { return mConfig; }
        }

        public Zonen Zonen
        {
            get { return mZonen; }
        }

        public FahrzeugGruppen FahrzeugGruppen
        {
            get { return mFahrzeugGruppen; }
        }

        public Fahrzeuge Fahrzeuge
        {
            get { return mFahrzeuge; }
        }

        public PersonalMan PersonalMan
        {
            get { return mPersonal; }
        }

        public Jobs Jobs
        {
            get { return mJobs; }
        }

        public Tarife Tarife
        {
            get { return mTarife; }
        }

        #endregion

        #region "MessageSocket Callbacks"

        public override void OnConnectionState(ConnectionState statenew, ConnectionState stateold)
        {
            Debug.WriteLineIf(DEBUG_MESSAGES, $"TaMiClient: {this}: STATE: {RemoteHost}:{RemotePort} State {stateold} -> {statenew}");
            base.OnConnectionState(statenew, stateold);
            
            switch (statenew)
            {
                case ConnectionState.DISCONNECTED:
                {
                    mLogInResult = -1;

                    mAppRights1 = 0;
                    mAppRights2 = 0;
                    mUsername1 = string.Empty;
                    mUsername2 = string.Empty;

                    mRegName1 = "";
                    mRegName2 = "";
                    mRegSerial = "";
                    mRegFhz = 0;
                    mRegCustomerId = 0;
                    break;
                }

                case ConnectionState.CONNECTED:
                {
                    //TODO: Das ist noch fest von TaMi Map
                    if (mUserId != -1)
                        SendLoginREQ(AppId.MAP, mSessionId, NotifyFlags.VEHICLES | NotifyFlags.JOBS, mUserId, mPassword);

                    break;
                }
            }

            //Fire Event
            if (this.ConnectionStateChange != null)
                this.ConnectionStateChange.Raise(this, new ConnectionStateChangeEventArgs((ConnectionState)statenew, (ConnectionState)stateold));
        }

        public override void OnMessageRecv(MessagePacket packet)
        {
            Debug.WriteLineIf(DEBUG_SENDRECV, $"TaMiClient: {this} RECV: " + RemoteHost + ":" + RemotePort + " Size=" + packet.WritePos + " Data=" + packet.ToString());
            EvaluatePacket(packet);
        }

        public override void OnMessageSend(MessagePacket packet)
        {
            Debug.WriteLineIf(DEBUG_SENDRECV, $"TaMiClient: {this} SEND: " + RemoteHost + ":" + RemotePort +  " Size=" + packet.WritePos + " Data=" + packet.ToString());
        }

        #endregion

        #region "MessageSocket EvaluatePacket"

        private void EvaluatePacket(MessagePacket packet)
        {
            TAC tac = (TAC)packet.getByteB();
            byte msgid = packet.getByteB();

            if (DEBUG_MESSAGES)
                Debug.WriteLine($"TaMiClient: {this} EVALUATE TAC: {tac} MsgId: {msgid}");

            mLastMessageRecvTicks = Environment.TickCount & Int32.MaxValue;



            //System.Windows.Forms.MessageBox.Show("Recv: " + msgid + " " + tac + " " + packet.ToString());

            //Debug.WriteLine($"{mId}: EvaluatePacket EID=" + eid + " TAC=[" + tac + "] " + (TAC)tac + " MsgID=" + String.Format( msgid + " Size=" + p.Length + " Data=" + p.ToString()));

            //Debug.WriteLineIf(DEBUG_MESSAGES, String.Format($"{mId}: " + RemoteHost + ":" + RemotePort + " EvaluatePacket MsgID={0,3} TAC=[{1,-3}] {2,12} ", msgid, tac, tac) + " Size=" + packet.WritePos + " Data=" + packet.ToString());

            switch ((TAC)tac)
            {
                //----------------------------------------
                case TAC.RESULTMSG:
                //----------------------------------------
                {
                    short nResult = packet.getShortS();
                    string extra;

                    if (packet.RemainingBytesToRead > 0)
                        extra = packet.getString(packet.RemainingBytesToRead);
                    else
                        extra = "";

                    ResultCodes resultCode = (ResultCodes)nResult;

#if DEBUG
                    Debug.WriteLine($"TaMiClient: {this}: " + RemoteHost + ":" + RemotePort + " RESULTMSG for MsgId: " + msgid + " Result: " + nResult + " (" + resultCode + ") Extra: '" + extra + "'");
                    if (resultCode != ResultCodes.SUCCESS)
                        System.Windows.Forms.MessageBox.Show("TaMiClient: " + RemoteHost + ":" + RemotePort + " RESULTMSG for MsgId: " + msgid + " Result: " + nResult + " (" + resultCode + ") Extra: '" + extra + "'");
#endif

                    break;
                }

                //----------------------------------------
                case TAC.LOGINMSG:
                //----------------------------------------
                {
                    //Debug.WriteLine("LOGINMSG MsgId: " + msgid);
                    int nTempLen;
                    short nResult = packet.getShortS();
                 
                     //Nicht weitermachen, sonst stimmt das Parsing nicht, da sich ab IP-PROTO 1.82 die LoginMSG geändert hat
                    if (nResult == 13) //PROTOVERSIONOUTDATED
                    {
                        Debug.WriteLineIf(DEBUG_MESSAGEDETAILS, $"TaMiClient: {this}: " + RemoteHost + ":" + RemotePort + " LOGIN_MSG Result=" + nResult + " " + (ResultCodes)nResult);

                        //Fire Event
                        if (this.LoginMessage != null)
                            this.LoginMessage.Raise(this, new LoginMessageEventArgs(msgid, nResult, false, 0, string.Empty, string.Empty));

                        mLogInResult = nResult;
                        mAppRights1 = 0;
                        mAppRights2 = 0;
                        mUsername1 = string.Empty;
                        mUsername2 = string.Empty;

                        return;
                    }

                    //Ab IP-PROTO 1.82 ist die Struktur anderst
                    short nFlags = packet.getShortS();
                    short nProtoVersion = packet.getShortS();
                    int apprights1 = packet.getIntI();
                    int apprights2 = packet.getIntI();
                    packet.getIntI(); //Frei
                    packet.getIntI(); //Frei

                    DateTime serverTime;

                    short serverUTCOffset = packet.getShortS();
                    short serverYear = packet.getShortS();
                    byte serverMonth = packet.getByteB();
                    byte serverDay = packet.getByteB();
                    byte serverHour = packet.getByteB();
                    byte serverMin = packet.getByteB();
                    byte serverSec = packet.getByteB();

                    serverTime = new DateTime(serverYear, serverMonth, serverDay, serverHour, serverMin, serverSec);
                 
                    nTempLen = packet.getShortI();
                    string username1 = packet.getString(nTempLen);

                    nTempLen = packet.getShortI();
                    string username2 = packet.getString(nTempLen);

                    nTempLen = packet.getShortI();
                    string allowedManIdList = packet.getString(nTempLen);

                    int userId;
                    if (packet.RemainingBytesToRead >= 4)
                        userId = packet.getIntI();
                    else
                        userId = 0;

                    bool userDetailsChanged, permissionChanged;

                    userDetailsChanged = mUserId != userId || !mUsername1.Equals(username1) || !mUsername2.Equals(username2);
                    permissionChanged  = mAppRights1 != apprights1 || mLogInResult != nResult; //Wenn sich Rechte geändert haben oder auch der Resultcode

                    if (userDetailsChanged)
                    {
                        mUserId = userId;
                        mUsername1 = username1;
                        mUsername2 = username2;
                    }

                    mAppRights1 = apprights1;
                    mAppRights2 = apprights2;

                    //Debug.WriteLine($"TaMiClient: {this} LoginMSG " + packet.ToString());
                    //Debug.WriteLine($"TaMiClient: {this} LoginMSG nAppRights1=0x" + mAppRights1.ToString("X") + " nAppRights2=0x" + mAppRights2.ToString("X"));

                    //Lade diverse Parameter direkt aus der Datenbank wenn eine neue Verbindung aufgebaut wurde
                    if (nResult == 1 && mLogInResult == -1)
                    {
                        SqlConnection connTaMi = this.OpenTaMiDB(false);

                        if (connTaMi != null)
                        {
                            mConfig.LoadDb(connTaMi);
                            mTarife.loadDb(connTaMi);

                            connTaMi.Close();
                        }
                    }

                    Debug.WriteLineIf(DEBUG_MESSAGEDETAILS, $"TaMiClient: {this}: " + RemoteHost + ":" + RemotePort + " LOGIN_MSG Result=" + nResult + " UserId: " + mUserId + " Username1: " + username1 + " Username2: " + username2 + " Time=" + serverTime.ToString());
                 
                    //Fire Event
                    if (this.LoginMessage != null)
                        this.LoginMessage.Raise(this, new LoginMessageEventArgs(msgid, nResult, permissionChanged, userId, username1, username2));

                    mLogInResult = nResult;
                    break;
                }

                //----------------------------------------
                case TAC.LOGOUTREQ:
                //----------------------------------------
                {
                    this.Disconnect();
                    break;
                }

                //----------------------------------------
                case TAC.VEHICLEMANMSG:
                //----------------------------------------
                {
                    VehicleManAction action = (VehicleManAction)packet.getByteB();
                    //Debug.WriteLine($"TaMiClient: {this} VEHICLEMANMSG MsgId: {msgid} action: {action}");

                    if (action == VehicleManAction.GETGROUPS && mFahrzeugGruppen != null)
                    {
                        List<FahrzeugGruppe> fhzGrpChanged = mFahrzeugGruppen.SyncFromMessagePacket(packet, action);

                        //Fire Event
                        if (this.VehicleManagerMessage != null)
                            this.VehicleManagerMessage.Raise(this, new VehicleManagerMessageEventArgs(msgid, action, null));

                    }

                    else if (mFahrzeuge != null)
                    {
                        SyncFlags syncFlags;
                        List<Fahrzeug> fahrzeugeChanged = mFahrzeuge.SyncFromMessagePacket(packet, action, out syncFlags);

                        //TODO: Fahrzeuge die bei einer erneuten VehicleManAction.STATICDATALIST fehlen, sollten entfernt werden. Da diese gesperrt wurden oder keine Lizenz mehr haben

                        //Fire Event
                        if (this.VehicleManagerMessage != null)
                            this.VehicleManagerMessage.Raise(this, new VehicleManagerMessageEventArgs(msgid, action, fahrzeugeChanged));
                    }

                    break;
                }

                //----------------------------------------
                case TAC.PERSONALMANMSG:
                //----------------------------------------
                {
                    PersonalManAction action = (PersonalManAction)packet.getByteB();
                    //Debug.WriteLine($"TaMiClient: {this} PERSONALMANMSG MsgId: {msgid} action: {action}");

                    if (mPersonal != null)
                    {
                        List<Personal> personalChanged = mPersonal.SyncFromMessagePacket(packet, action);

                        //Fire Event
                        //if (this.VehicleManagerMessage != null)
                        //    this.VehicleManagerMessage.Raise(this, new VehicleManagerMessageEventArgs(msgid, action, fahrzeugeChanged));
                    }

                    break;
                }


                //----------------------------------------
                case TAC.ZONEMANMSG:
                //----------------------------------------
                {
                    ZoneManAction action = (ZoneManAction)packet.getByteB();
                    //Debug.WriteLine($"TaMiClient: {this} ZONEMANMSG MsgId: {msgid} action: {action}");

                    if (mZonen != null)
                    {
                        List<Zone> zonenChanged = mZonen.SyncFromMessagePacket(packet, action);

                        //Fire Event
                        if (this.ZoneManagerMessage != null)
                            this.ZoneManagerMessage.Raise(this, new ZoneManagerMessageEventArgs(msgid, action, zonenChanged));
                    }

                    break;
                }


                //----------------------------------------
                case TAC.JOBMSG:
                //----------------------------------------
                {
                    List<Fahrzeug> fahrzeugeChanged;
                    JobAction action = (JobAction)packet.getByteB();
                    //Debug.WriteLine($"TaMiClient: {this} JOBMSG MsgId: {msgid} action: {action}");

                    switch (action)
                    {
                        case JobAction.GETLIST:
                        {
                            if (mJobs != null)
                            {
                                SyncFlags syncFlags;
                                List<string> jobIdsRemoved;
                                List<Job> jobsChanged = mJobs.SyncFromMessagePacket(packet, out syncFlags, out jobIdsRemoved);

                                fahrzeugeChanged = RefreshJobCountForAllFahrzeuge();

                                //Fire Event <JobsChanged>
                                if (this.JobGetListMessage != null && (syncFlags.HasFlag(SyncFlags.FULLSYNC) || jobsChanged.Count > 0 || jobIdsRemoved.Count > 0))
                                    this.JobGetListMessage.Raise(this, new JobGetListMessageEventArgs(0, syncFlags, jobsChanged, jobIdsRemoved));


                                //Fire Event <VehicleManagerMessage>
                                if (fahrzeugeChanged.Count > 0)
                                {
                                    if (this.VehicleManagerMessage != null)
                                        this.VehicleManagerMessage.Raise(this, new VehicleManagerMessageEventArgs(0, VehicleManAction.INTERNAL_JOBSCHANGED, fahrzeugeChanged));
                                }

                            }
                            break;
                        }

                        case JobAction.SYNCJOB:
                        {
                            string data = packet.getString(packet.RemainingBytesToRead);
                            
                            Job jobNew = new Job();
                            jobNew.DeserializeStatic(data.Split(TaMiClient.SEP_CHAR_COL));

                            Job jobOld = mJobs[jobNew.JobId];

                                
                            fahrzeugeChanged = new List<Fahrzeug>();
                            Fahrzeug fhz;

                            //Den "alten" Job vom Fahrzeug entfernen
                            if (jobOld != null && jobOld.Status == JobStatus.VERMITTELT && jobOld.FahrzeugId != 0)
                            {
                                fhz = mFahrzeuge[jobOld.FahrzeugId];
                                if (fhz != null)
                                {
                                    //fhz.CountJobsVermittelt--; 
                                    fhz.RemoveDispatchedJob(jobOld);  
                                    fahrzeugeChanged.Add(fhz); 
                                }
                            }

                            //Den "neuen" Job dem Fahrzeug hinzufügen
                            if (jobNew != null && jobNew.Status == JobStatus.VERMITTELT && jobNew.FahrzeugId != 0)
                            {
                                fhz = mFahrzeuge[jobNew.FahrzeugId];
                                if (fhz != null)
                                {
                                    //fhz.CountJobsVermittelt++;
                                    fhz.AddDispatchedJob(jobNew);

                                    //INFO: Wurde das Fahrzeug weiter oben bereits hinzugefügt entferne es wieder wenn es 
                                    //      keine Statusänderung gibt
                                    if (fahrzeugeChanged.Contains(fhz))
                                    {
                                        //Nichts relevantes für den vermittelten Job wurde geändert.
                                        if (jobOld.Status == jobNew.Status && 
                                            jobOld.Flag(JobFlag.APPROACH_DEPARTURE) == jobNew.Flag(JobFlag.APPROACH_DEPARTURE) &&
                                            jobOld.Flag(JobFlag.APPROACH_LOADED) == jobNew.Flag(JobFlag.APPROACH_LOADED) 
                                            )

                                            //Kein Redraw durch VehicleManAction.INTERNAL_JOBSCHANGED
                                            fahrzeugeChanged.Remove(fhz);
                                    }
                                    else
                                    {
                                        fahrzeugeChanged.Add(fhz);
                                    }
                                }
                            }


                            //Sync job
                            mJobs.SyncJob(jobNew);

                            //Fire Event <JobSyncMessage>
                            if (this.JobSyncMessage != null)
                                this.JobSyncMessage.Raise(this, new JobSyncMessageEventArgs(msgid, jobNew, jobOld));

                            //Fire Event <VehicleManagerMessage>
                            if (this.VehicleManagerMessage != null && fahrzeugeChanged.Count > 0)
                                this.VehicleManagerMessage.Raise(this, new VehicleManagerMessageEventArgs(0, VehicleManAction.INTERNAL_JOBSCHANGED, fahrzeugeChanged));


                            break;
                        }

                    } //switch (action)
                    break;
                }

                //----------------------------------------
                case TAC.JOBREPMSG:
                //----------------------------------------
                {
                    //List<Fahrzeug> fahrzeugeChanged;
                    JobAction action = (JobAction)packet.getByteB();
                    //Debug.WriteLine($"TaMiClient: {this} JOBREPMSG MsgId: {msgid} action: {action}");

                    switch (action)
                    {
                        case JobAction.GET: break;
                        case JobAction.SET: break;

                        //JobRep und alle Subitems löschen
                        case JobAction.DEL:
                        {
                            string repId = packet.getString(packet.RemainingBytesToRead);
                            Debug.WriteLine($"TaMiClient: {this} JOBREPMSG MsgId: " + msgid + " Action: " + action + " repId: " + repId);

                            break;
                        }

                        //JobRep Subitems löschen (Alle Aufträge mit der angegebenen repId)
                        case JobAction.DELSUBITEMS:
                        {
                            byte status = packet.getByteB();
                            string repId = packet.getString(packet.RemainingBytesToRead);
                            Debug.WriteLine($"TaMiClient: {this} JOBREPMSG MsgId: " + msgid + " Action: " + action + " repId: " + repId + " status: " + status);

                            List<Job> jobsRemoved = mJobs.RemoveByRelId(JobRelTyp.DAUER, repId, (JobStatus)status);

                            //Fire Event <JobsChanged>
                            if (this.JobsRemovedMessage != null && jobsRemoved.Count > 0)
                                this.JobsRemovedMessage.Raise(this, new JobGetListMessageEventArgs(0, (SyncFlags)0, jobsRemoved, null));

                            break;
                        }

                        //Den JobRep synchronisieren
                        case JobAction.SYNCJOB:
                        {
                            break;
                        }

                    }

                    break;
                }


                //----------------------------------------
                case TAC.CONFIGMSG:
                //----------------------------------------
                {
                    ConfigAction action = (ConfigAction)packet.getByteB();

                    switch (action)
                    {
                        case ConfigAction.REGINFORMATION:
                        {
                            string szData = packet.getString(packet.WritePos - packet.ReadPos);

                            string[] szItems = szData.Split('|');

                            if (szItems.Length >= 1) { mRegName1 = szItems[0]; }
                            if (szItems.Length >= 2) { mRegName2 = szItems[1]; }
                            if (szItems.Length >= 3) { mRegSerial = szItems[2]; }
                            if (szItems.Length >= 4) { mRegFhz = Convert.ToInt32(szItems[3]); }
                            if (szItems.Length >= 5) { mRegCustomerId = Convert.ToInt32(szItems[4]); }

                            //Fire Event
                            if (this.ConfigMessage != null)
                            {
                                ConfigMessageEventArgs eventargs = new ConfigMessageEventArgs(msgid, action);
                                eventargs.SetParam(0, "name1", mRegName1);
                                eventargs.SetParam(1, "name2", mRegName2);
                                eventargs.SetParam(2, "license", mRegSerial);
                                eventargs.SetParam(3, "fhz", mRegFhz.ToString());
                                eventargs.SetParam(4, "customerid", mRegCustomerId.ToString());

                                this.ConfigMessage.Raise(this, eventargs);
                            }
                            break;
                        }

                        case ConfigAction.SETDISPATCHAISTATUS:
                        {
                            string szData = packet.getString(packet.WritePos - packet.ReadPos);

                            //Fire Event
                            if (this.ConfigMessage != null)
                            {
                                ConfigMessageEventArgs eventargs = new ConfigMessageEventArgs(msgid, action);
                                eventargs.SetParam(0, "enabled", szData);

                                this.ConfigMessage.Raise(this, eventargs);
                            }

                            break;
                        }

                        case ConfigAction.SETDISPATCHAILOCKTIMES:
                        {
                            int nLocktimeAccept = packet.getIntI();
                            int nLocktimeDeny = packet.getIntI();
                            int LocktimeReturn = packet.getIntI();

                            //Fire Event
                            if (this.ConfigMessage != null)
                            {
                                ConfigMessageEventArgs eventargs = new ConfigMessageEventArgs(msgid, action);
                                eventargs.SetParam(0, "locktimeaccept", nLocktimeAccept.ToString());
                                eventargs.SetParam(0, "locktimedeny", nLocktimeDeny.ToString());
                                eventargs.SetParam(0, "locktimereturn", LocktimeReturn.ToString());

                                this.ConfigMessage.Raise(this, eventargs);
                            }

                            break;
                        }

                    } //switch (action)

                    break;
                }

                //----------------------------------------
                case TAC.KEEPALIVEREQ:
                //----------------------------------------
                {
                    //Antwort Datenpaket erstellen und senden
                    MessagePacket packetReply = new MessagePacket();
                    packetReply.putByte((byte)TAC.KEEPALIVEMSG);
                    packetReply.putByte(msgid);

                    SendPacket(packetReply);
                    break;
                }

                //----------------------------------------
                case TAC.KEEPALIVEMSG:
                //----------------------------------------
                {
                    break;
                }

                //----------------------------------------
                default:
                //----------------------------------------
                {
#if DEBUG
                    System.Windows.Forms.MessageBox.Show("Unhandled Message Received\n\nMsgId: " + msgid + "\nTAC: " + tac + "\n\nData:\n" + packet.ToString());
#endif
                    break;
                }

            } //switch (tac)


            //mLastProccessedMsgId jetzt setzen

            //Messages with mLastSendMsgId 0 from Server are Broadcasts, we can't wait for that
            if (msgid != 0)
                mLastProccessedMsgId = msgid;
        }

        #endregion

        #region "Static functions"

        public static string GetJobTypeText(JobTyp Id)
        {
            switch (Id)
            {
                case JobTyp.SOFORT:     return "Sofortfahrt";
                case JobTyp.NORMAL:     return "Vorbestellung";
                case JobTyp.DAUER:      return "Dauerauftrag";
                case JobTyp.AUTOBOOK:   return "Autobooking";
                case JobTyp.EXTERNAL:   return "Extern/Schnittstelle";

                default:                return "UNBEKANNT: " + Id;
            }
        }

        public static Color GetJobDispatchStateColor(JobDFStatus Id)
        {
            return GetJobDispatchStateColor(Id, 192);
        }

        public static Color GetJobDispatchStateColor(JobDFStatus Id, int alfa)
        {
            switch (Id)
            {
                case JobDFStatus.NONE:                          return Color.Empty;
                case JobDFStatus.TRANSFERING:                   return Color.FromArgb(alfa, 255,255,192);
                case JobDFStatus.TRANSFERED:                    return Color.FromArgb(alfa, 255, 128, 0);
                case JobDFStatus.TRANSFERFAILED:                return Color.FromArgb(alfa, 255, 0, 0);
                case JobDFStatus.RESPONSEACCEPTED:              return Color.FromArgb(alfa, 0, 192, 0);
                case JobDFStatus.RESPONSETIMEOUT:               return Color.FromArgb(alfa, 255, 0, 0);
                case JobDFStatus.RESPONSEREJECTED:              return Color.FromArgb(alfa, 255, 0, 0);
                case JobDFStatus.RESPONSEREJECTEDAUTO:          return Color.FromArgb(alfa, 255, 0, 0);

                case JobDFStatus.AUTODISPATCHING:               return Color.FromArgb(alfa, 45, 129, 255);
                case JobDFStatus.AUTODISPATCHFREEOFFERING:      return Color.FromArgb(alfa, 0, 90, 117);
                case JobDFStatus.AUTODISPATCHED:                return Color.FromArgb(alfa, 128, 255, 128);
                case JobDFStatus.AUTODISPATCHIMPOSSIBLE:        return Color.FromArgb(alfa, 128, 0, 128);
                case JobDFStatus.AUTODISPATCHFAILED:            return Color.FromArgb(alfa, 255, 0, 0);
                case JobDFStatus.AUTODISPATCHFAILEDWILLRETRY:   return Color.FromArgb(alfa, 180, 70, 0);
                case JobDFStatus.AUTODISPATCHNOFOUNDWILLRETRY:  return Color.FromArgb(alfa, 180, 70, 0);
                case JobDFStatus.AUTODISPATCHEDFREEOFFER:       return Color.FromArgb(alfa, 128, 255, 128);

                default: return Color.Empty;
            }
        }

        public static string GetShiftEntryTypeText(ShiftEntryType Id)
        {
            switch (Id)
            {
                case ShiftEntryType.SHIFTBEGINN: return "Schichtbeginn";
                case ShiftEntryType.SHIFTEND: return "Schichtende";
                case ShiftEntryType.TRIPBUSY: return "Besetztfahrt";
                case ShiftEntryType.TRIPFREE: return "Leerfahrt";

                case ShiftEntryType.TRIPBUSINESS: return "Geschäftsfahrt";
                case ShiftEntryType.TRIPPRIVATE: return "Privatfahrt";

                case ShiftEntryType.PAUSE: return "Pause";
                case ShiftEntryType.PAUSEABWESEND: return "Pause (Abwesend)";
                case ShiftEntryType.STANDBY: return "Bereitschaft";
                case ShiftEntryType.REFUEL: return "Tanken";
                case ShiftEntryType.WITHDRAW: return "Ausgabe";
                case ShiftEntryType.CANCELATION: return "Storno";

                default: return "UNBEKANNT";
            }
        }

        public static string GetPaytypeText(Paytype Id)
        {
            switch (Id)
            {
                case Paytype.UNKNOWN: return "";
                case Paytype.CASH: return "Bar";
                case Paytype.CARD: return "Kartenzahlung";
                case (Paytype)21: return "EC-Karte";
                case (Paytype)22: return "Kreditkarte";
                case (Paytype)23: return "Kundenkarte";
                case (Paytype)24: return "Fahrerkarte";
                case (Paytype)25: return "Tankkarte";
                case Paytype.BILL: return "Rechnung";
                case Paytype.HEALTHINSURANCE: return "Krankenfahrt";
                case Paytype.FAILEDTRIP: return "FEHLFAHRT";
                case Paytype.APP: return "App-Zahlung";
                case Paytype.APP_SUE: return "Eigene App";
                case Paytype.APP_TAXIDEUTSCHLAND: return "Taxi-Deutschland App";
                case Paytype.APP_TAXIEU: return "Taxi.eu App";
                case Paytype.PAYTYPE_TEST: return "Testfahrt";

                default: return "UNBEKANNT";
            }
        }



        #endregion

    } //class TaMiClient

    #region "Event Args"

    /// <summary>
    /// Provides data for a ConnectionStateChangeEvent event.
    /// </summary>
    public class ConnectionStateChangeEventArgs : EventArgs
    {
        /// <summary>
        /// Constructor for a new ConnectionStateChangeEventArgs object.
        /// </summary>
        public ConnectionStateChangeEventArgs(ConnectionState statenew, ConnectionState statelast)
        {
            this.StateNew = statenew;
            this.StateLast = statelast;
        }

        /// <summary>
        /// Gets the data that has been read.
        /// </summary>
        public ConnectionState StateNew { get; private set; }
        public ConnectionState StateLast { get; private set; }
    }


    /// <summary>
    /// Provides data for a LoginMessageEvent event.
    /// </summary>
    public class LoginMessageEventArgs : EventArgs
    {
        /// <summary>
        /// Constructor for a new LoginMessageEventArgs object.
        /// </summary>
        public LoginMessageEventArgs(byte msgid, int resultcode, bool permissionChanged, int userid, string username1, string username2)
        {
            this.MessageId = msgid;
            this.Resultcode = resultcode;
            this.PermissionChanged = permissionChanged;
            this.UserId = userid;
            this.Username1 = username1;
            this.Username2 = username2;
        }

        /// <summary>
        /// Gets the data that has been read.
        /// </summary>
        public int MessageId { get; private set; }
        public int Resultcode { get; private set; }
        public bool PermissionChanged { get; private set; }
        public int UserId { get; private set; }
        public string Username1 { get; private set; }
        public string Username2 { get; private set; }
     }


    /// <summary>
    /// Provides data for a ConfigMessageEvent event.
    /// </summary>
    public class ConfigMessageEventArgs : EventArgs
    {
        struct CustomPair
        {
            public string Key;
            public string Value;
        }
        private CustomPair[] data = new CustomPair[10];

        /// <summary>
        /// Constructor for a new ConfigMessageEventEventArgs object.
        /// </summary>
        public ConfigMessageEventArgs(byte msgid, ConfigAction action)
        {
            this.MessageId = msgid;
            this.Action = action;
        }

        /// <summary>
        /// Gets the data that has been read.
        /// </summary>
        public int          MessageId { get; private set; }
        public ConfigAction Action    { get; private set; }
        public string       GetParam(string key)
        { 
            foreach (CustomPair p in data)
            {
                if ((p.Key != null) && (p.Key.Equals(key))) 
                    return p.Value;
            }
            return null;
        }

        internal void SetParam(int index, string key, string value)
        {
            data[index].Key = key;
            data[index].Value = value;
        }

        public override string ToString()
        {
            return "ConfigMessage Action: " + this.Action + " (msgId: " + this.MessageId + ")";
        }
    }

    /// <summary>
    /// Provides data for a VehicleManagerMessage event.
    /// </summary>
    public class VehicleManagerMessageEventArgs : EventArgs
    {
        /// <summary>
        /// Constructor for a new VehicleManagerMessageEventArgs object.
        /// </summary>
        public VehicleManagerMessageEventArgs(byte msgid, VehicleManAction action, List<Fahrzeug> fahrzeuge)
        {
            this.MessageId = msgid;
            this.Action = action;
            this.Fahrzeuge = fahrzeuge;
        }

        /// <summary>
        /// Gets the data that has been read.
        /// </summary>
        public int MessageId { get; private set; }
        public VehicleManAction Action { get; private set; }
        public List<Fahrzeug> Fahrzeuge { get; private set; }

        public override string ToString()
        {
            return "VehicleManagerMessage Action: " + this.Action + " (msgId: " + this.MessageId + ")";
        }
    }

    /// <summary>
    /// Provides data for a ZoneManagerMessage event.
    /// </summary>
    public class ZoneManagerMessageEventArgs : EventArgs
    {
        /// <summary>
        /// Constructor for a new VehicleManagerMessageEventArgs object.
        /// </summary>
        public ZoneManagerMessageEventArgs(byte msgid, ZoneManAction action, List<Zone> zonen)
        {
            this.MessageId = msgid;
            this.Action = action;
            this.Zonen = zonen;
        }

        /// <summary>
        /// Gets the data that has been read.
        /// </summary>
        public int MessageId { get; private set; }
        public ZoneManAction Action { get; private set; }
        public List<Zone> Zonen { get; private set; }

        public override string ToString()
        {
            return "ZoneManagerMessage Action: " + this.Action + " (msgId: " + this.MessageId + ")";
        }
    }

    /// <summary>
    /// Provides data for a JobGetListMessage event.
    /// </summary>
    public class JobGetListMessageEventArgs : EventArgs
    {
        /// <summary>
        /// Constructor for a new JobGetListMessageEventArgs object.
        /// </summary>
        public JobGetListMessageEventArgs(byte msgid, SyncFlags syncFlags, List<Job> jobsChanged, List<string> jobsRemoved)
        {
            this.MessageId = msgid;
            this.SyncFlags = syncFlags;
            this.JobsChanged = jobsChanged;
            this.JobsRemoved = jobsRemoved;
        }

        /// <summary>
        /// Gets the data that has been read.
        /// </summary>
        public int MessageId { get; private set; }
        public SyncFlags SyncFlags { get; private set; }
        public List<Job> JobsChanged { get; private set; }
        public List<string> JobsRemoved { get; private set; }

        public override string ToString()
        {
            return "JobGetListMessage (msgId: " + this.MessageId + ")";
        }
    }

    /// <summary>
    /// Provides data for a JobSyncMessage event.
    /// </summary>
    public class JobSyncMessageEventArgs : EventArgs
    {
        /// <summary>
        /// Constructor for a new JobSyncMessageEventArgs object.
        /// </summary>
        public JobSyncMessageEventArgs(byte msgid, Job jobnew, Job jobold)
        {
            this.MessageId = msgid;
            this.JobNew = jobnew;
            this.JobOld = jobold;
        }

        /// <summary>
        /// Gets the data that has been read.
        /// </summary>
        public int MessageId { get; private set; }
        public Job JobNew { get; private set; }
        public Job JobOld { get; private set; }

        public override string ToString()
        {
            return "JobSyncMessage JobNew: " + this.JobNew + " JobOld: " + this.JobOld + " (msgId: " + this.MessageId + ")";
        }

    }


    #endregion
    
}

