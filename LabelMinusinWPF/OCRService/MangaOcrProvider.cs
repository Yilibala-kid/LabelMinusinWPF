using System.Diagnostics;     // Process、ProcessStartInfo 子进程管理
using System.IO;               // File、Path 文件操作
using System.Text.Json;        // JSON 序列化/反序列化（与 manga-ocr 进程通信）
using System.Windows;          // Size（图片尺寸）
using System.Windows.Media.Imaging; // BitmapSource（截图）
using RapidOcrNet;             // RapidOcr 检测引擎（PaddleOCR 检测部分）
using SkiaSharp;               // SKBitmap 图片解码

namespace LabelMinusinWPF.OCRService;

// MangaOcrProvider — manga-ocr 日文 OCR 提供者
// 采用混合架构：PaddleOCR RapidOcr 做文字检测 + manga-ocr Python 进程做文字识别
// ============================================================================

public sealed class MangaOcrProvider : IOcrProvider
{
    // 引擎名称标识
    public const string EngineName = "MangaOcr";

    // RecognizeAsync 内部已完成文本块合并，OcrPipeline 需跳过合并步骤
    public bool MergesRegionsInternally => true;

    // manga-ocr Python 子进程（跨线程共享，用锁保护）
    public static Process? SharedProcess { get; private set; }

    // 访问 SharedProcess 的线程安全锁
    public static readonly object SharedLock = new();

    // 懒加载的 PaddleOCR 检测引擎（实例级，每个 MangaOcrProvider 独立）
    private RapidOcr? _detEngine;

    // ================================================================
    // 进程生命周期
    // ================================================================

