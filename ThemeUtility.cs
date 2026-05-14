using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopFences
{
    public static class ThemeUtility
    {
        public static Color GetDominantWallpaperColor()
        {
            try
            {
                System.Text.StringBuilder wallpaperPath = new(260);
                NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETDESKWALLPAPER, 260, wallpaperPath, 0);
                string path = wallpaperPath.ToString();

                if (!File.Exists(path)) return Colors.Black;

                BitmapImage bmp = new();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.DecodePixelWidth = 50;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();

                int stride = bmp.PixelWidth * 4;
                byte[] pixels = new byte[bmp.PixelHeight * stride];
                bmp.CopyPixels(pixels, stride, 0);

                long r = 0, g = 0, b = 0;
                int pixelCount = 0;

                for (int i = 0; i < pixels.Length; i += 4)
                {
                    b += pixels[i];
                    g += pixels[i + 1];
                    r += pixels[i + 2];
                    pixelCount++;
                }

                if (pixelCount == 0) return Colors.Black;

                return Color.FromRgb((byte)(r / pixelCount), (byte)(g / pixelCount), (byte)(b / pixelCount));
            }
            catch
            {
                return Colors.Black;
            }
        }
    }
}