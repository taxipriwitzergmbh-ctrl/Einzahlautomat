using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SuE.Tools
{

    /*
     var userResult = AutoClosingMessageBox.Show("Yes or No?", "Caption", 1000, MessageBoxButtons.YesNo);
     if(userResult == System.Windows.Forms.DialogResult.Yes)
    { 
        // do something
     }
    */

    /* Async
    Task.Run(() =>
    {
        var dialogResult=  MessageBox.Show("Message", "Title", MessageBoxButtons.OKCancel);
        if (dialogResult == System.Windows.Forms.DialogResult.OK)
            MessageBox.Show("OK Clicked");
        else
            MessageBox.Show("Cancel Clicked");
    });
    */


    public class AutoClosingMessageBox
    {
        System.Threading.Timer _timeoutTimer;

        string _caption;
        DialogResult _result;
        DialogResult _timeoutResult; //ResultCode der im Fall des Timeouts zurückgegeben werden soll


        AutoClosingMessageBox(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, DialogResult timeoutResult = DialogResult.None, int timeout = 30, bool async = false)
        {
            _caption = caption;
            
            _timeoutTimer = new System.Threading.Timer(OnTimerElapsed, null, timeout * 1000, System.Threading.Timeout.Infinite);
            
            _timeoutResult = timeoutResult;

            if (async)
            {
                Task.Run(() =>
                {
                    using (_timeoutTimer)
                        _result = MessageBox.Show(text, caption, buttons, icon, defaultButton);
                });
            }
            else
            {
                using (_timeoutTimer)
                    _result = MessageBox.Show(text, caption, buttons, icon, defaultButton);
            }

        }


        public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, DialogResult timeoutResult = DialogResult.None, int timeout = 30)
        {
            return new AutoClosingMessageBox(text, caption, buttons, icon, defaultButton, timeoutResult, timeout, false)._result;
        }

        public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, DialogResult timeoutResult = DialogResult.None, int timeout = 30)
        {
            return new AutoClosingMessageBox(text, caption, buttons, icon, MessageBoxDefaultButton.Button1, timeoutResult, timeout, false)._result;
        }

        public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            return new AutoClosingMessageBox(text, caption, buttons, icon, MessageBoxDefaultButton.Button1, DialogResult.Cancel, 30, false)._result;
        }



        public static DialogResult ShowAsync(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, int timeout = 30)
        {
            return new AutoClosingMessageBox(text, caption, buttons, icon, MessageBoxDefaultButton.Button1, DialogResult.Cancel, timeout, true)._result;
        }

        public static DialogResult ShowAsync(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            return new AutoClosingMessageBox(text, caption, buttons, icon, MessageBoxDefaultButton.Button1, DialogResult.Cancel, 30, true)._result;
        }


        void OnTimerElapsed(object state)
        {
            IntPtr mbWnd = FindWindow("#32770", _caption); // lpClassName is #32770 for MessageBox
            if (mbWnd != IntPtr.Zero)
                SendMessage(mbWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            
            _timeoutTimer.Dispose();
            _result = _timeoutResult;
        }


        const int WM_CLOSE = 0x0010;

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        static extern IntPtr SendMessage(IntPtr hWnd, UInt32 Msg, IntPtr wParam, IntPtr lParam);
    }

}
