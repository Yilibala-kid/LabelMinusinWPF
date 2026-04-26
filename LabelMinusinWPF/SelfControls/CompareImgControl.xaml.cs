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
using static LabelMinusinWPF.OneProject;
using LabelMinusinWPF.Common;
using AppConstants = LabelMinusinWPF.Common.Constants;

namespace LabelMinusinWPF
{

    /// ImageReview.xaml 的交互逻辑

    public partial class CompareImgControl : UserControl
    {
        private DispatcherTimer _closeTimer;
        private string _currentImgPath = string.Empty;
        private bool _isSplitFollowMouse;

        public CompareImgControl()
        {
            InitializeComponent();
            LeftPicView.Snipped += (s, rect) => SyncCapture(rect);
            RightPicView.Snipped += (s, rect) => SyncCapture(rect);
            _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _closeTimer.Tick += (s, e) =>
            {
                if (ThumbBorder.IsMouseOver || PreviewPopup.IsMouseOver) return;
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
            var window = Window.GetWindow(this);
            if (window != null)
            {
                window.PreviewKeyDown -= OnKeyDown;
                window.PreviewKeyUp -= OnKeyUp;
                window.PreviewKeyDown += OnKeyDown;
                window.PreviewKeyUp += OnKeyUp;
            }
            if (DualImageSplitter != null) DualImageSplitter.Focusable = false;

            if (DataContext is CompareImgVM vm)
            {
                vm.PropertyChanged += (s, args) =>
                {
                    if (args.PropertyName == nameof(CompareImgVM.IsDualReViewEnabled))
                        _isSplitFollowMouse = vm.IsDualReViewEnabled;
                };
            }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Q || e.IsRepeat) return;
            if (FocusManager.GetFocusedElement(Window.GetWindow(this)) is TextBox) return;
            if (DataContext is CompareImgVM vm) vm.IsScreenShotEnabled = true;
        }

        private void OnKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Q && DataContext is CompareImgVM vm) vm.IsScreenShotEnabled = false;
        }

        private void OnMouseMoveForSplit(object sender, MouseEventArgs e)
        {
            if (!_isSplitFollowMouse || DataContext is not CompareImgVM vm) return;
            if (vm.IsScreenShotEnabled) return;

            var container = sender as FrameworkElement;
            if (container == null) return;

            double mouseX = e.GetPosition(container).X;
            double totalWidth = container.ActualWidth;
            if (totalWidth <= 0) return;

            double ratio = Math.Max(0.05, Math.Min(0.95, mouseX / totalWidth));
            vm.LeftColumnWidth = new GridLength(ratio, GridUnitType.Star);
            vm.RightColumnWidth = new GridLength(1 - ratio, GridUnitType.Star);
        }
        #endregion

        #region 一起截图
        // 在构造函数或 Loaded 事件中绑定


        private async void SyncCapture(Rect logicRect)
        {
            try
            {
                var bmpLeft = LeftPicView.CaptureRegion(logicRect);
                var bmpRight = RightPicView.CaptureRegion(logicRect);

                if (bmpLeft != null || bmpRight != null)
                {
                    string currentImgName = DualNameCombobox.SelectedItem?.ToString() ?? "截图";
                    // Freeze on UI thread before passing to background task
                    var frozenLeft = ScreenshotHelper.Freeze(bmpLeft);
                    var frozenRight = ScreenshotHelper.Freeze(bmpRight);
                    var result = await Task.Run(() => ScreenshotHelper.SaveSnip(
                        ScreenshotHelper.Combine([frozenLeft, frozenRight], currentImgName)));

                    ApplyScreenshotResult(result);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"截图失败: {ex.Message}");
                MessageBox.Show($"截图失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ApplyScreenshotResult(ScreenshotHelper.ScreenshotSaveResult? result)
        {
            if (result == null) return;

            _currentImgPath = result.FilePath;
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    _ = ScreenshotHelper.SetClipboard(result.PreviewImage);
                    ImgThumb.Source = result.PreviewImage;
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
                var result = ScreenshotHelper.SaveSnip(bitmap);
                ApplyScreenshotResult(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                System.Diagnostics.Debug.WriteLine($"文件访问权限错误: {ex.Message}");
                MessageBox.Show($"无法保存截图，请检查文件权限。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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
                var bitmap = ResourceHelper.LoadFromPath(_currentImgPath);
                if (bitmap == null) return;

                EditImage.Source = bitmap;

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

            try { if (InkEditor.Strokes.Count > 0) SaveInk(); }
            finally { PreviewPopup.IsOpen = false; InkEditor.Strokes.Clear(); DualScreenShot.Focus(); }
        }

        private void SaveInk()
        {
            try
            {
                var source = (BitmapSource)EditImage.Source;
                var displaySize = new Size(
                    DrawingContainer.ActualWidth > 0 ? DrawingContainer.ActualWidth : DrawingContainer.Width,
                    DrawingContainer.ActualHeight > 0 ? DrawingContainer.ActualHeight : DrawingContainer.Height);
                var merged = ScreenshotHelper.MergeInk(source, InkEditor.Strokes, displaySize);
                if (merged != null) SaveImage(merged);
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

        private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((CompareImgControl)d).Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
        }
        private void ExitBtn_Click(object sender, RoutedEventArgs e) => IsOpen = false;
        #endregion


        #region 简单功能
        private void TempChangePic_Click(bool isLeft)
        {
            var dialog = new OpenFileDialog { Filter = "图片文件|*.jpg;*.png;*.bmp;*.webp", Title = "暂时替换当前图片" };
            string? picPath = dialog.ShowDialog() == true ? dialog.FileName : null;
            if (string.IsNullOrEmpty(picPath)) return;

            var bitmap = ResourceHelper.LoadFromPath(picPath);
            if (bitmap == null) return;

            var target = isLeft ? LeftPicView.TargetImage : RightPicView.TargetImage;
            target.SetCurrentValue(Image.SourceProperty, bitmap);
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
            string folderPath = ScreenshotHelper.GetFolder();

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
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;

            var vm = (DataContext as CompareImgVM);
            if (sender is FrameworkElement { Name: "RightPicView" })
                vm?.RightImageVM.OpenResourceByPath(files, false);
            else if (sender is FrameworkElement { Name: "LeftPicView" })
                vm?.LeftImageVM.OpenResourceByPath(files, false);
        }
        private void OnDragEnter(object sender, DragEventArgs e) => UpdateDragEffect(e);

        private void OnDragOver(object sender, DragEventArgs e)
        {
            UpdateDragEffect(e);
            e.Handled = true;
        }

        private static void UpdateDragEffect(DragEventArgs e) =>
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        #endregion
    }
}
