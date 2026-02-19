using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControlzEx.Standard;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using SharpCompress.Archives;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using static LabelMinusinWPF.Modules;

namespace LabelMinusinWPF
{
    public partial class ImageReviewVM: ObservableObject
    {
        [ObservableProperty]
        private MainViewModel _leftImageVM = new();
        [ObservableProperty]
        private MainViewModel _rightImageVM = new();

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

            // 封装一个查找逻辑：不管后缀，只看基名是否一致
            Func<ImageInfo, bool> matchCriteria = img =>
                System.IO.Path.GetFileNameWithoutExtension(img.ImageName).Equals(value, StringComparison.OrdinalIgnoreCase);

            // 左右两侧同步选中
            LeftImageVM.SelectedImage = LeftImageVM.ImageList.FirstOrDefault(matchCriteria);
            RightImageVM.SelectedImage = RightImageVM.ImageList.FirstOrDefault(matchCriteria);
        }

        public ImageReviewVM()
        {
            // 监听左右两个列表的变化事件 (BindingList 使用 ListChanged)
            LeftImageVM.ImageList.ListChanged += ImageList_ListChanged;
            RightImageVM.ImageList.ListChanged += ImageList_ListChanged;
        }

        
        private void ImageList_ListChanged(object? sender, ListChangedEventArgs e)// 当任意一边的列表发生变化（加载图片/清空）时，重新计算并集
        {
            var leftNames = LeftImageVM.ImageList.Select(x => System.IO.Path.GetFileNameWithoutExtension(x.ImageName));
            var rightNames = RightImageVM.ImageList.Select(x => System.IO.Path.GetFileNameWithoutExtension(x.ImageName));

            // 取并集 -> 去重 -> 排序
            var union = leftNames.Union(rightNames)
                                 .Distinct()
                                 .OrderBy(n => n) // 建议按名称排序，方便查找
                                 .ToList();

            // 在 UI 线程更新集合
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                AllImageNames.Clear();
                foreach (var name in union)
                {
                    AllImageNames.Add(name);
                }
            });
            if (SelectedMergedName == null && AllImageNames.Count > 0)
            {
                SelectedMergedName = AllImageNames.FirstOrDefault();
            }
        }
        #endregion

        #region 上下切换

        // 上一张
        [RelayCommand(CanExecute = nameof(CanGoToPrevious))]
        private void PreviousImage()
        {
            if (string.IsNullOrEmpty(SelectedMergedName)) return;

            int currentIndex = AllImageNames.IndexOf(SelectedMergedName);
            if (currentIndex > 0)
            {
                SelectedMergedName = AllImageNames[currentIndex - 1];
            }
        }

        private bool CanGoToPrevious()
            => AllImageNames.Count > 0 && !string.IsNullOrEmpty(SelectedMergedName) && AllImageNames.IndexOf(SelectedMergedName) > 0;

        // 下一张
        [RelayCommand(CanExecute = nameof(CanGoToNext))]
        private void NextImage()
        {
            if (string.IsNullOrEmpty(SelectedMergedName)) return;

            int currentIndex = AllImageNames.IndexOf(SelectedMergedName);
            if (currentIndex < AllImageNames.Count - 1)
            {
                SelectedMergedName = AllImageNames[currentIndex + 1];
            }
        }

        private bool CanGoToNext()
            => AllImageNames.Count > 0 && !string.IsNullOrEmpty(SelectedMergedName) && AllImageNames.IndexOf(SelectedMergedName) < AllImageNames.Count - 1;

        #endregion

        #region 底栏功能
        // 交换图片命令
        [RelayCommand]
        private void SwapImages()
        {
            (LeftImageVM, RightImageVM) = (RightImageVM, LeftImageVM);
        }

        // 清空图片命令
        [RelayCommand]
        private void ClearImages()
        {
            LeftImageVM = new MainViewModel();
            RightImageVM = new MainViewModel();
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

        // ================= 3. 核心同步逻辑 (极简版) =================
        private bool _isUpdatingSync = false;

        // 提取的通用锁机制：安全执行代码而不触发循环同步
        private void RunWithSyncLock(Action action)
        {
            _isUpdatingSync = true;
            action();
            _isUpdatingSync = false;
        }

        // 提取的条件同步器
        private void Sync(Action syncAction)
        {
            if (IsSyncEnabled && !_isUpdatingSync)
            {
                RunWithSyncLock(syncAction);
            }
        }

        // 利用一行代码完成所有同步
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



        [ObservableProperty] private bool _isScreenShotEnabled = false;
    }
}
