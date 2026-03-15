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

        private void BgColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string colorName)
            {
                LabelStyleManager.Instance.TextBackgroundColor = colorName.ToLower() switch
                {
                    "white" => Colors.White,
                    "black" => Colors.Black,
                    "royalblue" => Color.FromRgb(65, 105, 225),
                    "green" => Colors.Green,
                    "red" => Colors.Red,
                    "yellow" => Colors.Yellow,
                    _ => Colors.Black
                };
                LabelStyleManager.Instance.SaveSettings();
            }
        }

        private void FgColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string colorName)
            {
                LabelStyleManager.Instance.TextForegroundColor = colorName.ToLower() switch
                {
                    "white" => Colors.White,
                    "black" => Colors.Black,
                    "royalblue" => Color.FromRgb(65, 105, 225),
                    "green" => Colors.Green,
                    "red" => Colors.Red,
                    "yellow" => Colors.Yellow,
                    _ => Colors.Black
                };
                LabelStyleManager.Instance.SaveSettings();
            }
        }
    }
}
