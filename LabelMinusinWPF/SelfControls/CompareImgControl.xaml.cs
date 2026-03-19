using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using System;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using static LabelMinusinWPF.MainVM;
using LabelMinusinWPF.Common;

namespace LabelMinusinWPF
{

    /// ImageReview.xaml 的交互逻辑

    public partial class CompareImgControl : UserControl
    {
        private const string ScreenshotFolderName = "ScreenShottemp";
        private DispatcherTimer _closeTimer;
        private string _currentImgPath = string.Empty;

        public CompareImgControl()
        {
            InitializeComponent();
            LeftPicView.Snipped += (s, rect) => SyncCapture(rect);
            RightPicView.Snipped += (s, rect) => SyncCapture(rect);
            // 初始化计时器 (对应 WinForm 的 closeTimer)
            _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _closeTimer.Tick += (s, e) =>
            {
                if (ThumbBorder.IsMouseOver || PreviewPopup.IsMouseOver) return;

                // 如果正在涂鸦（按住鼠标），不关闭
                if (InkEditor.IsMouseCaptured) return;

                _closeTimer.Stop();
                HidePopup();
            };

            // 右键清空涂鸦
            InkEditor.PreviewMouseRightButtonDown += (s, e) =>
            {
                InkEditor.Strokes.Clear();
            };
            this.Loaded += OnLoaded;

        }
        # region Q键控制截图
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // 找到承载此控件的窗口
            var window = Window.GetWindow(this);
            if (window != null)
            {
                // 使用 Preview 事件，因为它在子控件（包括 Popup）处理之前触发
                window.PreviewKeyDown -= OnKeyDown;
                window.PreviewKeyUp -= OnKeyUp;
                window.PreviewKeyDown += OnKeyDown;
                window.PreviewKeyUp += OnKeyUp;
            }

            // 确保GridSplitter不会捕获键盘焦点
            if (DualImageSplitter != null)
            {
                DualImageSplitter.Focusable = false;
            }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            // 检查按键是否为 Q
            if (e.Key == Key.Q && !e.IsRepeat)
            {
                if (FocusManager.GetFocusedElement(Window.GetWindow(this)) is TextBox) return;

                var vm = this.DataContext as CompareImgVM;
                if (vm != null)
                {
                    vm.IsScreenShotEnabled = true;
                    e.Handled = true; // 如果不想让 Q 键继续传递给 InkCanvas，可以取消注释
                }
            }
        }

