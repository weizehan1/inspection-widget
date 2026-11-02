using System.IO;
using InspectionWidget.Config;

namespace InspectionWidget.Tests
{
    /// <summary>
    /// ConfigPathResolver 优先序（v1.0.1 起）：exe 同目录 config.json 已存在 → 用它
    /// （便携兼容）；否则一律 AppData 位置（exe 文件夹保持只有一个 exe）。
    /// 外加两条 ConfigStore 落点用例：首次保存落 AppData 并建目录、写不了时不落盘。
    /// </summary>
    public static class ConfigPathResolverTests
    {
        private static string AppDataConfig(string localAppData)
        {
            return Path.Combine(Path.Combine(localAppData, "InspectionWidget"), "config.json");
        }

        public static void RunAll()
        {
            TestRunner.Run("路径：exe 同目录已有 config.json → 主路径优先（即使 AppData 也有）", () =>
            {
                string main = Path.Combine("C:\\App", "config.json");
                ConfigPaths p = ConfigPathResolver.Resolve("C:\\App", "C:\\Users\\u\\AppData\\Local",
                    delegate(string f) { return true; },
                    delegate(string d) { return true; });
                Assert.False(p.IsFallback);
                Assert.Equal(main, p.AuthoritativePath);
            });

            TestRunner.Run("路径：exe 同目录无 config → AppData 位置（即使 exe 目录可写）", () =>
            {
                ConfigPaths p = ConfigPathResolver.Resolve("C:\\App", "C:\\Users\\u\\AppData\\Local",
                    delegate(string f) { return false; },
                    delegate(string d) { return true; });
                Assert.True(p.IsFallback);
                Assert.Equal(AppDataConfig("C:\\Users\\u\\AppData\\Local"), p.AuthoritativePath);
            });

            TestRunner.Run("路径：仅 AppData 已有 config → AppData 位置", () =>
            {
                string appData = AppDataConfig("C:\\Users\\u\\AppData\\Local");
                ConfigPaths p = ConfigPathResolver.Resolve("C:\\App", "C:\\Users\\u\\AppData\\Local",
                    delegate(string f) { return f == appData; },
                    delegate(string d) { return false; });
                Assert.True(p.IsFallback);
                Assert.Equal(appData, p.AuthoritativePath);
            });

            TestRunner.Run("路径：两处都没有 → AppData 位置（首次保存落点）", () =>
            {
                ConfigPaths p = ConfigPathResolver.Resolve("C:\\App", "C:\\Local",
                    delegate(string f) { return false; },
                    delegate(string d) { return false; });
                Assert.True(p.IsFallback);
                Assert.Equal(AppDataConfig("C:\\Local"), p.AuthoritativePath);
            });

            TestRunner.Run("路径：首次保存实际落 AppData 并自动建目录，exe 目录不产生 config", () =>
            {
                string exeDir = ConfigStoreTests.MakeTempDir();
                string localRoot = ConfigStoreTests.MakeTempDir();
                try
                {
                    // localRoot 下故意不预建 InspectionWidget 子目录，验证 Save 建目录
                    ConfigPaths p = ConfigPathResolver.Resolve(exeDir, localRoot,
                        delegate(string f) { return File.Exists(f); },
                        delegate(string d) { return true; });
                    Assert.True(p.IsFallback);
                    Assert.Equal(AppDataConfig(localRoot), p.AuthoritativePath);

                    var store = new ConfigStore(p, null);
                    SaveResult r = store.Save(AppConfig.CreateDefault());
                    Assert.True(r.Saved, r.Error);
                    Assert.True(File.Exists(AppDataConfig(localRoot)), "配置须落在 AppData 位置");
                    Assert.False(File.Exists(Path.Combine(exeDir, "config.json")), "exe 目录不得产生 config.json");

                    LoadResult loaded = store.Load();
                    Assert.Equal(ConfigLoadKind.Ok, loaded.Kind);
                }
                finally
                {
                    ConfigStoreTests.Cleanup(exeDir);
                    ConfigStoreTests.Cleanup(localRoot);
                }
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
