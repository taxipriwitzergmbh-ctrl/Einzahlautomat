using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SuE.TaMi
{
    public static class TaMiTools
    {
        /*
            PROJEKT           :  TaMiTools
            VERSION           :  1.1

            ERSTELLUNGS-DATUM :  03.03.2022
            ÄNDERUNGS-DATUM   :  06.11.2025
            ÄNDERUNG:            siehe Entwicklungsgeschichte
            DURCHGEFUEHRT VON :  SuE-Software  [sue]

            FUNKTION          :  Klassenübergreifende Helper-Funktionen und Extensions

            BEMERKUNG         :  ---

            ======================== ENTWICKLUNGS-GESCHICHTE ========================

            --- Version 1.00 --------------------------------------------------------

            03.03.2022  mcs   Rausgelöste Funktionen die zuvor in der TaMiClient Klasse
                              waren.

            06.11.2025  mcs   Von TaMi.Map.Programm fhzstates hierher verschoben
                              - FhzStates DefaultFhzStates
                              - Methoden zur Initialisierung und Abfrage der DefaultFhzStates

        */

        private static string SERVER_DECIMAL_SEP = ",";  //Vom Server
        private static string NUMBER_DECIMAL_SEP = ",";  //Vom aktuellen Windows-Benutzer
        private static string NUMBER_THOUSAND_SEP = ".";

        private static readonly FhzStates DefaultFhzStates = new FhzStates();

        //Static Constructor
        //INFO: A static constructor is used to initialize any static data, or to perform a particular action that needs to be performed once only. It is called automatically before the first instance is created or any static members are referenced
        static TaMiTools()
        {
            NUMBER_DECIMAL_SEP = Thread.CurrentThread.CurrentCulture.NumberFormat.NumberDecimalSeparator;
            NUMBER_THOUSAND_SEP = Thread.CurrentThread.CurrentCulture.NumberFormat.NumberGroupSeparator;

            TaMiTools.InitDefaultFhzStates();
        }

        public static FhzStates GetDefaultFhzStates()
        {
            return DefaultFhzStates;
        }

        public static FhzState GetDefaultFhzStateById(int stateId)
        {
            return DefaultFhzStates.Get(stateId);
        }

        public static void InitDefaultFhzStates()
        {
            //Initialisiere Stati
            DefaultFhzStates.Add(new FhzState(1, "Anmeldung", "A", Color.Black, Color.FromArgb(255, 224, 224, 224)));
            DefaultFhzStates.Add(new FhzState(3, "Besetzt", "B", Color.Black, Color.FromArgb(255, 255, 0, 0)));
            DefaultFhzStates.Add(new FhzState(4, "Auftrag", "A", Color.Black, Color.FromArgb(255, 255, 255, 64)));
            DefaultFhzStates.Add(new FhzState(5, "Pause", "P", Color.Black, Color.FromArgb(255, 255, 64, 255)));
            DefaultFhzStates.Add(new FhzState(6, "Frei", "F", Color.Black, Color.FromArgb(255, 0, 192, 0)));
            DefaultFhzStates.Add(new FhzState(7, "Anfahrt", "AN", Color.Black, Color.FromArgb(255, 240, 134, 80)));
            DefaultFhzStates.Add(new FhzState(8, "Funk Aus", "A", Color.Black, Color.FromArgb(255, 128, 128, 128)));
            DefaultFhzStates.Add(new FhzState(11, "Reihe", "R", Color.Black, Color.FromArgb(255, 0, 128, 0)));
            DefaultFhzStates.Add(new FhzState(12, "Abwesend", "A", Color.Black, Color.FromArgb(255, 192, 192, 192)));
            DefaultFhzStates.Add(new FhzState(15, "Feierabend", "ZF", Color.Black, Color.FromArgb(255, 0, 192, 192)));
            DefaultFhzStates.Add(new FhzState(17, "Geschäftsfahrt", "GF", Color.WhiteSmoke, Color.FromArgb(255, 102, 102, 255)) /*System.Drawing.Color.FromArgb(255, 192, 192, 192))*/);
            DefaultFhzStates.Add(new FhzState(18, "Privatfahrt", "PF", Color.WhiteSmoke, Color.FromArgb(255, 102, 102, 255)));

            //Deprecated
            DefaultFhzStates.Add(new FhzState(255, "Unerreichbar", "U", Color.Black, Color.FromArgb(255, 192, 192, 192)));

            DefaultFhzStates.Get(1).VisibleOnMap = false;
            DefaultFhzStates.Get(8).VisibleOnMap = false;
        }

        public static string GetErrorText(ResultCodes resultCode)
        {
            switch (resultCode)
            {
                case ResultCodes.TIMEOUT:                       return "Zeitüberschreitung bei der Serveranfrage.";
                case ResultCodes.UNKNOWN:                       return "Unbekannter Fehler.";
                case ResultCodes.SUCCESS:                       return "Erfolgreich.";
                case ResultCodes.CMDUNKNOWN:                    return "Unbekannter Befehl.";
                case ResultCodes.FAILED:                        return "Vorgang fehlgeschlagen.";
                case ResultCodes.ERRPIDPASS:                    return "Personal-ID oder Passwort ungültig.";
                case ResultCodes.PROTOVERSIONOUTDATED:          return "Protokollversion veraltet.";
                case ResultCodes.NETWORKDISABLED:               return "Netzwerk deaktiviert.";
                case ResultCodes.NOTENOUGHRIGHTS:               return "Nicht genügend Rechte.";
                case ResultCodes.NOTENOUGHLICENCES:             return "Nicht genügend Lizenzen.";
                case ResultCodes.DEMOPERIODOVER:                return "Demoperiode abgelaufen.";
                case ResultCodes.PERSONALLOCKED:                return "Personal gesperrt.";
                case ResultCodes.OPERATIONCURRENTLYIMPOSSIBLE:  return "Operation derzeit nicht möglich.";
                case ResultCodes.INVALIDFHZID:                  return "Ungültige Fahrzeug-ID.";
                case ResultCodes.INVALIDPERSID:                 return "Ungültige Personal-ID.";
                case ResultCodes.SVRSHUTDOWN:                   return "Server wird heruntergefahren.";

                default:                                        return "Unbekannter Fehlercode: " + resultCode;
            }
        }

        #region Serialize und Deserialize Converters

        public static DateTime SerializeString2Date(string datestr)
        {
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
                return DateTime.MinValue;
            }
        }

        public static string SerializeDate2String(DateTime date)
        {
            if (date == DateTime.MinValue)
                return string.Empty;

            return date.ToString("yyyyMMddHHmmss");
        }

        public static string SerializeDouble2String(double value)
        {
            return Convert.ToString(value).Replace(NUMBER_DECIMAL_SEP, SERVER_DECIMAL_SEP);
        }

        public static float SerializeString2Float(string floatstr)
        {
            if (floatstr == null || floatstr.Length == 0)
                return 0;

            return Convert.ToSingle(floatstr.Replace(SERVER_DECIMAL_SEP, NUMBER_DECIMAL_SEP));
        }

        public static double SerializeString2Double(string doublestr)
        {
            if (doublestr == null || doublestr.Length == 0)
                return 0;

            return Convert.ToDouble(doublestr.Replace(SERVER_DECIMAL_SEP, NUMBER_DECIMAL_SEP));
        }

        public static string SerializeDecimal2String(decimal value)
        {
            return Convert.ToString(value).Replace(NUMBER_DECIMAL_SEP, SERVER_DECIMAL_SEP);
        }

        public static decimal SerializeString2Decimal(string moneystr)
        {
            if (moneystr == null || moneystr.Length == 0)
                return 0;

            return Convert.ToDecimal(moneystr.Replace(SERVER_DECIMAL_SEP, NUMBER_DECIMAL_SEP));
        }

        #endregion

    }
}
