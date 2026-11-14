# Hydrogen v1.1.0 执行记录

## Step 1：修复与验明 P1（配置、快捷链接与目录导航）

- **问题修复**：在 `tests/InspectionWidget.Tests.csproj` 补充系统程序集引用 `System.Xml`，解决 `tests/QuickLinkTests.cs(150,35)` 的 CS0012（`DataContractJsonSerializer` 基类 `XmlObjectSerializer` 签名依赖 `XmlReader`/`XmlWriter`）。
- **逐项核对 `work/v110/hy4-business-brief.md`**：
  - `QuickLinkEntry`（`Enabled` 构造与 `[OnDeserializing]` 默认 true）、`AppConfig.QuickLinks` / `PlanModeEnabled`、`SettingsDraft` 深拷贝贯通、`QuickLinkValidator`、`QuickLinkCloner`、`QuickLinkLauncher`、`InspectionFolderNavigator`、`ConfigJsonReader`（白名单/严格类型/可空列表/缺字段保留）、`ConfigStore`、`ConfigTransfer`（v1 兼容、MergeDevicesOnly 不覆盖、非 merge 缺字段保留）、`AppConfigCloner` 均符合契约。
  - 额外加固 `InspectionFolderNavigator` 对 Windows 盘符根目录（如 `C:\a.png` → `C:\`）的父目录解析与路径归一。
- **验证命令**：`DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib bash build.sh > /tmp/opencode/inspection-v110/hydrogen-step1-build.log 2>&1`
- **结果**：退出码 `0`；纯逻辑测试 **411/411 通过，失败 0**（含原 342 + P2 33 + P1 36）；Windows UI 未在 macOS 执行。

## Step 2：贯通 HideVisitedDevices 配置与纯显示过滤规则

- **先写行为用例**：新增 `tests/HideVisitedDevicesTests.cs`（13 条用例）并登记至 `tests/Program.cs`，覆盖默认构造/反序列化、`ConfigStore` 往返与老配置自愈、`AppConfigCloner`、`SettingsDraft`、`ConfigJsonReader` 严格布尔校验、`ConfigTransfer` 导出/MergeDevicesOnly/缺字段非 merge 保留，以及 `InspectionDeviceDisplayFilter` 的隐藏/计数保留/全隐藏空态区分/临时查看已点过/恢复未点/重置/跨日/计划模式关闭行为。
- **业务实现**：
  - `src/Config/AppConfig.cs`、`ConfigStore.cs`、`ConfigJsonReader.cs`、`ConfigTransfer.cs`、`SettingsDraft.cs`、`src/SettingsForm.cs`（`AppConfigCloner`）完整贯通 `HideVisitedDevices`（默认 `true`）。
  - 新增 `src/Planning/InspectionDeviceDisplayFilter.cs`，实现纯视图过滤（不修改 `config.Devices`、不减 `TotalCount`、区分 `NoDevicesConfigured` 与 `AllVisitedHidden`）。
- **验证命令**：`DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib bash build.sh > /tmp/opencode/inspection-v110/hydrogen-step2-build.log 2>&1`
- **结果**：退出码 `0`；纯逻辑测试 **424/424 通过，失败 0**。

## Step 3：落地方案 B UI、会话协调器与端到端集成测试

- **主题与布局（`src/Config/UiTheme.cs`、`src/Capture/PanelLayout.cs`）**：
  - 按 `docs/design/v110/DESIGN.md` 方案 B 落盘主色调（`#2463A0` / `#194D80` / `#EAF2FA`）、状态色（`#216A48`、`#B13232`、`#815315`）、文字与边框色阶及 `SubtitleFont` / `PanelTitleFont`。
  - 保留 v1.0.x 布局常量与方法兼容性，新增方案 B 主面板宽 `352px`、默认高 `660px` 及 `ScaledSchemeBPanelWidth` / `ComputeSchemeBHeight`。
