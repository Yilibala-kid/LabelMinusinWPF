using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using LabelMinusinWPF.Common;
using SharpCompress.Archives;

namespace LabelMinusinWPF.OCRService;

public static class OcrPipeline
{
    public static string DefaultModelRoot => Path.Combine(AppContext.BaseDirectory, "models");

    public static OcrModelInfo? FindPaddleModel()
        => FindModel(model => PaddleOcrPythonProvider.CanHandleEngine(model.Engine));

    public static OcrModelInfo? FindMangaModel()
        => FindModel(model => model.Engine.Equals(MangaOcrProvider.EngineName, StringComparison.OrdinalIgnoreCase));

    private static OcrModelInfo? FindModel(Func<OcrModelInfo, bool> predicate)
        => ScanModels().FirstOrDefault(predicate);

    public static IReadOnlyList<OcrModelInfo> ScanModels(string? modelRoot = null)
    {
        var root = modelRoot ?? DefaultModelRoot;
        if (!Directory.Exists(root))
            return [];

        return Directory.EnumerateDirectories(root)
            .Select(OcrModelInfo.TryRead)
            .OfType<OcrModelInfo>()
            .OrderBy(model => model.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    internal static IReadOnlyList<OcrTextRegion> BuildTextBlocks(
        IReadOnlyList<OcrTextRegion> regions,
        Size imageSize,
        AutoOcrOptions options,
        bool vertical)
    {
        var blocks = regions
            .Where(region => IsUsefulRegion(region, imageSize, options))
            .Select(region => (Region: region, Bounds: region.Bounds))
            .ToList();

        if (!options.MergeTextLines || blocks.Count < 2)
            return blocks.Select(block => block.Region).ToList();

        bool merged;
        do
        {
            merged = false;

            for (int i = 0; i < blocks.Count && !merged; i++)
            {
                for (int j = i + 1; j < blocks.Count; j++)
                {
                    if (!ShouldMerge(blocks[i].Bounds, blocks[j].Bounds, imageSize, options))
                        continue;

                    var (firstRegion, secondRegion) = OrderForTextMerge(
                        blocks[i].Region,
                        blocks[j].Region,
                        vertical,
                        options.RightToLeft);
                    var mergedBounds = Union(firstRegion.Bounds, secondRegion.Bounds);

                    blocks[i] = (
                        new OcrTextRegion(
                            firstRegion.Text + secondRegion.Text,
                            mergedBounds,
                            (firstRegion.Confidence + secondRegion.Confidence) / 2),
                        Union(blocks[i].Bounds, blocks[j].Bounds));

                    blocks.RemoveAt(j);
                    merged = true;
                    break;
                }
            }
        } while (merged);

        return blocks.Select(block => block.Region).ToList();
    }

    internal static IReadOnlyList<OcrTextRegion> DeduplicateRegions(
        IReadOnlyList<OcrTextRegion> regions,
        Size imageSize,
        AutoOcrOptions options)
    {
        if (!options.DeduplicateRegions || regions.Count <= 1)
            return regions;

        var sorted = regions.OrderByDescending(region => region.Confidence).ToList();
        var accepted = new List<OcrTextRegion>();
        var acceptedCenters = new List<Point>();

        foreach (var region in sorted)
        {
            Point center = new(
                imageSize.Width <= 0
                    ? 0.5
                    : Math.Clamp((region.Bounds.Left + region.Bounds.Width / 2) / imageSize.Width, 0, 1),
                imageSize.Height <= 0
                    ? 0.5
                    : Math.Clamp((region.Bounds.Top + region.Bounds.Height / 2) / imageSize.Height, 0, 1));

            if (acceptedCenters.Any(acceptedCenter =>
                    Distance(center, acceptedCenter) <= options.DeduplicateDistance))
                continue;

            accepted.Add(region);
            acceptedCenters.Add(center);
        }

        var acceptedSet = new HashSet<OcrTextRegion>(accepted);
        return regions.Where(acceptedSet.Contains).ToList();
    }

    internal static bool IsVerticalLayout(IReadOnlyList<OcrTextRegion> regions)
    {
        if (regions.Count == 0)
            return false;

        int vertical = 0;
        int horizontal = 0;
        double verticalScore = 0;
        double horizontalScore = 0;

        foreach (var region in regions)
        {
            double width = Math.Max(1, region.Bounds.Width);
            double height = Math.Max(1, region.Bounds.Height);
            double aspect = height / width;

            verticalScore += aspect;
            horizontalScore += width / height;

            if (aspect >= 1.8)
                vertical++;
            else if (width / height >= 1.8)
                horizontal++;
        }

        if (regions.Count <= 2)
            return vertical == regions.Count && verticalScore >= horizontalScore * 1.6;

        return vertical >= Math.Max(2, horizontal + 1)
            && verticalScore >= horizontalScore * 1.25;
    }

    internal static IReadOnlyList<OcrTextRegion> SortRegions(
        IReadOnlyList<OcrTextRegion> regions,
        bool rightToLeft,
        bool vertical)
    {
        if (regions.Count == 0)
            return [];

        return vertical
            ? SortVertical(regions, rightToLeft: true)
            : SortHorizontal(regions, rightToLeft);
    }

    internal static string SaveTempPng(BitmapSource bitmap)
    {
        string tempDirectory = Path.Combine(AppContext.BaseDirectory, OcrConstants.OcrTemp);
        Directory.CreateDirectory(tempDirectory);
        string tempPath = Path.Combine(tempDirectory, $"ocr_{Guid.NewGuid():N}.png");

        Application.Current.Dispatcher.Invoke(() =>
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new FileStream(tempPath, FileMode.Create);
            encoder.Save(stream);
        });

        return tempPath;
    }

    internal static async Task<T?> UseTempPngAsync<T>(
        BitmapSource bitmap,
        Func<string, Task<T?>> action)
    {
        string tempPath = SaveTempPng(bitmap);
        try
        {
            return await action(tempPath);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    internal static string ResolveImagePath(OneImage image)
    {
        var archiveResult = ResourceHelper.ParseArchivePath(image.ImagePath);
        if (!archiveResult.HasValue)
            return image.ImagePath;

        var (archivePath, entryPath) = archiveResult.Value;
        if (!File.Exists(archivePath))
            throw new FileNotFoundException("\u627e\u4e0d\u5230\u538b\u7f29\u5305", archivePath);

        string tempRoot = Path.Combine(
            AppContext.BaseDirectory,
            OcrConstants.OcrTemp,
            OcrConstants.AutoOcrSubDir);
        Directory.CreateDirectory(tempRoot);

        string extension = Path.GetExtension(entryPath);
        if (string.IsNullOrWhiteSpace(extension))
            extension = ".png";

        string hash = Convert.ToHexString(
            SHA1.HashData(Encoding.UTF8.GetBytes(image.ImagePath)))[..12];

        string targetPath = Path.Combine(
            tempRoot,
            $"{Path.GetFileNameWithoutExtension(entryPath)}_{hash}{extension}");

        if (File.Exists(targetPath))
            return targetPath;

        using var archive = ArchiveFactory.OpenArchive(archivePath);
        var entry = archive.Entries.FirstOrDefault(entry =>
            !entry.IsDirectory
            && entry.Key is not null
            && (entry.Key.Equals(entryPath, StringComparison.OrdinalIgnoreCase)
                || entry.Key.EndsWith("/" + entryPath, StringComparison.OrdinalIgnoreCase)
                || entry.Key.EndsWith("\\" + entryPath, StringComparison.OrdinalIgnoreCase)));

        if (entry == null)
            throw new FileNotFoundException("\u538b\u7f29\u5305\u4e2d\u627e\u4e0d\u5230\u56fe\u7247", entryPath);

        using var stream = File.Create(targetPath);
        entry.WriteTo(stream);

        return targetPath;
    }

    private static Rect Union(Rect left, Rect right) => new(
        Math.Min(left.Left, right.Left),
        Math.Min(left.Top, right.Top),
        Math.Max(left.Right, right.Right) - Math.Min(left.Left, right.Left),
        Math.Max(left.Bottom, right.Bottom) - Math.Min(left.Top, right.Top));

    private static bool IsUsefulRegion(OcrTextRegion region, Size imageSize, AutoOcrOptions options)
    {
        if (region.Bounds.Width <= 0 || region.Bounds.Height <= 0)
            return false;

        double areaRatio = region.Bounds.Width * region.Bounds.Height
            / Math.Max(1, imageSize.Width * imageSize.Height);
        double minSide = Math.Min(region.Bounds.Width, region.Bounds.Height);

        return areaRatio >= options.MinRegionAreaRatio && minSide >= options.MinRegionSide;
    }

    private static bool ShouldMerge(Rect left, Rect right, Size imageSize, AutoOcrOptions options)
    {
        double padding = Math.Max(imageSize.Width, imageSize.Height) * options.MergePaddingRatio;
        var expanded = new Rect(
            left.Left - padding,
            left.Top - padding,
            left.Width + padding * 2,
            left.Height + padding * 2);

        if (expanded.IntersectsWith(right))
        {
            double xOverlap = Math.Min(left.Right, right.Right) - Math.Max(left.Left, right.Left);
            double yOverlap = Math.Min(left.Bottom, right.Bottom) - Math.Max(left.Top, right.Top);

            if (xOverlap > -Math.Min(left.Width, right.Width) * 0.5
                || yOverlap > -Math.Min(left.Height, right.Height) * 0.5)
                return true;
        }

        double distance = Distance(Center(left), Center(right));
        double maxDistance = Math.Max(
            options.MergeMaxDistance,
            Math.Max((left.Width + right.Width) / 2, (left.Height + right.Height) / 2)
                * options.MergeDistanceScale);

        return distance <= maxDistance;
    }

    private static (OcrTextRegion First, OcrTextRegion Second) OrderForTextMerge(
        OcrTextRegion left,
        OcrTextRegion right,
        bool vertical,
        bool rightToLeft)
    {
        int comparison = vertical
            ? CompareVerticalOrder(left, right)
            : CompareHorizontalOrder(left, right, rightToLeft);

        return comparison <= 0 ? (left, right) : (right, left);
    }

    private static int CompareHorizontalOrder(
        OcrTextRegion left,
        OcrTextRegion right,
        bool rightToLeft)
    {
        double leftCenterY = left.Bounds.Top + left.Bounds.Height / 2;
        double rightCenterY = right.Bounds.Top + right.Bounds.Height / 2;
        double rowDelta = leftCenterY - rightCenterY;
        double tolerance = Math.Max(12, Math.Min(left.Bounds.Height, right.Bounds.Height) * 0.8);

        if (Math.Abs(rowDelta) > tolerance)
            return rowDelta.CompareTo(0);

        double leftX = left.Bounds.Left + left.Bounds.Width / 2;
        double rightX = right.Bounds.Left + right.Bounds.Width / 2;
        return rightToLeft
            ? rightX.CompareTo(leftX)
            : leftX.CompareTo(rightX);
    }

    private static int CompareVerticalOrder(OcrTextRegion left, OcrTextRegion right)
    {
        double leftCenterX = left.Bounds.Left + left.Bounds.Width / 2;
        double rightCenterX = right.Bounds.Left + right.Bounds.Width / 2;
        double columnDelta = leftCenterX - rightCenterX;
        double tolerance = Math.Max(12, Math.Min(left.Bounds.Width, right.Bounds.Width) * 0.8);

        if (Math.Abs(columnDelta) > tolerance)
            return rightCenterX.CompareTo(leftCenterX);

        double leftY = left.Bounds.Top + left.Bounds.Height / 2;
        double rightY = right.Bounds.Top + right.Bounds.Height / 2;
        return leftY.CompareTo(rightY);
    }

    private static IReadOnlyList<OcrTextRegion> SortHorizontal(
        IReadOnlyList<OcrTextRegion> regions,
        bool rightToLeft)
    {
        double averageHeight = regions.Average(region => Math.Max(1, region.Bounds.Height));
        double rowTolerance = Math.Max(24, averageHeight * 0.8);
        var rows = new List<List<OcrTextRegion>>();

        foreach (var region in regions.OrderBy(region => region.Bounds.Top))
        {
            double centerY = region.Bounds.Top + region.Bounds.Height / 2;
            var row = rows.FirstOrDefault(row =>
                Math.Abs(row.Average(item => item.Bounds.Top + item.Bounds.Height / 2) - centerY)
                    <= rowTolerance);

            if (row == null)
            {
                row = [];
                rows.Add(row);
            }

            row.Add(region);
        }

        return rows
            .OrderBy(row => row.Min(region => region.Bounds.Top))
            .SelectMany(row => rightToLeft
                ? row.OrderByDescending(region => region.Bounds.Left + region.Bounds.Width / 2)
                    .ThenBy(region => region.Bounds.Top)
                : row.OrderBy(region => region.Bounds.Left)
                    .ThenBy(region => region.Bounds.Top))
            .ToList();
    }

    private static IReadOnlyList<OcrTextRegion> SortVertical(
        IReadOnlyList<OcrTextRegion> regions,
        bool rightToLeft)
    {
        double averageWidth = regions.Average(region => Math.Max(1, region.Bounds.Width));
        double columnTolerance = Math.Max(24, averageWidth * 0.8);
        var columns = new List<List<OcrTextRegion>>();

        foreach (var region in regions.OrderBy(region => region.Bounds.Left))
        {
            double centerX = region.Bounds.Left + region.Bounds.Width / 2;
            var column = columns.FirstOrDefault(column =>
                Math.Abs(column.Average(item => item.Bounds.Left + item.Bounds.Width / 2) - centerX)
                    <= columnTolerance);

            if (column == null)
            {
                column = [];
                columns.Add(column);
            }

            column.Add(region);
        }

        return (rightToLeft
                ? columns.OrderByDescending(column => column.Min(region => region.Bounds.Left))
                : columns.OrderBy(column => column.Min(region => region.Bounds.Left)))
            .SelectMany(column => column.OrderBy(region => region.Bounds.Top))
            .ToList();
    }

    private static Point Center(Rect rect) =>
        new(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);

    private static double Distance(Point left, Point right)
    {
        double dx = left.X - right.X;
        double dy = left.Y - right.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
