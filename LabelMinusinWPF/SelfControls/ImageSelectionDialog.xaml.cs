using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace LabelMinusinWPF
{
    public partial class ImageSelectionDialog : Window
    {
        public class SelectableImage : INotifyPropertyChanged
        {
            public ImageInfo Image { get; set; }
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

            public event PropertyChangedEventHandler? PropertyChanged;

            public SelectableImage(ImageInfo image, bool isSelected)
            {
                Image = image;
                _isSelected = isSelected;
            }
        }

        public ObservableCollection<SelectableImage> Items { get; }
        public List<ImageInfo> SelectedImages { get; private set; }

        public ImageSelectionDialog(List<ImageInfo> availableImages, List<ImageInfo> currentImages)
        {
            InitializeComponent();

            var currentNames = new HashSet<string>(currentImages.Select(img => img.ImageName));
            Items = new ObservableCollection<SelectableImage>(
                availableImages.Select(img => new SelectableImage(img, currentNames.Contains(img.ImageName)))
            );

            ImageListBox.ItemsSource = Items;
            ImageListBox.PreviewMouseLeftButtonDown += ImageListBox_PreviewMouseLeftButtonDown;
            SelectedImages = new();
        }

        private void ImageListBox_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var item = ItemsControl.ContainerFromElement(ImageListBox, e.OriginalSource as DependencyObject) as ListBoxItem;
            if (item?.Content is SelectableImage selectableImage)
            {
                selectableImage.IsSelected = !selectableImage.IsSelected;
                e.Handled = true;
            }
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e) => SetAllSelection(true);

        private void DeselectAll_Click(object sender, RoutedEventArgs e) => SetAllSelection(false);

        private void SetAllSelection(bool value)
        {
            foreach (var item in Items)
                item.IsSelected = value;
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            SelectedImages = Items.Where(item => item.IsSelected).Select(item => item.Image).ToList();
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
