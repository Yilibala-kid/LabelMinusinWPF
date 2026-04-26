using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LabelMinusinWPF.Common;
using GroupManager = LabelMinusinWPF.Common.GroupManager;
using GroupConstants = LabelMinusinWPF.Common.GroupConstants;

namespace LabelMinusinWPF;

// 图片模型类，包含标签集合和图片信息
public partial class OneImage : ObservableObject
{
    public OneImage()
    {
        Labels.ListChanged += OnLabelsChanged;
    }

    private void OnLabelsChanged(object? sender, ListChangedEventArgs e)
    {
        bool changesActiveSet =
            e.ListChangedType is ListChangedType.ItemAdded
                or ListChangedType.ItemDeleted
                or ListChangedType.ItemMoved
                or ListChangedType.Reset
            || e.ListChangedType == ListChangedType.ItemChanged
                && e.PropertyDescriptor?.Name == nameof(OneLabel.IsDeleted);

        if (changesActiveSet)
        {
            OnPropertyChanged(nameof(ActiveLabels));
        }
    }

    #region 图片自身属性

    // 图片路径
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayImage), nameof(ImageName))]
    private string _imagePath = string.Empty;

    // 图片名称（从路径提取）
    public string ImageName => Path.GetFileName(ImagePath);

    // 用于显示的图片源（自动从压缩包或文件加载）
    public ImageSource? DisplayImage => GetImageSource();
    #endregion

    #region 携带标签
    public BindingList<OneLabel> Labels { get; } = [];// 标签列表
    [ObservableProperty] 
    private OneLabel? _selectedLabel;// 当前选中的标签
    public List<OneLabel> ActiveLabels => [.. Labels.Where(l => !l.IsDeleted)];// 活跃标签列表（排除已删除的）

    #endregion

    #region 图片加载

    // 根据图片路径获取图片源（支持从压缩包加载）
    private BitmapImage? GetImageSource()
    {
        if (string.IsNullOrEmpty(ImagePath)) return null;
        var archiveResult = ResourceHelper.ParseArchivePath(ImagePath);
        if (archiveResult.HasValue)
        {
            var (archivePath, entryPath) = archiveResult.Value;
            return ResourceHelper.LoadImageFromZip(archivePath, entryPath);
        }
        return ResourceHelper.LoadFromPath(ImagePath);
    }

    #endregion

    //TODO：撤消重做逻辑待审查
    #region 撤销重做

    // 撤销重做管理器
    public UndoRedoManager History { get; } = new();

    // 保存时的 UndoCount 快照
    public int SavedVersionCount { get; private set; } = 0;

    // 标记当前状态为"已保存"
    public void MarkAsSaved() => SavedVersionCount = History.UndoCount;

    // 当前选中标签的快照（用于撤销/重做）
    private LabelSnapshot? _labelSnapshot;

    // 选中标签改变前：提交当前快照
    partial void OnSelectedLabelChanging(OneLabel? value)
    {
        TryCommitCurrentSnapshot();
    }

    // 选中标签改变后：更新快照和命令状态
    partial void OnSelectedLabelChanged(OneLabel? value)
    {
        UpdateSnapshot();
        NotifyCommands();
        GroupManager.Instance.SetSelectedGroup(value?.Group);
    }

    // 判断是否可以撤销
    private bool CanUndo() => History.CanUndo || IsSelectedLabelDirty();

    // 判断是否可以重做
    private bool CanRedo() => History.CanRedo;

    // 判断当前选中标签是否有未保存的修改
    private bool IsSelectedLabelDirty()
    {
        if (SelectedLabel == null || _labelSnapshot == null) return false;
        return SelectedLabel.Text != _labelSnapshot.Text ||
               SelectedLabel.Group != _labelSnapshot.Group ||
               SelectedLabel.Position != _labelSnapshot.Position;
    }



    #endregion

    #region 命令
    [RelayCommand]// 添加标签命令
    public void AddLabel(Point? pos)
    {
        TryCommitCurrentSnapshot();
        var newLabel = new OneLabel("", GroupManager.Instance.SelectedGroup ?? GroupConstants.InBox, pos ?? new Point(0.5, 0.5));
        History.Execute(new AddCommand(Labels, newLabel));
        SelectedLabel = newLabel;
    }
    [RelayCommand]// 删除标签命令
    public void DeleteLabel(OneLabel? label)
    {
        if ((label ?? SelectedLabel) is not { } target) return;
        // 未保存的新标签（原文为空）直接移除，不进软删除历史
        if (string.IsNullOrEmpty(target.OriginalText) && string.IsNullOrEmpty(target.Text))
        {
            Labels.Remove(target);
            if (SelectedLabel == target) SelectedLabel = null;
            NotifyCommands();
            return;
        }
        TryCommitCurrentSnapshot();
        History.Execute(new DeleteCommand(target));
        if (SelectedLabel == target) SelectedLabel = null;
        NotifyCommands();
    }
    [RelayCommand(CanExecute = nameof(CanUndo))]// 撤销命令
    public void Undo()
    {
        TryCommitCurrentSnapshot();
        History.Undo();
        UpdateSnapshot();
        NotifyCommands();
    }
    [RelayCommand(CanExecute = nameof(CanRedo))]// 重做命令
    public void Redo()
    {
        History.Redo();
        UpdateSnapshot();
        NotifyCommands();
    }

    // 尝试提交当前快照
    private void TryCommitCurrentSnapshot()
    {
        if (IsSelectedLabelDirty())
        {
            History.Execute(new UpdateLabelCommand(SelectedLabel!, _labelSnapshot!));
            UpdateSnapshot();
        }
    }

    // 更新快照
    private void UpdateSnapshot() =>
        _labelSnapshot = SelectedLabel != null ? new LabelSnapshot(SelectedLabel) : null;

    // 通知命令状态变化
    private void NotifyCommands()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    #endregion
}
