using System;
using System.Windows;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GroupManager = LabelMinusinWPF.Common.GroupManager;
using GroupConstants = LabelMinusinWPF.Common.GroupConstants;

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
            int hyphenCount = 0;

            // --- 缓冲变量 ---
            int tempIndex = 0;
            string tempGroup = "";
            Point tempPos = default;
            StringBuilder textBuffer = new();
            bool isCollectingText = false;

            // 内部函数：当一个标签的数据读取完毕时，正式实例化并加入集合
            void CommitLabel()
            {
                if (isCollectingText && currentImgName != null)
                {
                    var label = new OneLabel(tempIndex, textBuffer.ToString().Trim(), tempGroup, tempPos);
                    database[currentImgName].Labels.Add(label);

                    // 重置缓冲
                    textBuffer.Clear();
                    isCollectingText = false;
                }
            }

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                if (line == "-") { hyphenCount++; continue; }
                if (hyphenCount == 1) { groupList.Add(line); continue; }

                if (hyphenCount == 2 && line.StartsWith("关联文件:"))
                {
                    var path = line.Replace("关联文件:", "").Trim();
                    sourceName = string.IsNullOrEmpty(path) ? null : path;
                    continue;
                }

                // 匹配图片：图片切换前，先提交上一个标签
                var imgMatch = ImgRegex.Match(line);
                if (imgMatch.Success)
                {
                    CommitLabel();
                    currentImgName = imgMatch.Groups[1].Value;
                    database[currentImgName] = new() { ImagePath = currentImgName };
                    continue;
                }

                // 匹配元数据：新标签开始前，先提交上一个标签
                var metaMatch = MetaRegex.Match(line);
                if (metaMatch.Success && currentImgName != null)
                {
                    CommitLabel();

                    tempIndex = int.Parse(metaMatch.Groups[1].Value);
                    tempPos = new Point(float.Parse(metaMatch.Groups[2].Value), float.Parse(metaMatch.Groups[3].Value));

                    int groupIdx = int.Parse(metaMatch.Groups[4].Value);
                    tempGroup = (groupIdx > 0 && groupIdx <= groupList.Count)
                                       ? GroupManager.NormalizeGroupName(groupList[groupIdx - 1])
                                       : (groupIdx == 2 ? GroupConstants.OutBox : GroupConstants.InBox);

                    isCollectingText = true; // 标记开始收集文本
                    continue;
                }

                // 收集文本逻辑
                if (isCollectingText && hyphenCount >= 2)
                {
                    if (textBuffer.Length > 0) textBuffer.AppendLine();
                    textBuffer.Append(line);
                }
            }

            // 循环结束后，不要忘记提交最后一个标签
            CommitLabel();

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
                    .OrderBy(g => g == GroupConstants.InBox ? 0 : (g == GroupConstants.OutBox ? 1 : 2))
                    .ThenBy(g => g).ToList();

            if (allGroups.Count == 0) { allGroups.Add(GroupConstants.InBox); allGroups.Add(GroupConstants.OutBox); }

            var groupToIdMap = allGroups
                .Select((group, index) => (group, Id: index + 1))
                .ToDictionary(x => x.group, x => x.Id);

            sb.AppendLine("1,0\n-\n" + string.Join("\n", allGroups) + "\n-\n");
            sb.AppendLine($"关联文件:{sourceName}");
            sb.AppendLine($"最后修改时间:{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");

            foreach (var imageInfo in imageList.OrderBy(img => img.ImageName))
            {

                sb.AppendLine($">>>>>>>>[{imageInfo.ImageName}]<<<<<<<<");

                foreach (var label in imageInfo.Labels.OrderBy(l => l.Index))
                {
                    if (mode == ExportMode.Diff && !(label.IsDeleted || label.Text != label.OriginalText || label.Group != label.OriginalGroup)) continue;
                    if (mode != ExportMode.Diff && label.IsDeleted) continue;

                    int groupValue = groupToIdMap.GetValueOrDefault(label.Group, 1);
                    sb.AppendLine($"----------------[{label.Index}]----------------[{label.X:F3},{label.Y:F3},{groupValue}]");

                    if (mode == ExportMode.Diff)
                    {
                        if (label.IsDeleted)
                            sb.AppendLine($"- [{label.OriginalText}]");
                        else if (string.IsNullOrEmpty(label.OriginalText))
                            sb.AppendLine($"+ [{label.Text}]");
                        else
                        {
                            sb.AppendLine($"* [{label.OriginalText}]");
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
