using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.IO;
using System.Windows;

namespace LabelMinusinWPF
{
    public partial class CompareImgVM : ObservableObject, IDisposable
    {
        private bool _disposed;
        [ObservableProperty]
        private MainVM _leftImageVM = new();
        [ObservableProperty]
        private MainVM _rightImageVM = new();

        #region combobox绑定
        // 1. 供 ComboBox 绑定的数据源 (所有图片名的并集)
        public ObservableCollection<string> AllImageNames { get; } = [];

        // 2. ComboBox 选中的名字
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(PreviousImageCommand))]
        [NotifyCanExecuteChangedFor(nameof(NextImageCommand))]
        private string? _selectedMergedName;

        // 当 ComboBox 选中项改变时，自动去左右两边找对应的图
        partial void OnSelectedMergedNameChanged(string? value)
        {
            if (string.IsNullOrEmpty(value)) return;

            LeftImageVM.SelectedImage = LeftImageVM.ImageList.FirstOrDefault(img =>
                Path.GetFileNameWithoutExtension(img.ImageName).Equals(value, StringComparison.OrdinalIgnoreCase));
            RightImageVM.SelectedImage = RightImageVM.ImageList.FirstOrDefault(img =>
                Path.GetFileNameWithoutExtension(img.ImageName).Equals(value, StringComparison.OrdinalIgnoreCase));
        }

        public CompareImgVM()
        {
            // 监听左右两个列表的变化事件 (BindingList 使用 ListChanged)
            LeftImageVM.ImageList.ListChanged += ImageList_ListChanged;
            RightImageVM.ImageList.ListChanged += ImageList_ListChanged;
        }

        // 当任意一边的列表发生变化（加载图片/清空）时，重新计算并集
        private void ImageList_ListChanged(object? sender, ListChangedEventArgs e)
        {
            var leftNames = LeftImageVM.ImageList.Select(x => Path.GetFileNameWithoutExtension(x.ImageName));
            var rightNames = RightImageVM.ImageList.Select(x => Path.GetFileNameWithoutExtension(x.ImageName));

            // 取并集(已自动去重) -> 排序
            var union = leftNames.Union(rightNames)
                                 .OrderBy(n => n)
                                 .ToList();

            // 在 UI 线程更新集合
            Application.Current.Dispatcher.Invoke(() =>
            {
                AllImageNames.Clear();
                foreach (var name in union)
                    AllImageNames.Add(name);

                if (SelectedMergedName == null && AllImageNames.Count > 0)
                    SelectedMergedName = AllImageNames.First();
            });
        }
        #endregion

        #region 上下切换

        // 上一张
        [RelayCommand(CanExecute = nameof(CanGoToPrevious))]
        private void PreviousImage()
        {
            int currentIndex = AllImageNames.IndexOf(SelectedMergedName!);
            if (currentIndex > 0)
                SelectedMergedName = AllImageNames[currentIndex - 1];
        }

        private bool CanGoToPrevious()
            => AllImageNames.Count > 0 && !string.IsNullOrEmpty(SelectedMergedName) && AllImageNames.IndexOf(SelectedMergedName) > 0;

        // 下一张
        [RelayCommand(CanExecute = nameof(CanGoToNext))]
        private void NextImage()
        {
            int currentIndex = AllImageNames.IndexOf(SelectedMergedName!);
            if (currentIndex < AllImageNames.Count - 1)
                SelectedMergedName = AllImageNames[currentIndex + 1];
        }

        private bool CanGoToNext()
            => AllImageNames.Count > 0 && !string.IsNullOrEmpty(SelectedMergedName) && AllImageNames.IndexOf(SelectedMergedName) < AllImageNames.Count - 1;

        #endregion

        #region 底栏功能
        // 交换图片命令
        [RelayCommand]
        private void SwapImages()
        {
            // 交换前先取消旧的事件订阅
            LeftImageVM.ImageList.ListChanged -= ImageList_ListChanged;
            RightImageVM.ImageList.ListChanged -= ImageList_ListChanged;

            // 交换 VM
            (LeftImageVM, RightImageVM) = (RightImageVM, LeftImageVM);

            // 交换后重新订阅事件
            LeftImageVM.ImageList.ListChanged += ImageList_ListChanged;
            RightImageVM.ImageList.ListChanged += ImageList_ListChanged;
        }

        // 清空图片命令（修复：重新订阅新 VM 的事件）
        [RelayCommand]
        private void ClearImages()
        {
            LeftImageVM.ImageList.ListChanged -= ImageList_ListChanged;
            RightImageVM.ImageList.ListChanged -= ImageList_ListChanged;

            LeftImageVM = new MainVM();
            RightImageVM = new MainVM();

            LeftImageVM.ImageList.ListChanged += ImageList_ListChanged;
            RightImageVM.ImageList.ListChanged += ImageList_ListChanged;

            AllImageNames.Clear();
            SelectedMergedName = null;
        }
        #endregion

        #region 同步显示

        // ================= 1. 开关属性 =================
        [ObservableProperty]
        private bool _isSyncEnabled = true;

        partial void OnIsSyncEnabledChanged(bool value)
        {
            // 开启同步时，让右边对齐左边
            if (value) RunWithSyncLock(() =>
            {
                RightZoom = LeftZoom;
                RightOffsetX = LeftOffsetX;
                RightOffsetY = LeftOffsetY;
            });
        }

        // ================= 2. 左右独立属性 =================
        [ObservableProperty] private double _leftZoom = 1.0;
        [ObservableProperty] private double _leftOffsetX = 0.0;
        [ObservableProperty] private double _leftOffsetY = 0.0;

        [ObservableProperty] private double _rightZoom = 1.0;
        [ObservableProperty] private double _rightOffsetX = 0.0;
        [ObservableProperty] private double _rightOffsetY = 0.0;

        // ================= 3. 核心同步逻辑 =================
        private bool _isUpdatingSync;

        private void RunWithSyncLock(Action action)
        {
            _isUpdatingSync = true;
            action();
            _isUpdatingSync = false;
        }

        private void Sync(Action syncAction)
        {
            if (IsSyncEnabled && !_isUpdatingSync)
                RunWithSyncLock(syncAction);
        }

        partial void OnLeftZoomChanged(double value) => Sync(() => RightZoom = value);
        partial void OnRightZoomChanged(double value) => Sync(() => LeftZoom = value);

        partial void OnLeftOffsetXChanged(double value) => Sync(() => RightOffsetX = value);
        partial void OnRightOffsetXChanged(double value) => Sync(() => LeftOffsetX = value);

        partial void OnLeftOffsetYChanged(double value) => Sync(() => RightOffsetY = value);
        partial void OnRightOffsetYChanged(double value) => Sync(() => LeftOffsetY = value);

        // ================= 4. 重置指令 =================
        [RelayCommand]
        public void ResetSync()
        {
            RunWithSyncLock(() =>
            {
                RightZoom = LeftZoom = 1.0;
                RightOffsetX = LeftOffsetX = 0.0;
                RightOffsetY = LeftOffsetY = 0.0;
            });
        }

        #endregion

        [ObservableProperty] private bool _isScreenShotEnabled;
        [ObservableProperty] private bool _isDualReViewEnabled;
        [ObservableProperty] private bool _isMenuOpen;
        [ObservableProperty] private GridLength _leftColumnWidth = new(1, GridUnitType.Star);
        [ObservableProperty] private GridLength _rightColumnWidth = new(1, GridUnitType.Star);

        [RelayCommand]
        private void ResetLayout()
        {
            LeftColumnWidth = new GridLength(1, GridUnitType.Star);
            RightColumnWidth = new GridLength(1, GridUnitType.Star);
        }

        [RelayCommand]
        private void ToggleTopDrawer() => IsMenuOpen = !IsMenuOpen;

        [RelayCommand]
        private void ToggleMode()
        {
            IsDualReViewEnabled = !IsDualReViewEnabled;
            if (IsDualReViewEnabled) IsSyncEnabled = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // 取消事件订阅
                    LeftImageVM.ImageList.ListChanged -= ImageList_ListChanged;
                    RightImageVM.ImageList.ListChanged -= ImageList_ListChanged;
                }
                _disposed = true;
            }
        }
    }
}
