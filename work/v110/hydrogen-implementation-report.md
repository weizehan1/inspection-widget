# InspectionWidget v1.1.0-rc 实施总结报告

> 注：本报告为实施方按交付要求出具的**诚实自查（Self-Check）**，并非独立第三方审计。所有编码、构建与纯逻辑/集成测试均在 macOS Mono 工具链下完成；Windows 实机 UI、Win32 交互及发布动作均未在本环境执行。

---

## 1. 交付范围与实现摘要

本次在分支 `feat/v1.1.0-inspection-redesign` 上完成了 `InspectionWidget` `v1.1.0-rc`（方案 B 改版）的剩余工程落地：

1. **P1 基线修复与核验**：
   - 在 [InspectionWidget.Tests.csproj](tests/InspectionWidget.Tests.csproj) 补充 `System.Xml` 引用以消除 `DataContractJsonSerializer` 依赖编译错误；
   - 加固 [InspectionFolderNavigator.cs](src/Storage/InspectionFolderNavigator.cs) 对 Windows 盘符根目录（如 `C:\a.png` → `C:\`）的父目录解析；
   - 核验 `QuickLinkEntry`、`QuickLinkValidator`、`QuickLinkCloner`、`QuickLinkLauncher`、`ConfigJsonReader`、`ConfigStore`、`ConfigTransfer` 与 `SettingsDraft` 已贯通且符合规格。
2. **新增配置字段与纯显示过滤**：
   - 在 [AppConfig.cs](src/Config/AppConfig.cs)、[ConfigStore.cs](src/Config/ConfigStore.cs)、[ConfigJsonReader.cs](src/Config/ConfigJsonReader.cs)、[ConfigTransfer.cs](src/Config/ConfigTransfer.cs)、[SettingsDraft.cs](src/Config/SettingsDraft.cs) 与 [SettingsForm.cs](src/SettingsForm.cs) 中完整贯通 `HideVisitedDevices`（默认 `true`，缺失自愈为 `true`，兼容 v1 配置导入及 `MergeDevicesOnly` 保留策略）。
   - 新增纯视图过滤模块 [InspectionDeviceDisplayFilter.cs](src/Planning/InspectionDeviceDisplayFilter.cs) 与 13 条行为回归用例 [HideVisitedDevicesTests.cs](tests/HideVisitedDevicesTests.cs)：点击即隐藏不改 `config.Devices`、不减少 `TotalCount`，且明确区分 `NoDevicesConfigured` 与 `AllVisitedHidden`。
3. **方案 B 会话状态、UI 与主题落地**：
   - **会话协调**：新增 [InspectionSessionController.cs](src/Planning/InspectionSessionController.cs)，串联 `PanelStateMachine`、`InspectionPlan`、`InspectionProgressStore`、`InspectionFolderNavigator`、`InspectionDeviceDisplayFilter`、设备改名进度迁移、非合并导入替换清空进度、跨日自动重置与「回到空闲仅弹一次完成提示」。
   - **主面板重构**：[MainForm.cs](src/MainForm.cs) 按 `DESIGN.md` 方案 B 重建为 352px 浮动面板：顶栏（「巡检助手」+ 独立中文日期行）、2 列快捷链接宫格、今日巡检计划进度卡（实时开关、「已点过 X / Y」、进度条、「查看已点过/只看未点过」、二次确认「重置本轮」）、可折叠 2 列凭据按钮（高亮与对勾标识）、可折叠设备清单（状态圆环/对勾、单行「恢复未点」、区分空配置与绿色「✓ 本轮已全部点过」空态）、底部「打开巡检文件夹 ↗」主按钮（支持实存暂存多目录选择菜单）；接入 `%LocalAppData%\InspectionWidget\progress.json` 持久化与 `CaptureRequest.PlanToken`。
   - **五页设置窗与校验协调**：[SettingsForm.cs](src/SettingsForm.cs) 重组为 5 页导航（`设备` / `凭据` / `快捷链接` / `存储与显示` / `配置备份` 含 `v1.1.0-rc` 关于卡片），保留 `SettingsUiTests` 依赖的所有内部字段与方法名；新增 12 参 `SettingsSaveCoordinator` 重载（链接出错定位于组 `2` 具体行与字段，存储定位于组 `3`），同时保留旧版 9/10 参签名以兼容既有用例；[QuickLinkValidator.cs](src/Config/QuickLinkValidator.cs) 补充 `MaxLinks = 12`。
   - **视觉与布局**：[UiTheme.cs](src/Config/UiTheme.cs) 落地冷蓝方案 B 色板与标题字体，[PanelLayout.cs](src/Capture/PanelLayout.cs) 保留 v1.0.x 布局常量并新增 `SchemeBPanelWidth = 352`、`ComputeSchemeBHeight` 等方法。
4. **端到端集成测试、版本号与文档**：
   - 新增 9 条方案 B 集成测试 [SchemeBIntegrationTests.cs](tests/SchemeBIntegrationTests.cs) 并接入 [Program.cs](tests/Program.cs)。
   - 更新 [AssemblyInfo.cs](src/Properties/AssemblyInfo.cs) 为 `1.1.0.0` / `v1.1.0-rc`，更新中英文 [README.md](README.md)，未执行任何提交、打标签或发版操作。

---

## 2. 变更文件清单

- **修改文件**：
  - `src/Config/AppConfig.cs`
  - `src/Config/ConfigJsonReader.cs`
  - `src/Config/ConfigStore.cs`
  - `src/Config/ConfigTransfer.cs`
  - `src/Config/QuickLinkValidator.cs`
  - `src/Config/SettingsDraft.cs`
  - `src/Config/UiTheme.cs`
  - `src/Capture/PanelLayout.cs`
  - `src/MainForm.cs`
  - `src/SettingsForm.cs`
  - `src/Properties/AssemblyInfo.cs`
  - `tests/InspectionWidget.Tests.csproj`
  - `tests/Program.cs`
  - `README.md`
- **新增业务与测试文件**：
  - `src/Planning/InspectionDeviceDisplayFilter.cs`
  - `src/Planning/InspectionSessionController.cs`
  - `src/Storage/InspectionFolderNavigator.cs`
  - `src/Config/QuickLinkCloner.cs`（P1 本轮分支已有，经核验）
  - `src/Links/QuickLinkLauncher.cs`（P1 本轮分支已有，经核验）
  - `tests/HideVisitedDevicesTests.cs`
  - `tests/SchemeBIntegrationTests.cs`
  - `work/v110/hydrogen-execution.md`
  - `work/v110/hydrogen-implementation-report.md`

---

## 3. 构建与测试证据

| 检查项 | 命令 | 结果 | 日志路径 |
| --- | --- | --- | --- |
| Step 1 基线修复回归 | `DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib bash build.sh` | 退出码 `0`，**411/411 通过，失败 0** | `/tmp/opencode/inspection-v110/hydrogen-step1-build.log` |
| Step 2 隐藏过滤 TDD 回归 | `DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib bash build.sh` | 退出码 `0`，**424/424 通过，失败 0** | `/tmp/opencode/inspection-v110/hydrogen-step2-build.log` |
| Step 3 方案 B 集成回归 | `DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib bash build.sh` | 退出码 `0`，**433/433 通过，失败 0** | `/tmp/opencode/inspection-v110/hydrogen-step3-build.log` |
| Step 4 最终纯净构建与回归 | `DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib bash build.sh` | 退出码 `0`，零 C# 警告，**433/433 通过，失败 0** | `/tmp/opencode/inspection-v110/hydrogen-step4-build.log` |
| 非 Windows UI 用例门禁检查 | `DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib mono tests/bin/Release/InspectionWidget.Tests.exe --windows-ui` | 退出码 `2`，正确报告 11 条 UI 用例未执行 | `/tmp/opencode/inspection-v110/hydrogen-winui-skip.log` |

- **最终构建产物校验（macOS Mono Release）**：
  - `build/InspectionWidget.exe`：`427,008` 字节，SHA-256 `22f487d7a99e834b0890ce3156793fd904e32d2141d6391d2c79cb8e370317ed`
  - `build/InspectionWidget.exe.manifest`：`1,038` 字节，SHA-256 `1b0630484ce745aa3d5d8a9380da7ea92ed97a365a62299d4fe3a46c7b838f70`

---

## 4. 诚实自查（Self-Check）

### 4.1 在 macOS Mono 下已充分验证的逻辑与行为
- 配置层：`AppConfig` / `ConfigStore` / `ConfigJsonReader` / `ConfigTransfer` / `SettingsDraft` 对 `QuickLinks`、`PlanModeEnabled`、`HideVisitedDevices` 的默认自愈、严格 JSON 类型校验、原子写、导出与 v1 兼容合并导入逻辑。
- 设置校验：`SettingsSaveCoordinator` v1.1.0 12 参重载及旧版 9/10 参兼容性，快捷链接错行与字段定位，双开关保存落盘。
- 规划与隐藏过滤：`InspectionDeviceDisplayFilter` 与 `InspectionSessionController` 的点击隐藏、`TotalCount` 守恒、取消/失败留痕、查看已点过与补截不增量、单行恢复未点、二次确认重置状态机、改名迁移、导入替换重置、跨日自动清零、忙碌/弹窗期间抑制与回到空闲单次完成提示。
- 目录导航与链接启动：`InspectionFolderNavigator` 的暂存多目录优先级排序、中文空格/盘符根目录处理、禁用静默建目录及注入 fake 的异常收敛。

### 4.2 尚未在 Windows 实机验证的缺口（需在 Windows 验收阶段补齐）
以下能力依赖 Windows 原生窗体消息循环、GDI+ 字体渲染与 Win32 API，在 macOS Mono 环境下**无法实测，不宣称已通过**：
1. **`SettingsUiTests` 11 条 WinForms 控件测试**：本次因非 Windows 环境未运行（`--windows-ui` 返回退出码 `2`），需在 Windows runner（如 GitHub Actions 或 Windows 10/11 本机）运行确认 5 页导航、`DataGridView` 100%/200% DPI 列宽、快捷链接编辑器与单选框控件行为。
2. **主面板与弹窗实际视觉渲染及 DPI 缩放**：方案 B 主面板（352px）、折叠交互、`FlowLayoutPanel` / `AutoScroll` 在多分辨率、100%–200% Windows 系统缩放及任务栏/多显示器工作区下的实际排版与 GDI+ 图标绘制效果。
3. **Win32 全局鼠标/键盘选窗钩子与截图引擎**：`PrintWindow` 被遮挡窗口抓取、屏幕拷贝自动隐藏回退、右键/Esc 取消、30 秒选窗超时，以及管理员权限窗口截取。
4. **系统外壳与持久化运行态**：真实 `Process.Start` 打开默认浏览器与 Windows 资源管理器多目录菜单、真实剪贴板序列号校验与 30 秒倒计时清空、托盘最小化与首次气泡提示、真实 DPAPI 加密/解密与跨日跨午夜定时器在挂机/锁屏场景下的表现。
5. **Windows 正式单文件打包**：`build.sh` 在 Mono 下会残留外置 `.manifest`；正式发布需由 Windows MSBuild / VS2022 或 GitHub Actions（`.github/workflows/build.yml`）内嵌清单生成最终单文件 exe。
