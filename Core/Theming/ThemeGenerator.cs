using System;
using System.Windows.Media;

namespace DesktopFences.Core.Theming
{

    public static class ThemeGenerator
    {

        public const string GeneratedThemeName = "Generated from desktop";
        public const string GeneratedThemeId   = "generated-from-desktop";

        private const double AchromaticThreshold = 0.012;

        private const double PrimaryTextContrast   = 9.0;
        private const double SecondaryTextContrast = 4.8;
        private const double GlyphContrast         = 4.5;
        private const double FocusRingContrast     = 3.5;

        private const double HeaderChromaRatio        = 1.45;
        private const double AccentChromaRatio        = 6.15;
        private const double TextPrimaryChromaRatio   = 1.50;
        private const double TextSecondaryChromaRatio = 2.90;
        private const double GlyphChromaRatio         = 2.90;
        private const double FocusRingChromaRatio     = 5.50;

        public static ThemeDefinition Generate(Color seed, bool dark = true, string? name = null)
        {
            var (_, seedChroma, hue) = ColorSpace.RgbToLch(seed);

            double rawChroma = seedChroma * 0.45;
            double baseChroma = seedChroma < AchromaticThreshold
                ? 0.0
                : Math.Clamp(rawChroma, 0.022, 0.055);

            double bgL     = dark ? 0.22 : 0.96;
            double headerL = dark ? 0.30 : 0.91;

            const byte BackgroundAlpha = 0xCC;
            Color backgroundOpaque = ColorSpace.FromLch(bgL, baseChroma, hue);
            Color background = Color.FromArgb(BackgroundAlpha,
                                              backgroundOpaque.R, backgroundOpaque.G, backgroundOpaque.B);
            Color header = ColorSpace.FromLch(headerL, baseChroma * HeaderChromaRatio, hue);

            Color accent        = SolveForContrast(background, hue, baseChroma * AccentChromaRatio,        4.5,                    preferLighter: dark);
            Color textPrimary   = SolveForContrast(background, hue, baseChroma * TextPrimaryChromaRatio,   PrimaryTextContrast,    preferLighter: dark);
            Color textSecondary = SolveForContrast(background, hue, baseChroma * TextSecondaryChromaRatio, SecondaryTextContrast,  preferLighter: dark);
            Color glyph         = SolveForContrast(background, hue, baseChroma * GlyphChromaRatio,         GlyphContrast,          preferLighter: dark);
            Color focusRing     = SolveForContrast(background, hue, baseChroma * FocusRingChromaRatio,     FocusRingContrast,      preferLighter: dark);
            Color textDisabled  = ColorSpace.FromLch(dark ? bgL + 0.22 : bgL - 0.22, baseChroma * 1.2, hue);

            double statusL = dark ? 0.68 : 0.45;
            Color success = ColorSpace.FromLch(statusL, 0.13, DegreesToRadians(145));
            Color error   = ColorSpace.FromLch(statusL, 0.17, DegreesToRadians(25));
            Color warning = ColorSpace.FromLch(statusL, 0.15, DegreesToRadians(80));

            byte washAlpha = (byte)(dark ? 0x2E : 0x24);
            Color wash = dark ? Colors.White : Colors.Black;

            string Hex(Color c, byte a = 255) => ColorSpace.ToHex(c, a);
            double accentChroma = ColorSpace.RgbToLch(accent).C;

            var theme = new ThemeDefinition
            {
                Schema = 1,
                Id = "",
                Name = name ?? GeneratedThemeName,
                Author = "Generated",
                Category = "Custom",
                BaseTheme = dark ? "Dark" : "Light",
                Description = $"Built from the colours in your desktop (seed {ColorSpace.ToHex(seed)}).",
                Colors = new ThemeColors
                {
                    Primary        = Hex(accent),
                    PrimaryHover   = Hex(ColorSpace.FromLch(Lightness(accent) + (dark ? 0.06 : -0.06), accentChroma, hue)),
                    PrimaryPressed = Hex(ColorSpace.FromLch(Lightness(accent) - (dark ? 0.08 : -0.08), accentChroma * 0.92, hue)),
                    Secondary      = Hex(textSecondary),
                    Accent         = Hex(accent),

                    Background       = Hex(background, BackgroundAlpha),
                    Surface          = Hex(wash, washAlpha),
                    SurfaceSubtle    = Hex(wash, (byte)(washAlpha * 0.55)),
                    SurfaceHover     = Hex(wash, (byte)(dark ? 0x1C : 0x14)),
                    SurfaceSelected  = Hex(wash, (byte)(dark ? 0x38 : 0x28)),
                    Header           = Hex(header),

                    Border       = Hex(wash, (byte)(dark ? 0x4D : 0x59)),
                    BorderSubtle = Hex(wash, (byte)(dark ? 0x24 : 0x1E)),
                    FocusRing    = Hex(focusRing),

                    TextPrimary   = Hex(textPrimary),
                    TextSecondary = Hex(textSecondary),
                    TextDisabled  = Hex(textDisabled),
                    TextOnAccent  = Hex(BestTextOn(accent)),
                    Glyph         = Hex(glyph),
                    GlyphHover    = Hex(textPrimary),

                    Success = Hex(success),
                    Error   = Hex(error),
                    Warning = Hex(warning),
                    Info    = Hex(accent),

                    SelectionFill   = Hex(accent, 0x33),
                    SelectionStroke = Hex(accent),
                    DropTarget      = Hex(accent, 0x55),

                    ScrollThumb       = Hex(textPrimary, 0x4D),
                    ScrollThumbHover  = Hex(textPrimary, 0x8C),
                    ScrollThumbActive = Hex(textPrimary, 0xC4),
                },
                Shape = new ThemeShape
                {
                    CornerRadius = 8, CornerRadiusSmall = 4, BorderThickness = 1,
                    ShadowOpacity = 0.35, ShadowBlurRadius = 16, ShadowDepth = 2
                },
                Typography = new ThemeTypography
                {
                    FontFamily = "Segoe UI Variable Text, Segoe UI Variable, Segoe UI, Arial",
                    MonospaceFontFamily = "Cascadia Mono, Consolas, Courier New",
                    Scale = 1.0, TitleWeight = "SemiBold"
                },
                Motion = new ThemeMotion
                {
                    SpeedMultiplier = 1.0, RollUpStyle = "Smooth", ThemeTransitionMs = 180
                }
            };

            return theme;
        }

