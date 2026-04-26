using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace LabelMinusinWPF.SelfControls
{
    public partial class LabelEditPanel : UserControl
    {
        #region Ä£Ê½ÇÐ»»
        public static readonly DependencyProperty IsReviewModeProperty =
            DependencyProperty.Register(nameof(IsReviewMode), typeof(bool), typeof(LabelEditPanel),
                new PropertyMetadata(false, OnIsReviewModeChanged));

        public static readonly DependencyProperty IsListVisibleProperty =
            DependencyProperty.Register(nameof(IsListVisible), typeof(bool), typeof(LabelEditPanel),
                new PropertyMetadata(true, OnIsListVisibleChanged));

        public static readonly DependencyProperty IsTextBoxVisibleProperty =
            DependencyProperty.Register(nameof(IsTextBoxVisible), typeof(bool), typeof(LabelEditPanel),
                new PropertyMetadata(true, OnIsTextBoxVisibleChanged));

        public bool IsReviewMode
        {
            get => (bool)GetValue(IsReviewModeProperty);
            set => SetValue(IsReviewModeProperty, value);
        }

        public bool IsListVisible
        {
            get => (bool)GetValue(IsListVisibleProperty);
            set => SetValue(IsListVisibleProperty, value);
        }

        public bool IsTextBoxVisible
        {
            get => (bool)GetValue(IsTextBoxVisibleProperty);
            set => SetValue(IsTextBoxVisibleProperty, value);
        }
        private static void OnIsReviewModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var panel = (LabelEditPanel)d;
            bool isReview = (bool)e.NewValue;

            panel.OriginalTextColumn.Visibility = isReview ? Visibility.Visible : Visibility.Collapsed;
            panel.ReviewBottomPanel.Visibility = isReview ? Visibility.Visible : Visibility.Collapsed;

            panel.RefreshItemsSource();
        }

        private static void OnIsListVisibleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var panel = (LabelEditPanel)d;
            bool isVisible = (bool)e.NewValue;

            panel.ListRow.Height = isVisible
                ? new GridLength(4, GridUnitType.Star)
                : new GridLength(0);
            panel.SplitterRow.Height = isVisible
                ? new GridLength(5)
                : new GridLength(0);
        }

        private static void OnIsTextBoxVisibleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var panel = (LabelEditPanel)d;
            panel.TextRow.Height = (bool)e.NewValue
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(0);
        }
        #endregion



        private OneProject? _project;
        private OneImage? _image;

        public LabelEditPanel()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_project != null)
                _project.PropertyChanged -= OnProjectPropertyChanged;

            DetachFromCurrentImage();

            _project = e.NewValue as OneProject;

            if (_project != null)
                _project.PropertyChanged += OnProjectPropertyChanged;

            AttachToImage(_project?.SelectedImage);
            RefreshItemsSource();
        }

        private void OnProjectPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(OneProject.SelectedImage))
            {
                DetachFromCurrentImage();
                AttachToImage(_project?.SelectedImage);
                RefreshItemsSource();
            }
        }

        private void OnImagePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(OneImage.ActiveLabels))
                Dispatcher.Invoke(RefreshItemsSource);
        }

        private void AttachToImage(OneImage? image)
        {
            _image = image;
            if (_image != null)
                _image.PropertyChanged += OnImagePropertyChanged;
        }

        private void DetachFromCurrentImage()
        {
            if (_image != null)
            {
                _image.PropertyChanged -= OnImagePropertyChanged;
                _image = null;
            }
        }

        private void RefreshItemsSource()
        {
            if (DataContext is not OneProject vm || vm.SelectedImage == null)
            {
                MainDataGrid.ItemsSource = null;
                return;
            }

            MainDataGrid.ItemsSource = IsReviewMode
                ? vm.SelectedImage.Labels
                : vm.SelectedImage.ActiveLabels;
        }


    }
}
