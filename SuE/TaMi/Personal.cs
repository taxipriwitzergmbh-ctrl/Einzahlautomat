using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SuE.TaMi
{
    public enum PersonalFlags : int
    {
        LOCKED                              = 0x1,

        IGNORELOGONSAMEMANDANT              = 0x100, //Diese Personalnummer kann sich aber doch auf allen Fahrzeugen aller Mandanten anmelden

        PERSONAL_FLAG_KEINE_ZEITERFASSUNGS_ANSICHT  = 0x100000, //Im Einzahlautomat soll für diesen Mitarbeiter keine Arbeitszeiten angezeigt werden, Button Zeiterfassung ausblenden
        PERSONAL_FLAG_ZEITERFASSUNG_EINZAHLAUTOMAT  = 0x200000  //Die Arbeitszeit kann im Einzahlautomat gestartet und beendet werden. Nur wenn dieses Flag vorhanden ist, soll der Button Anstempel/Abstempel bzw. Pause angezeigt werden.
    }

    public enum PersonalBeschäftigungsarten
    {
        DISPONENT = 0x1,
        FAHRER = 0x2,
        AUSHILFE = 0x4,
        TRAININGMODUS = 0x8,
        BEGLEITPERSON = 0x10,
 
        UNTERNEHMER = 0x80
    }


    public class Personal
    {
        private int         mId;
        private int         mManId;
        private PersonalFlags mFlags;
        private string      mOptionen;
        private int         mBeschäftigungsart;
        private string      mKürzel;
        private string      mAnrede;
        private string      mName;
        private string      mVorname;
        private string      mStrasse;
        private string      mHNr;
        private string      mPLZ;
        private string      mOrt;
        private string      mTelefon1;
        private string      mTelefon2;
        private DateTime    mGeburtstag; //CDate2SerialStr(mdGeburtstag, True)

        public string FormatName()
        {
            return (mVorname + " " + mName).Trim();
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

        public PersonalFlags Flags
        {
            get { return (PersonalFlags)mFlags; }
        }

        public bool HasFlag(PersonalFlags flag)
        {
            return (mFlags & flag) == flag;
        }

        public string Kürzel
        {
            get { return mKürzel; }
        }

        public string Anrede
        {
            get { return mAnrede; }
        }

        public string Name
        {
            get { return mName; }
        }

        public string Vorname
        {
            get { return mVorname; }
        }

        public string Telefon1
        {
            get { return mTelefon1; }
        }

        public string Telefon2
        {
            get { return mTelefon2; }
        }

        #endregion


        internal bool deserializeStatic(string[] data)
        {
            if (data == null) return false;

            //final char DOT = '.';
            //final char COMMA = ',';

            //string[] tokens = data.Split(TaMiClient.SEP_CHAR_COL);
            int t = 0;

            mId = Convert.ToInt32(data[t++]);
            mManId = Convert.ToInt32(data[t++]);
            mFlags = (PersonalFlags)Convert.ToInt32(data[t++]);
            mOptionen = data[t++];
            mBeschäftigungsart = Convert.ToInt32(data[t++]);
            mKürzel = data[t++];
            mAnrede = data[t++];
            mName = data[t++];
            mVorname = data[t++];
            mStrasse = data[t++];
            mHNr = data[t++];
            mPLZ = data[t++];
            mOrt = data[t++];
            mTelefon1 = data[t++];
            mTelefon2 = data[t++];
            mGeburtstag = TaMiTools.SerializeString2Date(data[t++]);

            return true;
        }

    }
}
