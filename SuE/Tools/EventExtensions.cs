using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;

namespace SuE.Tools
{
    /// <summary>Extension methods for EventHandler-type delegates.</summary>
    public static class EventExtensions
    {
        /// <summary>Raises the event (on the UI thread if available).</summary>
        /// <param name="multicastDelegate">The event to raise.</param>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">An EventArgs that contains the event data.</param>
        /// <returns>The return value of the event invocation or null if none.</returns>
        public static object Raise(this MulticastDelegate multicastDelegate, object sender, EventArgs eventArgs)
        {
            object retVal = null;

            MulticastDelegate threadSafeMulticastDelegate = multicastDelegate;
            if (threadSafeMulticastDelegate != null)
            {
                foreach (Delegate d in threadSafeMulticastDelegate.GetInvocationList())
                {
                    var synchronizeInvoke = d.Target as ISynchronizeInvoke;
                    
                    if (synchronizeInvoke != null && synchronizeInvoke.InvokeRequired)
                    {
                        try
                        {
                            IAsyncResult asyncResultBegin = synchronizeInvoke.BeginInvoke(d, new[] { sender, eventArgs });

                            //Hiweis, wird im EventHandler eine Exception ausgelöst. Dann wird nur diese Methode als Exception geloggt, nicht immer die ursprüngliche 
                            //Beispiel im EventHandler FormtimeLine.TaMiClient_OnVehicleManagerMessage bei e.Action == VehicleManAction.INTERNAL_JOBSCHANGED war die Suche nach einem GanntRow = null und es wurd auf diesen zugegriffen.

                            try
                            {
                                retVal = synchronizeInvoke.EndInvoke(asyncResultBegin);
                            }
                            catch (ObjectDisposedException)
                            {
                                //Debug.WriteLine("Ignore: " + ex);
                                //Ignore: Das kann passieren wenn der TaMiClient SocketThread noch ein Event feuert obwohl die Map schon beendet wird
                            }
                            catch (ThreadAbortException)
                            {
                                //ExceptionReporter.DoReportError(ex, "sender: " + (sender != null ? sender.ToString() : "null"), "eventArgs: " + (eventArgs != null ? eventArgs.ToString() : "null"));
                                //Debug.WriteLine("Ignore: " + ex);
                                //Ignore: Das kann passieren wenn der TaMiClient SocketThread beendet wird oder schon beendet ist
                            }

                            catch (Exception ex)
                            {
                                ExceptionReporter.DoReportError(ex, "sender: " + (sender != null ? sender.ToString() : "null"), "eventArgs: " + (eventArgs != null ? eventArgs.ToString() : "null"));
                                throw; // preserve stack
                            }

                        }
                        catch (ObjectDisposedException)  { Debug.WriteLine("Ignore: ObjectDisposedException"); } 
                    }
                    else
                    {
                        retVal = d.DynamicInvoke(new[] { sender, eventArgs });
                    }
                }
            }

            return retVal;
        }
    }
}
