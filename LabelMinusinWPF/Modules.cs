using Microsoft.Win32;
using SharpCompress.Archives;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;

namespace LabelMinusinWPF
{
    /// <summary>
    /// 核心模块：LabelPlus 格式的文本解析与导出
    /// </summary>
    internal class Modules
    {
        #region LabelPlus文本处理

        // 预编译正则（static readonly 保证只编译一次）
        private static readonly Regex ImgRegex = new(@">>>>>>>>\[(.*?)\]<<<<<<<<", RegexOptions.Compiled);
        private static readonly Regex MetaRegex = new(@"----------------\[(\d+)\]----------------\[([\d\.]+),([\d\.]+),(\d+)\]", RegexOptions.Compiled);

        /// <summary>
        /// 将 LabelPlus 格式的文本解析为 图片名→ImageInfo 字典
        /// </summary>
        /// <param name="content">文本内容</param>
        /// <param name="sourceName">输出：关联的压缩包文件名（如果有）</param>
        public static Dictionary<string, ImageInfo> ParseTextToLabels(string content, out string? sourceName)
        {
            sourceName = null;
            var database = new Dictionary<string, ImageInfo>();
            var groupList = new List<string>(); // 从文件头读取的动态分组

            string[] lines = content.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
            string? currentImgName = null;
            ImageLabel? currentLabel = null;
            int hyphenCount = 0; // 追踪 "-" 分隔符出现次数，用于区分文件头各段

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                // 1. 文件头：用 "-" 分隔的段落
                if (line == "-") { hyphenCount++; continue; }
                if (hyphenCount == 1) { groupList.Add(line); continue; }

                // 2. 第二个 "-" 之后、首个图片标记之前：提取关联文件信息
                if (hyphenCount == 2 && currentImgName == null && line.StartsWith("关联文件:"))
                {
                    var path = line.Replace("关联文件:", "").Trim();
                    sourceName = string.IsNullOrEmpty(path) ? null : path;
                    continue;
                }

                // 3. 图片标记行：>>>>>>>>[ imageName ]<<<<<<<<
                var imgMatch = ImgRegex.Match(line);
                if (imgMatch.Success)
                {
                    currentImgName = imgMatch.Groups[1].Value;
                    database[currentImgName] = new ImageInfo { ImagePath = currentImgName };
                    currentLabel = null;
                    continue;
                }

                // 4. 标注元数据行：----------------[index]----------------[x,y,group]
                var metaMatch = MetaRegex.Match(line);
                if (metaMatch.Success && currentImgName != null)
                {
                    int groupIdx = int.Parse(metaMatch.Groups[4].Value);
                    string groupName = (groupIdx > 0 && groupIdx <= groupList.Count)
                                       ? groupList[groupIdx - 1]
                                       : (groupIdx == 2 ? "框外" : "框内");

                    currentLabel = new ImageLabel
                    {
                        Index = int.Parse(metaMatch.Groups[1].Value),
                        Position = new Point(float.Parse(metaMatch.Groups[2].Value), float.Parse(metaMatch.Groups[3].Value)),
                        Group = groupName,
                        Text = ""
                    };
                    database[currentImgName].Labels.Add(currentLabel);
                    continue;
                }

                // 5. 标注文本内容（多行累加）
                if (currentLabel != null && hyphenCount >= 2)
                {
                    currentLabel.Text = string.IsNullOrEmpty(currentLabel.Text)
                                        ? line
                                        : currentLabel.Text + Environment.NewLine + line;
                }
            }

            // 解析完毕后，将当前 Text 锁定为 OriginalText
            foreach (var img in database.Values)
                foreach (var lbl in img.Labels)
                    lbl.LoadBaseContent(lbl.Text);

            return database;
        }

        public enum ExportMode { Original, Current, Diff }

