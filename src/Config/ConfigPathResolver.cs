using System;
using System.IO;

namespace InspectionWidget.Config
{
    /// <summary>权威配置路径判定结果。IsFallback=true 表示权威路径在 AppData（非 exe 同目录）。</summary>
    public sealed class ConfigPaths
    {
        public string AuthoritativePath;
        public bool IsFallback;
    }

    /// <summary>
    /// 权威路径判定（纯逻辑，探针注入便于测试）。v1.0.1 起的优先序：
    /// exe 同目录 config.json 已存在 → 用它（便携口径与已有副本兼容）；
    /// 不存在 → %LocalAppData%\InspectionWidget\config.json（exe 文件夹保持干净，
    /// 首次保存由 ConfigStore 建目录落盘）。一次只认一个权威位置，杜绝两处配置分叉。
    /// dirWritable 探针参数保留兼容旧调用，判定已不再依赖它（落点只看同目录文件是否存在）。
    /// </summary>
    public static class ConfigPathResolver
    {
        public const string ConfigFileName = "config.json";
        public const string FallbackDirName = "InspectionWidget";

        public static ConfigPaths Resolve(string exeDir, string localAppDataDir,
            Func<string, bool> fileExists, Func<string, bool> dirWritable)
        {
            string mainPath = Path.Combine(exeDir, ConfigFileName);
            if (fileExists != null && fileExists(mainPath))
                return new ConfigPaths { AuthoritativePath = mainPath, IsFallback = false };

            string fallbackDir = Path.Combine(localAppDataDir, FallbackDirName);
            string fallbackPath = Path.Combine(fallbackDir, ConfigFileName);
            return new ConfigPaths { AuthoritativePath = fallbackPath, IsFallback = true };
        }
    }
}
