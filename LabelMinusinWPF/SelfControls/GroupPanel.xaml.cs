using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LabelMinusinWPF.SelfControls
{
    public partial class GroupPanel : UserControl
    {
        public GroupPanel()
        {
            InitializeComponent();
        }

        private MainVM? ViewModel => DataContext as MainVM;

        private void AddGroup_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel is null) return;

            ViewModel.IsAddingGroup = true;
            NewGroupTextBox.Clear();
            NewGroupTextBox.Focus();
        }

        private void NewGroupTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (ViewModel is null) return;

            if (e.Key == Key.Escape)
                ViewModel.IsAddingGroup = false;
            else if (e.Key == Key.Enter)
                ViewModel.AddGroupCommand.Execute(null);
        }

        private void NewGroupTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.IsAddingGroup == true)
                ViewModel.AddGroupCommand.Execute(null);
        }
    }
}
