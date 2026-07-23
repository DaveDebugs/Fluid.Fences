using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace DesktopFences.Core.Theming
{

    public static class ThemeMigration
    {
        private const string MigrationMarkerKey = "ThemeEngineSchema";

        private static readonly JsonSerializerOptions ReadOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true
        };

        public static string ResolveThemeId(GlobalConfig? config)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(config?.ThemeId))
                    return config!.ThemeId!;

                var legacy = config?.Theme;
                if (legacy is null || string.IsNullOrWhiteSpace(legacy.ThemeName))
                    return ThemeCatalog.Default.Id;

                var match = ThemeCatalog.Get(legacy.ThemeName);

                bool nameMatches = string.Equals(match.Name, legacy.ThemeName, StringComparison.OrdinalIgnoreCase);
                if (nameMatches && ColoursMatch(match, legacy))
                {
                    ThemeLog.Info("Migrate", $"'{legacy.ThemeName}' maps cleanly to built-in '{match.Id}'.");
                    return match.Id;
                }

                var forked = FromLegacy(legacy, match);
                var saved = ThemeCatalog.Save(forked);

                ThemeLog.Info("Migrate", $"'{legacy.ThemeName}' customised, forked to user theme '{saved.Id}'.");
                return saved.Id;
            }
            catch (Exception ex)
            {
                ThemeLog.Error("Migrate", ex);
                return ThemeCatalog.Default.Id;
            }
        }

        public static async Task<string> ResolveThemeIdAsync(GlobalConfig? config)
        {
            try
            {

                if (!string.IsNullOrWhiteSpace(config?.ThemeId))
                    return config!.ThemeId!;

                var legacy = config?.Theme;
                if (legacy is null || string.IsNullOrWhiteSpace(legacy.ThemeName))
                    return ThemeCatalog.Default.Id;

                var match = ThemeCatalog.Get(legacy.ThemeName);

                bool nameMatches = string.Equals(match.Name, legacy.ThemeName, StringComparison.OrdinalIgnoreCase);
                if (nameMatches && ColoursMatch(match, legacy))
                {
                    ThemeLog.Info("Migrate", $"'{legacy.ThemeName}' -> built-in '{match.Id}'.");
                    return match.Id;
                }

                var forked = FromLegacy(legacy, match);
                var saved = await ThemeCatalog.SaveAsync(forked).ConfigureAwait(false);

                ThemeLog.Info("Migrate", $"'{legacy.ThemeName}' forked to user theme '{saved.Id}'.");
                return saved.Id;
            }
            catch (Exception ex)
            {
                ThemeLog.Error("Migrate", ex);
                return ThemeCatalog.Default.Id;
            }
        }

        private static bool ColoursMatch(ThemeDefinition builtin, ThemeSettings legacy)
        {
            static bool Same(string? a, string? b)
            {
                if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return true;
                if (!ThemeEngine.TryParseColor(a, out var ca)) return false;
                if (!ThemeEngine.TryParseColor(b, out var cb)) return false;
                return ca == cb;
            }

            var c = builtin.Colors;
            return Same(c.Background,    legacy.BackgroundColor)
                && Same(c.Header,        legacy.HeaderColor)
                && Same(c.TextPrimary,   legacy.FontColor)
                && Same(c.TextSecondary, legacy.SecondaryFontColor)
                && Same(c.Accent,        legacy.AccentColor);
        }

        public static ThemeDefinition FromLegacy(ThemeSettings legacy, ThemeDefinition? seed = null)
        {
            var basis = ThemeCatalog.Clone(seed ?? ThemeCatalog.Default);

            basis.Id = "";
            basis.IsBuiltIn = false;
            basis.SourcePath = null;
            basis.Category = "Custom";
            basis.Author = "Migrated";
            basis.Description = "Carried over from your previous Fluid Fences settings.";
            basis.Name = string.IsNullOrWhiteSpace(legacy.ThemeName)
                ? "My Theme (Imported)"
                : $"{legacy.ThemeName} (Imported)";

            if (!string.IsNullOrWhiteSpace(legacy.BackgroundColor))    basis.Colors.Background    = legacy.BackgroundColor;
            if (!string.IsNullOrWhiteSpace(legacy.HeaderColor))        basis.Colors.Header        = legacy.HeaderColor;
            if (!string.IsNullOrWhiteSpace(legacy.FontColor))          basis.Colors.TextPrimary   = legacy.FontColor;
            if (!string.IsNullOrWhiteSpace(legacy.SecondaryFontColor)) basis.Colors.TextSecondary = legacy.SecondaryFontColor;
            if (!string.IsNullOrWhiteSpace(legacy.AccentColor))        basis.Colors.Accent        = legacy.AccentColor;
            if (!string.IsNullOrWhiteSpace(legacy.PrimaryColor))       basis.Colors.Primary       = legacy.PrimaryColor;
            if (!string.IsNullOrWhiteSpace(legacy.SecondaryColor))     basis.Colors.Secondary     = legacy.SecondaryColor;
            if (!string.IsNullOrWhiteSpace(legacy.SurfaceColor))       basis.Colors.Surface       = legacy.SurfaceColor;
            if (!string.IsNullOrWhiteSpace(legacy.BorderColor))        basis.Colors.Border        = legacy.BorderColor;
            if (!string.IsNullOrWhiteSpace(legacy.SuccessColor))       basis.Colors.Success       = legacy.SuccessColor;
            if (!string.IsNullOrWhiteSpace(legacy.ErrorColor))         basis.Colors.Error         = legacy.ErrorColor;
            if (!string.IsNullOrWhiteSpace(legacy.WarningColor))       basis.Colors.Warning       = legacy.WarningColor;

            if (!string.IsNullOrWhiteSpace(legacy.AccentColor)
                && ThemeEngine.TryParseColor(legacy.AccentColor, out var accent))
            {
                string Hex(byte a) => $"#{a:X2}{accent.R:X2}{accent.G:X2}{accent.B:X2}";
                basis.Colors.SelectionStroke = Hex(0xFF);
                basis.Colors.SelectionFill   = Hex(0x33);
                basis.Colors.DropTarget      = Hex(0x55);
            }

            basis.Shape.CornerRadius = legacy.CornerRadius > 0 ? legacy.CornerRadius : 8;
            basis.Motion.RollUpStyle = legacy.RollUpAnimation.ToString();

            return basis;
        }

        public static void BackupConfigOnce(string configPath)
        {
            try
            {
                if (!File.Exists(configPath)) return;

                string backup = configPath + ".pre-theme-engine.bak";
                if (File.Exists(backup)) return;

                File.Copy(configPath, backup, overwrite: false);
                Debug.WriteLine($"[ThemeMigration] Backed up config to {backup}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ThemeMigration] Backup failed (non-fatal): {ex.Message}");
            }
        }

        public static GlobalConfig? TryReadConfig(string configPath)
        {
            try
            {
                if (!File.Exists(configPath))
                {
                    ThemeLog.Info("ReadConfig", $"No config at {configPath}; using defaults.");
                    return null;
                }

                var parsed = JsonSerializer.Deserialize<GlobalConfig>(File.ReadAllText(configPath), ReadOptions);

                if (parsed is null)
                    ThemeLog.Warn("ReadConfig", "Deserializer returned null for a non-empty config file.");

                return parsed;
            }
            catch (Exception ex)
            {
                ThemeLog.Error("ReadConfig", ex);
                return null;
            }
        }

        public static async Task<GlobalConfig?> TryReadConfigAsync(string configPath)
        {
            try
            {
                if (!File.Exists(configPath))
                {
                    ThemeLog.Info("ReadConfig", $"No config at {configPath}; using defaults.");
                    return null;
                }

                string json = await File.ReadAllTextAsync(configPath).ConfigureAwait(false);
                var parsed = JsonSerializer.Deserialize<GlobalConfig>(json, ReadOptions);

                if (parsed is null)
                    ThemeLog.Warn("ReadConfig", "Deserializer returned null for a non-empty config file.");

                return parsed;
            }
            catch (Exception ex)
            {

                ThemeLog.Error("ReadConfig", ex);
                return null;
            }
        }
    }
}
