using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Xml;

namespace SuE.Tools
{
    /// <summary>
    /// SuE Common Extension Functions 
    /// Version: 1.8
    /// Last Change: 12.09.2024
    /// </summary>
    public static class Extensions
    {

        #region string

        /// <summary>
        /// Cuts a string out between to strings
        /// </summary>
        /// <param name="startOffset">Index where to start the search</param>
        /// <param name="text1">Keyword so search for start position</param>
        /// <param name="text2">Keyword so search for end position</param>
        /// <param name="defaultValue">default value in case of failire or if nothing is found</param>
        /// <returns></returns>
        public static string GetBetween(this string str, string text1, string text2, string defaultValue)
        {
            return str.GetBetween(0, text1, text2, defaultValue);
        }
        public static string GetBetween(this string str, int startOffset, string text1, string text2, string defaultValue)
        {
            int p1 = str.IndexOf(text1, startOffset);
            if (p1 == -1) { return defaultValue; }

            int p2 = str.IndexOf(text2, p1);
            if (p2 == -1) { return defaultValue; }

            if (p2 < p1) { return defaultValue; }

            return str.Substring(p1 + text1.Length, p2 - p1 - text1.Length);
        }
        public static string GetBetween(this string str, int startOffset, string startText, string defaultValue)
        {
            int p1 = str.IndexOf(startText, startOffset);
            if (p1 == -1) { return defaultValue; }

            return str.Substring(p1 + startText.Length, str.Length - p1 - startText.Length);
        }

        /// <summary>
        /// Removes a string between to strings and returns the result or the original string
        /// </summary>
        /// <param name="startOffset">Index where to start the search</param>
        /// <param name="text1">Keyword so search for start position</param>
        /// <param name="text2">Keyword so search for end position</param>
        /// <returns></returns>
        public static string RemoveBetween(this string str, int startOffset, string text1, string text2)
        {
            int p1 = str.IndexOf(text1, startOffset);
            if (p1 == -1) { return str; }

            int p2 = str.IndexOf(text2, p1);
            if (p2 == -1) { return str; }

            if (p2 < p1) { return str; }

            string newString = str.Substring(0, p1) +
                               str.Substring(p2 + 1);

            return newString;
        }

        /// <summary>
        /// Contains with InvariantCultureIgnoreCase
        /// </summary>
        public static bool ContainsIgnoreCase(this string str, string value)
        {
            return str.IndexOf(value, StringComparison.InvariantCultureIgnoreCase) > 0;
        }

        /// <summary>
        /// Equals with InvariantCultureIgnoreCase
        /// </summary>
        public static bool EqualsIgnoreCase(this string str, string value)
        {
            return str.Equals(value, StringComparison.InvariantCultureIgnoreCase);
        }

        /// <summary>
        /// StartsWith with InvariantCultureIgnoreCase
        /// </summary>
        public static bool StartsWithIgnoreCase(this string str, string value)
        {
            return str.StartsWith(value, StringComparison.InvariantCultureIgnoreCase);
        }

