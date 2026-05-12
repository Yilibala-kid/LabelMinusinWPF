using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using LabelMinusinWPF.SelfControls;
using ExportMode = LabelMinusinWPF.Common.LabelPlusParser.ExportMode;
using WorkSpace = LabelMinusinWPF.Common.ProjectManager.WorkSpace;

namespace LabelMinusinWPF.Common
{
    public static class AppSettingsService
    {
        private const int CurrentVersion = 1;
        private const string SettingsFileName = "settings.json";
        private const string LegacyLabelStyleSettingsFileName = "LabelStyleSettings.json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private static readonly string[] EmptyStartupArgs = [];
        private static DispatcherTimer? _autoSaveTimer;
        private static MainWindow? _mainWindow;
        private static string[] _startupArgs = EmptyStartupArgs;

        public static AppSettings Current { get; private set; } = CreateDefault();

        private static string SettingsFilePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SettingsFileName);

        private static string LegacyLabelStyleSettingsFilePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LegacyLabelStyleSettingsFileName);

        public static void SetStartupArgs(string[]? args) =>
            _startupArgs = args ?? EmptyStartupArgs;

        public static void InitializeMainWindow(MainWindow window)
        {
            _mainWindow = window;
            LabelStylePanel.Instance.LoadSettings();
            ApplyAutoSaveInterval(window);

            window.Closing += (_, e) =>
            {
                if (!e.Cancel)
                    RecordCurrentProject(window);
            };

            window.Closed += (_, _) =>
            {
                if (!ReferenceEquals(_mainWindow, window)) return;

                _autoSaveTimer?.Stop();
                _autoSaveTimer = null;
                _mainWindow = null;
            };

            window.Dispatcher.BeginInvoke(() =>
            {
                if (HasStartupPayload()) return;

                TryRestoreLastProject(window);

                if (Current.Ui.OpenImageReviewOnStartup)
                    window.FullScreenReview.IsOpen = true;
            }, DispatcherPriority.Loaded);
        }

        public static void ApplyAutoSaveInterval(MainWindow? window = null)
        {
            _mainWindow = window ?? _mainWindow;
            if (_mainWindow == null) return;

            _autoSaveTimer ??= new DispatcherTimer();
            _autoSaveTimer.Stop();
            _autoSaveTimer.Tick -= AutoSaveTimer_Tick;
            _autoSaveTimer.Interval = TimeSpan.FromMinutes(Current.Ui.AutoSaveIntervalMinutes);
            _autoSaveTimer.Tick += AutoSaveTimer_Tick;
            _autoSaveTimer.Start();
        }

        public static string GetAutoSaveFolderPath() =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Constants.AutoSave.FolderName);

        public static void OpenAutoSaveFolder()
        {
            string folderPath = GetAutoSaveFolderPath();
            Directory.CreateDirectory(folderPath);
            Process.Start(new ProcessStartInfo
            {
                FileName = folderPath,
                UseShellExecute = true
            });
        }

        public static int NormalizeAutoSaveIntervalMinutes(int minutes) =>
            minutes <= 0
                ? Constants.AutoSave.IntervalMinutes
                : Math.Clamp(minutes, 1, 60);

        public static void SaveUiSettings(
            bool openImageReviewOnStartup,
            bool autoLoadLastProjectEnabled,
            int autoSaveIntervalMinutes,
            bool rightClickOpenEnabled)
        {
            Current.Ui.OpenImageReviewOnStartup = openImageReviewOnStartup;
            Current.Ui.AutoLoadLastProjectEnabled = autoLoadLastProjectEnabled;
            Current.Ui.AutoSaveIntervalMinutes = NormalizeAutoSaveIntervalMinutes(autoSaveIntervalMinutes);
            Current.Ui.RightClickOpenEnabled = rightClickOpenEnabled;
            Save();
            ApplyAutoSaveInterval();
        }

        public static void RecordLastProject(WorkSpace workSpace)
        {
            string path = workSpace.TxtPath;
            if (string.IsNullOrWhiteSpace(path)
                || !Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase)
                || !File.Exists(path))
                return;

            Current.Ui.LastProjectPath = path;
            Save();
        }

        private static void RecordCurrentProject(MainWindow window)
        {
            if (window.DataContext is OneProject vm)
                RecordLastProject(vm.WorkSpace);
        }

        public static void Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var settings = JsonSerializer.Deserialize<AppSettings>(
                        File.ReadAllText(SettingsFilePath),
                        JsonOptions);
                    Current = Normalize(settings);
                    return;
                }

                Current = CreateDefault();
                if (TryLoadLegacyLabelStyle(out var legacyLabelStyle))
                {
                    Current.LabelStyle = Normalize(legacyLabelStyle);
                    Save();
                }
            }
            catch
            {
                Current = CreateDefault();
            }
        }

        public static void Save()
        {
            try
            {
                Current = Normalize(Current);
                File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(Current, JsonOptions));
            }
            catch { }
        }

        private static void TryRestoreLastProject(MainWindow window)
        {
            if (!Current.Ui.AutoLoadLastProjectEnabled) return;
            string path = Current.Ui.LastProjectPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            if (window.DataContext is not OneProject vm) return;
            if (vm.WorkSpace != WorkSpace.Empty) return;

            try
            {
                vm.OpenResourceByPath([path], false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"自动加载上一次项目失败: {ex.Message}");
            }
        }

        private static bool TryLoadLegacyLabelStyle(out LabelStyleSettings labelStyle)
        {
            labelStyle = new LabelStyleSettings();
            try
            {
                if (!File.Exists(LegacyLabelStyleSettingsFilePath))
                    return false;

                var legacy = JsonSerializer.Deserialize<LabelStyleSettings>(
                    File.ReadAllText(LegacyLabelStyleSettingsFilePath));
                if (legacy == null)
                    return false;

                labelStyle = legacy;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static AppSettings CreateDefault() => new() { Version = CurrentVersion };

        private static AppSettings Normalize(AppSettings? settings)
        {
            settings ??= CreateDefault();
            settings.Version = CurrentVersion;
            settings.LabelStyle = Normalize(settings.LabelStyle);
            settings.Ui = Normalize(settings.Ui);
            return settings;
        }

        private static UiSettings Normalize(UiSettings? settings)
        {
            settings ??= new UiSettings();
            settings.LastProjectPath ??= "";
            settings.AutoSaveIntervalMinutes = NormalizeAutoSaveIntervalMinutes(settings.AutoSaveIntervalMinutes);
            return settings;
        }

        private static LabelStyleSettings Normalize(LabelStyleSettings? settings)
        {
            settings ??= new LabelStyleSettings();
            settings.DotStyle = NormalizeDotStyle(settings.DotStyle);
            settings.TextBackgroundColor = NormalizeColorName(settings.TextBackgroundColor, "White");
            settings.TextForegroundColor = NormalizeColorName(settings.TextForegroundColor, "Black");
            settings.TextBackgroundOpacity = Math.Clamp(settings.TextBackgroundOpacity, 0.0, 1.0);
            settings.LabelScale = Math.Clamp(settings.LabelScale, 0.3, 3.0);
            return settings;
        }

        private static string NormalizeDotStyle(string? dotStyle) =>
            dotStyle is "Circle" or "Square" or "Transparent" ? dotStyle : "Circle";

        private static string NormalizeColorName(string? colorName, string fallback) =>
            string.IsNullOrWhiteSpace(colorName) ? fallback : colorName;

        private static bool HasStartupPayload() =>
            _startupArgs.Any(arg => !string.IsNullOrWhiteSpace(arg));

        private static void AutoSaveTimer_Tick(object? sender, EventArgs e)
        {
            var window = _mainWindow;
            if (window?.DataContext is not OneProject vm) return;
            if (vm.WorkSpace == WorkSpace.Empty || !vm.HasUnsavedChanges()) return;
            if (window.FullScreenReview.IsOpen) return;

            try
            {
                string autoSaveFolder = GetAutoSaveFolderPath();
                Directory.CreateDirectory(autoSaveFolder);

                string originalFileName = string.IsNullOrEmpty(vm.WorkSpace.TxtName)
                    ? "未命名翻译"
                    : Path.GetFileNameWithoutExtension(vm.WorkSpace.TxtName);
                string autoSavePath = Path.Combine(autoSaveFolder, $"{originalFileName}_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

                File.WriteAllText(autoSavePath, LabelPlusParser.LabelsToText(vm.ImageList, vm.WorkSpace.ZipName, ExportMode.Current));
                CleanupOldAutoSaveFiles(autoSaveFolder, originalFileName);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"自动保存失败: {ex.Message}");
            }
        }

        private static void CleanupOldAutoSaveFiles(string autoSaveFolder, string baseFileName)
        {
            try
            {
                var files = Directory.GetFiles(autoSaveFolder, $"{baseFileName}_*.txt")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTime)
                    .Skip(Constants.AutoSave.MaxFiles);

                foreach (var file in files)
                    try { file.Delete(); } catch { }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"清理旧自动保存文件失败: {ex.Message}");
            }
        }
    }
}
