using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Win32;

namespace LabelMinusinWPF.Utilities
{
    public static class ContextMenuRegistrar
    {
        private const string OpenKey = "LabelMinus.Open";
        private const string ReviewKey = "LabelMinus.Review";

        private const string BasePath = @"Software\Classes";

        private static string GetExecutablePath()
        {
            return Assembly.GetExecutingAssembly().Location.Replace(".dll", ".exe");
        }

        private static IEnumerable<string> GetTargetExtensions()
        {
            return new List<string>
            {
                ".txt",
                ".zip",
                ".rar",
                ".7z"
            }
            .Concat(ProjectService.ImageExtensions)
            .Where(ext => !string.IsNullOrWhiteSpace(ext))
            .Select(ext => ext.StartsWith(".") ? ext : "." + ext)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        public static bool IsRegistered()
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                $@"{BasePath}\Directory\shell\{OpenKey}");
            return key != null;
        }

        public static void RegisterAll()
        {
            string exePath = GetExecutablePath();

            string openCommand = $"\"{exePath}\" \"%1\"";
            string reviewCommand = $"\"{exePath}\" --review \"%1\"";

            // 1️⃣ 文件夹
            RegisterMenu(@"Directory", OpenKey, "使用 LabelMinus 打开", openCommand, exePath);
            RegisterMenu(@"Directory", ReviewKey, "使用 LabelMinus 图校", reviewCommand, exePath);

            // 2️⃣ 指定扩展
            foreach (var ext in GetTargetExtensions())
            {
                string root = $@"SystemFileAssociations\{ext}";
                RegisterMenu(root, OpenKey, "使用 LabelMinus 打开", openCommand, exePath);
                RegisterMenu(root, ReviewKey, "使用 LabelMinus 图校", reviewCommand, exePath);
            }
        }

        public static void UnregisterAll()
        {
            var roots = new List<string> { "Directory" }
                .Concat(GetTargetExtensions()
                .Select(ext => $@"SystemFileAssociations\{ext}"));

            foreach (var root in roots)
            {
                DeleteKey($@"{BasePath}\{root}\shell\{OpenKey}");
                DeleteKey($@"{BasePath}\{root}\shell\{ReviewKey}");
            }
        }

        private static void RegisterMenu(
            string root,
            string keyName,
            string text,
            string command,
            string iconPath)
        {
            using var key = Registry.CurrentUser.CreateSubKey(
                $@"{BasePath}\{root}\shell\{keyName}");

            if (key == null) return;

            key.SetValue("", text);
            key.SetValue("Icon", iconPath);

            using var cmdKey = key.CreateSubKey("command");
            cmdKey?.SetValue("", command);
        }

        private static void DeleteKey(string fullPath)
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(fullPath, false);
            }
            catch
            {
                // 忽略删除失败
            }
        }
    }
}