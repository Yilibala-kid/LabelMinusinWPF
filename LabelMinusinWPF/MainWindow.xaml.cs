using LabelMinusinWPF.Common;
using LabelMinusinWPF.OCRService;
using LabelMinusinWPF.SelfControls;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Constants = LabelMinusinWPF.Common.Constants;

namespace LabelMinusinWPF;

public enum DisplayMode
{
    ImageOnly,
    ListAndTextBox,
    ListOnly,
    TextBoxOnly
}

public partial class MainWindow : Window
{
    private LabelSnapshot? _labelClipboard;
    private IReadOnlyList<ShortcutEntry>? _mainShortcuts;

    public MainWindow()
    {
        InitializeComponent();
        ClearTempFoldersInBackground();
        Closing += MainWindow_Closing;
        OcrPanel.Attach(PicView, this);
        AppSettingsService.InitializeMainWindow(this);
    }

    private OneProject? ViewModel => DataContext as OneProject;

    #region 生命周期

    private static void ClearTempFoldersInBackground() =>
        Task.Run(() => ProjectManager.ClearTempFolders(
            OcrConstants.OcrTemp,
            Constants.TempFolders.ScreenShotTemp,
            Constants.TempFolders.ArchiveTemp));

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (ViewModel is not { } viewModel || !viewModel.HasUnsavedChanges())
            return;

        var result = MessageBox.Show(
            "当前翻译有未保存的修改，是否保存？",
            "提示",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        switch (result)
        {
            case MessageBoxResult.Yes:
                viewModel.SaveCommand.Execute(null);
                break;
            case MessageBoxResult.Cancel:
                e.Cancel = true;
                break;
        }
    }

    #endregion

