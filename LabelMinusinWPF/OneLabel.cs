using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows;
using System.Windows.Media;
using LabelMinusinWPF.Common;
using Constants = LabelMinusinWPF.Common.Constants;


namespace LabelMinusinWPF
{
    public partial class OneLabel : ObservableObject
    {
        #region 基本属性
        [ObservableProperty] private int _index;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsModified))]
        private string _text = "";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(GroupBrush))]
        private string _group = Constants.Groups.Default;
        public SolidColorBrush GroupBrush => GroupColorManager.GetBrush(Group);
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(X), nameof(Y))]
        private Point _position = new(0, 0);

        #endregion





        #region UI 相关属性
        [ObservableProperty]
        private bool _isEditing;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsModified))]
        private bool _isDeleted;
        [ObservableProperty] private string _originalText = "";
        [ObservableProperty] private bool _isSelected;

        private bool _isModified;
        public bool IsModified => _isModified || IsDeleted;
        #endregion



        #region 快捷坐标访问
        public double X { get => Position.X; set => Position = Position with { X = Math.Clamp(value, 0, 1) }; }
        public double Y { get => Position.Y; set => Position = Position with { Y = Math.Clamp(value, 0, 1) }; }

        #endregion

        #region 业务方法
        partial void OnTextChanged(string value) => SetModified();
        partial void OnGroupChanged(string value) => SetModified();
        partial void OnIsDeletedChanged(bool value) => SetModified();

        public void RefreshGroupBrush() => OnPropertyChanged(nameof(GroupBrush));


        private void SetModified()
        {
            if (!_isModified) { _isModified = true; OnPropertyChanged(nameof(IsModified)); }
        }

        public void LoadBaseContent(string text)
        {
            OriginalText = text;
            Text = text;
            _isModified = false;
            OnPropertyChanged(nameof(IsModified));
        }
        #endregion
    }

}
