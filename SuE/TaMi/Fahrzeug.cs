using SuE.Tools;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace SuE.TaMi
{
    public enum FahrzeugFlags : int
    {
        LOCKED = 0x1,
        COMMBOXREQUIRED = 0x10,

        CODRIVERLOGIN = 0x80,                           //Beifahreranmeldung möglich
        LOGONONLYSAMEMANDANT = 0x100,
        AUTOGENERATEJOBONBUSY = 0x200,                  
        SHIFTREPORT = 0x400,                            //Schichterfassung

        EXTENDED_TRIPTYPES = 0x1000,                    //Geschäfts- und Privatfahrt freigegeben
        DONOTAUTODISPATCH = 0x2000,
        DISPAIOVERRIDE_SECONDFHZFIRST = 0x4000,
        DISPAIOVERRIDE_ASSECONDFHZFIRSTOFFE = 0x8000,
        NODISPATCH_AUTOBOOKING = 0x10000                //Das Fahrzeug soll keine Autobooking Aufträge erhalten
    }

    public enum FhzStatusEx : int
    {
        ROAMING = 0x0001,
        EMERGENCY = 0x0002,
        IGNITION = 0x0004,
        NOPOWER = 0x0008,
        TAXAMETER = 0x0010,
        SEATCONTACT = 0x0020,
        GPSINVALID = 0x0040,
        NOCOMMBOX = 0x0080,
        GPSDISABLED = 0x0100,
        TSEERROR = 0x0200,

        OFFLINE = 0x8000
    }


    public class Fahrzeug
    {
        //Statische Daten
        private int mId = 0;
        private int mManId = 0;
        private int mGrpId = 0;
        private int mFlags = 0;
        private string mName = "";
        private string mKennzeichen = "";
        private string mKonzessionsNr = "";
        private int mSitzplätze = 0;
        private string mOptionen;
        private string mTelefon;
        private string mFax;

        private string mKey;

        //Dynamische Daten
        private int mStatusId = 0;
        private DateTime mStatusChange = DateTime.MinValue;
        private FhzState mStatus = FhzStates.DEFAULT_STATE;
        private FhzStatusEx mStatusEx = 0;

        private int mFahrerId = 0;
        private Personal mFahrer;
        private DateTime mFahrerChange = DateTime.MinValue;
        private int mFahrerPauseMins = 0; //Anzahl der Minuten die in der Schicht bereits Pausen gemacht wurden

        private int mBeifahrerId = 0;
        private Personal mBeifahrer;
        private DateTime mBeifahrerChange = DateTime.MinValue;
        private int mBeifahrerPauseMins = 0; //Anzahl der Minuten die in der Schicht bereits Pausen gemacht wurden


        private DateTime mPosChange = DateTime.MinValue;
        private double mPosLat = 0;
        private double mPosLng = 0;

        private short mBearing = 0;
        private short mSpeed = 0;
        private short mAltitude = 0;

        private int mZoneCurrentId;
        private DateTime mZoneCurrentChange = DateTime.MinValue;

        private int mZoneTargetId;
        private DateTime mZoneTargetChange = DateTime.MinValue;

        private decimal mSchichtUmsatz;
        private float mSchichtKMGesamt;
        private float mSchichtKMBesetzt;


        //Interne Daten
        private int mCountJobsVermittelt;
        private Dictionary<string, Job> mJobsDispatched;
        private int mCountJobsInApproach;

        private Fahrzeuge mFahrzeuge;
        private TaMiClient mTaMiClient = null;


        public Fahrzeug(Fahrzeuge fahrzeuge)
        {
            mCountJobsVermittelt = 0;
            mJobsDispatched = new Dictionary<string, Job>();
            mCountJobsInApproach = 0;

            mFahrzeuge = fahrzeuge;
            mTaMiClient = fahrzeuge.TaMiClient;
        }

        #region "Propertys"

        public int Id
        {
            get { return mId; }
        }

        public int ManId
        {
            get { return mManId; }
        }

        public int GrpId
        {
            get { return mGrpId; }
        }

        public FahrzeugFlags Flags
        {
            get { return (FahrzeugFlags)mFlags; }
        }

        public bool HasFlag(FahrzeugFlags flag)
        {
            return (mFlags & ((int)flag)) == (int)flag;
        }

        public string Name
        {
            get { return mName; }
        }

        public string Kennzeichen
        {
            get { return mKennzeichen; }
        }

        public string KonzessionsNr
        {
            get { return mKonzessionsNr; }
        }

        public int Sitzplätze
        {
            get { return mSitzplätze; }
        }

        public string Optionen
        {
            get { return mOptionen; }
        }

        public string Telefon
        {
            get { return mTelefon; }
        }

        public int StatusId
        {
            get { return mStatusId; }
        }

        public DateTime StatusChange
        {
            get { return mStatusChange; }
        }

        public FhzState Status
        {
            get 
            {
                //Fahrzeug ist Frei oder in Auftrag und hat den StatusId Approch auf mindestens 1 Auftrag -> Setze Orange
                if ((mStatusId == 4 || mStatusId == 6) && mCountJobsInApproach > 0)
                    return TaMiTools.GetDefaultFhzStateById(7); //"In Anfahrt"                

                //Sonst mStatusId nach mStatusId
                else
                    return mStatus; 
            }
        }

        public bool IsLoggedOn
        {
            get { return (mStatusId > 1 && mStatusId != 8); }
        }

        public FhzStatusEx StatusEx
        {
            get { return mStatusEx; }
        }

        public bool IsStatusEx(FhzStatusEx mask)
        {
            return (mStatusEx & mask) == mask;
        }

        public string StatusExText
        {
            get { return Fahrzeug.StatusExToString(mStatusEx, false); }
        }

        public int FahrerId
        {
            get { return mFahrerId; }
        }

        public string FahrerName
        {
            get
            {
                if (mFahrer != null)
                    return mFahrer.FormatName();
                else if (mFahrerId != 0)
                    return "#" + mFahrerId;
                else
                    return string.Empty;
            }
        }

        public Personal Fahrer
        {
            get
            {
                return mFahrer;
            }
        }

        public DateTime FahrerChange
        {
            get { return mFahrerChange; }
        }

        public int FahrerPauseMins
        {
            get { return mFahrerPauseMins; }
        }


        public int BeifahrerId
        {
            get { return mBeifahrerId; }
        }

        public string BeifahrerName
        {
            get
            {
                if (mBeifahrer != null)
                    return mBeifahrer.FormatName();
                else if (mBeifahrerId != 0)
                    return "#" + mBeifahrerId;
                else
                    return string.Empty;
            }
        }

        public Personal Beifahrer
        {
            get
            {
                return mBeifahrer;
            }
        }

        public DateTime BeifahrerChange
        {
            get { return mBeifahrerChange; }
        }


        public int BeifahrerPauseMins
        {
            get { return mBeifahrerPauseMins; }
        }


        public DateTime PosOrStatusChange
        {
            get
            {
                if (mPosChange > mStatusChange)
                    return mPosChange;
                else
                    return mStatusChange;
            }
        }

        public DateTime PosChange
        {
            get { return mPosChange; }
        }

        public double PosLat
        {
            get { return mPosLat; }
        }

        public double PosLng
        {
            get { return mPosLng; }
        }

        //Ist jetzt im FhzMarker
        /*
        public List<GMap.NET.PointLatLng> PosHistory
        {
            get { return mPosHistory;  }
        }
        */

        public short Bearing
        {
            get { return mBearing; }
        }

        public short Speed
        {
            get { return mSpeed; }
        }

        public short Altitude
        {
            get { return mAltitude; }
        }

        public int ZoneCurrentId
        {
            get { return mZoneCurrentId; }
        }

        public DateTime ZoneCurrentChange
        {
            get { return mZoneCurrentChange; }
        }

        public int ZoneTargetId
        {
            get { return mZoneTargetId; }
        }

        public DateTime ZoneTargetChange
        {
            get { return mZoneTargetChange; }
        }

        public decimal SchichtUmsatz { get => mSchichtUmsatz; }

        public float SchichtKMGesamt { get => mSchichtKMGesamt; }
        public float SchichtKMBesetzt { get => mSchichtKMBesetzt; }

        public int CountJobsVermittelt
        {
            get          { return mCountJobsVermittelt; }
            //internal set { mCountJobsVermittelt = value; }
        }

        public string Key
        {
            get { return mKey; }
        }

        //true = changed
        internal bool ClearDispatchedJobs()
        {
            mCountJobsVermittelt = 0;
            mCountJobsInApproach = 0;

            if (mJobsDispatched.Count > 0)
            {
                mJobsDispatched.Clear();
                return true;
            }

            return false;
        }

        internal void AddDispatchedJob(Job job)    
        {
            //Debug.WriteLine($"Fhz {mId} AddDispatchedJob    {job}");

            if (job.Status != JobStatus.VERMITTELT)
                throw new Exception($"AddDispatchedJob {job} can not add to fhz {this}, because it is not in dispatched state!");

            if (mJobsDispatched.ContainsKey(job.JobId))
                throw new Exception($"AddDispatchedJob {job} already added for fhz {this}!");

            mCountJobsVermittelt++;
            mJobsDispatched.Add(job.JobId, job);

            //TODO: Das klappt so nicht, da eine Änderung von JobFlag.APPROACH_DEPARTURE so nicht berücksichtigt wird
            if (job.Flag(JobFlag.APPROACH_DEPARTURE))
                mCountJobsInApproach++;

            if (mId == 901)
                Debug.WriteLine("FHZ " + this + ": AddDispatchedJob mCountJobsInApproach: " + mCountJobsInApproach);

        }


        internal void RemoveDispatchedJob(Job job) 
        {
            //Debug.WriteLine($"Fhz {mId} RemoveDispatchedJob {job}");

            //if (job.StatusId != JobStatus.VERMITTELT)
            //    return;

            if (!mJobsDispatched.Remove(job.JobId))
            {
                //throw new Exception($"RemoveDispatchedJob {job} not in list for fhz {this}");
                ExceptionReporter.DoReportError(new Exception($"RemoveDispatchedJob {job} not in list for fhz {this}"));
                return;
            }

            mCountJobsVermittelt--;

            //TODO: Das klappt so nicht, da eine Änderung von JobFlag.APPROACH_DEPARTURE so nicht berücksichtigt wird
            if (job.Flag(JobFlag.APPROACH_DEPARTURE))
                mCountJobsInApproach--;

            if (mId == 901)
                Debug.WriteLine("FHZ " + this + ": RemoveDispatchedJob mCountJobsInApproach: " + mCountJobsInApproach);

        }


        public int CountJobsInApproach
        {
            get { return mCountJobsInApproach; }
        }


        Fahrzeuge fahrzeuge;
        public Fahrzeuge Fahrzeuge { get => fahrzeuge; internal set => fahrzeuge = value; }
        
        #endregion




        internal bool DeserializeStatic(string[] data)
        {
            if (data == null) return false;

            //final char DOT = '.';
            //final char COMMA = ',';

            //string[] tokens = data.Split(TaMiClient.SEP_CHAR_COL);
            int t = 0;

            mId = Convert.ToInt32(data[t++]);
            mManId = Convert.ToInt32(data[t++]);
            mFlags = Convert.ToInt32(data[t++]);
            mName = data[t++];
            mKennzeichen = data[t++];
            mKonzessionsNr = data[t++];
            mSitzplätze = Convert.ToInt32(data[t++]);
            mOptionen = data[t++];
            mTelefon = data[t++];
            mFax = data[t++];
            mGrpId = Convert.ToInt32(data[t++]);


            mKey = mTaMiClient.Id + "@" + mId.ToString();

            return true;
        }

        internal bool DeserializeDynamic(string[] data)
        {
            if (data == null) return false;

            //const char DOT = '.';
            //const char COMMA = ',';

            int statusid;
            int newFahrerId, newBeifahrerId;
            double poslatNew;
            double poslngNew;
            int t = 0;

            t++; //Skip FhzId
            mFahrerChange = TaMiTools.SerializeString2Date(data[t++]);
            newFahrerId = Convert.ToInt32(data[t++]);
            mStatusChange = TaMiTools.SerializeString2Date(data[t++]);
            statusid = Convert.ToInt32(data[t++]);
            mStatusEx = (FhzStatusEx)Convert.ToInt32(data[t++]);
            mFahrerPauseMins = Convert.ToInt32(data[t++]);
            t++; //Skip DateTime LockTime Fahrer
            mPosChange = TaMiTools.SerializeString2Date(data[t++]);
            poslatNew = TaMiTools.SerializeString2Double(data[t++]);
            poslngNew = TaMiTools.SerializeString2Double(data[t++]);
            mBearing = Convert.ToInt16(data[t++]);
            mSpeed = Convert.ToInt16(data[t++]);
            mZoneCurrentId = Convert.ToInt32(data[t++]);
            mZoneTargetId = Convert.ToInt32(data[t++]);

            mBeifahrerChange = TaMiTools.SerializeString2Date(data[t++]);
            newBeifahrerId = Convert.ToInt32(data[t++]);
            mBeifahrerPauseMins = Convert.ToInt32(data[t++]);

            //Zusätzliche Parameter sind vom Server vorhanden
            if (data.Length > t + 4) //data ist 0-basierend
            {
                mZoneCurrentChange = TaMiTools.SerializeString2Date(data[t++]);
                mZoneTargetChange = TaMiTools.SerializeString2Date(data[t++]);
                t++; //Freifeld
                mSchichtUmsatz = TaMiTools.SerializeString2Decimal(data[t++]);
                t++; //Personalumsatz der aktuellen Schicht

                //Zusätzliche Parameter sind vom Server vorhanden
                if (data.Length > t + 2) //data ist 0-basierend
                {
                    mSchichtKMGesamt = TaMiTools.SerializeString2Float(data[t++]);
                    mSchichtKMBesetzt = TaMiTools.SerializeString2Float(data[t++]);
                }

            }


            //Fahrer hat sich geändert
            if (mFahrerId != newFahrerId)
            {
                mFahrerId = newFahrerId;
                if (newFahrerId != 0)
                    mFahrer = mTaMiClient.PersonalMan.get(mFahrerId);
                else
                    mFahrer = null;
            }

            //Beifahrer hat sich geändert
            if (mBeifahrerId != newBeifahrerId)
            {
                mBeifahrerId = newBeifahrerId;
                if (newBeifahrerId != 0)
                    mBeifahrer = mTaMiClient.PersonalMan.get(mBeifahrerId);
                else
                    mBeifahrer = null;
            }

            //StatusId hat sich geändert
            if (mStatusId != statusid || mStatus == null)
            {
                mStatusId = statusid;
                mStatus = TaMiTools.GetDefaultFhzStateById(mStatusId); //Info: Status "Anfahrt" wird im Property .Status ausgegeben wenn CountInApproach > 0 ist
            }


            //Position geändert
            if (mPosLat != poslngNew && mPosLng != poslngNew)
            {
                //Aktuelle Position jetzt in mPosHistory archivieren bevor eine neue kommt
                /*
                if (mPosLat != 0 && mPosLng != 0)
                {
                    if (mPosHistory.Count >= MAX_POS_HISTORY) { mPosHistory.RemoveAt(0); }
                    mPosHistory.Add(new GMap.NET.PointLatLng(mPosLat, mPosLng));
                }
                */

                mPosLat = poslatNew;
                mPosLng = poslngNew;
            }

            return true;
        }



        public override string ToString()
        {
            return "Id: " + mId + " Name: " + mName;
        }


        public static string StatusExToString(FhzStatusEx statusex, bool withDescription)
        {
            if (statusex == 0) return string.Empty;
            
            StringBuilder sb = new StringBuilder(512);

            if (withDescription)
            {
                if ((statusex & FhzStatusEx.OFFLINE) == FhzStatusEx.OFFLINE)            { sb.Append("OL: Offline\n"); } //Zeige Offlinestatus zuerst an

                if ((statusex & FhzStatusEx.ROAMING) == FhzStatusEx.ROAMING)            { sb.Append("R: Roaming\n"); }
                if ((statusex & FhzStatusEx.EMERGENCY) == FhzStatusEx.EMERGENCY)        { sb.Append("AL: Notruf aktiv\n"); }
                if ((statusex & FhzStatusEx.IGNITION) == FhzStatusEx.IGNITION)          { sb.Append("ZÜ: Zündung an\n"); }
                if ((statusex & FhzStatusEx.NOPOWER) == FhzStatusEx.NOPOWER)            { sb.Append("NP: Im Akkubetrieb\n"); }
                if ((statusex & FhzStatusEx.TAXAMETER) == FhzStatusEx.TAXAMETER)        { sb.Append("TX: Taxameter besetzt\n"); }
                if ((statusex & FhzStatusEx.SEATCONTACT) == FhzStatusEx.SEATCONTACT)    { sb.Append("SK: Sitzkontakte aktiv\n"); }
                if ((statusex & FhzStatusEx.GPSINVALID) == FhzStatusEx.GPSINVALID)      { sb.Append("GI: GPS Signal ungültig\n"); }
                if ((statusex & FhzStatusEx.NOCOMMBOX) == FhzStatusEx.NOCOMMBOX)        { sb.Append("CB: Keine Verbindung zur CommBox\n"); }
                if ((statusex & FhzStatusEx.GPSDISABLED) == FhzStatusEx.GPSDISABLED)    { sb.Append("GD: GPS wurde im Gerät deaktiviert\n"); }
                if ((statusex & FhzStatusEx.TSEERROR) == FhzStatusEx.TSEERROR)          { sb.Append("TSE: TSE Störung\n"); }
            }
            else
            {

                if ((statusex & FhzStatusEx.OFFLINE) == FhzStatusEx.OFFLINE)            { sb.Append("OL "); } //Zeige Offlinestatus zuerst an

                if ((statusex & FhzStatusEx.ROAMING) == FhzStatusEx.ROAMING)            { sb.Append("R "); }
                if ((statusex & FhzStatusEx.EMERGENCY) == FhzStatusEx.EMERGENCY)        { sb.Append("AL "); }
                if ((statusex & FhzStatusEx.IGNITION) == FhzStatusEx.IGNITION)          { sb.Append("ZÜ "); }
                if ((statusex & FhzStatusEx.NOPOWER) == FhzStatusEx.NOPOWER)            { sb.Append("NP "); }
                if ((statusex & FhzStatusEx.TAXAMETER) == FhzStatusEx.TAXAMETER)        { sb.Append("TX "); }
                if ((statusex & FhzStatusEx.SEATCONTACT) == FhzStatusEx.SEATCONTACT)    { sb.Append("SK "); }
                if ((statusex & FhzStatusEx.GPSINVALID) == FhzStatusEx.GPSINVALID)      { sb.Append("GI "); }
                if ((statusex & FhzStatusEx.NOCOMMBOX) == FhzStatusEx.NOCOMMBOX)        { sb.Append("CB "); }
                if ((statusex & FhzStatusEx.GPSDISABLED) == FhzStatusEx.GPSDISABLED)    { sb.Append("GD "); }
                if ((statusex & FhzStatusEx.TSEERROR) == FhzStatusEx.TSEERROR)          { sb.Append("TSE "); }
            }
            return sb.ToString(0, sb.Length - 1);
        }


    }
}
