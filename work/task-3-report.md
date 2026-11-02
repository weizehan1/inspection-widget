# Task 3 报告：选窗与点击拦截

状态：**完成**（含本修复轮：报告补全 + M-1 收拢，见文末「修复轮」节）
实现提交：`a3cd297`（`feat(stage-3): 选窗钩子（成对吞点击/Esc取消/超时）`，父提交为基线 `4543554`，已用 `git log --oneline | head -2` 自核）
日期：2026-11-02

## 交付物（与代码现状逐一对应）

| 文件 | 状态 | 内容 |
|---|---|---|
| `src/Native/NativeMethods.cs` | 新建 | P/Invoke 集中声明，见下 NativeMethods 节 |
| `src/Capture/TargetClassifier.cs` | 新建（纯逻辑） | 排除判定 `IsExcluded(className, isSelf)`，见下 TargetClassifier 节 |
| `src/Capture/HookManager.cs` | 新建 | 全局低级钩子管理，见下 HookManager 节 |
| `src/Capture/WindowResolver.cs` | 新建 | 坐标反查顶层窗口，见下 WindowResolver 节 |
| `src/MainForm.cs` | 改（接线） | 状态变化集中入口 + 钩子事件处理，见下 MainForm 接线节 |
| `tests/TargetClassifierTests.cs` | 新建 | TargetClassifier 用例，已注册进 `tests/Program.cs` |

约束落实：全部代码只用 C# 7.2 以内语法（R6），`build.sh` 以 `/p:LangVersion=7.2` 覆盖编译生产与测试工程、由编译器直接验证语言子集；两个工程均未新增任何第三方引用，`csproj` 仅引用 `System.*` 框架程序集。

### NativeMethods（`src/Native/NativeMethods.cs`）

集中声明本任务所需的全部 P/Invoke，后续任务在此文件继续追加：

- 钩子与消息循环（user32）：`SetWindowsHookEx`、`UnhookWindowsHookEx`、`CallNextHookEx`、`PostThreadMessage`、`GetMessage` / `TranslateMessage` / `DispatchMessage`；kernel32：`GetModuleHandle`、`GetCurrentThreadId`。
- 窗口查询与操作（user32）：`WindowFromPoint`、`GetAncestor`（`GA_ROOT=2`）、`GetClassName`、`GetWindowText` / `GetWindowTextLength`、`IsIconic`、`IsWindow`、`GetWindowRect`、`SetForegroundWindow`、`BringWindowToTop`、`GetForegroundWindow`。
- 结构体（均 `[StructLayout(LayoutKind.Sequential)]`，字段顺序与 Win32 定义一致）：`POINT`（配 `ToPoint()`）、`RECT`（配 `ToRectangle()`）、`MSLLHOOKSTRUCT`、`KBDLLHOOKSTRUCT`、`MSG`；其中句柄 / `WPARAM` / `LPARAM` 为 `IntPtr`，结构体指针大小字段（`MSLLHOOKSTRUCT.dwExtraInfo`、`KBDLLHOOKSTRUCT.dwExtraInfo`、`MSG.wParam`）与 `PostThreadMessage` 的 `wParam` 为 `UIntPtr`。
- 委托：`HookProc` 为鼠标 / 键盘低级钩子共用的单一签名 `IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)`。
- 常量：`WH_MOUSE_LL=14`、`WH_KEYBOARD_LL=13`、`WM_LBUTTONDOWN=0x0201`、`WM_LBUTTONUP=0x0202`、`WM_RBUTTONDOWN=0x0204`、`WM_KEYDOWN=0x0100`、`WM_QUIT=0x0012`、`VK_ESCAPE=0x1B`、`GA_ROOT=2`。

### TargetClassifier（`src/Capture/TargetClassifier.cs`，纯逻辑）

`public static bool IsExcluded(string className, bool isSelf)`，判定顺序与简报 B 逐条一致：

