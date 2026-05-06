using System.IO;
using System.Linq;

namespace LabelMinusinWPF.OCRService;

public static class OcrEnvironment
{
    private static string PythonExe => Path.Combine(AppContext.BaseDirectory, "python", "python.exe");
    private static string MangaOcrScript => Path.Combine(AppContext.BaseDirectory, "models", "manga-ocr", "manga_ocr_infer.py");

    public static bool IsPythonInstalled => File.Exists(PythonExe);
    public static bool IsMangaOcrScriptReady => File.Exists(MangaOcrScript);
    public static bool IsMangaOcrModelReady =>
        Directory.Exists(Path.Combine(AppContext.BaseDirectory, "models", "manga-ocr", "model")) &&
        Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "models", "manga-ocr", "model")).Any();
    public static bool HasOnnxModels =>
        OcrPipeline.ScanModels().Any(m => PpOcrV5RapidOcrProvider.CanHandleEngine(m.Engine));
    public static bool IsMangaOcrRunning => MangaOcrProvider.SharedProcess != null;

    public static bool ReadyForProcessStart => IsPythonInstalled && IsMangaOcrScriptReady && IsMangaOcrModelReady;

    public static string GetSummary()
    {
        bool py = IsPythonInstalled, onnx = HasOnnxModels, script = IsMangaOcrScriptReady;

        if (!onnx && !py) return "OCR 环境未就绪：缺少 ONNX 模型和 Python 环境";
        if (!onnx) return "OCR 环境未就绪：缺少 ONNX 模型";
        if (!py) return "仅支持一键打点（一键识别和截图 OCR 需要 Python 环境）";
        if (!script) return "Python 已安装，但 manga-ocr 脚本缺失";
        if (!IsMangaOcrModelReady) return "Python 已安装，但 manga-ocr 模型未下载";
        if (!IsMangaOcrRunning) return "环境就绪，请点击 OCR 开关启动 ocr 模型";
        return "OCR 环境已就绪";
    }
}
