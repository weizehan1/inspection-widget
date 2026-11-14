using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using InspectionWidget.Config;

namespace InspectionWidget.Storage
{
    /// <summary>
    /// 巡检目录导航：优先使用本轮实际成功落盘路径的所属目录，
    /// 无落盘记录时退化到配置日期目录；只判断存在性，不创建目录。
    /// </summary>
    public sealed class InspectionFolderNavigator
    {
        private readonly Func<string, bool> _directoryExists;
        private readonly Action<string> _open;

        public InspectionFolderNavigator()
            : this(new Func<string, bool>(Directory.Exists), new Action<string>(OpenFolder))
        {
        }

        public InspectionFolderNavigator(Func<string, bool> directoryExists, Action<string> open)
        {
            _directoryExists = directoryExists ?? new Func<string, bool>(Directory.Exists);
            _open = open ?? new Action<string>(OpenFolder);
        }

        public List<string> ResolveFolders(string rootPath, string folderPattern,
            DateTime date, IEnumerable<string> savedPaths)
        {
            string configured = ConfiguredDirectory(rootPath, folderPattern, date);
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool hasSavedPaths = false;

            if (savedPaths != null)
            {
                foreach (string savedPath in savedPaths)
                {
                    if (string.IsNullOrWhiteSpace(savedPath))
                        continue;
                    hasSavedPaths = true;
                    string folder = ParentDirectory(savedPath);
                    if (string.IsNullOrEmpty(folder))
                        continue;
                    bool exists;
                    try
                    {
                        exists = _directoryExists(folder);
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    if (!exists)
                        continue;
                    if (seen.Add(NormalizeDirectory(folder)))
                        result.Add(folder);
                }
            }

            if (!hasSavedPaths && configured != null)
            {
                bool exists;
                try
                {
                    exists = _directoryExists(configured);
                }
                catch (Exception)
                {
                    exists = false;
                }
                if (exists && seen.Add(NormalizeDirectory(configured)))
                    result.Add(configured);
            }

            // 配置当日目录确有本轮截图时排首；否则保持落盘顺序。
            if (hasSavedPaths && configured != null)
            {
                string key = NormalizeDirectory(configured);
                for (int i = 0; i < result.Count; i++)
                {
                    if (!string.Equals(NormalizeDirectory(result[i]), key,
                        StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (i > 0)
                    {
                        string today = result[i];
                        result.RemoveAt(i);
                        result.Insert(0, today);
                    }
                    break;
                }
            }
            return result;
        }

        public bool TryOpenFolder(string folder, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(folder))
            {
                error = "巡检文件夹路径为空。";
                return false;
            }

            bool exists;
            try
            {
                exists = _directoryExists(folder);
            }
            catch (Exception ex)
            {
                error = "无法检查巡检文件夹：" + ex.Message;
                return false;
            }
            if (!exists)
            {
                error = "巡检文件夹不存在：" + folder;
                return false;
            }

            try
            {
                _open(folder);
                return true;
            }
            catch (Exception ex)
            {
                error = "打开巡检文件夹失败：" + ex.Message;
                return false;
            }
        }

        private static string ConfiguredDirectory(string rootPath, string folderPattern, DateTime date)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
                return null;
            string pattern = FolderNamePattern.Validate(folderPattern) == null
                ? folderPattern
                : FolderNamePattern.Default;
            try
            {
                return Path.Combine(rootPath, FolderNamePattern.Format(pattern, date));
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>同时兼容 Windows 反斜杠与 Unix 斜杠，避免跨平台取错父目录。</summary>
        private static string ParentDirectory(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;
            int index = path.LastIndexOfAny(new[] { '\\', '/' });
            if (index < 0)
                return null;
            if (index == 2 && path.Length >= 3 && path[1] == ':')
                return path.Substring(0, 3);
            return index == 0 ? path.Substring(0, 1) : path.Substring(0, index);
        }

        private static string NormalizeDirectory(string folder)
        {
            if (folder == null)
                return null;
            string normalized = folder.Replace('\\', '/');
            while (normalized.Length > 1 && normalized[normalized.Length - 1] == '/')
            {
                if (normalized.Length == 3 && normalized[1] == ':')
                    break;
                normalized = normalized.Substring(0, normalized.Length - 1);
            }
            return normalized;
        }

        private static void OpenFolder(string folder)
        {
            var start = new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            };
            Process.Start(start);
        }
    }
}
