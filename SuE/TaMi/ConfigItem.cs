using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;

namespace SuE.TaMi
{
    public class ConfigItem
    {
        /*
            PROJEKT           :  ConfigItem
            VERSION           :  1.00
            AUTOR             :  Stefan Schermer [mcs] / Peter Enke [mce]

                                   ===========================
                                   ==     SuE-Software      ==
                                   ===========================

            ERSTELLUNGS-DATUM :  28.07.14
            ÄNDERUNGS-DATUM   :  28.07.14
            ÄNDERUNG:            siehe Entwicklungsgeschichte
            DURCHGEFUEHRT VON :  SuE-Software  [sue]

            FUNKTION          :  Stellt den Wert eines Eintrags in der
                                 Konfiguration dar.

            BEMERKUNG         :  ---

            ======================== ENTWICKLUNGS-GESCHICHTE ========================

            --- Version 1.00 --------------------------------------------------------

            01.04.11  mcs   Beginn der Implementation

        */

        private const string KEY_DELIM = "\\";

        private int mlCfgID;
        private string msApplication;
        private string msGroup;
        private string msField;
        private string msDefaultValue;
        private string msDescription;

        private string msValue1;

        private bool mbChanged;
        private bool mbChangedValue;

        public ConfigItem(string app, string group, string field, string defaultvalue, string value, int cfgId = 0, string description = null)
        {
            msApplication = app;
            msGroup = group;
            msField = field;
            msDefaultValue = defaultvalue;
            msValue1 = value;
            mlCfgID = cfgId;
            msDescription = description ?? string.Empty;
            mbChanged = true;
            mbChangedValue = true;
        }

        internal ConfigItem(SqlDataReader reader)
        {
            msApplication = reader.GetSqlString(reader.GetOrdinal("App")).Value;
            msGroup = reader.GetSqlString(reader.GetOrdinal("Grp")).Value;
            msField = reader.GetSqlString(reader.GetOrdinal("Field")).Value;
            msDefaultValue = reader.GetSqlString(reader.GetOrdinal("DefaultValue")).Value;

            // Description (optional column)
            try
            {
                int ordDesc = reader.GetOrdinal("Description");
                if (ordDesc >= 0 && !reader.IsDBNull(ordDesc))
                    msDescription = reader.GetSqlString(ordDesc).Value;
                else
                    msDescription = string.Empty;
            }
            catch
            {
                msDescription = string.Empty;
            }

            // CfgID (optional column)
            try
            {
                int ordId = reader.GetOrdinal("CfgID");
                if (ordId >= 0 && !reader.IsDBNull(ordId))
                    mlCfgID = reader.GetInt32(ordId);
                else
                    mlCfgID = 0;
            }
            catch
            {
                mlCfgID = 0;
            }

            if (!reader.IsDBNull(reader.GetOrdinal("Value1")))
                msValue1 = reader.GetSqlString(reader.GetOrdinal("Value1")).Value;
            else
                msValue1 = reader.GetSqlString(reader.GetOrdinal("DefaultValue")).Value;

            mbChanged = false;
            mbChangedValue = false;
        }

        public string Key
        {
            get { return msApplication + KEY_DELIM + msGroup + KEY_DELIM + msField; }
        }

        public int CfgID
        {
            get { return mlCfgID; }
        }

        public string Application
        {
            get { return msApplication; }
        }

        public string Group
        {
            get { return msGroup; }
        }

        public string Field
        {
            get { return msField; }
        }

        public string DefaultValue
        {
            get { return msDefaultValue; }
        }

        public string Description
        {
            get { return msDescription; }
        }

        public string Value1
        {
            get { return msValue1; }
            set { if (msValue1.Equals(value) == false) { mbChangedValue = true; msValue1 = value; } }
        }

        public bool Changed
        {
            get { return mbChanged; }
        }

        public bool ChangedValue
        {
            get { return mbChangedValue; }
        }



        public override String ToString()
        {
            return msApplication + KEY_DELIM + msGroup + KEY_DELIM + msField + "=" + msValue1;
        }
    }
}
