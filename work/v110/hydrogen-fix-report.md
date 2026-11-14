# InspectionWidget v1.1.0-rc Hydrogen 审计缺陷修复报告

## 结论与影响

- **已完成的审计修复**：`work/v110/hydrogen-audit-fix-brief.md` 所列 **5 个 High + 5 个 Medium 缺陷及 1 项集成边界约束（`_pendingImage` 与完成弹窗次序）** 全部完成源码修复与本地回归建设。
- **Headless 回归与构建**：在 macOS + Mono 下执行 `DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib bash build.sh` 构建成功（`BUILD_EXIT=0`，零 C# 源码警告），全部 **439 / 439** 条 headless 测试通过（较修复前增加 6 组审计回归）；`assets/` 未做任何改动。
- **Windows UI 实跑状态**：新增了 **2** 条 `SettingsUiTests`（`AutoScroll` 凭据/链接滚动对齐）和 **6** 条 `MainFormUiTests`（DPI 缩放无已释放字体、主面板 `_scrollHost` 滚动对齐、Waiting 钩子 `SelfBounds` 同步、重复点击取消、`progress.json` 保护与保存失败先登记 `_pendingImage`、跨日延迟刷新），Windows UI 用例总计 **19** 条；受当前 macOS 环境限制，未运行实机 WinForms 断言（`--windows-ui` 返回退出码 `2` 并准确跳过 19 条），**必须由独立复审或 Windows 环境实跑通过 `--windows-ui` 与真机窗口操作后才算闭环**。

## 逐项对照表

| 条目 | 级别 | 修复核心与符号 | 新增/更新回归 |
| --- | --- | --- | --- |
| 1 | High | `InspectionPlan.RestoreWithStatus` / `ProgressRestoreStatus`（区分 `PreviousDayValid` 与 `CorruptOrInvalid`）、`InspectionProgressStore.TryBackupCorruptCopy`、`InspectionSessionController` 写保护与 `RetryLoadProgress`/`BackupAndResetProgress`、`MainForm._planRecoveryButton` | `SchemeBIntegrationTests`（审计1、审计1b）、`MainFormUiTests`（Case 5） |
| 2 | High | `InspectionSessionController.RecordSaveSucceeded` 取消 `!PlanModeEnabled` 门禁 | `SchemeBIntegrationTests`（审计2） |
| 3 | Medium | `InspectionSessionController.HasProgressError` / `RetrySaveProgress`、`MainForm.ShowProgressErrorFeedback` / `RestorePersistentFeedbackIfNeeded`、`CompletionPromptDialog` 警告 | `SchemeBIntegrationTests`（审计4与3） |
| 4 + 约束 | High | 5 参 `TryClaimCompletionPrompt(..., hiddenForCapture, panelVisible, out error)`、`MainForm.TryMaybeShowCompletionPrompt` 可见性校验、`ShowPanelFromTray` 补触发、`OnSaveCompleted` 先完成 `_pendingImage` 与 `RecordSaveSucceeded` 再切 `Idle` 并弹完成提示 | `SchemeBIntegrationTests`（审计4与3）、`MainFormUiTests`（Case 5） |
| 5 | High | `ConfigImportPlan.DevicesReplaced`、`SettingsDraft.ApplyPlan` 仅在 `plan.DevicesReplaced` 时清理改名与设替换标记 | `SchemeBIntegrationTests`（同步既有草稿断言 + 审计5） |
| 6 | Medium | `InspectionSessionController._pendingRolloverUiRefresh` / `ConsumePendingRolloverRefresh`、`MainForm.SyncRolloverUiIfIdle` | `SchemeBIntegrationTests`（审计6）、`MainFormUiTests`（Case 6） |
| 7 | Medium | `MainForm.OnDeviceRowActivated` 取消分支清空 `_waitingPlanToken`、触发 `SyncRolloverUiIfIdle` 与 `TryMaybeShowCompletionPrompt` | `MainFormUiTests`（Case 4） |
| 8 | Medium | `IWindowPickHooks`、`MainForm.UpdateWaitingHookSelfBounds()` 接入高度/位置变更并防 `_hooksDisposed` | `MainFormUiTests`（Case 3） |
| 9 | High | `MainForm.ApplyScaleAndRebuild` 延迟批量释放旧字体、`OnHandleCreated` DPI 漂移重建、`RebuildQuickLinks` 复用字号 | `MainFormUiTests`（Case 1） |
| 10 | Medium | `MainForm.LayoutSections`（仅 `_scrollHost` 直接子控件）、`SettingsForm.PrepareAutoScrollPanel` + `LayoutCredentialRows`/`LayoutLinkRows`（仅直接子行）叠加 `AutoScrollPosition` | `SettingsUiTests`（新增 2 条，共 13 条）、`MainFormUiTests`（Case 2） |

