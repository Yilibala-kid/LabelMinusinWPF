using System.IO;              // File、Path 文件操作
using System.Text.Json;        // JsonDocument、JsonElement 解析 model.json
using System.Windows;          // Rect 矩形类型（文字区域边界）

namespace LabelMinusinWPF.OCRService;

// IOcrProvider — OCR 提供者统一接口
// 所有 OCR 引擎（PpOcrV5RapidOcrProvider、MangaOcrProvider）均实现此接口
// OcrPipeline 通过此接口调用具体引擎，引擎可替换
// ============================================================================

public interface IOcrProvider
{
    // 对单张图片执行 OCR 识别，返回文字区域列表
    // imagePath  : 图片文件路径（本地路径）
    // model      : 模型配置信息（路径、类型）
    // options    : OCR 选项（阈值、合并策略、输出模式）
    // ct        : 取消令牌
    Task<IReadOnlyList<OcrTextRegion>> RecognizeAsync(
        string imagePath, OcrModelInfo model, AutoOcrOptions options, CancellationToken ct);

    /// <summary>
    /// 若 RecognizeAsync 已在内部完成文本块合并，则为 true。
    /// true 时 OcrPipeline 会跳过自身的 BuildTextBlocks 步骤。
    /// </summary>
    bool MergesRegionsInternally => false;
}

// OcrModelInfo — 单个 OCR 模型的配置信息（对应一个 model.json）
// 描述模型名称、引擎类型、文件目录、及其包含的模型文件路径
// ============================================================================

public sealed record OcrModelInfo(
    string Name,                        // 模型显示名称（如 "ch_PP-OCRv5_rec_server"）
    string Engine,                       // 引擎类型（如 "PpOcrV5RapidOcr"、"MangaOcr"）
    string DirectoryPath,               // 模型所在目录的完整路径
    string ManifestPath,                // model.json 文件的完整路径
    IReadOnlyDictionary<string, string> Files) // 其他模型文件的相对路径（如 detModel、recModel）
{
    /// <summary>根据 key 获取模型文件的完整路径</summary>
    public string? GetFilePath(string key) =>
        // 从 Files 字典查找 key 对应的相对路径
        // 组合为 DirectoryPath + 相对路径 返回
        Files.TryGetValue(key, out var path) && !string.IsNullOrWhiteSpace(path)
            ? Path.Combine(DirectoryPath, path) : null;

    /// <summary>
    /// 尝试从目录中读取 model.json 并解析为 OcrModelInfo。
    /// 若目录不存在或 model.json 解析失败，返回 null。
    /// </summary>
    internal static OcrModelInfo? TryRead(string modelDirectory)
    {
        // model.json 约定放在模型目录的根目录下
        string manifestPath = Path.Combine(modelDirectory, "model.json");

        // 文件不存在则跳过（不是有效模型目录）
        if (!File.Exists(manifestPath))
            return null;

        try
        {
            // 打开文件流并解析 JSON
            using var stream = File.OpenRead(manifestPath);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            // 读取 name（兼容 "name" 和 "id" 两种字段名）
            // 若均不存在则用目录名作为默认值
            string name = GetString(root, "name")
                ?? GetString(root, "id")
                ?? Path.GetFileName(modelDirectory);

            // 读取 engine 字段（如 "PpOcrV5RapidOcr"）
            string engine = GetString(root, "engine") ?? "";

            // 将 JSON 中除元数据字段外的所有字符串字段作为模型文件路径读取
            return new OcrModelInfo(
                name, engine, modelDirectory, manifestPath, ReadFiles(root));
        }
        catch
        {
            // JSON 格式错误或 IO 异常，返回 null（静默跳过）
            return null;
        }
    }

    /// <summary>
    /// 从 JSON 根元素中提取除元数据（id/name/engine/language）外的所有文件路径字段。
    /// 这些字段的键名（如 detModel、recModel）和值（相对路径）存入字典。
    /// </summary>
    private static Dictionary<string, string> ReadFiles(JsonElement root)
    {
        // 字典 key 不区分大小写（model.json 中字段名大小写不统一）
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 遍历 JSON 对象所有属性
        foreach (var property in root.EnumerateObject())
        {
            // 只处理字符串类型的值（非字符串字段如嵌套对象忽略）
            if (property.Value.ValueKind != JsonValueKind.String) continue;

            // 跳过元数据字段（这些不是文件路径）
            if (property.Name is not ("id" or "name" or "engine" or "language"))
                files[property.Name] = property.Value.GetString() ?? "";
        }

        return files;
    }

    /// <summary>
    /// 安全读取 JSON 对象的字符串属性：若字段不存在或类型不匹配，返回 null。
    /// </summary>
    private static string? GetString(JsonElement element, string propertyName) =>
        // 尝试获取指定属性，且类型为字符串
        element.TryGetProperty(propertyName, out var p)
            && p.ValueKind == JsonValueKind.String
            ? p.GetString() : null;
}

// OcrOutputMode — OCR 输出模式枚举
// ============================================================================

// RecognizedText : 输出识别出的文字内容（用于翻译场景）
// PositionOnly   : 只输出文字位置，不填文字内容（用于打点场景）
public enum OcrOutputMode { RecognizedText, PositionOnly }

// AutoOcrOptions — OCR 行为配置（后处理参数）
// ============================================================================

public sealed record AutoOcrOptions(
    double MinConfidence = 0.6,           // 最低置信度阈值，低于此值的结果被过滤
    OcrOutputMode OutputMode = OcrOutputMode.RecognizedText, // 输出模式：文字 or 仅位置
    bool MergeTextLines = false,         // 是否合并相邻文本块（横排长句需合并）
    bool RightToLeft = false,            // 横排时文字阅读方向（中文从左到右，日语漫画从右到左）
    double MinRegionAreaRatio = 0.00002,  // 最小区域面积（占图片面积比，过小为噪点）
    double MinRegionSide = 4,            // 最小边长（像素，过小为噪点）
    double MergePaddingRatio = 0.025,     // 合并时扩展间距（按图片尺寸比例）
    double MergeMaxDistance = 72,         // 最大合并距离阈值（像素，绝对值）
    double MergeDistanceScale = 2.2,     // 最大合并距离（按块尺寸比例，优先用比例）
    bool DeduplicateRegions = true,      // 是否去重（过滤中心点过近的区域）
    double DeduplicateDistance = 0.003)  // 去重阈值（归一化 0~1，超过视为重复）
{
    public static AutoOcrOptions Default { get; } = new(
        MinConfidence: 0.6,
        MergeTextLines: true,
        MergePaddingRatio: 0.008,
        MergeMaxDistance: 16,
        MergeDistanceScale: 0.6,
        DeduplicateRegions: true,
        DeduplicateDistance: 0.002);
}

// OcrTextRegion — 单个文字识别区域
// ============================================================================

// Text      : 识别出的文字内容（PositionOnly 模式下为空字符串）
// Bounds    : 文字区域边界矩形（像素单位）
// Confidence: 置信度 0~1（PpOcrV5 返回实际分数，MangaOcr 固定 1.0）
public sealed record OcrTextRegion(string Text, Rect Bounds, double Confidence);

// AutoOcrResult — OCR 批量处理结果
// ============================================================================

public sealed record AutoOcrResult(
    bool Success,
    int ImageCount,
    int LabelCount,
    string Message);
