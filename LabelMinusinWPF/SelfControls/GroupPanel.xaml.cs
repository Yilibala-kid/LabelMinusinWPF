using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LabelMinusinWPF.Common;

namespace LabelMinusinWPF.SelfControls
{
    public partial class GroupPanel : UserControl
    {
        public GroupPanel()
        {
            InitializeComponent();
            DataContext = GroupManager.Instance;
        }

        private void AddGroup_Click(object sender, RoutedEventArgs e)
        {
            NewGroupTextBox.Clear();
            AddGroupPanel.Visibility = Visibility.Visible;
            IdleButtonPanel.Visibility = Visibility.Collapsed;
            NewGroupTextBox.Focus();
        }

        private void SetIdleMode()
        {
            NewGroupTextBox.Clear();
            AddGroupPanel.Visibility = Visibility.Collapsed;
            IdleButtonPanel.Visibility = Visibility.Visible;
        }

        private void NewGroupTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                CancelAdd_Click(sender, new RoutedEventArgs());
            else if (e.Key == Key.Enter)
                ConfirmAdd_Click(sender, new RoutedEventArgs());
        }

        private void ConfirmAdd_Click(object sender, RoutedEventArgs e)
        {
            GroupManager.Instance.AddGroupCommand.Execute(NewGroupTextBox.Text);
            SetIdleMode();
        }

        private void CancelAdd_Click(object sender, RoutedEventArgs e)
        {
            SetIdleMode();
        }

        private async void DeleteGroup_Click(object sender, RoutedEventArgs e)
        {
            await GroupManager.Instance.DeleteGroupAsync(GroupManager.Instance.SelectedGroup);
        }
    }
}
