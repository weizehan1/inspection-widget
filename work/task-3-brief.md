# Task 3 简报：选窗与点击拦截

项目：巡检截图小组件 InspectionWidget（C# 7.3 / .NET Framework 4.8 / 零第三方依赖）。
工作目录：~/workspace/inspection-widget-src。前置：Task 0–2 已过关（MainForm 有状态机与等待态视觉，凭据点击为占位）。
权威：docs/实施计划.md Task 3 节；开发步骤阶段 3；白皮书 3.3/3.4/6.1。线程口径按计划裁决 R1：钩子独立线程、重活工作线程、UI 只收结果。

## A. src/Native/NativeMethods.cs（本任务先建全，后续任务往里加）
集中声明本任务需要的 P/Invoke，签名规范（IntPtr/UIntPtr、MarshalAs 口径）：
- user32：SetWindowsHookEx、UnhookWindowsHookEx、CallNextHookEx、WindowFromPoint、GetAncestor（GA_ROOT=2）、GetClassName、GetWindowText/GetWindowTextLength、IsIconic、IsWindow、GetWindowRect、SetForegroundWindow、BringWindowToTop、GetForegroundWindow（如需）。
- 结构体：POINT、RECT（配 ToRectangle 转换）。
- 委托类型：HookProc（LowLevelMouseProc / LowLevelKeyboardProc 均可共用一个签名 `IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)`）。
- 常量：WH_MOUSE_LL=14、WH_KEYBOARD_LL=13、WM_LBUTTONDOWN=0x0201、WM_LBUTTONUP=0x0202、WM_RBUTTONDOWN=0x0204、WM_KEYDOWN=0x0100、VK_ESCAPE=0x1B。
- MSLLHOOKSTRUCT / KBDLLHOOKSTRUCT 结构体（Marshal 布局）。

## B. src/Capture/TargetClassifier.cs（纯逻辑，先 TDD）
```csharp
public static class TargetClassifier {
    public static bool IsExcluded(string className, bool isSelf);
}
```
isSelf→恒 true；className 大小写不敏感命中 {Progman, WorkerW, Shell_TrayWnd, Shell_SecondaryTrayWnd}→true；null/空→true（查不到类名按排除处理，写测试锚定）。普通类名（"Notepad"、"Chrome_WidgetWin_1"）→false。
测试文件 tests/TargetClassifierTests.cs 覆盖上述，先 FAIL 后 PASS。

## C. src/Capture/HookManager.cs
```csharp
public sealed class HookManager : IDisposable {
    public event Action<System.Drawing.Point> ExternalClick;   // 外部左键按下坐标（物理像素）
    public event Action CancelRequested;                        // 右键或 Esc
    public event Action TimeoutElapsed;                         // 30 秒无操作
    public bool IsRunning { get; }
    public void Start(System.Drawing.Rectangle selfBounds);     // 在钩子线程上挂钩并跑消息循环
    public void Stop();                                          // 成对卸载并停线程，幂等
}
```
实现要点（逐条落实，审查会逐条核）：
1. Start 时新建专用线程（IsBackground=true），线程内 SetWindowsHookEx 鼠标+键盘，GetMessage 消息循环；委托存入**实例字段强引用**（防 GC），且钩子句柄、线程引用成对管理。
2. 鼠标回调：nCode>=0 且 WM_LBUTTONDOWN 时取 MSLLHOOKSTRUCT.pt：先判 selfBounds.Contains(pt)——在内→CallNextHookEx 放行并返回；外部→置 `_swallowNextUp=true`、记录坐标、返回 (IntPtr)1 吞掉。随后 WM_LBUTTONUP 且 _swallowNextUp→吞掉、清标志、通过线程外回调触发 ExternalClick（用 Control 的 BeginInvoke 由 MainForm 注入的同步上下文或直接事件——实现选：HookManager 暴露事件，触发前切到创建时捕获的 SynchronizationContext；拿不到则直接触发并注明由订阅方负责切线程）。**回调内除坐标判定外不做任何重活**，触发事件也只许投递不许同步等待。
3. WM_RBUTTONDOWN（外部或任意位置，组件自身右键也算取消语义——口径：任意位置右键都取消）→吞掉并投递 CancelRequested。键盘回调：WM_KEYDOWN 且 vkCode==VK_ESCAPE→吞掉并投递 CancelRequested（不论焦点）。
4. 超时：Start 时启动 30 秒 Timer（System.Threading.Timer），到期投递 TimeoutElapsed 并自停（Stop 由订阅方或内部调用，内部自停后 IsRunning=false）；任何一次有效活动不重置（口径：进入等待态后固定 30 秒，规格只要求无操作超时兜底，不做滑动续期）。
5. Stop：向钩子线程投递 WM_QUIT（PostThreadMessage）或用 Unhook + Thread.Join(短超时) 收尾；保证异常路径（Start 中途失败）也释放已挂的钩子并回报失败（Start 抛异常前清理，MainForm 收到异常回 Idle 并提示）。
6. 成对吞 Up 的收尾：若 Down 已吞但程序在此前 Stop（如取消/超时恰好在 Down/Up 之间），Stop 后仍须保证那一次 Up 被吞——实现口径：超时/取消的实际卸载延迟到 Up 到达或再等 200ms 安全窗后执行；简单做法：内部 `_pendingUpSwallow` 标志在 Stop 时若为真，先不卸钩，等 Up 被吞或 200ms 计时到再卸。此点写注释说明。

