using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LabelMinusinWPF.Common
{
    /// <summary>
    /// 枚举转布尔值转换器（用于 RadioButton 等控件绑定枚举）
    /// </summary>
    public class EnumToBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value?.Equals(parameter) ?? false;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => (bool)value! ? parameter : Binding.DoNothing;
    }

    /// <summary>
    /// 反向布尔值到可见性转换器
    /// </summary>
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
}
