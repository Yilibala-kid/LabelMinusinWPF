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
                ApplyRightClickOpenState(rightClickOpenEnabled);

                AppSettingsService.SaveUiSettings(
                    OpenImageReviewOnStartupCheckBox.IsChecked == true,
                    AutoLoadLastProjectCheckBox.IsChecked == true,
                    ValidateAutoSaveIntervalText(),
                    rightClickOpenEnabled);

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
