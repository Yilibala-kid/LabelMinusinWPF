using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Data;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LabelMinusinWPF.Common;

namespace LabelMinusinWPF;

// 图片模型类，包含标签集合和图片信息
public partial class OneImage : ObservableObject
{
    public OneImage()
    {
        ActiveLabelsView = new CollectionViewSource { Source = Labels }.View;
        ActiveLabelsView.Filter = item => item is OneLabel label && !label.IsDeleted;

        if (ActiveLabelsView is ICollectionViewLiveShaping liveView && liveView.CanChangeLiveFiltering)
        {
            liveView.LiveFilteringProperties.Add(nameof(OneLabel.IsDeleted));
            liveView.IsLiveFiltering = true;
        }

        if (ActiveLabelsView is INotifyCollectionChanged activeLabelsChanged)
            activeLabelsChanged.CollectionChanged += ActiveLabelsView_CollectionChanged;
    }

    private void ActiveLabelsView_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ActiveLabelsView));
        OnPropertyChanged(nameof(SelectedLabel));
    }

    #region 图片自身属性

    // 图片路径
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayImage), nameof(ImageName))]
    private string _imagePath = string.Empty;

    // 图片名称（从路径提取）
    public string ImageName => Path.GetFileName(ImagePath);

    // 用于显示的图片源（自动从压缩包或文件加载）
    public ImageSource? DisplayImage => GetImageSource();
    #endregion

    #region 携带标签
    public ObservableCollection<OneLabel> Labels { get; } = [];// 标签列表
    [ObservableProperty]
    private OneLabel? _selectedLabel;// 当前选中的标签
    public ICollectionView ActiveLabelsView { get; }// 活跃标签视图（排除已删除的）

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

}
