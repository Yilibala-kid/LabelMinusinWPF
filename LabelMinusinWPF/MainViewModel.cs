using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using SharpCompress.Archives;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using static LabelMinusinWPF.Modules;
namespace LabelMinusinWPF
{
    //TODO:目前ImageParent的伸缩会影响标记位置，需要改进

    
    public partial class MainViewModel : ObservableObject
    {
        // --- 状态属性/数据源 ---
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(OpenNowFolderCommand))]
        [NotifyCanExecuteChangedFor(nameof(SaveTranslationCommand))]
        private ProjectContext _currentProject = ProjectContext.Empty;
        public BindingList<ImageInfo> ImageList { get; } = [];
        [ObservableProperty] private ImageInfo? _selectedImage;
        #region 模式选择

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
    #region 模式选择
    public enum AppMode { See, LabelDo, OCR }
    public partial class MainViewModel
    {
        [ObservableProperty]
        private AppMode _currentMode = AppMode.LabelDo; // 默认值
        partial void OnCurrentModeChanged(AppMode value)// 一个钩子方法，当 CurrentMode 发生改变时会自动被调用
        {
            switch (value)
            {
                case AppMode.See:
                    IsPictureIndexVisible = false;
                    IsPictureTextVisible = false;
                    break;
                case AppMode.LabelDo:
                    IsPictureIndexVisible = true;
                    IsPictureTextVisible = false;
                    break;
                case AppMode.OCR:
                    // 处理 OCR 模式下的逻辑
                    // 例如: IsPictureIndexVisible = false;
                    IsPictureIndexVisible = false;
                    IsPictureTextVisible = false;
                    break;
            }
        }
    }
    public class EnumToBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return false;

            // 检查当前 ViewModel 的值是否等于 ConverterParameter 传入的值
            return value.Equals(parameter);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 当 UI 选中 (IsChecked = true) 时，将 ConverterParameter 的值回写给 ViewModel
            return (bool)value ? parameter : Binding.DoNothing;
        }
    }

    #endregion


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

        public string DisplayTitle => $"LabelMinus - {TxtPath ?? "未命名"} 【{(IsArchiveMode ? $"关联:{ZipName}" : "文件夹")}】";
    }
    public static class ProjectService
    {
        public static readonly string[] ImageExtensions = [".jpg", ".png", ".bmp", ".webp"];
        public static readonly string[] ZipExtensions = [".7z", ".zip", ".rar"];

        // 文件夹模式：ImagePath 存的是【绝对路径】
        public static List<ImageInfo> ScanFolder(string path) =>
            [.. Directory.EnumerateFiles(path)
            .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLower()))
            .Select(f => new ImageInfo { ImagePath = f })]; // 只需给 Path，Name 自动生成

        // 压缩包模式：ImagePath 存的是【EntryName】（如 "pics/1.jpg"）
        public static List<ImageInfo> ScanZip(string zipPath) =>
            [.. ArchiveHelper.GetImagePath(zipPath)
            .Select(f => new ImageInfo { ImagePath = f })]; // 同样只需给 Path

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

    #region 消息通知
    public partial class MainViewModel
    {
        // 定义消息队列
        [ObservableProperty]
        private ISnackbarMessageQueue _mainMessageQueue;

        public MainViewModel()
        {
            // 实例化队列
            MainMessageQueue = new SnackbarMessageQueue(TimeSpan.FromSeconds(2));
        }
    }
    #endregion

    #region 文件处理Partial
    public partial class MainViewModel
    {
        //CurrentProject和ImageList在上面
        #region 菜单命令：新建与打开与保存
        [RelayCommand]
        private void NewFolderTranslation() // 新建文件夹翻译
        {
            string? folder = DialogService.OpenFolder("选择要新建翻译的文件夹");
            if (!string.IsNullOrEmpty(folder)) OpenResourceByPath([folder], true);
        }

        [RelayCommand]
        private void NewZipTranslation() // 新建压缩包翻译
        {
            string? zipPath = DialogService.OpenFile("压缩文件|*.zip;*.7z;*.rar");
            if (!string.IsNullOrEmpty(zipPath)) OpenResourceByPath([zipPath], true);
        }

        [RelayCommand]
        private void OpenTranslation() // 打开现有 Txt
        {
            string? txtPath = DialogService.OpenFile("文本文件|*.txt");
            if (!string.IsNullOrEmpty(txtPath)) OpenResourceByPath([txtPath], false);
        }

        [RelayCommand]
        private void OpenImageFolder() // 仅预览文件夹
        {
            string? folder = DialogService.OpenFolder("选择要预览的文件夹");
            if (!string.IsNullOrEmpty(folder)) OpenResourceByPath([folder], false);
        }

        [RelayCommand]
        private void OpenImageOrZip() // 仅预览图片/压缩包
        {
            string[]? filepaths = DialogService.OpenFiles("支持的文件|*.zip;*.7z;*.rar;*.jpg;*.png;*.bmp");
            if (filepaths != null && filepaths.Length > 0)
            {
                OpenResourceByPath(filepaths, false);
            }
        }
        private bool CanSave() => CurrentProject != ProjectContext.Empty;
        [RelayCommand(CanExecute = nameof(CanSave))]
        private void SaveTranslation(string? mode)
        {
            bool isSaveAs = mode is string s && s == "As";

            string? targetPath = isSaveAs ? null : CurrentProject.TxtPath;
            DoSave(targetPath);
        }
        [RelayCommand(CanExecute = nameof(CanSave))]
        private void OpenNowFolder()
        {             
            string pathToOpen = CurrentProject.BaseFolderPath;
            if (!string.IsNullOrEmpty(pathToOpen) && Directory.Exists(pathToOpen))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = pathToOpen,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            else
            {
                MainMessageQueue.Enqueue("当前项目没有有效的文件夹路径可打开");
            }
        }
        #endregion


        //TODO：似乎有文件会被直接覆盖的bug，比如万一用户选了一个已经存在的txt路径作为新建翻译的目标路径，这时候会直接覆盖掉原来的txt文件，应该在保存的时候加个判断，如果文件已经存在了就提示用户是否覆盖
        #region 核心函数：加载与保存
        private void LoadProjectData(ProjectContext context, List<ImageInfo> images, string successMsg)//统一处理数据加载
        {
            if (!Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.Invoke(() => LoadProjectData(context, images, successMsg));
                return;
            }
            if (images.Count == 0)
            {
                MainMessageQueue.Enqueue("该路径下未找到支持的图片文件");
                return;
            }

            // 1. 更新上下文
            CurrentProject = context;

            // 2. 更新列表
            ImageList.Clear();
            foreach (var img in images) ImageList.Add(img);

            // 3. 选中第一张
            SelectedImage = ImageList.FirstOrDefault();

            MainMessageQueue.Enqueue($"{successMsg} (已加载 {images.Count} 张图片)");
        }
        /// <param name="paths">文件/文件夹路径数组</param>
        /// <param name="isCreateMode">是否为新建模式（决定是否自动生成保存文件名）</param>
        /// // 在 MainViewModel 类内部或外部定义
        public void OpenResourceByPath(string[]? paths, bool isCreateMode)// 统一资源入口：根据路径自动识别类型并加载
        {
            if (paths == null || paths.Length == 0) return;

            string firstPath = paths[0];
            string ext = Path.GetExtension(firstPath).ToLower();

            // --- 1. 优先处理图片组 (多选图片 或 单张图片) ---
            // 逻辑：只要输入中包含支持的图片，就视为“查看/编辑这些特定图片”模式
            var imageFiles = paths
                .Where(p => File.Exists(p) && ProjectService.ImageExtensions.Contains(Path.GetExtension(p).ToLower()))
                .ToList();

            if (imageFiles.Count > 0)
            {
                // 既然是直接选的图片，BaseFolderPath 就定为第一张图片所在的文件夹
                string baseFolder = Path.GetDirectoryName(imageFiles[0]) ?? string.Empty;
                // 构造 ImageInfo 列表
                var images = imageFiles.Select(p => new ImageInfo { ImagePath = p }).ToList();
                // 设定 Context
                // 如果是新建模式，我们可以预设一个文件名（比如 "选定图片_翻译.txt"），否则为 null (纯预览)
                string? defaultTxtName = isCreateMode ? "New_Translation.txt" : null;

                var context = new ProjectContext(baseFolder, defaultTxtName, null);

                LoadProjectData(context, images, isCreateMode ? "正在为一组图片创建翻译" : "正在预览选定图片");
                if (isCreateMode) DoSave(context.TxtPath); // 新建模式下立即保存
                return;
            }

            // --- 2. 处理翻译文档 (.txt) ---
            if (ext == ".txt" && File.Exists(firstPath))
            {
                try
                {
                    var (context, images) = ProjectService.LoadProjectFromTxt(firstPath);
                    LoadProjectData(context, images, $"已加载翻译：{context.TxtName}");
                }
                catch (Exception ex)
                {
                    MainMessageQueue.Enqueue($"解析 TXT 失败: {ex.Message}");
                }
                return;
            }

            // --- 3. 处理压缩包 (.zip, .rar, .7z) ---
            if (ProjectService.ZipExtensions.Contains(ext) && File.Exists(firstPath))
            {
                try
                {
                    // 扫描压缩包内容
                    var images = ProjectService.ScanZip(firstPath);

                    string baseFolder = Path.GetDirectoryName(firstPath) ?? string.Empty;
                    string zipName = Path.GetFileName(firstPath);

                    // 核心逻辑：新建模式自动生成 "xxx_翻译.txt"，预览模式则为 null
                    string? txtName = isCreateMode
                        ? Path.GetFileNameWithoutExtension(zipName) + "_翻译.txt"
                        : null;

                    var context = new ProjectContext(baseFolder, txtName, zipName);

                    LoadProjectData(context, images, isCreateMode
                        ? $"正在为压缩包【{zipName}】创建翻译"
                        : $"正在预览压缩包：{zipName}");
                    if (isCreateMode) DoSave(context.TxtPath); // 新建模式下立即保存
                }
                catch (Exception ex)
                {
                    MainMessageQueue.Enqueue($"读取压缩包失败: {ex.Message}");
                }
                return;
            }

            // --- 4. 处理文件夹 ---
            if (Directory.Exists(firstPath))
            {
                // 扫描文件夹
                var images = ProjectService.ScanFolder(firstPath);

                // 新建模式默认叫 "新建翻译.txt"，预览模式 null
                string? txtName = isCreateMode ? "新建翻译.txt" : null;

                var context = new ProjectContext(firstPath, txtName, null);

                LoadProjectData(context, images, isCreateMode
                    ? $"正在为文件夹【{firstPath}】创建翻译"
                    : $"正在预览文件夹：{firstPath}");
                if (isCreateMode) DoSave(context.TxtPath); // 新建模式下立即保存
                return;
            }
        }
        private void DoSave(string? targetPath)
        {
            if (ImageList.Count == 0) return;

            // 如果没有目标路径（新建模式 或 另存为），则请求路径
            if (string.IsNullOrEmpty(targetPath))
            {
                string defaultName = CurrentProject.IsArchiveMode
                    ? Path.GetFileNameWithoutExtension(CurrentProject.ZipName) + "_翻译.txt"
                    : "新建翻译.txt";

                targetPath = DialogService.SaveFile("文本文件|*.txt", defaultName);
                if (string.IsNullOrEmpty(targetPath)) return; // 用户取消
            }

            try
            {
                string outputText = Modules.LabelsToText(
                                    ImageList, // 直接传集合
                                    CurrentProject.ZipName,
                                    ExportMode.Current
                                );
                File.WriteAllText(targetPath, outputText);

                // 2. 保存成功后，更新当前上下文（比如从“新建”变成了“具体文件”）
                string newFolder = Path.GetDirectoryName(targetPath)!;
                string newTxtName = Path.GetFileName(targetPath);

                // Record 是不可变的，所以用 with 或者 new 创建新实例
                CurrentProject = new ProjectContext(newFolder, newTxtName, CurrentProject.ZipName);

                MainMessageQueue.Enqueue($"已保存翻译到 {targetPath}");
            }
            catch (Exception ex)
            {
                MainMessageQueue.Enqueue($"保存失败: {ex.Message}");
            }
        }
        #endregion

        public class DialogService
        {
            public static string? OpenFolder(string description)
            {
                // .NET Core 3.0+ / .NET 5+ 推荐使用 OpenFolderDialog
                var dialog = new OpenFolderDialog { Title = description };
                return dialog.ShowDialog() == true ? dialog.FolderName : null;
            }

            public static string[]? OpenFiles(string filter)
            {
                var dialog = new OpenFileDialog { Filter = filter, Multiselect = true };
                return dialog.ShowDialog() == true ? dialog.FileNames : null;
            }

            public static string? OpenFile(string filter, bool multiselect = false)
            {
                var dialog = new OpenFileDialog { Filter = filter, Multiselect = multiselect };
                return dialog.ShowDialog() == true ? dialog.FileName : null;
            }

            public static string? SaveFile(string filter, string defaultName)
            {
                var dialog = new SaveFileDialog { Filter = filter, FileName = defaultName };
                return dialog.ShowDialog() == true ? dialog.FileName : null;
            }

            public static void ShowMessage(string message, bool isError)
            {
                MessageBox.Show(message, isError ? "错误" : "提示", MessageBoxButton.OK, isError ? MessageBoxImage.Error : MessageBoxImage.Information);
            }
        }
    }
    #endregion
}
