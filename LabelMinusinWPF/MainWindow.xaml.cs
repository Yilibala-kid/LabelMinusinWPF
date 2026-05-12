using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelMinusinWPF.Common;
using LabelMinusinWPF.OCRService;
using LabelMinusinWPF.SelfControls;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Constants = LabelMinusinWPF.Common.Constants;
using ExportMode = LabelMinusinWPF.Common.LabelPlusParser.ExportMode;
using WorkSpace = LabelMinusinWPF.Common.ProjectManager.WorkSpace;

namespace LabelMinusinWPF
{

    /// Interaction logic for MainWindow.xaml

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
            Task.Run(() => ProjectManager.ClearTempFolders(
                OcrConstants.OcrTemp,
                Constants.TempFolders.ScreenShotTemp,
                Constants.TempFolders.ArchiveTemp));
            Closing += MainWindow_Closing;
            OcrPanel.Attach(PicView, this);
            AppSettingsService.InitializeMainWindow(this);
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (DataContext is OneProject viewModel && viewModel.HasUnsavedChanges())
            {
                var result = MessageBox.Show(
                    "当前翻译有未保存的修改，是否保存？",
                    "提示",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    viewModel.SaveCommand.Execute(null);
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                }
            }
        }
        
        #region 底栏图片控制
        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            var window = new SettingsDialog { Owner = this };
            window.ShowDialog();
        }

        private void FitToPage_Click(object sender, RoutedEventArgs e) => PicView.FitToView();
        private void FitWidth_Click(object sender, RoutedEventArgs e) => PicView.FitToWidth();
        private void FitHeight_Click(object sender, RoutedEventArgs e) => PicView.FitToHeight();
        private void ZoomIn_Click(object sender, RoutedEventArgs e) => PicView.ZoomScale *= 1.1;
        private void ZoomOut_Click(object sender, RoutedEventArgs e) => PicView.ZoomScale *= 0.9;
        #endregion



        #region OCR 功能

        private async void SetupOcrEnv_Click(object sender, RoutedEventArgs e)
            => await OcrPanel.InstallEnvironmentAsync();

        private async void AutoOcr_Click(object sender, RoutedEventArgs e)
            => await OcrPanel.RunAutoDotAsync();

        private async void AutoOcr_Batch(object sender, RoutedEventArgs e)
            => await OcrPanel.RunBatchAsync(
                ((MenuItem)sender).Tag as string == "JP" ? OcrEngineKind.Manga : OcrEngineKind.Paddle);

        private void OcrHelp_Click(object sender, RoutedEventArgs e)
            => OcrPanel.ShowHelp();
        #endregion

        #region 拖放文件支持
        private void OnFileDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnFileDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                if (DataContext is OneProject viewModel) viewModel.OpenResourceByPath(files, false);
        }
        #endregion

        #region 菜单栏：帮助
        private void OnOpenWebsite(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: string url })
            {
                try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
                catch (Exception ex) { MessageBox.Show($"无法打开网页: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is OneProject vm) vm.MsgQueue.Enqueue("本程序由No-Hifuu友情赞助");
        }

        private void License_Click(object sender, RoutedEventArgs e)
        {
            string path = System.IO.Path.Combine(AppContext.BaseDirectory, "ThirdPartyNotices.txt");
            if (System.IO.File.Exists(path))
            {
                try { Process.Start(new ProcessStartInfo { FileName = "notepad.exe", Arguments = $"\"{path}\"", UseShellExecute = true }); }
                catch (Exception ex) { MessageBox.Show($"无法打开许可文件: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
            else
            {
                MessageBox.Show("许可文件未找到。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        #endregion

        #region 菜单栏：显示控制
        private void OpenImageReview_Click(object sender, RoutedEventArgs e)
        {
            FullScreenReview.IsOpen = true;
        }
        private void OnLayoutModeClicked(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem clickedItem) return;
            if (clickedItem.Parent is not MenuItem parentMenu) return;

            foreach (var item in parentMenu.Items.OfType<MenuItem>())
                if (item.Tag is DisplayMode) item.IsChecked = false;

            clickedItem.IsChecked = true;

            if (clickedItem.Tag is DisplayMode mode) UpdateLayout(mode);
        }
        private void UpdateLayout(DisplayMode mode)
        {
            // 共同的列宽设置
            LeftColumn.Width = new GridLength(1, GridUnitType.Star);
            RightColumn.Width = new GridLength(1, GridUnitType.Star);
            MiddleColumn.Width = new GridLength(1, GridUnitType.Auto);

            switch (mode)
            {
                case DisplayMode.ImageOnly:
                    RightColumn.Width = new GridLength(0);
                    MiddleColumn.Width = new GridLength(0);
                    break;

                case DisplayMode.ListAndTextBox:
                    LabelEditPanel.IsListVisible = true;
                    LabelEditPanel.IsTextBoxVisible = true;
                    break;

                case DisplayMode.ListOnly:
                    LabelEditPanel.IsListVisible = true;
                    LabelEditPanel.IsTextBoxVisible = false;
                    break;

                case DisplayMode.TextBoxOnly:
                    LabelEditPanel.IsListVisible = false;
                    LabelEditPanel.IsTextBoxVisible = true;
                    break;
            }
        }
        #endregion

        #region 快捷键
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // 如果用户当前正在 TextBox 里输入文字，则不触发快捷键
            if (e.OriginalSource is System.Windows.Controls.TextBox) return;
            if (FullScreenReview.IsOpen) return; // 图校界面打开时禁用快捷键，避免冲突

            if (DataContext is OneProject vm)
            {
                switch (e.Key)
                {
                    // --- 图片切换 (A: 上一张, D: 下一张) ---
                    case Key.A:
                        vm.PreviousImageCommand.Execute(null);
                        e.Handled = true;
                        break;
                    case Key.D:
                        vm.NextImageCommand.Execute(null);
                        e.Handled = true;
                        break;

                    // --- 视图缩放 (R: 适应页面) ---
                    case Key.R:
                        PicView.FitToView();
                        e.Handled = true;
                        break;
                }
            }
        }
        #endregion
    }
}
