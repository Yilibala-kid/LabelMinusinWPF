using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
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
        ActiveLabelsView = new CollectionViewSource { Source = Labels }.View;
        ActiveLabelsView.Filter = item => item is OneLabel label && !label.IsDeleted;

        if (ActiveLabelsView is ICollectionViewLiveShaping liveView && liveView.CanChangeLiveFiltering)
        {
            liveView.LiveFilteringProperties.Add(nameof(OneLabel.IsDeleted));
            liveView.IsLiveFiltering = true;
        }

        if (ActiveLabelsView is INotifyCollectionChanged activeLabelsChanged)
            activeLabelsChanged.CollectionChanged += ActiveLabelsView_CollectionChanged;
    }

    private void ActiveLabelsView_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ActiveLabelsView));
        OnPropertyChanged(nameof(SelectedLabel));
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
    public ObservableCollection<OneLabel> Labels { get; } = [];// 标签列表
    [ObservableProperty]
    private OneLabel? _selectedLabel;// 当前选中的标签
    public ICollectionView ActiveLabelsView { get; }// 活跃标签视图（排除已删除的）

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

    #region 撤销重做

    private readonly UndoRedoManager _history = new();

    // 标记当前状态为"已保存"
    public void MarkAsSaved()
    {
        CommitPendingEdit();
        _history.MarkAsSaved();
        RefreshHistoryState();
    }

    public bool HasUnsavedChanges =>
        _history.HasUnsavedChanges || IsSelectedLabelDirty();

    // 当前选中标签的快照（用于撤销/重做）
    private LabelSnapshot? _labelSnapshot;

    // 选中标签改变前：提交当前快照
    partial void OnSelectedLabelChanging(OneLabel? value)
    {
        CommitPendingEdit();
        if (SelectedLabel != null)
            SelectedLabel.PropertyChanged -= SelectedLabel_PropertyChanged;
    }

    // 选中标签改变后：更新快照和命令状态
    partial void OnSelectedLabelChanged(OneLabel? value)
    {
        if (value != null)
            value.PropertyChanged += SelectedLabel_PropertyChanged;
        UpdateSnapshot();
        RefreshHistoryState();
        GroupManager.Instance.SetSelectedGroup(value?.Group);
    }

    // 判断是否可以撤销
    private bool CanUndo() => _history.CanUndo || IsSelectedLabelDirty();

    // 判断是否可以重做
    private bool CanRedo() => _history.CanRedo && !IsSelectedLabelDirty();

    // 判断当前选中标签是否有未保存的修改
    private bool IsSelectedLabelDirty() =>
        SelectedLabel != null && _labelSnapshot != null && !_labelSnapshot.Matches(SelectedLabel);

    private void SelectedLabel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(OneLabel.Text) or nameof(OneLabel.Group) or nameof(OneLabel.Position)))
            return;

        RefreshHistoryState();
        if (e.PropertyName == nameof(OneLabel.Group))
            GroupManager.Instance.SetSelectedGroup(SelectedLabel?.Group);
    }

    #endregion

    #region 命令
    [RelayCommand]// 添加标签命令
    public void AddLabel(Point? pos)
    {
        var newLabel = new OneLabel("", GroupManager.Instance.SelectedGroup ?? GroupConstants.InBox, pos ?? new Point(0.5, 0.5));
        AddLabelWithHistory(newLabel, select: true);
    }
    public OneLabel PasteLabel(LabelSnapshot snapshot)
    {
        var newLabel = new OneLabel(snapshot.Text, snapshot.Group, GetOffsetPastePosition(snapshot.Position));
        return AddLabelWithHistory(newLabel, select: true);
    }

    public void ApplyLabelContent(LabelSnapshot snapshot)
    {
        if (SelectedLabel is not { } target)
            return;

        CommitPendingEdit();
        var oldState = new LabelSnapshot(target);
        var newState = oldState with { Text = snapshot.Text, Group = snapshot.Group };
        if (oldState == newState)
            return;

        _history.Execute(
            () => newState.RestoreTo(target),
            () => oldState.RestoreTo(target));
        UpdateSnapshot();
        RefreshHistoryState();
        GroupManager.Instance.SetSelectedGroup(target.Group);
    }

    internal OneLabel AddLabelWithHistory(OneLabel label, bool select = false)
    {
        CommitPendingEdit();
        _history.Execute(
            () => { label.IsDeleted = false; Labels.Add(label); },
            () => { label.IsDeleted = true; Labels.Remove(label); },
            label);
        if (select)
            SelectedLabel = label;
        RefreshHistoryState();
        return label;
    }

    private static Point GetOffsetPastePosition(Point position)
    {
        const double offset = 0.02;
        return new Point(OffsetAxis(position.X), OffsetAxis(position.Y));

        static double OffsetAxis(double value) =>
            value <= 1 - offset ? value + offset : Math.Max(0, value - offset);
    }

    [RelayCommand]// 删除标签命令
    public void DeleteLabel(OneLabel? label)
    {
        if ((label ?? SelectedLabel) is not { } target) return;
        CommitPendingEdit();

        // 尚未保存的新建空标签可折叠为无操作。
        if (string.IsNullOrEmpty(target.OriginalText)
            && string.IsNullOrEmpty(target.Text)
            && _history.TryCancelLatest(target))
        {
            if (SelectedLabel == target) SelectedLabel = null;
            RefreshHistoryState();
            return;
        }

        _history.Execute(
            () => target.IsDeleted = true,
            () => target.IsDeleted = false);
        if (SelectedLabel == target) SelectedLabel = null;
        RefreshHistoryState();
    }
    [RelayCommand(CanExecute = nameof(CanUndo))]// 撤销命令
    public void Undo()
    {
        CommitPendingEdit();
        if (_history.Undo())
            RefreshSelectionAfterHistoryChange();
    }
    [RelayCommand(CanExecute = nameof(CanRedo))]// 重做命令
    public void Redo()
    {
        if (_history.Redo())
            RefreshSelectionAfterHistoryChange();
    }

    // 提交当前快照，将连续输入或拖拽合并为一个历史步骤。
    internal void CommitPendingEdit()
    {
        if (SelectedLabel is not { } selected
            || _labelSnapshot is not { } snapshot
            || snapshot.Matches(selected))
            return;

        var current = new LabelSnapshot(selected);
        _history.Execute(
            () => current.RestoreTo(selected),
            () => snapshot.RestoreTo(selected));
        UpdateSnapshot();
        RefreshHistoryState();
    }

    // 更新快照
    private void UpdateSnapshot() =>
        _labelSnapshot = SelectedLabel != null ? new LabelSnapshot(SelectedLabel) : null;

    private void RefreshSelectionAfterHistoryChange()
    {
        if (SelectedLabel is { } selected
            && (!Labels.Contains(selected) || selected.IsDeleted))
            SelectedLabel = null;
        else
            UpdateSnapshot();

        RefreshHistoryState();
    }

    // 通知历史状态变化
    private void RefreshHistoryState()
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    #endregion
}
