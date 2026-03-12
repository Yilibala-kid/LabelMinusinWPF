using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace LabelMinusinWPF.Common
{
    /// <summary>
    /// 项目操作辅助类 - 合并了 ProjectService, ProjectContext, FileSystemHelper 的功能
    /// </summary>
    public static class ProjectHelper
    {
        #region 项目上下文 (原 ProjectContext)

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
                        return Constants.AppName;

                    string pathInfo = !string.IsNullOrEmpty(TxtName) ? TxtPath : "未命名";
                    string modeInfo = IsArchiveMode ? $"关联:{ZipName}" : "文件夹";
                    return $"LabelMinus - {pathInfo} 【{modeInfo}】";
                }
            }
        }

        #endregion

        #region 项目服务 (原 ProjectService)

        /// <summary>支持的图片扩展名（不区分大小写）</summary>
        public static readonly HashSet<string> ImageExtensions = Constants.ImageExtensions;

        /// <summary>支持的压缩包扩展名（不区分大小写）</summary>
        public static readonly HashSet<string> ZipExtensions = Constants.ArchiveExtensions;

        /// <summary>扫描文件夹，返回所有支持格式的图片信息（ImagePath 为绝对路径）</summary>
        public static List<ImageInfo> ScanFolder(string path) =>
            [.. Directory.EnumerateFiles(path)
                .Where(f => ImageExtensions.Contains(Path.GetExtension(f)))
                .Select(f => new ImageInfo { ImagePath = f })];

        /// <summary>扫描压缩包，返回所有图片信息（ImagePath 为 EntryName）</summary>
        public static List<ImageInfo> ScanZip(string zipPath) =>
            [.. ResourceHelper.GetImagePath(zipPath)
                .Select(f => new ImageInfo { ImagePath = f })];

        /// <summary>从翻译 txt 文件加载项目上下文和图片列表</summary>
        public static (ProjectContext Context, List<ImageInfo> Images) LoadProjectFromTxt(string txtFilePath)
        {
            string content = File.ReadAllText(txtFilePath);
            string baseFolder = Path.GetDirectoryName(txtFilePath) ?? "";

            var database = LabelPlusParser.ParseTextToLabels(content, out string? zipName);
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

        #endregion

        #region 文件系统工具 (原 FileSystemHelper)

        /// <summary>
        /// 生成唯一文件名，避免覆盖已有文件
        /// </summary>
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

        /// <summary>
        /// 确保目录存在，不存在则创建
        /// </summary>
        public static void EnsureDirectoryExists(string path)
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        #endregion
    }
}
