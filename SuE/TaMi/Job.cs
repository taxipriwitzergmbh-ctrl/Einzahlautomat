using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

#if FMSJSON
using System.Text.Json;
#endif

namespace SuE.TaMi
{
    /*
        PROJEKT           :  Job
        VERSION           :  1.03
        
        ERSTELLUNGS-DATUM :  01.01.2021
        ÄNDERUNGS-DATUM   :  26.09.2024
        ÄNDERUNG:            siehe Entwicklungsgeschichte
        DURCHGEFUEHRT VON :  SuE-Software  [sue]

        FUNKTION          :  TaMi Auftrag

        BEMERKUNG         :  ---

        ======================== ENTWICKLUNGS-GESCHICHTE ========================

        --- Version 1.00 --------------------------------------------------------

        17.10.2018  mcs   Beginn der Implementation in NET

        --- Version 1.01 --------------------------------------------------------

        07.09.2023  mcs   Weitere Felder als Propertys hinzugefügt

        --- Version 1.02 --------------------------------------------------------

        12.09.2023  mcs   GetWgpStart und GetWgpDest hinzugefügt.
                          define FMSJSON hinzugefügt
                          Static Funktion Job.SerialString hinzugefügt

        --- Version 1.03 --------------------------------------------------------

        01.03.2024  mcs   Das vergessene Feld FlugNr hinzugefügt.
        17.09.2024  mcs   Weitere Ppropertys für Felder hinzugefügt.
        18.09.2024  mcs   ICloneable hinzugefügt
        26.09.2024  mcs   Property FahrzeugManId hinzugefügt

    */

    public class Job : ICloneable
    {
        #region "Variables"

        private List<JobPos> mPositionen = new List<JobPos>();

        private string mJobId = "";
        private int mManId = 1;

        private JobTyp mTyp = JobTyp.NORMAL;
        private JobRelTyp mRelTyp = JobRelTyp.KEIN;
        private string mRelId = "";
        private JobStatus mStatus = JobStatus.OFFEN;
        private JobDFStatus mStatusDF = JobDFStatus.NONE;

        private byte mPrio = 0;

        private JobFlag mFlags = JobFlag.NONE;
        private JobFlagFaktura mFlagsFaktura = JobFlagFaktura.NONE;

        private int mKID = 0;
        private string mKName = "";


        private DateTime mZeitAbfahrt;
        private DateTime mZeitFahrt;

        private DateTime mZeitAnlage;
        private int mUserAnlage;
        private DateTime mZeitBearbeitet;
        private int mUserBearbeitet;

        private DateTime mZeitAbgabe;
        private DateTime mZeitAnkunft;
        private DateTime mZeitStart;
        private DateTime mZeitAbgeschlossen;
        private int mDauerSoll;

        private string mOptionen = "";
        private byte mPersonen = 1;

        private string mZahlart = "";
        private string mKSt = "";
        private int mKtrId;
        private int mTarifId;

        private float mKM = 0;
        private int mWartezeit = 0;

        private byte mUStKz = 0;

        private decimal mPreis = 0;
        private decimal mZuzahlung = 0;

        private int mFahrzeugId = 0;
        private int mFahrzeugManId = 0;
        private int mFahrerId = 0;

        private string mFlugNr = "";
        private string mInfo = "";
        private string mRufnummer = "";

        private bool mChanged = false;

        #endregion


        #region "Propertys"

        public string JobId
        {
            get { return mJobId; }
        }

        public int ManId
        {
            get { return mManId; }
            set { if (mManId != value) { mManId = value; mChanged = true; } }
        }

        public JobTyp Typ
        {
            get { return mTyp; }
            set { if (mTyp != value) { mTyp = value; mChanged = true; } }
        }

        public JobRelTyp RelTyp
        {
            get { return mRelTyp; }
            set { if (mRelTyp != value) { mRelTyp = value; mChanged = true; } }
        }

        public string RelId
        {
            get { return mRelId; }
            set { if (mRelId != value) { mRelId = value; mChanged = true; } }
        }

        public JobStatus Status
        {
            get { return mStatus; }
            set { if (mStatus != value) { mStatus = value; mChanged = true; } }
        }

        public JobDFStatus StatusDF
        {
            get { return mStatusDF; }
            set { if (mStatusDF != value) { mStatusDF = value; mChanged = true; } }
        }

