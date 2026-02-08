using LabelMinusinWPF.ViewModels;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace LabelMinusinWPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }
       

        private void OpenImageReview_Click(object sender, RoutedEventArgs e)
        {
            FullScreenReview.Visibility = Visibility.Visible;
        }
        private void FullScreenReview_ExitClicked(object sender, EventArgs e)
        {
            FullScreenReview.Visibility = Visibility.Collapsed;
        }
    }
    public class RelayCommand : ICommand// 简单的 RelayCommand 实现
    {
        private readonly Action<object> _execute;
        private readonly Predicate<object> _canExecute;
        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
        {
            _execute = execute; _canExecute = canExecute;
        }
        public bool CanExecute(object parameter) => _canExecute == null || _canExecute(parameter);
        public void Execute(object parameter) => _execute(parameter);
        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
    public class ViewModelBase : INotifyPropertyChanged// 基础通知类，减少重复代码
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value)) return false;
            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}