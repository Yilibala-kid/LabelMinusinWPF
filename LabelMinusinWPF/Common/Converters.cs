using System;
using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace LabelMinusinWPF.Common
{
    public class EnumToBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value?.Equals(parameter) ?? false;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => (bool)value! ? parameter : Binding.DoNothing;
    }

    public class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
                return boolValue ? Visibility.Collapsed : Visibility.Visible;
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
                return visibility != Visibility.Visible;
            return false;
        }
    }

    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && !b;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && !b;
    }

    public class IsNullOrEmptyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isNullOrEmpty = value == null || (value is string str && string.IsNullOrEmpty(str));
            if (parameter is string p && p == "Disable")
                return !isNullOrEmpty;
            return isNullOrEmpty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public class IsNewLabelConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2) return false;
            bool isReviewMode = values.Length < 3 || values[0] is bool review && review;
            int offset = values.Length >= 3 ? 1 : 0;
            bool isDeleted = values[offset] is bool b && b;
            string originalText = values[offset + 1] as string ?? "";
            if (!isReviewMode) return false;
            return !isDeleted && string.IsNullOrEmpty(originalText);
        }
        public object[]? ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => null;
    }

    public class IsModifiedLabelConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 3) return false;
            bool isReviewMode = values.Length < 4 || values[0] is bool review && review;
            int offset = values.Length >= 4 ? 1 : 0;
            bool isDeleted = values[offset] is bool b && b;
            string text = values[offset + 1] as string ?? "";
            string originalText = values[offset + 2] as string ?? "";
            if (!isReviewMode) return false;
            return !isDeleted && !string.IsNullOrEmpty(originalText) && text != originalText;
        }
        public object[]? ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => null;
    }

    public class IndexPlusOneConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is int i ? i + 1 : 0;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    public class LabelEditPanelIndexConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 0 || values[0] is not OneLabel label)
                return "*";

            if (label.IsDeleted || values.Length > 1 && values[1] is bool isDeleted && isDeleted)
                return "-";

            if (values.Length > 2 && values[2] is IEnumerable activeLabels)
            {
                int index = 1;
                foreach (var activeLabel in activeLabels)
                {
                    if (ReferenceEquals(activeLabel, label))
                        return index.ToString(culture);
                    index++;
                }
            }

            return "*";
        }

        public object[]? ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => null;
    }

    public class SelectedLabelIndexConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not OneLabel label) return "#";
            if (label.IsDeleted) return "-";

            var vm = Application.Current.MainWindow?.DataContext as OneProject;
            if (vm?.SelectedImage?.ActiveLabelsView is IEnumerable activeLabels)
            {
                int index = 1;
                foreach (var activeLabel in activeLabels)
                {
                    if (ReferenceEquals(activeLabel, label))
                        return index.ToString(culture);

                    index++;
                }
            }

            return "*";
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    public class LabelTextVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            bool isMouseOver = values.Length > 0 && values[0] is bool b && b;
            bool isSelected = values.Length > 2 && ReferenceEquals(values[1], values[2]);
            return isMouseOver || isSelected ? Visibility.Visible : Visibility.Collapsed;
        }

        public object[]? ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => null;
    }

    public class LabelReferenceVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            bool isSame = values.Length > 1 && ReferenceEquals(values[0], values[1]);
            if (parameter is string p && p.Equals("Inverse", StringComparison.OrdinalIgnoreCase))
                isSame = !isSame;

            return isSame ? Visibility.Visible : Visibility.Collapsed;
        }

        public object[]? ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => null;
    }

    public class AnyTrueConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
            => values.Any(v => v is true);

        public object[]? ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => null;
    }

    public class ImageRelativePositionConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values[0] is not double relative || values[1] is not Image img || img.Source == null)
                return 0.0;
            bool isX = (string)parameter == "X";
            return relative * (isX ? img.ActualWidth : img.ActualHeight);
        }
        public object[]? ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => null;
    }

    public class LabelsSourceConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2) return null!;
            bool isReviewMode = values[0] is bool b && b;
            var image = values[1] as OneImage;
            if (image == null) return null!;
            return isReviewMode ? image.Labels : image.ActiveLabelsView;
        }

        public object[]? ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => null;
    }

}
