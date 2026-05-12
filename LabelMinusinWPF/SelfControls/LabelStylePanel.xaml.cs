using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelMinusinWPF.Common;

namespace LabelMinusinWPF.SelfControls
{
    [ObservableObject]
    public partial class LabelStylePanel : UserControl
    {
        public static LabelStylePanel Instance { get; private set; } = new();
        private bool _isApplyingSettings;

        public LabelStylePanel()
        {
            Instance = this;
            InitializeComponent();
            DataContext = this;
        }

        // --- Observable properties ---
        [ObservableProperty]
        private Style? _labelDotStyle;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LabelDotStyle))]
        private string _dotStyle = "Circle";

        [ObservableProperty]
        private Color _textBackgroundColor = Colors.White;

        [ObservableProperty]
        private Color _textForegroundColor = Colors.Black;

        [ObservableProperty]
        private double _textBackgroundOpacity = 1.0;

        [ObservableProperty]
        private double _labelScale = 1.0;

        private static readonly Dictionary<string, Style?> _dotStyleCache = new()
        {
            { "Circle", null },
            { "Square", null },
            { "Transparent", null }
        };

        partial void OnDotStyleChanged(string value)
        {
            UpdateDotStyle();
            SaveSettings();
        }

        partial void OnTextBackgroundOpacityChanged(double value) => SaveSettings();

        private void UpdateDotStyle()
        {
            if (Application.Current == null) return;
            if (_dotStyleCache[DotStyle] is null)
                _dotStyleCache[DotStyle] = Application.Current.TryFindResource(DotStyle switch
                {
                    "Circle" => "DefaultDotStyle",
                    "Square" => "SquareDotStyle",
                    "Transparent" => "TransparentDotStyle",
                    _ => "DefaultDotStyle"
                }) as Style;
            LabelDotStyle = _dotStyleCache[DotStyle];
        }

        public void LoadSettings()
        {
            try
            {
                var settings = AppSettingsService.Current.LabelStyle;
                _isApplyingSettings = true;
                DotStyle = settings.DotStyle;
                TextBackgroundColor = ColorFromString(settings.TextBackgroundColor);
                TextForegroundColor = ColorFromString(settings.TextForegroundColor);
                TextBackgroundOpacity = settings.TextBackgroundOpacity;
                LabelScale = settings.LabelScale;
            }
            catch { }
            finally
            {
                _isApplyingSettings = false;
            }
        }

        public void SaveSettings()
        {
            if (_isApplyingSettings) return;

            try
            {
                AppSettingsService.Current.LabelStyle.DotStyle = DotStyle;
                AppSettingsService.Current.LabelStyle.TextBackgroundColor = ColorToString(TextBackgroundColor);
                AppSettingsService.Current.LabelStyle.TextForegroundColor = ColorToString(TextForegroundColor);
                AppSettingsService.Current.LabelStyle.TextBackgroundOpacity = TextBackgroundOpacity;
                AppSettingsService.Current.LabelStyle.LabelScale = LabelScale;
                AppSettingsService.Save();
            }
            catch { }
        }

        // --- Commands ---
        [RelayCommand]
        public void ResetToDefaults()
        {
            try
            {
                _isApplyingSettings = true;
                DotStyle = "Circle";
                TextBackgroundColor = Colors.Black;
                TextForegroundColor = Colors.White;
                TextBackgroundOpacity = 0.5;
                LabelScale = 1.0;
            }
            finally
            {
                _isApplyingSettings = false;
            }

            SaveSettings();
        }

        [RelayCommand]
        public void ZoomInLabel() => AdjustLabelScale(+0.1);

        [RelayCommand]
        public void ZoomOutLabel() => AdjustLabelScale(-0.1);

        [RelayCommand]
        public void SetBackgroundColor(string colorName) => ApplyColor(isBackground: true, colorName);

        [RelayCommand]
        public void SetForegroundColor(string colorName) => ApplyColor(isBackground: false, colorName);

        private void AdjustLabelScale(double delta)
        {
            LabelScale = Math.Clamp(LabelScale + delta, 0.3, 3.0);
            SaveSettings();
        }

        private void ApplyColor(bool isBackground, string colorName)
        {
            if (isBackground) TextBackgroundColor = ColorFromString(colorName);
            else TextForegroundColor = ColorFromString(colorName);
            SaveSettings();
        }

        // --- Color conversion ---
        private static Color ColorFromString(string colorName)
        {
            try { return (Color)ColorConverter.ConvertFromString(colorName); }
            catch { return Colors.White; }
        }

        private static string ColorToString(Color color)
        {
            if (color == Colors.White) return "White";
            if (color == Colors.Black) return "Black";
            if (color == Colors.RoyalBlue) return "RoyalBlue";
            if (color == Colors.Transparent) return "Transparent";
            return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        }
    }
}
