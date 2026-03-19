using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LabelMinusinWPF.Common;

namespace LabelMinusinWPF;
public partial class OneImage : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayImage), nameof(ImageName))]
    private string _imagePath = string.Empty;// 图片完整路径
    public string ImageName => Path.GetFileName(ImagePath);// 图片名
    public ImageSource? DisplayImage => GetImageSource();//图片源获取


    public BindingList<OneLabel> Labels { get; } = [];// 图片包含的标签
    public List<OneLabel> ActiveLabels => [.. Labels.Where(l => !l.IsDeleted)];// 获取未删除的标签列表
    [ObservableProperty] private OneLabel? _selectedLabel;// 当前选中的标注
    


    private bool _isRefreshing = false;
    public OneImage()
    {
        Labels.ListChanged += (s, e) => { if (!_isRefreshing) RefreshIndices(); };
    }
    #region 业务逻辑方法
    private BitmapImage? GetImageSource()
    {
        if (string.IsNullOrEmpty(ImagePath)) return null;

        var archiveResult = ResourceHelper.ParseArchivePath(ImagePath);
        // 如果是压缩包路径，调用专门的解压加载方法
        if (archiveResult.HasValue)
        {
            var (archivePath, entryPath) = archiveResult.Value;
            return ResourceHelper.LoadImageFromZip(archivePath, entryPath);
        }
        // 如果是普通路径，直接加载
        return ResourceHelper.LoadFromPath(ImagePath);
    }
    public void RefreshIndices()
    {
        if (_isRefreshing) return;
        _isRefreshing = true;
        try
        {
            int nextIndex = 1;
            foreach (var lbl in Labels.OrderBy(l => l.IsDeleted).ThenBy(l => l.Index))
            {
                lbl.Index = nextIndex++;
            }
        }
        finally
        {
            _isRefreshing = false;
            OnPropertyChanged(nameof(ActiveLabels));
        }
    }
    #endregion


    #region 撤回与重做功能
    public UndoRedoManager History { get; } = new();
    private LabelSnapshot? _labelSnapshot;// 存储选中 Label 时的初始快照
    partial void OnSelectedLabelChanging(OneLabel? value)// 当 SelectedLabel 即将改变时调用,SelectedLabel 变更监听
    {
        TryCommitCurrentSnapshot();
        if (SelectedLabel != null) SelectedLabel.IsSelected = false;// 取消旧标签的选中状态
    }
    partial void OnSelectedLabelChanged(OneLabel? value)
    {
        if (value != null) value.IsSelected = true;
        UpdateSnapshot();
        NotifyCommands(); // 选中状态改变可能会使当前 Label 变脏，从而激活撤销按钮
    }
    private bool CanUndo() => History.CanUndo || IsSelectedLabelDirty();// 判断是否可以撤回：历史栈有东西 OR 当前选中的标签被改动过
    private bool CanRedo() => History.CanRedo;// 判断是否可以重做：历史栈有东西
    private bool IsSelectedLabelDirty()// 辅助方法,检查当前选中的标签是否有未提交的改动
    {
        if (SelectedLabel == null || _labelSnapshot == null) return false;
        return SelectedLabel.Text != _labelSnapshot.Text ||
               SelectedLabel.Group != _labelSnapshot.Group ||
               SelectedLabel.Position != _labelSnapshot.Position;
    }
    public string ActiveGroup { get; set; } = Constants.Groups.Default;// 新建标签时使用的默认组别
    [RelayCommand]
    public void AddLabel(Point? pos)
    {
        TryCommitCurrentSnapshot();
        int nextIndex = Labels.Count(l => !l.IsDeleted) + 1;
        var newLabel = new OneLabel
        {
            Index = nextIndex,
            Text = Constants.Label.NewLabelText,
            Group = ActiveGroup,
            Position = pos ?? new Point(0.5, 0.5)
        };
        History.Execute(new AddCommand(Labels, newLabel));
        SelectedLabel = newLabel; // 自动选中新标签
    }

    [RelayCommand]
    public void DeleteLabel(OneLabel? label)
    {
        if ((label ?? SelectedLabel) is not { } target) return;

        TryCommitCurrentSnapshot();
        History.Execute(new DeleteCommand(target));
        if (SelectedLabel == target) SelectedLabel = null;
        NotifyCommands();
    }
    [RelayCommand(CanExecute = nameof(CanUndo))]
    public void Undo()
    {
        TryCommitCurrentSnapshot();
        History.Undo();
        UpdateSnapshot();
        NotifyCommands();
    }
    [RelayCommand(CanExecute = nameof(CanRedo))]
    public void Redo()
    {
        History.Redo();
        UpdateSnapshot();
        NotifyCommands();
    }

    
    private void TryCommitCurrentSnapshot()//用来撤销当前选中标签的未提交修改（例如用户修改了标签但没有切换到其他标签或保存就关闭了窗口）
    {
        if (IsSelectedLabelDirty())        // 直接复用 IsSelectedLabelDirty 方法
        {
            History.Execute(new UpdateLabelCommand(SelectedLabel!, _labelSnapshot!, RefreshIndices));
            UpdateSnapshot();
        }
    }
    
    private void UpdateSnapshot() =>
        _labelSnapshot = SelectedLabel != null ? new LabelSnapshot(SelectedLabel) : null;// 统一处理快照刷新

    
    private void NotifyCommands()// 手动命令状态刷新
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }
    #endregion
}
