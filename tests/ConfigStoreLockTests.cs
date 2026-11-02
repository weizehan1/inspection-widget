using System.IO;
using InspectionWidget.Config;

namespace InspectionWidget.Tests
{
    /// <summary>ConfigStore 锁文件互斥：持锁时 Save 失败且正式文件不变；释锁后恢复可写。</summary>
    public static class ConfigStoreLockTests
    {
        public static void RunAll()
        {
            TestRunner.Run("锁：锁文件被独占时 Save 返回失败且正式文件内容不变", () =>
            {
                string dir = ConfigStoreTests.MakeTempDir();
                try
                {
                    ConfigStore store = ConfigStoreTests.MakeStore(dir);
                    SaveResult first = store.Save(AppConfig.CreateDefault());
                    Assert.True(first.Saved, first.Error);
                    string before = File.ReadAllText(ConfigStoreTests.ConfigFile(dir));

                    string lockPath = ConfigStoreTests.ConfigFile(dir) + ".lock";
                    using (var held = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                    {
                        AppConfig changed = AppConfig.CreateDefault();
                        changed.LastDevice = "不应写入";
                        SaveResult r = store.Save(changed);
                        Assert.False(r.Saved, "持锁时 Save 必须失败");
                        Assert.NotNull(r.Error);
                        Assert.True(r.Error.Length > 0);
                        Assert.Equal(before, File.ReadAllText(ConfigStoreTests.ConfigFile(dir)));
                    }
                }
                finally { ConfigStoreTests.Cleanup(dir); }
            });

            TestRunner.Run("锁：锁释放后 Save 恢复成功", () =>
            {
                string dir = ConfigStoreTests.MakeTempDir();
                try
                {
                    ConfigStore store = ConfigStoreTests.MakeStore(dir);
                    string lockPath = ConfigStoreTests.ConfigFile(dir) + ".lock";
                    using (var held = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                    {
                        Assert.False(store.Save(AppConfig.CreateDefault()).Saved);
                    }
                    SaveResult r = store.Save(AppConfig.CreateDefault());
                    Assert.True(r.Saved, r.Error);
                    Assert.True(File.Exists(ConfigStoreTests.ConfigFile(dir)));
                }
                finally { ConfigStoreTests.Cleanup(dir); }
            });
        }
    }
}
