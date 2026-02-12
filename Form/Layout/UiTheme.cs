using System.Drawing;

namespace TaMi_Einzahlautomat.UI.Layout
{
    internal static class UiTheme
    {
        public static readonly Color HeaderGradientStart = Color.FromArgb(13, 71, 161);
        public static readonly Color HeaderGradientEnd = Color.FromArgb(120, 200, 255);

        public static readonly Color PrimaryStart = Color.FromArgb(33, 150, 243);
        public static readonly Color PrimaryEnd = Color.FromArgb(13, 71, 161);

        public static readonly Color SecondaryStart = Color.FromArgb(96, 125, 139);
        public static readonly Color SecondaryEnd = Color.FromArgb(55, 71, 79);

        public static readonly Color SuccessStart = Color.FromArgb(46, 125, 50);
        public static readonly Color SuccessEnd = Color.FromArgb(27, 94, 32);

        public static readonly Color DangerStart = Color.FromArgb(239, 83, 80);
        public static readonly Color DangerEnd = Color.FromArgb(198, 40, 40);

        public const int HeaderHeight = 60;
        public const int HeaderCornerRadius = 24;
        public const int ButtonCornerRadius = 14;

        public static readonly Font HeaderFont = new Font("Segoe UI Variable", 18F, FontStyle.Bold);
        public static readonly Font DefaultFont = new Font("Segoe UI", 9F);
    }
}
