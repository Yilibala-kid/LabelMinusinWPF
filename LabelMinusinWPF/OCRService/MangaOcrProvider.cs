using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace LabelMinusinWPF.OCRService;

public sealed class MangaOcrProvider : IOcrProvider
{
    public const string EngineName = "MangaOcr";
    private const string ReadySignal = "manga-ocr ready";
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(180);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(180);

    public bool MergesRegionsInternally => true;

    private static Process? SharedProcess { get; set; }

    private static readonly object SharedLock = new();

    private static string MangaOcrScript =>
        Path.Combine(AppContext.BaseDirectory, OcrConstants.ModelsSubDir, "manga-ocr", "manga_ocr_infer.py");

    private static string MangaOcrModelDir =>
        Path.Combine(AppContext.BaseDirectory, OcrConstants.ModelsSubDir, "manga-ocr", "model");

    public static bool IsProcessRunning => SharedProcess is { HasExited: false };

    public static async Task EnsureProcessAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (IsProcessRunning)
            return;

        if (SharedProcess != null)
            StopProcess();

        if (!OcrEnvironment.ReadyForProcessStart)
            throw new InvalidOperationException("Python 环境或 manga-ocr 模型未配置，日文 OCR 不可用");

        progress?.Report("manga-ocr 模型加载中");
        await StartProcessAsync();
        ct.ThrowIfCancellationRequested();
        progress?.Report("manga-ocr 已启动");
    }

    private static Task StartProcessAsync()
        => Task.Run(() =>
        {
            Environment.SetEnvironmentVariable("PYTHONIOENCODING", "utf-8");
            Environment.SetEnvironmentVariable("PYTHONUTF8", "1");
            Environment.SetEnvironmentVariable("PYTHONUNBUFFERED", "1");

            lock (SharedLock)
            {
                if (SharedProcess != null) return;

                ValidateEnvironment();

                SharedProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = PythonEnvironmentInstaller.PythonExe,

                    Arguments = $"\"{MangaOcrScript}\"",

                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,

                    UseShellExecute = false,

                    CreateNoWindow = true
                });

                if (SharedProcess == null) return;

                var proc = SharedProcess;

                WaitForReadySignal(proc);

                DrainStderrBackground(proc);
            }
        });

    private static void ValidateEnvironment()
    {
        if (!File.Exists(PythonEnvironmentInstaller.PythonExe))
            throw new InvalidOperationException(
                "Python 环境未安装，请先通过 [OCR -> 配置 OCR 环境] 安装");

        if (!File.Exists(MangaOcrScript))
            throw new InvalidOperationException(
                $"manga-ocr 脚本不存在: {MangaOcrScript}");

        if (!Directory.Exists(MangaOcrModelDir)
            || !Directory.EnumerateFiles(MangaOcrModelDir).Any())
            throw new InvalidOperationException(
                "manga-ocr 模型未下载，请通过 [OCR → 配置 OCR 环境] 安装");
    }

    private static void WaitForReadySignal(Process proc)
    {
        var stderrLines = new List<string>();

        while (!proc.HasExited)
        {
            var readTask = proc.StandardError.ReadLineAsync();
            if (!readTask.Wait(StartupTimeout))
            {
                KillProcess(proc);
                SharedProcess = null;
                throw new TimeoutException("manga-ocr process startup timed out");
            }

            string? line = readTask.Result;
            if (line == null)
                break;

            stderrLines.Add(line);
            if (line.Contains(ReadySignal, StringComparison.OrdinalIgnoreCase))
                return;
        }

        string detail = stderrLines.Count > 0
            ? string.Join("\n", stderrLines)
            : "(no stderr output)";
        KillProcess(proc);
        SharedProcess = null;
        throw new InvalidOperationException(
            $"manga-ocr process failed to start:\n{detail[..Math.Min(detail.Length, 500)]}");
    }

    private static void KillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill();
        }
        catch { }

        try { process.Dispose(); } catch { }
    }
    private static void DrainStderrBackground(Process proc)
    {
        Task.Run(() =>
        {
            try
            {
                while (proc.StandardError.ReadLine() != null) { }
            }
            catch
            {
            }
        });
    }

    public static void StopProcess()
    {
        lock (SharedLock)
        {
            if (SharedProcess == null) return;

            try { SharedProcess.StandardInput.Close(); } catch { }

            try
            {
                if (!SharedProcess.HasExited)
                    SharedProcess.WaitForExit(3000);
            } catch { }

            try
            {
                if (!SharedProcess.HasExited)
                    SharedProcess.Kill();
            } catch { }

            try { SharedProcess.Dispose(); } catch { }

            SharedProcess = null;
        }
    }


    private static string? SendOcrRequest(string imagePath, int[][] boxes, CancellationToken ct)
    {
        var req = JsonSerializer.Serialize(new { image = imagePath, boxes });

        lock (SharedLock)
        {
            ct.ThrowIfCancellationRequested();

            var process = SharedProcess;
            if (process == null || process.HasExited)
                throw new InvalidOperationException("manga-ocr process is not running");

            process.StandardInput.WriteLine(req);
            process.StandardInput.Flush();

            var readTask = process.StandardOutput.ReadLineAsync();
            if (!readTask.Wait(RequestTimeout))
            {
                KillProcess(process);
                SharedProcess = null;
                throw new TimeoutException("manga-ocr request timed out");
            }

            ct.ThrowIfCancellationRequested();
            return readTask.Result;
        }
    }


    public async Task<IReadOnlyList<OcrTextRegion>> RecognizeAsync(
        string imagePath,
        OcrModelInfo model,
        AutoOcrOptions options,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (SharedProcess == null || SharedProcess.HasExited)
            return Array.Empty<OcrTextRegion>();

        var paddleModel = OcrPipeline.FindPaddleModel()
            ?? throw new InvalidOperationException("未找到 PP-OCRv6 模型配置");
        var detectionOptions = options with { DetectOnly = true };
        var rawRegions = await PaddleOcrPythonProvider.Shared.RecognizeAsync(
            imagePath,
            paddleModel,
            detectionOptions,
            ct);

        if (rawRegions.Count == 0)
            return Array.Empty<OcrTextRegion>();

        var imageSize = new Size(
            Math.Max(1, rawRegions.Max(region => region.Bounds.Right)),
            Math.Max(1, rawRegions.Max(region => region.Bounds.Bottom)));

        var mergedRegions = OcrPipeline.BuildTextBlocks(
            rawRegions, imageSize, options, OcrPipeline.IsVerticalLayout(rawRegions));

        var boxes = mergedRegions.Select(r =>
            new[]
            {
                (int)r.Bounds.Left,
                (int)r.Bounds.Top,
                (int)r.Bounds.Width,
                (int)r.Bounds.Height
            }).ToArray();

        var respLine = await Task.Run(
            () => SendOcrRequest(imagePath, boxes, ct),
            ct);

        if (respLine == null)
            throw new InvalidOperationException("manga-ocr 进程无响应");

        var texts = JsonSerializer
            .Deserialize<MangaOcrResponse>(respLine)!
            .texts ?? [];

        var results = new List<OcrTextRegion>();
        for (int i = 0; i < mergedRegions.Count; i++)
            results.Add(new OcrTextRegion(
                i < texts.Length ? texts[i] : "",
                mergedRegions[i].Bounds,
                1.0));

        return results;
    }

    private sealed record MangaOcrResponse(string[]? texts);
}
