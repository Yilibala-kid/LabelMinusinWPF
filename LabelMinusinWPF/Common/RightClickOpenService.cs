using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace LabelMinusinWPF.Common
{
    /// <summary>
    /// 右键打开功能：注册表上下文菜单 + 单实例 IPC + CLI 参数解析 + 文件打开调度
    /// </summary>
    public class RightClickOpenService : IDisposable
    {
        private const string MutexName = "LabelMinus_Unique_Instance_Mutex";
        private const string PipeName = "LabelMinusPipe";
        private const string OpenKey = "LabelMinus.Open";
        private const string ReviewKey = "LabelMinus.Review";
        private const string BasePath = @"Software\Classes";
        private static readonly TimeSpan ReviewOpenBatchDelay = TimeSpan.FromMilliseconds(350);
        private static readonly string[] TargetExtensions = [".txt", ".zip", ".rar", ".7z"];

        private readonly Mutex _mutex;
        private readonly CancellationTokenSource _cts = new();
        private readonly List<string> _pendingReviewPaths = [];
        private CancellationTokenSource? _reviewOpenCts;
        private MainWindow? _window;

        // ---- 工厂：单实例检查 ----

        public static RightClickOpenService? Create(string[] args)
        {
            var mutex = new Mutex(true, MutexName, out bool isFirstInstance);
            if (!isFirstInstance)
            {
                ForwardArgsToRunningInstance(args);
                return null;
            }
            return new RightClickOpenService(mutex);
        }

        private RightClickOpenService(Mutex mutex) => _mutex = mutex;

        public void Initialize(MainWindow window, string[] args)
        {
            _window = window;
            HandleArgs(args);
            StartPipeServer();
        }

        // ---- 注册表操作 ----

        private static string GetExecutablePath() =>
            Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "LabelMinusinWPF.exe");

        private static string BuildOpenCommand(string exePath)   => $"\"{exePath}\" \"%1\"";
        private static string BuildReviewCommand(string exePath) => $"\"{exePath}\" --review \"%1\"";

        private static IEnumerable<string> GetTargetExtensions() =>
            TargetExtensions
                .Concat(Constants.ImageExtensions)
                .Select(ext => ext.StartsWith('.') ? ext : "." + ext)
                .Distinct(StringComparer.OrdinalIgnoreCase);

        public static bool IsRegistered()
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"{BasePath}\Directory\shell\{OpenKey}");
            return key != null;
        }

        public static void RegisterAll()
        {
            string exePath = GetExecutablePath();
            string openCmd   = BuildOpenCommand(exePath);
            string reviewCmd = BuildReviewCommand(exePath);

            void Register(string root)
            {
                RegisterMenu(root, OpenKey,   "使用 LabelMinus 打开", openCmd,   exePath);
                RegisterMenu(root, ReviewKey, "使用 LabelMinus 图校", reviewCmd, exePath);
                SetMultiSelectModel(root, ReviewKey);
            }

            Register(@"Directory");
            foreach (var ext in GetTargetExtensions())
                Register($@"SystemFileAssociations\{ext}");
        }

        public static void UnregisterAll()
        {
            foreach (var root in GetRoots())
            {
                DeleteKey($@"{BasePath}\{root}\shell\{OpenKey}");
                DeleteKey($@"{BasePath}\{root}\shell\{ReviewKey}");
            }
        }

        private static IEnumerable<string> GetRoots() =>
            new[] { "Directory" }.Concat(
                GetTargetExtensions().Select(ext => $@"SystemFileAssociations\{ext}"));

        private static void RegisterMenu(string root, string keyName, string text, string command, string iconPath)
        {
            using var key = Registry.CurrentUser.CreateSubKey($@"{BasePath}\{root}\shell\{keyName}");
            if (key == null) return;
            key.SetValue("", text);
            key.SetValue("Icon", iconPath);
            using var cmdKey = key.CreateSubKey("command");
            cmdKey?.SetValue("", command);
        }

        private static void SetMultiSelectModel(string root, string keyName)
        {
            using var key = Registry.CurrentUser.CreateSubKey($@"{BasePath}\{root}\shell\{keyName}");
            key?.SetValue("MultiSelectModel", "Player");
        }

        private static void DeleteKey(string fullPath)
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(fullPath, false); }
            catch { /* 忽略删除失败 */ }
        }

        // ---- 单实例 IPC ----

        private static void ForwardArgsToRunningInstance(string[] args)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                    client.Connect(200);
                    using var writer = new StreamWriter(client);
                    writer.WriteLine(string.Join("\0", args));
                    writer.Flush();
                    return;
                }
                catch (TimeoutException)
                {
                    Thread.Sleep(50);
                }
                catch (IOException)
                {
                    Thread.Sleep(50);
                }
                catch
                {
                    return;
                }
            }
        }

        private async void StartPipeServer()
        {
            var token = _cts.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In);
                    await server.WaitForConnectionAsync(token);
                    using var reader = new StreamReader(server);
                    var message = await reader.ReadLineAsync(token);

                    if (!string.IsNullOrEmpty(message))
                        _window!.Dispatcher.Invoke(() => HandleArgs(message.Split('\0')));
                }
                catch (OperationCanceledException) { break; }
                catch { /* 其他异常静默继续 */ }
            }
        }

        // ---- CLI 参数解析与文件打开 ----

        private void HandleArgs(string[] args)
        {
            if (args.Length == 0) return;

            bool isReviewMode = args.Contains("--review");
            var paths = args
                .Where(a => a != "--review")
                .Select(a => a.Trim('"'))
                .Where(p => File.Exists(p) || Directory.Exists(p))
                .ToArray();

            if (paths.Length == 0) return;

            _window!.Dispatcher.BeginInvoke(() =>
            {
                if (isReviewMode)
                    QueueImageReviewWithPaths(paths);
                else
                    OpenFilesOnStartup(paths);
                FocusWindow();
            }, DispatcherPriority.Loaded);
        }

        private void OpenFilesOnStartup(string[] paths)
        {
            if (_window!.DataContext is not OneProject vm) return;
            if (_window.FullScreenReview.IsOpen)
                _window.FullScreenReview.IsOpen = false;
            vm.OpenResourceByPath(paths, false);
        }

        private void OpenImageReviewWithPaths(string[] paths)
        {
            if (!_window!.FullScreenReview.IsOpen)
                _window.FullScreenReview.IsOpen = true;

            if (_window.FullScreenReview.DataContext is not CompareImgVM reviewVm) return;

            reviewVm.LeftImageVM.OpenResourceByPath([paths[0]], false);
            if (paths.Length >= 2)
                reviewVm.RightImageVM.OpenResourceByPath([paths[1]], false);
        }

        private void QueueImageReviewWithPaths(string[] paths)
        {
            foreach (var path in paths)
                if (!_pendingReviewPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                    _pendingReviewPaths.Add(path);

            _reviewOpenCts?.Cancel();
            _reviewOpenCts?.Dispose();
            _reviewOpenCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            _ = FlushImageReviewQueueAsync(_reviewOpenCts.Token);
        }

        private async Task FlushImageReviewQueueAsync(CancellationToken token)
        {
            try
            {
                await Task.Delay(ReviewOpenBatchDelay, token);
                var paths = _pendingReviewPaths.ToArray();
                _pendingReviewPaths.Clear();

                if (paths.Length > 0)
                    OpenImageReviewWithPaths(paths);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void FocusWindow()
        {
            if (_window!.WindowState == WindowState.Minimized)
                _window.WindowState = WindowState.Normal;
            _window.Activate();
            _window.Topmost = true;
            _window.Topmost = false;
        }

        // ---- 清理 ----

        public void Dispose()
        {
            _cts.Cancel();
            _reviewOpenCts?.Cancel();
            _reviewOpenCts?.Dispose();
            _cts.Dispose();
            _mutex.Dispose();
        }
    }
}
