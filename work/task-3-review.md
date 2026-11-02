# Task 3 审查报告：选窗与点击拦截

审查范围：提交 4543554..a3cd297（`feat(stage-3): 选窗钩子（成对吞点击/Esc取消/超时）`），对照 work/task-3-brief.md（含 R6：C# 7.2 子集）。
方法：通读简报、报告与审查包，并直读源码原文（src/Native/NativeMethods.cs、src/Capture/TargetClassifier.cs、HookManager.cs、WindowResolver.cs、src/MainForm.cs 相关接线、tests/TargetClassifierTests.cs 与 tests/TestFramework.cs、Program.cs），独立复跑 `bash build.sh` 做计数对账。未改任何生产代码、未提交。

## 一、规格结论：✅ 通过

### 1. TargetClassifier（✅）
- `IsExcluded(className, isSelf)`：isSelf 恒 true（先于一切判断）；{Progman, WorkerW, Shell_TrayWnd, Shell_SecondaryTrayWnd} 以 `OrdinalIgnoreCase` 大小写不敏感命中；null/空（`string.IsNullOrEmpty`）按排除；普通类名（Notepad、Chrome_WidgetWin_1）放行。与简报 B 逐条一致。
- tests/TargetClassifierTests.cs 共 11 个用例覆盖上述全部分支（含大小写混合一例内 4 断言），已注册进 tests/Program.cs，复跑全 PASS。

### 2. HookManager（✅）
- **独立线程 + 消息循环**：Start 新建 `IsBackground=true` 专用线程，线程内依次 SetWindowsHookEx（鼠标 WH_MOUSE_LL + 键盘 WH_KEYBOARD_LL）后跑 GetMessage/Translate/Dispatch 循环，result<=0 退出并 Unhook。✅
- **委托强引用防 GC**：鼠标/键盘 HookProc 存于 HookSession 实例字段，句柄、线程、Timer 同在会话对象内成对管理；会话由 `_session` 与线程委托闭包双重持有，运行期不会被回收。✅
- **自身矩形先判放行**：WM_LBUTTONDOWN 回调先 `SelfBounds.Contains(pt)`，内→CallNextHookEx 放行返回；外部→记 PendingPoint、置 SwallowNextUp、返回 (IntPtr)1 吞 Down。✅
- **外部 Down/Up 成对吞**：WM_LBUTTONUP 仅在 SwallowNextUp 时吞掉、清标志，并以 **Down 点坐标** 投递 ExternalClick（Up 吞后才投递）。回调内除 PtrToStructure 取坐标、Contains 判定与 PostEvent 投递外无重活、无同步等待。✅
- **右键与全局 Esc**：WM_RBUTTONDOWN 不判位置（任意位置含自身）一律吞掉并投递 CancelRequested；键盘回调 WM_KEYDOWN 且 vkCode==VK_ESCAPE（不论焦点）吞掉并投递 CancelRequested。✅
- **30 秒超时自停**：Start 成功后起固定 30s `System.Threading.Timer`（一次性、不滑动续期，全文无重置计时代码；Start 重入仅更新 SelfBounds 不重置计时，与「进入等待后固定 30 秒」口径一致）；到期 OnTimeoutTimer 先 Stop() 自停（IsRunning 随 TeardownRequested 变 false）再投递 TimeoutElapsed。✅
- **Stop 成对卸载且异常路径释放**：Stop 幂等（TeardownRequested 守卫）；FinalizeTeardown 投 WM_QUIT→Join(500)→超时兜底从外部 Unhook 再 Join(500)，钩子在消息循环退出处亦 Unhook，双路均把句柄清零防重复卸载；HookThreadMain 挂钩中途失败时 catch 内先 Unhook 已挂钩子、置 StartError 再 Set Ready，Start 收到失败后 Stop 并抛 InvalidOperationException 给调用方。Dispose 先置 _disposed 再 Stop，可重复调用。✅
- **简报第 6 点（Down 已吞时 Stop 的 Up 收尾）已落实**：Stop 时若 SwallowNextUp 为真，不立即卸钩，起 200ms DeferredTeardownTimer 延迟收尾；配对 Up 先到则在 Up 回调内吞掉后由回调线程 FinalizeTeardown(fromHookThread=true)（只投 WM_QUIT、不 Join 自己）；200ms 到仍无 Up 则定时器强制收尾。代码注释已写明此口径。迟到的 ExternalClick 由 MainForm 的 State==Waiting 守卫忽略（见下）。✅
- **事件投递**：一律经 Start 时捕获的 SynchronizationContext.Post（只投递不等待）；捕获不到时直接触发且类注释已注明由订阅方负责切线程，MainForm 三个处理器均另有 InvokeRequired→BeginInvoke 兜底。✅

