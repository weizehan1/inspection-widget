using System;
using System.IO;
using InspectionWidget.Config;

namespace InspectionWidget.Tests
{
    /// <summary>草稿代次、原子提交与导入前恢复备份的编排回归。</summary>
    public static class SettingsApplyCoordinatorTests
    {
        public static void RunAll()
        {
            TestRunner.Run("提交编排：成功保存返回候选并推进代次", () =>
            {
                string dir = ConfigStoreTests.MakeTempDir();
                try
                {
                    ConfigStore store = ConfigStoreTests.MakeStore(dir);
                    Assert.True(store.Save(ConfigFor("初始")).Saved);
                    string revision = store.CurrentRevision;
                    AppConfig candidate = ConfigFor("候选");

                    SettingsApplyCoordinator.Outcome result =
                        SettingsApplyCoordinator.Execute(candidate, store, revision, false);
                    Assert.True(result.Applied, result.Error);
                    Assert.False(result.Stale);
                    Assert.NotNull(result.Save);
                    Assert.True(result.Save.Saved);
                    Assert.Equal(store.CurrentRevision, result.Save.Revision);
                    Assert.Equal("候选", store.Load().Config.Devices[0]);
                }
                finally { ConfigStoreTests.Cleanup(dir); }
            });

            TestRunner.Run("提交编排：代次变化返回 stale 且不写盘、不建恢复备份", () =>
            {
                string dir = ConfigStoreTests.MakeTempDir();
                try
                {
                    ConfigStore mine = ConfigStoreTests.MakeStore(dir);
                    Assert.True(mine.Save(ConfigFor("初始")).Saved);
                    string staleRevision = mine.CurrentRevision;

                    ConfigStore other = ConfigStoreTests.MakeStore(dir);
                    Assert.True(other.Save(ConfigFor("外部新值")).Saved);
                    byte[] externalBytes = File.ReadAllBytes(ConfigStoreTests.ConfigFile(dir));

                    SettingsApplyCoordinator.Outcome result =
                        SettingsApplyCoordinator.Execute(ConfigFor("旧草稿"), mine,
                            staleRevision, true);
                    Assert.False(result.Applied);
                    Assert.True(result.Stale);
                    Assert.True(result.Error.Contains("其他程序修改"));
                    Assert.Equal(Convert.ToBase64String(externalBytes),
                        Convert.ToBase64String(File.ReadAllBytes(ConfigStoreTests.ConfigFile(dir))));
                    Assert.Equal(0, Directory.GetFiles(dir, "config.preimport.*.json").Length);
                }
                finally { ConfigStoreTests.Cleanup(dir); }
            });

            TestRunner.Run("提交编排：导入提交先生成逐字节恢复备份再替换", () =>
            {
                string dir = ConfigStoreTests.MakeTempDir();
                try
                {
                    ConfigStore store = ConfigStoreTests.MakeStore(dir);
                    Assert.True(store.Save(ConfigFor("导入前")).Saved);
                    byte[] before = File.ReadAllBytes(ConfigStoreTests.ConfigFile(dir));
                    string revision = store.CurrentRevision;

                    SettingsApplyCoordinator.Outcome result =
                        SettingsApplyCoordinator.Execute(ConfigFor("导入后"), store,
                            revision, true);
                    Assert.True(result.Applied, result.Error);
                    Assert.NotNull(result.Save.PreImportBackupPath);
                    Assert.True(File.Exists(result.Save.PreImportBackupPath));
                    Assert.Equal(Convert.ToBase64String(before),
                        Convert.ToBase64String(File.ReadAllBytes(result.Save.PreImportBackupPath)));
                    Assert.False(result.Save.FirstCreation);
                    Assert.Equal("导入后", store.Load().Config.Devices[0]);

                    AppConfig again = ConfigFor("普通保存");
                    SettingsApplyCoordinator.Outcome normal =
                        SettingsApplyCoordinator.Execute(again, store,
                            store.CurrentRevision, false);
                    Assert.True(normal.Applied, normal.Error);
                    Assert.Null(normal.Save.PreImportBackupPath);
                    Assert.Equal(1, Directory.GetFiles(dir, "config.preimport.*.json").Length);
                }
                finally { ConfigStoreTests.Cleanup(dir); }
            });

            TestRunner.Run("提交编排：首次创建导入不伪造恢复备份但记录 FirstCreation", () =>
            {
                string dir = ConfigStoreTests.MakeTempDir();
                try
                {
                    ConfigStore store = ConfigStoreTests.MakeStore(dir);
                    SettingsApplyCoordinator.Outcome result =
                        SettingsApplyCoordinator.Execute(ConfigFor("首次"), store, string.Empty, true);
                    Assert.True(result.Applied, result.Error);
                    Assert.True(result.Save.FirstCreation);
                    Assert.Null(result.Save.PreImportBackupPath);
                    Assert.Equal(0, Directory.GetFiles(dir, "config.preimport.*.json").Length);
                }
                finally { ConfigStoreTests.Cleanup(dir); }
            });

            TestRunner.Run("提交编排：共享写保护阻止普通保存与导入提交", () =>
            {
                string dir = ConfigStoreTests.MakeTempDir();
                try
                {
                    ConfigStore store = ConfigStoreTests.MakeStore(dir);
                    Assert.True(store.Save(ConfigFor("保护前")).Saved);
                    byte[] before = File.ReadAllBytes(ConfigStoreTests.ConfigFile(dir));
                    store.WriteProtection.Enter("测试保护");

                    SettingsApplyCoordinator.Outcome normal =
                        SettingsApplyCoordinator.Execute(ConfigFor("普通"), store,
                            store.CurrentRevision, false);
                    Assert.False(normal.Applied);
                    Assert.True(normal.Error.Contains("保护状态"));
                    SettingsApplyCoordinator.Outcome imported =
                        SettingsApplyCoordinator.Execute(ConfigFor("导入"), store,
                            store.CurrentRevision, true);
                    Assert.False(imported.Applied);
                    Assert.Equal(Convert.ToBase64String(before),
                        Convert.ToBase64String(File.ReadAllBytes(ConfigStoreTests.ConfigFile(dir))));
                    Assert.Equal(0, Directory.GetFiles(dir, "config.preimport.*.json").Length);
                }
                finally { ConfigStoreTests.Cleanup(dir); }
            });

            TestRunner.Run("提交编排：空候选与空存储返回可读失败，不抛异常", () =>
            {
                var missingStore = SettingsApplyCoordinator.Execute(null, null, null, false);
                Assert.False(missingStore.Applied);
                Assert.True(!string.IsNullOrEmpty(missingStore.Error));
                string dir = ConfigStoreTests.MakeTempDir();
                try
                {
                    ConfigStore store = ConfigStoreTests.MakeStore(dir);
                    var missingCandidate = SettingsApplyCoordinator.Execute(
                        null, store, string.Empty, false);
                    Assert.False(missingCandidate.Applied);
                    Assert.True(!string.IsNullOrEmpty(missingCandidate.Error));
                }
                finally { ConfigStoreTests.Cleanup(dir); }
            });

            TestRunner.Run("提交编排：null 基线不可提交，有效代次才能写盘", () =>
            {
                string dir = ConfigStoreTests.MakeTempDir();
                try
                {
                    ConfigStore store = ConfigStoreTests.MakeStore(dir);
                    Assert.True(store.Save(ConfigFor("基线")).Saved);
                    byte[] before = File.ReadAllBytes(ConfigStoreTests.ConfigFile(dir));
                    SettingsApplyCoordinator.Outcome outcome =
                        SettingsApplyCoordinator.Execute(ConfigFor("无基线"), store, null, false);
                    Assert.False(outcome.Applied);
                    Assert.True(outcome.Error.Contains("基线"), outcome.Error);
                    Assert.Equal(Convert.ToBase64String(before),
                        Convert.ToBase64String(File.ReadAllBytes(
                            ConfigStoreTests.ConfigFile(dir))));

                    SettingsApplyCoordinator.Outcome valid =
                        SettingsApplyCoordinator.Execute(ConfigFor("有效"), store,
                            store.CurrentRevision, false);
                    Assert.True(valid.Applied, valid.Error);
                    Assert.Equal("有效", store.Load().Config.Devices[0]);
                }
                finally { ConfigStoreTests.Cleanup(dir); }
            });
        }

        private static AppConfig ConfigFor(string device)
        {
            AppConfig config = AppConfig.CreateDefault();
            config.Devices.Clear();
            config.Devices.Add(device);
            return config;
        }
    }
}
