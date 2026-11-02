using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using InspectionWidget.Native;

namespace InspectionWidget.Capture
{
    /// <summary>
    /// 全局低级钩子管理（选窗期间运行）：钩子挂在专用线程上并跑消息循环，
    /// 回调内只做坐标判定，事件一律投递（Start 时捕获的 SynchronizationContext，
    /// 拿不到则直接触发、由订阅方负责切线程），不在回调里同步等待或做重活。
    ///
    /// 行为口径：
    /// - 组件自身矩形内的左键按下放行（设备按钮 Click 照常工作）；
    /// - 外部左键 Down/Up 成对吞掉，Up 吞掉后才投递 ExternalClick；
    /// - 任意位置右键按下、任意焦点下 Esc 均吞掉并投递 CancelRequested；
    /// - 进入等待后固定 30 秒超时（不滑动续期），到期先自停再投递 TimeoutElapsed。
    /// </summary>
    public sealed class HookManager : IDisposable
    {
        /// <summary>
        /// 外部左键点击（物理像素坐标，取 Down 点；第二参数为事件所属钩子会话号，
        /// 供消费方做代次守卫：上一会话的迟到事件须丢弃）。在投递上下文上触发。
        /// </summary>
        public event Action<Point, int> ExternalClick;

        /// <summary>右键按下或 Esc 触发取消。在投递上下文上触发。</summary>
        public event Action CancelRequested;

        /// <summary>30 秒无操作超时（已自停）。在投递上下文上触发。</summary>
        public event Action TimeoutElapsed;

        private const int TimeoutMilliseconds = 30 * 1000;

        /// <summary>Down 已吞、Stop 先到时，等配对 Up 或安全窗到后再卸钩的时长。</summary>
        private const int PendingUpSafetyWindowMilliseconds = 200;

        private readonly object _lock = new object();
        private HookSession _session;
        private int _nextSessionId;
        private bool _disposed;

        public bool IsRunning
        {
            get
            {
                lock (_lock)
                {
                    return _session != null && !_session.TeardownRequested;
                }
            }
        }

        /// <summary>一次钩子会话的全部状态；委托存于此对象字段强引用防 GC。</summary>
        private sealed class HookSession
        {
            public int Id;
            public Thread Thread;
            public uint ThreadId;
            public IntPtr MouseHook;
            public IntPtr KeyboardHook;
            public NativeMethods.HookProc MouseProc;
            public NativeMethods.HookProc KeyboardProc;
            public Rectangle SelfBounds;
            public SynchronizationContext Context;
            public Timer TimeoutTimer;
            public Timer DeferredTeardownTimer;
            public readonly ManualResetEvent Ready = new ManualResetEvent(false);
            public Exception StartError;
            public volatile bool SwallowNextUp;
            public Point PendingPoint;
            public volatile bool TeardownRequested;
            public volatile bool TornDown;
        }

        /// <summary>
        /// 在钩子线程上挂钩并跑消息循环。已在运行则只更新自身矩形
        /// （不重置 30 秒超时：口径为进入等待态后固定 30 秒）。
        /// 挂钩失败抛 InvalidOperationException，抛出前已清理已挂的钩子。
        /// 返回本次钩子会话号：已在运行时返回现行会话号（不新建会话），
        /// 消费方据此对 ExternalClick 做代次守卫。
        /// </summary>
        public int Start(Rectangle selfBounds)
        {
            HookSession session;
            lock (_lock)
            {
                if (_disposed)
                    throw new ObjectDisposedException("HookManager");
                if (_session != null && !_session.TeardownRequested)
                {
                    _session.SelfBounds = selfBounds;
                    return _session.Id;
                }
                session = new HookSession();
                session.Id = ++_nextSessionId;
                session.SelfBounds = selfBounds;
                session.Context = SynchronizationContext.Current;
                session.MouseProc = delegate(int nCode, IntPtr wParam, IntPtr lParam)
                {
                    return MouseHookCallback(session, nCode, wParam, lParam);
                };
                session.KeyboardProc = delegate(int nCode, IntPtr wParam, IntPtr lParam)
                {
                    return KeyboardHookCallback(session, nCode, wParam, lParam);
                };
                _session = session;
            }

            var thread = new Thread(delegate() { HookThreadMain(session); });
            thread.IsBackground = true;
            thread.Name = "InspectionWidget.HookThread";
            session.Thread = thread;
            thread.Start();

            // 同步等挂钩结果：失败要让调用方（MainForm）当场知道并回 Idle 提示。
            if (!session.Ready.WaitOne(5000) || session.StartError != null)
            {
                Exception error = session.StartError != null
                    ? session.StartError
                    : new InvalidOperationException("钩子线程启动超时。");
                Stop();
                throw new InvalidOperationException("选窗钩子启动失败：" + error.Message, error);
            }

            session.TimeoutTimer = new Timer(
                delegate(object state) { OnTimeoutTimer(session); },
                null, TimeoutMilliseconds, Timeout.Infinite);
            return session.Id;
        }

