using System.Windows;
using System.Windows.Controls;

namespace LabelMinusinWPF.SelfControls
{
    public partial class LabelEditPanel : UserControl
    {
        #region 模式切换
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

        public LabelEditPanel()
        {
            InitializeComponent();
        }
    }
}