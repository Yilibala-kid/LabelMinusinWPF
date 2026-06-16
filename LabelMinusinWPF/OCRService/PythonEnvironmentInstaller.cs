using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;

namespace LabelMinusinWPF.OCRService;

public static class PythonEnvironmentInstaller
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };

    static PythonEnvironmentInstaller()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
    }

    private const string PythonVersion = "3.10.11";
    private const string PythonDownloadUrl =
        $"https://www.python.org/ftp/python/{PythonVersion}/python-{PythonVersion}-embed-amd64.zip";
    private const string GetPipUrl = "https://bootstrap.pypa.io/get-pip.py";

    private const string TorchIndexUrl = "https://download.pytorch.org/whl/cpu";
    private const string TorchPackage = "torch==2.12.0+cpu";

    private static readonly string[] PaddlePackages = ["paddlepaddle", "paddleocr"];
    private static readonly string[] PipPackages = ["manga-ocr", "huggingface_hub"];

    public static string PythonDir => Path.Combine(AppContext.BaseDirectory, "python");
    public static string PythonExe => Path.Combine(PythonDir, "python.exe");
    public static string PaddleOcrScript =>
        Path.Combine(AppContext.BaseDirectory, OcrConstants.ModelsSubDir, "paddle-ocr", "paddle_ocr_infer.py");

    public static void Uninstall()
    {
        if (Directory.Exists(PythonDir))
            Directory.Delete(PythonDir, recursive: true);

        string v6Dir = OcrConstants.PaddleOcrV6ModelRoot;
        if (Directory.Exists(v6Dir))
        {
            foreach (var directory in Directory.EnumerateDirectories(v6Dir))
                Directory.Delete(directory, recursive: true);
            foreach (var f in Directory.EnumerateFiles(v6Dir, "*.tmp"))
                File.Delete(f);
        }

        string mangaModelDir = Path.Combine(AppContext.BaseDirectory, OcrConstants.ModelsSubDir, "manga-ocr", "model");
        if (Directory.Exists(mangaModelDir))
            Directory.Delete(mangaModelDir, recursive: true);
    }

    public static async Task InstallWithConsoleReporterAsync(CancellationToken ct = default)
    {
        var reporter = new CleanConsoleInstallReporter();
        reporter.Start();

        try
        {
            await InstallAsync(reporter.Progress, ct);
            reporter.Succeed();
        }
        catch (OperationCanceledException)
        {
            reporter.Cancel();
            throw;
        }
        catch (Exception ex)
        {
            reporter.Fail(ex);
            throw;
        }
    }

    public static async Task InstallAsync(IProgress<string> progress, CancellationToken ct = default)
    {
        progress.Report("[stage:1/6] 准备 Python 嵌入环境");
        progress.Report("正在下载 Python 嵌入版...");
        string zipPath = Path.Combine(Path.GetTempPath(), $"python-{PythonVersion}-embed-amd64.zip");
        await DownloadWithProgressAsync(PythonDownloadUrl, zipPath,
            pct => progress.Report($"下载 Python: {pct}%"), ct);

        progress.Report("正在解压 Python...");
        if (Directory.Exists(PythonDir)) Directory.Delete(PythonDir, recursive: true);
        ZipFile.ExtractToDirectory(zipPath, PythonDir);
        File.Delete(zipPath);

        progress.Report("正在配置 Python 环境...");
        File.WriteAllText(Path.Combine(PythonDir, "python310._pth"),
            "python310.zip\r\n.\r\n\r\n# Uncomment to run site.main() automatically\r\nimport site\r\nLib\\site-packages\r\n");

        progress.Report("[stage:2/6] 安装 pip");
        progress.Report("正在安装 pip...");
        string getPipPath = Path.Combine(Path.GetTempPath(), "get-pip.py");
        await File.WriteAllBytesAsync(getPipPath, await Http.GetByteArrayAsync(GetPipUrl, ct), ct);
        await RunPythonAsync($"\"{getPipPath}\" --no-python-version-warning", ct);
        File.Delete(getPipPath);

        progress.Report("[stage:3/6] 安装 PaddleOCR 并下载 PP-OCRv6");
        var paddlePkgs = string.Join(" ", PaddlePackages);
        progress.Report($"安装 PaddleOCR 官方 pipeline: {paddlePkgs}");
        await RunPipWithProgressAsync(
            $"install --no-warn-script-location {paddlePkgs}",
            progress,
            ct);

        progress.Report("预热 PP-OCRv6 medium 模型（首次会下载官方模型）...");
        await WarmupPaddleOcrV6Async(progress, ct);

        progress.Report("[stage:4/6] 安装 torch CPU 运行库");
        progress.Report("安装 torch CPU 版（约 800MB，请耐心等待）...");
        await RunPipWithProgressAsync(
            $"install --no-warn-script-location {TorchPackage} --index-url {TorchIndexUrl}",
            progress,
            ct);

        progress.Report("[stage:5/6] 安装 manga-ocr 依赖");
        var allPkgs = string.Join(" ", PipPackages);
        progress.Report($"安装 Python 包: {allPkgs}");
        await RunPipWithProgressAsync(
            $"install --no-warn-script-location {allPkgs}",
            progress,
            ct);

        progress.Report("[stage:6/6] 下载 manga-ocr 模型");
        progress.Report("下载 manga-ocr 模型（约 400MB）...");
        string modelDir = Path.Combine(AppContext.BaseDirectory, OcrConstants.ModelsSubDir, "manga-ocr", "model");
        string scriptPath = Path.Combine(Path.GetTempPath(), "download_model.py");
        await File.WriteAllTextAsync(scriptPath,
            "import sys\n" +
            "import os\n" +
            "os.environ['TQDM_DISABLE'] = '1'\n" +
            "os.environ['HF_HUB_ENABLE_HF_TRANSFER'] = '0'\n" +
            "from huggingface_hub import snapshot_download\n" +
            "print('正在连接 HuggingFace...', flush=True)\n" +
            "snapshot_download('kha-white/manga-ocr-base', local_dir=r'" + modelDir + "')\n" +
            "print('模型下载完成', flush=True)\n", ct);
        await RunPythonWithProgressAsync($"\"{scriptPath}\"", progress, ct);
        try { File.Delete(scriptPath); } catch { }

        progress.Report("[done] OCR environment is ready");
        progress.Report("OCR 环境安装完成！");
    }

    private static async Task WarmupPaddleOcrV6Async(IProgress<string> progress, CancellationToken ct)
    {
        string modelRoot = OcrConstants.PaddleOcrV6ModelRoot;
        Directory.CreateDirectory(modelRoot);

        string scriptPath = Path.Combine(Path.GetTempPath(), "warmup_ppocrv6.py");
        await File.WriteAllTextAsync(scriptPath,
            "import os\n" +
            "os.environ['PADDLEOCR_HOME'] = r'" + modelRoot + "'\n" +
            "os.environ['PADDLE_PDX_CACHE_HOME'] = r'" + modelRoot + "'\n" +
            "os.environ.setdefault('FLAGS_allocator_strategy', 'auto_growth')\n" +
            "os.environ.setdefault('FLAGS_use_mkldnn', '0')\n" +
            "os.environ.setdefault('FLAGS_enable_mkldnn', '0')\n" +
            "os.environ.setdefault('FLAGS_enable_pir_api', '0')\n" +
            "import inspect\n" +
            "from paddleocr import TextDetection, TextRecognition\n" +
            "print('Loading PP-OCRv6 detection and recognition models...', flush=True)\n" +
            "def create(cls, kwargs):\n" +
            "    signature = inspect.signature(cls)\n" +
            "    if not any(param.kind == inspect.Parameter.VAR_KEYWORD for param in signature.parameters.values()):\n" +
            "        kwargs = {key: value for key, value in kwargs.items() if key in signature.parameters}\n" +
            "    return cls(**kwargs)\n" +
            "create(TextDetection, dict(model_name='" + OcrConstants.PaddleOcrV6DetectionModel + "', device='cpu', enable_mkldnn=False))\n" +
            "create(TextRecognition, dict(model_name='" + OcrConstants.PaddleOcrV6RecognitionModel + "', device='cpu', enable_mkldnn=False))\n" +
            "print('PP-OCRv6 ready', flush=True)\n", ct);

        try
        {
            await RunPythonWithProgressAsync($"\"{scriptPath}\"", progress, ct);
            if (!OcrEnvironment.HasPaddleOcrModels)
                throw new InvalidOperationException(
                    $"PP-OCRv6 模型下载未完成：未找到 {OcrConstants.PaddleOcrV6DetectionModel} 和 {OcrConstants.PaddleOcrV6RecognitionModel} 权重目录");
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { }
        }
    }

    private static async Task DownloadWithProgressAsync(
        string url, string targetPath, Action<int>? onProgress, CancellationToken ct)
    {
        var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        long total = resp.Content.Headers.ContentLength ?? -1L;
        string tmpPath = targetPath + ".tmp";
        await using (var src = await resp.Content.ReadAsStreamAsync(ct))
        await using (var dst = File.Create(tmpPath))
        {
            byte[] buf = new byte[OcrConstants.DownloadBufferSize];
            long read = 0; int n; int lastPct = -1;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                read += n;
                if (total > 0)
                {
                    int pct = (int)(read * 100 / total);
                    if (pct / 10 != lastPct / 10) { onProgress?.Invoke(pct); lastPct = pct; }
                }
                else if (read - read % 5_242_880 != lastPct)
                {
                    onProgress?.Invoke((int)(read / 1_048_576));
                    lastPct = (int)(read - read % 5_242_880);
                }
            }
        }
        File.Move(tmpPath, targetPath, overwrite: true);
    }

    private static async Task RunPythonAsync(string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = PythonExe, Arguments = arguments,
            UseShellExecute = false, CreateNoWindow = true
        };
        psi.Environment["PYTHONUNBUFFERED"] = "1";
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["PYTHONUTF8"] = "1";
        psi.Environment["PIP_PROGRESS_BAR"] = "on";
        psi.Environment["PADDLEOCR_HOME"] = OcrConstants.PaddleOcrV6ModelRoot;
        psi.Environment["PADDLE_PDX_CACHE_HOME"] = OcrConstants.PaddleOcrV6ModelRoot;

        using var p = Process.Start(psi)!;
        await p.WaitForExitAsync(ct);
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"Python 命令失败 (exit {p.ExitCode})，请查看上方输出");
    }

    private static Task RunPipAsync(string arguments, CancellationToken ct)
        => RunPythonAsync($"-m pip {arguments}", ct);

    private static async Task RunPythonWithProgressAsync(
        string arguments, IProgress<string> progress, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = PythonExe, Arguments = arguments,
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        psi.Environment["PYTHONUNBUFFERED"] = "1";
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["PYTHONUTF8"] = "1";
        psi.Environment["PIP_PROGRESS_BAR"] = "on";
        psi.Environment["PADDLEOCR_HOME"] = OcrConstants.PaddleOcrV6ModelRoot;
        psi.Environment["PADDLE_PDX_CACHE_HOME"] = OcrConstants.PaddleOcrV6ModelRoot;

        using var p = Process.Start(psi)!;

        var outputTask = Task.Run(async () =>
        {
            string? line;
            while ((line = await p.StandardOutput.ReadLineAsync(ct)) != null)
                progress.Report(line.Trim());
        }, ct);

        var errorTask = Task.Run(async () =>
        {
            string? line;
            while ((line = await p.StandardError.ReadLineAsync(ct)) != null)
                progress.Report(line.Trim());
        }, ct);

        await outputTask;
        await errorTask;
        await p.WaitForExitAsync(ct);

        if (p.ExitCode != 0)
            throw new InvalidOperationException($"Python 命令失败 (exit {p.ExitCode})，请查看上方输出");
    }

    private static Task RunPipWithProgressAsync(
        string arguments, IProgress<string> progress, CancellationToken ct)
        => RunPythonWithProgressAsync($"-m pip {arguments}", progress, ct);

    private sealed class CleanConsoleInstallReporter : IProgress<string>
    {
        private const int BarWidth = 28;
        private readonly object _lock = new();
        private string _currentStage = "";
        private string _lastProgressLine = "";

        public IProgress<string> Progress => this;

        public void Start()
        {
            lock (_lock)
            {
                try
                {
                    Console.Title = "LabelMinus OCR 环境安装";
                    Console.CursorVisible = false;
                    Console.Clear();
                }
                catch { }

                WriteColor("LabelMinus OCR 环境安装", ConsoleColor.Cyan);
                WriteMuted("将安装 Python、PaddleOCR/PP-OCRv6、torch、manga-ocr 与模型文件。");
                WriteMuted("窗口可以放在后台；完成或失败后会停在这里。");
                Console.WriteLine(new string('-', 64));
            }
        }

        public void Succeed()
        {
            lock (_lock)
            {
                Console.WriteLine();
                WriteColor("完成：OCR 环境和模型已经准备好。", ConsoleColor.Green);
                Console.WriteLine("按任意键关闭此窗口...");
                TryShowCursor();
            }
        }

        public void Cancel()
        {
            lock (_lock)
            {
                Console.WriteLine();
                WriteColor("安装已取消。", ConsoleColor.Yellow);
                Console.WriteLine("按任意键关闭此窗口...");
                TryShowCursor();
            }
        }

        public void Fail(Exception ex)
        {
            lock (_lock)
            {
                Console.WriteLine();
                WriteColor("安装失败。", ConsoleColor.Red);
                Console.WriteLine(ex.Message);
                Console.WriteLine("按任意键关闭此窗口...");
                TryShowCursor();
            }
        }

        public void Report(string rawMessage)
        {
            string message = Normalize(rawMessage);
            if (string.IsNullOrWhiteSpace(message))
                return;

            lock (_lock)
            {
                if (TryReadStage(message, out var stage))
                {
                    WriteStage(stage);
                    return;
                }

                if (message.StartsWith("[done]", StringComparison.OrdinalIgnoreCase))
                {
                    WriteColor("  模型与依赖校验完成。", ConsoleColor.Green);
                    return;
                }

                int? percent = TryReadPercent(message);
                if (percent.HasValue)
                {
                    DrawProgress(percent.Value, CleanProgressLabel(message));
                    return;
                }

                if (ShouldShow(message))
                    WriteLog(message);
            }
        }

        private void WriteStage(string stage)
        {
            if (stage == _currentStage)
                return;

            _currentStage = stage;
            _lastProgressLine = "";
            Console.WriteLine();
            WriteColor(stage, ConsoleColor.Cyan);
        }

        private static bool TryReadStage(string message, out string stage)
        {
            stage = "";
            const string prefix = "[stage:";
            if (!message.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;

            int end = message.IndexOf(']');
            if (end <= prefix.Length)
                return false;

            string index = message[prefix.Length..end].Trim();
            string title = message[(end + 1)..].Trim();
            stage = string.IsNullOrWhiteSpace(title)
                ? index
                : $"{index}  {title}";
            return true;
        }

        private static string Normalize(string message)
            => message.Trim().Replace('\r', ' ').Replace('\n', ' ');

        private static int? TryReadPercent(string message)
        {
            int percentIndex = message.LastIndexOf('%');
            if (percentIndex <= 0)
                return null;

            int start = percentIndex - 1;
            while (start >= 0 && char.IsDigit(message[start]))
                start--;

            string number = message[(start + 1)..percentIndex];
            return int.TryParse(number, out int value)
                ? Math.Clamp(value, 0, 100)
                : null;
        }

        private static bool ShouldShow(string message)
        {
            if (IsNoise(message))
                return false;

            return message.StartsWith("WARNING", StringComparison.OrdinalIgnoreCase)
                || message.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)
                || message.StartsWith("Successfully installed", StringComparison.OrdinalIgnoreCase)
                || message.StartsWith("Installing collected packages", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Loading PP-OCRv6", StringComparison.OrdinalIgnoreCase)
                || message.Contains("PP-OCRv6 ready", StringComparison.OrdinalIgnoreCase)
                || message.Contains("HuggingFace", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Fetching", StringComparison.OrdinalIgnoreCase)
                || message.Contains("下载", StringComparison.OrdinalIgnoreCase)
                || message.Contains("安装", StringComparison.OrdinalIgnoreCase)
                || message.Contains("完成", StringComparison.OrdinalIgnoreCase)
                || message.Contains("失败", StringComparison.OrdinalIgnoreCase)
                || message.Contains("连接", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNoise(string message)
            => message.StartsWith("Using cached", StringComparison.OrdinalIgnoreCase)
               || message.StartsWith("Requirement already satisfied", StringComparison.OrdinalIgnoreCase)
               || message.StartsWith("Collecting", StringComparison.OrdinalIgnoreCase)
               || message.StartsWith("Downloading", StringComparison.OrdinalIgnoreCase)
               || message.StartsWith("Installing build dependencies", StringComparison.OrdinalIgnoreCase)
               || message.Contains("which is not on PATH", StringComparison.OrdinalIgnoreCase)
               || message.StartsWith("Consider adding this directory", StringComparison.OrdinalIgnoreCase);

        private void DrawProgress(int percent, string label)
        {
            int filled = Math.Clamp(percent * BarWidth / 100, 0, BarWidth);
            string bar = new string('#', filled) + new string('-', BarWidth - filled);
            string line = $"  [{bar}] {percent,3}%  {Shorten(label, 48)}";

            if (line == _lastProgressLine)
                return;

            Console.WriteLine(line);
            _lastProgressLine = line;
        }

        private static string CleanProgressLabel(string message)
        {
            int percentIndex = message.LastIndexOf('%');
            if (percentIndex < 0)
                return message;

            string label = message[..percentIndex].TrimEnd();
            int colonIndex = label.LastIndexOf(':');
            return colonIndex >= 0 ? label[..colonIndex].Trim() : label;
        }

        private static void WriteLog(string message)
        {
            ConsoleColor color = ConsoleColor.Gray;
            if (message.StartsWith("WARNING", StringComparison.OrdinalIgnoreCase))
                color = ConsoleColor.Yellow;
            else if (message.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)
                     || message.Contains("失败", StringComparison.OrdinalIgnoreCase))
                color = ConsoleColor.Red;
            else if (message.StartsWith("Successfully installed", StringComparison.OrdinalIgnoreCase)
                     || message.Contains("ready", StringComparison.OrdinalIgnoreCase)
                     || message.Contains("完成", StringComparison.OrdinalIgnoreCase))
                color = ConsoleColor.Green;

            WriteColor($"  {Shorten(message, 92)}", color);
        }

        private static void WriteMuted(string text) => WriteColor(text, ConsoleColor.DarkGray);

        private static string Shorten(string text, int maxLength)
            => text.Length <= maxLength ? text : text[..Math.Max(0, maxLength - 3)] + "...";

        private static void WriteColor(string text, ConsoleColor color)
        {
            var oldColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ForegroundColor = oldColor;
        }

        private static void TryShowCursor()
        {
            try { Console.CursorVisible = true; } catch { }
        }
    }
}
