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
        public ObservableCollection<SelectableImage> Items { get; }
        public List<OneImage> SelectedImages { get; private set; }

        public ImageSelectDialog(
            List<OneImage> availableImages,
            List<OneImage> currentImages,
            string title = "选择图片",
            string description = "请选择要包含在翻译中的图片。")
        {
            InitializeComponent();

            Title = title;
            DialogHeader.Header = title;
            DialogHeader.Description = description;

            HashSet<string> currentNames = new(currentImages.Select(img => img.ImageName));
            Items = new ObservableCollection<SelectableImage>(
                availableImages.Select(img => new SelectableImage(img, currentNames.Contains(img.ImageName))));

            ImageListBox.ItemsSource = Items;
            ImageListBox.PreviewMouseLeftButtonDown += ImageListBox_PreviewMouseLeftButtonDown;
            SelectedImages = new List<OneImage>();
        }

        private void ImageListBox_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var item = ItemsControl.ContainerFromElement(ImageListBox, e.OriginalSource as DependencyObject) as ListBoxItem;
            if (item?.Content is SelectableImage selectableImage)
                selectableImage.IsSelected = !selectableImage.IsSelected;
            e.Handled = true;
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
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        public class SelectableImage : INotifyPropertyChanged
        {
            private bool _isSelected;

            public SelectableImage(OneImage image, bool isSelected)
            {
                Image = image;
                _isSelected = isSelected;
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            public OneImage Image { get; }
            public string ImageName => Image.ImageName;

            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    if (_isSelected == value) return;

                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }
        }
    }
}
