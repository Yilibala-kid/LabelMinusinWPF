using CommunityToolkit.Mvvm.Input;
using System.Windows;
using GroupManager = LabelMinusinWPF.Common.GroupManager;
using GroupConstants = LabelMinusinWPF.Common.GroupConstants;

namespace LabelMinusinWPF;

public partial class OneImage
{
    #region 命令
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

    [RelayCommand]// 添加标签命令
    public void AddLabel(Point? pos)
    {
        var newLabel = new OneLabel("", GroupManager.Instance.SelectedGroup ?? GroupConstants.InBox, pos ?? new Point(0.5, 0.5));
        AddLabelWithHistory(newLabel, select: true);
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



    #endregion
}
