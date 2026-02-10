using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace LabelMinusinWPF
{
    /// <summary>
    /// ImagewithLabelShow.xaml 的交互逻辑
    /// </summary>
    public partial class ImagewithLabelShow : UserControl
    {
        public ImagewithLabelShow()
        {
            InitializeComponent();
        }

        #region 拖拽标注点逻辑
        private bool _isDragging = false;

        private void Label_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is ImageLabel)
            {
                _isDragging = true;
                fe.CaptureMouse(); // 开始捕获鼠标

                // 同步选中状态到 ViewModel
                if (this.DataContext is ImageInfo imageInfo)
                {
                    imageInfo.SelectedLabel = (ImageLabel)fe.DataContext;
                }

                e.Handled = true;
            }
        }

        private void Label_MouseMove(object sender, MouseEventArgs e)
        {
            // 通过 Mouse.Captured 直接获取当前抓着的元素
            if (_isDragging && Mouse.Captured is FrameworkElement fe && fe.DataContext is ImageLabel label)
            {
                e.Handled = true;

                var pos = e.GetPosition(MarkerItemsControl);

                // 直接更新 DataContext 中的数据
                label.X = (float)Math.Clamp(pos.X / MarkerItemsControl.ActualWidth, 0, 1);
                label.Y = (float)Math.Clamp(pos.Y / MarkerItemsControl.ActualHeight, 0, 1);
            }
        }

        private void Label_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                if (Mouse.Captured != null)
                {
                    Mouse.Capture(null); // 这会释放当前任何正在捕获鼠标的元素
                }
                e.Handled = true;
            }
        }
        #endregion

    }

    public class ImageRelativePositionConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values[0] is not float relative || values[1] is not Image img || img.Source == null)
                return 0.0;

            // 直接从 Image 控件获取所需的 Actual 和 Pixel 尺寸
            double controlWidth = img.ActualWidth;
            double controlHeight = img.ActualHeight;
            double pixelWidth = img.Source.Width;
            double pixelHeight = img.Source.Height;

            double scale = Math.Min(controlWidth / pixelWidth, controlHeight / pixelHeight);
            double displayedWidth = pixelWidth * scale;
            double displayedHeight = pixelHeight * scale;

            bool isX = (string)parameter == "X";
            double offset = isX ? (controlWidth - displayedWidth) / 2 : (controlHeight - displayedHeight) / 2;
            double size = isX ? displayedWidth : displayedHeight;

            return offset + (relative * size);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => null;
    }
}
