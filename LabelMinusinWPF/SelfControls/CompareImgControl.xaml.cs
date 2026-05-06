using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using LabelMinusinWPF.Common;

namespace LabelMinusinWPF
{

    /// ImageReview.xaml 的交互逻辑

    public partial class CompareImgControl : UserControl
    {
        private DispatcherTimer _closeTimer;
        private string _currentImgPath = string.Empty;

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
        # region 快捷键
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
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (!IsShortcutScopeActive() || IsTextInputFocused()) return;
            if (DataContext is not CompareImgVM vm) return;

            var key = GetActualKey(e);
            var modifiers = Keyboard.Modifiers;

            if (TryHandleShortcut(key, modifiers, vm, e.IsRepeat))
                e.Handled = true;
        }

        private bool TryHandleShortcut(Key key, ModifierKeys modifiers, CompareImgVM vm, bool isRepeat)
        {
            switch (key)
            {
                case Key.Q when modifiers == ModifierKeys.None:
                    if (!isRepeat) SetScreenShotEnabled(true);
                    return true;

                case Key.Left when modifiers == ModifierKeys.None:
                case Key.A when modifiers == ModifierKeys.None:
                    ExecuteCommand(vm.PreviousImageCommand);
                    return true;

                case Key.Right when modifiers == ModifierKeys.None:
                case Key.D when modifiers == ModifierKeys.None:
                    ExecuteCommand(vm.NextImageCommand);
                    return true;

                case Key.R when modifiers == ModifierKeys.None:
                    ExecuteCommand(vm.ResetSyncCommand);
                    return true;

                case Key.R when modifiers == ModifierKeys.Control:
                    ResetSplitterLayout();
                    return true;

                case Key.G when modifiers == ModifierKeys.None:
                    ToggleMode();
                    return true;

                case Key.C when modifiers == ModifierKeys.None:
                    ExecuteCommand(vm.SwapImagesCommand);
                    return true;

                case Key.P when modifiers == ModifierKeys.None:
                    ExecuteCommand(vm.ClearImagesCommand);
                    return true;

                case Key.H when modifiers == ModifierKeys.None:
                    ToggleSplitFollowMouse();
                    return true;

                case Key.F1 when modifiers == ModifierKeys.None:
                    ToggleTopDrawer();
                    return true;

                default:
                    return false;
            }
        }

        private void OnKeyUp(object sender, KeyEventArgs e)
        {
            if (GetActualKey(e) != Key.Q) return;
            SetScreenShotEnabled(false);
            if (IsShortcutScopeActive()) e.Handled = true;
        }

        private bool IsShortcutScopeActive() => IsOpen && IsVisible;

        private bool IsTextInputFocused()
        {
            var window = Window.GetWindow(this);
            return Keyboard.FocusedElement is TextBox
                || (window != null && FocusManager.GetFocusedElement(window) is TextBox);
        }

        private static Key GetActualKey(KeyEventArgs e) => e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            Key.DeadCharProcessed => e.DeadCharProcessedKey,
            _ => e.Key
        };

        private static void ExecuteCommand(ICommand command)
        {
            if (command.CanExecute(null)) command.Execute(null);
        }
        #endregion

        #region 视图状态
        private void ToggleTopDrawer() => Toggle(ImageReviewMenu);

        private void ToggleMode() => Toggle(ModeToggle);

        private void ModeToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (DataContext is CompareImgVM vm) vm.IsSyncEnabled = true;
        }

        private void ToggleSplitFollowMouse() => Toggle(SplitFollowMouseToggle);

        private static void Toggle(ToggleButton toggle) => toggle.IsChecked = toggle.IsChecked != true;

        private void SetScreenShotEnabled(bool isEnabled) => DualScreenShot.IsChecked = isEnabled;

        private bool IsScreenShotEnabled() => DualScreenShot.IsChecked == true;

        private void ResetSplitter_Click(object sender, RoutedEventArgs e) => ResetSplitterLayout();

        private void ResetSplitterLayout()
        {
            LeftReviewColumn.Width = new GridLength(1, GridUnitType.Star);
            RightReviewColumn.Width = new GridLength(1, GridUnitType.Star);
        }
        #endregion

        #region 分割线跟随鼠标
        private void OnMouseMoveForSplit(object sender, MouseEventArgs e)
        {
            if (SplitFollowMouseToggle.IsChecked != true) return;
            if (IsScreenShotEnabled()) return;

            if (sender is not FrameworkElement { ActualWidth: > 0 } container) return;

            double ratio = GetSplitRatio(container, e);
            LeftReviewColumn.Width = new GridLength(ratio, GridUnitType.Star);
            RightReviewColumn.Width = new GridLength(1 - ratio, GridUnitType.Star);
        }

        private static double GetSplitRatio(FrameworkElement container, MouseEventArgs e)
        {
            double ratio = e.GetPosition(container).X / container.ActualWidth;
            return Math.Clamp(ratio, 0.05, 0.95);
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
            var control = (CompareImgControl)d;
            bool isOpen = (bool)e.NewValue;
            control.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
            if (!isOpen) control.SetScreenShotEnabled(false);
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
