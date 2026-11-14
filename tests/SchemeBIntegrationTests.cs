using System;
using System.Collections.Generic;
using System.IO;
using InspectionWidget.Capture;
using InspectionWidget.Config;
using InspectionWidget.Credentials;
using InspectionWidget.Planning;
using InspectionWidget.Storage;

namespace InspectionWidget.Tests
{
    /// <summary>
    /// v1.1.0 方案 B 端到端集成回归：涵盖会话协调器、点击即隐藏与总数不变、
    /// 取消/失败保留已点过、查看已点过与补截不重算、恢复未点、关闭隐藏开关、
    /// 设置改名/导入替换/取消、重启恢复与跨日重置、全隐空态与唯一完成提示，
    /// 以及 SettingsSaveCoordinator 快捷链接与存储组号校验。
    /// </summary>
    public static class SchemeBIntegrationTests
    {
        private sealed class StepClock : IClock
        {
            public DateTime Now { get; set; }
        }

        private sealed class DummyProtector : ICredentialProtector
        {
            public string Protect(string plainText)
            {
                return plainText == null ? string.Empty : "ENC:" + plainText;
            }

            public string Unprotect(string base64)
            {
                if (base64 != null && base64.StartsWith("ENC:", StringComparison.Ordinal))
                {
                    return base64.Substring(4);
                }
                throw new System.Security.Cryptography.CryptographicException("Invalid cipher text");
            }
        }

