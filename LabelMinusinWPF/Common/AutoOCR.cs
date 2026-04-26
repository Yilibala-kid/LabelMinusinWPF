using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using RapidOcrNet;
using SharpCompress.Archives;
using SkiaSharp;
using GroupConstants = LabelMinusinWPF.Common.GroupConstants;

namespace LabelMinusinWPF.Common;

public sealed class AutoOcrService
{
    private readonly IReadOnlyList<IAutoOcrProvider> _providers;

    public string ModelRoot { get; }

    public AutoOcrService(string? modelRoot = null, IEnumerable<IAutoOcrProvider>? providers = null)
    {
        ModelRoot = modelRoot ?? Path.Combine(AppContext.BaseDirectory, "Model");
        _providers = providers?.ToList() ?? [new PpOcrV5RapidOcrProvider()];
    }

    public IReadOnlyList<OcrModelInfo> ScanModels()
    {
        if (!Directory.Exists(ModelRoot))
            return [];

        return Directory.EnumerateDirectories(ModelRoot)
            .Select(TryReadModelInfo)
            .Where(model => model != null)
            .Select(model => model!)
            .OrderBy(model => model.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public OcrModelInfo? SelectPreferredModel(
        IReadOnlyList<OcrModelInfo> models,
        AutoOcrOptions? options = null)
    {
        options ??= new AutoOcrOptions();

        return models
            .Where(model => _providers.Any(provider => provider.CanHandle(model)))
            .OrderByDescending(model => GetModelScore(model, options))
            .ThenBy(model => model.Name, StringComparer.CurrentCultureIgnoreCase)
            .FirstOrDefault();
    }

    public async Task<AutoOcrResult> RunAsync(
        OneProject project,
        OcrModelInfo model,
        AutoOcrOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new AutoOcrOptions();

        if (project.ImageList.Count == 0)
            return AutoOcrResult.Failed("当前项目没有可 OCR 的图片");

        var provider = _providers.FirstOrDefault(p => p.CanHandle(model));
        if (provider == null)
            return AutoOcrResult.Failed($"没有可处理“{model.Engine}”模型的 OCR Provider");

        var validation = provider.ValidateModel(model);
        if (!validation.Success)
            return validation;

        int processedImages = 0;
        int createdLabels = 0;

        foreach (var image in project.ImageList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (options.SkipImagesWithLabels && image.ActiveLabels.Count > 0)
                continue;

            string imagePath = PrepareImagePath(image);
            var imageSize = GetImageSize(imagePath);
            var regions = await provider.RecognizeAsync(imagePath, model, options, cancellationToken);

            foreach (var region in SortRegions(regions, options.ReadingOrder).Where(r => !string.IsNullOrWhiteSpace(r.Text)))
            {
                Point position = ToRelativeCenter(region.Bounds, imageSize);
                var label = new OneLabel(region.Text.Trim(), GroupConstants.InBox, position);
                image.History.Execute(new AddCommand(image.Labels, label));
                createdLabels++;
            }

            processedImages++;
        }

        return AutoOcrResult.Succeeded(
            processedImages,
            createdLabels,
            $"OCR 完成：处理 {processedImages} 张图片，新增 {createdLabels} 个标签");
    }

    private OcrModelInfo? TryReadModelInfo(string modelDirectory)
    {
        string manifestPath = Path.Combine(modelDirectory, "model.json");
        if (!File.Exists(manifestPath))
            return null;

        try
        {
            using var stream = File.OpenRead(manifestPath);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            string id = GetString(root, "id") ?? Path.GetFileName(modelDirectory);
            string name = GetString(root, "name") ?? id;
            string engine = GetString(root, "engine") ?? "";
            string language = GetString(root, "language") ?? "";

            return new OcrModelInfo(
                id,
                name,
                engine,
                language,
                modelDirectory,
                manifestPath,
                ReadFiles(root));
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, string> ReadFiles(JsonElement root)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
                continue;

            if (property.Name.EndsWith("Model", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("dict", StringComparison.OrdinalIgnoreCase))
            {
                files[property.Name] = property.Value.GetString() ?? "";
            }
        }

        return files;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static int GetModelScore(OcrModelInfo model, AutoOcrOptions options)
    {
        string text = $"{model.Id} {model.Name} {model.Language} {string.Join(' ', model.Files.Values)}";
        int score = 0;

        if (ContainsAny(text, "japan", "japanese", "ja", "jp", "kana", "cjk"))
            score += options.Profile == OcrProfile.JapaneseManga ? 100 : 30;
        if (ContainsAny(text, "cjk", "zh-ja", "ja-en"))
            score += 40;
        if (ContainsAny(text, "server"))
            score += 10;
        if (ContainsAny(text, "latin", "en"))
            score -= options.Profile == OcrProfile.JapaneseManga ? 10 : 0;

        return score;
    }

    private static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<OcrTextRegion> SortRegions(IReadOnlyList<OcrTextRegion> regions, OcrReadingOrder readingOrder)
    {
        if (readingOrder == OcrReadingOrder.MangaRightToLeft)
            return SortMangaRegions(regions);

        return regions
            .OrderBy(region => region.Bounds.Top)
            .ThenBy(region => region.Bounds.Left)
            .ToList();
    }

    private static IReadOnlyList<OcrTextRegion> SortMangaRegions(IReadOnlyList<OcrTextRegion> regions)
    {
        if (regions.Count == 0)
            return [];

        double averageHeight = regions.Average(region => Math.Max(1, region.Bounds.Height));
        double rowTolerance = Math.Max(24, averageHeight * 0.8);
        var rows = new List<List<OcrTextRegion>>();

        foreach (var region in regions.OrderBy(region => region.Bounds.Top))
        {
            double centerY = region.Bounds.Top + region.Bounds.Height / 2;
            var row = rows.FirstOrDefault(candidate =>
                Math.Abs(candidate.Average(item => item.Bounds.Top + item.Bounds.Height / 2) - centerY) <= rowTolerance);

            if (row == null)
            {
                row = [];
                rows.Add(row);
            }

            row.Add(region);
        }

        return rows
            .OrderBy(row => row.Min(region => region.Bounds.Top))
            .SelectMany(row => row
                .OrderByDescending(region => region.Bounds.Left + region.Bounds.Width / 2)
                .ThenBy(region => region.Bounds.Top))
            .ToList();
    }

    private static Point ToRelativeCenter(Rect bounds, Size imageSize)
    {
        double x = imageSize.Width <= 0 ? 0.5 : (bounds.Left + bounds.Width / 2) / imageSize.Width;
        double y = imageSize.Height <= 0 ? 0.5 : (bounds.Top + bounds.Height / 2) / imageSize.Height;
        return new Point(Math.Clamp(x, 0, 1), Math.Clamp(y, 0, 1));
    }

    private static Size GetImageSize(string imagePath)
    {
        using var bitmap = SKBitmap.Decode(imagePath);
        if (bitmap == null)
            throw new InvalidOperationException($"无法读取图片尺寸：{imagePath}");

        return new Size(bitmap.Width, bitmap.Height);
    }

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
        if (string.IsNullOrWhiteSpace(ext))
            ext = ".png";

        string hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(image.ImagePath)))[..12];
        string targetPath = Path.Combine(tempRoot, $"{Path.GetFileNameWithoutExtension(entryPath)}_{hash}{ext}");
        if (File.Exists(targetPath))
            return targetPath;

        using var archive = ArchiveFactory.OpenArchive(archivePath);
        var entry = archive.Entries.FirstOrDefault(e =>
            !e.IsDirectory &&
            e.Key is not null &&
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

public interface IAutoOcrProvider
{
    string ProviderName { get; }

    bool CanHandle(OcrModelInfo model);

    AutoOcrResult ValidateModel(OcrModelInfo model);

    Task<IReadOnlyList<OcrTextRegion>> RecognizeAsync(
        string imagePath,
        OcrModelInfo model,
        AutoOcrOptions options,
        CancellationToken cancellationToken);
}

public sealed class PpOcrV5RapidOcrProvider : IAutoOcrProvider
{
    public const string EngineName = "PpOcrV5RapidOcr";
    private RapidOcr? _engine;
    private string? _loadedModelKey;

    public string ProviderName => EngineName;

    public bool CanHandle(OcrModelInfo model)
    {
        return model.Engine.Equals(EngineName, StringComparison.OrdinalIgnoreCase) ||
               model.Engine.Equals("RapidOcrNet", StringComparison.OrdinalIgnoreCase) ||
               model.Engine.Equals("PP-OCRv5", StringComparison.OrdinalIgnoreCase);
    }

    public AutoOcrResult ValidateModel(OcrModelInfo model)
    {
        string[] requiredKeys = ["detModel", "clsModel", "recModel", "dict"];
        var missing = requiredKeys
            .Where(key => string.IsNullOrWhiteSpace(model.GetFilePath(key)) || !File.Exists(model.GetFilePath(key)))
            .ToList();

        if (missing.Count > 0)
            return AutoOcrResult.Failed($"OCR 模型“{model.Name}”缺少文件: {string.Join(", ", missing)}");

        return AutoOcrResult.Succeeded(0, 0, "模型文件检查通过");
    }

    public Task<IReadOnlyList<OcrTextRegion>> RecognizeAsync(
        string imagePath,
        OcrModelInfo model,
        AutoOcrOptions options,
        CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyList<OcrTextRegion>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var bitmap = SKBitmap.Decode(imagePath)
                ?? throw new InvalidOperationException($"无法读取图片：{imagePath}");

            var engine = EnsureEngine(model);
            var rapidOptions = RapidOcrOptions.Default with
            {
                BoxScoreThresh = (float)options.MinConfidence,
                DoAngle = true
            };

            var result = engine.Detect(bitmap, rapidOptions);
            return result.TextBlocks
                .Select(block => ToRegion(block))
                .Where(region => region.Confidence >= options.MinConfidence)
                .ToList();
        }, cancellationToken);
    }

    private RapidOcr EnsureEngine(OcrModelInfo model)
    {
        string modelKey = model.ManifestPath;
        if (_engine != null && _loadedModelKey == modelKey)
            return _engine;

        _engine?.Dispose();
        _engine = new RapidOcr();
        _loadedModelKey = modelKey;

        _engine.InitModels(
            model.GetFilePath("detModel")!,
            model.GetFilePath("clsModel")!,
            model.GetFilePath("recModel")!,
            model.GetFilePath("dict")!);

        return _engine;
    }

    private static OcrTextRegion ToRegion(TextBlock block)
    {
        var points = block.BoxPoints;
        int minX = points.Min(p => p.X);
        int minY = points.Min(p => p.Y);
        int maxX = points.Max(p => p.X);
        int maxY = points.Max(p => p.Y);

        return new OcrTextRegion(
            block.GetText(),
            new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY)),
            block.BoxScore);
    }
}

