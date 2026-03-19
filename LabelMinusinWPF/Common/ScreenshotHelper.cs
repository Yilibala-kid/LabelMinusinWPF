using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LabelMinusinWPF.Common;

public static class ScreenshotHelper
{
    public const string DefaultFolderName = "ScreenShottemp";

    #region 图片合并

    /// <summary>图片 + 底部标签文字</summary>
    public static BitmapSource? CombineWithFooter(BitmapSource? img, string? labels)
    {
        if (img == null) return null;

        string[] lines = string.IsNullOrEmpty(labels) ? [] : labels.Split('\n');
        int count = lines.Length;
        double mainW = img.PixelWidth, mainH = img.PixelHeight;
        double footerH = Math.Clamp(mainH * 0.12, 50, 150) + Math.Min(count * 30, mainH * 0.5);
        double canvasW = mainW, canvasH = mainH + footerH;

        var rtb = new RenderTargetBitmap((int)canvasW, (int)canvasH, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // 背景
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, canvasW, canvasH));
            dc.DrawImage(img, new Rect(0, 0, mainW, mainH));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(255, 245, 238)), null, new Rect(0, mainH, canvasW, footerH));

            if (count > 0)
            {
                double fontSize = Math.Clamp(footerH * 0.6 / count, 12, 20);
                var tf = new Typeface(new FontFamily("Microsoft YaHei"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                double dpi = VisualTreeHelper.GetDpi(visual).PixelsPerDip;
                double startY = mainH + (footerH - count * fontSize * 1.2) / 2;

                for (int i = 0; i < count; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var ft = new FormattedText(line, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, tf, fontSize, Brushes.Black, dpi);
                    while (ft.Width > canvasW * 0.95 && fontSize > 8)
                        ft = new FormattedText(line, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, tf, --fontSize, Brushes.Black, dpi);
                    dc.DrawText(ft, new Point(canvasW * 0.03, startY + i * fontSize * 1.2));
                }
            }
        }
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    /// <summary>合并两张图片（左右排列）+ 页脚</summary>
    public static BitmapSource? CombineTwoImages(BitmapSource? left, BitmapSource? right, string footer)
    {
        if (left == null && right == null) return null;

        double lW = left?.PixelWidth ?? 0, lH = left?.PixelHeight ?? 0;
        double rW = right?.PixelWidth ?? 0, rH = right?.PixelHeight ?? 0;
        double mainW = lW + rW, mainH = Math.Max(lH, rH);
        double footerH = Math.Clamp(mainH * 0.12, 50, 150);
        double canvasW = mainW, canvasH = mainH + footerH;

        var rtb = new RenderTargetBitmap((int)canvasW, (int)canvasH, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // 背景
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, canvasW, canvasH));
            if (left != null) dc.DrawImage(left, new Rect(0, 0, lW, lH));
            if (right != null) dc.DrawImage(right, new Rect(lW, 0, rW, rH));

            // 分割线
            if (left != null && right != null)
                dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(65, 105, 225)), 2), new Point(lW, 0), new Point(lW, mainH));

            // 页脚
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(255, 245, 238)), null, new Rect(0, mainH, canvasW, footerH));

            double fontSize = Math.Max(footerH * 0.7, 14);
            var tf = new Typeface(new FontFamily("Microsoft YaHei"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            var ft = new FormattedText($"▲ {footer}", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, tf, fontSize, Brushes.RoyalBlue, VisualTreeHelper.GetDpi(visual).PixelsPerDip);
            while (ft.Width > canvasW * 0.9 && fontSize > 14)
                ft = new FormattedText($"▲ {footer}", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, tf, --fontSize, Brushes.RoyalBlue, VisualTreeHelper.GetDpi(visual).PixelsPerDip);
            dc.DrawText(ft, new Point((canvasW - ft.Width) / 2, mainH + (footerH - ft.Height) / 2));
        }
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    #endregion

    #region 图片保存

    /// <summary>保存并复制到剪贴板</summary>
    public static string? SaveAndCopyToClipboard(BitmapSource? bmp, string? name, string folder = DefaultFolderName)
    {
        if (bmp == null) return null;

        string folderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, folder);
        string filePath = Path.Combine(folderPath, $"{name ?? $"Capture_{DateTime.Now:yyyyMMdd_HHmmss}"}.jpg".Replace(".jpg.jpg", ""));
        Directory.CreateDirectory(folderPath);

        byte[] data;
        using (var ms = new MemoryStream())
        {
            var encoder = new JpegBitmapEncoder { QualityLevel = 75 };
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            encoder.Save(ms);
            data = ms.ToArray();
        }
        File.WriteAllBytes(filePath, data);

        try { Clipboard.SetImage(new JpegBitmapDecoder(new MemoryStream(data), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames.First()); }
        catch { }
        return filePath;
    }

    /// <summary>保存图片（迭代压缩直到满足条件）</summary>
    public static (string FilePath, byte[] Data)? SaveWithCompression(BitmapSource? bmp, string? name, string folder = DefaultFolderName,
        int startQuality = 100, int minQuality = 20, long maxSizeBytes = 1024 * 1024)
    {
        if (bmp == null) return null;

        string folderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, folder);
        string filePath = Path.Combine(folderPath, $"{name ?? $"Capture_{DateTime.Now:yyyyMMdd_HHmmss}"}.jpg".Replace(".jpg.jpg", ""));
        Directory.CreateDirectory(folderPath);

        int quality = startQuality;
        byte[] data;
        do
        {
            using var ms = new MemoryStream();
            var encoder = new JpegBitmapEncoder { QualityLevel = quality };
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            encoder.Save(ms);
            data = ms.ToArray();
            if (data.Length <= maxSizeBytes || quality <= minQuality) break;
            quality -= 10;
        } while (true);

        File.WriteAllBytes(filePath, data);
        return (filePath, data);
    }

    /// <summary>截图并保存（一步完成）</summary>
    public static string? CaptureAndSave(BitmapSource? img, string? labels, string? name, string folder = DefaultFolderName)
        => SaveAndCopyToClipboard(CombineWithFooter(img, labels), name, folder);

    /// <summary>合并两张图片并保存（一步完成）</summary>
    public static (string FilePath, byte[] Data)? SaveTwoImages(BitmapSource? left, BitmapSource? right, string footer,
        string folder = DefaultFolderName, int startQuality = 100, int minQuality = 20, long maxSizeBytes = 1024 * 1024)
    {
        var combined = CombineTwoImages(left, right, footer);
        return combined != null ? SaveWithCompression(combined, null, folder, startQuality, minQuality, maxSizeBytes) : null;
    }

    #endregion

    #region 图片裁剪

    /// <summary>裁剪图片区域</summary>
    public static BitmapSource? CropRegion(BitmapSource? bmp, Rect r)
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

    /// <summary>裁剪区域 + 标签渲染</summary>
    public static (BitmapSource? Image, List<OneLabel> Labels)? CaptureRegionWithLabels(BitmapSource? bmp, Rect r, IEnumerable<OneLabel> labels)
    {
        if (bmp == null) return null;
        var cropped = CropRegion(bmp, r);
        if (cropped == null) return null;

        var regionLabels = GetLabelsInRegion(r, labels);
        if (regionLabels.Count == 0) return (cropped, regionLabels);

        try
        {
            var style = SelfControls.LabelStyleManager.Instance;
            int w = cropped.PixelWidth, h = cropped.PixelHeight;
            double scale = style.LabelScale, rw = r.Width, rh = r.Height;
            var dotStyle = style.DotStyle;
            var tf = new Typeface(new FontFamily("Microsoft YaHei"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawImage(cropped, new Rect(0, 0, w, h));
                double dpi = VisualTreeHelper.GetDpi(visual).PixelsPerDip;
                foreach (var lb in regionLabels)
                {
                    double lx = (lb.X - r.X) / rw * w, ly = (lb.Y - r.Y) / rh * h;
                    var brush = lb.GroupBrush ?? Brushes.Red;
                    if (dotStyle == SelfControls.DotStyleType.Circle)
                        dc.DrawEllipse(brush, new Pen(Brushes.White, Math.Max(1, scale)), new Point(lx, ly), 9 * scale, 9 * scale);
                    else if (dotStyle == SelfControls.DotStyleType.Square)
                        dc.DrawRectangle(brush, new Pen(Brushes.White, Math.Max(1, scale)), new Rect(lx - 9 * scale, ly - 9 * scale, 18 * scale, 18 * scale));

                    var ft = new FormattedText(lb.Index.ToString(), System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, tf, 10 * scale, Brushes.White, dpi);
                    dc.DrawText(ft, new Point(lx - ft.Width / 2, ly - ft.Height / 2));
                }
            }
            rtb.Render(visual);
            rtb.Freeze();
            return (rtb, regionLabels);
        }
        catch { return (cropped, regionLabels); }
    }

    /// <summary>获取区域内的标签</summary>
    public static List<OneLabel> GetLabelsInRegion(Rect r, IEnumerable<OneLabel> labels)
        => [.. labels.Where(l => l.X >= r.X && l.X <= r.X + r.Width && l.Y >= r.Y && l.Y <= r.Y + r.Height)];

    #endregion
}
