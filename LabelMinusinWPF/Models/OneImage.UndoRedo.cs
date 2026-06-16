using CommunityToolkit.Mvvm.Input;
using LabelMinusinWPF.Common;
using System.ComponentModel;
using System.Linq;
using GroupManager = LabelMinusinWPF.Common.GroupManager;

namespace LabelMinusinWPF;

public partial class OneImage
{
    #region 撤销重做
    private readonly UndoRedoManager _history = new();
    private LabelSnapshot? _labelSnapshot;

    private bool CanUndo() => _history.CanUndo || IsSelectedLabelDirty();
    private bool CanRedo() => _history.CanRedo && !IsSelectedLabelDirty();

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
    public bool HasUnsavedChanges =>
        _history.HasUnsavedChanges || IsSelectedLabelDirty();

    // 判断当前选中标签是否有未保存的修改
    private bool IsSelectedLabelDirty() =>
        SelectedLabel is { } selected
        && _labelSnapshot is { } snapshot
        && !snapshot.Matches(selected);

    // 标记当前状态为"已保存"
    public void MarkAsSaved()
    {
        CommitPendingEdit();
        _history.MarkAsSaved();
        RefreshHistoryState();
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

    // 选中标签改变前：提交当前快照
    partial void OnSelectedLabelChanging(OneLabel? value)
    {
        CommitPendingEdit();
        if (SelectedLabel is { } selected)
            selected.PropertyChanged -= SelectedLabel_PropertyChanged;
    }

    // 选中标签改变后：更新快照和命令状态
    partial void OnSelectedLabelChanged(OneLabel? value)
    {
        if (value is { })
            value.PropertyChanged += SelectedLabel_PropertyChanged;
        UpdateSnapshot();
        RefreshHistoryState();
        GroupManager.Instance.SetSelectedGroup(value?.Group);
    }

    private void SelectedLabel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(OneLabel.Text) or nameof(OneLabel.Group) or nameof(OneLabel.Position)))
            return;

        RefreshHistoryState();
        if (e.PropertyName == nameof(OneLabel.Group))
            GroupManager.Instance.SetSelectedGroup(SelectedLabel?.Group);
    }



    private void RefreshSelectionAfterHistoryChange()
    {
        if (SelectedLabel is { } selected && (!Labels.Contains(selected) || selected.IsDeleted))
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
    // 更新快照
    private void UpdateSnapshot()
    {
        _labelSnapshot = SelectedLabel is { } selected ? new LabelSnapshot(selected) : null;
    }

    #endregion
}
