using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DesktopFences
{
    public partial class SettingsWindow : Wpf.Ui.Controls.FluentWindow
    {
        private readonly MainWindow? _callingFence;
        private MainWindow? _currentlySelectedFence;
        private bool _isLoadingFenceData;
        private bool _isColorInitializing;

        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        private readonly System.Windows.Threading.DispatcherTimer _liveUpdateTimer = new();
        private Color _pendingColor;
        private double _pendingOpacity;
        private bool _hasPendingColor;

        public SettingsWindow(MainWindow? callingFence, bool isFirstRun = false)
        {
            InitializeComponent();
            _callingFence = callingFence;

            PresetDropdown.SelectedIndex = 0;

            BuildNavigation();
            BuildHowTo();
            LoadThemeCatalog();
            LoadAllFences();
            RefreshStats();
            SwitchToTab("AboutGrid");

            _statusTimer.Tick += (_, _) => HideStatus();

            Core.Theming.ThemeEngine.ThemeApplied += OnThemeApplied;

            StartHomeLogoAnimation();
            WireVersionEasterEgg();

            if (isFirstRun)
            {

                AboutDescriptionText.Text =
                    "Welcome. Right-click the Fluid Fences icon in your system tray to create your first fence, " +
                    "then drag anything you like into it. Ctrl+Alt+Z hides them all when you need a clean desktop.";
                SwitchToTab("HowToGrid");
                ShowStatus("New here? These are the basics.", StatusKind.Info);
            }

            _ = LoadGlobalSettingsAsync();

            _liveUpdateTimer.Interval = TimeSpan.FromMilliseconds(33);
            _liveUpdateTimer.Tick += (s, e) =>
            {
                if (_hasPendingColor && _currentlySelectedFence is not null)
                {
                    _currentlySelectedFence.SetFenceColor(_pendingColor, _pendingOpacity);
                    _hasPendingColor = false;
                }
                else
                {
                    _liveUpdateTimer.Stop();
                }
            };
        }

        private void LoadThemeCatalog(string? filter = null)
        {
            try
            {
                if (Core.Theming.ThemeCatalog.Themes.Count == 0)
                    Core.Theming.ThemeCatalog.Reload();

                IEnumerable<Core.Theming.ThemeDefinition> items = Core.Theming.ThemeCatalog.Themes;

                if (!string.IsNullOrWhiteSpace(filter))
                {
                    string f = filter.Trim();
                    items = items.Where(t =>
                        t.Name.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                        t.Category.Contains(f, StringComparison.OrdinalIgnoreCase));
                }

                ThemeGallery.ItemsSource = items
                    .OrderBy(t => t.Category, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                Dispatcher.BeginInvoke(new Action(HighlightActiveSwatch),
                    System.Windows.Threading.DispatcherPriority.Loaded);
                UpdateContrastReadout();
            }
            catch (Exception ex) { LogError("LoadThemeCatalog", ex); }
        }

        private void HighlightActiveSwatch()
        {
            try
            {
                foreach (var item in ThemeGallery.Items)
                {

                    if (ThemeGallery.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem container)
                        continue;

                    container.ApplyTemplate();
                    if (FindDescendant<Border>(container, b => b.Tag is string) is not Border border) continue;

                    bool active = item is Core.Theming.ThemeDefinition t &&
                                  string.Equals(t.Id, App.ActiveThemeId, StringComparison.OrdinalIgnoreCase);

                    border.BorderBrush = active
                        ? (Brush)(TryFindResource("SystemAccentColorPrimaryBrush") ?? Brushes.DodgerBlue)
                        : Brushes.Transparent;
                }
            }
            catch (Exception ex) { LogError("HighlightActiveSwatch", ex); }
        }

        private static T? FindDescendant<T>(DependencyObject root, Func<T, bool>? match = null)
            where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T typed && (match is null || match(typed))) return typed;

                var deeper = FindDescendant(child, match);
                if (deeper is not null) return deeper;
            }
            return null;
        }

        private void ThemeSwatch_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not string id) return;
            ApplyThemeById(id);
        }

        private async void GenerateTheme_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool dark = !string.Equals(
                    Core.Theming.ThemeCatalog.Get(App.ActiveThemeId).BaseTheme, "Light",
                    StringComparison.OrdinalIgnoreCase);

                var ourWindows = Application.Current.Windows.OfType<Window>()
                                    .Where(w => w.Visibility == Visibility.Visible).ToList();

                ShowStatus("Reading your desktop\u2026", StatusKind.Info);
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);

                foreach (var w in ourWindows) w.Visibility = Visibility.Hidden;

                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                await System.Threading.Tasks.Task.Delay(220);

                (Core.Theming.ThemeDefinition? generated, Core.Theming.WallpaperSource source, string? problem) result;
                try
                {

                    uint? monitor = ResolveCurrentMonitorIndex();
                    Rect? region = ResolveCurrentMonitorBounds();

                    result = await System.Threading.Tasks.Task.Run(
                        () => Core.Theming.ThemeGenerator.GenerateFromDesktop(
                                  dark, allowScreenCapture: true, monitorIndex: monitor, region: region));
                }
                finally
                {
                    foreach (var w in ourWindows) w.Visibility = Visibility.Visible;
                    Activate();
                }

                var (generated, source, problem) = result;

                if (generated is null)
                {
                    ShowStatus(problem ?? "Could not read your desktop background.", StatusKind.Warning);
                    return;
                }

                generated.Name = Core.Theming.ThemeGenerator.GeneratedThemeName;

                var saved = await Core.Theming.ThemeCatalog.SaveAsync(
                    generated,
                    overwriteId: Core.Theming.ThemeGenerator.GeneratedThemeId);

                string? loop = ResolveThemeLoop(saved);
                App.SetTheme(saved.Id, animate: true, mediaPath: loop,
                             mediaOpacity: MediaOpacitySlider.Value / 100.0);
                ApplyMediaToOpenFences(saved, loop, MediaOpacitySlider.Value / 100.0);

                LoadThemeCatalog(ThemeFilter.Text);
                RefreshStats();

                string how = source switch
                {
                    Core.Theming.WallpaperSource.StaticFile   => "from your wallpaper image",
                    Core.Theming.WallpaperSource.DesktopLayer => "from your live desktop",
                    _ => "from your desktop"
                };

                var warnings = Core.Theming.ThemeEngine.AuditContrast(saved);
                string quality = warnings.Count == 0
                    ? "Meets WCAG AA throughout."
                    : $"{warnings.Count} token(s) below WCAG AA.";

                ShowStatus($"Theme generated {how}. {quality}", StatusKind.Success);
            }
            catch (Exception ex)
            {
                LogError("GenerateTheme", ex);
                ShowStatus("Theme generation failed. See app_error.log for details.", StatusKind.Error);
            }
        }

        private void ThemeSwatch_RightClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not string id) return;
            ShowSwatchMenu(Core.Theming.ThemeCatalog.Get(id), fe);
            e.Handled = true;
        }

        private void ShowSwatchMenu(Core.Theming.ThemeDefinition theme, FrameworkElement anchor)
        {
            var menu = new ContextMenu();

            var apply = new MenuItem { Header = "Apply" };
            apply.Click += (_, _) => ApplyThemeById(theme.Id);
            menu.Items.Add(apply);

            var export = new MenuItem { Header = "Export\u2026" };
            export.Click += (_, _) => ExportTheme_Click(this, new RoutedEventArgs());
            menu.Items.Add(export);

            menu.Items.Add(new Separator());

            if (theme.IsBuiltIn)
            {
                menu.Items.Add(new MenuItem { Header = "Built-in theme", IsEnabled = false });
            }
            else
            {
                var delete = new MenuItem { Header = $"Delete \u201c{theme.Name}\u201d" };
                delete.SetResourceReference(MenuItem.ForegroundProperty, "CustomErrorBrush");
                delete.Click += (_, _) => DeleteTheme(theme);
                menu.Items.Add(delete);
            }

            menu.PlacementTarget = anchor;
            menu.IsOpen = true;
        }

        private void DeleteTheme(Core.Theming.ThemeDefinition theme)
        {
            if (theme.IsBuiltIn) return;

            var snapshot = Core.Theming.ThemeCatalog.Clone(theme);
            bool wasActive = string.Equals(theme.Id, App.ActiveThemeId, StringComparison.OrdinalIgnoreCase);

            if (wasActive)
                App.SetTheme(Core.Theming.ThemeCatalog.Default.Id, animate: true);

            if (!Core.Theming.ThemeCatalog.Delete(theme))
            {
                ShowStatus($"Could not delete \u201c{theme.Name}\u201d.", StatusKind.Error);
                return;
            }

            LoadThemeCatalog(ThemeFilter.Text);
            RefreshStats();

            ShowStatus($"Deleted \u201c{theme.Name}\u201d.", StatusKind.Success, undo: () =>
            {
                var restored = Core.Theming.ThemeCatalog.Save(snapshot, overwriteId: snapshot.Id);
                if (wasActive) App.SetTheme(restored.Id, animate: true);
                LoadThemeCatalog(ThemeFilter.Text);
                RefreshStats();
                ShowStatus($"Restored \u201c{restored.Name}\u201d.", StatusKind.Success);
            });
        }

        private void ApplyThemeById(string id)
        {
            var theme = Core.Theming.ThemeCatalog.Get(id);
            string? loop = ResolveThemeLoop(theme);

            if (loop is not null)
            {
                MediaFilePathInput.Text = loop;
                if (MediaOpacitySlider.Value <= 0) MediaOpacitySlider.Value = 85;
            }

            App.SetTheme(theme.Id, animate: true, mediaPath: loop,
                         mediaOpacity: MediaOpacitySlider.Value / 100.0);
            ApplyMediaToOpenFences(theme, loop, MediaOpacitySlider.Value / 100.0);

            HighlightActiveSwatch();
            UpdateContrastReadout();
            RefreshStats();
            UpdateColorSourceCard();
            ShowStatus($"{theme.Name} applied.", StatusKind.Success);
        }

        private void OnThemeApplied(object? sender, Core.Theming.ThemeDefinition theme)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnThemeApplied(sender, theme)));
                return;
            }

            try
            {
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(this);
                HighlightActiveSwatch();

            }
            catch (Exception ex) { LogError("OnThemeApplied", ex); }
        }

        private void ThemeGallery_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter && e.Key != Key.Space) return;
            if (ThemeGallery.SelectedItem is not Core.Theming.ThemeDefinition theme) return;

            ApplyThemeById(theme.Id);
            e.Handled = true;
        }

        private void ThemeGallery_ContextMenuKey(object sender, KeyEventArgs e)
        {
            bool menuKey = e.Key == Key.Apps ||
                           (e.Key == Key.F10 && (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift);
            if (!menuKey) return;
            if (ThemeGallery.SelectedItem is not Core.Theming.ThemeDefinition theme) return;

            ShowSwatchMenu(theme, ThemeGallery);
            e.Handled = true;
        }

        private void ThemeFilter_TextChanged(object sender, TextChangedEventArgs e)
            => LoadThemeCatalog(ThemeFilter.Text);

        private void UpdateContrastReadout()
        {
            try
            {

                var theme = Core.Theming.ThemeCatalog.Get(App.ActiveThemeId);
                var warnings = Core.Theming.ThemeEngine.AuditContrast(theme);

                if (warnings.Count > 0)
                {
                    Core.Theming.ThemeLog.Warn("Contrast",
                        $"{theme.Name}: {warnings.Count} token(s) below WCAG AA -> {string.Join("; ", warnings)}");
                }
            }
            catch (Exception ex) { LogError("UpdateContrastReadout", ex); }
        }

        private async void ImportTheme_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import theme",
                Filter = "Fluid Fences themes (*.fftheme;*.json)|*.fftheme;*.json|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            var imported = await Core.Theming.ThemeCatalog.ImportAsync(dlg.FileName);
            if (imported is null)
            {
                ShowStatus("That file could not be read as a theme.", StatusKind.Error);
                return;
            }

            LoadThemeCatalog(ThemeFilter.Text);
            ShowStatus($"Imported '{imported.Name}'.", StatusKind.Success);
        }

        private async void ExportTheme_Click(object sender, RoutedEventArgs e)
        {
            var theme = Core.Theming.ThemeCatalog.Get(App.ActiveThemeId);
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export theme",
                FileName = theme.Id + Core.Theming.ThemeCatalog.ThemePackageExtension,
                Filter = "Fluid Fences theme (*.fftheme)|*.fftheme"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                string? media = System.IO.File.Exists(MediaFilePathInput.Text) ? MediaFilePathInput.Text : null;
                await Core.Theming.ThemeCatalog.ExportAsync(theme, dlg.FileName, media);
                ShowStatus($"Exported '{theme.Name}'.", StatusKind.Success);
            }
            catch (Exception ex)
            {
                LogError("ExportTheme", ex);
                ShowStatus("Export failed. See app_error.log for details.", StatusKind.Error);
            }
        }

        private static void LogError(string context, Exception ex)
        {
            try
            {
                string logPath = Path.Combine(App.ConfigFolder, "error.log");
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Settings_{context}: {ex.Message}\n");
            }
            catch { }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveCurrentFenceSettings();

            _statusTimer.Stop();
            _liveUpdateTimer.Stop();

            Core.Theming.ThemeEngine.ThemeApplied -= OnThemeApplied;

        }

        private static void RgbToHsl(Color rgb, out double h, out double s, out double l)
        {
            double r = rgb.R / 255.0; double g = rgb.G / 255.0; double b = rgb.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)); double min = Math.Min(r, Math.Min(g, b));
            l = (max + min) / 2.0;

            if (max == min) { h = s = 0; }
            else
            {
                double d = max - min;
                s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
                if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
                else if (max == g) h = (b - r) / d + 2;
                else h = (r - g) / d + 4;
                h /= 6;
            }
            h *= 360; s *= 100; l *= 100;
        }

        private static Color HslToRgb(double h, double s, double l)
        {
            double r, g, b;
            if (s == 0) { r = g = b = l; }
            else
            {
                static double Hue2Rgb(double p, double q, double t) { if (t < 0) t += 1; if (t > 1) t -= 1; if (t < 1 / 6.0) return p + (q - p) * 6 * t; if (t < 1 / 2.0) return q; if (t < 2 / 3.0) return p + (q - p) * (2 / 3.0 - t) * 6; return p; }
                double q = l < 0.5 ? l * (1 + s) : l + s - l * s; double p = 2 * l - q; h /= 360;
                r = Hue2Rgb(p, q, h + 1 / 3.0); g = Hue2Rgb(p, q, h); b = Hue2Rgb(p, q, h - 1 / 3.0);
            }
            return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }

        private async System.Threading.Tasks.Task LoadGlobalSettingsAsync()
        {
            string globalConfigPath = Path.Combine(App.ConfigFolder, "global_config.json");
            if (File.Exists(globalConfigPath))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(globalConfigPath);
                    if (JsonSerializer.Deserialize<GlobalConfig>(json) is GlobalConfig config)
                    {

                        TaskbarToggle.IsChecked = config.ShowTaskbarIcon;
                        RestoreFilesToggle.IsChecked = config.RestoreFilesOnDelete;
                        GhostModeToggle.IsChecked = config.EnableGhostMode;
                        PauseMediaToggle.IsChecked = config.PauseMediaOnRollup;

                        if (config.Theme != null)
                        {
                            MediaFilePathInput.Text = config.Theme.BackgroundMediaPath;
                            MediaOpacitySlider.Value = config.Theme.MediaOpacity * 100.0;
                            AnimationDropdown.SelectedIndex = (int)config.Theme.RollUpAnimation;

                            var active = Core.Theming.ThemeCatalog.Get(
                                !string.IsNullOrWhiteSpace(config.ThemeId) ? config.ThemeId : config.Theme.ThemeName);

                            _ = active;
                        }
                    }
                }
                catch (Exception ex) { LogError("LoadConfig", ex); }
            }

            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", false);
                StartupToggle.IsChecked = key?.GetValue("FluidFences") is not null;
            }
            catch (Exception ex) { LogError("ReadRegistry", ex); }

            ArmAutoSave();
        }

        private async void SaveGlobalSettings_Click(object sender, RoutedEventArgs e)
        {
            string globalConfigPath = Path.Combine(App.ConfigFolder, "global_config.json");
            bool showTaskbar = TaskbarToggle.IsChecked ?? true;
            bool restoreFiles = RestoreFilesToggle.IsChecked ?? true;
            bool ghostMode = GhostModeToggle.IsChecked ?? false;
            bool pauseMedia = PauseMediaToggle.IsChecked ?? true;

            Core.ThemeSettings newTheme = new()
            {
                ThemeName = Core.Theming.ThemeCatalog.Get(App.ActiveThemeId).Name,
                RollUpAnimation = (Core.AnimationStyle)AnimationDropdown.SelectedIndex,
                BackgroundMediaPath = MediaFilePathInput.Text,
                MediaOpacity = MediaOpacitySlider.Value / 100.0
            };

            var selectedTheme = Core.Theming.ThemeCatalog.Get(App.ActiveThemeId);

            newTheme.ThemeName = selectedTheme.Name;
            newTheme.BackgroundColor    = selectedTheme.Colors.Background    ?? newTheme.BackgroundColor;
            newTheme.HeaderColor        = selectedTheme.Colors.Header        ?? newTheme.HeaderColor;
            newTheme.FontColor          = selectedTheme.Colors.TextPrimary   ?? newTheme.FontColor;
            newTheme.SecondaryFontColor = selectedTheme.Colors.TextSecondary ?? newTheme.SecondaryFontColor;
            newTheme.AccentColor        = selectedTheme.Colors.Accent        ?? newTheme.AccentColor;

            await ConfigStore.UpdateAsync(c =>
            {
                c.FirstRunComplete     = true;
                c.ShowTaskbarIcon      = showTaskbar;
                c.RestoreFilesOnDelete = restoreFiles;
                c.EnableGhostMode      = ghostMode;
                c.PauseMediaOnRollup   = pauseMedia;
                c.ThemeId              = selectedTheme.Id;
                c.Theme                = newTheme;
            });
            App.PauseMediaOnRollup = pauseMedia;

            foreach (Window window in Application.Current.Windows)
            {
                if (window is MainWindow fence)
                {
                    fence.ShowInTaskbar = showTaskbar;
                    fence.UpdateGlobalGhostMode(ghostMode);

                    fence.ApplyTheme(newTheme);
                }
            }

            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                using RegistryKey? serializeKey = Registry.CurrentUser.CreateSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Serialize");

                if (StartupToggle.IsChecked == true)
                {
                    string exePath = Environment.ProcessPath!;
                    key?.SetValue("FluidFences", $"\"{exePath}\"");
                    serializeKey?.SetValue("StartupDelayInMS", 0, RegistryValueKind.DWord);
                }
                else
                {
                    key?.DeleteValue("FluidFences", false);
                    serializeKey?.DeleteValue("StartupDelayInMS", false);
                }
            }
            catch (Exception ex)
            {
                LogError("WriteRegistry", ex);
                ShowStatus("Could not change the Windows startup setting.", StatusKind.Warning);
            }

        }

        private void SaveLayout_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current is not App myApp) return;
            myApp.SaveCurrentLayout();
            ShowStatus("Layout snapshot saved.", StatusKind.Success);
        }
        private void RestoreLayout_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current is not App myApp) return;
            myApp.RestoreLayoutSnapshot();
            ShowStatus("Fences returned to the saved layout.", StatusKind.Success);
        }

        private void LoadAllFences()
        {
            FenceListBox.Items.Clear();

            foreach (Window window in Application.Current.Windows)
            {
                if (window is MainWindow { Visibility: Visibility.Visible } fence)
                {
                    ListBoxItem item = new() { Content = fence.FenceTitle, Tag = fence };
                    FenceListBox.Items.Add(item);

                    if (fence == _callingFence || (_callingFence is null && FenceListBox.SelectedItem is null))
                        FenceListBox.SelectedItem = item;
                }
            }

            bool any = FenceListBox.Items.Count > 0;
            NoFencesHint.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
            FenceDetailPanel.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            RefreshStats();
        }

        private void FenceListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ListBox { SelectedItem: ListBoxItem { Tag: MainWindow fence } } sourceBox)
            {

                _currentlySelectedFence = fence; _isLoadingFenceData = true;
                TitleInput.Text = fence.FenceTitle; AutoSortInput.Text = fence.AutoSortExtensions;
                EnableSearchToggle.IsChecked = fence.ShowSearch; PresetDropdown.SelectedIndex = 0;
                GhostModeDropdown.SelectedIndex = fence.GhostModeOverride;

                SortDropdown.SelectedItem = SortDropdown.Items
                    .OfType<ComboBoxItem>()
                    .FirstOrDefault(i => string.Equals(i.Tag as string, fence.FenceSortMethod,
                                                       StringComparison.OrdinalIgnoreCase))
                    ?? SortDropdown.Items.OfType<ComboBoxItem>().FirstOrDefault();

                UpdateColorSlidersFromFence(fence);
                UpdateColorSourceCard();
                _isLoadingFenceData = false;
            }
        }

        private void UpdateColorSlidersFromFence(MainWindow fence)
        {
            _isColorInitializing = true;
            Color startColor = fence.FenceColor; AlphaSlider.Value = fence.FenceOpacity * 100.0;
            if (BlendWallpaperToggle is not null) BlendWallpaperToggle.IsChecked = (AlphaSlider.Value <= 1.0);
            if (AutoMatchToggle is not null) AutoMatchToggle.IsChecked = fence.AutoMatchColor;

            bool isAuto = fence.AutoMatchColor; HueSlider.IsEnabled = !isAuto; SaturationSlider.IsEnabled = !isAuto; LightnessSlider.IsEnabled = !isAuto;
            double opacity = isAuto ? 0.4 : 1.0; HueSlider.Opacity = opacity; SaturationSlider.Opacity = opacity; LightnessSlider.Opacity = opacity;

            System.Drawing.Color sysColor = System.Drawing.Color.FromArgb(startColor.A, startColor.R, startColor.G, startColor.B);
            HueSlider.Value = sysColor.GetHue(); SaturationSlider.Value = sysColor.GetSaturation() * 100.0; LightnessSlider.Value = sysColor.GetBrightness() * 100.0;
            UpdateColorPreview(); _isColorInitializing = false;
        }

        private void AutoMatchToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_isColorInitializing || _currentlySelectedFence is null) return;

            bool isAuto = AutoMatchToggle.IsChecked == true;
            _currentlySelectedFence.AutoMatchColor = isAuto;

            _currentlySelectedFence.ColorSource = isAuto
                ? FenceColorSource.AutoMatch
                : FenceColorSource.Theme;

            HueSlider.IsEnabled = !isAuto; SaturationSlider.IsEnabled = !isAuto; LightnessSlider.IsEnabled = !isAuto;
            double opacity = isAuto ? 0.4 : 1.0; HueSlider.Opacity = opacity; SaturationSlider.Opacity = opacity;

            UpdateColorSlidersFromFence(_currentlySelectedFence);
            UpdateColorSourceCard();
            _currentlySelectedFence.DashboardSaveAndRefresh();
        }

        private void UseThemeColor_Click(object sender, RoutedEventArgs e)
        {
            if (_currentlySelectedFence is null) return;

            _currentlySelectedFence.UseThemeColor();

            _isColorInitializing = true;
            AutoMatchToggle.IsChecked = false;
            HueSlider.IsEnabled = SaturationSlider.IsEnabled = LightnessSlider.IsEnabled = true;
            HueSlider.Opacity = SaturationSlider.Opacity = 1.0;
            _isColorInitializing = false;

            UpdateColorSlidersFromFence(_currentlySelectedFence);
            UpdateColorSourceCard();
            ShowStatus("This fence follows the theme again.", StatusKind.Success);
        }

        private void UpdateColorSourceCard()
        {
            if (_currentlySelectedFence is null) return;

            switch (_currentlySelectedFence.ColorSource)
            {
                case FenceColorSource.Custom:
                    ColorSourceText.Text = "Colour is set on this fence";
                    ColorSourceHint.Text = "Themes will not change it while a custom colour is set.";
                    UseThemeColorBtn.Visibility = Visibility.Visible;
                    break;

                case FenceColorSource.AutoMatch:
                    ColorSourceText.Text = "Colour is matched to your wallpaper";
                    ColorSourceHint.Text = "Tracks the wallpaper's average colour and ignores the theme.";
                    UseThemeColorBtn.Visibility = Visibility.Visible;
                    break;

                default:
                    ColorSourceText.Text = "Colour is coming from the theme";
                    ColorSourceHint.Text = "Switching themes will restyle this fence.";
                    UseThemeColorBtn.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        private void ColorSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isColorInitializing || _currentlySelectedFence is null) return;
            if (sender == AlphaSlider && BlendWallpaperToggle is not null) BlendWallpaperToggle.IsChecked = (AlphaSlider.Value <= 1.0);
            UpdateColorPreview();
        }

        private void BlendWallpaperToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_isColorInitializing || _currentlySelectedFence is null) return;
            if (BlendWallpaperToggle.IsChecked == true) AlphaSlider.Value = 1; else AlphaSlider.Value = 70;
        }

        private void UpdateColorPreview()
        {
            double h = HueSlider.Value; double s = SaturationSlider.Value / 100.0; double l = LightnessSlider.Value / 100.0; double a = AlphaSlider.Value / 100.0;
            Color rgbColor = HslToRgb(h, s, l); Color finalColor = Color.FromArgb((byte)(a * 255), rgbColor.R, rgbColor.G, rgbColor.B);
            ColorPreview.Background = new SolidColorBrush(finalColor) { Opacity = a };

            Color pureHue = HslToRgb(h, 1.0, 0.5);
            BrightnessEndColor.Color = pureHue;
            AlphaEndColor.Color = finalColor with { A = 255 };

            SaturationStartColor.Color = HslToRgb(h, 0.0, l);
            SaturationEndColor.Color = HslToRgb(h, 1.0, l);

            if (!_isColorInitializing && _currentlySelectedFence is not null)
            {
                _pendingColor = finalColor; _pendingOpacity = a; _hasPendingColor = true;
                if (!_liveUpdateTimer.IsEnabled) _liveUpdateTimer.Start();
            }
        }

        private void PresetDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingFenceData || PresetDropdown.SelectedItem is null || AutoSortInput is null) return;
            string selection = ((ComboBoxItem)PresetDropdown.SelectedItem).Content.ToString() ?? "";
            switch (selection)
            {
                case "Images & Photos": AutoSortInput.Text = ".jpg, .jpeg, .png, .gif, .bmp, .webp, .svg, .ico, .tif"; break;
                case "Documents": AutoSortInput.Text = ".doc, .docx, .pdf, .txt, .rtf, .odt, .xls, .xlsx, .csv, .ppt, .pptx"; break;
                case "Audio & Music": AutoSortInput.Text = ".mp3, .wav, .flac, .ogg, .m4a, .wma"; break;
                case "Videos": AutoSortInput.Text = ".mp4, .mkv, .avi, .mov, .wmv, .webm, .m4v"; break;
                case "Archives (.zip)": AutoSortInput.Text = ".zip, .rar, .7z, .tar, .gz"; break;
                case "Apps & Shortcuts": AutoSortInput.Text = ".lnk, .exe, .url, .bat, .msi"; break;
            }
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentFenceSettings();
        }

        private void SaveCurrentFenceSettings()
        {
            if (_currentlySelectedFence is not null && !_isLoadingFenceData)
            {
                _currentlySelectedFence.FenceTitle = TitleInput.Text; _currentlySelectedFence.AutoSortExtensions = AutoSortInput.Text;
                _currentlySelectedFence.ShowSearch = EnableSearchToggle.IsChecked ?? true; _currentlySelectedFence.UpdateSearchVisibility();
                _currentlySelectedFence.GhostModeOverride = GhostModeDropdown.SelectedIndex;

                if (SortDropdown.SelectedItem is ComboBoxItem selectedSort && selectedSort.Tag is string sortKey)
                    _currentlySelectedFence.FenceSortMethod = sortKey;

                _currentlySelectedFence.DashboardSaveAndRefresh();

                if (FenceListBox.SelectedItem is ListBoxItem item1) { item1.Content = TitleInput.Text; }
            }
        }

        private void DeleteFenceBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentlySelectedFence is null) return;

            var fence = _currentlySelectedFence;
            string title = fence.FenceTitle;
            string id = fence.GetFenceId();

            fence.DashboardDelete();
            LoadAllFences();

            ShowStatus($"Deleted \u201c{title}\u201d.", StatusKind.Success, undo: () =>
            {
                var restored = new MainWindow(id);
                restored.Show();
                LoadAllFences();
                ShowStatus($"Restored \u201c{title}\u201d.", StatusKind.Success);
            });
        }

        private void VenmoQr_Click(object sender, RoutedEventArgs e)
        {
            const string venmoUrl =
                "https://www.paypal.com/qrcodes/venmocs/a0e66d13-f15f-4fbd-ba26-34008d486c61?created=1775532880";
            try
            {
                Process.Start(new ProcessStartInfo(venmoUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                LogError("VenmoQr_Click", ex);
                ShowStatus("Could not open your browser.", StatusKind.Error);
            }
        }

        private void BuyMeACoffee_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://buymeacoffee.com/davedebugs") { UseShellExecute = true });
        }

        private static void ApplyMediaToOpenFences(Core.Theming.ThemeDefinition theme, string? mediaPath, double opacity)
        {
            try
            {
                var legacy = new Core.ThemeSettings
                {
                    ThemeName           = theme.Name,
                    BackgroundColor     = theme.Colors.Background    ?? "",
                    HeaderColor         = theme.Colors.Header        ?? "",
                    FontColor           = theme.Colors.TextPrimary   ?? "",
                    SecondaryFontColor  = theme.Colors.TextSecondary ?? "",
                    AccentColor         = theme.Colors.Accent        ?? "",
                    BackgroundMediaPath = mediaPath ?? "",
                    MediaOpacity        = opacity
                };

                foreach (Window w in Application.Current.Windows)
                    if (w is MainWindow fence) fence.ApplyTheme(legacy);
            }
            catch (Exception ex) { LogError("ApplyMediaToOpenFences", ex); }
        }

        private static string? ResolveThemeLoop(Core.Theming.ThemeDefinition theme)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string loops   = System.IO.Path.Combine(baseDir, "Assets", "Loops");
                if (!System.IO.Directory.Exists(loops)) return null;

                foreach (string candidate in new[] { theme.Id, theme.Id.Replace('-', '_') })
                {
                    string path = System.IO.Path.Combine(loops, candidate + ".mp4");
                    if (System.IO.File.Exists(path)) return path;
                }

                return BorrowNearestLoop(theme, loops);
            }
            catch (Exception ex) { LogError("ResolveThemeLoop", ex); return null; }
        }

        private static string? BorrowNearestLoop(Core.Theming.ThemeDefinition theme, string loopFolder)
        {
            try
            {
                if (!Core.Theming.ThemeEngine.TryParseColor(theme.Colors.Background, out Color bg) ||
                    !Core.Theming.ThemeEngine.TryParseColor(theme.Colors.Accent, out Color accent))
                    return null;

                var bgLab  = Core.Theming.ColorSpace.RgbToOklab(bg.R, bg.G, bg.B);
                var accLab = Core.Theming.ColorSpace.RgbToOklab(accent.R, accent.G, accent.B);

                string? best = null;
                double bestDistance = double.MaxValue;

                foreach (var candidate in Core.Theming.ThemeCatalog.Themes)
                {
                    if (!candidate.IsBuiltIn) continue;
                    if (string.Equals(candidate.Id, theme.Id, StringComparison.OrdinalIgnoreCase)) continue;

                    string path = System.IO.Path.Combine(loopFolder, candidate.Id + ".mp4");
                    if (!System.IO.File.Exists(path))
                    {
                        path = System.IO.Path.Combine(loopFolder, candidate.Id.Replace('-', '_') + ".mp4");
                        if (!System.IO.File.Exists(path)) continue;
                    }

                    if (!Core.Theming.ThemeEngine.TryParseColor(candidate.Colors.Background, out Color cbg) ||
                        !Core.Theming.ThemeEngine.TryParseColor(candidate.Colors.Accent, out Color cacc))
                        continue;

                    var cbgLab  = Core.Theming.ColorSpace.RgbToOklab(cbg.R, cbg.G, cbg.B);
                    var caccLab = Core.Theming.ColorSpace.RgbToOklab(cacc.R, cacc.G, cacc.B);

                    double distance = OklabDistance(bgLab, cbgLab) * 2.0
                                    + OklabDistance(accLab, caccLab);

                    if (distance < bestDistance) { bestDistance = distance; best = path; }
                }

                if (best is not null)
                    Core.Theming.ThemeLog.Info("ResolveThemeLoop",
                        $"'{theme.Id}' ships no loop; borrowing {System.IO.Path.GetFileName(best)} (distance {bestDistance:F3})");

                return best;
            }
            catch (Exception ex) { LogError("BorrowNearestLoop", ex); return null; }
        }

        private static double OklabDistance((double L, double a, double b) x, (double L, double a, double b) y)
        {
            double dL = x.L - y.L, da = x.a - y.a, db = x.b - y.b;
            return Math.Sqrt(dL * dL + da * da + db * db);
        }

        private uint? ResolveCurrentMonitorIndex()
        {
            try
            {
                uint count = Core.Theming.WallpaperSampler.GetMonitorCount();
                if (count <= 1) return null;

                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                var current = System.Windows.Forms.Screen.FromHandle(helper.Handle);

                var screens = System.Windows.Forms.Screen.AllScreens;
                for (uint i = 0; i < count && i < screens.Length; i++)
                {

                    if (screens[i].Bounds == current.Bounds) return i;
                }
                return null;
            }
            catch (Exception ex) { LogError("ResolveCurrentMonitorIndex", ex); return null; }
        }

        private Rect? ResolveCurrentMonitorBounds()
        {
            try
            {
                if (System.Windows.Forms.Screen.AllScreens.Length <= 1) return null;

                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                var b = System.Windows.Forms.Screen.FromHandle(helper.Handle).Bounds;
                return new Rect(b.X, b.Y, b.Width, b.Height);
            }
            catch (Exception ex) { LogError("ResolveCurrentMonitorBounds", ex); return null; }
        }

        private void BrowseMedia_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Filter = "Media Files (*.mp4;*.gif)|*.mp4;*.gif|All Files (*.*)|*.*";
            if (dlg.ShowDialog() == true)
            {
                MediaFilePathInput.Text = dlg.FileName;
            }
        }

        private void ClearMedia_Click(object sender, RoutedEventArgs e)
        {
            MediaFilePathInput.Text = string.Empty;
        }

        private void AskAQuestion_Click(object sender, RoutedEventArgs e) { try { Process.Start(new ProcessStartInfo("mailto:davedebugs@outlook.com?subject=Fluid Fences - Question") { UseShellExecute = true }); } catch { } }
        private void SuggestAnIdea_Click(object sender, RoutedEventArgs e) { try { Process.Start(new ProcessStartInfo("mailto:davedebugs@outlook.com?subject=Fluid Fences - Suggestion") { UseShellExecute = true }); } catch { } }

        private bool _autoSaveArmed;

        private void ArmAutoSave()
        {
            if (_autoSaveArmed) return;
            _autoSaveArmed = true;

            foreach (var toggle in new[] { StartupToggle, TaskbarToggle, RestoreFilesToggle,
                                           GhostModeToggle, PauseMediaToggle })
            {
                toggle.Checked   += (_, _) => AutoSaveGlobal();
                toggle.Unchecked += (_, _) => AutoSaveGlobal();
            }

            EnableSearchToggle.Checked   += (_, _) => AutoSaveFence();
            EnableSearchToggle.Unchecked += (_, _) => AutoSaveFence();
            SortDropdown.SelectionChanged      += (_, _) => AutoSaveFence();
            GhostModeDropdown.SelectionChanged += (_, _) => AutoSaveFence();
            AnimationDropdown.SelectionChanged += (_, _) => AutoSaveGlobal();

            TitleInput.LostFocus     += (_, _) => AutoSaveFence();
            AutoSortInput.LostFocus  += (_, _) => AutoSaveFence();

            MediaOpacitySlider.ValueChanged += (_, _) => AutoSaveFence();
        }

        private void AutoSaveGlobal()
        {
            if (!_autoSaveArmed || _isLoadingFenceData) return;
            SaveGlobalSettings_Click(this, new RoutedEventArgs());
            ShowStatus("Saved.", StatusKind.Success);
        }

        private void AutoSaveFence()
        {
            if (!_autoSaveArmed || _isLoadingFenceData) return;
            SaveCurrentFenceSettings();
            if (FenceListBox.SelectedItem is ListBoxItem item) item.Content = TitleInput.Text;
            ShowStatus("Saved.", StatusKind.Success);
        }

        private sealed record NavEntry(string Tag, string Label, Wpf.Ui.Controls.SymbolRegular Icon, string Keywords);

        private static readonly NavEntry[] NavEntries =
        {
            new("AboutGrid",       "Home",       Wpf.Ui.Controls.SymbolRegular.Home24,     "about version support donate coffee"),
            new("GeneralGrid",     "General",    Wpf.Ui.Controls.SymbolRegular.Settings24, "startup windows taskbar ghost mode delete restore layout snapshot media pause"),
            new("FencesGrid",      "Fences",     Wpf.Ui.Controls.SymbolRegular.Board24,    "fence name title sort ghost search auto organise colour color opacity hue background video delete"),
            new("ThemeEditorGrid", "Themes",     Wpf.Ui.Controls.SymbolRegular.Color24,    "theme colour color dark light contrast accessibility import export animation motion"),
            new("HowToGrid",       "How to use", Wpf.Ui.Controls.SymbolRegular.Question24, "help guide shortcuts keyboard zen portal tabs merge"),
            new("FeedbackGrid",    "Feedback",   Wpf.Ui.Controls.SymbolRegular.Mail24,     "feedback bug question suggestion contact"),
        };

        private readonly Dictionary<string, System.Windows.Controls.Primitives.ToggleButton> _navItems = new();
        private string _currentTag = "AboutGrid";
        private bool _suppressNavChecked;

        private void BuildNavigation()
        {
            NavList.Children.Clear();
            _navItems.Clear();

            foreach (var entry in NavEntries)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new Wpf.Ui.Controls.SymbolIcon
                {
                    Symbol = entry.Icon,
                    FontSize = 16,
                    Margin = new Thickness(0, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
                row.Children.Add(new TextBlock
                {
                    Text = entry.Label,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 13
                });

                var button = new System.Windows.Controls.Primitives.ToggleButton
                {
                    Content = row,
                    Style = (Style)FindResource("NavButton"),
                    Tag = entry.Tag
                };
                System.Windows.Automation.AutomationProperties.SetName(button, entry.Label);

                string tag = entry.Tag;

                button.Checked += (sender, _) =>
                {
                    if (_suppressNavChecked) return;
                    SwitchToTab(tag);
                };

                button.Unchecked += (sender, _) =>
                {
                    if (_suppressNavChecked) return;
                    if (string.Equals(_currentTag, tag, StringComparison.Ordinal) &&
                        sender is System.Windows.Controls.Primitives.ToggleButton tb)
                    {
                        tb.IsChecked = true;
                    }
                };

                _navItems[tag] = button;
                NavList.Children.Add(button);
            }
        }

        private void SettingsSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            string q = SettingsSearch.Text?.Trim() ?? "";

            if (q.Length == 0)
            {
                foreach (var kv in _navItems) kv.Value.Visibility = Visibility.Visible;
                return;
            }

            var matches = new List<string>();
            foreach (var entry in NavEntries)
            {
                bool hit = entry.Label.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || entry.Keywords.Contains(q, StringComparison.OrdinalIgnoreCase);
                _navItems[entry.Tag].Visibility = hit ? Visibility.Visible : Visibility.Collapsed;
                if (hit) matches.Add(entry.Tag);
            }

            if (matches.Count == 1 && matches[0] != _currentTag) SwitchToTab(matches[0]);
        }

        public void SwitchToTab(string tag)
        {
            var pages = new Dictionary<string, FrameworkElement?>
            {
                ["AboutGrid"]       = AboutGrid,
                ["HowToGrid"]       = HowToGrid,
                ["GeneralGrid"]     = GeneralGrid,
                ["FencesGrid"]      = FencesGrid,
                ["ThemeEditorGrid"] = ThemeEditorGrid,
                ["FeedbackGrid"]    = FeedbackGrid,
            };

            foreach (var kv in pages)
                if (kv.Value is not null)
                    kv.Value.Visibility = kv.Key == tag ? Visibility.Visible : Visibility.Collapsed;

            _suppressNavChecked = true;
            try { foreach (var kv in _navItems) kv.Value.IsChecked = kv.Key == tag; }
            finally { _suppressNavChecked = false; }

            _currentTag = tag;
            PageScroller?.ScrollToTop();

            var duration = Core.Theming.ThemeEngine.ScaleDuration(150);
            if (duration == TimeSpan.Zero || PageHost is null) return;

            PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration));

            if (PageHost.RenderTransform is TranslateTransform tt)
                tt.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(8, 0, duration)
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        private enum StatusKind { Success, Info, Warning, Error }

        private Action? _undoAction;
        private readonly System.Windows.Threading.DispatcherTimer _statusTimer = new();

        private void ShowStatus(string message, StatusKind kind = StatusKind.Info, Action? undo = null)
        {
            try
            {
                StatusText.Text = message;
                _undoAction = undo;
                StatusUndo.Visibility = undo is null ? Visibility.Collapsed : Visibility.Visible;

                string brushKey;
                switch (kind)
                {
                    case StatusKind.Success:
                        StatusIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.CheckmarkCircle24;
                        brushKey = "CustomSuccessBrush"; break;
                    case StatusKind.Warning:
                        StatusIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Warning24;
                        brushKey = "CustomWarningBrush"; break;
                    case StatusKind.Error:
                        StatusIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.ErrorCircle24;
                        brushKey = "CustomErrorBrush"; break;
                    default:
                        StatusIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Info24;
                        brushKey = "CustomInfoBrush"; break;
                }
                StatusIcon.SetResourceReference(ForegroundProperty, brushKey);

                StatusStrip.Visibility = Visibility.Visible;

                _statusTimer.Stop();
                if (kind != StatusKind.Error)
                {
                    _statusTimer.Interval = TimeSpan.FromSeconds(undo is null ? 4 : 7);
                    _statusTimer.Start();
                }
            }
            catch (Exception ex) { LogError("ShowStatus", ex); }
        }

        private void HideStatus()
        {
            _statusTimer.Stop();
            _undoAction = null;
            StatusStrip.Visibility = Visibility.Collapsed;
            StatusUndo.Visibility = Visibility.Collapsed;
        }

        private void StatusDismiss_Click(object sender, RoutedEventArgs e) => HideStatus();

        private void StatusUndo_Click(object sender, RoutedEventArgs e)
        {
            var action = _undoAction;
            HideStatus();
            if (action is null)
            {
                ShowStatus("There is nothing to undo.", StatusKind.Warning);
                return;
            }

            try { action.Invoke(); }
            catch (Exception ex)
            {
                LogError("Undo", ex);
                ShowStatus($"Could not undo that: {ex.Message}", StatusKind.Error);
            }
        }

        private void RefreshStats()
        {
            try
            {
                int fences = Application.Current.Windows.OfType<MainWindow>()
                                .Count(f => f.Visibility == Visibility.Visible);
                StatFenceCount.Text  = fences.ToString(System.Globalization.CultureInfo.CurrentCulture);
                StatThemeCount.Text  = Core.Theming.ThemeCatalog.Themes.Count
                                          .ToString(System.Globalization.CultureInfo.CurrentCulture);
                StatActiveTheme.Text = Core.Theming.ThemeCatalog.Get(App.ActiveThemeId).Name;

                var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                VersionText.Text = v is null ? "" : $"Version {v.Major}.{v.Minor}.{v.Build}";
            }
            catch (Exception ex) { LogError("RefreshStats", ex); }
        }

        private void BuildHowTo()
        {
            static void Row(Panel host, string left, string right)
            {
                var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var keyText = new TextBlock { Text = left, FontSize = 12, FontWeight = FontWeights.SemiBold };
                var key = new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 3, 8, 3),
                    Margin = new Thickness(0, 0, 12, 0),
                    BorderThickness = new Thickness(1),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Child = keyText
                };
                key.SetResourceReference(Border.BackgroundProperty, "ControlFillColorSecondaryBrush");
                key.SetResourceReference(Border.BorderBrushProperty, "ControlElevationBorderBrush");
                Grid.SetColumn(key, 0);

                var desc = new TextBlock { Text = right, TextWrapping = TextWrapping.Wrap, FontSize = 13 };
                desc.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
                Grid.SetColumn(desc, 1);

                grid.Children.Add(key);
                grid.Children.Add(desc);
                host.Children.Add(grid);
            }

            ShortcutList.Children.Clear();
            Row(ShortcutList, "Ctrl + Alt + Z", "Zen Mode: hide every fence, then bring them all back.");
            Row(ShortcutList, "Ctrl + F",       "Search inside the focused fence.");
            Row(ShortcutList, "Ctrl + T",       "New tab in the focused fence.");
            Row(ShortcutList, "F10",            "Open the fence menu without reaching for the mouse.");
            Row(ShortcutList, "Ctrl + Arrows",  "Snap the fence to an edge of the current screen.");
            Row(ShortcutList, "Delete",         "Remove the selected items from the fence.");
            Row(ShortcutList, "Double-click",   "Roll the fence up into its title bar, or open it again.");

            BasicsList.Children.Clear();
            Row(BasicsList, "Create",   "Right-click the tray icon for an empty fence, or a Folder Portal that mirrors a real folder.");
            Row(BasicsList, "Fill",     "Drag files, folders or shortcuts straight in from anywhere.");
            Row(BasicsList, "Move",     "Drag the header. Fences snap to screen edges and follow you across monitors.");
            Row(BasicsList, "Resize",   "Drag any edge or corner.");
            Row(BasicsList, "Organise", "Set file extensions under Fences to sweep matching files off the Desktop automatically.");

            TabsList.Children.Clear();
            Row(TabsList, "New tab",  "Use the + button in the tab strip, or Ctrl+T.");
            Row(TabsList, "Reorder",  "Drag tabs left and right.");
            Row(TabsList, "Tear off", "Drag a tab onto the desktop and it becomes its own fence.");
            Row(TabsList, "Merge",    "Drop one fence's header onto another to combine them.");
        }

        private void StartHomeLogoAnimation()
        {
            try
            {
                string path = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "Assets", "morph_icon.mp4");
                if (!System.IO.File.Exists(path) || HomeLogoAnim is null) return;

                HomeLogoAnim.Source = new Uri(path, UriKind.Absolute);
                HomeLogoAnim.Play();
            }
            catch (Exception ex) { LogError("StartHomeLogoAnimation", ex); }
        }

        private void HomeLogoAnim_Ended(object sender, RoutedEventArgs e)
        {

            try { HomeLogoAnim.Position = TimeSpan.Zero; HomeLogoAnim.Play(); }
            catch (Exception ex) { LogError("HomeLogoAnim_Ended", ex); }
        }

        private int _versionClicks;
        private DateTime _lastVersionClick = DateTime.MinValue;

        private void WireVersionEasterEgg()
        {
            if (VersionText is null) return;
            VersionText.Background = System.Windows.Media.Brushes.Transparent;
            VersionText.MouseLeftButtonDown += VersionText_Clicked;
        }

        private void VersionText_Clicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var now = DateTime.UtcNow;

            _versionClicks = (now - _lastVersionClick).TotalSeconds <= 2.0 ? _versionClicks + 1 : 1;
            _lastVersionClick = now;

            if (_versionClicks >= 5)
            {
                _versionClicks = 0;
                try
                {
                    var egg = new RabbitEasterEgg { Owner = this };
                    egg.Show();
                }
                catch (Exception ex) { LogError("RabbitEasterEgg", ex); }
            }
        }

    }
}
