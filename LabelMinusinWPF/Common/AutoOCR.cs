using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Diagnostics;
using RapidOcrNet;
using SharpCompress.Archives;
using SkiaSharp;
using GroupConstants = LabelMinusinWPF.Common.GroupConstants;

namespace LabelMinusinWPF.Common;

// ============================================================================
// OcrPipeline — OCR 管线：模型发现 → 识别 → 后处理 → 创建 Label
// ============================================================================

public static class OcrPipeline
{
    public static string DefaultModelRoot => Path.Combine(AppContext.BaseDirectory, "models");

    /// <summary>扫描模型根目录，读取所有有效的 OCR 模型配置</summary>
    public static IReadOnlyList<OcrModelInfo> ScanModels(string? modelRoot = null)
    {
        var root = modelRoot ?? DefaultModelRoot;
        if (!Directory.Exists(root))
            return [];

        return Directory.EnumerateDirectories(root)
            .Select(OcrModelInfo.TryRead)
            .Where(model => model != null)
            .Select(model => model!)
            .OrderBy(model => model.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>执行完整 OCR 管线：识别 → 合并 → 去重 → 排序 → 创建标签</summary>
    public static async Task<AutoOcrResult> RunAsync(
        OneProject project,
        OcrModelInfo model,
        AutoOcrOptions options,
        Func<string, OcrModelInfo, AutoOcrOptions, CancellationToken,
             Task<IReadOnlyList<OcrTextRegion>>> recognizeAsync,
        IProgress<string> progress,
        IReadOnlyList<OneImage>? images = null,
        CancellationToken cancellationToken = default)
    {
        if (project.ImageList.Count == 0)
            return AutoOcrResult.Failed("当前项目没有可 OCR 的图片");

        int processedImages = 0;
        int createdLabels = 0;
        var imageList = images ?? project.ImageList;
        int totalCount = imageList.Count;

        foreach (var image in imageList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string imagePath = PrepareImagePath(image);
            var imageSize = GetImageSize(imagePath);

            var regions = await recognizeAsync(imagePath, model, options, cancellationToken);

            var blocks = BuildTextBlocks(regions, imageSize, options);
            var deduped = DeduplicateRegions(blocks, imageSize, options);

            int imageLabelIndex = 0;
            foreach (var block in SortRegions(deduped, options.RightToLeft))
            {
                imageLabelIndex++;
                Point position = ToRelativeTopRight(block.Bounds, imageSize);
                string text = options.OutputMode == OcrOutputMode.PositionOnly
                    ? $"Label{imageLabelIndex}"
                    : block.Text.Trim();

                if (string.IsNullOrWhiteSpace(text))
                    text = $"Label{imageLabelIndex}";

                var label = new OneLabel(text, GroupConstants.InBox, position);
                image.History.Execute(new AddCommand(image.Labels, label));
                createdLabels++;
            }

            processedImages++;
            Application.Current.Dispatcher.Invoke(() =>
                project.MsgQueue.Enqueue(
                    $"OCR 进度：{processedImages}/{totalCount} — {image.ImageName}"));
        }

        return AutoOcrResult.Succeeded(
            processedImages, createdLabels,
            $"OCR 完成：处理 {processedImages} 张图片，新增 {createdLabels} 个标签");
    }

    // ========================================================================
    // 后处理管线 — 过滤 → 合并 → 去重 → 排序
    // ========================================================================

    /// <summary>过滤无效区域并迭代合并相邻文本块</summary>
    internal static IReadOnlyList<OcrTextRegion> BuildTextBlocks(
        IReadOnlyList<OcrTextRegion> regions, Size imageSize, AutoOcrOptions options)
    {
        var candidates = regions
            .Where(r => IsUsefulRegion(r, imageSize, options))
            .Select(r => new TextRegionGroup(r))
            .ToList();

        if (!options.MergeTextLines || candidates.Count == 0)
            return candidates.Select(g => g.ToRegion()).ToList();

        bool merged;
        do
        {
            merged = false;
            for (int i = 0; i < candidates.Count && !merged; i++)
                for (int j = i + 1; j < candidates.Count; j++)
                    if (ShouldMerge(candidates[i].Bounds, candidates[j].Bounds, imageSize, options))
                    {
                        candidates[i].Add(candidates[j]);
                        candidates.RemoveAt(j);
                        merged = true;
                        break;
                    }
        } while (merged);

        return candidates.Select(g => g.ToRegion()).ToList();
    }

    /// <summary>检查区域是否足够大，用于过滤噪点</summary>
    private static bool IsUsefulRegion(OcrTextRegion r, Size imageSize, AutoOcrOptions o)
    {
        if (r.Bounds.Width <= 0 || r.Bounds.Height <= 0) return false;
        double areaRatio = r.Bounds.Width * r.Bounds.Height / Math.Max(1, imageSize.Width * imageSize.Height);
        double minSide = Math.Min(r.Bounds.Width, r.Bounds.Height);
        return areaRatio >= o.MinRegionAreaRatio && minSide >= o.MinRegionSide;
    }

    /// <summary>判断两个区域是否应合并：扩展后相交 或 中心距离小于阈值</summary>
    private static bool ShouldMerge(Rect a, Rect b, Size imageSize, AutoOcrOptions o)
    {
        var expanded = Expand(a, imageSize, o.MergePaddingRatio);
        if (expanded.IntersectsWith(b) && HasProjectionOverlap(a, b))
            return true;

        double dist = Distance(Center(a), Center(b));
        double maxDist = Math.Max(o.MergeMaxDistance,
            Math.Max((a.Width + b.Width) / 2, (a.Height + b.Height) / 2) * o.MergeDistanceScale);
        return dist <= maxDist;
    }

    /// <summary>检查两个矩形在 X 或 Y 方向是否有足够的投影重叠</summary>
    private static bool HasProjectionOverlap(Rect a, Rect b)
    {
        double xOverlap = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
        double yOverlap = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
        return xOverlap > -Math.Min(a.Width, b.Width) * 0.5
            || yOverlap > -Math.Min(a.Height, b.Height) * 0.5;
    }

    /// <summary>按图片最大边比例扩展矩形，用于邻近合并判断</summary>
    private static Rect Expand(Rect rect, Size imageSize, double ratio)
    {
        double padding = Math.Max(imageSize.Width, imageSize.Height) * ratio;
        return new Rect(rect.Left - padding, rect.Top - padding,
            rect.Width + padding * 2, rect.Height + padding * 2);
    }

    /// <summary>按置信度排序后去除中心点过近的重复区域</summary>
    private static IReadOnlyList<OcrTextRegion> DeduplicateRegions(
        IReadOnlyList<OcrTextRegion> regions, Size imageSize, AutoOcrOptions options)
    {
        if (!options.DeduplicateRegions || regions.Count <= 1)
            return regions;

        var sorted = regions.OrderByDescending(r => r.Confidence).ToList();
        var accepted = new List<OcrTextRegion>();
        var acceptedCenters = new List<Point>();

        foreach (var region in sorted)
        {
            Point center = ToRelativeCenter(region.Bounds, imageSize);
            if (!acceptedCenters.Any(ac => Distance(center, ac) <= options.DeduplicateDistance))
            {
                accepted.Add(region);
                acceptedCenters.Add(center);
            }
        }

        var acceptedSet = new HashSet<OcrTextRegion>(accepted);
        return regions.Where(r => acceptedSet.Contains(r)).ToList();
    }

    /// <summary>按阅读顺序排序：先按行分组，行内根据横排/竖排方向排列</summary>
    private static IReadOnlyList<OcrTextRegion> SortRegions(
        IReadOnlyList<OcrTextRegion> regions, bool rightToLeft)
    {
        if (regions.Count == 0) return [];

        double avgH = regions.Average(r => Math.Max(1, r.Bounds.Height));
        double rowTol = Math.Max(24, avgH * 0.8);
        var rows = new List<List<OcrTextRegion>>();

        foreach (var r in regions.OrderBy(r => r.Bounds.Top))
        {
            double cy = r.Bounds.Top + r.Bounds.Height / 2;
            var row = rows.FirstOrDefault(row =>
                Math.Abs(row.Average(x => x.Bounds.Top + x.Bounds.Height / 2) - cy) <= rowTol);
            if (row == null) { row = []; rows.Add(row); }
            row.Add(r);
        }

        return rows
            .OrderBy(row => row.Min(r => r.Bounds.Top))
            .SelectMany(row => rightToLeft
                ? row.OrderByDescending(r => r.Bounds.Left + r.Bounds.Width / 2)
                       .ThenBy(r => r.Bounds.Top)
                : row.OrderBy(r => r.Bounds.Left)
                       .ThenBy(r => r.Bounds.Top))
            .ToList();
    }

    private sealed class TextRegionGroup
    {
        private readonly List<OcrTextRegion> _regions;

        public TextRegionGroup(OcrTextRegion region) { _regions = [region]; Bounds = region.Bounds; }
        public Rect Bounds { get; private set; }

        public void Add(TextRegionGroup other)
        {
            _regions.AddRange(other._regions);
            Bounds = Union(Bounds, other.Bounds);
        }

        public OcrTextRegion ToRegion() => new(
            Text: string.Join("",
                _regions.OrderBy(r => r.Bounds.Top).ThenBy(r => r.Bounds.Left)
                    .Select(r => r.Text.Trim()).Where(t => !string.IsNullOrWhiteSpace(t))),
            Bounds: Bounds,
            Confidence: _regions.Count == 0 ? 0 : _regions.Average(r => r.Confidence));

        private static Rect Union(Rect a, Rect b) => new(
            Math.Min(a.Left, b.Left), Math.Min(a.Top, b.Top),
            Math.Max(a.Right, b.Right) - Math.Min(a.Left, b.Left),
            Math.Max(a.Bottom, b.Bottom) - Math.Min(a.Top, b.Top));
    }

    // ========================================================================
    // 文本块工具
    // ========================================================================

    /// <summary>将 RapidOcr 的 TextBlock 转换为 System.Windows.Rect</summary>
    internal static Rect BlockToRect(TextBlock block)
    {
        var pts = block.BoxPoints;
        int minX = pts.Min(p => p.X), minY = pts.Min(p => p.Y);
        int maxX = pts.Max(p => p.X), maxY = pts.Max(p => p.Y);
        return new Rect(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
    }

    // ========================================================================
    // 坐标计算
    // ========================================================================

    /// <summary>计算矩形中心点</summary>
    private static Point Center(Rect rect) =>
        new(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);

    /// <summary>计算两点之间的欧氏距离</summary>
    private static double Distance(Point a, Point b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>将矩形中心坐标转换为相对坐标 (0..1)，用于去重比较</summary>
    private static Point ToRelativeCenter(Rect bounds, Size imageSize) =>
        new(
            imageSize.Width <= 0 ? 0.5 : Clamp01((bounds.Left + bounds.Width / 2) / imageSize.Width),
            imageSize.Height <= 0 ? 0.5 : Clamp01((bounds.Top + bounds.Height / 2) / imageSize.Height));

    /// <summary>将矩形右上角坐标转换为相对坐标 (0..1)，用于 Label 定位</summary>
    private static Point ToRelativeTopRight(Rect bounds, Size imageSize) =>
        new(
            imageSize.Width <= 0 ? 0.5 : Clamp01(bounds.Right / imageSize.Width),
            imageSize.Height <= 0 ? 0.5 : Clamp01(bounds.Top / imageSize.Height));

    /// <summary>将值限制在 [0, 1] 区间</summary>
    private static double Clamp01(double v) => Math.Clamp(v, 0, 1);

    // ========================================================================
    // 图片工具
    // ========================================================================

    /// <summary>通过 SkiaSharp 解码图片获取宽高</summary>
    private static Size GetImageSize(string imagePath)
    {
        using var bitmap = SKBitmap.Decode(imagePath);
        if (bitmap == null)
            throw new InvalidOperationException($"无法读取图片尺寸：{imagePath}");
        return new Size(bitmap.Width, bitmap.Height);
    }

    /// <summary>解析图片路径（支持从压缩包提取到临时目录）</summary>
    private static string PrepareImagePath(OneImage image)
    {
        var archiveResult = ResourceHelper.ParseArchivePath(image.ImagePath);
        if (!archiveResult.HasValue)
            return image.ImagePath;

        var (archivePath, entryPath) = archiveResult.Value;
        if (!File.Exists(archivePath))
            throw new FileNotFoundException("找不到压缩包", archivePath);

        string tempRoot = Path.Combine(AppContext.BaseDirectory, Constants.TempFolders.OcrTemp, "AutoOCR");
        Directory.CreateDirectory(tempRoot);

        string ext = Path.GetExtension(entryPath);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".png";

        string hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(image.ImagePath)))[..12];
        string targetPath = Path.Combine(tempRoot,
            $"{Path.GetFileNameWithoutExtension(entryPath)}_{hash}{ext}");
        if (File.Exists(targetPath)) return targetPath;

        using var archive = ArchiveFactory.OpenArchive(archivePath);
        var entry = archive.Entries.FirstOrDefault(e =>
            !e.IsDirectory && e.Key is not null &&
            (e.Key.Equals(entryPath, StringComparison.OrdinalIgnoreCase) ||
             e.Key.EndsWith("/" + entryPath, StringComparison.OrdinalIgnoreCase) ||
             e.Key.EndsWith("\\" + entryPath, StringComparison.OrdinalIgnoreCase)));

        if (entry == null)
            throw new FileNotFoundException("压缩包中找不到图片", entryPath);

        using var fs = File.Create(targetPath);
        entry.WriteTo(fs);
        return targetPath;
    }
}

// ============================================================================
// PpOcrV5RapidOcrProvider — PaddleOCR 引擎（RapidOcrNet 封装）
// ============================================================================

public sealed class PpOcrV5RapidOcrProvider
{
    public const string EngineName = "PpOcrV5RapidOcr";
    private RapidOcr? _engine;
    private string? _loadedModelKey;

    /// <summary>判断引擎名是否匹配 PaddleOCR/RapidOcr 系列</summary>
    public static bool CanHandleEngine(string engine) =>
        engine.Equals(EngineName, StringComparison.OrdinalIgnoreCase) ||
        engine.Equals("RapidOcrNet", StringComparison.OrdinalIgnoreCase) ||
        engine.Equals("PP-OCRv5", StringComparison.OrdinalIgnoreCase);

    /// <summary>对单张图片执行 PaddleOCR 检测+识别</summary>
    public Task<IReadOnlyList<OcrTextRegion>> RecognizeAsync(
        string imagePath, OcrModelInfo model, AutoOcrOptions options, CancellationToken ct)
    {
        return Task.Run<IReadOnlyList<OcrTextRegion>>(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var bitmap = SKBitmap.Decode(imagePath)
                ?? throw new InvalidOperationException($"无法读取图片：{imagePath}");

            var engine = EnsureEngine(model);
            var result = engine.Detect(bitmap, RapidOcrOptions.Default with
            {
                BoxScoreThresh = (float)options.MinConfidence, DoAngle = true
            });

            return result.TextBlocks
                .Select(ToRegion)
                .Where(r => r.Confidence >= options.MinConfidence)
                .ToList();
        }, ct);
    }

    /// <summary>按需初始化或复用 RapidOcr 引擎实例</summary>
    private RapidOcr EnsureEngine(OcrModelInfo model)
    {
        string key = model.ManifestPath;
        if (_engine != null && _loadedModelKey == key) return _engine;

        _engine?.Dispose();
        _engine = new RapidOcr();
        _loadedModelKey = key;
        _engine.InitModels(
            model.GetFilePath("detModel")!, model.GetFilePath("clsModel")!,
            model.GetFilePath("recModel")!, model.GetFilePath("dict")!);
        return _engine;
    }

    // ================================================================
    // 共享引擎 — 截图 OCR 复用，Toggle ON 初始化，OFF 释放
    // ================================================================

    private static RapidOcr? SharedEngine;
    private static OcrModelInfo? SharedModel;

    /// <summary>初始化截图 OCR 共享引擎（OCR 开关打开时调用）</summary>
    public static void InitSharedEngine()
    {
        var model = OcrPipeline.ScanModels()
            .FirstOrDefault(m => CanHandleEngine(m.Engine));
        if (model == null)
            throw new InvalidOperationException("未找到 PaddleOCR 模型");
        SharedModel = model;
        SharedEngine = new RapidOcr();
        SharedEngine.InitModels(
            model.GetFilePath("detModel")!, model.GetFilePath("clsModel")!,
            model.GetFilePath("recModel")!, model.GetFilePath("dict")!);
    }

    /// <summary>使用共享引擎对单张图片执行 OCR（用于截图识别场景）</summary>
    public static Task<IReadOnlyList<OcrTextRegion>> RecognizeWithSharedEngine(
        string imagePath, OcrModelInfo _, AutoOcrOptions options, CancellationToken ct)
    {
        return Task.Run<IReadOnlyList<OcrTextRegion>>(() =>
        {
            ct.ThrowIfCancellationRequested();
            if (SharedEngine == null)
                throw new InvalidOperationException("共享引擎未初始化，请先打开 OCR 开关");
            using var bitmap = SKBitmap.Decode(imagePath)
                ?? throw new InvalidOperationException($"无法读取图片：{imagePath}");
            var result = SharedEngine.Detect(bitmap, RapidOcrOptions.Default with
            {
                BoxScoreThresh = (float)options.MinConfidence, DoAngle = true
            });
            return result.TextBlocks
                .Select(ToRegion)
                .Where(r => r.Confidence >= options.MinConfidence)
                .ToList();
        }, ct);
    }

    /// <summary>释放共享引擎资源（OCR 开关关闭时调用）</summary>
    public static void DisposeSharedEngine()
    {
        SharedEngine?.Dispose();
        SharedEngine = null;
        SharedModel = null;
    }

    /// <summary>识别 WPF 截图并返回合并后的文本（PaddleOCR 引擎）</summary>
    public static Task<string?> RecognizeScreenshot(BitmapSource bitmap)
    {
        return Task.Run(() =>
        {
            if (SharedEngine == null) return null;
            string tmpDir = Path.Combine(AppContext.BaseDirectory, Constants.TempFolders.OcrTemp);
            Directory.CreateDirectory(tmpDir);
            string tmpPath = Path.Combine(tmpDir, $"ocr_{Guid.NewGuid():N}.png");

            Application.Current.Dispatcher.Invoke(() =>
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var fs = new FileStream(tmpPath, FileMode.Create);
                encoder.Save(fs);
            });

            try
            {
                using var skBitmap = SKBitmap.Decode(tmpPath);
                if (skBitmap == null) return null;
                var detResult = SharedEngine.Detect(skBitmap, RapidOcrOptions.Default with
                {
                    BoxScoreThresh = 0.4f, DoAngle = true
                });
                var texts = detResult.TextBlocks
                    .Select(b => b.GetText())
                    .Where(t => !string.IsNullOrWhiteSpace(t));
                return string.Join("", texts);
            }
            finally { try { File.Delete(tmpPath); } catch { } }
        });
    }

