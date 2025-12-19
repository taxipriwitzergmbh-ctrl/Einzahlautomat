using System;
using System.Data.SqlClient;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace Geldautomat
{
    public class PersonalInfo
    {
        public const string DB_FIELDS = "PID,Name,Vorname,NFCTagUID,Fahrercode,Gesperrt,EintrittAm,AustrittAm,AppRechte,EMail";

        public int PID { get; set; }
        public string Name { get; set; }
        public string Vorname { get; set; }
        public string NFC { get; set; }
        public string Fahrercode { get; set; }
        public string EMail { get; set; }


        // Erweiterungen
        public bool Gesperrt { get; set; }
        public System.DateTime? Eintrittsdatum { get; set; }
        public System.DateTime? Austrittsdatum { get; set; }

        public int AppRights1 { get; set; }
        public int AppRights2 { get; set; }

        public bool IsAutomatAdmin
        {
            get { return (this.AppRights2 & (int)SuE.TaMi.AppRights2.AUTOMAT_ADMIN) == (int)SuE.TaMi.AppRights2.AUTOMAT_ADMIN; }
        }


        // NEU: vorab geladener Guthaben-Saldo (LoginContext) – falls gesetzt, erneute DB-Abfrage im AbrechnungForm vermeiden
        public decimal PreloadedGuthaben { get; set; }



        public string CheckIfCanLogin()
        {
            var now = DateTime.Now; 
            DateTime defExit = new DateTime(1899, 12, 30);

            if (Gesperrt)
                return "Zugang gesperrt.";

            if (Eintrittsdatum.HasValue && Eintrittsdatum.Value > now)
                return "Eintrittsdatum liegt in der Zukunft.";

            if (Austrittsdatum.HasValue && Austrittsdatum.Value != defExit && Austrittsdatum.Value < now)
                return "Austrittsdatum abgelaufen.";

            return null;
        }
    }
}