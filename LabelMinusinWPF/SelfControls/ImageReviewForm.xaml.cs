using CommunityToolkit.Mvvm.ComponentModel;
using ControlzEx.Standard;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
using System.Windows.Threading;
using static LabelMinusinWPF.MainViewModel;

namespace LabelMinusinWPF
{
    /// <summary>
    /// ImageReview.xaml 的交互逻辑
    /// </summary>
    public partial class ImageReView : UserControl
    {
        private DispatcherTimer _closeTimer;
        private string _currentImgPath;
        public ImageReView()
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
                SaveAndHidePopup();
            };

            // 右键清空涂鸦
            InkEditor.PreviewMouseRightButtonDown += (s, e) =>
            {
                InkEditor.Strokes.Clear();
            };
        }

        #region 同步截图
        // 在构造函数或 Loaded 事件中绑定


        private void SyncCapture(Rect logicRect)
        {
            // 从左边拿图
            var bmpLeft = LeftPicView.GetImageRegion(logicRect);
            // 从右边拿图（用同样的逻辑坐标）
            var bmpRight = RightPicView.GetImageRegion(logicRect);

            if (bmpLeft != null || bmpRight != null)
            {
                string currentImgName = DualNameCombobox.SelectedItem?.ToString() ?? "截图";
                CombineAndCopy(bmpLeft, bmpRight, currentImgName);
            }
        }
        private void CombineAndCopy(BitmapSource leftSource, BitmapSource rightSource, string footerText)
        {
            // 1. 如果两张图都是空的，直接返回
            if (leftSource == null && rightSource == null) return;

            // 2. 计算画布尺寸
            // 宽度 = 左图逻辑宽 + 右图逻辑宽
            double mainW = (leftSource?.PixelWidth ?? 0) + (rightSource?.PixelWidth ?? 0);
            // 高度 = 两者中最高的一个
            double mainH = Math.Max(leftSource?.PixelHeight ?? 0, rightSource?.PixelHeight ?? 0);

            // 动态计算页脚高度 (12%, 最小60, 最大150)
            double footerH = Math.Clamp(mainH * 0.12, 60, 150);

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

                // 绘制页脚文字 (动态计算字号)
                FormattedText ft = new FormattedText(
                    $"▲ {footerText}",
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Microsoft YaHei"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                    footerH * 0.8, // 字体大小设为页脚高度的一半
                    Brushes.RoyalBlue,
                    VisualTreeHelper.GetDpi(visual).PixelsPerDip);

                // 文字居中对齐
                Point textPos = new Point((canvasW - ft.Width) / 2, mainH + (footerH - ft.Height) / 2);
                dc.DrawText(ft, textPos);
            }

            // 4. 将绘图渲染为位图 (使用 96 DPI 保证原始像素 1:1)
            RenderTargetBitmap rtb = new RenderTargetBitmap((int)canvasW, (int)canvasH, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);

            // 5. 调用保存与压缩函数
            SaveAndCopyWPF(rtb);
        }
        private void SaveAndCopyWPF(BitmapSource bitmap)
        {
            string folderPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ScreenShottemp");
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
            try
            {
                using (MemoryStream ms = new MemoryStream(finalData))
                {
                    // 从压缩后的字节流创建一个新的解码器
                    JpegBitmapDecoder decoder = new JpegBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    BitmapSource compressedBitmap = decoder.Frames[0];

                    // 存入剪贴板
                    Clipboard.SetImage(compressedBitmap);
                    ImgThumb.Source = compressedBitmap;
                }
            }
            catch (Exception ex)
            {
                // 剪贴板可能被其他进程锁定
                System.Diagnostics.Debug.WriteLine("Clipboard error: " + ex.Message);
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

        private void SaveAndHidePopup()
        {
            if (!PreviewPopup.IsOpen) return;

            try
            {
                // 如果有涂鸦，则合成并保存
                if (InkEditor.Strokes.Count > 0)
                {
                    SaveInkToImage();
                }
            }
            finally
            {
                PreviewPopup.IsOpen = false;
                InkEditor.Strokes.Clear(); // 清空，下次显示干净的
            }
        }

        private void SaveInkToImage()
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
            SaveAndCopyWPF(rtb);

        }
        #endregion

        public event EventHandler ExitClicked=delegate { };
        private void ExitBtn_Click(object sender, RoutedEventArgs e) => ExitClicked?.Invoke(this, EventArgs.Empty);


        private void TempChangePic_Click(bool isLeft)
        {
            string? PicPath = DialogService.OpenFile("图片文件|*.jpg;*.png;*.bmp;*.webp");
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
    }
}
