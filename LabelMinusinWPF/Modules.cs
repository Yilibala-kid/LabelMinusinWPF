using System;
using SharpCompress.Archives;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LabelMinusinWPF
{
    internal class Modules
    {
    }
    public class ArchiveHelper
    {
        // 定义支持的图片后缀
        private static readonly HashSet<string> imageExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".webp"];

        /// <summary>
        /// 获取压缩包内所有图片的路径列表
        /// </summary>
        public static List<string> GetImageEntries(string archivePath)
        {
            var entryNames = new List<string>();

            // ArchiveFactory 可以自动识别文件格式 (Zip, Rar, 7z, Tar...)
            using (var archive = ArchiveFactory.Open(archivePath))
            {
                foreach (var entry in archive.Entries)
                {
                    if (!entry.IsDirectory && imageExtensions.Contains(Path.GetExtension(entry.Key).ToLower()))
                    {
                        entryNames.Add(entry.Key); // entry.Key 是压缩包内的相对路径
                    }
                }
            }
            return entryNames;
        }
        // 获取 .exe 下的 ArchiveTemp 文件夹路径
        private static readonly string TempFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ArchiveTemp");

        public static byte[]? ExtractFileToBytes(string archivePath, string fileName)
        {
            if (string.IsNullOrEmpty(archivePath) || string.IsNullOrEmpty(fileName)) return null;

            // 为防止不同压缩包有同名文件导致冲突，建议在 Temp 下建立以压缩包名命名的子文件夹
            string archiveName = Path.GetFileNameWithoutExtension(archivePath);
            string targetDir = Path.Combine(TempFolderPath, archiveName);
            string targetFilePath = Path.Combine(targetDir, fileName);

            try
            {
                // 1. 检查物理缓存：如果文件已存在，直接读取
                if (File.Exists(targetFilePath))
                {
                    return File.ReadAllBytes(targetFilePath);
                }

                // 2. 物理缓存不存在，执行解压
                if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

                using (var archive = ArchiveFactory.Open(archivePath))
                {
                    var entry = archive.Entries.FirstOrDefault(e =>
                        e.Key.Equals(fileName, StringComparison.OrdinalIgnoreCase) ||
                        e.Key.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase));

                    if (entry != null && !entry.IsDirectory)
                    {
                        // 解压并保存到磁盘
                        using (var fs = new FileStream(targetFilePath, FileMode.Create, FileAccess.Write))
                        {
                            entry.WriteTo(fs);
                        }
                        // 返回读取到的字节
                        return File.ReadAllBytes(targetFilePath);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"缓存读取或解压失败: {ex.Message}");
            }

            return null;
        }
        // 异步预加载相邻图片
        public static async Task PrefetchNeighbors(string archivePath, List<string> allFileNames, int currentIndex)
        {
            // 定义预加载范围，比如前后各 1 张
            int[] offsets = { 1, -1, 2, -2 };

            await Task.Run(() =>
            {
                string archiveName = Path.GetFileNameWithoutExtension(archivePath);
                string targetDir = Path.Combine(TempFolderPath, archiveName);

                // 找出真正需要解压的文件名（过滤掉已经在磁盘上的）
                var pendingFiles = offsets
                    .Select(o => currentIndex + o)
                    .Where(i => i >= 0 && i < allFileNames.Count)
                    .Select(i => allFileNames[i])
                    .Where(name => !File.Exists(Path.Combine(targetDir, name)))
                    .ToList();

                if (pendingFiles.Count == 0) return;

                try
                {
                    // --- 关键优化：只打开一次压缩包 ---
                    using var archive = ArchiveFactory.Open(archivePath);
                    foreach (var fileName in pendingFiles)
                    {
                        var entry = archive.Entries.FirstOrDefault(e =>
                            e.Key.Equals(fileName, StringComparison.OrdinalIgnoreCase) ||
                            e.Key.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase));

                        if (entry != null && !entry.IsDirectory)
                        {
                            string targetFilePath = Path.Combine(targetDir, fileName);
                            Directory.CreateDirectory(targetDir);

                            // 使用临时扩展名防止其他线程误读未写完的文件
                            string tempFile = targetFilePath + ".tmp";
                            using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
                            {
                                entry.WriteTo(fs);
                            }
                            if (File.Exists(targetFilePath)) File.Delete(targetFilePath);
                            File.Move(tempFile, targetFilePath);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"预加载批量解压失败: {ex.Message}");
                }
            });
        }
    }
}