- **草稿、校验与五页设置窗口（`src/Config/SettingsDraft.cs`、`src/Config/QuickLinkValidator.cs`、`src/SettingsForm.cs`）**：
  - `QuickLinkValidator` 增补上限常量 `MaxLinks = 12`；`SettingsDraft` 新增 `PendingDeviceRenames` 与 `PendingImportDeviceReplace` 并在 `LoadFrom` / `AcceptSaved` 时清空。
  - `SettingsSaveCoordinator` 保留旧版 9/10 参签名，新增 12 参 v1.1.0 重载以统一校验 `QuickLinks`（组 `2`，含出错行与字段定位 `LinkErrorIndex` / `LinkErrorOnTitle`）、存储与开关（组 `3`）。
  - `SettingsForm` 调整为 `760 × 590` 五页导航（`设备`、`凭据`、`快捷链接`、`存储与显示`、`配置备份` 含 `v1.1.0-rc` 关于卡），保留所有 `SettingsUiTests` 反射字段与方法名。
- **会话协调器与方案 B 主面板（`src/Planning/InspectionSessionController.cs`、`src/MainForm.cs`）**：
  - 新增纯状态会话协调器 `InspectionSessionController`，串联 `PanelStateMachine`、`InspectionPlan`、`InspectionProgressStore`、`InspectionFolderNavigator`、`InspectionDeviceDisplayFilter`、跨日自动重置、设备改名进度迁移、非合并导入重置与「回空闲仅弹一次完成提示」。
  - `MainForm` 按方案 B 重构：顶栏含「巡检助手」与独立中文日期行、2 列快捷链接宫格、今日巡检计划进度卡（开关、「已点过 X / Y」、进度条、「查看已点过/只看未点过」、二次确认「重置本轮」）、可折叠 2 列凭据按钮（含已复制高亮与对勾标记）、可折叠截图设备行（含圆环/对勾、单行「恢复未点」、区分 `NoDevicesConfigured` 与绿色「✓ 本轮已全部点过」空态）、底部「打开巡检文件夹 ↗」主按钮与多实存目录 `ContextMenuStrip` 选择器；接入 `%LocalAppData%\InspectionWidget\progress.json` 与独立 `CaptureRequest.PlanToken`。
- **集成回归（`tests/SchemeBIntegrationTests.cs`、`tests/Program.cs`）**：
  - 新增 9 条集成用例并登记至 `tests/Program.cs`；构建时修复 `DummyProtector.Unprotect` 接口实现与 `ConfigTransfer.TryBuildPlan` 调用签名。
- **验证命令**：`DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib bash build.sh > /tmp/opencode/inspection-v110/hydrogen-step3-build.log 2>&1`
- **结果**：退出码 `0`；纯逻辑及集成测试 **433/433 通过，失败 0**。

## Step 4：RC 版本收口、文档更新与最终回归验证

- **版本与说明**：
  - 更新 `src/Properties/AssemblyInfo.cs` 为 `AssemblyVersion("1.1.0.0")`、`AssemblyFileVersion("1.1.0.0")`、`AssemblyInformationalVersion("1.1.0-rc")`（不发布、不提交、不打标签）。
  - 更新 `README.md`（中英文）同步 `v1.1.0-rc` 方案 B 功能、五页设置说明、目录结构、验证状态与版本记录。
- **最终构建与产物核验**：
  - 执行：`DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib bash build.sh > /tmp/opencode/inspection-v110/hydrogen-step4-build.log 2>&1` → 退出码 `0`，零 C# 编 warning（仅 Mono xbuild v4.8 toolset 提示），**433/433 通过，失败 0**。
  - 执行：`DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib mono tests/bin/Release/InspectionWidget.Tests.exe --windows-ui > /tmp/opencode/inspection-v110/hydrogen-winui-skip.log 2>&1` → 按预期报告 11 条 Windows UI 用例在非 Windows 下未执行并退出码 `2`。
  - 产物 `build/InspectionWidget.exe`：`427,008` 字节，SHA-256 `22f487d7a99e834b0890ce3156793fd904e32d2141d6391d2c79cb8e370317ed`。
  - 产物 `build/InspectionWidget.exe.manifest`：`1,038` 字节，SHA-256 `1b0630484ce745aa3d5d8a9380da7ea92ed97a365a62299d4fe3a46c7b838f70`。
- **交付报告**：生成 `work/v110/hydrogen-implementation-report.md`。

