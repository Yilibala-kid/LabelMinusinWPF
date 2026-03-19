using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using LabelMinusinWPF.Common;
using LabelMinusinWPF.SelfControls;
using ExportMode = LabelMinusinWPF.Common.LabelPlusParser.ExportMode;
using Constants = LabelMinusinWPF.Common.Constants;
using AppMode = LabelMinusinWPF.Common.Constants.AppMode;

namespace LabelMinusinWPF
{

    public record GroupItem(string Name, SolidColorBrush Brush);

// 主视图模型

    public partial class MainVM : ObservableObject
    {
        #region 初始化

        [ObservableProperty]
        private ISnackbarMessageQueue _mainMessageQueue;
        public MainVM()
        {
            MainMessageQueue = new SnackbarMessageQueue(TimeSpan.FromSeconds(2));
            ImageList.ListChanged += OnImageListChanged;
        }

        private readonly HashSet<OneImage> _boundImages = [];


        private bool _suppressGroupNotify;


        private void RunWithoutGroupNotify(Action action)
        {
            _suppressGroupNotify = true;
            try { action(); }
            finally { _suppressGroupNotify = false; }
        }

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
                    foreach (var img in _boundImages.Except(ImageList).ToList())
                        UnhookImage(img);
                    break;
                case ListChangedType.Reset:
                    UnhookAllImages();
                    break;
            }
            NotifyGroupsChanged();
        }

        private void HookImage(OneImage img)
        {
            img.Labels.ListChanged -= OnLabelsChanged;
            img.Labels.ListChanged += OnLabelsChanged;
            _boundImages.Add(img);
        }

        private void UnhookImage(OneImage img)
        {
            img.Labels.ListChanged -= OnLabelsChanged;
            _boundImages.Remove(img);
        }

        private void UnhookAllImages()
        {
            foreach (var img in _boundImages)
                img.Labels.ListChanged -= OnLabelsChanged;
            _boundImages.Clear();
        }

        private void OnLabelsChanged(object? sender, ListChangedEventArgs e) =>
            NotifyGroupsChanged();
        #endregion
        // --- 项目上下文 ---
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(OpenCurrentFolderCommand))]
        [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
        [NotifyCanExecuteChangedFor(nameof(AdjustImageSetCommand))]
        [NotifyCanExecuteChangedFor(nameof(LinkZipCommand))]
        [NotifyCanExecuteChangedFor(nameof(ExportTxtCommand))]
        [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
        private ProjectHelper.ProjectContext _currentProject = ProjectHelper.ProjectContext.Empty;


        public BindingList<OneImage> ImageList { get; } = [];


        [ObservableProperty]
        private OneImage? _selectedImage;



        #region 组别汇总
        // 初始化时就带上默认值
        private List<GroupItem> _groups =
        [
            new GroupItem(Constants.Groups.Default, GroupBrushes.First()),
            new GroupItem(Constants.Groups.Outside, GroupBrushes.ElementAt(1)),
        ];
        public IReadOnlyList<GroupItem> AllGroups => _groups;

        private void RebuildGroupsCache()
        {
            // 定义必须存在的组
            string[] defaultGroups = Constants.Groups.Required;

            _groups = ImageList
                .SelectMany(img => img.Labels.Select(l => l.Group)) // 提取所有已存在的组名
                .Concat(defaultGroups)                             // 强制注入默认组别
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Distinct()                                        // 去重
                .OrderBy(g => g == Constants.Groups.Default ? 0 : (g == Constants.Groups.Outside ? 1 : 2))
                .ThenBy(g => g)
                .Select((g, i) => new GroupItem(g, GroupBrushes[i % GroupBrushes.Length]))
                .ToList();

            // 同步每个标签的组别颜色（逻辑保持不变）
            var brushMap = _groups.ToDictionary(g => g.Name, g => g.Brush);
            foreach (var img in ImageList)
            {
                foreach (var label in img.Labels)
                {
                    if (brushMap.TryGetValue(label.Group, out var brush))
                        label.GroupBrush = brush;
                }
            }
        }

        // 直接复用 Constants.Groups.Brushes 作为组别颜色
        private static SolidColorBrush[] GroupBrushes => Constants.Groups.Brushes;

        [ObservableProperty]
        private string? _selectedGroupName = Constants.Groups.Default;

        partial void OnSelectedGroupNameChanged(string? value)
        {
            if (_suppressGroupNotify || string.IsNullOrEmpty(value) || SelectedImage == null)
                return;

            SelectedImage.ActiveGroup = value;
            if (SelectedImage.SelectedLabel is { } label && label.Group != value)
            {
                // 修改 label.Group 会触发 OnLabelsChanged → NotifyGroupsChanged，
                // 用 _suppressGroupNotify 阻断级联，之后手动刷新一次
                RunWithoutGroupNotify(() => label.Group = value);
                NotifyGroupsChanged();
            }
        }

        private void NotifyGroupsChanged()
        {
            if (_suppressGroupNotify)
                return;

            var savedGroup = SelectedGroupName;

            // 重建缓存期间抑制 ListBox 选中项变化引起的回写
            RunWithoutGroupNotify(() =>
            {
                RebuildGroupsCache();
                OnPropertyChanged(nameof(AllGroups));
            });

            // 恢复选中状态
            var names = _groups.ConvertAll(g => g.Name);
            SelectedGroupName = names.Contains(savedGroup!)
                ? savedGroup!
                : names.FirstOrDefault() ?? Constants.Groups.Default;
        }
        #endregion

        #region 菜单栏：显示控制
        [ObservableProperty]
        private bool _isPictureIndexVisible = true;

        [ObservableProperty]
        private bool _isPictureTextVisible = true;
        #endregion

        #region OCR识别模式

        // 使用常量配置
        public static Dictionary<string, string> OcrWebsites => Constants.OcrWebsites.Websites;

        [ObservableProperty]
        private string _selectedOcrWebsite = Constants.OcrWebsites.DefaultWebsite;

        #endregion

        #region 底栏：图片切换
        [RelayCommand(CanExecute = nameof(CanGoToPrevious))]
        public void PreviousImage()
        {
            var idx = ImageList.IndexOf(SelectedImage!);
            if (idx > 0)
                SelectedImage = ImageList[idx - 1];
        }

        private bool CanGoToPrevious() => SelectedImage != null && ImageList.IndexOf(SelectedImage) > 0;

        [RelayCommand(CanExecute = nameof(CanGoToNext))]
        public void NextImage()
        {
            var idx = ImageList.IndexOf(SelectedImage!);
            if (idx < ImageList.Count - 1)
                SelectedImage = ImageList[idx + 1];
        }

        private bool CanGoToNext() => SelectedImage != null && ImageList.IndexOf(SelectedImage) < ImageList.Count - 1;

        private OneImage? _boundImg;


        partial void OnSelectedImageChanged(OneImage? value)
        {
            if (_boundImg != null)
                _boundImg.PropertyChanged -= OnLabelChanged;
            _boundImg = value;
            if (value != null)
            {
                value.PropertyChanged += OnLabelChanged;
                if (value.SelectedLabel != null)
                    SelectedGroupName = value.SelectedLabel.Group;
            }

            PreviousImageCommand.NotifyCanExecuteChanged();
            NextImageCommand.NotifyCanExecuteChanged();
        }


        private void OnLabelChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (
                e.PropertyName == nameof(OneImage.SelectedLabel)
                && sender is OneImage img
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
            var labels = SelectedImage.ActiveLabels;
            if (labels.Count == 0) return;

            // 没选中则选最后一个
            if (SelectedImage.SelectedLabel == null)
            {
                SelectedImage.SelectedLabel = labels[^1];
                return;
            }

            var idx = labels.IndexOf(SelectedImage.SelectedLabel);
            if (idx > 0)
                SelectedImage.SelectedLabel = labels[idx - 1];
        }

        private bool CanGoToPreviousLabel()
        {
            if (SelectedImage == null) return false;
            var labels = SelectedImage.ActiveLabels;
            if (labels.Count == 0) return false;
            if (SelectedImage.SelectedLabel == null) return true;
            return labels.IndexOf(SelectedImage.SelectedLabel) > 0;
        }

        [RelayCommand(CanExecute = nameof(CanGoToNextLabel))]
        public void NextLabel()
        {
            if (SelectedImage == null) return;
            var labels = SelectedImage.ActiveLabels;
            if (labels.Count == 0) return;

            // 没选中则选第一个
            if (SelectedImage.SelectedLabel == null)
            {
                SelectedImage.SelectedLabel = labels.First();
                return;
            }

            var idx = labels.IndexOf(SelectedImage.SelectedLabel);
            if (idx >= 0 && idx < labels.Count - 1)
                SelectedImage.SelectedLabel = labels[idx + 1];
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

    public partial class MainVM
    {
        [ObservableProperty]
        private AppMode _currentMode = AppMode.LabelDo;


        partial void OnCurrentModeChanged(AppMode value)
        {
            var isVisible = value == AppMode.LabelDo;
            IsPictureIndexVisible = isVisible;
            IsPictureTextVisible = isVisible;
        }
    }


    #endregion

    #region 文件处理
    public partial class MainVM
    {
        #region 菜单命令：新建 / 打开 / 保存


        [RelayCommand]
        private void NewFolder()
        {
            var dialog = new OpenFolderDialog { Title = "选择要新建翻译的文件夹" };
            string? folder = dialog.ShowDialog() == true ? dialog.FolderName : null;
            if (!string.IsNullOrEmpty(folder))
                OpenResourceByPath([folder], true);
        }


        [RelayCommand]
        private void NewZip()
        {
            var dialog = new OpenFileDialog { Filter = Constants.FileFilters.ArchiveFiles, Title = "选择要新建翻译的压缩包" };
            string? zipPath = dialog.ShowDialog() == true ? dialog.FileName : null;
            if (!string.IsNullOrEmpty(zipPath))
                OpenResourceByPath([zipPath], true);
        }


        [RelayCommand]
        private void OpenTxt()
        {
            var dialog = new OpenFileDialog { Filter = Constants.FileFilters.TextFiles, Title = "打开已有翻译" };
            string? txtPath = dialog.ShowDialog() == true ? dialog.FileName : null;
            if (!string.IsNullOrEmpty(txtPath))
                OpenResourceByPath([txtPath], false);
        }


        [RelayCommand]
        private void OpenFolder()
        {
            var dialog = new OpenFolderDialog { Title = "选择要预览的文件夹" };
            string? folder = dialog.ShowDialog() == true ? dialog.FolderName : null;
            if (!string.IsNullOrEmpty(folder))
                OpenResourceByPath([folder], false);
        }


        [RelayCommand]
        private void OpenResource()
        {
            var dialog = new OpenFileDialog { Filter = Constants.FileFilters.ImageAndArchive, Title = "选择要预览的图片（多张）或压缩包（单个）", Multiselect = true };
            string[]? filepaths = dialog.ShowDialog() == true ? dialog.FileNames : null;
            if (filepaths is { Length: > 0 })
                OpenResourceByPath(filepaths, false);
        }


        [RelayCommand]
        private void OpenImages()
        {
            var dialog = new OpenFileDialog { Filter = Constants.FileFilters.ImageFiles, Title = "选择要预览的图片（多张）", Multiselect = true };
            string[]? filepaths = dialog.ShowDialog() == true ? dialog.FileNames : null;
            if (filepaths is { Length: > 0 })
                OpenResourceByPath(filepaths, false);
        }


        [RelayCommand]
        private void OpenZip()
        {
            var dialog = new OpenFileDialog { Filter = Constants.FileFilters.ArchiveFiles, Title = "选择要预览的压缩包" };
            string? zipPath = dialog.ShowDialog() == true ? dialog.FileName : null;
            if (!string.IsNullOrEmpty(zipPath))
                OpenResourceByPath([zipPath], false);
        }

        private bool CanSave() => CurrentProject != ProjectHelper.ProjectContext.Empty;


        [RelayCommand(CanExecute = nameof(CanSave))]
        private void Save(string? mode)
        {
            bool isSaveAs = mode is "As";
            SaveCore(isSaveAs ? null : CurrentProject.TxtPath, ExportMode.Current, updateContext: true);
        }


        [RelayCommand(CanExecute = nameof(CanSave))]
        private void OpenCurrentFolder()
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
                MainMessageQueue.Enqueue(Constants.Msg.NoFolderPath);
            }
        }


        [RelayCommand(CanExecute = nameof(CanSave))]
        private void Clear()
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
                    Save(null);
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    return;
                }
            }

            // 清空所有数据
            RunWithoutGroupNotify(() =>
            {
                ImageList.Clear();
                SelectedImage = null;
                CurrentProject = ProjectHelper.ProjectContext.Empty;
            });
            NotifyGroupsChanged();

            MainMessageQueue.Enqueue(Constants.Msg.WorkspaceCleared);
        }


        public bool HasUnsavedChanges()
        {
            return ImageList.Any(img => img.Labels.Any(l => l.IsModified));
        }


        private void ReloadImages(List<OneImage> newImages, bool updateImagePath = false)
        {
            var existingData = ImageList.ToDictionary(img => img.ImageName, img => img);

            RunWithoutGroupNotify(() =>
            {
                ImageList.Clear();
                foreach (var img in newImages)
                {
                    if (existingData.TryGetValue(img.ImageName, out var existingImg))
                    {
                        if (updateImagePath)
                            existingImg.ImagePath = img.ImagePath;
                        ImageList.Add(existingImg);
                    }
                    else
                    {
                        ImageList.Add(img);
                    }
                }
            });
            NotifyGroupsChanged();
            SelectedImage = ImageList.FirstOrDefault();
        }


        [RelayCommand(CanExecute = nameof(CanSave))]
        private void AdjustImageSet()
        {
            List<OneImage> availableImages;

            // 根据是否关联压缩包，获取可用图片列表
            if (CurrentProject.IsArchiveMode && File.Exists(CurrentProject.ZipPath))
            {
                availableImages = ProjectHelper.ScanZip(CurrentProject.ZipPath);
            }
            else if (Directory.Exists(CurrentProject.BaseFolderPath))
            {
                availableImages = ProjectHelper.ScanFolder(CurrentProject.BaseFolderPath);
            }
            else
            {
                MainMessageQueue.Enqueue(Constants.Msg.NoImageSource);
                return;
            }

            // 创建选择对话框
            var dialog = new ImageSelectDialog(availableImages, ImageList.ToList());
            if (dialog.ShowDialog() == true)
            {
                ReloadImages(dialog.SelectedImages);
                MainMessageQueue.Enqueue(string.Format(Constants.Msg.ImageSetUpdated, ImageList.Count));
            }
        }


        [RelayCommand(CanExecute = nameof(CanSave))]
        private void LinkZip()
        {
            if (!Directory.Exists(CurrentProject.BaseFolderPath))
            {
                MainMessageQueue.Enqueue(Constants.Msg.NoValidFolderPathForZip);
                return;
            }

            // 获取当前文件夹中的所有压缩包
            var zipFiles = Directory.GetFiles(CurrentProject.BaseFolderPath)
                .Where(f => ProjectHelper.ZipExtensions.Contains(Path.GetExtension(f)))
                .Select(f => Path.GetFileName(f))
                .ToList();

            if (zipFiles.Count == 0)
            {
                MainMessageQueue.Enqueue(Constants.Msg.NoZipFilesFound);
                return;
            }

            // 创建选择对话框
            var dialog = new ZipDialog(zipFiles, CurrentProject.ZipName);
            if (dialog.ShowDialog() == true)
            {
                string? selectedZip = dialog.SelectedZip;

                // 更新项目上下文
                CurrentProject = new ProjectHelper.ProjectContext(
                    CurrentProject.BaseFolderPath,
                    CurrentProject.TxtName,
                    selectedZip
                );

                // 重新加载图片列表
                if (!string.IsNullOrEmpty(selectedZip))
                {
                    try
                    {
                        var zipImages = ProjectHelper.ScanZip(CurrentProject.ZipPath);
                        ReloadImages(zipImages, updateImagePath: true);
                        MainMessageQueue.Enqueue(string.Format(Constants.Msg.ZipLinked, selectedZip));
                    }
                    catch (Exception ex)
                    {
                        MainMessageQueue.Enqueue(string.Format(Constants.Msg.ZipLoadFailed, ex.Message));
                    }
                }
                else
                {
                    // 取消关联，切换到文件夹模式
                    var folderImages = ProjectHelper.ScanFolder(CurrentProject.BaseFolderPath);
                    ReloadImages(folderImages, updateImagePath: true);
                    MainMessageQueue.Enqueue(Constants.Msg.ZipLinkCanceled);
                }
            }
        }
        #endregion

        #region 核心函数：加载与保存


