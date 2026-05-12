using System.IO;
using System.Windows;
using LabelMinusinWPF.Common;
using SkiaSharp;

namespace LabelMinusinWPF.OCRService;

public static class AutoOcrService
{
    public static async Task<AutoOcrResult> RunAsync(
        OneProject project,
        AutoOcrRequest request,
        IProgress<string> progress,
        CancellationToken cancellationToken = default)
    {
        var options = CreateOptions(request);
        var (model, provider) = await ResolveProviderAsync(request.Engine, progress, cancellationToken);

        return request.Screenshot is { } screenshot
            ? await RunScreenshotAsync(project, screenshot, request.ScreenshotNormalizedRect, model, options, provider, cancellationToken)
            : await RunImagesAsync(project, request.Images, model, options, provider, progress, cancellationToken);
    }

    private static AutoOcrOptions CreateOptions(AutoOcrRequest request)
    {
        bool rightToLeft = request.Engine == OcrEngineKind.Manga
            || request.OutputMode == OcrOutputMode.PositionOnly;

        return AutoOcrOptions.Default with
        {
            OutputMode = request.OutputMode,
            RightToLeft = rightToLeft
        };
    }

    private static async Task<(OcrModelInfo Model, IOcrProvider Provider)> ResolveProviderAsync(
        OcrEngineKind engine,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        if (engine == OcrEngineKind.Paddle)
        {
            var model = OcrPipeline.FindPaddleModel()
                ?? throw new InvalidOperationException("未找到 PaddleOCR 模型");
            PpOcrV5RapidOcrProvider.EnsureSharedEngine(model);
            return (model, PpOcrV5RapidOcrProvider.Shared);
        }

        var mangaModel = OcrPipeline.ScanModels().FirstOrDefault(m =>
            m.Engine.Equals(MangaOcrProvider.EngineName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("未找到 manga-ocr 模型");

        if (MangaOcrProvider.SharedProcess is not { HasExited: false })
        {
            if (!OcrEnvironment.ReadyForProcessStart)
                throw new InvalidOperationException("Python 环境或 manga-ocr 模型未配置，日文 OCR 不可用");

            progress.Report("OCR模型加载中");
            await MangaOcrProvider.StartProcessAsync();
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report("OCR已启动");
        }

        return (mangaModel, new MangaOcrProvider());
    }

    private static async Task<AutoOcrResult> RunImagesAsync(
        OneProject project,
        IReadOnlyList<OneImage>? images,
        OcrModelInfo model,
        AutoOcrOptions options,
        IOcrProvider provider,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        int processedImages = 0;
        int createdLabels = 0;
        var imageList = images ?? project.ImageList;

        if (imageList.Count == 0)
            return new AutoOcrResult(false, 0, 0, "当前项目没有可 OCR 的图片");

        foreach (var image in imageList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string imagePath = OcrPipeline.PrepareImagePath(image);
            using var skBitmap = SKBitmap.Decode(imagePath);
            if (skBitmap == null)
                throw new InvalidOperationException($"无法读取图片尺寸：{imagePath}");

            var imageSize = new Size(skBitmap.Width, skBitmap.Height);
            var regions = await provider.RecognizeAsync(imagePath, model, options, cancellationToken);
            var finalRegions = PostProcessRegions(regions, imageSize, options, provider.MergesRegionsInternally);

            createdLabels += CreateLabelsFromRegions(image, finalRegions, imageSize, options);
            processedImages++;

            progress.Report($"OCR 进度：{processedImages}/{imageList.Count} — {image.ImageName}");
        }

        return new AutoOcrResult(true, processedImages, createdLabels,
            $"OCR 完成：处理 {processedImages} 张图片，新增 {createdLabels} 个标签");
    }

    private static async Task<AutoOcrResult> RunScreenshotAsync(
        OneProject project,
        System.Windows.Media.Imaging.BitmapSource screenshot,
        Rect? normalizedRect,
        OcrModelInfo model,
        AutoOcrOptions options,
        IOcrProvider provider,
        CancellationToken cancellationToken)
    {
        if (project.SelectedImage == null)
            return new AutoOcrResult(false, 0, 0, "当前没有选中的图片");
        if (normalizedRect == null)
            return new AutoOcrResult(false, 0, 0, "截图区域无效");

        return await OcrPipeline.WithTempPngAsync(screenshot, async tmpPath =>
        {
            using var skBitmap = SKBitmap.Decode(tmpPath);
            if (skBitmap == null)
                throw new InvalidOperationException($"无法读取截图：{tmpPath}");

            var screenshotSize = new Size(skBitmap.Width, skBitmap.Height);
            var regions = await provider.RecognizeAsync(tmpPath, model, options, cancellationToken);
            var finalRegions = PostProcessRegions(regions, screenshotSize, options, provider.MergesRegionsInternally);

            if (finalRegions.Count == 0)
                return new AutoOcrResult(true, 1, 0, "OCR 未识别到文字");

            var mappedRegions = MapScreenshotRegions(finalRegions, screenshotSize, normalizedRect.Value);
            var singleRegion = CombineForScreenshot(mappedRegions, options);

            if (options.OutputMode == OcrOutputMode.RecognizedText
                && string.IsNullOrWhiteSpace(singleRegion.Text))
                return new AutoOcrResult(true, 1, 0, "OCR 未识别到文字");

            int labels = CreateLabelsFromRegions(
                project.SelectedImage,
                [singleRegion],
                new Size(1, 1),
                options);

            string preview = singleRegion.Text.Trim();
            string message = string.IsNullOrWhiteSpace(preview)
                ? $"OCR 完成：新增 {labels} 个标签"
                : $"OCR 标签已添加：{preview[..Math.Min(30, preview.Length)]}";

            return new AutoOcrResult(true, 1, labels, message);
        }) ?? new AutoOcrResult(false, 0, 0, "OCR 截图处理失败");
    }

    private static IReadOnlyList<OcrTextRegion> PostProcessRegions(
        IReadOnlyList<OcrTextRegion> regions,
        Size imageSize,
        AutoOcrOptions options,
        bool alreadyMerged)
    {
        bool vertical = OcrPipeline.IsVerticalLayout(regions);
        var blocks = alreadyMerged
            ? regions
            : OcrPipeline.BuildTextBlocks(regions, imageSize, options, vertical);
        var deduped = OcrPipeline.DeduplicateRegions(blocks, imageSize, options);
        return OcrPipeline.SortRegions(deduped, options.RightToLeft, vertical);
    }

    internal static int CreateLabelsFromRegions(
        OneImage image,
        IReadOnlyList<OcrTextRegion> regions,
        Size imageSize,
        AutoOcrOptions options)
    {
        int createdLabels = 0;

        foreach (var region in regions)
        {
            string text = region.Text.Trim();
            if (string.IsNullOrWhiteSpace(text) || options.OutputMode == OcrOutputMode.PositionOnly)
                text = $"Label{createdLabels + 1}";

            var position = new Point(
                imageSize.Width <= 0
                    ? 0.5
                    : Math.Clamp(region.Bounds.Right / imageSize.Width, 0, 1),
                imageSize.Height <= 0
                    ? 0.5
                    : Math.Clamp(region.Bounds.Top / imageSize.Height, 0, 1));

            var label = new OneLabel(text, GroupConstants.InBox, position);
            image.History.Execute(new AddCommand(image.Labels, label));
            createdLabels++;
        }

        return createdLabels;
    }

    private static IReadOnlyList<OcrTextRegion> MapScreenshotRegions(
        IReadOnlyList<OcrTextRegion> regions,
        Size screenshotSize,
        Rect normalizedRect)
    {
        double width = Math.Max(1, screenshotSize.Width);
        double height = Math.Max(1, screenshotSize.Height);

        return regions.Select(r =>
        {
            var bounds = new Rect(
                normalizedRect.Left + r.Bounds.Left / width * normalizedRect.Width,
                normalizedRect.Top + r.Bounds.Top / height * normalizedRect.Height,
                r.Bounds.Width / width * normalizedRect.Width,
                r.Bounds.Height / height * normalizedRect.Height);

            return new OcrTextRegion(r.Text, bounds, r.Confidence);
        }).ToList();
    }

    private static OcrTextRegion CombineForScreenshot(
        IReadOnlyList<OcrTextRegion> regions,
        AutoOcrOptions options)
    {
        if (regions.Count == 1)
            return regions[0];

        var bounds = regions[0].Bounds;
        for (int i = 1; i < regions.Count; i++)
            bounds.Union(regions[i].Bounds);

        string text = options.OutputMode == OcrOutputMode.PositionOnly
            ? ""
            : string.Join("", regions.Select(r => r.Text.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t)));

        return new OcrTextRegion(text, bounds, regions.Average(r => r.Confidence));
    }
}
