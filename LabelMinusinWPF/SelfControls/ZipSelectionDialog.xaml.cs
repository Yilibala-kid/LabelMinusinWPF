using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace LabelMinusinWPF
{
    public partial class ZipSelectionDialog : Window
    {
        public string? SelectedZip { get; private set; }

        public ZipSelectionDialog(List<string> zipFiles, string? currentZip)
        {
            InitializeComponent();

            // 添加"无"选项用于取消关联
            var items = new List<string> { "无（取消关联）" };
            items.AddRange(zipFiles);

            ZipListBox.ItemsSource = items;

            // 设置当前选中项
            if (!string.IsNullOrEmpty(currentZip) && zipFiles.Contains(currentZip))
            {
                ZipListBox.SelectedItem = currentZip;
            }
            else
            {
                ZipListBox.SelectedIndex = 0;
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            if (ZipListBox.SelectedItem is string selected)
            {
                SelectedZip = selected.StartsWith("无") ? null : selected;
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("请选择一个压缩包", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