- `isSelf` 为真恒返回 `true`，先于一切其他判断；
- `className` 为 `null` 或空（`string.IsNullOrEmpty`）返回 `true`（查不到类名按排除处理，宁可让用户重点、不能截错窗口）；
- `className` 以 `StringComparison.OrdinalIgnoreCase` 大小写不敏感命中 `{Progman, WorkerW, Shell_TrayWnd, Shell_SecondaryTrayWnd}`（桌面与主 / 副屏任务栏）返回 `true`；
- 其余普通类名（如 `Notepad`、`Chrome_WidgetWin_1`）返回 `false` 放行。

### HookManager（`src/Capture/HookManager.cs`）

对外接口：事件 `ExternalClick(Point)` / `CancelRequested` / `TimeoutElapsed`，属性 `IsRunning`，方法 `Start(Rectangle selfBounds)` / `Stop()`（幂等），实现 `IDisposable`。逐条落实简报 C：

- **独立线程 + 消息循环**：`Start` 新建 `IsBackground=true` 专用线程，线程内依次 `SetWindowsHookEx` 挂鼠标 `WH_MOUSE_LL` 与键盘 `WH_KEYBOARD_LL` 钩子后跑 `GetMessage` / `TranslateMessage` / `DispatchMessage` 循环，循环退出（`result<=0`）后 `Unhook` 收尾。
- **委托强引用防 GC**：鼠标 / 键盘 `HookProc`、钩子句柄、线程、超时 `Timer` 均存于一次会话对象 `HookSession` 的实例字段内成对管理；会话由 `_session` 字段与线程委托闭包双重持有，运行期不会被回收。`Start` 重入（已在运行）仅更新 `SelfBounds`、不新建会话、不重置计时。
- **自身矩形先判放行**：鼠标回调 `WM_LBUTTONDOWN` 先以 `SelfBounds.Contains(pt)` 判定，矩形内走 `CallNextHookEx` 放行返回（设备按钮 Click / 取消 / 切换照常）；矩形外记录 `PendingPoint`、置 `SwallowNextUp`、返回 `(IntPtr)1` 吞掉 Down。
- **外部 Down / Up 成对吞**：`WM_LBUTTONUP` 仅在 `SwallowNextUp` 为真时吞掉、清标志，并以 Down 点坐标投递 `ExternalClick`（Up 吞掉后才投递）。回调内除 `PtrToStructure` 取坐标、`Contains` 判定与事件投递外无重活、无同步等待。
- **右键与全局 Esc 取消**：`WM_RBUTTONDOWN` 不判位置（任意位置含组件自身）一律吞掉并投递 `CancelRequested`；键盘回调 `WM_KEYDOWN` 且 `vkCode==VK_ESCAPE`（不论焦点）吞掉并投递 `CancelRequested`。
- **30 秒超时自停**：`Start` 成功后起固定 30 秒 `System.Threading.Timer`（一次性、不滑动续期，全文无重置计时代码；`Start` 重入亦不重置，与「进入等待后固定 30 秒」口径一致）；到期 `OnTimeoutTimer` 先 `Stop()` 自停（`IsRunning` 随 `TeardownRequested` 变 `false`）再投递 `TimeoutElapsed`。
- **Stop 成对卸载且异常路径释放**：`Stop` 以 `TeardownRequested` 守卫保证幂等；`FinalizeTeardown` 投 `WM_QUIT` → `Join(500)` → 超时兜底从外部 `Unhook` 再 `Join(500)`，钩子在消息循环退出处亦 `Unhook`，双路均把句柄清零防重复卸载。钩子线程挂钩中途失败时，`catch` 内先 `Unhook` 已挂钩子、置 `StartError` 再置 `Ready`；`Start` 收到失败后 `Stop` 并抛 `InvalidOperationException` 给调用方。`Dispose` 先置 `_disposed` 再 `Stop`，可重复调用。
- **Down 已吞时 Stop 的 Up 收尾（简报 C 第 6 点）**：`Stop` 时若 `SwallowNextUp` 为真，不立即卸钩，起 200ms `DeferredTeardownTimer` 延迟收尾；配对 Up 先到则在 Up 回调内吞掉后由回调线程 `FinalizeTeardown(fromHookThread: true)`（只投 `WM_QUIT`、不 `Join` 自己）；200ms 到仍无 Up 则定时器强制收尾。代码注释已写明此口径。迟到的 `ExternalClick` 由 MainForm 的 `State==Waiting` 守卫忽略。
- **事件投递**：一律经 `Start` 时捕获的 `SynchronizationContext.Post`（只投递不等待）；捕获不到时直接触发且类注释已注明由订阅方负责切线程，MainForm 三个处理器均另有 `InvokeRequired` → `BeginInvoke` 兜底。

