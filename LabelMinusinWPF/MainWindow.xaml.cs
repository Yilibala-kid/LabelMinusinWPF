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
                Constants.TempFolders.OcrTemp,
                Constants.TempFolders.ScreenShotTemp,
                Constants.TempFolders.ArchiveTemp));
            Closing += MainWindow_Closing;
            RegisterMenu.IsChecked = RightClickOpenService.IsRegistered();
            LabelStylePanel.Instance.LoadSettings();
            InitializeAutoSave();
            PicView.ScreenshotCaptured += OnScreenshotCaptured;
            ScreenshotOcrToggle.Checked += ScreenshotOcrToggle_Checked;
            ScreenshotOcrToggle.Unchecked += ScreenshotOcrToggle_Unchecked;
            Closing += (_, _) =>
            {
                MangaOcrProvider.StopProcess();
                PpOcrV5RapidOcrProvider.DisposeSharedEngine();
            };
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

        #region OCR 功能

        private async void ScreenshotOcrToggle_Checked(object sender, RoutedEventArgs e)
        {
            ScreenshotOcrToggle.IsEnabled = false;

            try
            {
                PpOcrV5RapidOcrProvider.InitSharedEngine();
                (DataContext as OneProject)?.MsgQueue.Enqueue("PaddleOCR 就绪");
            }
            catch (Exception ex)
            {
                (DataContext as OneProject)?.MsgQueue.Enqueue($"PaddleOCR 加载失败: {ex.Message}");
                ScreenshotOcrToggle.IsChecked = false;
                ScreenshotOcrToggle.IsEnabled = true;
                return;
            }

            ScreenshotOcrToggle.IsEnabled = true;

            if (OcrEnvironment.ReadyForProcessStart)
            {
                (DataContext as OneProject)?.MsgQueue.Enqueue("manga-ocr 启动中...");
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await MangaOcrProvider.StartProcessAsync();
                        Application.Current.Dispatcher.Invoke(() =>
                            (DataContext as OneProject)?.MsgQueue.Enqueue("manga-ocr 就绪"));
                    }
                    catch (Exception ex)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                            (DataContext as OneProject)?.MsgQueue.Enqueue(
                                $"manga-ocr 启动失败: {ex.Message}（中英模式仍可用）"));
                    }
                });
            }
            else
            {
                (DataContext as OneProject)?.MsgQueue.Enqueue(
                    "Python 环境未安装，日文截图 OCR 不可用");
            }
        }

        private void ScreenshotOcrToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            MangaOcrProvider.StopProcess();
            PpOcrV5RapidOcrProvider.DisposeSharedEngine();
            (DataContext as OneProject)?.MsgQueue.Enqueue("截图 OCR 已关闭");
        }

        private void Recognize_Click(object sender, RoutedEventArgs e)//字体识别按钮，手动
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

        private async void OnScreenshotCaptured(object? sender, ImageLabelViewer.ScreenshotEventArgs e)
        {
            if (ScreenshotOcrToggle.IsChecked != true) return;
            if (DataContext is not OneProject vm || vm.SelectedImage == null) return;

            bool usePaddle = (OcrEngineSelector.SelectedItem as ComboBoxItem)?.Content?.ToString() != "日";

            try
            {
                string? text;
                if (usePaddle)
                {
                    text = await PpOcrV5RapidOcrProvider.RecognizeScreenshot(e.Bitmap);
                }
                else
                {
                    if (MangaOcrProvider.SharedProcess == null) return;
                    text = await MangaOcrProvider.RecognizeScreenshot(e.Bitmap);
                }

                if (string.IsNullOrWhiteSpace(text)) return;

                var label = new OneLabel(text.Trim(), GroupConstants.InBox,
                    new Point(e.NormalizedRect.Right, e.NormalizedRect.Top));
                vm.SelectedImage.History.Execute(new AddCommand(vm.SelectedImage.Labels, label));
                vm.MsgQueue.Enqueue($"OCR 标签已添加：{text.Trim()[..Math.Min(30, text.Trim().Length)]}");
            }
            catch (Exception ex)
            {
                vm.MsgQueue.Enqueue($"OCR 失败: {ex.Message}");
            }
        }

        private async void SetupOcrEnv_Click(object sender, RoutedEventArgs e)
        {
            if (OcrEnvironment.IsPythonInstalled)
            {
                MessageBox.Show(OcrEnvironment.GetSummary(), "OCR 环境", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                "未找到 Python 环境。是否自动安装到程序目录？\n（将下载嵌入版 Python + torch + manga-ocr，约 1GB）",
                "配置 OCR 环境", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            var vm = DataContext as OneProject;
            var cts = new CancellationTokenSource();

            // 在 MsgQueue 中实时显示安装进度，同时用 MessageDialog 做完成提示
            try
            {
                var progress = new Progress<string>(msg => vm?.MsgQueue.Enqueue(msg));

                await PythonEnvironmentInstaller.InstallAsync(progress, cts.Token);

                Application.Current.Dispatcher.Invoke(() =>
                    MessageBox.Show("Python OCR 环境安装完成！", "OCR 环境",
                        MessageBoxButton.OK, MessageBoxImage.Information));
            }
            catch (OperationCanceledException)
            {
                vm?.MsgQueue.Enqueue("OCR 环境安装已取消");
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
                    MessageBox.Show($"安装失败: {ex.Message}", "OCR 环境",
                        MessageBoxButton.OK, MessageBoxImage.Error));
            }
        }






        private async void AutoOcr_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not OneProject vm) return;

            if (!OcrEnvironment.ReadyForAutoDot)
            {
                vm.MsgQueue.Enqueue("未找到 ONNX 模型，请将模型放入 models/ 目录");
                return;
            }

            var models = OcrPipeline.ScanModels();
            var model = models.FirstOrDefault(m =>
                PpOcrV5RapidOcrProvider.CanHandleEngine(m.Engine));
            if (model == null)
            {
                vm.MsgQueue.Enqueue("未找到 PaddleOCR 模型");
                return;
            }

            var provider = new PpOcrV5RapidOcrProvider();
            var progress = new Progress<string>(msg => vm.MsgQueue.Enqueue(msg));
            try
            {
                var result = await OcrPipeline.RunAsync(vm, model,
                    AutoOcrOptions.JapaneseManga, provider.RecognizeAsync, progress);
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

        private async void AutoOcr_Batch(object sender, RoutedEventArgs e)
        {
            if (DataContext is not OneProject vm) return;
            bool isJP = (sender as MenuItem)?.Tag?.ToString() == "JP";

            // 1) 自动开 Toggle（环境检查由 Checked 事件处理）
            if (ScreenshotOcrToggle.IsChecked != true)
            {
                ScreenshotOcrToggle.IsChecked = true;
                if (isJP)
                {
                    (DataContext as OneProject)?.MsgQueue.Enqueue("manga-ocr 启动中，请稍候...");
                    while (MangaOcrProvider.SharedProcess == null)
                        await Task.Delay(200);
                }
            }
            else if (isJP && MangaOcrProvider.SharedProcess == null)
            {
                (DataContext as OneProject)?.MsgQueue.Enqueue("manga-ocr 启动中，请稍候...");
                while (MangaOcrProvider.SharedProcess == null)
                    await Task.Delay(200);
            }

            // 2) 图片选择
            string desc = isJP ? "日文 manga-ocr" : "中英文 PaddleOCR";
            var dialog = new ImageSelectDialog([.. vm.ImageList], [.. vm.ImageList],
                title: "选择 OCR 图片", description: $"请选择要进行 {desc} 识别的图片：");
            if (dialog.ShowDialog() != true || dialog.SelectedImages.Count == 0) return;

            // 3) 模型扫描
            var model = isJP
                ? OcrPipeline.ScanModels().FirstOrDefault(m =>
                    m.Engine.Equals(MangaOcrProvider.EngineName, StringComparison.OrdinalIgnoreCase))
                : OcrPipeline.ScanModels().FirstOrDefault(m =>
                    PpOcrV5RapidOcrProvider.CanHandleEngine(m.Engine));
            if (model == null)
            {
                vm.MsgQueue.Enqueue(isJP ? "未找到 manga-ocr 模型" : "未找到 PaddleOCR 模型");
                return;
            }

            // 4) 运行
            var options = isJP
                ? AutoOcrOptions.JapaneseManga with { OutputMode = OcrOutputMode.RecognizedText }
                : AutoOcrOptions.ChineseEnglish;
            var progress = new Progress<string>(msg => vm.MsgQueue.Enqueue(msg));
            try
            {
                AutoOcrResult result;
                if (isJP)
                {
                    var provider = new MangaOcrProvider();
                    result = await OcrPipeline.RunAsync(vm, model, options,
                        provider.RecognizeAsync, progress, dialog.SelectedImages);
                }
                else
                {
                    result = await OcrPipeline.RunAsync(vm, model, options,
                        PpOcrV5RapidOcrProvider.RecognizeWithSharedEngine, progress, dialog.SelectedImages);
                }
                vm.MsgQueue.Enqueue(result.Message);
            }
            catch (OperationCanceledException) { vm.MsgQueue.Enqueue("OCR 已取消"); }
            catch (Exception ex) { vm.MsgQueue.Enqueue($"OCR 失败: {ex.Message}"); }
        }

        private void OcrHelp_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                """
                === OCR 功能说明 ===
                【功能概述】
                本程序的 OCR 功能用于自动检测图片中的文字区域
                并创建标签，大幅减少手动打点的工作量。
                ────────────────────────────
                【三种 OCR 操作】
                ① 一键打点（高级 → 一键打点）
                   · 使用 PaddleOCR ONNX 模型（进程内，无需 Python）
                   · 仅检测文字坐标，自动生成编号标签
                   · 适用于所有语言，速度快，2-5 秒/张

                ② 一键识别（高级 → 一键识别）
                   · 使用 manga-ocr（PaddleOCR 检测 + ViT-BERT 识别）
                   · 检测并识别日文文字，自动填入识别结果
                   · 仅支持日文，需配置 Python 环境

                ③ 截图打点
                   · OCR 开关 → 在图片上框选区域进行截图，识别结果自动创建为新标签
                ────────────────────────────
                【环境配置】
                Python 环境：
                  高级 → 配置 OCR 环境 → 自动下载安装
                  安装内容：嵌入版 Python + PyTorch + manga-ocr
                  约需 1GB 磁盘空间，首次安装需约 5 分钟

                OCR 模型文件：
                  模型存放目录：程序目录\models\
                  · models\v5\        — PaddleOCR ONNX 模型（一键打点用）
                  · models\manga-ocr\ — manga-ocr 脚本（一键识别用）
                ────────────────────────────
                【注意事项】
                · "一键识别" 使用日漫预设参数，非日文场景效果可能不佳
                · manga-ocr 进程需手动启停（OCR 切换按钮）
                · 建议 OCR 完成后先检查再保存，可 Ctrl+Z 撤销
                """,
                "OCR 功能说明",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

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
