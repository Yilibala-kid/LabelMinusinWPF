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
                var pos = _pendingDragPosition.Value;
                _draggingLabel.X = pos.X;
                _draggingLabel.Y = pos.Y;
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

        // 2. 鼠标移动
        private void OnViewportMove(object sender, MouseEventArgs e)
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
        private void OnViewportUp(object sender, MouseButtonEventArgs e)
        {
            // 如果鼠标按下和抬起的距离很短，且没有在拖动标签 -> 视为点击，新建标签
            if ((e.GetPosition(ViewportGrid) - _mouseDownPosition).Length < 2 && OnlySEE!=true)
            {
                AddLabel(e.GetPosition(TargetImage)); // 直接传入相对于图片的坐标
            }
            ViewportGrid.ReleaseMouseCapture();
            _draggingLabel = null;
        }

        private void OnViewportWheel(object sender, MouseWheelEventArgs e)
        {
            // --- 新增逻辑：锁定状态下的平移处理 ---
            // 如果横向或纵向被锁定，滚轮将变为平移操作
            if (IsXLocked || IsYLocked)
            {
                double scrollSpeed = 0.5; // 平移速度系数，可根据手感调整
                double delta = e.Delta * scrollSpeed;

                if (IsXLocked && !IsYLocked)
                {
                    // 锁定横向时，滚轮控制纵向平移
                    this.OffsetY += delta;
                }
                else if (IsYLocked && !IsXLocked)
                {
                    // 锁定纵向时，滚轮控制横向平移
                    this.OffsetX += delta;
                }
                // 如果两者都锁定，你可以选择不操作，或者默认移动纵向

                e.Handled = true;
                return; // 跳过下方的缩放逻辑
            }

            // --- 原有的缩放逻辑 ---
            double zoomFactor = e.Delta > 0 ? 1.1 : 0.9;
            double oldScale = this.ZoomScale;
            double newScale = oldScale * zoomFactor;

            if (newScale < 0.1 || newScale > 30) return;

            Point mouseInViewport = e.GetPosition(ViewportGrid);

            // 计算缩放中心点补偿
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
            // 如果没捕获鼠标或没有拖拽对象，直接返回
            if (!LabelItemsControl.IsMouseCaptured || _draggingLabel == null) return;

            Point currentPos = e.GetPosition(LabelItemsControl);

            // 【位移判定】只有当鼠标移动距离超过系统阈值（默认约4像素）时，才认为是在”拖拽”
            if (!_isRealDragging)
            {
                double diffX = Math.Abs(currentPos.X - _dragStartPoint.X);
                double diffY = Math.Abs(currentPos.Y - _dragStartPoint.Y);

                if (diffX > SystemParameters.MinimumHorizontalDragDistance ||
                    diffY > SystemParameters.MinimumVerticalDragDistance)
                {
                    _isRealDragging = true;
                }
            }

            // 执行拖拽逻辑（使用节流机制）
            if (_isRealDragging)
            {
                Point mousePosInImage = e.GetPosition(TargetImage);

                if (TargetImage.ActualWidth > 0 && TargetImage.ActualHeight > 0)
                {
                    // 计算目标比例坐标
                    double newX = mousePosInImage.X / TargetImage.ActualWidth;
                    double newY = mousePosInImage.Y / TargetImage.ActualHeight;

                    // 存储待应用的位置（节流）
                    _pendingDragPosition = new Point(Math.Clamp(newX, 0, 1), Math.Clamp(newY, 0, 1));
                    _hasPendingDragUpdate = true;

                    // 启动节流计时器（如果未运行）
                    if (!_dragThrottleTimer.IsEnabled)
                    {
                        _dragThrottleTimer.Start();
                    }
                }
            }

            e.Handled = true;
        }
        private void OnLabelContainerUp(object sender, MouseButtonEventArgs e)
        {
            // 应用最终位置（确保拖动结束时位置正确）
            if (_hasPendingDragUpdate && _pendingDragPosition.HasValue && _draggingLabel != null)
            {
                _draggingLabel.X = _pendingDragPosition.Value.X;
                _draggingLabel.Y = _pendingDragPosition.Value.Y;
            }

            // 停止节流计时器
            _dragThrottleTimer.Stop();
            _hasPendingDragUpdate = false;
            _pendingDragPosition = null;

            if (LabelItemsControl.IsMouseCaptured)
            {
                LabelItemsControl.ReleaseMouseCapture();
            }

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
                // 1. 正常选中
                ShowingImage.SelectedLabel = label;

                // 2. 准备拖拽信息
                _draggingLabel = label;
                _dragStartPoint = e.GetPosition(LabelItemsControl); // 以容器为基准记录起点
                _isRealDragging = false;

                // 3. 强制捕获鼠标，确保鼠标移出标签范围也能继续拖动
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
                {
                    ShowingImage.DeleteLabelCommand.Execute(label);
                }
            }
        }
        #endregion

        #region 辅助逻辑方法

        // 辅助：新建标签 (保持原本逻辑)
        private void AddLabel(Point posInImage)
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

            Point snipEndImage = e.GetPosition(TargetImage);

            // 关键修改 1：计算【归一化矩形 (0.0~1.0)】，彻底解决不同控件大小截图不同步的问题！
            double imgW = TargetImage.ActualWidth;
            double imgH = TargetImage.ActualHeight;

            if (imgW == 0 || imgH == 0) return;

            Rect normRect = new Rect(
                Math.Min(_snipStartImage.X, snipEndImage.X) / imgW,
                Math.Min(_snipStartImage.Y, snipEndImage.Y) / imgH,
                Math.Abs(_snipStartImage.X - snipEndImage.X) / imgW,
                Math.Abs(_snipStartImage.Y - snipEndImage.Y) / imgH
            );

            // 过滤掉无效的微小点击
            if (normRect.Width > 0.01 && normRect.Height > 0.01)
            {
                if (IsRegionCaptureWithLabels)
                {
                    // 带标签框选模式：通知外部处理
                    SnappedWithLabels?.Invoke(this, normRect);
                }
                else if (IsSyncRequired)
                {
                    // 抛出归一化矩形，父窗口收到后可以直接转给另一个 ImageLabelViewer
                    Snipped?.Invoke(this, normRect);
                }
                else
                {
                    var myBmp = CaptureRegion(normRect);
                    if (myBmp != null)
                    {
                        Clipboard.SetImage(myBmp);
                    }
                }
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
            Point pos = TargetImage.TranslatePoint(new Point(0, 0), ContentGrid);
            // 计算位移（让图片居中显示，而不是死板地贴在左上角）如果你只想贴在左上角，直接设为 0 即可
            this.OffsetX = (ViewportGrid.ActualWidth - (TargetImage.ActualWidth * scale)) / 2- pos.X * scale;
            this.OffsetY = (ViewportGrid.ActualHeight - (TargetImage.ActualHeight * scale)) / 2-pos.Y * scale;
        }

        // 保持原来的接口，只是转发调用
        public void FitToView() => Fit("All");
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
                new PropertyMetadata(false, (d, e) => {
                    if (d is ImageLabelViewer ctrl && e.NewValue is bool isOn)
                    {
                        ctrl.SnipCanvas.Visibility = isOn ? Visibility.Visible : Visibility.Collapsed;
                        ctrl.ViewportGrid.Cursor = isOn ? Cursors.Cross : Cursors.Arrow;
                    }
                }));

        // 截图模式开关
        public bool IsScreenShotMode
        {
            get => (bool)GetValue(IsScreenShotModeProperty);
            set => SetValue(IsScreenShotModeProperty, value);
        }
        public static readonly DependencyProperty IsScreenShotModeProperty =
            DependencyProperty.Register(nameof(IsScreenShotMode), typeof(bool), typeof(ImageLabelViewer),
                new PropertyMetadata(false, (d, e) => {
                    if (d is ImageLabelViewer ctrl && e.NewValue is bool isOn)
                    {
                        ctrl.SnipCanvas.Visibility = isOn ? Visibility.Visible : Visibility.Collapsed;
                        ctrl.ViewportGrid.Cursor = isOn ? Cursors.Cross : Cursors.Arrow;
                    }
                }));
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
