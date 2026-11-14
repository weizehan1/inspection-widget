# InspectionWidget v1.1.0-rc Hydrogen 审计缺陷修复执行记录

- 分支与基线：`feat/v1.1.0-inspection-redesign` @ `1cb70d8`
- 备份目录：`/tmp/opencode/inspection-v110/backup-1cb70d8/`（已备份修改前目标源码）
- 构建与日志：`/tmp/opencode/inspection-v110/hydrogen-fix-build.log`、`hydrogen-fix-winui.log`
- 约束遵守：`assets/` 未修改；未改动 `work/v110/hydrogen-audit-findings.json` 状态；未执行提交/推送/释放/子代理操作。

## 按审计条目落地的代码与测试清单

1. **Item 1（High，`progress.json` 读取失败静默覆盖 + 次日有效旧文件语义区分）**
   - [src/Planning/InspectionPlan.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/src/Planning/InspectionPlan.cs)：新增 `ProgressRestoreStatus`（`Restored` / `PreviousDayValid` / `CorruptOrInvalid`）与 `RestoreWithStatus(...)`，先校验结构再按日期判定（`< today` 归为 `PreviousDayValid`，`> today` 归为 `CorruptOrInvalid`），原 `Restore(...)` 委托至该方法以保持既有契约。
   - [src/Planning/InspectionProgressStore.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/src/Planning/InspectionProgressStore.cs)：`TryLoad` 先缓存原始字节 `_lastLoadedBytes`，新增 `TryBackupCorruptCopy(out backupPath, out error)`（落盘 `progress.corrupt.<yyyyMMdd-HHmmss>.<8hex>.json`）。
   - [src/Planning/InspectionSessionController.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/src/Planning/InspectionSessionController.cs)：新增 `IsProgressProtected`、`RetryLoadProgress`、`BackupAndResetProgress`；`IsProgressProtected == true` 时拒绝写盘；`PreviousDayValid` 视作干净新轮而不进入永久写保护。
   - [src/MainForm.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/src/MainForm.cs)：在 `_planCardPanel` 内新增 `_planRecoveryButton` 与 `ProgressRecoveryDialog` / `HandleProgressRecoveryAction`（支持重试读取、先备份再重置新轮）。
   - 回归：[tests/SchemeBIntegrationTests.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/tests/SchemeBIntegrationTests.cs)（`审计1`、`审计1b`）、[tests/MainFormUiTests.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/tests/MainFormUiTests.cs)（损坏状态持久提示与恢复按钮）。

2. **Item 2（High，`PlanModeEnabled == false` 路径未记录 `SavedPaths`）**
   - [src/Planning/InspectionSessionController.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/src/Planning/InspectionSessionController.cs)：移除 `RecordSaveSucceeded` 中的 `!PlanModeEnabled` 拦截，普通模式同样持久化 `SavedPaths`（含 fallback 暂存路径）。
   - 回归：[tests/SchemeBIntegrationTests.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/tests/SchemeBIntegrationTests.cs)（`审计2`）。

3. **Item 3（Medium，`progress.json` 写盘失败可感知提示）**
   - [src/Planning/InspectionSessionController.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/src/Planning/InspectionSessionController.cs)：暴露 `ProgressError`、`HasProgressError`、`RetrySaveProgress(out error)`，并在 5 参 `TryClaimCompletionPrompt` 中透出 `out error`。
   - [src/MainForm.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/src/MainForm.cs)：所有进度写盘调用点（`RetryConfigLoad`、`OnSettingsClicked`、`OnDeviceRowActivated`、`OnSaveCompleted`、`OnResetRoundClicked`、`OnUnmarkDeviceClicked`、`TryMaybeShowCompletionPrompt`）接入 `ShowProgressErrorFeedback` 与 `RestorePersistentFeedbackIfNeeded`，并在 `_planCardPanel` 展示「重试保存」，`CompletionPromptDialog` 对持久化失败附带警告提示。
   - 回归：[tests/SchemeBIntegrationTests.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/tests/SchemeBIntegrationTests.cs)（`审计4与3`）。

4. **Item 4 + Extra（High，托盘隐藏态不可见主窗抢认领完成弹窗；保存失败先登记 `_pendingImage` 再回 `Idle`）**
   - [src/Planning/InspectionSessionController.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/src/Planning/InspectionSessionController.cs)：新增 5 参 `TryClaimCompletionPrompt(..., hiddenForCapture, panelVisible, out error)`（保留 4 参兼容重载）。
   - [src/MainForm.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/src/MainForm.cs)：`TryMaybeShowCompletionPrompt` 在 `!Visible || WindowState == FormWindowState.Minimized || _hiddenForCapture || Opacity < 0.5` 时中止；`ShowPanelFromTray` 恢复后调用 `SyncRolloverUiIfIdle()` 与 `TryMaybeShowCompletionPrompt()`；`OnSaveCompleted` 在进入 `Idle` 并触发 `TryMaybeShowCompletionPrompt()` 之前先完成 `_pendingImage` / `_pendingRequest` 与 `RecordSaveSucceeded` 收尾。
   - 回归：[tests/SchemeBIntegrationTests.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/tests/SchemeBIntegrationTests.cs)（`审计4与3`）、[tests/MainFormUiTests.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/tests/MainFormUiTests.cs)（保存失败先登记 `_pendingImage`、不可见不认领）。

