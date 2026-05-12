using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using LabelMinusinWPF.Common;
using LabelMinusinWPF.SelfControls;

namespace LabelMinusinWPF.OCRService;

public partial class OcrPanel : UserControl
{
    private const string OcrModelLoading = "OCR模型加载中";
    private const string OcrStarted = "OCR已启动";
    private const string ScreenshotOcrClosed = "截图 OCR 已关闭";
    private const string OcrCanceled = "OCR 已取消";
    private Window? _ownerWindow;
    private ImageLabelViewer? _picView;
    private bool _suppressStopNotification;
    private bool _isCommandRunning;

    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    public OcrPanel()
    {
        InitializeComponent();

        ScreenshotOcrToggle.Checked += (_, _) => _ = StartScreenshotOcrAsync();
        ScreenshotOcrToggle.Unchecked += (_, _) =>
        {
            StopOcrEngines(notify: !_suppressStopNotification);
            _suppressStopNotification = false;
        };
    }

    public void Attach(
        ImageLabelViewer picView,
        Window ownerWindow)
    {
        _picView = picView;
        _ownerWindow = ownerWindow;

        DataContext = ownerWindow.DataContext;
        ownerWindow.DataContextChanged += (_, e) => DataContext = e.NewValue;

        picView.ScreenshotCaptured += (_, e) => _ = HandleScreenshotOcrAsync(e);
        ownerWindow.Closing += (_, _) => StopOcrEngines();
    }

    public Task RunAutoDotAsync() => RunOcrWithDialogAsync(
        "选择打点图片", "请选择要进行一键打点的图片：",
        OcrOutputMode.PositionOnly,
        OcrEngineKind.Paddle);

    public Task RunBatchAsync(OcrEngineKind kind)
    {
        bool isManga = kind == OcrEngineKind.Manga;
        return RunOcrWithDialogAsync(
            "选择 OCR 图片",
            $"请选择要进行{(isManga ? "日文 manga-ocr" : "中英文 PaddleOCR")} 识别的图片：",
            OcrOutputMode.RecognizedText,
            kind);
    }

    private async Task RunOcrWithDialogAsync(
        string title,
        string description,
        OcrOutputMode outputMode,
        OcrEngineKind kind)
    {
        if (!TryBeginCommand()) return;
        var vm = ViewModel;
        if (vm == null) { EndCommand(); return; }

        try
        {
            var dialog = new ImageSelectDialog([.. vm.ImageList], [.. vm.ImageList],
                title: title, description: description);
            if (dialog.ShowDialog() != true || dialog.SelectedImages.Count == 0) return;

            var progress = new Progress<string>(Enqueue);
            var result = await AutoOcrService.RunAsync(
                vm,
                new AutoOcrRequest(
                    Images: [.. dialog.SelectedImages],
                    OutputMode: outputMode,
                    Engine: kind),
                progress);
            Enqueue(result.Message);
        }
        catch (OperationCanceledException) { Enqueue(OcrCanceled); }
        catch (Exception ex) { Enqueue(OcrFailed(ex)); }
        finally { EndCommand(); }
    }

    public async Task InstallEnvironmentAsync()
    {
        if (!TryBeginCommand()) return;

        try
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

                await PythonEnvironmentInstaller.InstallAsync(progress);

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
        finally
        {
            EndCommand();
        }
    }