        public byte Prio
        {
            get { return mPrio; }
            set { if (mPrio != value) { mPrio = value; mChanged = true; } }
        }

        public bool Flag(JobFlag flag)
        {
            return (mFlags & flag) == flag;
        }

        public void Flag(JobFlag flag, bool setOrUnset)
        {
            if (setOrUnset) //Setzen
            {
                if ((mFlags & flag) != flag) mFlags |= flag;
            }
            else //Entfernen
            {
                if ((mFlags & flag) == flag) mFlags -= flag;
            }
        }

        public int KID
        {
            get { return mKID; }
            set { if (mKID != value) { mKID = value; mChanged = true; } }
        }

        public string KName
        {
            get { return mKName; }
            set { if (mKName != value) { mKName = value; mChanged = true; } }
        }


        public DateTime ZeitFahrt
        {
            get { return mZeitFahrt; }
            set { if (mZeitFahrt != value) { mZeitFahrt = value; mChanged = true; } }
        }

        public DateTime ZeitAbfahrt
        {
            get { return mZeitAbfahrt; }
            set { if (mZeitAbfahrt != value) { mZeitAbfahrt = value; mChanged = true; } }
        }


        public DateTime ZeitStart
        {
            get { return mZeitStart; }
            set { if (mZeitStart != value) { mZeitStart = value; mChanged = true; } }
        }

        public DateTime ZeitAbgeschlossen
        {
            get { return mZeitAbgeschlossen; }
            set { if (mZeitAbgeschlossen != value) { mZeitAbgeschlossen = value; mChanged = true; } }
        }


        public DateTime ZeitAnlage
        {
            get { return mZeitAnlage; }
            //set { if (mZeitAnlage != value) { mZeitAnlage = value; mChanged = true; } }
        }

        public int UserAnlage
        {
            get { return mUserAnlage; }
            //set { if (mUserAnlage != value) { mUserAnlage = value; mChanged = true; } }
        }

        public DateTime ZeitBearbeitet
        {
            get { return mZeitBearbeitet; }
            set { if (mZeitBearbeitet != value) { mZeitBearbeitet = value; mChanged = true; } }
        }

        public int UserBearbeitet
        {
            get { return mUserBearbeitet; }
            set { if (mUserBearbeitet != value) { mUserBearbeitet = value; mChanged = true; } }
        }


        public int DauerSoll
        {
            get { return mDauerSoll; }
            set { if (mDauerSoll != value) { mDauerSoll = value; mChanged = true; } }
        }

        public byte Personen
        {
            get { return mPersonen; }
            set { if (mPersonen != value) { mPersonen = value; mChanged = true; } }
        }


        public string Optionen
        {
            get { return mOptionen; }
            set { if (mOptionen != value) { mOptionen = value; mChanged = true; } }
        }

        public string Zahlart
        {
            get { return mZahlart; }
            set { if (mZahlart != value) { mZahlart = value; mChanged = true; } }
        }

        public string KSt
        {
            get { return mKSt; }
            set { if (mKSt != value) { mKSt = value; mChanged = true; } }
        }

        public int KtrId
        {
            get { return mKtrId; }
            set { if (mKtrId != value) { mKtrId = value; mChanged = true; } }
        }

        public int TarifId
        {
            get { return mTarifId; }
            set { if (mTarifId != value) { mTarifId = value; mChanged = true; } }
        }



        public float KM
        {
            get { return mKM; }
            set { if (mKM != value) { mKM = value; mChanged = true; } }
        }

        public decimal Preis
        {
            get { return mPreis; }
            set { if (mPreis != value) { mPreis = value; mChanged = true; } }
        }

        public decimal Zuzahlung
        {
            get { return mZuzahlung; }
            set { if (mZuzahlung != value) { mZuzahlung = value; mChanged = true; } }
        }

        public int FahrzeugId
        {
            get { return mFahrzeugId; }
            set { if (mFahrzeugId != value) { mFahrzeugId = value; mChanged = true; } }
        }

        public int FahrzeugManId
        {
            get { return mFahrzeugManId; }
            set { if (mFahrzeugManId != value) { mFahrzeugManId = value; mChanged = true; } }
        }

        public int FahrerId
        {
            get { return mFahrerId; }
            set { if (mFahrerId != value) { mFahrerId = value; mChanged = true; } }
        }

        public string FlugNr
        {
            get { return mFlugNr; }
            set { if (mFlugNr != value) { mFlugNr = value; mChanged = true; } }
        }

