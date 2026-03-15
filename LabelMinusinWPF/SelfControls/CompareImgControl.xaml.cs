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
            // 1. 如果两张图都是空的，直接返回
            if (leftSource == null && rightSource == null) return;

            // 2. 使用预先捕获的尺寸计算画布尺寸
            double mainW = leftW + rightW;
            double mainH = Math.Max(leftH, rightH);

            // 动态计算页脚高度
            double footerH = Math.Clamp(mainH * 0.12, 50, 150);

            double canvasW = mainW;
            double canvasH = mainH + footerH;

            // 创建渲染目标
            RenderTargetBitmap? rtb = null;
            BitmapSource? finalBitmap = null;

            try
            {
                // 3. 在后台线程渲染
                rtb = new RenderTargetBitmap((int)canvasW, (int)canvasH, 96, 96, PixelFormats.Pbgra32);

                DrawingVisual visual = new DrawingVisual();
                using (DrawingContext dc = visual.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, canvasW, canvasH));

                    // 使用预捕获的尺寸
                    dc.DrawImage(leftSource, new Rect(0, 0, leftW, leftH));
                    dc.DrawImage(rightSource, new Rect(leftW, 0, rightW, rightH));

                    // 蓝色分割线
                    Pen bluePen = new Pen(new SolidColorBrush(Color.FromRgb(65, 105, 225)), 2);
                    dc.DrawLine(bluePen, new Point(leftW, 0), new Point(leftW, mainH));

                    // 页脚背景
                    dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(255, 245, 238)), null,
                                     new Rect(0, mainH, canvasW, footerH));

                    // 字号
                    double fontSize = Math.Max(footerH * 0.7, 14);
                    FormattedText ft = new FormattedText(
                        $"▲ {footerText}",
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface(new FontFamily("Microsoft YaHei"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                        fontSize,
                        Brushes.RoyalBlue,
                        VisualTreeHelper.GetDpi(visual).PixelsPerDip);

                    double maxTextWidth = canvasW * 0.9;
                    while (ft.Width > maxTextWidth && fontSize > 14)
                    {
                        fontSize -= 1;
                        ft = new FormattedText(
                            $"▲ {footerText}",
                            System.Globalization.CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight,
                            new Typeface(new FontFamily("Microsoft YaHei"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                            fontSize,
                            Brushes.RoyalBlue,
                            VisualTreeHelper.GetDpi(visual).PixelsPerDip);
                    }

                    Point textPos = new Point((canvasW - ft.Width) / 2, mainH + (footerH - ft.Height) / 2);
                    dc.DrawText(ft, textPos);
                }

                rtb.Render(visual);
                finalBitmap = rtb;

                // 4. 压缩并保存（在后台线程执行）
                SaveInBackground(finalBitmap);
            }
            finally
            {
                rtb?.Freeze();
            }
        }

        private void SaveInBackground(BitmapSource bitmap)
        {
            string folderPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ScreenshotFolderName);
            string filePath = System.IO.Path.Combine(folderPath, $"Capture_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
            System.IO.Directory.CreateDirectory(folderPath);

            // 压缩：初始质量 85（平衡质量和性能）
            int quality = 85;
            byte[]? finalData = null;

            using (var ms = new System.IO.MemoryStream())
            {
                var encoder = new JpegBitmapEncoder { QualityLevel = quality };
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                encoder.Save(ms);
                finalData = ms.ToArray();

                // 如果大于 2MB 才进一步压缩，减少迭代
                if (finalData.Length > 2 * 1024 * 1024)
                {
                    quality = 70;
                    ms.SetLength(0);
                    ms.Position = 0;
                    encoder = new JpegBitmapEncoder { QualityLevel = quality };
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    encoder.Save(ms);
                    finalData = ms.ToArray();
                }
            }

            System.IO.File.WriteAllBytes(filePath, finalData!);
            _currentImgPath = filePath;

            // 回到 UI 线程更新剪贴板和缩略图
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    using var ms = new System.IO.MemoryStream(finalData!);
                    var decoder = new JpegBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    var compressedBitmap = decoder.Frames.First();
                    Clipboard.SetImage(compressedBitmap);
                    ImgThumb.Source = compressedBitmap;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"更新剪贴板失败: {ex.Message}");
                }
            });
        }
        private void CombineImages(BitmapSource? leftSource, BitmapSource? rightSource, string footerText)
        {
            // 1. 如果两张图都是空的，直接返回
            if (leftSource == null && rightSource == null) return;

            // 2. 计算画布尺寸
            // 宽度 = 左图逻辑宽 + 右图逻辑宽
            double mainW = (leftSource?.PixelWidth ?? 0) + (rightSource?.PixelWidth ?? 0);
            // 高度 = 两者中最高的一个
            double mainH = Math.Max(leftSource?.PixelHeight ?? 0, rightSource?.PixelHeight ?? 0);

            // 动态计算页脚高度 (12%, 最小60, 最大150)
            double footerH = Math.Clamp(mainH * 0.12, 10, 150);

            double canvasW = mainW;
            double canvasH = mainH + footerH;

            // 3. 开始在内存中绘图
            DrawingVisual visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                // 绘制白色背景
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, canvasW, canvasH));

                double currentX = 0;

                // 绘制左图
                if (leftSource != null)
                {
                    dc.DrawImage(leftSource, new Rect(0, 0, leftSource.PixelWidth, leftSource.PixelHeight));
                    currentX = leftSource.PixelWidth;
                }

                // 绘制右图
                if (rightSource != null)
                {
                    dc.DrawImage(rightSource, new Rect(currentX, 0, rightSource.PixelWidth, rightSource.PixelHeight));
                }

                // 绘制蓝色分割线 (仅在双图并存时)
                if (leftSource != null && rightSource != null)
                {
                    Pen bluePen = new Pen(new SolidColorBrush(Color.FromRgb(65, 105, 225)), 2); // RoyalBlue
                    dc.DrawLine(bluePen, new Point(leftSource.PixelWidth, 0), new Point(leftSource.PixelWidth, mainH));
                }

                // 绘制页脚背景 (SeaShell 颜色: 255, 245, 238)
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(255, 245, 238)), null,
                                 new Rect(0, mainH, canvasW, footerH));

                // 1. 基础字号设定（保持你原有的逻辑作为上限）
                double fontSize = footerH * 0.7; // 稍微调小比例，留出上下边距

                // 2. 创建一个初步的 FormattedText 用于测量宽度
                FormattedText ft = new FormattedText(
                    $"▲ {footerText}",
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Microsoft YaHei"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                    fontSize,
                    Brushes.RoyalBlue,
                    VisualTreeHelper.GetDpi(visual).PixelsPerDip);

                // 3. 【关键修正】检查文字是否太宽。如果文字比画布宽（留出10%边距），则按比例缩小字号
                double maxTextWidth = canvasW * 0.9; // 允许文字占用的最大宽度（90% 画布宽）
                if (ft.Width > maxTextWidth)
                {
                    // 按宽度比例缩小字号
                    fontSize = fontSize * (maxTextWidth / ft.Width);

                    // 重新生成缩放后的文字
                    ft = new FormattedText(
                        $"▲ {footerText}",
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface(new FontFamily("Microsoft YaHei"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                        fontSize,
                        Brushes.RoyalBlue,
                        VisualTreeHelper.GetDpi(visual).PixelsPerDip);
                }

                // 4. 文字居中对齐绘制（逻辑不变）
                Point textPos = new Point((canvasW - ft.Width) / 2, mainH + (footerH - ft.Height) / 2);
                dc.DrawText(ft, textPos);
            }

            // 4. 将绘图渲染为位图 (使用 96 DPI 保证原始像素 1:1)
            RenderTargetBitmap rtb = new RenderTargetBitmap((int)canvasW, (int)canvasH, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);

            // 5. 调用保存与压缩函数
            SaveImage(rtb);
        }
        private void SaveImage(BitmapSource bitmap)
        {
            try
            {
                string folderPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ScreenshotFolderName);
                string filePath = System.IO.Path.Combine(folderPath, $"Capture_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
                Directory.CreateDirectory(folderPath);

                // 1. 初始化质量为 100（不压缩）
                int quality = 100;
                byte[] finalData;

                // 2. 迭代压缩逻辑
                do
                {
                    using (MemoryStream ms = new MemoryStream())
                    {
                        JpegBitmapEncoder encoder = new JpegBitmapEncoder { QualityLevel = quality };
                        encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        encoder.Save(ms);
                        finalData = ms.ToArray();
                    }

                    // 如果文件小于 1MB，或者画质已经压到 20 了，就退出循环
                    if (finalData.Length <= 1024 * 1024 || quality <= 20) break;

                    // 步进降低质量
                    quality -= 10;
                } while (true);

                // 3. 写入磁盘文件
                File.WriteAllBytes(filePath, finalData);
                _currentImgPath = filePath;

                // 4. 将 finalData 写入剪贴板
                using (MemoryStream ms = new MemoryStream(finalData))
                {
                    // 从压缩后的字节流创建一个新的解码器
                    JpegBitmapDecoder decoder = new JpegBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    BitmapSource compressedBitmap = decoder.Frames.First();

                    // 存入剪贴板
                    Clipboard.SetImage(compressedBitmap);
                    ImgThumb.Source = compressedBitmap;
                }
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
