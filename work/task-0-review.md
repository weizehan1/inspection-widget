# Task 0 审查报告：工程骨架、清单、本地 Git 与构建链

审查对象：简报 `work/task-0-brief.md`、实现报告 `work/task-0-report.md`、审查包 `work/task-0-review-package.md`（提交区间 3cb364c..7457764）。
审查方式：逐条核对简报与全局约束；只读命令独立复核（原地重跑 `bash build.sh`、用 `git archive HEAD` 解出到 /tmp 做干净构建、`git log/show/status`、`git remote -v`、`stat/file/strings`）。除本审查文件外未修改任何文件，未提交。

## 结论速览

- **① 规格符合性：✅ 符合**（逐条核对无缺口，见第一节）
- **② 代码质量：通过**（Critical 0 条、Important 1 条、Minor 3 条，见第三节）

## 一、规格符合性核对（✅，无缺口）

| 简报条目 | 核对结果 |
| ---- | ---- |
| 1. `app.manifest`：`requireAdministrator`（uiAccess="false"） | ✅ diff 与磁盘文件一致，`level="requireAdministrator" uiAccess="false"` |
| 1. DPI：`dpiAwareness`=`PerMonitorV2,PerMonitor`、`dpiAware`=`True/PM` | ✅ 两项均在，值准确 |
| 1. supportedOS 含 Win10/11 GUID `{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}` | ✅ 在位 |
| 2. csproj：WinExe、RootNamespace/AssemblyName=InspectionWidget | ✅ |
| 2. TargetFrameworkVersion=v4.8、LangVersion=7.3、AnyCPU、Prefer32Bit=false | ✅ v4.8 在位；LangVersion 7.3 在全局及 Debug/Release 三处均写明；Prefer32Bit=false 在 Debug/Release 两组均有 |
| 2. ApplicationManifest 指向 `..\app.manifest` | ✅ |
| 2. 引用仅限 System、System.Core、System.Drawing、System.Windows.Forms、System.Security、System.Runtime.Serialization，零第三方包 | ✅ 恰为该六项，无 PackageReference、无其他引用 |
| 2. Compile 采用简报允许的通配方式 | ✅ `<Compile Include="**\*.cs" />`，属简报明示可选方案 |
| 3. `InspectionWidget.sln`：VS Version 17 格式、引用 src 工程、Debug/Release Any CPU | ✅ 工程 GUID 与 csproj 的 ProjectGuid 一致，配置映射完整 |
| 4. `src/Program.cs`：`[STAThread]`、单实例 Mutex 名 `InspectionWidget.SingleInstance`、已持有直接 return | ✅ 三项均符合；Mutex 以 `using` 持有至 `Application.Run` 结束，单实例语义正确 |
| 4. DPI 兜底：P/Invoke `SetProcessDpiAwarenessContext(new IntPtr(-4))`（user32）、失败忽略、本任务内联声明 | ✅ 在位，try/catch 吞失败，调用点在建窗之前 |
| 4. EnableVisualStyles / SetCompatibleTextRenderingDefault(false) / Run(new MainForm()) | ✅ 三行齐全 |
| 5. `src/MainForm.cs`：标题「巡检截图」、TopMost=true、230×200、定位主屏 WorkingArea 右下角 | ✅ 四项均符合 |
| 6. `build.sh`：msbuild 优先、否则 xbuild，Release 构建 csproj；exe 与 `InspectionWidget.exe.manifest` 放进 `build/`；tests 存在才跑；失败非零退出 | ✅ 脚本含 `set -euo pipefail`、工具选择、产物复制、tests/*.csproj 存在性检测；实测行为与描述一致（见第二节） |
| 7. `README.md`：标题、简介、编译方法（VS2022 与 Mono 两路）、运行说明（管理员确认、首次选根目录）、版本记录空表 | ✅ 全部齐备 |
| 提交：信息 `feat(stage-0): 工程骨架、manifest、Mono 构建链`、不得加 remote、不得 push | ✅ 提交 7457764 信息准确且恰含简报的 7 个文件（262 行新增，与审查包 diffstat 一致）；`git remote -v` 输出为空，git 配置中无任何 remote 条目 |
| 全局约束：零第三方包 | ✅ 已核，无 NuGet/第三方引用 |

说明：实现报告自述未按简报字面用 `git add -A`、改为逐项添加 7 个文件，理由是避开协调者新放入的无关未跟踪文件。经 `git show 7457764 --stat` 核对，提交内容与简报要求的文件集合完全一致，该偏离不构成规格缺口，且处理更稳妥。

## 二、独立复核结果（构建、字节数、警告、Git）

- **构建可复现**：在原仓库重跑 `bash build.sh`，退出码 0；另用 `git archive HEAD` 解出到 /tmp 临时目录做无 bin/obj 的干净构建，退出码同样为 0，产物 `build/InspectionWidget.exe` 与 `build/InspectionWidget.exe.manifest` 均生成。build.sh「真能构建」成立。
- **exe 字节数**：三次口径一致——磁盘现有 `build/InspectionWidget.exe` 为 **4608 字节**，原地重建后仍为 4608 字节，/tmp 干净构建仍为 4608 字节；`file` 识别为 PE32 (GUI) Mono/.Net assembly。manifest 副本 1038 字节，与 `app.manifest` 等大。
- **⚠️ 关于「14336」的特别说明**：审查委托中提到「报告里声称的 exe 字节数为 14336」，但实际读到的实现报告在三处（构建摘要、ls 结果、结论行）写的均为 **4608 字节**，全仓 grep 也搜不到 14336。独立复核证明报告的 4608 正确，「14336」无出处，应为委托转述有误，不是实现方的问题。
- **构建警告**：独立构建（原地增量与干净全量各一次）均为 **1 条警告、0 错误**，警告原文与报告一致：`TargetFrameworkVersion 'v4.8' not supported by this toolset (ToolsVersion: 14.0)`；干净构建的 mcs 命令行确认实际引用的是 `/usr/lib/mono/4.8-api` 下的程序集、`/langversion:7.2`（见 Important-1）、`/target:winexe`。报告对警告的描述属实。
- **tests 分支**：当前无 tests/*.csproj，脚本实测输出「未发现 tests 工程，跳过测试」，符合「存在才跑」。
- **Git**：`git log` 为 76fc5bd（Task 0 报告 docs 提交）→ 7457764（功能提交）→ 3cb364c，与审查包区间一致；工作区另有 PROGRESS.md 未提交修改及 build/、work/ 下未跟踪文件，均不属于本任务提交，未计入缺口。

## 三、代码质量（通过）

### Critical（0 条）

无。

### Important（1 条）

1. **双构建链语言版本不一致（已披露的已知约束）**：csproj 按规格保留 LangVersion 7.3，但 `build.sh` 的 xbuild 分支用 `/p:LangVersion=7.2` 覆盖（Mono 6.8 的 mcs 上限 7.2，干净构建命令行已实证）。后果：Mono 链实际按 C# 7.2 编译，后续任务若用了 7.3 独有语法，会出现「VS2022 能编、Mono 构建失败」的双链分叉。实现方已在 build.sh 注释与报告中明确披露并给出「守 7.2 语法子集」的建议，故评为非阻塞的 Important：后续每个任务的简报都应带上这条约束，直到构建链升级。

### Minor（3 条）

1. **build/ 未被 .gitignore 覆盖完全**：.gitignore 有 `*.exe` 但无 `*.exe.manifest`，`build/` 目录本身也未忽略；后续若有人执行 `git add -A`，manifest 副本会被误提交。实现报告已指出并留待协调者处置，建议补一行 `build/`。
2. **build.sh 的 tests 检测较粗**：只取第一个 `tests/*.csproj` 构建，测试 exe 只在 `tests/bin/Release/*.exe` 中查找并用 mono 运行。Task 0 阶段 tests 不存在、不影响本任务；将来 tests 工程若多于一个或输出路径不同，可能漏跑，建议届时再收紧。
3. **Program.cs 的 Mutex 未处理 AbandonedMutexException**：首个实例异常退出后，下一个实例在 `new Mutex(true, ..., out createdNew)` 处可能抛 AbandonedMutexException 导致启动即崩。属占位阶段的健壮性提示，建议在后续任务给 Main 加一层兜底时顺手处理。

其余细节无问题：Mutex 生命周期、DPI 调用时机（在 EnableVisualStyles/Run 之前）、MainForm 用 Width/Height 计算右下角定位等均正确。

## 四、无法从 diff 验证的事项（不猜测，单列）

1. **Windows 实机行为**：UAC 提权弹窗、PerMonitorV2 在 Win10/11 多显示器不同 DPI 下的实际效果、单实例在 Windows 上的实际拦截、MainForm 在真实 Windows 工作区的右下角定位——本机为 Linux/Mono，均无法验证；实现报告第 4 条已声明留待 Task 9 的 Windows 实跑验收。
2. **VS2022 构建链**：本机无 VS2022/msbuild，只验证了 Mono xbuild 链；sln/csproj 在 VS2022 中打开与构建、通配 Compile 在 VS 增量构建下的表现，未实测。
3. **清单嵌入**：对 Mono 构建出的 exe 做 strings 检查，未见 manifest 内容（未嵌入），当前靠同目录 `InspectionWidget.exe.manifest` 副本生效；VS2022 构建时 ApplicationManifest 是否正确嵌入 exe，无法在本机验证（同属第 1、2 项的 Windows 验收范围）。
4. **「从未 push」**：本地只能核到当前无 remote 配置、无 remote 条目（`git remote -v` 为空），在此状态下不存在可 push 的目标；历史时点是否曾短暂加过 remote，本地记录无法证明或证伪。

## 五、总评

Task 0 的 7 个交付文件与简报逐条对齐，全局约束（v4.8 / LangVersion 7.3 / Prefer32Bit=false / 六项引用 / 零第三方包 / manifest 三要素 / Mutex 名 / STAThread / DPI 兜底 / build.sh 真构建 / 无 remote）全部满足且经独立复核属实；构建警告与 exe 字节数（4608）报告无误。质量通过，唯一 Important 为已披露的 Mono 7.2 语言子集约束，需在后续任务简报中持续携带。
