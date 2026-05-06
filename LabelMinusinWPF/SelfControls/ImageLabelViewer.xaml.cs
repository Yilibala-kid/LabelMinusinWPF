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
            DataContextChanged += (_, _) => EditingLabel = null;
        }

        public OneImage? ShowingImage => DataContext as OneImage;

        public OneLabel? EditingLabel
        {
            get => (OneLabel?)GetValue(EditingLabelProperty);
            set => SetValue(EditingLabelProperty, value);
        }

        public static readonly DependencyProperty EditingLabelProperty =
            DependencyProperty.Register(nameof(EditingLabel), typeof(OneLabel), typeof(ImageLabelViewer),
                new PropertyMetadata(null));

        #region 第一层：整体拖动与缩放
        private Point _lastMousePosition; // 记录鼠标按下时的坐标（用于计算位移量）
        private Point _mouseDownPosition; // 用于计算是否超过点击阈值

        private void OnViewportDown(object sender, MouseButtonEventArgs e)
        {
            _mouseDownPosition = _lastMousePosition = e.GetPosition(ViewportGrid);

            EditingLabel = null;
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
            if ((e.GetPosition(ViewportGrid) - _mouseDownPosition).Length < 2 && CanLabel)
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
                _draggingLabel.X = Math.Clamp(mousePosInImage.X / TargetImage.ActualWidth, 0, 1);
                _draggingLabel.Y = Math.Clamp(mousePosInImage.Y / TargetImage.ActualHeight, 0, 1);
            }
            e.Handled = true;
        }

        private void OnLabelContainerUp(object sender, MouseButtonEventArgs e)
        {
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
                EditingLabel = null;
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
                EditingLabel = null;
                ShowingImage.SelectedLabel = label;
                if (ShowingImage.DeleteLabelCommand.CanExecute(label))
                    ShowingImage.DeleteLabelCommand.Execute(label);
            }
        }
        #endregion

        #region 第三层(下)：标签编辑
        private void LabelText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: OneLabel label } || ShowingImage == null) return;

            ShowingImage.SelectedLabel = label;
            if (e.ClickCount >= 2)
                EditingLabel = label;

            e.Handled = true;
        }

        private void LabelTextEditBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is not true || sender is not TextBox textBox) return;

            Dispatcher.BeginInvoke(() =>
            {
                if (!textBox.IsVisible || !ReferenceEquals(textBox.DataContext, EditingLabel)) return;
                textBox.Focus();
                textBox.CaretIndex = textBox.Text.Length;
            });
        }

        private void LabelTextEditBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            EditingLabel = null;
        }

        private void LabelTextEditBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift
                || e.Key == Key.Escape)
            {
                EditingLabel = null;
                Keyboard.ClearFocus();
                e.Handled = true;
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
                if (IsSyncRequired)
                    Snipped?.Invoke(this, normRect);
                else if (CaptureRegion(normRect) is { } myBmp)
                {
                    _ = ScreenshotHelper.SetClipboard(myBmp);
                    ScreenshotCaptured?.Invoke(this, new(myBmp, normRect));
                }
            }
        }

        public BitmapSource? CaptureRegion(Rect normRect)// 供外部调用，用于实现"同步"裁剪相同区域
        {
            if (TargetImage.Source is not BitmapSource bitmapSource) return null;
            return ScreenshotHelper.Crop(bitmapSource, normRect);
        }

        #endregion

        // ==================== 事件 ====================
        // 当截图完成时，通知外部（父窗口）
        public event EventHandler<Rect>? Snipped;
        // 截图完成后带 BitmapSource 的事件（MainWindow 用）
        public event EventHandler<ScreenshotEventArgs>? ScreenshotCaptured;

        public sealed record ScreenshotEventArgs(BitmapSource Bitmap, Rect NormalizedRect);

        // ==================== 锁定状态 ====================
        public bool IsXLocked { get; set; }
        public bool IsYLocked { get; set; }

        // ==================== 缩放适配方法 ====================
        private void Fit(string mode = "All")
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

        // ==================== 截图模式开关 ====================
        public bool IsScreenShotMode
        {
            get => (bool)GetValue(IsScreenShotModeProperty);
            set => SetValue(IsScreenShotModeProperty, value);
        }
        public static readonly DependencyProperty IsScreenShotModeProperty =
            DependencyProperty.Register(nameof(IsScreenShotMode), typeof(bool), typeof(ImageLabelViewer),
                new PropertyMetadata(false));
        // 是否需要同步模式（true则通知外部，false则直接存剪贴板）
        public bool IsSyncRequired
        {
            get => (bool)GetValue(IsSyncRequiredProperty);
            set => SetValue(IsSyncRequiredProperty, value);
        }
        public static readonly DependencyProperty IsSyncRequiredProperty =
            DependencyProperty.Register(nameof(IsSyncRequired), typeof(bool), typeof(ImageLabelViewer), new PropertyMetadata(false));

        // ==================== 缩放与偏移 ====================
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

        // ==================== 显示控制 ====================
        public bool CanLabel
        {
            get => (bool)GetValue(CanLabelProperty);
            set => SetValue(CanLabelProperty, value);
        }
        public static readonly DependencyProperty CanLabelProperty =
            DependencyProperty.Register(nameof(CanLabel), typeof(bool), typeof(ImageLabelViewer), new PropertyMetadata(false));


    }

}
