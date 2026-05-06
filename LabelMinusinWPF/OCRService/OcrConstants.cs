namespace LabelMinusinWPF.OCRService;

public static class OcrConstants
{
    public static ICollection<string> OcrWebsiteKeys => Websites.Keys;
    public static readonly Dictionary<string, string> Websites = new()
    {
        ["识字体网 (LikeFont)"] = "https://www.likefont.com/",
        ["AI识别 (YuzuMarker)"] = "https://huggingface.co/spaces/gyrojeff/YuzuMarker.FontDetection",
        ["必应"] = "https://www.bing.com/visualsearch"
    };

    public const string DefaultWebsite = "AI识别 (YuzuMarker)";
    public const string OcrTemp = "OCRtemp";
    public const string AutoOcrSubDir = "AutoOCR";
    public const int DownloadBufferSize = 81920;
}
