using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace LabelMinusinWPF.OCRService;

public sealed class PaddleOcrPythonProvider : IOcrProvider
{
    public const string EngineName = "PaddleOcrPython";
    private const string ReadySignal = "paddle-ocr ready";
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(180);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(180);

    private static readonly object SharedLock = new();
    private static Process? SharedProcess;

    public static readonly PaddleOcrPythonProvider Shared = new();

    public static bool IsProcessRunning => SharedProcess is { HasExited: false };

    public static bool CanHandleEngine(string engine) =>
        engine.Equals(EngineName, StringComparison.OrdinalIgnoreCase) ||
        engine.Equals("PaddleOCR", StringComparison.OrdinalIgnoreCase) ||
        engine.Equals("PP-OCRv6", StringComparison.OrdinalIgnoreCase);

    public static async Task EnsureProcessAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (IsProcessRunning)
            return;

        if (SharedProcess != null)
            StopProcess();

        ValidateEnvironment();

        progress?.Report("PaddleOCR 模型加载中");
        await StartProcessAsync(ct);
        progress?.Report("PaddleOCR 已启动");
    }

    public static void StopProcess()
    {
        lock (SharedLock)
        {
            if (SharedProcess == null)
                return;

            try { SharedProcess.StandardInput.Close(); } catch { }
            try
            {
                if (!SharedProcess.HasExited)
                    SharedProcess.WaitForExit(3000);
            }
            catch { }

            try
            {
                if (!SharedProcess.HasExited)
                    SharedProcess.Kill();
            }
            catch { }

            try { SharedProcess.Dispose(); } catch { }
            SharedProcess = null;
        }
    }

    public async Task<IReadOnlyList<OcrTextRegion>> RecognizeAsync(
        string imagePath,
        OcrModelInfo model,
        AutoOcrOptions options,
        CancellationToken ct)
    {
        await EnsureProcessAsync(ct: ct);
        ct.ThrowIfCancellationRequested();

        var response = await Task.Run(
            () => SendOcrRequest(imagePath, options.DetectOnly, ct),
            ct);
        if (!string.IsNullOrWhiteSpace(response.Error))
            throw new InvalidOperationException($"PaddleOCR 识别失败: {response.Error}");

        return response.Regions
            .Select(region => new OcrTextRegion(
                region.Text ?? "",
                new Rect(region.Box[0], region.Box[1], region.Box[2], region.Box[3]),
                region.Score))
            .Where(region => region.Confidence >= options.MinConfidence)
            .ToList();
    }

    private static Task StartProcessAsync(CancellationToken ct) =>
        Task.Run(() =>
        {
            lock (SharedLock)
            {
                if (SharedProcess != null)
                    return;

                var startInfo = new ProcessStartInfo
                {
                    FileName = PythonEnvironmentInstaller.PythonExe,
                    Arguments = $"\"{PythonEnvironmentInstaller.PaddleOcrScript}\"",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
                startInfo.Environment["PYTHONUTF8"] = "1";
                startInfo.Environment["PYTHONUNBUFFERED"] = "1";
                startInfo.Environment["PADDLEOCR_HOME"] = OcrConstants.PaddleOcrV6ModelRoot;
                startInfo.Environment["PADDLE_PDX_CACHE_HOME"] = OcrConstants.PaddleOcrV6ModelRoot;

                var process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("无法启动 PaddleOCR 进程");

                SharedProcess = process;
                WaitForReadySignal(process, ct);
                DrainStderrBackground(process);
            }
        }, ct);

    private static void WaitForReadySignal(Process process, CancellationToken ct)
    {
        var stderrLines = new List<string>();

        while (!process.HasExited)
        {
            ct.ThrowIfCancellationRequested();

            var readTask = process.StandardError.ReadLineAsync();
            if (!readTask.Wait(StartupTimeout))
            {
                KillProcess(process);
                SharedProcess = null;
                throw new TimeoutException("PaddleOCR process startup timed out");
            }

            string? line = readTask.Result;
            if (line == null)
                break;

            stderrLines.Add(line);
            if (line.Contains(ReadySignal, StringComparison.OrdinalIgnoreCase))
                return;
        }

        string detail = stderrLines.Count == 0
            ? "(no stderr output)"
            : string.Join(Environment.NewLine, stderrLines);
        KillProcess(process);
        SharedProcess = null;
        throw new InvalidOperationException(
            $"PaddleOCR process failed to start:\n{detail[..Math.Min(detail.Length, 800)]}");
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
    private static void DrainStderrBackground(Process process)
    {
        Task.Run(() =>
        {
            try
            {
                while (process.StandardError.ReadLine() != null) { }
            }
            catch { }
        });
    }

    private static PaddleOcrResponse SendOcrRequest(
        string imagePath,
        bool detectOnly,
        CancellationToken ct)
    {
        lock (SharedLock)
        {
            ct.ThrowIfCancellationRequested();

            var process = SharedProcess;
            if (process == null || process.HasExited)
                throw new InvalidOperationException("PaddleOCR process is not running");

            string request = JsonSerializer.Serialize(new
            {
                image = imagePath,
                detect_only = detectOnly
            });

            process.StandardInput.WriteLine(request);
            process.StandardInput.Flush();

            var readTask = process.StandardOutput.ReadLineAsync();
            if (!readTask.Wait(RequestTimeout))
            {
                KillProcess(process);
                SharedProcess = null;
                throw new TimeoutException("PaddleOCR request timed out");
            }

            ct.ThrowIfCancellationRequested();
            string? line = readTask.Result;

            if (line == null)
                throw new InvalidOperationException("PaddleOCR process returned no response");

            return JsonSerializer.Deserialize<PaddleOcrResponse>(line)
                ?? new PaddleOcrResponse([], "PaddleOCR returned an empty response");
        }
    }

    private static void ValidateEnvironment()
    {
        if (File.Exists(PythonEnvironmentInstaller.PythonExe)
            && File.Exists(PythonEnvironmentInstaller.PaddleOcrScript)
            && !OcrEnvironment.HasPaddleOcrModels)
            throw new InvalidOperationException(
                "PP-OCRv6 模型权重未下载，请先通过 [OCR -> 配置 OCR 环境] 完成环境配置");

        if (!File.Exists(PythonEnvironmentInstaller.PythonExe))
            throw new InvalidOperationException("Python 环境未安装，请先通过 [OCR -> 配置 OCR 环境] 安装");

        if (!File.Exists(PythonEnvironmentInstaller.PaddleOcrScript))
            throw new InvalidOperationException($"PaddleOCR 脚本不存在: {PythonEnvironmentInstaller.PaddleOcrScript}");
    }

    private sealed record PaddleOcrResponse(
        [property: JsonPropertyName("regions")] PaddleOcrRegion[] Regions,
        [property: JsonPropertyName("error")] string? Error);

    private sealed record PaddleOcrRegion(
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("score")] double Score,
        [property: JsonPropertyName("box")] double[] Box);
}
