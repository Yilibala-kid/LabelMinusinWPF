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

        public static Dictionary<string, OneImage> TextToLabels(string content, out string? sourceName)
        {
            sourceName = null;
            Dictionary<string, OneImage> database = [];
            List<string> groupList = [];

            string[] lines = content.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
            string? currentImgName = null;
            OneLabel? currentLabel = null;
            int hyphenCount = 0;

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                if (line == "-") { hyphenCount++; continue; }
                if (hyphenCount == 1) { groupList.Add(line); continue; }

                if (hyphenCount == 2 && currentImgName == null && line.StartsWith("关联文件:"))
                {
                    var path = line.Replace("关联文件:", "").Trim();
                    sourceName = string.IsNullOrEmpty(path) ? null : path;
                    continue;
                }

                var imgMatch = ImgRegex.Match(line);
                if (imgMatch.Success)
                {
                    currentImgName = imgMatch.Groups[1].Value;
                    database[currentImgName] = new() { ImagePath = currentImgName };
                    currentLabel = null;
                    continue;
                }

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

                if (currentLabel != null && hyphenCount >= 2)
                {
                    currentLabel.Text = string.IsNullOrEmpty(currentLabel.Text)
                                        ? line
                                        : currentLabel.Text + Environment.NewLine + line;
                }
            }

            foreach (var img in database.Values)
                foreach (var lbl in img.Labels)
                    lbl.LoadBaseContent(lbl.Text);

            return database;
        }

        public enum ExportMode { Original, Current, Diff }

        public static string LabelsToText(IEnumerable<OneImage> images, string? sourceName, ExportMode mode = ExportMode.Current)
        {
            var imageList = images.ToList();
            if (imageList.Count == 0) return string.Empty;

            StringBuilder sb = new();

            var allGroups = imageList
                    .SelectMany(img => img.Labels)
                    .Select(l => l.Group).Distinct()
                    .OrderBy(g => g == Constants.Groups.Default ? 0 : (g == Constants.Groups.Outside ? 1 : 2))
                    .ThenBy(g => g).ToList();

            if (allGroups.Count == 0) { allGroups.Add(Constants.Groups.Default); allGroups.Add(Constants.Groups.Outside); }

            var groupToIdMap = allGroups
                .Select((group, index) => (group, Id: index + 1))
                .ToDictionary(x => x.group, x => x.Id);

            sb.AppendLine("1,0\n-\n" + string.Join("\n", allGroups) + "\n-\n");
            sb.AppendLine($"关联文件:{sourceName}");
            sb.AppendLine($"最后修改时间:{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");

            foreach (var imageInfo in imageList.OrderBy(img => img.ImageName))
            {
                if (mode == ExportMode.Diff && !imageInfo.Labels.Any(l => l.IsModified))
                    continue;

                string pureName = Path.GetFileName(imageInfo.ImagePath ?? imageInfo.ImageName);
                sb.AppendLine($">>>>>>>>[{pureName}]<<<<<<<<");

                foreach (var label in imageInfo.Labels.OrderBy(l => l.Index))
                {
                    if (mode == ExportMode.Diff && !label.IsModified) continue;
                    if (mode != ExportMode.Diff && label.IsDeleted) continue;

                    int groupValue = groupToIdMap.GetValueOrDefault(label.Group, 1);
                    sb.AppendLine($"----------------[{label.Index}]----------------[{label.X:F3},{label.Y:F3},{groupValue}]");

                    if (mode == ExportMode.Diff)
                    {
                        if (label.IsDeleted)
                            sb.AppendLine($"- [已删除]\n{label.OriginalText}");
                        else if (string.IsNullOrEmpty(label.OriginalText))
                            sb.AppendLine($"+ [新增]\n{label.Text}");
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
