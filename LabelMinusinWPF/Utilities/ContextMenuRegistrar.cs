using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace LabelMinusinWPF.Utilities
{
    public static class ContextMenuRegistrar
    {
        private const string OpenKey = "LabelMinus.Open";
        private const string ReviewKey = "LabelMinus.Review";

        private static string GetExecutablePath()
        {
            return Assembly.GetExecutingAssembly().Location.Replace(".dll", ".exe");
        }

        private static IEnumerable<string> GetTargetExtensions()
        {
            var extensions = new List<string> { ".txt", ".zip", ".rar", ".7z" };
            var imgExts = ProjectService.ImageExtensions;
            return extensions.Concat(imgExts).Distinct().Where(s => !string.IsNullOrEmpty(s));
        }

        public static bool IsRegistered()
        {
            try
            {
                // 只要检查文件夹下是否有我们的菜单即可判断
                using var key = Registry.ClassesRoot.OpenSubKey(@"Directory\shell\" + OpenKey);
                return key != null;
            }
            catch
            {
                return false;
            }
        }

        public static void RegisterAll()
        {
            string exePath = GetExecutablePath();

            // 准备两个命令：一个不带参数(Open)，一个带--review(Review)
            string openCommand = $"\"{exePath}\" \"%1\"";
            string reviewCommand = $"\"{exePath}\" --review \"%1\"";

            // 1. 处理文件夹 (Directory)
            RegisterMenuItem(@"Directory", OpenKey, "使用 LabelMinus 打开", openCommand, exePath);
            RegisterMenuItem(
                @"Directory",
                ReviewKey,
                "使用 LabelMinus 图校",
                reviewCommand,
                exePath
            );

            // 2. 处理具体文件后缀
            foreach (var ext in GetTargetExtensions())
            {
                string root = $@"SystemFileAssociations\{ext}";
                RegisterMenuItem(root, OpenKey, "使用 LabelMinus 打开", openCommand, exePath);
                RegisterMenuItem(root, ReviewKey, "使用 LabelMinus 图校", reviewCommand, exePath);
            }
        }

        public static void UnregisterAll()
        {
            string[] roots = { @"Directory" };
            var allRoots = roots.Concat(
                GetTargetExtensions().Select(ext => $@"SystemFileAssociations\{ext}")
            );

            foreach (var root in allRoots)
            {
                DeleteKeyTree($@"{root}\shell\{OpenKey}");
                DeleteKeyTree($@"{root}\shell\{ReviewKey}");
            }
        }

        private static void RegisterMenuItem(
            string rootPath,
            string keyName,
            string text,
            string command,
            string icon
        )
        {
            using var key = Registry.ClassesRoot.CreateSubKey($@"{rootPath}\shell\{keyName}");
            if (key != null)
            {
                key.SetValue("", text);
                key.SetValue("Icon", icon);
                using var cmdKey = key.CreateSubKey("command");
                cmdKey?.SetValue("", command);
            }
        }

        private static void DeleteKeyTree(string path)
        {
            Registry.ClassesRoot.DeleteSubKeyTree(path, false);
        }
    }
}
