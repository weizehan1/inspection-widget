using System;
using System.Collections.Generic;
using System.IO;
using InspectionWidget.Config;
using InspectionWidget.Planning;
using InspectionWidget.Storage;

namespace InspectionWidget.Tests
{
    /// <summary>
    /// 已点过设备显示方式（HideVisitedDevices）配置贯通与纯显示过滤行为用例。
    /// </summary>
    public static class HideVisitedDevicesTests
    {
        private sealed class FakeClock : IClock
        {
            public DateTime Now { get; set; }
        }

        public static void RunAll()
        {
            // ---------- 配置与交换链路 ----------

            TestRunner.Run("隐藏配置：默认构造与 CreateDefault 均为 true", () =>
            {
                Assert.True(new AppConfig().HideVisitedDevices);
                Assert.True(AppConfig.CreateDefault().HideVisitedDevices);
            });

            TestRunner.Run("隐藏配置：老配置缺字段 → Defaulted 且 HideVisitedDevices=true", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    File.WriteAllText(ConfigFile(dir),
                        "{\"RootPath\":\"D:/shots\",\"Devices\":[\"设备甲\"]}");
                    LoadResult r = MakeStore(dir).Load();
                    Assert.Equal(ConfigLoadKind.Defaulted, r.Kind);
                    Assert.True(r.Config.HideVisitedDevices);
                }
                finally { Cleanup(dir); }
            });

