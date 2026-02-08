using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LabelMinusinWPF
{
    public class ImageInfo : ViewModelBase
    {
        #region 1. 私有字段与状态锁
        private string _imageName = string.Empty;
        private ImageLabel? _selectedLabel;
        private bool _isRefreshing = false;
        #endregion

        #region 图片显示
        private string _fullPath = string.Empty;
        public string FullPath
        {
            get => _fullPath;
            set => SetProperty(ref _fullPath, value);
        }

        // 供 XAML 绑定的属性
        public ImageSource? ImageSource
        {
            get
            {
                if (string.IsNullOrEmpty(FullPath) || !File.Exists(FullPath)) return null;

                try
                {
                    // 使用 OnLoad 确保不占用文件，这样你在标注时还能去资源管理器改文件名
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(FullPath);
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
        #endregion


        #region 图片下的标签
        #region 数据集合与选中状态
        /// <summary>
        /// 当前图片下的所有标注
        /// </summary>
        public BindingList<ImageLabel> Labels { get; } = [];

        /// <summary>
        /// 当前选中的标注（解决你关心的 Current 绑定问题）
        /// </summary>
        public ImageLabel? SelectedLabel
        {
            get => _selectedLabel;
            set => SetProperty(ref _selectedLabel, value);
        }

        /// <summary>
        /// 只读属性：获取未删除的标签列表
        /// </summary>
        [Browsable(false)]
        public List<ImageLabel> ActiveLabels => [.. Labels.Where(l => !l.IsDeleted)];
        #endregion

        #region 基础属性
        public string ImageName
        {
            get => _imageName;
            set => SetProperty(ref _imageName, value);
        }
        #endregion

        #region 4. 初始化与事件监听
        public ImageInfo()
        {
            InitializeEvents();
        }

        private void InitializeEvents()
        {
            // 监听集合变化，自动维护序号逻辑
            Labels.ListChanged += (s, e) =>
            {
                if (_isRefreshing) return;

                // 只有在增删、移动位置时才需要重排索引
                if (e.ListChangedType is ListChangedType.ItemAdded
                                     or ListChangedType.ItemDeleted
                                     or ListChangedType.ItemMoved)
                {
                    RefreshIndices();
                }
            };
        }
        #endregion

        #region 5. 业务逻辑方法
        /// <summary>
        /// 重新计算所有标签的逻辑序号
        /// </summary>
        public void RefreshIndices()
        {
            _isRefreshing = true;
            Labels.RaiseListChangedEvents = false;

            try
            {
                int nextIndex = 1;

                // 1. 处理活跃标签（按当前 Index 排序后重排连续序号）
                var activeGroup = Labels.Where(l => !l.IsDeleted).OrderBy(l => l.Index);
                foreach (var lbl in activeGroup)
                {
                    lbl.Index = nextIndex++;
                }

                // 2. 处理已删除标签（排在最后，保持导出顺序）
                var deletedGroup = Labels.Where(l => l.IsDeleted);
                foreach (var lbl in deletedGroup)
                {
                    lbl.Index = nextIndex++;
                }
            }
            finally
            {
                // 使用 finally 确保即便出错也能恢复事件抛出
                Labels.RaiseListChangedEvents = true;
                Labels.ResetBindings(); // 通知 UI 整体刷新
                _isRefreshing = false;
            }
        }

        /// <summary>
        /// 保存后重置所有修改标记
        /// </summary>
        public void ResetModificationFlags()
        {
            foreach (var l in Labels) l.IsModified = false;
        }
        #endregion
        #endregion
    }
}