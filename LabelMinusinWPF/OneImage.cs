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

// 图片模型类，包含标签集合和图片信息
public partial class OneImage : ObservableObject
{
    #region 基本属性

    // 图片路径
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayImage), nameof(ImageName))]
    private string _imagePath = string.Empty;

    // 图片名称（从路径提取）
    public string ImageName => Path.GetFileName(ImagePath);

    // 用于显示的图片源（自动从压缩包或文件加载）
    public ImageSource? DisplayImage => GetImageSource();

    // 标签列表
    public BindingList<OneLabel> Labels { get; } = [];

    // 活跃标签列表（排除已删除的）
    public List<OneLabel> ActiveLabels => [.. Labels.Where(l => !l.IsDeleted)];

    // 当前选中的标签
    [ObservableProperty] private OneLabel? _selectedLabel;

    // 是否正在刷新索引（防止递归）
    private bool _isRefreshing;

    // 构造函数：监听标签列表变化以刷新索引
    public OneImage()
    {
        Labels.ListChanged += (s, e) => { if (!_isRefreshing) RefreshIndices(); };
    }

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

    #region 标签管理

    // 刷新所有标签的索引（已删除的标签索引排在最后）
    public void RefreshIndices()
    {
        if (_isRefreshing) return;
        _isRefreshing = true;
        try
        {
            int nextIndex = 1;
            foreach (var lbl in Labels.OrderBy(l => l.IsDeleted).ThenBy(l => l.Index))
                lbl.Index = nextIndex++;
        }
        finally { _isRefreshing = false; }
        Application.Current.Dispatcher.BeginInvoke(
            () => OnPropertyChanged(nameof(ActiveLabels)));
    }

    #endregion

    #region 撤销重做

    // 撤销重做管理器
    public UndoRedoManager History { get; } = new();

    // 当前选中标签的快照（用于撤销/重做）
    private LabelSnapshot? _labelSnapshot;

    // 选中标签改变前：提交当前快照
    partial void OnSelectedLabelChanging(OneLabel? value)
    {
        TryCommitCurrentSnapshot();
        if (SelectedLabel != null) SelectedLabel.IsSelected = false;
    }

    // 选中标签改变后：更新快照和命令状态
    partial void OnSelectedLabelChanged(OneLabel? value)
    {
        if (value != null) value.IsSelected = true;
        UpdateSnapshot();
        NotifyCommands();
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

    // 当前活跃的组别
    public string ActiveGroup { get; set; } = Constants.Groups.Default;

    #endregion

    #region 命令

    // 添加标签命令
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
        SelectedLabel = newLabel;
    }

    // 删除标签命令
    [RelayCommand]
    public void DeleteLabel(OneLabel? label)
    {
        if ((label ?? SelectedLabel) is not { } target) return;
        TryCommitCurrentSnapshot();
        History.Execute(new DeleteCommand(target));
        if (SelectedLabel == target) SelectedLabel = null;
        NotifyCommands();
    }

    // 撤销命令
    [RelayCommand(CanExecute = nameof(CanUndo))]
    public void Undo()
    {
        TryCommitCurrentSnapshot();
        History.Undo();
        UpdateSnapshot();
        NotifyCommands();
    }

    // 重做命令
    [RelayCommand(CanExecute = nameof(CanRedo))]
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
            History.Execute(new UpdateLabelCommand(SelectedLabel!, _labelSnapshot!, RefreshIndices));
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
