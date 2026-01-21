using SuE.Tools;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace SuE.TaMi
{
    public class Fahrzeuge : IEnumerable<Fahrzeug>
    {
        private Dictionary<int, Fahrzeug> mFahrzeuge;
        
        public Fahrzeuge()
        {
            mFahrzeuge = new Dictionary<int, Fahrzeug>();
        }

        public Fahrzeug this[int id]
        {
            get
            {
                Fahrzeug fhz;
                mFahrzeuge.TryGetValue(id, out fhz);
                return fhz;
            }
            //set { mylist.Insert(index, value); }
        }

        public int Count
        {
            get { return mFahrzeuge.Count();  }
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

        public List<Fahrzeug> ToList()
        {
            List<Fahrzeug> result;
            lock (mFahrzeuge)
            {
                result = mFahrzeuge.Values.ToList();
            }
            return result;
        }

        #region "IEnumerable<Fahrzeug>"

        public IEnumerator<Fahrzeug> GetEnumerator()
        {
            return mFahrzeuge.Values.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        #endregion


        /// <summary>
        /// Synchronisiert die Fahrzeugdaten mit dem Datenpaket aus einer TAC.VEHICLEMANMSG
        /// </summary>
        internal List<Fahrzeug> SyncFromMessagePacket(MessagePacket packet, VehicleManAction vehicleManAction, out SyncFlags SyncFlags)
        {
            lock (mFahrzeuge)
            {
                //Debug.WriteLine("Fahrzeuge SyncFromMessagePacket " + vehicleManAction);

                short bSyncVersion = 0;
                SyncFlags nSyncFlags = 0;
                int nSyncCount = 0;
                string szChangesUpto = "";

                //Die anderen VehicleMan Datentelegramme enthalten (noch) keine Sync Informationen
                if (vehicleManAction == VehicleManAction.STATICDATALIST)
                {
                    bSyncVersion = packet.getByteS();
                    nSyncFlags = (SyncFlags)packet.getShortI();
                    nSyncCount = packet.getIntI();
                    szChangesUpto = packet.getString(14);
                }

                SyncFlags = nSyncFlags;

                string szTemp = packet.getString(packet.RemainingBytesToRead);

                //System.Windows.Forms.MessageBox.Show("action=" + action + "\nszTemp\n" + szTemp);

                List<Fahrzeug> listFahrzeugeChanged = new List<Fahrzeug>();
                int fahrzeugId;
                Fahrzeug fahrzeug;
                string[] szItem;
                string[] szItems = szTemp.Split(TaMiClient.SEP_CHAR_ROW);

                /*
                if (nSyncCount != szItems.Length - 1)
                {
                    System.Windows.Forms.MessageBox.Show("action=" + action + "\nSyncCount=" + nSyncCount + "\nszItems.Length=" + szItems.Length + "\nszTemp\n" + szTemp);
                }
                */

                //Beim FullSync erstmal alle Fahrzeuge entfernen, es könnten welche nicht mehr vorhanden sein. 
                if (vehicleManAction == VehicleManAction.STATICDATALIST && nSyncFlags == SyncFlags.FULLSYNC)
                    mFahrzeuge.Clear();

                for (int i = 0; i < szItems.Length; i++)
                {
                    szItem = szItems[i].Split(TaMiClient.SEP_CHAR_COL);

                    //INFO: STATICDATALIST und DYNDATALIST haben eine leere Zeile am Ende anhängen, DYNDATASYNC dagegen nicht. 
                    //      Im Falle einer leeren Zeile wird jetzt abgebrochen.
                    if (szItem.Length < 2) { break; }

                    //Debug.WriteLine("Fahrzeuge " + vehicleManAction + " Data=" + szItems[i]);

                    fahrzeugId = Convert.ToInt32(szItem[0]);
                    if (fahrzeugId > 0)
                    {
                        mFahrzeuge.TryGetValue(fahrzeugId, out fahrzeug);

                        switch (vehicleManAction)
                        {
                            case VehicleManAction.STATICDATALIST:
                            {
                                //TODO: Fahrzeuge die nicht mehr vorhanden sind sollten entfernt werden, da diese nicht mehr lizensiert bzw. vom Anwender gesperrt sind!
                                if (fahrzeug == null)
                                {
                                    fahrzeug = new Fahrzeug(this);
                                    fahrzeug.Fahrzeuge = this;

                                    mFahrzeuge.Add(fahrzeugId, fahrzeug);
                                } //if (fahrzeug == null)

                                fahrzeug.DeserializeStatic(szItem);

                                listFahrzeugeChanged.Add(fahrzeug);
                                break;
                            }

                            case VehicleManAction.DYNDATALIST:
                            case VehicleManAction.DYNDATASYNC:
                            {
                                if (fahrzeug != null)
                                {
                                    fahrzeug.DeserializeDynamic(szItem);
                                    fahrzeug.Fahrzeuge = this;
                                    listFahrzeugeChanged.Add(fahrzeug);
                                }
                                break;
                            }

                        } //switch (TaMiVehicleManAction)

                        //Debug.WriteLine("Fahrzeuge::SyncFromMessagePacket Fhz:" + fahrzeugId + " Name=" + fahrzeug.Name);

                    } //if fahrzeugId > 0

                    // if (fahrzeug != null) { System.Windows.Forms.MessageBox.Show("i=" + i + "FhzId=" + fahrzeugId + "\nFhz=" + fahrzeug, vehicleManAction.ToString()); }

                } //for


                //TODO: Fahrzeuge entfernen die nicht mehr in der STATIC DATA LIST sind!

                if (vehicleManAction == VehicleManAction.STATICDATALIST)
                {
                    if (listFahrzeugeChanged.Count != nSyncCount)
                        throw new Exception($"Fahrzeuge.Sync invalid counts. Action: {vehicleManAction} Count: {listFahrzeugeChanged.Count} SyncCount: {nSyncCount} SyncFlags: {((int)nSyncFlags)}\n Data: {packet}");
                }

                return listFahrzeugeChanged;

            } //lock (mFahrzeuge)


        }
        
    }

}