        public string Info
        {
            get { return mInfo; }
            set { if (mInfo != value) { mInfo = value; mChanged = true; } }
        }


        public JobPos GetWgp(int Index)
        {
            try { return mPositionen[Index]; }
            catch (Exception) { return null; }
        }

        public JobPos GetWgpStart()
        {
            try { return mPositionen[0]; }
            catch (Exception) { return null; }
        }

        public JobPos GetWgpDest()
        {
            if (mPositionen.Count <= 1)
                return null;

            try { return mPositionen[mPositionen.Count - 1]; }
            catch (Exception) { return null; }
        }

        public void AddWgp(JobPos wgp)
        {
            mPositionen.Add(wgp);
        }

        public int WgpCount
        {
            get { return mPositionen.Count; }
        }


        public bool Changed
        {
            get { return mChanged; }
        }

        #endregion


        #region "Functions"

        public object Clone()
        {
            return Job.FromTaMiSerialString(this.SerializeStatic());
        }

        public override string ToString()
        {
            return mZeitFahrt + " " + mKName;
        }

        #endregion


        #region "Serialize/Deserialize Functions"

        internal bool DeserializeStatic(string serialString)
        {
            return DeserializeStatic(serialString.Split('|'));
        }

        internal bool DeserializeStatic(string[] data)
        {
            if (data == null) return false;
            int t = 0;

            mJobId = data[t++];
            mManId = Convert.ToInt32(data[t++]);
            mTyp = (JobTyp)Convert.ToInt32(data[t++]);
            mRelTyp = (JobRelTyp)Convert.ToInt32(data[t++]);
            mRelId = data[t++];
            mStatus = (JobStatus)Convert.ToInt32(data[t++]);
            mStatusDF = (JobDFStatus)Convert.ToInt32(data[t++]);
            mPrio = Convert.ToByte(data[t++]);
            mFlags = (JobFlag)Convert.ToInt32(data[t++]);
            mFlagsFaktura = (JobFlagFaktura)Convert.ToInt32(data[t++]);
            mKID = Convert.ToInt32(data[t++]);
            mKName = data[t++];

            mZeitAbfahrt = TaMiTools.SerializeString2Date(data[t++]);
            mZeitFahrt = TaMiTools.SerializeString2Date(data[t++]);
            mZeitAnlage = TaMiTools.SerializeString2Date(data[t++]);
            mUserAnlage = Convert.ToInt32(data[t++]);
            mZeitBearbeitet = TaMiTools.SerializeString2Date(data[t++]);
            mUserBearbeitet = Convert.ToInt32(data[t++]);
            mZeitAbgabe = TaMiTools.SerializeString2Date(data[t++]);
            mZeitAnkunft = TaMiTools.SerializeString2Date(data[t++]);
            mZeitStart = TaMiTools.SerializeString2Date(data[t++]);
            mZeitAbgeschlossen = TaMiTools.SerializeString2Date(data[t++]);
            mDauerSoll = Convert.ToInt32(data[t++]);
            mPersonen = Convert.ToByte(data[t++]);
            mOptionen = data[t++];
            mZahlart = data[t++];
            mKSt = data[t++];
            mKtrId = Convert.ToInt32(data[t++]);
            mTarifId = Convert.ToInt32(data[t++]);
            mWartezeit = Convert.ToInt32(data[t++]);
            mKM = Convert.ToSingle(TaMiTools.SerializeString2Double(data[t++]));
            mUStKz = Convert.ToByte(data[t++]);
            mPreis = TaMiTools.SerializeString2Decimal(data[t++]);
            mZuzahlung = TaMiTools.SerializeString2Decimal(data[t++]);
            mFahrzeugId = Convert.ToInt32(data[t++]);
            mFahrzeugManId = Convert.ToInt32(data[t++]);
            mFahrerId = Convert.ToInt32(data[t++]);
            mInfo = data[t++];
            mRufnummer = data[t++];
            mFlugNr = data[t++];

            int nPosCount = Convert.ToInt32(data[t++]);

            mPositionen.Clear();
            for (int i = 1; i <= nPosCount; i++)
            {
                JobPos pos = new JobPos();
                pos.DeserializeStatic(data, t);

                mPositionen.Add(pos);

                t += 12; //12 Felder in JobPos
            }

            mChanged = false;
            return true;
        }

