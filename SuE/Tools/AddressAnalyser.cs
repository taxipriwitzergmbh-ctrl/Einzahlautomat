using SuE.Tools;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Collections;
using System.Diagnostics;

namespace SuE.TaMi
{
    /*  Version 1.2 (Last Change: 18.06.24) */
    /* 
     * Version 1.0 
     * ------------------------------------------------------------------------------------------------------
     *
     * 03.05.24: Beginn der Implementation
     * 
     * Version 1.1 
     * ------------------------------------------------------------------------------------------------------
     *
     * 17.06.24: Anpassung für NET 4.6 (TaMi Map)
     * 
     * Version 1.2 
     * ------------------------------------------------------------------------------------------------------
     *
     * 17.06.24: Schreibweisen wie "Ostend 12 Loffenau" werden jetzt auch ausgewertet
     *
     * 18.06.24: Analyse optimiert. ArrayElemtente werden in SplitAddressString(string addressText, out string[] resultAddressParts)
     *           jetzt entfernt wenn sie leer sind und Leerzeichen abgetrimmt.
    */



    public class AddressAnalyser
    {
        private static readonly List<string> localityTypes = new List<string>();

        private enum AddressPartType
        {
            Undefined = 0,
            StreetAndHNo = 1,
            Street = 2,
            HNo = 3,
            PostcodeAndCity = 4,
            Postcode = 5,
            City = 6,
            Locality = 7,
        }


        //Liste mit typischen Örtlichkeiten
        public static void InitLocalityTypes()
        {
            localityTypes.Add("bahnhof");

            localityTypes.Add("gasthof");
            localityTypes.Add("gasthaus");
            localityTypes.Add("getränkemarkt");

            localityTypes.Add("hotel");

            localityTypes.Add("kino");
            localityTypes.Add("klinik");
            localityTypes.Add("klinikum");
            localityTypes.Add("krankenhaus");

            localityTypes.Add("messe");

            localityTypes.Add("rehazentrum");
            localityTypes.Add("reiterstube");
            localityTypes.Add("reiterstüble");

            localityTypes.Add("tennisclub");
            localityTypes.Add("taxistand");
            localityTypes.Add("taxiplatz");
        }

        //Prüft ob der Text eine Örtlichkeit sein könnte
        public static bool IsLocality(string text)
        {
            string lowerText = text.ToLower();

            foreach (string localityType in localityTypes)
            {
                if (lowerText.Contains(localityType))
                {
                    if (!HasStreetnameEnding(lowerText))
                        return true;
                }
            }

            return false;
        }

        //Prüft ob es sich um ein Strassennamen handeln könnte
        public static bool HasStreetnameEnding(string text)
        {
            string lowerText = text.ToLower();

            if (lowerText.Contains("strasse"))
                return true;

            if (lowerText.EndsWith("str"))
                return true;

            if (lowerText.EndsWith("str."))
                return true;

            if (lowerText.EndsWith("weg"))
                return true;

            if (lowerText.EndsWith("pfad"))
                return true;


            return false;
        }


        public static bool SplitAddressString(string addressText, out string[] resultAddressParts)
        {
            string[] addressParts;
            char[] trimChars = new char[] { ' ' };

            //Durch ... In ... getrennt
            if (addressText.Contains(" In "))
            {
                addressParts = AddressAnalyser.SplitInTwoParts(addressText, " In ");
                addressParts = RemoveEmptyArrayItems(addressParts, trimChars);
            }

            //Durch ... in ... getrennt
            else if (addressText.Contains(" in "))
            {
                addressParts = AddressAnalyser.SplitInTwoParts(addressText, " in ");
                addressParts = RemoveEmptyArrayItems(addressParts, trimChars);
            }

            //Komma getrennt
            else if (addressText.Contains(','))
            {
                addressParts = addressText.Split(',');
                addressParts = RemoveEmptyArrayItems(addressParts, trimChars);
            }


            //Trenne durch Leerzeichen und analysiere den Inhalt
            else
            {
                addressParts = addressText.Split(' ');
                addressParts = RemoveEmptyArrayItems(addressParts, trimChars);

                //"Waldweg 1 Loffenau" oder "Waldweg 76532 Loffenau"
                if (addressParts.Length == 3)
                {
                    //Waldweg 1 Loffenau
                    if (addressParts[1].Length >= 1 && addressParts[1].Length < 5 && addressParts[1].Substring(0, 1).IsNumber())
                    {
                        resultAddressParts = new string[2];
                        resultAddressParts[0] = (addressParts[0] + " " + addressParts[1]).Trim();
                        resultAddressParts[1] = addressParts[2];

                        return true;
                    }

                    //5 stellige Nummer, dann müsste es PLZ und Ortsname sein
                    //Waldweg 76532 Loffenau
                    else if (addressParts[1].Length >= 1 && addressParts[1].Length == 5 && addressParts[1].Substring(0, 5).IsNumber())
                    {
                        resultAddressParts = new string[2];
                        resultAddressParts[0] = addressParts[0];
                        resultAddressParts[1] = (addressParts[1] + " " + addressParts[2]).Trim();

                        return true;
                    }

                    resultAddressParts = null;
                    return false;
                }


                //Ostendstr 12 76532 Loffenau
                else if (addressParts.Length >= 4)
                {
                    resultAddressParts = new string[2];
                    int n = 0;

                    for (int i = 0; i < addressParts.Length; i++)
                    {
                        //Der Wert ist eine PLZ, trage das gleich in Part 1 ein
                        if (n == 0 && addressParts[i].Length == 5 && addressParts[i].IsNumber())
                            n++;

                        resultAddressParts[n] = (resultAddressParts[n] + " " + addressParts[i]).Trim();

                        //Müsste die Hausnummer sein, ab dann sollte PLZ und Ort kommen.
                        if (n == 0 && addressParts[i].ContainsNumbers(1, 4))
                            n++;
                    }

                    return true;
                }


                //TODO: Sind es mehr als 2 Worte wird es schwieriger.
                else if (addressParts.Length != 2)
                {
                    resultAddressParts = null;
                    return false;
                }

            }


            resultAddressParts = addressParts;
            return addressParts != null;
        }