// 统一数据加载入口

        private void LoadProject(
            ProjectHelper.ProjectContext context,
            List<OneImage> images,
            string successMsg
        )
        {
            // 确保在 UI 线程执行（支持从后台线程调用）
            if (!Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.Invoke(() =>
                    LoadProject(context, images, successMsg)
                );
                return;
            }

            if (images.Count == 0)
            {
                MainMessageQueue.Enqueue(Constants.Msg.NoImages);
                return;
            }

            CurrentProject = context;

            // 批量操作：抑制每次 Add 触发的 NotifyGroupsChanged，最后统一刷新一次
            RunWithoutGroupNotify(() =>
            {
                ImageList.Clear();
                foreach (var img in images)
                    ImageList.Add(img);
            });
            NotifyGroupsChanged();

            SelectedImage = ImageList.FirstOrDefault();
            MainMessageQueue.Enqueue(string.Format(Constants.Msg.LoadSuccess, successMsg, images.Count));
        }


// 统一资源入口
// param paths: 文件或文件夹路径数组
// param isCreateMode: 新建/预览模式
        public void OpenResourceByPath(string[]? paths, bool isCreateMode)
        {
            if (paths is not { Length: > 0 })
                return;

            string firstPath = paths.First();
            string ext = Path.GetExtension(firstPath);

            // --- 1. 图片组：只要输入中包含支持的图片，就直接加载这些图片 ---
            var imageFiles = paths
                .Where(p =>
                    File.Exists(p) && ProjectHelper.ImageExtensions.Contains(Path.GetExtension(p))
                )
                .ToList();

            if (imageFiles.Count > 0)
            {
                string baseFolder = Path.GetDirectoryName(imageFiles.First()) ?? string.Empty;
                var images = imageFiles.Select(p => new OneImage { ImagePath = p }).ToList();
                string? defaultTxtName = isCreateMode ? ProjectHelper.GenerateUniqueFileName(baseFolder, "New_Translation", ".txt") : null;
                var context = new ProjectHelper.ProjectContext(baseFolder, defaultTxtName, null);

                LoadProject(
                    context,
                    images,
                    isCreateMode ? "正在为一组图片创建翻译" : "正在预览选定图片"
                );
                if (isCreateMode)
                    SaveCore(context.TxtPath, ExportMode.Current, updateContext: true);
                return;
            }

            // --- 2. 翻译文档 (.txt) ---
            if (ext.Equals(".txt", StringComparison.OrdinalIgnoreCase) && File.Exists(firstPath))
            {
                try
                {
                    var (context, images) = ProjectHelper.LoadProjectFromTxt(firstPath);
                    LoadProject(context, images, $"已加载翻译：{context.TxtName}");
                }
                catch (Exception ex)
                {
                    MainMessageQueue.Enqueue(string.Format(Constants.Msg.ParseTxtFailed, ex.Message));
                }
                return;
            }

            // --- 3. 压缩包 (.zip, .rar, .7z) ---
            if (ProjectHelper.ZipExtensions.Contains(ext) && File.Exists(firstPath))
            {
                try
                {
                    var images = ProjectHelper.ScanZip(firstPath);
                    string baseFolder = Path.GetDirectoryName(firstPath) ?? string.Empty;
                    string zipName = Path.GetFileName(firstPath);
                    string? txtName = isCreateMode
                        ? Path.GetFileNameWithoutExtension(zipName) + "_翻译.txt"
                        : null;
                    var context = new ProjectHelper.ProjectContext(baseFolder, txtName, zipName);

                    LoadProject(
                        context,
                        images,
                        isCreateMode
                            ? $"正在为压缩包【{zipName}】创建翻译"
                            : $"正在预览压缩包：{zipName}"
                    );
                    if (isCreateMode)
                        SaveCore(context.TxtPath, ExportMode.Current, updateContext: true);
                }
                catch (Exception ex)
                {
                    MainMessageQueue.Enqueue(string.Format(Constants.Msg.ReadZipErr, ex.Message));
                }
                return;
            }

            // --- 4. 文件夹 ---
            if (Directory.Exists(firstPath))
            {
                var images = ProjectHelper.ScanFolder(firstPath);
                string? txtName = isCreateMode ? ProjectHelper.GenerateUniqueFileName(firstPath, "新建翻译", ".txt") : null;
                var context = new ProjectHelper.ProjectContext(firstPath, txtName, null);

                LoadProject(
                    context,
                    images,
                    isCreateMode
                        ? $"正在为文件夹【{firstPath}】创建翻译"
                        : $"正在预览文件夹：{firstPath}"
                );
                if (isCreateMode)
                    SaveCore(context.TxtPath, ExportMode.Current, updateContext: true);
            }
        }


