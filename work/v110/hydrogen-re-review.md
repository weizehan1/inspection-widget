# InspectionWidget v1.1.0-rc 范围限定独立复审报告

- **新受审提交（HEAD）**：`8fe14eae6bb0f58e9ca2d83e9a6c667bc7ff105f`
- **前次受审基线**：`96165980503e3c764eb4a45811afdbbad54b0553`
- **复审范围**：仅针对原审计报告 [hydrogen-audit.md](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/work/v110/hydrogen-audit.md) 与 [hydrogen-audit-findings.json](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/work/v110/hydrogen-audit-findings.json) 所列 10 项问题及本次修复 diff（`9616598..8fe14ea`）引入的潜在回归；原审计文件与源码/测试均保持未修改。
- **资产说明**：主负责人已于本次修复前置轮次通过 ImageGen 更新 `assets/app-icon.png`、`assets/app.ico.base64.txt` 与 `assets/app.ico`，且新 `AppIcon` 嵌入读取已由主负责人验证。
- **源码级复审结论**：**原 10 项问题源码与无头回归均已 `ADDRESSED`，未发现修复 diff 引入的新增代码缺陷（`hydrogen-re-review-findings.json` 为 `[]`）**；但 **19 条 Windows UI 用例（含新增 2 条 `SettingsUiTests` 与 6 条 `MainFormUiTests`）及真实 Windows 桌面特性仍未实跑**，在 Windows 实机闭环前不得直接宣布 Windows 可发布。

---

## 一、逐项复审裁定摘要

