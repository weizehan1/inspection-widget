# 巡检助手设置与配置改造 · 验收记录

## 1. 结论

代码改造与复审修正已完成，独立分支上的 Mono 构建与默认纯逻辑回归通过。交付物为本地 `build/InspectionWidget.exe`，但它是 macOS 上 Mono/xbuild 构建的 PE32 .NET 程序，不是 Windows MSBuild Release 产物。

Windows 真实 WinForms 启动、设置页截图、DPAPI、剪贴板、托盘、截图引擎、系统 DPI 与工作区组合均未在本机执行，不能宣称通过。`--windows-ui` 的 11 条窗体用例在非 Windows 环境被明确跳过，退出码为 `2`。

## 2. 代码与分支

| 项目 | 实际值 |
| --- | --- |
| 仓库 | `https://github.com/weizehan1/inspection-widget.git` |
| 分支 | `feat/settings-config-ux` |
| HEAD | `d8a4a3c8229058324ef2c7c0948b499f0ac53597` |
| 提交状态 | 未提交，全部改动在工作树 |
| 基线 | `main` 的 `d8a4a3c8229058324ef2c7c0948b499f0ac53597` |
| 复审前备份 | `/Users/zach/Documents/Codex/2026-11-12/inspection-widget-backups/review-fix-20261112-113915` |
| 复审备份摘要 | `877acfbff187d51ba3c5dc3d742c66efdc7a6510de89b8beb35f4b4279d9c2c2` |

修改文件：

```text
README.md
build.sh
.github/workflows/build.yml
src/Capture/HookManager.cs
src/Capture/PanelLayout.cs
src/Config/ConfigStore.cs
src/MainForm.cs
src/SettingsForm.cs
tests/ConfigStoreTests.cs
tests/InspectionWidget.Tests.csproj
tests/PanelLayoutTests.cs
tests/Program.cs
tests/V103Tests.cs
```

新增文件：

```text
src/Config/AtomicConfigWriter.cs
src/Config/ConfigJsonReader.cs
src/Config/ConfigTransfer.cs
src/Config/ConfigValidator.cs
src/Config/ConfigWriteProtection.cs
src/Config/SettingsApplyCoordinator.cs
src/Config/SettingsDraft.cs
src/Config/UiTheme.cs
src/ConfigTransferForm.cs
src/DeviceEditorControl.cs
tests/AtomicConfigWriterTests.cs
tests/ConfigTransferTests.cs
tests/ConfigWriteProtectionTests.cs
tests/SettingsApplyCoordinatorTests.cs
tests/SettingsDraftTests.cs
tests/SettingsUiTests.cs
work/settings-config-ux/execution-log.md
work/settings-config-ux/verification.md
```

以上均在 goal 允许范围内；本轮额外获准修改 `.github/workflows/build.yml`。`assets/app.ico` 是构建时从 `assets/app.ico.base64.txt` 还原的未跟踪临时文件，已清理；base64 源文件保留。

`.github/workflows/build.yml` 新增面向 `main` 的 `pull_request` 触发，并在默认测试后执行 `--windows-ui`，退出码非零即失败；`push main`、`v*` 标签发版与手动触发规则保留。

## 3. 构建与测试证据

### Mono 构建

命令：

```bash
PATH="$PWD/../tools/mono/mono/6.14.1/bin:$PATH" \
DYLD_LIBRARY_PATH="$PWD/../tools/bottles/mono-libgdiplus-6.2/6.2/lib:$PWD/../tools/bottles/libexif-0.6.26/0.6.26/lib${DYLD_LIBRARY_PATH:+:$DYLD_LIBRARY_PATH}" \
bash build.sh
```

| 项目 | 结果 |
| --- | --- |
| 退出码 | `0` |
| 主工程 | `0 Error`，34 个警告 |
| 测试工程 | `0 Error`，2 个警告 |
| 默认测试 | 总数 `342`，通过 `342`，失败 `0` |
| 图标还原 | 构建前从 `assets/app.ico.base64.txt` 还原；ICO 头为 `00 00 01 00` |