5. **Item 5（High，部分设置非合并导入未含 `Devices` 时误置 `DevicesReplaced`）**
   - [src/Config/ConfigTransfer.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/src/Config/ConfigTransfer.cs)：`ConfigImportPlan` 增加 `DevicesReplaced`，仅在 `!options.MergeDevicesOnly && document.Has("Devices")` 时置 `true`。
   - [src/Config/SettingsDraft.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/src/Config/SettingsDraft.cs)：`ApplyPlan` 仅当 `plan.DevicesReplaced` 为 `true` 时设置 `PendingImportDeviceReplace = true` 并清空 `PendingDeviceRenames`。
   - 回归：[tests/SchemeBIntegrationTests.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/tests/SchemeBIntegrationTests.cs)（同步更新既有草稿测试用例明确设置 `PresentFields.Add("Devices")`，并新增 `审计5`）。

6. **Item 6（Medium，跨午夜期间其他逻辑先触发 `EnsureCurrentDay` 导致 UI 漏刷新）**
   - [src/Planning/InspectionSessionController.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/src/Planning/InspectionSessionController.cs)：`EnsureCurrentDayResetIfRolled` 维护 `_pendingRolloverUiRefresh`、`HasPendingRolloverRefresh`、`ConsumePendingRolloverRefresh()`。
   - [src/MainForm.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/src/MainForm.cs)：新增 `SyncRolloverUiIfIdle`（跟踪 `_uiRenderedPlanDate` 与 `_pendingDayRolloverRefresh`）并在空闲收敛路径触发。
   - 回归：[tests/SchemeBIntegrationTests.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/tests/SchemeBIntegrationTests.cs)（`审计6`）、[tests/MainFormUiTests.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/tests/MainFormUiTests.cs)（Waiting 跨日取消回 Idle 刷新）。

7. **Item 7（Medium，Waiting 态再次点击当前设备取消未还原完成提示与待刷新）**
   - [src/MainForm.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/src/MainForm.cs)：`OnDeviceRowActivated` 重复点击当前设备取消回 `Idle` 时清空 `_waitingPlanToken`，依次调用 `OnStateChanged()`、`SyncRolloverUiIfIdle()`、`ShowFeedback("已取消选窗", false)`、`TryMaybeShowCompletionPrompt()`，不主动抢焦点。
   - 回归：[tests/MainFormUiTests.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/tests/MainFormUiTests.cs)。

8. **Item 8（Medium，Waiting 态面板高度/位置变动未同步钩子 `SelfBounds`）**
   - [src/Capture/HookManager.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/src/Capture/HookManager.cs)：抽出 `internal interface IWindowPickHooks : IDisposable` 供测试注入，新增 `CurrentSelfBounds`。
   - [src/MainForm.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/src/MainForm.cs)：新增 `UpdateWaitingHookSelfBounds()`（带 `!_hooksDisposed && !IsDisposed` 守卫）并接入 `ApplyHeightAnchored`、`OnSizeChanged`、`OnLocationChanged`、`ClampToWorkingArea`。
   - 回归：[tests/MainFormUiTests.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/tests/MainFormUiTests.cs)。

9. **Item 9（High，缩放重建前先销毁旧 Font 与 `OnHandleCreated` DPI 漂移）**
   - [src/MainForm.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/src/MainForm.cs)：新增 `SwapScaledFont`、`EnsureScaledFonts(List<IDisposable>)`、`ApplyScaleAndRebuild`，将旧字体延后至静态/动态控件完成重新绑定后再统一释放；移除 `RebuildQuickLinks` 内每行临时 `new Font`；`OnHandleCreated` 检测 `ScaleFactor != _appliedScaleFactor` 时重算宽高与控件；新增 `GetSystemDpiScale()` virtual 钩子与测试构造器。
   - 回归：[tests/MainFormUiTests.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/tests/MainFormUiTests.cs)（`OnHandleCreated` DPI `1.0 -> 2.0` 全控件 Font 存活校验）。

10. **Item 10（Medium，`AutoScroll=true` 容器内手动定位未计入 `AutoScrollPosition` 偏移）**
    - [src/MainForm.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/src/MainForm.cs)：`LayoutSections` 先按逻辑 Y 计算总高并设 `_scrollHost.AutoScrollMinSize`、钳制滚动，再仅对 `_scrollHost` 直接子控件叠加 `_scrollHost.AutoScrollPosition`（绝不对非滚动容器下层子控件叠加）。
    - [src/SettingsForm.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/src/SettingsForm.cs)：新增 `PrepareAutoScrollPanel`，更新 `LayoutCredentialRows` 与 `LayoutLinkRows` 对 `_credentialRowsPanel`、`_linkRowsPanel` 直接子行叠加 `AutoScrollPosition` 并在新增行时 `ScrollControlIntoView`。
    - 回归：[tests/SettingsUiTests.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/tests/SettingsUiTests.cs)（`CaseCount` `11 -> 13`）、[tests/MainFormUiTests.cs](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/tests/MainFormUiTests.cs)。
