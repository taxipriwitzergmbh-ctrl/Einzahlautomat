using SuE.Tools;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace SuE.TaMi
{
    public class FahrzeugGruppen : IEnumerable<FahrzeugGruppe>
    {
        private Dictionary<int, FahrzeugGruppe> mGruppen;

        public FahrzeugGruppen()
        {
            mGruppen = new Dictionary<int, FahrzeugGruppe>();
        }

        public FahrzeugGruppe this[int id]
        {
            get
            {
                FahrzeugGruppe grp;
                mGruppen.TryGetValue(id, out grp);
                return grp;
            }
            //set { mylist.Insert(index, value); }
        }

        public int Count
        {
            get { return mGruppen.Count(); }
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

        #region "IEnumerable<Fahrzeug>"

        public IEnumerator<FahrzeugGruppe> GetEnumerator()
        {
            return mGruppen.Values.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        #endregion


        /// <summary>
        /// Synchronisiert die Fahrzeuggruppen mit dem Datenpaket aus einer TAC.VEHICLEMANMSG
        /// </summary>
        internal List<FahrzeugGruppe> SyncFromMessagePacket(MessagePacket packet, VehicleManAction vehicleManAction)
        {
            short bSyncVersion = 0;
            int nSyncFlags = 0;
            int nSyncCount = 0;
            string szChangesUpto = "";

            bSyncVersion = packet.getByteS();
            nSyncFlags = packet.getShortI();
            nSyncCount = packet.getIntI();
            szChangesUpto = packet.getString(14);

            string szTemp = packet.getString(packet.RemainingBytesToRead);

            //System.Windows.Forms.MessageBox.Show("action=" + action + "\nszTemp\n" + szTemp);

            List<FahrzeugGruppe> listChanged = new List<FahrzeugGruppe>();
            int grpId;
            FahrzeugGruppe grp;
            string[] szItem;
            string[] szItems = szTemp.Split(TaMiClient.SEP_CHAR_ROW);

            //if (nSyncCount != szItems.Length - 1)
            //    throw new Exception($"FahrzeugGruppen Sync invalid counts. Count: {szItems.Length} nSyncCount: {nSyncCount} SyncFlags: {((int)SyncFlags)}");

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
                //      Im Falle einer leeren Zeile wird jetzt abgebrochen.
                if (szItem.Length < 2) { break; }

                grpId = Convert.ToInt32(szItem[0]);
                if (grpId > 0)
                {
                    mGruppen.TryGetValue(grpId, out grp);

                    switch (vehicleManAction)
                    {
                        case VehicleManAction.GETGROUPS:
                            {
                                //TODO: Fahrzeuge die nicht mehr vorhanden sind sollten entfernt werden, da diese nicht mehr Lizensiert bzw Gesperrt sind!
                                if (grp == null) { grp = new FahrzeugGruppe(); grp.FahrzeugGruppen = this; mGruppen.Add(grpId, grp); }

                                grp.deserializeStatic(szItem);

                                //System.Windows.Forms.MessageBox.Show("id=" + grp.Id + ": " + grp.Name);

                                listChanged.Add(grp);

                                break;
                            }

                    } //switch (TaMiVehicleManAction)

                    //Debug.WriteLine("Fahrzeuge::SyncFromMessagePacket Fhz:" + fhzId + " Name=" + fhz.Name);

                }

                // if (fhz != null) { System.Windows.Forms.MessageBox.Show("i=" + i + "FhzId=" + fhzId + "\nFhz=" + fhz, vehicleManAction.ToString()); }

            } //for

            if (listChanged.Count != nSyncCount)
                throw new Exception($"FahrzeugGruppen.Sync invalid counts. Count: {listChanged.Count} nSyncCount: {nSyncCount} SyncFlags: {((int)nSyncFlags)}");

            return listChanged;
        }

    }

}
