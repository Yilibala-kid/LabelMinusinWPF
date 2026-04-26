using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using LabelMinusinWPF.Common;

namespace LabelMinusinWPF
{
    public partial class OcrWindow : Window
    {
        private string? _tempImagePath;

        private WebView2 WebBrowser { get; } = new();

        public OcrWindow(BitmapSource screenshot, string websiteUrl, string websiteName)
        {
            InitializeComponent();
            WebBrowserHost.Children.Add(WebBrowser);

            ScreenshotImage.Source = screenshot;
            SaveImageToOcrTemp(screenshot);

            Title = $"OCR识别 - {websiteName}";
            WebsiteTitle.Text = websiteName;

            InitializeWebView(websiteUrl);
            Closed += OnClosed;
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

        private void SaveImageToOcrTemp(BitmapSource bitmapSource)//这个是为了能够在拖动窗口时把图片文件拖出去用的，所以必须要有个物理文件存在，不能直接拖BitmapSource
        {
            try
            {
                _tempImagePath = ScreenshotHelper.SaveSnip(bitmapSource, null, Constants.TempFolders.OcrTemp)?.FilePath;
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

        private void OnClosed(object? sender, EventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(_tempImagePath) && File.Exists(_tempImagePath))
                    File.Delete(_tempImagePath);
            }
            catch { /* 忽略删除失败 */ }
        }
    }
}
