using System.IO;                  // Directory、File、Path 等文件系统操作
using System.Security.Cryptography; // SHA1 计算图片路径哈希
using System.Text;                 // Encoding.UTF8
using System.Text.Json;             // JSON 序列化（预留）
using System.Windows;               // Size、Point、Rect 等 UI 类型
using System.Diagnostics;           // Debug（预留）
using LabelMinusinWPF.Common;       // GroupConstants、AddCommand 等业务类型
using RapidOcrNet;                 // TextBlock 类型（来自 PpOcrV5 引擎）
using SharpCompress.Archives;      // 压缩包（zip/rar/7z）读取
using SkiaSharp;                    // SKBitmap 图片解码

namespace LabelMinusinWPF.OCRService;

// OcrPipeline — OCR 管线：模型发现 → 识别 → 后处理 → 创建 Label
// ============================================================================

public static class OcrPipeline
{
    // 模型文件的根目录，默认为程序目录下 /models
    public static string DefaultModelRoot => Path.Combine(AppContext.BaseDirectory, "models");

    // 在 models/ 中查找第一个可被 PpOcrV5RapidOcrProvider 处理的模型（PaddleOCR）
    public static OcrModelInfo? FindPaddleModel()
        => ScanModels().FirstOrDefault(m => PpOcrV5RapidOcrProvider.CanHandleEngine(m.Engine));

    /// <summary>扫描模型根目录，读取所有有效的 OCR 模型配置</summary>
    public static IReadOnlyList<OcrModelInfo> ScanModels(string? modelRoot = null)
    {
        // 若未指定 root 则使用默认目录
        var root = modelRoot ?? DefaultModelRoot;

        // 目录不存在则返回空列表
        if (!Directory.Exists(root))
            return [];

        // 遍历所有子目录，尝试解析 model.json
        return Directory.EnumerateDirectories(root)
            .Select(OcrModelInfo.TryRead)          // 每个子目录尝试解析为 OcrModelInfo
            .Where(model => model != null)         // 过滤解析失败的目录
            .Select(model => model!)               // 去掉 nullable 包装
            .OrderBy(model => model.Name, StringComparer.CurrentCultureIgnoreCase) // 按名称排序
            .ToList();
    }

