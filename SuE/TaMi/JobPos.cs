using System;
using System.Globalization;

#if FMSJSON
using System.Text.Json;
#endif

namespace SuE.TaMi
{
    /*
        PROJEKT           :  JobPos
        VERSION           :  1.03
        
        ERSTELLUNGS-DATUM :  17.10.2018
        ÄNDERUNGS-DATUM   :  18.09.2024
        ÄNDERUNG:            siehe Entwicklungsgeschichte
        DURCHGEFUEHRT VON :  SuE-Software  [sue]

        FUNKTION          :  Wegpunkt eines Job

        BEMERKUNG         :  ---

        ======================== ENTWICKLUNGS-GESCHICHTE ========================

        --- Version 1.00 --------------------------------------------------------

        17.10.2018  mcs   Beginn der Implementation in NET


        --- Version 1.02 --------------------------------------------------------

        12.09.2023  mcs   GetWgpStart und GetWgpDest hinzugefügt.
                          define FMSJSON hinzugefügt
                          Static Funktion Job.SerialString hinzugefügt

        --- Version 1.03 --------------------------------------------------------

        16.01.24  mcs     Enum TAMI_WGP_TYPEN in TAMI_WGP_FLAGS umbenannt und
                          entsprechend &H1, &H2, usw.
                          Property WgpTyp in Flags umgeändert

        18.09.24  mcs     ICloneable implementiert

    */

    public class JobPos : ICloneable
    {
        #region "Variables"

        //Statische Daten
        private JobPosFlags mFlags = 0;

        private int mQID = 0;
        private string mQuick = string.Empty;

        private int mSID = 0;
        private string mStrasse = string.Empty;
        private string mHNr = string.Empty;

        private int mOID = 0;
        private string mPLZ = string.Empty;
        private string mOrt = string.Empty;

        private int mVorlauf = 0;

        private double mLat = 0;
        private double mLng = 0;

        private bool mChanged = false;

        #endregion

        #region "Propertys"

        public JobPosFlags Flags
        {
            get { return mFlags; }
            set { if (mFlags != value) { mFlags = value; mChanged = true; } }
        }

        public int QID
        {
            get { return mQID; }
            set { if (mQID != value) { mQID = value; mChanged = true; } }
        }

        public string Quick
        {
            get { return mQuick; }
            set { if (mQuick != value) { mQuick = value; mChanged = true; } }
        }

        public int SID
        {
            get { return mSID; }
            set { if (mSID != value) { mSID = value; mChanged = true; } }
        }

        public string Strasse
        {
            get { return mStrasse; }
            set { if (mStrasse != value) { mStrasse = value; mChanged = true; } }
        }

        public string HNr
        {
            get { return mHNr; }
            set { if (mHNr != value) { mHNr = value; mChanged = true; } }
        }

        public int OID
        {
            get { return mOID; }
            set { if (mOID != value) { mOID = value; mChanged = true; } }
        }

        public string PLZ
        {
            get { return mPLZ; }
            set { if (mPLZ != value) { mPLZ = value; mChanged = true; } }
        }

        public string Ort
        {
            get { return mOrt; }
            set { if (mOrt != value) { mOrt = value; mChanged = true; } }
        }

        public int Vorlauf
        {
            get { return mVorlauf; }
            set { if (mVorlauf != value) { mVorlauf = value; mChanged = true; } }
        }

        public double Lat
        {
            get { return mLat; }
            set { if (mLat != value) { mLat = value; mChanged = true; } }
        }

        public double Lng
        {
            get { return mLng; }
            set { if (mLng != value) { mLng = value; mChanged = true; } }
        }

        public bool Changed
        {
            get { return mChanged; }
        }


        #endregion

        #region "Functions"

        public string GetText(string separator, bool bPLZ)
        {
            if (separator == null) separator = ", ";
            else if (separator.Length == 0) separator = ", ";

            string szTemp = "";

            if (mQuick.Length > 0) szTemp += mQuick + separator;
            if (mStrasse.Length > 0) szTemp += (mStrasse + " " + mHNr).Trim() + separator;
            if ((bPLZ) && (mPLZ.Length + mOrt.Length > 0)) szTemp += (mPLZ + " " + mOrt).Trim() + separator;
            if ((bPLZ == false) && (mOrt.Length > 0)) szTemp += mOrt.Trim() + separator;

            if (szTemp.EndsWith(separator))
                szTemp = szTemp.Substring(0, szTemp.Length - separator.Length);

            return szTemp;
        }

        public bool IsLatLngValid()
        {
            return (mLat != 0 && mLng != 0);
        }

        public override string ToString()
        {
            return GetText(", ", true);
        }

        #endregion


        #region "Deserialize Functions"

        internal bool DeserializeStatic(string[] data, int startIndex)
        {
            if (data == null) return false;

            int t = startIndex;

            mFlags = (JobPosFlags)Convert.ToInt32(data[t++]);
            mQID = Convert.ToInt32(data[t++]);
            mQuick = data[t++];
            mSID = Convert.ToInt32(data[t++]);
            mStrasse = data[t++];
            mHNr = data[t++];
            mOID = Convert.ToInt32(data[t++]);
            mPLZ = data[t++];
            mOrt = data[t++];
            mVorlauf = Convert.ToInt32(data[t++]);
            mLat = Convert.ToSingle(TaMiTools.SerializeString2Double(data[t++]));
            mLng = Convert.ToSingle(TaMiTools.SerializeString2Double(data[t++]));

            return true;
        }

        #endregion

        #region "FMS JSON deserialize/serialize Functions"
#if FMSJSON

        public void FromFMSJSON(JsonElement jsonElement)
        {
            //string typeStr = string.Empty;
            string state = "DE";
            string text = string.Empty;
            string stopoverType = string.Empty; //Gültige Werte: "pickup" oder "dropoff"

            foreach (JsonProperty depProperty in jsonElement.EnumerateObject())
            {
                string value = depProperty.Value.ToString();

                switch (depProperty.Name.ToLower())
                {
                    case "x":               mLng = Convert.ToDouble(value, CultureInfo.InvariantCulture); break;
                    case "y":               mLat = Convert.ToDouble(value, CultureInfo.InvariantCulture); break;
                    case "city":            mOrt = value; break;
                    case "postcode":        mPLZ = value; break;
                    case "streetname":      mStrasse = value; break;
                    case "streetnumber":    mHNr = value; break;
                    case "state":           state = value; break; //Nation (ISO 3166-1 alpha-2)
                    case "text":            text = value; break; //Access description
                    case "poiname":         mQuick = value; break;

                    case "type":            stopoverType = value; break; //"pickup" oder "dropoff" bei stopovers
                }

                mChanged = true;
            }

            if (mChanged)
            {
                //Ländercode anfügen, wenn es nicht im Heimatland ist
                if (!"DE".Equals(state,StringComparison.InvariantCultureIgnoreCase) && !"D".Equals(state, StringComparison.InvariantCultureIgnoreCase) && mPLZ.Length > 0)
                {
                    mPLZ = state + "-" + mPLZ;
                }
            }

        }

        public void FromFMSJSON(JsonProperty jsonProperty)
        {
            FromFMSJSON(jsonProperty.Value);
        }

#endif //FMSJSON
        #endregion

        #region "ICloneable"

        //Siehe: https://stackoverflow.com/questions/21116554/proper-way-to-implement-icloneable
        //Werte werden kopiert, aber Objekte die referenziert sind nicht! (Dazu bitte Link lesen)
        public object Clone()
        {
            return this.MemberwiseClone();
        }

        #endregion

    }
}
