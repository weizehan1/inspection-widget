# InspectionWidget v1.1.0-rc 独立代码与行为审计报告

- **审计目标提交（HEAD）**：`96165980503e3c764eb4a45811afdbbad54b0553`
- **对比基线（v1.0.4）**：`b9044e1`
- **核查变更范围**：`b9044e1..96165980503e3c764eb4a45811afdbbad54b0553 -- src tests README.md`（30 files changed, 5973 insertions(+), 427 deletions(-)）
- **资产后置说明**：主负责人已于本次代码审计期间根据用户新请求更新 `assets/app-icon.png`、`assets/app.ico.base64.txt` 及 `assets/app.ico`（未改动 `src/` 与 `tests/`）；`AppIcon` 嵌入验证与新产物哈希绑定由主负责人在整体验收阶段统一确认，本审计结论针对 `96165980503e3c764eb4a45811afdbbad54b0553` 源码有效。
- **最终结论**：**需修复**（在进入 Windows UI 真机验收与最终发版前，建议先修复以下 5 项高危与 5 项中危问题）

---

## 一、审计范围与方法

本次审计以独立只读视角完整核查了 v1.1.0 候选版本的规划层（`InspectionSessionController`、`InspectionPlan`、`InspectionProgressStore`、`InspectionDeviceDisplayFilter`）、存储与快捷跳转（`InspectionFolderNavigator`、`ScreenshotSaver`、`QuickLinkLauncher`）、配置与导入导出（`AppConfig`、`ConfigJsonReader`、`ConfigStore`、`ConfigTransfer`、`SettingsDraft`）、WinForms 界面与交互层（`MainForm`、`SettingsForm`、`ConfigTransferForm`、`DeviceEditorControl`、`UiTheme`、`HookManager`、`PanelStateMachine`）及新增/改写测试文件。

行为验证方面，未改动主仓源码或测试产物，仅依据协调者预编译的 `build/InspectionWidget.exe` 在 `/tmp/opencode/inspection-v110/audit/AuditRepro.cs` 编写独立复现程序跑出真实执行日志（`/tmp/opencode/inspection-v110/audit/repro.log`），并核对了主仓既有测试日志（`/tmp/opencode/inspection-v110/codex-candidate-build.log` 结尾：`用例总数：433，通过：433，失败：0`）。

---

## 二、核心发现摘要（按严重程度）

### High（5 项）

1. **`progress.json` 读取或恢复失败被静默吞掉，后续操作直接覆盖损坏文件**（`src/Planning/InspectionSessionController.cs:48-58`）
   - **触发条件与影响**：当 `progress.json` 损坏、截断、超 1 MiB 或字段校验不通过时，`_progressStore.TryLoad` / `_plan.Restore` 返回 `false`，但构造函数丢弃 `error` 与 `restoreError`，既无写保护也无界面错误状态。界面静默展示 `0 / N`，随后任意一次 `TrySelectDevice`、`ApplySavedConfig`、`ReloadConfig` 或 `RecordSaveSucceeded` 都会通过 `AtomicConfigWriter` 直接覆盖原文件，造成当日巡检进度与排查现场不可逆丢失。
   - **实跑复现证据**：`REPRO-1 storeOk=False storeErr_len=163 visited_after_ctor=0 selected=True select_err=<null> progress_overwritten=True after_len=228`。
   - **最小修复建议**：在 `InspectionSessionController` 增加 `ProgressLoadError` 与写入保护标志，加载或同日恢复失败时拒绝静默覆盖原文件，并在主面板提供明确告警与显式重置/备份恢复入口。

2. **普通模式（`PlanModeEnabled=false`）丢弃全部保存与兜底目录，导致「打开文件夹」遗漏暂存截图**（`src/Planning/InspectionSessionController.cs:146-147`，`src/MainForm.cs:2574-2581`）
   - **触发条件与影响**：`PlanModeEnabled` 默认为 `false`。在此模式下，`RecordSaveSucceeded` 在函数开头因 `!PlanModeEnabled` 直接返回 `false`，不调用 `_plan.RecordSavedPath` 也不落盘。当目标目录无权限或不可写而截图自动落入 `FallbackScreenshots` 兜底目录（`UsedFallback=true`）时，`OpenInspectionFolders()` 无法从会话中获取任何实际保存目录：若主目录不存在则提示「今天尚无截图」，若主目录存在则只打开空白或旧目录、漏掉真正的暂存目录。
   - **实跑复现证据**：`REPRO-2 planMode=False recorded=False savedPaths=0 openFolders=0`。
   - **最小修复建议**：将「记录今日真实保存目录/路径」与 `PlanModeEnabled` 的计数显示解耦，无论是否启用计划统计，均应持久化或追踪当日真实落盘目录，使 `ResolveOpenFolders()` 在普通模式下同样准确汇总所有已用目录（含兜底目录）。

