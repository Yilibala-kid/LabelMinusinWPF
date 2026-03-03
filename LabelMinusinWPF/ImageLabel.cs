using CommunityToolkit.Mvvm.ComponentModel;
using MahApps.Metro.Controls;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;


namespace LabelMinusinWPF
{
    public partial class ImageLabel : ObservableObject
    {
        #region 基本属性
        [ObservableProperty] private int _index;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsModified))] // Text 变了，IsModified 也要刷新
        private string _text = "";

        [ObservableProperty] private string _group = "框内";
        [ObservableProperty] private string _remark = "这是备注";
        [ObservableProperty] private double _fontSize = 20.0;
        [ObservableProperty] private string _fontFamily = "微软雅黑";
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

        /// <summary>当前标签是否被选中</summary>
        [ObservableProperty] private bool _isSelected;

        /// <summary>组别对应的画刷颜色（由 ViewModel 同步）</summary>
        [ObservableProperty] private SolidColorBrush _groupBrush = Brushes.Red;

        private bool _isModified = false;
        public bool IsModified => _isModified || IsDeleted;
        #endregion



        #region 快捷坐标访问
        public double X { get => Position.X; set => Position = Position with { X = Math.Clamp(value, 0, 1) }; }
        public double Y { get => Position.Y; set => Position = Position with { Y = Math.Clamp(value, 0, 1) }; }

        #endregion

        #region 5. 业务方法
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
            _originalText = text;
            Text = text; // 此时会触发 OnTextChanged，但你可以根据需求手动重置
            _isModified = false;
            OnPropertyChanged(nameof(IsModified));
        }

        public ImageLabel Clone() => (ImageLabel)this.MemberwiseClone();
        #endregion
    }

}
