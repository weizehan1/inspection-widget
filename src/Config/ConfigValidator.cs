using System;
using System.Collections.Generic;
using System.IO;

namespace InspectionWidget.Config
{
    /// <summary>外部配置的纯校验：设备、路径、字段值和凭据结构。</summary>
    public static class ConfigValidator
    {
        private static readonly HashSet<string> ReservedDirectoryNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "CON", "PRN", "AUX", "NUL",
                "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
            };

        public static string ValidateDevices(IReadOnlyList<string> devices)
        {
            if (devices == null)
                return "设备列表不能为空。";

            var errors = new List<string>();
            for (int i = 0; i < devices.Count; i++)
            {
                var others = new List<string>();
                for (int j = 0; j < devices.Count; j++)
                {
                    if (j != i)
                        others.Add(devices[j]);
                }
                string error = DeviceNameValidator.Validate(devices[i], others);
                if (error != null)
                {
                    string name = devices[i] == null ? "（空值）" : "「" + devices[i] + "」";
                    errors.Add("第 " + (i + 1) + " 行" + name + "：" + error);
                }
            }
            return errors.Count == 0 ? null : string.Join("；", errors.ToArray());
        }

        public static string ValidateRootPath(string path)
        {
            if (path == null)
                return null;
            if (string.IsNullOrWhiteSpace(path))
                return "截图保存目录不能为空字符串。";
            string trimmed = path.TrimEnd(' ', '.');
            if (trimmed.Length != path.Length)
                return "截图保存目录不能以空格或点结尾。";
            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                if (char.IsControl(c))
                    return "截图保存目录不能包含控制字符。";
                if (c == '<' || c == '>' || c == '"' || c == '|' || c == '?' || c == '*'
                    || c == ':')
                {
                    if (c == ':' && i == 1 && char.IsLetter(trimmed[0])
                        && trimmed.Length > 2
                        && (trimmed[2] == '\\' || trimmed[2] == '/'))
                    {
                        continue;
                    }
                    return "截图保存目录不能包含 < > \" | ? * : 等字符（盘符后的冒号除外）。";
                }
            }

            bool isUnc = trimmed.StartsWith("\\\\", StringComparison.Ordinal);
            if (isUnc)
            {
                string[] parts = trimmed.Substring(2).Split(
                    new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    return "UNC 路径必须包含服务器名和共享名。";
                for (int i = 0; i < parts.Length; i++)
                {
                    if (IsReservedDirectoryName(parts[i]))
                        return "截图保存目录不能使用 Windows 保留名：" + parts[i];
                }
                return null;
            }

            if (!(trimmed.Length >= 3 && char.IsLetter(trimmed[0])
                && trimmed[1] == ':' && (trimmed[2] == '\\' || trimmed[2] == '/')))
                return "截图保存目录必须是绝对路径（盘符或 UNC）。";

            string[] segments = trimmed.Substring(3).Split(
                new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < segments.Length; i++)
            {
                if (IsReservedDirectoryName(segments[i]))
                    return "截图保存目录不能使用 Windows 保留名：" + segments[i];
            }
            return null;
        }

        private static bool IsReservedDirectoryName(string segment)
        {
            string main = segment.Split('.')[0].TrimEnd(' ');
            return ReservedDirectoryNames.Contains(main);
        }

        public static string ValidateFolderPattern(string pattern)
        {
            return FolderNamePattern.Validate(pattern);
        }

        public static string ValidateCredentialList(List<CredentialEntry> credentials)
        {
            if (credentials == null)
                return "凭据列表不能为空值。";
            if (credentials.Count > CredentialRowCollector.MaxCredentials)
                return "凭据最多 " + CredentialRowCollector.MaxCredentials + " 条。";

            var titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < credentials.Count; i++)
            {
                CredentialEntry entry = credentials[i];
                if (entry == null)
                    return "凭据列表第 " + (i + 1) + " 项为空。";
                if (string.IsNullOrWhiteSpace(entry.Title))
                    return "凭据列表第 " + (i + 1) + " 项名称为空。";
                if (!titles.Add(entry.Title))
                    return "凭据名称「" + entry.Title + "」重复。";
            }
            return null;
        }

        public static string ValidateUiScale(int percent)
        {
            return UiScale.IsValid(percent) ? null : "界面缩放必须是 100 / 125 / 150 / 175 / 200 之一。";
        }

        public static string ValidateCaptureMode(int value)
        {
            return value == 0 || value == 1 || value == 2 ? null : "截图模式值无效。";
        }
    }
}