        internal string SerializeStatic()
        {
            const char DELIM = '|';
            StringBuilder sb = new StringBuilder();

            sb.Append(mJobId); sb.Append(DELIM);
            sb.Append(mManId); sb.Append(DELIM);
            sb.Append((int)mTyp); sb.Append(DELIM);
            sb.Append((byte)mRelTyp); sb.Append(DELIM);
            sb.Append(mRelId); sb.Append(DELIM);
            sb.Append((int)mStatus); sb.Append(DELIM);
            sb.Append((int)mStatusDF); sb.Append(DELIM);
            sb.Append(mPrio); sb.Append(DELIM);
            sb.Append((int)mFlags); sb.Append(DELIM);
            sb.Append((int)mFlagsFaktura); sb.Append(DELIM);
            sb.Append(mKID); sb.Append(DELIM);
            sb.Append(mKName); sb.Append(DELIM);
            sb.Append(TaMiTools.SerializeDate2String(mZeitAbfahrt)); sb.Append(DELIM);
            sb.Append(TaMiTools.SerializeDate2String(mZeitFahrt)); sb.Append(DELIM);

            sb.Append(TaMiTools.SerializeDate2String(mZeitAnlage)); sb.Append(DELIM);
            sb.Append(mUserAnlage); sb.Append(DELIM);
            sb.Append(TaMiTools.SerializeDate2String(mZeitBearbeitet)); sb.Append(DELIM);
            sb.Append(mUserBearbeitet); sb.Append(DELIM);

            sb.Append(TaMiTools.SerializeDate2String(mZeitAbgabe)); sb.Append(DELIM);
            sb.Append(TaMiTools.SerializeDate2String(mZeitAnkunft)); sb.Append(DELIM);
            sb.Append(TaMiTools.SerializeDate2String(mZeitStart)); sb.Append(DELIM);
            sb.Append(TaMiTools.SerializeDate2String(mZeitAbgeschlossen)); sb.Append(DELIM);

            sb.Append(mDauerSoll); sb.Append(DELIM);
            sb.Append(mPersonen); sb.Append(DELIM);
            sb.Append(mOptionen); sb.Append(DELIM);
            sb.Append(mZahlart); sb.Append(DELIM);
            sb.Append(mKSt); sb.Append(DELIM);
            sb.Append(mKtrId); sb.Append(DELIM);
            sb.Append(mTarifId); sb.Append(DELIM);
            sb.Append(mWartezeit); sb.Append(DELIM);
            sb.Append(TaMiTools.SerializeDouble2String(mKM)); sb.Append(DELIM);
            sb.Append(mUStKz); sb.Append(DELIM);
            sb.Append(TaMiTools.SerializeDecimal2String(mPreis)); sb.Append(DELIM);
            sb.Append(TaMiTools.SerializeDecimal2String(mZuzahlung)); sb.Append(DELIM);
            sb.Append(mFahrzeugId); sb.Append(DELIM);
            sb.Append(mFahrzeugManId); sb.Append(DELIM);
            sb.Append(mFahrerId); sb.Append(DELIM);
            sb.Append(mInfo); sb.Append(DELIM);
            sb.Append(mRufnummer); sb.Append(DELIM);
            sb.Append(mFlugNr); sb.Append(DELIM);

            sb.Append(mPositionen.Count); sb.Append(DELIM);

            for (int i = 0; i < mPositionen.Count; i++)
            {
                JobPos pos = mPositionen[i];

                sb.Append((byte)pos.Flags); sb.Append(DELIM);
                sb.Append(pos.QID); sb.Append(DELIM);
                sb.Append(pos.Quick); sb.Append(DELIM);
                sb.Append(pos.SID); sb.Append(DELIM);
                sb.Append(pos.Strasse); sb.Append(DELIM);
                sb.Append(pos.HNr); sb.Append(DELIM);
                sb.Append(pos.OID); sb.Append(DELIM);
                sb.Append(pos.PLZ); sb.Append(DELIM);
                sb.Append(pos.Ort); sb.Append(DELIM);
                sb.Append(pos.Vorlauf); sb.Append(DELIM);
                sb.Append(TaMiTools.SerializeDouble2String(pos.Lat)); sb.Append(DELIM);
                sb.Append(TaMiTools.SerializeDouble2String(pos.Lng)); sb.Append(DELIM);
            }

            return sb.ToString();
        }

        #endregion

        #region "Static Function"

