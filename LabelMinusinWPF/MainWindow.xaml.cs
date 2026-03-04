using LabelMinusinWPF.Utilities;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using System.ComponentModel;
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
                    MessageBox.Show("右键菜单注册成功！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    ContextMenuRegistrar.UnregisterAll();
                    MessageBox.Show("右键菜单已取消注册", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // 回滚勾选状态，因为本次操作没成功
                menuItem.IsChecked = !shouldRegister;

                var result = MessageBox.Show(
                    "修改注册表需要管理员权限。\n\n是否立即以管理员身份重启程序？",
                    "权限不足",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    RestartAsAdmin();
                }
            }
            catch (Exception ex)
            {
                menuItem.IsChecked = !shouldRegister;
                MessageBox.Show($"操作失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void RestartAsAdmin()
        {
            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = System.Environment.ProcessPath, // 获取当前运行的 exe 路径
                UseShellExecute = true,
                Verb = "runas" // 关键：触发 UAC 提权
            };

            try
            {
                System.Diagnostics.Process.Start(processInfo);
                Application.Current.Shutdown(); // 关闭当前非管理员实例
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // 用户在 UAC 界面点击了“否”
                MessageBox.Show("未获得管理员权限，操作已取消。", "提示");
            }
        }
        #endregion

        private void OpenImageReview_Click(object sender, RoutedEventArgs e)
        {
            // 直接操作状态，控件会自动显示
            FullScreenReview.IsOpen = true;
        }


        private void DotStyleSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 确保组件已经加载
            if (DotStyleSelector.SelectedItem is ComboBoxItem item && PicView != null)
            {
                string styleKey = item.Tag.ToString();
                // 动态查找资源字典中的样式并赋值给图片控件
                PicView.LabelDotStyle = (Style)FindResource(styleKey);

                // 更新预览
                if (DotPreview != null)
                {
                    switch (styleKey)
                    {
                        case "DefaultDotStyle":
                            DotPreview.Width = 20;
                            DotPreview.Height = 20;
                            DotPreview.Fill = new SolidColorBrush(Colors.RoyalBlue);
                            DotPreview.Stroke = new SolidColorBrush(Colors.White);
                            DotPreview.StrokeThickness = 2;
                            break;
                        case "SquareDotStyle":
                            DotPreview.Width = 16;
                            DotPreview.Height = 16;
                            DotPreview.Fill = new SolidColorBrush(Colors.RoyalBlue);
                            DotPreview.Stroke = new SolidColorBrush(Colors.White);
                            DotPreview.StrokeThickness = 2;
                            break;
                        case "TransparentDotStyle":
                            DotPreview.Width = 20;
                            DotPreview.Height = 20;
                            DotPreview.Fill = Brushes.Transparent;
                            DotPreview.Stroke = new SolidColorBrush(Colors.RoyalBlue);
                            DotPreview.StrokeThickness = 2;
                            break;
                    }
                }
            }
        }

        private void BackgroundColorSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BackgroundColorSelector.SelectedItem is ComboBoxItem item && TextPreviewBackground != null && PicView != null)
            {
                string colorName = item.Tag.ToString();
                Color color = colorName switch
                {
                    "White" => Colors.White,
                    "Black" => Colors.Black,
                    "RoyalBlue" => Colors.RoyalBlue,
                    _ => Colors.White
                };
                TextPreviewBackground.Color = color;
                PicView.TextBackgroundColor = new SolidColorBrush(color);
            }
        }

        private void ForegroundColorSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ForegroundColorSelector.SelectedItem is ComboBoxItem item && TextPreviewForeground != null && PicView != null)
            {
                string colorName = item.Tag.ToString();
                Color color = colorName switch
                {
                    "White" => Colors.White,
                    "Black" => Colors.Black,
                    "RoyalBlue" => Colors.RoyalBlue,
                    _ => Colors.Black
                };
                TextPreviewForeground.Color = color;
                PicView.TextForegroundColor = new SolidColorBrush(color);
            }
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
    }



}