### WindowResolver（`src/Capture/WindowResolver.cs`）

类型：`enum ResolveReason { Ok, Excluded, Minimized, Gone }`；`ResolveResult { IntPtr Hwnd; string Title; string ClassName; ResolveReason Reason }`；静态方法 `ResolveTopLevel(Point pt, IntPtr selfHwnd)` 与 `StillValid(IntPtr hwnd)`。

`ResolveTopLevel` 流程（与代码现状一致）：`WindowFromPoint` → `GetAncestor(GA_ROOT)`（零句柄回退原 `hwnd`，点本身零句柄直接 `Gone`）→ `IsWindow` 复核（无效为 `Gone`，此为简报未明列的合理补强）→ 以 `isSelf = (root == selfHwnd)` 与 `GetClassName` 结果送 `TargetClassifier.IsExcluded` 判定（命中为 `Excluded`）→ 取标题（只作提示）→ `IsIconic` 判定（为真为 `Minimized`）→ 其余为 `Ok` 并带标题。口径：`Excluded` 不在此处改状态、由 MainForm 保持 Waiting；`Minimized` 由 MainForm `Cancel` 回 Idle。说明：代码现状为取标题在 `IsIconic` 判定之前（审查 M-2 记录的顺序微差），`Minimized` 结果会多带一个 `Title`，而 MainForm 最小化分支不使用 `Title`，无功能影响。

`StillValid`：零句柄返回 `false`，否则 `IsWindow && !IsIconic`，供 Task 4 截图前复核。

### MainForm 接线（`src/MainForm.cs`）

- **单一状态变化入口 `OnStateChanged()`**：按状态机 `State` 决定 `Waiting` → `_hooks.Start(this.Bounds)`、其余 → `_hooks.Stop()`，并刷新等待态视觉。设备按钮、`CancelRequested`、`TimeoutElapsed`、`Ok`（`BeginCapture` 后）、`OnTargetResolved`（`CompleteCapture` 后）均只改状态机后调它。`Start` 抛异常时 `catch` 内 `Cancel` 回 Idle + `RefreshStateVisuals` + `Stop` + 错误反馈，无钩子泄漏。
- **Waiting 禁拖**：`OnDragMouseDown` 在 Waiting 时直接返回（`_dragging` 不置位），保证 `Start` 时取的 `Bounds` 在整个等待期有效。
- **`OnHookExternalClick` 分支**：非 Waiting 直接忽略（迟到 / 异常路径守卫）；先 `WindowResolver.ResolveTopLevel(pt, this.Handle)` 解析再决定转移（状态机无 `Capturing→Waiting` 转移，排除分支必须留在 Waiting，故不先行 `BeginCapture`）：
  - `Excluded` → 反馈「点到桌面/任务栏，已忽略，请点目标窗口」+ 保持 Waiting 并重启钩子（新一轮 30 秒，修复轮后经 `RestartHooksWhileWaiting()`，见文末）；
  - `Gone` → 反馈「该窗口已关闭，请重新点选」+ 保持 Waiting 并重启钩子（同上）；
  - `Minimized` → 反馈「该窗口已最小化，请先打开」+ `Cancel` 回 Idle（经 `OnStateChanged` 停钩）；
  - `Ok` → `BeginCapture()` 成功才继续，经 `OnStateChanged` 停钩后调接入点 `OnTargetResolved(result, CurrentRequestId)`；本任务占位反馈「已选中窗口：<标题>」并 `CompleteCapture` 回 Idle，真实截图由 Task 4 在同一入口接入。
