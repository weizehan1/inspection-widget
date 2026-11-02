using System;
using System.Collections.Generic;
using System.IO;

namespace InspectionWidget.Config
{
    /// <summary>
    /// 设备名校验（纯逻辑）：设备名会直接用作文件名拼路径，必须拦下
    /// 非法字符、控制字符、空名、`.`/`..`、尾部空格或点、Windows 设备保留名，
    /// 并按大小写不敏感查重。返回 null 表示合法，否则返回中文错误原因。
    /// </summary>
    public static class DeviceNameValidator
    {
        private const string InvalidChars = "\\/:*?\"<>|";

        private static readonly HashSet<string> ReservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        /// <summary>完整路径长度上限：须 &lt; 260，即最长 259。</summary>
        public const int MaxFullPathLength = 259;

        public static string Validate(string name, IReadOnlyList<string> existing)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "设备名不能为空。";

            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (char.IsControl(c))
                    return "设备名不能包含控制字符。";
                if (InvalidChars.IndexOf(c) >= 0)
                    return "设备名不能包含 \\ / : * ? \" < > | 等字符。";
            }

            if (name == "." || name == "..")
                return "设备名不能是 . 或 ..。";

            if (name.EndsWith(" ", StringComparison.Ordinal) || name.EndsWith(".", StringComparison.Ordinal))
                return "设备名末尾不能是空格或点。";

            // 保留名按第一个点前的主名比对：CON、con.txt、nul.png 都算保留名
            string mainName = name.Split('.')[0];
            if (ReservedNames.Contains(mainName))
                return "设备名不能使用 Windows 保留名（如 CON、PRN、NUL、COM1、LPT1）。";

            if (existing != null)
            {
                for (int i = 0; i < existing.Count; i++)
                {
                    if (string.Equals(existing[i], name, StringComparison.OrdinalIgnoreCase))
                        return "已有同名设备（设备名不区分大小写）。";
                }
            }

            return null;
        }

        /// <summary>整表校验：逐个合法性 + 列表内部（含大小写不敏感）重名，返回第一个错误。</summary>
        public static string ValidateAll(IReadOnlyList<string> names)
        {
            if (names == null)
                return "设备列表为空。";

            for (int i = 0; i < names.Count; i++)
            {
                var others = new List<string>();
                for (int j = 0; j < names.Count; j++)
                {
                    if (j != i)
                        others.Add(names[j]);
                }
                string error = Validate(names[i], others);
                if (error != null)
                    return error;
            }
            return null;
        }

        /// <summary>
        /// 完整路径长度校验：rootPath 与 name 直接 Path.Combine 后的总长度超过 259 返回错误。
        /// 调用方（Task 5/7）应把日期子目录等也算进 rootPath 再调，或以最终文件名（含顺延后缀与扩展名）作 name 传入。
        /// </summary>
        public static string ValidateFullPathLength(string rootPath, string name)
        {
            if (string.IsNullOrEmpty(rootPath))
                return "根目录为空，无法校验完整路径。";
            if (string.IsNullOrEmpty(name))
                return "设备名不能为空。";

            string fullPath = Path.Combine(rootPath, name);
            if (fullPath.Length > MaxFullPathLength)
                return "完整路径过长（" + fullPath.Length + " 字符，上限 " + MaxFullPathLength + "），请缩短根目录或设备名。";
            return null;
        }
    }
}
