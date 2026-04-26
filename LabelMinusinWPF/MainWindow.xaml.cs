using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelMinusinWPF.Common;
using LabelMinusinWPF.SelfControls;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
                Constants.TempFolders.OcrTemp,
                Constants.TempFolders.ScreenShotTemp,
                Constants.TempFolders.ArchiveTemp));
            Closing += MainWindow_Closing;
            RegisterMenu.IsChecked = RightClickOpenService.IsRegistered();
            LabelStylePanel.Instance.LoadSettings();
            InitializeAutoSave();
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            // 获取 ViewModel
            if (DataContext is OneProject viewModel && viewModel.HasUnsavedChanges())
            {
                var result = MessageBox.Show(
                    "当前翻译有未保存的修改，是否保存？",
                    "提示",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question
                );

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
        #region 菜单栏：显示控制
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

        #region 底栏图片控制
        private void FitToPage_Click(object sender, RoutedEventArgs e) => PicView.FitToView();
        private void FitWidth_Click(object sender, RoutedEventArgs e) => PicView.FitToWidth();
        private void FitHeight_Click(object sender, RoutedEventArgs e) => PicView.FitToHeight();
        private void ZoomIn_Click(object sender, RoutedEventArgs e) => PicView.ZoomScale *= 1.1;
        private void ZoomOut_Click(object sender, RoutedEventArgs e) => PicView.ZoomScale *= 0.9;
        #endregion


        #region 右键菜单：注册/注销
        private void OnToggleContextMenu(object sender, RoutedEventArgs e)
        {
            var menuItem = (MenuItem)sender;
            bool shouldRegister = menuItem.IsChecked;

            try
            {
                if (shouldRegister)
                {
                    RightClickOpenService.RegisterAll();
                    MessageBox.Show("右键菜单注册成功！", "提示",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    RightClickOpenService.UnregisterAll();
                    MessageBox.Show("右键菜单已取消注册", "提示",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                menuItem.IsChecked = !shouldRegister;
                MessageBox.Show($"操作失败：{ex.Message}",
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        #endregion
        private void OpenImageReview_Click(object sender, RoutedEventArgs e)
        {
            FullScreenReview.IsOpen = true;
        }

        private void Recognize_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not OneProject vm) return;
            var screenshot = ScreenshotHelper.GetClipboard();
            if (screenshot == null) { vm.MsgQueue.Enqueue("请先截图，再点击识别"); return; }

            string websiteName = OcrWebsiteSelector.SelectedItem as string ?? Constants.OcrWebsites.DefaultWebsite;
            string websiteUrl = Constants.OcrWebsites.Websites.TryGetValue(websiteName, out var url)
                                ? url
                                : Constants.OcrWebsites.Websites[Constants.OcrWebsites.DefaultWebsite];

            new OcrWindow(screenshot, websiteUrl, websiteName).Show();
        }


        private async void AutoOcr_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not OneProject vm) return;
            if (vm.ImageList.Count == 0)
            {
                vm.MsgQueue.Enqueue("当前项目没有可 OCR 的图片");
                return;
            }

            var service = new AutoOcrService();
            var models = service.ScanModels();
            if (models.Count == 0)
            {
                vm.MsgQueue.Enqueue($"未找到可用 OCR 模型，请将模型放入程序目录下的 Model 文件夹：{service.ModelRoot}");
                return;
            }

            try
            {
                var options = AutoOcrOptions.JapaneseManga;
                var model = service.SelectPreferredModel(models, options) ?? models[0];
                vm.MsgQueue.Enqueue($"OCR 模型：{model.Name}（日漫预设）");
                var result = await service.RunAsync(vm, model, options);
                vm.MsgQueue.Enqueue(result.Message);
            }
            catch (OperationCanceledException)
            {
                vm.MsgQueue.Enqueue("OCR 已取消");
            }
            catch (Exception ex)
            {
                vm.MsgQueue.Enqueue($"OCR 失败: {ex.Message}");
            }
        }

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

        #region 快捷键
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // 【重要防护】如果用户当前正在 TextBox 里输入文字，则不触发快捷键
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

                    // --- 标签切换 (W: 上一个, S: 下一个) ---
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
