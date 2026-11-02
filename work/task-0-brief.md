# Task 0 简报：工程骨架、清单、本地 Git 与构建链

项目：巡检截图小组件 InspectionWidget（C# WinForms / .NET Framework 4.8 / 零第三方依赖）。
工作目录：~/workspace/inspection-widget-src（已 git init，分支 main，已有 .gitignore、docs/实施计划.md、PROGRESS.md）。
完整规格在 docs/实施计划.md 的 Global Constraints，本简报是本任务的全部要求。

## 要创建的文件

1. `app.manifest`（仓库根目录）：
   - `requestedExecutionLevel level="requireAdministrator" uiAccess="false"`
   - DPI 感知：`dpiAwareness` = `PerMonitorV2,PerMonitor`，`dpiAware` = `True/PM`
   - supportedOS 含 Windows 10/11 GUID `{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}`

2. `src/InspectionWidget.csproj`（老式工程文件，供 VS2022 与 Mono msbuild 共用）：
   - OutputType=WinExe，RootNamespace/AssemblyName=InspectionWidget
   - TargetFrameworkVersion=v4.8，LangVersion=7.3，Platform AnyCPU，Prefer32Bit=false
   - ApplicationManifest 指向 `..\app.manifest`
   - 引用仅限：System、System.Core、System.Drawing、System.Windows.Forms、System.Security、System.Runtime.Serialization
   - Compile 项含通配或逐项列出 src 下 .cs（本任务只有 Program.cs、MainForm.cs；用逐项列出并在后续任务中追加的方式，或用 `<Compile Include="**\*.cs" />` 通配——选通配，减少后续任务改工程文件的冲突）

3. `InspectionWidget.sln`：VS 解决方案文件，引用 src 工程，含 Debug/Release Any CPU 配置。手写标准 sln 文本即可（VS Version 17 格式）。

4. `src/Program.cs`：
   - `Program.Main()` 带 `[STAThread]`
   - 单实例：`Mutex`（名 `InspectionWidget.SingleInstance`），已持有则直接 return
   - DPI 兜底：P/Invoke `SetProcessDpiAwarenessContext(new IntPtr(-4))`（user32），失败忽略；声明放本文件或直接写在本文件内（Native 模块 Task 3 才建，本任务先内联私有声明）
   - `Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new MainForm());`

5. `src/MainForm.cs`：最小占位窗体——`Form`，标题「巡检截图」，`TopMost=true`，尺寸 230×200，启动后定位主屏 WorkingArea 右下角（Task 2 会重写，本任务只需编译通过且定位逻辑在）。

6. `build.sh`（仓库根目录，bash）：
   - 用 Mono `msbuild`（找不到则 `xbuild`）以 Release 配置构建 `src/InspectionWidget.csproj`
   - 把产物 `InspectionWidget.exe` 与 `app.manifest`（复制为 `InspectionWidget.exe.manifest`）放进 `build/`
   - 若存在 tests 工程则一并构建并运行测试（本任务 tests 尚不存在，脚本写成「存在才跑」）
   - 任一步失败以非零退出

7. `README.md`：标题、一段简介、编译方法（VS2022 + .NET Framework 4.8 Release；或本机 Mono `bash build.sh`）、运行说明（双击 exe、管理员确认、首次选根目录）、版本记录章节（空表头即可，Task 9 填）。

## 验证（必须当场运行并把输出写进报告）
- `bash build.sh` 退出码 0
- `ls -la build/` 显示 InspectionWidget.exe 与 InspectionWidget.exe.manifest，并报告 exe 字节数

## 提交
`git add -A && git commit -m "feat(stage-0): 工程骨架、manifest、Mono 构建链"`
不得加 remote、不得 push。

## 报告
写到 work/task-0-report.md：状态（DONE / DONE_WITH_CONCERNS / BLOCKED）、创建的文件清单、build.sh 完整输出摘要、exe 字节数、提交哈希、遗留问题。返回消息只给：状态、提交哈希、一行验证摘要、 concerns（若有）。
全程简体中文。不要派生任何子代理，自己完成。
