using System;

namespace InspectionWidget.Config
{
    /// <summary>
    /// 截图日期文件夹名模式（v1.0.2）：占位 yyyy/MM/dd 可混排固定文字，
    /// 如「巡检-yyyyMMdd」「yyyy年MM月dd日」；默认与旧口径一致（yyyy-MM-dd）。
    /// 格式化与校验均为纯逻辑：保存落盘与「打开今日文件夹」共用 Format，
    /// 设置保存与 Saver 兜底共用 Validate。
    /// </summary>
    public static class FolderNamePattern
    {
        public const string Default = "yyyy-MM-dd";

        // Windows 文件名非法字符显式清单（不用 Path.GetInvalidFileNameChars：
        // Linux/Mono 下该集合只有 '/' 与 '\0'，会把 Windows 非法模式放行）。
        private static readonly char[] InvalidChars =
            { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };

        /// <summary>
        /// 按模式把日期格式化为文件夹名。先把占位换成哨兵再回填数值，
        /// 避免固定文字里的字母被二次替换。null/空模式按 Default。
        /// </summary>
        public static string Format(string pattern, DateTime date)
        {
            if (string.IsNullOrEmpty(pattern))
                pattern = Default;
            return pattern
                .Replace("yyyy", "\u0001")
                .Replace("MM", "\u0002")
                .Replace("dd", "\u0003")
                .Replace("\u0001", date.Year.ToString("D4"))
                .Replace("\u0002", date.Month.ToString("D2"))
                .Replace("\u0003", date.Day.ToString("D2"));
        }

        /// <summary>校验：合法返回 null，否则返回人话错误（设置保存据此拦截）。</summary>
        public static string Validate(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                return "日期文件夹名不能为空。";
            for (int i = 0; i < pattern.Length; i++)
            {
                char c = pattern[i];
                if (c < 0x20 || Array.IndexOf(InvalidChars, c) >= 0)
                    return "日期文件夹名不能包含 \\ / : * ? \" < > | 等字符。";
            }
            string sample = Format(pattern, new DateTime(2026, 11, 3));
            if (string.IsNullOrWhiteSpace(sample) || sample == "." || sample == "..")
                return "日期文件夹名不能是「.」或「..」。";
            return null;
        }

        /// <summary>加载自愈：null/空补 Default，其余原样（合法性由保存校验把关）。</summary>
        public static string Normalize(string pattern)
        {
            return string.IsNullOrEmpty(pattern) ? Default : pattern;
        }
    }
}
