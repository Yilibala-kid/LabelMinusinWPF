using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelMinusinWPF.Common;
using LabelMinusinWPF.SelfControls;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using Constants = LabelMinusinWPF.Common.Constants;
using ExportMode = LabelMinusinWPF.Common.LabelPlusParser.ExportMode;
using WorkSpace = LabelMinusinWPF.Common.ProjectManager.WorkSpace;

namespace LabelMinusinWPF
{
    // 主视图模型

    public partial class MainVM : ObservableObject
    {
        #region 初始化

        [ObservableProperty]
        private ISnackbarMessageQueue _mainMessageQueue;

        public MainVM()
        {
            MainMessageQueue = new SnackbarMessageQueue(TimeSpan.FromSeconds(2));
            SyncGroupColors();
        }



        #endregion

        // --- 项目上下文 ---
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(OpenCurrentFolderCommand))]
        [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
        [NotifyCanExecuteChangedFor(nameof(AdjustImageSetCommand))]
        [NotifyCanExecuteChangedFor(nameof(LinkZipCommand))]
        [NotifyCanExecuteChangedFor(nameof(ExportTxtCommand))]
        [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
        private WorkSpace _workSpace = WorkSpace.Empty;

        public BindingList<OneImage> ImageList { get; } = [];

        [ObservableProperty]
        private OneImage? _selectedImage;// 当前图片
        partial void OnSelectedImageChanged(OneImage? value)// 图片切换逻辑
        {
            NotifySelectedGroupChanged();

            PreviousImageCommand.NotifyCanExecuteChanged();
            NextImageCommand.NotifyCanExecuteChanged();
        }

        partial void OnSelectedImageChanging(OneImage? oldValue, OneImage? newValue)
        {
            if (oldValue != null)
                oldValue.PropertyChanged -= OnSelectedImagePropertyChanged;
            if (newValue != null)
                newValue.PropertyChanged += OnSelectedImagePropertyChanged;
        }

        private void OnSelectedImagePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(OneImage.SelectedLabel) || sender is not OneImage image)
                return;

            if (image.SelectedLabel is { } label)
                image.ActiveGroup = GroupColorManager.NormalizeGroupName(label.Group);

            NotifySelectedGroupChanged();
        }

        #region 组别

        public ObservableCollection<string> AllGroups { get; } =
            new ObservableCollection<string>([.. Constants.Groups.Required]);

        public string SelectedGroupName
        {
            get => GroupColorManager.NormalizeGroupName(
                SelectedImage?.SelectedLabel?.Group
                ?? SelectedImage?.ActiveGroup
                ?? Constants.Groups.Default);
            set
            {
                if (SelectedImage == null)
                    return;

                string normalized = GroupColorManager.NormalizeGroupName(value);
                bool changed = false;

                if (EnsureGroupExists(normalized))
                {
                    SyncGroupColors();
                    changed = true;
                }

                if (!string.Equals(SelectedImage.ActiveGroup, normalized, StringComparison.Ordinal))
                {
                    SelectedImage.ActiveGroup = normalized;
                    changed = true;
                }

                if (SelectedImage.SelectedLabel is { } label && label.Group != normalized)
                {
                    label.Group = normalized;
                    changed = true;
                }

                if (changed)
                    OnPropertyChanged(nameof(SelectedGroupName));
            }
        }
        [ObservableProperty]
        private bool _isAddingGroup;
        [ObservableProperty]
        private string? _newGroupName;

        /// <summary>从标签重建组别列表；可选择保留当前未被标签使用的自定义组别</summary>
        private void SyncGroupsFromLabels(bool preserveExistingCustomGroups = false)
        {
            IEnumerable<string> groups = ImageList
                .SelectMany(img => img.Labels)
                .Where(lbl => !lbl.IsDeleted)
                .Select(lbl => lbl.Group);

            if (preserveExistingCustomGroups)
                groups = AllGroups
                    .Where(group => !Constants.Groups.Required.Contains(group))
                    .Concat(groups);

            ResetGroups(groups);

            SyncGroupColors();
        }

        private static IEnumerable<string> GetCustomGroups(IEnumerable<string> groupNames) =>
            groupNames
                .Select(GroupColorManager.NormalizeGroupName)
                .Where(group => !Constants.Groups.Required.Contains(group))
                .Distinct();

        private void NotifySelectedGroupChanged()
        {
            if (EnsureGroupExists(SelectedGroupName))
                SyncGroupColors();

            OnPropertyChanged(nameof(SelectedGroupName));
        }

        private void ResetGroups(IEnumerable<string>? customGroups = null)
        {
            AllGroups.Clear();
            foreach (string group in Constants.Groups.Required
                .Concat(GetCustomGroups(customGroups ?? Enumerable.Empty<string>())))
                AllGroups.Add(group);
        }

        private bool EnsureGroupExists(string groupName)
        {
            string normalized = GroupColorManager.NormalizeGroupName(groupName);
            if (AllGroups.Contains(normalized))
                return false;

            AllGroups.Add(normalized);
            return true;
        }

        private void SyncGroupColors()
        {
            GroupColorManager.SetGroupOrder(AllGroups);

            foreach (OneLabel label in ImageList.SelectMany(img => img.Labels))
                label.RefreshGroupBrush();
        }

        [RelayCommand]
        private void AddGroup()
        {
            string name = NewGroupName?.Trim() ?? "";
            NewGroupName = "";
            IsAddingGroup = false;
            if (string.IsNullOrWhiteSpace(name)) return;
            if (AllGroups.Contains(name)) { MainMessageQueue.Enqueue("组别已存在"); return; }
            if (EnsureGroupExists(name))
                SyncGroupColors();
            SelectedGroupName = GroupColorManager.NormalizeGroupName(name);
        }

        [RelayCommand]
        private void DeleteGroup()
        {
            string groupName = GroupColorManager.NormalizeGroupName(SelectedGroupName);

            if (Constants.Groups.Required.Contains(groupName))
                { MainMessageQueue.Enqueue("默认组别不可删除"); return; }
            if (ImageList.Any(img => img.Labels.Any(l => !l.IsDeleted && l.Group == groupName)))
                { MainMessageQueue.Enqueue("有标签正在使用该组别"); return; }
            AllGroups.Remove(groupName);
            SyncGroupColors();
            if (SelectedImage != null)
                SelectedGroupName = Constants.Groups.Default;
            else
                OnPropertyChanged(nameof(SelectedGroupName));
        }

        #endregion

        #region 图片切换
        [RelayCommand(CanExecute = nameof(CanGoToPrevious))]
        public void PreviousImage()
        {
            var idx = ImageList.IndexOf(SelectedImage!);
            if (idx > 0)
                SelectedImage = ImageList[idx - 1];
        }

        private bool CanGoToPrevious() =>
            SelectedImage != null && ImageList.IndexOf(SelectedImage) > 0;

        [RelayCommand(CanExecute = nameof(CanGoToNext))]
        public void NextImage()
        {
            var idx = ImageList.IndexOf(SelectedImage!);
            if (idx < ImageList.Count - 1)
                SelectedImage = ImageList[idx + 1];
        }

        private bool CanGoToNext() =>
            SelectedImage != null && ImageList.IndexOf(SelectedImage) < ImageList.Count - 1;
        #endregion

        #region 标签切换
        [RelayCommand(CanExecute = nameof(CanGoToPreviousLabel))]
        public void PreviousLabel()
        {
            if (SelectedImage == null)
                return;
            var labels = SelectedImage.ActiveLabels;
            if (labels.Count == 0)
                return;

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
            if (SelectedImage == null)
                return false;
            var labels = SelectedImage.ActiveLabels;
            if (labels.Count == 0)
                return false;
            if (SelectedImage.SelectedLabel == null)
                return true;
            return labels.IndexOf(SelectedImage.SelectedLabel) > 0;
        }

        [RelayCommand(CanExecute = nameof(CanGoToNextLabel))]
        public void NextLabel()
        {
            if (SelectedImage == null)
                return;
            var labels = SelectedImage.ActiveLabels;
            if (labels.Count == 0)
                return;

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
            if (SelectedImage == null)
                return false;
            var activeLabels = SelectedImage.ActiveLabels;
            if (activeLabels.Count == 0)
                return false;
            if (SelectedImage.SelectedLabel == null)
                return true;

            int currentIndex = activeLabels.IndexOf(SelectedImage.SelectedLabel);
            return currentIndex >= 0 && currentIndex < activeLabels.Count - 1;
        }
        #endregion
    }

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
            var dialog = new OpenFileDialog
            {
                Filter = Constants.FileFilters.ArchiveFiles,
                Title = "选择要新建翻译的压缩包",
            };
            string? zipPath = dialog.ShowDialog() == true ? dialog.FileName : null;
            if (!string.IsNullOrEmpty(zipPath))
                OpenResourceByPath([zipPath], true);
        }

        [RelayCommand]
        private void OpenTxt()
        {
            var dialog = new OpenFileDialog
            {
                Filter = Constants.FileFilters.TextFiles,
                Title = "打开已有翻译",
            };
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
            var dialog = new OpenFileDialog
            {
                Filter = Constants.FileFilters.ImageAndArchive,
                Title = "选择要预览的图片（多张）或压缩包（单个）",
                Multiselect = true,
            };
            string[]? filepaths = dialog.ShowDialog() == true ? dialog.FileNames : null;
            if (filepaths is { Length: > 0 })
                OpenResourceByPath(filepaths, false);
        }

        [RelayCommand]
        private void OpenImages()
        {
            var dialog = new OpenFileDialog
            {
                Filter = Constants.FileFilters.ImageFiles,
                Title = "选择要预览的图片（多张）",
                Multiselect = true,
            };
            string[]? filepaths = dialog.ShowDialog() == true ? dialog.FileNames : null;
            if (filepaths is { Length: > 0 })
                OpenResourceByPath(filepaths, false);
        }

        [RelayCommand]
        private void OpenZip()
        {
            var dialog = new OpenFileDialog
            {
                Filter = Constants.FileFilters.ArchiveFiles,
                Title = "选择要预览的压缩包",
            };
            string? zipPath = dialog.ShowDialog() == true ? dialog.FileName : null;
            if (!string.IsNullOrEmpty(zipPath))
                OpenResourceByPath([zipPath], false);
        }

        private bool CanSave() => WorkSpace != WorkSpace.Empty;

        [RelayCommand(CanExecute = nameof(CanSave))]
        private void Save(string? mode)
        {
            bool isSaveAs = mode is "As";
            SaveCore(isSaveAs ? null : WorkSpace.TxtPath, ExportMode.Current, updateContext: true);
        }
        #endregion




        #region 其他文件操作：打开文件夹、清空、调整图集、关联压缩包
        [RelayCommand(CanExecute = nameof(CanSave))]
        private void OpenCurrentFolder()
        {
            string pathToOpen = WorkSpace.BaseFolderPath;
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
            // 1. 检查是否有未保存的修改
            if (HasUnsavedChanges())
            {
                var result = MessageBox.Show(
                    "当前翻译有未保存的修改，是否保存？", "提示",
                    MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes) Save(null);
                else if (result == MessageBoxResult.Cancel) return;
            }

            // 2. 挂起 UI 刷新，清空数据
            ImageList.RaiseListChangedEvents = false;
            ImageList.Clear();
            ImageList.RaiseListChangedEvents = true;
            ImageList.ResetBindings(); // 通知 UI 列表已清空

            SelectedImage = null;
            WorkSpace = WorkSpace.Empty;

            ResetGroups();
            SyncGroupColors();
            OnPropertyChanged(nameof(SelectedGroupName));
            MainMessageQueue.Enqueue(Constants.Msg.WorkspaceCleared);
        }

        public bool HasUnsavedChanges()
        {
            return ImageList.Any(img => img.Labels.Any(l => l.IsModified));
        }

        private void ReloadImages(List<OneImage> newImages, bool updateImagePath = false)
        {
            var existingData = ImageList.ToDictionary(img => img.ImageName, img => img);

            // 挂起 UI 刷新，进行批量操作
            ImageList.RaiseListChangedEvents = false;
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

            // 批量操作完成，统一刷新界面
            ImageList.RaiseListChangedEvents = true;
            ImageList.ResetBindings();

            SyncGroupsFromLabels(preserveExistingCustomGroups: true);
            SelectedImage = ImageList.FirstOrDefault();
        }

        [RelayCommand(CanExecute = nameof(CanSave))]
        private void AdjustImageSet()
        {
            List<OneImage> availableImages;

            // 根据是否关联压缩包，获取可用图片列表
            if (WorkSpace.IsArchiveMode && File.Exists(WorkSpace.ZipPath))
            {
                availableImages = ProjectManager.ScanZip(WorkSpace.ZipPath);
            }
            else if (Directory.Exists(WorkSpace.BaseFolderPath))
            {
                availableImages = ProjectManager.ScanFolder(WorkSpace.BaseFolderPath);
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
                MainMessageQueue.Enqueue(
                    string.Format(Constants.Msg.ImageSetUpdated, ImageList.Count)
                );
            }
        }

        [RelayCommand(CanExecute = nameof(CanSave))]
        private void LinkZip()
        {
            if (!Directory.Exists(WorkSpace.BaseFolderPath))
            {
                MainMessageQueue.Enqueue(Constants.Msg.NoValidFolderPathForZip);
                return;
            }

            // 获取当前文件夹中的所有压缩包
            var zipFiles = Directory
                .GetFiles(WorkSpace.BaseFolderPath)
                .Where(f => ProjectManager.ZipExtensions.Contains(Path.GetExtension(f)))
                .Select(f => Path.GetFileName(f))
                .ToList();

            if (zipFiles.Count == 0)
            {
                MainMessageQueue.Enqueue(Constants.Msg.NoZipFilesFound);
                return;
            }

            // 创建选择对话框
            var dialog = new ZipDialog(zipFiles, WorkSpace.ZipName);
            if (dialog.ShowDialog() == true)
            {
                string? selectedZip = dialog.SelectedZip;

                // 更新项目上下文
                WorkSpace = new WorkSpace(WorkSpace.BaseFolderPath, WorkSpace.TxtName, selectedZip);

                // 重新加载图片列表
                if (!string.IsNullOrEmpty(selectedZip))
                {
                    try
                    {
                        var zipImages = ProjectManager.ScanZip(WorkSpace.ZipPath);
                        ReloadImages(zipImages, updateImagePath: true);
                        MainMessageQueue.Enqueue(
                            string.Format(Constants.Msg.ZipLinked, selectedZip)
                        );
                    }
                    catch (Exception ex)
                    {
                        MainMessageQueue.Enqueue(
                            string.Format(Constants.Msg.ZipLoadFailed, ex.Message)
                        );
                    }
                }
                else
                {
                    // 取消关联，切换到文件夹模式
                    var folderImages = ProjectManager.ScanFolder(WorkSpace.BaseFolderPath);
                    ReloadImages(folderImages, updateImagePath: true);
                    MainMessageQueue.Enqueue(Constants.Msg.ZipLinkCanceled);
                }
            }
        }
        #endregion




        #region 核心函数：加载与保存


        // 统一数据加载入口
        private void LoadProject(WorkSpace context, List<OneImage> images, string successMsg)
        {
            // 确保在 UI 线程执行（支持从后台线程调用）
            if (!Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.Invoke(() => LoadProject(context, images, successMsg));
                return;
            }

            if (images.Count == 0)
            {
                MainMessageQueue.Enqueue(Constants.Msg.NoImages);
                return;
            }

            WorkSpace = context;

            // 挂起 UI 刷新，进行批量插入
            ImageList.RaiseListChangedEvents = false;
            ImageList.Clear();
            foreach (var img in images)
            {
                ImageList.Add(img);
            }
            ImageList.RaiseListChangedEvents = true;
            ImageList.ResetBindings(); // 瞬间将所有新图片渲染到界面

            SyncGroupsFromLabels();
            SelectedImage = ImageList.FirstOrDefault();
            MainMessageQueue.Enqueue(
                string.Format(Constants.Msg.LoadSuccess, successMsg, images.Count)
            );
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
                    File.Exists(p) && ProjectManager.ImageExtensions.Contains(Path.GetExtension(p))
                )
                .ToList();

            if (imageFiles.Count > 0)
            {
                string baseFolder = Path.GetDirectoryName(imageFiles.First()) ?? string.Empty;
                var images = imageFiles.Select(p => new OneImage { ImagePath = p }).ToList();
                string? defaultTxtName = isCreateMode
                    ? ProjectManager.GenerateUniqueFileName(baseFolder, "New_Translation", ".txt")
                    : null;
                var context = new WorkSpace(baseFolder, defaultTxtName, null);

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
                    var (context, images) = ProjectManager.LoadProjectFromTxt(firstPath);
                    LoadProject(context, images, $"已加载翻译：{context.TxtName}");
                }
                catch (Exception ex)
                {
                    MainMessageQueue.Enqueue(
                        string.Format(Constants.Msg.ParseTxtFailed, ex.Message)
                    );
                }
                return;
            }

            // --- 3. 压缩包 (.zip, .rar, .7z) ---
            if (ProjectManager.ZipExtensions.Contains(ext) && File.Exists(firstPath))
            {
                try
                {
                    var images = ProjectManager.ScanZip(firstPath);
                    string baseFolder = Path.GetDirectoryName(firstPath) ?? string.Empty;
                    string zipName = Path.GetFileName(firstPath);
                    string? txtName = isCreateMode
                        ? Path.GetFileNameWithoutExtension(zipName) + "_翻译.txt"
                        : null;
                    var context = new WorkSpace(baseFolder, txtName, zipName);

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
                var images = ProjectManager.ScanFolder(firstPath);
                string? txtName = isCreateMode
                    ? ProjectManager.GenerateUniqueFileName(firstPath, "新建翻译", ".txt")
                    : null;
                var context = new WorkSpace(firstPath, txtName, null);

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
        private void SaveCore(
            string? targetPath,
            ExportMode mode = ExportMode.Current,
            bool updateContext = false
        )
        {
            if (ImageList.Count == 0)
                return;

            // 无目标路径时弹出保存对话框
            if (string.IsNullOrEmpty(targetPath))
            {
                string defaultName = WorkSpace.IsArchiveMode
                    ? Path.GetFileNameWithoutExtension(WorkSpace.ZipName) + "_翻译.txt"
                    : "新建翻译.txt";

                // 根据导出模式调整默认文件名
                if (mode == ExportMode.Original)
                    defaultName = Path.GetFileNameWithoutExtension(defaultName) + "_原翻译.txt";
                else if (mode == ExportMode.Diff)
                    defaultName = Path.GetFileNameWithoutExtension(defaultName) + "_修改文档.txt";

                var saveDialog = new SaveFileDialog
                {
                    Filter = Constants.FileFilters.TextFiles,
                    FileName = defaultName,
                };
                targetPath = saveDialog.ShowDialog() == true ? saveDialog.FileName : null;
                if (string.IsNullOrEmpty(targetPath))
                    return;
            }

            try
            {
                string outputText = LabelPlusParser.LabelsToText(
                    ImageList,
                    WorkSpace.ZipName,
                    mode
                );
                File.WriteAllText(targetPath, outputText);

                // 只有在 updateContext 为 true 时才更新项目上下文
                if (updateContext)
                {
                    WorkSpace = new WorkSpace(
                        Path.GetDirectoryName(targetPath)!,
                        Path.GetFileName(targetPath),
                        WorkSpace.ZipName
                    );
                }

                string modeText = mode switch
                {
                    ExportMode.Original => "原翻译",
                    ExportMode.Diff => "修改文档",
                    _ => "翻译",
                };
                MainMessageQueue.Enqueue(
                    string.Format(Constants.Msg.Saved, modeText, Path.GetFileName(targetPath))
                );
            }
            catch (Exception ex)
            {
                MainMessageQueue.Enqueue(string.Format(Constants.Msg.SaveErr, ex.Message));
            }
        }

        [RelayCommand(CanExecute = nameof(CanSave))]
        private void ExportTxt(string? modeStr)
        {
            if (!Enum.TryParse<ExportMode>(modeStr, out var mode))
                return;
            SaveCore(null, mode, updateContext: false);
        }
        #endregion
    }
    #endregion
}
