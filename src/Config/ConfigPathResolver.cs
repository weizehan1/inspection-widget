using System;
using System.IO;

namespace InspectionWidget.Config
{
    /// <summary>权威配置路径判定结果。</summary>
    public sealed class ConfigPaths
    {
        public string AuthoritativePath;
        public bool IsFallback;
    }

    /// <summary>
    /// 权威路径判定（纯逻辑，探针注入便于测试）：
    /// 回退文件已存在 → 回退优先；否则 exe 目录可写 → 主路径；否则回退路径。
    /// 一次只认一个权威位置，杜绝两处配置分叉。
    /// </summary>
    public static class ConfigPathResolver
    {
        public const string ConfigFileName = "config.json";
        public const string FallbackDirName = "InspectionWidget";

        public static ConfigPaths Resolve(string exeDir, string localAppDataDir,
            Func<string, bool> fileExists, Func<string, bool> dirWritable)
        {
            string fallbackDir = Path.Combine(localAppDataDir, FallbackDirName);
            string fallbackPath = Path.Combine(fallbackDir, ConfigFileName);

            if (fileExists != null && fileExists(fallbackPath))
                return new ConfigPaths { AuthoritativePath = fallbackPath, IsFallback = true };

            string mainPath = Path.Combine(exeDir, ConfigFileName);
            if (dirWritable != null && dirWritable(exeDir))
                return new ConfigPaths { AuthoritativePath = mainPath, IsFallback = false };

            return new ConfigPaths { AuthoritativePath = fallbackPath, IsFallback = true };
        }
    }
}
