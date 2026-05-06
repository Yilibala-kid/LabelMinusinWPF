using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using LabelMinusinWPF.Common;
using LabelMinusinWPF.SelfControls;

namespace LabelMinusinWPF.OCRService;

public static class OcrUIActions
{
    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    // ================================================================
    // 构造函数初始化
    // ================================================================

    public static void WireUp(MainWindow window)
    {
        window.ScreenshotOcrToggle.Checked += (_, _) =>
            _ = StartOcrEnginesAsync(window.ScreenshotOcrToggle, window.DataContext as OneProject);
        window.ScreenshotOcrToggle.Unchecked += (_, _) =>
            StopOcrEngines(window.DataContext as OneProject);
        window.PicView.ScreenshotCaptured += (_, e) =>
            _ = HandleScreenshotOcrAsync(window.ScreenshotOcrToggle, window.OcrEngineSelector,
                window.DataContext as OneProject, e);
        window.Closing += (_, _) =>
        {
            MangaOcrProvider.StopProcess();
            PpOcrV5RapidOcrProvider.DisposeSharedEngine();
        };
    }

    // ================================================================
    // 截图 OCR 开关
    // ================================================================

    public static async Task StartOcrEnginesAsync(ToggleButton toggle, OneProject? vm)
    {
        toggle.IsEnabled = false;

        try
        {
            PpOcrV5RapidOcrProvider.InitSharedEngine();
            vm?.MsgQueue.Enqueue("PaddleOCR 就绪");
        }
        catch (Exception ex)
        {
            vm?.MsgQueue.Enqueue($"PaddleOCR 加载失败: {ex.Message}");
            toggle.IsChecked = false;
            toggle.IsEnabled = true;
            return;
        }

        toggle.IsEnabled = true;

        if (OcrEnvironment.ReadyForProcessStart)
        {
            vm?.MsgQueue.Enqueue("manga-ocr 启动中...");
            _ = Task.Run(async () =>
            {
                try
                {
                    await MangaOcrProvider.StartProcessAsync();
                    Application.Current.Dispatcher.Invoke(() =>
                        vm?.MsgQueue.Enqueue("manga-ocr 就绪"));
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                        vm?.MsgQueue.Enqueue(
                            $"manga-ocr 启动失败: {ex.Message}（中英模式仍可用）"));
                }
            });
        }
        else
        {
            vm?.MsgQueue.Enqueue("Python 环境未安装，日文截图 OCR 不可用");
        }
    }

    public static void StopOcrEngines(OneProject? vm)
    {
        MangaOcrProvider.StopProcess();
        PpOcrV5RapidOcrProvider.DisposeSharedEngine();
        vm?.MsgQueue.Enqueue("截图 OCR 已关闭");
    }

    // ================================================================
    // 截图保存（供两个引擎共享）
    // ================================================================

    public static string SaveBitmapToTempPng(BitmapSource bitmap)
    {
        string tmpDir = Path.Combine(AppContext.BaseDirectory, OcrConstants.OcrTemp);
        Directory.CreateDirectory(tmpDir);
        string tmpPath = Path.Combine(tmpDir, $"ocr_{Guid.NewGuid():N}.png");

        Application.Current.Dispatcher.Invoke(() =>
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var fs = new FileStream(tmpPath, FileMode.Create);
            encoder.Save(fs);
        });
        return tmpPath;
    }

    // ================================================================
    // 手动字体识别（打开浏览器 OCR 窗口）
    // ================================================================

    public static void OpenWebOcr(OneProject? vm, ComboBox websiteSelector)
    {
        if (vm == null) return;
        var screenshot = ScreenshotHelper.GetClipboard();
        if (screenshot == null) { vm.MsgQueue.Enqueue("请先截图，再点击识别"); return; }

        string websiteName = websiteSelector.SelectedItem as string ?? OcrConstants.DefaultWebsite;
        string websiteUrl = OcrConstants.Websites.TryGetValue(websiteName, out var url)
                            ? url
                            : OcrConstants.Websites[OcrConstants.DefaultWebsite];

        new OcrWindow(screenshot, websiteUrl, websiteName).Show();
    }

    // ================================================================
    // 截图 OCR 识别（PaddleOCR 或 manga-ocr）
    // ================================================================

    public static async Task HandleScreenshotOcrAsync(
        ToggleButton ocrToggle, ComboBox engineSelector, OneProject? vm,
        ImageLabelViewer.ScreenshotEventArgs e)
    {
        if (ocrToggle.IsChecked != true) return;
        if (vm?.SelectedImage == null) return;

        bool usePaddle = (engineSelector.SelectedItem as ComboBoxItem)?.Content?.ToString() != "日";

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

    // ================================================================
    // 配置 OCR 环境
    // ================================================================

    public static async Task InstallOcrEnvAsync(OneProject? vm)
    {
        if (OcrEnvironment.IsPythonInstalled || OcrEnvironment.HasOnnxModels)
        {
            string msg = OcrEnvironment.GetSummary() + "\n\n是否删除所有 OCR 环境并重新配置？";
            var deleteResult = MessageBox.Show(msg, "OCR 环境",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (deleteResult == MessageBoxResult.Yes)
                PythonEnvironmentInstaller.Uninstall();
            else
                return;
        }

        var result = MessageBox.Show(
            "未找到 Python 环境。是否自动安装到程序目录？\n（将下载嵌入版 Python + torch + manga-ocr，约 2GB）",
            "配置 OCR 环境", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
            return;

        var cts = new CancellationTokenSource();

        AllocConsole();
        Console.OutputEncoding = Encoding.UTF8;
        var stdout = Console.OpenStandardOutput();
        var writer = new StreamWriter(stdout, Encoding.UTF8) { AutoFlush = true };
        Console.SetOut(writer);
        Console.SetError(writer);

        Console.WriteLine("=== LabelMinus OCR 环境安装 ===");
        Console.WriteLine();

        try
        {
            var progress = new Progress<string>(msg =>
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}"));

            await PythonEnvironmentInstaller.InstallAsync(progress, cts.Token);

            Console.WriteLine();
            Console.WriteLine("安装完成！按任意键关闭此窗口...");
            Console.ReadKey(intercept: true);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("安装已取消，按任意键关闭...");
            Console.ReadKey(intercept: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"安装失败: {ex.Message}");
            Console.WriteLine("按任意键关闭...");
            Console.ReadKey(intercept: true);
        }
        finally
        {
            FreeConsole();
        }
    }

    // ================================================================
    // 一键打点（Position Only）
    // ================================================================

    public static async Task AutoDotAsync(OneProject? vm)
    {
        if (vm == null) return;

        if (!OcrEnvironment.HasOnnxModels)
        {
            vm.MsgQueue.Enqueue("未找到 ONNX 模型，请将模型放入 models/ 目录");
            return;
        }

        var model = OcrPipeline.FindPaddleModel();
        if (model == null)
        {
            vm.MsgQueue.Enqueue("未找到 PaddleOCR 模型");
            return;
        }

        PpOcrV5RapidOcrProvider.InitSharedEngine();
        var progress = new Progress<string>(msg => vm.MsgQueue.Enqueue(msg));
        try
        {
            var result = await OcrPipeline.RunAsync(vm, model,
                AutoOcrOptions.JapaneseManga, PpOcrV5RapidOcrProvider.Shared, progress);
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

    // ================================================================
    // 一键识别（批量，中英/日文）
    // ================================================================

    private static async Task<bool> WaitForMangaOcrProcess(OneProject? vm, int timeoutMs = 30000)
    {
        vm?.MsgQueue.Enqueue("manga-ocr 启动中，请稍候...");
        var sw = Stopwatch.StartNew();
        while (MangaOcrProvider.SharedProcess == null)
        {
            if (sw.ElapsedMilliseconds > timeoutMs)
            {
                vm?.MsgQueue.Enqueue("manga-ocr 启动超时，请确认环境已配置");
                return false;
            }
            await Task.Delay(200);
        }
        return true;
    }

    public static async Task AutoOcrBatchAsync(ToggleButton ocrToggle, OneProject? vm, string tag)
    {
        if (vm == null) return;
        bool isJP = tag == "JP";

        if (ocrToggle.IsChecked != true)
        {
            ocrToggle.IsChecked = true;
            if (isJP && !await WaitForMangaOcrProcess(vm))
                return;
        }
        else if (isJP && MangaOcrProvider.SharedProcess == null)
        {
            if (!await WaitForMangaOcrProcess(vm))
                return;
        }

        string desc = isJP ? "日文 manga-ocr" : "中英文 PaddleOCR";
        var dialog = new ImageSelectDialog([.. vm.ImageList], [.. vm.ImageList],
            title: "选择 OCR 图片", description: $"请选择要进行 {desc} 识别的图片：");
        if (dialog.ShowDialog() != true || dialog.SelectedImages.Count == 0) return;

        var model = isJP
            ? OcrPipeline.ScanModels().FirstOrDefault(m =>
                m.Engine.Equals(MangaOcrProvider.EngineName, StringComparison.OrdinalIgnoreCase))
            : OcrPipeline.FindPaddleModel();
        if (model == null)
        {
            vm.MsgQueue.Enqueue(isJP ? "未找到 manga-ocr 模型" : "未找到 PaddleOCR 模型");
            return;
        }

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
                    provider, progress, dialog.SelectedImages);
            }
            else
            {
                result = await OcrPipeline.RunAsync(vm, model, options,
                    PpOcrV5RapidOcrProvider.Shared, progress, dialog.SelectedImages);
            }
            vm.MsgQueue.Enqueue(result.Message);
        }
        catch (OperationCanceledException) { vm.MsgQueue.Enqueue("OCR 已取消"); }
        catch (Exception ex) { vm.MsgQueue.Enqueue($"OCR 失败: {ex.Message}"); }
    }

    // ================================================================
    // OCR 帮助
    // ================================================================

    public static void ShowOcrHelp()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "models", "ocr_help.txt");
        string text = File.Exists(path) ? File.ReadAllText(path) : "帮助文件未找到";
        MessageBox.Show(text, "OCR 功能说明", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
