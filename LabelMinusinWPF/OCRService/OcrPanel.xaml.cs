using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using LabelMinusinWPF.Common;
using LabelMinusinWPF.SelfControls;

namespace LabelMinusinWPF.OCRService;

public partial class OcrPanel : UserControl
{
    private const string PaddleReady = "PaddleOCR 就绪";
    private const string MangaStarting = "manga-ocr 启动中...";
    private const string MangaReady = "manga-ocr 就绪";
    private const string MangaUnavailable = "Python 环境或 manga-ocr 模型未配置，日文 OCR 不可用";
    private const string ScreenshotOcrClosed = "截图 OCR 已关闭";
    private const string OcrCanceled = "OCR 已取消";
    private readonly DispatcherTimer _closeTimer;
    private RadioButton? _ocrButton;
    private Popup? _popup;
    private Window? _ownerWindow;
    private bool _suppressStopNotification;
    private bool _isCommandRunning;
    private int _mangaStartVersion;

    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    public OcrPanel()
    {
        InitializeComponent();

        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _closeTimer.Tick += (_, _) => CloseIfPointerOutside();

        MouseEnter += (_, _) => _closeTimer.Stop();
        MouseLeave += (_, _) => StartCloseTimer();
        ScreenshotOcrToggle.Checked += (_, _) => _ = StartScreenshotOcrAsync();
        ScreenshotOcrToggle.Unchecked += (_, _) =>
        {
            StopOcrEngines(notify: !_suppressStopNotification);
            _suppressStopNotification = false;
        };
    }

    public void Attach(
        ImageLabelViewer picView,
        RadioButton ocrButton,
        Popup popup,
        Window ownerWindow)
    {
        _ocrButton = ocrButton;
        _popup = popup;
        _ownerWindow = ownerWindow;

        DataContext = ownerWindow.DataContext;
        ownerWindow.DataContextChanged += (_, e) => DataContext = e.NewValue;

        ocrButton.Checked += (_, _) => OpenOnHover();
        ocrButton.MouseEnter += (_, _) => OpenOnHover();
        ocrButton.MouseLeave += (_, _) => StartCloseTimer();
        ocrButton.Unchecked += (_, _) =>
        {
            ClosePanel();
            DisableScreenshotOcr(notify: false);
        };

        picView.ScreenshotCaptured += (_, e) => _ = HandleScreenshotOcrAsync(e);
        ownerWindow.Closing += (_, _) => StopOcrEngines();
    }

    public Task RunAutoDotAsync() => RunOcrWithDialogAsync(
        "选择打点图片", "请选择要进行一键打点的图片：",
        OcrEngineKind.Paddle,
        AutoOcrOptions.Default with { OutputMode = OcrOutputMode.PositionOnly, RightToLeft = true },
        PpOcrV5RapidOcrProvider.Shared);

    public Task RunBatchAsync(OcrEngineKind kind)
    {
        bool isManga = kind == OcrEngineKind.Manga;
        return RunOcrWithDialogAsync(
            "选择 OCR 图片",
            $"请选择要进行{(isManga ? "日文 manga-ocr" : "中英文 PaddleOCR")} 识别的图片：",
            kind,
            AutoOcrOptions.Default with { RightToLeft = isManga },
            isManga ? new MangaOcrProvider() : PpOcrV5RapidOcrProvider.Shared);
    }

    private async Task RunOcrWithDialogAsync(
        string title,
        string description,
        OcrEngineKind kind,
        AutoOcrOptions options,
        IOcrProvider provider)
    {
        if (!TryBeginCommand()) return;
        var vm = ViewModel;
        if (vm == null) { EndCommand(); return; }

        try
        {
            var model = kind == OcrEngineKind.Manga ? MangaModel() : OcrPipeline.FindPaddleModel();
            if (model == null)
            {
                Enqueue(kind == OcrEngineKind.Manga
                    ? "未找到 manga-ocr 模型" : "未找到 PaddleOCR 模型");
                return;
            }

            var dialog = new ImageSelectDialog([.. vm.ImageList], [.. vm.ImageList],
                title: title, description: description);
            if (dialog.ShowDialog() != true || dialog.SelectedImages.Count == 0) return;

            if (!await EnsureReadyAsync(kind, model))
                return;

            var progress = new Progress<string>(Enqueue);
            var result = await OcrPipeline.RunAsync(
                vm, model, options, provider, progress, dialog.SelectedImages);
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

        new OcrWindow(screenshot, websiteUrl, websiteName).Show();
    }

    private OneProject? ViewModel => _ownerWindow?.DataContext as OneProject ?? DataContext as OneProject;

    private void OpenOnHover()
    {
        if (_ocrButton?.IsChecked != true || _ocrButton.IsMouseOver != true || _popup == null)
            return;

        _closeTimer.Stop();
        _popup.IsOpen = true;
    }

    private void StartCloseTimer()
    {
        if (_popup?.IsOpen != true)
            return;

        _closeTimer.Stop();
        _closeTimer.Start();
    }

    private void CloseIfPointerOutside()
    {
        _closeTimer.Stop();

        if (_popup?.IsOpen != true)
            return;

        if (_ocrButton?.IsMouseOver == true
            || IsMouseOver
            || WebsiteSelector.IsDropDownOpen
            || EngineSelector.IsDropDownOpen)
            return;

        ClosePanel();
    }

    private void ClosePanel()
    {
        _closeTimer.Stop();
        if (_popup != null)
            _popup.IsOpen = false;
    }

    private async Task StartScreenshotOcrAsync()
    {
        ScreenshotOcrToggle.IsEnabled = false;

        if (!TryStartPaddle(notifySuccess: false))
        {
            _suppressStopNotification = true;
            ScreenshotOcrToggle.IsChecked = false;
            ScreenshotOcrToggle.IsEnabled = true;
            return;
        }

        ScreenshotOcrToggle.IsEnabled = true;
        _ = StartMangaInBackgroundAsync();
    }

    private async Task StartMangaInBackgroundAsync()
    {
        int startVersion = ++_mangaStartVersion;

        bool ok;
        try
        {
            ok = await EnsureMangaReadyCore(notify: true);
        }
        catch (Exception ex)
        {
            ok = false;
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (ScreenshotOcrToggle.IsChecked == true && startVersion == _mangaStartVersion)
                    Enqueue(PaddleReady + $"（日文启动失败: {ex.Message}）");
            });
            return;
        }