        private static Color SolveForContrast(Color background, double hue, double chroma,
                                              double target, bool preferLighter)
        {
            Color backdrop = ColorSpace.EffectiveBackdrop(background);

            foreach (double scale in new[] { 1.0, 0.8, 0.62, 0.45, 0.3, 0.18, 0.08 })
            {
                double c = chroma * scale;
                double lo = preferLighter ? ColorSpace.RgbToLch(backdrop).L : 0.0;
                double hi = preferLighter ? 1.0 : ColorSpace.RgbToLch(backdrop).L;
                Color best = ColorSpace.FromLch(preferLighter ? 1.0 : 0.0, c, hue);
                bool found = false;

                for (int i = 0; i < 32; i++)
                {
                    double mid = (lo + hi) / 2.0;
                    Color candidate = ColorSpace.FromLch(mid, c, hue);

                    if (ColorSpace.Contrast(candidate, backdrop) < target)
                    {
                        if (preferLighter) lo = mid; else hi = mid;
                    }
                    else
                    {
                        best = candidate; found = true;
                        if (preferLighter) hi = mid; else lo = mid;
                    }
                }

                if (found && ColorSpace.Contrast(best, backdrop) >= target * 0.99)
                    return best;
            }

            return ColorSpace.FromLch(preferLighter ? 0.99 : 0.02, 0.0, hue);
        }

        private static Color BestTextOn(Color fill) =>
            ColorSpace.Contrast(Colors.White, fill) >= ColorSpace.Contrast(Colors.Black, fill)
                ? Colors.White : Colors.Black;

        private static double Lightness(Color c) => ColorSpace.RgbToLch(c).L;

        private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

        public static (ThemeDefinition? Theme, WallpaperSource Source, string? Problem)
            GenerateFromDesktop(bool dark = true, bool allowScreenCapture = false, uint? monitorIndex = null,
                                System.Windows.Rect? region = null)
        {

            var sample = WallpaperSampler.CaptureMany(frames: 5, delayMs: 300,
                                                      allowScreenCapture: allowScreenCapture,
                                                      monitorIndex: monitorIndex,
                                                      region: region);
            if (sample is null)
                return (null, WallpaperSource.None,
                        "Your desktop background could not be read. Live wallpapers that render with DirectX are not always readable this way.");

            var palette = PaletteExtractor.Extract(sample.Bgra, sample.Width, sample.Height);
            if (palette.Count == 0)
                return (null, sample.Source, "No usable colours were found in your desktop background.");

            var seed = PaletteExtractor.SelectSeed(palette);
            if (seed is null)
                return (null, sample.Source, "Your desktop background has no distinct colour to build from.");

            ThemeLog.Info("Generate",
                $"source={sample.Source} seed={ColorSpace.ToHex(seed.Value)} " +
                $"top={string.Join(" | ", System.Linq.Enumerable.Take(palette, 3))}");

            return (Generate(seed.Value, dark), sample.Source, null);
        }
    }
}
