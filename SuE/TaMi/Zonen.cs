using SuE.Tools;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace SuE.TaMi
{
    public class Zonen : IEnumerable<Zone>
    {
        private Dictionary<int, Zone> mZonen;


        public Zonen()
        {
            mZonen = new Dictionary<int, Zone>();
        }

        public Zone this[int id]
        {
            get
            {
                Zone zone;
                mZonen.TryGetValue(id, out zone);
                return zone;
            }
            //set { mylist.Insert(index, value); }
        }

        public int Count
        {
            get { return mZonen.Count(); }
        }


        public Zone get(int id)
        {
            Zone zone;
            mZonen.TryGetValue(id, out zone);
            return zone;
        }

        public string getName(int id)
        {
            if (id <= 0) { return ""; }

            Zone zone;
            if (mZonen.TryGetValue(id, out zone))
                return zone.Name;

            return "#" + id;
        }

        public string getKürzel(int id)
        {
            if (id <= 0) { return ""; }

            Zone zone;
            if (mZonen.TryGetValue(id, out zone))
                return zone.Kürzel;

            return "#" + id;
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

        #region "IEnumerable<Zone>"

        public IEnumerator<Zone> GetEnumerator()
        {
            return mZonen.Values.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        #endregion

        /// <summary>
        /// Synchronisiert die Zonen mit dem Datenpaket aus einer TAC.ZONEMANMSG
        /// </summary>
        internal List<Zone> SyncFromMessagePacket(MessagePacket packet, ZoneManAction zoneManAction)
        {
            short bSyncVersion = 0;
            int nSyncFlags = 0;
            int nSyncCount = 0;
            string szChangesUpto = "";

            //Die anderen VehicleMan Datentelegramme enthalten (noch) keine Sync Informationen
            if (zoneManAction == ZoneManAction.STATICDATALIST)
            {
                bSyncVersion = packet.getByteS();
                nSyncFlags = packet.getShortI();
                nSyncCount = packet.getIntI();
                szChangesUpto = packet.getString(14);
            }

            string szTemp = packet.getString(packet.RemainingBytesToRead);

            //System.Windows.Forms.MessageBox.Show("action=" + action + "\nszTemp\n" + szTemp);

            List<Zone> listZonesChanged = new List<Zone>();
            int zoneId;
            Zone zone;
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

                zoneId = Convert.ToInt32(szItem[0]);
                if (zoneId > 0)
                {
                    mZonen.TryGetValue(zoneId, out zone);

                    switch (zoneManAction)
                    {
                        case ZoneManAction.STATICDATALIST:
                        {
                            if (zone == null) 
                            { 
                                zone = new Zone(); 
                                mZonen.Add(zoneId, zone);
                            }

                            zone.deserializeStatic(szItem);
                            listZonesChanged.Add(zone);

                            break;
                        }


                    } //switch (TaMiVehicleManAction)
                }

                // if (fhz != null) { System.Windows.Forms.MessageBox.Show("i=" + i + "FhzId=" + fhzId + "\nFhz=" + fhz, vehicleManAction.ToString()); }

            } //for


            if (zoneManAction == ZoneManAction.STATICDATALIST)
            {
                if (listZonesChanged.Count != nSyncCount)
                    throw new Exception($"Zonen.Sync invalid counts. Count: {listZonesChanged.Count} nSyncCount: {nSyncCount} SyncFlags: {((int)nSyncFlags)}");
            }

            return listZonesChanged;
        }



    }

}
