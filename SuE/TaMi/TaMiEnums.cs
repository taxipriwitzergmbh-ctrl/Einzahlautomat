using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SuE.TaMi
{
    /*
        PROJEKT           :  Enums
        VERSION           :  1.03
        
        ERSTELLUNGS-DATUM :  17.10.2018
        ÄNDERUNGS-DATUM   :  09.12.2025
        ÄNDERUNG:            siehe Entwicklungsgeschichte
        DURCHGEFUEHRT VON :  SuE-Software  [sue]

        FUNKTION          :  Alle Enums für TaMi

        BEMERKUNG         :  ---

        ======================== ENTWICKLUNGS-GESCHICHTE ========================

        --- Version 1.00 --------------------------------------------------------

        17.10.2018  mcs   Beginn der Implementation in NET

        --- Version 1.02 --------------------------------------------------------

        26.01.2024  mcs   ShiftEntryFlags und ShiftFlags hinzugefügt

        --- Version 1.03 --------------------------------------------------------

        09.12.2025  mcs   ShiftEntryFlags und ShiftFlags hinzugefügt

    */

    #region Enums

    public enum AppId : byte
    {
        MANAGER = 1,
        DISPO = 2,
        FAKTURA = 3,
        DISPOAPP = 4,
        MAP = 5,
        FAHRERTERM = 6,
        KASSENCLIENT = 7,

        ATBSERVER = 31,
        SYNC = 32
    }

    public enum NotifyFlags : ushort
    {
        NONE = 0x00,
        CONFIRMMESSAGES = 0x01, //Anfragen vom Cient werden mit CONF bestätigt
        JOBS = 0x02,
        BILLS = 0x04,
        ZONES = 0x08,
        CALLS = 0x10,

        VEHICLES = 0x40,

        PROTOCOL = 0x8000,
    }

    public enum SyncFlags : ushort
    {
        FULLSYNC = 0x01 //Vollsynchronisierung
    }

    public enum TAC : byte
    {
        NONE = 0,

        RESULTMSG = 1,

        CLIENTUPDATEREQ = 3,
        CLIENTUPDATEMSG = 4,

        LOGINREQ = 5,
        LOGINMSG = 6,
        LOGOUTREQ = 7,
        LOGOUTMSG = 8,

        CALLMANREQ = 10,
        CALLMANMSG = 11,
        CALLSYNCREQ = 12,
        CALLSYNCMSG = 13,

        OBJECTCHANGEREQ = 16,
        OBJECTCHANGEMSG = 17,

        VEHICLEMANREQ = 21,
        VEHICLEMANMSG = 22,

        PERSONALMANREQ = 23,
        PERSONALMANMSG = 24,

        JOBREQ = 41,
        JOBMSG = 42,

        JOBREPREQ = 45,
        JOBREPMSG = 46,

        ZONEMANREQ = 51,
        ZONEMANMSG = 52,

        CONFIGREQ = 201,
        CONFIGMSG = 202,

        PROTOMSG = 210,
        STATUSINFMSG = 211,

        KEEPALIVEREQ = 254,
        KEEPALIVEMSG = 255
    }

    public enum ResultCodes : short
    {
        TIMEOUT = -1,
        UNKNOWN = 0,
        SUCCESS = 1,

        CMDUNKNOWN = 10,
        FAILED = 11,
        ERRPIDPASS = 12,
        PROTOVERSIONOUTDATED = 13,
        NETWORKDISABLED = 14,
        NOTENOUGHRIGHTS = 15,
        NOTENOUGHLICENCES = 16,
        DEMOPERIODOVER = 17,
        PERSONALLOCKED = 18, /*Gesperrt, Ein-/Austrittsdatum*/

        OPERATIONCURRENTLYIMPOSSIBLE = 21,

        INVALIDFHZID = 51,
        INVALIDPERSID = 52,

        SVRSHUTDOWN = 110
    }

    public enum AppRights1 : int
    {
        MANAGER = 0x01,
        DISPO = 0x02,
        DISPOQUERYFULL = 0x04,
        CONTROLDISPATCHAI = 0x08,
        VIEWLOG = 0x010,
        DISPOEDITPRICE = 0x020,
        DISPOEDITJOB = 0x040,
        DISPOEDITREPEATING = 0x080,

        FAKTURA = 0x0100,

        DISPOEDITCUSTOMER = 0x0200,
        DISPOCHANGEFILTER = 0x0400,
        DISPODISPATCH = 0x0800,
        DISPOEDITJOBCOMPLETED = 0x1000,
        DISPOVIEWREPEATING = 0x02000,
        DISPOVIEWTOTALS = 0x04000,
        DISPOQUERYJOBS = 0x08000,

        KASSENCLIENT = 0x200000,
        DISPOAPP = 0x0400000,
        CHANGESETTINGS = 0x0800000
    }

    public enum AppRights2 : int
    {
        ZEITERFASSUNG_EDIT = 0x400000,

        SCHICHT_EDIT = 0x1000000,

        FAHRERTERMALL = 0x2000000,
        AUTOMAT_ADMIN = 0x4000000  //Priwitzer Kassenautomat Software (Admin Modus)
    }

    //Actions für VEHICLEMANREQ, VEHICLEMANMSG
    public enum VehicleManAction : byte
    {
        REQPOSUPDATE = 4, // Abfrage der Position (PositionStatusREQ an FleetServer)
        SETPOSSTATUS = 5, // Setze StatusId/Position des Fahrzeugs für Debugzwecke setzen.
        STATICDATALIST = 6,
        DYNDATALIST = 11,
        DYNDATASYNC = 12,

        GETGROUPS = 21,

        INTERNAL_JOBSCHANGED = 251 /*Kommt nicht vom Server wird nur intern verwendet um mitzuteilen das sich die Anzahl der aktiven Aufträge auf dem Fhz geändert hat. */
    }

    //Actions für PERSONALMANREQ, PERSONALMANMSG
    public enum PersonalManAction : byte
    {
        STATICDATALIST = 6
    }

    //Actions für TAMI_TAC_ZONEMANREQ, TAMI_TAC_ZONEMANMSG
    public enum ZoneManAction : byte
    {
        //ZONELIST = 1, //ALT

        STATICDATALIST = 6,
        ZONEFHZREPOS = 11,
        ZONEFHZCHANGE = 31,
    }


    //Actions für CONFIGREQ, CONFIGMSG
    public enum ConfigAction : byte
    {
        REGINFORMATION = 1,

        SETDISPATCHAISTATUS = 101,
        SETDISPATCHAILOCKTIMES = 102,
        SETDISPATCHFHZLOCKTIME = 103,
        SETDISPATCHFHRLOCKTIME = 104
    }

    //Actions für JOBREQ, JOBMSG
    public enum JobAction : byte
    {
        GET = 1,
        SET = 2,
        DEL = 3,
        DELSUBITEMS = 4,   //Um Dispo mitzuteilen das alle SubItems mit RepID = 'xxxxx' gelöscht wurden!

        GETLIST = 6,

        GETLOG = 11,
        GETETA = 12,
        GETDISPATCHFHZLIST = 13,

        DISPATCH = 21,
        DISPATCH_ALL = 22,

        SYNCJOB = 51
    }


    public enum ShiftEntryType : byte
    {
        SHIFTBEGINN = 1,
        SHIFTEND = 2,
        TRIPBUSY = 3,
        TRIPFREE = 4,         /*Leer Fahrt*/
        /*Anfahrt zum Kunden*/

        TRIPBUSINESS = 7,     /*Geschäftsfahrt*/
        TRIPPRIVATE = 8,      /*Privatfahrt*/

        PAUSE = 10,           /*Pause*/
        PAUSEABWESEND = 11,   /*Pause (Abwesend)*/
        STANDBY = 12,         /*Bereitschaftszeit*/
        REFUEL = 13,          /*Tanken*/
        WITHDRAW = 14,        /*Ausgabe*/

        CANCELATION = 91,      /*Storno (Umsatz)*/

        BASF = 201,            /* Extra für Böhm/BASF Sonderwünsche */
    }

    public enum ShiftEntryFlags
    {
        PRINTED = 0x1,                          //Wurde bereits vom Fahrer in den Schichtdaten (FahrerTerm) ausgedruckt 0=Noch nicht ausgedruckt
        MARKED = 0x2,                           //Datensatz soll markiert/hervorgehoben werden. In der Anzeige für den Benutzer
        SITZKONTAKT = 0x10,                     //Von Taxameter: 1=Mit Sitzkontakt 0=Ohne Sitzkontakt
        KREDITFAHRT = 0x20,                     //Von Taxameter: 1=Kreditfahrt vom Taxameter
        PAUSCHALFAHRT = 0x40,                   //Von Taxameter: 1=Pauschalfahrt vom Taxameter
        EURO = 0x80,                            //Von Taxameter: 1=Euro 0=Nationale Währung (z.B. DM) (unused)

        TRINKGELD_AG = 0x400,                   //Trinkgeld Arbeitgeber (Steuerpflichtiger Umsatz), Trinkgeld ist für Arbeitnehmer (Steuerfrei)
        PRINTEDRECEIPT = 0x800,                 //Es wurde ein Beleg/Quittung gedruckt
        SIGNATURE = 0x1000,                     //Es wurde elektronisch unterschrieben
        PRICETRIPISDIFFFROMLASTTRIP = 0x2000,   //Der Fahrpreis wurde vom Taxameterdatensatz interpoliert (siehe TaxameterRecord.fromHale)
        FROMTAXAMETER = 0x4000,                 //1=Daten sind vom Taxameter
    }

    public enum ShiftFlags
    {
        CTSCOMPLETE = 0x1,          //Wurde für/von Kassenautomaten als abgerechnet markiert bzw. komplett ausgeglichen (nicht wieder anzeigen, auch wenn Restsumme > 0)
        USERFORCECOMPLETE = 0x4,    //Anwender hat die Schicht als "abgeschlossen" markiert (wird nicht wieder im Kassenautomat angezeigt)
        USERCHECKED = 0x8,          //Anwender hat die Schicht als "geprüft" markiert
    }

    public enum Paytype : byte
    {
        UNKNOWN = 0,
        CASH = 10,
        CARD = 20,  /*CARD_XXX Typen*/
        BILL = 30,
        HEALTHINSURANCE = 40,
        VOUCHER = 50,
        FAILEDTRIP = 60,
        APP = 70,
        APP_SUE = 71,
        APP_TAXIDEUTSCHLAND = 72,
        APP_TAXIEU = 73,
        PAYTYPE_TEST = 100
    }

    #endregion

    #region Enums for Config Tags

    public static class ConfigTag
    {
        public const string APP_SERVER = "SERVER";
        public const string APP_DISPO = "DISPO";
        public const string APP_FAKTURA = "FAKTURA";

        public const string CFG_ALLGEMEIN = "Allgemein";
        public const string CFG_EDITOR = "Editor";
        public const string CFG_KARTE = "Karte";

    }

    #endregion

    #region Enums for Job and JobPos

    public enum JobTyp : byte
    {
        NORMAL = 0,
        DAUER = 1,
        SOFORT = 2,
        AUTOBOOK = 3,
        EXTERNAL = 4,       //Aus externer Software
        LINIE = 5,          //Linienmodul von TaMi (AST/ALT)
        LINIE_EXTERN = 6,   //Linienauftrag aus einer externen Software: AST/ALT
    }

    public enum JobRelTyp : byte
    {
        KEIN = 0,
        DAUER = 1,
        CLONE = 2,
        AUTOPICKUP = 6,
        CALL = 11,
        WEBBOOK = 12,
        WEBORDER = 13,        //TaMi Weborder (http://weborder.sue-software.de/?cid=xxxx)

        STAEDLERMOSYS = 122,  //KMG MoSys-Anbindung über TaMi Sync
        ESMANSAT = 126,       //ESM Ansat Schnittstelle über TaMi Autobook Server
        TDIMO = 127,          //T.DiMo (AST/ALT) Schnittstelle über TaMi Autobook Server,

        TAXIEU = 131,         //Taxi.eu App Anbindung

        DBVOUCHER = 133,      //DB Gutschein über FMS
    }

    public enum JobStatus : byte
    {
        OFFEN = 0,
        VERMITTELT = 1,
        ERFOLGREICH = 2,
        FEHLFAHRT = 3,
        STORNIERT = 4,
        ANGEBOT = 11
    }

    public enum JobDFStatus : byte
    {
        NONE = 0,
        TRANSFERING = 1,                   //Wird an Endgerät übertragen
        TRANSFERED = 2,                    //Erfolgreich an Endgerät übertragen
        TRANSFERFAILED = 3,                //Übertragung an Endgerät fehlgeschlagen
        RESPONSEACCEPTED = 4,              //Fahrer hat bestätigt
        RESPONSETIMEOUT = 5,               //Fahrer hat gar keine Rückmeldung gesendet
        RESPONSEREJECTED = 6,              //Fahrer hat abgelehnt
        RESPONSEREJECTEDAUTO = 7,          //Fahrer hat automatisch abgelehnt (Ablauf des Timeouts)

        AUTODISPATCHING = 11,              //Wird automatisch vermittelt
        AUTODISPATCHFREEOFFERING = 12,     //Ist im freien Angebot
        AUTODISPATCHED = 13,               //Wurde automatisch vermittelt
        AUTODISPATCHIMPOSSIBLE = 14,       //Kann niemals automatisch vermittelt werden
        AUTODISPATCHFAILED = 15,           //Automatische Vermittlung ist fehlgeschlagen
        AUTODISPATCHFAILEDWILLRETRY = 16,  //Automatische Vermittlung ist fehlgeschlagen (technischer Fehler), wird wiederholt.
        AUTODISPATCHNOFOUNDWILLRETRY = 17, //Automatische Vermittlung ist fehlgeschlagen (kein Fhz gefunden/verfügbar), wird wiederholt.
        AUTODISPATCHEDFREEOFFER = 18,      //Wurde automatisch als Freies Angebot vermittelt
    }

    public enum JobFlag : int
    {
        NONE = 0x00,
        TEXT = 0x01,                          //Es ist ein Testauftrag

        APPROACH_DEPARTURE = 0x04,            //Der Fahrer meldet Anfahrt zum Abholort
        APPROACH_LOADED = 0x08,               //Der Fahrer hat an der Startadresse geladen
        PREPLANNED_MANUAL = 0x10,             //Der Auftrag wurde manuell vorgeplant (z.B. In der Dispo mit v### oder d###)

        GEOCODEDONE = 0x80,                   //Die Geocodierung wurde durchgeführt
        GEOCODEFAIL = 0x100,                  //Die Geocodierung ist fehlgeschlagen

        LINIE_NEXTDAY = 0x800,                //Das Auftragsdatum wurde ein Tag erhöht, weil die Zeit im Fahrplan nach 00:00 Uhr ist (Plan ist dann aber vom Vortag)

        CUSTOMERSIGNED = 0x1000,              //Es muss eine Image-Unterschrift zu dem Auftrag existieren
        DRIVERCHANGED_SOMETHING = 0x2000,     //Der Fahrer hat etwas geändert (Zahlart/Preis)
        SYNCWITHORIGIN = 0x4000,              //Auftrag muss noch mit der Quelle synchronisiert werden (z.B.: Taxi-Deutschland Preis bei RF-TD)

        PHONENUMBER_UNSAVED = 0x8000,         //Die Rufnummer wurde keinem Kunden zugeordnet
    }

    public enum JobFlagFaktura : int
    {
        NONE = 0,
        ALREADYUSED = 0x01
    }

    public enum JobPosFlags : byte
    {
        //VERALTET: DEFAULT = 1,     //Adresse vom Benutzer eingegeben
        //FROMSYSTEM = 0x02,         //Veraltet 09.10.25: Adresse durch System/Geocodierung erstellt (automatisch)
        GEOCODED = 0x04,             //Adresse wurde bereits geocodiert. Nicht mehr geocodieren bzw. in FahrerApp Lat/Lng für Navigation verwenden
        PREFERCOORDINATES = 0x08,    //Koordinaten für Navigation bevorzugen
        EINSTIEG = 0x10,             //Wegpunkt ist ein Einstieg
        AUSSTIEG = 0x20,             //Wegpunkt ist ein Ausstieg
    }

    #endregion
}
