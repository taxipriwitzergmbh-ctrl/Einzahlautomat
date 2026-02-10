// Auto-generated minimal resource accessor for TaMi_Einzahlautomat
namespace TaMi_Einzahlautomat.Properties {
    using System;
    using System.Drawing;
    using System.Resources;
    using System.Reflection;

    internal static class Resources {
        private static ResourceManager resourceMan;
        private static System.Globalization.CultureInfo resourceCulture;
        internal static ResourceManager ResourceManager {
            get {
                if (object.ReferenceEquals(resourceMan, null)) {
                    // BaseName must match the fully qualified resx resource name
                    resourceMan = new ResourceManager("TaMi_Einzahlautomat.Properties.Resources", typeof(Resources).Assembly);
                }
                return resourceMan;
            }
        }
        internal static System.Globalization.CultureInfo Culture {
            get { return resourceCulture; }
            set { resourceCulture = value; }
        }
        private static Image GetImage(string name) {
            object obj = ResourceManager.GetObject(name, resourceCulture);
            return (Image)obj;
        }
        private static Icon GetIcon(string name) {
            object obj = ResourceManager.GetObject(name, resourceCulture);
            return (Icon)obj;
        }
        internal static Image Hintergrund => GetImage("Hintergrund");
        internal static Image Hintergrund_Abrechnen => GetImage("Hintergrund_Abrechnen");
        internal static Image NFC => GetImage("NFC");
        internal static Image _1cent => GetImage("_1cent");
        internal static Image _2cent => GetImage("_2cent");
        internal static Image _2cent1 => GetImage("_2cent1");
        internal static Image _5cent => GetImage("_5cent");
        internal static Image _5cent1 => GetImage("_5cent1");
        internal static Image _10cent => GetImage("_10cent");
        internal static Image _20cent => GetImage("_20cent");
        internal static Image _50cent => GetImage("_50cent");
        internal static Image _1euro => GetImage("_1euro");
        internal static Image _2euro => GetImage("_2euro");
        internal static Image _5euro => GetImage("_5euro");
        internal static Image _10euro => GetImage("_10euro");
        internal static Image _20euro => GetImage("_20euro");
        internal static Image _50euro => GetImage("_50euro");
        internal static Image _100euro => GetImage("_100euro");
        internal static Icon Geldautomat_Win11 => GetIcon("Geldautomat_Win11");
    }
}
