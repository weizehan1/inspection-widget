using System.IO;
using InspectionWidget.Config;

namespace InspectionWidget.Tests
{
    /// <summary>ConfigPathResolver 三种情形（假探针）+ 两处不可写时 Save 失败不落盘。</summary>
    public static class ConfigPathResolverTests
    {
        public static void RunAll()
        {
            TestRunner.Run("路径：exe 目录可写且无回退文件 → 主路径", () =>
            {
                ConfigPaths p = ConfigPathResolver.Resolve("C:\\App", "C:\\Users\\u\\AppData\\Local",
                    delegate(string f) { return false; },
                    delegate(string d) { return d == "C:\\App"; });
                Assert.False(p.IsFallback);
                Assert.Equal(Path.Combine("C:\\App", "config.json"), p.AuthoritativePath);
            });

            TestRunner.Run("路径：exe 目录不可写 → 回退路径", () =>
            {
                ConfigPaths p = ConfigPathResolver.Resolve("C:\\App", "C:\\Users\\u\\AppData\\Local",
                    delegate(string f) { return false; },
                    delegate(string d) { return false; });
                Assert.True(p.IsFallback);
                string expected = Path.Combine(Path.Combine("C:\\Users\\u\\AppData\\Local", "InspectionWidget"), "config.json");
                Assert.Equal(expected, p.AuthoritativePath);
            });

            TestRunner.Run("路径：回退文件已存在 → 即使 exe 可写也回退优先", () =>
            {
                string fallback = Path.Combine(Path.Combine("C:\\Users\\u\\AppData\\Local", "InspectionWidget"), "config.json");
                ConfigPaths p = ConfigPathResolver.Resolve("C:\\App", "C:\\Users\\u\\AppData\\Local",
                    delegate(string f) { return f == fallback; },
                    delegate(string d) { return true; });
                Assert.True(p.IsFallback);
                Assert.Equal(fallback, p.AuthoritativePath);
            });

            TestRunner.Run("路径：两处都不可写且无回退文件 → 仍给回退路径", () =>
            {
                ConfigPaths p = ConfigPathResolver.Resolve("C:\\App", "C:\\Local",
                    delegate(string f) { return false; },
                    delegate(string d) { return false; });
                Assert.True(p.IsFallback);
                Assert.Equal(Path.Combine(Path.Combine("C:\\Local", "InspectionWidget"), "config.json"), p.AuthoritativePath);
            });

            TestRunner.Run("路径：目录不可写时 Save 返回失败且不产生配置文件", () =>
            {
                string exeDir = ConfigStoreTests.MakeTempDir();
                string localDir = ConfigStoreTests.MakeTempDir();
                try
                {
                    ConfigPaths p = ConfigPathResolver.Resolve(exeDir, localDir,
                        delegate(string f) { return false; },
                        delegate(string d) { return false; });
                    Assert.True(p.IsFallback);
                    var store = new ConfigStore(p, delegate(string d) { return false; });
                    SaveResult r = store.Save(AppConfig.CreateDefault());
                    Assert.False(r.Saved);
                    Assert.NotNull(r.Error);
                    Assert.True(r.Error.Length > 0);
                    Assert.False(File.Exists(p.AuthoritativePath), "写不了时不得产生正式文件");
                }
                finally
                {
                    ConfigStoreTests.Cleanup(exeDir);
                    ConfigStoreTests.Cleanup(localDir);
                }
            });
        }
    }
}
