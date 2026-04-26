using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelMinusinWPF.Common;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;

namespace LabelMinusinWPF
{
    public partial class CompareImgVM : ObservableObject
    {
        [ObservableProperty]
        private OneProject _leftImageVM = new();

        [ObservableProperty]
        private OneProject _rightImageVM = new();

        #region combobox绑定
        public ObservableCollection<string> AllImageNames { get; } = [];

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(PreviousImageCommand))]
        [NotifyCanExecuteChangedFor(nameof(NextImageCommand))]
        private string? _selectedMergedName;

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
            LeftImageVM.ImageList.ListChanged += ImageList_ListChanged;
            RightImageVM.ImageList.ListChanged += ImageList_ListChanged;
        }

        private void ImageList_ListChanged(object? sender, ListChangedEventArgs e)
        {
            var union = LeftImageVM.ImageList.Select(x => Path.GetFileNameWithoutExtension(x.ImageName))
                .Union(RightImageVM.ImageList.Select(x => Path.GetFileNameWithoutExtension(x.ImageName)))
                .OrderBy(n => n)
                .ToList();

            Application.Current.Dispatcher.Invoke(() =>
            {
                AllImageNames.Clear();
                union.ForEach(AllImageNames.Add);
                if (SelectedMergedName == null && AllImageNames.Count > 0)
                    SelectedMergedName = AllImageNames.First();
            });
        }
        #endregion

        #region 上下切换
        [RelayCommand(CanExecute = nameof(CanGoToPrevious))]
        private void PreviousImage()
        {
            int idx = NavigationHelper.NavigateIndex(AllImageNames.IndexOf(SelectedMergedName!), AllImageNames.Count, forward: false);
            if (idx >= 0) SelectedMergedName = AllImageNames[idx];
        }
        private bool CanGoToPrevious() => NavigationHelper.NavigateIndex(AllImageNames.IndexOf(SelectedMergedName!), AllImageNames.Count, forward: false) >= 0;

        [RelayCommand(CanExecute = nameof(CanGoToNext))]
        private void NextImage()
        {
            int idx = NavigationHelper.NavigateIndex(AllImageNames.IndexOf(SelectedMergedName!), AllImageNames.Count, forward: true);
            if (idx >= 0) SelectedMergedName = AllImageNames[idx];
        }
        private bool CanGoToNext() => NavigationHelper.NavigateIndex(AllImageNames.IndexOf(SelectedMergedName!), AllImageNames.Count, forward: true) >= 0;
        #endregion

        #region 底栏功能
        [RelayCommand]
        private void SwapImages()
        {
            LeftImageVM.ImageList.ListChanged -= ImageList_ListChanged;
            RightImageVM.ImageList.ListChanged -= ImageList_ListChanged;
            (LeftImageVM, RightImageVM) = (RightImageVM, LeftImageVM);
            LeftImageVM.ImageList.ListChanged += ImageList_ListChanged;
            RightImageVM.ImageList.ListChanged += ImageList_ListChanged;
        }

        [RelayCommand]
        private void ClearImages()
        {
            LeftImageVM.ImageList.ListChanged -= ImageList_ListChanged;
            RightImageVM.ImageList.ListChanged -= ImageList_ListChanged;
            LeftImageVM = new();
            RightImageVM = new();
            LeftImageVM.ImageList.ListChanged += ImageList_ListChanged;
            RightImageVM.ImageList.ListChanged += ImageList_ListChanged;
            AllImageNames.Clear();
            SelectedMergedName = null;
        }
        #endregion

        #region 同步显示
        [ObservableProperty]
        private bool _isSyncEnabled = true;

        partial void OnIsSyncEnabledChanged(bool value)
        {
            if (value) RunWithSyncLock(() => { RightZoom = LeftZoom; RightOffsetX = LeftOffsetX; RightOffsetY = LeftOffsetY; });
        }

        [ObservableProperty]
        private double _leftZoom = 1.0;

        [ObservableProperty]
        private double _leftOffsetX = 0.0;

        [ObservableProperty]
        private double _leftOffsetY = 0.0;

        [ObservableProperty]
        private double _rightZoom = 1.0;

        [ObservableProperty]
        private double _rightOffsetX = 0.0;

        [ObservableProperty]
        private double _rightOffsetY = 0.0;

        private bool _isUpdatingSync;

        private void RunWithSyncLock(Action action)
        {
            _isUpdatingSync = true;
            action();
            _isUpdatingSync = false;
        }

        private void Sync(Action syncAction)
        {
            if (IsSyncEnabled && !_isUpdatingSync) RunWithSyncLock(syncAction);
        }

        partial void OnLeftZoomChanged(double value) => Sync(() => RightZoom = value);
        partial void OnRightZoomChanged(double value) => Sync(() => LeftZoom = value);
        partial void OnLeftOffsetXChanged(double value) => Sync(() => RightOffsetX = value);
        partial void OnRightOffsetXChanged(double value) => Sync(() => LeftOffsetX = value);
        partial void OnLeftOffsetYChanged(double value) => Sync(() => RightOffsetY = value);
        partial void OnRightOffsetYChanged(double value) => Sync(() => LeftOffsetY = value);

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

        [ObservableProperty]
        private bool _isScreenShotEnabled;

        [ObservableProperty]
        private bool _isDualReViewEnabled;

        [ObservableProperty]
        private bool _isMenuOpen;

        [ObservableProperty]
        private GridLength _leftColumnWidth = new(1, GridUnitType.Star);

        [ObservableProperty]
        private GridLength _rightColumnWidth = new(1, GridUnitType.Star);

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
    }
}
