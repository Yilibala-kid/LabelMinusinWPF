using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using static LabelMinusinWPF.Modules;
using LabelMinusinWPF.Utilities;

namespace LabelMinusinWPF
{
    /// <summary>组别显示项：名称 + 对应颜色</summary>
    public record GroupItem(string Name, SolidColorBrush Brush);
    /// <summary>
    /// 主视图模型：管理图片列表、选中状态、模式切换等核心逻辑
    /// </summary>
    public partial class MainViewModel : ObservableObject
    {
        #region 初始化
        /// <summary>底部 Snackbar 消息队列</summary>
        [ObservableProperty]
        private ISnackbarMessageQueue _mainMessageQueue;
        public MainViewModel()
        {
            MainMessageQueue = new SnackbarMessageQueue(TimeSpan.FromSeconds(2));
            ImageList.ListChanged += OnImageListChanged;
        }

        private readonly HashSet<ImageInfo> _hookedImages = [];

        /// <summary>抑制组别刷新（批量操作、防循环触发时使用）</summary>
        private bool _suppressGroupNotify;

        private void OnImageListChanged(object? sender, ListChangedEventArgs e)
        {
            // 增删图片时同步 Labels 事件绑定
            switch (e.ListChangedType)
            {
                case ListChangedType.ItemAdded:
                    HookImage(ImageList[e.NewIndex]);
                    break;
                case ListChangedType.ItemDeleted:
                    // BindingList 已移除该项，通过差集找到并解绑
                    foreach (var img in _hookedImages.Except(ImageList).ToList())
                        UnhookImage(img);
                    break;
                case ListChangedType.Reset:
                    UnhookAllImages();
                    break;
            }
            NotifyGroupsChanged();
        }

        private void HookImage(ImageInfo img)
        {
            img.Labels.ListChanged -= OnLabelsChanged;
            img.Labels.ListChanged += OnLabelsChanged;
            _hookedImages.Add(img);
        }

        private void UnhookImage(ImageInfo img)
        {
            img.Labels.ListChanged -= OnLabelsChanged;
            _hookedImages.Remove(img);
        }

        private void UnhookAllImages()
        {
            foreach (var img in _hookedImages)
                img.Labels.ListChanged -= OnLabelsChanged;
            _hookedImages.Clear();
        }

