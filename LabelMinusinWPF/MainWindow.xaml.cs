using Microsoft.Win32;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
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
using MaterialDesignThemes.Wpf;

namespace LabelMinusinWPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }


        #region 黑白模式
        private static void ToggleDarkMode(bool isDark)
        {
            var paletteHelper = new PaletteHelper();
            // 显式指定 MaterialDesignThemes.Wpf.ITheme 以防冲突
            var theme = paletteHelper.GetTheme();

            // 修改基础主题
            theme.SetBaseTheme(isDark ? BaseTheme.Dark : BaseTheme.Light);

            // 重新应用
            paletteHelper.SetTheme(theme);
        }
        private void DarkMode_Checked(object sender, RoutedEventArgs e)
        {
            ToggleDarkMode(true);
        }

        private void DarkMode_Unchecked(object sender, RoutedEventArgs e)
        {
            ToggleDarkMode(false);
        }
        #endregion

        #region 图片缩放与平移与截图
        private bool _isPanning = false; // 改名，避免与标签拖拽的 _isDragging 混淆
        private Point _lastMousePosition;


        // --- 缩放逻辑 (鼠标滚轮) ---
        private void ImageParent_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            var element = ImageParent;
            Point position = e.GetPosition(element);

            // 计算缩放比例
            double scaleFactor = e.Delta > 0 ? 1.1 : 0.9;
            double newScale = ImageScale.ScaleX * scaleFactor;

            // 限制缩放范围（0.1倍到10倍）
            if (newScale < 0.1 || newScale > 10) return;

            // 这一步是实现“以鼠标指向点为中心缩放”的核心算法
            double relativeX = position.X - ImageTranslate.X;
            double relativeY = position.Y - ImageTranslate.Y;

            ImageTranslate.X -= relativeX * (scaleFactor - 1);
            ImageTranslate.Y -= relativeY * (scaleFactor - 1);

            ImageScale.ScaleX = newScale;
            ImageScale.ScaleY = newScale;
        }

        // --- 平移逻辑 (鼠标中键或左键) ---
        // 1. 正常的按下逻辑
        private void ImageParent_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // 如果点到了标签点，子控件会 Handle 事件，这里就不会触发
            if (e.ChangedButton == MouseButton.Left || e.ChangedButton == MouseButton.Middle)
            {
                StartPanning(e.GetPosition(ImageParent));
            }
        }

        // 2. 核心：封装开始平移的方法，供子控件“接力”调用
        public void StartPanning(Point startPoint)
        {
            _lastMousePosition = startPoint;
            _isPanning = true;
            ImageParent.CaptureMouse();
            Mouse.OverrideCursor = Cursors.Hand;
        }
        private void MyLabelControl_RequestDragImage(object sender, RequestDragImageEventArgs e)
        {
            // 将子控件传来的坐标转换成父控件 ImageParent 的坐标
            Point parentPoint = PicView.TranslatePoint(e.MousePosition, ImageParent);

            // 调用你之前写的平移启动逻辑
            StartPanning(parentPoint);
        }
        // 3. 移动逻辑
        private void ImageParent_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanning)
            {
                Point currentPosition = e.GetPosition(ImageParent);
                Vector delta = currentPosition - _lastMousePosition;

                ImageTranslate.X += delta.X;
                ImageTranslate.Y += delta.Y;

                _lastMousePosition = currentPosition;
            }
        }

        // 4. 抬起逻辑
        private void ImageParent_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isPanning)
            {
                _isPanning = false;
                ImageParent.ReleaseMouseCapture();
                Mouse.OverrideCursor = null;
            }
        }


        #endregion

        #region 截图功能
        private bool _isSnipping = false;
        private Point _snipStartPoint;
        
        private void SnipCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _isSnipping = true;
            _snipStartPoint = e.GetPosition(SnipCanvas); // 直接相对于 Canvas 取点

            SnipRectangle.Visibility = Visibility.Visible;
            Canvas.SetLeft(SnipRectangle, _snipStartPoint.X);
            Canvas.SetTop(SnipRectangle, _snipStartPoint.Y);
            SnipRectangle.Width = 0;
            SnipRectangle.Height = 0;

            SnipCanvas.CaptureMouse(); // 此时是 Canvas 捕获鼠标
            e.Handled = true;
        }

        private void SnipCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isSnipping && SnipCanvas.IsMouseCaptured)
            {
                var pos = e.GetPosition(SnipCanvas);

                double x = Math.Min(_snipStartPoint.X, pos.X);
                double y = Math.Min(_snipStartPoint.Y, pos.Y);
                double w = Math.Abs(_snipStartPoint.X - pos.X);
                double h = Math.Abs(_snipStartPoint.Y - pos.Y);

                Canvas.SetLeft(SnipRectangle, x);
                Canvas.SetTop(SnipRectangle, y);
                SnipRectangle.Width = w;
                SnipRectangle.Height = h;
            }
            e.Handled = true;
        }

        private void SnipCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isSnipping)
            {
                SnipCanvas.ReleaseMouseCapture();
                _isSnipping = false;

                // 执行截图逻辑
                CaptureRegion(Canvas.GetLeft(SnipRectangle),
                              Canvas.GetTop(SnipRectangle),
                              SnipRectangle.Width,
                              SnipRectangle.Height);

                SnipRectangle.Visibility = Visibility.Collapsed;
            }
            e.Handled = true;
        }
        private void CaptureRegion(double x, double y, double width, double height)
        {
            try
            {
                // ==========================================
                // 【修复问题2】：隐藏截图层（蓝框和黑色遮罩）
                // ==========================================
                SnipCanvas.Visibility = Visibility.Collapsed;

                // 强制立刻刷新一次UI，确保隐藏动作在截图前生效
                ImageParent.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);

                // ==========================================
                // 【修复问题1】：处理系统 DPI 缩放带来的坐标偏移
                // ==========================================
                // 获取当前屏幕的 DPI 缩放比例
                PresentationSource source = PresentationSource.FromVisual(ImageParent);
                double dpiX = 96.0;
                double dpiY = 96.0;
                if (source != null && source.CompositionTarget != null)
                {
                    dpiX = 96.0 * source.CompositionTarget.TransformToDevice.M11;
                    dpiY = 96.0 * source.CompositionTarget.TransformToDevice.M22;
                }

                // 1. 将容器的逻辑尺寸转换为物理像素尺寸
                int parentWidth = (int)(ImageParent.ActualWidth * dpiX / 96.0);
                int parentHeight = (int)(ImageParent.ActualHeight * dpiY / 96.0);

                if (parentWidth <= 0 || parentHeight <= 0) return;

                // 渲染目标位图 (使用真实的 DPI 而不是写死的 96)
                RenderTargetBitmap rtb = new (parentWidth, parentHeight, dpiX, dpiY, PixelFormats.Pbgra32);

                // 此时由于 SnipCanvas 已隐藏，渲染出来的只有纯净的图片和标签
                rtb.Render(ImageParent);

                // 2. 将鼠标选框的逻辑坐标也转换为物理像素坐标
                int cropX = (int)(x * dpiX / 96.0);
                int cropY = (int)(y * dpiY / 96.0);
                int cropWidth = (int)(width * dpiX / 96.0);
                int cropHeight = (int)(height * dpiY / 96.0);

                // 3. 严格边界检查，防止因浮点数转换导致的越界崩溃
                cropX = Math.Max(0, cropX);
                cropY = Math.Max(0, cropY);
                cropWidth = Math.Min(cropWidth, parentWidth - cropX);
                cropHeight = Math.Min(cropHeight, parentHeight - cropY);

                if (cropWidth <= 0 || cropHeight <= 0) return;

                // 4. 进行裁剪
                Int32Rect cropRect = new(cropX, cropY, cropWidth, cropHeight);
                CroppedBitmap croppedBmp = new(rtb, cropRect);

                // ✅ 复制到系统剪贴板
                Clipboard.SetImage(croppedBmp);
                //MessageBox.Show("截图已复制到剪贴板！", "提示");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"截图失败: {ex.Message}");
            }
            finally
            {
                // ==========================================
                // 【恢复状态】
                // ==========================================
                SnipRectangle.Visibility = Visibility.Collapsed; // 蓝框缩回去了，保持隐藏

                // 重点：不要直接写 SnipCanvas.Visibility = Visibility.Visible
                // 因为你的 Canvas 是靠 XAML 里的 DataTrigger(OCR模式) 控制可见性的
                // 直接赋值会破坏绑定，用 ClearValue 把控制权交还给绑定
                SnipCanvas.ClearValue(VisibilityProperty);
            }
        }
        #endregion
        private void ResetView_Click(object sender, RoutedEventArgs e)
        {
            ImageScale.ScaleX = 1;
            ImageScale.ScaleY = 1;

            ImageTranslate.X = 0;
            ImageTranslate.Y = 0;
        }
        private void OpenImageReview_Click(object sender, RoutedEventArgs e)
        {
            FullScreenReview.Visibility = Visibility.Visible;
        }
        private void FullScreenReview_ExitClicked(object sender, EventArgs e)
        {
            FullScreenReview.Visibility = Visibility.Collapsed;
        }
    }



}