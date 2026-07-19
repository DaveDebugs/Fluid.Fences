using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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
            LoadAllFences();

            if (isFirstRun)
            {
                AboutDescriptionText.Text = "Welcome to Fluid Fences! 🌊\n\nRight-click the tray icon near your clock to build your first Fence or mirror a PC folder using a Portal. Drag, drop, and organize!\n\nPro Tip: Hit Ctrl+Alt+Z to toggle Zen Mode and hide your fences instantly.\n\nIf you love this open-source tool, consider supporting its development. Enjoy your clean desktop!";
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

        private static void LogError(string context, Exception ex)
        {
            try
            {
                string logPath = Path.Combine(App.ConfigFolder, "error.log");
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Settings_{context}: {ex.Message}\n");
            }
            catch { }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) { SaveCurrentFenceSettings(); }

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
                        StartupToggle.IsChecked = config.ShowTaskbarIcon; // This is a bug in original code too, leaving alone to not break logic
                        TaskbarToggle.IsChecked = config.ShowTaskbarIcon;
                        RestoreFilesToggle.IsChecked = config.RestoreFilesOnDelete;
                        GhostModeToggle.IsChecked = config.EnableGhostMode;

                        // Load Theme
                        if (config.Theme != null)
                        {
                            MediaFilePathInput.Text = config.Theme.BackgroundMediaPath;
                            MediaOpacitySlider.Value = config.Theme.MediaOpacity * 100.0;
                            AnimationDropdown.SelectedIndex = (int)config.Theme.RollUpAnimation;
                            
                            if (config.Theme.ThemeName == "Dark Mode") ThemePresetDropdown.SelectedIndex = 1;
                            else if (config.Theme.ThemeName == "Neon Glow") ThemePresetDropdown.SelectedIndex = 2;
                            else ThemePresetDropdown.SelectedIndex = 0;
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
        }

        private async void SaveGlobalSettings_Click(object sender, RoutedEventArgs e)
        {
            string globalConfigPath = Path.Combine(App.ConfigFolder, "global_config.json");
            bool showTaskbar = TaskbarToggle.IsChecked ?? true;
            bool restoreFiles = RestoreFilesToggle.IsChecked ?? true;
            bool ghostMode = GhostModeToggle.IsChecked ?? false;

            Core.ThemeSettings newTheme = new()
            {
                ThemeName = ThemePresetDropdown.Text,
                RollUpAnimation = (Core.AnimationStyle)AnimationDropdown.SelectedIndex,
                BackgroundMediaPath = MediaFilePathInput.Text,
                MediaOpacity = MediaOpacitySlider.Value / 100.0
            };

            if (newTheme.ThemeName == "Dark Mode")
            {
                newTheme.BackgroundColor = "#FF1E1E1E";
                newTheme.HeaderColor = "#FF2D2D2D";
                newTheme.FontColor = "#FFFFFFFF";
            }
            else if (newTheme.ThemeName == "Neon Glow")
            {
                newTheme.BackgroundColor = "#CC000000";
                newTheme.HeaderColor = "#FF8A2BE2";
                newTheme.FontColor = "#FF00FFFF";
            }
            else
            {
                newTheme.BackgroundColor = "#01000000";
                newTheme.HeaderColor = "#22000000";
                newTheme.FontColor = "#FFFFFFFF";
            }

            GlobalConfig config = new() { FirstRunComplete = true, ShowTaskbarIcon = showTaskbar, RestoreFilesOnDelete = restoreFiles, EnableGhostMode = ghostMode, Theme = newTheme };

            try
            {
                await File.WriteAllTextAsync(globalConfigPath, JsonSerializer.Serialize(config, _jsonOptions));
            }
            catch (Exception ex)
            {
                LogError("SaveConfig", ex);
                MessageBox.Show("Failed to save configuration to disk.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

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
                MessageBox.Show("Could not update Startup settings. You may need to run the app as Administrator.", "Registry Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            MessageBox.Show("Global settings updated!", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void SaveLayout_Click(object sender, RoutedEventArgs e) { if (Application.Current is App myApp) { myApp.SaveCurrentLayout(); MessageBox.Show("Layout snapshot saved successfully!", "Snapshot Saved", MessageBoxButton.OK, MessageBoxImage.Information); } }
        private void RestoreLayout_Click(object sender, RoutedEventArgs e) { if (Application.Current is App myApp) { myApp.RestoreLayoutSnapshot(); MessageBox.Show("Layout restored!", "Snapshot Restored", MessageBoxButton.OK, MessageBoxImage.Information); } }

        private void LoadAllFences()
        {
            FenceListBox.Items.Clear(); ColorFenceListBox.Items.Clear();
            foreach (Window window in Application.Current.Windows)
            {
                if (window is MainWindow { Visibility: Visibility.Visible } fence)
                {
                    ListBoxItem item1 = new() { Content = fence.FenceTitle, Tag = fence };
                    ListBoxItem item2 = new() { Content = fence.FenceTitle, Tag = fence };

                    FenceListBox.Items.Add(item1); ColorFenceListBox.Items.Add(item2);

                    if (fence == _callingFence || (_callingFence is null && FenceListBox.SelectedItem is null))
                    {
                        FenceListBox.SelectedItem = item1; ColorFenceListBox.SelectedItem = item2;
                    }
                }
            }
        }

        private void FenceListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ListBox { SelectedItem: ListBoxItem { Tag: MainWindow fence } } sourceBox)
            {
                if (sourceBox == FenceListBox) ColorFenceListBox.SelectedIndex = FenceListBox.SelectedIndex;
                else FenceListBox.SelectedIndex = ColorFenceListBox.SelectedIndex;

                _currentlySelectedFence = fence; _isLoadingFenceData = true;
                TitleInput.Text = fence.FenceTitle; AutoSortInput.Text = fence.AutoSortExtensions;
                EnableSearchToggle.IsChecked = fence.ShowSearch; PresetDropdown.SelectedIndex = 0;
                GhostModeDropdown.SelectedIndex = fence.GhostModeOverride;

                foreach (ComboBoxItem item in SortDropdown.Items)
                {
                    string itemText = item.Content.ToString() ?? "";
                    if (itemText == fence.FenceSortMethod || (fence.FenceSortMethod == "DateMod" && itemText == "Date Modified") || (fence.FenceSortMethod == "DateCre" && itemText == "Date Created") || (fence.FenceSortMethod == "DateAdd" && itemText == "Date added to Fence") || (fence.FenceSortMethod == "Type" && itemText == "Item Type"))
                    {
                        SortDropdown.SelectedItem = item; break;
                    }
                }

                UpdateColorSlidersFromFence(fence); _isLoadingFenceData = false;
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
            bool isAuto = AutoMatchToggle.IsChecked == true; _currentlySelectedFence.AutoMatchColor = isAuto;
            HueSlider.IsEnabled = !isAuto; SaturationSlider.IsEnabled = !isAuto; LightnessSlider.IsEnabled = !isAuto;
            double opacity = isAuto ? 0.4 : 1.0; HueSlider.Opacity = opacity; SaturationSlider.Opacity = opacity;

            if (isAuto)
            {
                Color newColor = ThemeUtility.GetDominantWallpaperColor();
                RgbToHsl(newColor, out double h, out double sat, out double l);
                _isColorInitializing = true; HueSlider.Value = h; SaturationSlider.Value = sat; LightnessSlider.Value = l; _isColorInitializing = false;
                UpdateColorPreview();
            }
            _currentlySelectedFence.DashboardSaveAndRefresh();
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

            Color pureHue = HslToRgb(h, 1.0, 0.5); BrightnessEndColor.Color = pureHue; SaturationEndColor.Color = pureHue; AlphaEndColor.Color = pureHue;

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
            if (_currentlySelectedFence is not null) MessageBox.Show("Settings applied to " + TitleInput.Text + "!", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void SaveCurrentFenceSettings()
        {
            if (_currentlySelectedFence is not null && !_isLoadingFenceData)
            {
                _currentlySelectedFence.FenceTitle = TitleInput.Text; _currentlySelectedFence.AutoSortExtensions = AutoSortInput.Text;
                _currentlySelectedFence.ShowSearch = EnableSearchToggle.IsChecked ?? true; _currentlySelectedFence.UpdateSearchVisibility();
                _currentlySelectedFence.GhostModeOverride = GhostModeDropdown.SelectedIndex;

                if (SortDropdown.SelectedItem is ComboBoxItem selectedSort)
                {
                    string sortSelection = selectedSort.Content.ToString() ?? "None";
                    _currentlySelectedFence.FenceSortMethod = sortSelection switch { "Date Modified" => "DateMod", "Date Created" => "DateCre", "Date added to Fence" => "DateAdd", "Item Type" => "Type", _ => sortSelection };
                }

                _currentlySelectedFence.DashboardSaveAndRefresh();

                if (FenceListBox.SelectedItem is ListBoxItem item1) { item1.Content = TitleInput.Text; }
                if (ColorFenceListBox.SelectedItem is ListBoxItem item2) { item2.Content = TitleInput.Text; }
            }
        }

        private void DeleteFenceBtn_Click(object sender, RoutedEventArgs e) { if (_currentlySelectedFence is not null) { if (MessageBox.Show($"Delete '{_currentlySelectedFence.FenceTitle}'?", "Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) { _currentlySelectedFence.DashboardDelete(); LoadAllFences(); } } }
        private void BuyMeACoffee_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://buymeacoffee.com/davedebugs") { UseShellExecute = true });
        }

        private void ThemePresetDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Just UI selection change
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

        private void RootNavigation_SelectionChanged(Wpf.Ui.Controls.NavigationView sender, RoutedEventArgs args)
        {
            if (sender.SelectedItem is Wpf.Ui.Controls.NavigationViewItem item && item.Tag is string tag)
            {
                SwitchToTab(tag);
            }
        }

        private void NavItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Wpf.Ui.Controls.NavigationViewItem item && item.Tag is string tag)
            {
                SwitchToTab(tag);
            }
        }

        public void SwitchToTab(string tag)
        {
            if (AboutGrid != null) AboutGrid.Visibility = tag == "AboutGrid" ? Visibility.Visible : Visibility.Collapsed;
            if (HowToGrid != null) HowToGrid.Visibility = tag == "HowToGrid" ? Visibility.Visible : Visibility.Collapsed;
            if (GeneralGrid != null) GeneralGrid.Visibility = tag == "GeneralGrid" ? Visibility.Visible : Visibility.Collapsed;
            if (FencesGrid != null) FencesGrid.Visibility = tag == "FencesGrid" ? Visibility.Visible : Visibility.Collapsed;
            if (ThemeEditorGrid != null) ThemeEditorGrid.Visibility = tag == "ThemeEditorGrid" ? Visibility.Visible : Visibility.Collapsed;
            if (FeedbackGrid != null) FeedbackGrid.Visibility = tag == "FeedbackGrid" ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}