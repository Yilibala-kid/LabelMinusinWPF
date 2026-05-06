using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LabelMinusinWPF.Common;
using LabelMinusinWPF.SelfControls;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Constants = LabelMinusinWPF.Common.Constants;
using GroupManager = LabelMinusinWPF.Common.GroupManager;
using GroupConstants = LabelMinusinWPF.Common.GroupConstants;
using ExportMode = LabelMinusinWPF.Common.LabelPlusParser.ExportMode;
using WorkSpace = LabelMinusinWPF.Common.ProjectManager.WorkSpace;

namespace LabelMinusinWPF
{
    #region 文件处理
    public partial class OneProject
    {
        #region 菜单命令：新建 / 打开 / 保存
        [RelayCommand]
        private void NewFolder() => OpenFolderDialogAndLoad("选择要新建翻译的文件夹", true);
        [RelayCommand]
        private void NewZip() => OpenFileDialogAndLoad(Constants.FileFilters.ArchiveFiles, "选择要新建翻译的压缩包", false, true);
        [RelayCommand]
        private void OpenTxt() => OpenFileDialogAndLoad(Constants.FileFilters.TextFiles, "打开已有翻译", false, false);
        [RelayCommand]
        private void OpenFolder() => OpenFolderDialogAndLoad("选择要预览的文件夹", false);
        [RelayCommand]
        private void OpenImages() => OpenFileDialogAndLoad(Constants.FileFilters.ImageFiles, "选择要预览的图片（多张）", true, false);
        [RelayCommand]
        private void OpenZip() => OpenFileDialogAndLoad(Constants.FileFilters.ArchiveFiles, "选择要预览的压缩包", false, false);

        private void OpenFolderDialogAndLoad(string title, bool isCreateMode)
        {
            var dialog = new OpenFolderDialog { Title = title };
            string? folder = dialog.ShowDialog() == true ? dialog.FolderName : null;
            if (!string.IsNullOrEmpty(folder))
                OpenResourceByPath([folder], isCreateMode);
        }