    /// <summary>执行完整 OCR 管线：识别 → 合并 → 去重 → 排序 → 创建标签</summary>
    public static async Task<AutoOcrResult> RunAsync(
        OneProject project,       // 当前项目（含图片列表和消息队列）
        OcrModelInfo model,        // 使用的模型配置
        AutoOcrOptions options,    // OCR 选项（阈值、合并策略、输出模式等）
        IOcrProvider provider,     // OCR 提供者（PpOcr 或 MangaOcr）
        IProgress<string> progress, // 进度报告回调
        IReadOnlyList<OneImage>? images = null, // 要处理图片列表，null 表示全部
        CancellationToken cancellationToken = default)
    {
        // 无可处理图片时提前返回
        if (project.ImageList.Count == 0)
            return AutoOcrResult.Failed("当前项目没有可 OCR 的图片");

        int processedImages = 0;   // 已处理图片计数
        int createdLabels = 0;     // 已创建标签计数

        // 若未指定图片列表则处理项目全部图片
        var imageList = images ?? project.ImageList;
        int totalCount = imageList.Count;

        // 逐张处理图片
        foreach (var image in imageList)
        {
            // 支持取消
            cancellationToken.ThrowIfCancellationRequested();

            // 解析图片路径（若是压缩包内图片则解压到临时目录）
            string imagePath = PrepareImagePath(image);

            // 解码图片获取尺寸
            using var skBitmap = SKBitmap.Decode(imagePath);
            if (skBitmap == null)
                throw new InvalidOperationException($"无法读取图片尺寸：{imagePath}");

            // 保存图片尺寸（单位：像素）
            var imageSize = new Size(skBitmap.Width, skBitmap.Height);

            // 调用 OCR 提供者执行识别，得到文字区域列表
            var regions = await provider.RecognizeAsync(imagePath, model, options, cancellationToken);

            // 自动判断是否为竖排（日文漫画）布局
            bool vertical = IsVerticalLayout(regions);

            // MangaOcrProvider 的 RecognizeAsync 内部已完成合并，跳过 OcrPipeline 的合并步骤
            // 其他 Provider（如 PpOcrV5）需要在此做后处理合并
            var blocks = provider is MangaOcrProvider
                ? regions
                : BuildTextBlocks(regions, imageSize, options, vertical);

            // 去重（过滤中心点距离过近的区域）
            var deduped = DeduplicateRegions(blocks, imageSize, options);

            int imageLabelIndex = 0;

            // 按阅读顺序遍历每个文字块，创建 Label
            foreach (var block in SortRegions(deduped, options.RightToLeft, vertical))
            {
                imageLabelIndex++;

                // 计算标签归一化位置（相对于图片宽高，范围 0~1）
                // 水平方向取区域右边缘，垂直方向取区域顶部
                Point position = new(
                    imageSize.Width <= 0
                        ? 0.5
                        : Math.Clamp(block.Bounds.Right / imageSize.Width, 0, 1),
                    imageSize.Height <= 0
                        ? 0.5
                        : Math.Clamp(block.Bounds.Top / imageSize.Height, 0, 1));

                // 取文字内容，去首尾空白
                string text = block.Text.Trim();

                // 若是"仅位置模式"或文字为空，则生成占位标签名
                if (string.IsNullOrWhiteSpace(text) || options.OutputMode == OcrOutputMode.PositionOnly)
                    text = $"Label{imageLabelIndex}";

                // 创建 OneLabel 并通过 Undo/Redo 命令加入图片标签列表
                var label = new OneLabel(text, Common.GroupConstants.InBox, position);
                image.History.Execute(new AddCommand(image.Labels, label));

                createdLabels++;
            }

            processedImages++;

            // 进度消息投送到 UI 线程的消息队列
            Application.Current.Dispatcher.Invoke(() =>
                project.MsgQueue.Enqueue(
                    $"OCR 进度：{processedImages}/{totalCount} — {image.ImageName}"));
        }

        // 返回成功结果
        return AutoOcrResult.Succeeded(
            processedImages, createdLabels,
            $"OCR 完成：处理 {processedImages} 张图片，新增 {createdLabels} 个标签");
    }

    // ========================================================================
    // 后处理管线 — 过滤 → 合并 → 去重 → 排序
    // ========================================================================

    /// <summary>过滤无效区域并迭代合并相邻文本块</summary>
    internal static IReadOnlyList<OcrTextRegion> BuildTextBlocks(
        IReadOnlyList<OcrTextRegion> regions, // 原始识别结果
        Size imageSize,                       // 图片尺寸（用于计算相对阈值）
        AutoOcrOptions options,               // 合并参数
        bool vertical)                        // 是否竖排布局
    {
        // 第一步：过滤掉面积/边长过小的噪点区域
        var blocks = regions
            .Where(r => IsUsefulRegion(r, imageSize, options))
            .Select(r => (Region: r, Bounds: r.Bounds)) // 同时保留 Bounds 副本供合并用
            .ToList();

        // 合并关闭或不足两个块时直接返回（无需合并）
        if (!options.MergeTextLines || blocks.Count < 2)
            return blocks.Select(b => b.Region).ToList();

        bool merged;
        do
        {
            merged = false;

            // 两两比较，查找可合并的相邻块
            for (int i = 0; i < blocks.Count && !merged; i++)
                for (int j = i + 1; j < blocks.Count; j++)
                    if (ShouldMerge(blocks[i].Bounds, blocks[j].Bounds, imageSize, options))
                    {
                        // 取出两个待合并区域
                        var (ar, br) = (blocks[i].Region, blocks[j].Region);

                        // 合并：文字拼接 + 外接矩形 + 平均置信度
                        blocks[i] = (
                            new OcrTextRegion(
                                ar.Text + br.Text,
                                Union(ar.Bounds, br.Bounds),
                                (ar.Confidence + br.Confidence) / 2),
                            Union(blocks[i].Bounds, blocks[j].Bounds));

                        // 移除被合并的块 j
                        blocks.RemoveAt(j);
                        merged = true;
                        break;
                    }
        } while (merged); // 迭代直到本轮没有合并发生

        return blocks.Select(b => b.Region).ToList();
    }

