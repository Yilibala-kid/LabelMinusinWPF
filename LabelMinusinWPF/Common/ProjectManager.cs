using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace LabelMinusinWPF.Common
{
    public static class ProjectManager
    {
        public record WorkSpace(
            string BaseFolderPath = "",
            string? TxtName = null,
            string? ZipName = null
        )
        {
            public static WorkSpace Empty => new();

            public string TxtPath =>
                !string.IsNullOrEmpty(TxtName) ? Path.Combine(BaseFolderPath, TxtName) : "";

            public string ZipPath =>
                !string.IsNullOrEmpty(ZipName) ? Path.Combine(BaseFolderPath, ZipName) : "";

            public bool IsArchiveMode => !string.IsNullOrEmpty(ZipName);

            public string DisplayTitle
            {
                get
                {
                    if (string.IsNullOrEmpty(BaseFolderPath))
                        return Constants.AppName;

                    string pathInfo = !string.IsNullOrEmpty(TxtName) ? TxtPath : "正在预览";
                    string modeInfo = IsArchiveMode ? $"{ZipName}" : "文件夹";
                    return $"LabelMinus - {pathInfo} 【{modeInfo}】";
                }
            }
        }

        public static List<OneImage> ScanFolder(string path) =>
            [
                .. Directory
                    .EnumerateFiles(path)
                    .Where(f => Constants.ImageExtensions.Contains(Path.GetExtension(f)))
                    .Select(f => new OneImage { ImagePath = f }),
            ];

        public static List<OneImage> ScanZip(string zipPath) =>
            [.. ResourceHelper.GetImagePath(zipPath).Select(f => new OneImage { ImagePath = f })];

        public static (WorkSpace Context, List<OneImage> Images) GetProjectFromTxt(string txtFilePath)
        {
            string content = File.ReadAllText(txtFilePath);
            string baseFolder = Path.GetDirectoryName(txtFilePath) ?? "";

            var database = LabelPlusParser.TextToLabels(content, out string? zipName);
            WorkSpace context = new(baseFolder, Path.GetFileName(txtFilePath), zipName);

            if (context.IsArchiveMode && File.Exists(context.ZipPath))
            {
                var zipImages = ScanZip(context.ZipPath);
                var zipImageDict = zipImages.ToDictionary(img => Path.GetFileName(img.ImagePath), img => img);

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
                foreach (var item in database)
                    item.Value.ImagePath = Path.Combine(baseFolder, item.Key);
                return (context, [.. database.Values]);
            }
        }

        public static string GenerateUniqueFileName(string folder, string baseName, string extension)
        {
            string fileName = $"{baseName}{extension}";
            string fullPath = Path.Combine(folder, fileName);

            if (!File.Exists(fullPath)) return fileName;

            int counter = 1;
            while (File.Exists(fullPath = Path.Combine(folder, $"{baseName}({counter}){extension}")))
                counter++;

            return $"{baseName}({counter}){extension}";
        }

        public static void ClearTempFolders(params string[] tempFolderNames)
        {
            foreach (string folderName in tempFolderNames)
            {
                try
                {
                    string folderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, folderName);

                    if (!Directory.Exists(folderPath)) { Directory.CreateDirectory(folderPath); continue; }

                    DirectoryInfo di = new(folderPath);

                    foreach (FileInfo file in di.EnumerateFiles())
                        try { file.Delete(); }
                        catch (IOException) { /* 静默跳过 */ }

                    foreach (DirectoryInfo dir in di.EnumerateDirectories())
                        try { dir.Delete(true); }
                        catch (IOException) { /* 静默跳过 */ }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"清理 {folderName} 失败: {ex.Message}");
                }
            }
        }
    }
}
