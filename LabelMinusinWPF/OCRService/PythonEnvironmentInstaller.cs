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

    private static readonly string[] PipPackages = ["manga-ocr", "huggingface_hub"];

    private static readonly string[] ModelScopeTags = ["v3.7.0", "v3.4.0", "master"];
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

    public static async Task InstallAsync(IProgress<string> progress, CancellationToken ct = default)
    {
        // Step 1: 下载嵌入版 Python
        progress.Report("正在下载 Python 嵌入版...");
        string zipPath = Path.Combine(Path.GetTempPath(), $"python-{PythonVersion}-embed-amd64.zip");
        await DownloadWithProgressAsync(PythonDownloadUrl, zipPath,
            pct => progress.Report($"下载 Python: {pct}%"), ct);

        // Step 2: 解压
        progress.Report("正在解压 Python...");
        if (Directory.Exists(PythonDir)) Directory.Delete(PythonDir, recursive: true);
        ZipFile.ExtractToDirectory(zipPath, PythonDir);
        File.Delete(zipPath);

        // Step 3: 配置 python310._pth
        progress.Report("正在配置 Python 环境...");
        File.WriteAllText(Path.Combine(PythonDir, "python310._pth"),
            "python310.zip\r\n.\r\n\r\n# Uncomment to run site.main() automatically\r\nimport site\r\nLib\\site-packages\r\n");

        // Step 4: 安装 pip
        progress.Report("正在安装 pip...");
        string getPipPath = Path.Combine(Path.GetTempPath(), "get-pip.py");
        await File.WriteAllBytesAsync(getPipPath, await Http.GetByteArrayAsync(GetPipUrl, ct), ct);
        await RunPythonAsync($"\"{getPipPath}\" --no-python-version-warning", ct);
        File.Delete(getPipPath);

        // Step 5: 安装 torch (CPU)
        progress.Report("安装 torch CPU 版（约 800MB，请耐心等待）...");
        await RunPipWithProgressAsync($"install torch --index-url {TorchIndexUrl}", progress, ct);

        // Step 6: 安装其余包
        var allPkgs = string.Join(" ", PipPackages);
        progress.Report($"安装 Python 包: {allPkgs}");
        await RunPipWithProgressAsync($"install {allPkgs}", progress, ct);

        // Step 7: 下载 manga-ocr 模型
        progress.Report("下载 manga-ocr 模型（约 400MB）...");
        string modelDir = Path.Combine(AppContext.BaseDirectory, "models", "manga-ocr", "model");
        string scriptPath = Path.Combine(Path.GetTempPath(), "download_model.py");
        await File.WriteAllTextAsync(scriptPath,
            "import sys\n" +
            "from huggingface_hub import snapshot_download\n" +
            "import os\n" +
            // 重定向 tqdm 输出到 stdout，方便 C# 端读取进度
            "os.environ['TQDM_DISABLE'] = '1'\n" +
            "os.environ['HF_HUB_ENABLE_HF_TRANSFER'] = '0'\n" +
            "print('正在连接 HuggingFace...', flush=True)\n" +
            "snapshot_download('kha-white/manga-ocr-base', local_dir=r'" + modelDir + "')\n" +
            "print('模型下载完成', flush=True)\n", ct);
        await RunPythonWithProgressAsync($"\"{scriptPath}\"", progress, ct);
        try { File.Delete(scriptPath); } catch { }

        // Step 8: 下载 ONNX 模型
        await DownloadOnnxModelsAsync(progress, ct);

        progress.Report("Python OCR 环境安装完成！");
    }

    // ========================================================================
    // 辅助方法
    // ========================================================================

    private static async Task DownloadOnnxModelsAsync(IProgress<string> progress, CancellationToken ct)
    {
        string modelDir = Path.Combine(AppContext.BaseDirectory, "models", "v5");
        Directory.CreateDirectory(modelDir);

        for (int i = 0; i < OnnxModels.Length; i++)
        {
            var (file, subDir) = OnnxModels[i];
            string targetPath = Path.Combine(modelDir, file);
            if (File.Exists(targetPath) && new FileInfo(targetPath).Length > 0) continue;

            Exception? lastEx = null;
            bool found = false;
            foreach (var tag in ModelScopeTags)
            {
                string url = $"https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/{tag}/onnx/PP-OCRv5/{subDir}/{file}";
                progress.Report($"下载 ONNX 模型 ({i + 1}/{OnnxModels.Length}): {file}...");
                try
                {
                    await DownloadWithProgressAsync(url, targetPath,
                        pct => progress.Report($"下载 {file}: {pct}%"), ct);
                    found = true; break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { lastEx = ex; }
            }
            if (found) continue;

            throw new InvalidOperationException(
                $"模型 {file} 下载失败，已尝试 {ModelScopeTags.Length} 个镜像。" +
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
}
