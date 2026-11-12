using System;
using System.IO;
using System.Threading;
using InspectionWidget.Config;

namespace InspectionWidget.Tests
{
    /// <summary>读取失败后的共享写入保护与应用自管路径判定。</summary>
    public static class ConfigWriteProtectionTests
    {
        public static void RunAll()
        {
            TestRunner.Run("写保护：进入、原因兜底、解除均线程安全可见", () =>
            {
                var guard = new ConfigWriteProtection();
                Assert.False(guard.IsProtected);
                Assert.Null(guard.Reason);
                guard.Enter(null);
                Assert.True(guard.IsProtected);
                Assert.True(guard.Reason.Contains("保存已暂停"));
                guard.Enter("自定义原因");
                Assert.Equal("自定义原因", guard.Reason);
                guard.Clear();
                Assert.False(guard.IsProtected);
                Assert.Null(guard.Reason);
            });

            TestRunner.Run("写保护：权威配置、锁文件、导入与损坏备份路径均拦截", () =>
            {
                string dir = Path.Combine(Path.GetTempPath(), "iw-paths-" + Guid.NewGuid().ToString("N"));
                string auth = Path.Combine(dir, "config.json");
                Assert.True(ConfigWriteProtection.IsManagedConfigPath(auth, auth));
                Assert.True(ConfigWriteProtection.IsManagedConfigPath(
                    Path.Combine(dir, "CONFIG.JSON"), auth));
                Assert.True(ConfigWriteProtection.IsManagedConfigPath(auth + ".lock", auth));
                Assert.True(ConfigWriteProtection.IsManagedConfigPath(
                    Path.Combine(dir, "config.preimport.20260101.abcdef12.json"), auth));
                Assert.True(ConfigWriteProtection.IsManagedConfigPath(
                    Path.Combine(dir, "config.corrupt.20260101.1.json"), auth));
                Assert.False(ConfigWriteProtection.IsManagedConfigPath(
                    Path.Combine(dir, "export.json"), auth));
                Assert.False(ConfigWriteProtection.IsManagedConfigPath(
                    Path.Combine(dir, "sub", "config.preimport.20260101.json"), auth));
                Assert.False(ConfigWriteProtection.IsManagedConfigPath(null, auth));
                Assert.False(ConfigWriteProtection.IsManagedConfigPath(auth, null));
            });

            TestRunner.Run("写保护：Load 遇锁占用进入保护，释锁后重读才解除", () =>
            {
                string dir = ConfigStoreTests.MakeTempDir();
                try
                {
                    ConfigStore writer = ConfigStoreTests.MakeStore(dir);
                    AppConfig original = AppConfig.CreateDefault();
                    original.RootPath = "D:\\safe";
                    Assert.True(writer.Save(original).Saved);
                    byte[] before = File.ReadAllBytes(ConfigStoreTests.ConfigFile(dir));

                    ConfigStore reader = ConfigStoreTests.MakeStore(dir);
                    using (var held = new FileStream(
                        ConfigStoreTests.ConfigFile(dir) + ".lock",
                        FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                    {
                        LoadResult failed = reader.Load();
                        Assert.Equal(ConfigLoadKind.IoErrorKept, failed.Kind);
                        Assert.True(reader.WriteProtection.IsProtected);
                        Assert.True(!string.IsNullOrEmpty(failed.Error));
                    }

                    AppConfig changed = AppConfig.CreateDefault();
                    changed.RootPath = "E:\\must-not-write";
                    SaveResult blocked = reader.Save(changed);
                    Assert.False(blocked.Saved);
                    Assert.True(blocked.Error.Contains("保护状态"));
                    Assert.Equal(Convert.ToBase64String(before),
                        Convert.ToBase64String(File.ReadAllBytes(ConfigStoreTests.ConfigFile(dir))));

                    LoadResult recovered = reader.Load();
                    Assert.True(recovered.Kind != ConfigLoadKind.IoErrorKept,
                        "释锁后应能完成读取，实际：" + recovered.Kind);
                    Assert.False(reader.WriteProtection.IsProtected);
                    Assert.Equal("D:\\safe", recovered.Config.RootPath);
                    Assert.True(reader.Save(changed).Saved);
                }
                finally { ConfigStoreTests.Cleanup(dir); }
            });

            TestRunner.Run("写保护：失败读取 Revision 不可提交，重读后新草稿可保存", () =>
            {
                string dir = ConfigStoreTests.MakeTempDir();
                try
                {
                    ConfigStore store = ConfigStoreTests.MakeStore(dir);
                    AppConfig original = AppConfig.CreateDefault();
                    original.RootPath = "D:\\A";
                    Assert.True(store.Save(original).Saved);
                    byte[] before = File.ReadAllBytes(ConfigStoreTests.ConfigFile(dir));

                    using (var held = new FileStream(
                        ConfigStoreTests.ConfigFile(dir) + ".lock",
                        FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                    {
                        LoadResult failed = store.Load();
                        Assert.Equal(ConfigLoadKind.IoErrorKept, failed.Kind);
                        Assert.True(ConfigStore.IsUnusableRevision(failed.Revision));
                        Assert.True(ConfigStore.IsUnusableRevision(store.CurrentRevision));

                        SettingsApplyCoordinator.Outcome oldDraft =
                            SettingsApplyCoordinator.Execute(
                                failed.Config, store, failed.Revision, false);
                        Assert.False(oldDraft.Applied);
                        Assert.Equal(Convert.ToBase64String(before),
                            Convert.ToBase64String(File.ReadAllBytes(
                                ConfigStoreTests.ConfigFile(dir))));
                    }

                    LoadResult recovered = store.Load();
                    Assert.False(ConfigStore.IsUnusableRevision(recovered.Revision));
                    AppConfig changed = AppConfig.CreateDefault();
                    changed.RootPath = "E:\\B";
                    SettingsApplyCoordinator.Outcome fresh =
                        SettingsApplyCoordinator.Execute(
                            changed, store, recovered.Revision, false);
                    Assert.True(fresh.Applied, fresh.Error);
                    Assert.Equal("E:\\B", store.Load().Config.RootPath);
                }
                finally { ConfigStoreTests.Cleanup(dir); }
            });

            TestRunner.Run("写保护：提交越过入口检查后进入保护，原子替换被拒绝", () =>
            {
                string dir = ConfigStoreTests.MakeTempDir();
                try
                {
                    string target = ConfigStoreTests.ConfigFile(dir);
                    var paths = new ConfigPaths { AuthoritativePath = target, IsFallback = false };
                    var protection = new ConfigWriteProtection();
                    var reached = new ManualResetEventSlim(false);
                    var proceed = new ManualResetEventSlim(false);
                    var writer = new AtomicConfigWriter();
                    var store = new ConfigStore(paths, delegate { return true; },
                        writer, new RealBackups(), protection, delegate
                        {
                            reached.Set();
                            proceed.Wait(5000);
                        });
                    AppConfig original = AppConfig.CreateDefault();
                    original.RootPath = "D:\\stable";
                    Assert.True(store.Save(original).Saved);
                    reached.Reset();
                    proceed.Reset();
                    byte[] before = File.ReadAllBytes(target);

                    SaveResult result = null;
                    Exception threadError = null;
                    var thread = new Thread(delegate()
                    {
                        try
                        {
                            AppConfig changed = AppConfig.CreateDefault();
                            changed.RootPath = "E:\\new";
                            result = store.Save(changed);
                        }
                        catch (Exception ex)
                        {
                            threadError = ex;
                        }
                    });
                    thread.Start();
                    Assert.True(reached.Wait(5000), "保存线程未到达最终提交点");
                    protection.Enter("测试：提交期间读取失败");
                    proceed.Set();
                    thread.Join();

                    Assert.Null(threadError);
                    Assert.NotNull(result);
                    Assert.False(result.Saved);
                    Assert.True(result.Error.Contains("保护"), result.Error);
                    Assert.Equal(Convert.ToBase64String(before),
                        Convert.ToBase64String(File.ReadAllBytes(target)));
                }
                finally { ConfigStoreTests.Cleanup(dir); }
            });

            TestRunner.Run("写保护：获准写盘但原子替换失败时返回真实错误，不误报保护", () =>
            {
                string dir = ConfigStoreTests.MakeTempDir();
                try
                {
                    string target = ConfigStoreTests.ConfigFile(dir);
                    File.WriteAllText(target, "old");
                    byte[] before = File.ReadAllBytes(target);
                    var fake = new FailingReplaceOperations();
                    var store = new ConfigStore(
                        new ConfigPaths { AuthoritativePath = target, IsFallback = false },
                        delegate { return true; },
                        AtomicConfigWriter.ForTests(fake),
                        new RealBackups());
                    AppConfig changed = AppConfig.CreateDefault();
                    changed.RootPath = "E:\\write-failed";
                    SaveResult result = store.Save(changed);

                    Assert.False(result.Saved);
                    Assert.True(result.Error.Contains("模拟替换失败"), result.Error);
                    Assert.False(result.Error.Contains("保护"), result.Error);
                    Assert.Equal(Convert.ToBase64String(before),
                        Convert.ToBase64String(File.ReadAllBytes(target)));
                    Assert.Equal(1, fake.ReplaceCalls);
                }
                finally { ConfigStoreTests.Cleanup(dir); }
            });

        }

        private sealed class FailingReplaceOperations : AtomicConfigWriter.IFileOperations
        {
            public int ReplaceCalls;
            public bool Exists(string path) { return true; }
            public void Replace(string source, string destination)
            {
                ReplaceCalls++;
                throw new IOException("模拟替换失败");
            }
            public void Move(string source, string destination)
            {
                throw new IOException("不应回退移动覆盖");
            }
        }

        private sealed class RealBackups : ConfigStore.IConfigBackupOperations
        {
            public string WriteCorruptBackup(string target, byte[] bytes)
            {
                throw new NotSupportedException();
            }
            public string WritePreImportBackup(string target, byte[] bytes)
            {
                throw new NotSupportedException();
            }
        }

    }
}
