using LabelMinusinWPF.SelfControls;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Windows;

namespace LabelMinusinWPF
{
    public partial class App : Application
    {
        private const string UniqueEventName = "LabelMinus_Unique_Instance_Mutex";
        private Mutex? _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            _mutex = new Mutex(true, UniqueEventName, out bool isFirstInstance);

            if (!isFirstInstance)
            {
                SendArgsToRunningInstance(e.Args);
                Current.Shutdown();
                return;
            }

            base.OnStartup(e);

            MainWindow mainWindow = new();
            mainWindow.Show();

            HandleArgs(mainWindow, e.Args);
            StartPipeServer(mainWindow);
        }

        private void SendArgsToRunningInstance(string[] args)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", "LabelMinusPipe", PipeDirection.Out);
                client.Connect(500);
                using var writer = new StreamWriter(client);
                writer.WriteLine(string.Join("|", args));
                writer.Flush();
            }
            catch { /* 处理连接失败 */ }
        }

        private async void StartPipeServer(MainWindow mainWindow)
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream("LabelMinusPipe", PipeDirection.In);
                    await server.WaitForConnectionAsync();
                    using var reader = new StreamReader(server);
                    var message = await reader.ReadLineAsync();

                    if (!string.IsNullOrEmpty(message))
                    {
                        string[] args = message.Split('|');
                        mainWindow.Dispatcher.Invoke(() =>
                        {
                            HandleArgs(mainWindow, args);
                            if (mainWindow.WindowState == WindowState.Minimized)
                                mainWindow.WindowState = WindowState.Normal;
                            mainWindow.Activate();
                            mainWindow.Topmost = true;
                            mainWindow.Topmost = false;
                        });
                    }
                }
                catch { /* 处理异常 */ }
            }
        }

        private void HandleArgs(MainWindow mainWindow, string[] args)
        {
            if (args.Length == 0) return;

            var paths = args.Where(arg => arg != "--review")
                            .Select(arg => arg.Trim('\"'))
                            .Where(p => File.Exists(p) || Directory.Exists(p))
                            .ToArray();

            bool isReviewMode = args.Contains("--review");

            mainWindow.Dispatcher.BeginInvoke(() =>
            {
                if (isReviewMode)
                    foreach (var path in paths)
                        mainWindow.OpenImageReviewSmart(path);
                else if (paths.Length > 0)
                    mainWindow.OpenFilesOnStartup(paths);
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }
}