3. **多处 `progressErr` 进度保存失败被静默忽略，或被常规成功提示覆盖**（`src/MainForm.cs:1261-1277, 1950-1955, 2024-2039, 2574-2590, 3013-3015`）
   - **触发条件与影响**：
     - `OnSettingsClicked`（2026-2039 行）：调用 `ApplySavedConfig` 后完全未读 `progressErr`，直接 `ShowSuccessFeedback("设置已应用")`；
     - `RetryConfigLoad`（1261-1277 行）：调用 `ReloadConfig` 后直接 `ShowSuccessFeedback("配置已重新读取")`；
     - `OnSaveCompleted`（2576-2590 行）：截图文件写入成功而 `progress.json` 写盘失败时未提示 `progressErr`，直接显示 `✓ xxx.png` 成功提示；
     - `TryMaybeShowCompletionPrompt`（3014 行）：忽略 `TryClaimCompletionPrompt` 的 `progressErr`，若 `CompletionPrompted=true` 未能落盘，重启后会重复弹窗且用户无感知；
     - `OnDeviceRowActivated`（1950-1954 行）：虽拼出 `"进度暂未落盘：" + progressErr`，但使用短时普通反馈，在成功/保存回调后会立即被刷掉。
   - **最小修复建议**：针对每一处 `progressErr != null` 统一走持续错误/告警展示且不被同轮成功文案覆盖；`TryClaimCompletionPrompt` 若持久化失败应在界面显式提示未能保存「不再重复提醒」标记。

4. **最小化隐藏到系统托盘时完成最后一个设备仍直接弹出无宿主 `CompletionPromptDialog`**（`src/MainForm.cs:3009-3028`）
   - **触发条件与影响**：`TryClaimCompletionPrompt` 仅传入 `_hiddenForCapture || Opacity < 0.5`，遗漏了 `!Visible`（托盘隐藏态）。用户在最小化到托盘期间由其它路径/延迟保存完成最后一个设备，或正在待保存/托盘状态变更时，会直接消耗掉 `CompletionPrompted=true` 并调用无 owner 的 `dialog.ShowDialog()`；若选择「打开文件夹」且有多个目录，还会针对隐藏的 `_openFolderButton` 弹出右键菜单。`ShowPanelFromTray` 恢复可见后也未再次检查完成提示。
   - **最小修复建议**：把 `!Visible`（以及 `_pendingImage != null` 失败待重试态是否应阻塞）纳入门控条件，确保仅在主面板正常可见且空闲无未处理失败时弹出，并在 `ShowPanelFromTray` 恢复显示时补调 `TryMaybeShowCompletionPrompt()`。

5. **非合并导入未含 `Devices` 的配置文件仍将 `PendingImportDeviceReplace=true`，误清当日进度**（`src/Config/SettingsDraft.cs:239-258`，`src/Config/ConfigTransfer.cs:162-171`）
   - **触发条件与影响**：`ConfigTransfer.TryBuildPlan` 在 `!MergeDevicesOnly && !document.Has("Devices")` 时保留 `current.Devices`，而 `ConfigImportPlan` 未记录 `DevicesReplaced` 布尔标记。`SettingsDraft.ApplyPlan(plan, false)` 在 `!mergeOnly` 分支无条件设置 `PendingImportDeviceReplace = true` 并清空 `PendingDeviceRenames`。保存设置后，`ApplySavedConfig` 调用 `_plan.ReplaceDevices(...)` 清空当日已巡检进度。
   - **实跑复现证据**：`REPRO-3 parsed=True parseErr=<null> docHasDevices=False built=True planErr=<null> pendingImportDeviceReplace=True visitedBefore=1 visitedAfter=0`。
   - **最小修复建议**：在 `ConfigImportPlan` 增加 `DevicesReplaced` 字段（仅 `!mergeOnly && document.Has("Devices")` 时为 `true`），由 `SettingsDraft.ApplyPlan` 依据 `plan.DevicesReplaced` 决定是否重置进度与清理改名记录。

---

### Medium（5 项）

6. **跨午夜定时器在忙碌期间跳过界面重建，次日周期 `rolled` 信号丢失导致列表与计数脱节**（`src/MainForm.cs:2991-3007`）
   - **触发与实跑证据**：`REPRO-4` 显示跨午夜首 tick 时 `EnsureCurrentDayResetIfRolled` 返回 `rolledTick1=True` 并清零内部计数，次 tick 即返回 `rolledTick2=False`。若首 tick 时恰逢 `Waiting` 或 `_saveInFlight`，`MainForm.OnDayRolloverTick` 跳过 `RebuildPlanCard()` 与 `RebuildDeviceButtons()`，此后不再有重试标志，前一日被隐藏的设备不会重现。
   - **最小修复建议**：在 `MainForm` 维护持久的日期变更/待重建状态（如比对 UI 渲染日期与 `_session.Plan.Date`），或一旦 `rolled` 为真即记录 `_pendingRolloverUiSync`，并在回到 `Idle && !_saveInFlight` 时强制重建。

