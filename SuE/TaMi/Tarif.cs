using SuE.TaMi;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace SuE.TaMi
{
    /*  Version 1.0 (Last Change: 21.04.23) */
    /* 
     * Version 1.0 
     * ------------------------------------------------------------------------------------------------------
     * 
     * 21.04.23: ChangeLog angelegt.
     *           Implementation auf Basis von CTarifStrecke aus Faktura/Dispo
     *           
     * Version 1.1 
     * ------------------------------------------------------------------------------------------------------
     * 
     * 21.04.23: Code auf Basis von Faktura.CTarifStrecke Version 1.95 aktualisiert
     *
     */

    public enum TarifFlags : int
    {
        NETTO = 0x1,
        NOGRADUATION = 0x02,

        DTA_SWAPAMOUNTANDPRICE = 0x80, /*Bei Taxameter/Vereinbarungspreis wird Menge und Preis im DTA Export vertausch (die AOK braucht das so)*/
        DTA = 0x100,
        DTA_STADT = 0x200,
        DTA_WAITTIMECOMPLETE = 0x400, /* Nach Übersteigen der Freiminuten werden die gesamten Minuten der Wartezeit berechnet */

        HIDEINPRICEINFO = 0x800,
        HIDEINFAKTURA = 0x1000,
        HIDEINDISPO = 0x2000,

        LEERKM_NICHT_GESAMTKM = 0x10000,
        WARTEZEIT_NICHT_JEANGEFANGENES = 0x40000,

    }

    public class Tarif
    {
        #region "Variables"

        //Statische Daten
        private int     mId;
        private string  mName = "";
        private int     mFlags = 0;

        private string  mDTAVertragsschlüssel = "";

        private float   mKMKorrekturFaktor = 1;

        private byte    mSteuersatz1 = 1;
        private float   mSteuerStrecke = 0;

        private float   mRabattPercent = 0;
        private string  mRabattDTAPNr = "";

        private float   mFixKosten1StreckeVon = 0;
        private float   mFixKosten1StreckeBis = 0;
        private float   mFixKosten1StreckeFrei = 0;
        private decimal mFixKosten1 = 0;        
        private string  mFixKosten1DTAPNr = "";

        private float   mFixKosten2StreckeVon = 0;
        private float   mFixKosten2StreckeBis = 0;
        private float   mFixKosten2StreckeFrei = 0;
        private decimal mFixKosten2 = 0;
        private string  mFixKosten2DTAPNr = "";

        private float   mBesetzt1Strecke = 0;
        private decimal mBesetzt1Kosten = 0;
        private string  mBesetzt1DTAPNr = "";
        private float   mBesetzt2Strecke = 0;
        private decimal mBesetzt2Kosten = 0;
        private string  mBesetzt2DTAPNr = "";
        private float   mBesetzt3Strecke = 0;
        private decimal mBesetzt3Kosten = 0;
        private string  mBesetzt3DTAPNr = "";
        private float   mBesetzt4Strecke = 0;
        private decimal mBesetzt4Kosten = 0;
        private string  mBesetzt4DTAPNr = "";
        private decimal mBesetzt5Kosten = 0;
        private string  mBesetzt5DTAPNr = "";

        private double  mLeer1Strecke = 0;
        private decimal mLeer1Kosten = 0;
        private string  mLeer1DTAPNr = "";
        private double  mLeer2Strecke = 0;
        private decimal mLeer2Kosten = 0;
        private string  mLeer2DTAPNr = "";
        private double  mLeer3Strecke = 0;
        private decimal mLeer3Kosten = 0;
        private string  mLeer3DTAPNr = "";
        private double  mLeer4Strecke = 0;
        private decimal mLeer4Kosten = 0;
        private string  mLeer4DTAPNr = "";
        private decimal mLeer5Kosten = 0;
        private string  mLeer5DTAPNr = "";

        private decimal mWartezeitKosten = 0;
        private int     mWartezeitProMin = 0;
        private int     mWartezeitFreiMin = 0;
        private string  mWartezeitDTAPNr = "";

        private float   mMehrpersFaktor = 1;
        private float   mMehrpersZuschlag2Per = 0;
        private float   mMehrpersZuschlag3Per = 0;
        private string  mMehrpersDTAPNr = "";

        private decimal mMindestBetrag = 0;
        private string  mMindestBetragDTAPNr = "";



        #endregion

        #region "Propertys"

        public int Id
        {
            get { return mId; }
            //set { if (mId != value) { mId = value; mChanged = true; } }
        }

        public string Name
        {
            get { return mName; }
        }

        public int Flags
        {
            get { return mFlags; }
        }

        public bool HasFlag(TarifFlags flag)
        {
            return (mFlags & ((int)flag)) == (int)flag;
        }

        #endregion


        #region "Functions"




        //Gibt die Anzahl der Frei (enthaltenen) KM anhand der Gesamtstrecke zurück
        public double   GetEnthalteneKMBesetzt(double kmBesetzt)
        {
            double freiKM = 0;

            if (kmBesetzt >= mFixKosten1StreckeVon && kmBesetzt <= mFixKosten1StreckeBis)
                freiKM += mFixKosten1StreckeFrei;

            if (kmBesetzt >= mFixKosten2StreckeVon && kmBesetzt <= mFixKosten2StreckeBis)
                freiKM += mFixKosten2StreckeFrei;

            return freiKM;
        }

        //Gibt die Grundgebühren anhand der KM zurück
        public decimal CalculateFixKosten(double kmBesetzt, double kmLeer)
        {
            double gesamtKM;
            decimal amount = 0;

            if (HasFlag(TarifFlags.LEERKM_NICHT_GESAMTKM))
                gesamtKM = kmBesetzt;
            else
                gesamtKM = kmBesetzt + kmLeer;

            //Fixkosten1
            if (gesamtKM >= mFixKosten1StreckeVon && gesamtKM <= mFixKosten1StreckeBis)
                amount += mFixKosten1;

            //Fixkosten2
            if (gesamtKM >= mFixKosten2StreckeVon && gesamtKM <= mFixKosten2StreckeBis)
                amount += mFixKosten2;

            return amount;
        }

        public decimal CalculateWartezeit(int wartezeit)
        {
            if (mWartezeitKosten == 0) { return 0; }
            if (mWartezeitProMin == 0) { return 0; }
            if (wartezeit - mWartezeitFreiMin <= 0) {  return 0; }

            int wartezeitMenge;

            if (HasFlag(TarifFlags.DTA_WAITTIMECOMPLETE))
                wartezeitMenge = Math.Abs(wartezeit / mWartezeitProMin);
            else
                wartezeitMenge = Math.Abs((wartezeit - mWartezeitFreiMin) / mWartezeitProMin);

            if (HasFlag(TarifFlags.WARTEZEIT_NICHT_JEANGEFANGENES) && mWartezeitProMin > 1)
            {
                if (wartezeit % mWartezeitProMin > 0) wartezeitMenge += 1;
            }
            
            return wartezeitMenge * mWartezeitKosten;
        }

        public decimal CalculateKMBesetzt(double kmBesetzt)
        {
            decimal amount = 0;

            if (kmBesetzt != 0)
            {
                //Ohne Staffelung
                if (HasFlag(TarifFlags.NOGRADUATION))
                {
                    //Besetzt KM
                    if (kmBesetzt >= 0 && kmBesetzt <= mBesetzt1Strecke)
                        amount = (decimal)kmBesetzt * mBesetzt1Kosten;

                    else if (kmBesetzt > mBesetzt1Strecke && kmBesetzt <= mBesetzt2Strecke)
                        amount = (decimal)kmBesetzt * mBesetzt2Kosten;

                    else if (kmBesetzt > mBesetzt2Strecke && kmBesetzt <= mBesetzt3Strecke)
                        amount = (decimal)kmBesetzt * mBesetzt3Kosten;

                    else if (kmBesetzt > mBesetzt3Strecke && kmBesetzt <= mBesetzt4Strecke)
                        amount = (decimal)kmBesetzt * mBesetzt4Kosten;

                    else if (kmBesetzt > mBesetzt4Strecke)
                        amount = (decimal)kmBesetzt * mBesetzt5Kosten;

                }

                //Mit Staffelung
                else
                {
                    double kmRest, kmStufe;

                    //Besetzt KM
                    kmRest = kmBesetzt;

                    //Stufe 1
                    kmStufe = mBesetzt1Strecke;
                    amount += (decimal)(kmStufe > kmRest ? kmRest : kmStufe) * mBesetzt1Kosten;
                    kmRest = kmRest - kmStufe;
                    if (kmRest <= 0) goto KMBesetztComplete;

                    //Stufe 2
                    kmStufe = mBesetzt2Strecke - (kmBesetzt - kmRest);
                    amount += (decimal)(kmStufe > kmRest ? kmRest : kmStufe) * mBesetzt2Kosten;
                    kmRest = kmRest - kmStufe;
                    if (kmRest <= 0) goto KMBesetztComplete;

                    //Stufe 3
                    kmStufe = mBesetzt3Strecke - (kmBesetzt - kmRest);
                    amount += (decimal)(kmStufe > kmRest ? kmRest : kmStufe) * mBesetzt3Kosten;
                    kmRest = kmRest - kmStufe;
                    if (kmRest <= 0) goto KMBesetztComplete;

                    //Stufe 4
                    kmStufe = mBesetzt4Strecke - (kmBesetzt - kmRest);
                    amount += (decimal)(kmStufe > kmRest ? kmRest : kmStufe) * mBesetzt4Kosten;
                    kmRest = kmRest - kmStufe;
                    if (kmRest <= 0) goto KMBesetztComplete;

                    //Rest KM bis unendlich
                    amount += (decimal)kmRest * mBesetzt5Kosten;
                }
            } //if (kmBesetzt != 0)

            KMBesetztComplete:

            return amount;
        }

        public decimal CalculateKMLeer(double kmLeer)
        {
            decimal amount = 0;

            if (kmLeer != 0)
            {
                //Ohne Staffelung
                if (HasFlag(TarifFlags.NOGRADUATION))
                {
                    //Leer KM
                    if (kmLeer >= 0 && kmLeer <= mLeer1Strecke)
                        amount = (decimal)kmLeer * mLeer1Kosten;

                    else if (kmLeer > mLeer1Strecke && kmLeer <= mLeer2Strecke)
                        amount = (decimal)kmLeer * mLeer2Kosten;

                    else if (kmLeer > mLeer2Strecke && kmLeer <= mLeer3Strecke)
                        amount = (decimal)kmLeer * mLeer3Kosten;

                    else if (kmLeer > mLeer3Strecke && kmLeer <= mLeer4Strecke)
                        amount = (decimal)kmLeer * mLeer4Kosten;

                    else if (kmLeer > mLeer4Strecke)
                        amount = (decimal)kmLeer * mLeer5Kosten;

                }

                //Mit Staffelung
                else
                {
                    double kmRest, kmStufe;

                    //Leer KM
                    kmRest = kmLeer;

                    //Stufe 1
                    kmStufe = mLeer1Strecke;
                    amount += (decimal)(kmStufe > kmRest ? kmRest : kmStufe) * mLeer1Kosten;
                    kmRest = kmRest - kmStufe;
                    if (kmRest <= 0) goto KMLeerComplete;

                    //Stufe 2
                    kmStufe = mLeer2Strecke - (kmLeer - kmRest);
                    amount += (decimal)(kmStufe > kmRest ? kmRest : kmStufe) * mLeer2Kosten;
                    kmRest = kmRest - kmStufe;
                    if (kmRest <= 0) goto KMLeerComplete;

                    //Stufe 3
                    kmStufe = mLeer3Strecke - (kmLeer - kmRest);
                    amount += (decimal)(kmStufe > kmRest ? kmRest : kmStufe) * mLeer3Kosten;
                    kmRest = kmRest - kmStufe;
                    if (kmRest <= 0) goto KMLeerComplete;

                    //Stufe 4
                    kmStufe = mLeer4Strecke - (kmLeer - kmRest);
                    amount += (decimal)(kmStufe > kmRest ? kmRest : kmStufe) * mLeer4Kosten;
                    kmRest = kmRest - kmStufe;
                    if (kmRest <= 0) goto KMLeerComplete;

                    //Rest KM bis unendlich
                    amount += (decimal)kmRest * mLeer5Kosten;
                }
            } //if (kmLeer != 0)

            KMLeerComplete:

            return amount;
        }

        public decimal Calculate(double kmBesetzt, double kmLeer, int wartezeit)
        {
            decimal amount = 0;
            double enthalteneKMBesetzt = GetEnthalteneKMBesetzt(kmBesetzt);
            
            if (kmBesetzt > enthalteneKMBesetzt )
                amount = CalculateKMBesetzt(kmBesetzt - enthalteneKMBesetzt); //KMBesetzt

            amount += CalculateKMLeer(kmLeer);       //KMLeer
            amount += CalculateWartezeit(wartezeit); //Wartezeit
            amount += CalculateFixKosten(kmBesetzt, kmLeer); //Fixkosten

            //Debug.WriteLine(mName + " A=" + amount + " B=" + Math.Round(amount, 2, MidpointRounding.AwayFromZero));

            //Rabatt
            if (mRabattPercent > 0)
                amount -= (decimal)(((float)amount / 100) * mRabattPercent);
            
            //Mindestbetrag
            if (mMindestBetrag != 0 && amount < mMindestBetrag)
                amount = mMindestBetrag;

            return Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        }






        internal void LoadFromReader(SqlDataReader reader)
        {
            mId                     = reader.GetSqlInt32(reader.GetOrdinal("TSID")).Value;
            mName                   = reader.GetSqlString(reader.GetOrdinal("TSName")).Value;
            mFlags                  = reader.GetSqlInt32(reader.GetOrdinal("Flags")).Value;

            mKMKorrekturFaktor      = reader.GetSqlSingle(reader.GetOrdinal("KMKorrekturFaktor")).Value;
            mDTAVertragsschlüssel   = reader.GetSqlString(reader.GetOrdinal("DTAVertragsschlüssel")).Value;

            mSteuersatz1            = reader.GetSqlByte(reader.GetOrdinal("SteuerSatz1")).Value;
            mSteuersatz1            = reader.GetSqlByte(reader.GetOrdinal("SteuerSatz2")).Value;
            mSteuerStrecke          = reader.GetSqlSingle(reader.GetOrdinal("SteuerSatz2Strecke")).Value;

            mRabattPercent          = reader.GetSqlSingle(reader.GetOrdinal("Rabatt")).Value;
            mRabattDTAPNr           = reader.GetSqlString(reader.GetOrdinal("RabattDTAPNr")).Value;

            mFixKosten1StreckeVon   = reader.GetSqlSingle(reader.GetOrdinal("FixKosten1StreckeVon")).Value;
            mFixKosten1StreckeBis   = reader.GetSqlSingle(reader.GetOrdinal("FixKosten1StreckeBis")).Value;
            mFixKosten1             = reader.GetSqlMoney(reader.GetOrdinal("FixKosten1")).Value;
            mFixKosten1StreckeFrei  = reader.GetSqlSingle(reader.GetOrdinal("FixKosten1StreckeFrei")).Value;
            mFixKosten1DTAPNr       = reader.GetSqlString(reader.GetOrdinal("FixKosten1DTAPNr")).Value;

            mFixKosten2StreckeVon   = reader.GetSqlSingle(reader.GetOrdinal("FixKosten2StreckeVon")).Value;
            mFixKosten2StreckeBis   = reader.GetSqlSingle(reader.GetOrdinal("FixKosten2StreckeBis")).Value;
            mFixKosten2             = reader.GetSqlMoney(reader.GetOrdinal("FixKosten2")).Value;
            mFixKosten2StreckeFrei  = reader.GetSqlSingle(reader.GetOrdinal("FixKosten2StreckeFrei")).Value;
            mFixKosten2DTAPNr       = reader.GetSqlString(reader.GetOrdinal("FixKosten2DTAPNr")).Value;

            mMindestBetrag          = reader.GetSqlMoney(reader.GetOrdinal("MindestBetrag")).Value;
            mMindestBetragDTAPNr    = reader.GetSqlString(reader.GetOrdinal("MindestBetragDTAPNr")).Value;

            mBesetzt1Strecke        = reader.GetSqlSingle(reader.GetOrdinal("Besetzt1Strecke")).Value;
            mBesetzt1Kosten         = reader.GetSqlMoney(reader.GetOrdinal("Besetzt1Kosten")).Value;
            mBesetzt1DTAPNr         = reader.GetSqlString(reader.GetOrdinal("Besetzt1DTAPNr")).Value;
            mBesetzt2Strecke        = reader.GetSqlSingle(reader.GetOrdinal("Besetzt2Strecke")).Value;
            mBesetzt2Kosten         = reader.GetSqlMoney(reader.GetOrdinal("Besetzt2Kosten")).Value;
            mBesetzt2DTAPNr         = reader.GetSqlString(reader.GetOrdinal("Besetzt2DTAPNr")).Value;
            mBesetzt3Strecke        = reader.GetSqlSingle(reader.GetOrdinal("Besetzt3Strecke")).Value;
            mBesetzt3Kosten         = reader.GetSqlMoney(reader.GetOrdinal("Besetzt3Kosten")).Value;
            mBesetzt3DTAPNr         = reader.GetSqlString(reader.GetOrdinal("Besetzt3DTAPNr")).Value;
            mBesetzt4Strecke        = reader.GetSqlSingle(reader.GetOrdinal("Besetzt4Strecke")).Value;
            mBesetzt4Kosten         = reader.GetSqlMoney(reader.GetOrdinal("Besetzt4Kosten")).Value;
            mBesetzt4DTAPNr         = reader.GetSqlString(reader.GetOrdinal("Besetzt4DTAPNr")).Value;
            mBesetzt5Kosten         = reader.GetSqlMoney(reader.GetOrdinal("Besetzt5Kosten")).Value;
            mBesetzt5DTAPNr         = reader.GetSqlString(reader.GetOrdinal("Besetzt5DTAPNr")).Value;

            mLeer1Strecke = reader.GetSqlSingle(reader.GetOrdinal("Leer1Strecke")).Value;
            mLeer1Kosten = reader.GetSqlMoney(reader.GetOrdinal("Leer1Kosten")).Value;
            mLeer1DTAPNr = reader.GetSqlString(reader.GetOrdinal("Leer1DTAPNr")).Value;
            mLeer2Strecke = reader.GetSqlSingle(reader.GetOrdinal("Leer2Strecke")).Value;
            mLeer2Kosten = reader.GetSqlMoney(reader.GetOrdinal("Leer2Kosten")).Value;
            mLeer2DTAPNr = reader.GetSqlString(reader.GetOrdinal("Leer2DTAPNr")).Value;
            mLeer3Strecke = reader.GetSqlSingle(reader.GetOrdinal("Leer3Strecke")).Value;
            mLeer3Kosten = reader.GetSqlMoney(reader.GetOrdinal("Leer3Kosten")).Value;
            mLeer3DTAPNr = reader.GetSqlString(reader.GetOrdinal("Leer3DTAPNr")).Value;
            mLeer4Strecke = reader.GetSqlSingle(reader.GetOrdinal("Leer4Strecke")).Value;
            mLeer4Kosten = reader.GetSqlMoney(reader.GetOrdinal("Leer4Kosten")).Value;
            mLeer4DTAPNr = reader.GetSqlString(reader.GetOrdinal("Leer4DTAPNr")).Value;
            mLeer5Kosten = reader.GetSqlMoney(reader.GetOrdinal("Leer5Kosten")).Value;
            mLeer5DTAPNr = reader.GetSqlString(reader.GetOrdinal("Leer5DTAPNr")).Value;       

            mWartezeitProMin = reader.GetSqlInt32(reader.GetOrdinal("WartezeitProMin")).Value;
            mWartezeitFreiMin = reader.GetSqlInt32(reader.GetOrdinal("WartezeitFreiMin")).Value;
            mWartezeitKosten = reader.GetSqlMoney(reader.GetOrdinal("WartezeitKosten")).Value;
            mWartezeitDTAPNr = reader.GetSqlString(reader.GetOrdinal("WartezeitDTAPNr")).Value;

            mMehrpersFaktor = reader.GetSqlSingle(reader.GetOrdinal("MehrpersFaktor")).Value;
            mMehrpersZuschlag2Per = reader.GetSqlByte(reader.GetOrdinal("MehrpersZuschlag2Per")).Value;
            mMehrpersZuschlag3Per = reader.GetSqlByte(reader.GetOrdinal("MehrpersZuschlag3Per")).Value;
            mMehrpersDTAPNr = reader.GetSqlString(reader.GetOrdinal("MehrpersDTAPNr")).Value;
        }


        public override string ToString()
        {
            return mId + " " + mName;
        }

        #endregion


    }
}
