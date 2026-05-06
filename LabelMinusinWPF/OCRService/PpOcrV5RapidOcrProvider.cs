using System.IO;                    // 文件路径操作、临时文件读写
using System.Windows;                // Size、Point 等 UI 类型
using System.Windows.Media.Imaging;   // BitmapSource 截图类型
using RapidOcrNet;                   // RapidOcr .NET 封装
using SkiaSharp;                     // 跨平台图片解码

namespace LabelMinusinWPF.OCRService;

public sealed class PpOcrV5RapidOcrProvider : IOcrProvider
{
    // 引擎名称标识，用于 OcrModelInfo.Engine 字段匹配
    public const string EngineName = "PpOcrV5RapidOcr";

    // 当前实例关联的 RapidOcr 引擎（实例模式下一键打点用）
    private RapidOcr? _engine;

    // 当前实例已加载模型的 manifest 路径，用于判断是否需要重新加载
    private string? _loadedModelKey;

    // 判断给定引擎名称是否可由本类处理（支持多种命名形式）
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

    // 对单张图片执行文字检测（仅检测，不做识别），返回文字区域列表
    private static IReadOnlyList<OcrTextRegion> RunDetection(
        RapidOcr engine, string imagePath, double minConfidence, CancellationToken ct)
    {
        // 检查取消信号
        ct.ThrowIfCancellationRequested();

        // 从文件路径解码图片为 SKBitmap
        using var bitmap = SKBitmap.Decode(imagePath)
            ?? throw new InvalidOperationException($"无法读取图片：{imagePath}");

        // 调用 RapidOcr 检测：只做检测不做角度分类（DoAngle=true 同时修正文字方向）
        var result = engine.Detect(bitmap, RapidOcrOptions.Default with
        {
            // 最低置信度阈值，过低块会被过滤
            BoxScoreThresh = (float)minConfidence,
            // 启用文字方向检测（竖排日文需此参数）
            DoAngle = true
        });

        // 将检测结果 TextBlock 转换为内部 OcrTextRegion 格式
        // 同时按 minConfidence 二次过滤（RapidOcr 内部返回结果可能不完全准确）
        return result.TextBlocks
            .Select(b => new OcrTextRegion(b.GetText(), OcrPipeline.BlockToRect(b), b.BoxScore))
            .Where(r => r.Confidence >= minConfidence)
            .ToList();
    }

    // ================================================================
    // 实例方法 — 一键打点用
    // ================================================================

    // 一键打点时通过实例方法调用，每次处理一张图片
    public Task<IReadOnlyList<OcrTextRegion>> RecognizeAsync(
        string imagePath, OcrModelInfo model, AutoOcrOptions options, CancellationToken ct)
    {
        // 在后台线程执行检测，避免阻塞 UI
        return Task.Run(
            () => RunDetection(EnsureEngine(model), imagePath, options.MinConfidence, ct),
            ct);
    }

    // 确保实例已加载对应模型的引擎，避免重复创建（懒加载）
    private RapidOcr EnsureEngine(OcrModelInfo model)
    {
        // 以 manifest 路径作为缓存 key
        string key = model.ManifestPath;

        // 命中缓存且引擎未 Dispose，直接返回
        if (_engine != null && _loadedModelKey == key)
            return _engine;

        // 旧引擎存在则释放
        _engine?.Dispose();

        // 创建并缓存新引擎
        _engine = CreateEngine(model);
        _loadedModelKey = key;
        return _engine;
    }

    // ================================================================
    // 共享引擎 — 截图 OCR / 批量识别复用
    // ================================================================

    // 共享引擎的线程安全锁
    private static readonly object SharedLock = new();

    // 全局共享的 RapidOcr 引擎实例（截图 OCR 和批量识别共用）
    private static RapidOcr? SharedEngine;

    // 初始化共享引擎（在 OCR 开关打开时调用一次）
    public static void InitSharedEngine()
    {
        lock (SharedLock)
        {
            // 自动查找 models/ 目录下的 PaddleOCR 模型
            var model = OcrPipeline.FindPaddleModel()
                ?? throw new InvalidOperationException("未找到 PaddleOCR 模型");

            // 释放旧引擎（如有）
            SharedEngine?.Dispose();

            // 创建并持有新引擎
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

    // 静态共享 Provider 实例，供 OcrPipeline.RunAsync 直接使用
    public static readonly IOcrProvider Shared = new SharedProvider();

    // SharedProvider：实现 IOcrProvider 接口，内部委托给 RecognizeWithSharedEngine
    private sealed class SharedProvider : IOcrProvider
    {
        public Task<IReadOnlyList<OcrTextRegion>> RecognizeAsync(
            string imagePath, OcrModelInfo model, AutoOcrOptions options, CancellationToken ct)
            => RecognizeWithSharedEngine(imagePath, model, options, ct);
    }

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
        return Task.Run(() =>
        {
            // 安全获取共享引擎
            RapidOcr? engine;
            lock (SharedLock) { engine = SharedEngine; }

            // 引擎未启动则返回空
            if (engine == null) return null;

            // 将截图 BitmapSource 保存为临时 PNG 文件（引擎需文件路径）
            string tmpPath = OcrUIActions.SaveBitmapToTempPng(bitmap);

            try
            {
                // 用 SkiaSharp 解码临时 PNG
                using var skBitmap = SKBitmap.Decode(tmpPath);

                // 解码失败时清理临时文件并返回空
                if (skBitmap == null)
                {
                    try { File.Delete(tmpPath); } catch { }
                    return null;
                }

                // 调用 RapidOcr 检测（使用较低阈值 0.4 适合截图场景）
                var result = engine.Detect(skBitmap, RapidOcrOptions.Default with
                {
                    BoxScoreThresh = 0.4f,
                    DoAngle = true
                });

                // 提取所有文字块并拼接，滤除空白
                return string.Join("",
                    result.TextBlocks
                        .Select(b => b.GetText())
                        .Where(t => !string.IsNullOrWhiteSpace(t)));
            }
            finally
            {
                // 无论成功与否，清理临时 PNG 文件
                try { File.Delete(tmpPath); } catch { }
            }
        });
    }
}
