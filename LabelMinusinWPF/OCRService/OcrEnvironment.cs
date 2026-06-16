using System.IO;

namespace LabelMinusinWPF.OCRService;

public static class OcrEnvironment
{
    private static string PythonExe => Path.Combine(AppContext.BaseDirectory, "python", "python.exe");
    private static string PaddleOcrScript => Path.Combine(AppContext.BaseDirectory, OcrConstants.ModelsSubDir, "paddle-ocr", "paddle_ocr_infer.py");
    private static string MangaOcrScript => Path.Combine(AppContext.BaseDirectory, OcrConstants.ModelsSubDir, "manga-ocr", "manga_ocr_infer.py");

    public static bool IsPythonInstalled => File.Exists(PythonExe);

    private static bool IsPaddleOcrScriptReady => File.Exists(PaddleOcrScript);

    private static bool HasPaddleOcrConfig =>
        OcrPipeline.ScanModels().Any(m => PaddleOcrPythonProvider.CanHandleEngine(m.Engine));

    private static bool IsMangaOcrScriptReady => File.Exists(MangaOcrScript);

    private static bool IsMangaOcrModelReady =>
        Directory.Exists(Path.Combine(AppContext.BaseDirectory, OcrConstants.ModelsSubDir, "manga-ocr", "model")) &&
        Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, OcrConstants.ModelsSubDir, "manga-ocr", "model")).Any();

    public static bool HasPaddleOcrModels =>
        HasPaddleOcrConfig &&
        HasPaddleOcrV6ModelFiles(OcrConstants.PaddleOcrV6DetectionModel) &&
        HasPaddleOcrV6ModelFiles(OcrConstants.PaddleOcrV6RecognitionModel);

    public static bool ReadyForPaddleOcr =>
        IsPythonInstalled &&
        IsPaddleOcrScriptReady &&
        HasPaddleOcrModels;

    public static bool ReadyForProcessStart =>
        ReadyForPaddleOcr &&
        IsMangaOcrScriptReady &&
        IsMangaOcrModelReady;

    public static string GetSummary()
    {
        bool py = IsPythonInstalled;
        bool paddle = HasPaddleOcrModels;
        bool paddleScript = IsPaddleOcrScriptReady;
        bool mangaScript = IsMangaOcrScriptReady;

        if (!HasPaddleOcrConfig && !py) return "OCR 环境未就绪：缺少 PP-OCRv6 配置和 Python 环境";
        if (!HasPaddleOcrConfig) return "OCR 环境未就绪：缺少 PP-OCRv6 配置";
        if (!py) return "PP-OCRv6 配置已就绪；官方 PaddleOCR pipeline 需要 Python 环境";
        if (!paddleScript) return "Python 已安装，但 PaddleOCR 脚本缺失";
        if (!paddle) return "Python 已安装，但 PP-OCRv6 模型权重未下载";
        if (!mangaScript) return "Python 已安装，但 manga-ocr 脚本缺失";
        if (!IsMangaOcrModelReady) return "Python 已安装，但 manga-ocr 模型未下载";
        if (!PaddleOcrPythonProvider.IsProcessRunning && !MangaOcrProvider.IsProcessRunning)
            return "OCR 环境已就绪；截图 OCR 开关会启动识别引擎";
        return "OCR 环境已就绪";
    }

    private static bool HasPaddleOcrV6ModelFiles(string modelName)
        => HasFiles(Path.Combine(OcrConstants.PaddleOcrV6ModelRoot, modelName)) ||
           HasFiles(Path.Combine(
               OcrConstants.PaddleOcrV6ModelRoot,
               OcrConstants.PaddleOcrOfficialModelsSubDir,
               modelName)) ||
           HasFiles(Path.Combine(OcrConstants.UserPaddleXOfficialModelRoot, modelName));

    private static bool HasFiles(string directory)
        => Directory.Exists(directory) &&
           Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Any();
}