public sealed record OcrModelInfo(
    string Id,
    string Name,
    string Engine,
    string Language,
    string DirectoryPath,
    string ManifestPath,
    IReadOnlyDictionary<string, string> Files)
{
    public string? GetFilePath(string key)
    {
        if (!Files.TryGetValue(key, out var relativePath) || string.IsNullOrWhiteSpace(relativePath))
            return null;

        return Path.Combine(DirectoryPath, relativePath);
    }
}

public enum OcrProfile
{
    General,
    JapaneseManga
}

public enum OcrReadingOrder
{
    TopLeft,
    MangaRightToLeft
}

public sealed record AutoOcrOptions(
    double MinConfidence = 0.5,
    bool SkipImagesWithLabels = false,
    OcrReadingOrder ReadingOrder = OcrReadingOrder.TopLeft,
    OcrProfile Profile = OcrProfile.General)
{
    public static AutoOcrOptions JapaneseManga { get; } = new(
        MinConfidence: 0.4,
        SkipImagesWithLabels: false,
        ReadingOrder: OcrReadingOrder.MangaRightToLeft,
        Profile: OcrProfile.JapaneseManga);
}

public sealed record OcrTextRegion(
    string Text,
    Rect Bounds,
    double Confidence);

public sealed record AutoOcrResult(
    bool Success,
    int ImageCount,
    int LabelCount,
    string Message)
{
    public static AutoOcrResult Succeeded(int imageCount, int labelCount, string message)
        => new(true, imageCount, labelCount, message);

    public static AutoOcrResult Failed(string message)
        => new(false, 0, 0, message);
}
