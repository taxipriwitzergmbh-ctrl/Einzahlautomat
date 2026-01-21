
#define HAS_UI

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;


namespace SuE.Tools
{
    /*  Version 3.2 (Last Change: 01.04.25) */
    /* 
     * Version 1.0 
     * ------------------------------------------------------------------------------------------------------
     *
     * 05.04.15: Beginn der Implementation
     * 
     * Version 1.1 
     * ------------------------------------------------------------------------------------------------------
     * 
     * 11.05.15: try catch Block in der Funktion Post eingebaut
     * 
     * 16.06.15: appuptime in doReportError eingebunden.
     * 
     * 07.07.15: appuptime wird nun nur in Sekunden angegeben.
     *           Exception.InnerException wird nun auch übermittelt, sofern diese gesetzt ist
     * 
     * Version 1.2
     * ------------------------------------------------------------------------------------------------------
     * 
     * 20.08.15: SetAdditionField eingebaut, damit können Anwendungsspezifische Informationen übergeben werden.
     * 
     * Version 1.3
     * ------------------------------------------------------------------------------------------------------
     * 
     * 23.08.19: define SHOW_MESSAGEBOX hinzugefügt
     * 
     * Version 1.4
     * ------------------------------------------------------------------------------------------------------
     * 
     * 05.02.20: doReportError um die optionalen Parameter info1, info2 erweitert.
     *           Für eine zusätzliche Info beim manuellen Aufruf der Funktion.
     * 
     * Version 1.5
     * ------------------------------------------------------------------------------------------------------
     * 
     * 03.12.20: Fehler beseitigt wenn e.InnerException.TargetSite  = null ist
     *           AppName und AppVersion werden in doReportError jetzt via Reflection ausgelesen.
     * 
     * Version 1.6
     * ------------------------------------------------------------------------------------------------------
     * 
     * 16.02.21: URI_ERRORREPORT von 1und1 WebServer auf SuE-Server geändert.
     *           Funktion InstallHandlers() zur Vereinfachung hinzugefügt.
     *           define HAS_UI hinzugefügt für UI und nicht UI Applikationen
     * 
     * 10.06.21: doReportError in DoReportError umbenannt
     * 
     * Version 1.7
     * ------------------------------------------------------------------------------------------------------
     * 
     * 18.06.21: Die Anwendungsversion wurde falsch übermittelt. An der 3ten Stelle wird jetzt die korrekte
     *           Nummer übergeben.
     * 
     * Version 1.8
     * ------------------------------------------------------------------------------------------------------
     * 
     * 26.07.21: #define SHOW_MESSAGEBOX entfernt und gegen Variable ErrorReporter.ShowMessageBox getauscht.
     *           So kann ein automatisierter GUI Prozess auch keine Fehlermeldung anzeigen.
     *           
     * Version 1.9
     * ------------------------------------------------------------------------------------------------------
     * 
     * 03.08.22: Parameter e bei DoReportException kann jetzt auch null sein
     * 
     * 24.01.23: Schreibfehler korrigiert.
     * 
     * Version 2.0
     * ------------------------------------------------------------------------------------------------------
     * 
     * 08.04.23: Von WebClient auf HTTPClient umgebaut.
     * 
     * Version 2.1
     * ------------------------------------------------------------------------------------------------------
     * 
     * 24.04.23: Für Android und iOS erweitert. Dort kommen die Handler MainActivity.cs und iOS in AppDelegate
     * 
     * Version 2.2
     * ------------------------------------------------------------------------------------------------------
     * 
     * 04.03.24: PostForm überarbeitet und von asynchron auf synchron geändert.
     *           Der GlobalBrokerService als Dienst/Console konnte HttpClient.PostAsync nicht aufrufen.
     *           Der Grund dafür ist unbekannt, aber als ich es synchron programmiert habe ging es.
     *           
     *           HTTP_TIMEOUT hinzugefügt und auf 10 Sekunden festgelegt.
     * 
     * Version 2.3
     * ------------------------------------------------------------------------------------------------------
     * 
     * 25.03.24: AggregateException Handling hinzugefügt. Es werden nun alle Exceptions aufgelistet.
     * 
     * Version 2.4
     * ------------------------------------------------------------------------------------------------------
     * 
     * 28.03.24: Prüfung auf null Werte/Objekte in DoReportException verbessert
     * 
     * Version 2.5
     * ------------------------------------------------------------------------------------------------------
     * 
     * 29.03.24: Extension Methode für Task Klasse hinzugefügt um Exceptions zu loggen ohne die Anwendung
     *           dadurch abschmieren zu lassen.
     * 
     * Version 2.6
     * ------------------------------------------------------------------------------------------------------
     * 
     * 17.04.24: Code für NET 5+ und NET 4
     *           MessageBox anstatt mit Ja/Nein auf OK geändert. Da im ExceptionHandler die Ausführung
     *           sowieso nicht fortgesetzt werden kann.
     * 
     * Version 2.7
     * ------------------------------------------------------------------------------------------------------
     * 
     * 19.04.24: Die Prüfung Environment.UserInteractive wieder entfernt. Der if# HAS_UI und ShowMessageBox
     *           Parameter reichen.
     * 
     * Version 2.8
     * ------------------------------------------------------------------------------------------------------
     * 
     * 03.05.24: Der AppName und die AppVersion werden in der Funktion InstallHandlers ausgelesen und
     *           zwischengespeichert. Je nach Thread ist es nämlich ein anderer Name.
     *           #
     * 
     * Version 2.9
     * ------------------------------------------------------------------------------------------------------
     * 
     * 01.08.24: Methode AddExceptionDetails hinzugefügt. Anstatt alles einzeln der items Liste hinzuzufügen.
     *           Doppelte Information über .Source entfernt und aufgeräumt
     *           Exception.Data wird auch zu den jeweiligen Exception Details ausgegeben.
     *           
     * Version 3.0
     * ------------------------------------------------------------------------------------------------------       
     *
     * 11.08.24: Property ExitAppOnException hinzugefügt.
     *           Text in der Messagebox etwas klarer formatiert.
     * 01.10.24: appuptime als Timespan mit Format Tage, Stunden, Minuten, Sekunden und Millisekunden
     *
     * Version 3.1
     * ------------------------------------------------------------------------------------------------------       
     *
     * 25.03.25: Task.Run(() => ....); In DoReportException hinzugefügt für die Version vor NET5_0_OR_GREATER
     *           ExitAppOnException ist per Default nun false
     *
     * Version 3.2
     * ------------------------------------------------------------------------------------------------------       
     *
     * 01.04.25: Kleiner Fix bei MessageBox. Direkter Verweis auf System.Windows.Forms.Application hinzugefügt.
     *           Und dafür using System.Windows.Froms entfernt.
     * 
    */

