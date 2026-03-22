using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LabelMinusinWPF.Common;
using LabelMinusinWPF.SelfControls;

namespace LabelMinusinWPF
{

// ImageLabelViewer.xaml 的交互逻辑

    public partial class ImageLabelViewer : UserControl
    {
        public ImageLabelViewer()
        {
            InitializeComponent();
            // 初始化拖动节流计时器（约60fps）
            _dragThrottleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _dragThrottleTimer.Tick += OnDragTick;
        }

        public OneImage? ShowingImage => DataContext as OneImage;

        #region 拖动节流优化
        private readonly DispatcherTimer _dragThrottleTimer;
        private Point? _pendingDragPosition; // 待应用的目标位置
        private bool _hasPendingDragUpdate;

        private void OnDragTick(object? sender, EventArgs e)
        {
            if (_pendingDragPosition.HasValue && _draggingLabel != null && TargetImage.ActualWidth > 0 && TargetImage.ActualHeight > 0)
            {
                _draggingLabel.X = _pendingDragPosition.Value.X;
                _draggingLabel.Y = _pendingDragPosition.Value.Y;
            }
            _hasPendingDragUpdate = false;
            _dragThrottleTimer.Stop();
        }
        #endregion

        #region 第一层：整体拖动与缩放
        private Point _lastMousePosition; // 记录鼠标按下时的坐标（用于计算位移量）
        private Point _mouseDownPosition; // 用于计算是否超过点击阈值

        private void OnViewportDown(object sender, MouseButtonEventArgs e)
        {
            _mouseDownPosition = _lastMousePosition = e.GetPosition(ViewportGrid);

            ShowingImage?.SelectedLabel = null;

            ViewportGrid.CaptureMouse();
        }

        private void OnViewportMove(object sender, MouseEventArgs e)
        {
            if (!ViewportGrid.IsMouseCaptured) return;
            var currentPos = e.GetPosition(ViewportGrid);
            var delta = currentPos - _lastMousePosition;
            if ((currentPos - _mouseDownPosition).Length > 2)
            {
                if (!IsXLocked) this.OffsetX += delta.X;
                if (!IsYLocked) this.OffsetY += delta.Y;
            }
            _lastMousePosition = currentPos;
        }

        private void OnViewportUp(object sender, MouseButtonEventArgs e)
        {
            if ((e.GetPosition(ViewportGrid) - _mouseDownPosition).Length < 2 && OnlySEE != true)
                AddLabel(e.GetPosition(TargetImage));
            ViewportGrid.ReleaseMouseCapture();
            _draggingLabel = null;
        }

        private void OnViewportWheel(object sender, MouseWheelEventArgs e)
        {
            if (IsXLocked || IsYLocked)
            {
                double delta = e.Delta * 0.5;
                if (IsXLocked) this.OffsetY += delta;
                else if (IsYLocked) this.OffsetX += delta;
                e.Handled = true;
                return;
            }

            double zoomFactor = e.Delta > 0 ? 1.1 : 0.9;
            double oldScale = this.ZoomScale;
            double newScale = oldScale * zoomFactor;
            if (newScale < 0.1 || newScale > 30) return;

            Point mouseInViewport = e.GetPosition(ViewportGrid);
            double absX = (mouseInViewport.X - this.OffsetX) / oldScale;
            double absY = (mouseInViewport.Y - this.OffsetY) / oldScale;

            this.ZoomScale = newScale;
            this.OffsetX = mouseInViewport.X - (absX * newScale);
            this.OffsetY = mouseInViewport.Y - (absY * newScale);
            e.Handled = true;
        }
        #endregion

        #region 第二层：处理标签移动
        private void OnLabelContainerMove(object sender, MouseEventArgs e)
        {
            if (!LabelItemsControl.IsMouseCaptured || _draggingLabel == null) return;

            Point currentPos = e.GetPosition(LabelItemsControl);
            if (!_isRealDragging)
            {
                double diffX = Math.Abs(currentPos.X - _dragStartPoint.X);
                double diffY = Math.Abs(currentPos.Y - _dragStartPoint.Y);
                if (diffX > SystemParameters.MinimumHorizontalDragDistance || diffY > SystemParameters.MinimumVerticalDragDistance)
                    _isRealDragging = true;
            }

            if (_isRealDragging && TargetImage.ActualWidth > 0 && TargetImage.ActualHeight > 0)
            {
                Point mousePosInImage = e.GetPosition(TargetImage);
                _pendingDragPosition = new Point(Math.Clamp(mousePosInImage.X / TargetImage.ActualWidth, 0, 1), Math.Clamp(mousePosInImage.Y / TargetImage.ActualHeight, 0, 1));
                _hasPendingDragUpdate = true;
                if (!_dragThrottleTimer.IsEnabled) _dragThrottleTimer.Start();
            }
            e.Handled = true;
        }