        private void OnKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Q)
            {
                
                var vm = this.DataContext as CompareImgVM;
                if (vm != null)
                {
                    vm.IsScreenShotEnabled = false;
                }
            }
        }
        #endregion

        #region 一起截图
        // 在构造函数或 Loaded 事件中绑定


        private async void SyncCapture(Rect logicRect)
        {
            try
            {
                // 从左边拿图
                var bmpLeft = LeftPicView.GetImageRegion(logicRect);
                // 从右边拿图（用同样的逻辑坐标）
                var bmpRight = RightPicView.GetImageRegion(logicRect);

                if (bmpLeft != null || bmpRight != null)
                {
                    string currentImgName = DualNameCombobox.SelectedItem?.ToString() ?? "截图";

                    // 在 UI 线程捕获图片尺寸（避免跨线程访问）
                    int leftW = bmpLeft?.PixelWidth ?? 0;
                    int leftH = bmpLeft?.PixelHeight ?? 0;
                    int rightW = bmpRight?.PixelWidth ?? 0;
                    int rightH = bmpRight?.PixelHeight ?? 0;

                    // 【关键修复】在 UI 线程中冻结 BitmapSource，使其可以在后台线程中使用
                    BitmapSource? frozenLeft = null;
                    BitmapSource? frozenRight = null;

                    if (bmpLeft != null)
                    {
                        frozenLeft = CloneAndFreeze(bmpLeft);
                    }
                    if (bmpRight != null)
                    {
                        frozenRight = CloneAndFreeze(bmpRight);
                    }

                    await Task.Run(() => CombineAndSave(frozenLeft, frozenRight, currentImgName, leftW, leftH, rightW, rightH));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"截图失败: {ex.Message}");
                MessageBox.Show($"截图失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private BitmapSource CloneAndFreeze(BitmapSource source)
        {
            // 创建位图的克隆并冻结，使其可以在后台线程中使用
            var clone = new WriteableBitmap(source);
            clone.Freeze();
            return clone;
        }

        private void CombineAndSave(BitmapSource? leftSource, BitmapSource? rightSource, string footerText, int leftW, int leftH, int rightW, int rightH)
        {
            var result = ScreenshotHelper.SaveTwoImages(leftSource, rightSource, footerText, ScreenshotFolderName, 85, 70, 2 * 1024 * 1024);
            if (result == null) return;
            UpdateClipboardAndThumb(result.Value);
        }

        private void UpdateClipboardAndThumb((string FilePath, byte[] Data) result)
        {
            _currentImgPath = result.FilePath;
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    using var ms = new System.IO.MemoryStream(result.Data);
                    var decoder = new JpegBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    var bmp = decoder.Frames.First();
                    Clipboard.SetImage(bmp);
                    ImgThumb.Source = bmp;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"更新剪贴板失败: {ex.Message}");
                }
            });
        }

        private void SaveImage(BitmapSource bitmap)
        {
            try
            {
                var result = ScreenshotHelper.SaveWithCompression(bitmap, null, ScreenshotFolderName);
                if (result == null) return;
                UpdateClipboardAndThumb(result.Value);
            }
            catch (UnauthorizedAccessException ex)
            {
                System.Diagnostics.Debug.WriteLine($"文件访问权限错误: {ex.Message}");
                MessageBox.Show($"无法保存截图，请检查文件权限。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (IOException ex)
            {
                System.Diagnostics.Debug.WriteLine($"IO错误: {ex.Message}");
                MessageBox.Show($"保存截图失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"截图保存失败: {ex.Message}");
                MessageBox.Show($"截图保存失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        #endregion


        #region 编辑截图
        private void ThumbBorder_MouseEnter(object sender, MouseEventArgs e)
        {
            if (!File.Exists(_currentImgPath)) return;

            _closeTimer.Stop();

            if (!PreviewPopup.IsOpen)
            {
                // 1. 加载图片
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(_currentImgPath);
                bitmap.EndInit();

                EditImage.Source = bitmap;

                // 2. 计算尺寸 (自适应)
                double maxW = SystemParameters.WorkArea.Width * 0.8;
                double maxH = SystemParameters.WorkArea.Height * 0.6;
                double scale = Math.Min(maxW / bitmap.PixelWidth, maxH / bitmap.PixelHeight);

                DrawingContainer.Width = bitmap.PixelWidth * scale;
                DrawingContainer.Height = bitmap.PixelHeight * scale;

                PreviewPopup.IsOpen = true;
            }
        }

        private void ThumbBorder_MouseLeave(object sender, MouseEventArgs e)
        {
            _closeTimer.Start();
        }

        private void HidePopup()
        {
            if (!PreviewPopup.IsOpen) return;

            try
            {
                // 如果有涂鸦，则合成并保存
                if (InkEditor.Strokes.Count > 0)
                {
                    SaveInk();
                }
            }
            finally
            {
                PreviewPopup.IsOpen = false;
                InkEditor.Strokes.Clear(); // 清空，下次显示干净的
                DualScreenShot.Focus();
            }
        }

        private void SaveInk()
        {
            try
            {
                // 1. 创建 RenderTargetBitmap，大小与原始图片一致
                var source = (BitmapSource)EditImage.Source;
                RenderTargetBitmap rtb = new RenderTargetBitmap(
                    source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Default);

                // 2. 利用 DrawingVisual 叠加图片和笔迹
                DrawingVisual dv = new DrawingVisual();
                using (DrawingContext dc = dv.RenderOpen())
                {
                    // 画原图
                    dc.DrawImage(source, new Rect(0, 0, source.PixelWidth, source.PixelHeight));

                    // 画笔迹 (由于 InkCanvas 是基于显示尺寸的，我们需要一个缩放变换)
                    double scaleX = source.PixelWidth / DrawingContainer.Width;
                    double scaleY = source.PixelHeight / DrawingContainer.Height;
                    dc.PushGuidelineSet(new GuidelineSet());
                    dc.PushTransform(new ScaleTransform(scaleX, scaleY));

                    // 绘制笔迹
                    InkEditor.Strokes.Draw(dc);
                }

                rtb.Render(dv);

                // 3. 压缩保存 (复用你之前的 SaveAndCopyWPF 逻辑)
                SaveImage(rtb);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存涂鸦失败: {ex.Message}");
                MessageBox.Show($"保存涂鸦失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        #endregion


        #region 打开关闭图校
        // 定义 IsOpen 依赖属性
        public static readonly DependencyProperty IsOpenProperty =
            DependencyProperty.Register("IsOpen", typeof(bool), typeof(CompareImgControl),
                new PropertyMetadata(false, OnIsOpenChanged));

        public bool IsOpen
        {
            get { return (bool)GetValue(IsOpenProperty); }
            set { SetValue(IsOpenProperty, value); }
        }

        // IsOpen 发生变化时，自动控制自身的 Visibility
        private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (CompareImgControl)d;
            bool isOpen = (bool)e.NewValue;
            control.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
        }
        private void ExitBtn_Click(object sender, RoutedEventArgs e)
        {
            this.IsOpen = false;
        }
        #endregion


        #region 简单功能
        private void TempChangePic_Click(bool isLeft)
        {
            var dialog = new OpenFileDialog { Filter = "图片文件|*.jpg;*.png;*.bmp;*.webp", Title = "暂时替换当前图片" };
            string? PicPath = dialog.ShowDialog() == true ? dialog.FileName : null;
            if (!string.IsNullOrEmpty(PicPath))
            {
                try
                {
                    BitmapImage bitmap = new();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(PicPath, UriKind.Absolute);
                    // 重要：这行代码能让图片加载完后立刻释放文件句柄，防止文件被占用
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    // 冻结对象可以提高跨线程性能并使其只读
                    bitmap.Freeze();
                    // 使用 SetCurrentValue 临时修改，不破坏 XAML 中的 Binding
                    if (isLeft)
                        LeftPicView.TargetImage.SetCurrentValue(Image.SourceProperty, bitmap);
                    else
                        RightPicView.TargetImage.SetCurrentValue(Image.SourceProperty, bitmap);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"图片加载失败: {ex.Message}");
                }
            }
        }
        private void LeftChangePic_Click(object sender, RoutedEventArgs e)
        {
            TempChangePic_Click(true);
        }

        private void RightChangePic_Click(object sender, RoutedEventArgs e)
        {
            TempChangePic_Click(false);
        }

        private void OpenScreenshotFolder_Click(object sender, RoutedEventArgs e)
        {
            string folderPath =System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ScreenShottemp");

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = folderPath,
                UseShellExecute = true
            });
        }
        #endregion


        #region 拖入文件显示
        private void OnFileDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length == 0) return;

                // 1. 找到触发拖拽的控件
                var targetView = sender as FrameworkElement;

                // 2. 向上寻找包含 RightImageVM 的 DataContext (假设是在 MainVM 里)
                // 或者直接从当前 View 的 DataContext 向上找
                if (targetView?.Name == "RightPicView")
                {
                    // 假设这里的 Parent DataContext 就是你的 MainVM
                    var mainVM = this.DataContext as CompareImgVM;
                    mainVM?.RightImageVM.OpenResourceByPath(files,false);
                }
                else if (targetView?.Name == "LeftPicView")
                {
                    var mainVM = this.DataContext as CompareImgVM;
                    mainVM?.LeftImageVM.OpenResourceByPath(files, false);
                }
            }
        }
        private void OnDragEnter(object sender, DragEventArgs e)
        {
            UpdateDragEffect(e);
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            UpdateDragEffect(e);
            // 必须设置 Handled 为 true，否则默认逻辑可能会覆盖你的设置
            e.Handled = true;
        }

        private void UpdateDragEffect(DragEventArgs e)
        {
            // 检查拖进来的是不是文件
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                // 如果是文件，显示“复制”图标（那个带加号的指针）
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                // 如果不是文件（比如拖的一段文字），显示“禁止”
                e.Effects = DragDropEffects.None;
            }
        }
        #endregion
    }
}
