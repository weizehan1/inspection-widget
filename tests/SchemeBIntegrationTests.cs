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
                var doc = new ConfigImportDocument { Config = imported };
                doc.PresentFields.Add("Devices");
                ConfigImportPlan plan;
                string planErr;
                Assert.True(ConfigTransfer.TryBuildPlan(
                    doc, cfg,
                    new ConfigImportOptions { MergeDevicesOnly = false },
                    new DummyProtector(), out plan, out planErr));
                Assert.True(plan.DevicesReplaced);
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

            TestRunner.Run("审计1：progress.json损坏与同日坏快照均进入保护态，禁止静默覆盖，支持重试与先备份再重置", () =>
            {
                string dir = CreateTempDir();
                try
                {
                    var clock = new StepClock { Now = new DateTime(2026, 11, 14, 10, 0, 0) };
                    string progressPath = Path.Combine(dir, "progress.json");
                    byte[] brokenBytes = System.Text.Encoding.UTF8.GetBytes("{ broken json ");
                    File.WriteAllBytes(progressPath, brokenBytes);

                    var store = new InspectionProgressStore(progressPath);
                    var cfg = BuildPlanConfig("D:\\Shots", new[] { "核心", "备机" }, true, true);
                    var sm = new PanelStateMachine();
                    var controller = new InspectionSessionController(clock, sm, store, cfg);

                    Assert.True(controller.IsProgressProtected);
                    Assert.True(controller.HasProgressError);
                    Assert.NotNull(controller.ProgressError);

                    // 后续选窗、保存路径、应用配置、取消标记、重置本轮都不得静默覆盖坏原件
                    InspectionPlanToken token;
                    string err;
                    Assert.True(controller.TrySelectDevice("核心", out token, out err));
                    Assert.NotNull(err);
                    Assert.True(sm.Cancel());
                    Assert.True(controller.RecordSaveSucceeded(Path.Combine(dir, "shot.png"), token, out err));
                    Assert.NotNull(err);
                    controller.ApplySavedConfig(cfg, null, false, out err);
                    Assert.NotNull(err);
                    Assert.Equal(brokenBytes.Length, File.ReadAllBytes(progressPath).Length);

                    // 同日语义坏快照（勾选不在清单内的设备）同样进入保护态且不覆盖
                    string sameDayCorruptPath = Path.Combine(dir, "same-day-bad.json");
                    var sameDayBadPlan = new InspectionPlan(clock, new[] { "旧设备" });
                    InspectionPlanToken badToken = sameDayBadPlan.CurrentToken;
                    sameDayBadPlan.MarkVisited("旧设备", badToken);
                    var badSnap = sameDayBadPlan.CreateSnapshot();
                    badSnap.Devices = new List<string> { "核心" }; // VisitedDevices 还含 "旧设备" -> Restore 失败
                    var tempStore = new InspectionProgressStore(sameDayCorruptPath);
                    Assert.True(tempStore.TrySave(badSnap, out err), err);
                    byte[] sameDayBadBytes = File.ReadAllBytes(sameDayCorruptPath);

                    var badController = new InspectionSessionController(clock, new PanelStateMachine(), tempStore, cfg);
                    Assert.True(badController.IsProgressProtected);
                    Assert.True(badController.TrySelectDevice("核心", out token, out err));
                    Assert.NotNull(err);
                    Assert.Equal(sameDayBadBytes.Length, File.ReadAllBytes(sameDayCorruptPath).Length);

                    // 备份失败时绝不解除保护或覆写原文件
                    var failBackupStore = new InspectionProgressStore(
                        progressPath, AtomicConfigWriter.ForTests(new FailingBackupOps()));
                    var failCtrl = new InspectionSessionController(clock, new PanelStateMachine(), failBackupStore, cfg);
                    Assert.True(failCtrl.IsProgressProtected);
                    string backupPath;
                    Assert.False(failCtrl.BackupAndResetProgress(out backupPath, out err));
                    Assert.True(failCtrl.IsProgressProtected);
                    Assert.NotNull(err);
                    Assert.Equal(brokenBytes.Length, File.ReadAllBytes(progressPath).Length);

                    // 成功备份后才清理保护并写入新轮
                    Assert.True(controller.BackupAndResetProgress(out backupPath, out err), err);
                    Assert.False(controller.IsProgressProtected);
                    Assert.False(controller.HasProgressError);
                    Assert.True(File.Exists(backupPath));
                    Assert.Equal(brokenBytes.Length, File.ReadAllBytes(backupPath).Length);
                    Assert.Equal(0, controller.Plan.VisitedCount);

                    // 修复损坏文件后显式 RetryLoadProgress 可解除保护恢复进度
                    string retryPath = Path.Combine(dir, "retry-progress.json");
                    File.WriteAllBytes(retryPath, brokenBytes);
                    var retryStore = new InspectionProgressStore(retryPath);
                    var retryCtrl = new InspectionSessionController(clock, new PanelStateMachine(), retryStore, cfg);
                    Assert.True(retryCtrl.IsProgressProtected);
                    Assert.False(retryCtrl.RetryLoadProgress(out err));
                    Assert.True(retryCtrl.IsProgressProtected);

                    var goodPlan = new InspectionPlan(clock, new[] { "核心", "备机" });
                    goodPlan.MarkVisited("核心", goodPlan.CurrentToken);
                    Assert.True(retryStore.TrySave(goodPlan.CreateSnapshot(), out err), err);
                    Assert.True(retryCtrl.RetryLoadProgress(out err), err);
                    Assert.False(retryCtrl.IsProgressProtected);
                    Assert.Equal(1, retryCtrl.Plan.VisitedCount);
                }
                finally { SafeDeleteDir(dir); }
            });

            TestRunner.Run("审计1b：有效的昨日进度不是损坏，次日新轮不进入永久写保护且可正常写入", () =>
            {
                string dir = CreateTempDir();
                try
                {
                    var day1Clock = new StepClock { Now = new DateTime(2026, 11, 14, 18, 0, 0) };
                    string progressPath = Path.Combine(dir, "progress.json");
                    var store = new InspectionProgressStore(progressPath);
                    var cfg = BuildPlanConfig("D:\\Shots", new[] { "核心", "备机" }, true, true);
                    var day1Plan = new InspectionPlan(day1Clock, cfg.Devices);
                    Assert.True(day1Plan.MarkVisited("核心", day1Plan.CurrentToken));
                    string err;
                    Assert.True(store.TrySave(day1Plan.CreateSnapshot(), out err), err);

                    // 次日启动：读取到昨日有效进度 -> 不得进入写保护，今日可正常点击保存
                    var day2Clock = new StepClock { Now = new DateTime(2026, 11, 15, 9, 0, 0) };
                    var day2Ctrl = new InspectionSessionController(day2Clock, new PanelStateMachine(), store, cfg);
                    Assert.False(day2Ctrl.IsProgressProtected);
                    Assert.False(day2Ctrl.HasProgressError);
                    Assert.Equal(0, day2Ctrl.Plan.VisitedCount);

                    InspectionPlanToken token;
                    Assert.True(day2Ctrl.TrySelectDevice("核心", out token, out err));
                    Assert.Null(err);
                    Assert.Equal(1, day2Ctrl.Plan.VisitedCount);
                }
                finally { SafeDeleteDir(dir); }
            });

            TestRunner.Run("审计2：计划关闭时仍记录全部暂存与混合保存目录，重启后打开目录不遗漏", () =>
            {
                string dir = CreateTempDir();
                try
                {
                    var clock = new StepClock { Now = new DateTime(2026, 11, 14, 11, 0, 0) };
                    string rootDir = Path.Combine(dir, "PrimaryRoot");
                    string fallbackDir = Path.Combine(dir, "FallbackShots", "2026-11-14");
                    Directory.CreateDirectory(fallbackDir);

                    var cfg = BuildPlanConfig(rootDir, new[] { "核心", "备机" }, false, true);
                    string progressPath = Path.Combine(dir, "progress.json");
                    var store = new InspectionProgressStore(progressPath);
                    var sm = new PanelStateMachine();
                    var controller = new InspectionSessionController(clock, sm, store, cfg);

                    // 情况A：PlanMode=false 且仅落入暂存目录（主当日目录尚不存在）
                    InspectionPlanToken t1;
                    string err;
                    Assert.True(controller.TrySelectDevice("核心", out t1, out err));
                    Assert.Equal(0, controller.Plan.VisitedCount); // 计划关闭不计进度
                    Assert.True(sm.BeginCapture());
                    Assert.True(sm.CompleteCapture());
                    string fallbackShot = Path.Combine(fallbackDir, "核心.png");
                    File.WriteAllText(fallbackShot, "img");
                    Assert.True(controller.RecordSaveSucceeded(fallbackShot, t1, out err), err);
                    Assert.Null(err);

                    List<string> foldersA = controller.ResolveOpenFolders();
                    Assert.Equal(1, foldersA.Count);
                    Assert.Equal(fallbackDir, foldersA[0]);

                    // 重启后依旧能找回该暂存目录
                    var restartedA = new InspectionSessionController(clock, new PanelStateMachine(), store, cfg);
                    List<string> foldersAfterRestartA = restartedA.ResolveOpenFolders();
                    Assert.Equal(1, foldersAfterRestartA.Count);
                    Assert.Equal(fallbackDir, foldersAfterRestartA[0]);

                    // 情况B：主当日目录与暂存目录混合，重启后两者都列出且主当日目录排首
                    string primaryTodayDir = Path.Combine(rootDir, "巡检-2026-11-14");
                    Directory.CreateDirectory(primaryTodayDir);
                    string primaryShot = Path.Combine(primaryTodayDir, "备机.png");
                    File.WriteAllText(primaryShot, "img2");

                    var smB = restartedA;
                    var machineB = new PanelStateMachine();
                    var ctrlB = new InspectionSessionController(clock, machineB, store, cfg);
                    InspectionPlanToken t2;
                    Assert.True(ctrlB.TrySelectDevice("备机", out t2, out err));
                    Assert.True(machineB.BeginCapture());
                    Assert.True(machineB.CompleteCapture());
                    Assert.True(ctrlB.RecordSaveSucceeded(primaryShot, t2, out err));

                    var restartedB = new InspectionSessionController(clock, new PanelStateMachine(), store, cfg);
                    List<string> foldersB = restartedB.ResolveOpenFolders();
                    Assert.Equal(2, foldersB.Count);
                    Assert.Equal(primaryTodayDir, foldersB[0]);
                    Assert.Equal(fallbackDir, foldersB[1]);
                }
                finally { SafeDeleteDir(dir); }
            });

            TestRunner.Run("审计4与3：托盘隐藏时不认领完成提示、恢复后可认领；通知落盘失败暴露警告状态", () =>
            {
                string dir = CreateTempDir();
                try
                {
                    var clock = new StepClock { Now = new DateTime(2026, 11, 14, 14, 0, 0) };
                    string progressPath = Path.Combine(dir, "progress.json");
                    var store = new InspectionProgressStore(progressPath);
                    var cfg = BuildPlanConfig("D:\\Shots", new[] { "核心" }, true, true);
                    var sm = new PanelStateMachine();
                    var ctrl = new InspectionSessionController(clock, sm, store, cfg);

                    InspectionPlanToken token;
                    string err;
                    Assert.True(ctrl.TrySelectDevice("核心", out token, out err));
                    Assert.True(sm.Cancel());

                    // 托盘隐藏（panelVisible=false）不得认领且不消耗资格
                    Assert.False(ctrl.TryClaimCompletionPrompt(false, false, false, false, out err));
                    Assert.False(ctrl.Plan.CompletionPrompted);

                    // 恢复面板（panelVisible=true）后认领成功
                    Assert.True(ctrl.TryClaimCompletionPrompt(false, false, false, true, out err));
                    Assert.True(ctrl.Plan.CompletionPrompted);

                    // 通知落盘失败时 progressErr != null 且控制器维持 HasProgressError
                    var failWriteOps = new FailingBackupOps();
                    var failCtrl = new InspectionSessionController(
                        clock,
                        new PanelStateMachine(),
                        new InspectionProgressStore(Path.Combine(dir, "fail.json"), AtomicConfigWriter.ForTests(failWriteOps)),
                        cfg);
                    Assert.True(failCtrl.TrySelectDevice("核心", out token, out err));
                    failCtrl.Plan.Unmark("核心"); // 模拟前序操作成功，最后完成落盘失败
                    Assert.True(failCtrl.Plan.MarkVisited("核心", token));
                    Assert.NotNull(err); // TrySelectDevice 的落盘已失败
                    Assert.True(failCtrl.HasProgressError);
                    // 换用允许第一次写入、在 Claim 时拒绝替换的 writer 测试 Claim 落盘失败
                    var toggleOps = new ToggleFailWriteOps();
                    string togglePath = Path.Combine(dir, "toggle-fail.json");
                    var toggleSm = new PanelStateMachine();
                    var toggleCtrl = new InspectionSessionController(
                        clock, toggleSm,
                        new InspectionProgressStore(togglePath, AtomicConfigWriter.ForTests(toggleOps)),
                        cfg);
                    Assert.True(toggleCtrl.TrySelectDevice("核心", out token, out err));
                    Assert.Null(err);
                    Assert.True(toggleSm.Cancel());
                    toggleOps.ShouldFail = true;
                    Assert.True(toggleCtrl.TryClaimCompletionPrompt(false, false, false, true, out err));
                    Assert.NotNull(err);
                    Assert.True(toggleCtrl.HasProgressError);
                }
                finally { SafeDeleteDir(dir); }
            });

            TestRunner.Run("审计5：非合并部分设置导入未含Devices时不设置DevicesReplaced，保留当日勾选与改名映射", () =>
            {
                string dir = CreateTempDir();
                try
                {
                    AppConfig current = BuildPlanConfig("D:\\Shots", new[] { "核心A", "核心B" }, true, true);
                    SettingsDraft draft = SettingsDraft.FromConfig(current, "rev1");
                    draft.ApplyDeviceRename("核心A", "核心A-改");
                    Assert.Equal(1, draft.PendingDeviceRenames.Count);

                    ConfigImportDocument partialDoc;
                    string parseErr;
                    string json = "{\"Format\":\"InspectionWidget.Settings\",\"Version\":1,"
                        + "\"CredentialMode\":\"Excluded\","
                        + "\"Config\":{\"RootPath\":\"D:\\\\Shots\",\"HideVisitedDevices\":false}}";
                    Assert.True(ConfigJsonReader.TryParse(json, out partialDoc, out parseErr), parseErr);
                    Assert.False(partialDoc.Has("Devices"));

                    ConfigImportPlan plan;
                    string planErr;
                    Assert.True(ConfigTransfer.TryBuildPlan(
                        partialDoc, current,
                        new ConfigImportOptions { MergeDevicesOnly = false },
                        new DummyProtector(), out plan, out planErr), planErr);
                    Assert.False(plan.DevicesReplaced);

                    draft.ApplyPlan(plan, false);
                    Assert.False(draft.PendingImportDeviceReplace);
                    Assert.Equal(1, draft.PendingDeviceRenames.Count);
                    Assert.False(draft.HideVisitedDevices);

                    // 验证控制器收到 false 后不会重置今日已点过状态
                    var clock = new StepClock { Now = new DateTime(2026, 11, 14, 15, 0, 0) };
                    var store = new InspectionProgressStore(Path.Combine(dir, "progress.json"));
                    var ctrl = new InspectionSessionController(clock, new PanelStateMachine(), store, current);
                    InspectionPlanToken token;
                    string err;
                    Assert.True(ctrl.TrySelectDevice("核心A", out token, out err));
                    AppConfig afterPartial = AppConfigCloner.Clone(current);
                    afterPartial.Devices = new List<string> { "核心A-改", "核心B" };
                    afterPartial.HideVisitedDevices = false;
                    ctrl.ApplySavedConfig(afterPartial, draft.PendingDeviceRenames, draft.PendingImportDeviceReplace, out err);
                    Assert.True(ctrl.Plan.IsVisited("核心A-改"));
                    Assert.Equal(1, ctrl.Plan.VisitedCount);
                }
                finally { SafeDeleteDir(dir); }
            });

            TestRunner.Run("审计6：跨午夜忙碌期间其他调用先触发EnsureCurrentDay后，待刷新标志保留且旧轮不混入新轮", () =>
            {
                string dir = CreateTempDir();
                try
                {
                    var clock = new StepClock { Now = new DateTime(2026, 11, 14, 23, 59, 50) };
                    var store = new InspectionProgressStore(Path.Combine(dir, "progress.json"));
                    var cfg = BuildPlanConfig("D:\\Shots", new[] { "核心", "备机" }, true, true);
                    var sm = new PanelStateMachine();
                    var ctrl = new InspectionSessionController(clock, sm, store, cfg);

                    InspectionPlanToken day1Token;
                    string err;
                    Assert.True(ctrl.TrySelectDevice("核心", out day1Token, out err));
                    Assert.True(sm.BeginCapture());

                    // 跨过午夜，保存回调先于定时器或恢复前调用 EnsureCurrentDay / RecordSaveSucceeded
                    clock.Now = new DateTime(2026, 11, 15, 0, 0, 5);
                    string day1Shot = Path.Combine(dir, "巡检-2026-11-14", "核心.png");
                    Assert.False(ctrl.RecordSaveSucceeded(day1Shot, day1Token, out err));
                    Assert.Equal(0, ctrl.Plan.VisitedCount);

                    // 虽然 Plan.Date 已切到次日，但待 UI 刷新信号必须仍然保留直到消费
                    Assert.True(ctrl.HasPendingRolloverRefresh);
                    bool rolled;
                    Assert.False(ctrl.EnsureCurrentDayResetIfRolled(out rolled));
                    Assert.True(ctrl.HasPendingRolloverRefresh);
                    Assert.True(ctrl.ConsumePendingRolloverRefresh());
                    Assert.False(ctrl.HasPendingRolloverRefresh);
                }
                finally { SafeDeleteDir(dir); }
            });
        }

        private sealed class FailingBackupOps : AtomicConfigWriter.IFileOperations
        {
            public bool Exists(string path) { return File.Exists(path); }
            public void Replace(string source, string destination) { throw new IOException("模拟写入失败"); }
            public void Move(string source, string destination) { throw new IOException("模拟写入失败"); }
            public void Delete(string path) { if (File.Exists(path)) File.Delete(path); }
            public void WriteAllBytes(string path, byte[] bytes) { File.WriteAllBytes(path, bytes); }
            public void FlushDirectory(string directory) { }
        }

        private sealed class ToggleFailWriteOps : AtomicConfigWriter.IFileOperations
        {
            public bool ShouldFail;
            public bool Exists(string path) { return File.Exists(path); }
            public void Replace(string source, string destination)
            {
                if (ShouldFail) throw new IOException("模拟替换失败");
                File.Replace(source, destination, null, true);
            }
            public void Move(string source, string destination)
            {
                if (ShouldFail) throw new IOException("模拟移动失败");
                if (File.Exists(destination)) File.Delete(destination);
                File.Move(source, destination);
            }
            public void Delete(string path) { if (File.Exists(path)) File.Delete(path); }
            public void WriteAllBytes(string path, byte[] bytes) { File.WriteAllBytes(path, bytes); }
            public void FlushDirectory(string directory) { }
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