        //SplitAddressString
        public static bool SplitAddressString(string addressText, out string örtlichkeit, out string strasse, out string hnr, out string plz, out string ort)
        {
            örtlichkeit = String.Empty;
            strasse = String.Empty;
            hnr = String.Empty;
            plz = String.Empty;
            ort = String.Empty;

            if (addressText == null || addressText.Length == 0)
                return false;

            string[] adressParts;
            AddressAnalyser.SplitAddressString(addressText, out adressParts);

            if (adressParts == null || adressParts.Length == 0)
                return false;


            //Es konnte nichts durch Utils.SplitAddressString getrennt werden.
            if (adressParts.Length == 1)
            {
                örtlichkeit = adressParts[0];
                return true;
            }


            //Versuche alle 5 Adressteile zu finden
            AddressPartType[] adressTypes = new AddressPartType[adressParts.Length];
            int n = -1;

            for (int i = 0; i < adressParts.Length; i++)

            //foreach (string adressPart in adressParts)
            {
                n++;
                string text = adressParts[i].ToLower();

                //Strasse mit oder ohne Hausnummer prüfen: Am Waldhof, Auf der Miss, Waldstrasse, Rosenstraße, Hauptstr, Wasserweg
                if (text.StartsWith("am ") || text.StartsWith("auf der ") || text.ContainsIgnoreCase("strasse") || text.ContainsIgnoreCase("straße") || text.ContainsIgnoreCase("str.") || text.ContainsIgnoreCase("str ") || text.EndsWith("weg"))
                {
                    adressTypes[i] = AddressPartType.StreetAndHNo;
                    break;
                }

                //Nur Hausnummer "1" bis "9999"
                else if (text.Length < 5 && text.IsNumber())
                {
                    adressTypes[i] = AddressPartType.HNo;
                    break;
                }

                //Nur PLZ "12345"
                else if (text.Length == 5 && text.IsNumber())
                {
                    adressTypes[i] = AddressPartType.Postcode;
                    break;
                }

                //PLZ und Ort prüfen: 12345 Musterstadt
                else if (text.Length > 4 && text.Substring(0, 5).IsNumber())
                {
                    adressTypes[i] = AddressPartType.PostcodeAndCity;
                    break;
                }

                //Örtlichkeiten (hotel, bahnhof, krankenhaus, usw...)
                else if (IsLocality(text))
                {
                    adressTypes[i] = AddressPartType.Locality;
                    break;
                }

            } //foreach (string adressPart in addressParts)



            //Alle nicht zugeteilten addressParts noch einmal durchgehen
            for (int i = 0; i < adressParts.Length; i++)
            {
                if (adressTypes[i] == AddressPartType.Undefined && adressParts[i].Length > 1)
                {
                    if (strasse.Length == 0 && adressParts[i].ContainsNumbers(1, 4))
                    {
                        adressTypes[i] = AddressPartType.StreetAndHNo;
                    }
                    else if (ort.Length == 0)
                    {
                        adressTypes[i] = AddressPartType.PostcodeAndCity;
                    }
                }

            }


            //Wurde bei 2 Elementen, 2x AddressPartType.PostcodeAndCity zugeteilt muss der erste eine Strasse oder eine Örtlichkeit sein.
            //Beispiel: Großer Markt, Wesel oder Großer Markt in Wesel
            if (adressTypes.Length == 2 && adressTypes[0] == AddressPartType.PostcodeAndCity && adressTypes[1] == AddressPartType.PostcodeAndCity)
            {
                adressTypes[0] = AddressPartType.StreetAndHNo;
            }


            //Gefundene Adressteile in die Variablen übernehmen
            for (int j = 0; j < adressTypes.Length; j++)
            {
                if (adressTypes[j] == AddressPartType.StreetAndHNo)
                {
                    AddressAnalyser.SplitStrasseHNr(adressParts[j], out strasse, out hnr);
                }
                else if (adressTypes[j] == AddressPartType.PostcodeAndCity)
                {
                    AddressAnalyser.SplitPLZOrt(adressParts[j], out plz, out ort);
                }
                else if (adressTypes[n] == AddressPartType.Locality)
                {
                    örtlichkeit = adressParts[j];
                }
            }


            //Erster Buchstabe sollte groß sein
            if (örtlichkeit.Length > 1)
            {
                örtlichkeit = örtlichkeit.Substring(0, 1).ToUpperInvariant() + örtlichkeit.Substring(1);
            }

            if (strasse.Length > 1)
            {
                strasse = strasse.Substring(0, 1).ToUpperInvariant() + strasse.Substring(1);
            }

            if (ort.Length > 1)
            {
                ort = ort.Substring(0, 1).ToUpperInvariant() + ort.Substring(1);
            }


            //Debug.WriteLine($"Örtlichkeit: {örtlichkeit} Strasse: {strasse} HNr.: {hnr} PLZ: {plz} Ort: {ort}");


            return true;
        }