    /// <summary>
    /// Sendet ein Fehlerbericht über unbehandelte Ausnahmen an SuE-Software und zeigt eine
    /// ggf. eine Messagebox mit deren Inhalt an.
    /// </summary>
    public static class ExceptionReporter
    {
        private static readonly string URI_ERRORREPORT = "http://er.sue-software.de/er/put"; //SuE-Server
        private static TimeSpan HTTP_TIMEOUT = new TimeSpan(0, 0, 10); // 10 Sekunden als Default

        private static string mAppName;
        private static string mAppVersion;
        private static readonly NameValueCollection additionalFields = new NameValueCollection();

        // = false, damit der GlobalBroker nicht abschmiert
        // = false, sonst wird der AsyncTask PostForm nicht abgeschlossen (Xamarin)
        public static bool ExitAppOnException = false;

#if HAS_UI
        public static bool ShowMessageBox = true;
#endif

        /// <summary>
        /// Globale ExceptionHandler installieren um unbehandelte Ausnahmen zu berichten
        /// </summary>
        public static void InstallHandlers()
        {
            try
            {
                System.Reflection.Assembly callingAssembly = System.Reflection.Assembly.GetCallingAssembly();
                System.Reflection.AssemblyName name = callingAssembly.GetName();
                mAppName = (name.Name != null ? name.Name : "null");

                if (name.Version != null)
                    mAppVersion = name.Version.Major + "." + name.Version.Minor + "." + name.Version.Build + "." + name.Version.Revision;
                else
                    mAppVersion = "null";

            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);

                mAppName = "failed to get: " + ex.Message;
                mAppVersion = mAppName;
            }

#if !DEBUG
#if HAS_UI
            System.Windows.Forms.Application.ThreadException += new ThreadExceptionEventHandler(Application_UnhandledThreadExceptionHandler); // Ereignis-Handler für UI-Threads:  
            System.Windows.Forms.Application.SetUnhandledExceptionMode(System.Windows.Forms.UnhandledExceptionMode.CatchException);
#endif
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException; // Ereignis-Hanlder für nicht UI-Threads:
            TaskScheduler.UnobservedTaskException += TaskSchedulerOnUnobservedTaskException;
#endif //#if !DEBUG
        }


