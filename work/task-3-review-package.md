# Review package Task 3: 4543554..HEAD
a3cd297 feat(stage-3): 选窗钩子（成对吞点击/Esc取消/超时）

 src/Capture/HookManager.cs      | 354 ++++++++++++++++++++++++++++++++++++++++
 src/Capture/TargetClassifier.cs |  35 ++++
 src/Capture/WindowResolver.cs   | 108 ++++++++++++
 src/MainForm.cs                 | 141 +++++++++++++++-
 src/Native/NativeMethods.cs     | 175 ++++++++++++++++++++
 tests/Program.cs                |   1 +
 tests/TargetClassifierTests.cs  |  72 ++++++++
 7 files changed, 885 insertions(+), 1 deletion(-)

diff --git a/src/Capture/HookManager.cs b/src/Capture/HookManager.cs
new file mode 100644
index 0000000..197f1e7
--- /dev/null
+++ b/src/Capture/HookManager.cs
@@ -0,0 +1,354 @@
+using System;
+using System.ComponentModel;
+using System.Drawing;
+using System.Runtime.InteropServices;
+using System.Threading;
+using InspectionWidget.Native;
+
+namespace InspectionWidget.Capture
+{
+    /// <summary>
+    /// 全局低级钩子管理（选窗期间运行）：钩子挂在专用线程上并跑消息循环，
+    /// 回调内只做坐标判定，事件一律投递（Start 时捕获的 SynchronizationContext，
+    /// 拿不到则直接触发、由订阅方负责切线程），不在回调里同步等待或做重活。
+    ///
+    /// 行为口径：
+    /// - 组件自身矩形内的左键按下放行（设备按钮 Click 照常工作）；
+    /// - 外部左键 Down/Up 成对吞掉，Up 吞掉后才投递 ExternalClick；
+    /// - 任意位置右键按下、任意焦点下 Esc 均吞掉并投递 CancelRequested；
+    /// - 进入等待后固定 30 秒超时（不滑动续期），到期先自停再投递 TimeoutElapsed。
+    /// </summary>
+    public sealed class HookManager : IDisposable
+    {
+        /// <summary>外部左键点击（物理像素坐标，取 Down 点）。在投递上下文上触发。</summary>
+        public event Action<Point> ExternalClick;
+
+        /// <summary>右键按下或 Esc 触发取消。在投递上下文上触发。</summary>
+        public event Action CancelRequested;
+
+        /// <summary>30 秒无操作超时（已自停）。在投递上下文上触发。</summary>
+        public event Action TimeoutElapsed;
+
+        private const int TimeoutMilliseconds = 30 * 1000;
+
+        /// <summary>Down 已吞、Stop 先到时，等配对 Up 或安全窗到后再卸钩的时长。</summary>
+        private const int PendingUpSafetyWindowMilliseconds = 200;
+
+        private readonly object _lock = new object();
+        private HookSession _session;
+        private int _nextSessionId;
+        private bool _disposed;
+
+        public bool IsRunning
+        {
+            get
+            {
+                lock (_lock)
+                {
+                    return _session != null && !_session.TeardownRequested;
+                }
+            }
+        }
+
+        /// <summary>一次钩子会话的全部状态；委托存于此对象字段强引用防 GC。</summary>
+        private sealed class HookSession
+        {
+            public int Id;
+            public Thread Thread;
+            public uint ThreadId;
+            public IntPtr MouseHook;
+            public IntPtr KeyboardHook;
+            public NativeMethods.HookProc MouseProc;
+            public NativeMethods.HookProc KeyboardProc;
+            public Rectangle SelfBounds;
+            public SynchronizationContext Context;
+            public Timer TimeoutTimer;
+            public Timer DeferredTeardownTimer;
+            public readonly ManualResetEvent Ready = new ManualResetEvent(false);
+            public Exception StartError;
+            public volatile bool SwallowNextUp;
+            public Point PendingPoint;
+            public volatile bool TeardownRequested;
+            public volatile bool TornDown;
+        }
+
+        /// <summary>
+        /// 在钩子线程上挂钩并跑消息循环。已在运行则只更新自身矩形
+        /// （不重置 30 秒超时：口径为进入等待态后固定 30 秒）。
+        /// 挂钩失败抛 InvalidOperationException，抛出前已清理已挂的钩子。
+        /// </summary>
+        public void Start(Rectangle selfBounds)
+        {
+            HookSession session;
+            lock (_lock)
+            {
+                if (_disposed)
+                    throw new ObjectDisposedException("HookManager");
+                if (_session != null && !_session.TeardownRequested)
+                {
+                    _session.SelfBounds = selfBounds;
+                    return;
+                }
+                session = new HookSession();
+                session.Id = ++_nextSessionId;
+                session.SelfBounds = selfBounds;
+                session.Context = SynchronizationContext.Current;
+                session.MouseProc = delegate(int nCode, IntPtr wParam, IntPtr lParam)
+                {
+                    return MouseHookCallback(session, nCode, wParam, lParam);
+                };
+                session.KeyboardProc = delegate(int nCode, IntPtr wParam, IntPtr lParam)
+                {
+                    return KeyboardHookCallback(session, nCode, wParam, lParam);
+                };
+                _session = session;
+            }
+
+            var thread = new Thread(delegate() { HookThreadMain(session); });
+            thread.IsBackground = true;
+            thread.Name = "InspectionWidget.HookThread";
+            session.Thread = thread;
+            thread.Start();
+
+            // 同步等挂钩结果：失败要让调用方（MainForm）当场知道并回 Idle 提示。
+            if (!session.Ready.WaitOne(5000) || session.StartError != null)
+            {
+                Exception error = session.StartError != null
+                    ? session.StartError
+                    : new InvalidOperationException("钩子线程启动超时。");
+                Stop();
+                throw new InvalidOperationException("选窗钩子启动失败：" + error.Message, error);
+            }
+
+            session.TimeoutTimer = new Timer(
+                delegate(object state) { OnTimeoutTimer(session); },
+                null, TimeoutMilliseconds, Timeout.Infinite);
+        }
+
+        /// <summary>成对卸载并停线程，幂等；未运行直接返回。</summary>
+        public void Stop()
+        {
+            HookSession session;
+            lock (_lock)
+            {
+                session = _session;
+                if (session == null || session.TeardownRequested)
+                    return;
+                session.TeardownRequested = true;
+                if (session.TimeoutTimer != null)
+                    session.TimeoutTimer.Dispose();
+            }
+
+            // 成对吞 Up 的收尾：Down 已被吞、配对 Up 还没到时不能立刻卸钩，
+            // 否则那次 Up 会漏给目标窗口（等于多点了一下）。此时延迟卸载：
+            // 等 Up 到达被吞（回调内完成收尾），或 200ms 安全窗到后强制收尾。
+            if (session.SwallowNextUp)
+            {
+                session.DeferredTeardownTimer = new Timer(
+                    delegate(object state) { FinalizeTeardown(session, false); },
+                    null, PendingUpSafetyWindowMilliseconds, Timeout.Infinite);
+                return;
+            }
+            FinalizeTeardown(session, false);
+        }
+
+        public void Dispose()
+        {
+            lock (_lock)
+            {
+                if (_disposed)
+                    return;
+                _disposed = true;
+            }
+            Stop();
+        }
+
+        // ---------- 钩子线程 ----------
+
+        private void HookThreadMain(HookSession session)
+        {
+            session.ThreadId = NativeMethods.GetCurrentThreadId();
+            try
+            {
+                IntPtr hMod = NativeMethods.GetModuleHandle(null);
+                session.MouseHook = NativeMethods.SetWindowsHookEx(
+                    NativeMethods.WH_MOUSE_LL, session.MouseProc, hMod, 0);
+                if (session.MouseHook == IntPtr.Zero)
+                    throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowsHookEx(WH_MOUSE_LL) 失败。");
+                session.KeyboardHook = NativeMethods.SetWindowsHookEx(
+                    NativeMethods.WH_KEYBOARD_LL, session.KeyboardProc, hMod, 0);
+                if (session.KeyboardHook == IntPtr.Zero)
+                    throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowsHookEx(WH_KEYBOARD_LL) 失败。");
+            }
+            catch (Exception ex)
+            {
+                // 异常路径成对释放：已挂上的先卸掉再回报失败。
+                Unhook(session);
+                session.StartError = ex;
+                session.Ready.Set();
+                return;
+            }
+            session.Ready.Set();
+
+            NativeMethods.MSG msg;
+            while (true)
+            {
+                int result = NativeMethods.GetMessage(out msg, IntPtr.Zero, 0, 0);
+                if (result <= 0) // 0=WM_QUIT，-1=出错，均退出循环收尾
+                    break;
+                NativeMethods.TranslateMessage(ref msg);
+                NativeMethods.DispatchMessage(ref msg);
+            }
+            Unhook(session);
+        }
+
+        private static void Unhook(HookSession session)
+        {
+            if (session.MouseHook != IntPtr.Zero)
+            {
+                NativeMethods.UnhookWindowsHookEx(session.MouseHook);
+                session.MouseHook = IntPtr.Zero;
+            }
+            if (session.KeyboardHook != IntPtr.Zero)
+            {
+                NativeMethods.UnhookWindowsHookEx(session.KeyboardHook);
+                session.KeyboardHook = IntPtr.Zero;
+            }
+        }
+
+        private void FinalizeTeardown(HookSession session, bool fromHookThread)
+        {
+            lock (_lock)
+            {
+                if (session.TornDown)
+                    return;
+                session.TornDown = true;
+                if (object.ReferenceEquals(_session, session))
+                    _session = null;
+                if (session.DeferredTeardownTimer != null)
+                    session.DeferredTeardownTimer.Dispose();
+                if (session.TimeoutTimer != null)
+                    session.TimeoutTimer.Dispose();
+            }
+
+            if (session.ThreadId != 0)
+                NativeMethods.PostThreadMessage(session.ThreadId, NativeMethods.WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
+
+            // 在钩子线程自身里（配对 Up 回调）不能 Join 自己：消息循环会在
+            // 本次回调返回后收到 WM_QUIT 退出并卸钩，这里只投递不等待。
+            if (fromHookThread)
+                return;
+
+            Thread thread = session.Thread;
+            if (thread != null && thread.IsAlive)
+            {
+                if (!thread.Join(500))
+                {
+                    // 兜底：线程未按时退出时从外部直接卸钩，再短等一次。
+                    Unhook(session);
+                    thread.Join(500);
+                }
+            }
+        }
+
+        // ---------- 回调（钩子线程上执行，只做坐标判定与投递） ----------
+
+        private IntPtr MouseHookCallback(HookSession session, int nCode, IntPtr wParam, IntPtr lParam)
+        {
+            if (nCode >= 0)
+            {
+                int message = wParam.ToInt32();
+                if (message == NativeMethods.WM_LBUTTONDOWN)
+                {
+                    var data = (NativeMethods.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.MSLLHOOKSTRUCT));
+                    Point pt = data.pt.ToPoint();
+                    if (session.SelfBounds.Contains(pt))
+                        return NativeMethods.CallNextHookEx(session.MouseHook, nCode, wParam, lParam);
+                    session.PendingPoint = pt;
+                    session.SwallowNextUp = true;
+                    return (IntPtr)1;
+                }
+                if (message == NativeMethods.WM_LBUTTONUP && session.SwallowNextUp)
+                {
+                    session.SwallowNextUp = false;
+                    Point pt = session.PendingPoint;
+                    PostEvent(session, delegate
+                    {
+                        Action<Point> handler = ExternalClick;
+                        if (handler != null)
+                            handler(pt);
+                    });
+                    if (session.TeardownRequested)
+                        FinalizeTeardown(session, true);
+                    return (IntPtr)1;
+                }
+                if (message == NativeMethods.WM_RBUTTONDOWN)
+                {
+                    // 口径：任意位置右键都算取消（含组件自身）。
+                    PostEvent(session, delegate
+                    {
+                        Action handler = CancelRequested;
+                        if (handler != null)
+                            handler();
+                    });
+                    return (IntPtr)1;
+                }
+            }
+            return NativeMethods.CallNextHookEx(session.MouseHook, nCode, wParam, lParam);
+        }
+
+        private IntPtr KeyboardHookCallback(HookSession session, int nCode, IntPtr wParam, IntPtr lParam)
+        {
+            if (nCode >= 0 && wParam.ToInt32() == NativeMethods.WM_KEYDOWN)
+            {
+                var data = (NativeMethods.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.KBDLLHOOKSTRUCT));
+                if (data.vkCode == NativeMethods.VK_ESCAPE)
+                {
+                    PostEvent(session, delegate
+                    {
+                        Action handler = CancelRequested;
+                        if (handler != null)
+                            handler();
+                    });
+                    return (IntPtr)1;
+                }
+            }
+            return NativeMethods.CallNextHookEx(session.KeyboardHook, nCode, wParam, lParam);
+        }
+
+        // ---------- 超时 ----------
+
+        private void OnTimeoutTimer(HookSession session)
+        {
+            lock (_lock)
+            {
+                if (!object.ReferenceEquals(_session, session) || session.TeardownRequested)
+                    return;
+            }
+            Stop();
+            PostEvent(session, delegate
+            {
+                Action handler = TimeoutElapsed;
+                if (handler != null)
+                    handler();
+            });
+        }
+
+        /// <summary>
+        /// 事件投递：优先走 Start 时捕获的 SynchronizationContext（只 Post 不等待）；
+        /// 捕获不到时直接在当前线程触发，由订阅方负责切线程。
+        /// </summary>
+        private static void PostEvent(HookSession session, Action action)
+        {
+            SynchronizationContext context = session.Context;
+            if (context != null)
+            {
+                context.Post(delegate(object state) { action(); }, null);
+            }
+            else
+            {
+                action();
+            }
+        }
+    }
+}
diff --git a/src/Capture/TargetClassifier.cs b/src/Capture/TargetClassifier.cs
new file mode 100644
index 0000000..3bc47ee
--- /dev/null
+++ b/src/Capture/TargetClassifier.cs
@@ -0,0 +1,35 @@
+using System;
+
+namespace InspectionWidget.Capture
+{
+    /// <summary>
+    /// 选窗目标分类（纯逻辑）：判断点中的顶层窗口是否应被排除（不作为截图目标）。
+    /// 排除口径：组件自身恒排除；桌面（Progman/WorkerW）与任务栏
+    /// （Shell_TrayWnd/Shell_SecondaryTrayWnd）排除，类名大小写不敏感；
+    /// 类名查不到（null/空）按排除处理——宁可让用户重点，也不能截错窗口。
+    /// </summary>
+    public static class TargetClassifier
+    {
+        private static readonly string[] ExcludedClassNames =
+        {
+            "Progman",
+            "WorkerW",
+            "Shell_TrayWnd",
+            "Shell_SecondaryTrayWnd"
+        };
+
+        public static bool IsExcluded(string className, bool isSelf)
+        {
+            if (isSelf)
+                return true;
+            if (string.IsNullOrEmpty(className))
+                return true;
+            for (int i = 0; i < ExcludedClassNames.Length; i++)
+            {
+                if (string.Equals(className, ExcludedClassNames[i], StringComparison.OrdinalIgnoreCase))
+                    return true;
+            }
+            return false;
+        }
+    }
+}
diff --git a/src/Capture/WindowResolver.cs b/src/Capture/WindowResolver.cs
new file mode 100644
index 0000000..b83c5aa
--- /dev/null
+++ b/src/Capture/WindowResolver.cs
@@ -0,0 +1,108 @@
+using System;
+using System.Drawing;
+using System.Text;
+using InspectionWidget.Native;
+
+namespace InspectionWidget.Capture
+{
+    /// <summary>选窗解析结果分类。</summary>
+    public enum ResolveReason
+    {
+        Ok,
+        Excluded,
+        Minimized,
+        Gone
+    }
+
+    /// <summary>选窗解析结果：标题只作提示用，Hwnd 为顶层窗口句柄。</summary>
+    public sealed class ResolveResult
+    {
+        public IntPtr Hwnd;
+        public string Title;
+        public string ClassName;
+        public ResolveReason Reason;
+    }
+
+    /// <summary>
+    /// 由屏幕坐标反查顶层窗口：WindowFromPoint → GetAncestor(GA_ROOT) →
+    /// 自身/桌面/任务栏排除 → 最小化判定 → 取标题（只作提示）。
+    /// 本类含 P/Invoke，只能在 Windows 实机运行；判定口径复用纯逻辑 TargetClassifier。
+    /// </summary>
+    public static class WindowResolver
+    {
+        public static ResolveResult ResolveTopLevel(Point pt, IntPtr selfHwnd)
+        {
+            var result = new ResolveResult
+            {
+                Hwnd = IntPtr.Zero,
+                Title = string.Empty,
+                ClassName = null,
+                Reason = ResolveReason.Gone
+            };
+
+            var nativePoint = new NativeMethods.POINT { X = pt.X, Y = pt.Y };
+            IntPtr hwnd = NativeMethods.WindowFromPoint(nativePoint);
+            if (hwnd == IntPtr.Zero)
+                return result;
+
+            IntPtr root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
+            if (root == IntPtr.Zero)
+                root = hwnd;
+            result.Hwnd = root;
+
+            if (!NativeMethods.IsWindow(root))
+            {
+                result.Reason = ResolveReason.Gone;
+                return result;
+            }
+
+            bool isSelf = root == selfHwnd;
+            string className = GetClassNameText(root);
+            result.ClassName = className;
+            if (TargetClassifier.IsExcluded(className, isSelf))
+            {
+                result.Reason = ResolveReason.Excluded;
+                return result;
+            }
+
+            result.Title = GetWindowTitle(root);
+            if (NativeMethods.IsIconic(root))
+            {
+                result.Reason = ResolveReason.Minimized;
+                return result;
+            }
+
+            result.Reason = ResolveReason.Ok;
+            return result;
+        }
+
+        /// <summary>截图前复核：句柄仍是有效窗口且未被最小化。</summary>
+        public static bool StillValid(IntPtr hwnd)
+        {
+            if (hwnd == IntPtr.Zero)
+                return false;
+            return NativeMethods.IsWindow(hwnd) && !NativeMethods.IsIconic(hwnd);
+        }
+
+        private static string GetClassNameText(IntPtr hwnd)
+        {
+            var builder = new StringBuilder(256);
+            int length = NativeMethods.GetClassName(hwnd, builder, builder.Capacity);
+            if (length <= 0)
+                return null;
+            return builder.ToString();
+        }
+
+        private static string GetWindowTitle(IntPtr hwnd)
+        {
+            int length = NativeMethods.GetWindowTextLength(hwnd);
+            if (length <= 0)
+                return string.Empty;
+            var builder = new StringBuilder(length + 1);
+            int copied = NativeMethods.GetWindowText(hwnd, builder, builder.Capacity);
+            if (copied <= 0)
+                return string.Empty;
+            return builder.ToString();
+        }
+    }
+}
diff --git a/src/MainForm.cs b/src/MainForm.cs
index 88a9471..9d93b4d 100644
--- a/src/MainForm.cs
+++ b/src/MainForm.cs
@@ -32,10 +32,11 @@ namespace InspectionWidget
         private const int SideMargin = 10;
         private const int ContentWidth = PanelLayout.PanelWidth - SideMargin * 2; // 210
 
         private readonly ConfigStore _store;
         private readonly PanelStateMachine _stateMachine;
