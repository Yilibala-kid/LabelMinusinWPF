using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LabelMinusinWPF.SelfControls
{

    public enum DotStyleType
    {
        Circle,
        Square,
        Transparent
    }


    public class LabelStyleSettings
    {
        public DotStyleType DotStyle { get; set; } = DotStyleType.Circle;
        public string TextBackgroundColor { get; set; } = "White";
        public string TextForegroundColor { get; set; } = "Black";
        public double TextBackgroundOpacity { get; set; } = 1.0;
        public double LabelScale { get; set; } = 1.0;


        public static LabelStyleSettings CreateDefault() => new()
        {
            DotStyle = DotStyleType.Circle,
            TextBackgroundColor = "White",
            TextForegroundColor = "Black",
            TextBackgroundOpacity = 0.5,
            LabelScale = 1.0
        };
    }

    public partial class LabelStyleManager : ObservableObject
    {
        private static readonly Lazy<LabelStyleManager> _instance = new(() => new LabelStyleManager());
        public static LabelStyleManager Instance => _instance.Value;

        private LabelStyleManager()
        {
            UpdateDotStyle();
            UpdateBrushes();
        }

        // --- 可观察属性 ---
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LabelDotStyle))]
        private DotStyleType _dotStyle = DotStyleType.Circle;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TextBackgroundBrush))]
        private Color _textBackgroundColor = Colors.White;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TextForegroundBrush))]
        private Color _textForegroundColor = Colors.Black;

        [ObservableProperty]
        private double _textBackgroundOpacity = 1.0;

        [ObservableProperty]
        private double _labelScale = 1.0;

        // --- 计算属性（返回WPF对象）---
        private Style? _labelDotStyle;
        public Style? LabelDotStyle
        {
            get => _labelDotStyle;
            private set => SetProperty(ref _labelDotStyle, value);
        }

        private Brush? _textBackgroundBrush;
        public Brush? TextBackgroundBrush
        {
            get => _textBackgroundBrush;
            private set => SetProperty(ref _textBackgroundBrush, value);
        }

        private Brush? _textForegroundBrush;
        public Brush? TextForegroundBrush
        {
            get => _textForegroundBrush;
            private set => SetProperty(ref _textForegroundBrush, value);
        }

        // --- 属性变更处理 ---
        partial void OnDotStyleChanged(DotStyleType value) => UpdateDotStyle();
        partial void OnTextBackgroundColorChanged(Color value) => UpdateBrushes();
        partial void OnTextForegroundColorChanged(Color value) => UpdateBrushes();

        // --- 样式解析方法 ---
        private void UpdateDotStyle()
        {
            if (Application.Current == null) return;

            string styleKey = DotStyle switch
            {
                DotStyleType.Circle => "DefaultDotStyle",
                DotStyleType.Square => "SquareDotStyle",
                DotStyleType.Transparent => "TransparentDotStyle",
                _ => "DefaultDotStyle"
            };

            LabelDotStyle = Application.Current.TryFindResource(styleKey) as Style;
        }

        // 画刷缓存字典
        private static readonly Dictionary<Color, SolidColorBrush> _brushCache = new();

        private void UpdateBrushes()
        {
            // 使用缓存的画刷，避免重复创建
            if (!_brushCache.TryGetValue(TextBackgroundColor, out var bgBrush))
            {
                bgBrush = new SolidColorBrush(TextBackgroundColor);
                _brushCache[TextBackgroundColor] = bgBrush;
            }
            TextBackgroundBrush = bgBrush;

            if (!_brushCache.TryGetValue(TextForegroundColor, out var fgBrush))
            {
                fgBrush = new SolidColorBrush(TextForegroundColor);
                _brushCache[TextForegroundColor] = fgBrush;
            }
            TextForegroundBrush = fgBrush;
        }

        // --- 持久化方法 ---
        private static string SettingsFilePath
        {
            get
            {
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string appFolder = Path.Combine(appDataPath, "LabelMinusinWPF");
                Directory.CreateDirectory(appFolder);
                return Path.Combine(appFolder, "LabelStyleSettings.json");
            }
        }

        public void LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    var settings = JsonSerializer.Deserialize<LabelStyleSettings>(json);
                    if (settings != null)
                    {
                        DotStyle = settings.DotStyle;
                        TextBackgroundColor = ColorFromString(settings.TextBackgroundColor);
                        TextForegroundColor = ColorFromString(settings.TextForegroundColor);
                        TextBackgroundOpacity = settings.TextBackgroundOpacity;
                        LabelScale = settings.LabelScale;
                    }
                }
            }
            catch
            {
                // 加载失败时使用默认值
            }
        }

        public void SaveSettings()
        {
            try
            {
                var settings = new LabelStyleSettings
                {
                    DotStyle = DotStyle,
                    TextBackgroundColor = ColorToString(TextBackgroundColor),
                    TextForegroundColor = ColorToString(TextForegroundColor),
                    TextBackgroundOpacity = TextBackgroundOpacity,
                    LabelScale = LabelScale
                };

                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);
            }
            catch
            {
                // 保存失败时静默处理
            }
        }

        [RelayCommand]
        public void ResetToDefaults()
        {
            DotStyle = DotStyleType.Circle;
            TextBackgroundColor = Colors.Black;
            TextForegroundColor = Colors.White;
            TextBackgroundOpacity = 0.5;
            LabelScale = 1.0;
        }


        [RelayCommand]
        public void ZoomInLabel()
        {
            LabelScale = Math.Min(LabelScale + 0.1, 3.0);
            SaveSettings();
        }


        [RelayCommand]
        public void ZoomOutLabel()
        {
            LabelScale = Math.Max(LabelScale - 0.1, 0.3);
            SaveSettings();
        }

        // --- 颜色转换辅助方法 ---
        private static Color ColorFromString(string colorName)
        {
            try
            {
                return (Color)ColorConverter.ConvertFromString(colorName);
            }
            catch
            {
                return Colors.White;
            }
        }

        private static string ColorToString(Color color)
        {
            // 尝试匹配常用颜色名称
            if (color == Colors.White) return "White";
            if (color == Colors.Black) return "Black";
            if (color == Colors.RoyalBlue) return "RoyalBlue";
            if (color == Colors.Transparent) return "Transparent";

            // 否则返回十六进制格式
            return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        }
    }
}
