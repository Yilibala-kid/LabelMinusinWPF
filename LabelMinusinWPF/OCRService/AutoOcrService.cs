using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using LabelMinusinWPF.Common;

namespace LabelMinusinWPF.OCRService;

public static class AutoOcrService
{
    private const string NoRecognizedTextMessage = "OCR 未识别到文字";

    public static async Task<AutoOcrResult> RunAsync(
        OneProject project,
        AutoOcrRequest request,
        IProgress<string> progress,
        CancellationToken cancellationToken = default)
    {
        var options = CreateOptions(request);
        var runtime = await GetRuntimeAsync(request.Engine, progress, cancellationToken);

        return request.Screenshot is { } screenshot
            ? await RunScreenshotAsync(project, screenshot, request.ScreenshotNormalizedRect, runtime, options, cancellationToken)
            : await RunImagesAsync(project, request.Images, runtime, options, progress, cancellationToken);
    }

    private static AutoOcrOptions CreateOptions(AutoOcrRequest request)
    {
        bool detectOnly = request.Engine == OcrEngineKind.Manga
            || request.OutputMode == OcrOutputMode.PositionOnly;

        return AutoOcrOptions.Default with
        {
            OutputMode = request.OutputMode,
            RightToLeft = detectOnly,
            DetectOnly = detectOnly
        };
    }

    private static async Task<OcrRuntime> GetRuntimeAsync(
        OcrEngineKind engine,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        if (engine == OcrEngineKind.Paddle)
        {
            var model = OcrPipeline.FindPaddleModel()
                ?? throw new InvalidOperationException("未找到 PP-OCRv6 模型配置");
            await PaddleOcrPythonProvider.EnsureProcessAsync(progress, cancellationToken);
            return new OcrRuntime(model, PaddleOcrPythonProvider.Shared);
        }

        var mangaModel = OcrPipeline.FindMangaModel()
            ?? throw new InvalidOperationException("未找到 manga-ocr 模型");

        await MangaOcrProvider.EnsureProcessAsync(progress, cancellationToken);
        return new OcrRuntime(mangaModel, new MangaOcrProvider());
    }

    private static async Task<AutoOcrResult> RunImagesAsync(
        OneProject project,
        IReadOnlyList<OneImage>? images,
        OcrRuntime runtime,
        AutoOcrOptions options,
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

            progress.Report($"OCR 开始：第 {processedImages + 1}/{imageList.Count} 页 — {image.ImageName}");

            string imagePath = OcrPipeline.ResolveImagePath(image);
            var result = await RecognizeFileAsync(
                imagePath,
                runtime,
                options,
                cancellationToken,
                path => $"无法读取图片尺寸：{path}");

            createdLabels += AddLabels(image, result.Regions, result.ImageSize, options);
            processedImages++;

            progress.Report($"OCR 进度：{processedImages}/{imageList.Count} — {image.ImageName}");
        }

