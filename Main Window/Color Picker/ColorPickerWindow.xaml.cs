using System;
using System.Windows;
using System.Windows.Media;

namespace DesktopFences
{
    public partial class ColorPickerWindow : Window
    {
        public SolidColorBrush? SelectedBrush { get; private set; }
        private bool _isInitializing = true;

        public ColorPickerWindow(SolidColorBrush initialBrush)
        {
            InitializeComponent();

            // Extract starting HSL/Alpha from the provided brush
            Color startColor = initialBrush.Color;
            AlphaSlider.Value = (initialBrush.Opacity * 100.0);

            System.Drawing.Color sysColor = System.Drawing.Color.FromArgb(startColor.A, startColor.R, startColor.G, startColor.B);
            HueSlider.Value = sysColor.GetHue();
            SaturationSlider.Value = sysColor.GetSaturation() * 100.0;
            LightnessSlider.Value = sysColor.GetBrightness() * 100.0;

            ColorPreview.Background = initialBrush;
            SelectedBrush = initialBrush;

            _isInitializing = false;
            UpdateColorFromSliders();
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing) return;
            UpdateColorFromSliders();
        }

        private void UpdateColorFromSliders()
        {
            double h = HueSlider.Value;
            double s = SaturationSlider.Value / 100.0;
            double l = LightnessSlider.Value / 100.0;
            double a = AlphaSlider.Value / 100.0;

            Color rgbColor = HslToRgb(h, s, l);
            Color finalColor = Color.FromArgb((byte)(a * 255), rgbColor.R, rgbColor.G, rgbColor.B);

            // Update UI preview
            SelectedBrush = new SolidColorBrush(finalColor) { Opacity = a };
            ColorPreview.Background = SelectedBrush;

            // Dynamically update the slider gradient tracks to match the hue
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

        // Standard Math formula to convert HSL (Hue, Saturation, Lightness) to RGB
        private Color HslToRgb(double h, double s, double l)
        {
            double r, g, b;

            if (s == 0)
            {
                r = g = b = l; // achromatic
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

        private void ComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {

        }
    }
}