    /// <summary>将 RapidOcr 检测结果转为统一的 OcrTextRegion</summary>
    private static OcrTextRegion ToRegion(TextBlock block) =>
        new(block.GetText(), OcrPipeline.BlockToRect(block), block.BoxScore);
}

// ============================================================================
// MangaOcrProvider — 日漫 OCR（PaddleOCR 检测 + manga-ocr 识别）
// ============================================================================

public sealed class MangaOcrProvider
{
    public const string EngineName = "MangaOcr";
    public static Process? SharedProcess;
    public static readonly object SharedLock = new();

    private RapidOcr? _detEngine;

    // ================================================================
    // 进程生命周期
    // ================================================================

    /// <summary>启动 manga-ocr Python 持久进程，等待就绪信号</summary>
    public static Task StartProcessAsync(Action<string>? onProgress = null)
    {
        return Task.Run(() =>
        {
            lock (SharedLock)
            {
                if (SharedProcess != null) return;

                string pythonExe = Path.Combine(AppContext.BaseDirectory, "python", "python.exe");
                if (!File.Exists(pythonExe))
                    throw new InvalidOperationException(
                        "Python 环境未安装，请先通过 [高级 -> 配置 OCR 环境] 安装");

                var scriptPath = Path.Combine(AppContext.BaseDirectory, "models", "manga-ocr", "manga_ocr_infer.py");
                if (!File.Exists(scriptPath))
                    throw new InvalidOperationException(
                        $"manga-ocr 脚本不存在: {scriptPath}");

                var modelDir = Path.Combine(AppContext.BaseDirectory, "models", "manga-ocr", "model");
                if (!Directory.Exists(modelDir) || !Directory.EnumerateFiles(modelDir).Any())
                    throw new InvalidOperationException(
                        "manga-ocr 模型未下载，请通过 [OCR → 配置 OCR 环境] 安装");

                var psi = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = $"\"{scriptPath}\"",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                SharedProcess = Process.Start(psi);
                if (SharedProcess == null) return;

                // 用局部引用捕获当前进程，防止 Stop→Start 后旧 consumer 读到新进程
                var proc = SharedProcess;

                // 收集 stderr 直到"就绪"，保留全部输出用于失败诊断
                var stderrLines = new List<string>();
                string? line;
                while ((line = proc.StandardError.ReadLine()) != null)
                {
                    stderrLines.Add(line);
                    if (line.Contains("就绪")) break;
                    onProgress?.Invoke(line.Trim());
                }

                // 进程在输出"就绪"前退出了
                if (proc.HasExited)
                {
                    proc.Dispose();
                    SharedProcess = null;
                    string detail = stderrLines.Count > 0
                        ? string.Join("\n", stderrLines)
                        : "(无 stderr 输出)";
                    throw new InvalidOperationException(
                        $"manga-ocr 进程启动失败:\n{detail[..Math.Min(detail.Length, 500)]}");
                }

                // 后台持续消费 stderr，用局部引用而非 SharedProcess 字段
                Task.Run(() =>
                {
                    try
                    {
                        while (proc.StandardError.ReadLine() != null) { }
                    }
                    catch { }
                });
            }
        });
    }

