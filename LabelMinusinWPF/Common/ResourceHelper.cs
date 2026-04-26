using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using SharpCompress.Archives;

namespace LabelMinusinWPF.Common
{
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
}
