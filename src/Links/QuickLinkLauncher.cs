using System;
using System.Diagnostics;
using InspectionWidget.Config;

namespace InspectionWidget.Links
{
    /// <summary>用默认浏览器打开快捷链接；启动动作可注入，异常与非法协议转可读错误。</summary>
    public sealed class QuickLinkLauncher
    {
        private readonly Action<string> _open;

        public QuickLinkLauncher()
            : this(new Action<string>(OpenInDefaultBrowser))
        {
        }

        public QuickLinkLauncher(Action<string> open)
        {
            _open = open ?? new Action<string>(OpenInDefaultBrowser);
        }

        public bool TryOpen(string url, out string error)
        {
            error = QuickLinkValidator.ValidateUrl(url);
            if (error != null)
                return false;
            try
            {
                _open(url.Trim());
                return true;
            }
            catch (Exception ex)
            {
                error = "打开快捷链接失败：" + ex.Message;
                return false;
            }
        }

        private static void OpenInDefaultBrowser(string url)
        {
            var start = new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            Process.Start(start);
        }
    }
}