    /// <summary>关闭 manga-ocr 进程：先优雅关闭 stdin，超时后强制终止</summary>
    public static void StopProcess()
    {
        lock (SharedLock)
        {
            if (SharedProcess == null) return;
            try { SharedProcess.StandardInput.Close(); } catch { }
            try { if (!SharedProcess.HasExited) SharedProcess.WaitForExit(3000); } catch { }
            try { if (!SharedProcess.HasExited) SharedProcess.Kill(); } catch { }
            try { SharedProcess.Dispose(); } catch { }
            SharedProcess = null;
        }
    }

    /// <summary>识别 WPF 截图并返回 manga-ocr 识别文本（通过持久进程）</summary>
    public static Task<string?> RecognizeScreenshot(BitmapSource bitmap)
    {
        int w = bitmap.PixelWidth;
        int h = bitmap.PixelHeight;
        return Task.Run(() =>
        {
            if (SharedProcess == null || SharedProcess.HasExited) return null;
            string tmpDir = Path.Combine(AppContext.BaseDirectory, Constants.TempFolders.OcrTemp);
            Directory.CreateDirectory(tmpDir);
            string tmpPath = Path.Combine(tmpDir, $"ocr_{Guid.NewGuid():N}.png");

            Application.Current.Dispatcher.Invoke(() =>
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var fs = new FileStream(tmpPath, FileMode.Create);
                encoder.Save(fs);
            });

            var req = JsonSerializer.Serialize(new
            {
                image = tmpPath,
                boxes = new[] { new[] { 0, 0, w, h } }
            });

            string? result = null;
            lock (SharedLock)
            {
                SharedProcess.StandardInput.WriteLine(req);
                SharedProcess.StandardInput.Flush();
                var respLine = SharedProcess.StandardOutput.ReadLine();
                if (respLine != null)
                {
                    var resp = JsonSerializer.Deserialize<MangaOcrResponse>(respLine)!;
                    result = resp.texts is { Length: > 0 } ? resp.texts[0] : null;
                }
            }

            try { File.Delete(tmpPath); } catch { }
            return result;
        });
    }

    /// <summary>先用 PaddleOCR 检测文本框，再交由 manga-ocr 进程识别文本</summary>
    public Task<IReadOnlyList<OcrTextRegion>> RecognizeAsync(
        string imagePath, OcrModelInfo model, AutoOcrOptions options, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var bitmap = SKBitmap.Decode(imagePath)
                ?? throw new InvalidOperationException($"Cannot decode: {imagePath}");

            EnsureDetEngine(model);
            if (SharedProcess is null or { HasExited: true }) return Array.Empty<OcrTextRegion>();

            var blocks = _detEngine.Detect(bitmap, RapidOcrOptions.Default with
            {
                BoxScoreThresh = (float)options.MinConfidence, DoAngle = true
            }).TextBlocks.Where(b => b.BoxScore >= options.MinConfidence).ToList();

            if (blocks.Count == 0) return Array.Empty<OcrTextRegion>();

            var imageSize = new Size(bitmap.Width, bitmap.Height);
            var rawRegions = blocks.Select(b =>
                new OcrTextRegion(b.GetText(), OcrPipeline.BlockToRect(b), b.BoxScore)).ToList();

            var mergedRegions = OcrPipeline.BuildTextBlocks(rawRegions, imageSize, options);

            var boxes = mergedRegions.Select(r =>
                new[] { (int)r.Bounds.Left, (int)r.Bounds.Top, (int)r.Bounds.Width, (int)r.Bounds.Height }).ToArray();

            var req = JsonSerializer.Serialize(new { image = imagePath, boxes });
            string? respLine;
            lock (SharedLock)
            {
                SharedProcess.StandardInput.WriteLine(req);
                SharedProcess.StandardInput.Flush();
                respLine = SharedProcess.StandardOutput.ReadLine();
            }

            if (respLine == null)
                throw new InvalidOperationException("manga-ocr process unresponsive");

            var texts = JsonSerializer.Deserialize<MangaOcrResponse>(respLine)!.texts ?? [];

            var results = new List<OcrTextRegion>();
            for (int i = 0; i < mergedRegions.Count; i++)
            {
                string text = i < texts.Length ? texts[i] : "";
                results.Add(new OcrTextRegion(text, mergedRegions[i].Bounds, 1.0));
            }

            return (IReadOnlyList<OcrTextRegion>)results;
        }, ct);
    }

    /// <summary>按需初始化 PaddleOCR 检测引擎，用于 manga-ocr 的文本检测阶段</summary>
    private void EnsureDetEngine(OcrModelInfo model)
    {
        if (_detEngine != null) return;

        string modelDir = Path.GetDirectoryName(model.DirectoryPath)!;
        var detInfo = Directory.EnumerateDirectories(modelDir)
            .Select(OcrModelInfo.TryRead)
            .FirstOrDefault(m => m is { Engine: PpOcrV5RapidOcrProvider.EngineName });

        if (detInfo == null)
            throw new InvalidOperationException(
                "未找到 PaddleOCR 检测模型。manga-ocr 需要 ONNX 检测模型，" +
                "请检查 models/ 目录是否包含 PpOcrV5RapidOcr 模型。");

        _detEngine = new RapidOcr();
        _detEngine.InitModels(
            detInfo.GetFilePath("detModel")!, detInfo.GetFilePath("clsModel")!,
            detInfo.GetFilePath("recModel")!, detInfo.GetFilePath("dict")!);
    }

    private sealed record MangaOcrResponse(string[]? texts);
}

