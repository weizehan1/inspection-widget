# 巡检助手设置与配置改造 · 执行日志

## 1. 开工环境

| 项目 | 实际值 |
| --- | --- |
| 日期 | 2026-11-12 |
| 工作区 | `/Users/zach/Documents/Codex/2026-11-12/users-zach-documents-codex-2026-11/inspection-widget` |
| 远端 | `https://github.com/weizehan1/inspection-widget.git` |
| 分支 | `feat/settings-config-ux` |
| 审查基线 | `d8a4a3c8229058324ef2c7c0948b499f0ac53597` |
| 基线版本文案 | `v1.0.3` |
| 操作系统 | macOS 27.2（Darwin 27.2.0，arm64） |
| Git | 2.53.0 |
| .NET / Mono | 系统无 MSBuild、Wine、PowerShell、dotnet；使用工作区父目录 `tools/mono/mono/6.14.1` 的 Mono 6.14.1 与 xbuild 14.0 |
| libgdiplus | 工作区父目录 `tools/bottles/mono-libgdiplus-6.2/6.2/lib` |
| libexif | 工作区父目录 `tools/bottles/libexif-0.6.26/0.6.26/lib` |

仓库从远端完整获取。`main` 仍为审查基线 `d8a4a3c8229058324ef2c7c0948b499f0ac53597`，未发现需要移植的后来提交。工作从该基线新建独立分支 `feat/settings-config-ux`，未推送。

开工备份位于工作区之外：

```text
/Users/zach/Documents/Codex/2026-11-12/inspection-widget-backups/20261112-100317
├── context.txt
├── planned-files.txt
├── planned-files.tar.gz
└── tracked-sha256.txt
```

备份包含全部 Git 跟踪文件的 SHA-256 清单，以及预计修改文件的原始副本；只使用项目源码，没有复制生产配置、实际凭据或历史截图。

复审前工作树备份位于工作区之外：

```text
/Users/zach/Documents/Codex/2026-11-12/inspection-widget-backups/review-fix-20261112-113915
├── status.txt
├── tracked-changes.patch
├── worktree-files.tar.gz
├── file-list.txt
└── files-sha256.txt
```

备份文件数 28，汇总摘要 `877acfbff187d51ba3c5dc3d742c66efdc7a6510de89b8beb35f4b4279d9c2c2`。

## 2. 实施阶段

### 第一阶段：统一草稿、设备管理与保存行为

- 设置窗口改为五页导航：设备管理 / 凭据管理 / 截图与存储 / 配置与备份 / 关于。
- 新增 `SettingsDraft` 作为唯一编辑态；设备表格、凭据行与导入候选都引用同一份草稿。
- 设备页改为单列 `DataGridView`，行序即主面板按钮顺序；支持新增空白草稿行、删除、上移、下移、Enter 提交与 Esc 取消单元格编辑。
- 保存前统一结束当前单元格编辑；非法输入保留并定位到行号。空名单合法，空白设备行在提交时必须补齐或移除。
- 复用 `DeviceNameValidator` 的大小写不敏感查重、保留名、非法字符、尾部空格/点和完整路径校验。
- 设备改名时 `LastDevice` 跟随；删除被引用设备时清空 `LastDevice`。
- 底栏改为“应用 / 保存并关闭 / 取消”。应用保持窗口，保存并关闭提交后关窗，取消和右上角关闭只放弃最近一次成功应用之后的草稿；有未保存更改时提示“放弃更改 / 继续编辑”。
- `Saved` 在成功落盘后立即触发一次；主面板同步刷新运行配置、布局、设备按钮、凭据和剪贴板守卫。失败时 `Saved` 次数为零，磁盘与运行态保持上一次成功配置。
- 应用成功后整份草稿基线、dirty 状态、根目录提示与凭据行状态一起重置。
- 凭据保留原始 `NeedsReset`；清除可在当前草稿撤销；排序时状态随条目移动；成功输入新内容并加密后清除需重设标记。
- 主面板仅在 `AutoClearClipboard` 实际改变时重建剪贴板守卫，避免无关保存丢掉 30 秒计时。

### 第二阶段：可靠配置导入导出

