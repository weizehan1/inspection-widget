# Review package Task 4: 8ceea89..HEAD
8aa97e7 docs: Task 4 执行报告
cdfa92e feat(stage-4): 截图引擎（PrintWindow裁边/黑图检测/BitBlt回退）

 src/Capture/BlackFrameDetector.cs   |  42 +++++
 src/Capture/CaptureGeometry.cs      |  58 ++++++
 src/Capture/HookManager.cs          |  19 +-
 src/Capture/WindowCapture.cs        | 344 ++++++++++++++++++++++++++++++++++++
 src/MainForm.cs                     | 128 ++++++++++++--
 src/Native/NativeMethods.cs         |  12 ++
 tests/BlackFrameDetectorTests.cs    | 105 +++++++++++
 tests/CaptureGeometryTests.cs       |  86 +++++++++
 tests/InspectionWidget.Tests.csproj |   1 +
 tests/Program.cs                    |   2 +
 work/task-4-report.md               |  78 ++++++++
 11 files changed, 859 insertions(+), 16 deletions(-)

diff --git a/src/Capture/BlackFrameDetector.cs b/src/Capture/BlackFrameDetector.cs
new file mode 100644
index 0000000..e390d33
--- /dev/null
+++ b/src/Capture/BlackFrameDetector.cs
@@ -0,0 +1,42 @@
+using System.Drawing;
+
+namespace InspectionWidget.Capture
+{
+    /// <summary>
+    /// 黑图判定（只判「可疑」）：15×15 网格均匀采样，纯黑 (R=G=B=0) 或
+    /// 全透明 (A=0) 的采样点占比 &gt; 98% 时判可疑。采样仅 225 点，用
+    /// GetPixel 实现即可；GetPixel 对 32bpp ARGB 返回含 alpha 的颜色，
+    /// 对不透明格式 A=255。
+    /// </summary>
+    public static class BlackFrameDetector
+    {
+        private const int GridSize = 15;
+        private const double SuspectRatio = 0.98;
+
+        public static bool IsSuspect(Bitmap bmp)
+        {
+            if (bmp == null)
+                return true;
+            if (bmp.Width <= 0 || bmp.Height <= 0)
+                return true;
+
+            int suspect = 0;
+            int total = GridSize * GridSize;
+            for (int gy = 0; gy < GridSize; gy++)
+            {
+                for (int gx = 0; gx < GridSize; gx++)
+                {
+                    int x = (int)((gx + 0.5) * bmp.Width / (double)GridSize);
+                    int y = (int)((gy + 0.5) * bmp.Height / (double)GridSize);
+                    if (x >= bmp.Width) x = bmp.Width - 1;
+                    if (y >= bmp.Height) y = bmp.Height - 1;
+
+                    Color c = bmp.GetPixel(x, y);
+                    if (c.A == 0 || (c.R == 0 && c.G == 0 && c.B == 0))
+                        suspect++;
+                }
+            }
+            return suspect / (double)total > SuspectRatio;
+        }
+    }
+}
diff --git a/src/Capture/CaptureGeometry.cs b/src/Capture/CaptureGeometry.cs
new file mode 100644
index 0000000..95384fc
--- /dev/null
+++ b/src/Capture/CaptureGeometry.cs
@@ -0,0 +1,58 @@
+using System.Drawing;
+
+namespace InspectionWidget.Capture
+{
+    /// <summary>裁边计算结果：Valid=false 时 Crop 为 Empty，调用方回退整幅 winRect。</summary>
+    public struct CropResult
+    {
+        public bool Valid;
+        public Rectangle Crop;
+    }
+
+    /// <summary>
+    /// PrintWindow 抓图后的裁边几何（纯逻辑）：PrintWindow 按 GetWindowRect 完整
+    /// 矩形抓图，DWM 扩展边框矩形相对它的偏移与尺寸即真实内容区（去掉透明边框）。
+    /// </summary>
+    public static class CaptureGeometry
+    {
+        public static CropResult ComputeCrop(Rectangle winRect, Rectangle dwmRect)
+        {
+            var invalid = new CropResult { Valid = false, Crop = Rectangle.Empty };
+            if (winRect.Width <= 0 || winRect.Height <= 0)
+                return invalid;
+            if (dwmRect.Width <= 0 || dwmRect.Height <= 0)
+                return invalid;
+
+            int x = dwmRect.X - winRect.X;
+            int y = dwmRect.Y - winRect.Y;
+            int width = dwmRect.Width;
+            int height = dwmRect.Height;
+
+            // 夹紧到 winRect 范围内：偏移 <0 时从 0 起并相应扣减尺寸，
+            // 右下超出时按 win 尺寸截断。
+            if (x < 0)
+            {
+                width += x;
+                x = 0;
+            }
+            if (y < 0)
+            {
+                height += y;
+                y = 0;
+            }
+            if (x + width > winRect.Width)
+                width = winRect.Width - x;
+            if (y + height > winRect.Height)
+                height = winRect.Height - y;
+
+            if (width <= 0 || height <= 0)
+                return invalid;
+
+            return new CropResult
+            {
+                Valid = true,
+                Crop = new Rectangle(x, y, width, height)
+            };
+        }
+    }
+}
diff --git a/src/Capture/HookManager.cs b/src/Capture/HookManager.cs
index 197f1e7..ea01331 100644
--- a/src/Capture/HookManager.cs
+++ b/src/Capture/HookManager.cs
@@ -18,12 +18,15 @@ namespace InspectionWidget.Capture
     /// - 任意位置右键按下、任意焦点下 Esc 均吞掉并投递 CancelRequested；
     /// - 进入等待后固定 30 秒超时（不滑动续期），到期先自停再投递 TimeoutElapsed。
     /// </summary>
     public sealed class HookManager : IDisposable
     {
-        /// <summary>外部左键点击（物理像素坐标，取 Down 点）。在投递上下文上触发。</summary>
-        public event Action<Point> ExternalClick;
+        /// <summary>
+        /// 外部左键点击（物理像素坐标，取 Down 点；第二参数为事件所属钩子会话号，
+        /// 供消费方做代次守卫：上一会话的迟到事件须丢弃）。在投递上下文上触发。
+        /// </summary>
+        public event Action<Point, int> ExternalClick;
 
         /// <summary>右键按下或 Esc 触发取消。在投递上下文上触发。</summary>
         public event Action CancelRequested;
 
         /// <summary>30 秒无操作超时（已自停）。在投递上下文上触发。</summary>
@@ -74,22 +77,24 @@ namespace InspectionWidget.Capture
 
         /// <summary>
         /// 在钩子线程上挂钩并跑消息循环。已在运行则只更新自身矩形
         /// （不重置 30 秒超时：口径为进入等待态后固定 30 秒）。
         /// 挂钩失败抛 InvalidOperationException，抛出前已清理已挂的钩子。
+        /// 返回本次钩子会话号：已在运行时返回现行会话号（不新建会话），
+        /// 消费方据此对 ExternalClick 做代次守卫。
         /// </summary>
-        public void Start(Rectangle selfBounds)
+        public int Start(Rectangle selfBounds)
         {
             HookSession session;
             lock (_lock)
             {
                 if (_disposed)
                     throw new ObjectDisposedException("HookManager");
                 if (_session != null && !_session.TeardownRequested)
                 {
                     _session.SelfBounds = selfBounds;
-                    return;
+                    return _session.Id;
                 }
                 session = new HookSession();
                 session.Id = ++_nextSessionId;
                 session.SelfBounds = selfBounds;
                 session.Context = SynchronizationContext.Current;
@@ -121,10 +126,11 @@ namespace InspectionWidget.Capture
             }
 
             session.TimeoutTimer = new Timer(
                 delegate(object state) { OnTimeoutTimer(session); },
                 null, TimeoutMilliseconds, Timeout.Infinite);
+            return session.Id;
         }
 
         /// <summary>成对卸载并停线程，幂等；未运行直接返回。</summary>
         public void Stop()
         {
@@ -270,15 +276,16 @@ namespace InspectionWidget.Capture
                 }
                 if (message == NativeMethods.WM_LBUTTONUP && session.SwallowNextUp)
                 {
                     session.SwallowNextUp = false;
                     Point pt = session.PendingPoint;
+                    int sessionId = session.Id;
                     PostEvent(session, delegate
                     {
-                        Action<Point> handler = ExternalClick;
+                        Action<Point, int> handler = ExternalClick;
                         if (handler != null)
-                            handler(pt);
+                            handler(pt, sessionId);
                     });
                     if (session.TeardownRequested)
                         FinalizeTeardown(session, true);
                     return (IntPtr)1;
                 }
diff --git a/src/Capture/WindowCapture.cs b/src/Capture/WindowCapture.cs
new file mode 100644
index 0000000..3bf8138
--- /dev/null
+++ b/src/Capture/WindowCapture.cs
@@ -0,0 +1,344 @@
+using System;
+using System.Collections.Generic;
+using System.Drawing;
+using System.Threading;
+using InspectionWidget.Config;
+using InspectionWidget.Native;
+
+namespace InspectionWidget.Capture
+{
+    /// <summary>截图结果分类。</summary>
+    public enum CaptureStatus
+    {
+        Ok,
+        SuspectProtected,
+        WindowGone,
+        Failed
+    }
+
+    /// <summary>截图结果：Status=Ok 时 Image 所有权归调用方；Message 仅日志/诊断与失败提示用。</summary>
+    public sealed class CaptureOutcome
+    {
+        public CaptureStatus Status;
+        public Bitmap Image;
+        public string Message;
+    }
+
+    /// <summary>
+    /// 截图引擎：PrintWindow（完整矩形抓 + DWM 裁边）优先，黑图/失败/超时回退
+    /// BitBlt 屏幕拷贝（置前核查 + 自遮挡隐藏）。同步方法，调用方负责在工作
+    /// 线程调用；类内部不开新线程做 UI 操作，hide/show 由注入方切线程执行。
+    /// PrintWindow/BitBlt 均为 Windows 专属路径，本机 Mono 仅保证编译。
+    /// </summary>
+    public sealed class WindowCapture
+    {
+        private const uint PwRenderFullContent = 0x00000002;
+        private const int PrintWindowTimeoutMilliseconds = 3000;
+
+        // ---------- PrintWindow 超时迟到回收 ----------
+
+        /// <summary>
+        /// 一次 PrintWindow 尝试：超时后位图仍被迟到线程使用，禁止 Dispose，
+        /// 登记到回收表，迟到线程完成后自行 Dispose 并摘除。
+        /// </summary>
+        private sealed class PrintAttempt
+        {
+            public Bitmap Bitmap;
+            public bool Ok;
+            public volatile bool Finished;
+            public volatile bool Abandoned;
+        }
+
+        private static readonly object LateLock = new object();
+        private static readonly List<PrintAttempt> LateAttempts = new List<PrintAttempt>();
+
+        /// <summary>迟到回收表中未完成条目数（诊断用）。</summary>
+        public static int LateAttemptCount
+        {
+            get { lock (LateLock) { return LateAttempts.Count; } }
+        }
+
+        public CaptureOutcome Capture(IntPtr hwnd, CaptureMode mode, Rectangle selfBounds,
+            Action hideSelf, Action showSelf)
+        {
+            // 1. 复核：窗口已关闭/最小化 → WindowGone
+            if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd) || NativeMethods.IsIconic(hwnd))
+                return Gone("窗口已关闭或最小化");
+
+            Rectangle winRect;
+            if (!TryGetWindowRect(hwnd, out winRect) || winRect.Width <= 0 || winRect.Height <= 0)
+                return Gone("窗口已关闭或无法取得尺寸");
+
+            // DWM 扩展边框矩形：失败则退回 winRect 并在 Message 注明
+            string dwmNote = null;
+            Rectangle dwmRect = winRect;
+            var dwmNative = new NativeMethods.RECT();
+            int hr = NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
+                out dwmNative, System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.RECT)));
+            if (hr == 0)
+            {
+                Rectangle candidate = dwmNative.ToRectangle();
+                if (candidate.Width > 0 && candidate.Height > 0)
+                    dwmRect = candidate;
+                else
+                    dwmNote = "DWM 查询失败，可能含透明边";
+            }
+            else
+            {
+                dwmNote = "DWM 查询失败，可能含透明边";
+            }
+
+            // 2. PrintWindow 路径
+            if (mode != CaptureMode.BitBltOnly)
+            {
+                CaptureOutcome pwOutcome = TryPrintWindow(hwnd, winRect, dwmRect, mode, dwmNote);
+                if (pwOutcome != null)
+                    return pwOutcome;
+                // null 表示走 BitBlt 回退
+                if (mode == CaptureMode.PrintWindowOnly)
+                    return Failed("PrintWindow 截图失败", dwmNote); // 防御：TryPrintWindow 在 Only 档不返回 null
+            }
+
+            // 3. BitBlt 回退
+            return CaptureViaBitBlt(hwnd, winRect, dwmRect, selfBounds, hideSelf, showSelf, dwmNote);
+        }
+
+        // ---------- PrintWindow ----------
+
+        /// <summary>返回 null 表示应走 BitBlt 回退（仅 Auto 档）。</summary>
+        private CaptureOutcome TryPrintWindow(IntPtr hwnd, Rectangle winRect, Rectangle dwmRect, CaptureMode mode, string dwmNote)
+        {
+            var bmp = new Bitmap(winRect.Width, winRect.Height);
+            var attempt = new PrintAttempt { Bitmap = bmp };
+
+            var thread = new Thread(delegate()
+            {
+                bool ok = false;
+                try
+                {
+                    using (Graphics g = Graphics.FromImage(bmp))
+                    {
+                        IntPtr hdc = g.GetHdc();
+                        try
+                        {
+                            ok = NativeMethods.PrintWindow(hwnd, hdc, PwRenderFullContent);
+                        }
+                        finally
+                        {
+                            g.ReleaseHdc(hdc);
+                        }
+                    }
+                }
+                catch
+                {
+                    ok = false;
+                }
+                attempt.Ok = ok;
+                lock (LateLock)
+                {
+                    attempt.Finished = true;
+                    if (attempt.Abandoned)
+                    {
+                        // 调用方已超时放弃：迟到线程负责释放位图并摘除登记
+                        bmp.Dispose();
+                        LateAttempts.Remove(attempt);
+                    }
+                }
+            });
+            thread.IsBackground = true;
+            thread.Name = "InspectionWidget.PrintWindow";
+            thread.Start();
+
+            if (!thread.Join(PrintWindowTimeoutMilliseconds))
+            {
+                bool finishedLate;
+                lock (LateLock)
+                {
+                    // 与迟到线程的 finally 串行化：若它恰已完成，按完成处理
+                    finishedLate = attempt.Finished;
+                    if (!finishedLate)
+                    {
+                        attempt.Abandoned = true;
+                        LateAttempts.Add(attempt);
+                    }
+                }
+                if (!finishedLate)
+                {
+                    // 超时：不 Dispose 位图（迟到线程仍在用），已登记回收表
+                    if (mode == CaptureMode.PrintWindowOnly)
+                        return Failed("PrintWindow 超时（3 秒）", dwmNote);
+                    return null;
+                }
+            }
+
+            if (!attempt.Ok)
+            {
+                bmp.Dispose();
+                if (mode == CaptureMode.PrintWindowOnly)
+                    return Failed("PrintWindow 返回失败", dwmNote);
+                return null;
+            }
+
+            // 裁边：Valid 才裁（裁剪产生新 Bitmap 后 Dispose 原图），无效用整幅
+            Bitmap result = bmp;
+            CropResult crop = CaptureGeometry.ComputeCrop(winRect, dwmRect);
+            if (crop.Valid && (crop.Crop.X != 0 || crop.Crop.Y != 0
+                || crop.Crop.Width != winRect.Width || crop.Crop.Height != winRect.Height))
+            {
+                Bitmap cropped = bmp.Clone(crop.Crop, bmp.PixelFormat);
+                bmp.Dispose();
+                result = cropped;
+            }
+
+            // 4. 截图后复核：窗口位置/尺寸变了 → WindowGone
+            if (WindowMoved(hwnd, winRect))
+            {
+                result.Dispose();
+                return Gone("窗口已移动，请重试");
+            }
+
+            if (BlackFrameDetector.IsSuspect(result))
+            {
+                result.Dispose();
+                if (mode == CaptureMode.PrintWindowOnly)
+                    return new CaptureOutcome
+                    {
+                        Status = CaptureStatus.SuspectProtected,
+                        Image = null,
+                        Message = AppendNote("截图疑似黑图，可能受系统保护", dwmNote)
+                    };
+                return null;
+            }
+
+            return new CaptureOutcome
+            {
+                Status = CaptureStatus.Ok,
+                Image = result,
+                Message = dwmNote
+            };
+        }
+
+        // ---------- BitBlt 回退 ----------
+
+        private CaptureOutcome CaptureViaBitBlt(IntPtr hwnd, Rectangle winRect, Rectangle dwmRect,
+            Rectangle selfBounds, Action hideSelf, Action showSelf, string dwmNote)
+        {
+            if (dwmRect.Width <= 0 || dwmRect.Height <= 0)
+                return Failed("目标窗口尺寸无效", dwmNote);
+
+            // 置前并核查结果：不等 → 等 100ms 再查一次，仍不等 → Failed
+            NativeMethods.SetForegroundWindow(hwnd);
+            NativeMethods.BringWindowToTop(hwnd);
+            if (NativeMethods.GetForegroundWindow() != hwnd)
+            {
+                Thread.Sleep(100);
+                if (NativeMethods.GetForegroundWindow() != hwnd)
+                    return Failed("窗口置前被拒，请手动点开目标窗口后重试", dwmNote);
+            }
+
+            bool hidden = false;
+            Bitmap bmp = null;
+            try
+            {
+                if (selfBounds.Width > 0 && selfBounds.Height > 0
+                    && selfBounds.IntersectsWith(dwmRect) && hideSelf != null)
+                {
+                    hideSelf();
+                    hidden = true;
+                    Thread.Sleep(60);
+                }
+
+                bmp = new Bitmap(dwmRect.Width, dwmRect.Height);
+                using (Graphics g = Graphics.FromImage(bmp))
+                {
+                    g.CopyFromScreen(dwmRect.Location, Point.Empty, dwmRect.Size);
+                }
+            }
+            catch (Exception ex)
+            {
+                if (bmp != null) bmp.Dispose();
+                return Failed("屏幕拷贝失败：" + ex.Message, dwmNote);
+            }
+            finally
+            {
+                if (hidden && showSelf != null)
+                {
+                    try { showSelf(); }
+                    catch { /* 恢复失败不掩盖截图结果 */ }
+                }
+            }
+
+            // 4. 截图后复核
+            if (WindowMoved(hwnd, winRect))
+            {
+                bmp.Dispose();
+                return Gone("窗口已移动，请重试");
+            }
+
+            if (BlackFrameDetector.IsSuspect(bmp))
+            {
+                bmp.Dispose();
+                return new CaptureOutcome
+                {
+                    Status = CaptureStatus.SuspectProtected,
+                    Image = null,
+                    Message = AppendNote("截图疑似黑图，可能受系统保护", dwmNote)
+                };
+            }
+
+            return new CaptureOutcome
+            {
+                Status = CaptureStatus.Ok,
+                Image = bmp,
+                Message = dwmNote
+            };
+        }
+
+        // ---------- 辅助 ----------
+
+        private static bool TryGetWindowRect(IntPtr hwnd, out Rectangle rect)
+        {
+            rect = Rectangle.Empty;
+            NativeMethods.RECT native;
+            if (!NativeMethods.GetWindowRect(hwnd, out native))
+                return false;
+            rect = native.ToRectangle();
+            return true;
+        }
+
+        /// <summary>抓图后复核：窗口不在了或矩形变了都视为已移动/关闭。</summary>
+        private static bool WindowMoved(IntPtr hwnd, Rectangle startRect)
+        {
+            if (!NativeMethods.IsWindow(hwnd))
+                return true;
+            Rectangle now;
+            if (!TryGetWindowRect(hwnd, out now))
+                return true;
+            return now != startRect;
+        }
+
+        private static CaptureOutcome Gone(string message)
+        {
+            return new CaptureOutcome { Status = CaptureStatus.WindowGone, Image = null, Message = message };
+        }
+
+        private static CaptureOutcome Failed(string message, string note)
+        {
+            return new CaptureOutcome
+            {
+                Status = CaptureStatus.Failed,
+                Image = null,
+                Message = AppendNote(message, note)
+            };
+        }
+
+        private static string AppendNote(string message, string note)
+        {
+            if (string.IsNullOrEmpty(note))
+                return message;
+            if (string.IsNullOrEmpty(message))
+                return note;
+            return message + "（" + note + "）";
+        }
+    }
+}
diff --git a/src/MainForm.cs b/src/MainForm.cs
index 9e878de..cfe281e 100644
--- a/src/MainForm.cs
+++ b/src/MainForm.cs
@@ -39,10 +39,14 @@ namespace InspectionWidget
 
         // 设备名单校验错误（手改 config 造成）：非 null 时常驻提示并禁用设备按钮
         private string _deviceListError;
         private bool _rootPromptShown;
 
