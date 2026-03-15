using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace LabelMinusinWPF
{
    public partial class OcrWindow : Window
    {
        private string? _tempImagePath;
        public OcrWindow(BitmapSource screenshot, string websiteUrl, string websiteName)
        {
            InitializeComponent();

            // 设置截图
            ScreenshotImage.Source = screenshot;
            SaveImageToOcrTemp(screenshot);
            // 设置标题
            Title = $"OCR识别 - {websiteName}";
            WebsiteTitle.Text = websiteName;

            // 初始化WebView2
            InitializeWebView(websiteUrl);
            this.Closed += OnClosed;
        }

        private async void InitializeWebView(string url)
        {
            try
            {
                await WebBrowser.EnsureCoreWebView2Async(null);
                WebBrowser.CoreWebView2.Navigate(url);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"WebView2初始化失败：{ex.Message}\n\n请确保已安装WebView2 Runtime。",
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveImageToOcrTemp(BitmapSource bitmapSource)
        {
            try
            {
                // 获取 OCRtemp 文件夹的绝对路径
                string folderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OCRtemp");

                // 确保文件夹存在
                if (!Directory.Exists(folderPath))
                    Directory.CreateDirectory(folderPath);

                // 生成唯一文件名，防止多开窗口时冲突
                string fileName = $"temp_{DateTime.Now:HHmmssfff}.png";
                _tempImagePath = Path.Combine(folderPath, fileName);

                using (var fileStream = new FileStream(_tempImagePath, FileMode.Create))
                {
                    BitmapEncoder encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
                    encoder.Save(fileStream);
                }
            }
            catch (Exception ex)
            {
                // 这里可以调用你 ViewModel 里的消息队列报错
                MessageBox.Show($"图片缓存失败: {ex.Message}");
            }
        }

        // 鼠标移动事件：检测左键按下并启动拖拽
        private void OnScreenshotMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && !string.IsNullOrEmpty(_tempImagePath) && File.Exists(_tempImagePath))
            {
                DataObject data = new DataObject(DataFormats.FileDrop, new string[] { _tempImagePath });
                DragDrop.DoDragDrop(ScreenshotImage, data, DragDropEffects.Copy);
            }
        }

        // 窗口关闭时清理临时文件
        private void OnClosed(object? sender, System.EventArgs e)
        {
            if (!string.IsNullOrEmpty(_tempImagePath) && File.Exists(_tempImagePath))
            {
                try
                {
                    File.Delete(_tempImagePath);
                }
                catch { /* 忽略删除失败，系统最终也会清理 Temp 文件夹 */ }
            }
        }
    }
}
