using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace SuE.Tools
{
    public static class FormExtensions
    {

        public static bool IsOnScreenCompletly(this Form form)
        {
            Rectangle formRectangle = new Rectangle(form.Left, form.Top, form.Width, form.Height);

            Screen[] screens = Screen.AllScreens;
            foreach (Screen screen in screens)
            {
                if (screen.WorkingArea.Contains(formRectangle))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsOnScreenPartly(this Form form)
        {
            const int offset = 8;

            Rectangle checkRectTopLeft  = new Rectangle(form.Left + offset, form.Top + offset, offset, offset);
            Rectangle checkRectTopRight = new Rectangle(form.Right - offset, form.Top + offset, offset, offset);

            Screen[] screens = Screen.AllScreens;
            foreach (Screen screen in screens)
            {
                if (screen.WorkingArea.Contains(checkRectTopLeft) || screen.WorkingArea.Contains(checkRectTopRight))
                {
                    return true;
                }
            }

            return false;
        }

        public static void ResetPositionIfNotOnScreenPartly(this Form form)
        {
            if (!form.IsOnScreenPartly())
            {
                form.SetDesktopLocation(0, 0);
            }
        }

    }
}