- **Cancel / Timeout**：`OnHookCancelRequested` 与 `OnHookTimeoutElapsed` 均 `Cancel` 状态机 + `OnStateChanged` 停钩 + 对应反馈（取消为「已取消选窗」，超时为「等待超时，已取消」）；钩子已自停时 `Stop` 幂等无副作用。
- **FormClosing 必停钩**：`OnFormClosing` 先 `_hooks.Stop()` 再 `_hooks.Dispose()`；重写的 `Dispose(bool)` 亦 `Dispose` 钩子，重复释放有守卫。

## TDD 证据

TargetClassifier 按简报 B 先 TDD，如实过程如下：

- **RED（先 FAIL）**：先写 `tests/TargetClassifierTests.cs` 并注册进 `tests/Program.cs`，此时 `src/Capture/TargetClassifier.cs` 尚不存在，运行 `bash build.sh` 因被测类型缺失而编译失败、测试无法通过——此为预期的先行失败，锚定了 `IsExcluded` 的签名与判定口径后才动实现。
- **GREEN（后 PASS）**：实现 `src/Capture/TargetClassifier.cs` 后，同一命令 `bash build.sh` 转为退出 0，TargetClassifier 全部用例通过且既有用例无回归（总数见下节）。
- 用例构成：`tests/TargetClassifierTests.cs` 共 11 个 `TestRunner.Run` 用例，覆盖 `isSelf` 恒排除（含类名为 `null` 时）、`Progman` / `WorkerW` / `Shell_TrayWnd` / `Shell_SecondaryTrayWnd` 各一例、大小写不敏感一例（内含 4 个断言）、`null` 按排除、空字符串按排除、普通类名 `Notepad` 与 `Chrome_WidgetWin_1` 放行各一例，与简报 B 的全部分支一一对应。

## 用例总数

运行时口径（计数单位为 `TestRunner.Run` 的一次运行时调用）：

- Task 3 前基线：**120**
- 本任务新增（TargetClassifier）：**11**
- 合计总数：**131**，全部通过、0 失败

`bash build.sh` 退出 0。说明：静态 `TestRunner.Run` 调用点与运行时条数存在既有差额（`DeviceNameValidatorTests` 有一处 `Run` 写在循环内展开多条），历次对账口径一致，故总数只用上述运行时口径，不用静态点数口径。

## 提交

- 实现提交：`a3cd297` — `feat(stage-3): 选窗钩子（成对吞点击/Esc取消/超时）`
- 本修复轮提交：`fix(stage-3): 补全报告并收拢钩子重启逻辑`，哈希以 `git log` 核对为准（本报告随该提交一同入库，回报中给出）。

## 遗留问题（钩子运行时行为全部待 Windows 实机逐项验收）

本 Linux / Mono 环境下 `SynchronizationContext` 与 Win32 低级钩子均不可用，以下行为只有代码与编译层面的证据、无运行时实测，须在 Windows 实机验收时逐项销账，不计入自动化通过结论：

