using LabelMinusinWPF.Common;
using LabelMinusinWPF.SelfControls;
using System.Windows;

namespace LabelMinusinWPF
{
    public partial class App : Application
    {
        private RightClickOpenService? _service;

        protected override void OnStartup(StartupEventArgs e)
        {
            AppSettingsService.SetStartupArgs(e.Args);
            AppSettingsService.Load();
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnWindowLoaded));

            _service = RightClickOpenService.Create(e.Args);
            if (_service == null)
            {
                // 第二实例：参数已转发给主实例
                Current.Shutdown();
                return;
            }

            base.OnStartup(e);
            MainWindow mainWindow = new();
            mainWindow.Show();
            _service.Initialize(mainWindow, e.Args);
        }

        private static void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is Window window)
                DarkModeBehavior.ApplyCurrentTheme(window);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            AppSettingsService.Save();
            _service?.Dispose();
            base.OnExit(e);
        }
    }
}
