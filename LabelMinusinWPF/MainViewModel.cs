using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
namespace LabelMinusinWPF
{
    //TODO:目前ImageParent的伸缩会影响标记位置，需要改进

    public enum AppMode { See, LabelDo, OCR }
    public partial class MainViewModel : ObservableObject
    {
        // --- 状态属性/数据源 ---
        [ObservableProperty] private ProjectContext _currentProject = ProjectContext.Empty;
        public BindingList<ImageInfo> ImageList { get; } = [];
        [ObservableProperty] private ImageInfo? _selectedImage;
        #region 模式选择
        [ObservableProperty]
        private AppMode _currentMode = AppMode.LabelDo; // 默认选中第一个

        [RelayCommand]
        private void ChangeMode(AppMode newMode)
        {
            CurrentMode = newMode;
            if (CurrentMode == AppMode.See)
            {
                IsPictureIndexVisible = false;
                IsPictureTextVisible = false;
            }
            if (CurrentMode == AppMode.LabelDo)
            {
                IsPictureIndexVisible = true;
                IsPictureTextVisible = false;
            }
        }
        #endregion
        #region 文件处理
        [RelayCommand]
        private void OpenImage()
        {
            OpenFileDialog dialog = new()
            {
                Filter = "图片文件|*.jpg;*.png;*.bmp|压缩文件|*.zip;*.rar|所有文件|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                // 报错排查：检查 SelectedImage 是否为 null
                if (SelectedImage != null)
                {
                    SelectedImage.ImagePath = dialog.FileName;
                }
                else
                {
                    // 如果为空，创建一个新实例并选中它
                    var newImage = new ImageInfo { ImagePath = dialog.FileName };
                    ImageList.Add(newImage);
                    SelectedImage = newImage;
                }
            }
        }
        [RelayCommand]
        public void OpenTranslation()
        {
            // 1. 文件选择对话框
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "翻译文本 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                Title = "选择翻译项目文件"
            };

            if (dialog.ShowDialog() != true) return;

            string txtPath = dialog.FileName;

            try
            {
                // 2. 调用 Service 层进行解析 (建议放在 Task.Run 避免界面卡死)
                var (newContext, images) = ProjectService.LoadProjectFromTxt(txtPath);

                // 3. 更新 UI 数据
                // 由于删除了 OnCurrentProjectChanged，我们在这里手动赋值
                CurrentProject = newContext;

                ImageList.Clear();
                foreach (var img in images)
                {
                    ImageList.Add(img);
                }

                // 4. 默认选中第一张
                SelectedImage = ImageList.FirstOrDefault();
            }
            catch (Exception ex)
            {
                // 这里可以接入你的消息提示框
                System.Windows.MessageBox.Show($"加载项目失败: {ex.Message}");
            }
        }
        #endregion

        #region 菜单栏部分参数
        // 控制序号显示
        [ObservableProperty]
        private bool _isPictureIndexVisible = true;

        // 控制文本显示
        [ObservableProperty]
        private bool _isPictureTextVisible = true;
        #endregion

        #region 底栏/图片浏览
        [RelayCommand(CanExecute = nameof(CanGoToPrevious))]
        public void PreviousImage()
        {
            if (ImageList == null || ImageList.Count <= 1 || SelectedImage == null) return;

            int currentIndex = ImageList.IndexOf(SelectedImage);
            if (currentIndex > 0)
            {
                SelectedImage = ImageList[currentIndex - 1];
            }
        }
        private bool CanGoToPrevious() => ImageList != null && SelectedImage != null && ImageList.IndexOf(SelectedImage) > 0;
        [RelayCommand(CanExecute = nameof(CanGoToNext))]
        public void NextImage()
        {
            if (ImageList == null || ImageList.Count <= 1 || SelectedImage == null) return;

            int currentIndex = ImageList.IndexOf(SelectedImage);
            if (currentIndex < ImageList.Count - 1)
            {
                SelectedImage = ImageList[currentIndex + 1];
            }
        }
        private bool CanGoToNext() =>ImageList != null && SelectedImage != null && ImageList.IndexOf(SelectedImage) < ImageList.Count - 1;
        partial void OnSelectedImageChanged(ImageInfo? value)
        {
            PreviousImageCommand.NotifyCanExecuteChanged();
            NextImageCommand.NotifyCanExecuteChanged();
        }

        #endregion
    }

    #region 文件
    public record ProjectContext(
        string BaseFolderPath = "",
        string? TxtName = null,
        string? ZipName = null)
    {
        // 静态工厂：利用 record 的构造函数
        public static ProjectContext Empty => new();

        // 计算属性：使用 => 简写
        public string TxtPath => !string.IsNullOrEmpty(TxtName) ? Path.Combine(BaseFolderPath, TxtName) : "";
        public string ZipPath => !string.IsNullOrEmpty(ZipName) ? Path.Combine(BaseFolderPath, ZipName) : "";

        public bool IsArchiveMode => !string.IsNullOrEmpty(ZipName);

        public string DisplayTitle => $"LabelMinus - {TxtName ?? "未命名"} [{(IsArchiveMode ? $"关联:{ZipName}" : "文件夹")}]";
    }
    public static class ProjectService
    {
        private static readonly string[] Extensions = [".jpg", ".png", ".bmp", ".webp"];

        // 文件夹模式：ImagePath 存的是【绝对路径】
        public static List<ImageInfo> ScanFolder(string path) =>
            [.. Directory.EnumerateFiles(path)
            .Where(f => Extensions.Contains(Path.GetExtension(f).ToLower()))
            .Select(f => new ImageInfo { ImagePath = f })]; // 只需给 Path，Name 自动生成

        // 压缩包模式：ImagePath 存的是【EntryName】（如 "pics/1.jpg"）
        public static List<ImageInfo> ScanZip(string zipPath) =>
            [.. ArchiveHelper.GetImageEntries(zipPath)
            .Select(name => new ImageInfo { ImagePath = name })]; // 同样只需给 Path

        public static (ProjectContext Context, List<ImageInfo> Images) LoadProjectFromTxt(string txtFilePath)
        {
            string content = File.ReadAllText(txtFilePath);
            string baseFolder = Path.GetDirectoryName(txtFilePath) ?? "";

            // 1. 调用你现有的解析函数
            var database = Modules.ParseTextToLabels(content, out string? zipName);

            // 2. 创建 Context
            var context = new ProjectContext(baseFolder, Path.GetFileName(txtFilePath), zipName);

            // 3. 核心补全逻辑：将解析出来的图片名转为真实的 ImagePath
            foreach (var item in database)
            {
                var imgInfo = item.Value;
                if (context.IsArchiveMode)
                {
                    // 压缩包模式：ImagePath 存的是 EntryName（解析出来的就是）
                    imgInfo.ImagePath = item.Key;
                }
                else
                {
                    // 文件夹模式：ImagePath 需要拼接绝对路径
                    imgInfo.ImagePath = Path.Combine(baseFolder, item.Key);
                }
            }

            return (context, [.. database.Values]);
        }
    }
    #endregion
}
