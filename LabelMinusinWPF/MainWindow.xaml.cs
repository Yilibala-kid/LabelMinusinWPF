using LabelMinusinWPF.Utilities;
using MahApps.Metro.Controls;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using static LabelMinusinWPF.Modules;
using static MaterialDesignThemes.Wpf.Theme;

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
        public MainWindow()
        {
            InitializeComponent();
            Task.Run(() => FileSystemHelper.ClearTempFolders("OCRtemp", "ScreenShottemp", "Archivetemp"));
            Closing += MainWindow_Closing;
            Loaded += MainWindow_Loaded;
            RegisterMenu.IsChecked = ContextMenuRegistrar.IsRegistered();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 监听ViewModel的DisplayMode变化
            if (DataContext is MainViewModel viewModel)
            {
                UpdateLayout(DisplayMode.ListAndTextBox);
            }
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
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
            string websiteUrl = vm.OcrWebsites.TryGetValue(websiteName, out var url)
                                ? url
                                : vm.OcrWebsites["百度"]; // 默认回退到百度

            var ocrWindow = new OcrRecognitionWindow(screenshot, websiteUrl, websiteName);
            ocrWindow.Show();
        }


        private void BgColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn) return;

            string colorName = btn.Tag.ToString();
            Color color = colorName switch
            {
                "White" => Colors.White,
                "Black" => Colors.Black,
                "RoyalBlue" => Colors.RoyalBlue,
                "Transparent" => Colors.Transparent,
                _ => Colors.White
            };

            SelfControls.LabelStyleManager.Instance.TextBackgroundColor = color;

        }

        private void FgColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn) return;

            string colorName = btn.Tag.ToString();
            Color color = colorName switch
            {
                "White" => Colors.White,
                "Black" => Colors.Black,
                "RoyalBlue" => Colors.RoyalBlue,
                _ => Colors.Black
            };

            SelfControls.LabelStyleManager.Instance.TextForegroundColor = color;
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
        public void OpenFileOnStartup(string filePath)
        {
            if (DataContext is MainViewModel vm && System.IO.File.Exists(filePath))
            {
                vm.OpenResourceByPath([filePath], false);
            }
        }

        public void OpenImageReviewWithFile(string filePath)
        {
            if (DataContext is MainViewModel vm && System.IO.File.Exists(filePath))
            {
                // 先打开图校界面
                FullScreenReview.IsOpen = true;

                // 获取图校界面的ViewModel并加载文件
                if (FullScreenReview.DataContext is ImageReviewVM reviewVm)
                {
                    // 判断文件类型，加载到左侧或右侧
                    reviewVm.LeftImageVM.OpenResourceByPath([filePath], false);
                }
            }
        }
        #endregion


    }



}