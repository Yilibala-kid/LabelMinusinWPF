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

        [ObservableProperty] private string _group = Constants.Groups.Default;
        //[ObservableProperty] private string _remark = Constants.Label.DefaultRemark;
        //[ObservableProperty] private double _fontSize = Constants.Label.DefaultFontSize;
        //[ObservableProperty] private string _fontFamily = Constants.Label.DefaultFontFamily;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(X), nameof(Y))]
        private Point _position = new(0, 0);

        #endregion





        #region UI 相关属性
        [ObservableProperty]
        private bool _isEditing;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsModified))]
        private bool _isDeleted = false;

        [ObservableProperty] private string _originalText = "";

        // 当前标签是否被选中
        [ObservableProperty] private bool _isSelected;

        // 组别对应的画刷颜色（由 ViewModel 同步）
        [ObservableProperty] private SolidColorBrush _groupBrush = Constants.Groups.Brushes[0];

        private bool _isModified = false;
        public bool IsModified => _isModified || IsDeleted;
        #endregion



        #region 快捷坐标访问
        public double X { get => Position.X; set => Position = Position with { X = Math.Clamp(value, 0, 1) }; }
        public double Y { get => Position.Y; set => Position = Position with { Y = Math.Clamp(value, 0, 1) }; }

        #endregion

        #region 业务方法
        partial void OnTextChanged(string value) => SetModified();
        partial void OnIsDeletedChanged(bool value) => SetModified();

        private void SetModified()
        {
            if (!_isModified)
            {
                _isModified = true;
                // 修改点 2：因为修改了 _isModified，必须手动通知 IsModified 属性发生了变化
                OnPropertyChanged(nameof(IsModified));
            }
        }

        public void LoadBaseContent(string text)
        {
            OriginalText = text;
            Text = text; // 此时会触发 OnTextChanged，但你可以根据需求手动重置
            _isModified = false;
            OnPropertyChanged(nameof(IsModified));
        }
        #endregion
    }

}
