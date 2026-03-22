using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace LabelMinusinWPF
{
    public partial class ImageSelectDialog : Window
    {
        // 可选择的图片包装类，实现 INotifyPropertyChanged 以支持 UI 绑定
        public class SelectableImage : INotifyPropertyChanged
        {
            public OneImage Image { get; set; }
            public string ImageName => Image.ImageName;

            private bool _isSelected;
            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    if (_isSelected != value)
                    {
                        _isSelected = value;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                    }
                }
            }

            public SelectableImage(OneImage image, bool isSelected)
            {
                Image = image;
                _isSelected = isSelected;
            }

            public event PropertyChangedEventHandler? PropertyChanged;
        }

        public ObservableCollection<SelectableImage> Items { get; }
        public List<OneImage> SelectedImages { get; private set; }

        public ImageSelectDialog(List<OneImage> availableImages, List<OneImage> currentImages)
        {
            InitializeComponent();

            // 根据已有图片名称构建集合，快速判断是否已被选中
            HashSet<string> currentNames = new(currentImages.Select(img => img.ImageName));
            Items = new ObservableCollection<SelectableImage>(
                availableImages.Select(img => new SelectableImage(img, currentNames.Contains(img.ImageName)))
            );

            ImageListBox.ItemsSource = Items;
            ImageListBox.PreviewMouseLeftButtonDown += ImageListBox_PreviewMouseLeftButtonDown;
            SelectedImages = new List<OneImage>();
        }

        // 点击列表项时切换选中状态
        private void ImageListBox_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var item = ItemsControl.ContainerFromElement(ImageListBox, e.OriginalSource as DependencyObject) as ListBoxItem;
            if (item?.Content is SelectableImage selectableImage)
                selectableImage.IsSelected = !selectableImage.IsSelected;
            e.Handled = true;
        }

        // 全选
        private void SelectAll_Click(object sender, RoutedEventArgs e) => SetAllSelection(true);

        // 取消全选
        private void DeselectAll_Click(object sender, RoutedEventArgs e) => SetAllSelection(false);

        // 批量设置选中状态
        private void SetAllSelection(bool value)
        {
            foreach (var item in Items)
                item.IsSelected = value;
        }

        // 确认选择
        private void OK_Click(object sender, RoutedEventArgs e)
        {
            SelectedImages = Items.Where(item => item.IsSelected).Select(item => item.Image).ToList();
            DialogResult = true;
            Close();
        }

        // 取消选择
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
