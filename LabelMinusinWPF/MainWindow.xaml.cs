using Microsoft.Win32;
using System.ComponentModel;
using System.Globalization;
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
using MaterialDesignThemes.Wpf;

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
            this.DataContext = new MainViewModel();
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

        #region 图片缩放与平移
        private Point _lastMousePosition;
        private bool _isDragging;

        // --- 缩放逻辑 (鼠标滚轮) ---
        private void ImageParent_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            var element = ImageParent;
            Point position = e.GetPosition(element);

            // 计算缩放比例
            double scaleFactor = e.Delta > 0 ? 1.1 : 0.9;
            double newScale = ImageScale.ScaleX * scaleFactor;

            // 限制缩放范围（0.1倍到10倍）
            if (newScale < 0.1 || newScale > 10) return;

            // 这一步是实现“以鼠标指向点为中心缩放”的核心算法
            double relativeX = position.X - ImageTranslate.X;
            double relativeY = position.Y - ImageTranslate.Y;

            ImageTranslate.X -= relativeX * (scaleFactor - 1);
            ImageTranslate.Y -= relativeY * (scaleFactor - 1);

            ImageScale.ScaleX = newScale;
            ImageScale.ScaleY = newScale;
        }

        // --- 平移逻辑 (鼠标中键或左键) ---
        private void ImageParent_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // 这里使用中键或左键拖拽，你可以根据习惯修改
            if (e.ChangedButton == MouseButton.Left || e.ChangedButton == MouseButton.Middle)
            {
                _lastMousePosition = e.GetPosition(ImageParent);
                _isDragging = true;
                ImageParent.CaptureMouse(); // 捕获鼠标，防止移出范围失效
                Mouse.OverrideCursor = Cursors.Hand; // 变成小手
            }
        }

        private void ImageParent_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                Point currentPosition = e.GetPosition(ImageParent);
                Vector delta = currentPosition - _lastMousePosition;

                ImageTranslate.X += delta.X;
                ImageTranslate.Y += delta.Y;

                _lastMousePosition = currentPosition;
            }
        }

        private void ImageParent_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            ImageParent.ReleaseMouseCapture();
            Mouse.OverrideCursor = null;
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