        /// <summary>
        /// Extension to Task class. Damit wird eine unbehandelete Ausnahme im Task geloggt, lässt die App durch den Task aber nicht abstürtzen.
        /// </summary>
        /// <returns></returns>
        public static Task LogTaskExceptions(this Task task)
        {
            task.ContinueWith(t =>
            {
                if (t.Exception != null)
                {
                    ExceptionReporter.DoReportError(t.Exception.Flatten(), "Task: " + task.ToString());
                }
            },
            TaskContinuationOptions.OnlyOnFaulted);

            return task;
        }



        /// <summary>
        /// Verarbeitet eine unbehandelte Ausnahme
        /// </summary>
        public static void Handle_UnhandledException(Exception e)
        {
            //Fehlerbericht hochladen
            DoReportError(e);

#if HAS_UI
            //MessageBox anzeigen, wenn gewünscht.
            if (ShowMessageBox)
            {
                System.Windows.Forms.MessageBox.Show("Es ist ein unerwarteter Fehler aufgetreten.\n\nMeldung: " + e.Message + "\nQuelle: " + e.Source + "\n\nStack:\n" + e.StackTrace,
                                mAppName, 
                                System.Windows.Forms.MessageBoxButtons.OK, 
                                System.Windows.Forms.MessageBoxIcon.Stop);

                /*
                //17.04.2024 In Handle_UnhandledException wird die Anwendung immer abgebrochen 
                
                DialogResult result = MessageBox.Show("Es ist ein unerwarteter Fehler aufgetreten. Fortsetzen?\n\n" + e.Message + " in " + e.Source + "\n\n" + e.StackTrace + "\n\nAusführung fortfahren?",
                                      Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Stop,
                                      MessageBoxDefaultButton.Button1);

                //if (result == DialogResult.Yes)
                //{
                //    return;  
                //}
                */
            }
#endif

            Environment.ExitCode = e.HResult;

            //25.03.25 Ob das hilft damit bei Xamarin Android/iOS Fehlermeldungen über den AsyncTask auch gesendet werden?
#if !NET5_0_OR_GREATER
            Thread.Sleep(2500);
#endif

            //Damit der GlobalBroker nicht abschmiert
            if (ExitAppOnException)
                Environment.Exit(e.HResult);
        }

        /// <summary>
        /// Fügt ein zusätzliches Feld mit Informationen für den Fehlerbericht hinzu
        /// </summary>
        public static void SetAdditionalField(string key, string value)
        {
            if (additionalFields == null) return;
            additionalFields[key] = value;
        }

        /// <summary>
        /// Bereitet den Inhalt einer Exception, Systeminfos und Anwenderdaten in eine
        /// NameValueCollection zum versand mit Post auf
        /// </summary>
        public static void DoReportError(Exception e)
        {
#if NET5_0_OR_GREATER
            DoReportError(e, null, null);
#else
            Task.Run(() => DoReportError(e, null, null)).Wait();
#endif
        }

