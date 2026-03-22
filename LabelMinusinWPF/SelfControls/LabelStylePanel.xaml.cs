using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LabelMinusinWPF.SelfControls
{
    public partial class LabelStylePanel : UserControl
    {
        public LabelStylePanel()
        {
            InitializeComponent();
        }

        // 解析颜色名称为 Color 对象
        private static Color ParseColor(string name) => name.ToLower() switch
        {
            "white" => Colors.White,
            "black" => Colors.Black,
            "royalblue" => Color.FromRgb(65, 105, 225),
            "green" => Colors.Green,
            "red" => Colors.Red,
            "yellow" => Colors.Yellow,
            _ => Colors.Black
        };

        // 设置背景颜色
        private void BgColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string colorName)
            {
                LabelStyleManager.Instance.TextBackgroundColor = ParseColor(colorName);
                LabelStyleManager.Instance.SaveSettings();
            }
        }

        // 设置前景颜色
        private void FgColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string colorName)
            {
                LabelStyleManager.Instance.TextForegroundColor = ParseColor(colorName);
                LabelStyleManager.Instance.SaveSettings();
            }
        }
    }
}