        private void OnLabelsChanged(object? sender, ListChangedEventArgs e) =>
            NotifyGroupsChanged();
        #endregion
        // --- 项目上下文 ---
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(OpenNowFolderCommand))]
        [NotifyCanExecuteChangedFor(nameof(SaveTranslationCommand))]
        [NotifyCanExecuteChangedFor(nameof(AdjustImageSetCommand))]
        [NotifyCanExecuteChangedFor(nameof(AssociateZipCommand))]
        [NotifyCanExecuteChangedFor(nameof(ExportCurrentCommand))]
        [NotifyCanExecuteChangedFor(nameof(ExportOriginalCommand))]
        [NotifyCanExecuteChangedFor(nameof(ExportDiffCommand))]
        [NotifyCanExecuteChangedFor(nameof(ClearWorkspaceCommand))]
        private ProjectContext _currentProject = ProjectContext.Empty;

        /// <summary>当前加载的图片列表（使用 BindingList 以支持 ListChanged 事件）</summary>
        public BindingList<ImageInfo> ImageList { get; } = [];

        /// <summary>当前选中的图片</summary>
        [ObservableProperty]
        private ImageInfo? _selectedImage;

        #region 组别汇总
        // 初始化时就带上默认值
        private List<GroupItem> _allGroupsCache =
        [
            new GroupItem("框内", GroupBrushes[0]),
            new GroupItem("框外", GroupBrushes[1]),
        ];
        public IReadOnlyList<GroupItem> AllGroups => _allGroupsCache;

        private void RebuildGroupsCache()
        {
            // 定义必须存在的组
            string[] defaultGroups = ["框内", "框外"];

            _allGroupsCache = ImageList
                .SelectMany(img => img.Labels.Select(l => l.Group)) // 提取所有已存在的组名
                .Concat(defaultGroups)                             // 强制注入“框内”“框外”
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Distinct()                                        // 去重
                .OrderBy(g => g == "框内" ? 0 : (g == "框外" ? 1 : 2))
                .ThenBy(g => g)
                .Select((g, i) => new GroupItem(g, GroupBrushes[i % GroupBrushes.Length]))
                .ToList();

            // 同步每个标签的组别颜色（逻辑保持不变）
            var brushMap = _allGroupsCache.ToDictionary(g => g.Name, g => g.Brush);
            foreach (var img in ImageList)
            {
                foreach (var label in img.Labels)
                {
                    if (brushMap.TryGetValue(label.Group, out var brush))
                        label.GroupBrush = brush;
                }
            }
        }

        private static readonly SolidColorBrush[] GroupBrushes = CreateGroupBrushes();

        private static SolidColorBrush[] CreateGroupBrushes()
        {
            Color[] colors =
            [
                Color.FromRgb(234, 67, 53),
                Color.FromRgb(66, 133, 244),
                Color.FromRgb(52, 168, 83),
                Color.FromRgb(251, 188, 4),
                Color.FromRgb(171, 71, 188),
                Color.FromRgb(0, 172, 193),
                Color.FromRgb(255, 112, 67),
                Color.FromRgb(141, 110, 99),
            ];
            return colors
                .Select(c =>
                {
                    var b = new SolidColorBrush(c);
                    b.Freeze();
                    return b;
                })
                .ToArray();
        }

        [ObservableProperty]
        private string? _selectedGroupName = "框内";

        partial void OnSelectedGroupNameChanged(string? value)
        {
            if (_suppressGroupNotify || string.IsNullOrEmpty(value) || SelectedImage == null)
                return;

            SelectedImage.ActiveGroup = value;
            if (SelectedImage.SelectedLabel is { } label && label.Group != value)
            {
                // 修改 label.Group 会触发 OnLabelsChanged → NotifyGroupsChanged，
                // 用 _suppressGroupNotify 阻断级联，之后手动刷新一次
                _suppressGroupNotify = true;
                try
                {
                    label.Group = value;
                }
                finally
                {
                    _suppressGroupNotify = false;
                }
                NotifyGroupsChanged();
            }
        }

        private void NotifyGroupsChanged()
        {
            if (_suppressGroupNotify)
                return;

            var savedGroup = SelectedGroupName;

            // 重建缓存期间抑制 ListBox 选中项变化引起的回写
            _suppressGroupNotify = true;
            try
            {
                RebuildGroupsCache();
                OnPropertyChanged(nameof(AllGroups));
            }
            finally
            {
                _suppressGroupNotify = false;
            }

            // 恢复选中状态
            var names = _allGroupsCache.ConvertAll(g => g.Name);
            SelectedGroupName = names.Contains(savedGroup)
                ? savedGroup
                : names.FirstOrDefault() ?? "框内";
        }
        #endregion

        #region 菜单栏：显示控制
        [ObservableProperty]
        private bool _isPictureIndexVisible = true;

        [ObservableProperty]
        private bool _isPictureTextVisible = true;
        #endregion

        #region OCR识别模式

        // 公开字典，让 XAML 和后台都能直接访问
        public Dictionary<string, string> OcrWebsites { get; } = new()
        {
            ["识字体网 (LikeFont)"] = "https://www.likefont.com/",
            ["AI识别 (YuzuMarker)"] = "https://huggingface.co/spaces/gyrojeff/YuzuMarker.FontDetection",
            ["必应"] = "https://www.bing.com/visualsearch"
        };

        [ObservableProperty]
        private string _selectedOcrWebsite = "AI识别 (YuzuMarker)";

        #endregion

        #region 底栏：图片切换
        [RelayCommand(CanExecute = nameof(CanGoToPrevious))]
        public void PreviousImage()
        {
            int currentIndex = ImageList.IndexOf(SelectedImage!);
            if (currentIndex > 0)
                SelectedImage = ImageList[currentIndex - 1];
        }

        private bool CanGoToPrevious() =>
            SelectedImage != null && ImageList.IndexOf(SelectedImage) > 0;

        [RelayCommand(CanExecute = nameof(CanGoToNext))]
        public void NextImage()
        {
            int currentIndex = ImageList.IndexOf(SelectedImage!);
            if (currentIndex < ImageList.Count - 1)
                SelectedImage = ImageList[currentIndex + 1];
        }

        private bool CanGoToNext() =>
            SelectedImage != null && ImageList.IndexOf(SelectedImage) < ImageList.Count - 1;

        private ImageInfo? _watchedImage;

        /// <summary>选中图片变更时：刷新按钮状态、监听 SelectedLabel 以同步组别</summary>
        partial void OnSelectedImageChanged(ImageInfo? value)
        {
            if (_watchedImage != null)
                _watchedImage.PropertyChanged -= OnImageSelectedLabelChanged;
            _watchedImage = value;
            if (value != null)
            {
                value.PropertyChanged += OnImageSelectedLabelChanged;
                if (value.SelectedLabel != null)
                    SelectedGroupName = value.SelectedLabel.Group;
            }

            PreviousImageCommand.NotifyCanExecuteChanged();
            NextImageCommand.NotifyCanExecuteChanged();
        }

        /// <summary>当前图片的 SelectedLabel 变更时，同步高亮到对应组别</summary>
        private void OnImageSelectedLabelChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (
                e.PropertyName == nameof(ImageInfo.SelectedLabel)
                && sender is ImageInfo img
                && img.SelectedLabel != null
            )
                SelectedGroupName = img.SelectedLabel.Group;
        }
        #endregion
        #region 标签切换
        [RelayCommand(CanExecute = nameof(CanGoToPreviousLabel))]
        public void PreviousLabel()
        {
            if (SelectedImage == null) return;

            var activeLabels = SelectedImage.ActiveLabels;
            if (activeLabels.Count == 0) return;

            // 如果当前没选中标签，按“上一个”则选中最后一个
            if (SelectedImage.SelectedLabel == null)
            {
                SelectedImage.SelectedLabel = activeLabels[activeLabels.Count - 1];
                return;
            }

            int currentIndex = activeLabels.IndexOf(SelectedImage.SelectedLabel);
            if (currentIndex > 0)
                SelectedImage.SelectedLabel = activeLabels[currentIndex - 1];
        }

        private bool CanGoToPreviousLabel()
        {
            if (SelectedImage == null) return false;
            var activeLabels = SelectedImage.ActiveLabels;
            if (activeLabels.Count == 0) return false;
            if (SelectedImage.SelectedLabel == null) return true;

            return activeLabels.IndexOf(SelectedImage.SelectedLabel) > 0;
        }

        [RelayCommand(CanExecute = nameof(CanGoToNextLabel))]
        public void NextLabel()
        {
            if (SelectedImage == null) return;

            var activeLabels = SelectedImage.ActiveLabels;
            if (activeLabels.Count == 0) return;

            // 如果当前没选中标签，按“下一个”则选中第一个
            if (SelectedImage.SelectedLabel == null)
            {
                SelectedImage.SelectedLabel = activeLabels[0];
                return;
            }

            int currentIndex = activeLabels.IndexOf(SelectedImage.SelectedLabel);
            if (currentIndex >= 0 && currentIndex < activeLabels.Count - 1)
                SelectedImage.SelectedLabel = activeLabels[currentIndex + 1];
        }

        private bool CanGoToNextLabel()
        {
            if (SelectedImage == null) return false;
            var activeLabels = SelectedImage.ActiveLabels;
            if (activeLabels.Count == 0) return false;
            if (SelectedImage.SelectedLabel == null) return true;

            int currentIndex = activeLabels.IndexOf(SelectedImage.SelectedLabel);
            return currentIndex >= 0 && currentIndex < activeLabels.Count - 1;
        }
        #endregion
    }

    #region 模式选择

    public enum AppMode
    {
        See,
        LabelDo,
        OCR,
    }

    public partial class MainViewModel
    {
        [ObservableProperty]
        private AppMode _currentMode = AppMode.LabelDo;

        /// <summary>模式切换时更新界面元素的可见性</summary>
        partial void OnCurrentModeChanged(AppMode value)
        {
            switch (value)
            {
                case AppMode.See:
                case AppMode.OCR:
                    IsPictureIndexVisible = false;
                    IsPictureTextVisible = false;
                    break;
                case AppMode.LabelDo:
                    IsPictureIndexVisible = true;
                    IsPictureTextVisible = true;
                    break;
            }
        }
        [RelayCommand]
        private void SetAppMode(AppMode mode)
        {
            CurrentMode = mode;
        }
    }


    #endregion

    #region 文件处理
    public partial class MainViewModel
    {
        #region 菜单命令：新建 / 打开 / 保存

        /// <summary>新建文件夹翻译</summary>
        [RelayCommand]
        private void NewFolderTranslation()
        {
            string? folder = DialogService.OpenFolder("选择要新建翻译的文件夹");
            if (!string.IsNullOrEmpty(folder))
                OpenResourceByPath([folder], true);
        }

        /// <summary>新建压缩包翻译</summary>
        [RelayCommand]
        private void NewZipTranslation()
        {
            string? zipPath = DialogService.OpenFile(
                "压缩文件|*.zip;*.7z;*.rar",
                "选择要新建翻译的压缩包"
            );
            if (!string.IsNullOrEmpty(zipPath))
                OpenResourceByPath([zipPath], true);
        }

        /// <summary>打开已有的翻译 txt 文件</summary>
        [RelayCommand]
        private void OpenTranslation()
        {
            string? txtPath = DialogService.OpenFile("文本文件|*.txt", "打开已有翻译");
            if (!string.IsNullOrEmpty(txtPath))
                OpenResourceByPath([txtPath], false);
        }

        /// <summary>仅预览文件夹中的图片</summary>
        [RelayCommand]
        private void OpenImageFolder()
        {
            string? folder = DialogService.OpenFolder("选择要预览的文件夹");
            if (!string.IsNullOrEmpty(folder))
                OpenResourceByPath([folder], false);
        }

        /// <summary>仅预览图片或压缩包</summary>
        [RelayCommand]
        private void OpenImageOrZip()
        {
            string[]? filepaths = DialogService.OpenFiles(
                "支持的文件|*.zip;*.7z;*.rar;*.jpg;*.png;*.bmp",
                "选择要预览的图片（多张）或压缩包（单个）"
            );
            if (filepaths is { Length: > 0 })
                OpenResourceByPath(filepaths, false);
        }

        /// <summary>仅预览图片</summary>
        [RelayCommand]
        private void OpenImage()
        {
            string[]? filepaths = DialogService.OpenFiles(
                "支持的文件|*.jpg;*.png;*.bmp",
                "选择要预览的图片（多张）"
            );
            if (filepaths is { Length: > 0 })
                OpenResourceByPath(filepaths, false);
        }

        /// <summary>仅预览压缩包</summary>
        [RelayCommand]
        private void OpenZip()
        {
            string? zipPath = DialogService.OpenFile(
                "压缩文件|*.zip;*.7z;*.rar",
                "选择要预览的压缩包"
            );
            if (!string.IsNullOrEmpty(zipPath))
                OpenResourceByPath([zipPath], false);
        }

        private bool CanSave() => CurrentProject != ProjectContext.Empty;

        /// <summary>保存翻译（mode 为 "As" 时另存为）</summary>
        [RelayCommand(CanExecute = nameof(CanSave))]
        private void SaveTranslation(string? mode)
        {
            bool isSaveAs = mode is "As";
            DoSave(isSaveAs ? null : CurrentProject.TxtPath, ExportMode.Current, updateContext: true);
        }

        /// <summary>在资源管理器中打开当前项目所在文件夹</summary>
        [RelayCommand(CanExecute = nameof(CanSave))]
        private void OpenNowFolder()
        {
            string pathToOpen = CurrentProject.BaseFolderPath;
            if (!string.IsNullOrEmpty(pathToOpen) && Directory.Exists(pathToOpen))
            {
                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = pathToOpen,
                        UseShellExecute = true,
                        Verb = "open",
                    }
                );
            }
            else
            {
                MainMessageQueue.Enqueue("当前项目没有有效的文件夹路径可打开");
            }
        }

        /// <summary>清空工作区</summary>
        [RelayCommand(CanExecute = nameof(CanSave))]
        private void ClearWorkspace()
        {
            // 检查是否有未保存的修改
            if (HasUnsavedChanges())
            {
                var result = MessageBox.Show(
                    "当前翻译有未保存的修改，是否保存？",
                    "提示",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question
                );

                if (result == MessageBoxResult.Yes)
                {
                    SaveTranslation(null);
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    return;
                }
            }

            // 清空所有数据
            _suppressGroupNotify = true;
            try
            {
                ImageList.Clear();
                SelectedImage = null;
                CurrentProject = ProjectContext.Empty;
            }
            finally
            {
                _suppressGroupNotify = false;
            }
            NotifyGroupsChanged();

            MainMessageQueue.Enqueue("工作区已清空");
        }

        /// <summary>检查是否有未保存的修改</summary>
        public bool HasUnsavedChanges()
        {
            return ImageList.Any(img => img.Labels.Any(l => l.IsModified));
        }

        /// <summary>调整图集：选择当前工作文件夹或压缩包中需要包含的图片</summary>
        [RelayCommand(CanExecute = nameof(CanSave))]
        private void AdjustImageSet()
        {
            List<ImageInfo> availableImages;

            // 根据是否关联压缩包，获取可用图片列表
            if (CurrentProject.IsArchiveMode && File.Exists(CurrentProject.ZipPath))
            {
                availableImages = ProjectService.ScanZip(CurrentProject.ZipPath);
            }
            else if (Directory.Exists(CurrentProject.BaseFolderPath))
            {
                availableImages = ProjectService.ScanFolder(CurrentProject.BaseFolderPath);
            }
            else
            {
                MainMessageQueue.Enqueue("无法找到有效的图片源");
                return;
            }

            // 创建选择对话框
            var dialog = new ImageSelectionDialog(availableImages, ImageList.ToList());
            if (dialog.ShowDialog() == true)
            {
                var selectedImages = dialog.SelectedImages;

                // 保留已有标注数据
                var existingData = ImageList.ToDictionary(img => img.ImageName, img => img);

                _suppressGroupNotify = true;
                try
                {
                    ImageList.Clear();
                    foreach (var img in selectedImages)
                    {
                        // 如果该图片之前有标注数据，恢复它
                        if (existingData.TryGetValue(img.ImageName, out var existingImg))
                        {
                            ImageList.Add(existingImg);
                        }
                        else
                        {
                            ImageList.Add(img);
                        }
                    }
                }
                finally
                {
                    _suppressGroupNotify = false;
                }
                NotifyGroupsChanged();

                SelectedImage = ImageList.FirstOrDefault();
                MainMessageQueue.Enqueue($"已更新图集，当前包含 {ImageList.Count} 张图片");
            }
        }

        /// <summary>关联压缩包：选择或取消关联压缩包</summary>
        [RelayCommand(CanExecute = nameof(CanSave))]
        private void AssociateZip()
        {
            if (!Directory.Exists(CurrentProject.BaseFolderPath))
            {
                MainMessageQueue.Enqueue("当前项目没有有效的文件夹路径");
                return;
            }

            // 获取当前文件夹中的所有压缩包
            var zipFiles = Directory.GetFiles(CurrentProject.BaseFolderPath)
                .Where(f => ProjectService.ZipExtensions.Contains(Path.GetExtension(f)))
                .Select(f => Path.GetFileName(f))
                .ToList();

            if (zipFiles.Count == 0)
            {
                MainMessageQueue.Enqueue("当前文件夹中没有找到压缩包文件");
                return;
            }

            // 创建选择对话框
            var dialog = new ZipSelectionDialog(zipFiles, CurrentProject.ZipName);
            if (dialog.ShowDialog() == true)
            {
                string? selectedZip = dialog.SelectedZip;

                // 更新项目上下文
                CurrentProject = new ProjectContext(
                    CurrentProject.BaseFolderPath,
                    CurrentProject.TxtName,
                    selectedZip
                );

                // 重新加载图片列表
                if (!string.IsNullOrEmpty(selectedZip))
                {
                    try
                    {
                        var zipImages = ProjectService.ScanZip(CurrentProject.ZipPath);
                        var existingData = ImageList.ToDictionary(img => img.ImageName, img => img);

                        _suppressGroupNotify = true;
                        try
                        {
                            ImageList.Clear();
                            foreach (var img in zipImages)
                            {
                                if (existingData.TryGetValue(img.ImageName, out var existingImg))
                                {
                                    existingImg.ImagePath = img.ImagePath;
                                    ImageList.Add(existingImg);
                                }
                                else
                                {
                                    ImageList.Add(img);
                                }
                            }
                        }
                        finally
                        {
                            _suppressGroupNotify = false;
                        }
                        NotifyGroupsChanged();

                        SelectedImage = ImageList.FirstOrDefault();
                        MainMessageQueue.Enqueue($"已关联压缩包：{selectedZip}");
                    }
                    catch (Exception ex)
                    {
                        MainMessageQueue.Enqueue($"加载压缩包失败: {ex.Message}");
                    }
                }
                else
                {
                    // 取消关联，切换到文件夹模式
                    var folderImages = ProjectService.ScanFolder(CurrentProject.BaseFolderPath);
                    var existingData = ImageList.ToDictionary(img => img.ImageName, img => img);

                    _suppressGroupNotify = true;
                    try
                    {
                        ImageList.Clear();
                        foreach (var img in folderImages)
                        {
                            if (existingData.TryGetValue(img.ImageName, out var existingImg))
                            {
                                existingImg.ImagePath = img.ImagePath;
                                ImageList.Add(existingImg);
                            }
                            else
                            {
                                ImageList.Add(img);
                            }
                        }
                    }
                    finally
                    {
                        _suppressGroupNotify = false;
                    }
                    NotifyGroupsChanged();

                    SelectedImage = ImageList.FirstOrDefault();
                    MainMessageQueue.Enqueue("已取消压缩包关联，切换到文件夹模式");
                }
            }
        }
        #endregion

        //TODO：似乎有文件会被直接覆盖的bug，比如万一用户选了一个已经存在的txt路径作为新建翻译的目标路径，这时候会直接覆盖掉原来的txt文件，应该在保存的时候加个判断，如果文件已经存在了就提示用户是否覆盖

        #region 核心函数：加载与保存

        /// <summary>
        /// 统一数据加载入口：更新上下文、填充图片列表、选中首张图片
        /// </summary>
        private void LoadProjectData(
            ProjectContext context,
            List<ImageInfo> images,
            string successMsg
        )
        {
            // 确保在 UI 线程执行（支持从后台线程调用）
            if (!Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.Invoke(() =>
                    LoadProjectData(context, images, successMsg)
                );
                return;
            }

            if (images.Count == 0)
            {
                MainMessageQueue.Enqueue("该路径下未找到支持的图片文件");
                return;
            }

            CurrentProject = context;

            // 批量操作：抑制每次 Add 触发的 NotifyGroupsChanged，最后统一刷新一次
            _suppressGroupNotify = true;
            try
            {
                ImageList.Clear();
                foreach (var img in images)
                    ImageList.Add(img);
            }
            finally
            {
                _suppressGroupNotify = false;
            }
            NotifyGroupsChanged();

            SelectedImage = ImageList.FirstOrDefault();
            MainMessageQueue.Enqueue($"{successMsg} (已加载 {images.Count} 张图片)");
        }

        /// <summary>
        /// 统一资源入口：根据路径自动识别资源类型（图片/txt/压缩包/文件夹）并加载
        /// </summary>
        /// <param name="paths">文件或文件夹路径数组</param>
        /// <param name="isCreateMode">true=新建模式（自动生成保存文件名），false=预览/打开模式</param>
        public void OpenResourceByPath(string[]? paths, bool isCreateMode)
        {
            if (paths is not { Length: > 0 })
                return;

            string firstPath = paths[0];
            string ext = Path.GetExtension(firstPath);

            // --- 1. 图片组：只要输入中包含支持的图片，就直接加载这些图片 ---
            var imageFiles = paths
                .Where(p =>
                    File.Exists(p) && ProjectService.ImageExtensions.Contains(Path.GetExtension(p))
                )
                .ToList();

            if (imageFiles.Count > 0)
            {
                string baseFolder = Path.GetDirectoryName(imageFiles[0]) ?? string.Empty;
                var images = imageFiles.Select(p => new ImageInfo { ImagePath = p }).ToList();
                string? defaultTxtName = isCreateMode ? FileSystemHelper.GenerateUniqueFileName(baseFolder, "New_Translation", ".txt") : null;
                var context = new ProjectContext(baseFolder, defaultTxtName, null);

                LoadProjectData(
                    context,
                    images,
                    isCreateMode ? "正在为一组图片创建翻译" : "正在预览选定图片"
                );
                if (isCreateMode)
                    DoSave(context.TxtPath, ExportMode.Current, updateContext: true);
                return;
            }

            // --- 2. 翻译文档 (.txt) ---
            if (ext.Equals(".txt", StringComparison.OrdinalIgnoreCase) && File.Exists(firstPath))
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

            // --- 3. 压缩包 (.zip, .rar, .7z) ---
            if (ProjectService.ZipExtensions.Contains(ext) && File.Exists(firstPath))
            {
                try
                {
                    var images = ProjectService.ScanZip(firstPath);
                    string baseFolder = Path.GetDirectoryName(firstPath) ?? string.Empty;
                    string zipName = Path.GetFileName(firstPath);
                    string? txtName = isCreateMode
                        ? Path.GetFileNameWithoutExtension(zipName) + "_翻译.txt"
                        : null;
                    var context = new ProjectContext(baseFolder, txtName, zipName);

                    LoadProjectData(
                        context,
                        images,
                        isCreateMode
                            ? $"正在为压缩包【{zipName}】创建翻译"
                            : $"正在预览压缩包：{zipName}"
                    );
                    if (isCreateMode)
                        DoSave(context.TxtPath, ExportMode.Current, updateContext: true);
                }
                catch (Exception ex)
                {
                    MainMessageQueue.Enqueue($"读取压缩包失败: {ex.Message}");
                }
                return;
            }

            // --- 4. 文件夹 ---
            if (Directory.Exists(firstPath))
            {
                var images = ProjectService.ScanFolder(firstPath);
                string? txtName = isCreateMode ? FileSystemHelper.GenerateUniqueFileName(firstPath, "新建翻译", ".txt") : null;
                var context = new ProjectContext(firstPath, txtName, null);

                LoadProjectData(
                    context,
                    images,
                    isCreateMode
                        ? $"正在为文件夹【{firstPath}】创建翻译"
                        : $"正在预览文件夹：{firstPath}"
                );
                if (isCreateMode)
                    DoSave(context.TxtPath, ExportMode.Current, updateContext: true);
            }
        }

        /// <summary>
        /// 执行保存：将当前图片列表导出为翻译文本并写入文件
        /// </summary>
        /// <param name="targetPath">保存路径，为 null 时弹出另存为对话框</param>
        /// <param name="mode">导出模式：Current/Original/Diff</param>
        /// <param name="updateContext">是否更新项目上下文（仅正常保存时为 true）</param>
        private void DoSave(string? targetPath, ExportMode mode = ExportMode.Current, bool updateContext = false)
        {
            if (ImageList.Count == 0)
                return;

            // 无目标路径时弹出保存对话框
            if (string.IsNullOrEmpty(targetPath))
            {
                string defaultName = CurrentProject.IsArchiveMode
                    ? Path.GetFileNameWithoutExtension(CurrentProject.ZipName) + "_翻译.txt"
                    : "新建翻译.txt";

                // 根据导出模式调整默认文件名
                if (mode == ExportMode.Original)
                    defaultName = Path.GetFileNameWithoutExtension(defaultName) + "_原翻译.txt";
                else if (mode == ExportMode.Diff)
                    defaultName = Path.GetFileNameWithoutExtension(defaultName) + "_修改文档.txt";

                targetPath = DialogService.SaveFile("文本文件|*.txt", defaultName);
                if (string.IsNullOrEmpty(targetPath))
                    return;
            }

            try
            {
                string outputText = Modules.LabelsToText(
                    ImageList,
                    CurrentProject.ZipName,
                    mode
                );
                File.WriteAllText(targetPath, outputText);

                // 只有在 updateContext 为 true 时才更新项目上下文
                if (updateContext)
                {
                    CurrentProject = new ProjectContext(
                        Path.GetDirectoryName(targetPath)!,
                        Path.GetFileName(targetPath),
                        CurrentProject.ZipName
                    );
                }

                string modeText = mode switch
                {
                    ExportMode.Original => "原翻译",
                    ExportMode.Diff => "修改文档",
                    _ => "翻译"
                };
                MainMessageQueue.Enqueue($"已保存{modeText}到 {Path.GetFileName(targetPath)}");
            }
            catch (Exception ex)
            {
                MainMessageQueue.Enqueue($"保存失败: {ex.Message}");
            }
        }

        /// <summary>导出原翻译</summary>
        [RelayCommand(CanExecute = nameof(CanSave))]
        private void ExportOriginal()
        {
            DoSave(null, ExportMode.Original, updateContext: false);
        }

        /// <summary>导出现翻译</summary>
        [RelayCommand(CanExecute = nameof(CanSave))]
        private void ExportCurrent()
        {
            DoSave(null, ExportMode.Current, updateContext: false);
        }

        /// <summary>导出修改文档</summary>
        [RelayCommand(CanExecute = nameof(CanSave))]
        private void ExportDiff()
        {
            DoSave(null, ExportMode.Diff, updateContext: false);
        }
        #endregion
    }
    #endregion
}
