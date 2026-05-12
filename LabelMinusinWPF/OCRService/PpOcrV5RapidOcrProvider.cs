using System.IO;                    // 文件路径操作、临时文件读写
using System.Windows;                // Size、Point 等 UI 类型
using System.Windows.Media.Imaging;   // BitmapSource 截图类型
using RapidOcrNet;                   // RapidOcr .NET 封装
using SkiaSharp;                     // 跨平台图片解码

namespace LabelMinusinWPF.OCRService;

public class PpOcrV5RapidOcrProvider : IOcrProvider
{
    public const string EngineName = "PpOcrV5RapidOcr";

    public static bool CanHandleEngine(string engine) =>
        engine.Equals(EngineName, StringComparison.OrdinalIgnoreCase) ||
        engine.Equals("RapidOcrNet", StringComparison.OrdinalIgnoreCase) ||
        engine.Equals("PP-OCRv5", StringComparison.OrdinalIgnoreCase);

    // ================================================================
    // RapidOcr 引擎工厂（被实例方法和静态共享引擎共用）
    // ================================================================

    // 根据模型信息创建并初始化 RapidOcr 引擎实例
    internal static RapidOcr CreateEngine(OcrModelInfo model)
    {
        // 新建引擎对象
        var engine = new RapidOcr();
        // 传入四个模型文件路径：检测、方向分类、识别、字典
        engine.InitModels(
            model.GetFilePath("detModel")!,
            model.GetFilePath("clsModel")!,
            model.GetFilePath("recModel")!,
            model.GetFilePath("dict")!);
        return engine;
    }

    // ================================================================
    // 核心检测方法（统一三种调用路径）
    // ================================================================

    // 对单张图片执行文字检测，返回文字区域列表（SKBitmap 重载，MangaOcr 共享用）
    internal static IReadOnlyList<OcrTextRegion> RunDetection(
        RapidOcr engine, SKBitmap bitmap, double minConfidence, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var result = engine.Detect(bitmap, RapidOcrOptions.Default with
        {
            BoxScoreThresh = (float)minConfidence,
            DoAngle = true
        });
        return result.TextBlocks
            .Select(b => new OcrTextRegion(b.GetText(), OcrPipeline.BlockToRect(b), b.BoxScore))
            .Where(r => r.Confidence >= minConfidence)
            .ToList();
    }

    // 对单张图片文件执行文字检测
    private static IReadOnlyList<OcrTextRegion> RunDetection(
        RapidOcr engine, string imagePath, double minConfidence, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var bitmap = SKBitmap.Decode(imagePath)
            ?? throw new InvalidOperationException($"无法读取图片：{imagePath}");
        return RunDetection(engine, bitmap, minConfidence, ct);
    }

    // ================================================================
    // 共享引擎 — 截图 OCR / 批量识别复用
    // ================================================================

    // 共享引擎的线程安全锁
    private static readonly object SharedLock = new();

    // 全局共享的 RapidOcr 引擎实例（截图 OCR 和批量识别共用）
    private static RapidOcr? SharedEngine;

    public static bool IsSharedEngineReady
    {
        get { lock (SharedLock) return SharedEngine != null; }
    }

    // 初始化共享引擎（在 OCR 开关打开时调用一次）
    public static void InitSharedEngine(OcrModelInfo? model = null)
    {
        lock (SharedLock)
        {
            // 自动查找 models/ 目录下的 PaddleOCR 模型
            model ??= OcrPipeline.FindPaddleModel()
                ?? throw new InvalidOperationException("未找到 PaddleOCR 模型");

            // 释放旧引擎（如有）
            SharedEngine?.Dispose();

            // 创建并持有新引擎
            SharedEngine = CreateEngine(model);
        }
    }

    public static void EnsureSharedEngine(OcrModelInfo? model = null)
    {
        lock (SharedLock)
        {
            if (SharedEngine != null) return;

            model ??= OcrPipeline.FindPaddleModel()
                ?? throw new InvalidOperationException("未找到 PaddleOCR 模型");
            SharedEngine = CreateEngine(model);
        }
    }

    // 通过共享引擎执行识别（接收任意 model 参数但实际使用共享引擎）
    public static Task<IReadOnlyList<OcrTextRegion>> RecognizeWithSharedEngine(
        string imagePath, OcrModelInfo _, AutoOcrOptions options, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            // 线程安全地获取共享引擎引用
            RapidOcr? engine;
            lock (SharedLock) { engine = SharedEngine; }

            // 引擎未初始化（OCR 开关未开）则返回空
            if (engine == null)
                throw new InvalidOperationException("共享引擎未初始化，请先打开 OCR 开关");

            // 执行检测
            return RunDetection(engine, imagePath, options.MinConfidence, ct);
        }, ct);
    }

    // 静态单例，供 OcrPipeline.RunAsync 直接使用
    public static readonly PpOcrV5RapidOcrProvider Shared = new();

    Task<IReadOnlyList<OcrTextRegion>> IOcrProvider.RecognizeAsync(
        string imagePath, OcrModelInfo model, AutoOcrOptions options, CancellationToken ct)
        => RecognizeWithSharedEngine(imagePath, model, options, ct);

    // 释放共享引擎（在 OCR 开关关闭或程序退出时调用）
    public static void DisposeSharedEngine()
    {
        lock (SharedLock)
        {
            // Dispose 并置空
            SharedEngine?.Dispose();
            SharedEngine = null;
        }
    }

    // 对截图 BitmapSource 执行 OCR，返回拼接后的文字字符串（截图 OCR 用）
    public static Task<string?> RecognizeScreenshot(BitmapSource bitmap)
    {
        RapidOcr? engine;
        lock (SharedLock) { engine = SharedEngine; }
        if (engine == null) return Task.FromResult<string?>(null);

        return OcrPipeline.WithTempPngAsync(bitmap, tmpPath => Task.Run(() =>
        {
            using var skBitmap = SKBitmap.Decode(tmpPath);
            if (skBitmap == null) return (string?)null;

            var result = engine.Detect(skBitmap, RapidOcrOptions.Default with
            {
                BoxScoreThresh = 0.4f,
                DoAngle = true
            });

            return string.Join("",
                result.TextBlocks
                    .Select(b => b.GetText())
                    .Where(t => !string.IsNullOrWhiteSpace(t)));
        }));
    }
}