    #region 设置与 OCR

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsDialog { Owner = this };
        if (window.ShowDialog() == true)
            OcrPanel.RefreshWebsiteSelector();
    }

    private async void SetupOcrEnv_Click(object sender, RoutedEventArgs e) =>
        await OcrPanel.InstallEnvironmentAsync();

    private async void AutoOcr_Click(object sender, RoutedEventArgs e) =>
        await OcrPanel.RunAutoDotAsync();

    private async void AutoOcr_Batch(object sender, RoutedEventArgs e) =>
        await OcrPanel.RunBatchAsync(IsJapaneseOcrMenu(sender)
            ? OcrEngineKind.Manga
            : OcrEngineKind.Paddle);

    private static bool IsJapaneseOcrMenu(object sender) =>
        sender is MenuItem { Tag: string tag } && tag == "JP";

    private void OcrHelp_Click(object sender, RoutedEventArgs e) =>
        OcrPanel.ShowHelp();

    #endregion

    #region 图片视图控制

    private void FitToPage_Click(object sender, RoutedEventArgs e) => PicView.FitToView();
    private void FitWidth_Click(object sender, RoutedEventArgs e) => PicView.FitToWidth();
    private void FitHeight_Click(object sender, RoutedEventArgs e) => PicView.FitToHeight();
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => PicView.ZoomScale *= 1.1;
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => PicView.ZoomScale *= 0.9;

    #endregion

    #region 拖放文件

    private void OnFileDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnFileDrop(object sender, DragEventArgs e)
    {
        if (ViewModel is null || !e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            ViewModel.OpenResourceByPath(files, false);
    }

    #endregion

    #region 帮助与外部链接

    private void OnOpenWebsite(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string url })
            OpenExternal(url, "无法打开网页");
    }

    private void About_Click(object sender, RoutedEventArgs e) =>
        ViewModel?.MsgQueue.Enqueue("本程序由No-Hifuu友情赞助");

    private void License_Click(object sender, RoutedEventArgs e)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "ThirdPartyNotices.txt");
        if (!File.Exists(path))
        {
            MessageBox.Show("许可文件未找到。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        OpenExternal("notepad.exe", "无法打开许可文件", $"\"{path}\"");
    }

    private static void OpenExternal(string fileName, string errorPrefix, string? arguments = null)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments ?? "",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{errorPrefix}: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Shortcuts_Click(object sender, RoutedEventArgs e) =>
        CreateShortcutDialog().ShowDialog();

    private Window CreateShortcutDialog()
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

        root.Children.Add(CreateShortcutNote());
        AddGridChild(root, CreateShortcutColumns(), row: 1);
        AddGridChild(root, CreateDialogOkButton(dialog), row: 2);

        dialog.Content = root;
        return dialog;
    }

    private static void AddGridChild(Grid grid, UIElement child, int row, int column = 0)
    {
        Grid.SetRow(child, row);
        Grid.SetColumn(child, column);
        grid.Children.Add(child);
    }

    private static TextBlock CreateShortcutNote() => new()
    {
        Text = "说明：全局快捷键始终生效；“非编辑时”快捷键仅在文本框没有焦点时生效，编辑文字时会优先使用文本框自己的 Ctrl+Z / Ctrl+Y / Ctrl+C / Ctrl+V 等操作。",
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 14),
        Foreground = SystemColors.GrayTextBrush
    };

    private Grid CreateShortcutColumns()
    {
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        columns.Children.Add(CreateShortcutColumn(
            ("全局：文件", GetShortcutHelpBody("全局：文件")),
            ("全局：标签导航", GetShortcutHelpBody("全局：标签导航")),
            ("非编辑时：标签操作", GetShortcutHelpBody("非编辑时：标签操作"))));
        AddGridChild(columns, CreateShortcutColumn(
            ("非编辑时：图片与视图", GetShortcutHelpBody("非编辑时：图片与视图")),
            ("图校界面", ImageReviewShortcutHelp)), row: 0, column: 2);

        return columns;
    }

    private static Button CreateDialogOkButton(Window dialog)
    {
        var button = new Button
        {
            Content = "确定",
            Width = 88,
            Margin = new Thickness(0, 16, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            IsDefault = true,
            IsCancel = true
        };
        button.Click += (_, _) => dialog.Close();
        return button;
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

    private const string ImageReviewShortcutHelp = """
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
        """;

    #endregion

    #region 显示布局

    private void OpenImageReview_Click(object sender, RoutedEventArgs e) =>
        FullScreenReview.IsOpen = true;

    private void OnLayoutModeClicked(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: DisplayMode mode })
            SelectLayoutMode(mode);
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
        ResetMainColumns();

        switch (mode)
        {
            case DisplayMode.ImageOnly:
                HideRightPanel();
                break;
            case DisplayMode.ListAndTextBox:
                SetEditorPanels(listVisible: true, textBoxVisible: true);
                break;
            case DisplayMode.ListOnly:
                SetEditorPanels(listVisible: true, textBoxVisible: false);
                break;
            case DisplayMode.TextBoxOnly:
                SetEditorPanels(listVisible: false, textBoxVisible: true);
                break;
        }
    }

    private void ResetMainColumns()
    {
        LeftColumn.Width = new GridLength(1, GridUnitType.Star);
        RightColumn.Width = new GridLength(1, GridUnitType.Star);
        MiddleColumn.Width = GridLength.Auto;
    }

    private void HideRightPanel()
    {
        RightColumn.Width = new GridLength(0);
        MiddleColumn.Width = new GridLength(0);
    }

    private void SetEditorPanels(bool listVisible, bool textBoxVisible)
    {
        LabelEditPanel.IsListVisible = listVisible;
        LabelEditPanel.IsTextBoxVisible = textBoxVisible;
    }

    #endregion

    #region 快捷键定义

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

    private IReadOnlyList<ShortcutEntry> MainShortcuts =>
        _mainShortcuts ??= CreateMainShortcuts();

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
            Key.F, ModifierKeys.None, _ => ExecuteAction(PicView.FitToView)),
        new("非编辑时：图片与视图", "1", "浏览模式", ShortcutScope.NonTextEditing,
            Key.D1, ModifierKeys.None, _ => ExecuteAction(() => SeeBtn.IsChecked = true), Key.NumPad1),
        new("非编辑时：图片与视图", "2", "标记模式", ShortcutScope.NonTextEditing,
            Key.D2, ModifierKeys.None, _ => ExecuteAction(() => LabelBtn.IsChecked = true), Key.NumPad2),
        new("非编辑时：图片与视图", "3", "截图模式", ShortcutScope.NonTextEditing,
            Key.D3, ModifierKeys.None, _ => ExecuteAction(() => ScreenshotBtn.IsChecked = true), Key.NumPad3),
        new("非编辑时：图片与视图", "Ctrl+1", "全部显示", ShortcutScope.NonTextEditing,
            Key.D1, ModifierKeys.Control, _ => ExecuteAction(() => SelectLayoutMode(DisplayMode.ListAndTextBox)), Key.NumPad1),
        new("非编辑时：图片与视图", "Ctrl+2", "仅图片", ShortcutScope.NonTextEditing,
            Key.D2, ModifierKeys.Control, _ => ExecuteAction(() => SelectLayoutMode(DisplayMode.ImageOnly)), Key.NumPad2),
        new("非编辑时：图片与视图", "Ctrl+3", "仅列表", ShortcutScope.NonTextEditing,
            Key.D3, ModifierKeys.Control, _ => ExecuteAction(() => SelectLayoutMode(DisplayMode.ListOnly)), Key.NumPad3),
        new("非编辑时：图片与视图", "Ctrl+4", "仅文本框", ShortcutScope.NonTextEditing,
            Key.D4, ModifierKeys.Control, _ => ExecuteAction(() => SelectLayoutMode(DisplayMode.TextBoxOnly)), Key.NumPad4),
        new("非编辑时：图片与视图", "F2", "显示/隐藏工具箱", ShortcutScope.NonTextEditing,
            Key.F2, ModifierKeys.None, _ => ExecuteAction(() => DrawerTrigger.IsChecked = DrawerTrigger.IsChecked != true)),
        new("非编辑时：图片与视图", "F3", "文本校对", ShortcutScope.NonTextEditing,
            Key.F3, ModifierKeys.None, _ => ExecuteAction(() => TextReviewToggle.IsChecked = TextReviewToggle.IsChecked != true)),
        new("非编辑时：图片与视图", "F4", "图片校对", ShortcutScope.NonTextEditing,
            Key.F4, ModifierKeys.None, _ => ExecuteAction(() => FullScreenReview.IsOpen = true)),
    ];

    #endregion

    #region 快捷键处理

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (FullScreenReview.IsOpen)
            return;

        var key = GetActualKey(e);
        var modifiers = Keyboard.Modifiers;
        if (IsTabNavigationShortcut(key, modifiers))
            e.Handled = true;

        if (ViewModel is not { } viewModel)
            return;

        var shortcut = FindShortcut(key, modifiers, IsTextInputFocused(e));
        if (shortcut == null)
            return;

        e.Handled = true;
        if (!e.IsRepeat)
            shortcut.Execute(viewModel);
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

    #endregion

    #region 快捷键动作

    private static bool ExecuteAction(Action action)
    {
        action();
        return true;
    }

    private static bool ExecuteCommand(ICommand? command, object? parameter = null)
    {
        if (command?.CanExecute(parameter) != true)
            return false;

        command.Execute(parameter);
        return true;
    }

    private bool SelectAdjacentLabel(OneProject viewModel, int delta)
    {
        if (viewModel.SelectedImage is not { } image)
            return false;

        var labels = image.ActiveLabelsView.Cast<OneLabel>().Where(label => !label.IsDeleted).ToList();
        if (labels.Count == 0)
            return false;

        var currentIndex = image.SelectedLabel is null ? -1 : labels.IndexOf(image.SelectedLabel);
        var nextIndex = currentIndex < 0
            ? (delta > 0 ? 0 : labels.Count - 1)
            : (currentIndex + delta + labels.Count) % labels.Count;

        image.SelectedLabel = labels[nextIndex];
        BeginEditSelectedLabel(viewModel);
        return true;
    }

    private bool BeginEditSelectedLabel(OneProject viewModel)
    {
        if (viewModel.SelectedImage?.SelectedLabel is not { } label)
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

    private bool ClearLabelEditing(OneProject viewModel)
    {
        var hadSelection = viewModel.SelectedImage?.SelectedLabel != null;
        var hadInlineEdit = PicView.EditingLabel != null;

        PicView.EditingLabel = null;
        LabelEditPanel.ClearTextEditorFocus();
        Keyboard.ClearFocus();

        if (viewModel.SelectedImage != null)
            viewModel.SelectedImage.SelectedLabel = null;

        return hadSelection || hadInlineEdit;
    }

    private static bool DeleteSelectedLabel(OneProject viewModel)
    {
        if (viewModel.SelectedImage is not { } image || image.SelectedLabel is null)
            return false;

        if (!image.DeleteLabelCommand.CanExecute(image.SelectedLabel))
            return false;

        image.DeleteLabelCommand.Execute(image.SelectedLabel);
        return true;
    }

    private bool CopySelectedLabel(OneProject viewModel)
    {
        if (viewModel.SelectedImage?.SelectedLabel is not { } label)
            return false;

        _labelClipboard = new LabelSnapshot(label);
        viewModel.MsgQueue.Enqueue("已复制当前标签");
        return true;
    }

    private bool PasteCopiedLabel(OneProject viewModel)
    {
        if (_labelClipboard == null || viewModel.SelectedImage is not { } image)
            return false;

        image.PasteLabel(_labelClipboard);
        BeginEditSelectedLabel(viewModel);
        viewModel.MsgQueue.Enqueue("已粘贴标签");
        return true;
    }

    private bool ApplyCopiedLabelText(OneProject viewModel)
    {
        if (_labelClipboard == null || viewModel.SelectedImage?.SelectedLabel == null)
            return false;

        viewModel.SelectedImage.ApplyLabelContent(_labelClipboard);
        BeginEditSelectedLabel(viewModel);
        viewModel.MsgQueue.Enqueue("已套用复制标签文本");
        return true;
    }

    private void CopySelectedLabel_Click(object sender, RoutedEventArgs e) =>
        ExecuteWithViewModel(CopySelectedLabel);

    private void PasteLabel_Click(object sender, RoutedEventArgs e) =>
        ExecuteWithViewModel(PasteCopiedLabel);

    private void ApplyCopiedLabelText_Click(object sender, RoutedEventArgs e) =>
        ExecuteWithViewModel(ApplyCopiedLabelText);

    private void ExecuteWithViewModel(Func<OneProject, bool> action)
    {
        if (ViewModel is { } viewModel)
            action(viewModel);
    }

    #endregion
}
