using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace LabelMinusinWPF
{
    public partial class ZipDialog : Window
    {
        private const string NoneValue = "";

        public ZipDialog(List<string> zipFiles, string? currentZip)
        {
            InitializeComponent();

            var items = new[] { ZipOption.None }.Concat(zipFiles.Select(ZipOption.FromPath)).ToList();
            ZipListBox.ItemsSource = items;
            ZipListBox.SelectedItem = items.FirstOrDefault(item => item.Value == currentZip) ?? items[0];
        }

        public string? SelectedZip { get; private set; }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            if (ZipListBox.SelectedItem is not ZipOption selected)
            {
                MessageBox.Show("请选择一个压缩包。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SelectedZip = selected.Value == NoneValue ? null : selected.Value;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private sealed record ZipOption(string Name, string Description, string Icon, string Value)
        {
            public static ZipOption None { get; } = new(
                "无",
                "取消压缩包关联，切换回当前文件夹。",
                "FolderOff",
                NoneValue);

            public static ZipOption FromPath(string path) => new(
                System.IO.Path.GetFileName(path),
                path,
                "FolderZip",
                path);
        }
    }
}