- 新交换格式为 `InspectionWidget.Settings`、版本 1，`CredentialMode` 只接受 `Excluded` 或 `DpapiCurrentUser`。
- 普通导出省略凭据、窗口坐标、`LastDevice` 和 `TrayHintShown`；本机完整备份携带全部持久字段与原始 DPAPI 密文，`NeedsReset` 不进入文件。
- 导出只使用最近一次成功应用的配置；有未保存草稿时提供“应用后导出 / 导出已保存配置 / 取消”，应用失败即停止导出。
- 导入使用无副作用解析器：5 MiB 上限、UTF-8/BOM、重复键、尾随 JSON、未知格式/版本/模式、错误类型与非法枚举均给中文错误；兼容已有裸 `AppConfig` JSON，且至少要求合法 `Devices`。
- 通过实际 JSON 顶层成员判断字段存在性，区分字段缺失、显式 null、false 与空数组；嵌套同名属性不被误认作顶层字段。
- 导入预览展示来源、设备数量与顺序变化、设置差异、路径选择和凭据状态；默认仅合并设备，可选替换参与字段，凭据替换需明确勾选。
- 合并按 `StringComparer.OrdinalIgnoreCase` 去重，保持当前顺序，新设备按导入顺序追加；重复导入同一设备文件保持幂等。
- 已有密文通过 `ICredentialProtector` 在内存中检查可解密性，明文立即失去引用；解不开时保留密文并标为需重设，普通设置仍可导入。
- 导入确认只更新草稿，取消预览或取消设置窗口都不改变最后一次应用的配置。
- `ConfigStore` 增加 SHA-256 代次、期望代次检查、共享写保护、同目录唯一临时文件、刷盘与 `File.Replace` / `File.Move` 原子提交。
- 导入提交在同一锁内先把现有正式配置逐字节备份为 `config.preimport.<时间戳>.<唯一后缀>.json`，备份失败即停止；首次创建记录 `FirstCreation`。
- 读取失败进入写保护，覆盖窗口拖动、折叠、退出、首次目录选择、设置保存与导入提交；主面板与设置入口都提供重试读取，成功后先更新运行配置、基线与草稿代次。
- 本地加载发现 null 设备/凭据元素等结构错误时按损坏契约备份原件；备份失败进入保护态。损坏判定与备份使用同一份已锁定读取的字节。
- 所有设置提交核对草稿依赖的代次；外部写入或保护恢复导致代次变化时拒绝覆盖，提示重新加载或重新预览。

### 第三阶段：界面整理与验证入口

- 沿用浅色中性背景与蓝色主操作色，统一字体、间距、按钮高度、标题和辅助文字；只使用系统字体和既有 WinForms 控件。
- 设置页使用 Dock / TableLayoutPanel / FlowLayoutPanel 布局，内容可滚动，底栏固定；窗口初始尺寸与最小尺寸按当前工作区限制。
- 截图与存储页增加当前日期文件夹名实时预览和完整路径可复制文本。
- 主面板整体高度受当前工作区限制，设备与凭据长列表放入可滚动内容区；标题、分区入口和底部反馈保持可达。
- 主面板按系统 DPI 与 UiScale 手动档位计算布局，并响应 `OnDpiChanged`；设置窗与导入预览沿用同一“应用档位 × 系统 DPI”合并因子，关闭 WinForms 自动 DPI 缩放，避免系统 DPI 与应用档位二次相乘；窗口位置恢复要求与工作区至少半屏相交并钳制回工作区。
- 关键按钮设置 `AccessibleName`；设备表格支持 Enter/Esc，凭据上移下移可键盘操作，长错误可定位到具体控件。
- `build.sh` 在编译前从 `assets/app.ico.base64.txt` 按需还原图标，已有有效 ICO 不覆盖；解码或 ICO 头校验失败即退出。
- README 中文与英文同步五页设置、设备表格、导入导出、原子写、恢复备份、构建与验证边界；版本仍为 `v1.0.3（本地改造）`，未虚构新版本号。
- 测试接入现有 TestRunner；新增纯逻辑回归并在 `tests/Program.cs` 注册。`--windows-ui` 仅在 Windows STA 线程运行真实窗体用例，非 Windows 报告未执行数量并返回退出码 2。

### 第四阶段：复审修正

- 主面板重读配置后比较旧/新 `AutoClearClipboard`，仅开关变化时重建剪贴板守卫；开关不变保留未到期计时，失败读取不替换运行态服务。读取结果为空时回退默认配置，避免空引用。
- `OnDpiChanged` 统一刷新静态布局、设备按钮、凭据控件与状态视觉，保存并恢复滚动位置；DPI 或跨屏尺寸变化后按当前工作区重新钳制面板尺寸与位置，并只更新等待态钩子的自身矩形，不重启会话、不重置 30 秒计时。
- 设备表格去掉脏状态即时 `CommitEdit`，仅在单元格结束编辑且名称实际变化时同步 `LastDevice`；Windows STA 用例改为真实显示窗体、真实编辑控件输入，并覆盖改名后应用、合法与非法输入 Esc 取消。
- 旧裸配置凭据缺失/null 密文兼容为空密文并标记需重设；新交换格式继续严格拒绝缺失/null 密文；显式空字符串保留为空字符串。
- `RootPath` 校验拒绝非法冒号、目录保留名、不完整 UNC、相对路径、尾部空格/点与控制字符，保留合法盘符绝对目录、合法 UNC 与 null；解析、导入计划与设置候选保存均走同一规则。
- 失败读取使用不可提交基线，设置协调器拒绝 null 与失败读取代次；成功重读后的旧草稿失效，新草稿可正常保存。
- 写保护增加保护代次与最终提交锁；保护在提交前进入则拒绝写盘，保护在写盘期间进入会等待本次写入结束。原子写失败与未获准执行分开判断，返回真实写入错误。
- 导入预览新增可滚动、可复制的设备变化列表，显示最终顺序、新增与移除名称；确认弹窗按 owner 所在工作区限制尺寸并居中。
- `.github/workflows/build.yml` 新增面向 `main` 的 `pull_request` 触发，在默认测试后执行 `--windows-ui` 并对退出码严格失败；发版规则保留。

