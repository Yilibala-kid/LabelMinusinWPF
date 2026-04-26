using LabelMinusinWPF.Common;
using System.Windows;

namespace LabelMinusinWPF
{
    public partial class App : Application
    {
        private RightClickOpenService? _service;

        protected override void OnStartup(StartupEventArgs e)
        {
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

        protected override void OnExit(ExitEventArgs e)
        {
            _service?.Dispose();
            base.OnExit(e);
        }
    }
}