        /// <summary>
        /// 将图片列表导出为 LabelPlus 格式文本
        /// </summary>
        /// <param name="images">图片集合</param>
        /// <param name="sourceName">关联文件名（压缩包名），可为 null</param>
        /// <param name="mode">导出模式：Original/Current/Diff</param>
        public static string LabelsToText(IEnumerable<ImageInfo> images, string? sourceName, ExportMode mode = ExportMode.Current)
        {
            var imageList = images.ToList();
            if (imageList.Count == 0) return string.Empty;

            var sb = new StringBuilder();

            // --- 1. 收集所有分组并建立 分组名→ID 映射 ---
            var allGroups = imageList
                    .SelectMany(img => img.Labels)
                    .Select(l => l.Group).Distinct()
                    .OrderBy(g => g == "框内" ? 0 : (g == "框外" ? 1 : 2))
                    .ThenBy(g => g).ToList();

            if (allGroups.Count == 0) { allGroups.Add("框内"); allGroups.Add("框外"); }

            var groupToIdMap = allGroups
                .Select((g, i) => (Name: g, Id: i + 1))
                .ToDictionary(x => x.Name, x => x.Id);

            // --- 写入文件头 ---
            sb.AppendLine("1,0\n-\n" + string.Join("\n", allGroups) + "\n-\n");
            sb.AppendLine($"关联文件:{sourceName}");
            sb.AppendLine($"最后修改时间:{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");

            // --- 2. 遍历图片 ---
            foreach (var imageInfo in imageList.OrderBy(img => img.ImageName))
            {
                // Diff 模式：跳过无变动的图片
                if (mode == ExportMode.Diff && !imageInfo.Labels.Any(l => l.IsModified))
                    continue;

                string pureName = Path.GetFileName(imageInfo.ImagePath ?? imageInfo.ImageName);
                sb.AppendLine($">>>>>>>>[{pureName}]<<<<<<<<");

                // --- 3. 遍历标注 ---
                foreach (var label in imageInfo.Labels.OrderBy(l => l.Index))
                {
                    // 根据模式过滤标签
                    if (mode == ExportMode.Diff && !label.IsModified) continue;
                    if (mode != ExportMode.Diff && label.IsDeleted) continue;

                    // 写入坐标和组信息（查不到分组时默认为 1）
                    int groupValue = groupToIdMap.GetValueOrDefault(label.Group, 1);
                    sb.AppendLine($"----------------[{label.Index}]----------------[{label.X:F3},{label.Y:F3},{groupValue}]");

                    // --- 4. 写入文本内容 ---
                    if (mode == ExportMode.Diff)
                    {
                        if (label.IsDeleted)
                        {
                            sb.AppendLine($"- [已删除]\n{label.OriginalText}");
                        }
                        else if (string.IsNullOrEmpty(label.OriginalText))
                        {
                            sb.AppendLine($"+ [新增]\n{label.Text}");
                        }
                        else
                        {
                            // 关键改进：紧凑的改前改后对比
                            sb.AppendLine($"* [原文]: {label.OriginalText.Replace("\n", " ")}");
                            sb.AppendLine($"{label.Text}");
                        }
                    }
                    else
                    {
                        sb.AppendLine(mode == ExportMode.Original ? label.OriginalText : label.Text);
                    }
                    sb.AppendLine();
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }
        #endregion
    }

    /// <summary>
    /// 压缩包操作辅助类：图片路径提取、文件解压缓存、相邻图片预加载
    /// </summary>
    public class ArchiveHelper
    {
        /// <summary>支持的图片后缀（不区分大小写）</summary>
        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
            { ".jpg", ".jpeg", ".png", ".bmp", ".webp" };

        /// <summary>解压缓存目录（位于 exe 同级 ArchiveTemp 文件夹下）</summary>
        private static readonly string TempFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ArchiveTemp");

        /// <summary>
        /// 获取压缩包内所有图片的路径列表
        /// </summary>
        public static List<string> GetImagePath(string archivePath)
        {
            using var archive = ArchiveFactory.OpenArchive(archivePath);

            return archive.Entries
                .Where(entry => !entry.IsDirectory)
                .Select(entry => new { Key = entry.Key, Ext = Path.GetExtension(entry.Key) })
                // 关键修复：确保扩展名不为 null 且存在于集合中
                .Where(x => x.Ext is string ext && ImageExtensions.Contains(ext))
                .Select(x => Path.GetFullPath(Path.Combine(archivePath, x.Key)))
                .ToList();
        }

        /// <summary>
        /// 从压缩包中提取指定文件为 byte[]（带磁盘缓存）
        /// </summary>
        public static byte[]? ExtractFileToBytes(string archivePath, string fileName)
        {
            if (string.IsNullOrEmpty(archivePath) || string.IsNullOrEmpty(fileName)) return null;

            // 以压缩包名建子文件夹，防止不同压缩包同名文件冲突
            string archiveName = Path.GetFileNameWithoutExtension(archivePath);
            string targetDir = Path.Combine(TempFolderPath, archiveName);
            string targetFilePath = Path.Combine(targetDir, fileName);

            try
            {
                // 命中磁盘缓存，直接返回
                if (File.Exists(targetFilePath))
                    return File.ReadAllBytes(targetFilePath);

                // 缓存未命中，执行解压
                Directory.CreateDirectory(targetDir);

                using var archive = ArchiveFactory.OpenArchive(archivePath);
                var entry = archive.Entries.FirstOrDefault(e =>
                    e.Key.Equals(fileName, StringComparison.OrdinalIgnoreCase) ||
                    e.Key.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase));

                if (entry != null && !entry.IsDirectory)
                {
                    // 先写入临时文件再重命名，确保 ReadAllBytes 不会读到未写完的文件
                    using (var fs = new FileStream(targetFilePath, FileMode.Create, FileAccess.Write))
                    {
                        entry.WriteTo(fs);
                    }
                    return File.ReadAllBytes(targetFilePath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"缓存读取或解压失败: {ex.Message}");
            }

            return null;
        }
        
    }

    #region 撤销重做功能

    /// <summary>撤销/重做命令接口</summary>
    public interface IUndoCommand
    {
        void Execute();
        void Undo();
    }

    /// <summary>撤销重做管理器：维护两个栈实现无限撤销/重做</summary>
    public class UndoRedoManager
    {
        private readonly Stack<IUndoCommand> _undoStack = new();
        private readonly Stack<IUndoCommand> _redoStack = new();

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        /// <summary>执行命令并压入撤销栈（清空重做栈）</summary>
        public void Execute(IUndoCommand command)
        {
            command.Execute();
            _undoStack.Push(command);
            _redoStack.Clear();
        }

        public void Undo()
        {
            if (!CanUndo) return;
            var command = _undoStack.Pop();
            command.Undo();
            _redoStack.Push(command);
        }

        public void Redo()
        {
            if (!CanRedo) return;
            var command = _redoStack.Pop();
            command.Execute();
            _undoStack.Push(command);
        }

        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
        }
    }

