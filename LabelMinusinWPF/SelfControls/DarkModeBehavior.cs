using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using MaterialDesignThemes.Wpf;
using static MaterialDesignThemes.Wpf.Theme;

namespace LabelMinusinWPF.SelfControls
{
    public static class DarkModeBehavior
    {
        private static bool _currentIsDarkMode;

        public static readonly DependencyProperty IsDarkModeProperty =
            DependencyProperty.RegisterAttached("IsDarkMode", typeof(bool), typeof(DarkModeBehavior),
                new PropertyMetadata(false, OnDarkModeChanged));

        public static bool GetIsDarkMode(DependencyObject obj) => (bool)obj.GetValue(IsDarkModeProperty);
        public static void SetIsDarkMode(DependencyObject obj, bool value) => obj.SetValue(IsDarkModeProperty, value);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        private static void OnDarkModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            bool isDark = (bool)e.NewValue;
            _currentIsDarkMode = isDark;

            var paletteHelper = new PaletteHelper();
            var theme = paletteHelper.GetTheme();
            theme.SetBaseTheme(isDark ? BaseTheme.Dark : BaseTheme.Light);
            paletteHelper.SetTheme(theme);

            foreach (Window window in Application.Current.Windows)
            {
                ApplyWindowChrome(window, isDark);
            }
        }

        public static void ApplyCurrentTheme(Window window)
        {
            if (window == null)
                return;

            ApplyWindowChrome(window, _currentIsDarkMode);
        }

        private static void ApplyWindowChrome(Window window, bool isDark)
        {
            IntPtr hWnd = new WindowInteropHelper(window).Handle;
            if (hWnd == IntPtr.Zero)
                return;

            int darkMode = isDark ? 1 : 0;
            DwmSetWindowAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
        }
    }
}