    // 启动 manga-ocr Python 子进程（异步，在后台线程执行）
    public static Task StartProcessAsync(Action<string>? onProgress = null)
        => Task.Run(() =>
        {
            lock (SharedLock)
            {
                // 已有进程则跳过（避免重复启动）
                if (SharedProcess != null) return;

                // 启动前校验环境：Python、脚本、模型是否齐全
                ValidateEnvironment();

                // 创建 Python 子进程
                SharedProcess = Process.Start(new ProcessStartInfo
                {
                    // Python 解释器路径（由 PythonEnvironmentInstaller 安装）
                    FileName = PythonEnvironmentInstaller.PythonExe,

                    // manga-ocr 推理脚本路径
                    Arguments = $"\"{Path.Combine(
                        AppContext.BaseDirectory,
                        "models", "manga-ocr",
                        "manga_ocr_infer.py")}\"",

                    // 父子进程通过标准输入输出通信（JSON 行协议）
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,

                    // 不使用 shell，避免弹出窗口
                    UseShellExecute = false,

                    // 不创建控制台窗口
                    CreateNoWindow = true
                });

                if (SharedProcess == null) return;

                // 局部变量引用，防止 Stop→Start 切换期间外部读到新进程
                var proc = SharedProcess;

                // 等待进程输出"就绪"信号（manga-ocr 启动完成后会打印到 stderr）
                WaitForReadySignal(proc, onProgress);

                // 后台持续读取 stderr，避免管道缓冲区满导致子进程阻塞
                DrainStderrBackground(proc);
            }
        });

    // 启动前检查 Python、脚本、模型是否全部就位，任一缺失则抛异常
    private static void ValidateEnvironment()
    {
        // 检查 Python 解释器是否存在
        if (!File.Exists(PythonEnvironmentInstaller.PythonExe))
            throw new InvalidOperationException(
                "Python 环境未安装，请先通过 [高级 -> 配置 OCR 环境] 安装");

        // 检查 manga-ocr 推理脚本是否存在
        string scriptPath = Path.Combine(
            AppContext.BaseDirectory, "models", "manga-ocr", "manga_ocr_infer.py");
        if (!File.Exists(scriptPath))
            throw new InvalidOperationException(
                $"manga-ocr 脚本不存在: {scriptPath}");

        // 检查 manga-ocr 模型目录是否有文件（HuggingFace 下载）
        string modelDir = Path.Combine(
            AppContext.BaseDirectory, "models", "manga-ocr", "model");
        if (!Directory.Exists(modelDir)
            || !Directory.EnumerateFiles(modelDir).Any())
            throw new InvalidOperationException(
                "manga-ocr 模型未下载，请通过 [OCR → 配置 OCR 环境] 安装");
    }

    // 阻塞读取 stderr，直到读到"就绪"信号或进程异常退出
    private static void WaitForReadySignal(Process proc, Action<string>? onProgress)
    {
        var stderrLines = new List<string>();
        string? line;

        // 逐行读取 stderr
        while ((line = proc.StandardError.ReadLine()) != null)
        {
            stderrLines.Add(line);

            // manga-ocr 启动完成后会输出一行包含"就绪"的消息
            if (line.Contains("就绪")) break;

            // 回调通知上层（显示启动进度）
            onProgress?.Invoke(line.Trim());
        }

        // 若在读到就绪信号前进程已退出，说明启动失败
        if (proc.HasExited)
        {
            proc.Dispose();

            // 将 SharedProcess 置 null 避免外部认为进程还在
            SharedProcess = null;

            // 取前500字符作为错误详情
            string detail = stderrLines.Count > 0
                ? string.Join("\n", stderrLines)
                : "(无 stderr 输出)";
            throw new InvalidOperationException(
                $"manga-ocr 进程启动失败:\n{detail[..Math.Min(detail.Length, 500)]}");
        }
    }

    // 后台线程：持续读取 stderr 直到进程退出，防止管道缓冲区满造成死锁
    private static void DrainStderrBackground(Process proc)
    {
        Task.Run(() =>
        {
            try
            {
                // 持续读取直到流关闭或进程退出
                while (proc.StandardError.ReadLine() != null) { }
            }
            catch
            {
                // 忽略进程已退出时的读取异常
            }
        });
    }

    // 停止 manga-ocr 进程（安全关闭：先发关闭信号，等3秒，再强制Kill）
    public static void StopProcess()
    {
        lock (SharedLock)
        {
            if (SharedProcess == null) return;

            // 关闭标准输入（部分进程会据此优雅退出）
            try { SharedProcess.StandardInput.Close(); } catch { }

            // 等待最多3秒让进程自然退出
            try
            {
                if (!SharedProcess.HasExited)
                    SharedProcess.WaitForExit(3000);
            } catch { }

            // 超时未退则强制终止
            try
            {
                if (!SharedProcess.HasExited)
                    SharedProcess.Kill();
            } catch { }

            // 释放资源
            try { SharedProcess.Dispose(); } catch { }

            SharedProcess = null;
        }
    }

    // ================================================================
    // JSON 请求/响应协议
    // ================================================================

    // 通过 stdin/stdout 与 manga-ocr 进程通信
    // 请求：{ "image": "/path/to/img.png", "boxes": [[x,y,w,h], ...] }
    // 响应：{ "texts": ["识别文字1", "识别文字2", ...] }
    private static string? SendOcrRequest(string imagePath, int[][] boxes)
    {
        // 序列化请求为 JSON（紧凑格式）
        var req = JsonSerializer.Serialize(new { image = imagePath, boxes });

        lock (SharedLock)
        {
            // 写入 stdin 并 flush（确保 Python 端能立即读到）
            SharedProcess!.StandardInput.WriteLine(req);
            SharedProcess.StandardInput.Flush();

            // 读取一行响应（manga-ocr 每请求返回一行 JSON）
            return SharedProcess.StandardOutput.ReadLine();
        }
    }

    // ================================================================
    // 识别方法
    // ================================================================

    // 对截图 BitmapSource 执行日文 OCR，返回拼接文字（截图 OCR 模式用）
    public static Task<string?> RecognizeScreenshot(BitmapSource bitmap)
    {
        int w = bitmap.PixelWidth, h = bitmap.PixelHeight;
        if (SharedProcess == null || SharedProcess.HasExited)
            return Task.FromResult<string?>(null);

        return OcrPipeline.WithTempPngAsync(bitmap, tmpPath => Task.Run(() =>
        {
            var respLine = SendOcrRequest(tmpPath, [[0, 0, w, h]]);
            if (respLine == null) return null;

            var resp = JsonSerializer.Deserialize<MangaOcrResponse>(respLine)!;
            return resp.texts is { Length: > 0 } ? resp.texts[0] : null;
        }));
    }

    // 对指定图片文件执行完整日文 OCR：检测 → 合并 → 发给 manga-ocr 识别 → 返回结果
    public Task<IReadOnlyList<OcrTextRegion>> RecognizeAsync(
        string imagePath,
        OcrModelInfo model,       // 实际使用 model.DirectoryPath 定位检测模型
        AutoOcrOptions options,
        CancellationToken ct)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            using var bitmap = SKBitmap.Decode(imagePath)
                ?? throw new InvalidOperationException($"Cannot decode: {imagePath}");

            EnsureDetEngine(model);

            if (SharedProcess == null || SharedProcess.HasExited)
                return Array.Empty<OcrTextRegion>();

            var rawRegions = PpOcrV5RapidOcrProvider.RunDetection(
                _detEngine!, bitmap, options.MinConfidence, ct);

            if (rawRegions.Count == 0) return Array.Empty<OcrTextRegion>();

            var imageSize = new Size(bitmap.Width, bitmap.Height);

            // 检测结果后处理：过滤噪点 + 合并相邻文本块
            var mergedRegions = OcrPipeline.BuildTextBlocks(
                rawRegions, imageSize, options, OcrPipeline.IsVerticalLayout(rawRegions));

            // 将合并后区域转换为 manga-ocr 请求所需的 boxes 格式
            var boxes = mergedRegions.Select(r =>
                new[]
                {
                    (int)r.Bounds.Left,
                    (int)r.Bounds.Top,
                    (int)r.Bounds.Width,
                    (int)r.Bounds.Height
                }).ToArray();

            // 第二步：将检测框发给 manga-ocr Python 进程做精确识别
            var respLine = SendOcrRequest(imagePath, boxes);

            if (respLine == null)
                throw new InvalidOperationException("manga-ocr process unresponsive");

            // 反序列化识别结果
            var texts = JsonSerializer
                .Deserialize<MangaOcrResponse>(respLine)!
                .texts ?? [];

            // 第三步：将识别结果与对应检测框合并，生成最终 OcrTextRegion
            // manga-ocr 返回顺序与请求 boxes 顺序一一对应
            var results = new List<OcrTextRegion>();
            for (int i = 0; i < mergedRegions.Count; i++)
                results.Add(new OcrTextRegion(
                    i < texts.Length ? texts[i] : "", // 文字为空表示该区域无文字
                    mergedRegions[i].Bounds,
                    1.0)); // manga-ocr 不返回置信度，固定 1.0

            return (IReadOnlyList<OcrTextRegion>)results;
        }, ct);
    }

    // ================================================================
    // 检测引擎（PaddleOCR 检测 + manga-ocr 识别）
    // ================================================================

    // 懒加载检测引擎：从 model 所在目录查找 PpOcrV5 检测模型并初始化
    private void EnsureDetEngine(OcrModelInfo model)
    {
        // 已初始化则跳过
        if (_detEngine != null) return;

        // model.json 位于 models/manga-ocr/ 目录
        // 检测模型（PpOcrV5）放在 models/v5/ 子目录
        string modelDir = Path.GetDirectoryName(model.DirectoryPath)!;

        // 在 modelDir 的子目录中查找 PpOcrV5RapidOcr 引擎的模型
        var detInfo = Directory.EnumerateDirectories(modelDir)
            .Select(OcrModelInfo.TryRead)
            .FirstOrDefault(m =>
                m is { Engine: PpOcrV5RapidOcrProvider.EngineName })
            ?? throw new InvalidOperationException(
                "未找到 PaddleOCR 检测模型。manga-ocr 需要 ONNX 检测模型，"
                + "请检查 models/ 目录是否包含 PpOcrV5RapidOcr 模型。");

        // 用 PpOcrV5RapidOcrProvider 的工厂方法创建检测引擎
        _detEngine = PpOcrV5RapidOcrProvider.CreateEngine(detInfo);
    }

    // manga-ocr JSON 响应格式：{ "texts": ["文字1", "文字2", ...] }
    private sealed record MangaOcrResponse(string[]? texts);
}