    /// <summary>新增标注命令</summary>
    public class AddCommand(BindingList<ImageLabel> list, ImageLabel label) : IUndoCommand
    {
        public void Execute() { label.IsDeleted = false; list.Add(label); }
        public void Undo() { label.IsDeleted = true; list.Remove(label); }
    }

    /// <summary>删除标注命令（软删除）</summary>
    public class DeleteCommand(ImageLabel label) : IUndoCommand
    {
        public void Execute() { label.IsDeleted = true; }
        public void Undo() { label.IsDeleted = false; }
    }

    /// <summary>标注属性修改命令（基于快照的撤销/重做）</summary>
    public class UpdateLabelCommand : IUndoCommand
    {
        private readonly ImageLabel _target;
        private readonly LabelSnapshot _oldState;
        private readonly LabelSnapshot _newState;
        private readonly Action _refreshAction;

        public UpdateLabelCommand(ImageLabel target, LabelSnapshot oldState, Action refresh)
        {
            _target = target;
            _oldState = oldState;
            _newState = new LabelSnapshot(target);
            _refreshAction = refresh;
        }

        public void Execute() { _newState.RestoreTo(_target); _refreshAction(); }
        public void Undo() { _oldState.RestoreTo(_target); _refreshAction(); }
    }

    /// <summary>标注状态快照（记录 Text、Group、Position）</summary>
    public class LabelSnapshot
    {
        public string Text { get; }
        public string Group { get; }
        public Point Position { get; }

