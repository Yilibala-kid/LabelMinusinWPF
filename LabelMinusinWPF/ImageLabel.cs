using CommunityToolkit.Mvvm.ComponentModel;
using MahApps.Metro.Controls;
using System.ComponentModel;
using System.Runtime.CompilerServices;


namespace LabelMinusinWPF
{
    public partial class ImageLabel : ObservableObject
    {
        [ObservableProperty] private int _index;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsModified))] // Text 变了，IsModified 也要刷新
        private string _text = "";

        [ObservableProperty] private string _group = "框内";
        [ObservableProperty] private string _remark = "这是备注";
        [ObservableProperty] private double _fontSize = 20.0;
        [ObservableProperty] private string _fontFamily = "微软雅黑";
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(X), nameof(Y), nameof(Width), nameof(Height))]
        private BoundingBox _position = BoundingBox.Default;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsModified))]
        private bool _isDeleted = false;

        [ObservableProperty] private string _originalText = "";

        private bool _isModified = false;
        public bool IsModified => _isModified || IsDeleted;
        #region 快捷坐标访问
        public float X { get => Position.X; set => Position = Position with { X = Math.Clamp(value, 0, 1) }; }
        public float Y { get => Position.Y; set => Position = Position with { Y = Math.Clamp(value, 0, 1) }; }
        public float Width { get => Position.Width; set => Position = Position with { Width = value }; }
        public float Height { get => Position.Height; set => Position = Position with { Height = value }; }
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

    public readonly record struct BoundingBox(float X, float Y, float Width, float Height)
    {
        public static BoundingBox Default => new(0, 0, 0, 0);
    }
}
