using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace LabelMinusinWPF
{
    public partial class ZipDialog : Window
    {
        public string? SelectedZip { get; private set; }

        public ZipDialog(List<string> zipFiles, string? currentZip)
        {
            InitializeComponent();

            // 添加"无"选项用于取消关联
            var items = new[] { "无（取消关联）" }.Concat(zipFiles).ToList();

            ZipListBox.ItemsSource = items;

            // 设置当前选中项
            ZipListBox.SelectedItem = !string.IsNullOrEmpty(currentZip) && zipFiles.Contains(currentZip)
                ? currentZip
                : items[0];
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            if (ZipListBox.SelectedItem is not string selected)
            {
                MessageBox.Show("请选择一个压缩包", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            SelectedZip = selected.StartsWith("无") ? null : selected;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
