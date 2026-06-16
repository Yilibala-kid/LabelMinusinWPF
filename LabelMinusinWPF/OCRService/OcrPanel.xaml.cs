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
    private const string OcrModelLoading = "OCR 模型加载中";
    private const string OcrStarted = "OCR 已启动";
    private const string ScreenshotOcrClosed = "截图 OCR 已关闭";
    private const string OcrCanceled = "OCR 已取消";

    private Window? _ownerWindow;
    private ImageLabelViewer? _picView;
    private bool _suppressStopNotification;
    private bool _isCommandRunning;
    private Dictionary<string, string> _activeWebsites = [];

    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    public OcrPanel()
    {
        InitializeComponent();
        RefreshWebsiteSelector();

        ScreenshotOcrToggle.Checked += (_, _) => _ = StartLiveOcrAsync();
        ScreenshotOcrToggle.Unchecked += (_, _) =>
        {
            StopEngines(notify: !_suppressStopNotification);
            _suppressStopNotification = false;
        };
    }

    public void Attach(ImageLabelViewer picView, Window ownerWindow)
    {
        _picView = picView;
        _ownerWindow = ownerWindow;

        DataContext = ownerWindow.DataContext;
        ownerWindow.DataContextChanged += (_, e) => DataContext = e.NewValue;

        picView.ScreenshotCaptured += (_, e) => _ = RecognizeScreenshotAsync(e);
        ownerWindow.Closing += (_, _) => StopEngines();
    }

    public void RefreshWebsiteSelector()
    {
        string? selectedWebsite = WebsiteSelector.SelectedItem as string;
        _activeWebsites = CreateActiveWebsites();
        WebsiteSelector.ItemsSource = _activeWebsites.Keys.ToList();

        if (!string.IsNullOrWhiteSpace(selectedWebsite)
            && _activeWebsites.ContainsKey(selectedWebsite))
        {
            WebsiteSelector.SelectedItem = selectedWebsite;
            return;
        }

        if (WebsiteSelector.Items.Count > 0)
            WebsiteSelector.SelectedIndex = 0;
    }

    public Task DotAsync() => RunWithPickerAsync(
        "选择打点图片",
        "请选择要用 PP-OCRv6 自动打点的图片：",
        OcrOutputMode.PositionOnly,
        OcrEngineKind.Paddle);

    public Task RecognizeAsync(OcrEngineKind kind)
    {
        bool isManga = kind == OcrEngineKind.Manga;
        string engineName = isManga ? "manga-ocr" : "Paddle-ocr";
        return RunWithPickerAsync(
            "选择 OCR 图片",
            $"请选择要使用 {engineName} 识别的图片：",
            OcrOutputMode.RecognizedText,
            kind);
    }

    public Task RecognizeAsync() => RecognizeAsync(SelectedEngine);

    public Task InstallAsync()
        => RunExclusiveAsync(InstallCoreAsync, handleErrors: false);

    public void ShowHelp()
    {
        string path = Path.Combine(AppContext.BaseDirectory, OcrConstants.ModelsSubDir, "ocr_help.txt");
        string text = File.Exists(path) ? File.ReadAllText(path) : "帮助文件未找到";
        MessageBox.Show(text, "OCR 功能说明", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public void OpenWebOcr()
    {
        if (ViewModel == null)
            return;

        var screenshot = ScreenshotHelper.GetClipboard();
        if (screenshot == null)
        {
            Enqueue("请先截图，再打开字体识别网站");
            return;
        }

        string websiteName = WebsiteSelector.SelectedItem as string ?? OcrConstants.DefaultWebsite;
        string websiteUrl = _activeWebsites.TryGetValue(websiteName, out var url)
            ? url
            : OcrConstants.Websites[OcrConstants.DefaultWebsite];

        var window = new OcrWindow(screenshot, websiteUrl, websiteName);
        AttachScreenshotPreviewUpdater(window);
        window.Show();
    }

    private Task RunWithPickerAsync(
        string title,
        string description,
        OcrOutputMode outputMode,
        OcrEngineKind kind)
        => RunExclusiveAsync(async () =>
        {
            var vm = ViewModel;
            if (vm == null)
                return;

            var dialog = new ImageSelectDialog(
                [.. vm.ImageList],
                [.. vm.ImageList],
                title: title,
                description: description);

            if (dialog.ShowDialog() != true || dialog.SelectedImages.Count == 0)
                return;

            await RunRequestAsync(
                vm,
                new AutoOcrRequest(
                    Images: [.. dialog.SelectedImages],
                    OutputMode: outputMode,
                    Engine: kind));
        });

    private async Task InstallCoreAsync()
    {
        if (OcrEnvironment.IsPythonInstalled || OcrEnvironment.HasPaddleOcrModels)
        {
            string message = OcrEnvironment.GetSummary()
                + "\n\n是否删除所有 OCR 环境并重新配置？";
            var deleteResult = MessageBox.Show(
                message,
                "OCR 环境",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (deleteResult == MessageBoxResult.Yes)
                PythonEnvironmentInstaller.Uninstall();
            else
                return;
        }

        var result = MessageBox.Show(
            "未找到完整 OCR 环境。是否自动安装到程序目录？\n（将安装官方 PaddleOCR/PP-OCRv6、嵌入版 Python、torch 和 manga-ocr，约 2GB+）",
            "配置 OCR 环境",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
            return;

        AllocConsole();
        Console.OutputEncoding = Encoding.UTF8;
        var stdout = Console.OpenStandardOutput();
        var writer = new StreamWriter(stdout, Encoding.UTF8) { AutoFlush = true };
        Console.SetOut(writer);
        Console.SetError(writer);

        try
        {
            await PythonEnvironmentInstaller.InstallWithConsoleReporterAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
        }
        finally
        {
            Console.ReadKey(intercept: true);
            FreeConsole();
        }
    }

    private static Dictionary<string, string> CreateActiveWebsites()
    {
        var customUrls = AppSettingsService.Current.Ui.FontRecognitionWebsiteUrls;
        if (customUrls.Count > 0)
            return customUrls.ToDictionary(url => url, url => url, StringComparer.OrdinalIgnoreCase);

        return new Dictionary<string, string>(OcrConstants.Websites);
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

    private async Task StartLiveOcrAsync()
    {
        ScreenshotOcrToggle.IsEnabled = false;
        Enqueue(OcrModelLoading);

        try
        {
            await Task.WhenAll(
                PaddleOcrPythonProvider.EnsureProcessAsync(),
                MangaOcrProvider.EnsureProcessAsync());
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

    private void StopEngines(bool notify = false)
    {
        MangaOcrProvider.StopProcess();
        PaddleOcrPythonProvider.StopProcess();
        if (notify)
            Enqueue(ScreenshotOcrClosed);
    }

    private async Task RecognizeScreenshotAsync(ImageLabelViewer.ScreenshotEventArgs e)
    {
        var vm = ViewModel;
        if (ScreenshotOcrToggle.IsChecked != true || vm?.SelectedImage == null)
            return;

        try
        {
            await RunRequestAsync(
                vm,
                new AutoOcrRequest(
                    Screenshot: e.Bitmap,
                    ScreenshotNormalizedRect: e.NormalizedRect,
                    OutputMode: OcrOutputMode.RecognizedText,
                    Engine: SelectedEngine));
        }
        catch (Exception ex)
        {
            Enqueue(OcrFailed(ex));
        }
    }

    private async Task RunRequestAsync(OneProject vm, AutoOcrRequest request)
    {
        var progress = new Progress<string>(Enqueue);
        var result = await AutoOcrService.RunAsync(vm, request, progress);
        Enqueue(result.Message);
    }

    private async Task RunExclusiveAsync(Func<Task> action, bool handleErrors = true)
    {
        if (!TryBeginCommand())
            return;

        try
        {
            await action();
        }
        catch (OperationCanceledException) when (handleErrors)
        {
            Enqueue(OcrCanceled);
        }
        catch (Exception ex) when (handleErrors)
        {
            Enqueue(OcrFailed(ex));
        }
        finally
        {
            EndCommand();
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
        BatchRecognizeButton.IsEnabled = enabled;
        SetupButton.IsEnabled = enabled;
    }

    private async void AutoDot_Click(object sender, RoutedEventArgs e) => await DotAsync();
    private async void BatchRecognize_Click(object sender, RoutedEventArgs e) => await RecognizeAsync();
    private async void SetupOcrEnv_Click(object sender, RoutedEventArgs e) => await InstallAsync();
    private void OcrHelp_Click(object sender, RoutedEventArgs e) => ShowHelp();
    private void OpenWebOcr_Click(object sender, RoutedEventArgs e) => OpenWebOcr();

    private OcrEngineKind SelectedEngine =>
        (EngineSelector.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "Manga"
            ? OcrEngineKind.Manga
            : OcrEngineKind.Paddle;
}
