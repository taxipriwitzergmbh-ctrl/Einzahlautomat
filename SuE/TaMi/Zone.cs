using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SuE.TaMi
{

    public enum ZoneTypes : int
    {
        RAUM = 1,
        HALTEPLATZ = 2
    }

    public enum ZoneFlags : int
    {
        LOCKED = 0x1,

        TARGET = 0x10,
        INVISIBLEDRIVER = 0x20
    }

    public enum DispatchOrder : int
    {
        FIRSTFHZFIRST = 0,
        LASTFHZFIRST = 1,
        SECONDFHZFIRST = 2,
        FIRSTFHZFIRSTWITHEXCEPTION = 3
    }

    public enum ZoneStates : int
    {
        NONE = 0,
        AVAILABLE = 1,
        UNAVAILABLE = 2,
        TARGET = 3
    }

    public class Zone
    {
        int mId;
        ZoneTypes mTyp;
        ZoneFlags mFlags;
        string mName;
        string mKürzel;
        string mFahrtzielGruppe;
        int mFahrtzielIndex;
        DispatchOrder mDispatchOrder;
        double mMidLat;
        double mMidLng;

        //Private mcFhzAvail As Collection       'Prio 1: Eingebucht und verfügbar (Frei, Pause)
        //Private mcFhzUnAvail As Collection     'Prio 2: Eingebucht und nicht verfügbar (Besetzt, usw.)
        //Private mcFhzTarget As Collection      'Angemeldete Fahrzeuge als Fahrziel

        public int Id
        {
            get { return mId; }
        }

        public ZoneTypes Typ
        {
            get { return mTyp; }
        }

        public string Name
        {
            get { return mName; }
        }

        public string Kürzel
        {
            get { return mKürzel; }
        }


        internal bool deserializeStatic(string[] data)
        {
            if (data == null) return false;

            //final char DOT = '.';
            //final char COMMA = ',';

            //string[] tokens = data.Split(TaMiClient.SEP_CHAR_COL);
            int t = 0;

            mId = Convert.ToInt32(data[t++]);
            mTyp = (ZoneTypes)Convert.ToInt32(data[t++]);
            mFlags = (ZoneFlags)Convert.ToInt32(data[t++]);
            mName = data[t++];
            mKürzel = data[t++];
            mFahrtzielGruppe = data[t++];
            mFahrtzielIndex = Convert.ToInt32(data[t++]);
            mDispatchOrder = (DispatchOrder)Convert.ToInt32(data[t++]);

            mMidLat = TaMiTools.SerializeString2Double(data[t++]);
            mMidLng = TaMiTools.SerializeString2Double(data[t++]);

            return true;
        }

        public override string ToString()
        {
            return "Id: " + mId + " Name: " + mName;
        }

    }
}
