# Task 0 执行报告：工程骨架、清单、本地 Git 与构建链

## 状态

DONE_WITH_CONCERNS

（功能全部达成、构建实测通过；concerns 为 Mono 工具链的两点适配，见「遗留问题与适配说明」。）

## 创建的文件清单

| 文件 | 说明 |
| ---- | ---- |
| `app.manifest` | 管理员权限 `requireAdministrator`（uiAccess=false）；DPI：`dpiAwareness`=`PerMonitorV2,PerMonitor`、`dpiAware`=`True/PM`；supportedOS 含 Windows 10/11 GUID `{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}` |
| `src/InspectionWidget.csproj` | 老式工程：WinExe、RootNamespace/AssemblyName=InspectionWidget、TargetFrameworkVersion=v4.8、LangVersion=7.3、AnyCPU、Prefer32Bit=false、ApplicationManifest 指向 `..\app.manifest`；引用仅 System、System.Core、System.Drawing、System.Windows.Forms、System.Security、System.Runtime.Serialization；Compile 用通配 `**\*.cs`；Debug/Release 两组属性均写全 OutputPath 等（兼容 xbuild）；工程 GUID `{C3CF6B60-4871-4E2E-B57E-82EFD26A79F2}` |
| `InspectionWidget.sln` | VS Version 17 格式，引用 src 工程，含 Debug/Release Any CPU 配置 |
| `src/Program.cs` | `[STAThread]` Main；单实例 Mutex 名 `InspectionWidget.SingleInstance`，已持有直接 return；P/Invoke user32 `SetProcessDpiAwarenessContext(new IntPtr(-4))` 内联声明、失败忽略；EnableVisualStyles / SetCompatibleTextRenderingDefault(false) / Run(new MainForm()) |
| `src/MainForm.cs` | 占位窗体：标题「巡检截图」、TopMost=true、尺寸 230×200、启动定位主屏 WorkingArea 右下角 |
| `build.sh` | msbuild 优先、否则 xbuild，Release 构建 csproj；产物 exe 与 app.manifest（改名 InspectionWidget.exe.manifest）放入 `build/`；tests 工程存在才构建并运行；`set -euo pipefail` 保证失败非零退出；已 chmod +x |
| `README.md` | 标题、简介、编译方法（VS2022 + .NET Framework 4.8 Release；或 Mono `bash build.sh`）、运行说明（双击 exe、管理员确认、首次选根目录）、版本记录空表 |

未改动 `PROGRESS.md` 与 `docs/`。

## 构建验证（当场实跑）

命令：`bash build.sh`（本机无 msbuild，实际走 xbuild，Mono 6.8.0.105）。

最终一次完整运行输出摘要：

```
使用构建工具：xbuild
构建 src/InspectionWidget.csproj（Release）...
XBuild Engine Version 14.0 / Mono, Version 6.8.0.105
Target CoreCompile: mcs ... /out:obj/Release/InspectionWidget.exe Program.cs MainForm.cs
  ... /target:winexe /langversion:7.2 /reference:/usr/lib/mono/4.8-api/System.dll 等 4.8-api 引用
Build succeeded.
  1 Warning(s) / 0 Error(s)
产物已放入 build/：
  InspectionWidget.exe（4608 字节）
  InspectionWidget.exe.manifest（1038 字节）
未发现 tests 工程，跳过测试。
构建完成。
退出码：0
```

另做了一次重复构建（不清 obj/bin 直接再跑），同样退出码 0，确认通配 Compile 在增量构建下无重复引用问题。

`ls -la build/` 实测结果：

```
-rwxrwx--- 1 root nogroup 4608 InspectionWidget.exe
-rw-rw---- 1 root nogroup 1038 InspectionWidget.exe.manifest
```

- **exe 字节数：4608 字节**（`file` 识别为 PE32 executable (GUI) Mono/.Net assembly）
- manifest 副本字节数：1038 字节

构建警告 1 条：xbuild 工具集提示 `TargetFrameworkVersion 'v4.8' not supported by this toolset (ToolsVersion: 14.0)`；但本机 `/usr/lib/mono/4.8-api` 引用程序集实际存在，编译命令行确实引用的是 4.8-api 下的各程序集，构建成功，仅为工具集版本提示，不影响产物。

## 提交

- 功能提交哈希：`745776474fab758e5e721e64e51121858f579c1a`（短：7457764）
- 提交信息：`feat(stage-0): 工程骨架、manifest、Mono 构建链`
- 提交内容：上述 7 个文件（未用 `git add -A` 全量添加，原因见遗留问题第 3 条）；未加 remote、未 push。
- 本报告文件随后以独立 docs 提交入库，其哈希以 `git log` 为准。

## 遗留问题与适配说明

1. **LangVersion 适配（构建调用层）**：首次实测 `bash build.sh` 失败——Mono 6.8 的 mcs 最高只支持 C# 7.2，csproj 的 LangVersion=7.3 触发 `error CS1617: Invalid -langversion option '7.3'`。处理：csproj 内按简报保留 7.3（VS2022/.NET Framework csc 原生支持，供 Windows 端构建）；build.sh 的 xbuild 分支增加 `/p:LangVersion=7.2` 全局属性覆盖（msbuild 分支不加），并在脚本内注释说明。改后构建通过。本项目代码若后续使用 7.3 独有语法，Mono 端会编不过，写代码时请守住 7.2 语法子集。
2. **xbuild 的 v4.8 工具集警告**：见上文，仅警告、引用实际走 4.8-api，无需改 TargetFrameworkVersion。
3. **提交范围**：执行 `git status` 时发现工作区另有协调者新放入的未跟踪文件 `work/task-6-brief.md`、`work/task-7-brief.md` 及 `build/` 产物目录（其中 `InspectionWidget.exe.manifest` 未被 .gitignore 覆盖）。为免把无关文件扫进本任务提交，本次改为逐项添加本任务 7 个文件提交；`build/` 产物与两份新简报仍为未跟踪状态，留待协调者处置。如需严格照简报 `git add -A`，请协调者确认后补交。
4. exe 未做 Windows 实机运行验证（本机为 Linux/Mono，仅能验证编译产物为合法 PE32 WinExe 程序集）；管理员清单与 DPI 行为需在 Task 9 的 Windows 实跑验收中确认。
