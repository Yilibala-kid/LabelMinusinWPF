using MahApps.Metro.Controls;
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
        public ImageInfo? ViewModel => DataContext as ImageInfo;

        #region 状态变量
        private bool _isDragging = false;
        private bool _isCanvasClick = false;
        private Point _canvasClickPoint;
        private Point _startDragPoint;
        // 新增：记录拖拽开始时标签的原始位置，用于计算位移，避免“瞬移”
        private double _startLabelX;
        private double _startLabelY;
        #endregion

        #region 鼠标按下：区分点击画布与点击标签

        // 1. 父容器按下（点击在空白画布上）
        private void LabelItemsControl_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 因为标签自身的 Label_LeftMouseDown 已经标记了 e.Handled = true
            // 所以能走到这里的，必然是点击在了没有标签的空白区域
            var clickPoint = e.GetPosition(LabelItemsControl);

            _isCanvasClick = true;
            _canvasClickPoint = clickPoint;

            // 捕获鼠标，准备后续判断是新建标签还是拖动画布
            LabelItemsControl.CaptureMouse();
            e.Handled = true;
        }

        // 2. 子元素按下（点击在已有标签上）
        private void Label_LeftMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border { DataContext: ImageLabel label })
            {
                if (ViewModel == null) return;

                // 选中标签
                ViewModel.SelectedLabel = label;

                // 【修复】正式开启拖拽状态
                _isDragging = true;
                _startDragPoint = e.GetPosition(LabelItemsControl);

                // 【修复】记录标签当前的起始坐标
                _startLabelX = label.X;
                _startLabelY = label.Y;

                // 由父容器捕获鼠标，防止拖动过快时鼠标脱离标签范围
                LabelItemsControl.CaptureMouse();
                e.Handled = true;
            }
        }
        #endregion

        #region 鼠标移动：处理拖拽标签或拖动画布
        private void LabelItemsControl_MouseMove(object sender, MouseEventArgs e)
        {
            // 如果没有捕获鼠标，直接返回
            if (!LabelItemsControl.IsMouseCaptured) return;

            var currentPoint = e.GetPosition(LabelItemsControl);

            // 场景 A：正在拖拽已有标签
            if (_isDragging && ViewModel?.SelectedLabel != null)
            {
                if (LabelItemsControl.ActualWidth > 0 && LabelItemsControl.ActualHeight > 0)
                {
                    // 【修复】计算相对位移 (Delta)，实现平滑拖拽
                    double dx = (currentPoint.X - _startDragPoint.X) / LabelItemsControl.ActualWidth;
                    double dy = (currentPoint.Y - _startDragPoint.Y) / LabelItemsControl.ActualHeight;

                    // 更新坐标，并限制在 0~1 范围内
                    ViewModel.SelectedLabel.X = Math.Clamp(_startLabelX + dx, 0, 1);
                    ViewModel.SelectedLabel.Y = Math.Clamp(_startLabelY + dy, 0, 1);
                }
            }
            // 场景 B：在画布上按住并移动（判断是否达到拖动画布的阈值）
            else if (_isCanvasClick)
            {
                var diff = currentPoint - _canvasClickPoint;
                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    // 移动超过阈值，不再视为“点击新建标签”，而是触发拖动画布
                    LabelItemsControl.ReleaseMouseCapture();
                    _isDragging = false;
                    _isCanvasClick = false;

                    RaiseEvent(new RequestDragImageEventArgs(RequestDragImageEvent, e.GetPosition(this)));
                }
            }
        }
        #endregion

        #region 鼠标抬起：释放状态与新建标签
        // 【修复】合并 MouseUp 逻辑，只保留这一个方法
        private void LabelItemsControl_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (LabelItemsControl.IsMouseCaptured)
            {
                LabelItemsControl.ReleaseMouseCapture();

                // 如果是在画布上点击，并且没有触发画布拖动，则新建标签
                if (_isCanvasClick)
                {
                    var clickPoint = e.GetPosition(LabelItemsControl);
                    AddNewLabel(clickPoint);
                }

                // 统一重置所有状态
                _isDragging = false;
                _isCanvasClick = false;
            }
        }
        #endregion

        // 右键删除逻辑和 AddNewLabel 保持你原本的实现即可，没有问题。
        private void AddNewLabel(Point position)
        {
            if (ViewModel == null) return;

            // 计算相对坐标 (0.0 - 1.0)
            double relativeX = Math.Clamp(position.X / LabelItemsControl.ActualWidth, 0, 1);
            double relativeY = Math.Clamp(position.Y / LabelItemsControl.ActualHeight, 0, 1);

            // 创建新标签
            var newLabel = new ImageLabel
            {
                X = relativeX,
                Y = relativeY,
                Index = ViewModel.Labels.Count + 1,
                Text = $"标签{ViewModel.Labels.Count + 1}"
            };

            // 添加到集合
            ViewModel.Labels.Add(newLabel);

            // 选中新创建的标签
            ViewModel.SelectedLabel = newLabel;
        }
        // 4. 右键按下：删除逻辑
        private void Label_RightMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border { DataContext: ImageLabel label } && ViewModel != null)
            {
                label.IsDeleted = true;

                // 如果删除的是当前选中的，清理选中状态
                if (ViewModel.SelectedLabel == label)
                {
                    ViewModel.SelectedLabel = null;
                }

                ViewModel.RefreshIndices();
            }
            e.Handled = true;
        }

        #region 外部显示控制/自设参数
        // 控制文字显示的属性
        // 声明路由事件（冒泡类型）
        public static readonly RoutedEvent RequestDragImageEvent = EventManager.RegisterRoutedEvent(
            "RequestDragImage", RoutingStrategy.Bubble, typeof(EventHandler<RequestDragImageEventArgs>), typeof(ImagewithLabelShow));

        public event EventHandler<RequestDragImageEventArgs> RequestDragImage
        {
            add { AddHandler(RequestDragImageEvent, value); }
            remove { RemoveHandler(RequestDragImageEvent, value); }
        }
        public bool IsTextVisible
        {
            get => (bool)GetValue(IsTextVisibleProperty);
            set => SetValue(IsTextVisibleProperty, value);
        }

        public static readonly DependencyProperty IsTextVisibleProperty =
            DependencyProperty.Register(nameof(IsTextVisible), typeof(bool), typeof(ImagewithLabelShow), new PropertyMetadata(true));

        // --- 新增：控制点显示的属性 ---
        public bool IsIndexVisible
        {
            get => (bool)GetValue(IsIndexVisibleProperty);
            set => SetValue(IsIndexVisibleProperty, value);
        }

        public static readonly DependencyProperty IsIndexVisibleProperty =
            DependencyProperty.Register(nameof(IsIndexVisible), typeof(bool), typeof(ImagewithLabelShow), new PropertyMetadata(true));


        #endregion

    }
    public class RequestDragImageEventArgs : RoutedEventArgs
    {
        public Point MousePosition { get; set; }
        public RequestDragImageEventArgs(RoutedEvent routedEvent, Point position)
            : base(routedEvent) => MousePosition = position;
    }
    #region 图片坐标转换器
    public class ImageRelativePositionConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            double relative = (double)values[0];
            Image img = (Image)values[1];

            if (img.Source == null) return 0.0;

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

            return offset + relative * size;
        }

        public object[]? ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => null;
    }
    #endregion
}
