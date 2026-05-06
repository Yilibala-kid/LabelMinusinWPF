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

        [ObservableProperty]
        private bool _isFuzzyMatchEnabled = true;

        partial void OnIsFuzzyMatchEnabledChanged(bool value)
        {
            ImageList_ListChanged(null, null!);
        }

        partial void OnSelectedMergedNameChanged(string? value)
        {
            if (string.IsNullOrEmpty(value)) return;
            LeftImageVM.SelectedImage = FindBestMatch(LeftImageVM.ImageList, value);
            RightImageVM.SelectedImage = FindBestMatch(RightImageVM.ImageList, value);
        }

        public CompareImgVM()
        {
            LeftImageVM.ImageList.ListChanged += ImageList_ListChanged;
            RightImageVM.ImageList.ListChanged += ImageList_ListChanged;
        }

        private void ImageList_ListChanged(object? sender, ListChangedEventArgs e)
        {
            var union = BuildUnionList();

            Application.Current.Dispatcher.Invoke(() =>
            {
                AllImageNames.Clear();
                union.ForEach(AllImageNames.Add);
                if (SelectedMergedName == null && AllImageNames.Count > 0)
                    SelectedMergedName = AllImageNames.First();
            });
        }

        private List<string> BuildUnionList()
        {
            if (IsFuzzyMatchEnabled)
            {
                var keyToDisplay = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var img in LeftImageVM.ImageList)
                {
                    var key = FileNameNormalizer.Normalize(Path.GetFileNameWithoutExtension(img.ImageName));
                    keyToDisplay.TryAdd(key, Path.GetFileNameWithoutExtension(img.ImageName));
                }
                foreach (var img in RightImageVM.ImageList)
                {
                    var key = FileNameNormalizer.Normalize(Path.GetFileNameWithoutExtension(img.ImageName));
                    keyToDisplay.TryAdd(key, Path.GetFileNameWithoutExtension(img.ImageName));
                }
                return keyToDisplay.Values.OrderBy(n => n).ToList();
            }

            return LeftImageVM.ImageList.Select(x => Path.GetFileNameWithoutExtension(x.ImageName))
                .Union(RightImageVM.ImageList.Select(x => Path.GetFileNameWithoutExtension(x.ImageName)))
                .OrderBy(n => n)
                .ToList();
        }

        private OneImage? FindBestMatch(BindingList<OneImage> imageList, string selectedName)
        {
            if (IsFuzzyMatchEnabled)
            {
                var selectedKey = FileNameNormalizer.Normalize(selectedName);
                return imageList.FirstOrDefault(img =>
                    FileNameNormalizer.Normalize(Path.GetFileNameWithoutExtension(img.ImageName)) == selectedKey);
            }

            return imageList.FirstOrDefault(img =>
                Path.GetFileNameWithoutExtension(img.ImageName).Equals(selectedName, StringComparison.OrdinalIgnoreCase));
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

    }
}