+        private readonly HookManager _hooks;
         private AppConfig _config;
 
         // 设备名单校验错误（手改 config 造成）：非 null 时常驻提示并禁用设备按钮
         private string _deviceListError;
         private bool _rootPromptShown;
@@ -94,10 +95,11 @@ namespace InspectionWidget
 
         protected override void Dispose(bool disposing)
         {
             if (disposing)
             {
+                if (_hooks != null) _hooks.Dispose();
                 if (_font13 != null) _font13.Dispose();
                 if (_font13Bold != null) _font13Bold.Dispose();
                 if (_font11 != null) _font11.Dispose();
             }
             base.Dispose(disposing);
@@ -105,10 +107,14 @@ namespace InspectionWidget
 
         public MainForm()
         {
             _store = new ConfigStore();
             _stateMachine = new PanelStateMachine();
+            _hooks = new HookManager();
+            _hooks.ExternalClick += OnHookExternalClick;
+            _hooks.CancelRequested += OnHookCancelRequested;
+            _hooks.TimeoutElapsed += OnHookTimeoutElapsed;
 
             Text = "巡检截图";
             FormBorderStyle = FormBorderStyle.None;
             TopMost = true;
             ShowInTaskbar = true;
@@ -567,11 +573,11 @@ namespace InspectionWidget
             if (_stateMachine.State == PanelState.Waiting)
             {
                 _config.LastDevice = _stateMachine.CurrentDevice;
                 SaveBestEffort();
             }
-            RefreshStateVisuals();
+            OnStateChanged();
         }
 
         private void OnCredentialHeaderClicked(object sender, EventArgs e)
         {
             _config.IsCredentialCollapsed = !_config.IsCredentialCollapsed;
@@ -662,10 +668,139 @@ namespace InspectionWidget
             }
 
             Invalidate();
         }
 
