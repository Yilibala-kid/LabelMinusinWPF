using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.Globalization;
using System.Reflection.Emit;
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
using MahApps.Metro.Controls;

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

        public ImageInfo? ShowingImage => DataContext as ImageInfo;

        #region 第一层：整体拖动与缩放
        private Point _lastMousePosition; // 记录鼠标按下时的坐标（用于计算位移量）
        private Point _mouseDownPosition; // 用于计算是否超过点击阈值

        private void Viewport_MouseLeftDown(object sender, MouseButtonEventArgs e)
        {
            _mouseDownPosition = _lastMousePosition = e.GetPosition(ViewportGrid);

            if (ShowingImage != null)
            {
                ShowingImage.SelectedLabel = null;
            }

            ViewportGrid.CaptureMouse();
        }

        // 2. 鼠标移动
        private void Viewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (!ViewportGrid.IsMouseCaptured) return;// 如果没有捕获鼠标，什么都不做

            var currentPos = e.GetPosition(ViewportGrid);

            var delta = currentPos - _lastMousePosition;
            // 只有移动距离超过阈值才算拖动（防抖）
            if ((currentPos - _mouseDownPosition).Length > 2)
            {
                if (!IsXLocked) MyTranslateTransform.X += delta.X;
                if (!IsYLocked) MyTranslateTransform.Y += delta.Y;
            }
            _lastMousePosition = currentPos;
        }

        // 3. 鼠标抬起
        private void Viewport_MouseLeftUp(object sender, MouseButtonEventArgs e)
        {
            // 如果鼠标按下和抬起的距离很短，且没有在拖动标签 -> 视为点击，新建标签
            if ((e.GetPosition(ViewportGrid) - _mouseDownPosition).Length < 2)
            {
                AddNewLabel(e.GetPosition(TargetImage)); // 直接传入相对于图片的坐标
            }
            ViewportGrid.ReleaseMouseCapture();
            _draggingLabel = null;
        }

        private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)// 鼠标滚轮缩放
        {
            var st = MyScaleTransform;
            var tt = MyTranslateTransform;

            double zoom = e.Delta > 0 ? 1.1 : 0.9;
            Point relative = e.GetPosition(ContentGrid); // 鼠标相对于内容的中心点

            // 简单的中心缩放公式
            st.ScaleX *= zoom;
            st.ScaleY *= zoom;
            tt.X = relative.X * st.ScaleX / zoom * (1 - zoom) + tt.X; // 修正位移以保持鼠标下内容不动
            tt.Y = relative.Y * st.ScaleY / zoom * (1 - zoom) + tt.Y;
        }
        #endregion


        #region 第二层：处理标签移动
        private void LabelItemsControl_MouseMove(object sender, MouseEventArgs e)
        {
            // 如果没有捕获鼠标，什么都不做
            if (!LabelItemsControl.IsMouseCaptured) return;

            var currentPos = e.GetPosition(LabelItemsControl);

            // 正在拖拽标签
            if (_draggingLabel != null)
            {
                Point posInImage = e.GetPosition(TargetImage);

                if (TargetImage.ActualWidth > 0 && TargetImage.ActualHeight > 0)
                {
                    _draggingLabel.X = posInImage.X / TargetImage.ActualWidth;
                    _draggingLabel.Y = posInImage.Y / TargetImage.ActualHeight;
                }
            }
            _lastMousePosition = currentPos;
            e.Handled = true; // 阻止事件冒泡，防止 ViewportGrid 误认为是拖动空白处
        }
        private void LabelItemsControl_MouseLeftUp(object sender, MouseButtonEventArgs e)
        {

            LabelItemsControl.ReleaseMouseCapture();
            _draggingLabel = null;
            e.Handled = true;
        }
        #endregion

        #region 第三层(上)：标签选中与删除
        private ImageLabel? _draggingLabel; // 当前拖动的标签，非空即代表正在拖拽标签
        private void LabelNode_LeftMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement elm && elm.DataContext is ImageLabel label && ShowingImage != null)
            {
                _draggingLabel = label;

                ShowingImage.SelectedLabel = label;

                LabelItemsControl.CaptureMouse();

                _lastMousePosition = e.GetPosition(ViewportGrid);

                e.Handled = true;
            }
        }
        private void LabelNode_RightMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: ImageLabel label } && ShowingImage != null)
            {
                label.IsDeleted = true;
                if (ShowingImage.SelectedLabel == label) ShowingImage.SelectedLabel = null;
                e.Handled = true;
            }
        }
        #endregion

        #region 辅助逻辑方法

        // 辅助：新建标签 (保持原本逻辑)
        private void AddNewLabel(Point posInImage)
        {
            if (ShowingImage == null || TargetImage.ActualWidth == 0) return;

            ShowingImage.Labels.Add(new ImageLabel
            {
                X = Math.Clamp(posInImage.X / TargetImage.ActualWidth, 0, 1),
                Y = Math.Clamp(posInImage.Y / TargetImage.ActualHeight, 0, 1),
                Index = ShowingImage.Labels.Count + 1,
                Text = $"标签{ShowingImage.Labels.Count + 1}",
            });
            ShowingImage.SelectedLabel = ShowingImage.Labels.Last();
        }

        #endregion

        #region 第三层(下)：文本框编辑相关
        private void LabelText_MouseLeftDown(object sender, MouseButtonEventArgs e)
        {
            // 双击判定
            if (e.ClickCount == 2)
            {
                var border = sender as Border;
                var grid = border?.Parent as Grid;

                // 在 DataTemplate 内部查找对应的 TextBox
                // 如果你用了 x:Name，也可以通过 grid.FindName 查找
                var textBox = grid?.Children.OfType<TextBox>().FirstOrDefault();

                if (border != null && textBox != null)
                {
                    border.Visibility = Visibility.Collapsed;
                    textBox.Visibility = Visibility.Visible;

                    // 必须延迟一点点 Focus，确保控件已渲染
                    textBox.Focus();
                    textBox.SelectAll();
                }
            }
            e.Handled = true; // 阻止冒泡到 Canvas 触发新建标签
        }
        // 失去焦点时，自动切回显示模式
        private void LabelEdit_LostFocus(object sender, RoutedEventArgs e)
        {
            var textBox = sender as TextBox;
            var grid = textBox?.Parent as Grid;
            var border = grid?.Children.OfType<Border>().FirstOrDefault();

            if (border != null && textBox != null)
            {
                textBox.Visibility = Visibility.Collapsed;
                border.Visibility = Visibility.Visible;
            }
        }
        private void LabelEdit_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape || (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control))
            {
                // 强制让父容器获取焦点，从而触发 TextBox 的 LostFocus 事件
                ViewportGrid.Focus();
            }
        }
        #endregion

        #region 暴露给外部的状态属性/公开控制方法
       
        public bool IsXLocked { get; set; } = false;
        public bool IsYLocked { get; set; } = false;

        // 适应全屏 (Fit to Page)
        public void Fit(string mode = "All")
        {
            if (TargetImage.ActualWidth == 0 || ViewportGrid.ActualWidth == 0) return;

            double sX = ViewportGrid.ActualWidth / TargetImage.ActualWidth;
            double sY = ViewportGrid.ActualHeight / TargetImage.ActualHeight;

            double scale = mode switch
            {
                "Width" => sX,
                "Height" => sY,
                _ => Math.Min(sX, sY)
            };

            MyScaleTransform.ScaleX = MyScaleTransform.ScaleY = scale;
            MyTranslateTransform.X = MyTranslateTransform.Y = 0; // 归零位移，左上角对齐
        }

        // 保持原来的接口，只是转发调用
        public void FitToView() => Fit("All");
        public void FitToWidth() => Fit("Width");
        public void FitToHeight() => Fit("Height");

        #endregion

        #region 外部显示控制/自设参数
        public bool IsTextVisible
        {
            get => (bool)GetValue(IsTextVisibleProperty);
            set => SetValue(IsTextVisibleProperty, value);
        }

        public static readonly DependencyProperty IsTextVisibleProperty =
            DependencyProperty.Register(
                nameof(IsTextVisible),
                typeof(bool),
                typeof(ImagewithLabelShow),
                new PropertyMetadata(true)
            );

        // --- 新增：控制点显示的属性 ---
        public bool IsIndexVisible
        {
            get => (bool)GetValue(IsIndexVisibleProperty);
            set => SetValue(IsIndexVisibleProperty, value);
        }

        public static readonly DependencyProperty IsIndexVisibleProperty =
            DependencyProperty.Register(
                nameof(IsIndexVisible),
                typeof(bool),
                typeof(ImagewithLabelShow),
                new PropertyMetadata(true)
            );

        #endregion
    }

    #region 图片坐标转换器
    public class ImageRelativePositionConverter : IMultiValueConverter
    {
        public object Convert(
            object[] values,
            Type targetType,
            object parameter,
            CultureInfo culture
        )
        {
            // values[0] 是相对坐标 (0~1)
            // values[1] 是 Image 控件
            if (
                values[0] is not double relative
                || values[1] is not Image img
                || img.Source == null
            )
                return 0.0;

            bool isX = (string)parameter == "X";
            double size = isX ? img.ActualWidth : img.ActualHeight;

            return relative * size;
        }

        public object[]? ConvertBack(
            object value,
            Type[] targetTypes,
            object parameter,
            CultureInfo culture
        ) => null;
    }
    #endregion
}
