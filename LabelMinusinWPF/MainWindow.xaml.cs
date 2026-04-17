using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelMinusinWPF.Common;
using LabelMinusinWPF.SelfControls;
using MahApps.Metro.Controls;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using static MaterialDesignThemes.Wpf.Theme;
using Constants = LabelMinusinWPF.Common.Constants;
using ExportMode = LabelMinusinWPF.Common.LabelPlusParser.ExportMode;
using WorkSpace = LabelMinusinWPF.Common.ProjectManager.WorkSpace;

namespace LabelMinusinWPF
{

    /// Interaction logic for MainWindow.xaml

    ///
    public enum DisplayMode
    {
        ImageOnly,
        ListAndTextBox,
        ListOnly,
        TextBoxOnly
    }
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Task.Run(() => ProjectManager.ClearTempFolders(
                Constants.TempFolders.OcrTemp,
                Constants.TempFolders.ScreenShotTemp,
                Constants.TempFolders.ArchiveTemp));
            Closing += MainWindow_Closing;
            RegisterMenu.IsChecked = ContextMenuRegistrar.IsRegistered();
            LabelStylePanel.Instance.LoadSettings();
            InitializeAutoSave();
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            // 获取 ViewModel
            if (DataContext is MainVM viewModel && viewModel.HasUnsavedChanges())
            {
                var result = MessageBox.Show(
                    "当前翻译有未保存的修改，是否保存？",
                    "提示",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question
                );

                if (result == MessageBoxResult.Yes)
                {
                    viewModel.SaveCommand.Execute(null);
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                }
            }
        }
        #region 黑白模式控制
        // 定义依赖属性
        public static readonly DependencyProperty IsDarkModeProperty =
            DependencyProperty.Register("IsDarkMode", typeof(bool), typeof(MainWindow),
                new PropertyMetadata(false, OnDarkModeChanged));

        public bool IsDarkMode
        {
            get { return (bool)GetValue(IsDarkModeProperty); }
            set { SetValue(IsDarkModeProperty, value); }
        }

        // 当属性改变时，自动触发主题切换
        private static void OnDarkModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            bool isDark = (bool)e.NewValue;
            var paletteHelper = new PaletteHelper();
            var theme = paletteHelper.GetTheme();
            theme.SetBaseTheme(isDark ? BaseTheme.Dark : BaseTheme.Light);
            paletteHelper.SetTheme(theme);
            // 同时更新标题栏颜色
            if (d is MainWindow mainWindow)
            {
                mainWindow.UpdateTitleBarColor(isDark);
            }
        }
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int IntPtrSize = 4;

        private void UpdateTitleBarColor(bool isDark)
        {
            IntPtr hWnd = new WindowInteropHelper(this).Handle;
            int darkMode = isDark ? 1 : 0;
            _ = DwmSetWindowAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, IntPtrSize);
        }
        #endregion

        #region 菜单栏：显示控制
        private void OnLayoutModeClicked(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem clickedItem) return;
            if (clickedItem.Parent is not MenuItem parentMenu) return;

            foreach (var item in parentMenu.Items.OfType<MenuItem>())
                if (item.Tag is DisplayMode) item.IsChecked = false;

            clickedItem.IsChecked = true;

            if (clickedItem.Tag is DisplayMode mode) UpdateLayout(mode);
        }
        private void UpdateLayout(DisplayMode mode)
        {
            // 共同的列宽设置
            LeftColumn.Width = new GridLength(1, GridUnitType.Star);
            RightColumn.Width = new GridLength(1, GridUnitType.Star);
            MiddleColumn.Width = new GridLength(1, GridUnitType.Auto);

            switch (mode)
            {
                case DisplayMode.ImageOnly:
                    RightColumn.Width = new GridLength(0);
                    MiddleColumn.Width = new GridLength(0);
                    break;

                case DisplayMode.ListAndTextBox:
                    DataGridRow.Height = new GridLength(4, GridUnitType.Star);
                    SplitterRow.Height = new GridLength(1, GridUnitType.Auto);
                    TextBoxRow.Height = new GridLength(1, GridUnitType.Star);
                    break;

                case DisplayMode.ListOnly:
                    DataGridRow.Height = new GridLength(4, GridUnitType.Star);
                    SplitterRow.Height = new GridLength(0);
                    TextBoxRow.Height = new GridLength(0);
                    break;

                case DisplayMode.TextBoxOnly:
                    DataGridRow.Height = new GridLength(0);
                    SplitterRow.Height = new GridLength(0);
                    TextBoxRow.Height = new GridLength(1, GridUnitType.Star);
                    break;
            }
        }
        #endregion

        #region 底栏图片控制
        private void FitToPage_Click(object sender, RoutedEventArgs e) => PicView.FitToView();
        private void FitWidth_Click(object sender, RoutedEventArgs e) => PicView.FitToWidth();
        private void FitHeight_Click(object sender, RoutedEventArgs e) => PicView.FitToHeight();
        private void ZoomIn_Click(object sender, RoutedEventArgs e) => PicView.ZoomScale *= 1.1;
        private void ZoomOut_Click(object sender, RoutedEventArgs e) => PicView.ZoomScale *= 0.9;
        #endregion


        #region 右键菜单：注册/注销
        private void OnToggleContextMenu(object sender, RoutedEventArgs e)
        {
            var menuItem = (MenuItem)sender;
            bool shouldRegister = menuItem.IsChecked;

            try
            {
                if (shouldRegister)
                {
                    ContextMenuRegistrar.RegisterAll();
                    MessageBox.Show("右键菜单注册成功！", "提示",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    ContextMenuRegistrar.UnregisterAll();
                    MessageBox.Show("右键菜单已取消注册", "提示",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                // 回滚勾选状态
                menuItem.IsChecked = !shouldRegister;

                MessageBox.Show($"操作失败：{ex.Message}",
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        #endregion
        private void OpenImageReview_Click(object sender, RoutedEventArgs e)
        {
            FullScreenReview.IsOpen = true;
        }

        private void Recognize_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainVM vm) return;
            var screenshot = ScreenshotHelper.TryGetClipboardImage();
            if (screenshot == null) { vm.MsgQueue.Enqueue("请先截图，再点击识别"); return; }

            string websiteName = OcrWebsiteSelector.SelectedItem as string ?? Constants.OcrWebsites.DefaultWebsite;
            string websiteUrl = Constants.OcrWebsites.Websites.TryGetValue(websiteName, out var url)
                                ? url
                                : Constants.OcrWebsites.Websites[Constants.OcrWebsites.DefaultWebsite];

            new OcrWindow(screenshot, websiteUrl, websiteName).Show();
        }


        #region 拖放文件支持
        private void OnFileDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnFileDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                if (DataContext is MainVM viewModel) viewModel.OpenResourceByPath(files, false);
        }
        #endregion
        private void OnOpenWebsite(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: string url })
            {
                try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
                catch (Exception ex) { MessageBox.Show($"无法打开网页: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
        }
        private void About_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainVM vm) vm.MsgQueue.Enqueue("本程序由No-Hifuu友情赞助");
        }

        #region 命令行启动支持
        public void OpenFilesOnStartup(string[] paths)
        {
            if (DataContext is MainVM vm && paths.Length > 0)
            {
                if (FullScreenReview.IsOpen) FullScreenReview.IsOpen = false;
                vm.OpenResourceByPath(paths, false);
            }
        }
        public void OpenImageReviewSmart(string newPath)
        {
            if (DataContext is not MainVM vm) return;
            if (!FullScreenReview.IsOpen) FullScreenReview.IsOpen = true;

            Dispatcher.BeginInvoke(() =>
            {
                if (FullScreenReview.DataContext is CompareImgVM reviewVm)
                {
                    if (reviewVm.LeftImageVM.ImageList.Count == 0)
                        reviewVm.LeftImageVM.OpenResourceByPath([newPath], false);
                    else
                        reviewVm.RightImageVM.OpenResourceByPath([newPath], false);

                    FocusWindow();
                }
            }, DispatcherPriority.Loaded);
        }
        public void FocusWindow()
        {
            if (this.WindowState == WindowState.Minimized)
                this.WindowState = WindowState.Normal;

            this.Activate(); // 尝试获取焦点
        }
        #endregion

        #region 快捷键
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // 【重要防护】如果用户当前正在 TextBox 里输入文字，则不触发快捷键
            if (e.OriginalSource is System.Windows.Controls.TextBox) return;
            if (FullScreenReview.IsOpen) return; // 图校界面打开时禁用快捷键，避免冲突
            if (DataContext is MainVM vm)
            {
                switch (e.Key)
                {
                    // --- 图片切换 (A: 上一张, D: 下一张) ---
                    case Key.A:
                        vm.PreviousImageCommand.Execute(null);
                        e.Handled = true;
                        break;
                    case Key.D:
                        vm.NextImageCommand.Execute(null);
                        e.Handled = true;
                        break;

                    // --- 标签切换 (W: 上一个, S: 下一个) ---
                    case Key.W:
                        vm.SelectedImage?.PreviousLabelCommand.Execute(null);
                        e.Handled = true;
                        break;
                    case Key.S:
                        vm.SelectedImage?.NextLabelCommand.Execute(null);
                        e.Handled = true;
                        break;
                    case Key.R:
                        PicView.FitToView();
                        e.Handled = true;
                        break;
                }
            }
        }
        #endregion

        #region 自动保存功能
        private DispatcherTimer? _autoSaveTimer;
        private void InitializeAutoSave()
        {
            _autoSaveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(Constants.AutoSave.IntervalMinutes)
            };
            _autoSaveTimer.Tick += AutoSaveTimer_Tick;
            _autoSaveTimer.Start();
        }

        private void AutoSaveTimer_Tick(object? sender, EventArgs e)
        {
            if (DataContext is not MainVM vm) return;
            if (vm.WorkSpace == WorkSpace.Empty || !vm.HasUnsavedChanges()) return;
            if (FullScreenReview.IsOpen) return;

            try
            {
                string autoSaveFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AutoSave");
                Directory.CreateDirectory(autoSaveFolder);

                string originalFileName = string.IsNullOrEmpty(vm.WorkSpace.TxtName)
                    ? "未命名翻译"
                    : Path.GetFileNameWithoutExtension(vm.WorkSpace.TxtName);
                string autoSavePath = Path.Combine(autoSaveFolder, $"{originalFileName}_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

                File.WriteAllText(autoSavePath, LabelPlusParser.LabelsToText(vm.ImageList, vm.WorkSpace.ZipName, ExportMode.Current));
                CleanupOldAutoSaveFiles(autoSaveFolder, originalFileName);
            }
            catch (Exception ex) { Debug.WriteLine($"自动保存失败: {ex.Message}"); }
        }

        private static void CleanupOldAutoSaveFiles(string autoSaveFolder, string baseFileName)
        {
            try
            {
                var files = Directory.GetFiles(autoSaveFolder, $"{baseFileName}_*.txt")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTime)
                    .Skip(Constants.AutoSave.MaxFiles);

                foreach (var file in files)
                    try { file.Delete(); } catch { /* 删除失败不影响程序运行 */ }
            }
            catch (Exception ex) { Debug.WriteLine($"清理旧自动保存文件失败: {ex.Message}"); }
        }
        #endregion

    }
}
