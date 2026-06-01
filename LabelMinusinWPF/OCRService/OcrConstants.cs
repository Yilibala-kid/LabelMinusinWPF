namespace LabelMinusinWPF.OCRService;

public static class OcrConstants
{
    public static ICollection<string> OcrWebsiteKeys => Websites.Keys;
    public static readonly Dictionary<string, string> Websites = new()
    {
        ["LikeFont 识字体网"] = "https://www.likefont.com/",
        ["YuzuMarker 字体识别"] = "https://huggingface.co/spaces/gyrojeff/YuzuMarker.FontDetection",
        ["Bing 视觉搜索"] = "https://www.bing.com/visualsearch"
    };

    public const string DefaultWebsite = "YuzuMarker 字体识别";
    public const string OcrTemp = "OCRtemp";
    public const string AutoOcrSubDir = "AutoOCR";
    public const int DownloadBufferSize = 81920;
}
