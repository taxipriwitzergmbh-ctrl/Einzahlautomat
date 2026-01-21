using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SuE.TaMi
{
    public class Optionen
    {

        //Gibt ein string array mit den fehlenden result[0] und vorhandenen result[1] Optionen zurück
        public static string[] SplitMissingOptions(string requiredOptionString, string availableOptionString)
        {
            string[] result = { string.Empty, string.Empty };

            //Es werden gar keine Optionen benötigt
            if (requiredOptionString.Length == 0)
                return result;


            string[] requiredOptions = requiredOptionString.Split(' ');
            string[] availableOptions = availableOptionString.Split(' ');
            
            for (int i = 0; i < requiredOptions.Length; i++)
            {
                if (requiredOptions[i].Length > 0 && !availableOptions.Contains(requiredOptions[i]))
                    result[0] += requiredOptions[i] + " ";
                else
                    result[1] += requiredOptions[i] + " ";
            }

            result[0] = result[0].Trim();
            result[1] = result[1].Trim();

            return result;
        }

        //Gibt die Anzahl der fehlende Optionen zurück
        public static int GetMissingOptionCount(string requiredOptionString, string availableOptionString)
        {
            //Es werden gar keine Optionen benötigt
            if (requiredOptionString.Length == 0)
                return 0;


            string[] requiredOptions  = requiredOptionString.Split(' ');
            string[] availableOptions = availableOptionString.Split(' ');
            int result = 0;

            for (int i = 0; i < requiredOptions.Length; i++)
            {
                if (requiredOptions[i].Length > 0 && !availableOptions.Contains(requiredOptions[i]))
                    result++;
            }

            return result;
        }


    }
}
