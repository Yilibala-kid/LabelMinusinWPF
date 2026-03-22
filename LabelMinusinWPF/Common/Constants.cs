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
            public const string ImageFiles = "支持的文件|*.jpg;*.png;*.bmp";
            public const string ArchiveFiles = "压缩文件|*.zip;*.7z;*.rar";
            public const string TextFiles = "文本文件|*.txt";
            public const string ImageAndArchive = "支持的文件|*.zip;*.7z;*.rar;*.jpg;*.png;*.bmp";
        }
        #endregion

        #region 组别名称
        public static class Groups
        {
            public const string Default = "框内";
            public const string Outside = "框外";
            public static readonly string[] Required = [Default, Outside];

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
        public static class Label
        {
            public const string NewLabelText = "新标签";
            public const string DefaultRemark = "这是备注";
            public const double DefaultFontSize = 20.0;
            public const string DefaultFontFamily = "微软雅黑";
        }
        #endregion

        #region 应用程序模式
        public enum AppMode { See, LabelDo, OCR }
        #endregion

        #region OCR 识别
        public static ICollection<string> OcrWebsiteKeys => OcrWebsites.Websites.Keys;
        public static class OcrWebsites
        {
            public static readonly Dictionary<string, string> Websites = new()
            {
                ["识字体网 (LikeFont)"] = "https://www.likefont.com/",
                ["AI识别 (YuzuMarker)"] = "https://huggingface.co/spaces/gyrojeff/YuzuMarker.FontDetection",
                ["必应"] = "https://www.bing.com/visualsearch"
            };

            public static ICollection<string> Keys => Websites.Keys;
            public const string DefaultWebsite = "AI识别 (YuzuMarker)";
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
            public const string OcrTemp = "OCRtemp";
            public const string ScreenShotTemp = "ScreenShottemp";
        }

        public static class Msg
        {
            #region 对话框
            public const string UnsavedPrompt = "当前翻译有未保存的修改，是否保存？";
            public const string UnsavedTitle = "提示";
            public const string ScreenshotPrompt = "请先截图，再点击识别";
            public const string AboutMessage = "本程序由No-Hifuu友情赞助";
            #endregion

            #region 图片/文件夹
            public const string NoFolderPath = "当前项目没有有效的文件夹路径可打开";
            public const string WorkspaceCleared = "工作区已清空";
            public const string NoImageSource = "无法找到有效的图片源";
            public const string ImageSetUpdated = "已更新图集，当前包含 {0} 张图片";
            public const string NoImages = "该路径下未找到支持的图片文件";
            public const string LoadSuccess = "{0} (已加载 {1} 张图片)";
            public const string ParseTxtFailed = "解析 TXT 失败: {0}";
            #endregion

            #region 压缩包
            public const string NoValidFolderPathForZip = "当前项目没有有效的文件夹路径";
            public const string NoZipFilesFound = "当前文件夹中没有找到压缩包文件";
            public const string ZipLinked = "已关联压缩包：{0}";
            public const string ZipLoadFailed = "加载压缩包失败: {0}";
            public const string ZipLinkCanceled = "已取消压缩包关联，切换到文件夹模式";
            public const string ReadZipErr = "读取压缩包失败: {0}";
            #endregion

            #region 保存
            public const string Saved = "已保存{0}到 {1}";
            public const string SaveErr = "保存失败: {0}";
            #endregion
        }
        #endregion
    }
}