+        // ---------- 选窗钩子接线（Task 3） ----------
+
+        /// <summary>
+        /// 状态变化集中点：刷新视觉，并按状态统一启停钩子（Waiting→Start，其余→Stop）。
+        /// 设备按钮、ExternalClick、Cancel 三条路径都只改状态机再调本方法，防钩子泄漏。
+        /// </summary>
+        private void OnStateChanged()
+        {
+            RefreshStateVisuals();
+            if (_stateMachine.State == PanelState.Waiting)
+            {
+                try
+                {
+                    // Bounds 随拖动变化，Start 时取当前值；等待态期间禁用拖动（见
+                    // OnDragMouseDown），保证 selfBounds 在整个等待期有效。
+                    _hooks.Start(this.Bounds);
+                }
+                catch (Exception ex)
+                {
+                    _stateMachine.Cancel();
+                    RefreshStateVisuals();
+                    _hooks.Stop();
+                    ShowFeedback("选窗启动失败：" + ex.Message, true);
+                }
+            }
+            else
+            {
+                _hooks.Stop();
+            }
+        }
+
+        /// <summary>
+        /// 外部点击选窗：先解析目标再决定状态转移——只有命中有效窗口才
+        /// BeginCapture（状态机无 Capturing→Waiting 转移，排除分支必须留在 Waiting）。
+        /// </summary>
+        private void OnHookExternalClick(Point pt)
+        {
+            if (InvokeRequired)
+            {
+                BeginInvoke(new Action<Point>(OnHookExternalClick), pt);
+                return;
+            }
+            if (_stateMachine.State != PanelState.Waiting)
+                return;
+
+            ResolveResult result = WindowResolver.ResolveTopLevel(pt, this.Handle);
+            switch (result.Reason)
+            {
+                case ResolveReason.Excluded:
+                    ShowFeedback("点到桌面/任务栏，已忽略，请点目标窗口", true);
+                    // 保持 Waiting 并重启钩子（新一轮 30 秒超时）
+                    _hooks.Stop();
+                    try
+                    {
+                        _hooks.Start(this.Bounds);
+                    }
+                    catch (Exception ex)
+                    {
+                        _stateMachine.Cancel();
+                        OnStateChanged();
+                        ShowFeedback("选窗启动失败：" + ex.Message, true);
+                    }
+                    break;
+                case ResolveReason.Minimized:
+                    ShowFeedback("该窗口已最小化，请先打开", true);
+                    _stateMachine.Cancel();
+                    OnStateChanged();
+                    break;
+                case ResolveReason.Ok:
+                    if (!_stateMachine.BeginCapture())
+                        return;
+                    OnStateChanged(); // Capturing → 停钩
+                    OnTargetResolved(result, _stateMachine.CurrentRequestId);
+                    break;
+                default: // Gone
+                    ShowFeedback("该窗口已关闭，请重新点选", true);
+                    _hooks.Stop();
+                    try
+                    {
+                        _hooks.Start(this.Bounds);
+                    }
+                    catch (Exception ex)
+                    {
+                        _stateMachine.Cancel();
+                        OnStateChanged();
+                        ShowFeedback("选窗启动失败：" + ex.Message, true);
+                    }
+                    break;
+            }
+        }
+
+        private void OnHookCancelRequested()
+        {
+            if (InvokeRequired)
+            {
+                BeginInvoke(new Action(OnHookCancelRequested));
+                return;
+            }
+            bool cancelled = _stateMachine.Cancel();
+            OnStateChanged();
+            if (cancelled)
+                ShowFeedback("已取消选窗", false);
+        }
+
+        private void OnHookTimeoutElapsed()
+        {
+            if (InvokeRequired)
+            {
+                BeginInvoke(new Action(OnHookTimeoutElapsed));
+                return;
+            }
+            bool cancelled = _stateMachine.Cancel();
+            OnStateChanged(); // 钩子已自停，Stop 幂等
+            if (cancelled)
+                ShowFeedback("等待超时，已取消", true);
+        }
+
+        /// <summary>
+        /// Task 4 接入点：真实截图引擎以 (result.Hwnd, requestId) 为输入在此启动，
+        /// 完成后 CompleteCapture / 失败 FailCapture 回 Idle。本任务先占位：
+        /// 反馈已选中窗口标题并直接回 Idle。
+        /// </summary>
+        private void OnTargetResolved(ResolveResult result, int requestId)
+        {
+            ShowSuccessFeedback("已选中窗口：" + (string.IsNullOrEmpty(result.Title) ? "（无标题）" : result.Title));
+            _stateMachine.CompleteCapture();
+            OnStateChanged();
+        }
+
         protected override void OnPaint(PaintEventArgs e)
         {
             base.OnPaint(e);
             bool waiting = _stateMachine.State == PanelState.Waiting;
             if (waiting)
@@ -768,10 +903,12 @@ namespace InspectionWidget
         }
 
         private void OnDragMouseDown(object sender, MouseEventArgs e)
         {
             if (e.Button != MouseButtons.Left) return;
+            // 等待态禁用拖动：钩子以 Start 时的 Bounds 判自身矩形，拖动会使其失效。
+            if (_stateMachine.State == PanelState.Waiting) return;
             var control = sender as Control;
             if (control == null) return;
             _dragging = true;
             // _dragOffset 必须换算为相对窗体的坐标：发送者可能是标题栏内的标签
             // （titleLabel 在 (10,9)、dateLabel 在 (82,11)），e.X/e.Y 是相对发送者
@@ -803,10 +940,12 @@ namespace InspectionWidget
             SaveBestEffort();
         }
 
         private void OnFormClosing(object sender, FormClosingEventArgs e)
         {
+            _hooks.Stop();
+            _hooks.Dispose();
             SaveWindowPosition();
         }
 
         // ---------- 保存（BestEffort） ----------
 
