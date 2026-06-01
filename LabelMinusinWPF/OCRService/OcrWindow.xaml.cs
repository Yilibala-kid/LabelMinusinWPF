using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using LabelMinusinWPF.Common;

namespace LabelMinusinWPF.OCRService
{
    public partial class OcrWindow : Window
    {
        private string? _tempImagePath;

        private WebView2 WebBrowser { get; } = new();

        public OcrWindow(BitmapSource screenshot, string websiteUrl, string websiteName)
        {
            InitializeComponent();
            WebBrowserHost.Children.Add(WebBrowser);

            UpdateScreenshot(screenshot);

            Title = $"字体识别 - {websiteName}";
            WebsiteTitle.Text = websiteName;

            InitializeWebView(websiteUrl);
            Closed += OnClosed;
        }

        public void UpdateScreenshot(BitmapSource screenshot)
        {
            ScreenshotImage.Source = screenshot;
            SaveImageToOcrTemp(screenshot);
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
                MessageBox.Show(
                    $"WebView2 初始化失败：{ex.Message}\n\n请确保已安装 WebView2 Runtime。",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private void SaveImageToOcrTemp(BitmapSource bitmapSource)// 保存为临时文件，方便拖到外部字体识别网站。
        {
            try
            {
                DeleteTempImage();
                _tempImagePath = ScreenshotHelper.SaveSnip(bitmapSource, null, OcrConstants.OcrTemp)?.FilePath;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"图片缓存失败：{ex.Message}");
            }
        }

        private void OnScreenshotMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || string.IsNullOrEmpty(_tempImagePath) || !File.Exists(_tempImagePath)) return;

            var data = new DataObject(DataFormats.FileDrop, new[] { _tempImagePath });
            DragDrop.DoDragDrop(ScreenshotImage, data, DragDropEffects.Copy);
        }

        private void DeleteTempImage()
        {
            try
            {
                if (!string.IsNullOrEmpty(_tempImagePath) && File.Exists(_tempImagePath))
                    File.Delete(_tempImagePath);
                _tempImagePath = null;
            }
            catch { }
        }

        private void OnClosed(object? sender, EventArgs e) => DeleteTempImage();
    }
}