## 3. 本轮实际命令

构建环境前缀：

```bash
PATH="$PWD/../tools/mono/mono/6.14.1/bin:$PATH" \
DYLD_LIBRARY_PATH="$PWD/../tools/bottles/mono-libgdiplus-6.2/6.2/lib:$PWD/../tools/bottles/libexif-0.6.26/0.6.26/lib${DYLD_LIBRARY_PATH:+:$DYLD_LIBRARY_PATH}"
```

完整构建：

```bash
PATH="$PWD/../tools/mono/mono/6.14.1/bin:$PATH" \
DYLD_LIBRARY_PATH="$PWD/../tools/bottles/mono-libgdiplus-6.2/6.2/lib:$PWD/../tools/bottles/libexif-0.6.26/0.6.26/lib${DYLD_LIBRARY_PATH:+:$DYLD_LIBRARY_PATH}" \
bash build.sh
```

结果：先对主工程和测试工程执行 `xbuild /t:Clean`，再完整构建，退出码 `0`；主工程 `34 Warning(s), 0 Error(s)`，测试工程 `2 Warning(s), 0 Error(s)`；默认测试 `342 / 342` 通过。警告来自 xbuild 对 .NET Framework 4.8 的固定提示、源码通配重复项与一个未使用字段。

Windows UI 入口：

```bash
PATH="$PWD/../tools/mono/mono/6.14.1/bin:$PATH" \
DYLD_LIBRARY_PATH="$PWD/../tools/bottles/mono-libgdiplus-6.2/6.2/lib:$PWD/../tools/bottles/libexif-0.6.26/0.6.26/lib${DYLD_LIBRARY_PATH:+:$DYLD_LIBRARY_PATH}" \
mono tests/bin/Release/InspectionWidget.Tests.exe --windows-ui
```

结果：退出码 `2`；纯逻辑 `342 / 342` 通过；输出 `[SKIP] Windows UI：当前不是 Windows，未执行 11 条；这些用例不计为通过。`（11 条业务窗体用例 + 1 条线程异常兜底。）

静态检查与产物：

```text
git diff --check                         通过
git diff --name-only                     13 个修改文件
未跟踪新增（不含临时图标）               18 个文件
build/InspectionWidget.exe               378880 字节
SHA-256                                  02c3d5173194514ac0eb5bef4ac5efa02df0bfc946f33a1b09c16b0dc0d08f9a
格式                                     PE32 executable (GUI) Intel 80386 Mono/.Net assembly
```

构建还生成测试程序 `tests/bin/Release/InspectionWidget.Tests.exe`，SHA-256 为 `3286fbd2bf8336852e39473a75fdcac293e3982cb5c7c1294772d4e68d2d4ecc`。`assets/app.ico` 与 `/tmp/iw-review-build.log`、`/tmp/iw-review-windows-ui.log` 已在收尾删除。

## 4. 交付状态

- 代码位于独立分支 `feat/settings-config-ux`，HEAD 仍为基线 `d8a4a3c8229058324ef2c7c0948b499f0ac53597`，全部改动尚未提交。
- 已交付 Mono/xbuild 构建的 `build/InspectionWidget.exe` 及外置清单；不是 Windows MSBuild Release 产物。
- Windows 编译、真实窗体测试与 `pull_request` CI 结果留给主审在 PR 中取得；本机只验证了代码可编译、默认回归与非 Windows 跳过行为。
- 未推送、未打标签、未创建 Release、未安装系统软件、未调整权限或服务。
- Windows 专属构建、真实窗体截图与运行验收未完成，详见 `work/settings-config-ux/verification.md`。

## 主审独立复验

2026-11-12 主审在修复交回后重新运行相同 Mono 环境前缀下的 bash build.sh，退出码 0，342 条纯逻辑测试通过，失败 0。另两路独立探针复验了旧凭据/路径和写保护/失败草稿/错误分类，均通过。Windows PR CI 待执行。主审重建后 exe SHA-256 为 813224878a8e381fc32470ef5f400695a1836a03ae03b8e83da46bd3ccca074e。