// ============================================================================
// 数据模型
// ============================================================================

public sealed record OcrModelInfo(
    string Name, string Engine, string DirectoryPath, string ManifestPath,
    IReadOnlyDictionary<string, string> Files)
{
    /// <summary>根据 key 获取模型文件的完整路径</summary>
    public string? GetFilePath(string key) =>
        Files.TryGetValue(key, out var path) && !string.IsNullOrWhiteSpace(path)
            ? Path.Combine(DirectoryPath, path) : null;

    /// <summary>尝试从目录中读取 model.json 并解析为 OcrModelInfo</summary>
    internal static OcrModelInfo? TryRead(string modelDirectory)
    {
        string manifestPath = Path.Combine(modelDirectory, "model.json");
        if (!File.Exists(manifestPath))
            return null;

        try
        {
            using var stream = File.OpenRead(manifestPath);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            string name = GetString(root, "name") ?? GetString(root, "id") ?? Path.GetFileName(modelDirectory);
            string engine = GetString(root, "engine") ?? "";

            return new OcrModelInfo(name, engine, modelDirectory, manifestPath, ReadFiles(root));
        }
        catch { return null; }
    }

    /// <summary>从 JSON 根元素中提取除元数据外的所有文件路径字段</summary>
    private static Dictionary<string, string> ReadFiles(JsonElement root)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String) continue;
            if (property.Name is not ("id" or "name" or "engine" or "language"))
                files[property.Name] = property.Value.GetString() ?? "";
        }
        return files;
    }

    /// <summary>安全读取 JSON 对象的字符串属性</summary>
    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() : null;
}