        public static Job FromTaMiSerialString(string serialisationString)
        {
            Job job = new Job();
            if (!job.DeserializeStatic(serialisationString))
                throw new Exception("Unable to deserialize job");

            return job;
        }

        #endregion

        #region "FMS JSON deserialize/serialize Functions"
#if FMSJSON

        public void FromFMSJSON(string jsonText)
        {
            JsonDocument json = JsonDocument.Parse(jsonText);
            JobPos wgp;
            bool isTest = false;
            int orderType = -1;
            int priceType = 0; //1 - fixed price, 2 - estimated price, 3 - base price


            //Debug.WriteLine($"Job.FromJSON: {jsonText}");

            mTyp = JobTyp.AUTOBOOK;
            mRelTyp = JobRelTyp.DBVOUCHER;
            mZeitFahrt = DateTime.Now;
            mZeitAbfahrt = DateTime.Now;

            foreach (JsonProperty property in json.RootElement.EnumerateObject())
            {
                //Debug.WriteLine($" - property: '{property.Name}'='{property.Value}'");

                switch (property.Name.ToLower())
                {
                    case "ordertype": orderType = Convert.ToInt32(property.Value.ToString()); break; //1=Taxi
                    case "departuretime":
                    {
                        string dateStr = property.Value.ToString();

                        if (dateStr != null && dateStr.Length > 0)
                        {
                            mZeitFahrt = DateTime.ParseExact(dateStr, "dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
                            if (Math.Abs((mZeitFahrt - DateTime.Now).TotalSeconds) > 300)
                                mTyp = JobTyp.NORMAL;
                            else
                                mTyp = JobTyp.SOFORT;
                        }

                        break;
                    }

                    case "referenceid": mRelId = property.Value.ToString(); break; //FMS Auftrags-Id
                    case "customername": mKName = property.Value.ToString(); break;
                    case "customerphone": mRufnummer = property.Value.ToString(); break;
                    case "costcenter": mKSt = property.Value.ToString(); break;
                    case "test": isTest = property.Value.ToString().Equals("true", StringComparison.InvariantCultureIgnoreCase); break;

                    //Startadresse
                    case "departureaddress":
                    {
                        wgp = new JobPos();
                        wgp.FromFMSJSON(property.Value);

                        if (mPositionen.Count > 0)
                            mPositionen.Insert(0, wgp);
                        else
                            mPositionen.Add(wgp);

                        break;
                    }

                    //Zieladresse
                    case "destinationaddress":
                    {
                        wgp = new JobPos();
                        wgp.FromFMSJSON(property.Value);

                        if (wgp.Changed)
                            mPositionen.Add(wgp);

                        break;
                    }

                    //Zwischenstops
                    case "stopovers": //Sollte immer nach departure und destination kommen.
                    {
                        foreach (JsonElement stopoverElement in property.Value.EnumerateArray())
                        {
                            //Debug.WriteLine("stopover: " + stopoverElement.ToString());
                            wgp = new JobPos();
                            wgp.FromFMSJSON(stopoverElement);

                            //Nur wenn das stopoverElement nicht leer ist
                            if (wgp.Changed)
                            {
                                //Wenn mindestens 2 Wegpunkte vorhanden sind, vor dem letzten (destination) einfügen. Sonst anhängen
                                if (mPositionen.Count > 1)
                                    mPositionen.Insert(mPositionen.Count - 1, wgp);
                                else
                                    mPositionen.Add(wgp);
                            }
                        }

                        break;
                    }

                    //List of requested order options. The order options must be activated on the dispatch center as well to work properly. (The full list of all different possible order options is shown in the general API description).
                    case "attributelist":
                    {
                        foreach (JsonElement attributelistElement in property.Value.EnumerateArray())
                        {
                            //Debug.WriteLine("attributelist: " + transportElement.ToString());

                            foreach (JsonProperty attributeProperty in attributelistElement.EnumerateObject())
                            {
                                //Debug.WriteLine("attributeProperty: " + attributeProperty.ToString());
                                string value = attributeProperty.Value.ToString();

                                switch (attributeProperty.Name.ToLower())
                                {
                                    case "id":
                                    {
                                        int attributId = Convert.ToInt32(value);
                                        switch (attributId)
                                        {
                                            case 130: mOptionen += "KISI "; break; //Kindersitzerhöhung (Klasse 2/3) 
                                        }
                                    }
                                    break;
                                }
                            }

                        }
                        break;
                    }

                    case "transportlist":
                    {
                        mPersonen = 0;

                        foreach (JsonElement transportElement in property.Value.EnumerateArray())
                        {
                            //Debug.WriteLine("transportList: " + transportElement.ToString());
                            string transportType = string.Empty;
                            int transportAmount = 0;

                            foreach (JsonProperty transportProp in transportElement.EnumerateObject())
                            {
                                string value = transportProp.Value.ToString();

                                switch (transportProp.Name.ToLower())
                                {
                                    case "type": transportType = value; break;
                                    case "count": transportAmount = Convert.ToInt32(value); break;
                                }
                            }

                            if (transportType.Equals("PASSENGER", StringComparison.InvariantCultureIgnoreCase))
                                mPersonen += (byte)transportAmount;

                        }


                        break;
                    }

                    case "price":
                    {
                        foreach (JsonProperty priceProperty in property.Value.EnumerateObject())
                        {
                            //Debug.WriteLine("priceProperty: " + priceProperty.ToString());
                            string value = priceProperty.Value.ToString();

                            switch (priceProperty.Name.ToLower())
                            {
                                case "amount": mPreis = Convert.ToDecimal(value, CultureInfo.InvariantCulture); break;
                                case "taxpercentage": mUStKz = TaMiTools.SteuersatzZuKz(Convert.ToSingle(value, CultureInfo.InvariantCulture)); break;
                                case "tarifkm": mKM = Convert.ToSingle(value, CultureInfo.InvariantCulture); break;
                                case "type": priceType = Convert.ToInt32(value, CultureInfo.InvariantCulture); break;
                            }
                        }

                        break;
                    }

                    case "voucher":
                    {
                        string voucherType = string.Empty;
                        string voucherCode = string.Empty;
                        string voucherId = string.Empty;
                        string tccRefId = string.Empty; //Wird von TCC durch die aktivierung des Gutscheins vergeben

                        foreach (JsonProperty voucherProperty in property.Value.EnumerateObject())
                        {
                            //Debug.WriteLine("voucherProperty: " + voucherProperty.ToString());
                            string value = voucherProperty.Value.ToString();

                            switch (voucherProperty.Name.ToLower())
                            {
                                case "vouchertype": voucherType = value; break;
                                case "vouchercode": voucherCode = value; break;
                                case "voucherid": voucherId = value; break;
                                case "referenceid": tccRefId = value; break; //referenceId die von TCC bei der Eingabe/Abruf des Codes generiert wurde
                            }
                        }

                        //Voucher ist ein DB-Gutschein
                        mRelTyp = (voucherType.Equals("DB", StringComparison.InvariantCultureIgnoreCase) ? JobRelTyp.DBVOUCHER : JobRelTyp.KEIN);
                        mInfo = "{DB$ VCODE: " + voucherCode + " ID: " + voucherId + " %ADDITIONAL_PRICE_INFO% $}";

                        //Ist dieser Wert gesetzt wurde der Auftrag über ein Fahrzeug abgerufen/erstellt
                        //Beispiel RefId: "-NZVW4dgY2_AevZjm3fS:902" nach dem : kommt die FahrzeugId
                        if (tccRefId.Length > 0)
                        {
                            string[] parts = tccRefId.Split(':');
                            if (parts.Length >= 2)
                            {
                                mFahrzeugId = Convert.ToInt32(parts[1]);
                            }
                        }

                        break;
                    }

                }
            } //foreach

            //DB-Gutschein
            if (mRelTyp == JobRelTyp.DBVOUCHER)
            {
                mZahlart = "RF";
            }

            //Wenn priceTyp = 2, oder priceTyp = 3 ist, dann ist es kein Festpreis. Felder umschreiben, so dass der Fahrer den Taxameterpreis eingeben muss.
            if (priceType != 1)
            {
                string additionalInfo = "CIRCA-PREIS: " + mPreis + " CIRCA-KM: " + mKM;
                mInfo = mInfo.Replace("%ADDITIONAL_PRICE_INFO%", additionalInfo);
                mPreis = 0; //Damit der Fahrer eine Angabe machen muss
                mKM = 0;    //Damit die KM vom Taxameter oder GPS übermittelt werden
            }
            //Festpreis
            else
            {
                mInfo = mInfo.Replace("%ADDITIONAL_PRICE_INFO%", "FEST-PREIS: " + mPreis);
            }

        }

#endif //FMSJSON
        #endregion
    }
}