        return new AutoOcrResult(true, processedImages, createdLabels,
            $"OCR 完成：处理 {processedImages} 张图片，新增 {createdLabels} 个标签");
    }

    private static async Task<AutoOcrResult> RunScreenshotAsync(
        OneProject project,
        BitmapSource screenshot,
        Rect? normalizedRect,
        OcrRuntime runtime,
        AutoOcrOptions options,
        CancellationToken cancellationToken)
    {
        var selectedImage = project.SelectedImage;
        if (selectedImage == null)
            return new AutoOcrResult(false, 0, 0, "当前没有选中的图片");
        if (normalizedRect == null)
            return new AutoOcrResult(false, 0, 0, "截图区域无效");

        return await OcrPipeline.UseTempPngAsync(screenshot, async tempPath =>
        {
            var result = await RecognizeFileAsync(
                tempPath,
                runtime,
                options,
                cancellationToken,
                path => $"无法读取截图：{path}");

            if (result.Regions.Count == 0)
                return NoTextResult();

            var mappedRegions = MapToImage(result.Regions, result.ImageSize, normalizedRect.Value);
            var singleRegion = MergeRegions(mappedRegions, options);

            return ShouldSkipEmptyText(singleRegion, options)
                ? NoTextResult()
                : AddScreenshotLabel(selectedImage, singleRegion, options);
        }) ?? new AutoOcrResult(false, 0, 0, "OCR 截图处理失败");
    }

    private static async Task<RecognizedImage> RecognizeFileAsync(
        string imagePath,
        OcrRuntime runtime,
        AutoOcrOptions options,
        CancellationToken cancellationToken,
        Func<string, string> decodeErrorMessage)
    {
        var imageSize = ReadImageSize(imagePath, decodeErrorMessage);
        var regions = await runtime.Provider.RecognizeAsync(
            imagePath,
            runtime.Model,
            options,
            cancellationToken);
        var finalRegions = PostProcess(
            regions,
            imageSize,
            options,
            runtime.Provider.MergesRegionsInternally);

        return new RecognizedImage(finalRegions, imageSize);
    }

    private static Size ReadImageSize(string imagePath, Func<string, string> decodeErrorMessage)
    {
        try
        {
            using var stream = File.OpenRead(imagePath);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.DelayCreation,
                BitmapCacheOption.None);
            var frame = decoder.Frames.FirstOrDefault()
                ?? throw new InvalidOperationException(decodeErrorMessage(imagePath));
            return new Size(frame.PixelWidth, frame.PixelHeight);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(decodeErrorMessage(imagePath), ex);
        }
    }

    private static IReadOnlyList<OcrTextRegion> PostProcess(
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

    internal static int AddLabels(
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
            image.AddLabelWithHistory(label);
            createdLabels++;
        }

        return createdLabels;
    }

    private static IReadOnlyList<OcrTextRegion> MapToImage(
        IReadOnlyList<OcrTextRegion> regions,
        Size screenshotSize,
        Rect normalizedRect)
    {
        double width = Math.Max(1, screenshotSize.Width);
        double height = Math.Max(1, screenshotSize.Height);

        return regions.Select(region =>
        {
            var bounds = new Rect(
                normalizedRect.Left + region.Bounds.Left / width * normalizedRect.Width,
                normalizedRect.Top + region.Bounds.Top / height * normalizedRect.Height,
                region.Bounds.Width / width * normalizedRect.Width,
                region.Bounds.Height / height * normalizedRect.Height);

            return new OcrTextRegion(region.Text, bounds, region.Confidence);
        }).ToList();
    }

    private static OcrTextRegion MergeRegions(
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
            : string.Join("", regions.Select(region => region.Text.Trim())
                .Where(text => !string.IsNullOrWhiteSpace(text)));

        return new OcrTextRegion(text, bounds, regions.Average(region => region.Confidence));
    }

    private static bool ShouldSkipEmptyText(OcrTextRegion region, AutoOcrOptions options) =>
        options.OutputMode == OcrOutputMode.RecognizedText
        && string.IsNullOrWhiteSpace(region.Text);

    private static AutoOcrResult NoTextResult() =>
        new(true, 1, 0, NoRecognizedTextMessage);

    private static AutoOcrResult AddScreenshotLabel(
        OneImage selectedImage,
        OcrTextRegion region,
        AutoOcrOptions options)
    {
        int labels = AddLabels(selectedImage, [region], new Size(1, 1), options);
        string preview = region.Text.Trim();
        string message = string.IsNullOrWhiteSpace(preview)
            ? $"OCR 完成：新增 {labels} 个标签"
            : $"OCR 标签已添加：{preview[..Math.Min(30, preview.Length)]}";

        return new AutoOcrResult(true, 1, labels, message);
    }

    private sealed record OcrRuntime(OcrModelInfo Model, IOcrProvider Provider);

    private sealed record RecognizedImage(IReadOnlyList<OcrTextRegion> Regions, Size ImageSize);
}
