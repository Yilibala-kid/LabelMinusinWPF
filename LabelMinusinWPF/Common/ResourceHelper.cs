using SharpCompress.Archives;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LabelMinusinWPF.Common
{
    /// <summary>
    /// 资源加载辅助类 - 合并了 ArchiveHelper 和 ImageHelper 的功能
    /// </summary>
    public static class ResourceHelper
    {
        #region 压缩包操作 (原 ArchiveHelper)

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
                .Where(x => x.Ext is string ext && Constants.ImageExtensions.Contains(ext) && x.Key is not null)
                .Select(x => Path.GetFullPath(Path.Combine(archivePath, x.Key!)))
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

        /// <summary>
        /// 从压缩包路径中提取压缩包路径和内部文件路径
        /// </summary>
        public static (string archivePath, string entryPath)? ParseArchivePath(string fullPath)
        {
            foreach (var suffix in Constants.ArchivePathSuffixes)
            {
                int index = fullPath.IndexOf(suffix, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    string archivePath = fullPath.Substring(0, index + suffix.Length - 1);
                    string entryPath = fullPath.Substring(index + suffix.Length);
                    return (archivePath, entryPath);
                }
            }
            return null;
        }

        #endregion

        #region 图片加载 (原 ImageHelper)

        /// <summary>
        /// 从文件路径加载 BitmapImage
        /// </summary>
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

        /// <summary>
        /// 从字节数组加载 BitmapImage
        /// </summary>
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

        /// <summary>
        /// 检查文件是否为支持的图片格式
        /// </summary>
        public static bool IsImageFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return Constants.ImageExtensions.Contains(ext);
        }

        #endregion
    }
}
