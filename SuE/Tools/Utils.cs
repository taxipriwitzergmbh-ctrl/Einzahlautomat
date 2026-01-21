using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Text;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;

namespace SuE.Tools
{
    /*  Version 1.0  (Last Change: 01.07.21) */
    /* 
     * Version 1.0 
     * ------------------------------------------------------------------------------------------------------
     *
     * 01.07.21: Beginn der Implementation
     * 

     */

    class Utils
    {
        //in .NET 4.0, TLS 1.2 is not supported, but if you have .NET 4.5 (or above) installed on the system
        //then you still can opt in for TLS 1.2 even if your application framework doesn't support it.
        //The only problem is that SecurityProtocolType in .NET 4.0 doesn't have an entry for TLS1.2,
        //so we'd have to use a numerical representation of this enum value:
        public static void EnableMaximumTLSVersion()
        {
            //Versuche bis TLS 1.3
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls | (SecurityProtocolType)3072 | (SecurityProtocolType)12288;
            }
            catch (Exception)
            {
                //Versuche bis TLS 1.2
                try
                {
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls | (SecurityProtocolType)3072;
                }

                //Sonst nur bis TLS 1.1
                catch (Exception)
                {
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls;
                }
            }

        }

        /*
        public static PointLatLng TryParseToLatLng(string text, char separator = ',')
        {
            //Suche nach Koordinaten
            string[] ary;
            ary = text.Split(separator);

            if (ary.Length > 1)
            {
                double dblLat;
                double dblLng;
                try
                {
                    dblLat = Convert.ToDouble(ary[0].Trim(), CultureInfo.InvariantCulture);
                    dblLng = Convert.ToDouble(ary[1].Trim(), CultureInfo.InvariantCulture);

                    return new PointLatLng(dblLat, dblLng);
                }
                catch (FormatException)
                {
                    //Ignore
                }

                return PointLatLng.Empty;
            }

            return PointLatLng.Empty;
        }
        */


        /// <summary>
        /// Posts string to an url and returns the reply as a string
        /// </summary>
        /// <param name="url">The URL.</param>
        /// <returns></returns>
        public static async Task<string> PostToURL(string url, string contentType, string content)
        {
            StringContent httpContent = new StringContent(content, Encoding.UTF8, contentType);

            HttpClient httpClient = new HttpClient();
            HttpResponseMessage response = await httpClient.PostAsync(url, httpContent);

            string replyContent;
            if (response.Content != null)
            {
                byte[] byteArrayContent = await response.Content.ReadAsByteArrayAsync();
                replyContent = Encoding.UTF8.GetString(byteArrayContent, 0, byteArrayContent.Length);
            }
            else
            {
                replyContent = string.Empty;
            }

            if (response.IsSuccessStatusCode)
            {
                return replyContent;
            }
            else
            {
#if NET8_0_OR_GREATER
                throw new HttpRequestException(replyContent, null, response.StatusCode);
#else
                throw new HttpRequestException(replyContent + " HTTP Result: " + response.StatusCode);
#endif
            }
        }


    }
}