### 3. WindowResolver（✅，顺序小差见 Minor M-2）
- 流程：WindowFromPoint→GetAncestor(GA_ROOT)，零句柄回退原 hwnd；新增 IsWindow 复核→Gone（简报未明列、属合理补强）；isSelf=（root==selfHwnd）与 GetClassName 结果送 TargetClassifier→Excluded；IsIconic→Minimized；其余 Ok 并取标题。Reason 口径与简报一致：Excluded 不在此处改状态、由 MainForm 保持 Waiting，Minimized 由 MainForm Cancel 回 Idle。✅
- StillValid：零句柄→false，否则 IsWindow && !IsIconic，供截图前复核。✅

### 4. MainForm 接线（✅，集中入口的小出入见 Minor M-1）
- **单一状态变化入口**：`OnStateChanged()` 按 State 决定 Waiting→Start(this.Bounds)、其余→Stop，并刷新视觉；设备按钮、CancelRequested、TimeoutElapsed、Ok（BeginCapture 后）、OnTargetResolved（CompleteCapture 后）均只改状态机后调它。Start 抛异常时 catch 内 Cancel 回 Idle + RefreshStateVisuals + Stop + 错误反馈，无钩子泄漏。✅
- **Waiting 禁拖**：OnDragMouseDown 在 Waiting 时直接 return（_dragging 不置位），Start 时取的 Bounds 在整个等待期有效。✅
- **ExternalClick 分支**：非 Waiting 直接忽略（迟到/异常路径守卫）；Excluded→提示「点到桌面/任务栏，已忽略，请点目标窗口」+ 保持 Waiting 并 Stop 后 Start 重启钩子（新一轮 30s）；Gone→提示已关闭 + 同样重启；Minimized→提示「该窗口已最小化，请先打开」+ Cancel 回 Idle（经 OnStateChanged 停钩）；Ok→BeginCapture 成功才继续、停钩后调接入点 `OnTargetResolved(result, CurrentRequestId)`，本任务占位反馈「已选中窗口：<标题>」并 CompleteCapture 回 Idle。✅
- **Cancel/Timeout**：均 Cancel 状态机 + OnStateChanged 停钩 + 对应反馈（超时文案「等待超时，已取消」）；钩子已自停时 Stop 幂等无副作用。✅
- **FormClosing 必停钩**：OnFormClosing 先 `_hooks.Stop()` 再 `_hooks.Dispose()`；且重写的 Dispose(bool) 亦 Dispose 钩子，重复释放有守卫。✅

