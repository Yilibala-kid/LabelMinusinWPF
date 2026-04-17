using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SharpCompress.Archives;

namespace LabelMinusinWPF.Common
{
    #region 值转换器
    public class EnumToBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value?.Equals(parameter) ?? false;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => (bool)value! ? parameter : Binding.DoNothing;
    }

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

    public class ImageRelativePositionConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values[0] is not double relative || values[1] is not Image img || img.Source == null)
                return 0.0;
            bool isX = (string)parameter == "X";
            return relative * (isX ? img.ActualWidth : img.ActualHeight);
        }
        public object[]? ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => null;
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

        private static string GetExecutablePath() =>
            Assembly.GetExecutingAssembly().Location.Replace(".dll", ".exe");

        private static IEnumerable<string> GetTargetExtensions() =>
            TargetExtensions
                .Concat(Constants.ImageExtensions)
                .Where(ext => !string.IsNullOrWhiteSpace(ext))
                .Select(ext => ext.StartsWith('.') ? ext : "." + ext)
                .Distinct(StringComparer.OrdinalIgnoreCase);

        public static bool IsRegistered()
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"{BasePath}\Directory\shell\{OpenKey}");
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
                .Concat(GetTargetExtensions().Select(ext => $@"SystemFileAssociations\{ext}"))
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
            string iconPath
        )
        {
            using var key = Registry.CurrentUser.CreateSubKey(
                $@"{BasePath}\{root}\shell\{keyName}"
            );

            if (key == null)
                return;

            key.SetValue("", text);
            key.SetValue("Icon", iconPath);

            using var cmdKey = key.CreateSubKey("command");
            cmdKey?.SetValue("", command);
        }

        private static void DeleteKey(string fullPath)
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(fullPath, false); }
            catch { /* 忽略删除失败 */ }
        }
    }

    #endregion

    #region 项目操作辅助
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

    #endregion

    #region 资源加载
    public static class ResourceHelper
    {
        private static readonly string TempFolderPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            Constants.TempFolders.ArchiveTemp
        );

        public static List<string> GetImagePath(string archivePath)
        {
            using var archive = ArchiveFactory.OpenArchive(archivePath);
            return archive
                .Entries.Where(entry => !entry.IsDirectory)
                .Select(entry => (Key: entry.Key, Ext: Path.GetExtension(entry.Key)))
                .Where(x => x.Ext is not null && Constants.ImageExtensions.Contains(x.Ext) && x.Key is not null)
                .Select(x => Path.GetFullPath(Path.Combine(archivePath, x.Key!)))
                .ToList();
        }

        public static (string archivePath, string entryPath)? ParseArchivePath(string fullPath)
        {
            foreach (var suffix in Constants.ZipSuffixes)
            {
                int index = fullPath.IndexOf(suffix, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    string archivePath = fullPath[..(index + suffix.Length - 1)];
                    string entryPath = fullPath[(index + suffix.Length)..];
                    return (archivePath, entryPath);
                }
            }
            return null;
        }

        public static BitmapImage? LoadImageFromZip(string archivePath, string fileName)
        {
            if (string.IsNullOrEmpty(archivePath) || string.IsNullOrEmpty(fileName))
                return null;

            string archiveName = Path.GetFileNameWithoutExtension(archivePath);
            string targetDir = Path.Combine(TempFolderPath, archiveName);
            string targetFilePath = Path.Combine(targetDir, fileName);

            try
            {
                if (File.Exists(targetFilePath))
                    return LoadFromPath(targetFilePath);

                Directory.CreateDirectory(targetDir);

                using var archive = ArchiveFactory.OpenArchive(archivePath);
                var entry = archive.Entries.FirstOrDefault(e =>
                    e.Key is not null
                    && (e.Key.Equals(fileName, StringComparison.OrdinalIgnoreCase)
                        || e.Key.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase)));

                if (entry == null || entry.IsDirectory)
                    return null;

                using var fs = File.Create(targetFilePath);
                entry.WriteTo(fs);
                return LoadFromPath(targetFilePath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"解压图片失败: {ex.Message}");
                return null;
            }
        }

        public static BitmapImage? LoadFromPath(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

            try
            {
                var bmp = new BitmapImage(new Uri(path, UriKind.Absolute));
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
    #endregion

}