        public static void RunAll()
        {
            TestRunner.Run("方案B布局：主面板宽 352 随缩放与工作区自适应钳制", () =>
            {
                Assert.Equal(352, PanelLayout.SchemeBPanelWidth);
                Assert.Equal(528, PanelLayout.ScaledSchemeBPanelWidth(1.5));
                Assert.Equal(380, PanelLayout.ComputeSchemeBHeight(380, 1080, 8, 1.0));
                Assert.Equal(660, PanelLayout.ComputeSchemeBHeight(920, 1080, 8, 1.0));
                Assert.Equal(500, PanelLayout.ComputeSchemeBHeight(920, 508, 8, 1.0));
            });

            TestRunner.Run("方案B保存协调：快捷链接校验归组 2 且定位出错行与字段", () =>
            {
                AppConfig cfg = AppConfig.CreateDefault();
                cfg.RootPath = "D:\\Inspection";
                var links = new List<QuickLinkEntry>
                {
                    new QuickLinkEntry { Title = "监控", Url = "https://example.com", Enabled = true },
                    new QuickLinkEntry { Title = "", Url = "https://a.example", Enabled = true }
                };
                SettingsSaveCoordinator.Outcome emptyTitle = SettingsSaveCoordinator.Build(
                    cfg, new List<string> { "R1" }, new List<CredentialRowCollector.RowInput>(),
                    links, cfg.RootPath, cfg.FolderNamePattern, true, 100,
                    CaptureMode.Auto, true, false, new DummyProtector());
                Assert.False(emptyTitle.Saved);
                Assert.Equal(2, emptyTitle.ErrorGroup);
                Assert.Equal(1, emptyTitle.LinkErrorIndex);
                Assert.True(emptyTitle.LinkErrorOnTitle);

                links[1].Title = "损坏链接";
                links[1].Url = "javascript:alert(1)";
                SettingsSaveCoordinator.Outcome badScheme = SettingsSaveCoordinator.Build(
                    cfg, new List<string> { "R1" }, new List<CredentialRowCollector.RowInput>(),
                    links, cfg.RootPath, cfg.FolderNamePattern, true, 100,
                    CaptureMode.Auto, true, false, new DummyProtector());
                Assert.False(badScheme.Saved);
                Assert.Equal(2, badScheme.ErrorGroup);
                Assert.Equal(1, badScheme.LinkErrorIndex);
                Assert.False(badScheme.LinkErrorOnTitle);
            });

            TestRunner.Run("方案B保存协调：新版存储错误归组 3，正常保存落入 QuickLinks 与双开关", () =>
            {
                AppConfig cfg = AppConfig.CreateDefault();
                var links = new List<QuickLinkEntry>
                {
                    new QuickLinkEntry { Title = " 门户 ", Url = " https://portal.example.com ", Enabled = true }
                };
                SettingsSaveCoordinator.Outcome badPattern = SettingsSaveCoordinator.Build(
                    cfg, new List<string> { "R1" }, new List<CredentialRowCollector.RowInput>(),
                    links, "D:\\Inspection", "bad/pattern", true, 100,
                    CaptureMode.Auto, true, false, new DummyProtector());
                Assert.False(badPattern.Saved);
                Assert.Equal(3, badPattern.ErrorGroup);

                SettingsSaveCoordinator.Outcome ok = SettingsSaveCoordinator.Build(
                    cfg, new List<string> { "R1" }, new List<CredentialRowCollector.RowInput>(),
                    links, "D:\\Inspection", "yyyy-MM-dd", true, 125,
                    CaptureMode.PrintWindowOnly, true, false, new DummyProtector());
                Assert.True(ok.Saved, ok.Error);
                Assert.True(ok.Result.PlanModeEnabled);
                Assert.False(ok.Result.HideVisitedDevices);
                Assert.Equal(1, ok.Result.QuickLinks.Count);
                Assert.Equal("门户", ok.Result.QuickLinks[0].Title);
                Assert.Equal("https://portal.example.com", ok.Result.QuickLinks[0].Url);
            });

            TestRunner.Run("方案B草稿：改名记录与非合并导入替换标记在保存后清空", () =>
            {
                AppConfig cfg = AppConfig.CreateDefault();
                cfg.Devices = new List<string> { "核心A", "核心B" };
                SettingsDraft draft = SettingsDraft.FromConfig(cfg, "rev1");
                draft.ApplyDeviceRename("核心A", "核心A-改");
                Assert.Equal(1, draft.PendingDeviceRenames.Count);
                Assert.Equal("核心A", draft.PendingDeviceRenames[0].OldName);
                Assert.Equal("核心A-改", draft.PendingDeviceRenames[0].NewName);

                draft.AcceptSaved(cfg, "rev2");
                Assert.Equal(0, draft.PendingDeviceRenames.Count);
                Assert.False(draft.PendingImportDeviceReplace);

                AppConfig imported = AppConfig.CreateDefault();
                imported.RootPath = @"C:\Shots";
                cfg.RootPath = @"C:\Shots";
                imported.Devices = new List<string> { "新机" };
                ConfigImportPlan plan;
                string planErr;
                Assert.True(ConfigTransfer.TryBuildPlan(
                    new ConfigImportDocument { Config = imported }, cfg,
                    new ConfigImportOptions { MergeDevicesOnly = false },
                    new DummyProtector(), out plan, out planErr));
                draft.ApplyPlan(plan, false);
                Assert.True(draft.PendingImportDeviceReplace);
            });

            TestRunner.Run("集成：点击即隐藏、总数与配置不变、取消与失败后仍保持隐藏", () =>
            {
                string dir = CreateTempDir();
                try
                {
                    var clock = new StepClock { Now = new DateTime(2026, 11, 14, 9, 0, 0) };
                    var store = new InspectionProgressStore(Path.Combine(dir, "progress.json"));
                    var cfg = BuildPlanConfig("D:\\Shots", new[] { "核心", "接入", "汇聚" }, true, true);
                    var sm = new PanelStateMachine();
                    var controller = new InspectionSessionController(clock, sm, store, cfg);

                    InspectionPlanToken t1;
                    string err;
                    Assert.True(controller.TrySelectDevice("核心", out t1, out err));
                    Assert.Equal(PanelState.Waiting, sm.State);
                    DeviceDisplaySnapshot snap1 = controller.BuildDeviceDisplay();
                    Assert.Equal(3, snap1.TotalCount);
                    Assert.Equal(1, snap1.VisitedCount);
                    Assert.Equal(2, snap1.Items.Count);
                    Assert.Equal("接入", snap1.Items[0].Name);
                    Assert.Equal("汇聚", snap1.Items[1].Name);
                    Assert.Equal(3, cfg.Devices.Count);

                    // 取消选窗后仍保持已点过且隐藏
                    Assert.True(sm.Cancel());
                    DeviceDisplaySnapshot afterCancel = controller.BuildDeviceDisplay();
                    Assert.Equal(1, afterCancel.VisitedCount);
                    Assert.Equal(2, afterCancel.Items.Count);

                    // 第二台进入截屏但失败：也仍保持已点过且隐藏
                    InspectionPlanToken t2;
                    Assert.True(controller.TrySelectDevice("接入", out t2, out err));
                    Assert.True(sm.BeginCapture());
                    Assert.True(sm.FailCapture());
                    DeviceDisplaySnapshot afterFail = controller.BuildDeviceDisplay();
                    Assert.Equal(2, afterFail.VisitedCount);
                    Assert.Equal(1, afterFail.Items.Count);
                    Assert.Equal("汇聚", afterFail.Items[0].Name);
                }
                finally { SafeDeleteDir(dir); }
            });

            TestRunner.Run("集成：查看已点过可临时显现并补截不增量，恢复未点后重回未点列表", () =>
            {
                string dir = CreateTempDir();
                try
                {
                    var clock = new StepClock { Now = new DateTime(2026, 11, 14, 10, 0, 0) };
                    var store = new InspectionProgressStore(Path.Combine(dir, "progress.json"));
                    var cfg = BuildPlanConfig("D:\\Shots", new[] { "核心", "汇聚" }, true, true);
                    var sm = new PanelStateMachine();
                    var controller = new InspectionSessionController(clock, sm, store, cfg);

                    InspectionPlanToken token;
                    string err;
                    Assert.True(controller.TrySelectDevice("核心", out token, out err));
                    Assert.True(sm.BeginCapture());
                    Assert.True(sm.CompleteCapture());
                    Assert.Equal(1, controller.BuildDeviceDisplay().Items.Count);

                    // 临时查看已点过
                    Assert.True(controller.ToggleShowVisited());
                    DeviceDisplaySnapshot revealed = controller.BuildDeviceDisplay();
                    Assert.Equal(2, revealed.Items.Count);
                    Assert.True(revealed.Items[0].Visited);
                    Assert.False(revealed.Items[1].Visited);

                    // 补截已点过项不重复增加计数
                    Assert.True(controller.TrySelectDevice("核心", out token, out err));
                    Assert.True(sm.Cancel());
                    Assert.Equal(1, controller.Plan.VisitedCount);

                    // 关闭查看已点过并恢复未点
                    Assert.False(controller.ToggleShowVisited());
                    Assert.True(controller.UnmarkDevice("核心", out err));
                    DeviceDisplaySnapshot restored = controller.BuildDeviceDisplay();
                    Assert.Equal(0, restored.VisitedCount);
                    Assert.Equal(2, restored.Items.Count);
                }
                finally { SafeDeleteDir(dir); }
            });

            TestRunner.Run("集成：关闭隐藏设置保留全列表对勾；关闭计划显示全部且保留当日已点进度", () =>
            {
                string dir = CreateTempDir();
                try
                {
                    var clock = new StepClock { Now = new DateTime(2026, 11, 14, 11, 0, 0) };
                    var store = new InspectionProgressStore(Path.Combine(dir, "progress.json"));
                    var cfg = BuildPlanConfig("D:\\Shots", new[] { "D1", "D2" }, true, false);
                    var sm = new PanelStateMachine();
                    var controller = new InspectionSessionController(clock, sm, store, cfg);

                    InspectionPlanToken token;
                    string err;
                    Assert.True(controller.TrySelectDevice("D1", out token, out err));
                    Assert.True(sm.Cancel());

                    DeviceDisplaySnapshot visibleAll = controller.BuildDeviceDisplay();
                    Assert.Equal(2, visibleAll.Items.Count);
                    Assert.True(visibleAll.Items[0].Visited);
                    Assert.False(visibleAll.Items[1].Visited);

                    // 关掉计划后再打开，同日进度仍在
                    controller.SetPlanModeEnabled(false);
                    DeviceDisplaySnapshot planOff = controller.BuildDeviceDisplay();
                    Assert.Equal(2, planOff.Items.Count);
                    Assert.False(planOff.Items[0].Visited);

                    controller.SetPlanModeEnabled(true);
                    Assert.Equal(1, controller.Plan.VisitedCount);
                }
                finally { SafeDeleteDir(dir); }
            });

            TestRunner.Run("集成：全部点过空态、忙碌/弹窗期间抑制、回到空闲后只弹一次完成提示", () =>
            {
                string dir = CreateTempDir();
                try
                {
                    var clock = new StepClock { Now = new DateTime(2026, 11, 14, 14, 0, 0) };
                    var store = new InspectionProgressStore(Path.Combine(dir, "progress.json"));
                    var cfg = BuildPlanConfig("D:\\Shots", new[] { "D1", "D2" }, true, true);
                    var sm = new PanelStateMachine();
                    var controller = new InspectionSessionController(clock, sm, store, cfg);

                    InspectionPlanToken t1, t2;
                    string err;
                    Assert.True(controller.TrySelectDevice("D1", out t1, out err));
                    Assert.True(sm.BeginCapture());
                    Assert.True(sm.CompleteCapture());

                    // 点最后一台：进入 Waiting，此时不能弹窗
                    Assert.True(controller.TrySelectDevice("D2", out t2, out err));
                    DeviceDisplaySnapshot allHidden = controller.BuildDeviceDisplay();
                    Assert.Equal(0, allHidden.Items.Count);
                    Assert.Equal(DeviceListEmptyKind.AllVisitedHidden, allHidden.EmptyKind);
                    Assert.False(controller.TryClaimCompletionPrompt(false, false, false, out err));

                    // 进入 Capturing / 保存中 / 设置弹窗 / 隐身截图均不能弹窗
                    Assert.True(sm.BeginCapture());
                    Assert.False(controller.TryClaimCompletionPrompt(false, false, false, out err));
                    Assert.True(sm.CompleteCapture());
                    Assert.False(controller.TryClaimCompletionPrompt(true, false, false, out err));
                    Assert.False(controller.TryClaimCompletionPrompt(false, true, false, out err));
                    Assert.False(controller.TryClaimCompletionPrompt(false, false, true, out err));

                    // 空闲无阻塞时触发且仅触发一次
                    Assert.True(controller.TryClaimCompletionPrompt(false, false, false, out err));
                    Assert.False(controller.TryClaimCompletionPrompt(false, false, false, out err));

                    // 同日重启后也不重复弹窗，已点与隐藏均恢复
                    var restarted = new InspectionSessionController(clock, new PanelStateMachine(), store, cfg);
                    Assert.Equal(2, restarted.Plan.VisitedCount);
                    Assert.Equal(DeviceListEmptyKind.AllVisitedHidden, restarted.BuildDeviceDisplay().EmptyKind);
                    Assert.False(restarted.TryClaimCompletionPrompt(false, false, false, out err));
                }
                finally { SafeDeleteDir(dir); }
            });

            TestRunner.Run("集成：跨日自动重置与设置应用（改名保留、导入替换重置、取消无影响）", () =>
            {
                string dir = CreateTempDir();
                try
                {
                    var clock = new StepClock { Now = new DateTime(2026, 11, 14, 23, 59, 0) };
                    var store = new InspectionProgressStore(Path.Combine(dir, "progress.json"));
                    var cfg = BuildPlanConfig("D:\\Shots", new[] { "核心", "备机" }, true, true);
                    var sm = new PanelStateMachine();
                    var controller = new InspectionSessionController(clock, sm, store, cfg);

                    InspectionPlanToken token;
                    string err;
                    Assert.True(controller.TrySelectDevice("核心", out token, out err));
                    Assert.True(sm.Cancel());

                    // 改名保存：保留“核心”的已点过状态到“核心新”
                    AppConfig renamedCfg = AppConfigCloner.Clone(cfg);
                    renamedCfg.Devices = new List<string> { "核心新", "备机" };
                    controller.ApplySavedConfig(
                        renamedCfg,
                        new[] { new DeviceRenameRecord { OldName = "核心", NewName = "核心新" } },
                        false,
                        out err);
                    Assert.True(controller.Plan.IsVisited("核心新"));
                    Assert.Equal(1, controller.Plan.VisitedCount);

                    // 非合并导入替换：开新轮清空对勾
                    AppConfig importedCfg = AppConfigCloner.Clone(cfg);
                    importedCfg.Devices = new List<string> { "独立设备" };
                    controller.ApplySavedConfig(importedCfg, null, true, out err);
                    Assert.Equal(0, controller.Plan.VisitedCount);
                    Assert.Equal(1, controller.BuildDeviceDisplay().Items.Count);

                    // 重新点选并跨午夜后自动清空、重现全部设备
                    Assert.True(controller.TrySelectDevice("独立设备", out token, out err));
                    Assert.True(sm.Cancel());
                    Assert.Equal(DeviceListEmptyKind.AllVisitedHidden, controller.BuildDeviceDisplay().EmptyKind);

                    clock.Now = new DateTime(2026, 11, 15, 0, 1, 0);
                    bool rolled;
                    Assert.True(controller.EnsureCurrentDayResetIfRolled(out rolled));
                    Assert.True(rolled);
                    DeviceDisplaySnapshot nextDay = controller.BuildDeviceDisplay();
                    Assert.Equal(0, nextDay.VisitedCount);
                    Assert.Equal(1, nextDay.Items.Count);
                    Assert.Equal("独立设备", nextDay.Items[0].Name);
                }
                finally { SafeDeleteDir(dir); }
            });
        }

        private static AppConfig BuildPlanConfig(string rootPath, IEnumerable<string> devices,
            bool planMode, bool hideVisited)
        {
            AppConfig cfg = AppConfig.CreateDefault();
            cfg.RootPath = rootPath;
            cfg.Devices = new List<string>(devices);
            cfg.PlanModeEnabled = planMode;
            cfg.HideVisitedDevices = hideVisited;
            return cfg;
        }

        private static string CreateTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "iw-sb-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void SafeDeleteDir(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
            catch { }
        }
    }
}
