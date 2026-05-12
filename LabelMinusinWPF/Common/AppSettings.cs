namespace LabelMinusinWPF.Common
{
    public class AppSettings
    {
        public int Version { get; set; } = 1;
        public LabelStyleSettings LabelStyle { get; set; } = new();
        public UiSettings Ui { get; set; } = new();
    }

    public class LabelStyleSettings
    {
        public string DotStyle { get; set; } = "Circle";
        public string TextBackgroundColor { get; set; } = "White";
        public string TextForegroundColor { get; set; } = "Black";
        public double TextBackgroundOpacity { get; set; } = 1.0;
        public double LabelScale { get; set; } = 1.0;
    }

    public class UiSettings
    {
        public bool RightClickOpenEnabled { get; set; }
        public bool OpenImageReviewOnStartup { get; set; }
        public bool AutoLoadLastProjectEnabled { get; set; }
        public string LastProjectPath { get; set; } = "";
        public int AutoSaveIntervalMinutes { get; set; } = Constants.AutoSave.IntervalMinutes;
    }
}