        /// <summary>
        /// IsNumber
        /// </summary>
        public static bool IsNumber(this string str)
        {
            foreach (char c in str)
            {
                if (!Char.IsNumber(c))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// ContainsNumbers - Counts all numbers in str and returns true if the count is between @minDigits and @maxDigits
        /// </summary>
        public static bool ContainsNumbers(this string str, int minDigits = 1, int maxDigits = 9999)
        {
            int numbersDetected = 0;

            foreach (char c in str)
            {
                if (Char.IsNumber(c))
                    numbersDetected++;
            }

            return numbersDetected >= minDigits && numbersDetected <= maxDigits;
        }

        /// <summary>
        /// ContainsNumbersContinious - Returns true if @str contains a countinious series of numbers of at least @minDigits numbers and maximum @maxDigits numbers
        /// </summary>
        public static bool ContainsNumbersContinious(this string str, int minDigits = 1, int maxDigits = 9999)
        {
            int numbersDetected = 0;

            foreach (char c in str)
            {
                if (Char.IsNumber(c))
                    numbersDetected++;
                else
                    numbersDetected = 0;


                if (numbersDetected >= minDigits && numbersDetected <= maxDigits)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Removes characters that are longer than @maxLength
        /// </summary>
        /// <param name="maxLength">Maximum length of the string</param>
        /// <returns></returns>
        public static string Limit(this string str, int maxLength)
        {
            return str.Substring(0, maxLength);
        }

        /// <summary>
        /// Fills the string with @chr up to total @maxLength of this string
        /// </summary>
        /// <param name="chr">Character to fill up with</param>
        /// <param name="maxLength">Length to prefix the string with</param>
        /// <returns></returns>
        public static string Prefix(this string str, char chr, int maxLength)
        {
            if (str.Length > maxLength)
                return str;

            return new string(chr, (maxLength - str.Length)) + str;
        }

        /// <summary>
        /// Escapes special chars for MySQL Databases
        /// </summary>
        public static string EscapeForMySQL(this string str)
        {
            return str.Replace("'", "\\'");
        }

        #endregion //string

        #region Timespan

        /// <summary>
        /// Removes Milliseconds from Timespan
        /// </summary>
        public static TimeSpan StripMilliseconds(this TimeSpan timespan)
        {
            return new TimeSpan(timespan.Days, timespan.Hours, timespan.Minutes, timespan.Seconds);
        }

        /// <summary>
        /// Format TimeSpan to Xh Ym Zs
        /// </summary>
        public static string ToStringHMS(this TimeSpan timespan)
        {
            return string.Format("{0}h {1}m {2}s", (int)timespan.TotalHours, timespan.Minutes, timespan.Seconds);
        }

        /// <summary>
        /// Format TimeSpan to Nd Xh Ym Zs
        /// </summary>
        public static string ToStringDHMS(this TimeSpan timespan)
        {
            return string.Format("{0}d {1}h {2}m {3}s", (int)timespan.TotalDays, timespan.Hours, timespan.Minutes, timespan.Seconds);
        }

        #endregion

        #region XML

        /// <summary>
        /// Creates a new node with name and sets its innerText
        /// </summary>
        /// <param name="name">Name of the new node</param>
        /// <param name="innerText">Value of the innerText Property of the new node</param>
        /// <returns></returns>
        public static XmlNode AppendNodeText(this XmlNode node, string prefix, string name, string namespaceUri, string innerText)
        {
            XmlNode nodeNew;
            if (node is XmlDocument)
                nodeNew = (node as XmlDocument).CreateNode(XmlNodeType.Element, prefix, name, namespaceUri);
            else
                nodeNew = node.OwnerDocument.CreateNode(XmlNodeType.Element, prefix, name, namespaceUri);

            if (innerText != null && innerText.Length > 0)
                nodeNew.InnerText = innerText;

            node.AppendChild(nodeNew);

            return nodeNew;
        }

        /// <summary>
        /// Creates a new node with name and sets its innerText
        /// </summary>
        /// <param name="name">Name of the new node</param>
        /// <param name="innerText">Value of the innerText Property of the new node</param>
        /// <returns></returns>
        public static XmlNode AppendNodeText(this XmlNode node, string name, string innerText)
        {
            return node.AppendNodeText(null, name, null, innerText);
        }
        public static XmlNode AppendNode(this XmlNode node, string prefix, string name, string namespaceUri)
        {
            return node.AppendNodeText(prefix, name, namespaceUri, null);
        }
        public static XmlNode AppendNode(this XmlNode node, string name)
        {
            return node.AppendNodeText(name, null);
        }


        /// <summary>
        /// Creates a new attribute with name and sets its value
        /// </summary>
        /// <param name="name">Name of the attribute to set</param>
        /// <param name="value">Value of the attribute</param>
        /// <returns></returns>
        public static void SetAttribute(this XmlNode node, string name, string value)
        {
            ((XmlElement)node).SetAttribute(name, value);
        }




        public static XmlNode FindFirstChild(this XmlNode parent, string childnode)
        {
            foreach (XmlNode node in parent.ChildNodes)
            {
                if (childnode.Equals(node.Name, StringComparison.InvariantCultureIgnoreCase))
                    return node;
            }

            return null;
        }


        public static string GetChildInnerValue(this XmlNode parent, string childnode, string defaultvalue = "")
        {
            XmlNode child = parent.FindFirstChild(childnode);
            if (child == null) return defaultvalue;

            return child.InnerText;
        }

        public static int GetChildInnerValue(this XmlNode parent, string childnode, int defaultvalue = -1)
        {
            string text = GetChildInnerValue(parent, childnode, "");

            if (text.Length == 0)
                return defaultvalue;


            int result;

            try { result = Int32.Parse(text); }
            catch (FormatException) { result = defaultvalue; }

            return result;
        }

        public static DateTime GetChildInnerValue(this XmlNode parent, string childnode, DateTime defaultValue)
        {
            string text = GetChildInnerValue(parent, childnode, "");

            if (text.Length == 0) { return defaultValue; }

            DateTime result;
            try
            {
                result = DateTime.Parse(text, null, System.Globalization.DateTimeStyles.RoundtripKind);
            }
            catch (FormatException)
            {
                result = defaultValue;
            }

            return result;
        }

        /// <summary>
        /// Outputs the XmlDocument in a nice human readable text
        /// </summary>
        /// <returns></returns>
        public static string OuterXmlNice(this XmlDocument doc)
        {
            string result = "";

            MemoryStream mStream = new MemoryStream();
            XmlTextWriter writer = new XmlTextWriter(mStream, Encoding.Unicode);
            XmlDocument document = new XmlDocument();

            try
            {
                // Load the XmlDocument with the XML.
                document.LoadXml(doc.OuterXml);

                writer.Formatting = Formatting.Indented;

                // Write the XML into a formatting XmlTextWriter
                document.WriteContentTo(writer);
                writer.Flush();
                mStream.Flush();

                // Have to rewind the MemoryStream in order to read
                // its contents.
                mStream.Position = 0;

                // Read MemoryStream contents into a StreamReader.
                StreamReader sReader = new StreamReader(mStream);

                // Extract the text from the StreamReader.
                string formattedXml = sReader.ReadToEnd();

                result = formattedXml;
            }
            catch (XmlException)
            {
                // Handle the exception
            }

            mStream.Close();
            writer.Close();
            return result;
        }

        public static void DebugPrintChildren(this XmlNode parent, string prefix = "")
        {
            Debug.WriteLine("");
            Debug.WriteLine(prefix + " DEBUG Children of " + parent.Name);

            foreach (XmlNode node in parent.ChildNodes)
            {
                //XmlNode? node = parent.ChildNodes[i];

                if (node == null)
                    Debug.WriteLine(prefix + "null!");
                else if (node.HasChildNodes)
                    Debug.WriteLine(prefix + node.Name + ": HasChildNodes=true");
                else
                    Debug.WriteLine(prefix + node.Name + ": " + node.InnerText);

            }

        }

        #endregion //XML

        #region Network

        public static bool IsPrivateV4(this IPAddress ipaddress)
        {
            int[] ipParts = ipaddress.ToString().Split(new String[] { "." }, StringSplitOptions.RemoveEmptyEntries)
                                     .Select(s => int.Parse(s)).ToArray();
            // in private ip range
            if (ipParts[0] == 10 ||
               (ipParts[0] == 192 && ipParts[1] == 168) ||
               (ipParts[0] == 172 && (ipParts[1] >= 16 && ipParts[1] <= 31)))
            {
                return true;
            }

            // IP Address is probably public.
            // This doesn't catch some VPN ranges like OpenVPN and Hamachi.
            return false;
        }

        #endregion

    }
}