+        // Waiting 会话代次守卫（Task 3 审查 M-3）：进入 Waiting 时记录 HookManager
+        // 返回的钩子会话号，ExternalClick 消费前比对，上一会话的迟到事件直接丢弃。
+        private int _waitingSessionId;
+
         private Panel _titleBarPanel;
         private Label _deviceHeaderLabel;
         private Panel _deviceZonePanel;
         private Panel _credentialHeaderPanel;
         private Label _credentialArrowLabel;
@@ -683,14 +687,17 @@ namespace InspectionWidget
             {
                 try
                 {
                     // Bounds 随拖动变化，Start 时取当前值；等待态期间禁用拖动（见
                     // OnDragMouseDown），保证 selfBounds 在整个等待期有效。
-                    _hooks.Start(this.Bounds);
+                    // Start 返回钩子会话号：已在运行（Waiting 内切换设备）返回现行
+                    // 会话号、守卫不变；新会话则更新守卫（M-3 代次守卫）。
+                    _waitingSessionId = _hooks.Start(this.Bounds);
                 }
                 catch (Exception ex)
                 {
+                    _waitingSessionId = 0;
                     _stateMachine.Cancel();
                     RefreshStateVisuals();
                     _hooks.Stop();
                     ShowFeedback("选窗启动失败：" + ex.Message, true);
                 }
@@ -703,17 +710,21 @@ namespace InspectionWidget
 
         /// <summary>
         /// 外部点击选窗：先解析目标再决定状态转移——只有命中有效窗口才
         /// BeginCapture（状态机无 Capturing→Waiting 转移，排除分支必须留在 Waiting）。
         /// </summary>
-        private void OnHookExternalClick(Point pt)
+        private void OnHookExternalClick(Point pt, int sessionId)
         {
             if (InvokeRequired)
             {
-                BeginInvoke(new Action<Point>(OnHookExternalClick), pt);
+                BeginInvoke(new Action<Point, int>(OnHookExternalClick), pt, sessionId);
                 return;
             }
+            // 代次守卫（M-3）：事件所属会话号与当前 Waiting 会话不一致 →
+            // 上一会话的迟到事件，直接丢弃，不解析不转移状态。
+            if (sessionId != _waitingSessionId)
+                return;
             if (_stateMachine.State != PanelState.Waiting)
                 return;
 
             ResolveResult result = WindowResolver.ResolveTopLevel(pt, this.Handle);
             switch (result.Reason)
@@ -750,14 +761,16 @@ namespace InspectionWidget
         private void RestartHooksWhileWaiting()
         {
             _hooks.Stop();
             try
             {
-                _hooks.Start(this.Bounds);
+                // 重启产生新会话：更新守卫，旧会话迟到的点击事件将被丢弃（M-3）
+                _waitingSessionId = _hooks.Start(this.Bounds);
             }
             catch (Exception ex)
             {
+                _waitingSessionId = 0;
                 _stateMachine.Cancel();
                 OnStateChanged();
                 ShowFeedback("选窗启动失败：" + ex.Message, true);
             }
         }
@@ -787,19 +800,114 @@ namespace InspectionWidget
             if (cancelled)
                 ShowFeedback("等待超时，已取消", true);
         }
 
         /// <summary>
-        /// Task 4 接入点：真实截图引擎以 (result.Hwnd, requestId) 为输入在此启动，
-        /// 完成后 CompleteCapture / 失败 FailCapture 回 Idle。本任务先占位：
-        /// 反馈已选中窗口标题并直接回 Idle。
+        /// Task 4 接入点：状态机已 BeginCapture（Capturing）。截图引擎放工作线程
+        /// 执行（PrintWindow 同步阻塞不能压 UI/钩子线程），结果 BeginInvoke 回 UI
+        /// 线程处理；hideSelf/showSelf 由此处注入、用 Invoke 切 UI 线程改 Opacity
+        /// 并等待完成（BitBlt 防自遮挡）。成功图暂只反馈并 Dispose，保存由
+        /// Task 5 接入；全部路径 CompleteCapture/FailCapture 回 Idle 且经
+        /// OnStateChanged 集中点停钩。
         /// </summary>
         private void OnTargetResolved(ResolveResult result, int requestId)
         {
-            ShowSuccessFeedback("已选中窗口：" + (string.IsNullOrEmpty(result.Title) ? "（无标题）" : result.Title));
-            _stateMachine.CompleteCapture();
-            OnStateChanged();
+            IntPtr hwnd = result.Hwnd;
+            string title = string.IsNullOrEmpty(result.Title) ? "（无标题）" : result.Title;
+            CaptureMode mode = _config.CaptureMode;
+            Rectangle selfBounds = this.Bounds;
+
+            Action hideSelf = delegate
+            {
+                if (IsDisposed) return;
+                Invoke(new Action(delegate { Opacity = 0; }));
+            };
+            Action showSelf = delegate
+            {
+                if (IsDisposed) return;
+                Invoke(new Action(delegate { Opacity = 1; }));
+            };
+
+            var worker = new System.Threading.Thread(new System.Threading.ThreadStart(delegate
+            {
+                CaptureOutcome outcome;
+                try
+                {
+                    outcome = new WindowCapture().Capture(hwnd, mode, selfBounds, hideSelf, showSelf);
+                }
+                catch (Exception ex)
+                {
+                    outcome = new CaptureOutcome
+                    {
+                        Status = CaptureStatus.Failed,
+                        Image = null,
+                        Message = "截图异常：" + ex.Message
+                    };
+                }
+
+                if (IsDisposed)
+                {
+                    if (outcome.Image != null) outcome.Image.Dispose();
+                    return;
+                }
+                try
+                {
+                    BeginInvoke(new Action(delegate { OnCaptureCompleted(outcome, requestId, title); }));
+                }
+                catch (ObjectDisposedException)
+                {
+                    // 窗体正在关闭：迟到结果直接释放丢弃
+                    if (outcome.Image != null) outcome.Image.Dispose();
+                }
+                catch (InvalidOperationException)
+                {
+                    if (outcome.Image != null) outcome.Image.Dispose();
+                }
+            }));
+            worker.IsBackground = true;
+            worker.Name = "InspectionWidget.CaptureWorker";
+            worker.Start();
+        }
+
+        /// <summary>
+        /// 截图结果回 UI 线程处理：先做迟到校验（requestId 与状态机 CurrentRequestId
+        /// 比对，不一致直接 Dispose 图并丢弃），再按状态反馈并回 Idle。
+        /// </summary>
+        private void OnCaptureCompleted(CaptureOutcome outcome, int requestId, string title)
+        {
+            if (requestId != _stateMachine.CurrentRequestId
+                || _stateMachine.State != PanelState.Capturing)
+            {
+                if (outcome.Image != null) outcome.Image.Dispose();
+                return;
+            }
+
+            bool completed;
+            switch (outcome.Status)
+            {
+                case CaptureStatus.Ok:
+                    // Task 5 保存接入前：先反馈并释放图（所有权在本方）
+                    if (outcome.Image != null) outcome.Image.Dispose();
+                    ShowSuccessFeedback("已截图：" + title + "（保存阶段 5 接入）");
+                    completed = _stateMachine.CompleteCapture();
+                    break;
+                case CaptureStatus.SuspectProtected:
+                    ShowFeedback("该窗口可能受系统保护，截不到画面", true);
+                    completed = _stateMachine.FailCapture();
+                    break;
+                case CaptureStatus.WindowGone:
+                    ShowFeedback(string.IsNullOrEmpty(outcome.Message) ? "该窗口已关闭，请重新点选" : outcome.Message, true);
+                    completed = _stateMachine.FailCapture();
+                    break;
+                default: // Failed
+                    ShowFeedback(string.IsNullOrEmpty(outcome.Message) ? "截图失败，请重试" : outcome.Message, true);
+                    completed = _stateMachine.FailCapture();
+                    break;
+            }
+
+            if (completed)
+                OnStateChanged(); // 回 Idle → 集中点停钩（Capturing 时钩已停，幂等）
         }
 
         protected override void OnPaint(PaintEventArgs e)
         {
             base.OnPaint(e);
diff --git a/src/Native/NativeMethods.cs b/src/Native/NativeMethods.cs
index 9798108..0daa9a1 100644
--- a/src/Native/NativeMethods.cs
+++ b/src/Native/NativeMethods.cs
@@ -162,10 +162,22 @@ namespace InspectionWidget.Native
         public static extern bool BringWindowToTop(IntPtr hWnd);
 
         [DllImport("user32.dll")]
         public static extern IntPtr GetForegroundWindow();
 
+        [DllImport("user32.dll", SetLastError = true)]
+        [return: MarshalAs(UnmanagedType.Bool)]
+        public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
+
+        // ---------- dwmapi ----------
+
+        /// <summary>DwmGetWindowAttribute 属性：扩展边框边界（真实内容区，含透明边修正）。</summary>
+        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
+
+        [DllImport("dwmapi.dll")]
+        public static extern int DwmGetWindowAttribute(IntPtr hWnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);
+
         // ---------- kernel32 ----------
 
         [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
         public static extern IntPtr GetModuleHandle(string lpModuleName);
 
diff --git a/tests/BlackFrameDetectorTests.cs b/tests/BlackFrameDetectorTests.cs
new file mode 100644
index 0000000..37ccddb
--- /dev/null
+++ b/tests/BlackFrameDetectorTests.cs
@@ -0,0 +1,105 @@
+using System.Collections.Generic;
+using System.Drawing;
+using System.Drawing.Imaging;
+using InspectionWidget.Capture;
+
+namespace InspectionWidget.Tests
+{
+    /// <summary>
+    /// BlackFrameDetector 黑图判定（Mono + libgdiplus 真 Bitmap）：
+    /// 15×15 网格采样、纯黑或全透明占比 &gt; 98% 为可疑。
+    /// 98% 边界两条按采样点构造法落实：先全黑，再在已知采样坐标精准涂白。
+    /// </summary>
+    public static class BlackFrameDetectorTests
+    {
+        private const int Size = 100;
+
+        /// <summary>与实现同口径的采样坐标：(i+0.5)/15 比例取整。</summary>
+        private static List<Point> SamplePoints(int width, int height)
+        {
+            var points = new List<Point>();
+            for (int gy = 0; gy < 15; gy++)
+            {
+                for (int gx = 0; gx < 15; gx++)
+                {
+                    int x = (int)((gx + 0.5) * width / 15.0);
+                    int y = (int)((gy + 0.5) * height / 15.0);
+                    if (x >= width) x = width - 1;
+                    if (y >= height) y = height - 1;
+                    points.Add(new Point(x, y));
+                }
+            }
+            return points;
+        }
+
+        private static Bitmap FilledBitmap(Color color)
+        {
+            var bmp = new Bitmap(Size, Size, PixelFormat.Format32bppArgb);
+            for (int y = 0; y < Size; y++)
+                for (int x = 0; x < Size; x++)
+                    bmp.SetPixel(x, y, color);
+            return bmp;
+        }
+
+        public static void RunAll()
+        {
+            TestRunner.Run("黑图：全黑 100×100 → 可疑", () =>
+            {
+                using (Bitmap bmp = FilledBitmap(Color.Black))
+                    Assert.True(BlackFrameDetector.IsSuspect(bmp));
+            });
+
+            TestRunner.Run("黑图：全透明 → 可疑", () =>
+            {
+                // 新建 32bpp ARGB 位图默认全透明（A=0）
+                using (var bmp = new Bitmap(Size, Size, PixelFormat.Format32bppArgb))
+                    Assert.True(BlackFrameDetector.IsSuspect(bmp));
+            });
+
+            TestRunner.Run("黑图：全白 → 不可疑", () =>
+            {
+                using (Bitmap bmp = FilledBitmap(Color.White))
+                    Assert.False(BlackFrameDetector.IsSuspect(bmp));
+            });
+
+            TestRunner.Run("黑图：黑底中央白块（采样必含白点）→ 不可疑", () =>
+            {
+                using (Bitmap bmp = FilledBitmap(Color.Black))
+                {
+                    for (int y = 40; y < 60; y++)
+                        for (int x = 40; x < 60; x++)
+                            bmp.SetPixel(x, y, Color.White);
+                    Assert.False(BlackFrameDetector.IsSuspect(bmp));
+                }
+            });
+
+            TestRunner.Run("黑图：null → 可疑", () =>
+            {
+                Assert.True(BlackFrameDetector.IsSuspect(null));
+            });
+
+            TestRunner.Run("黑图：边界 222 黑 3 白（98.67% > 98%）→ 可疑", () =>
+            {
+                using (Bitmap bmp = FilledBitmap(Color.Black))
+                {
+                    List<Point> points = SamplePoints(Size, Size);
+                    Assert.Equal(225, points.Count);
+                    for (int i = 0; i < 3; i++)
+                        bmp.SetPixel(points[i].X, points[i].Y, Color.White);
+                    Assert.True(BlackFrameDetector.IsSuspect(bmp));
+                }
+            });
+
+            TestRunner.Run("黑图：边界 220 黑 5 白（97.8% ≤ 98%）→ 不可疑", () =>
+            {
+                using (Bitmap bmp = FilledBitmap(Color.Black))
+                {
+                    List<Point> points = SamplePoints(Size, Size);
+                    for (int i = 0; i < 5; i++)
+                        bmp.SetPixel(points[i].X, points[i].Y, Color.White);
+                    Assert.False(BlackFrameDetector.IsSuspect(bmp));
+                }
+            });
+        }
+    }
+}
diff --git a/tests/CaptureGeometryTests.cs b/tests/CaptureGeometryTests.cs
new file mode 100644
index 0000000..099e44e
--- /dev/null
+++ b/tests/CaptureGeometryTests.cs
@@ -0,0 +1,86 @@
+using System.Drawing;
+using InspectionWidget.Capture;
+
+namespace InspectionWidget.Tests
+{
+    /// <summary>CaptureGeometry 裁边几何：标准偏移、全幅、负坐标副屏、异常矩形、部分越界夹紧。</summary>
+    public static class CaptureGeometryTests
+    {
+        public static void RunAll()
+        {
+            TestRunner.Run("几何：标准偏移裁边 (7,0,802,609)", () =>
+            {
+                CropResult r = CaptureGeometry.ComputeCrop(
+                    new Rectangle(100, 100, 816, 616),
+                    new Rectangle(107, 100, 802, 609));
+                Assert.True(r.Valid);
+                Assert.Equal(new Rectangle(7, 0, 802, 609), r.Crop);
+            });
+
+            TestRunner.Run("几何：两矩形相同 → 全幅裁边", () =>
+            {
+                CropResult r = CaptureGeometry.ComputeCrop(
+                    new Rectangle(100, 100, 816, 616),
+                    new Rectangle(100, 100, 816, 616));
+                Assert.True(r.Valid);
+                Assert.Equal(new Rectangle(0, 0, 816, 616), r.Crop);
+            });
+
+            TestRunner.Run("几何：负坐标副屏偏移一致", () =>
+            {
+                CropResult r = CaptureGeometry.ComputeCrop(
+                    new Rectangle(-1200, 50, 816, 616),
+                    new Rectangle(-1193, 50, 802, 609));
+                Assert.True(r.Valid);
+                Assert.Equal(new Rectangle(7, 0, 802, 609), r.Crop);
+            });
+
+            TestRunner.Run("几何：dwm 宽为 0 → 无效且 Crop 为 Empty", () =>
+            {
+                CropResult r = CaptureGeometry.ComputeCrop(
+                    new Rectangle(100, 100, 816, 616),
+                    new Rectangle(107, 100, 0, 609));
+                Assert.False(r.Valid);
+                Assert.Equal(Rectangle.Empty, r.Crop);
+            });
+
+            TestRunner.Run("几何：winRect 宽高 ≤0 → 无效", () =>
+            {
+                CropResult r = CaptureGeometry.ComputeCrop(
+                    new Rectangle(100, 100, 0, 616),
+                    new Rectangle(107, 100, 802, 609));
+                Assert.False(r.Valid);
+                Assert.Equal(Rectangle.Empty, r.Crop);
+            });
+
+            TestRunner.Run("几何：dwm 左侧部分越界 → 夹紧", () =>
+            {
+                // dwm 比 win 左移 10：相对偏移 -10、宽 816 → 夹紧为 (0,0,806,616)
+                CropResult r = CaptureGeometry.ComputeCrop(
+                    new Rectangle(100, 100, 816, 616),
+                    new Rectangle(90, 100, 816, 616));
+                Assert.True(r.Valid);
+                Assert.Equal(new Rectangle(0, 0, 806, 616), r.Crop);
+            });
+
+            TestRunner.Run("几何：dwm 右下部分越界 → 夹紧", () =>
+            {
+                // 偏移 (7,0)、宽 900 超出 win 宽 816 → 截为 809；高 700 超出 616 → 截为 616
+                CropResult r = CaptureGeometry.ComputeCrop(
+                    new Rectangle(100, 100, 816, 616),
+                    new Rectangle(107, 100, 900, 700));
+                Assert.True(r.Valid);
+                Assert.Equal(new Rectangle(7, 0, 809, 616), r.Crop);
+            });
+
+            TestRunner.Run("几何：dwm 完全越界 → 夹紧后宽 ≤0 无效", () =>
+            {
+                CropResult r = CaptureGeometry.ComputeCrop(
+                    new Rectangle(100, 100, 816, 616),
+                    new Rectangle(2000, 100, 100, 100));
+                Assert.False(r.Valid);
+                Assert.Equal(Rectangle.Empty, r.Crop);
+            });
+        }
+    }
+}
diff --git a/tests/InspectionWidget.Tests.csproj b/tests/InspectionWidget.Tests.csproj
index 6d3d827..49fc735 100644
--- a/tests/InspectionWidget.Tests.csproj
+++ b/tests/InspectionWidget.Tests.csproj
@@ -39,10 +39,11 @@
     <LangVersion>7.3</LangVersion>
   </PropertyGroup>
   <ItemGroup>
     <Reference Include="System" />
     <Reference Include="System.Core" />
+    <Reference Include="System.Drawing" />
     <Reference Include="System.Runtime.Serialization" />
   </ItemGroup>
   <ItemGroup>
     <Compile Include="**\*.cs" />
   </ItemGroup>
diff --git a/tests/Program.cs b/tests/Program.cs
index d5b4676..b4629dd 100644
--- a/tests/Program.cs
+++ b/tests/Program.cs
@@ -12,10 +12,12 @@ namespace InspectionWidget.Tests
             ConfigPathResolverTests.RunAll();
             ConfigStoreLockTests.RunAll();
             PanelLayoutTests.RunAll();
             PanelStateMachineTests.RunAll();
             TargetClassifierTests.RunAll();
+            CaptureGeometryTests.RunAll();
+            BlackFrameDetectorTests.RunAll();
 
             Console.WriteLine("----------------------------------------");
             Console.WriteLine("用例总数：" + TestRunner.TotalCount
                 + "，通过：" + TestRunner.PassedCount
                 + "，失败：" + TestRunner.FailedCount);
diff --git a/work/task-4-report.md b/work/task-4-report.md
new file mode 100644
index 0000000..29498ea
--- /dev/null
+++ b/work/task-4-report.md
@@ -0,0 +1,78 @@
+# Task 4 执行报告：截图引擎
+
+## 状态
+
+**DONE**（本机可验证部分全过；PrintWindow/BitBlt/钩子等 Windows 专属路径仅保证编译通过，实机清单见「遗留」）。
+
+实现提交：`cdfa92e`（`feat(stage-4): 截图引擎（PrintWindow裁边/黑图检测/BitBlt回退）`，父提交为基线 `8ceea89`，已用 `git log --oneline | head -2` 自核）。本报告随其后的报告提交一同入库，哈希以协调者 `git log` 核对为准。
+
+## 交付内容
+
+| 文件 | 内容 |
+|---|---|
+| `src/Capture/CaptureGeometry.cs`（新建） | `CropResult { Valid, Crop }` + `CaptureGeometry.ComputeCrop(winRect, dwmRect)` 纯逻辑裁边 |
+| `src/Capture/BlackFrameDetector.cs`（新建） | `BlackFrameDetector.IsSuspect(Bitmap)`：15×15 网格采样、纯黑或全透明占比 > 98% 判可疑，null → true |
+| `src/Capture/WindowCapture.cs`（新建） | `WindowCapture.Capture(hwnd, mode, selfBounds, hideSelf, showSelf)` → `CaptureOutcome { Status, Image, Message }`，`CaptureStatus { Ok, SuspectProtected, WindowGone, Failed }` |
+| `src/Native/NativeMethods.cs`（追加） | `PrintWindow`、`DwmGetWindowAttribute` + 常量 `DWMWA_EXTENDED_FRAME_BOUNDS = 9`（`GetForegroundWindow` Task 3 已有，本任务未重复加） |
+| `src/Capture/HookManager.cs`（修改） | M-3 补接线：`ExternalClick` 改为携带会话号 `Action<Point, int>`，`Start` 改为返回钩子会话号 `int`（见下节） |
+| `src/MainForm.cs`（修改） | `OnTargetResolved` 真实接线（工作线程截图 + BeginInvoke 回 UI + requestId 迟到校验）；Waiting 会话代次守卫（M-3） |
+| `tests/CaptureGeometryTests.cs`（新建） | 几何 8 条 |
+| `tests/BlackFrameDetectorTests.cs`（新建） | 黑图 7 条（Mono + libgdiplus 真 Bitmap） |
+| `tests/Program.cs`、`tests/InspectionWidget.Tests.csproj`（修改） | 注册两组新测试；测试工程补 `System.Drawing` 引用（Bitmap/Rectangle 直接使用所需） |
+
+全程 C# 7.2 以内语法（R6）：`build.sh` 走 xbuild + `/p:LangVersion=7.2` 编译通过，无 7.3 独有语法。
+
+## TDD 证据（先 FAIL 后 PASS）
+
+### 几何（CaptureGeometryTests，8 条）
+- **RED**：先只写测试与注册，运行 `bash build.sh` 退出码 1，测试工程编译失败——`CS0246: The type or namespace name 'CropResult' could not be found`、`CS0103: The name 'BlackFrameDetector' does not exist` 等共 39 个错误（日志 `/tmp/task4-red.log`，实现类尚不存在，符合预期）。
+- **GREEN**：实现 `CaptureGeometry` 后 8 条全 PASS：标准偏移 (100,100,816,616)/(107,100,802,609)→(7,0,802,609)；两矩形相同→(0,0,816,616)；负坐标副屏 (-1200,50,…)/(-1193,50,…)→(7,0,802,609)；dwm 宽 0→无效且 Crop=Empty；winRect 宽 0→无效；dwm 左侧部分越界 (90,100,816,616)→夹紧 (0,0,806,616)；dwm 右下部分越界 (107,100,900,700)→夹紧 (7,0,809,616)；dwm 完全越界 (2000,100,100,100)→夹紧后宽 ≤0 无效。
+- 夹紧算法：先算相对偏移与尺寸，偏移 <0 时归 0 并相应扣减宽/高，右下超出按 win 尺寸截断，截断后宽或高 ≤0 → Valid=false。
+
+### 黑图（BlackFrameDetectorTests，7 条，Mono + libgdiplus 真跑）
+- **RED**：同上（类不存在编译失败）。
+- **环境插曲（如实记录）**：首次 GREEN 运行几何 8 条全 PASS、黑图测试因 `DllNotFoundException: libgdiplus.so.0` 失败——libgdiplus 本体已装（/usr/lib/libgdiplus.so.0 在），但其依赖 `libgif.so.7`、`libexif.so.12` 缺失（`ldd` 实测 not found），且本机 apt 索引为空、`apt-get update` 因外部仓库 404 失败拿不到候选包。处置：从 Ubuntu 官方池直下 noble 对应 deb 并 `dpkg -i`：`libgif7_5.2.2-1ubuntu1_amd64.deb`、`libexif12_0.6.24-1build2_amd64.deb`；`ldd` 复查依赖齐全后黑图测试真跑通过。此为本机测试环境修复，不涉及产品代码。
+- **GREEN**：7 条全 PASS：全黑 100×100→true；全透明（32bpp ARGB 新建默认 A=0）→true；全白→false；黑底中央白块（40–59 区间，采样网格必含 43/50/56 等白点）→false；null→true；**98% 边界两条按简报采样点构造法落实**——测试内以与实现同口径公式 (i+0.5)/15 比例取整算出 225 个采样坐标，先全黑、再精准涂白：涂 3 点（222 黑，98.67% > 98%）→true；涂 5 点（220 黑，97.8% ≤ 98%）→false。
+
+### 总账
+- `bash build.sh` 退出 0；**用例总数 146（基线 131 + 几何 8 + 黑图 7），通过 146，失败 0**，既有用例无回归。
+
+## WindowCapture 流程落实（对照简报逐条）
+
+1. **复核**：`!IsWindow(hwnd)` 或 `IsIconic` → `WindowGone`；`GetWindowRect` 取 winRect；`DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS=9)` 取 dwmRect，失败（HRESULT ≠ 0 或尺寸无效）则 dwmRect=winRect 且 Message 注明「DWM 查询失败，可能含透明边」（Message 仅日志/诊断与失败提示用）。
+2. **PrintWindow 路径**（mode != BitBltOnly）：按 winRect 尺寸建 Bitmap，`Graphics.GetHdc()` → `PrintWindow(hwnd, hdc, 0x00000002 PW_RENDERFULLCONTENT)` → `ReleaseHdc`。该步在独立后台线程执行，调用线程 `Join(3000)` 等待：
+   - 超时且线程未完成 → **不 Dispose 位图**，将 attempt 登记到静态「迟到回收表」（`LateAttempts`，含位图与完成/放弃标志，锁保护）；迟到线程在自己的 finally 段（锁内）发现 Abandoned 后自行 Dispose 并摘除。等待与迟到完成恰好相撞时以锁串行化：若线程已 Finished 则按完成结果继续处理，不误登记。超时后 Auto 档走 BitBlt 回退；PrintWindowOnly 档直接 Failed 并注明「PrintWindow 超时（3 秒）」。
+   - 返回 false → 回退（PrintWindowOnly → Failed）。
+   - 成功 → `ComputeCrop` 裁边：Valid 且裁剪区与整幅不同才 `Clone` 产生新图并 Dispose 原图；无效用整幅。
+   - 结果图 `IsSuspect` → Dispose 并回退（PrintWindowOnly → SuspectProtected）。
+3. **BitBlt 回退**（mode != PrintWindowOnly）：`SetForegroundWindow` + `BringWindowToTop` 后以 `GetForegroundWindow()` 核查是否等于 hwnd，不等 → 等 100ms 再查一次，仍不等 → Failed，Message 为简报原文「窗口置前被拒，请手动点开目标窗口后重试」；selfBounds 与 dwmRect 相交 → `hideSelf()`、`Thread.Sleep(60)`；`Graphics.CopyFromScreen(dwmRect.Location, Point.Empty, dwmRect.Size)` 按物理像素抓 dwmRect；`finally showSelf()`（隐藏过才恢复，恢复异常不掩盖截图结果）。抓后 `IsSuspect` → Dispose、SuspectProtected（Message 注明「截图疑似黑图，可能受系统保护」）；否则 Ok。
+4. **截图后复核**（两路都查）：再次 `GetWindowRect` 与开始时比对，窗口不在或位置/尺寸变化 → Dispose、WindowGone，Message「窗口已移动，请重试」。
+5. **资源纪律**：中间 Bitmap/HDC 全部 using/finally 释放；返回的 Image 所有权归调用方；异常兜底捕获转 Failed（MainForm 侧再包一层异常转 Failed）。
+
+注：简报 C 节接口签名按计划文档口径落地为实例类 `WindowCapture`（非 `IWindowCapture` 接口，与简报给出的 `public sealed class WindowCapture` 一致）。
+
+## MainForm 接线（简报 D 节）
+
+- `OnTargetResolved(result, requestId)`：调用方已 `BeginCapture` 进 Capturing。本方法组装 `(result.Hwnd, _config.CaptureMode, this.Bounds)`，起后台工作线程执行 `WindowCapture.Capture`；`hideSelf/showSelf` 以 `Invoke`（阻塞式）切 UI 线程改 `Opacity`（0 / 1）并等待完成，满足「类内部不开新线程做 UI 操作、hide/show 由注入方切线程」口径。
+- 结果以 `BeginInvoke` 回 UI 线程 `OnCaptureCompleted`：**先做迟到校验**——requestId 与状态机 `CurrentRequestId` 不一致或状态已非 Capturing → 直接 Dispose 图并丢弃，不反馈不转移。
+- 分支反馈：Ok → 成功反馈「已截图：<标题>（保存阶段 5 接入）」并 Dispose 图、`CompleteCapture` 回 Idle；SuspectProtected → 错误反馈「该窗口可能受系统保护，截不到画面」、`FailCapture`；WindowGone → 反馈其 Message、`FailCapture`；Failed → 反馈其 Message、`FailCapture`。全部回 Idle 路径都经既有 `OnStateChanged` 集中点（停钩幂等）。
+
+## 补接线：Waiting 会话代次守卫（Task 3 审查 M-3，本任务必做项）
+
+做法（事件携带会话号方案）：
+- `HookManager` 复用其内部既有钩子会话号（`HookSession.Id`，每次新建会话自增）：`Start(Rectangle)` 改为返回 `int` 会话号——新建会话返回新号；已在运行（Waiting 内切换设备）只更新 SelfBounds 并返回**现行**会话号，不误伤当前会话。
+- `ExternalClick` 事件签名由 `Action<Point>` 改为 `Action<Point, int>`：Up 投递时在闭包内捕获所属 `session.Id` 随事件带回（投递走 SynchronizationContext.Post，值在投递时刻已固化，不受后续会话变化影响）。
+- `MainForm` 新增字段 `_waitingSessionId`：`OnStateChanged` 进 Waiting 成功 Start 后记录返回值；`RestartHooksWhileWaiting`（Stop+Start 重启）后更新为新会话号；Start 失败路径清 0。`OnHookExternalClick(pt, sessionId)` 在切回 UI 线程后、状态检查之前先比对 `sessionId != _waitingSessionId` → 直接丢弃（不解析窗口、不转移状态、不重启钩子）。
+- CancelRequested / TimeoutElapsed 未携带会话号：二者迟到最坏后果是多一次 Cancel（状态机非 Waiting 时 Cancel 返回 false 且无副作用）或超时后自停，风险与本守卫目标（迟到点击误触发截图）不同，本轮按任务书范围只守 ExternalClick。
+
+## 遗留（待 Windows 实机，本机仅编译通过）
+
+PrintWindow/BitBlt/CopyFromScreen/钩子会话号投递均为 Windows 专属路径，本机 Mono 只保证编译，下列逐项待实机（对应开发步骤阶段 4 过关标准）：
+
+1. **七格矩阵逐格验收**：目标窗口分别处于「被其他窗口遮一半、完全未遮挡、位于右下角与小组件重叠、在副屏（负坐标）上、所在屏缩放 150%、最大化、普通窗口化」七种情形下，产出图均须完整、无阴影黑边、无小组件残影且文字清晰；黑图路径须给提示且不落盘黑图。
+2. **补测六项**：跨屏不同缩放、纯色合法窗口、目标移动（本实现对应 WindowGone「窗口已移动，请重试」分支）、置前被拒（对应 Failed 文案分支）、窗口部分出屏、PrintWindow 阻塞（对应 3 秒超时 + 迟到回收表路径，须实测迟到位图被回收、无句柄/内存泄漏、`LateAttemptCount` 归零）。
+3. **DWM 裁边实机核对**：透明边窗口（资源管理器/浏览器类）裁后无透明边、无内容截断；DWM 查询失败回退整幅的行为核对。
+4. **M-3 守卫实机验证**：上一 Waiting 会话迟到的 ExternalClick（如重启钩子瞬间在途的点击）被丢弃、新会话点击正常进 Capturing；Waiting 内切换设备不误杀当前会话事件。
+5. **BitBlt 自遮挡时序**：组件与目标重叠时 Opacity 隐藏 + 60ms 等待足够消除残影；置前核查在 UAC/管理员窗口组合下行为（组件为管理员启动，置前一般应成功，被拒分支待触发验证）。
+6. **PrintWindowOnly / BitBltOnly 强制档**三档模式实机各跑一轮：Only 档不切换方法、超时/黑图分别落 Failed / SuspectProtected。
+7. **R4 既有偏差**（计划裁决，非本任务新增）：黑图无独立预览确认窗，回退亦黑时提示不落盘；待实机评估后补。
