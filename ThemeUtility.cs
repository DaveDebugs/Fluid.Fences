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
            var sampled = Core.Theming.WallpaperSampler.AverageColor();
            if (sampled is Color c) return c;

            Core.Theming.ThemeLog.Warn("AutoMatch", "Wallpaper unreadable; auto-match falling back to black.");
            return Colors.Black;
        }
    }
}
