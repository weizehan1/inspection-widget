using System;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace InspectionWidget
{
    /// <summary>
    /// 应用图标加载（v1.0.2）：从嵌入资源（assets/app.ico，csproj 中
    /// EmbeddedResource 的 LogicalName）读取，主面板、设置窗口与托盘共用，
    /// 不依赖外部文件。资源缺失时返回 null（调用方保持系统默认图标）。
    /// </summary>
    public static class AppIcon
    {
        public const string ResourceName = "InspectionWidget.AppIcon.ico";

        /// <summary>加载嵌入的 app.ico；失败/缺失返回 null。返回的 Icon 由调用方持有。</summary>
        public static Icon Load()
        {
            try
            {
                Assembly assembly = typeof(AppIcon).Assembly;
                Stream stream = assembly.GetManifestResourceStream(ResourceName);
                if (stream == null)
                {
                    // LogicalName 未被工具链采纳时的兜底：找任意 .ico 资源
                    string[] names = assembly.GetManifestResourceNames();
                    for (int i = 0; i < names.Length; i++)
                    {
                        if (names[i].EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                        {
                            stream = assembly.GetManifestResourceStream(names[i]);
                            break;
                        }
                    }
                }
                if (stream == null)
                    return null;
                using (stream)
                {
                    return new Icon(stream);
                }
            }
            catch
            {
                // 图标只是外观：加载失败绝不阻断启动
                return null;
            }
        }
    }
}
