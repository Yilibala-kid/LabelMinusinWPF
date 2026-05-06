using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace LabelMinusinWPF.Common;

/// <summary>
/// 在程序目录下自动安装嵌入版 Python + OCR 依赖，替代 setup_manga_ocr.bat
/// </summary>
public static class PythonEnvironmentInstaller
{
    private const string PythonVersion = "3.10.11";
    private const string PythonDownloadUrl =
        $"https://www.python.org/ftp/python/{PythonVersion}/python-{PythonVersion}-embed-amd64.zip";
    private const string GetPipUrl = "https://bootstrap.pypa.io/get-pip.py";

    // 国内用户可将 PipIndex 替换为 https://pypi.tuna.tsinghua.edu.cn/simple
    private const string PipIndexUrl = "https://pypi.org/simple/";
    private const string TorchIndexUrl = "https://download.pytorch.org/whl/cpu";

    private static readonly string[] Stage1Packages = ["torch", "manga-ocr", "Pillow"];
    private static readonly string[] Stage2Packages = ["huggingface_hub"];

    public static string PythonDir => Path.Combine(AppContext.BaseDirectory, "python");
    public static string PythonExe => Path.Combine(PythonDir, "python.exe");

    public static async Task InstallAsync(IProgress<string> progress, CancellationToken ct = default)
    {
        // ---------- Step 1: 下载嵌入版 Python ----------
        progress.Report("正在下载 Python 嵌入版...");
        string zipPath = Path.Combine(Path.GetTempPath(), $"python-{PythonVersion}-embed-amd64.zip");

        await DownloadWithProgressAsync(PythonDownloadUrl, zipPath, null, ct);

        // ---------- Step 2: 解压 ----------
        progress.Report("正在解压 Python...");
        if (Directory.Exists(PythonDir))
            Directory.Delete(PythonDir, recursive: true);
        ZipFile.ExtractToDirectory(zipPath, PythonDir);
        File.Delete(zipPath);

        // ---------- Step 3: 配置 python310._pth ----------
        progress.Report("正在配置 Python 环境...");
        string pthPath = Path.Combine(PythonDir, "python310._pth");
        File.WriteAllText(pthPath,
            "python310.zip\r\n.\r\n\r\n# Uncomment to run site.main() automatically\r\nimport site\r\nLib\\site-packages\r\n");

        // ---------- Step 4: 安装 pip ----------
        progress.Report("正在安装 pip...");
        string getPipPath = Path.Combine(Path.GetTempPath(), "get-pip.py");
        using (var hc = new HttpClient())
        {
            byte[] getPipBytes = await hc.GetByteArrayAsync(GetPipUrl, ct);
            await File.WriteAllBytesAsync(getPipPath, getPipBytes, ct);
        }
        await RunPythonAsync($"\"{getPipPath}\" --no-python-version-warning", progress, ct);
        File.Delete(getPipPath);

        // ---------- Step 5: 安装 torch（必须使用 PyTorch CPU 索引） ----------
        progress.Report("安装 torch (CPU 版)...");
        await RunPipAsync($"install torch --index-url {TorchIndexUrl}", progress, ct);

        // ---------- Step 6: 安装其余 Python 包 ----------
        var allPkgs = string.Join(" ", Stage1Packages.Where(p => p != "torch").Concat(Stage2Packages));
        progress.Report($"安装 Python 包: {allPkgs}");
        await RunPipAsync($"install {allPkgs}", progress, ct);

        // ---------- Step 7: 下载 manga-ocr 模型到本地 ----------
        progress.Report("下载 manga-ocr 模型（约 400MB）...");
        string modelDir = Path.Combine(AppContext.BaseDirectory, "models", "manga-ocr", "model");
        string downloadScript =
            $"import sys\n" +
            $"from huggingface_hub import snapshot_download\n" +
            $"snapshot_download('kha-white/manga-ocr-base', local_dir=r'{modelDir}')\n";
        string scriptPath = Path.Combine(Path.GetTempPath(), "download_model.py");
        await File.WriteAllTextAsync(scriptPath, downloadScript, ct);
        try
        {
            await RunPythonAsync($"\"{scriptPath}\"", progress, ct);
        }
        finally { try { File.Delete(scriptPath); } catch { } }

        progress.Report("Python OCR 环境安装完成！");
    }

    // ========================================================================
    // 辅助方法
    // ========================================================================

    private static async Task DownloadWithProgressAsync(
        string url, string targetPath, Action<int>? onProgress, CancellationToken ct)
    {
        using var hc = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        var resp = await hc.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        long total = resp.Content.Headers.ContentLength ?? -1L;
        using var src = await resp.Content.ReadAsStreamAsync(ct);
        using var dst = File.Create(targetPath);

        byte[] buf = new byte[81920];
        long read = 0;
        int n;
        int lastPct = -1;
        while ((n = await src.ReadAsync(buf, ct)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, n), ct);
            read += n;
            if (total > 0)
            {
                int pct = (int)(read * 100 / total);
                if (pct / 10 != lastPct / 10) { onProgress?.Invoke(pct); lastPct = pct; }
            }
        }
    }

    private static async Task RunPythonAsync(string arguments, IProgress<string> progress, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = PythonExe,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var p = Process.Start(psi)!;

        // 只收集输出用于错误诊断，不逐行推送到 UI（pip 输出有几百行）
        var stderrTask = p.StandardError.ReadToEndAsync(ct);
        _ = ConsumeAndDiscardAsync(p.StandardOutput, ct);
        await p.WaitForExitAsync(ct);
        string stderr = await stderrTask;

        if (p.ExitCode != 0)
        {
            string detail = stderr.Length > 500 ? stderr[^500..] : stderr;
            throw new InvalidOperationException($"Python 命令失败 (exit {p.ExitCode}): {detail}");
        }
    }

    private static async Task RunPipAsync(string arguments, IProgress<string> progress, CancellationToken ct)
    {
        await RunPythonAsync($"-m pip {arguments}", progress, ct);
    }

    private static async Task ConsumeAndDiscardAsync(StreamReader reader, CancellationToken ct)
    {
        try
        {
            var buf = new char[4096];
            while (await reader.ReadAsync(buf, ct) > 0) { }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }
}