public enum OcrOutputMode { RecognizedText, PositionOnly }

public sealed record AutoOcrOptions(
    double MinConfidence = 0.5,
    OcrOutputMode OutputMode = OcrOutputMode.RecognizedText,
    bool MergeTextLines = false,
    bool RightToLeft = false,
    double MinRegionAreaRatio = 0.00002,
    double MinRegionSide = 4,
    double MergePaddingRatio = 0.025,
    double MergeMaxDistance = 72,
    double MergeDistanceScale = 2.2,
    bool DeduplicateRegions = true,
    double DeduplicateDistance = 0.003)
{
    public static AutoOcrOptions JapaneseManga { get; } = new(
        MinConfidence: 0.4,
        OutputMode: OcrOutputMode.PositionOnly,
        MergeTextLines: true,
        RightToLeft: true,
        MergePaddingRatio: 0.008, MergeMaxDistance: 16, MergeDistanceScale: 0.6,
        DeduplicateRegions: true, DeduplicateDistance: 0.002);

    public static AutoOcrOptions ChineseEnglish { get; } = new(
        MinConfidence: 0.4,
        OutputMode: OcrOutputMode.RecognizedText,
        MergeTextLines: true,
        RightToLeft: true,
        MergePaddingRatio: 0.008, MergeMaxDistance: 16, MergeDistanceScale: 0.6,
        DeduplicateRegions: true, DeduplicateDistance: 0.002);
}