        public LabelSnapshot(ImageLabel label)
        {
            Text = label.Text;
            Group = label.Group;
            Position = label.Position;
        }

        public void RestoreTo(ImageLabel label)
        {
            label.Text = Text;
            label.Group = Group;
            label.Position = Position;
        }
    }
    #endregion

    #region 项目上下文与服务
    /// <summary>文件对话框封装</summary>
    public class DialogService
    {
        public static string? OpenFolder(string description)
        {
            var dialog = new OpenFolderDialog { Title = description };
            return dialog.ShowDialog() == true ? dialog.FolderName : null;
        }

        public static string[]? OpenFiles(string filter, string description)
        {
            var dialog = new OpenFileDialog { Filter = filter, Multiselect = true, Title = description };
            return dialog.ShowDialog() == true ? dialog.FileNames : null;
        }

        public static string? OpenFile(string filter, string description, bool multiselect = false)
        {
            var dialog = new OpenFileDialog { Filter = filter, Multiselect = multiselect, Title = description };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        public static string? SaveFile(string filter, string defaultName)
        {
            var dialog = new SaveFileDialog { Filter = filter, FileName = defaultName };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        public static void ShowMessage(string message, bool isError)
        {
            MessageBox.Show(message, isError ? "错误" : "提示",
                MessageBoxButton.OK, isError ? MessageBoxImage.Error : MessageBoxImage.Information);
        }
    }

    /// <summary>文件系统工具类</summary>
    public static class FileSystemHelper
    {
        /// <summary>
        /// 生成唯一文件名，避免覆盖已有文件
        /// </summary>
        /// <param name="folder">目标文件夹路径</param>
        /// <param name="baseName">基础文件名（不含扩展名）</param>
        /// <param name="extension">文件扩展名（含点号，如 ".txt"）</param>
        /// <returns>唯一的文件名（不含路径）</returns>
        public static string GenerateUniqueFileName(string folder, string baseName, string extension)
        {
            string fileName = baseName + extension;
            string fullPath = Path.Combine(folder, fileName);

            if (!File.Exists(fullPath))
                return fileName;

            int counter = 1;
            do
            {
                fileName = $"{baseName}({counter}){extension}";
                fullPath = Path.Combine(folder, fileName);
                counter++;
            } while (File.Exists(fullPath));

            return fileName;
        }

        /// <summary>
        /// 清理临时文件夹：删除文件夹内所有文件和子文件夹，但保留文件夹本身
        /// </summary>
        /// <param name="tempFolderNames">需要清理的临时文件夹名称列表</param>
        public static void ClearTempFolders(params string[] tempFolderNames)
        {
            foreach (string folderName in tempFolderNames)
            {
                try
                {
                    string folderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, folderName);

                    // 如果文件夹不存在，创建它
                    if (!Directory.Exists(folderPath))
                    {
                        Directory.CreateDirectory(folderPath);
                        continue;
                    }

                    DirectoryInfo di = new(folderPath);

                    // 删除所有文件
                    foreach (FileInfo file in di.EnumerateFiles())
                    {
                        try { file.Delete(); }
                        catch (IOException) { /* 文件可能正在被占用，静默跳过 */ }
                    }

                    // 递归删除所有子文件夹
                    foreach (DirectoryInfo dir in di.EnumerateDirectories())
                    {
                        try { dir.Delete(true); }
                        catch (IOException) { /* 文件夹内有文件被占用，静默跳过 */ }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"清理 {folderName} 失败: {ex.Message}");
                }
            }
        }
    }
    #endregion

