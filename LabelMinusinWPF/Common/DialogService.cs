using Microsoft.Win32;
using System;
using System.Windows;

namespace LabelMinusinWPF.Common
{
    /// <summary>文件对话框封装</summary>
    public class DialogService
    {
        public static string? OpenFolder(string description)
        {
            var dialog = new OpenFolderDialog { Title = description };
            return dialog.ShowDialog() == true ? dialog.FolderName : null;
        }

        public static string[]? OpenFiles(string filter, string description)
        {
            var dialog = new OpenFileDialog { Filter = filter, Multiselect = true, Title = description };
            return dialog.ShowDialog() == true ? dialog.FileNames : null;
        }

        public static string? OpenFile(string filter, string description, bool multiselect = false)
        {
            var dialog = new OpenFileDialog { Filter = filter, Multiselect = multiselect, Title = description };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        public static string? SaveFile(string filter, string defaultName)
        {
            var dialog = new SaveFileDialog { Filter = filter, FileName = defaultName };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        public static void ShowMessage(string message, bool isError)
        {
            MessageBox.Show(message, isError ? "错误" : "提示",
                MessageBoxButton.OK, isError ? MessageBoxImage.Error : MessageBoxImage.Information);
        }

        /// <summary>显示确认对话框</summary>
        public static MessageBoxResult Confirm(string message, string title = "确认")
        {
            return MessageBox.Show(message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        }
    }
}