1. **Item 1（High，`progress.json` 读取保护与损坏备份重置）— `ADDRESSED`（Windows UI：`untested`）**
   - 代码在 [InspectionPlan.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/src/Planning/InspectionPlan.cs#L284-L398) 增加 `RestoreWithStatus`，先验证版本、字段、设备及路径合法性，再严格区分 `PreviousDayValid`（`< today`，不进入损坏写保护）与 `CorruptOrInvalid`。
   - [InspectionProgressStore.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/src/Planning/InspectionProgressStore.cs#L130-L166) 与 [InspectionSessionController.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/src/Planning/InspectionSessionController.cs#L193-L264) 实现写保护、备份失败保持保护以及 `RetryLoadProgress` / `BackupAndResetProgress`。
   - 独立复现 `CHECK-1`（`/tmp/opencode/inspection-v110/review2/rereview.log`）验证：`protected=True`、`preservedBytes=True`、`backupMatch=True`、`yestProtected=False`；协调者重跑 `REPRO-1` 得 `progress_overwritten=False`。
2. **Item 2（High，普通模式记录保存与兜底目录）— `ADDRESSED`（Windows `explorer.exe`：`untested`）**
   - [InspectionSessionController.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/src/Planning/InspectionSessionController.cs#L199-L217) 移除 `RecordSaveSucceeded` 的 `!PlanModeEnabled` 拦截。`CHECK-2` 与 `REPRO-2` 实跑均确认普通模式全暂存及重启后 `ResolveOpenFolders()` 返回真实暂存目录（`recorded=True savedCount=1 openCount=1 restartOpenCount=1 sameDir=True`）。
3. **Item 3（High，`progressErr` 可见性、不覆盖与持久化警示）— `ADDRESSED`（Windows UI：`untested`）**
   - 核查 [MainForm.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/src/MainForm.cs#L1394-L1483) 中 `RetryConfigLoad`、`OnSettingsClicked`、`OnDeviceRowActivated`、`OnSaveCompleted`、`OnResetRoundClicked`、`OnUnmarkDeviceClicked` 与 `TryMaybeShowCompletionPrompt`，均已显式处理 `progressErr` 且接入 `ShowProgressErrorFeedback` / `RestorePersistentFeedbackIfNeeded` / `CompletionPromptDialog` 警告，并提供「重试保存」入口；`CHECK-3` 验证落盘失败时 `HasProgressError=True` 且可调用 `RetrySaveProgress`。
4. **Item 4（High，托盘隐藏态不抢占完成弹窗 + 先登记 `_pendingImage` 再切 `Idle`）— `ADDRESSED`（Windows UI：`untested`）**
   - `TryClaimCompletionPrompt` 增加 `panelVisible` 门控，`TryMaybeShowCompletionPrompt`（[MainForm.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/src/MainForm.cs#L3343-L3382)）拦截 `!Visible || WindowState == FormWindowState.Minimized || _hiddenForCapture || Opacity < 0.5` 并始终以 `dialog.ShowDialog(this)` 附着可见宿主；`ShowPanelFromTray` 补调完成检查；`OnSaveCompleted` 先登记 `_pendingImage` 再进 `Idle`，且保留规格允许的「截图保存失败后可见面板带失败说明的一次完成询问」。`CHECK-4` 实测隐藏态返回 `False` 且不消耗 `CompletionPrompted`。
5. **Item 5（High，部分设置非合并导入不误清空当日进度）— `ADDRESSED`（Windows 专属 GUI：`not-applicable`）**
   - [ConfigTransfer.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/src/Config/ConfigTransfer.cs#L29-L169) 与 [SettingsDraft.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/src/Config/SettingsDraft.cs#L256-L260) 引入 `DevicesReplaced`。`CHECK-5` 与 `REPRO-3` 实测缺 `Devices` 的非合并导入保持 `PendingImportDeviceReplace=False`、`PendingDeviceRenames` 完整并保留已巡检计数。
6. **Item 6（Medium，跨午夜忙碌期间保留待刷新信号）— `ADDRESSED`（Windows UI：`untested`）**
   - 控制器保留 `_pendingRolloverUiRefresh` 直至被 `ConsumePendingRolloverRefresh()` 消费，[MainForm.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/src/MainForm.cs#L3323-L3341) 新增 `SyncRolloverUiIfIdle()` 并接入全部分支回退 `Idle` 路径；`CHECK-6` 验证先触发跨日与旧令牌拒收后，`HasPendingRolloverRefresh` 仍为 `True`。
7. **Item 7（Medium，`Waiting` 重复点击同设备回 `Idle`）— `ADDRESSED`（Windows UI：`untested`）**
   - [MainForm.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/src/MainForm.cs#L2200-L2218) 在取消回 `Idle` 时清空 `_waitingPlanToken`、触发 `OnStateChanged()`、`SyncRolloverUiIfIdle()`、`ShowFeedback("已取消选窗", false)` 与 `TryMaybeShowCompletionPrompt()`。
8. **Item 8（Medium，`Waiting` 态窗体尺寸/位置变动同步钩子 `SelfBounds`）— `ADDRESSED`（Windows UI：`untested`）**
   - [HookManager.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/src/Capture/HookManager.cs#L10-L19) 抽象 `IWindowPickHooks`，`MainForm.UpdateWaitingHookSelfBounds` 已接入 `ApplyHeightAnchored`、`OnSizeChanged`、`OnLocationChanged` 与 `ClampToWorkingArea`。
9. **Item 9（High，高 DPI `OnHandleCreated` 与字体寿命安全）— `ADDRESSED`（Windows UI：`untested`）**
   - `ApplyScaleAndRebuild`（[MainForm.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/src/MainForm.cs#L509-L545)）先确保全部分配新字体并调用 `ApplyScaleToStaticLayout` 与 `RefreshConfigState` 重建动态行，再统一 `Dispose` 旧字体集合；核查 `ApplyScaleToStaticLayout` 覆盖全部静态控件，无遗漏悬挂引用。
10. **Item 10（Medium，`AutoScroll` 滚动后布局偏移）— `ADDRESSED`（Windows UI：`untested`）**
    - `MainForm.LayoutSections` 与 [SettingsForm.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/src/SettingsForm.cs#L1533-L1736) 的 `PrepareAutoScrollPanel` 仅对 `AutoScroll=true` 容器的直接子控件计入 `AutoScrollPosition` 偏移并在行缩减时钳制滚动位置，未错误叠加内层子控件。

---

## 二、测试核查与 Windows 未验证缺口

- **已验证（macOS Mono Headless + 独立复现）**：
  - 协调者整仓构建回归：`/tmp/opencode/inspection-v110/codex-fixed-final-build.log` 报告 `439 / 439` 通过（含 `审计1`、`审计1b`、`审计2`、`审计4与3`、`审计5`、`审计6`）。
  - 协调者重跑旧复现：`/tmp/opencode/inspection-v110/codex-fixed-repro.log`（`REPRO-1..3` 修复生效）。
  - 独立复审校验程序：`/tmp/opencode/inspection-v110/review2/ReReviewCheck.cs` 编译引用 `build/InspectionWidget.exe` 执行 `/tmp/opencode/inspection-v110/review2/rereview.log`，`CHECK-1..6` 全部符合预期。
  - 新增 Windows 用例静态质量核查：[MainFormUiTests.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/tests/MainFormUiTests.cs)（6 条）与 [SettingsUiTests.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/tests/SettingsUiTests.cs#L414-L494)（第 12–13 条）均实例化真实 `TestMainForm` / `SettingsForm` 并以真实事件/方法推进控件状态，日期断言 `"2026年11月15日 星期日"` 与 `MainForm.FormatFullDate` 一致，未发现虚假断言。
- **未验证（Windows 实机缺口）**：
  - **19 条 Windows UI 用例（`SettingsUiTests` 13 条 + `MainFormUiTests` 6 条）** 在 macOS 上跳过，尚未在 Windows 实跑。
  - 真实 Windows 下的高 DPI 跨屏缩放字体渲染、`AutoScroll` 拖动后控件重排、托盘最小化/恢复弹窗、`explorer.exe` 拉起主/暂存目录及 Win32 低级鼠键钩子自身边界判定，须由 Windows 真机 `--windows-ui` 全量跑通并完成抽检后方可认定闭环。