### 5. NativeMethods 与约束（✅）
- 简报 A 所列 P/Invoke 全部齐备（另含实现所必需的 PostThreadMessage、GetMessage/TranslateMessage/DispatchMessage、GetModuleHandle、GetCurrentThreadId 与 WM_QUIT 常量）；签名口径：句柄/WPARAM/LPARAM 为 IntPtr，PostThreadMessage 的 wParam 与结构体指针大小字段（MSLLHOOKSTRUCT/KBDLLHOOKSTRUCT 的 dwExtraInfo、MSG 的 wParam）为 UIntPtr；POINT/RECT/MSLLHOOKSTRUCT/KBDLLHOOKSTRUCT/MSG 均 `[StructLayout(LayoutKind.Sequential)]`，字段顺序与 Win32 定义一致；RECT.ToRectangle、POINT.ToPoint 齐备；HookProc 为鼠标/键盘共用的单一委托签名。✅
- **C# 7.2 子集（R6）**：build.sh 以 `/p:LangVersion=7.2` 覆盖编译生产 + 测试工程，本次复跑退出 0，语言子集由编译器直接验证；diff 内仅匿名方法、对象初始化器、三元等旧语法，无 7.3 独有语法。✅
- **零第三方引用**：csproj 仅 System.* 框架程序集，本次 diff 未新增任何 Reference/PackageReference。✅

## 二、质量结论：通过（Critical 0 / Important 1 / Minor 4）

### Critical
无。

### Important
- **I-1 报告文件不完整且用例总数写错（文档缺陷，代码不受影响）**：work/task-3-report.md 在磁盘上仅 14 行，HookManager 交付物一条写到中途断句，最后一行即字面量「...[truncated 2769 chars]」——简报要求的 TDD 证据、用例总数构成、Windows 实测遗留问题等后续章节在文件中根本不存在（协调者转述的「报告称 128」在本审查所读到的文件里亦无从查证）。且 128 与实测不符（见第四节，确切总数 131，差 3 无出处）。要求实现方补全报告全文并把总数更正为 131。

### Minor
- **M-1 Excluded/Gone 两分支绕过集中入口**：OnHookExternalClick 的 Excluded 与 Gone 分支直接 `_hooks.Stop(); _hooks.Start(...)` 并各自复制一份约 12 行的重启 + 异常处理，未走 OnStateChanged（状态未变、无法直接套用可理解），与 OnStateChanged 注释「启停集中、三条路径都只调本方法」的表述不完全相符。功能正确（异常时 Cancel + OnStateChanged + 反馈，无泄漏），但重启逻辑已有两份副本，后续改启停策略易漏改一处；建议抽 `RestartHooksWhileWaiting()` 私有方法收拢。
- **M-2 WindowResolver 取标题顺序与简报 D 微差**：代码在 IsIconic 判定**之前**先 GetWindowTitle，简报流程为 IsIconic→Minimized 在前、取标题在后。结果差异仅 Minimized 结果会多带一个 Title，而 MainForm 最小化分支不使用 Title，无功能影响；仅记录口径出入。
- **M-3 迟到事件无会话代次校验**：HookManager 的事件不带会话标识，MainForm 仅以 State==Waiting 守卫迟到投递。极端时序下（Down 已吞后取消/超时、200ms 安全窗内经键盘激活设备按钮重入 Waiting、随后旧会话的配对 Up 到达），旧 Up 投递的 ExternalClick 可能被新 Waiting 会话当作新点选消费。物理上需「按住左键同时用键盘点设备按钮」，概率极低；Task 4 联调时可考虑给事件加 session/request 代次或在 Stop 时戳记失效点位。
- **M-4 Start 启动超时的理论泄漏窗口（Windows 无法验证）**：Ready.WaitOne(5000) 超时后 Stop→FinalizeTeardown 以 PostThreadMessage 退线程；若钩子线程此刻尚未建立消息队列，该投递可能失败，兜底为 Join 超时后外部 Unhook + 再 Join(500)（结果未再校验）。若钩子恰在兜底 Unhook 之后才装上并进入循环，理论上残留一个无主钩子线程。触发条件为钩子线程 5 秒未能完成挂钩，概率极低，列此备查，不要求本轮改。

