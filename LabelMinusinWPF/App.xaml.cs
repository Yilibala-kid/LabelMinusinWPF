using LabelMinusinWPF.SelfControls;
using System.Configuration;
using System.Data;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Windows;

namespace LabelMinusinWPF
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        // 互斥体名称，建议使用唯一的 GUID
        private const string UniqueEventName = "LabelMinus_Unique_Instance_Mutex";
        private Mutex? _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            _mutex = new Mutex(true, UniqueEventName, out bool isFirstInstance);

            if (!isFirstInstance)
            {
                // --- 已经有实例在运行了：发送参数给它 ---
                SendArgsToRunningInstance(e.Args);
                Current.Shutdown(); // 退出当前实例
                return;
            }

            // --- 它是第一个实例：正常启动并启动监听器 ---
            base.OnStartup(e);
            LabelStyleManager.Instance.LoadSettings();

            var mainWindow = new MainWindow();
            mainWindow.Show();

            // 处理第一个实例自带的启动参数
            HandleArgs(mainWindow, e.Args);

            // 启动后台线程监听后续打开的文件请求
            StartPipeServer(mainWindow);
        }

        private void SendArgsToRunningInstance(string[] args)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", "LabelMinusPipe", PipeDirection.Out);
                client.Connect(500); // 500ms 超时
                using var writer = new StreamWriter(client);
                // 将所有参数拼接成一行，用特定字符分隔（或转为 JSON）
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
                        // 回到 UI 线程处理新路径
                        mainWindow.Dispatcher.Invoke(() =>
                        {
                            HandleArgs(mainWindow, args);
                            // 顺便把窗口拉到最前面
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

        // 统筹参数处理逻辑
        private void HandleArgs(MainWindow mainWindow, string[] args)
        {
            if (args.Length == 0) return;

            // 提取有效路径
            var paths = args.Where(arg => arg != "--review")
                            .Select(arg => arg.Trim('\"'))
                            .Where(p => File.Exists(p) || Directory.Exists(p))
                            .ToArray();

            bool isReviewMode = args.Contains("--review");

            mainWindow.Dispatcher.BeginInvoke(() =>
            {
                if (isReviewMode)
                {
                    // 核心逻辑：如果是图校模式，逐个处理路径
                    foreach (var path in paths)
                    {
                        // 调用上面定义的智能填充方法
                        mainWindow.OpenImageReviewSmart(path);
                    }
                }
                else if (paths.Length > 0)
                {
                    // 普通模式：直接打开
                    mainWindow.OpenFilesOnStartup(paths);
                }
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }
}
