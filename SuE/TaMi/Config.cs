using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace SuE.TaMi
{
    public class Config
    {
        /*
            PROJEKT           :  Config
            VERSION           :  1.01
            AUTOR             :  Stefan Schermer [mcs] / Peter Enke [mce]

                               ===========================
                               ==     SuE-Software      ==
                               ===========================

            ERSTELLUNGS-DATUM :  28.07.14
            ÄNDERUNGS-DATUM   :  20.07.15
            ÄNDERUNG:            siehe Entwicklungsgeschichte
            DURCHGEFUEHRT VON :  SuE-Software  [sue]

            FUNKTION          :  Lädt und speichert Konfigurationswerte in einer Datenbanktabelle

            BEMERKUNG         :  ---

            ======================== ENTWICKLUNGS-GESCHICHTE ========================

            --- Version 1.00 --------------------------------------------------------

            28.07.14  mcs   Beginn der Implementation
         
            20.07.15  mcs   IOException, UnauthorizedAccessException in SaveXML wird abgefangen 
                            

        */

        /// <summary>
        /// Wird ausgelöst wenn ein AppConfigItem geändert wurde.
        /// </summary>
        //public event EventHandler<AppConfigItemChangeEventArgs> OnAppConfigItemChange;
        
        private const string KEY_DELIM = "\\";
        private const string XML_ROOT = "Settings";
        private const string DATEFORMAT = "yyyyMMddHHmmss";

        private Dictionary<string, ConfigItem> mValues;
        private string mApplication;


        public Config()
        {
            mValues = new Dictionary<string, ConfigItem>(20);
        }


        /// <summary>
        /// Lädt die Einstellungen aus der Datenbank
        /// </summary>
        internal void LoadDb(SqlConnection conn)
        {
            mValues.Clear();
            if (conn == null) return;

            SqlCommand cmdQuery;
            SqlDataReader reader;
            ConfigItem configItem;

            //Query Data
            cmdQuery = new SqlCommand();
            cmdQuery.Connection = conn;
            cmdQuery.CommandText = "SELECT TConfig.*, TConfigValues.Value1 FROM TConfig LEFT OUTER JOIN TConfigValues ON TConfig.CfgID = TConfigValues.CfgID";

            reader = cmdQuery.ExecuteReader();

            while (reader.Read())
            {
                configItem = new ConfigItem(reader);
                //Debug.WriteLine($"Config Load: {configItem}");
                mValues.Add(configItem.Key.ToLowerInvariant(), configItem);
            }

            reader.Close();
        }


        /// <summary>
        /// Die zugehörige TaMiClient Instanz
        /// </summary>
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


        public string Application
        {
            get
            {
                return mApplication;
            }
            set
            {
                mApplication = value;
            }
        }



        /// <summary>
        /// Gibt den Wert als string eines Feldes einer Gruppe zurück
        /// </summary>
        public string Get(string group, string field, string defaultvalue = "", string application = "")
        {
         ConfigItem cfgItem;
         string app;
         string key;

         if (application.Length > 0) { app = application; } else { app = mApplication; }
         key = (app + KEY_DELIM + group + KEY_DELIM + field).ToLowerInvariant();

         if (mValues.TryGetValue(key, out cfgItem))
         {
            return cfgItem.Value1;
         }
         else
         {
            return defaultvalue;
         }
        }

        /// <summary>
        /// Erstellt oder ändert den Wert als string eines Feldes einer Gruppe
        /// </summary>
        public void Set(string group, string field, string value, string defaultvalue = "", string application = "")
        {
         ConfigItem cfgItem;
         string app;
         string key;

         if (application.Length > 0) { app = application; } else { app = mApplication; }
         key = (app + KEY_DELIM + group + KEY_DELIM + field).ToLowerInvariant();

         if (mValues.TryGetValue(key, out cfgItem))
         {
            cfgItem.Value1 = value;
         }
         else
         {
            cfgItem = new ConfigItem(app, group, field, defaultvalue, value);
            mValues.Add(key, cfgItem);
         }

         //Event auslösen
         if (cfgItem != null)
         {
          if (cfgItem.ChangedValue)
          {
           //if (this.OnAppConfigItemChange != null)
           //    this.OnAppConfigItemChange(this, new AppConfigItemChangeEventArgs());
           
           //cfgItem.ResetChangedValue();
          }
         }

        }




        /// <summary>
        /// Gibt den Wert als double eines Feldes einer Gruppe zurück
        /// </summary>
        public double Get(string group, string field, double defaultvalue = 0, string application = "")
        {
            try
            {
                return Convert.ToDouble(this.Get(group, field, defaultvalue.ToString(), application));
            }
            catch (Exception)
            {
                return defaultvalue;
            }
        }

        /// <summary>
        /// Erstellt oder ändert den Wert als double eines Feldes einer Gruppe
        /// </summary>
        public void Set(string group, string field, double value, double defaultvalue = 0, string application = "")
        {
            this.Set(group, field, value.ToString(), defaultvalue.ToString(), application);
        }




        /// <summary>
        /// Gibt den Wert als float eines Feldes einer Gruppe zurück
        /// </summary>
        public float Get(string group, string field, float defaultvalue = 0, string application = "")
        {
            try
            {
                return Convert.ToSingle(this.Get(group, field, defaultvalue.ToString(), application));
            }
            catch (Exception)
            {
                return defaultvalue;
            }
        }

        /// <summary>
        /// Erstellt oder ändert den Wert als float eines Feldes einer Gruppe
        /// </summary>
        public void Set(string group, string field, float value, float defaultvalue = 0, string application = "")
        {
            this.Set(group, field, value.ToString(), defaultvalue.ToString(), application);
        }





        /// <summary>
        /// Gibt den Wert als int eines Feldes einer Gruppe zurück
        /// </summary>
        public int Get(string group, string field, int defaultvalue = 0, string application = "")
        {
            try
            {
                return Convert.ToInt32(this.Get(group, field, defaultvalue.ToString(), application));
            }
            catch (Exception)
            {
                return defaultvalue;
            }
        }

        /// <summary>
        /// Erstellt oder ändert den Wert als int eines Feldes einer Gruppe
        /// </summary>
        public void Set(string group, string field, int value, int defaultvalue = 0, string application = "")
        {
                this.Set(group, field, value.ToString(), defaultvalue.ToString(), application);
        }



        /// <summary>
        /// Gibt den Wert als short eines Feldes einer Gruppe zurück
        /// </summary>
        public short Get(string group, string field, short defaultvalue = 0, string application = "")
        {
            try
            {
                return Convert.ToInt16(this.Get(group, field, defaultvalue.ToString(), application));
            }
            catch (Exception)
            {
                return defaultvalue;
            }
        }

        /// <summary>
        /// Erstellt oder ändert den Wert als short eines Feldes einer Gruppe
        /// </summary>
        public void Set(string group, string field, short value, short defaultvalue = 0, string application = "")
        {
            this.Set(group, field, value.ToString(), defaultvalue.ToString(), application);
        }



        /// <summary>
        /// Gibt den Wert als byte eines Feldes einer Gruppe zurück
        /// </summary>
        public byte Get(string group, string field, byte defaultvalue = 0, string application = "")
        {
            try
            {
                return Convert.ToByte(this.Get(group, field, defaultvalue.ToString(), application));
            }
            catch (Exception)
            {
                return defaultvalue;
            }
        }

        /// <summary>
        /// Erstellt oder ändert den Wert als byte eines Feldes einer Gruppe
        /// </summary>
        public void Set(string group, string field, byte value, byte defaultvalue = 0, string application = "")
        {
            this.Set(group, field, value.ToString(), defaultvalue.ToString(), application);
        }



        /// <summary>
        /// Gibt den Wert als bool eines Feldes einer Gruppe zurück
        /// </summary>
        public bool Get(string group, string field, bool defaultvalue = false, string application = "")
        {
            try
            {
                return (Convert.ToInt32(this.Get(group, field, (defaultvalue ? "1" : "0"), application)) == 1);
            }
            catch (Exception)
            {
                return defaultvalue;
            }
        }

        /// <summary>
        /// Erstellt oder ändert den Wert als bool eines Feldes einer Gruppe
        /// </summary>
        public void Set(string group, string field, bool value, bool defaultvalue = false, string application = "")
        {
            this.Set(group, field, (value ? "1" : "0"), (defaultvalue ? "1" : "0"), application);
        }



        /// <summary>
        /// Gibt den Wert als DateTime eines Feldes einer Gruppe zurück
        /// </summary>
        public DateTime Get(string group, string field, DateTime defaultvalue = default(DateTime), string application = "")
        {
            ConfigItem cfgItem;
            string app;
            string key;

            if (application.Length > 0) { app = application; } else { app = mApplication; }
            key = (app + KEY_DELIM + group + KEY_DELIM + field).ToLowerInvariant();

            if (mValues.TryGetValue(key, out cfgItem))
            {
                string datestr = cfgItem.Value1;
                int l = datestr.Length;

                if (l == 0) { return DateTime.MinValue; }

                int year = 0, month = 0, day = 0, hour = 0, mins = 0, secs = 0;

                //Datum konvertieren
                if ((l == 8) || (l >= 14))
                {
                        year = Convert.ToInt32(datestr.Substring(0, 4));
                        month = Convert.ToInt32(datestr.Substring(4, 2));
                        day = Convert.ToInt32(datestr.Substring(6, 2));
                }
                //Zeit konvertieren
                if ((l == 6) || (l >= 14))
                {
                    hour = Convert.ToInt32(datestr.Substring(8, 2));
                    mins = Convert.ToInt32(datestr.Substring(10, 2));
                    secs = Convert.ToInt32(datestr.Substring(12, 2));
                }

                try
                {
                    DateTime result = new DateTime(year, month, day, hour, mins, secs);
                    return result;
                }
                catch (Exception)
                {
                    return defaultvalue;
                }
            }
            else
            {
                return defaultvalue;
            }
        }

        /// <summary>
        /// Erstellt oder ändert den Wert als DateTime eines Feldes einer Gruppe
        /// </summary>
        public void Set(string group, string field, DateTime value, DateTime defaultvalue = default(DateTime), string application = "")
        {
            this.Set(group, field, value.ToString(DATEFORMAT), defaultvalue.ToString(DATEFORMAT), application);
        }



        /// <summary>
        /// Setzt die Spaltendefinitionen eines DataGridView durch die gespeicherten Werte
        /// </summary>
        public bool GetDgvColumns(string group, string field, System.Windows.Forms.DataGridView datagrid, string defaultvalue = "", string application = "")
        {
            string value = this.Get(group, field, defaultvalue, application);

            string[] cols = value.Split(';');
            string[] col;

            int col_index;
            string col_name;
            string col_headertext;
            int col_alignment;
            int col_width;
            System.Windows.Forms.DataGridViewColumn dgc;

            for (int i = 0; i < cols.Length; i++)
            {
                //Debug.WriteLine("COL: " + cols[i]);

                col = null;
                try { if (cols[i].Length >= 4) { col = cols[i].Substring(1, cols[i].Length - 2).Split(','); } }
                catch { }

                //Debug.WriteLine("COL: " + col);

                if (col != null)
                {
                    try
                    {
                        col_index = Convert.ToInt32(col[0]);
                        col_name = col[1];
                        col_headertext = col[2];
                        col_alignment = Convert.ToInt32(col[3]);
                        col_width = Convert.ToInt32(col[4]);

                        try { dgc = datagrid.Columns[col_name]; }
                        catch { dgc = null; }
                        if ((dgc != null) && (dgc.Name.Equals(col_name)))
                        {
                            dgc.HeaderText = col_headertext;
                            dgc.DefaultCellStyle.Alignment = (System.Windows.Forms.DataGridViewContentAlignment)col_alignment;
                            dgc.Width = col_width;
                        }

                    }
                    catch { }
                }


            }

            /*
            try
            {
                return (Convert.ToInt32(this.Get(group, field, (defaultvalue ? "1" : "0"), application)) == 1);
            }
            catch (Exception)
            {
                return defaultvalue;
            }
             */
            return false;
        }


        /// <summary>
        /// Erstellt oder ändert den Wert als Definitionen von Column Headern eines Feldes einer Gruppe
        /// </summary>
        public void SetDgvColumns(string group, string field, System.Windows.Forms.DataGridView datagrid, string defaultvalue = "", string application = "")
        {
            StringBuilder sbColumns = new StringBuilder();
            StringBuilder sbColumn = new StringBuilder();

            foreach (System.Windows.Forms.DataGridViewColumn col in datagrid.Columns)
            {
                sbColumn.Append("[");

                sbColumn.Append(col.Index); sbColumn.Append(",");
                
                sbColumn.Append(col.Name); sbColumn.Append(",");
                sbColumn.Append(col.HeaderText); sbColumn.Append(",");
                sbColumn.Append((int)col.DefaultCellStyle.Alignment); sbColumn.Append(",");
                sbColumn.Append(col.Width);
                
                sbColumn.Append("]");

                sbColumns.Append(sbColumn.ToString());
                sbColumns.Append(";");
                sbColumn.Clear();

            }

            this.Set(group, field, sbColumns.ToString(), defaultvalue, application);
        }



    }

}