diff --git a/src/Native/NativeMethods.cs b/src/Native/NativeMethods.cs
new file mode 100644
index 0000000..9798108
--- /dev/null
+++ b/src/Native/NativeMethods.cs
@@ -0,0 +1,175 @@
+using System;
+using System.Drawing;
+using System.Runtime.InteropServices;
+using System.Text;
+
+namespace InspectionWidget.Native
+{
+    /// <summary>
+    /// 全部 P/Invoke 集中声明（本任务先建钩子/选窗所需，后续任务往里加）。
+    /// 签名口径：句柄与指针一律 IntPtr，WPARAM/LPARAM 相关一律 IntPtr，
+    /// 结构体内指针大小相关字段用 UIntPtr。
+    /// </summary>
+    internal static class NativeMethods
+    {
+        // ---------- 常量 ----------
+
+        public const int WH_MOUSE_LL = 14;
+        public const int WH_KEYBOARD_LL = 13;
+
+        public const int WM_LBUTTONDOWN = 0x0201;
+        public const int WM_LBUTTONUP = 0x0202;
+        public const int WM_RBUTTONDOWN = 0x0204;
+        public const int WM_KEYDOWN = 0x0100;
+        public const int WM_QUIT = 0x0012;
+
+        public const int VK_ESCAPE = 0x1B;
+
+        /// <summary>GetAncestor 标志：取根窗口（顶层窗口）。</summary>
+        public const uint GA_ROOT = 2;
+
+        // ---------- 结构体 ----------
+
+        [StructLayout(LayoutKind.Sequential)]
+        public struct POINT
+        {
+            public int X;
+            public int Y;
+
+            public Point ToPoint()
+            {
+                return new Point(X, Y);
+            }
+        }
+
+        [StructLayout(LayoutKind.Sequential)]
+        public struct RECT
+        {
+            public int Left;
+            public int Top;
+            public int Right;
+            public int Bottom;
+
+            public Rectangle ToRectangle()
+            {
+                return Rectangle.FromLTRB(Left, Top, Right, Bottom);
+            }
+        }
+
+        /// <summary>低级鼠标钩子数据（MSLLHOOKSTRUCT）。</summary>
+        [StructLayout(LayoutKind.Sequential)]
+        public struct MSLLHOOKSTRUCT
+        {
+            public POINT pt;
+            public uint mouseData;
+            public uint flags;
+            public uint time;
+            public UIntPtr dwExtraInfo;
+        }
+
+        /// <summary>低级键盘钩子数据（KBDLLHOOKSTRUCT）。</summary>
+        [StructLayout(LayoutKind.Sequential)]
+        public struct KBDLLHOOKSTRUCT
+        {
+            public uint vkCode;
+            public uint scanCode;
+            public uint flags;
+            public uint time;
+            public UIntPtr dwExtraInfo;
+        }
+
+        /// <summary>消息循环用 MSG。</summary>
+        [StructLayout(LayoutKind.Sequential)]
+        public struct MSG
+        {
+            public IntPtr hwnd;
+            public uint message;
+            public UIntPtr wParam;
+            public IntPtr lParam;
+            public uint time;
+            public POINT pt;
+        }
+
+        // ---------- 委托 ----------
+
+        /// <summary>
+        /// 低级钩子回调签名（鼠标/键盘共用）。实例必须被强引用持有，
+        /// 否则会被 GC 回收导致回调时崩溃（HookManager 以会话字段持有）。
+        /// </summary>
+        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
+
+        // ---------- user32：钩子与消息循环 ----------
+
+        [DllImport("user32.dll", SetLastError = true)]
+        public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
+
+        [DllImport("user32.dll", SetLastError = true)]
+        [return: MarshalAs(UnmanagedType.Bool)]
+        public static extern bool UnhookWindowsHookEx(IntPtr hhk);
+
+        [DllImport("user32.dll")]
+        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
+
+        [DllImport("user32.dll", SetLastError = true)]
+        [return: MarshalAs(UnmanagedType.Bool)]
+        public static extern bool PostThreadMessage(uint idThread, uint msg, UIntPtr wParam, IntPtr lParam);
+
+        [DllImport("user32.dll", SetLastError = true)]
+        public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
+
+        [DllImport("user32.dll")]
+        [return: MarshalAs(UnmanagedType.Bool)]
+        public static extern bool TranslateMessage(ref MSG lpMsg);
+
+        [DllImport("user32.dll")]
+        public static extern IntPtr DispatchMessage(ref MSG lpMsg);
+
+        // ---------- user32：窗口查询与操作 ----------
+
+        [DllImport("user32.dll")]
+        public static extern IntPtr WindowFromPoint(POINT point);
+
+        [DllImport("user32.dll")]
+        public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);
+
+        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
+        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
+
+        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
+        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
+
+        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
+        public static extern int GetWindowTextLength(IntPtr hWnd);
+
+        [DllImport("user32.dll")]
+        [return: MarshalAs(UnmanagedType.Bool)]
+        public static extern bool IsIconic(IntPtr hWnd);
+
+        [DllImport("user32.dll")]
+        [return: MarshalAs(UnmanagedType.Bool)]
+        public static extern bool IsWindow(IntPtr hWnd);
+
+        [DllImport("user32.dll", SetLastError = true)]
+        [return: MarshalAs(UnmanagedType.Bool)]
+        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
+
+        [DllImport("user32.dll")]
+        [return: MarshalAs(UnmanagedType.Bool)]
+        public static extern bool SetForegroundWindow(IntPtr hWnd);
+
+        [DllImport("user32.dll", SetLastError = true)]
+        [return: MarshalAs(UnmanagedType.Bool)]
+        public static extern bool BringWindowToTop(IntPtr hWnd);
+
+        [DllImport("user32.dll")]
+        public static extern IntPtr GetForegroundWindow();
+
+        // ---------- kernel32 ----------
+
+        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
+        public static extern IntPtr GetModuleHandle(string lpModuleName);
+
+        [DllImport("kernel32.dll")]
+        public static extern uint GetCurrentThreadId();
+    }
+}
diff --git a/tests/Program.cs b/tests/Program.cs
index 5564c5e..d5b4676 100644
--- a/tests/Program.cs
+++ b/tests/Program.cs
@@ -11,10 +11,11 @@ namespace InspectionWidget.Tests
             ConfigStoreTests.RunAll();
             ConfigPathResolverTests.RunAll();
             ConfigStoreLockTests.RunAll();
             PanelLayoutTests.RunAll();
             PanelStateMachineTests.RunAll();
