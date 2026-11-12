using System;
using System.Collections.Generic;
using InspectionWidget.Config;

namespace InspectionWidget.Tests
{
    /// <summary>设置窗口唯一草稿与 LastDevice/导入合并状态回归。</summary>
    public static class SettingsDraftTests
    {
        public static void RunAll()
        {
            TestRunner.Run("草稿：全字段与凭据运行时状态进入同一编辑态", () =>
            {
                AppConfig config = MakeConfig();
                config.Credentials.Add(new CredentialEntry
                {
                    Title = "原凭据", EncryptedSecret = "QUJD", NeedsReset = true
                });
                SettingsDraft draft = SettingsDraft.FromConfig(config, "rev-1");
                Assert.Equal("rev-1", draft.SavedRevision);
                Assert.Equal(3, draft.Devices.Count);
                Assert.Equal("乙", draft.DeviceNames()[1]);
                Assert.Equal(1, draft.Credentials.Count);
                Assert.True(draft.Credentials[0].OriginalNeedsReset);
                Assert.Equal("QUJD", draft.Credentials[0].OriginalSecret);
                Assert.Equal("D:\\shots", draft.RootPath);
                Assert.False(draft.AutoClearClipboard);
                Assert.Equal(175, draft.UiScalePercent);
                Assert.Equal("乙", draft.LastDevice);
            });

            TestRunner.Run("草稿：空行可存在，由提交校验决定是否补齐", () =>
            {
                SettingsDraft draft = SettingsDraft.FromConfig(
                    AppConfig.CreateDefault(), string.Empty);
                draft.Devices.Clear();
                int index = draft.AddBlankDevice();
                Assert.Equal(0, index);
                Assert.Equal(1, draft.Devices.Count);
                Assert.Equal(string.Empty, draft.Devices[0].Name);
            });

            TestRunner.Run("草稿：改名时 LastDevice 跟随，含大小写与连续改名", () =>
            {
                SettingsDraft draft = SettingsDraft.FromConfig(MakeConfig(), string.Empty);
                draft.ApplyDeviceRename("乙", "B2");
                Assert.Equal("B2", draft.LastDevice);
                draft.ApplyDeviceRename("b2", "最终设备");
                Assert.Equal("最终设备", draft.LastDevice);
                draft.ApplyDeviceRename("甲", "甲-新");
                Assert.Equal("最终设备", draft.LastDevice);
            });

            TestRunner.Run("草稿：删除被引用设备清空 LastDevice，删其他设备保留", () =>
            {
                SettingsDraft draft = SettingsDraft.FromConfig(MakeConfig(), string.Empty);
                Assert.True(draft.RemoveDeviceAt(0));
                Assert.Equal("乙", draft.LastDevice);
                Assert.True(draft.RemoveDeviceAt(0));
                Assert.Null(draft.LastDevice);
                Assert.Equal(1, draft.Devices.Count);
            });

            TestRunner.Run("草稿：排序与删除首末行边界正确", () =>
            {
                SettingsDraft draft = SettingsDraft.FromConfig(MakeConfig(), string.Empty);
                Assert.True(draft.MoveDevice(1, -1));
                Assert.Equal("乙", draft.Devices[0].Name);
                Assert.False(draft.MoveDevice(0, -1));
                Assert.True(draft.MoveDevice(0, 1));
                Assert.Equal("甲", draft.Devices[0].Name);
                Assert.False(draft.MoveDevice(2, 1));
                Assert.False(draft.RemoveDeviceAt(-1));
                Assert.False(draft.RemoveDeviceAt(99));
            });

            TestRunner.Run("草稿：合并设备保持当前顺序、大小写去重并保持幂等", () =>
            {
                SettingsDraft draft = SettingsDraft.FromConfig(MakeConfig(), string.Empty);
                draft.MergeDevices(new[] { "乙", "丙", "D" });
                Assert.Equal("甲,乙,丁,丙,D",
                    string.Join(",", draft.DeviceNames().ToArray()));
                draft.MergeDevices(new[] { "d", "丙", "戊" });
                Assert.Equal("甲,乙,丁,丙,D,戊",
                    string.Join(",", draft.DeviceNames().ToArray()));
            });

            TestRunner.Run("草稿：导入仅合并时只改设备，不动其他设置和凭据", () =>
            {
                AppConfig current = MakeConfig();
                current.Credentials.Add(new CredentialEntry
                {
                    Title = "保留凭据", EncryptedSecret = "QUJD"
                });
                SettingsDraft draft = SettingsDraft.FromConfig(current, string.Empty);

                ConfigImportPlan plan = MakePlan("替换后根目录");
                plan.Devices = new List<string> { "甲", "乙", "丁", "新设备" };
                plan.Result.Devices = plan.Devices;
                plan.Result.RootPath = "E:\\ignored";
                plan.Result.AutoClearClipboard = true;
                draft.ApplyPlan(plan, true);

                Assert.Equal("甲,乙,丁,新设备",
                    string.Join(",", draft.DeviceNames().ToArray()));
                Assert.Equal("D:\\shots", draft.RootPath);
                Assert.False(draft.AutoClearClipboard);
                Assert.Equal(1, draft.Credentials.Count);
                Assert.Equal("保留凭据", draft.Credentials[0].Title);
                Assert.True(draft.ImportPending);
            });

            TestRunner.Run("草稿：导入替换更新参与字段并保留未选项", () =>
            {
                SettingsDraft draft = SettingsDraft.FromConfig(MakeConfig(), string.Empty);
                ConfigImportPlan plan = MakePlan("E:\\new");
                plan.Devices = new List<string> { "丙" };
                plan.Result.Devices = plan.Devices;
                plan.Result.AutoClearClipboard = true;
                plan.Result.UiScalePercent = 200;
                plan.Result.CaptureMode = CaptureMode.BitBltOnly;
                plan.Result.LastDevice = null;
                draft.ApplyPlan(plan, false);

                Assert.Equal("丙", draft.Devices[0].Name);
                Assert.Equal("E:\\new", draft.RootPath);
                Assert.True(draft.AutoClearClipboard);
                Assert.Equal(200, draft.UiScalePercent);
                Assert.Equal(CaptureMode.BitBltOnly, draft.CaptureMode);
                Assert.Null(draft.LastDevice);
            });

            TestRunner.Run("草稿：导入替换凭据生成需重设状态且密文不进明文输入框", () =>
            {
                SettingsDraft draft = SettingsDraft.FromConfig(MakeConfig(), string.Empty);
                ConfigImportPlan plan = MakePlan("D:\\shots");
                plan.Devices = new List<string> { "甲" };
                plan.Result.Devices = plan.Devices;
                plan.CredentialsReplaced = true;
                plan.Credentials = new List<CredentialEntry>
                {
                    new CredentialEntry
                    {
                        Title = "可用", EncryptedSecret = "QUJD", NeedsReset = false
                    },
                    new CredentialEntry
                    {
                        Title = "需重设", EncryptedSecret = "%%%", NeedsReset = true
                    }
                };
                plan.Result.Credentials = plan.Credentials;

                draft.ApplyPlan(plan, false);
                Assert.Equal(2, draft.Credentials.Count);
                Assert.Equal("QUJD", draft.Credentials[0].OriginalSecret);
                Assert.False(draft.Credentials[0].OriginalNeedsReset);
                Assert.True(draft.Credentials[1].OriginalNeedsReset);
                Assert.False(draft.Credentials[1].IsNew);
            });

            TestRunner.Run("草稿：成功应用后整份基线、dirty 与导入标记重置", () =>
            {
                SettingsDraft draft = SettingsDraft.FromConfig(MakeConfig(), "old");
                draft.ImportPending = true;
                AppConfig saved = MakeConfig();
                saved.RootPath = "E:\\saved";
                saved.Devices = new List<string> { "最终" };
                saved.LastDevice = "最终";
                draft.AcceptSaved(saved, "new");

                Assert.Equal("new", draft.SavedRevision);
                Assert.False(draft.ImportPending);
                Assert.Equal("E:\\saved", draft.RootPath);
                Assert.Equal("最终", draft.LastDevice);
                Assert.Equal(1, draft.Devices.Count);
            });

            TestRunner.Run("草稿：清除凭据在快照中为空密文并标记需重设", () =>
            {
                AppConfig config = MakeConfig();
                config.Credentials.Add(new CredentialEntry
                {
                    Title = "A", EncryptedSecret = "QUJD"
                });
                SettingsDraft draft = SettingsDraft.FromConfig(config, string.Empty);
                draft.Credentials[0].Cleared = true;
                AppConfig snapshot = draft.SnapshotForTransferPreview();
                Assert.Equal(string.Empty, snapshot.Credentials[0].EncryptedSecret);
                Assert.True(snapshot.Credentials[0].NeedsReset);
            });

            TestRunner.Run("草稿：null 配置得到可编辑默认草稿，null 列表不崩溃", () =>
            {
                SettingsDraft draft = SettingsDraft.FromConfig(null, null);
                Assert.Equal(2, draft.Devices.Count);
                Assert.Equal(0, draft.Credentials.Count);
                Assert.NotNull(draft.SavedRevision ?? string.Empty);

                AppConfig config = MakeConfig();
                config.Devices = null;
                config.Credentials = null;
                SettingsDraft fromNullLists = SettingsDraft.FromConfig(config, string.Empty);
                Assert.Equal(0, fromNullLists.Devices.Count);
                Assert.Equal(0, fromNullLists.Credentials.Count);
            });
        }

        private static AppConfig MakeConfig()
        {
            return new AppConfig
            {
                RootPath = "D:\\shots",
                Devices = new List<string> { "甲", "乙", "丁" },
                Credentials = new List<CredentialEntry>(),
                AutoClearClipboard = false,
                CaptureMode = CaptureMode.PrintWindowOnly,
                IsDeviceCollapsed = true,
                IsCredentialCollapsed = true,
                UiScalePercent = 175,
                FolderNamePattern = "巡检-yyyyMMdd",
                LastDevice = "乙",
                WindowLeft = 10,
                WindowTop = 20,
                TrayHintShown = true
            };
        }

        private static ConfigImportPlan MakePlan(string root)
        {
            AppConfig result = MakeConfig();
            result.RootPath = root;
            return new ConfigImportPlan
            {
                Result = result,
                Devices = new List<string>(result.Devices),
                Credentials = new List<CredentialEntry>()
            };
        }
    }
}
