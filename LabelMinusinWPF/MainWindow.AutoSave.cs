using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using LabelMinusinWPF.Common;
using Constants = LabelMinusinWPF.Common.Constants;
using ExportMode = LabelMinusinWPF.Common.LabelPlusParser.ExportMode;
using WorkSpace = LabelMinusinWPF.Common.ProjectManager.WorkSpace;

namespace LabelMinusinWPF
{
    public partial class MainWindow
    {
        private DispatcherTimer? _autoSaveTimer;

        private void InitializeAutoSave()
        {
            _autoSaveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(Constants.AutoSave.IntervalMinutes)
            };
            _autoSaveTimer.Tick += AutoSaveTimer_Tick;
            _autoSaveTimer.Start();
        }

        private void AutoSaveTimer_Tick(object? sender, EventArgs e)
        {
            if (DataContext is not OneProject vm) return;
            if (vm.WorkSpace == WorkSpace.Empty || !vm.HasUnsavedChanges()) return;
            if (FullScreenReview.IsOpen) return;

            try
            {
                string autoSaveFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AutoSave");
                Directory.CreateDirectory(autoSaveFolder);

                string originalFileName = string.IsNullOrEmpty(vm.WorkSpace.TxtName)
                    ? "未命名翻译"
                    : Path.GetFileNameWithoutExtension(vm.WorkSpace.TxtName);
                string autoSavePath = Path.Combine(autoSaveFolder, $"{originalFileName}_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

                File.WriteAllText(autoSavePath, LabelPlusParser.LabelsToText(vm.ImageList, vm.WorkSpace.ZipName, ExportMode.Current));
                CleanupOldAutoSaveFiles(autoSaveFolder, originalFileName);
            }
            catch (Exception ex) { Debug.WriteLine($"自动保存失败: {ex.Message}"); }
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
                    try { file.Delete(); } catch { /* 删除失败不影响程序运行 */ }
            }
            catch (Exception ex) { Debug.WriteLine($"清理旧自动保存文件失败: {ex.Message}"); }
        }
    }
}