1. **成对吞点击不误触**：外部左键 Down / Up 真被成对吞掉，目标窗口收不到一次完整点击；含「Down 已吞、取消 / 超时恰在 Down / Up 之间」时 200ms 安全窗内松开的 Up 仍被吞掉、不漏给目标窗口。
2. **自身放行**：组件自身矩形内点击真放行，设备按钮的 Click / 取消 / 切换照常工作；含物理像素坐标与窗体 `Bounds` 在高 DPI 下的对齐是否准确。
3. **Esc / 右键取消**：任意位置右键、任意焦点下 Esc 真被吞掉且只触发一次取消（不重复投递、不漏给目标应用）。
4. **30 秒超时**：实机计时准确，到期先自停再提示「等待超时，已取消」；含「超时恰在 Down / Up 之间」的收尾时序。
5. **最小化提示**：点中已最小化窗口时真走 `Minimized` 分支、提示「该窗口已最小化，请先打开」并回 Idle 停钩。
6. **标题识别一致性**：`WindowFromPoint` → `GetAncestor(GA_ROOT)` 对真实窗口（含桌面 `Progman` / `WorkerW`、主 / 副屏任务栏、子控件、已关闭窗口）的解析结果，以及 `GetClassName` / 标题取值与实机所见一致；`Ok` 分支占位反馈「已选中窗口：<标题>」的标题正确。
7. 其余实机项：`WH_MOUSE_LL` / `WH_KEYBOARD_LL` 在实机的装钩成功率；`SynchronizationContext` 在实机 UI 线程上确为 WinForms 上下文、`Post` 真切回 UI 线程；专用线程消息泵的实际派发、`WM_QUIT` 退出循环与 `Unhook` 配对实效、`FinalizeTeardown` 中 `Join(500)` 兜底路径；对高完整性级别（管理员）窗口的点击受 UIPI 约束低级钩子可能看不到，须实机确认并记入验收表；`Start` 失败注入（钩子被安全软件拦截等）时 MainForm 回 Idle + 提示的实景路径。

另有审查备查项（本轮不改，记录口径）：M-2 取标题顺序微差（见 WindowResolver 节，无功能影响）；M-3 迟到事件无会话代次校验（极端时序下旧会话配对 Up 理论上可能被新 Waiting 会话消费，概率极低，并入 Task 4 联调考虑）；M-4 `Start` 启动超时的理论泄漏窗口（钩子线程长时间未能完成挂钩时，概率极低，备查）。

## 修复轮（Task 3 审查 I-1 + M-1）

审查报告 `work/task-3-review.md`：规格通过，质量 Critical 0 / Important 1 / Minor 4。本轮处置其中两项：

- **I-1 报告不完整且总数错误（文档缺陷，代码不受影响）**：原 `work/task-3-report.md` 在磁盘上被截断（残稿中途断句、含字面截断标记），简报要求的 TDD 证据、用例总数构成、Windows 实测遗留问题等章节缺失，且流传的总数与实测不符。本文件即为覆盖残稿的完整重写：交付物与代码现状逐项对应、TDD 过程如实补全、遗留问题按实机验收口径列全；用例总数只用可复现的运行时口径——基线 120、新增 11、总数 131（与审查独立复跑及协调者复跑一致）。
- **M-1 Excluded / Gone 两分支钩子重启逻辑重复**：`OnHookExternalClick` 的 `Excluded` 与 `Gone` 两分支原各自内联一份「`Stop` → `Start` + 异常处理」逻辑（各约 12 行）。已抽为私有方法 `RestartHooksWhileWaiting()` 收拢，两分支改调该方法。行为保持完全一致：仍为先 `Stop` 再以 `this.Bounds` `Start`；异常时仍为 `Cancel` 状态机 + 调既有集中入口 `OnStateChanged()` + 反馈「选窗启动失败：<信息>」；两分支各自的先行反馈文案（`Excluded` 为「点到桌面/任务栏，已忽略，请点目标窗口」、`Gone` 为「该窗口已关闭，请重新点选」）与保持 Waiting 的语义不变。未改其他任何逻辑。

验证：本轮改完后亲跑 `bash build.sh` 须为退出 0、0 错误，总数 131 全部通过、0 失败（实际结果以回报的一行测试摘要为准）。