        //SplitStrasseHNr
        public static void SplitStrasseHNr(string str, out string strasse, out string hnr)
        {
            //strasse = string.Empty;
            //hnr = string.Empty;

            int idxHNrStart = -1;
            int idxHNrEnd = -1;

            for (int i = str.Length - 1; i >= 0; i--)
            {
                if (Char.IsNumber(str[i]))
                {
                    idxHNrStart = i;

                    if (idxHNrEnd == -1)
                        idxHNrEnd = i;
                }
                else if (str[i] == '-' || str[i] == '.')
                {
                    //Bindestrich oder Punkt ist erlaubt für HNr.: 12-15, 4.1c
                }
                else
                {
                    break; //Anderes Zeichen außer Zahl, Ende
                }
            }

            if (idxHNrStart > -1 && idxHNrEnd > -1)
            {
                //Ist vor der Hausnummer ein Punkt, übergehe diesen
                if (str[idxHNrStart] == '.')
                    idxHNrStart++;

                //Strasse
                strasse = str.Substring(0, idxHNrStart).Trim();

                //HNr
                //idxHNrStart++;
                hnr = str.Substring(idxHNrStart, (idxHNrEnd + 1 - idxHNrStart));
            }
            else
            {
                strasse = str;
                hnr = String.Empty;
            }

            //Endet der Strassenname mit einem Punkt, entferne diesen
            if (strasse.EndsWith("."))
                strasse = strasse.Substring(0, strasse.Length - 1);
        }

        //SplitPLZOrt
        public static void SplitPLZOrt(string str, out string plz, out string ort)
        {
            plz = string.Empty;
            ort = string.Empty;

            string[] result = str.Trim().Split(' ');

            if (result.Length > 1)
            {
                if (result[0].ContainsNumbersContinious(4, 8))
                { 
                    plz = result[0];
                    ort = result[1];
                }
                else if (result[1].ContainsNumbersContinious(4, 8))
                {
                    plz = result[1];
                    ort = result[0];
                }
                else
                {
                    plz = string.Empty;
                    ort = str;
                }
            }

            else if (result.Length == 1) 
            {
                if (result[0].ContainsNumbersContinious(4, 8))
                    plz = result[0];
                else
                    ort = result[0];
            }
        }


        //SplitInTwoParts
        private static string[] SplitInTwoParts(string text, string separator)
        {
#if NET8_0_OR_GREATER
            return text.Split(separator, 2);
#else
            string[] result;
            int p = text.IndexOf(separator);

            if (p > 0)
            {
                result = new string[2];
                result[0] = text.Substring(0, p);
                result[1] = text.Substring(p + separator.Length);
            }
            else
            {
                result = null;
            }

            return result;
#endif
        }

        //RemoveEmptyArrayItems
        private static string[] RemoveEmptyArrayItems(string[] sourceArray, char[] trimChars) 
        {
            ArrayList arrayList = new ArrayList();

            foreach (string item in sourceArray) 
            {
                string itemNew;

                if (trimChars != null)
                    itemNew = item.Trim(trimChars);
                else
                    itemNew = item;

                if (itemNew.Length > 0)
                    arrayList.Add(itemNew);
            }

            return (string[])arrayList.ToArray(typeof(string));
        }

    }
}