    // 计算两个矩形的并集（外接矩形）
    private static Rect Union(Rect a, Rect b) => new(
        Math.Min(a.Left, b.Left),              // 外接矩形左边界
        Math.Min(a.Top, b.Top),                // 外接矩形上边界
        Math.Max(a.Right, b.Right) - Math.Min(a.Left, b.Left),   // 外接矩形宽度
        Math.Max(a.Bottom, b.Bottom) - Math.Min(a.Top, b.Top)); // 外接矩形高度

    /// <summary>检查区域是否足够大，用于过滤噪点</summary>
    private static bool IsUsefulRegion(OcrTextRegion r, Size imageSize, AutoOcrOptions o)
    {
        // 宽或高为零的无意义
        if (r.Bounds.Width <= 0 || r.Bounds.Height <= 0) return false;

        // 计算区域占图片面积的比值
        double areaRatio = r.Bounds.Width * r.Bounds.Height
            / Math.Max(1, imageSize.Width * imageSize.Height);

        // 计算最小边长
        double minSide = Math.Min(r.Bounds.Width, r.Bounds.Height);

        // 同时满足面积比和最小边长阈值才算有效
        return areaRatio >= o.MinRegionAreaRatio && minSide >= o.MinRegionSide;
    }

    /// <summary>判断两个区域是否应合并：扩展后相交 或 中心距离小于阈值</summary>
    private static bool ShouldMerge(Rect a, Rect b, Size imageSize, AutoOcrOptions o)
    {
        // 按图片尺寸比例计算扩展 padding
        double padding = Math.Max(imageSize.Width, imageSize.Height) * o.MergePaddingRatio;

        // a 向四周扩展 padding 后的矩形
        var expanded = new Rect(
            a.Left - padding, a.Top - padding,
            a.Width + padding * 2, a.Height + padding * 2);

        // 扩展后与 b 相交（说明两个块足够接近）
        if (expanded.IntersectsWith(b))
        {
            // 计算两个矩形重叠的宽高（负值表示分离）
            double xo = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
            double yo = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);

            // 重叠超过较小边长一半，认为是真正重叠而非点相交
            if (xo > -Math.Min(a.Width, b.Width) * 0.5
                || yo > -Math.Min(a.Height, b.Height) * 0.5)
                return true;
        }

        // 扩展后不相交：改用中心点距离判断
        double dist = Distance(Center(a), Center(b));

        // 最大允许距离 = 固定阈值 与（平均边长 × 缩放系数）中的较大值
        double maxDist = Math.Max(
            o.MergeMaxDistance,
            Math.Max((a.Width + b.Width) / 2, (a.Height + b.Height) / 2) * o.MergeDistanceScale);