        bool stillNeeded = false;
        Application.Current.Dispatcher.Invoke(() =>
            stillNeeded = ScreenshotOcrToggle.IsChecked == true
                && startVersion == _mangaStartVersion);

        if (!stillNeeded)
        {
            MangaOcrProvider.StopProcess();
            return;
        }

        if (ok)
            Enqueue("OCR 已开启");
        else
            Enqueue(PaddleReady + "（日文不可用）");
    }

    private void StopOcrEngines(bool notify = false)
    {
        _mangaStartVersion++;
        MangaOcrProvider.StopProcess();
        PpOcrV5RapidOcrProvider.DisposeSharedEngine();
        if (notify) Enqueue(ScreenshotOcrClosed);
    }

    private void DisableScreenshotOcr(bool notify)
    {
        if (ScreenshotOcrToggle.IsChecked == true)
        {
            _suppressStopNotification = !notify;
            ScreenshotOcrToggle.IsChecked = false;
            return;
        }

        StopOcrEngines(notify);
    }

    private bool TryStartPaddle(bool notifySuccess = true, OcrModelInfo? model = null)
    {
        try
        {
            PpOcrV5RapidOcrProvider.InitSharedEngine(model);
            if (notifySuccess) Enqueue(PaddleReady);
            return true;
        }
        catch (Exception ex)
        {
            Enqueue(PaddleLoadFailed(ex));
            return false;
        }
    }

    private async Task<bool> EnsureReadyAsync(OcrEngineKind kind, OcrModelInfo? model = null)
    {
        if (kind == OcrEngineKind.Paddle)
            return TryStartPaddle(notifySuccess: false, model);

        try
        {
            bool ok = await EnsureMangaReadyCore(notify: true);
            if (ok)
                Enqueue(MangaReady);
            else
                Enqueue(MangaUnavailable);
            return ok;
        }
        catch (Exception ex)
        {
            Enqueue(MangaStartFailed(ex));
            return false;
        }
    }

    private async Task<bool> EnsureMangaReadyCore(bool notify)
    {
        if (MangaOcrProvider.SharedProcess is { HasExited: false })
            return true;
        if (MangaOcrProvider.SharedProcess != null)
            MangaOcrProvider.StopProcess();

        if (!OcrEnvironment.ReadyForProcessStart)
            return false;

        if (notify) Enqueue(MangaStarting);
        await MangaOcrProvider.StartProcessAsync();
        return true;
    }

    private async Task HandleScreenshotOcrAsync(ImageLabelViewer.ScreenshotEventArgs e)
    {
        var vm = ViewModel;
        if (ScreenshotOcrToggle.IsChecked != true || vm?.SelectedImage == null)
            return;

        try
        {
            if (IsMangaSelected && MangaOcrProvider.SharedProcess == null)
                return;

            string? text = IsMangaSelected
                ? await MangaOcrProvider.RecognizeScreenshot(e.Bitmap)
                : await PpOcrV5RapidOcrProvider.RecognizeScreenshot(e.Bitmap);
            if (string.IsNullOrWhiteSpace(text)) return;

            var label = new OneLabel(text.Trim(), GroupConstants.InBox,
                new Point(e.NormalizedRect.Right, e.NormalizedRect.Top));
            vm.SelectedImage.History.Execute(new AddCommand(vm.SelectedImage.Labels, label));
            Enqueue(LabelAdded(text));
        }
        catch (Exception ex)
        {
            Enqueue(OcrFailed(ex));
        }
    }

    private void Enqueue(string message) => ViewModel?.MsgQueue.Enqueue(message);

    private static string PaddleLoadFailed(Exception ex) => $"PaddleOCR 加载失败: {ex.Message}";
    private static string MangaStartFailed(Exception ex) => $"manga-ocr 启动失败: {ex.Message}（中英模式仍可用）";
    private static string OcrFailed(Exception ex) => $"OCR 失败: {ex.Message}";

    private static string LabelAdded(string text)
    {
        text = text.Trim();
        return $"OCR 标签已添加：{text[..Math.Min(30, text.Length)]}";
    }

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

    public enum OcrEngineKind
    {
        Paddle,
        Manga
    }

    private bool IsMangaSelected =>
        (EngineSelector.SelectedItem as ComboBoxItem)?.Content?.ToString() == "日文";

    private static OcrModelInfo? MangaModel() =>
        OcrPipeline.ScanModels().FirstOrDefault(m =>
            m.Engine.Equals(MangaOcrProvider.EngineName, StringComparison.OrdinalIgnoreCase));
}
