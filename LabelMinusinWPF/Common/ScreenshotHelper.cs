using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LabelMinusinWPF.Common;

public static class ScreenshotHelper
{
    public const string DefaultFolder = Constants.TempFolders.ScreenShotTemp;
    public sealed record ScreenshotSaveResult(string FilePath, BitmapSource? PreviewImage);

    public static string GetFolder(string folder = DefaultFolder)
    {
        string folderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, folder);
        Directory.CreateDirectory(folderPath);
        return folderPath;
    }

    public static BitmapSource? Freeze(BitmapSource? source)
    {
        if (source == null || source.IsFrozen) return source;
        var clone = new WriteableBitmap(source);
        clone.Freeze();
        return clone;
    }

    // Decode the saved JPEG bytes so callers can reuse the exact compressed preview.
    private static BitmapSource? DecodeJpegPreview(byte[]? data)
    {
        if (data == null || data.Length == 0) return null;

        using var ms = new MemoryStream(data);
        var decoder = new JpegBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        return Freeze(decoder.Frames.FirstOrDefault());
    }

    public static bool SetClipboard(BitmapSource? image)
    {
        var frozen = Freeze(image);
        if (frozen == null) return false;

        try { Clipboard.SetImage(frozen); return true; }
        catch { return false; }
    }

    public static BitmapSource? GetClipboard()
    {
        try { return Clipboard.ContainsImage() ? Freeze(Clipboard.GetImage()) : null; }
        catch { return null; }
    }


    #region 图片合并

    /// <summary>合并图片（支持单图/多图横排）+ 页脚</summary>
    public static BitmapSource? Combine(BitmapSource?[] images, string? footer)
    {
        var validImages = images
            .Select(Freeze)
            .OfType<BitmapSource>()
            .ToList();
        if (validImages.Count == 0) return null;

        double[] widths = validImages.Select(i => (double)i.PixelWidth).ToArray();
        double[] heights = validImages.Select(i => (double)i.PixelHeight).ToArray();
        double mainW = widths.Sum();
        double mainH = heights.Max();
        double footerH = Math.Clamp(mainH * 0.12, 50, 150);

        double canvasW = mainW, canvasH = mainH + footerH;
        var rtb = new RenderTargetBitmap((int)canvasW, (int)canvasH, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, canvasW, canvasH));

            double x = 0;
            for (int i = 0; i < validImages.Count; i++)
            {
                dc.DrawImage(validImages[i], new Rect(x, 0, widths[i], heights[i]));
                x += widths[i];
                if (i < validImages.Count - 1)
                    dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(65, 105, 225)), 10), new Point(x, 0), new Point(x, mainH));
            }

            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(255, 245, 238)), null, new Rect(0, mainH, canvasW, footerH));

            if (!string.IsNullOrEmpty(footer))
            {
                double fontSize = Math.Max(footerH * 0.7, 14);
                var tf = new Typeface(new FontFamily("Microsoft YaHei"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
                var ft = new FormattedText($"▲ {footer}", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, tf, fontSize, Brushes.RoyalBlue, VisualTreeHelper.GetDpi(visual).PixelsPerDip);
                while (ft.Width > canvasW * 0.9 && fontSize > 14)
                    ft = new FormattedText($"▲ {footer}", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, tf, --fontSize, Brushes.RoyalBlue, VisualTreeHelper.GetDpi(visual).PixelsPerDip);
                dc.DrawText(ft, new Point((canvasW - ft.Width) / 2, mainH + (footerH - ft.Height) / 2));
            }
        }
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    #endregion

    #region 图片保存

    public static ScreenshotSaveResult? SaveSnip(BitmapSource? bmp, string? name = null, string folder = DefaultFolder,
        int startQuality = 100, int minQuality = 20, long maxSizeBytes = 1024 * 1024)
    {
        var frozen = Freeze(bmp);
        if (frozen == null) return null;

        string filePath = Path.Combine(GetFolder(folder), $"{name ?? $"Capture_{DateTime.Now:yyyyMMdd_HHmmss}"}.jpg".Replace(".jpg.jpg", ""));
        int quality = startQuality;
        byte[] data;

        do
        {
            using var ms = new MemoryStream();
            var encoder = new JpegBitmapEncoder { QualityLevel = quality };
            encoder.Frames.Add(BitmapFrame.Create(frozen));
            encoder.Save(ms);
            data = ms.ToArray();
            if (data.Length <= maxSizeBytes || quality <= minQuality) break;
            quality -= 10;
        } while (true);

        File.WriteAllBytes(filePath, data);
        return new ScreenshotSaveResult(filePath, DecodeJpegPreview(data));
    }

    public static BitmapSource? MergeInk(BitmapSource? source, StrokeCollection? strokes, Size displaySize)
    {
        if (source == null) return null;
        if (strokes == null || strokes.Count == 0) return Freeze(source);
        if (displaySize.Width <= 0 || displaySize.Height <= 0) return Freeze(source);

        var rtb = new RenderTargetBitmap(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();

        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawImage(source, new Rect(0, 0, source.PixelWidth, source.PixelHeight));

            double scaleX = source.PixelWidth / displaySize.Width;
            double scaleY = source.PixelHeight / displaySize.Height;
            dc.PushGuidelineSet(new GuidelineSet());
            dc.PushTransform(new ScaleTransform(scaleX, scaleY));
            strokes.Draw(dc);
            dc.Pop();
            dc.Pop();
        }

        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    #endregion

    #region 图片裁剪

    /// <summary>裁剪图片区域</summary>
    public static BitmapSource? Crop(BitmapSource? bmp, Rect r)
    {
        if (bmp == null) return null;
        int w = bmp.PixelWidth, h = bmp.PixelHeight;
        int x = Math.Clamp((int)(r.X * w), 0, w);
        int y = Math.Clamp((int)(r.Y * h), 0, h);
        int w2 = Math.Clamp((int)(r.Width * w), 1, w - x);
        int h2 = Math.Clamp((int)(r.Height * h), 1, h - y);
        try { return new CroppedBitmap(bmp, new Int32Rect(x, y, w2, h2)); }
        catch { return null; }
    }

    #endregion
}
