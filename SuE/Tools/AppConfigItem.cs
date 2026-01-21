using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SuE.Tools
{
    public class AppConfigItem
    {
        /*
            PROJEKT           :  AppConfigItem
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

        public AppConfigItem(string app, string group, string field, string defaultvalue, string value)
        {
            msApplication = app;
            msGroup = group;
            msField = field;
            msDefaultValue = defaultvalue;
            msValue1 = value;
            // initialize optional fields to explicit defaults to avoid warnings
            mlCfgID = 0;
            msDescription = string.Empty;
            mbChanged = true;
            mbChangedValue = true;
        }

        // optional extended ctor allowing direct assignment
        public AppConfigItem(string app, string group, string field, string defaultvalue, string value, int cfgId, string description)
            : this(app, group, field, defaultvalue, value)
        {
            mlCfgID = cfgId;
            msDescription = description ?? string.Empty;
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



        public override string ToString()
        {
            return msApplication + KEY_DELIM + msGroup + KEY_DELIM + msField + ": '" + msValue1 + "' Default: '" + msDefaultValue + "' " + msDescription;
        }


    }
}
