using System;
using System.Text.Json.Serialization;

namespace DesktopFences.Core
{
    public enum AnimationStyle
    {
        Smooth,
        Bounce,
        Linear,
        None
    }

    public class ThemeSettings : ObservableObject
    {
        private string _themeName = "Fluid Glass (Default)";
        public string ThemeName
        {
            get => _themeName;
            set { _themeName = value; OnPropertyChanged(); }
        }

        private string _primaryColor = "#0078D7";
        public string PrimaryColor
        {
            get => _primaryColor;
            set { _primaryColor = value; OnPropertyChanged(); }
        }

        private string _secondaryColor = "#2B88D8";
        public string SecondaryColor
        {
            get => _secondaryColor;
            set { _secondaryColor = value; OnPropertyChanged(); }
        }

        private string _accentColor = "#0078D7";
        public string AccentColor
        {
            get => _accentColor;
            set { _accentColor = value; OnPropertyChanged(); }
        }

        private string _backgroundColor = "#01000000";
        public string BackgroundColor
        {
            get => _backgroundColor;
            set { _backgroundColor = value; OnPropertyChanged(); }
        }

        private string _surfaceColor = "#151515";
        public string SurfaceColor
        {
            get => _surfaceColor;
            set { _surfaceColor = value; OnPropertyChanged(); }
        }

        private string _headerColor = "#22000000";
        public string HeaderColor
        {
            get => _headerColor;
            set { _headerColor = value; OnPropertyChanged(); }
        }

        private string _fontColor = "#FFFFFFFF";
        public string FontColor
        {
            get => _fontColor;
            set { _fontColor = value; OnPropertyChanged(); }
        }
        
        private string _secondaryFontColor = "#FFAAAAAA";
        public string SecondaryFontColor
        {
            get => _secondaryFontColor;
            set { _secondaryFontColor = value; OnPropertyChanged(); }
        }

        private string _borderColor = "#333333";
        public string BorderColor
        {
            get => _borderColor;
            set { _borderColor = value; OnPropertyChanged(); }
        }

        private string _successColor = "#107C10";
        public string SuccessColor
        {
            get => _successColor;
            set { _successColor = value; OnPropertyChanged(); }
        }

        private string _errorColor = "#E81123";
        public string ErrorColor
        {
            get => _errorColor;
            set { _errorColor = value; OnPropertyChanged(); }
        }

        private string _warningColor = "#FFB900";
        public string WarningColor
        {
            get => _warningColor;
            set { _warningColor = value; OnPropertyChanged(); }
        }

        private double _cornerRadius = 8.0;
        public double CornerRadius
        {
            get => _cornerRadius;
            set { _cornerRadius = value; OnPropertyChanged(); }
        }

        private AnimationStyle _rollUpAnimation = AnimationStyle.Smooth;
        public AnimationStyle RollUpAnimation
        {
            get => _rollUpAnimation;
            set { _rollUpAnimation = value; OnPropertyChanged(); }
        }

        private string _backgroundMediaPath = "";
        public string BackgroundMediaPath
        {
            get => _backgroundMediaPath;
            set { _backgroundMediaPath = value; OnPropertyChanged(); }
        }

        private double _mediaOpacity = 1.0;
        public double MediaOpacity
        {
            get => _mediaOpacity;
            set { _mediaOpacity = value; OnPropertyChanged(); }
        }
    }
}
