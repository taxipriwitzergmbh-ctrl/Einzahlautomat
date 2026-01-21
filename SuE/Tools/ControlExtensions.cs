using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace SuE.Tools
{
    public static class ControlExtensions
    {

        #region System.Drawing.Point

        /// <summary>
        /// Returns the largest distance X or Y between two Points
        /// </summary>
        public static int LargestDistance(this System.Drawing.Point p1, System.Drawing.Point p2)
        {
            if (p1 == System.Drawing.Point.Empty || p2 == System.Drawing.Point.Empty)
                return -1;

            int dX = Math.Abs(p1.X - p2.X);
            int dY = Math.Abs(p1.Y - p2.Y);

            //Debug.WriteLine("dX: " + dX + " dY: " + dY);

            return dX > dY ? dX : dY;
        }

        #endregion

        #region ProgressBar

        /// <summary>
        /// Sets the progress bar value, without using Windows Aero animation
        /// </summary>
        public static void SetProgressNoAnimation(this System.Windows.Forms.ProgressBar pb, int value)
        {
            // To get around this animation, we need to move the progress bar backwards.
            if (value == pb.Maximum)
            {
                // Special case (can't set value > Maximum).
                pb.Value = value;           // Set the value
                pb.Value = value - 1;       // Move it backwards
            }
            else
            {
                pb.Value = value + 1;       // Move past
            }
            pb.Value = value;               // Move to correct value
        }

        #endregion

        #region DataGridView



        #endregion



        #region System.Window.Forms.Control

        /// <summary>
        /// Enables or disables the double Buffering of a control
        /// </summary>
        public static void SetDoubleBuffered(this Control control, bool setting)
        {
            if (control.Disposing || control.IsDisposed)
                return;

            Type controlType = control.GetType();
            PropertyInfo pi = controlType.GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
            pi.SetValue(control, setting, null);
        }

        /// <summary>
        /// Enables or disables the redrawing in WM_PAINT of a control
        /// </summary>
        [DllImport("user32.dll", EntryPoint = "SendMessageA", ExactSpelling = true, CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern int SendMessage(IntPtr hwnd, int wMsg, int wParam, int lParam);
        private const int WM_SETREDRAW = 0xB;

        public static void SetRedraw(this Control target, bool enable)
        {
            if (target.Disposing || target.IsDisposed)
                return;

            SendMessage(target.Handle, WM_SETREDRAW, (enable ? 1 : 0), 0);
            if (enable)
            {
                target.Refresh();
            }

        }

        /*
        public static void SuspendDrawing(this Control target)
        {
            SendMessage(target.Handle, WM_SETREDRAW, 0, 0);
        }

        public static void ResumeDrawing(this Control target) { ResumeDrawing(target, true); }
        public static void ResumeDrawing(this Control target, bool redraw)
        {
            SendMessage(target.Handle, WM_SETREDRAW, 1, 0);

            if (redraw)
            {
                target.Refresh();
            }
        }
        */

        #endregion


        #region "XMLNode"

        public static System.Xml.XmlNode AddNode(this System.Xml.XmlNode node, string name, string innerText)
        {
            System.Xml.XmlNode nodeNew =  node.OwnerDocument.CreateNode(System.Xml.XmlNodeType.Element, name, string.Empty);
            nodeNew.InnerText = innerText;
            node.AppendChild(nodeNew);

            return nodeNew;
        }

        #endregion



        }
}