    #region 项目上下文
    /// <summary>
    /// 项目上下文：记录当前加载的基础路径、翻译文件名、压缩包名等信息
    /// </summary>
    public record ProjectContext(
        string BaseFolderPath = "",
        string? TxtName = null,
        string? ZipName = null)
    {
        public static ProjectContext Empty => new();

        /// <summary>翻译文件完整路径</summary>
        public string TxtPath => !string.IsNullOrEmpty(TxtName) ? Path.Combine(BaseFolderPath, TxtName) : "";

        /// <summary>压缩包完整路径</summary>
        public string ZipPath => !string.IsNullOrEmpty(ZipName) ? Path.Combine(BaseFolderPath, ZipName) : "";

        /// <summary>是否为压缩包模式</summary>
        public bool IsArchiveMode => !string.IsNullOrEmpty(ZipName);

        /// <summary>窗口标题栏显示文本</summary>
        public string DisplayTitle
        {
            get
            {
                if (string.IsNullOrEmpty(BaseFolderPath))
                    return "LabelMinus";

                string pathInfo = !string.IsNullOrEmpty(TxtName) ? TxtPath : "未命名";
                string modeInfo = IsArchiveMode ? $"关联:{ZipName}" : "文件夹";
                return $"LabelMinus - {pathInfo} 【{modeInfo}】";
            }
        }
    }

    /// <summary>
    /// 项目服务：负责扫描文件夹/压缩包中的图片，以及从 txt 文件加载项目数据
    /// </summary>
    public static class ProjectService
    {
        /// <summary>支持的图片扩展名（不区分大小写）</summary>
        public static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
            { ".jpg", ".png", ".bmp", ".webp" };

        /// <summary>支持的压缩包扩展名（不区分大小写）</summary>
        public static readonly HashSet<string> ZipExtensions = new(StringComparer.OrdinalIgnoreCase)
            { ".7z", ".zip", ".rar" };

        /// <summary>扫描文件夹，返回所有支持格式的图片信息（ImagePath 为绝对路径）</summary>
        public static List<ImageInfo> ScanFolder(string path) =>
            [.. Directory.EnumerateFiles(path)
                .Where(f => ImageExtensions.Contains(Path.GetExtension(f)))
                .Select(f => new ImageInfo { ImagePath = f })];

        /// <summary>扫描压缩包，返回所有图片信息（ImagePath 为 EntryName）</summary>
        public static List<ImageInfo> ScanZip(string zipPath) =>
            [.. ArchiveHelper.GetImagePath(zipPath)
                .Select(f => new ImageInfo { ImagePath = f })];

        /// <summary>从翻译 txt 文件加载项目上下文和图片列表</summary>
        public static (ProjectContext Context, List<ImageInfo> Images) LoadProjectFromTxt(string txtFilePath)
        {
            string content = File.ReadAllText(txtFilePath);
            string baseFolder = Path.GetDirectoryName(txtFilePath) ?? "";

            var database = Modules.ParseTextToLabels(content, out string? zipName);
            var context = new ProjectContext(baseFolder, Path.GetFileName(txtFilePath), zipName);

            // 如果关联了压缩包，从压缩包中加载所有图片
            if (context.IsArchiveMode && File.Exists(context.ZipPath))
            {
                var zipImages = ScanZip(context.ZipPath);
                var zipImageDict = zipImages.ToDictionary(img => Path.GetFileName(img.ImagePath), img => img);

                // 将 txt 中的标注数据合并到压缩包图片中
                foreach (var item in database)
                {
                    string imageName = item.Key;
                    if (zipImageDict.TryGetValue(imageName, out var zipImage))
                    {
                        // 将标注数据复制到压缩包图片对象中
                        foreach (var label in item.Value.Labels)
                            zipImage.Labels.Add(label);
                    }
                }

                return (context, zipImages);
            }
            else
            {
                // 文件夹模式：补全 ImagePath 为绝对路径
                foreach (var item in database)
                {
                    item.Value.ImagePath = Path.Combine(baseFolder, item.Key);
                }

                return (context, [.. database.Values]);
            }
        }
    }
    #endregion
    public class EnumToBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value?.Equals(parameter);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => (bool)value ? parameter : Binding.DoNothing;
    }
    /// <summary>反向布尔值到可见性转换器</summary>
    public class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
                return boolValue ? Visibility.Collapsed : Visibility.Visible;
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
                return visibility != Visibility.Visible;
            return false;
        }
    }
}
