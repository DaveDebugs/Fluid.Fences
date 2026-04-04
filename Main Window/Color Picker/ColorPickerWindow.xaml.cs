using System;
using System.Windows;
using System.Windows.Media;

namespace DesktopFences
{
    public partial class ColorPickerWindow : Window
    {
        public SolidColorBrush SelectedBrush { get; private set; }
        private bool _isInitializing = true;

        public ColorPickerWindow(SolidColorBrush currentBrush)
        {
            InitializeComponent();

            // Extract the current color's data
            Color c = currentBrush.Color;
            System.Drawing.Color drawingColor = System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);

            // Set sliders to match current color
            HueSlider.Value = drawingColor.GetHue();
            SaturationSlider.Value = drawingColor.GetSaturation();
            BrightnessSlider.Value = drawingColor.GetBrightness();
            TransparencySlider.Value = currentBrush.Opacity;

            _isInitializing = false;
            UpdatePreview();
        }

        private void Sliders_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing) return;
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            // Calculate new color from sliders
            Color newColor = ColorFromHSL(HueSlider.Value, SaturationSlider.Value, BrightnessSlider.Value);

            // Create a brush and apply the transparency slider
            SelectedBrush = new SolidColorBrush(newColor)
            {
                Opacity = TransparencySlider.Value
            };

            // Update the live preview box in the UI
            ColorOverlay.Background = SelectedBrush;
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        // --- HSL TO RGB ALGORITHM ---
        private static Color ColorFromHSL(double h, double s, double l)
        {
            byte r = 0, g = 0, b = 0;
            if (s == 0)
            {
                r = g = b = (byte)(l * 255);
            }
            else
            {
                double v1, v2;
                double hue = h / 360;
                v2 = (l < 0.5) ? (l * (1 + s)) : ((l + s) - (l * s));
                v1 = 2 * l - v2;
                r = (byte)(255 * HueToRGB(v1, v2, hue + (1.0f / 3)));
                g = (byte)(255 * HueToRGB(v1, v2, hue));
                b = (byte)(255 * HueToRGB(v1, v2, hue - (1.0f / 3)));
            }
            return Color.FromArgb(255, r, g, b);
        }

        private static double HueToRGB(double v1, double v2, double vH)
        {
            if (vH < 0) vH += 1;
            if (vH > 1) vH -= 1;
            if ((6 * vH) < 1) return (v1 + (v2 - v1) * 6 * vH);
            if ((2 * vH) < 1) return v2;
            if ((3 * vH) < 2) return (v1 + (v2 - v1) * ((2.0f / 3) - vH) * 6);
            return v1;
        }
    }
}