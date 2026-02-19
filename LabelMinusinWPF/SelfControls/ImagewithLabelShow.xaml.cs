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

            ShowingImage?.SelectedLabel = null;

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
                if (!IsXLocked) this.OffsetX += delta.X;
                if (!IsYLocked) this.OffsetY += delta.Y;
            }
            _lastMousePosition = currentPos;
        }

        // 3. 鼠标抬起
        private void Viewport_MouseLeftUp(object sender, MouseButtonEventArgs e)
        {
            // 如果鼠标按下和抬起的距离很短，且没有在拖动标签 -> 视为点击，新建标签
            if ((e.GetPosition(ViewportGrid) - _mouseDownPosition).Length < 2 && OnlySEE!=true)
            {
                AddNewLabel(e.GetPosition(TargetImage)); // 直接传入相对于图片的坐标
            }
            ViewportGrid.ReleaseMouseCapture();
            _draggingLabel = null;
        }

        private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)// 鼠标滚轮缩放
        {
            // 1. 获取缩放比例
            double zoomFactor = e.Delta > 0 ? 1.1 : 0.9;

            // 2. 获取当前状态（从依赖属性取，而不是从 Transform 对象取）
            double oldScale = this.ZoomScale;
            double newScale = oldScale * zoomFactor;

            // 限制缩放范围，防止缩到看不见或者内存爆炸
            if (newScale < 0.1 || newScale > 30) return;

            // 3. 计算“以鼠标为中心”的位移补偿
            // 我们需要知道鼠标相对于 ViewportGrid（容器）的位置
            Point mouseInViewport = e.GetPosition(ViewportGrid);

            // 计算鼠标点相对于内容原点的“原始像素”坐标
            // 公式：原始坐标 = (当前屏幕坐标 - 当前偏移) / 当前缩放
            double absX = (mouseInViewport.X - this.OffsetX) / oldScale;
            double absY = (mouseInViewport.Y - this.OffsetY) / oldScale;

            // 4. 更新依赖属性
            // 先改缩放
            this.ZoomScale = newScale;

            // 再更新位移：新位移 = 鼠标坐标 - (原始坐标 * 新缩放)
            // 这样可以确保原始坐标那个点在缩放后依然重合在鼠标坐标上
            this.OffsetX = mouseInViewport.X - (absX * newScale);
            this.OffsetY = mouseInViewport.Y - (absY * newScale);

            // 这一步非常重要：标记事件已处理，防止外层滚动条跟着动
            e.Handled = true;
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
                ShowingImage.SelectedLabel = label;
                if (ShowingImage.DeleteLabelCommand.CanExecute(label))
                {
                    ShowingImage.DeleteLabelCommand.Execute(label);
                }
            }
        }
        #endregion

        #region 辅助逻辑方法

        // 辅助：新建标签 (保持原本逻辑)
        private void AddNewLabel(Point posInImage)
        {
            if (ShowingImage == null || TargetImage.ActualWidth == 0) return;

            // 1. 计算归一化的百分比坐标
            double xPercent = Math.Clamp(posInImage.X / TargetImage.ActualWidth, 0, 1);
            double yPercent = Math.Clamp(posInImage.Y / TargetImage.ActualHeight, 0, 1);
            Point normalizedPoint = new(xPercent, yPercent);

            // 2. 调用新增命令
            // 它会自动完成：记录撤回点 -> 加入列表 -> 自动编号 -> 自动选中
            if (ShowingImage.AddLabelCommand.CanExecute(normalizedPoint))
            {
                ShowingImage.AddLabelCommand.Execute(normalizedPoint);
            }
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
        private void LabelEdit_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape || (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control))
            {
                // 强制让父容器获取焦点，从而触发 TextBox 的 LostFocus 事件
                ViewportGrid.Focus();
            }
        }
        #endregion

        #region 第四层：截图功能
        private bool _isSnipping = false;
        private Point _snipStartUI;    // 选框在 Canvas 上的起始点
        private Point _snipStartImage; // 选框在图片逻辑坐标上的起始点

        private void SnipCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isSnipping = true;
            _snipStartUI = e.GetPosition(SnipCanvas);

            // 关键：即使图片缩放了，GetPosition(TargetImage) 也会返回
            // 相对于原始图片比例的逻辑坐标（由 WPF 渲染树自动计算）
            _snipStartImage = e.GetPosition(TargetImage);

            SnipRectangle.Visibility = Visibility.Visible;
            Canvas.SetLeft(SnipRectangle, _snipStartUI.X);
            Canvas.SetTop(SnipRectangle, _snipStartUI.Y);
            SnipRectangle.Width = 0;
            SnipRectangle.Height = 0;

            SnipCanvas.CaptureMouse();
            e.Handled = true;
        }

        private void SnipCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isSnipping) return;

            Point currentUI = e.GetPosition(SnipCanvas);

            // 计算 UI 矩形位置和大小（支持反向拉框）
            double x = Math.Min(_snipStartUI.X, currentUI.X);
            double y = Math.Min(_snipStartUI.Y, currentUI.Y);
            double w = Math.Abs(_snipStartUI.X - currentUI.X);
            double h = Math.Abs(_snipStartUI.Y - currentUI.Y);

            Canvas.SetLeft(SnipRectangle, x);
            Canvas.SetTop(SnipRectangle, y);
            SnipRectangle.Width = w;
            SnipRectangle.Height = h;
        }

        private void SnipCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isSnipping) return;
            _isSnipping = false;
            SnipCanvas.ReleaseMouseCapture();
            SnipRectangle.Visibility = Visibility.Collapsed;

            Point snipEndImage = e.GetPosition(TargetImage);

            // 计算在图片上的逻辑区域 Rect
            Rect logicRect = new Rect(
                Math.Min(_snipStartImage.X, snipEndImage.X),
                Math.Min(_snipStartImage.Y, snipEndImage.Y),
                Math.Abs(_snipStartImage.X - snipEndImage.X),
                Math.Abs(_snipStartImage.Y - snipEndImage.Y)
            );

            if (logicRect.Width > 2 && logicRect.Height > 2)
            {
                if (IsSyncRequired)
                {
                    // 场景 A：同步模式
                    // 仅抛出事件，由父窗口统一处理左右两张图的合并和剪贴板操作
                    Snipped?.Invoke(this, logicRect);
                }
                else
                {
                    // 场景 B：独立模式
                    // 直接执行本地高清裁剪并存入剪贴板
                    var myBmp = CaptureOriginalBitmapRegion(logicRect);
                    if (myBmp != null)
                    {
                        Clipboard.SetImage(myBmp);
                    }
                }
            }
        }

        private BitmapSource? CaptureOriginalBitmapRegion(Rect logicRect)
        {
            if (TargetImage.Source is not BitmapSource bitmapSource) return null;

            // 计算比例：原始像素宽 / 控件当前显示的宽
            // 注意：ActualWidth 是控件在界面上的逻辑大小
            double ratioX = bitmapSource.PixelWidth / TargetImage.ActualWidth;
            double ratioY = bitmapSource.PixelHeight / TargetImage.ActualHeight;

            // 映射到真实像素坐标
            int pxX = (int)(logicRect.X * ratioX);
            int pxY = (int)(logicRect.Y * ratioY);
            int pxW = (int)(logicRect.Width * ratioX);
            int pxH = (int)(logicRect.Height * ratioY);

            // 边界安全检查
            pxX = Math.Max(0, Math.Min(pxX, bitmapSource.PixelWidth));
            pxY = Math.Max(0, Math.Min(pxY, bitmapSource.PixelHeight));
            pxW = Math.Min(pxW, bitmapSource.PixelWidth - pxX);
            pxH = Math.Min(pxH, bitmapSource.PixelHeight - pxY);

            if (pxW <= 0 || pxH <= 0) return null;

            try
            {
                // 从原始 Bitmap 直接切割，不经过 RenderTargetBitmap，保证 100% 清晰
                return new CroppedBitmap(bitmapSource, new Int32Rect(pxX, pxY, pxW, pxH));
            }
            catch { return null; }
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
            this.ZoomScale = scale;

            // 计算位移（让图片居中显示，而不是死板地贴在左上角）如果你只想贴在左上角，直接设为 0 即可
            this.OffsetX = (ViewportGrid.ActualWidth - (TargetImage.ActualWidth * scale)) / 2;
            this.OffsetY = (ViewportGrid.ActualHeight - (TargetImage.ActualHeight * scale)) / 2;
        }

        // 保持原来的接口，只是转发调用
        public void FitToView() => Fit("All");
        public void FitToWidth() => Fit("Width");
        public void FitToHeight() => Fit("Height");

        #endregion

        #region 外部显示控制/自设参数
        // 截图模式开关
        public bool IsScreenShotMode
        {
            get { return (bool)GetValue(IsScreenShotModeProperty); }
            set { SetValue(IsScreenShotModeProperty, value); }
        }
        public static readonly DependencyProperty IsScreenShotModeProperty =
            DependencyProperty.Register("IsScreenShotMode", typeof(bool), typeof(ImagewithLabelShow), 
                new PropertyMetadata(false, OnScreenShotModeChanged));

        private static void OnScreenShotModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (ImagewithLabelShow)d;
            bool isModeOn = (bool)e.NewValue;

            control.SnipCanvas.Visibility = isModeOn ? Visibility.Visible : Visibility.Collapsed;
            control.ViewportGrid.Cursor = isModeOn ? Cursors.Cross : Cursors.Arrow;
        }
        // 当截图完成时，通知外部（父窗口）
        public event EventHandler<Rect>? Snipped;

        // 供外部调用，用于实现“同步”裁剪相同区域
        public BitmapSource? GetImageRegion(Rect logicRect)
        {
            return CaptureOriginalBitmapRegion(logicRect);
        }
        // 是否需要同步模式（true则通知外部，false则直接存剪贴板）
        public bool IsSyncRequired
        {
            get { return (bool)GetValue(IsSyncRequiredProperty); }
            set { SetValue(IsSyncRequiredProperty, value); }
        }

        public static readonly DependencyProperty IsSyncRequiredProperty =
            DependencyProperty.Register("IsSyncRequired", typeof(bool), typeof(ImagewithLabelShow), new PropertyMetadata(false));





        public static readonly DependencyProperty ZoomScaleProperty =
                DependencyProperty.Register("ZoomScale", typeof(double), typeof(ImagewithLabelShow),
                    new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public double ZoomScale
        {
            get => (double)GetValue(ZoomScaleProperty);
            set => SetValue(ZoomScaleProperty, value);
        }

        // 2. X 轴平移依赖属性 (默认值为 0)
        public static readonly DependencyProperty OffsetXProperty =
            DependencyProperty.Register("OffsetX", typeof(double), typeof(ImagewithLabelShow),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public double OffsetX
        {
            get => (double)GetValue(OffsetXProperty);
            set => SetValue(OffsetXProperty, value);
        }

        // 3. Y 轴平移依赖属性
        public static readonly DependencyProperty OffsetYProperty =
            DependencyProperty.Register("OffsetY", typeof(double), typeof(ImagewithLabelShow),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public double OffsetY
        {
            get => (double)GetValue(OffsetYProperty);
            set => SetValue(OffsetYProperty, value);
        }
        public bool OnlySEE
        {
            get => (bool)GetValue(OnlySEEProperty);
            set => SetValue(OnlySEEProperty, value);
        }

        public static readonly DependencyProperty OnlySEEProperty =
            DependencyProperty.Register(
                nameof(OnlySEE),
                typeof(bool),
                typeof(ImagewithLabelShow),
                new PropertyMetadata(true)
            );
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
