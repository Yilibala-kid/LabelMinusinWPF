using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace LabelMinusinWPF.Common
{
    // --- 组名常量 ---
    public static class GroupConstants
    {
        public const string InBox = "框内";
        public const string OutBox = "框外";
        public static readonly string[] Default = [InBox, OutBox];
        public static readonly SolidColorBrush[] Brushes =
        [
            new(Color.FromRgb(234, 67, 53)),
            new(Color.FromRgb(66, 133, 244)),
            new(Color.FromRgb(52, 168, 83)),
            new(Color.FromRgb(251, 188, 4)),
            new(Color.FromRgb(171, 71, 188)),
            new(Color.FromRgb(0, 172, 193)),
            new(Color.FromRgb(255, 112, 67)),
            new(Color.FromRgb(141, 110, 99)),
        ];
    }

    public partial class GroupManager : ObservableObject
    {
        // --- 单例 ---
        public static GroupManager Instance { get; } = new();

        // --- 实例成员：集合和状态 ---
        public ObservableCollection<string> AllGroups { get; } = [.. GroupConstants.Default];

        [ObservableProperty]
        private string? _selectedGroup = GroupConstants.Default[0];

        // --- 消息定义 ---
        // DeleteGroup 查询请求：GroupManager → OneProject
        public record DeleteGroupQueryMessage(string GroupName, TaskCompletionSource<List<OneLabel>> Tcs);
        // DeleteGroup 查询响应：OneProject → GroupManager
        public record DeleteGroupResponseMessage(string GroupName, List<OneLabel> Labels, TaskCompletionSource<List<OneLabel>> Tcs);
        // GroupManager 显示提示消息
        public record GroupManagerShowMessageMessage(string Message);
        // GroupManager SelectedGroup 改变 → OneProject 更新 label group
        public record GroupManagerSelectedGroupChangedMessage(string GroupName);

        public static string NormalizeGroupName(string? groupName) =>
            string.IsNullOrWhiteSpace(groupName) ? GroupConstants.InBox : groupName.Trim();

        private GroupManager()
        {
            // 注册 DeleteGroupResponseMessage 处理器
            WeakReferenceMessenger.Default.Register<DeleteGroupResponseMessage>(this, (r, m) =>
            {
                m.Tcs.TrySetResult(m.Labels);
            });
        }

        // OneImage 直接调用的方法
        public void SetSelectedGroup(string? groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName)) return;
            SelectedGroup = NormalizeGroupName(groupName);
        }

        partial void OnSelectedGroupChanged(string? value)
        {
            if (value != null)
                WeakReferenceMessenger.Default.Send(new GroupManagerSelectedGroupChangedMessage(value));
        }

        // --- 命令 ---
        [RelayCommand]
        public void AddGroup(string? input)
        {
            string trimmed = input?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                WeakReferenceMessenger.Default.Send(new GroupManagerShowMessageMessage("组名不能为空"));
                return;
            }
            if (AllGroups.Contains(trimmed))
            {
                WeakReferenceMessenger.Default.Send(new GroupManagerShowMessageMessage($"组【{trimmed}】已存在"));
                return;
            }
            AllGroups.Add(trimmed);
            SelectedGroup = trimmed;
            WeakReferenceMessenger.Default.Send(new GroupManagerShowMessageMessage($"组【{trimmed}】已添加"));
        }

        [RelayCommand]
        public async Task DeleteGroupAsync(string? groupName)
        {
            string trimmed = groupName?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(trimmed)) return;
            if (GroupConstants.Default.Contains(trimmed))
            {
                WeakReferenceMessenger.Default.Send(new GroupManagerShowMessageMessage($"组【{trimmed}】为默认组，无法删除"));
                return;
            }

            var tcs = new TaskCompletionSource<List<OneLabel>>();
            WeakReferenceMessenger.Default.Send(new DeleteGroupQueryMessage(trimmed, tcs));

            var labels = await tcs.Task;
            if (labels.Count > 0)
            {
                WeakReferenceMessenger.Default.Send(new GroupManagerShowMessageMessage($"组【{trimmed}】有标签正在使用，无法删除"));
                return;
            }

            AllGroups.Remove(trimmed);
            if (string.Equals(SelectedGroup, trimmed, StringComparison.Ordinal))
                SelectedGroup = GroupConstants.InBox;
            WeakReferenceMessenger.Default.Send(new GroupManagerShowMessageMessage($"组【{trimmed}】已删除"));
        }

        public void SyncGroupsFromLabels(IEnumerable<string>? customGroups = null)
        {
            if (customGroups == null) return;

            var groupsToKeep = customGroups
                .Select(NormalizeGroupName)
                .Where(g => !GroupConstants.Default.Contains(g))
                .Concat(AllGroups.Where(g => !GroupConstants.Default.Contains(g)))
                .Distinct()
                .ToList();

            AllGroups.Clear();
            foreach (string g in GroupConstants.Default.Concat(groupsToKeep))
                AllGroups.Add(g);
        }


    }

    // 供 XAML 使用的 Converter: 根据组名返回对应的 Brush（循环使用预定义的颜色）
    public class GroupNameToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string normalized = GroupManager.NormalizeGroupName(value as string ?? "");
            int index = GroupManager.Instance.AllGroups.IndexOf(normalized);
            return GroupConstants.Brushes[index % GroupConstants.Brushes.Length];
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