## 实测证据（最终编辑后执行）

1. **完整构建与 Headless 回归**：
   - 命令：`DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib bash build.sh`
   - 日志：`/tmp/opencode/inspection-v110/hydrogen-fix-build.log`
   - 结果：`EXIT=0`；测试汇总：`用例总数：439，通过：439，失败：0`（`0` C# 源码编译警告，仅余既有的 Mono `xbuild` `warning CS2002: Source file '...AssemblyAttribute.cs' specified multiple times`）。
2. **Windows UI 跳过语义核对**：
   - 命令：`DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib mono tests/bin/Release/InspectionWidget.Tests.exe --windows-ui`
   - 日志：`/tmp/opencode/inspection-v110/hydrogen-fix-winui.log`
   - 结果：`EXIT=2`；输出：`[SKIP] Windows UI：当前不是 Windows，未执行 19 条；这些用例不计为通过。`；Headless 部分 `439 / 439` 通过。
3. **产物与哈希（2026-11-14）**：
   - [build/InspectionWidget.exe](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/build/InspectionWidget.exe)：SHA-256 `a8d5b91bdc5fbd15c38e7af17977c1377cedd65ce2d0b6b96df5d93b7316e8cf`
   - [build/InspectionWidget.exe.manifest](file:///Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110/build/InspectionWidget.exe.manifest)：SHA-256 `1b0630484ce745aa3d5d8a9380da7ea92ed97a365a62299d4fe3a46c7b838f70`

## Windows 待验证清单与已知边界

- **自动化**：在 Windows 上运行 `InspectionWidget.Tests.exe --windows-ui`，确认新增与既有共 **19 条** WinForms 控件用例全部 `[PASS]` 且总计 **458 条**通过（退出码 `0`）。
- **人工抽检建议**：
  1. 跨显示器不同缩放比例拖动或系统缩放切换（100% <-> 150%/200%）下观察主面板按钮文字与布局，确认无系统字体对象异常。
  2. 分别让设置窗「凭据」「快捷链接」及主面板列表产生垂直滚动条，下滚后上移/删除条目、折叠/展开分组，确认无错位或空白漂移。
  3. 破坏 `progress.json` 启动，分别测试「重试读取」与「备份并重置」，验证原坏文件备份在同目录且新轮恢复可写；再将有效 `progress.json` 日期改作昨日启动，确认直接进入今日空轮且不报损坏保护。
  4. 分别核对「托盘隐藏态」与「保存失败待重试」两个条件：最小化到托盘态时若满足本轮全部完成，验证隐藏期间不认领也不弹窗，点开恢复面板可见后立即弹一次完成提示；当主面板可见且抓图/保存流程已结束（`Idle` 且 `!_saveInFlight`）但存在保存失败待重试图（`_pendingImage != null`）时，验证先保留待重试图供点击重试、同时允许弹出带失败说明的一次完成询问对话窗。
- **已知边界**：Mono xbuild 构建提示 `TargetFrameworkVersion 'v4.8'` 回退及重复 `AssemblyAttribute.cs` 警告属既有工具链现象；WinForms UI 实际句柄消息循环、GDI+ 字体绘制与真实高 DPI 显示器缩放只在 Windows 运行时生效，macOS 上不可验证。
