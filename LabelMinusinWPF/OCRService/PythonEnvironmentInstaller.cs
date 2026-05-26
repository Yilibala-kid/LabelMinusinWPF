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

    private static readonly string[] PipPackages = ["manga-ocr", "huggingface_hub"];

    private static readonly string[] ModelScopeTags = ["master", "v3.7.0", "v3.4.0"];
    private static readonly (string File, string SubDir)[] OnnxModels =
    [
        ("ch_PP-OCRv5_det_server.onnx", "det"),
        ("ch_PP-LCNet_x1_0_textline_ori_cls_server.onnx", "cls"),
        ("ch_PP-OCRv5_rec_server.onnx", "rec"),
    ];

    public static string PythonDir => Path.Combine(AppContext.BaseDirectory, "python");
    public static string PythonExe => Path.Combine(PythonDir, "python.exe");

    public static void Uninstall()
    {
        if (Directory.Exists(PythonDir))
            Directory.Delete(PythonDir, recursive: true);

        string v5Dir = Path.Combine(AppContext.BaseDirectory, "models", "v5");
        if (Directory.Exists(v5Dir))
            foreach (var f in Directory.EnumerateFiles(v5Dir, "*.onnx"))
                File.Delete(f);

        string mangaModelDir = Path.Combine(AppContext.BaseDirectory, "models", "manga-ocr", "model");
        if (Directory.Exists(mangaModelDir))
            Directory.Delete(mangaModelDir, recursive: true);
    }

    public static async Task InstallWithConsoleReporterAsync(CancellationToken ct = default)
    {
        var reporter = new ConsoleInstallReporter();
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
        await DownloadOnnxModelsAsync(progress, ct);

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

        progress.Report("正在安装 pip...");
        string getPipPath = Path.Combine(Path.GetTempPath(), "get-pip.py");
        await File.WriteAllBytesAsync(getPipPath, await Http.GetByteArrayAsync(GetPipUrl, ct), ct);
        await RunPythonAsync($"\"{getPipPath}\" --no-python-version-warning", ct);
        File.Delete(getPipPath);

        progress.Report("安装 torch CPU 版（约 800MB，请耐心等待）...");
        await RunPipWithProgressAsync(
            $"install --no-warn-script-location {TorchPackage} --index-url {TorchIndexUrl}",
            progress,
            ct);

        var allPkgs = string.Join(" ", PipPackages);
        progress.Report($"安装 Python 包: {allPkgs}");
        await RunPipWithProgressAsync(
            $"install --no-warn-script-location {allPkgs}",
            progress,
            ct);

        progress.Report("下载 manga-ocr 模型（约 400MB）...");
        string modelDir = Path.Combine(AppContext.BaseDirectory, "models", "manga-ocr", "model");
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

        progress.Report("Python OCR 环境安装完成！");
    }

    private static async Task DownloadOnnxModelsAsync(IProgress<string> progress, CancellationToken ct)
    {
        string modelDir = Path.Combine(AppContext.BaseDirectory, "models", "v5");
        Directory.CreateDirectory(modelDir);

        for (int i = 0; i < OnnxModels.Length; i++)
        {
            var (file, subDir) = OnnxModels[i];
            string targetPath = Path.Combine(modelDir, file);
            if (File.Exists(targetPath) && new FileInfo(targetPath).Length > 0) continue;

            progress.Report($"下载 ONNX 模型 ({i + 1}/{OnnxModels.Length}): {file}...");

            Exception? lastEx = null;
            bool found = false;
            for (int tagIndex = 0; tagIndex < ModelScopeTags.Length; tagIndex++)
            {
                string tag = ModelScopeTags[tagIndex];
                string url = $"https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/{tag}/onnx/PP-OCRv5/{subDir}/{file}";
                try
                {
                    await DownloadWithProgressAsync(url, targetPath,
                        pct => progress.Report($"下载 {file}: {pct}%"), ct);
                    found = true; break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    lastEx = ex;
                    try { File.Delete(targetPath + ".tmp"); } catch { }

                    if (tagIndex < ModelScopeTags.Length - 1)
                        progress.Report($"ONNX 模型 {file} 源 {tag} 下载失败，尝试备用源...");
                }
            }
            if (found) continue;

            throw new InvalidOperationException(
                $"模型 {file} 下载失败，已尝试 {ModelScopeTags.Length} 个来源。" +
                (lastEx != null ? $" 最后错误: {lastEx.Message}" : ""));
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
        psi.Environment["PIP_PROGRESS_BAR"] = "on";

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
        psi.Environment["PIP_PROGRESS_BAR"] = "on";

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

    private sealed class ConsoleInstallReporter : IProgress<string>
    {
        private const int BarWidth = 32;
        private readonly object _lock = new();
        private string _stage = "";
        private string _lastLine = "";

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
                Console.WriteLine("正在准备 PaddleOCR、Python 和 manga-ocr 依赖。窗口可以放在后台，完成后会停在这里。");
                Console.WriteLine(new string('-', 68));
                Console.WriteLine();
            }
        }

        public void Succeed()
        {
            lock (_lock)
            {
                Console.WriteLine();
                WriteColor("安装完成！OCR 环境已经就绪。", ConsoleColor.Green);
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
            if (string.IsNullOrWhiteSpace(message)) return;

            lock (_lock)
            {
                string stage = DetectStage(message);
                if (!string.IsNullOrEmpty(stage) && stage != _stage)
                {
                    _stage = stage;
                    Console.WriteLine();
                    WriteColor($"[{DateTime.Now:HH:mm:ss}] {_stage}", ConsoleColor.Cyan);
                }

                int? percent = TryReadPercent(message);
                if (percent.HasValue)
                {
                    DrawProgress(percent.Value, Shorten(message, 64));
                    return;
                }

                if (ShouldShow(message))
                    WriteLog(message);
            }
        }

        private static string Normalize(string message)
            => message.Trim().Replace('\r', ' ').Replace('\n', ' ');

        private static string DetectStage(string message)
        {
            if (message.Contains("ONNX", StringComparison.OrdinalIgnoreCase))
                return "1/6 PaddleOCR 模型";
            if (message.Contains("Python", StringComparison.OrdinalIgnoreCase))
                return "2/6 Python 嵌入环境";
            if (message.Contains("pip", StringComparison.OrdinalIgnoreCase))
                return "3/6 pip 安装";
            if (message.Contains("torch", StringComparison.OrdinalIgnoreCase))
                return "4/6 torch CPU 依赖";
            if (message.Contains("Python 包", StringComparison.OrdinalIgnoreCase)
                || message.Contains("manga-ocr", StringComparison.OrdinalIgnoreCase)
                    && !message.Contains("模型", StringComparison.OrdinalIgnoreCase))
                return "5/6 Python OCR 包";
            if (message.Contains("HuggingFace", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Fetching", StringComparison.OrdinalIgnoreCase)
                || message.Contains("manga-ocr", StringComparison.OrdinalIgnoreCase))
                return "6/6 manga-ocr 模型";
            return "";
        }

        private static int? TryReadPercent(string message)
        {
            int percentIndex = message.LastIndexOf('%');
            if (percentIndex <= 0) return null;

            int start = percentIndex - 1;
            while (start >= 0 && char.IsDigit(message[start])) start--;
            string number = message[(start + 1)..percentIndex];
            return int.TryParse(number, out int value)
                ? Math.Clamp(value, 0, 100)
                : null;
        }

        private static bool ShouldShow(string message)
        {
            if (message.StartsWith("Using cached", StringComparison.OrdinalIgnoreCase))
                return false;
            if (message.StartsWith("Requirement already satisfied", StringComparison.OrdinalIgnoreCase))
                return false;
            if (message.Contains("which is not on PATH", StringComparison.OrdinalIgnoreCase))
                return false;
            if (message.StartsWith("Consider adding this directory", StringComparison.OrdinalIgnoreCase))
                return false;

            return message.StartsWith("Collecting", StringComparison.OrdinalIgnoreCase)
                || message.StartsWith("Installing", StringComparison.OrdinalIgnoreCase)
                || message.StartsWith("Successfully", StringComparison.OrdinalIgnoreCase)
                || message.StartsWith("WARNING", StringComparison.OrdinalIgnoreCase)
                || message.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)
                || message.Contains("失败", StringComparison.OrdinalIgnoreCase)
                || message.Contains("完成", StringComparison.OrdinalIgnoreCase)
                || message.Contains("正在", StringComparison.OrdinalIgnoreCase)
                || message.Contains("安装", StringComparison.OrdinalIgnoreCase)
                || message.Contains("下载", StringComparison.OrdinalIgnoreCase)
                || message.Contains("连接", StringComparison.OrdinalIgnoreCase);
        }

        private void DrawProgress(int percent, string label)
        {
            int filled = Math.Clamp(percent * BarWidth / 100, 0, BarWidth);
            string bar = new string('#', filled) + new string('-', BarWidth - filled);
            string line = $"  [{bar}] {percent,3}%  {label}";

            if (line == _lastLine) return;

            Console.WriteLine(line);
            _lastLine = line;
        }

        private static void WriteLog(string message)
        {
            ConsoleColor color = ConsoleColor.Gray;
            if (message.StartsWith("WARNING", StringComparison.OrdinalIgnoreCase))
                color = ConsoleColor.Yellow;
            else if (message.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)
                     || message.Contains("失败", StringComparison.OrdinalIgnoreCase))
                color = ConsoleColor.Red;
            else if (message.StartsWith("Successfully", StringComparison.OrdinalIgnoreCase)
                     || message.Contains("完成", StringComparison.OrdinalIgnoreCase))
                color = ConsoleColor.Green;

            WriteColor($"  {Shorten(message, 96)}", color);
        }

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
