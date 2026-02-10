using SharpCompress.Archives;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace LabelMinusinWPF
{
    internal class Modules
    {
        public static Dictionary<string, ImageInfo> ParseTextToLabels(string content, out string? sourceName)//文本解析
        {
            sourceName = null;
            var database = new Dictionary<string, ImageInfo>();
            var groupList = new List<string>(); // 存储从文件头读取到的动态分组

            // 使用预编译正则，提高性能
            var imgRegex = new Regex(@">>>>>>>>\[(.*?)\]<<<<<<<<", RegexOptions.Compiled);
            var metaRegex = new Regex(@"----------------\[(\d+)\]----------------\[([\d\.]+),([\d\.]+),(\d+)\]", RegexOptions.Compiled);

            string[] lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            string? currentImgName = null;
            ImageLabel? currentLabel = null;
            int hyphenCount = 0; // 用于追踪我们处理到了第几个 "-" 分隔符

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line)) continue;
                // 1. 处理文件头分组
                if (line == "-") { hyphenCount++; continue; }
                if (hyphenCount == 1) { groupList.Add(line); continue; }
                if (hyphenCount == 2 && currentImgName == null)// 在第二个 "-" 之后，图片标记出现之前，寻找路径信息
                {
                    if (line.StartsWith("关联文件:"))
                    {
                        var path = line.Replace("关联文件:", "").Trim();
                        sourceName = string.IsNullOrEmpty(path) ? null : path;
                    }
                }
                // 2. 识别图片
                var imgMatch = imgRegex.Match(line);
                if (imgMatch.Success)
                {
                    currentImgName = imgMatch.Groups[1].Value;

                    // 这里非常关键：ImageInfo 现在依赖 ImagePath！
                    // 我们先创建一个临时的 ImageInfo，ImagePath 稍后在 ViewModel 里根据 BaseFolderPath 补全
                    database[currentImgName] = new ImageInfo { ImagePath = currentImgName };
                    currentLabel = null;
                    continue;
                }

                // --- 3. 识别标注元数据行 ---
                var metaMatch = metaRegex.Match(line);
                if (metaMatch.Success && currentImgName != null)
                {
                    // 在处理新 Label 前，锁定上一个 Label 的原文
                    currentLabel?.LoadBaseContent(currentLabel.Text);

                    int groupIdx = int.Parse(metaMatch.Groups[4].Value);
                    string groupName = (groupIdx > 0 && groupIdx <= groupList.Count)
                                       ? groupList[groupIdx - 1]
                                       : (groupIdx == 2 ? "框外" : "框内");

                    currentLabel = new ImageLabel
                    {
                        Index = int.Parse(metaMatch.Groups[1].Value),
                        Position = new BoundingBox(float.Parse(metaMatch.Groups[2].Value), float.Parse(metaMatch.Groups[3].Value), 0, 0),
                        Group = groupName,
                        Text = "" // 暂时为空，等待下方读取文本行
                    };
                    database[currentImgName].Labels.Add(currentLabel);
                    continue;
                }

                // --- 4. 识别标注文本内容 ---
                // 排除掉干扰行（如头部信息和分隔符）
                if (currentLabel != null && hyphenCount >= 2)
                {
                    currentLabel.Text = string.IsNullOrEmpty(currentLabel.Text)
                                        ? line
                                        : currentLabel.Text + Environment.NewLine + line;
                }
            }
            foreach (var img in database.Values)
            {
                foreach (var lbl in img.Labels)
                {
                    // 将当前读到的 Text 正式转为 OriginalText，并重置 IsModified
                    lbl.LoadBaseContent(lbl.Text);
                }
            }

            return database;
        }
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

                using var archive = ArchiveFactory.Open(archivePath);

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
                    // 注意：这里保留 fs 的块是为了确保 File.ReadAllBytes 执行前文件已关闭并释放

                    return File.ReadAllBytes(targetFilePath);
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