        public static void DoReportError(Exception e, string info1)
        {
#if NET5_0_OR_GREATER
            DoReportError(e, info1, null);
#else
            Task.Run(() => DoReportError(e, info1, null)).Wait();
#endif
        }

#if NET5_0_OR_GREATER
        public static void DoReportError(Exception e, string? info1, string? info2)
#else
        public static async void DoReportError(Exception e, string info1, string info2)
#endif
        {
            //TODO: Umlaute der nvps Felder konvertieren
            //nvps.Add("test", ("Test: äöüÄÖÜ?!$%"));

            //NameValueCollection nvps = new NameValueCollection();

            List<KeyValuePair<string, string>> items = new List<KeyValuePair<string, string>>(15);

            AddKvp(items, "app", mAppName);
            AddKvp(items, "appversion", mAppVersion);

            //App-Uptime
            try
            {
                TimeSpan appUptime = (DateTime.Now - Process.GetCurrentProcess().StartTime);
                string appUptimeText = string.Format("{0}d {1}h {2}m {3}s {4}ms", (int)appUptime.TotalDays, appUptime.Hours, appUptime.Minutes, appUptime.Seconds, appUptime.Milliseconds);

                AddKvp(items, "appuptime", appUptimeText);
            }
            catch (Exception)
            {
            }


            //additionalFields hinzufügen
            if (additionalFields != null)
            {
                foreach (string key in additionalFields)
                {
                    AddKvp(items, key, additionalFields[key]);
                }
            }


            //info 1 hinzufügen
            if (info1 != null && info1.Length > 0)
                AddKvp(items, "info1", info1);

            //info 2 hinzufügen
            if (info2 != null && info2.Length > 0)
                AddKvp(items, "info2", info2);


            if (e != null)
            {
                AddExceptionDetails(items, string.Empty, e);

                if (e.InnerException != null)
                    AddExceptionDetails(items, "in-", e.InnerException);

            } //if (e != null)         


            //AggregateException hat eine Liste mit Exceptions
            if (e is AggregateException)
            {
                AggregateException ae = (AggregateException)e;
                int n = 0;

                foreach (Exception innerEx in ae.Flatten().InnerExceptions)
                {
                    n++;
                    AddExceptionDetails(items, "agg-" + n, innerEx);

                    if (innerEx.InnerException != null)
                        AddExceptionDetails(items, "agg-" + n + "in-", innerEx.InnerException);

                } //foreach (Exception innerEx in ae.Flatten().InnerExceptions)
            } //if (e is AggregateException)


            //Systemdetails
            try
            {
                AddKvp(items, "net-version", Environment.Version.ToString());
                AddKvp(items, "os-version", Environment.OSVersion.ToString());
                AddKvp(items, "machinename", Environment.MachineName.ToString());
                AddKvp(items, "username", Environment.UserDomainName + "\\" + Environment.UserName);
                AddKvp(items, "commandline", Environment.CommandLine);
                AddKvp(items, "env-stracktrace", Environment.StackTrace);
            }
            catch (Exception) { }


            //PostForm
#if NET5_0_OR_GREATER
            string response = PostForm(URI_ERRORREPORT, items);
#else
            string response = await PostForm(URI_ERRORREPORT, items);
#endif
            Debug.WriteLine("ExceptionReporter DoReportErrror server response: '" + response + "'");
        }


#if NET5_0_OR_GREATER
        /// <summary>
        /// PostForm (synchron) von KeyValuePairs als HTTP-Form an eine Uri per HTTPClient
        /// </summary>
        private static string PostForm(string url, List<KeyValuePair<string, string>> formItems)
        {
            string responseContent = "-unset-";

            try
            {
                using (var client = new HttpClient())
                {
                    Uri uri = new Uri(url);
                    client.BaseAddress = uri;
                    client.Timeout = HTTP_TIMEOUT;

                    HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri)
                    {
                        Content = new FormUrlEncodedContent(formItems)
                    };

                    HttpResponseMessage response = client.Send(request);

                    using var reader = new StreamReader(response.Content.ReadAsStream());
                    responseContent = reader.ReadToEnd();
                }
            }
            catch (Exception e)
            {
                responseContent = "ExceptionReporter PostForm Error: " + e.ToString();
                Debug.WriteLine(responseContent);
            }

            return responseContent;
        }
