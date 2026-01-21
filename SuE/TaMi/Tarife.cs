using SuE.Tools;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace SuE.TaMi
{
    public class Tarife : IEnumerable<Tarif>
    {
        private Dictionary<int, Tarif> mTarife;

        public Tarife()
        {
            mTarife = new Dictionary<int, Tarif>();
        }

        public Tarif this[int id]
        {
            get
            {
                Tarif tarif;
                mTarife.TryGetValue(id, out tarif);
                return tarif;
            }
            //set { mylist.Insert(index, value); }
        }

        public int Count
        {
            get { return mTarife.Count(); }
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

        #region "IEnumerable<Tarif>"

        public IEnumerator<Tarif> GetEnumerator()
        {
            return mTarife.Values.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        #endregion


        /// <summary>
        /// Lädt die Tarife aus der Datenbank
        /// </summary>
        internal void loadDb(SqlConnection conn)
        {
            mTarife.Clear();
            if (conn == null) return;

            SqlCommand cmdQuery = null;
            SqlDataReader reader = null;
            Tarif tarif;

            //Query Data
            cmdQuery = new SqlCommand();
            cmdQuery.Connection = conn;
            cmdQuery.CommandText = "SELECT * FROM TTarifStrecke ORDER BY TSID";

            reader = cmdQuery.ExecuteReader();

            while (reader.Read())
            {
                tarif = new Tarif();
                tarif.LoadFromReader(reader);

                mTarife.Add(tarif.Id, tarif);
            }

            reader.Close();
        }

    }

}
