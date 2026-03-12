using LabelMinusinWPF.Common;
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
using AppMode = LabelMinusinWPF.Common.Constants.AppMode;
using Constants = LabelMinusinWPF.Common.Constants;
using ExportMode = LabelMinusinWPF.Common.LabelPlusParser.ExportMode;

namespace LabelMinusinWPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
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
        private DispatcherTimer? _autoSaveTimer;
        private const int AutoSaveIntervalMinutes = 5;
        private const int MaxAutoSaveFiles = 20;

        public MainWindow()
        {
            InitializeComponent();
            Task.Run(() => ProjectHelper.ClearTempFolders("OCRtemp", "ScreenShottemp", "Archivetemp"));
            Closing += MainWindow_Closing;
            Loaded += MainWindow_Loaded;
            RegisterMenu.IsChecked = ContextMenuRegistrar.IsRegistered();
            InitializeAutoSave();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 监听ViewModel的DisplayMode变化
            if (DataContext is MainViewModel viewModel)
            {
                UpdateLayout(DisplayMode.ListAndTextBox);
            }

            // 监听样式管理器的颜色变化以更新按钮高亮
            SelfControls.LabelStyleManager.Instance.PropertyChanged += LabelStyleManager_PropertyChanged;
            UpdateColorButtonHighlight();
        }

        private void LabelStyleManager_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SelfControls.LabelStyleManager.TextBackgroundColor) ||
                e.PropertyName == nameof(SelfControls.LabelStyleManager.TextForegroundColor))
            {
                UpdateColorButtonHighlight();
            }
        }

        private void UpdateColorButtonHighlight()
        {
            var styleManager = SelfControls.LabelStyleManager.Instance;

            // 更新背景颜色按钮高亮
            BgColorWhite.Tag = styleManager.TextBackgroundColor == Colors.White ? "Selected" : "White";
            BgColorBlack.Tag = styleManager.TextBackgroundColor == Colors.Black ? "Selected" : "Black";
            BgColorBlue.Tag = styleManager.TextBackgroundColor == Colors.RoyalBlue ? "Selected" : "RoyalBlue";
            BgColorTransparent.Tag = styleManager.TextBackgroundColor == Colors.Transparent ? "Selected" : "Transparent";

            // 更新前景颜色按钮高亮
            FgColorWhite.Tag = styleManager.TextForegroundColor == Colors.White ? "Selected" : "White";
            FgColorBlack.Tag = styleManager.TextForegroundColor == Colors.Black ? "Selected" : "Black";
            FgColorBlue.Tag = styleManager.TextForegroundColor == Colors.RoyalBlue ? "Selected" : "RoyalBlue";
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            // 停止自动保存计时器
            _autoSaveTimer?.Stop();

            // 获取 ViewModel
            if (DataContext is MainViewModel viewModel && viewModel.HasUnsavedChanges())
            {
                var result = MessageBox.Show(
                    "当前翻译有未保存的修改，是否保存？",
                    "提示",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question
                );

                if (result == MessageBoxResult.Yes)
                {
                    viewModel.SaveTranslationCommand.Execute(null);
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                }
            }
            SelfControls.LabelStyleManager.Instance.SaveSettings();
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

        private void UpdateTitleBarColor(bool isDark)
        {
            IntPtr hWnd = new WindowInteropHelper(this).Handle;
            int darkMode = isDark ? 1 : 0;
            DwmSetWindowAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
        }
        #endregion

        #region 菜单栏：显示控制
        private void OnLayoutModeClicked(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem clickedItem) return;
            if (clickedItem.Parent is not MenuItem parentMenu) return;

            // 1. 实现互斥单选
            foreach (var item in parentMenu.Items.OfType<MenuItem>())
            {
                if (item.Tag is DisplayMode)
                {
                    item.IsChecked = false;
                }
            }

            // 2. 勾选当前点击的项
            clickedItem.IsChecked = true;

            // 3. 执行布局更新
            if (clickedItem.Tag is DisplayMode mode)
            {
                UpdateLayout(mode);
            }
        }
        private void UpdateLayout(DisplayMode mode)
        {
            switch (mode)
            {
                case DisplayMode.ImageOnly:
                    RightColumn.Width = new GridLength(0);
                    MiddleColumn.Width = new GridLength(0);
                    break;

                case DisplayMode.ListAndTextBox:
                    LeftColumn.Width = new GridLength(1,GridUnitType.Star);
                    RightColumn.Width = new GridLength(1, GridUnitType.Star);
                    MiddleColumn.Width = new GridLength(1, GridUnitType.Auto);
                    DataGridRow.Height = new GridLength(4, GridUnitType.Star);
                    SplitterRow.Height = new GridLength(1, GridUnitType.Auto);
                    TextBoxRow.Height = new GridLength(1, GridUnitType.Star);
                    break;

                case DisplayMode.ListOnly:
                    LeftColumn.Width = new GridLength(1, GridUnitType.Star);
                    RightColumn.Width = new GridLength(1, GridUnitType.Star);
                    MiddleColumn.Width = new GridLength(1, GridUnitType.Auto);
                    DataGridRow.Height = new GridLength(4, GridUnitType.Star);
                    SplitterRow.Height = new GridLength(0);
                    TextBoxRow.Height = new GridLength(0);
                    break;

                case DisplayMode.TextBoxOnly:
                    LeftColumn.Width = new GridLength(1, GridUnitType.Star);
                    RightColumn.Width = new GridLength(1, GridUnitType.Star);
                    MiddleColumn.Width = new GridLength(1, GridUnitType.Auto);
                    DataGridRow.Height = new GridLength(0);
                    SplitterRow.Height = new GridLength(0);
                    TextBoxRow.Height = new GridLength(1, GridUnitType.Star);
                    break;
            }
        }
        #endregion

        #region 底栏图片控制
        // 适应视图
        private void FitToPage_Click(object sender, RoutedEventArgs e)
        {
            PicView.FitToView();
        }

        // 适应宽度
        private void FitWidth_Click(object sender, RoutedEventArgs e)
        {
            PicView.FitToWidth();
        }

        // 适应高度
        private void FitHeight_Click(object sender, RoutedEventArgs e)
        {
            PicView.FitToHeight();
        }

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
            // 直接操作状态，控件会自动显示
            FullScreenReview.IsOpen = true;
        }
        private void Recognize_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;

            // 从剪贴板获取最近截图的图片
            BitmapSource? screenshot = null;
            try
            {
                if (Clipboard.ContainsImage())
                    screenshot = Clipboard.GetImage();
            }
            catch { }

            if (screenshot == null)
            {
                vm.MainMessageQueue.Enqueue("请先截图，再点击识别");
                return;
            }

            string websiteName = vm.SelectedOcrWebsite;
            // 直接用 TryGetValue 获取，安全又简洁
            string websiteUrl = MainViewModel.OcrWebsites.TryGetValue(websiteName, out var url)
                                ? url
                                : MainViewModel.OcrWebsites["百度"]; // 默认回退到百度

            var ocrWindow = new OcrRecognitionWindow(screenshot, websiteUrl, websiteName);
            ocrWindow.Show();
        }


        private void BgColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn) return;
            var color = GetColorFromButton(btn, "BgColor");
            SelfControls.LabelStyleManager.Instance.TextBackgroundColor = color;
        }

        private void FgColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn) return;
            var color = GetColorFromButton(btn, "FgColor");
            SelfControls.LabelStyleManager.Instance.TextForegroundColor = color;
        }

        private static Color GetColorFromButton(System.Windows.Controls.Button btn, string prefix)
        {
            string colorName = btn.Tag?.ToString() ?? string.Empty;
            if (colorName == "Selected")
                colorName = btn.Name.Replace(prefix, "");

            return colorName switch
            {
                "White" => Colors.White,
                "Black" => Colors.Black,
                "RoyalBlue" => Colors.RoyalBlue,
                "Transparent" => Colors.Transparent,
                _ => prefix == "BgColor" ? Colors.White : Colors.Black
            };
        }

        #region 拖放文件支持
        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0 && DataContext is MainViewModel viewModel)
                {
                    viewModel.OpenResourceByPath(files, false);
                }
            }
        }
        #endregion
        private void OnOpenWebsite(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string url)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true // 必须设置为 true 才能在 .NET Core 中打开浏览器
                    });
                }
                catch (System.Exception ex)
                {
                    MessageBox.Show($"无法打开网页: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        private void About_Click(object sender, RoutedEventArgs e)
        {
            if (this.DataContext is MainViewModel vm)
            {
                // 访问 MainMessageQueue
                vm.MainMessageQueue.Enqueue("本程序由No-Hifuu友情赞助");
            }
        }

        #region 命令行启动支持
        public void OpenFilesOnStartup(string[] paths)
        {
            if (DataContext is MainViewModel vm && paths.Length > 0)
            {
                if (FullScreenReview.IsOpen)
                {
                    FullScreenReview.IsOpen = false;
                }
                // 直接传递整个数组，ViewModel 会负责循环加载
                vm.OpenResourceByPath(paths, false);
            }
        }
        public void OpenImageReviewSmart(string newPath)
        {
            if (DataContext is not MainViewModel vm) return;

            // 1. 确保图校界面是打开的
            if (!FullScreenReview.IsOpen)
            {
                FullScreenReview.IsOpen = true;
            }

            // 2. 这里的延迟执行非常重要，确保子 VM 已经就绪
            Dispatcher.BeginInvoke(() =>
            {
                if (FullScreenReview.DataContext is ImageReviewVM reviewVm)
                {
                    // 如果左侧 VM 还没有路径，或者当前不是对比模式且左侧是空的
                    // 我们简单的逻辑：如果左侧没图，放左侧；否则放右侧。
                    bool isLeftEmpty = reviewVm.LeftImageVM.ImageList.Count == 0;

                    if (isLeftEmpty)
                    {
                        reviewVm.LeftImageVM.OpenResourceByPath([newPath], false);
                    }
                    else
                    {
                        // 左侧有图了，把新图放右侧，并强制开启双图对比
                        reviewVm.RightImageVM.OpenResourceByPath([newPath], false);
                    }

                    // 激活窗口并置顶提醒
                    this.FocusWindow();
                }
            }, System.Windows.Threading.DispatcherPriority.Loaded);
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
            if (DataContext is MainViewModel vm)
            {
                switch (e.Key)
                {
                    // --- 模式切换 (1, 2, 3) ---
                    case Key.D1:
                    case Key.NumPad1:
                        vm.CurrentMode = AppMode.See;
                        e.Handled = true;
                        break;
                    case Key.D2:
                    case Key.NumPad2:
                        vm.CurrentMode = AppMode.LabelDo;
                        e.Handled = true;
                        break;
                    case Key.D3:
                    case Key.NumPad3:
                        vm.CurrentMode = AppMode.OCR;
                        e.Handled = true;
                        break;

                    // --- 图片切换 (A: 上一张, D: 下一张) ---
                    case Key.A:
                        if (vm.PreviousImageCommand.CanExecute(null))
                            vm.PreviousImageCommand.Execute(null);
                        e.Handled = true;
                        break;
                    case Key.D:
                        if (vm.NextImageCommand.CanExecute(null)) // RelayCommand 会自动处理 CanExecute
                            vm.NextImageCommand.Execute(null);
                        e.Handled = true;
                        break;

                    // --- 标签切换 (W: 上一个, S: 下一个) ---
                    case Key.W:
                        if (vm.PreviousLabelCommand.CanExecute(null))
                            vm.PreviousLabelCommand.Execute(null);
                        e.Handled = true;
                        break;
                    case Key.S:
                        if (vm.NextLabelCommand.CanExecute(null))
                            vm.NextLabelCommand.Execute(null);
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
        private void InitializeAutoSave()
        {
            _autoSaveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(AutoSaveIntervalMinutes)
            };
            _autoSaveTimer.Tick += AutoSaveTimer_Tick;
            _autoSaveTimer.Start();
        }

        private void AutoSaveTimer_Tick(object? sender, EventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;

            // 只有在MainWindow中有翻译项目且有修改时才自动保存
            if (vm.CurrentProject == ProjectHelper.ProjectContext.Empty || !vm.HasUnsavedChanges())
                return;

            // 不保存ImageReview中的内容
            if (FullScreenReview.IsOpen)
                return;

            try
            {
                // 获取程序所在目录
                string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string autoSaveFolder = System.IO.Path.Combine(appDirectory, "AutoSave");

                // 确保AutoSave文件夹存在
                Directory.CreateDirectory(autoSaveFolder);

                // 生成文件名: 翻译文件名_保存时间.txt
                string originalFileName = string.IsNullOrEmpty(vm.CurrentProject.TxtName)
                    ? "未命名翻译"
                    : System.IO.Path.GetFileNameWithoutExtension(vm.CurrentProject.TxtName);
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string autoSaveFileName = $"{originalFileName}_{timestamp}.txt";
                string autoSavePath = System.IO.Path.Combine(autoSaveFolder, autoSaveFileName);

                // 保存翻译
                string outputText = LabelPlusParser.LabelsToText(
                    vm.ImageList,
                    vm.CurrentProject.ZipName,
                    ExportMode.Current
                );
                File.WriteAllText(autoSavePath, outputText);

                // 清理旧的自动保存文件,只保留最新的20个
                CleanupOldAutoSaveFiles(autoSaveFolder, originalFileName);
            }
            catch (Exception ex)
            {
                // 自动保存失败不影响用户操作,只记录日志
                System.Diagnostics.Debug.WriteLine($"自动保存失败: {ex.Message}");
            }
        }

        private void CleanupOldAutoSaveFiles(string autoSaveFolder, string baseFileName)
        {
            try
            {
                // 获取所有相关的自动保存文件
                var files = Directory.GetFiles(autoSaveFolder, $"{baseFileName}_*.txt")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTime)
                    .ToList();

                // 删除超过MaxAutoSaveFiles的旧文件
                foreach (var file in files.Skip(MaxAutoSaveFiles))
                {
                    try
                    {
                        file.Delete();
                    }
                    catch
                    {
                        // 删除失败不影响程序运行
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"清理旧自动保存文件失败: {ex.Message}");
            }
        }
        #endregion
    }



}