public sealed record OcrTextRegion(string Text, Rect Bounds, double Confidence);

public sealed record AutoOcrResult(bool Success, int ImageCount, int LabelCount, string Message)
{
    /// <summary>创建 OCR 成功结果</summary>
    public static AutoOcrResult Succeeded(int n, int l, string m) => new(true, n, l, m);
    /// <summary>创建 OCR 失败结果</summary>
    public static AutoOcrResult Failed(string m) => new(false, 0, 0, m);
}

// ============================================================================
// OcrEnvironment — 统一 OCR 环境检查
// ============================================================================

//TODO:先检查自带环境，不存在再检查系统manga-ocr环境
public static class OcrEnvironment
{
    private static string PythonExe => Path.Combine(AppContext.BaseDirectory, "python", "python.exe");
    private static string MangaOcrScript => Path.Combine(AppContext.BaseDirectory, "models", "manga-ocr", "manga_ocr_infer.py");

    public static bool IsPythonInstalled => File.Exists(PythonExe);
    public static bool IsMangaOcrScriptReady => File.Exists(MangaOcrScript);
    public static bool IsMangaOcrModelReady =>
        Directory.Exists(Path.Combine(AppContext.BaseDirectory, "models", "manga-ocr", "model")) &&
        Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "models", "manga-ocr", "model")).Any();
    public static bool HasOnnxModels =>
        OcrPipeline.ScanModels().Any(m => PpOcrV5RapidOcrProvider.CanHandleEngine(m.Engine));
    public static bool IsMangaOcrRunning => MangaOcrProvider.SharedProcess != null;

    public static bool ReadyForAutoDot => HasOnnxModels;
    public static bool ReadyForProcessStart => IsPythonInstalled && IsMangaOcrScriptReady && IsMangaOcrModelReady;
    public static bool ReadyForAutoOcr => ReadyForAutoDot && ReadyForProcessStart;
    public static bool ReadyForScreenshotOcr => ReadyForProcessStart && IsMangaOcrRunning;

    /// <summary>获取当前 OCR 环境的就绪状态描述</summary>
    public static string GetSummary()
    {
        bool py = IsPythonInstalled;
        bool onnx = HasOnnxModels;
        bool script = IsMangaOcrScriptReady;
        bool running = IsMangaOcrRunning;

        if (!onnx && !py) return "OCR 环境未就绪：缺少 ONNX 模型和 Python 环境";
        if (!onnx) return "OCR 环境未就绪：缺少 ONNX 模型";
        if (!py) return "仅支持一键打点（一键识别和截图 OCR 需要 Python 环境）";
        if (!script) return "Python 已安装，但 manga-ocr 脚本缺失";
        if (!IsMangaOcrModelReady) return "Python 已安装，但 manga-ocr 模型未下载";
        if (!running) return "环境就绪，请点击 OCR 开关启动 ocr 模型";
        return "OCR 环境已就绪";
    }
}
