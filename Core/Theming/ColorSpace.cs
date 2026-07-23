using System;
using System.Windows.Media;

namespace DesktopFences.Core.Theming
{

    internal static class ColorSpace
    {

        public static double ToLinear(double srgb01) =>
            srgb01 <= 0.04045 ? srgb01 / 12.92 : Math.Pow((srgb01 + 0.055) / 1.055, 2.4);

        public static double ToSrgb(double linear) =>
            linear <= 0.0031308 ? linear * 12.92 : 1.055 * Math.Pow(Math.Max(linear, 0), 1.0 / 2.4) - 0.055;

        public static (double L, double a, double b) RgbToOklab(byte r, byte g, byte b)
        {
            double lr = ToLinear(r / 255.0), lg = ToLinear(g / 255.0), lb = ToLinear(b / 255.0);

            double l = 0.4122214708 * lr + 0.5363325363 * lg + 0.0514459929 * lb;
            double m = 0.2119034982 * lr + 0.6806995451 * lg + 0.1073969566 * lb;
            double s = 0.0883024619 * lr + 0.2817188376 * lg + 0.6299787005 * lb;

            double l_ = Math.Cbrt(l), m_ = Math.Cbrt(m), s_ = Math.Cbrt(s);

            return (0.2104542553 * l_ + 0.7936177850 * m_ - 0.0040720468 * s_,
                    1.9779984951 * l_ - 2.4285922050 * m_ + 0.4505937099 * s_,
                    0.0259040371 * l_ + 0.7827717662 * m_ - 0.8086757660 * s_);
        }

        public static Color OklabToRgb(double L, double a, double b)
        {
            double l_ = L + 0.3963377774 * a + 0.2158037573 * b;
            double m_ = L - 0.1055613458 * a - 0.0638541728 * b;
            double s_ = L - 0.0894841775 * a - 1.2914855480 * b;

            double l = l_ * l_ * l_, m = m_ * m_ * m_, s = s_ * s_ * s_;

            double lr =  4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s;
            double lg = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s;
            double lb = -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s;

            return Color.FromRgb(Clamp255(ToSrgb(lr) * 255.0),
                                 Clamp255(ToSrgb(lg) * 255.0),
                                 Clamp255(ToSrgb(lb) * 255.0));
        }

        public static Color FromLch(double L, double chroma, double hueRadians) =>
            OklabToRgb(L, chroma * Math.Cos(hueRadians), chroma * Math.Sin(hueRadians));

        public static (double L, double C, double h) RgbToLch(Color c)
        {
            var (L, a, b) = RgbToOklab(c.R, c.G, c.B);
            return (L, Math.Sqrt(a * a + b * b), Math.Atan2(b, a));
        }

        public static double RelativeLuminance(Color c) =>
            0.2126 * ToLinear(c.R / 255.0) +
            0.7152 * ToLinear(c.G / 255.0) +
            0.0722 * ToLinear(c.B / 255.0);

        public static double Contrast(Color a, Color b)
        {
            double l1 = RelativeLuminance(a), l2 = RelativeLuminance(b);
            if (l1 < l2) (l1, l2) = (l2, l1);
            return (l1 + 0.05) / (l2 + 0.05);
        }

        public static Color CompositeOver(Color over, Color under)
        {
            double a = over.A / 255.0;
            return Color.FromRgb(
                Clamp255(over.R * a + under.R * (1 - a)),
                Clamp255(over.G * a + under.G * (1 - a)),
                Clamp255(over.B * a + under.B * (1 - a)));
        }

        public static readonly Color NeutralBackdrop = Color.FromRgb(0x8A, 0x8A, 0x8A);

        public static Color EffectiveBackdrop(Color backgroundToken) =>
            backgroundToken.A >= 255
                ? backgroundToken
                : CompositeOver(backgroundToken, NeutralBackdrop);

        private static byte Clamp255(double v) =>
            (byte)Math.Round(Math.Clamp(v, 0, 255));

        public static string ToHex(Color c, byte alpha = 255) =>
            $"#{alpha:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
    }
}
