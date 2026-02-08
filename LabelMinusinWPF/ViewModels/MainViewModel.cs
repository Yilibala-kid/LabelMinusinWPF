using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace LabelMinusinWPF.ViewModels // 建议加上 ViewModels 命名空间
{
    public enum EditMode
    {
        Label,   // 标记
        Review,  // 文校
        OCR      // 识别
    }
    public class MainViewModel : ViewModelBase
    {
        // --- 状态属性 ---
        private ProjectContext _currentProject = ProjectContext.Empty;
        public ProjectContext CurrentProject
        {
            get => _currentProject;
            set { SetProperty(ref _currentProject, value); OnPropertyChanged(nameof(DisplayTitle)); }
        }

        public string DisplayTitle => CurrentProject.DisplayTitle;

        // --- 数据源 (替代 imageDatabase 和 PicNameBindingSource) ---
        public ObservableCollection<ImageInfo> ImageList { get; } = [];

        private ImageInfo? _selectedImage;
        public ImageInfo? SelectedImage
        {
            get => _selectedImage;
            set { if (SetProperty(ref _selectedImage, value)); }
        }
        #region 模式选择
        private bool _isMarkMode;
        private bool _isTextReviewMode;
        private bool _isRecognitionMode;
        private bool _isImageReviewMode;

        public bool IsMarkMode
        {
            get => _isMarkMode;
            set { _isMarkMode = value; OnPropertyChanged(); }
        }

        public bool IsTextReviewMode
        {
            get => _isTextReviewMode;
            set { _isTextReviewMode = value; OnPropertyChanged(); }
        }

        public bool IsRecognitionMode
        {
            get => _isRecognitionMode;
            set { _isRecognitionMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsWebsiteComboBoxVisible)); }
        }

        public bool IsImageReviewMode
        {
            get => _isImageReviewMode;
            set { _isImageReviewMode = value; OnPropertyChanged(); }
        }

        public Visibility IsWebsiteComboBoxVisible => IsRecognitionMode ? Visibility.Visible : Visibility.Collapsed;

        // 切换模式的方法
        public void SwitchMode(string mode)
        {
            IsMarkMode = mode == "Mark";
            IsTextReviewMode = mode == "TextReview";
            IsRecognitionMode = mode == "Recognition";
            IsImageReviewMode = mode == "ImageReview";
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

    }

    public class ProjectContext
    {
        public string BaseFolderPath { get; set; } = string.Empty;// 显式存储文件夹路径        
        public string TxtName { get; set; } = null;// 翻译文件路径（预览模式下为空）       
        public string? ZipName { get; set; } = null;// 压缩包文件名（文件夹模式下为空）
        public static ProjectContext Empty => new();
        // 静态工厂方法：封装创建逻辑
        public static ProjectContext Create(string baseFolder, string? txtName, string? zipName)
        {
            return new ProjectContext
            {
                BaseFolderPath = baseFolder,
                TxtName = string.IsNullOrWhiteSpace(txtName) ? null : txtName,
                ZipName = string.IsNullOrWhiteSpace(zipName) ? null : zipName
            };
        }
        // 计算属性：翻译文件的文件名
        public string TxtPath => !string.IsNullOrEmpty(TxtName)
                ? Path.Combine(BaseFolderPath, TxtName)
                : string.Empty;

        // 计算属性：判断当前是否为压缩包模式
        public string ZipPath => !string.IsNullOrEmpty(ZipName)
                ? Path.Combine(BaseFolderPath, ZipName)
                : string.Empty;
        public bool IsArchiveMode => !string.IsNullOrEmpty(ZipName);
        // 计算属性：生成标题栏文字
        public string DisplayTitle => $"LabelMinus - {TxtName} [{(IsArchiveMode ? $"关联:{ZipName}" : "文件夹")}]";
    }
    public static class ProjectService
    {
        // 对应你的 GetFolderImages
        public static List<ImageInfo> ScanFolder(string path)
        {
            var extensions = new[] { ".jpg", ".png", ".bmp", ".webp" };
            return Directory.EnumerateFiles(path)
                .Where(f => extensions.Contains(Path.GetExtension(f).ToLower()))
                .Select(f => new ImageInfo { ImageName = Path.GetFileName(f) })
                .ToList();
        }

        // 对应你的 GetZipImages
        public static List<ImageInfo> ScanZip(string zipPath)
        {
            // 调用你之前的 ArchiveHelper
            return ArchiveHelper.GetImageEntries(zipPath)
                .Select(name => new ImageInfo { ImageName = name })
                .ToList();
        }
    }
    public class ModeToBrushConverter : IValueConverter
    {
        public EditMode TargetMode { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is EditMode currentMode && currentMode == TargetMode)
            {
                // 返回高亮色（例如 Material Design 的 Primary 颜色）
                return new SolidColorBrush(Color.FromRgb(103, 58, 183)); // 示例紫色
            }
            return Brushes.Transparent; // 不高亮时透明
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
    public class EnumToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value?.Equals(parameter) ?? false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value?.Equals(true) == true ? parameter : Binding.DoNothing;
        }
    }
}
