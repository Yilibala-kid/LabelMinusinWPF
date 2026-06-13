using System.IO;

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
    public const string ModelsSubDir = "models";
    public const string OcrTemp = "OCRtemp";
    public const string AutoOcrSubDir = "AutoOCR";
    public const string PaddleOcrV6SubDir = "v6";
    public const string PaddleOcrOfficialModelsSubDir = "official_models";
    public const string PaddleOcrV6DetectionModel = "PP-OCRv6_medium_det";
    public const string PaddleOcrV6RecognitionModel = "PP-OCRv6_medium_rec";
    public const int DownloadBufferSize = 81920;

    public static string PaddleOcrV6ModelRoot =>
        Path.Combine(AppContext.BaseDirectory, ModelsSubDir, PaddleOcrV6SubDir);

    public static string UserPaddleXOfficialModelRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".paddlex",
            PaddleOcrOfficialModelsSubDir);
}
