using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace DesktopFences.Core
{
    public static class ThemeManager
    {
        public static void ApplyTheme(ThemeSettings theme)
        {
            if (Application.Current == null) return;

            var newDict = new ResourceDictionary();

            AddColorResource(newDict, "CustomPrimaryColor", theme.PrimaryColor);
            AddColorResource(newDict, "CustomSecondaryColor", theme.SecondaryColor);
            AddColorResource(newDict, "CustomAccentColor", theme.AccentColor);
            AddColorResource(newDict, "CustomBackgroundColor", theme.BackgroundColor);
            AddColorResource(newDict, "CustomSurfaceColor", theme.SurfaceColor);
            AddColorResource(newDict, "CustomHeaderColor", theme.HeaderColor);
            AddColorResource(newDict, "CustomFontColor", theme.FontColor);
            AddColorResource(newDict, "CustomSecondaryFontColor", theme.SecondaryFontColor);
            AddColorResource(newDict, "CustomBorderColor", theme.BorderColor);
            AddColorResource(newDict, "CustomSuccessColor", theme.SuccessColor);
            AddColorResource(newDict, "CustomErrorColor", theme.ErrorColor);
            AddColorResource(newDict, "CustomWarningColor", theme.WarningColor);

            newDict.Add("CustomCornerRadius", new CornerRadius(theme.CornerRadius));

            // Swap out the old custom dictionary if it exists
            var existingDict = Application.Current.Resources.MergedDictionaries
                .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("CustomTheme.xaml"));

            if (existingDict != null)
            {
                // We keep the source URI so we can find it again
                newDict.Source = existingDict.Source;
                var index = Application.Current.Resources.MergedDictionaries.IndexOf(existingDict);
                Application.Current.Resources.MergedDictionaries[index] = newDict;
            }
            else
            {
                // Fallback if not found, just add it (though it shouldn't happen)
                Application.Current.Resources.MergedDictionaries.Add(newDict);
            }
        }

        private static void AddColorResource(ResourceDictionary dict, string key, string hexColor)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hexColor)) hexColor = "#00000000";
                
                var color = (Color)ColorConverter.ConvertFromString(hexColor);
                dict.Add(key, color);
                
                // Add the corresponding SolidColorBrush
                var brushKey = key.Replace("Color", "Brush");
                var brush = new SolidColorBrush(color);
                brush.Freeze(); // Freeze for performance
                dict.Add(brushKey, brush);
            }
            catch
            {
                // Fallback to transparent on parsing error
                dict.Add(key, Colors.Transparent);
                dict.Add(key.Replace("Color", "Brush"), Brushes.Transparent);
            }
        }
    }
}
