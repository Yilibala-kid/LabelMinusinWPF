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

namespace LabelMinusinWPF
{
    public partial class ImageInfo : ObservableObject, IDisposable
    {
        // 图片完整路径
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ImageSource), nameof(ImageName))]
        private string _imagePath = string.Empty;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ImageSource))]
        private string? _zipEntryName; // 压缩包内文件的相对路径

        public string ImageName => ZipEntryName ?? Path.GetFileName(ImagePath);
        // 图片包含的标签
        public BindingList<ImageLabel> Labels { get; } = [];
        // 新建标签时使用的默认组别（由 MainVM 同步）
        public string ActiveGroup { get; set; } = Constants.Groups.Default;

        // 只读属性：获取未删除的标签列表（带缓存优化）
        private List<ImageLabel>? _cachedActiveLabels;
        private bool _activeLabelsCacheValid;
        public List<ImageLabel> ActiveLabels
        {
            get
            {
                if (_activeLabelsCacheValid && _cachedActiveLabels != null)
                    return _cachedActiveLabels;

                _cachedActiveLabels = [.. Labels.Where(l => !l.IsDeleted)];
                _activeLabelsCacheValid = true;
                return _cachedActiveLabels;
            }
        }

        // 当前选中的标注
        [ObservableProperty] private ImageLabel? _selectedLabel;
        #region 图片源获取
        public ImageSource? ImageSource
        {
            get
            {
                if (string.IsNullOrEmpty(ImagePath)) return null;

                try
                {
                    // 使用 ResourceHelper 解析路径
                    var archiveResult = ResourceHelper.ParseArchivePath(ImagePath);
                    if (archiveResult.HasValue)
                    {
                        // 压缩包逻辑
                        var (archivePath, entryPath) = archiveResult.Value;
                        byte[]? data = ResourceHelper.ExtractFileToBytes(archivePath, entryPath);
                        return data != null ? ResourceHelper.LoadFromBytes(data) : null;
                    }
                    else
                    {
                        // 普通文件逻辑 - 每次直接加载
                        return ResourceHelper.LoadFromPath(ImagePath);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"加载图片失败: {ex.Message}");
                    return null;
                }
            }
        }

        #endregion

        private bool _isRefreshing = false;

        // 用于事件取消订阅的处理器
        private readonly ListChangedEventHandler _labelsListChangedHandler;
        private readonly EventHandler _requeryHandler;

        public ImageInfo()
        {
            // 订阅 BindingList 的 ListChanged 事件
            _labelsListChangedHandler = (s, e) =>
            {
                // 当列表发生增删改操作时，刷新索引
                if (!_isRefreshing) RefreshIndices();
                // 使缓存失效
                _activeLabelsCacheValid = false;
            };
            Labels.ListChanged += _labelsListChangedHandler;

            // 自动刷新 Undo/Redo 按钮的可点击状态
            _requeryHandler = (s, e) =>
            {
                UndoCommand.NotifyCanExecuteChanged();
                RedoCommand.NotifyCanExecuteChanged();
            };
            CommandManager.RequerySuggested += _requeryHandler;
        }

        // 释放资源，取消事件订阅
        public void Dispose()
        {
            Labels.ListChanged -= _labelsListChangedHandler;
            CommandManager.RequerySuggested -= _requeryHandler;
            GC.SuppressFinalize(this);
        }

        #region 业务逻辑方法
        public void RefreshIndices()
        {
            if (_isRefreshing) return;
            _isRefreshing = true;

            try
            {
                int nextIndex = 1;
                var sortedList = Labels.OrderBy(l => l.IsDeleted).ThenBy(l => l.Index).ToList();// 按是否删除排序，未删除的在前，已删除的在后，然后统一重新分配 Index
                foreach (var lbl in sortedList)
                {
                    lbl.Index = nextIndex++;
                }
            }
            finally
            {
                OnPropertyChanged(nameof(ActiveLabels));// 2. 通知 UI 关联属性刷新
                _isRefreshing = false;
            }
        }

        public ICommand SelectLabelCommand => new RelayCommand<ImageLabel>(label =>
        {
            SelectedLabel = label;
        });
        #endregion


        #region 撤回与重做功能
        // 用于存储选中 Label 时的初始快照
        private LabelSnapshot? _labelSnapshot;
        #region SelectedLabel 变更监听

        // 当 SelectedLabel 即将改变时调用
        partial void OnSelectedLabelChanging(ImageLabel? value)
        {
            TryCommitCurrentSnapshot();
            // 取消旧标签的选中状态
            if (SelectedLabel != null) SelectedLabel.IsSelected = false;
        }

        // 当 SelectedLabel 改变完成后调用
        partial void OnSelectedLabelChanged(ImageLabel? value)
        {
            if (value != null)
            {
                value.IsSelected = true;
                // 记录进入编辑状态时的初始快照
                _labelSnapshot = new LabelSnapshot(value);
            }
            else
            {
                _labelSnapshot = null;
            }
        }
        #endregion

        public UndoRedoManager History { get; } = new();
        
        private bool CanUndo() => History.CanUndo || IsSelectedLabelDirty();// 判断是否可以撤回：历史栈有东西 OR 当前选中的标签被改动过
        private bool CanRedo() => History.CanRedo;// 判断是否可以重做：历史栈有东西
        private bool IsSelectedLabelDirty()// 辅助方法：检查当前选中的标签是否有未提交的改动
        {
            if (SelectedLabel == null || _labelSnapshot == null) return false;
            return SelectedLabel.Text != _labelSnapshot.Text ||
                   SelectedLabel.Group != _labelSnapshot.Group ||
                   SelectedLabel.Position != _labelSnapshot.Position;
        }
        [RelayCommand]
        public void AddLabel(Point? pos)
        {
            TryCommitCurrentSnapshot();
            int nextIndex = Labels.Count(l => !l.IsDeleted) + 1;
            var newLabel = new ImageLabel
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
        public void DeleteLabel(ImageLabel? label)
        {
            var target = label ?? SelectedLabel;
            if (target == null) return;

            TryCommitCurrentSnapshot();//如果删除的正好是正在编辑的标签，也需要先把改动记录下来


            History.Execute(new DeleteCommand(target));
            if (SelectedLabel == target) SelectedLabel = null;
        }

        [RelayCommand(CanExecute = nameof(CanUndo))]
        public void Undo()
        {
            TryCommitCurrentSnapshot();// 1. 看看当前选中的标签是不是正在被编辑
            History.Undo();// 2. 执行撤回
            if (SelectedLabel != null)// 3. 撤回后，由于标签内容变了，必须同步更新快照，否则下次对比会出错
            {
                _labelSnapshot = new LabelSnapshot(SelectedLabel);
            }
        }
        [RelayCommand(CanExecute = nameof(CanRedo))]
        public void Redo()
        {
            History.Redo();

            // 【补充】重做后也要同步快照！
            if (SelectedLabel != null)
            {
                _labelSnapshot = new LabelSnapshot(SelectedLabel);
            }
        }

        //用来撤销当前选中标签的未提交修改（例如用户修改了标签但没有切换到其他标签或保存就关闭了窗口）
        private void TryCommitCurrentSnapshot()
        {        
            if (SelectedLabel != null && _labelSnapshot != null)// 如果当前有选中的标签，且它与快照不一致（被改动过）
            {
                if (SelectedLabel.Text != _labelSnapshot.Text ||
                    SelectedLabel.Group != _labelSnapshot.Group ||
                    SelectedLabel.Position != _labelSnapshot.Position)
                {    
                    History.Execute(new UpdateLabelCommand(SelectedLabel, _labelSnapshot, RefreshIndices));// 将当前的改动作为一个新的命令压入栈
                    _labelSnapshot = new LabelSnapshot(SelectedLabel);// 【关键】提交后，更新快照为当前最新状态，防止重复提交
                }
            }
        }
        #endregion
    }
}