using System;
using System.Collections.Generic;

namespace InspectionWidget.Config
{
    /// <summary>快捷链接纯校验：不联网，只判断名称与绝对 http/https 地址。</summary>
    public static class QuickLinkValidator
    {
        public const int MaxLinks = 12;

        public static string Validate(QuickLinkEntry entry)
        {
            if (entry == null)
                return "快捷链接条目为空。";
            if (string.IsNullOrWhiteSpace(entry.Title))
                return "快捷链接名称不能为空。";
            return ValidateUrl(entry.Url);
        }

        public static string ValidateAll(IEnumerable<QuickLinkEntry> entries)
        {
            if (entries == null)
                return null;
            int index = 0;
            foreach (QuickLinkEntry entry in entries)
            {
                index++;
                string error = Validate(entry);
                if (error != null)
                    return "第 " + index + " 条快捷链接：" + error;
            }
            return null;
        }

        /// <summary>启动入口与条目共用：只放行绝对 http/https，不做额外字符限制。</summary>
        public static string ValidateUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return "快捷链接地址不能为空。";
            Uri parsed;
            bool created;
            try
            {
                created = Uri.TryCreate(url.Trim(), UriKind.Absolute, out parsed);
            }
            catch (Exception)
            {
                return "快捷链接地址格式无效。";
            }
            if (!created || parsed == null)
                return "快捷链接地址必须是绝对的 http/https 网址。";
            if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return "快捷链接地址只能使用 http 或 https，不接受 file/javascript 等协议。";
            if (string.IsNullOrEmpty(parsed.Host))
                return "快捷链接地址缺少主机名。";
            return null;
        }
    }
}