## 三、无法验证（钩子运行时行为，须 Windows 实机验收，不计入上述结论）
- WH_MOUSE_LL / WH_KEYBOARD_LL 在实机的装钩成功率与吞键实效：外部 Down/Up 真被成对吞掉（目标窗口收不到一次完整点击）、自身矩形内点击真放行（设备按钮 Click/取消/切换照常）、任意位置右键与任意焦点 Esc 真被吞且只触发一次取消。
- SynchronizationContext 在实机 UI 线程上确为 WinForms 上下文、Post 真切回 UI 线程（本 Linux/Mono 环境 SynchronizationContext 与钩子均不可用）。
- 专用线程消息泵对低级钩子回调的实际派发、WM_QUIT 退出循环与 Unhook 配对实效、FinalizeTeardown 中 Join(500) 兜底路径、200ms 安全窗内「按住左键期间 Esc 取消、松开后 Up 仍被吞」的实景时序。
- WindowFromPoint→GetAncestor(GA_ROOT) 对真实窗口（含桌面 Progman/WorkerW、主/副屏任务栏、最小化窗口、子控件）的解析结果与 ClassName/Title 取值；物理像素坐标与窗体 Bounds 在高 DPI 下的对齐。
- 30 秒 Timer 的实机计时与「超时恰在 Down/Up 之间」的收尾；对高完整性级别（管理员）窗口的点击——低级钩子受 UIPI 约束可能看不到，须实机确认并记入验收表。
- Start 失败注入（钩子被安全软件拦截等）时 MainForm 回 Idle + 提示的实景路径。

## 四、计数对账（专节）：确切总数 = 131（运行时口径），报告「128」差 3、无出处

### 独立复跑证据
- 本审查独立执行 `bash build.sh` 一次：退出码 **0**；测试汇总为 **总数 131 / 通过 131 / 失败 0**，`[PASS]` 131 行、`[FAIL]` 0 行（Mono 控制台中文为乱码，数字与 PASS/FAIL 标记清晰可读）。与协调者亲跑的 131/131 一致。

### 计数单位与构成
- 计数单位是 **TestRunner.Run 的一次运行时调用 = 1 个用例**（TotalCount = PassedCount + FailedCount）。
- 源码中 `TestRunner.Run` 静态调用点共 **123** 处：AppConfig 6 + ConfigPathResolver 5 + ConfigStoreLock 2 + ConfigStore 8 + DeviceNameValidator 39 + PanelLayout 32 + PanelStateMachine 20 + TargetClassifier **11**（本任务新增，与其文件逐行点数一致）。
- 静态 123 与运行时 131 差 **+8**，来源唯一（与 Task 1/2 对账同一处）：`tests/DeviceNameValidatorTests.cs` 第 21–26 行有 1 处 Run 写在 `foreach` 循环内，对 9 个非法字符（\\ / : * ? " < > |）各执行一次，静态 1 处 → 运行时 9 条，净 +8。其余七个测试文件均无循环展开。
- 交叉验证：Task 3 前基线运行时 = 120（Task 2 关账口径），本任务新增 TargetClassifier 11 条，120 + 11 = **131**，与复跑完全一致。

### 「128」的结论
- 任何可复现口径都得不出 128：运行时 131、静态调用点 123、基线 120、新增 11，两两组合亦无 128；与实测差 **3**，在现行 HEAD 代码中找不到对应来源（疑为中间版本数字或误算）。且如 I-1 所述，磁盘上的报告文件本身被截断，该数字无法从报告原文复核。结论：**确切运行时总数为 131，全部通过；报告中的 128 应更正为 131。**

## 五、总评
规格 ✅ 通过；质量通过（Critical 0 / Important 1 / Minor 4）。代码层面简报 A–E 逐条落实，重点复核的成对吞 Up 收尾（简报第 6 点）、异常路径释放与集中启停均成立；唯一 Important 为报告文件截断不完整且总数误写 128（实测 131），属文档整改项，不阻塞 Task 4 接入，但须补全报告后归档。钩子运行时行为全部列入第三节，须在 Windows 实机验收时逐项销账。