        /// <summary>成对卸载并停线程，幂等；未运行直接返回。</summary>
        public void Stop()
        {
            HookSession session;
            lock (_lock)
            {
                session = _session;
                if (session == null || session.TeardownRequested)
                    return;
                session.TeardownRequested = true;
                if (session.TimeoutTimer != null)
                    session.TimeoutTimer.Dispose();
            }

            // 成对吞 Up 的收尾：Down 已被吞、配对 Up 还没到时不能立刻卸钩，
            // 否则那次 Up 会漏给目标窗口（等于多点了一下）。此时延迟卸载：
            // 等 Up 到达被吞（回调内完成收尾），或 200ms 安全窗到后强制收尾。
            if (session.SwallowNextUp)
            {
                session.DeferredTeardownTimer = new Timer(
                    delegate(object state) { FinalizeTeardown(session, false); },
                    null, PendingUpSafetyWindowMilliseconds, Timeout.Infinite);
                return;
            }
            FinalizeTeardown(session, false);
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed)
                    return;
                _disposed = true;
            }
            Stop();
        }

        // ---------- 钩子线程 ----------

        private void HookThreadMain(HookSession session)
        {
            session.ThreadId = NativeMethods.GetCurrentThreadId();
            try
            {
                IntPtr hMod = NativeMethods.GetModuleHandle(null);
                session.MouseHook = NativeMethods.SetWindowsHookEx(
                    NativeMethods.WH_MOUSE_LL, session.MouseProc, hMod, 0);
                if (session.MouseHook == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowsHookEx(WH_MOUSE_LL) 失败。");
                session.KeyboardHook = NativeMethods.SetWindowsHookEx(
                    NativeMethods.WH_KEYBOARD_LL, session.KeyboardProc, hMod, 0);
                if (session.KeyboardHook == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowsHookEx(WH_KEYBOARD_LL) 失败。");
            }
            catch (Exception ex)
            {
                // 异常路径成对释放：已挂上的先卸掉再回报失败。
                Unhook(session);
                session.StartError = ex;
                session.Ready.Set();
                return;
            }
            session.Ready.Set();

            NativeMethods.MSG msg;
            while (true)
            {
                int result = NativeMethods.GetMessage(out msg, IntPtr.Zero, 0, 0);
                if (result <= 0) // 0=WM_QUIT，-1=出错，均退出循环收尾
                    break;
                NativeMethods.TranslateMessage(ref msg);
                NativeMethods.DispatchMessage(ref msg);
            }
            Unhook(session);
        }

        private static void Unhook(HookSession session)
        {
            if (session.MouseHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(session.MouseHook);
                session.MouseHook = IntPtr.Zero;
            }
            if (session.KeyboardHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(session.KeyboardHook);
                session.KeyboardHook = IntPtr.Zero;
            }
        }

        private void FinalizeTeardown(HookSession session, bool fromHookThread)
        {
            lock (_lock)
            {
                if (session.TornDown)
                    return;
                session.TornDown = true;
                if (object.ReferenceEquals(_session, session))
                    _session = null;
                if (session.DeferredTeardownTimer != null)
                    session.DeferredTeardownTimer.Dispose();
                if (session.TimeoutTimer != null)
                    session.TimeoutTimer.Dispose();
            }

            if (session.ThreadId != 0)
                NativeMethods.PostThreadMessage(session.ThreadId, NativeMethods.WM_QUIT, UIntPtr.Zero, IntPtr.Zero);

            // 在钩子线程自身里（配对 Up 回调）不能 Join 自己：消息循环会在
            // 本次回调返回后收到 WM_QUIT 退出并卸钩，这里只投递不等待。
            if (fromHookThread)
                return;

            Thread thread = session.Thread;
            if (thread != null && thread.IsAlive)
            {
                if (!thread.Join(500))
                {
                    // 兜底：线程未按时退出时从外部直接卸钩，再短等一次。
                    Unhook(session);
                    thread.Join(500);
                }
            }
        }

        // ---------- 回调（钩子线程上执行，只做坐标判定与投递） ----------

        private IntPtr MouseHookCallback(HookSession session, int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int message = wParam.ToInt32();
                if (message == NativeMethods.WM_LBUTTONDOWN)
                {
                    var data = (NativeMethods.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.MSLLHOOKSTRUCT));
                    Point pt = data.pt.ToPoint();
                    if (session.SelfBounds.Contains(pt))
                        return NativeMethods.CallNextHookEx(session.MouseHook, nCode, wParam, lParam);
                    session.PendingPoint = pt;
                    session.SwallowNextUp = true;
                    return (IntPtr)1;
                }
                if (message == NativeMethods.WM_LBUTTONUP && session.SwallowNextUp)
                {
                    session.SwallowNextUp = false;
                    Point pt = session.PendingPoint;
                    int sessionId = session.Id;
                    PostEvent(session, delegate
                    {
                        Action<Point, int> handler = ExternalClick;
                        if (handler != null)
                            handler(pt, sessionId);
                    });
                    if (session.TeardownRequested)
                        FinalizeTeardown(session, true);
                    return (IntPtr)1;
                }
                if (message == NativeMethods.WM_RBUTTONDOWN)
                {
                    // 口径：任意位置右键都算取消（含组件自身）。
                    PostEvent(session, delegate
                    {
                        Action handler = CancelRequested;
                        if (handler != null)
                            handler();
                    });
                    return (IntPtr)1;
                }
            }
            return NativeMethods.CallNextHookEx(session.MouseHook, nCode, wParam, lParam);
        }

        private IntPtr KeyboardHookCallback(HookSession session, int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam.ToInt32() == NativeMethods.WM_KEYDOWN)
            {
                var data = (NativeMethods.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.KBDLLHOOKSTRUCT));
                if (data.vkCode == NativeMethods.VK_ESCAPE)
                {
                    PostEvent(session, delegate
                    {
                        Action handler = CancelRequested;
                        if (handler != null)
                            handler();
                    });
                    return (IntPtr)1;
                }
            }
            return NativeMethods.CallNextHookEx(session.KeyboardHook, nCode, wParam, lParam);
        }

        // ---------- 超时 ----------

        private void OnTimeoutTimer(HookSession session)
        {
            lock (_lock)
            {
                if (!object.ReferenceEquals(_session, session) || session.TeardownRequested)
                    return;
            }
            Stop();
            PostEvent(session, delegate
            {
                Action handler = TimeoutElapsed;
                if (handler != null)
                    handler();
            });
        }

        /// <summary>
        /// 事件投递：优先走 Start 时捕获的 SynchronizationContext（只 Post 不等待）；
        /// 捕获不到时直接在当前线程触发，由订阅方负责切线程。
        /// </summary>
        private static void PostEvent(HookSession session, Action action)
        {
            SynchronizationContext context = session.Context;
            if (context != null)
            {
                context.Post(delegate(object state) { action(); }, null);
            }
            else
            {
                action();
            }
        }
    }
}
