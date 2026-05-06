using System.Windows.Media;

namespace LabelMinusinWPF.Common
{
    public static class Constants
    {
        #region 文件扩展名
        public static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".webp"
        };

        public static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".zip", ".rar", ".7z"
        };

        public static readonly string[] ZipSuffixes = [".zip\\", ".rar\\", ".7z\\"];
        #endregion

        #region 文件对话框过滤器
        public static class FileFilters
        {
            private static string ToFilterPattern(HashSet<string> extensions)
                => string.Join(";", extensions.Select(e => "*" + e));

            public static readonly string ImageFiles = "图片|" + ToFilterPattern(ImageExtensions);
            public static readonly string ArchiveFiles = "压缩包|" + string.Join(";", ArchiveExtensions.Select(e => "*" + e));
            public static readonly string TextFiles = "翻译文件|*.txt";
            public static readonly string ImageAndArchive = "图片或压缩包|" + string.Join(";", ImageExtensions.Concat(ArchiveExtensions).Select(e => "*" + e));
        }
        #endregion

        #region UI 显示
        public const string AppName = "LabelMinus";

        public static class AutoSave
        {
            public const int IntervalMinutes = 5;
            public const int MaxFiles = 20;
            public const string FolderName = "AutoSave";
        }

        public static class TempFolders
        {
            public const string ArchiveTemp = "ArchiveTemp";
            public const string ScreenShotTemp = "ScreenShottemp";
        }
        #endregion
    }
}
