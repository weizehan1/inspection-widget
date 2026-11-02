# Task 8 简报：异常兜底、Release 打包与验收矩阵落表

项目：巡检截图小组件 InspectionWidget（C# 7.3 / .NET Framework 4.8 / 零第三方依赖）。
工作目录：~/workspace/inspection-widget-src。前置：Task 0–7 已过关。
权威：docs/实施计划.md Task 8 节；开发步骤阶段 8 与 15 项验收表；白皮书第 11 节。

## A. src/ErrorLog.cs
```csharp
public static class ErrorLog {
    public static void Write(string context, Exception ex);
    public static string CurrentLogPath { get; }   // 实际写入路径（exe 同目录优先，回退 LocalAppData）
}
```
- 目标文件：`<BaseDirectory>\error.log`；打开/写入失败→`%LocalAppData%\InspectionWidget\error.log`；再失败→静默放弃。**Write 内部绝不抛异常**（全 try/catch 吞掉）。
- 内容格式：`[yyyy-MM-dd HH:mm:ss] <context> <ExceptionType>: <Message>` + 堆栈（多行），追加写。绝不写凭据明文/截图内容——调用方只传环节名，本类不接收业务数据。
- 给 ErrorLog 写一个小测试（tests/ErrorLogTests.cs）：写入临时目录（提供内部可注入目录的构造/静态设置点，测后恢复）→文件存在且含环节名与异常类型；路径不可写时不抛异常。

## B0. 顺手修 Task 1 审查 Minor-3
`ConfigStore.Save` 的写文件段/取锁段只捕获 IOException/UnauthorizedAccessException，序列化异常会穿透。给写段补一层兜底捕获（Exception→SaveResult{ Saved=false, Error=摘要 }），保持既有语义不变，补一条用例或在报告中说明验证方式。

## B. Program.cs 全局异常挂载
- 顺手修 Task 0 审查 Minor-3：`new Mutex(true, name, out createdNew)` 在首个实例异常退出后可能抛 `AbandonedMutexException`——捕获该异常并视为「已取得所有权」继续启动（该异常语义即本进程已成为所有者）。
- `Application.ThreadException` → ErrorLog.Write("UI线程", e.Exception) + 尝试让 MainForm 回到 Idle 并保留待保存图：实现口径——MainForm 暴露静态/实例方法 `RecoverToIdle()`，Program 通过 Application.OpenForms 找到 MainForm 调用；找不到或调用失败只记日志。处理完不退出（可恢复）。
- `AppDomain.CurrentDomain.UnhandledException` → ErrorLog.Write("非UI线程", ...)；若 IsTerminating→不再拦截（系统会终止），日志尽力写。
- MainForm 内已有的工作线程异常都已各自处理（Task 3–6），本任务只加这两处兜底与 RecoverToIdle。

## C. 资源纪律自查（写进验收记录，不逐个改代码除非发现真实泄漏）
- 全仓 grep：`new Bitmap`、`Graphics.FromImage`、`GetHdc`、`CreateCompatibleDC`、`SetWindowsHookEx`、`new Timer`、`FileStream`，逐处核对 using/finally/Dispose/成对卸载，列成清单写进 docs/验收记录.md 的「资源纪律核对」一节。发现确实未释放的，修掉并在报告中注明（修复属本任务范围）。

## D. Release 构建与产物落位（补充交付要求，硬性）
- build.sh 的 Release 路径确认：优化开启（Optimize=true 的 Release 属性组）、产物拷入 build/ 时**不带 .pdb**（xbuild 若生成 pdb/mdb，拷贝步骤排除）。
- 最终产物固定路径：`build/InspectionWidget.exe`（补充要求①）。同时保留 `build/InspectionWidget.exe.manifest` 外置清单（R5）。
- 记录 exe 字节数与 SHA256（`sha256sum build/InspectionWidget.exe`），写进报告与验收记录。
- 体积目标量级 ≤300KB 仅供参考：Mono 产物以实测为准，超限如实记录并注明「正式交付建议 Windows VS2022 重编」（R5 已有口径，不算失败）。

## E. docs/验收记录.md（新建）
按开发步骤阶段 8 的 15 项验收表逐项列（验收项 / 对应阶段 / 状态 / 证据或实机判据）。状态只许三类：
- 「已验证」：本机有当场证据（构建/测试输出），写明证据出处（哪个测试文件/命令）；
- 「仅编译通过」：代码按规格实现且编译过，但行为需运行时环境；
- 「待实机」：必须在 Windows 真机验证，写明通过判据（抄开发步骤表）。
逐项映射（参考，须按代码实际状态如实填，不许全填已验证）：
1. 管理员窗口可截——仅编译通过（manifest+代码就绪）/实机判据；
2. 遮挡完整无阴影边——仅编译通过（几何逻辑已单测→几何部分可注「逻辑已验证、成图待实机」）；
3. 右下角防自遮挡——仅编译通过；
4. 副屏不偏不空——几何逻辑已验证、成图待实机；
5. 150% 缩放——DPI 声明就绪、待实机；
6. 最小化有提示——逻辑就绪、待实机；
7. 重名顺延与暂存——**已验证**（Task 5 测试）；
8. 凭据加密与身份边界——DPAPI 仅编译通过、待实机（跨账号判据）；
9. 自动清空与退出清空——决策逻辑已验证（Task 6 测试）、真实剪贴板待实机；
10. 配置损坏自愈——**已验证**（Task 1 测试）；
11. 连续截图稳定性——待实机（附资源纪律核对清单结论）；
12. 输入与异步生命周期——钩子逻辑就绪、待实机；
13. 保存故障兜底——**已验证**（Task 5 测试，两级暂存与不覆盖）；
14. 分享场景干净——待实机（只发 exe 首次初始化判据）；
15. 折叠与高度——高度逻辑已验证（Task 2 测试）、实机视觉待验。

## 验证与提交
- `bash build.sh` 退出 0；ErrorLog 测试 PASS、全部既有不回归；产物与哈希实测记录。
- commit：`feat(stage-8): 异常兜底/Release打包/验收记录`
- 报告 work/task-8-report.md：状态、证据、exe 大小与 SHA256、资源核对结论、提交哈希、遗留。返回只给状态/哈希/一行摘要/concerns。
全程简体中文。不要派生子代理。不加 remote、不 push。

> **补充约束（R6，Task 0 审查裁决）**：全部代码只许 C# 7.2 以内语法（Mono mcs 上限），不得用 7.3 独有语法。