+            TargetClassifierTests.RunAll();
 
             Console.WriteLine("----------------------------------------");
             Console.WriteLine("用例总数：" + TestRunner.TotalCount
                 + "，通过：" + TestRunner.PassedCount
                 + "，失败：" + TestRunner.FailedCount);
diff --git a/tests/TargetClassifierTests.cs b/tests/TargetClassifierTests.cs
new file mode 100644
index 0000000..dffb370
--- /dev/null
+++ b/tests/TargetClassifierTests.cs
@@ -0,0 +1,72 @@
+using InspectionWidget.Capture;
+
+namespace InspectionWidget.Tests
+{
+    /// <summary>
+    /// TargetClassifier 排除判定：isSelf 恒排除、桌面/任务栏类名命中（大小写不敏感）、
+    /// 类名查不到（null/空）按排除处理、普通窗口类名放行。
+    /// </summary>
+    public static class TargetClassifierTests
+    {
+        public static void RunAll()
+        {
+            TestRunner.Run("分类器：isSelf=true 时普通类名也恒排除", () =>
+            {
+                Assert.True(TargetClassifier.IsExcluded("Notepad", true));
+            });
+
+            TestRunner.Run("分类器：isSelf=true 且类名为 null 也恒排除", () =>
+            {
+                Assert.True(TargetClassifier.IsExcluded(null, true));
+            });
+
+            TestRunner.Run("分类器：Progman（桌面）排除", () =>
+            {
+                Assert.True(TargetClassifier.IsExcluded("Progman", false));
+            });
+
+            TestRunner.Run("分类器：WorkerW（桌面）排除", () =>
+            {
+                Assert.True(TargetClassifier.IsExcluded("WorkerW", false));
+            });
+
+            TestRunner.Run("分类器：Shell_TrayWnd（任务栏）排除", () =>
+            {
+                Assert.True(TargetClassifier.IsExcluded("Shell_TrayWnd", false));
+            });
+
+            TestRunner.Run("分类器：Shell_SecondaryTrayWnd（副屏任务栏）排除", () =>
+            {
+                Assert.True(TargetClassifier.IsExcluded("Shell_SecondaryTrayWnd", false));
+            });
+
+            TestRunner.Run("分类器：类名匹配大小写不敏感", () =>
+            {
+                Assert.True(TargetClassifier.IsExcluded("progman", false));
+                Assert.True(TargetClassifier.IsExcluded("WORKERW", false));
+                Assert.True(TargetClassifier.IsExcluded("shell_traywnd", false));
+                Assert.True(TargetClassifier.IsExcluded("SHELL_SECONDARYTRAYWND", false));
+            });
+
+            TestRunner.Run("分类器：类名为 null 按排除处理", () =>
+            {
+                Assert.True(TargetClassifier.IsExcluded(null, false));
+            });
+
+            TestRunner.Run("分类器：类名为空字符串按排除处理", () =>
+            {
+                Assert.True(TargetClassifier.IsExcluded(string.Empty, false));
+            });
+
+            TestRunner.Run("分类器：普通类名 Notepad 不排除", () =>
+            {
+                Assert.False(TargetClassifier.IsExcluded("Notepad", false));
+            });
+
+            TestRunner.Run("分类器：普通类名 Chrome_WidgetWin_1 不排除", () =>
+            {
+                Assert.False(TargetClassifier.IsExcluded("Chrome_WidgetWin_1", false));
+            });
+        }
+    }
+}
