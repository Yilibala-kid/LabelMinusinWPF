using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using System.ComponentModel;
using System.Globalization;
using System.IO;
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
using System.Runtime.InteropServices;
using System.Windows.Interop;

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
            Task.Run(() => ClearTempFolders());
        }
        private static void ClearTempFolders()
        {
            // 定义需要清理的文件夹列表
            string[] tempFolders = ["OCRtemp", "ScreenShottemp", "Archivetemp"];

            foreach (string folderName in tempFolders)
            {
                try
                {
                    string folderPath =System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, folderName);

                    if (!Directory.Exists(folderPath))
                    {
                        Directory.CreateDirectory(folderPath);
                        continue;
                    }

                    // 优化点：不要直接删除文件夹，而是删除文件夹里的内容
                    DirectoryInfo di = new(folderPath);

                    // 1. 直接强力删除所有文件
                    foreach (FileInfo file in di.EnumerateFiles())
                    {
                        try { file.Delete(); }
                        catch (IOException) { /* 文件可能正在被占用，静默跳过 */ }
                    }

                    // 2. 递归删除所有子文件夹
                    foreach (DirectoryInfo dir in di.EnumerateDirectories())
                    {
                        try { dir.Delete(true); }
                        catch (IOException) { /* 文件夹内有文件被占用，静默跳过 */ }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"清理 {folderName} 失败: {ex.Message}");
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
            }
        }
    }



}