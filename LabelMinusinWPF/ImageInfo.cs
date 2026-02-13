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

namespace LabelMinusinWPF
{
    public partial class ImageInfo : ObservableObject
    {
        // 图片完整路径
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ImageSource), nameof(ImageName))]
        private string _imagePath = string.Empty;
        // 图片名
        public string ImageName => string.IsNullOrEmpty(ImagePath)
                                    ? "未命名"
                                    : System.IO.Path.GetFileName(ImagePath);//System.IO.Path.GetFileNameWithoutExtension(ImagePath)
        // 图片包含的标签
        public BindingList<ImageLabel> Labels { get; } = [];
        // 只读属性：获取未删除的标签列表
        public BindingList<ImageLabel> ActiveLabels => [.. Labels.Where(l => !l.IsDeleted)];
        // 当前选中的标注
        [ObservableProperty] private ImageLabel? _selectedLabel;
        // 图片显示
        public ImageSource? ImageSource
        {
            get
            {
                if (string.IsNullOrEmpty(ImagePath) || !File.Exists(ImagePath)) return null;

                try
                {
                    // 使用 OnLoad 确保不占用文件，这样你在标注时还能去资源管理器改文件名
                    BitmapImage bitmap = new();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(ImagePath);
                    bitmap.EndInit();
                    bitmap.Freeze(); // 冻结对象，提高 UI 渲染性能并允许跨线程
                    return bitmap;
                }
                catch
                {
                    return null;
                }
            }
        }

        private bool _isRefreshing = false;

        public ImageInfo()
        {
            // 订阅 BindingList 的 ListChanged 事件
            Labels.ListChanged += (s, e) =>
            {
                // 当列表发生增删改操作时，刷新索引
                if (!_isRefreshing) RefreshIndices();
            };
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
        public ICommand DeleteLabelCommand => new RelayCommand<ImageLabel>(label =>
        {
            if (label == null) return;
            label.IsDeleted = true;
            RefreshIndices(); // 刷新索引逻辑
        });

        public ICommand SelectLabelCommand => new RelayCommand<ImageLabel>(label =>
        {
            SelectedLabel = label;
        });
        //public void ResetModificationFlags()// 保存后重置所有修改标记
        //{
        //    foreach (var l in Labels) l._isModified = false;
        //}
        #endregion

    }
}