#else
        /// <summary>
        /// HTTP-Post von KeyValuePairs als HTTP-Form an eine Uri per HTTPClient
        /// </summary>
        private static async Task<string> PostForm(string uri, List<KeyValuePair<string, string>> formItems)
        {
            byte[] responseBytes = null;
            string responseString = string.Empty;

            try
            {
                using (var client = new HttpClient())
                {
                    client.BaseAddress = new Uri(uri);

                    HttpContent content = new FormUrlEncodedContent(formItems);
                    HttpResponseMessage response = await client.PostAsync(uri, content);

                    responseBytes = await response.Content.ReadAsByteArrayAsync();

                    responseString = System.Text.Encoding.UTF8.GetString(responseBytes);

                    //Debug.WriteLine("ExceptionReporter DoReportErrror server response: '" + responseString + "'");

                    //string resultContent = await result.Content.ReadAsStringAsync();
                    //Console.WriteLine(resultContent);
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine("ExceptionReporter Post Error: " + e.ToString());
            }

            return responseString;
        }
#endif


#if ISNOT_USED
        /// <summary>
        /// PostForm (asynchron) von KeyValuePairs als HTTP-Form an eine Uri per HTTPClient
        /// </summary>
        private static async Task<string> PostFormAsync(string url, List<KeyValuePair<string, string>> formItems)
        {
            string responseContent = "-unset-";

            try
            {
                using (var client = new HttpClient())
                {
                    Uri uri = new Uri(url);
                    client.BaseAddress = uri;
                    client.Timeout = HTTP_TIMEOUT;

                    //Request
                    HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri)
                    {
                        Content = new FormUrlEncodedContent(formItems)
                    };

                    //Send Async
                    HttpResponseMessage response = await client.SendAsync(request);

                    //Read Result Sync
                    using var reader = new StreamReader(response.Content.ReadAsStream());
                    responseContent = reader.ReadToEnd();
                }
            }
            catch (Exception e)
            {
                responseContent = "ExceptionReporter PostFormAsync Error: " + e.ToString();
                Debug.WriteLine(responseContent);
            }

            return responseContent;
        }
#endif

#if NET5_0_OR_GREATER
        private static void AddKvp(List<KeyValuePair<string, string>> list, string key, string? value)
#else
        private static void AddKvp(List<KeyValuePair<string, string>> list, string key, string value)
#endif
        {
            list.Add(new KeyValuePair<string, string>(key, (value == null ? "null" : value)));
        }

        private static void AddExceptionDetails(List<KeyValuePair<string, string>> list, string prefix, Exception e)
        {
            if (e == null)
            {
                AddKvp(list, prefix + "exception", "null");
                return;
            }

            AddKvp(list, prefix + "message", e.Message != null ? e.Message : "null");
            AddKvp(list, prefix + "type", e.GetType() != null ? e.GetType().ToString() : "null");
            AddKvp(list, prefix + "source", e.Source != null ? e.Source : "null");
            AddKvp(list, prefix + "target", e.TargetSite != null ? e.TargetSite.ToString() : "null");
            AddKvp(list, prefix + "stacktrace", e.StackTrace != null ? e.StackTrace : "null");

            if (e.Data != null)
            {
                string dataList = string.Empty;

                foreach (DictionaryEntry dataEntry in e.Data)
                {
                    try
                    {
                        dataList += dataEntry.Key + "=" + (dataEntry.Value != null ? dataEntry.Value : "null") + "; ";
                    }
                    catch (Exception) { }
                }

                AddKvp(list, prefix + "data", dataList);

            }

        }


        #region "Default Exception Handler CallBacks from System"

        /// <summary>
        /// Zeigt eine unbehandelte Exception der Application in einer MessageBox an
        /// </summary>
        private static void Application_UnhandledThreadExceptionHandler(object sender, System.Threading.ThreadExceptionEventArgs threadexception)
        {
            Handle_UnhandledException(threadexception.Exception);
        }

        /// <summary>
        /// Zeigt eine unbehandelte Exception der aller anderen Objekte in einer MessageBox an
        /// </summary>
        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Handle_UnhandledException((Exception)e.ExceptionObject);
        }

#if NET5_0_OR_GREATER
        private static void TaskSchedulerOnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs unobservedTaskExceptionEventArgs)
#else
        private static void TaskSchedulerOnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs unobservedTaskExceptionEventArgs)
#endif
        {
            Handle_UnhandledException((Exception)unobservedTaskExceptionEventArgs.Exception);

            //Damit gibt es kein AppCrash
            unobservedTaskExceptionEventArgs.SetObserved();

        }

        #endregion
    }
}