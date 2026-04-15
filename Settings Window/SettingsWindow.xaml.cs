using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopFences
{
    public partial class SettingsWindow : Window
    {
        [LibraryImport("user32.dll")] internal static partial IntPtr GetDC(IntPtr hwnd);
        [LibraryImport("user32.dll")] internal static partial int ReleaseDC(IntPtr hwnd, IntPtr hDC);
        [LibraryImport("gdi32.dll")] internal static partial uint GetPixel(IntPtr hDC, int x, int y);

        private const int SPI_GETDESKWALLPAPER = 0x0073;
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SystemParametersInfo(int uiAction, int uiParam, System.Text.StringBuilder pvParam, int fWinIni);

        private MainWindow? _callingFence;
        private MainWindow? _currentlySelectedFence;
        private bool _isLoadingFenceData = false;
        private bool _isColorInitializing = false;

        private readonly System.Windows.Threading.DispatcherTimer _liveUpdateTimer = new();
        private Color _pendingColor;
        private double _pendingOpacity;
        private bool _hasPendingColor = false;

        public SettingsWindow(MainWindow? callingFence, bool isFirstRun = false)
        {
            InitializeComponent();
            _callingFence = callingFence;
            PresetDropdown.SelectedIndex = 0;
            LoadAllFences();

            if (isFirstRun)
            {
                AboutDescriptionText.Text = "Thank you for using Fluid Fences! This was a labor of love for keeping messy desktops organized.\n\nNote: Fences will run in the background. Look for the fluid icon in your System Tray (near the clock) to access settings or create new fences!\n\nThis app is open-source, but if you'd like to support it, please consider donating. Thank you!";
            }

            LoadGlobalSettings();

            _liveUpdateTimer.Interval = TimeSpan.FromMilliseconds(33);
            _liveUpdateTimer.Tick += (s, e) =>
            {
                if (_hasPendingColor && _currentlySelectedFence != null)
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

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveCurrentFenceSettings();
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

        private void LoadGlobalSettings()
        {
            string configFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopFences");
            string globalConfigPath = Path.Combine(configFolder, "global_config.json");
            if (File.Exists(globalConfigPath))
            {
                try
                {
                    string json = File.ReadAllText(globalConfigPath);
                    if (System.Text.Json.JsonSerializer.Deserialize<GlobalConfig>(json) is GlobalConfig config)
                    {
                        TaskbarToggle.IsChecked = config.ShowTaskbarIcon;
                        RestoreFilesToggle.IsChecked = config.RestoreFilesOnDelete;
                    }
                }
                catch { }
            }

            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", false);
                StartupToggle.IsChecked = key?.GetValue("FluidFences") != null;
            }
            catch { }
        }

        private void SaveGlobalSettings_Click(object sender, RoutedEventArgs e)
        {
            string configFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopFences");
            string globalConfigPath = Path.Combine(configFolder, "global_config.json");
            bool showTaskbar = TaskbarToggle.IsChecked ?? true;
            bool restoreFiles = RestoreFilesToggle.IsChecked ?? true;

            GlobalConfig config = new() { FirstRunComplete = true, ShowTaskbarIcon = showTaskbar, RestoreFilesOnDelete = restoreFiles };
            File.WriteAllText(globalConfigPath, System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            foreach (Window window in Application.Current.Windows)
            {
                if (window is MainWindow fence) fence.ShowInTaskbar = showTaskbar;
            }

            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                if (StartupToggle.IsChecked == true)
                {
                    string exePath = Process.GetCurrentProcess().MainModule!.FileName;
                    key?.SetValue("FluidFences", $"\"{exePath}\"");
                }
                else
                {
                    key?.DeleteValue("FluidFences", false);
                }
            }
            catch { MessageBox.Show("Could not update Startup settings. You may need to run the app as Administrator.", "Registry Error"); }

            MessageBox.Show("Global settings updated!", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void LoadAllFences()
        {
            FenceListBox.Items.Clear();
            ColorFenceListBox.Items.Clear();

            foreach (Window window in Application.Current.Windows)
            {
                if (window is MainWindow fence && fence.Visibility == Visibility.Visible)
                {
                    ListBoxItem item1 = new() { Content = fence.FenceTitle, Tag = fence };
                    ListBoxItem item2 = new() { Content = fence.FenceTitle, Tag = fence };

                    FenceListBox.Items.Add(item1);
                    ColorFenceListBox.Items.Add(item2);

                    if (fence == _callingFence || (_callingFence == null && FenceListBox.SelectedItem == null))
                    {
                        FenceListBox.SelectedItem = item1;
                        ColorFenceListBox.SelectedItem = item2;
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

                _currentlySelectedFence = fence;
                _isLoadingFenceData = true;

                TitleInput.Text = fence.FenceTitle;
                AutoSortInput.Text = fence.AutoSortExtensions;
                EnableSearchToggle.IsChecked = fence.ShowSearch;
                PresetDropdown.SelectedIndex = 0;

                foreach (ComboBoxItem item in SortDropdown.Items)
                {
                    string itemText = item.Content.ToString() ?? "";
                    if (itemText == fence.FenceSortMethod ||
                       (fence.FenceSortMethod == "DateMod" && itemText == "Date Modified") ||
                       (fence.FenceSortMethod == "DateCre" && itemText == "Date Created") ||
                       (fence.FenceSortMethod == "DateAdd" && itemText == "Date added to Fence") ||
                       (fence.FenceSortMethod == "Type" && itemText == "Item Type"))
                    {
                        SortDropdown.SelectedItem = item;
                        break;
                    }
                }

                UpdateColorSlidersFromFence(fence);
                _isLoadingFenceData = false;
            }
        }

        private void UpdateColorSlidersFromFence(MainWindow fence)
        {
            _isColorInitializing = true;

            Color startColor = fence.FenceColor;
            AlphaSlider.Value = fence.FenceOpacity * 100.0;

            if (BlendWallpaperToggle != null) BlendWallpaperToggle.IsChecked = (AlphaSlider.Value <= 1.0);
            if (AutoMatchToggle != null) AutoMatchToggle.IsChecked = fence.AutoMatchColor;

            bool isAuto = fence.AutoMatchColor;
            HueSlider.IsEnabled = !isAuto;
            SaturationSlider.IsEnabled = !isAuto;
            LightnessSlider.IsEnabled = !isAuto;

            double opacity = isAuto ? 0.4 : 1.0;
            HueSlider.Opacity = opacity;
            SaturationSlider.Opacity = opacity;
            LightnessSlider.Opacity = opacity;

            System.Drawing.Color sysColor = System.Drawing.Color.FromArgb(startColor.A, startColor.R, startColor.G, startColor.B);
            HueSlider.Value = sysColor.GetHue();
            SaturationSlider.Value = sysColor.GetSaturation() * 100.0;
            LightnessSlider.Value = sysColor.GetBrightness() * 100.0;

            UpdateColorPreview();
            _isColorInitializing = false;
        }

        private void AutoMatchToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_isColorInitializing || _currentlySelectedFence == null) return;

            bool isAuto = AutoMatchToggle.IsChecked == true;
            _currentlySelectedFence.AutoMatchColor = isAuto;

            HueSlider.IsEnabled = !isAuto;
            SaturationSlider.IsEnabled = !isAuto;
            LightnessSlider.IsEnabled = !isAuto;

            double opacity = isAuto ? 0.4 : 1.0;
            HueSlider.Opacity = opacity;
            SaturationSlider.Opacity = opacity;
            LightnessSlider.Opacity = opacity;

            if (isAuto)
            {
                Color newColor = GetDominantWallpaperColor();
                RgbToHsl(newColor, out double h, out double sat, out double l);

                _isColorInitializing = true;
                HueSlider.Value = h;
                SaturationSlider.Value = sat;
                LightnessSlider.Value = l;
                _isColorInitializing = false;

                UpdateColorPreview();
            }

            _currentlySelectedFence.DashboardSaveAndRefresh();
        }

        private void ColorSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isColorInitializing || _currentlySelectedFence == null) return;

            if (sender == AlphaSlider && BlendWallpaperToggle != null)
            {
                BlendWallpaperToggle.IsChecked = (AlphaSlider.Value <= 1.0);
            }

            UpdateColorPreview();
        }

        private void BlendWallpaperToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_isColorInitializing || _currentlySelectedFence == null) return;

            if (BlendWallpaperToggle.IsChecked == true) AlphaSlider.Value = 1;
            else AlphaSlider.Value = 70;
        }

        private void UpdateColorPreview()
        {
            double h = HueSlider.Value;
            double s = SaturationSlider.Value / 100.0;
            double l = LightnessSlider.Value / 100.0;
            double a = AlphaSlider.Value / 100.0;

            Color rgbColor = HslToRgb(h, s, l);
            Color finalColor = Color.FromArgb((byte)(a * 255), rgbColor.R, rgbColor.G, rgbColor.B);

            ColorPreview.Background = new SolidColorBrush(finalColor) { Opacity = a };

            Color pureHue = HslToRgb(h, 1.0, 0.5);
            BrightnessEndColor.Color = pureHue;
            SaturationEndColor.Color = pureHue;
            AlphaEndColor.Color = pureHue;

            if (!_isColorInitializing && _currentlySelectedFence != null)
            {
                _pendingColor = finalColor;
                _pendingOpacity = a;
                _hasPendingColor = true;
                if (!_liveUpdateTimer.IsEnabled) _liveUpdateTimer.Start();
            }
        }

        private Color GetDominantWallpaperColor()
        {
            try
            {
                System.Text.StringBuilder wallpaperPath = new System.Text.StringBuilder(260);
                SystemParametersInfo(SPI_GETDESKWALLPAPER, 260, wallpaperPath, 0);
                string path = wallpaperPath.ToString();

                if (!File.Exists(path)) return Colors.Black;

                BitmapImage bmp = new BitmapImage();
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

        private void PresetDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingFenceData || PresetDropdown.SelectedItem == null || AutoSortInput == null) return;
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
            if (_currentlySelectedFence != null)
            {
                MessageBox.Show("Settings applied to " + TitleInput.Text + "!", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void SaveCurrentFenceSettings()
        {
            if (_currentlySelectedFence != null && !_isLoadingFenceData)
            {
                _currentlySelectedFence.FenceTitle = TitleInput.Text;
                _currentlySelectedFence.AutoSortExtensions = AutoSortInput.Text;
                _currentlySelectedFence.ShowSearch = EnableSearchToggle.IsChecked ?? true;

                if (_currentlySelectedFence.FindName("SearchPanel") is StackPanel searchPanel) { searchPanel.Visibility = _currentlySelectedFence.ShowSearch ? Visibility.Visible : Visibility.Collapsed; }

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

        private void DeleteFenceBtn_Click(object sender, RoutedEventArgs e) { if (_currentlySelectedFence != null) { if (MessageBox.Show($"Delete '{_currentlySelectedFence.FenceTitle}'?", "Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) { _currentlySelectedFence.DashboardDelete(); LoadAllFences(); } } }
        private void BuyMeACoffee_Click(object sender, RoutedEventArgs e) { try { Process.Start(new ProcessStartInfo("https://www.paypal.com/qrcodes/venmocs/a0e66d13-f15f-4fbd-ba26-34008d486c61?created=1775532880") { UseShellExecute = true }); } catch { } }
        private void AskAQuestion_Click(object sender, RoutedEventArgs e) { try { Process.Start(new ProcessStartInfo("mailto:davedebugs@outlook.com?subject=Fluid Fences - Question") { UseShellExecute = true }); } catch { } }
        private void SuggestAnIdea_Click(object sender, RoutedEventArgs e) { try { Process.Start(new ProcessStartInfo("mailto:davedebugs@outlook.com?subject=Fluid Fences - Suggestion") { UseShellExecute = true }); } catch { } }
    }
}