## D. src/Capture/WindowResolver.cs
```csharp
public enum ResolveReason { Ok, Excluded, Minimized, Gone }
public sealed class ResolveResult { public IntPtr Hwnd; public string Title; public string ClassName; public ResolveReason Reason; }
public static class WindowResolver {
    public static ResolveResult ResolveTopLevel(System.Drawing.Point pt, IntPtr selfHwnd);
    public static bool StillValid(IntPtr hwnd);   // IsWindow && !IsIconic，供截图前复核
}
```
流程：WindowFromPoint→GetAncestor(GA_ROOT)→与 selfHwnd 同→Excluded；GetClassName 后 TargetClassifier 判定→Excluded；IsIconic→Minimized；GetWindowText 取标题（只作提示）。

## E. MainForm 接线
- 进入 Waiting（状态机 SelectDevice 成功且转入 Waiting）：`_hooks.Start(this.Bounds)`（注意 Bounds 随拖动变化，Start 时取当前值即可；等待态期间窗体不可拖动——标题栏拖动在 Waiting 时禁用，避免 selfBounds 失效）。
- ExternalClick：BeginInvoke 回 UI → 状态机 BeginCapture() 成功才继续：WindowResolver.ResolveTopLevel(pt, this.Handle)：
  - Excluded→反馈提示「点到桌面/任务栏，已忽略，请点目标窗口」+ 保持 Waiting 并重启钩子（Stop 后 Start）；
  - Minimized→提示「该窗口已最小化，请先打开」+ Cancel 回 Idle、停钩；
  - Ok→（本任务）反馈显示「已选中窗口：<标题>」+ CompleteCapture 回 Idle、停钩（真实截图 Task 4 接入同一入口，代码留 `OnTargetResolved(result, requestId)` 方法作为接入点）。
- CancelRequested / TimeoutElapsed：Cancel 回 Idle + 停钩 + 反馈提示（超时提示「等待超时，已取消」）。
- 设备按钮在 Waiting 的取消/切换语义已在状态机层：钩子放行了自身点击，按钮 Click 照常走 Task 2 逻辑；但状态变化时须同步钩子启停——把启停集中到 `OnStateChanged()` 一个方法里按 State 决定（Waiting→Start，其余→Stop），按钮 Click、ExternalClick、Cancel 三条路径都只改状态机再调它，防钩子泄漏。
- FormClosing 必 Stop+Dispose 钩子。

## 验证与提交
- `bash build.sh` 退出 0；TargetClassifier 测试 PASS 且全部既有测试不回归。
- commit：`feat(stage-3): 选窗钩子（成对吞点击/Esc取消/超时）`
- 报告 work/task-3-report.md：状态、TDD 证据、用例总数、提交哈希、遗留问题（含哪些只能 Windows 实测）。返回只给状态/哈希/一行摘要/concerns。
全程简体中文。不要派生子代理。

> **补充约束（R6，Task 0 审查裁决）**：全部代码只许 C# 7.2 以内语法（Mono mcs 上限），不得用 7.3 独有语法。
