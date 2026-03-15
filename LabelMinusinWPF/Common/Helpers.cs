using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Data;
using Microsoft.Win32;
using SharpCompress.Archives;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LabelMinusinWPF.Common
{
    #region 值转换器

    // 枚举转布尔值转换器（用于 RadioButton 等控件绑定枚举）
    public class EnumToBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value?.Equals(parameter) ?? false;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => (bool)value! ? parameter : Binding.DoNothing;
    }

    // 反向布尔值到可见性转换器
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

    #endregion

    #region 右键菜单注册

    // 系统右键菜单注册器
    public static class ContextMenuRegistrar
    {
        private const string OpenKey = "LabelMinus.Open";
        private const string ReviewKey = "LabelMinus.Review";

        private const string BasePath = @"Software\Classes";

        private static readonly string[] TargetExtensions = [".txt", ".zip", ".rar", ".7z"];

        private static string GetExecutablePath()
        {
            return Assembly.GetExecutingAssembly().Location.Replace(".dll", ".exe");
        }

        private static IEnumerable<string> GetTargetExtensions()
        {
            return TargetExtensions
            .Concat(ProjectHelper.ImageExtensions)
            .Where(ext => !string.IsNullOrWhiteSpace(ext))
            .Select(ext => ext.StartsWith('.') ? ext : "." + ext)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        public static bool IsRegistered()
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                $@"{BasePath}\Directory\shell\{OpenKey}");
            return key != null;
        }

        public static void RegisterAll()
        {
            string exePath = GetExecutablePath();

            string openCommand = $"\"{exePath}\" \"%1\"";
            string reviewCommand = $"\"{exePath}\" --review \"%1\"";

            // 1️文件夹
            RegisterMenu(@"Directory", OpenKey, "使用 LabelMinus 打开", openCommand, exePath);
            RegisterMenu(@"Directory", ReviewKey, "使用 LabelMinus 图校", reviewCommand, exePath);

            // 2️指定扩展
            foreach (var ext in GetTargetExtensions())
            {
                string root = $@"SystemFileAssociations\{ext}";
                RegisterMenu(root, OpenKey, "使用 LabelMinus 打开", openCommand, exePath);
                RegisterMenu(root, ReviewKey, "使用 LabelMinus 图校", reviewCommand, exePath);
            }
        }

        public static void UnregisterAll()
        {
            var roots = new[] { "Directory" }
                .Concat(GetTargetExtensions()
                .Select(ext => $@"SystemFileAssociations\{ext}"))
                .ToList();

            foreach (var root in roots)
            {
                DeleteKey($@"{BasePath}\{root}\shell\{OpenKey}");
                DeleteKey($@"{BasePath}\{root}\shell\{ReviewKey}");
            }
        }

        private static void RegisterMenu(
            string root,
            string keyName,
            string text,
            string command,
            string iconPath)
        {
            using var key = Registry.CurrentUser.CreateSubKey(
                $@"{BasePath}\{root}\shell\{keyName}");

            if (key == null) return;

            key.SetValue("", text);
            key.SetValue("Icon", iconPath);

            using var cmdKey = key.CreateSubKey("command");
            cmdKey?.SetValue("", command);
        }

        private static void DeleteKey(string fullPath)
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(fullPath, false);
            }
            catch
            {
                // 忽略删除失败
            }
        }
    }

    #endregion

    #region 项目操作辅助

    // 项目操作辅助类 - 合并了 ProjectService, ProjectContext, FileSystemHelper 的功能
    public static class ProjectHelper
    {
        // 项目上下文：记录当前加载的基础路径、翻译文件名、压缩包名等信息
        public record ProjectContext(
            string BaseFolderPath = "",
            string? TxtName = null,
            string? ZipName = null)
        {
            public static ProjectContext Empty => new();

            // 翻译文件完整路径
            public string TxtPath => !string.IsNullOrEmpty(TxtName) ? Path.Combine(BaseFolderPath, TxtName) : "";

            // 压缩包完整路径
            public string ZipPath => !string.IsNullOrEmpty(ZipName) ? Path.Combine(BaseFolderPath, ZipName) : "";

            // 是否为压缩包模式
            public bool IsArchiveMode => !string.IsNullOrEmpty(ZipName);

            // 窗口标题栏显示文本
            public string DisplayTitle
            {
                get
                {
                    if (string.IsNullOrEmpty(BaseFolderPath))
                        return Constants.AppName;

                    string pathInfo = !string.IsNullOrEmpty(TxtName) ? TxtPath : "未命名";
                    string modeInfo = IsArchiveMode ? $"关联:{ZipName}" : "文件夹";
                    return $"LabelMinus - {pathInfo} 【{modeInfo}】";
                }
            }
        }

        // 支持的图片扩展名（不区分大小写）
        public static readonly HashSet<string> ImageExtensions = Constants.ImageExtensions;

        // 支持的压缩包扩展名（不区分大小写）
        public static readonly HashSet<string> ZipExtensions = Constants.ArchiveExtensions;

        // 扫描文件夹，返回所有支持格式的图片信息（ImagePath 为绝对路径）
        public static List<ImageInfo> ScanFolder(string path) =>
            [.. Directory.EnumerateFiles(path)
                .Where(f => ImageExtensions.Contains(Path.GetExtension(f)))
                .Select(f => new ImageInfo { ImagePath = f })];

        // 扫描压缩包，返回所有图片信息（ImagePath 为 EntryName）
        public static List<ImageInfo> ScanZip(string zipPath) =>
            [.. ResourceHelper.GetImagePath(zipPath)
                .Select(f => new ImageInfo { ImagePath = f })];

        // 从翻译 txt 文件加载项目上下文和图片列表
        public static (ProjectContext Context, List<ImageInfo> Images) LoadProjectFromTxt(string txtFilePath)
        {
            string content = File.ReadAllText(txtFilePath);
            string baseFolder = Path.GetDirectoryName(txtFilePath) ?? "";

            var database = LabelPlusParser.TextToLabels(content, out string? zipName);
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

        // 生成唯一文件名，避免覆盖已有文件
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

        // 清理临时文件夹：删除文件夹内所有文件和子文件夹，但保留文件夹本身
        public static void ClearTempFolders(params string[] tempFolderNames)
        {
            foreach (string folderName in tempFolderNames)
            {
                try
                {
                    string folderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, folderName);

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

    #region 资源加载辅助

    // 资源加载辅助类 - 合并了 ArchiveHelper 和 ImageHelper 的功能
    public static class ResourceHelper
    {
        // 解压缓存目录（位于 exe 同级 ArchiveTemp 文件夹下）
        private static readonly string TempFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ArchiveTemp");

        // 获取压缩包内所有图片的路径列表
        public static List<string> GetImagePath(string archivePath)
        {
            using var archive = ArchiveFactory.OpenArchive(archivePath);

            return archive.Entries
                .Where(entry => !entry.IsDirectory)
                .Select(entry => (Key: entry.Key, Ext: Path.GetExtension(entry.Key)))
                .Where(x => x.Ext is not null && Constants.ImageExtensions.Contains(x.Ext) && x.Key is not null)
                .Select(x => Path.GetFullPath(Path.Combine(archivePath, x.Key!)))
                .ToList();
        }

        // 从压缩包中提取指定文件为 byte[]（带磁盘缓存）
        public static byte[]? ExtractFileToBytes(string archivePath, string fileName)
        {
            if (string.IsNullOrEmpty(archivePath) || string.IsNullOrEmpty(fileName)) return null;

            // 以压缩包名建子文件夹，防止不同压缩包同名文件冲突
            string archiveName = Path.GetFileNameWithoutExtension(archivePath);
            string targetDir = Path.Combine(TempFolderPath, archiveName);
            string targetFilePath = Path.Combine(targetDir, fileName);

            try
            {
                // 命中磁盘缓存，直接返回（优化：避免重复读取）
                if (File.Exists(targetFilePath))
                    return File.ReadAllBytes(targetFilePath);

                // 缓存未命中，执行解压
                Directory.CreateDirectory(targetDir);

                using var archive = ArchiveFactory.OpenArchive(archivePath);
                var entry = archive.Entries.FirstOrDefault(e =>
                    e.Key is not null && (e.Key.Equals(fileName, StringComparison.OrdinalIgnoreCase) ||
                    e.Key.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase)));

                if (entry != null && !entry.IsDirectory)
                {
                    // 直接写入内存并返回，避免重复 I/O
                    using var ms = new MemoryStream();
                    entry.WriteTo(ms);
                    byte[] data = ms.ToArray();

                    // 同时写入缓存
                    File.WriteAllBytes(targetFilePath, data);
                    return data;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"缓存读取或解压失败: {ex.Message}");
            }

            return null;
        }

        // 从压缩包路径中提取压缩包路径和内部文件路径
        public static (string archivePath, string entryPath)? ParseArchivePath(string fullPath)
        {
            foreach (var suffix in Constants.ZipSuffixes)
            {
                int index = fullPath.IndexOf(suffix, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    // 使用 Range 语法简化
                    string archivePath = fullPath[..(index + suffix.Length - 1)];
                    string entryPath = fullPath[(index + suffix.Length)..];
                    return (archivePath, entryPath);
                }
            }
            return null;
        }

        // 从文件路径加载 BitmapImage
        public static BitmapImage? LoadFromPath(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"加载图片失败: {ex.Message}");
                return null;
            }
        }

        // 从字节数组加载 BitmapImage
        public static BitmapImage? LoadFromBytes(byte[] data)
        {
            if (data == null || data.Length == 0) return null;

            try
            {
                using var ms = new MemoryStream(data);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"加载图片失败: {ex.Message}");
                return null;
            }
        }
        }
    }

    #endregion
