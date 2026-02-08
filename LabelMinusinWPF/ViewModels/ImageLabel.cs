using System.ComponentModel;
using System.Runtime.CompilerServices;


namespace LabelMinusinWPF
{
    public class ImageLabel : ViewModelBase
    {
        #region 1. 字段定义
        private int _index;
        private string _text = "";
        private string _originalText = "";
        private string _group = "框内";
        private string _remark = "这是备注";
        private double _fontSize = 20.0;
        private string _fontFamily = "微软雅黑";
        private BoundingBox _position = BoundingBox.Default;
        private bool _isModified = false;
        private bool _isDeleted = false;
        #endregion

        #region 2. 公共数据属性 (Data)
        [DisplayName("序号")]
        public int Index { get => _index; set => SetProperty(ref _index, value); }

        [DisplayName("文本内容")]
        public string Text { get => _text; set => SetProperty(ref _text, value); }

        [DisplayName("分组")]
        public string Group { get => _group; set => SetProperty(ref _group, value); }

        [DisplayName("位置")]
        public BoundingBox Position { get => _position; set => SetProperty(ref _position, value); }

        [DisplayName("字号")]
        public double FontSize { get => _fontSize; set => SetProperty(ref _fontSize, value); }

        [DisplayName("字体")]
        public string FontFamily { get => _fontFamily; set => SetProperty(ref _fontFamily, value); }

        [DisplayName("备注")]
        public string Remark { get => _remark; set => SetProperty(ref _remark, value); }
        #endregion

        #region 3. 状态与逻辑属性 (State)
        [Browsable(false)]
        public bool IsDeleted { get => _isDeleted; set => SetProperty(ref _isDeleted, value); }

        [Browsable(false)] // 只有内容改了或被删了才叫已修改
        public bool IsModified { get => _isModified || _isDeleted; set => SetProperty(ref _isModified, value); }

        public string OriginalText => _originalText;
        #endregion

        #region 4. 快捷坐标访问 (UI Helpers)
        // 使用 with 关键字简化逻辑，通过 Position 统一触发通知
        [Browsable(false)] public float X { get => _position.X; set => Position = _position with { X = Math.Clamp(value, 0, 1) }; }
        [Browsable(false)] public float Y { get => _position.Y; set => Position = _position with { Y = Math.Clamp(value, 0, 1) }; }
        [Browsable(false)] public float Width { get => _position.Width; set => Position = _position with { Width = value }; }
        [Browsable(false)] public float Height { get => _position.Height; set => Position = _position with { Height = value }; }
        #endregion

        #region 5. 业务方法
        public void LoadBaseContent(string text)
        {
            _originalText = text;
            _text = text;
            _isModified = false;
            OnPropertyChanged(nameof(Text));
        }

        // 统一拦截
        // 只有在这些核心属性变化时，才标记为已修改
        // 1. Text 变了
        // 2. IsDeleted 状态变了 (即新建后删除或恢复)
        protected new bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            bool changed = base.SetProperty(ref storage, value, propertyName);

            if (changed)
            {

                if (propertyName == nameof(Text) || propertyName == nameof(IsDeleted))
                {
                    if (!_isModified) // 如果已经是 true 了就没必要再跑一次逻辑
                    {
                        IsModified = true;
                    }
                }
            }
            return changed;
        }

        public ImageLabel Clone() => (ImageLabel)this.MemberwiseClone();
        #endregion
    }

    public readonly record struct BoundingBox(float X, float Y, float Width, float Height)
    {
        public static BoundingBox Default => new(0, 0, 0, 0);
    }
}