警告来源：xbuild 14.0 不支持 `v4.8` 工具集的固定提示、源码通配导致 `CS2002` 重复文件提示，以及 `MainForm._settingsUsesGearImage` 未使用警告。没有编译错误。以上为 `xbuild /t:Clean` 后重新构建的结果，不是增量编译。

测试进程同样依赖本机的 `libgdiplus` 与 `libexif` 运行库路径；仅设置 `PATH` 而不设置 `DYLD_LIBRARY_PATH` 时，测试会在退出阶段因 `System.Drawing.Image` 初始化失败而退出码 `255`。上述构建命令同时设置两者，实际运行结果为 `342 / 342`。

### 默认纯逻辑测试覆盖

实际通过项包括：

- 设备名校验、路径长度、排序边界、LastDevice 跟随与删除清空。
- 设置草稿的整份编辑态、空行、导入合并/替换、基线重置、凭据需重设状态。
- 配置往返、缺字段默认、损坏备份、结构错误、锁占用、写保护、导入恢复备份失败、备份期间目标改写。
- 新交换格式与旧裸 JSON 解析：未知字段/版本/模式、重复键、尾随 JSON、错误类型、非法枚举、null 凭据、超限、大小写重复设备。
- 导入预览不修改当前配置、仅合并幂等、替换参与字段、RootPath 默认保留、本机字段显式恢复、密文可解/不可解状态。
- 导出阻止覆盖权威配置、锁文件与恢复备份；普通导出原子写入且无临时残留。
- 原子写的移动/替换、替换失败保留旧目标、回调异常清理临时文件。
- 提交编排的代次检查、null/失败读取基线拒绝、导入前逐字节备份、首次创建、共享写保护。
- 写保护与提交竞争：保护在最终提交前进入时拒绝写盘；获准写盘但原子替换失败时返回真实写入错误，不误报保护。
- 重读配置后剪贴板守卫重建决策：开关双向变化重建，开关不变与读取失败保留原服务。
- 导入预览设备变化文本：最终顺序、新增、移除、顺序未变化。
- 150% 面板在 1366×768 与 1920×1080 工作区内的尺寸和位置钳制。
- 旧裸配置缺失/null 密文兼容、新格式严格拒绝、旧凭据列表 null/缺失保留当前凭据。
- 选窗钩子矩形更新：未运行时不新建会话；已释放后调用抛 `ObjectDisposedException`。等待态主面板在 DPI/跨屏钳制后调用该入口，只更新现行会话矩形，不重启会话或重置 30 秒计时。

### `--windows-ui`

命令：

```bash
PATH="$PWD/../tools/mono/mono/6.14.1/bin:$PATH" \
DYLD_LIBRARY_PATH="$PWD/../tools/bottles/mono-libgdiplus-6.2/6.2/lib:$PWD/../tools/bottles/libexif-0.6.26/0.6.26/lib${DYLD_LIBRARY_PATH:+:$DYLD_LIBRARY_PATH}" \
mono tests/bin/Release/InspectionWidget.Tests.exe --windows-ui
```

| 项目 | 结果 |
| --- | --- |
| 退出码 | `2` |
| 纯逻辑 | 总数 `342`，通过 `342`，失败 `0` |
| Windows 窗体用例 | `[SKIP]` 11 条业务用例未执行，线程异常兜底不计为常规用例 |
| 是否算 Windows UI 通过 | 否 |

## 4. 交付 exe

| 项目 | 实际值 |
| --- | --- |
| 路径 | `build/InspectionWidget.exe` |
| 大小 | `378880` 字节 |
| SHA-256 | `813224878a8e381fc32470ef5f400695a1836a03ae03b8e83da46bd3ccca074e` |
| 格式 | PE32 executable (GUI) Intel 80386 Mono/.Net assembly |
| 清单 | `build/InspectionWidget.exe.manifest`（外置） |
| 构建工具 | Mono 6.14.1 / xbuild 14.0 / C# 7.2 |
| Windows Release 状态 | 未构建、未验证 |

此 exe 可用于本机静态检查和后续 Windows 复验的对照，但不能替代 VS2022 / MSBuild 在 .NET Framework 4.8 下的正式 Release 构建。

