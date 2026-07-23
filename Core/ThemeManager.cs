using System;
using System.Windows;

namespace DesktopFences.Core
{

    public static class ThemeManager
    {

        public static void ApplyTheme(ThemeSettings theme)
        {
            if (theme is null || Application.Current is null) return;

            var catalogMatch = Theming.ThemeCatalog.Get(theme.ThemeName);

            bool isKnownPreset =
                string.Equals(catalogMatch.Name, theme.ThemeName, StringComparison.OrdinalIgnoreCase);

            var definition = isKnownPreset
                ? catalogMatch
                : Theming.ThemeMigration.FromLegacy(theme, catalogMatch);

            Theming.ThemeEngine.Apply(definition, animate: true);
        }
    }
}
