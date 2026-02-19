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

        #region 黑白模式
        private static void ToggleDarkMode(bool isDark)
        {
            var paletteHelper = new PaletteHelper();
            // 显式指定 MaterialDesignThemes.Wpf.ITheme 以防冲突
            var theme = paletteHelper.GetTheme();

            // 修改基础主题
            theme.SetBaseTheme(isDark ? BaseTheme.Dark : BaseTheme.Light);

            // 重新应用
            paletteHelper.SetTheme(theme);
        }
        private void DarkMode_Checked(object sender, RoutedEventArgs e)
        {
            ToggleDarkMode(true);
        }

        private void DarkMode_Unchecked(object sender, RoutedEventArgs e)
        {
            ToggleDarkMode(false);
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
            FullScreenReview.Visibility = Visibility.Visible;
        }
        private void FullScreenReview_ExitClicked(object sender, EventArgs e)
        {
            FullScreenReview.Visibility = Visibility.Collapsed;
        }
    }



}