            TestRunner.Run("隐藏配置：Save/Load 往返 false 保留且 Kind=Ok", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    var cfg = AppConfig.CreateDefault();
                    cfg.HideVisitedDevices = false;
                    Config.SaveResult saved = MakeStore(dir).Save(cfg);
                    Assert.True(saved.Saved, saved.Error);

                    LoadResult r = MakeStore(dir).Load();
                    Assert.Equal(ConfigLoadKind.Ok, r.Kind);
                    Assert.False(r.Config.HideVisitedDevices);
                }
                finally { Cleanup(dir); }
            });

            TestRunner.Run("隐藏配置：AppConfigCloner 深拷贝保留 HideVisitedDevices", () =>
            {
                var cfg = AppConfig.CreateDefault();
                cfg.HideVisitedDevices = false;
                AppConfig clone = global::InspectionWidget.AppConfigCloner.Clone(cfg);
                Assert.False(clone.HideVisitedDevices);
                clone.HideVisitedDevices = true;
                Assert.False(cfg.HideVisitedDevices);
            });

            TestRunner.Run("隐藏配置：SettingsDraft LoadFrom/Snapshot/ApplyPlan/AcceptSaved 贯通", () =>
            {
                var cfg = AppConfig.CreateDefault();
                cfg.HideVisitedDevices = false;
                var draft = SettingsDraft.FromConfig(cfg, "rev-1");
                Assert.False(draft.HideVisitedDevices);

                draft.HideVisitedDevices = true;
                Assert.False(cfg.HideVisitedDevices);
                Assert.True(draft.SnapshotForTransferPreview().HideVisitedDevices);

                var incoming = AppConfig.CreateDefault();
                incoming.HideVisitedDevices = false;
                var plan = new ConfigImportPlan
                {
                    Result = incoming,
                    Devices = new List<string> { "设备甲" }
                };
                draft.ApplyPlan(plan, true);
                Assert.True(draft.HideVisitedDevices, "mergeOnly 不得覆盖 HideVisitedDevices");

                draft.ApplyPlan(plan, false);
                Assert.False(draft.HideVisitedDevices, "非 merge 应更新草稿 HideVisitedDevices");

                var saved = AppConfig.CreateDefault();
                saved.HideVisitedDevices = true;
                draft.AcceptSaved(saved, "rev-2");
                Assert.True(draft.HideVisitedDevices);
            });

            TestRunner.Run("隐藏配置：导入读取严格校验布尔值，缺字段不入 PresentFields", () =>
            {
                string[] bad = {
                    Exchange("{\"Devices\":[],\"HideVisitedDevices\":\"true\"}"),
                    Exchange("{\"Devices\":[],\"HideVisitedDevices\":1}"),
                    Exchange("{\"Devices\":[],\"HideVisitedDevices\":null}")
                };
                for (int i = 0; i < bad.Length; i++)
                {
                    ConfigImportDocument badDoc;
                    string badErr;
                    Assert.False(ConfigJsonReader.TryParse(bad[i], out badDoc, out badErr),
                        "非法 HideVisitedDevices 应拒绝：" + bad[i]);
                    Assert.NotNull(badErr);
                }

                ConfigImportDocument missingDoc;
                string err;
                Assert.True(ConfigJsonReader.TryParse(
                    Exchange("{\"Devices\":[\"甲\"]}"), out missingDoc, out err), err);
                Assert.False(missingDoc.Has("HideVisitedDevices"));

                ConfigImportDocument legacyMissingDoc;
                Assert.True(ConfigJsonReader.TryParse(
                    "{\"Devices\":[\"甲\"]}", out legacyMissingDoc, out err), err);
                Assert.False(legacyMissingDoc.Has("HideVisitedDevices"));
            });

            TestRunner.Run("隐藏配置：普通导出与完整备份携带 HideVisitedDevices", () =>
            {
                var cfg = AppConfig.CreateDefault();
                cfg.HideVisitedDevices = false;

                string excluded = ConfigTransfer.BuildExportJson(cfg, ConfigJsonReader.CredentialModeExcluded);
                Assert.True(excluded.Contains("\"HideVisitedDevices\": false"), excluded);
                ConfigImportDocument excludedDoc;
                string err;
                Assert.True(ConfigJsonReader.TryParse(excluded, out excludedDoc, out err), err);
                Assert.True(excludedDoc.Has("HideVisitedDevices"));
                Assert.False(excludedDoc.Config.HideVisitedDevices);

                string full = ConfigTransfer.BuildExportJson(cfg, ConfigJsonReader.CredentialModeDpapi);
                Assert.True(full.Contains("\"HideVisitedDevices\": false"), full);
                ConfigImportDocument fullDoc;
                Assert.True(ConfigJsonReader.TryParse(full, out fullDoc, out err), err);
                Assert.True(fullDoc.Has("HideVisitedDevices"));
                Assert.False(fullDoc.Config.HideVisitedDevices);
            });

            TestRunner.Run("隐藏配置：MergeDevicesOnly 与缺字段非 merge 均保留本机设置", () =>
            {
                var current = AppConfig.CreateDefault();
                current.HideVisitedDevices = false;

                ConfigImportDocument docWithTrue;
                string err;
                Assert.True(ConfigJsonReader.TryParse(
                    Exchange("{\"Devices\":[\"新设备\"],\"HideVisitedDevices\":true}"),
                    out docWithTrue, out err), err);
                ConfigImportPlan mergePlan;
                Assert.True(ConfigTransfer.TryBuildPlan(docWithTrue, current,
                    new ConfigImportOptions { MergeDevicesOnly = true }, null, out mergePlan, out err), err);
                Assert.False(mergePlan.Result.HideVisitedDevices);

                ConfigImportDocument docMissing;
                Assert.True(ConfigJsonReader.TryParse(
                    Exchange("{\"Devices\":[\"新设备\"]}"), out docMissing, out err), err);
                ConfigImportPlan missingPlan;
                Assert.True(ConfigTransfer.TryBuildPlan(docMissing, current,
                    new ConfigImportOptions { MergeDevicesOnly = false }, null, out missingPlan, out err), err);
                Assert.False(missingPlan.Result.HideVisitedDevices);

                ConfigImportPlan replacePlan;
                Assert.True(ConfigTransfer.TryBuildPlan(docWithTrue, current,
                    new ConfigImportOptions { MergeDevicesOnly = false }, null, out replacePlan, out err), err);
                Assert.True(replacePlan.Result.HideVisitedDevices);
            });

            // ---------- 纯显示过滤规则 ----------

            TestRunner.Run("隐藏过滤：点击被接受即隐藏但清单总数不变，配置不删除", () =>
            {
                var clock = new FakeClock { Now = new DateTime(2026, 11, 14, 9, 0, 0) };
                var devices = new List<string> { "设备A", "设备B", "设备C" };
                var plan = new InspectionPlan(clock, devices);

                DeviceDisplaySnapshot before = InspectionDeviceDisplayFilter.Build(
                    plan, devices, true, true, false);
                Assert.Equal(3, before.Items.Count);
                Assert.True(before.IsFilteringActive);
                Assert.Equal(DeviceListEmptyKind.None, before.EmptyKind);

                Assert.True(plan.MarkVisited("设备A", plan.CurrentToken));
                DeviceDisplaySnapshot after = InspectionDeviceDisplayFilter.Build(
                    plan, devices, true, true, false);
                Assert.Equal(2, after.Items.Count);
                Assert.Equal("设备B", after.Items[0].Name);
                Assert.Equal("设备C", after.Items[1].Name);
                Assert.Equal(3, after.TotalCount);
                Assert.Equal(1, after.VisitedCount);
                Assert.Equal(3, plan.TotalCount);
                Assert.Equal(3, devices.Count);
            });

            TestRunner.Run("隐藏过滤：全部隐藏空态区分于未配置空态，且可正常触发一次完成通知", () =>
            {
                var clock = new FakeClock { Now = new DateTime(2026, 11, 14, 9, 0, 0) };
                var emptyPlan = new InspectionPlan(clock, new string[0]);
                DeviceDisplaySnapshot emptySnapshot = InspectionDeviceDisplayFilter.Build(
                    emptyPlan, new string[0], true, true, false);
                Assert.Equal(DeviceListEmptyKind.NoDevicesConfigured, emptySnapshot.EmptyKind);

                var devices = new[] { "设备A", "设备B" };
                var plan = new InspectionPlan(clock, devices);
                Assert.True(plan.MarkVisited("设备A", plan.CurrentToken));
                Assert.True(plan.MarkVisited("设备B", plan.CurrentToken));

                DeviceDisplaySnapshot allHidden = InspectionDeviceDisplayFilter.Build(
                    plan, devices, true, true, false);
                Assert.Equal(0, allHidden.Items.Count);
                Assert.Equal(2, allHidden.TotalCount);
                Assert.Equal(2, allHidden.VisitedCount);
                Assert.Equal(DeviceListEmptyKind.AllVisitedHidden, allHidden.EmptyKind);
                Assert.True(plan.IsComplete);
                Assert.True(plan.TryClaimCompletionPrompt(false));
                Assert.False(plan.TryClaimCompletionPrompt(false));
            });

            TestRunner.Run("隐藏过滤：查看已点过可临时显露全部并补截，不改变计数与通知资格", () =>
            {
                var clock = new FakeClock { Now = new DateTime(2026, 11, 14, 9, 0, 0) };
                var devices = new[] { "设备A", "设备B" };
                var plan = new InspectionPlan(clock, devices);
                Assert.True(plan.MarkVisited("设备A", plan.CurrentToken));
                Assert.True(plan.MarkVisited("设备B", plan.CurrentToken));
                Assert.True(plan.TryClaimCompletionPrompt(false));

                DeviceDisplaySnapshot overrideView = InspectionDeviceDisplayFilter.Build(
                    plan, devices, true, true, true);
                Assert.False(overrideView.IsFilteringActive);
                Assert.Equal(2, overrideView.Items.Count);
                Assert.True(overrideView.Items[0].Visited);
                Assert.True(overrideView.Items[1].Visited);
                Assert.Equal(DeviceListEmptyKind.None, overrideView.EmptyKind);

                // 重复截图不累计，不重开通知
                Assert.False(plan.MarkVisited("设备A", plan.CurrentToken));
                Assert.Equal(2, plan.VisitedCount);
                Assert.True(plan.CompletionPrompted);
                Assert.False(plan.TryClaimCompletionPrompt(false));
            });

            TestRunner.Run("隐藏过滤：恢复未点、重置与跨日自动重现设备", () =>
            {
                var clock = new FakeClock { Now = new DateTime(2026, 11, 14, 9, 0, 0) };
                var devices = new[] { "设备A", "设备B" };
                var plan = new InspectionPlan(clock, devices);
                Assert.True(plan.MarkVisited("设备A", plan.CurrentToken));
                Assert.True(plan.MarkVisited("设备B", plan.CurrentToken));

                plan.Unmark("设备A");
                DeviceDisplaySnapshot afterUnmark = InspectionDeviceDisplayFilter.Build(
                    plan, devices, true, true, false);
                Assert.Equal(1, afterUnmark.Items.Count);
                Assert.Equal("设备A", afterUnmark.Items[0].Name);
                Assert.False(afterUnmark.Items[0].Visited);
                Assert.Equal(DeviceListEmptyKind.None, afterUnmark.EmptyKind);

                plan.ResetRound();
                DeviceDisplaySnapshot afterReset = InspectionDeviceDisplayFilter.Build(
                    plan, devices, true, true, false);
                Assert.Equal(2, afterReset.Items.Count);

                Assert.True(plan.MarkVisited("设备A", plan.CurrentToken));
                clock.Now = clock.Now.AddDays(1);
                plan.EnsureCurrentDay();
                DeviceDisplaySnapshot nextDay = InspectionDeviceDisplayFilter.Build(
                    plan, devices, true, true, false);
                Assert.Equal(2, nextDay.Items.Count);
                Assert.False(nextDay.Items[0].Visited);
            });

            TestRunner.Run("隐藏过滤：关闭隐藏设置保留全列表对勾；关闭计划模式显示全部且保留当日进度", () =>
            {
                var clock = new FakeClock { Now = new DateTime(2026, 11, 14, 9, 0, 0) };
                var devices = new[] { "设备A", "设备B" };
                var plan = new InspectionPlan(clock, devices);
                Assert.True(plan.MarkVisited("设备A", plan.CurrentToken));

                DeviceDisplaySnapshot keepVisible = InspectionDeviceDisplayFilter.Build(
                    plan, devices, true, false, false);
                Assert.False(keepVisible.IsFilteringActive);
                Assert.Equal(2, keepVisible.Items.Count);
                Assert.True(keepVisible.Items[0].Visited);
                Assert.False(keepVisible.Items[1].Visited);

                DeviceDisplaySnapshot planOff = InspectionDeviceDisplayFilter.Build(
                    plan, devices, false, true, false);
                Assert.False(planOff.IsFilteringActive);
                Assert.Equal(2, planOff.Items.Count);
                Assert.False(planOff.Items[0].Visited, "计划关闭时不显示对勾");
                Assert.Equal(1, plan.VisitedCount, "计划关闭不清除当日进度");
            });
        }

        private static string Exchange(string config)
        {
            return "{\"Format\":\"" + ConfigJsonReader.ExchangeFormat
                + "\",\"Version\":1,\"CredentialMode\":\"Excluded\",\"Config\":" + config + "}";
        }

        private static string MakeTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "iw-hide-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string ConfigFile(string dir)
        {
            return Path.Combine(dir, "config.json");
        }

        private static ConfigStore MakeStore(string dir)
        {
            return new ConfigStore(
                new ConfigPaths { AuthoritativePath = ConfigFile(dir), IsFallback = false },
                delegate(string d) { return true; });
        }

        private static void Cleanup(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch { }
        }
    }
}
