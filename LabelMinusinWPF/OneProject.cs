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
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Constants = LabelMinusinWPF.Common.Constants;
using GroupManager = LabelMinusinWPF.Common.GroupManager;
using GroupConstants = LabelMinusinWPF.Common.GroupConstants;
using ExportMode = LabelMinusinWPF.Common.LabelPlusParser.ExportMode;
using WorkSpace = LabelMinusinWPF.Common.ProjectManager.WorkSpace;

namespace LabelMinusinWPF
{
    // 主视图模型

    public partial class OneProject : ObservableObject
    {
        #region 初始化

        [ObservableProperty]
        private ISnackbarMessageQueue _MsgQueue;

        public OneProject()
        {
            MsgQueue = new SnackbarMessageQueue(TimeSpan.FromSeconds(2));
            ImageList.ListChanged += (_, _) => SortImagesByNameCommand.NotifyCanExecuteChanged();

            // 注册 GroupManager 消息处理器
            WeakReferenceMessenger.Default.Register<GroupManager.GroupManagerShowMessageMessage>(this, (r, m) =>
            {
                MsgQueue.Enqueue(m.Message);
            });

            WeakReferenceMessenger.Default.Register<GroupManager.GroupManagerSelectedGroupChangedMessage>(this, (r, m) =>
            {
                if (SelectedImage?.SelectedLabel != null && m.GroupName != null)
                    SelectedImage.SelectedLabel.Group = m.GroupName;
            });

            // DeleteGroup 查询响应
            WeakReferenceMessenger.Default.Register<GroupManager.DeleteGroupQueryMessage>(this, (r, m) =>
            {
                var labels = ImageList
                    .SelectMany(img => img.Labels)
                    .Where(lbl => !lbl.IsDeleted && lbl.Group == m.GroupName)
                    .ToList();

                if (labels.Count > 0)
                {
                    var locations = labels.Select((lbl, idx) =>
                    {
                        var img = ImageList.FirstOrDefault(i => i.Labels.Contains(lbl));
                        int labelIdx = img?.Labels.IndexOf(lbl) + 1 ?? 0;
                        return $"[{img?.ImageName ?? "?"}-{labelIdx}]";
                    });
                    string locationStr = string.Join(", ", locations);
                    WeakReferenceMessenger.Default.Send(new GroupManager.GroupManagerShowMessageMessage(
                        $"组【{m.GroupName}】被以下标签使用：{locationStr}"));
                }

                WeakReferenceMessenger.Default.Send(new GroupManager.DeleteGroupResponseMessage(m.GroupName, labels, m.Tcs));
            });
        }
        #endregion

        // --- 项目上下文 ---
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(OpenWorkFolderCommand))]
        [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
        [NotifyCanExecuteChangedFor(nameof(AdjustImageSetCommand))]
        [NotifyCanExecuteChangedFor(nameof(LinkZipCommand))]
        [NotifyCanExecuteChangedFor(nameof(ExportTxtCommand))]
        [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
        private WorkSpace _workSpace = WorkSpace.Empty;

        public BindingList<OneImage> ImageList { get; } = [];

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(PreviousImageCommand))]
        [NotifyCanExecuteChangedFor(nameof(NextImageCommand))]
        private OneImage? _selectedImage;// 当前图片

        #region 图片切换
        [RelayCommand(CanExecute = nameof(CanGoToPrevious))]
        public void PreviousImage()
        {
            int idx = NavigationHelper.NavigateIndex(ImageList.IndexOf(SelectedImage!), ImageList.Count, forward: false);
            if (idx >= 0) SelectedImage = ImageList[idx];
        }
        private bool CanGoToPrevious() => NavigationHelper.NavigateIndex(ImageList.IndexOf(SelectedImage!), ImageList.Count, forward: false) >= 0;

        [RelayCommand(CanExecute = nameof(CanGoToNext))]
        public void NextImage()
        {
            int idx = NavigationHelper.NavigateIndex(ImageList.IndexOf(SelectedImage!), ImageList.Count, forward: true);
            if (idx >= 0) SelectedImage = ImageList[idx];
        }
        private bool CanGoToNext() => NavigationHelper.NavigateIndex(ImageList.IndexOf(SelectedImage!), ImageList.Count, forward: true) >= 0;
        #endregion

        #region Image sorting
        [RelayCommand(CanExecute = nameof(CanSortImagesByName))]
        private void SortImagesByName()
        {
            if (!CanSortImagesByName())
                return;

            var selectedImage = SelectedImage;
            var sortedImages = ImageList
                .OrderBy(img => img.ImageName, NaturalFileNameComparer.Instance)
                .ToList();

            ImageList.RaiseListChangedEvents = false;
            try
            {
                ImageList.Clear();
                sortedImages.ForEach(ImageList.Add);
            }
            finally
            {
                ImageList.RaiseListChangedEvents = true;
                ImageList.ResetBindings();
            }

            SelectedImage = selectedImage != null && ImageList.Contains(selectedImage)
                ? selectedImage
                : ImageList.FirstOrDefault();
            PreviousImageCommand.NotifyCanExecuteChanged();
            NextImageCommand.NotifyCanExecuteChanged();
            MsgQueue.Enqueue("已按名称重新排列图片");
        }

        private bool CanSortImagesByName() => ImageList.Count > 1;

        private sealed class NaturalFileNameComparer : IComparer<string>
        {
            public static NaturalFileNameComparer Instance { get; } = new();

            public int Compare(string? x, string? y) =>
                StrCmpLogicalW(x ?? string.Empty, y ?? string.Empty);

            [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
            private static extern int StrCmpLogicalW(string x, string y);
        }
        #endregion
    }
}