    public void ShowHelp()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "models", "ocr_help.txt");
        string text = File.Exists(path) ? File.ReadAllText(path) : "帮助文件未找到";
        MessageBox.Show(text, "OCR 功能说明", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public void OpenWebOcr()
    {
        var vm = ViewModel;
        if (vm == null) return;

        var screenshot = ScreenshotHelper.GetClipboard();
        if (screenshot == null)
        {
            Enqueue("请先截图，再点击识别");
            return;
        }

        string websiteName = WebsiteSelector.SelectedItem as string ?? OcrConstants.DefaultWebsite;
        string websiteUrl = OcrConstants.Websites.TryGetValue(websiteName, out var url)
            ? url
            : OcrConstants.Websites[OcrConstants.DefaultWebsite];

        var window = new OcrWindow(screenshot, websiteUrl, websiteName);
        AttachScreenshotPreviewUpdater(window);
        window.Show();
    }

    private OneProject? ViewModel => _ownerWindow?.DataContext as OneProject ?? DataContext as OneProject;

    private void AttachScreenshotPreviewUpdater(OcrWindow window)
    {
        var picView = _picView;
        if (picView == null)
            return;

        EventHandler<ImageLabelViewer.ScreenshotEventArgs>? handler = null;
        handler = (_, e) => window.UpdateScreenshot(e.Bitmap);
        picView.ScreenshotCaptured += handler;
        window.Closed += (_, _) => picView.ScreenshotCaptured -= handler;
    }

    private async Task StartScreenshotOcrAsync()
    {
        ScreenshotOcrToggle.IsEnabled = false;
        Enqueue(OcrModelLoading);

        try
        {
            PpOcrV5RapidOcrProvider.InitSharedEngine();
            await EnsureMangaReadyAsync();
            Enqueue(OcrStarted);
        }
        catch (Exception ex)
        {
            Enqueue(OcrFailed(ex));
            _suppressStopNotification = true;
            ScreenshotOcrToggle.IsChecked = false;
        }
        finally
        {
            ScreenshotOcrToggle.IsEnabled = true;
        }
    }

    private void StopOcrEngines(bool notify = false)
    {
        MangaOcrProvider.StopProcess();
        PpOcrV5RapidOcrProvider.DisposeSharedEngine();
        if (notify) Enqueue(ScreenshotOcrClosed);
    }

    private static async Task EnsureMangaReadyAsync()
    {
        if (MangaOcrProvider.SharedProcess is { HasExited: false })
            return;

        if (MangaOcrProvider.SharedProcess != null)
            MangaOcrProvider.StopProcess();

        if (!OcrEnvironment.ReadyForProcessStart)
            throw new InvalidOperationException("Python 环境或 manga-ocr 模型未配置，日文 OCR 不可用");

        await MangaOcrProvider.StartProcessAsync();
    }

    private async Task HandleScreenshotOcrAsync(ImageLabelViewer.ScreenshotEventArgs e)
    {
        var vm = ViewModel;
        if (ScreenshotOcrToggle.IsChecked != true || vm?.SelectedImage == null)
            return;

        try
        {
            var progress = new Progress<string>(Enqueue);
            var result = await AutoOcrService.RunAsync(
                vm,
                new AutoOcrRequest(
                    Screenshot: e.Bitmap,
                    ScreenshotNormalizedRect: e.NormalizedRect,
                    OutputMode: OcrOutputMode.RecognizedText,
                    Engine: IsMangaSelected ? OcrEngineKind.Manga : OcrEngineKind.Paddle),
                progress);

            Enqueue(result.Message);
        }
        catch (Exception ex)
        {
            Enqueue(OcrFailed(ex));
        }
    }

    private void Enqueue(string message) => ViewModel?.MsgQueue.Enqueue(message);

    private static string OcrFailed(Exception ex) => $"OCR 失败: {ex.Message}";

    private bool TryBeginCommand()
    {
        if (_isCommandRunning)
        {
            Enqueue("OCR 操作正在进行，请稍候");
            return false;
        }

        _isCommandRunning = true;
        SetCommandButtonsEnabled(false);
        return true;
    }

    private void EndCommand()
    {
        _isCommandRunning = false;
        SetCommandButtonsEnabled(true);
    }

    private void SetCommandButtonsEnabled(bool enabled)
    {
        AutoDotButton.IsEnabled = enabled;
        BatchCnEnButton.IsEnabled = enabled;
        BatchJpButton.IsEnabled = enabled;
        SetupButton.IsEnabled = enabled;
    }

    private async void AutoDot_Click(object sender, RoutedEventArgs e) => await RunAutoDotAsync();
    private async void BatchCnEn_Click(object sender, RoutedEventArgs e) => await RunBatchAsync(OcrEngineKind.Paddle);
    private async void BatchJp_Click(object sender, RoutedEventArgs e) => await RunBatchAsync(OcrEngineKind.Manga);
    private async void SetupOcrEnv_Click(object sender, RoutedEventArgs e) => await InstallEnvironmentAsync();
    private void OcrHelp_Click(object sender, RoutedEventArgs e) => ShowHelp();
    private void OpenWebOcr_Click(object sender, RoutedEventArgs e) => OpenWebOcr();

    private bool IsMangaSelected =>
        (EngineSelector.SelectedItem as ComboBoxItem)?.Content?.ToString() == "日文";

}
