using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LabelMinusinWPF.Common
{
    // LabelPlus 格式的文本解析与导出
    public static class LabelPlusParser
    {
        // 预编译正则（static readonly 保证只编译一次）
        private static readonly Regex ImgRegex = new(@">>>>>>>>\[(.*?)\]<<<<<<<<", RegexOptions.Compiled);
        private static readonly Regex MetaRegex = new(@"----------------\[(\d+)\]----------------\[([\d\.]+),([\d\.]+),(\d+)\]", RegexOptions.Compiled);

        // 将 LabelPlus 格式的文本解析为 图片名→OneImage 字典
        public static Dictionary<string, OneImage> TextToLabels(string content, out string? sourceName)
        {
            sourceName = null;
            var database = new Dictionary<string, OneImage>();
            var groupList = new List<string>();

            string[] lines = content.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
            string? currentImgName = null;
            OneLabel? currentLabel = null;
            int hyphenCount = 0;

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

                // 3. 图片标记行
                var imgMatch = ImgRegex.Match(line);
                if (imgMatch.Success)
                {
                    currentImgName = imgMatch.Groups[1].Value;
                    database[currentImgName] = new OneImage { ImagePath = currentImgName };
                    currentLabel = null;
                    continue;
                }

                // 4. 标注元数据行
                var metaMatch = MetaRegex.Match(line);
                if (metaMatch.Success && currentImgName != null)
                {
                    int groupIdx = int.Parse(metaMatch.Groups[4].Value);
                    string groupName = (groupIdx > 0 && groupIdx <= groupList.Count)
                                       ? groupList[groupIdx - 1]
                                       : (groupIdx == 2 ? Constants.Groups.Outside : Constants.Groups.Default);

                    currentLabel = new OneLabel
                    {
                        Index = int.Parse(metaMatch.Groups[1].Value),
                        Position = new System.Windows.Point(float.Parse(metaMatch.Groups[2].Value), float.Parse(metaMatch.Groups[3].Value)),
                        Group = groupName,
                        Text = ""
                    };
                    database[currentImgName].Labels.Add(currentLabel);
                    continue;
                }

                // 5. 标注文本内容（多行累加）
                if (currentLabel != null && hyphenCount >= 2)
                {
                    // 使用 StringBuilder 优化多行拼接
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

        // 将图片列表导出为 LabelPlus 格式文本
        public static string LabelsToText(IEnumerable<OneImage> images, string? sourceName, ExportMode mode = ExportMode.Current)
        {
            var imageList = images.ToList();
            if (imageList.Count == 0) return string.Empty;

            var sb = new StringBuilder();

            // --- 1. 收集所有分组并建立映射 ---
            var allGroups = imageList
                    .SelectMany(img => img.Labels)
                    .Select(l => l.Group).Distinct()
                    .OrderBy(g => g == Constants.Groups.Default ? 0 : (g == Constants.Groups.Outside ? 1 : 2))
                    .ThenBy(g => g).ToList();

            if (allGroups.Count == 0) { allGroups.Add(Constants.Groups.Default); allGroups.Add(Constants.Groups.Outside); }

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

                    // 写入坐标和组信息
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
    }
}