        private void OpenFileDialogAndLoad(string filter, string title, bool multiselect, bool isCreateMode)
        {
            var dialog = new OpenFileDialog { Filter = filter, Title = title, Multiselect = multiselect };
            if (dialog.ShowDialog() != true) return;
            string[] paths = multiselect ? dialog.FileNames : [dialog.FileName];
            if (paths is { Length: > 0 } && !string.IsNullOrEmpty(paths[0]))
                OpenResourceByPath(paths, isCreateMode);
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
        private void OpenWorkFolder()
        {
            string pathToOpen = WorkSpace.BaseFolderPath;
            if (!string.IsNullOrEmpty(pathToOpen) && Directory.Exists(pathToOpen))
                Process.Start(new ProcessStartInfo { FileName = pathToOpen, UseShellExecute = true, Verb = "open" });
            else
                MsgQueue.Enqueue("当前项目没有有效的文件夹路径可打开");
        }

        [RelayCommand(CanExecute = nameof(CanSave))]
        private void Clear()
        {
            if (HasUnsavedChanges())
            {
                var result = MessageBox.Show(
                    "当前翻译有未保存的修改，是否保存？", "提示",
                    MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes) Save(null);
                else if (result == MessageBoxResult.Cancel) return;
            }

            ImageList.Clear();
            SelectedImage = null;
            WorkSpace = WorkSpace.Empty;
            MsgQueue.Enqueue("工作区已清空");
        }

        public bool HasUnsavedChanges() =>
            ImageList.Any(img => img.History.UndoCount != img.SavedVersionCount);



        [RelayCommand(CanExecute = nameof(CanSave))]
        private void AdjustImageSet()
        {
            List<OneImage> availableImages;

            // 根据是否关联压缩包，获取可用图片列表
            if (WorkSpace.IsArchiveMode && File.Exists(WorkSpace.ZipPath))
                availableImages = ProjectManager.ScanZip(WorkSpace.ZipPath);
            else if (Directory.Exists(WorkSpace.BaseFolderPath))
                availableImages = ProjectManager.ScanFolder(WorkSpace.BaseFolderPath);
            else
            {
                MsgQueue.Enqueue("无法找到有效的图片源");
                return;
            }

            // 创建选择对话框
            var dialog = new ImageSelectDialog(availableImages, [.. ImageList]);
            if (dialog.ShowDialog() == true)
            {
                ReloadImages(dialog.SelectedImages);
                MsgQueue.Enqueue(string.Format("已更新图集，当前包含 {0} 张图片", ImageList.Count));
            }
        }

        [RelayCommand(CanExecute = nameof(CanSave))]
        private void LinkZip()
        {
            if (!Directory.Exists(WorkSpace.BaseFolderPath))
            {
                MsgQueue.Enqueue("当前项目没有有效的文件夹路径");
                return;
            }

            // 获取当前文件夹中的所有压缩包
            var zipFiles = Directory
                .GetFiles(WorkSpace.BaseFolderPath)
                .Where(f => Constants.ArchiveExtensions.Contains(Path.GetExtension(f)))
                .Select(f => Path.GetFileName(f))
                .ToList();

            if (zipFiles.Count == 0)
            {
                MsgQueue.Enqueue("当前文件夹中没有找到压缩包文件");
                return;
            }

            // 创建选择对话框
            var dialog = new ZipDialog(zipFiles, WorkSpace.ZipName);
            if (dialog.ShowDialog() == true)
            {
                string? selectedZip = dialog.SelectedZip;

                // 更新项目上下文
                WorkSpace = new(WorkSpace.BaseFolderPath, WorkSpace.TxtName, selectedZip);

                // 重新加载图片列表
                if (!string.IsNullOrEmpty(selectedZip))
                {
                    try
                    {
                        var zipImages = ProjectManager.ScanZip(WorkSpace.ZipPath);
                        ReloadImages(zipImages, updateImagePath: true);
                        MsgQueue.Enqueue(
                            string.Format("已关联压缩包：{0}", selectedZip)
                        );
                    }
                    catch (Exception ex)
                    {
                        MsgQueue.Enqueue(
                            string.Format("加载压缩包失败: {0}", ex.Message)
                        );
                    }
                }
                else
                {
                    // 取消关联，切换到文件夹模式
                    var folderImages = ProjectManager.ScanFolder(WorkSpace.BaseFolderPath);
                    ReloadImages(folderImages, updateImagePath: true);
                    MsgQueue.Enqueue("已取消压缩包关联，在当前文件夹获取图片");
                }
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




        #region 核心函数：加载与保存
        private void ReloadImages(List<OneImage> newImages, bool updateImagePath = false)
        {
            var existingData = ImageList.ToDictionary(img => img.ImageName, img => img);
            ImageList.Clear();

            foreach (var img in newImages)
            {
                if (existingData.TryGetValue(img.ImageName, out var existingImg))
                {
                    if (updateImagePath) existingImg.ImagePath = img.ImagePath;
                    ImageList.Add(existingImg);
                }
                else ImageList.Add(img);
            }

            SelectedImage = ImageList.FirstOrDefault();
        }

        private void LoadProject(WorkSpace context, List<OneImage> images, string successMsg)
        {
            if (images.Count == 0)
            {
                MsgQueue.Enqueue("该路径下未找到支持的图片文件");
                return;
            }
            WorkSpace = context;
            ImageList.Clear();
            images.ForEach(ImageList.Add);

            var groups = ImageList.SelectMany(img => img.Labels).Where(lbl => !lbl.IsDeleted).Select(lbl => lbl.Group);
            GroupManager.Instance.SyncGroupsFromLabels(customGroups: groups);
            SelectedImage = ImageList.FirstOrDefault();
            MsgQueue.Enqueue(string.Format("{0} (已加载 {1} 张图片)", successMsg, images.Count));
        }

        // 统一资源入口
        // param paths: 文件或文件夹路径数组
        // param isCreateMode: 新建/预览模式
        public void OpenResourceByPath(string[]? paths, bool isCreateMode)
        {
            if (paths is not { Length: > 0 })
                return;

            if (HasUnsavedChanges())
            {
                var result = MessageBox.Show(
                    "当前翻译有未保存的修改，是否保存？", "提示",
                    MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes) Save(null);
                else if (result == MessageBoxResult.Cancel) return;
            }

            string firstPath = paths.First();
            string ext = Path.GetExtension(firstPath);

            // --- 1. 图片组：只要输入中包含支持的图片，就直接加载这些图片 ---
            var imageFiles = paths
                .Where(p =>
                    File.Exists(p) && Constants.ImageExtensions.Contains(Path.GetExtension(p))
                )
                .ToList();

            if (imageFiles.Count > 0)
            {
                string baseFolder = Path.GetDirectoryName(imageFiles.First()) ?? string.Empty;
                var images = imageFiles.Select(p => new OneImage { ImagePath = p }).ToList();
                string? defaultTxtName = isCreateMode
                    ? ProjectManager.GenerateUniqueFileName(baseFolder, "New_Translation", ".txt")
                    : null;
                WorkSpace context = new(baseFolder, defaultTxtName, null);

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
                    var (context, images) = ProjectManager.GetProjectFromTxt(firstPath);
                    LoadProject(context, images, $"已加载翻译：{context.TxtName}");
                }
                catch (Exception ex)
                {
                    MsgQueue.Enqueue(string.Format("解析 TXT 失败: {0}", ex.Message));
                }
                return;
            }

            // --- 3. 压缩包 (.zip, .rar, .7z) ---
            if (Constants.ArchiveExtensions.Contains(ext) && File.Exists(firstPath))
            {
                try
                {
                    var images = ProjectManager.ScanZip(firstPath);
                    string baseFolder = Path.GetDirectoryName(firstPath) ?? string.Empty;
                    string zipName = Path.GetFileName(firstPath);
                    string? txtName = isCreateMode
                        ? Path.GetFileNameWithoutExtension(zipName) + "_翻译.txt"
                        : null;
                    WorkSpace context = new(baseFolder, txtName, zipName);

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
                    MsgQueue.Enqueue(string.Format("读取压缩包失败: {0}", ex.Message));
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
                WorkSpace context = new(firstPath, txtName, null);

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
                    WorkSpace = new(
                        Path.GetDirectoryName(targetPath)!,
                        Path.GetFileName(targetPath),
                        WorkSpace.ZipName
                    );
                    ImageList.ToList().ForEach(img => img.MarkAsSaved());
                }

                string modeText = mode switch
                {
                    ExportMode.Original => "原翻译",
                    ExportMode.Diff => "修改文档",
                    _ => "翻译",
                };
                MsgQueue.Enqueue(
                    string.Format("已保存{0}到 {1}", modeText, Path.GetFileName(targetPath))
                );
            }
            catch (Exception ex)
            {
                MsgQueue.Enqueue(string.Format("保存失败: {0}", ex.Message));
            }
        }


        #endregion
    }
    #endregion
}
