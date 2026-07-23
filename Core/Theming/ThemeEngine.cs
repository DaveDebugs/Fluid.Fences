using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace DesktopFences.Core.Theming
{

    public static class ThemeEngine
    {

        public static ThemeDefinition? Current { get; private set; }

        public static event EventHandler<ThemeDefinition>? ThemeApplied;

        private static ThemeDefinition? _prePreviewTheme;
        private static DispatcherTimer? _transitionTimer;

        private static readonly (Func<ThemeColors, string?> Get, string[] Keys)[] ColorMap =
        {

            (c => c.Primary,             new[] { "CustomPrimaryColor" }),
            (c => c.PrimaryHover,        new[] { "CustomPrimaryHoverColor" }),
            (c => c.PrimaryPressed,      new[] { "CustomPrimaryPressedColor" }),
            (c => c.Secondary,           new[] { "CustomSecondaryColor" }),
            (c => c.Accent,              new[] { "CustomAccentColor" }),

            (c => c.Background,          new[] { "CustomBackgroundColor" }),
            (c => c.Surface,             new[] { "CustomSurfaceColor" }),
            (c => c.SurfaceSubtle,       new[] { "CustomSurfaceSubtleColor" }),
            (c => c.SurfaceHover,        new[] { "CustomSurfaceHoverColor" }),
            (c => c.SurfaceSelected,     new[] { "CustomSurfaceSelectedColor" }),
            (c => c.Header,              new[] { "CustomHeaderColor" }),

            (c => c.Border,              new[] { "CustomBorderColor" }),
            (c => c.BorderSubtle,        new[] { "CustomBorderSubtleColor" }),
            (c => c.FocusRing,           new[] { "CustomFocusRingColor" }),

            (c => c.TextPrimary,         new[] { "CustomTextPrimaryColor", "CustomFontColor" }),
            (c => c.TextSecondary,       new[] { "CustomTextSecondaryColor", "CustomSecondaryFontColor" }),
            (c => c.TextDisabled,        new[] { "CustomTextDisabledColor" }),
            (c => c.TextOnAccent,        new[] { "CustomTextOnAccentColor" }),
            (c => c.Glyph,               new[] { "CustomGlyphColor" }),
            (c => c.GlyphHover,          new[] { "CustomGlyphHoverColor" }),

            (c => c.Success,             new[] { "CustomSuccessColor" }),
            (c => c.Error,               new[] { "CustomErrorColor" }),
            (c => c.Warning,             new[] { "CustomWarningColor" }),
            (c => c.Info,                new[] { "CustomInfoColor" }),

            (c => c.SelectionFill,       new[] { "CustomSelectionFillColor" }),
            (c => c.SelectionStroke,     new[] { "CustomSelectionStrokeColor" }),
            (c => c.DropTarget,          new[] { "CustomDropTargetColor" }),
            (c => c.ScrollThumb,         new[] { "CustomScrollThumbColor" }),
            (c => c.ScrollThumbHover,    new[] { "CustomScrollThumbHoverColor" }),
            (c => c.ScrollThumbActive,   new[] { "CustomScrollThumbActiveColor" }),
        };

        public static void Apply(ThemeDefinition theme, bool animate = false)
        {
            ArgumentNullException.ThrowIfNull(theme);
            if (Application.Current is null) return;

            StopTransition();

            bool canAnimate = animate
                              && Current is not null
                              && !IsReducedMotion()
                              && GetTransitionMs(theme) > 0;

            if (canAnimate)
            {
                AnimateTo(theme);
            }
            else
            {
                WriteTokens(theme);
                Commit(theme);
            }
        }

        public static void Preview(ThemeDefinition theme)
        {
            if (theme is null) return;
            _prePreviewTheme ??= Current;
            StopTransition();
            WriteTokens(theme);
        }

        public static void CancelPreview()
        {
            if (_prePreviewTheme is null) return;
            StopTransition();
            WriteTokens(_prePreviewTheme);
            Commit(_prePreviewTheme);
            _prePreviewTheme = null;
        }

        public static void CommitPreview(ThemeDefinition theme)
        {
            _prePreviewTheme = null;
            Commit(theme);
        }

        public static void ForgetPreview() => _prePreviewTheme = null;

        public static bool IsReducedMotion()
        {
            try
            {

                return !SystemParameters.ClientAreaAnimation;
            }
            catch { return false; }
        }

        public static TimeSpan ScaleDuration(double milliseconds)
        {
            if (IsReducedMotion()) return TimeSpan.Zero;
            double scale = Current?.Motion?.SpeedMultiplier ?? 1.0;
            if (scale <= 0) return TimeSpan.Zero;
            return TimeSpan.FromMilliseconds(milliseconds * scale);
        }

        private static void SetColorAndBrush(ResourceDictionary res, string colorKey, Color color)
        {
            res[colorKey] = color;

            if (!colorKey.EndsWith("Color", StringComparison.Ordinal)) return;
            string brushKey = string.Concat(colorKey.AsSpan(0, colorKey.Length - 5), "Brush");

            var brush = new SolidColorBrush(color);
            brush.Freeze();
            res[brushKey] = brush;
        }

        private static void WriteTokens(ThemeDefinition theme)
        {
            var res = Application.Current.Resources;
            var fallback = ThemeDefinition.Fallback;

            foreach (var (get, keys) in ColorMap)
            {
                string? raw = get(theme.Colors);

                if (string.IsNullOrWhiteSpace(raw)) continue;

                if (!TryParseColor(raw, out Color color)) continue;

                foreach (string key in keys) SetColorAndBrush(res, key, color);
            }

            if (theme.Shape.CornerRadius is double r)
                res["CustomCornerRadius"] = new CornerRadius(Clamp(r, 0, 32));
            if (theme.Shape.CornerRadiusSmall is double rs)
                res["CustomCornerRadiusSmall"] = new CornerRadius(Clamp(rs, 0, 32));
            if (theme.Shape.BorderThickness is double bt)
                res["CustomBorderThickness"] = Clamp(bt, 0, 8);
            if (theme.Shape.ShadowOpacity is double so)
                res["CustomShadowOpacity"] = Clamp(so, 0, 1);
            if (theme.Shape.ShadowBlurRadius is double sb)
                res["CustomShadowBlurRadius"] = Clamp(sb, 0, 64);
            if (theme.Shape.ShadowDepth is double sd)
                res["CustomShadowDepth"] = Clamp(sd, 0, 32);

            if (!string.IsNullOrWhiteSpace(theme.Typography.FontFamily))
            {
                try { res["CustomFontFamily"] = new FontFamily(theme.Typography.FontFamily); }
                catch {  }
            }
            if (!string.IsNullOrWhiteSpace(theme.Typography.MonospaceFontFamily))
            {
                try { res["CustomMonospaceFontFamily"] = new FontFamily(theme.Typography.MonospaceFontFamily); }
                catch { }
            }
            if (theme.Typography.Scale is double fs)
            {

                res["CustomFontScale"] = Clamp(fs, 0.8, 1.5);
            }

            if (theme.Motion.SpeedMultiplier is double ms)
                res["CustomMotionScale"] = Clamp(ms, 0, 3);
            if (theme.Motion.ThemeTransitionMs is int tms)
                res["CustomThemeTransitionMs"] = Math.Max(0, tms);

            foreach (var kv in theme.CustomTokens)
            {
                if (string.IsNullOrWhiteSpace(kv.Key)) continue;
                if (TryParseColor(kv.Value, out Color c)) res[kv.Key] = c;
                else res[kv.Key] = kv.Value;
            }

            ApplyBaseTheme(theme);
        }

        private static void ApplyBaseTheme(ThemeDefinition theme)
        {
            try
            {
                bool wantLight = string.Equals(theme.BaseTheme, "Light", StringComparison.OrdinalIgnoreCase);
                var target = wantLight
                    ? Wpf.Ui.Appearance.ApplicationTheme.Light
                    : Wpf.Ui.Appearance.ApplicationTheme.Dark;

                if (Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme() != target)
                {
                    Wpf.Ui.Appearance.ApplicationThemeManager.Apply(target, updateAccent: false);
                }

                if (TryParseColor(theme.Colors.Accent, out Color accent))
                {
                    Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(accent, target);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ThemeEngine] Base theme switch failed: {ex.Message}");
            }
        }

        private static void Commit(ThemeDefinition theme)
        {
            Current = theme;
            ThemeApplied?.Invoke(null, theme);
        }

        private static void AnimateTo(ThemeDefinition target)
        {
            var res = Application.Current.Resources;
            int durationMs = GetTransitionMs(target);

            var lerps = new List<(string Key, Color From, Color To)>();
            foreach (var (get, keys) in ColorMap)
            {
                string? raw = get(target.Colors);
                if (string.IsNullOrWhiteSpace(raw)) continue;
                if (!TryParseColor(raw, out Color to)) continue;

                foreach (string key in keys)
                {
                    if (res[key] is Color from && from != to)
                        lerps.Add((key, from, to));
                }
            }

            if (lerps.Count == 0)
            {
                WriteTokens(target);
                Commit(target);
                return;
            }

            var clock = Stopwatch.StartNew();
            _transitionTimer = new DispatcherTimer(DispatcherPriority.Render)
            {

                Interval = TimeSpan.FromMilliseconds(16)
            };

            _transitionTimer.Tick += (s, e) =>
            {
                double t = Math.Min(1.0, clock.Elapsed.TotalMilliseconds / durationMs);
                double eased = EaseOutCubic(t);

                foreach (var (key, from, to) in lerps)
                    SetColorAndBrush(res, key, LerpColor(from, to, eased));

                if (t >= 1.0)
                {
                    StopTransition();

                    WriteTokens(target);
                    Commit(target);
                }
            };

            _transitionTimer.Start();
        }

        private static void StopTransition()
        {
            if (_transitionTimer is null) return;
            _transitionTimer.Stop();
            _transitionTimer = null;
        }

        private static int GetTransitionMs(ThemeDefinition theme)
            => theme.Motion?.ThemeTransitionMs ?? 180;

        public static bool TryParseColor(string? raw, out Color color)
        {
            color = Colors.Transparent;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            try
            {
                object? parsed = ColorConverter.ConvertFromString(raw.Trim());
                if (parsed is Color c) { color = c; return true; }
                return false;
            }
            catch { return false; }
        }

        private static Color LerpColor(Color a, Color b, double t) => Color.FromArgb(
            (byte)Math.Round(a.A + (b.A - a.A) * t),
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t));

        private static double EaseOutCubic(double t) => 1 - Math.Pow(1 - t, 3);

        private static double Clamp(double v, double min, double max)
            => v < min ? min : v > max ? max : v;

        public static double RelativeLuminance(Color c)
        {
            static double Channel(byte v)
            {
                double s = v / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        }

        public static double ContrastRatio(Color foreground, Color background)
        {
            double l1 = RelativeLuminance(foreground);
            double l2 = RelativeLuminance(background);
            if (l1 < l2) (l1, l2) = (l2, l1);
            return (l1 + 0.05) / (l2 + 0.05);
        }

        public static IReadOnlyList<string> AuditContrast(ThemeDefinition theme)
        {
            var warnings = new List<string>();

            Color backdrop = ColorSpace.NeutralBackdrop;
            if (TryParseColor(theme.Colors.Background, out Color bg))
                backdrop = ColorSpace.EffectiveBackdrop(bg);

            void Check(string? fg, string label, double required)
            {
                if (!TryParseColor(fg, out Color c)) return;
                double ratio = ContrastRatio(c, backdrop);
                if (ratio < required)
                    warnings.Add($"{label}: {ratio:F1}:1 below the {required:F1}:1 needed for WCAG AA.");
            }

            Check(theme.Colors.TextPrimary, "Primary text", 4.5);
            Check(theme.Colors.TextSecondary, "Secondary text", 4.5);
            Check(theme.Colors.Glyph, "Header glyphs", 3.0);
            Check(theme.Colors.FocusRing, "Focus ring", 3.0);
            Check(theme.Colors.Border, "Borders", 3.0);

            return warnings;
        }
    }
}
