using System.Windows.Media;

namespace LabelMinusinWPF.Common
{
    /// <summary>
    /// 全局常量定义
    /// </summary>
    public static class Constants
    {
        #region 文件扩展名

        /// <summary>支持的图片扩展名</summary>
        public static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".webp"
        };

        /// <summary>支持的压缩包扩展名</summary>
        public static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".zip", ".rar", ".7z"
        };

        /// <summary>压缩包特征后缀（用于路径识别）</summary>
        public static readonly string[] ArchivePathSuffixes = { ".zip\\", ".rar\\", ".7z\\" };

        #endregion

        #region 文件对话框过滤器

        /// <summary>文件对话框过滤器</summary>
        public static class FileFilters
        {
            public const string ImageFiles = "支持的文件|*.jpg;*.png;*.bmp";
            public const string ArchiveFiles = "压缩文件|*.zip;*.7z;*.rar";
            public const string TextFiles = "文本文件|*.txt";
            public const string ImageAndArchive = "支持的文件|*.zip;*.7z;*.rar;*.jpg;*.png;*.bmp";
        }

        #endregion

        #region 组别名称

        /// <summary>默认组别名称</summary>
        public static class Groups
        {
            public const string Default = "框内";
            public const string Outside = "框外";

            /// <summary>必须存在的默认组别</summary>
            public static readonly string[] Required = { Default, Outside };

            /// <summary>组别颜色数组</summary>
            public static readonly SolidColorBrush[] Brushes =
            [
                new SolidColorBrush(Color.FromRgb(234, 67, 53)),
                new SolidColorBrush(Color.FromRgb(66, 133, 244)),
                new SolidColorBrush(Color.FromRgb(52, 168, 83)),
                new SolidColorBrush(Color.FromRgb(251, 188, 4)),
                new SolidColorBrush(Color.FromRgb(171, 71, 188)),
                new SolidColorBrush(Color.FromRgb(0, 172, 193)),
                new SolidColorBrush(Color.FromRgb(255, 112, 67)),
                new SolidColorBrush(Color.FromRgb(141, 110, 99)),
            ];
        }

        #endregion

        #region 标签默认值

        /// <summary>标签默认值</summary>
        public static class Label
        {
            public const string NewLabelText = "新标签";
            public const string DefaultGroup = "框内";
            public const string DefaultRemark = "这是备注";
            public const double DefaultFontSize = 20.0;
            public const string DefaultFontFamily = "微软雅黑";
        }

        #endregion

        #region 应用程序模式

        /// <summary>应用程序模式</summary>
        public enum AppMode
        {
            See,
            LabelDo,
            OCR
        }

        #endregion

        #region OCR 识别

        /// <summary>OCR 识别网站配置</summary>
        public static class OcrWebsites
        {
            public static readonly Dictionary<string, string> Websites = new()
            {
                ["识字体网 (LikeFont)"] = "https://www.likefont.com/",
                ["AI识别 (YuzuMarker)"] = "https://huggingface.co/spaces/gyrojeff/YuzuMarker.FontDetection",
                ["必应"] = "https://www.bing.com/visualsearch"
            };

            public const string DefaultWebsite = "AI识别 (YuzuMarker)";
        }

        #endregion

        #region UI 显示

        /// <summary>应用程序名称</summary>
        public const string AppName = "LabelMinus";

        /// <summary>自动保存设置</summary>
        public static class AutoSave
        {
            public const int IntervalMinutes = 5;
            public const int MaxFiles = 20;
            public const string FolderName = "AutoSave";
        }

        /// <summary>临时文件夹名称</summary>
        public static class TempFolders
        {
            public const string ArchiveTemp = "ArchiveTemp";
            public const string OcrTemp = "OCRtemp";
            public const string ScreenShotTemp = "ScreenShottemp";
        }

        #endregion
    }
}
