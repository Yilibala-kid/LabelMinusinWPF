using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using LabelMinusinWPF.Common;
using Constants = LabelMinusinWPF.Common.Constants;
using GroupManager = LabelMinusinWPF.Common.GroupManager;
using GroupConstants = LabelMinusinWPF.Common.GroupConstants;

namespace LabelMinusinWPF
{
    public partial class OneLabel : ObservableObject
    {
        public OneLabel(string text, string group, Point pos)
        {
            _text = _originalText = text;
            _group = _originalGroup = group;
            _position = _originalPosition = pos;

            _isDeleted = false;
        }

        #region 基本属性
        [ObservableProperty]
        private string _originalText = "";
        [ObservableProperty]
        private string _text = "";

        [ObservableProperty]
        private string _originalGroup = GroupConstants.InBox;
        [ObservableProperty]
        private string _group = GroupConstants.InBox;

        [ObservableProperty]
        private Point _originalPosition = new(0, 0);
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(X), nameof(Y))]
        private Point _position = new(0, 0);
        #endregion


        #region UI 相关属性
        [ObservableProperty]
        private bool _isDeleted;
        #endregion


        #region 快捷坐标访问
        public double X
        {
            get => Position.X;
            set => Position = Position with { X = Math.Clamp(value, 0, 1) };
        }
        public double Y
        {
            get => Position.Y;
            set => Position = Position with { Y = Math.Clamp(value, 0, 1) };
        }

        #endregion
    }
}