7. **`Waiting` 态下再次点击同一设备切换回 `Idle` 时漏掉提示清理与完成弹窗检查**（`src/MainForm.cs:1939-1955`）
   - **触发与实跑证据**：`REPRO-5` 显示同一设备二次点击将 `PanelStateMachine` 从 `Waiting` 切回 `Idle` 且保留已点记录；但 `MainForm.OnDeviceRowActivated` 仅在 `State == Waiting` 时刷新反馈条，切回 `Idle` 后残留「请点目标窗口…」引导文案且未调用 `TryMaybeShowCompletionPrompt()`。
   - **最小修复建议**：在 `OnDeviceRowActivated` 发现状态切换为 `Idle` 时同步清理/恢复反馈提示并补调 `TryMaybeShowCompletionPrompt()`。

8. **`Waiting` 态折叠分组或切换开关改变窗口高度后未更新 `HookManager.UpdateSelfBounds`**（`src/MainForm.cs:1825, 1868, 1878, 1895, 1912, 1964`）
   - **触发与影响**：`Waiting` 态下折叠卡片、切换 `PlanModeEnabled` / `ShowVisitedOverride`、重置本轮或取消标注后都会调用 `ApplyHeightAnchored()` 修改 `Bounds`，但未通知 `_hooks.UpdateSelfBounds(Bounds)`，导致鼠标钩子仍以旧窗口边界判断自身区域。
   - **最小修复建议**：将 `_hooks?.UpdateSelfBounds(Bounds)` 统一收口到 `ApplyHeightAnchored()` 内（当 `_stateMachine.State == PanelState.Waiting` 时更新）。

9. **高 DPI 下 `MainForm.OnHandleCreated` 释放缓存 `Font` 后未重建动态子控件**（`src/MainForm.cs:425-433, 226-235`）
   - **触发与影响**：高 DPI Windows 显示器上 `DeviceDpi` 往往在句柄创建时才从 96 变为 144/192；`ApplyScaleToStaticLayout()` 会通过 `GetScaledFont` `Dispose` 掉构造阶段生成的旧字体对象，但 `_quickLinkControls`、`_deviceRows`、`_credentialControls` 等在构造函数创建的动态控件仍持有已释放 `Font` 且尺寸未重算。
   - **最小修复建议**：在 `OnHandleCreated` 检测到缩放倍率变更后，重建动态区控件并重新计算布局高度。

10. **`AutoScroll` 容器滚动状态下直接按逻辑 `y` 设置子控件 `Location` 导致错位**（`src/MainForm.cs:984-1040`，`src/SettingsForm.cs:1544, 1672`）
    - **触发与影响**：WinForms 的 `AutoScroll=true` 容器在滚动条下移后，`Control.Location` 相对于当前滚动视口，直接赋值 `new Point(0, y)` 会叠加滚动偏移导致子控件整体下移甚至被挤出可视范围。
    - **最小修复建议**：`MainForm.LayoutSections`、`SettingsForm.LayoutCredentialRows` 与 `SettingsForm.LayoutLinkRows` 计算位置时统一加上容器的 `AutoScrollPosition.Y`。

---

## 三、测试覆盖度与 Windows 验证缺口

1. **现有测试现状**：`build.sh` 下 433/433 无头测试在 macOS Mono 通过；但观察测试文件实现可见，标称「端到端集成」的 `SchemeBIntegrationTests` 主要在构造 `InspectionSessionController` 与布局计算函数，`MainForm`、`SettingsForm`、`HookManager` 联动与托盘/弹窗/滚动反馈覆盖未接通真实窗体事件循环，因此漏掉了上述 4 项 `MainForm` 集成缺陷。
2. **Windows 验证缺口（未验证）**：本机为 macOS，`--windows-ui` 自测套件在实现报告中以退出码 2 跳过；以下行为需在修复后于真实 Windows 桌面环境补测：
   - 100% / 125% / 150% / 200% DPI 与长设备名/多设备场景下的 Scheme B 主面板及 5 页 `SettingsForm` 真实 GDI+ 绘制、文本换行截断与零字体异常；
   - `MainForm` 系统托盘最小化/还原周期内的 `CompletionPromptDialog` 与多目录 `ContextMenuStrip` 锚点位置；
   - 全局鼠键钩子在 `Waiting` 态窗体高度动态变化时的自身区域排除；
   - 双模式（`PlanModeEnabled=true/false`）下主目录与 `FallbackScreenshots` 的 `explorer.exe` 真实拉起。