        return dist <= maxDist;
    }

    /// <summary>按置信度排序后去除中心点过近的重复区域</summary>
    private static IReadOnlyList<OcrTextRegion> DeduplicateRegions(
        IReadOnlyList<OcrTextRegion> regions,
        Size imageSize,
        AutoOcrOptions options)
    {
        // 去重关闭或只有一个区域时直接返回
        if (!options.DeduplicateRegions || regions.Count <= 1)
            return regions;

        // 按置信度从高到低排序，高置信度区域优先保留
        var sorted = regions.OrderByDescending(r => r.Confidence).ToList();

        var accepted = new List<OcrTextRegion>();      // 保留的区域
        var acceptedCenters = new List<Point>();       // 对应中心点（归一化 0~1）

        foreach (var region in sorted)
        {
            // 计算归一化中心点
            Point center = new(
                imageSize.Width <= 0
                    ? 0.5
                    : Math.Clamp(
                        (region.Bounds.Left + region.Bounds.Width / 2) / imageSize.Width,
                        0, 1),
                imageSize.Height <= 0
                    ? 0.5
                    : Math.Clamp(
                        (region.Bounds.Top + region.Bounds.Height / 2) / imageSize.Height,
                        0, 1));

            // 如果与已有保留区域的中心距离超过阈值，则接受；否则跳过（重复）
            if (!acceptedCenters.Any(ac => Distance(center, ac) <= options.DeduplicateDistance))
            {
                accepted.Add(region);
                acceptedCenters.Add(center);
            }
        }

        // 用 HashSet 高效过滤：只保留被接受的区域（保持原始顺序）
        var acceptedSet = new HashSet<OcrTextRegion>(accepted);
        return regions.Where(r => acceptedSet.Contains(r)).ToList();
    }

    /// <summary>自动判断是否为竖排文字（瘦高框占多）</summary>
    internal static bool IsVerticalLayout(IReadOnlyList<OcrTextRegion> regions)
    {
        // 少于 3 个区域时无法判断，默认横排
        if (regions.Count < 3) return false;

        int vertical = 0, horizontal = 0;

        // 统计瘦高框（竖排特征）和扁平框（横排特征）的数量
        foreach (var r in regions)
        {
            if (r.Bounds.Height > r.Bounds.Width * 1.3)
                vertical++;   // 高度明显大于宽度 → 竖排
            else if (r.Bounds.Width > r.Bounds.Height * 1.3)
                horizontal++; // 宽度明显大于高度 → 横排
        }

        // 竖排框更多则判定为竖排布局
        return vertical > horizontal;
    }

    /// <summary>按阅读顺序排序：横排按行、竖排按列</summary>
    private static IReadOnlyList<OcrTextRegion> SortRegions(
        IReadOnlyList<OcrTextRegion> regions,
        bool rightToLeft, // 横排时是否从右到左阅读（如日语片假名漫画）
        bool vertical)   // 是否竖排布局
    {
        if (regions.Count == 0) return [];

        // CJK 竖排永远是右→左的列顺序，忽略 rightToLeft 参数
        return vertical
            ? SortVertical(regions, rightToLeft: true)
            : SortHorizontal(regions, rightToLeft);
    }

    // 横排文字按行排序：先按顶边 y 坐标分行，再在行内按 x 排序
    private static IReadOnlyList<OcrTextRegion> SortHorizontal(
        IReadOnlyList<OcrTextRegion> regions, bool rightToLeft)
    {
        // 计算所有区域平均高度，用于确定行间距容差
        double avgH = regions.Average(r => Math.Max(1, r.Bounds.Height));

        // 行容差 = max(24px, 平均高度 × 0.8)，避免行间距太小导致跨行
        double rowTol = Math.Max(24, avgH * 0.8);

        var rows = new List<List<OcrTextRegion>>();

        // 按顶边坐标升序遍历所有区域，逐步归入已有行或创建新行
        foreach (var r in regions.OrderBy(r => r.Bounds.Top))
        {
            // 当前区域中心的 y 坐标
            double cy = r.Bounds.Top + r.Bounds.Height / 2;

            // 查找是否有已有行其中心与当前 cy 足够接近（在同一行内）
            var row = rows.FirstOrDefault(row =>
                Math.Abs(row.Average(x => x.Bounds.Top + x.Bounds.Height / 2) - cy) <= rowTol);

            // 没有找到匹配行则创建新行
            if (row == null)
            {
                row = [];
                rows.Add(row);
            }

            row.Add(r);
        }

        // 对所有行按顶边坐标排序（自上而下）
        // 然后根据 rightToLeft 决定行内文字方向
        return rows
            .OrderBy(row => row.Min(r => r.Bounds.Top))
            .SelectMany(row => rightToLeft
                // 从右到左：行内按右边界降序，再按顶边升序
                ? row.OrderByDescending(r => r.Bounds.Left + r.Bounds.Width / 2)
                       .ThenBy(r => r.Bounds.Top)
                // 从左到右：行内按左边界升序，再按顶边升序
                : row.OrderBy(r => r.Bounds.Left)
                       .ThenBy(r => r.Bounds.Top))
            .ToList();
    }

    // 竖排文字按列排序：先按左边界 x 坐标分列，再在列内按 y 排序
    private static IReadOnlyList<OcrTextRegion> SortVertical(
        IReadOnlyList<OcrTextRegion> regions, bool rightToLeft)
    {
        // 计算所有区域平均宽度，用于确定列间距容差
        double avgW = regions.Average(r => Math.Max(1, r.Bounds.Width));

        // 列容差 = max(24px, 平均宽度 × 0.8)
        double colTol = Math.Max(24, avgW * 0.8);

        var columns = new List<List<OcrTextRegion>>();

        // 按左边界坐标升序遍历所有区域，逐步归入已有列或创建新列
        foreach (var r in regions.OrderBy(r => r.Bounds.Left))
        {
            // 当前区域中心的 x 坐标
            double cx = r.Bounds.Left + r.Bounds.Width / 2;

            // 查找是否有已有列其中心与当前 cx 足够接近（在同一列内）
            var col = columns.FirstOrDefault(col =>
                Math.Abs(col.Average(x => x.Bounds.Left + x.Bounds.Width / 2) - cx) <= colTol);

            if (col == null)
            {
                col = [];
                columns.Add(col);
            }

            col.Add(r);
        }

        // 根据 rightToLeft 决定列顺序：日漫竖排固定右到左
        return (rightToLeft
                // 右到左：按最左边界降序（最右边的列排在前面）
                ? columns.OrderByDescending(col => col.Min(r => r.Bounds.Left))
                // 左到右：按最左边界升序
                : columns.OrderBy(col => col.Min(r => r.Bounds.Left)))
            // 列内按顶边坐标升序（自上而下）
            .SelectMany(col => col.OrderBy(r => r.Bounds.Top))
            .ToList();
    }

    // ========================================================================
    // 文本块工具
    // ========================================================================

    /// <summary>将 RapidOcr 的 TextBlock（四个角点）转换为 System.Windows.Rect</summary>
    internal static Rect BlockToRect(TextBlock block)
    {
        // 四个角点
        var pts = block.BoxPoints;

        // 计算最小外接矩形
        int minX = pts.Min(p => p.X), minY = pts.Min(p => p.Y);
        int maxX = pts.Max(p => p.X), maxY = pts.Max(p => p.Y);

        // 返回 Rect（左上角 x,y + 宽高）
        // 宽高至少为 1，避免零宽高导致除零问题
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

    // ========================================================================
    // 图片工具
    // ========================================================================

    /// <summary>
    /// 解析图片路径。
    /// 若图片在压缩包（zip/rar/7z）内，则解压到 OCRtemp/AutoOCR/ 并返回临时文件路径；
    /// 若图片在文件系统上，直接返回原路径。
    /// </summary>
    private static string PrepareImagePath(OneImage image)
    {
        // 尝试解析图片路径，判断是否在压缩包内
        var archiveResult = ResourceHelper.ParseArchivePath(image.ImagePath);

        // 不在压缩包内，直接返回原路径
        if (!archiveResult.HasValue)
            return image.ImagePath;

        var (archivePath, entryPath) = archiveResult.Value;

        // 压缩包文件不存在则报错
        if (!File.Exists(archivePath))
            throw new FileNotFoundException("找不到压缩包", archivePath);

        // 临时目录：OCRtemp/AutoOCR/（不清理，程序退出时由系统删除）
        string tempRoot = Path.Combine(
            AppContext.BaseDirectory,
            OcrConstants.OcrTemp,
            OcrConstants.AutoOcrSubDir);
        Directory.CreateDirectory(tempRoot);

        // 保留原始文件扩展名（解压后仍用原扩展名）
        string ext = Path.GetExtension(entryPath);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".png";

        // 用图片路径的 SHA1 前12位做哈希，避免同一压缩包内多张同名图片冲突
        string hash = Convert.ToHexString(
            SHA1.HashData(Encoding.UTF8.GetBytes(image.ImagePath)))[..12];

        // 目标临时文件路径
        string targetPath = Path.Combine(tempRoot,
            $"{Path.GetFileNameWithoutExtension(entryPath)}_{hash}{ext}");

        // 若临时文件已存在（同一张图已解压过），直接返回缓存路径
        if (File.Exists(targetPath)) return targetPath;

        // 打开压缩包并查找对应条目
        using var archive = ArchiveFactory.OpenArchive(archivePath);

        var entry = archive.Entries.FirstOrDefault(e =>
            !e.IsDirectory
            && e.Key is not null
            // 支持三种路径格式：完全相等、或以 / 或 \ 结尾后跟路径名
            && (e.Key.Equals(entryPath, StringComparison.OrdinalIgnoreCase)
                || e.Key.EndsWith("/" + entryPath, StringComparison.OrdinalIgnoreCase)
                || e.Key.EndsWith("\\" + entryPath, StringComparison.OrdinalIgnoreCase)));

        if (entry == null)
            throw new FileNotFoundException("压缩包中找不到图片", entryPath);

        // 解压写入临时文件
        using var fs = File.Create(targetPath);
        entry.WriteTo(fs);

        return targetPath;
    }
}
