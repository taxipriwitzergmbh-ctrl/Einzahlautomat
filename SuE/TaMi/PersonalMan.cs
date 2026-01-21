using SuE.Tools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SuE.TaMi
{
    public class PersonalMan
    {
        private Dictionary<int, Personal> mPersonal;

        public PersonalMan()
        {
            mPersonal = new Dictionary<int, Personal>();
        }

        public Personal get(int id)
        {
            Personal personal;
            mPersonal.TryGetValue(id, out personal);
            return personal;
        }

        public string getName(int id)
        {
            Personal personal;
            if (mPersonal.TryGetValue(id, out personal))
            {
                return (personal.Vorname + " " + personal.Name).Trim();
            }

            return "#" + id.ToString();
        }


        TaMiClient tamiClient;
        public TaMiClient TaMiClient
        {
            get
            {
                return tamiClient;
            }
            internal set
            {
                tamiClient = value;
            }
        }

        /// <summary>
        /// Synchronisiert die Fahrzeugdaten mit dem Datenpaket aus einer TAC.VEHICLEMANMSG
        /// </summary>
        internal List<Personal> SyncFromMessagePacket(MessagePacket packet, PersonalManAction personalManAction)
        {
            short   bSyncVersion = 0;
            int     nSyncFlags = 0;
            int     nSyncCount = 0;
            string  szChangesUpto = "";

            //Die anderen VehicleMan Datentelegramme enthalten (noch) keine Sync Informationen
            if (personalManAction == PersonalManAction.STATICDATALIST)
            {
                bSyncVersion = packet.getByteS();
                nSyncFlags = packet.getShortI();
                nSyncCount = packet.getIntI();
                szChangesUpto = packet.getString(14);
            }

            string szTemp = packet.getString(packet.RemainingBytesToRead);

            //System.Windows.Forms.MessageBox.Show("action=" + action + "\nszTemp\n" + szTemp);

            List<Personal> listPersonalChanged = new List<Personal>();
            int personalId;
            Personal personal;
            string[] szItem;
            string[] szItems = szTemp.Split(TaMiClient.SEP_CHAR_ROW);

            /*
            if (nSyncCount != szItems.Length - 1)
            {
                System.Windows.Forms.MessageBox.Show("action=" + action + "\nSyncCount=" + nSyncCount + "\nszItems.Length=" + szItems.Length + "\nszTemp\n" + szTemp);
            }
            */


            for (int i = 0; i < szItems.Length; i++)
            {
                szItem = szItems[i].Split(TaMiClient.SEP_CHAR_COL);

                //INFO: STATICDATALIST und DYNDATALIST haben eine leere Zeile am Ende anhängen, DYNDATASYNC dagegen nicht. 
                //      Im falle einer leeren Zeile wird jetzt abgebrochen.
                if (szItem.Length < 2) { break; }

                personalId = Convert.ToInt32(szItem[0]);
                if (personalId > 0)
                {
                    mPersonal.TryGetValue(personalId, out personal);

                    switch (personalManAction)
                    {
                        case PersonalManAction.STATICDATALIST:
                        {
                            if (personal == null) 
                            { 
                                personal = new Personal(); 
                                mPersonal.Add(personalId, personal); 
                            }

                            personal.deserializeStatic(szItem);
                            
                            listPersonalChanged.Add(personal);
                            break;
                        }

                    } //switch (TaMiVehicleManAction)
                }

                // if (fhz != null) { System.Windows.Forms.MessageBox.Show("i=" + i + "FhzId=" + fhzId + "\nFhz=" + fhz, vehicleManAction.ToString()); }
                
            } //for


            if (personalManAction == PersonalManAction.STATICDATALIST)
            {
                if (listPersonalChanged.Count != nSyncCount)
                    throw new Exception($"Personal.Sync invalid counts. Count: {listPersonalChanged.Count} nSyncCount: {nSyncCount} SyncFlags: {((int)nSyncFlags)}");
            }

            return listPersonalChanged;
        }

    }
}