        private void OnLabelContainerUp(object sender, MouseButtonEventArgs e)
        {
            if (_hasPendingDragUpdate && _pendingDragPosition.HasValue && _draggingLabel != null)
            {
                _draggingLabel.X = _pendingDragPosition.Value.X;
                _draggingLabel.Y = _pendingDragPosition.Value.Y;
            }
            _dragThrottleTimer.Stop();
            _hasPendingDragUpdate = false;
            _pendingDragPosition = null;
            if (LabelItemsControl.IsMouseCaptured) LabelItemsControl.ReleaseMouseCapture();
            _draggingLabel = null;
            _isRealDragging = false;
            e.Handled = true;
        }
        #endregion

        #region 第三层(上)：标签选中与删除
        private OneLabel? _draggingLabel; // 当前拖动的标签，非空即代表正在拖拽标签
        private Point _dragStartPoint;      // 鼠标按下的起始点（用于判定位移）
        private bool _isRealDragging;       // 是否真正开始移动的标志
        private void LabelNode_LeftMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement elm && elm.DataContext is OneLabel label && ShowingImage != null)
            {
                ShowingImage.SelectedLabel = label;
                _draggingLabel = label;
                _dragStartPoint = e.GetPosition(LabelItemsControl);
                _isRealDragging = false;
                LabelItemsControl.CaptureMouse();
                e.Handled = true;
            }
        }
        private void LabelNode_RightMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: OneLabel label } && ShowingImage != null)
            {
                ShowingImage.SelectedLabel = label;
                if (ShowingImage.DeleteLabelCommand.CanExecute(label))
                    ShowingImage.DeleteLabelCommand.Execute(label);
            }
        }
        #endregion

        #region 辅助逻辑方法

        private void AddLabel(Point posInImage)
        {
            if (ShowingImage == null || TargetImage.ActualWidth == 0) return;
            Point normalizedPoint = new(Math.Clamp(posInImage.X / TargetImage.ActualWidth, 0, 1), Math.Clamp(posInImage.Y / TargetImage.ActualHeight, 0, 1));
            if (ShowingImage.AddLabelCommand.CanExecute(normalizedPoint))
                ShowingImage.AddLabelCommand.Execute(normalizedPoint);
        }

        #endregion

        #region 第三层(下)：文本点击
        private void LabelText_MouseLeftDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true; // 阻止冒泡到 Canvas 触发新建标签
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
            Canvas.SetLeft(SnipRectangle, Math.Min(_snipStartUI.X, currentUI.X));
            Canvas.SetTop(SnipRectangle, Math.Min(_snipStartUI.Y, currentUI.Y));
            SnipRectangle.Width = Math.Abs(_snipStartUI.X - currentUI.X);
            SnipRectangle.Height = Math.Abs(_snipStartUI.Y - currentUI.Y);
        }

        private void SnipCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isSnipping) return;
            _isSnipping = false;
            SnipCanvas.ReleaseMouseCapture();
            SnipRectangle.Visibility = Visibility.Collapsed;

            double imgW = TargetImage.ActualWidth;
            double imgH = TargetImage.ActualHeight;
            if (imgW == 0 || imgH == 0) return;

            Point snipEndImage = e.GetPosition(TargetImage);
            Rect normRect = new(
                Math.Min(_snipStartImage.X, snipEndImage.X) / imgW,
                Math.Min(_snipStartImage.Y, snipEndImage.Y) / imgH,
                Math.Abs(_snipStartImage.X - snipEndImage.X) / imgW,
                Math.Abs(_snipStartImage.Y - snipEndImage.Y) / imgH
            );

            if (normRect.Width > 0.01 && normRect.Height > 0.01)
            {
                if (IsRegionCaptureWithLabels)
                    SnappedWithLabels?.Invoke(this, normRect);
                else if (IsSyncRequired)
                    Snipped?.Invoke(this, normRect);
                else if (CaptureRegion(normRect) is { } myBmp)
                    _ = ScreenshotHelper.TrySetClipboardImage(myBmp);
            }
        }

        private BitmapSource? CaptureRegion(Rect normRect)
        {
            if (TargetImage.Source is not BitmapSource bitmapSource) return null;
            return ScreenshotHelper.CropRegion(bitmapSource, normRect);
        }

        /// <summary>
        /// 捕获指定区域（图片+该区域内的标签）
        /// 返回：(截图Bitmap, 选区内的标签列表)
        /// </summary>
        public (BitmapSource? Image, List<OneLabel> LabelsInRegion)? CaptureRegionWithLabels(Rect normRect)
        {
            if (ShowingImage == null || TargetImage.Source is not BitmapSource bitmapSource) return null;
            return ScreenshotHelper.CaptureRegionWithLabels(bitmapSource, normRect, ShowingImage.ActiveLabels);
        }



        #endregion

        #region 暴露给外部的状态属性/公开控制方法
        public bool IsXLocked { get; set; }
        public bool IsYLocked { get; set; }

        public void Fit(string mode = "All")
        {
            if (TargetImage.ActualWidth == 0 || ViewportGrid.ActualWidth == 0) return;
            double sX = ViewportGrid.ActualWidth / TargetImage.ActualWidth;
            double sY = ViewportGrid.ActualHeight / TargetImage.ActualHeight;
            double scale = mode switch { "Width" => sX, "Height" => sY, _ => Math.Min(sX, sY) };
            this.ZoomScale = scale;
            Point pos = TargetImage.TranslatePoint(new Point(0, 0), ContentGrid);
            this.OffsetX = (ViewportGrid.ActualWidth - (TargetImage.ActualWidth * scale)) / 2 - pos.X * scale;
            this.OffsetY = (ViewportGrid.ActualHeight - (TargetImage.ActualHeight * scale)) / 2 - pos.Y * scale;
        }

        public void FitToView() => Fit();
        public void FitToWidth() => Fit("Width");
        public void FitToHeight() => Fit("Height");

        #endregion

        #region 外部显示控制/自设参数
        // 当截图完成时，通知外部（父窗口）
        public event EventHandler<Rect>? Snipped;
        // 当带标签截图完成时，通知外部（父窗口）
        public event EventHandler<Rect>? SnappedWithLabels;

        // 带标签框选模式开关
        public bool IsRegionCaptureWithLabels
        {
            get => (bool)GetValue(IsRegionCaptureWithLabelsProperty);
            set => SetValue(IsRegionCaptureWithLabelsProperty, value);
        }
        public static readonly DependencyProperty IsRegionCaptureWithLabelsProperty =
            DependencyProperty.Register(nameof(IsRegionCaptureWithLabels), typeof(bool), typeof(ImageLabelViewer),
                new PropertyMetadata(false, OnSnipModeChanged));

        private static void OnSnipModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ImageLabelViewer ctrl && e.NewValue is bool isOn)
            {
                ctrl.SnipCanvas.Visibility = isOn ? Visibility.Visible : Visibility.Collapsed;
                ctrl.ViewportGrid.Cursor = isOn ? Cursors.Cross : Cursors.Arrow;
            }
        }

        public bool IsScreenShotMode
        {
            get => (bool)GetValue(IsScreenShotModeProperty);
            set => SetValue(IsScreenShotModeProperty, value);
        }
        public static readonly DependencyProperty IsScreenShotModeProperty =
            DependencyProperty.Register(nameof(IsScreenShotMode), typeof(bool), typeof(ImageLabelViewer),
                new PropertyMetadata(false, OnSnipModeChanged));
        // 供外部调用，用于实现”同步”裁剪相同区域
        public BitmapSource? GetImageRegion(Rect normRect) => CaptureRegion(normRect);

        // 是否需要同步模式（true则通知外部，false则直接存剪贴板）
        public bool IsSyncRequired
        {
            get => (bool)GetValue(IsSyncRequiredProperty);
            set => SetValue(IsSyncRequiredProperty, value);
        }
        public static readonly DependencyProperty IsSyncRequiredProperty =
            DependencyProperty.Register(nameof(IsSyncRequired), typeof(bool), typeof(ImageLabelViewer), new PropertyMetadata(false));





        public double ZoomScale
        {
            get => (double)GetValue(ZoomScaleProperty);
            set => SetValue(ZoomScaleProperty, value);
        }
        public static readonly DependencyProperty ZoomScaleProperty =
            DependencyProperty.Register(nameof(ZoomScale), typeof(double), typeof(ImageLabelViewer), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public double OffsetX
        {
            get => (double)GetValue(OffsetXProperty);
            set => SetValue(OffsetXProperty, value);
        }
        public static readonly DependencyProperty OffsetXProperty =
            DependencyProperty.Register(nameof(OffsetX), typeof(double), typeof(ImageLabelViewer), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public double OffsetY
        {
            get => (double)GetValue(OffsetYProperty);
            set => SetValue(OffsetYProperty, value);
        }
        public static readonly DependencyProperty OffsetYProperty =
            DependencyProperty.Register(nameof(OffsetY), typeof(double), typeof(ImageLabelViewer), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
        public bool OnlySEE
        {
            get => (bool)GetValue(OnlySEEProperty);
            set => SetValue(OnlySEEProperty, value);
        }

        public static readonly DependencyProperty OnlySEEProperty =
            DependencyProperty.Register(
                nameof(OnlySEE),
                typeof(bool),
                typeof(ImageLabelViewer),
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
                typeof(ImageLabelViewer),
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
                typeof(ImageLabelViewer),
                new PropertyMetadata(true)
            );

        // --- 标签点样式（外部可自定义） ---
        public Style LabelDotStyle
        {
            get => (Style)GetValue(LabelDotStyleProperty);
            set => SetValue(LabelDotStyleProperty, value);
        }
        public static readonly DependencyProperty LabelDotStyleProperty =
            DependencyProperty.Register(
                nameof(LabelDotStyle),
                typeof(Style),
                typeof(ImageLabelViewer),
                new PropertyMetadata(null)
            );

        // --- 标签文字样式（外部可自定义） ---
        public Style LabelTextStyle
        {
            get => (Style)GetValue(LabelTextStyleProperty);
            set => SetValue(LabelTextStyleProperty, value);
        }
        public static readonly DependencyProperty LabelTextStyleProperty =
            DependencyProperty.Register(
                nameof(LabelTextStyle),
                typeof(Style),
                typeof(ImageLabelViewer),
                new PropertyMetadata(null)
            );

        // --- 文字背景透明度 ---
        public double TextBackgroundOpacity
        {
            get => (double)GetValue(TextBackgroundOpacityProperty);
            set => SetValue(TextBackgroundOpacityProperty, value);
        }
        public static readonly DependencyProperty TextBackgroundOpacityProperty =
            DependencyProperty.Register(
                nameof(TextBackgroundOpacity),
                typeof(double),
                typeof(ImageLabelViewer),
                new PropertyMetadata(0.7)
            );

        // --- 文字背景颜色 ---
        public Brush TextBackgroundColor
        {
            get => (Brush)GetValue(TextBackgroundColorProperty);
            set => SetValue(TextBackgroundColorProperty, value);
        }
        public static readonly DependencyProperty TextBackgroundColorProperty =
            DependencyProperty.Register(
                nameof(TextBackgroundColor),
                typeof(Brush),
                typeof(ImageLabelViewer),
                new PropertyMetadata(Brushes.White)
            );

        // --- 文字前景颜色 ---
        public Brush TextForegroundColor
        {
            get => (Brush)GetValue(TextForegroundColorProperty);
            set => SetValue(TextForegroundColorProperty, value);
        }
        public static readonly DependencyProperty TextForegroundColorProperty =
            DependencyProperty.Register(
                nameof(TextForegroundColor),
                typeof(Brush),
                typeof(ImageLabelViewer),
                new PropertyMetadata(Brushes.Black)
            );

        #endregion

        #region 标签缩放


        public double LabelScale
        {
            get => (double)GetValue(LabelScaleProperty);
            set => SetValue(LabelScaleProperty, value);
        }
        public static readonly DependencyProperty LabelScaleProperty =
            DependencyProperty.Register(
                nameof(LabelScale),
                typeof(double),
                typeof(ImageLabelViewer),
                new PropertyMetadata(1.0)
            );

        #endregion
    }

    #region 图片坐标转换器
    public class ImageRelativePositionConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values[0] is not double relative || values[1] is not Image img || img.Source == null)
                return 0.0;
            bool isX = (string)parameter == "X";
            return relative * (isX ? img.ActualWidth : img.ActualHeight);
        }

        public object[]? ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => null;
    }
    #endregion

    #region 多布尔值转 Visibility
    public class AllBoolToVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
            => values.All(v => v is true) ? Visibility.Visible : Visibility.Collapsed;

        public object[]? ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => null;
    }
    #endregion
}
