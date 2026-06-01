using System;
using System.Windows;
using System.Windows.Input;
using LabelMinusinWPF.Common;

namespace LabelMinusinWPF
{
    public partial class SettingsDialog : Window
    {
        private readonly bool _initialRightClickOpenState;
        private int _lastValidAutoSaveIntervalMinutes;

        public SettingsDialog()
        {
            InitializeComponent();

            _initialRightClickOpenState = RightClickOpenService.IsRegistered();
            LoadSettingsState();
        }

        private void LoadSettingsState()
        {
            _lastValidAutoSaveIntervalMinutes = AppSettingsService.Current.Ui.AutoSaveIntervalMinutes;

            RightClickOpenCheckBox.IsChecked = _initialRightClickOpenState;
            OpenImageReviewOnStartupCheckBox.IsChecked = AppSettingsService.Current.Ui.OpenImageReviewOnStartup;
            AutoLoadLastProjectCheckBox.IsChecked = AppSettingsService.Current.Ui.AutoLoadLastProjectEnabled;
            AutoSaveIntervalTextBox.Text = _lastValidAutoSaveIntervalMinutes.ToString();
            FontRecognitionWebsiteUrlsTextBox.Text = string.Join(
                Environment.NewLine,
                AppSettingsService.Current.Ui.FontRecognitionWebsiteUrls);
        }

        private void AutoSaveIntervalTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;

            ValidateAutoSaveIntervalText();
            e.Handled = true;
        }

        private void AutoSaveIntervalTextBox_LostFocus(object sender, RoutedEventArgs e) =>
            ValidateAutoSaveIntervalText();

        private int ValidateAutoSaveIntervalText()
        {
            if (!int.TryParse(AutoSaveIntervalTextBox.Text, out int minutes))
                minutes = _lastValidAutoSaveIntervalMinutes;

            minutes = AppSettingsService.NormalizeAutoSaveIntervalMinutes(minutes);
            _lastValidAutoSaveIntervalMinutes = minutes;
            AutoSaveIntervalTextBox.Text = minutes.ToString();
            return minutes;
        }

        private bool TryGetFontRecognitionWebsiteUrls(out List<string> urls)
        {
            urls = [];

            foreach (string line in FontRecognitionWebsiteUrlsTextBox.Text.Split(
                ["\r\n", "\n", "\r"],
                StringSplitOptions.None))
            {
                string url = line.Trim();
                if (string.IsNullOrWhiteSpace(url))
                    continue;

                if (!AppSettingsService.IsValidHttpUrl(url))
                {
                    MessageBox.Show(
                        $"外部字体识别网站只支持 http:// 或 https:// 网址：\n{url}",
                        "设置错误",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return false;
                }

                urls.Add(url);
            }

            urls = AppSettingsService.NormalizeFontRecognitionWebsiteUrls(urls);
            FontRecognitionWebsiteUrlsTextBox.Text = string.Join(Environment.NewLine, urls);
            return true;
        }

        private void OpenAutoSaveFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AppSettingsService.OpenAutoSaveFolder();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开自动保存文件夹：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            bool rightClickOpenEnabled = RightClickOpenCheckBox.IsChecked == true;

            try
            {
                if (!TryGetFontRecognitionWebsiteUrls(out var fontRecognitionWebsiteUrls))
                    return;

                ApplyRightClickOpenState(rightClickOpenEnabled);

                AppSettingsService.SaveUiSettings(
                    OpenImageReviewOnStartupCheckBox.IsChecked == true,
                    AutoLoadLastProjectCheckBox.IsChecked == true,
                    ValidateAutoSaveIntervalText(),
                    rightClickOpenEnabled,
                    fontRecognitionWebsiteUrls);

                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存设置失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyRightClickOpenState(bool enabled)
        {
            if (enabled == _initialRightClickOpenState)
                return;

            if (enabled)
                RightClickOpenService.RegisterAll();
            else
                RightClickOpenService.UnregisterAll();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
