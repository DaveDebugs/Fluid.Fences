using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopFences
{
    public partial class ColorPickerWindow : Window
    {
        [LibraryImport("user32.dll")] internal static partial IntPtr GetDC(IntPtr hwnd);
        [LibraryImport("user32.dll")] internal static partial int ReleaseDC(IntPtr hwnd, IntPtr hDC);
        [LibraryImport("gdi32.dll")] internal static partial uint GetPixel(IntPtr hDC, int x, int y);

        private const int SPI_GETDESKWALLPAPER = 0x0073;
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SystemParametersInfo(int uiAction, int uiParam, System.Text.StringBuilder pvParam, int fWinIni);

        public SolidColorBrush? SelectedBrush { get; private set; }
        public bool AutoMatch { get; private set; }

        private bool _isInitializing = true;

        public ColorPickerWindow(SolidColorBrush initialBrush, bool autoMatch)
        {
            InitializeComponent();

            AutoMatch = autoMatch;
            if (AutoMatchToggle != null) AutoMatchToggle.IsChecked = AutoMatch;

            Color startColor = initialBrush.Color;
            AlphaSlider.Value = (initialBrush.Opacity * 100.0);

            if (BlendWallpaperToggle != null) BlendWallpaperToggle.IsChecked = (AlphaSlider.Value <= 1.0);

            System.Drawing.Color sysColor = System.Drawing.Color.FromArgb(startColor.A, startColor.R, startColor.G, startColor.B);
            HueSlider.Value = sysColor.GetHue();
            SaturationSlider.Value = sysColor.GetSaturation() * 100.0;
            LightnessSlider.Value = sysColor.GetBrightness() * 100.0;

            ColorPreview.Background = initialBrush;
            SelectedBrush = initialBrush;

            _isInitializing = false;

            if (AutoMatch)
            {
                TriggerAutoMatch();
            }
            else
            {
                UpdateColorFromSliders();
            }
            ToggleSliders(!AutoMatch);
        }

        private void Eyedropper_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (AutoMatch) return;

            Window pickerOverlay = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                Topmost = true,
                Cursor = Cursors.Cross,
                ShowInTaskbar = false,
                Left = SystemParameters.VirtualScreenLeft,
                Top = SystemParameters.VirtualScreenTop,
                Width = SystemParameters.VirtualScreenWidth,
                Height = SystemParameters.VirtualScreenHeight
            };

            pickerOverlay.PreviewMouseLeftButtonDown += (s, args) =>
            {
                var mousePos = System.Windows.Forms.Cursor.Position;
                pickerOverlay.Hide();
                Application.Current.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);

                IntPtr hdc = GetDC(IntPtr.Zero);
                uint pixel = GetPixel(hdc, mousePos.X, mousePos.Y);
                ReleaseDC(IntPtr.Zero, hdc);

                byte r = (byte)(pixel & 0x000000FF);
                byte g = (byte)((pixel & 0x0000FF00) >> 8);
                byte b = (byte)((pixel & 0x00FF0000) >> 16);

                Color pickedColor = Color.FromRgb(r, g, b);

                RgbToHsl(pickedColor, out double h, out double sat, out double l);

                _isInitializing = true;
                HueSlider.Value = h;
                SaturationSlider.Value = sat;
                LightnessSlider.Value = l;
                _isInitializing = false;

                if (BlendWallpaperToggle != null) BlendWallpaperToggle.IsChecked = false;
                if (AlphaSlider.Value <= 1) AlphaSlider.Value = 70;

                UpdateColorFromSliders();
                pickerOverlay.Close();
            };

            pickerOverlay.PreviewKeyDown += (s, args) => { if (args.Key == Key.Escape) pickerOverlay.Close(); };
            pickerOverlay.Show();
        }

        private void AutoMatchToggle_Click(object sender, RoutedEventArgs e)
        {
            AutoMatch = AutoMatchToggle.IsChecked == true;
            ToggleSliders(!AutoMatch);

            if (AutoMatch)
            {
                TriggerAutoMatch();
            }
        }

        private void TriggerAutoMatch()
        {
            Color newColor = GetDominantWallpaperColor();
            RgbToHsl(newColor, out double h, out double sat, out double l);

            _isInitializing = true;
            HueSlider.Value = h;
            SaturationSlider.Value = sat;
            LightnessSlider.Value = l;
            _isInitializing = false;

            UpdateColorFromSliders();
        }

        private void ToggleSliders(bool enable)
        {
            HueSlider.IsEnabled = enable;
            SaturationSlider.IsEnabled = enable;
            LightnessSlider.IsEnabled = enable;

            double opacity = enable ? 1.0 : 0.4;
            HueSlider.Opacity = opacity;
            SaturationSlider.Opacity = opacity;
            LightnessSlider.Opacity = opacity;

            if (EyedropperIcon != null) EyedropperIcon.Opacity = opacity;
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

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing) return;

            if (sender == AlphaSlider && BlendWallpaperToggle != null)
            {
                BlendWallpaperToggle.IsChecked = (AlphaSlider.Value <= 1.0);
            }

            UpdateColorFromSliders();
        }

        private void BlendWallpaperToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            if (BlendWallpaperToggle.IsChecked == true) AlphaSlider.Value = 1;
            else AlphaSlider.Value = 70;
        }

        private void UpdateColorFromSliders()
        {
            double h = HueSlider.Value;
            double s = SaturationSlider.Value / 100.0;
            double l = LightnessSlider.Value / 100.0;
            double a = AlphaSlider.Value / 100.0;

            Color rgbColor = HslToRgb(h, s, l);
            Color finalColor = Color.FromArgb((byte)(a * 255), rgbColor.R, rgbColor.G, rgbColor.B);

            SelectedBrush = new SolidColorBrush(finalColor) { Opacity = a };
            ColorPreview.Background = SelectedBrush;

            Color pureHue = HslToRgb(h, 1.0, 0.5);
            BrightnessEndColor.Color = pureHue;
            SaturationEndColor.Color = pureHue;
            AlphaEndColor.Color = pureHue;
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.Close();
        }

        private Color HslToRgb(double h, double s, double l)
        {
            double r, g, b;

            if (s == 0)
            {
                r = g = b = l;
            }
            else
            {
                Func<double, double, double, double> hue2rgb = (p, q, t) =>
                {
                    if (t < 0) t += 1;
                    if (t > 1) t -= 1;
                    if (t < 1 / 6.0) return p + (q - p) * 6 * t;
                    if (t < 1 / 2.0) return q;
                    if (t < 2 / 3.0) return p + (q - p) * (2 / 3.0 - t) * 6;
                    return p;
                };

                double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
                double p = 2 * l - q;
                h /= 360;

                r = hue2rgb(p, q, h + 1 / 3.0);
                g = hue2rgb(p, q, h);
                b = hue2rgb(p, q, h - 1 / 3.0);
            }

            return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }
    }
}