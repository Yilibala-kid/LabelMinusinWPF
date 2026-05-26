using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelMinusinWPF.Common;
using LabelMinusinWPF.OCRService;
using LabelMinusinWPF.SelfControls;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Constants = LabelMinusinWPF.Common.Constants;
using ExportMode = LabelMinusinWPF.Common.LabelPlusParser.ExportMode;
using WorkSpace = LabelMinusinWPF.Common.ProjectManager.WorkSpace;

namespace LabelMinusinWPF
{

    /// Interaction logic for MainWindow.xaml

    ///
    public enum DisplayMode
    {
        ImageOnly,
        ListAndTextBox,
        ListOnly,
        TextBoxOnly
    }
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Task.Run(() => ProjectManager.ClearTempFolders(
                OcrConstants.OcrTemp,
                Constants.TempFolders.ScreenShotTemp,
                Constants.TempFolders.ArchiveTemp));
            Closing += MainWindow_Closing;
            OcrPanel.Attach(PicView, this);
            AppSettingsService.InitializeMainWindow(this);
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (DataContext is OneProject viewModel && viewModel.HasUnsavedChanges())
            {
                var result = MessageBox.Show(
                    "当前翻译有未保存的修改，是否保存？",
                    "提示",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    viewModel.SaveCommand.Execute(null);
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                }
            }
        }
        
        #region 底栏图片控制
        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            var window = new SettingsDialog { Owner = this };
            window.ShowDialog();
        }

        private void FitToPage_Click(object sender, RoutedEventArgs e) => PicView.FitToView();
        private void FitWidth_Click(object sender, RoutedEventArgs e) => PicView.FitToWidth();
        private void FitHeight_Click(object sender, RoutedEventArgs e) => PicView.FitToHeight();
        private void ZoomIn_Click(object sender, RoutedEventArgs e) => PicView.ZoomScale *= 1.1;
        private void ZoomOut_Click(object sender, RoutedEventArgs e) => PicView.ZoomScale *= 0.9;
        #endregion



        #region OCR 功能

        private async void SetupOcrEnv_Click(object sender, RoutedEventArgs e)
            => await OcrPanel.InstallEnvironmentAsync();

        private async void AutoOcr_Click(object sender, RoutedEventArgs e)
            => await OcrPanel.RunAutoDotAsync();

        private async void AutoOcr_Batch(object sender, RoutedEventArgs e)
            => await OcrPanel.RunBatchAsync(
                ((MenuItem)sender).Tag as string == "JP" ? OcrEngineKind.Manga : OcrEngineKind.Paddle);

        private void OcrHelp_Click(object sender, RoutedEventArgs e)
            => OcrPanel.ShowHelp();
        #endregion

        #region 拖放文件支持
        private void OnFileDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnFileDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                if (DataContext is OneProject viewModel) viewModel.OpenResourceByPath(files, false);
        }
        #endregion

        #region 菜单栏：帮助
        private void OnOpenWebsite(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: string url })
            {
                try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
                catch (Exception ex) { MessageBox.Show($"无法打开网页: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is OneProject vm) vm.MsgQueue.Enqueue("本程序由No-Hifuu友情赞助");
        }

        private void Shortcuts_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Window
            {
                Title = "快捷键说明",
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                MaxHeight = 720,
                MinWidth = 760
            };

            var root = new Grid { Margin = new Thickness(18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var columns = new Grid();
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var note = new TextBlock
            {
                Text = "说明：全局快捷键始终生效；“非编辑时”快捷键仅在文本框没有焦点时生效，编辑文字时会优先使用文本框自己的 Ctrl+Z / Ctrl+Y / Ctrl+C / Ctrl+V 等操作。",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14),
                Foreground = SystemColors.GrayTextBrush
            };

            var left = CreateShortcutColumn(
                ("全局：文件", GetShortcutHelpBody("全局：文件")),
                ("全局：标签导航", GetShortcutHelpBody("全局：标签导航")),
                ("非编辑时：标签操作", GetShortcutHelpBody("非编辑时：标签操作")));
            var right = CreateShortcutColumn(
                ("非编辑时：图片与视图", GetShortcutHelpBody("非编辑时：图片与视图")),
                ("图校界面",
                    """
                    Q：按住截图
                    A / 左方向键：上一张图片
                    D / 右方向键：下一张图片
                    R：重置同步
                    Ctrl+R：重置分割线
                    G：切换同步模式
                    C：交换左右图片
                    P：清空图片
                    H：切换分割线跟随鼠标
                    F1：显示/隐藏图校工具栏
                    """));
            Grid.SetColumn(right, 2);
            columns.Children.Add(left);
            columns.Children.Add(right);

            var okButton = new Button
            {
                Content = "确定",
                Width = 88,
                Margin = new Thickness(0, 16, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                IsDefault = true,
                IsCancel = true
            };
            okButton.Click += (_, _) => dialog.Close();
            Grid.SetRow(columns, 1);
            Grid.SetRow(okButton, 2);

            root.Children.Add(note);
            root.Children.Add(columns);
            root.Children.Add(okButton);
            dialog.Content = root;
            dialog.ShowDialog();
        }

        private string GetShortcutHelpBody(string group) =>
            string.Join(
                Environment.NewLine,
                MainShortcuts
                    .Where(shortcut => shortcut.Group == group)
                    .Select(shortcut => $"{shortcut.GestureText}：{shortcut.Description}"));

        private static StackPanel CreateShortcutColumn(params (string Title, string Body)[] sections)
        {
            var panel = new StackPanel { Orientation = Orientation.Vertical };
            foreach (var (title, body) in sections)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = title,
                    FontSize = 17,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, panel.Children.Count == 0 ? 0 : 14, 0, 4)
                });
                panel.Children.Add(CreateShortcutTextBlock(body));
            }

            return panel;
        }

        private static TextBlock CreateShortcutTextBlock(string text) => new()
        {
            Text = text,
            FontSize = 14,
            LineHeight = 22,
            TextWrapping = TextWrapping.Wrap
        };

        private void License_Click(object sender, RoutedEventArgs e)
        {
            string path = System.IO.Path.Combine(AppContext.BaseDirectory, "ThirdPartyNotices.txt");
            if (System.IO.File.Exists(path))
            {
                try { Process.Start(new ProcessStartInfo { FileName = "notepad.exe", Arguments = $"\"{path}\"", UseShellExecute = true }); }
                catch (Exception ex) { MessageBox.Show($"无法打开许可文件: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
            else
            {
                MessageBox.Show("许可文件未找到。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        #endregion

        #region 菜单栏：显示控制
        private void OpenImageReview_Click(object sender, RoutedEventArgs e)
        {
            FullScreenReview.IsOpen = true;
        }
        private void OnLayoutModeClicked(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem clickedItem) return;

            if (clickedItem.Tag is DisplayMode mode) SelectLayoutMode(mode);
        }

        private void SelectLayoutMode(DisplayMode mode)
        {
            AllDisplayMenuItem.IsChecked = mode == DisplayMode.ListAndTextBox;
            OnlyPicMenuItem.IsChecked = mode == DisplayMode.ImageOnly;
            ListOnlyMenuItem.IsChecked = mode == DisplayMode.ListOnly;
            TextBoxOnlyMenuItem.IsChecked = mode == DisplayMode.TextBoxOnly;
            UpdateLayout(mode);
        }

        private void UpdateLayout(DisplayMode mode)
        {
            // 共同的列宽设置
            LeftColumn.Width = new GridLength(1, GridUnitType.Star);
            RightColumn.Width = new GridLength(1, GridUnitType.Star);
            MiddleColumn.Width = new GridLength(1, GridUnitType.Auto);

            switch (mode)
            {
                case DisplayMode.ImageOnly:
                    RightColumn.Width = new GridLength(0);
                    MiddleColumn.Width = new GridLength(0);
                    break;

                case DisplayMode.ListAndTextBox:
                    LabelEditPanel.IsListVisible = true;
                    LabelEditPanel.IsTextBoxVisible = true;
                    break;

                case DisplayMode.ListOnly:
                    LabelEditPanel.IsListVisible = true;
                    LabelEditPanel.IsTextBoxVisible = false;
                    break;

                case DisplayMode.TextBoxOnly:
                    LabelEditPanel.IsListVisible = false;
                    LabelEditPanel.IsTextBoxVisible = true;
                    break;
            }
        }
        #endregion

        #region 快捷键
        private LabelSnapshot? _labelClipboard;
        private IReadOnlyList<ShortcutEntry>? _mainShortcuts;

        private enum ShortcutScope
        {
            Global,
            NonTextEditing
        }

        private sealed record ShortcutEntry(
            string Group,
            string GestureText,
            string Description,
            ShortcutScope Scope,
            Key Key,
            ModifierKeys Modifiers,
            Func<OneProject, bool> Execute,
            Key? AlternateKey = null)
        {
            public bool Matches(Key key, ModifierKeys modifiers) =>
                Modifiers == modifiers && (Key == key || AlternateKey == key);
        }

        private IReadOnlyList<ShortcutEntry> MainShortcuts => _mainShortcuts ??= CreateMainShortcuts();

        private IReadOnlyList<ShortcutEntry> CreateMainShortcuts() =>
        [
            new("全局：文件", "Ctrl+N", "为文件夹新建", ShortcutScope.Global,
                Key.N, ModifierKeys.Control, vm => ExecuteCommand(vm.NewFolderCommand)),
            new("全局：文件", "Ctrl+Shift+N", "为压缩包新建", ShortcutScope.Global,
                Key.N, ModifierKeys.Control | ModifierKeys.Shift, vm => ExecuteCommand(vm.NewZipCommand)),
            new("全局：文件", "Ctrl+O", "导入翻译", ShortcutScope.Global,
                Key.O, ModifierKeys.Control, vm => ExecuteCommand(vm.OpenTxtCommand)),
            new("全局：文件", "Alt+O", "预览压缩包", ShortcutScope.Global,
                Key.O, ModifierKeys.Alt, vm => ExecuteCommand(vm.OpenZipCommand)),
            new("全局：文件", "Ctrl+Shift+O", "预览文件夹", ShortcutScope.Global,
                Key.O, ModifierKeys.Control | ModifierKeys.Shift, vm => ExecuteCommand(vm.OpenFolderCommand)),
            new("全局：文件", "Ctrl+Alt+O", "选择图片", ShortcutScope.Global,
                Key.O, ModifierKeys.Control | ModifierKeys.Alt, vm => ExecuteCommand(vm.OpenImagesCommand)),
            new("全局：文件", "Ctrl+S", "保存翻译", ShortcutScope.Global,
                Key.S, ModifierKeys.Control, vm => ExecuteCommand(vm.SaveCommand)),
            new("全局：文件", "Ctrl+Shift+S", "翻译另存为", ShortcutScope.Global,
                Key.S, ModifierKeys.Control | ModifierKeys.Shift, vm => ExecuteCommand(vm.SaveCommand, "As")),
            new("全局：文件", "Ctrl+F", "打开图片所在文件夹", ShortcutScope.Global,
                Key.F, ModifierKeys.Control, vm => ExecuteCommand(vm.OpenWorkFolderCommand)),

            new("全局：标签导航", "Tab", "循环选择并编辑下一个标签", ShortcutScope.Global,
                Key.Tab, ModifierKeys.None, vm => SelectAdjacentLabel(vm, 1)),
            new("全局：标签导航", "Shift+Tab", "循环选择并编辑上一个标签", ShortcutScope.Global,
                Key.Tab, ModifierKeys.Shift, vm => SelectAdjacentLabel(vm, -1)),
            new("全局：标签导航", "Esc", "退出编辑并清除选中标签", ShortcutScope.Global,
                Key.Escape, ModifierKeys.None, ClearLabelEditing),

            new("非编辑时：标签操作", "Ctrl+Z", "撤销", ShortcutScope.NonTextEditing,
                Key.Z, ModifierKeys.Control, vm => ExecuteCommand(vm.SelectedImage?.UndoCommand)),
            new("非编辑时：标签操作", "Ctrl+Y", "重做", ShortcutScope.NonTextEditing,
                Key.Y, ModifierKeys.Control, vm => ExecuteCommand(vm.SelectedImage?.RedoCommand)),
            new("非编辑时：标签操作", "Enter", "编辑当前标签", ShortcutScope.NonTextEditing,
                Key.Enter, ModifierKeys.None, BeginEditSelectedLabel),
            new("非编辑时：标签操作", "Delete", "删除当前标签", ShortcutScope.NonTextEditing,
                Key.Delete, ModifierKeys.None, DeleteSelectedLabel),
            new("非编辑时：标签操作", "Ctrl+C", "复制当前标签", ShortcutScope.NonTextEditing,
                Key.C, ModifierKeys.Control, CopySelectedLabel),
            new("非编辑时：标签操作", "Ctrl+V", "粘贴为新标签", ShortcutScope.NonTextEditing,
                Key.V, ModifierKeys.Control, PasteCopiedLabel),
            new("非编辑时：标签操作", "Ctrl+Shift+V", "将文本粘贴到当前标签", ShortcutScope.NonTextEditing,
                Key.V, ModifierKeys.Control | ModifierKeys.Shift, ApplyCopiedLabelText),

            new("非编辑时：图片与视图", "A", "上一张图片", ShortcutScope.NonTextEditing,
                Key.A, ModifierKeys.None, vm => ExecuteCommand(vm.PreviousImageCommand)),
            new("非编辑时：图片与视图", "D", "下一张图片", ShortcutScope.NonTextEditing,
                Key.D, ModifierKeys.None, vm => ExecuteCommand(vm.NextImageCommand)),
            new("非编辑时：图片与视图", "F", "适应视图", ShortcutScope.NonTextEditing,
                Key.F, ModifierKeys.None, _ => { PicView.FitToView(); return true; }),
            new("非编辑时：图片与视图", "1", "浏览模式", ShortcutScope.NonTextEditing,
                Key.D1, ModifierKeys.None, _ => { SeeBtn.IsChecked = true; return true; }, Key.NumPad1),
            new("非编辑时：图片与视图", "2", "标记模式", ShortcutScope.NonTextEditing,
                Key.D2, ModifierKeys.None, _ => { LabelBtn.IsChecked = true; return true; }, Key.NumPad2),
            new("非编辑时：图片与视图", "3", "截图模式", ShortcutScope.NonTextEditing,
                Key.D3, ModifierKeys.None, _ => { ScreenshotBtn.IsChecked = true; return true; }, Key.NumPad3),
            new("非编辑时：图片与视图", "Ctrl+1", "全部显示", ShortcutScope.NonTextEditing,
                Key.D1, ModifierKeys.Control, _ => { SelectLayoutMode(DisplayMode.ListAndTextBox); return true; }, Key.NumPad1),
            new("非编辑时：图片与视图", "Ctrl+2", "仅图片", ShortcutScope.NonTextEditing,
                Key.D2, ModifierKeys.Control, _ => { SelectLayoutMode(DisplayMode.ImageOnly); return true; }, Key.NumPad2),
            new("非编辑时：图片与视图", "Ctrl+3", "仅列表", ShortcutScope.NonTextEditing,
                Key.D3, ModifierKeys.Control, _ => { SelectLayoutMode(DisplayMode.ListOnly); return true; }, Key.NumPad3),
            new("非编辑时：图片与视图", "Ctrl+4", "仅文本框", ShortcutScope.NonTextEditing,
                Key.D4, ModifierKeys.Control, _ => { SelectLayoutMode(DisplayMode.TextBoxOnly); return true; }, Key.NumPad4),
            new("非编辑时：图片与视图", "F2", "显示/隐藏工具箱", ShortcutScope.NonTextEditing,
                Key.F2, ModifierKeys.None, _ => { DrawerTrigger.IsChecked = DrawerTrigger.IsChecked != true; return true; }),
            new("非编辑时：图片与视图", "F3", "文本校对", ShortcutScope.NonTextEditing,
                Key.F3, ModifierKeys.None, _ => { TextReviewToggle.IsChecked = TextReviewToggle.IsChecked != true; return true; }),
            new("非编辑时：图片与视图", "F4", "图片校对", ShortcutScope.NonTextEditing,
                Key.F4, ModifierKeys.None, _ => { FullScreenReview.IsOpen = true; return true; }),
        ];

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (FullScreenReview.IsOpen) return; // 图校界面打开时禁用快捷键，避免冲突

            var key = GetActualKey(e);
            var modifiers = Keyboard.Modifiers;
            if (IsTabNavigationShortcut(key, modifiers))
                e.Handled = true;

            if (DataContext is not OneProject vm)
                return;

            var shortcut = FindShortcut(key, modifiers, IsTextInputFocused(e));
            if (shortcut == null)
                return;

            e.Handled = true;
            if (!e.IsRepeat)
                shortcut.Execute(vm);
        }

        private ShortcutEntry? FindShortcut(Key key, ModifierKeys modifiers, bool isTextInputFocused) =>
            MainShortcuts.FirstOrDefault(shortcut =>
                shortcut.Matches(key, modifiers)
                && (!isTextInputFocused || shortcut.Scope == ShortcutScope.Global));

        private static bool IsTabNavigationShortcut(Key key, ModifierKeys modifiers) =>
            key == Key.Tab && (modifiers == ModifierKeys.None || modifiers == ModifierKeys.Shift);

        private static bool IsTextInputFocused(KeyEventArgs e) =>
            e.OriginalSource is TextBox || Keyboard.FocusedElement is TextBox;

        private static Key GetActualKey(KeyEventArgs e) => e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            Key.DeadCharProcessed => e.DeadCharProcessedKey,
            _ => e.Key
        };

        private static bool ExecuteCommand(ICommand? command, object? parameter = null)
        {
            if (command?.CanExecute(parameter) != true)
                return false;

            command.Execute(parameter);
            return true;
        }

        private bool SelectAdjacentLabel(OneProject vm, int delta)
        {
            if (vm.SelectedImage is not { } image)
                return false;

            var labels = image.ActiveLabelsView.Cast<OneLabel>().Where(label => !label.IsDeleted).ToList();
            if (labels.Count == 0)
                return false;

            var currentIndex = image.SelectedLabel is null ? -1 : labels.IndexOf(image.SelectedLabel);
            var nextIndex = currentIndex < 0
                ? (delta > 0 ? 0 : labels.Count - 1)
                : (currentIndex + delta + labels.Count) % labels.Count;

            image.SelectedLabel = labels[nextIndex];
            BeginEditSelectedLabel(vm);
            return true;
        }

        private bool BeginEditSelectedLabel(OneProject vm)
        {
            if (vm.SelectedImage?.SelectedLabel is not { } label)
                return false;

            if (RightColumn.Width.Value > 0 && LabelEditPanel.IsTextBoxVisible)
            {
                PicView.EditingLabel = null;
                LabelEditPanel.FocusTextEditor();
            }
            else
            {
                PicView.EditingLabel = label;
            }

            return true;
        }

        private bool ClearLabelEditing(OneProject vm)
        {
            var hadSelection = vm.SelectedImage?.SelectedLabel != null;
            var hadInlineEdit = PicView.EditingLabel != null;

            PicView.EditingLabel = null;
            LabelEditPanel.ClearTextEditorFocus();
            Keyboard.ClearFocus();

            if (vm.SelectedImage != null)
                vm.SelectedImage.SelectedLabel = null;

            return hadSelection || hadInlineEdit;
        }

        private static bool DeleteSelectedLabel(OneProject vm)
        {
            if (vm.SelectedImage is not { } image || image.SelectedLabel is null)
                return false;

            if (!image.DeleteLabelCommand.CanExecute(image.SelectedLabel))
                return false;

            image.DeleteLabelCommand.Execute(image.SelectedLabel);
            return true;
        }

        private bool CopySelectedLabel(OneProject vm)
        {
            if (vm.SelectedImage?.SelectedLabel is not { } label)
                return false;

            _labelClipboard = new LabelSnapshot(label);
            vm.MsgQueue.Enqueue("已复制当前标签");
            return true;
        }

        private bool PasteCopiedLabel(OneProject vm)
        {
            if (_labelClipboard == null || vm.SelectedImage is not { } image)
                return false;

            image.PasteLabel(_labelClipboard);
            BeginEditSelectedLabel(vm);
            vm.MsgQueue.Enqueue("已粘贴标签");
            return true;
        }

        private bool ApplyCopiedLabelText(OneProject vm)
        {
            if (_labelClipboard == null || vm.SelectedImage?.SelectedLabel == null)
                return false;

            vm.SelectedImage.ApplyLabelContent(_labelClipboard);
            BeginEditSelectedLabel(vm);
            vm.MsgQueue.Enqueue("已套用复制标签文本");
            return true;
        }

        private void CopySelectedLabel_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is OneProject vm) CopySelectedLabel(vm);
        }

        private void PasteLabel_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is OneProject vm) PasteCopiedLabel(vm);
        }

        private void ApplyCopiedLabelText_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is OneProject vm) ApplyCopiedLabelText(vm);
        }
        #endregion
    }
}