// 执行保存
// param targetPath: 保存路径
// param mode: 导出模式
// param updateContext: 是否更新项目上下文
        private void SaveCore(string? targetPath, ExportMode mode = ExportMode.Current, bool updateContext = false)
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

                var saveDialog = new SaveFileDialog { Filter = Constants.FileFilters.TextFiles, FileName = defaultName };
                targetPath = saveDialog.ShowDialog() == true ? saveDialog.FileName : null;
                if (string.IsNullOrEmpty(targetPath))
                    return;
            }

            try
            {
                string outputText = LabelPlusParser.LabelsToText(
                    ImageList,
                    CurrentProject.ZipName,
                    mode
                );
                File.WriteAllText(targetPath, outputText);

                // 只有在 updateContext 为 true 时才更新项目上下文
                if (updateContext)
                {
                    CurrentProject = new ProjectHelper.ProjectContext(
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
                MainMessageQueue.Enqueue(string.Format(Constants.Msg.Saved, modeText, Path.GetFileName(targetPath)));
            }
            catch (Exception ex)
            {
                MainMessageQueue.Enqueue(string.Format(Constants.Msg.SaveErr, ex.Message));
            }
        }


        [RelayCommand(CanExecute = nameof(CanSave))]
        private void ExportTxt(string? modeStr)
        {
            if (!Enum.TryParse<ExportMode>(modeStr, out var mode)) return;
            SaveCore(null, mode, updateContext: false);
        }
        #endregion
    }
    #endregion
}