## 5. 已完成的静态检查

- `git diff --check`：通过，无空白错误。
- `git diff --name-only`：13 个修改文件，均在允许范围。
- 未跟踪新增文件：18 个文件（16 个产品/测试文件与 2 个验证文档），均在允许范围；`assets/app.ico` 是构建临时产物，已在收尾清理，base64 源文件保留。
- `build/`、`src/bin/`、`src/obj/`、`tests/bin/`、`tests/obj/` 均被 Git 忽略，没有编译产物进入跟踪内容。
- 代码、日志与测试夹具未包含生产配置、实际凭据、历史截图或明文秘密。

## 6. 未验证项

以下项目因当前环境不是 Windows、缺少 MSBuild/.NET Framework 4.8 与真实桌面会话，未执行：

- VS2022 / MSBuild 的 Windows Release 构建。
- 真实 WinForms 启动与设置五页截图。
- 设备新增、改名、删除、排序、Enter/Esc 与保存链的真实窗体交互；相关 STA 用例已加入 `--windows-ui`，但本机未执行。
- 配置导入导出完整往返、预览取消、导入后取消与应用即时刷新。
- DPAPI 在异机/异用户环境下的凭据可解密性与“需重设”提示。
- 真实剪贴板 30 秒自动清除。
- 托盘隐藏/恢复、截图引擎、管理员窗口抓取。
- 100% / 150% / 200% 应用缩放。
- Windows 100% / 150% 系统 DPI。
- 1366×768 与 1920×1080 工作区。
- `--windows-ui` 的 11 条真实窗体用例。
- Windows 上的真实设置窗体显示、子页 `Visible`、编辑控件输入与 Esc 取消。
- `pull_request` 工作流在 GitHub Windows runner 上的编译与 `--windows-ui` 结果。
- 注入保存/备份失败后的真实 UI 草稿保留与运行态不替换；纯逻辑层已覆盖对应磁盘行为。
- 真实 Windows 上合法完整备份经导入入口恢复普通设置与凭据。

## 7. Windows 最短复现步骤

在 Windows 10/11、已安装 VS2022「.NET 桌面开发」与 .NET Framework 4.8 目标包的环境：

```powershell
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuildPath = & $vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
if (-not $msbuildPath) { throw "MSBuild unavailable" }
[IO.File]::WriteAllBytes("assets/app.ico", [Convert]::FromBase64String((Get-Content "assets/app.ico.base64.txt" -Raw)))
& $msbuildPath InspectionWidget.sln /p:Configuration=Release /m
if ($LASTEXITCODE -ne 0) { throw "Application build failed" }
& $msbuildPath tests\InspectionWidget.Tests.csproj /p:Configuration=Release /p:Platform=AnyCPU
if ($LASTEXITCODE -ne 0) { throw "Test build failed" }
& .\tests\bin\Release\InspectionWidget.Tests.exe
if ($LASTEXITCODE -ne 0) { throw "Tests failed" }
& .\tests\bin\Release\InspectionWidget.Tests.exe --windows-ui
if ($LASTEXITCODE -ne 0) { throw "Windows UI tests failed" }
Get-FileHash .\src\bin\Release\InspectionWidget.exe -Algorithm SHA256
```

随后用合成设备与临时目录启动 `src\bin\Release\InspectionWidget.exe`，逐项验证：

1. 设备改名后直接保存、编辑时切页/排序、新增后保存、重复名、空名、删除首末行、取消、应用后继续修改再取消、`LastDevice` 跟随。
2. 普通导出与完整备份往返、预览取消、导入后取消、仅合并幂等、替换凭据与空数组清空、异机密文需重设。
3. 应用后主面板即时刷新；保存失败时磁盘字节不变、草稿保留、运行态不替换。
4. DPAPI 在另一 Windows 用户或机器上的状态提示。
5. 剪贴板复制与 30 秒清除、托盘隐藏恢复、截图一次与重名顺延、分区折叠、退出。
6. 100% / 150% / 200% 应用缩放，Windows 100% / 150% 系统 DPI，1366×768 与 1920×1080 工作区；保存真实窗体截图作为证据。
