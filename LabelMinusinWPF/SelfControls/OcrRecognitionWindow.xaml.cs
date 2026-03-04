using Microsoft.Web.WebView2.Core;
using System;
using System.Windows;
using System.Windows.Media.Imaging;

namespace LabelMinusinWPF
{
    public partial class OcrRecognitionWindow : Window
    {
        public OcrRecognitionWindow(BitmapSource screenshot, string websiteUrl, string websiteName)
        {
            InitializeComponent();

            // 设置截图
            ScreenshotImage.Source = screenshot;

            // 设置标题
            Title = $"OCR识别 - {websiteName}";
            WebsiteTitle.Text = websiteName;

            // 初始化WebView2
            InitializeWebView(websiteUrl);
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
    }
}
