using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SuE.TaMi
{
    public class FahrzeugGruppe
    {
        private int mId = 0;
        private string mName;


        #region "Propertys"

        public int Id
        {
            get { return mId; }
        }

        public string Name
        {
            get { return mName; }
        }


        FahrzeugGruppen fahrzeugGruppen;
        public FahrzeugGruppen FahrzeugGruppen
        {
            get
            {
                return fahrzeugGruppen;
            }
            internal set
            {
                fahrzeugGruppen = value;
            }
        }

        #endregion


        internal bool deserializeStatic(string[] data)
        {
            if (data == null) return false;

            int t = 0;

            mId = Convert.ToInt32(data[t++]);
            mName = data[t++];

            return true;
        }

    }
}
