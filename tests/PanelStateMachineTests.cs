using System.Drawing;
using InspectionWidget.Capture;
using InspectionWidget.Native;

namespace InspectionWidget.Tests
{
    /// <summary>PanelStateMachine 全部转移：三态、取消/切换语义、请求号递增、非法转移拒绝。</summary>
    public static class PanelStateMachineTests
    {
        public static void RunAll()
        {
            TestRunner.Run("状态机：初始为 Idle、无设备、请求号 0", () =>
            {
                var sm = new PanelStateMachine();
                Assert.Equal(PanelState.Idle, sm.State);
                Assert.Null(sm.CurrentDevice);
                Assert.Equal(0, sm.CurrentRequestId);
            });

            TestRunner.Run("状态机：Idle 点设备 → Waiting 并记录设备", () =>
            {
                var sm = new PanelStateMachine();
                Assert.True(sm.SelectDevice("设备A"));
                Assert.Equal(PanelState.Waiting, sm.State);
                Assert.Equal("设备A", sm.CurrentDevice);
            });

            TestRunner.Run("状态机：Waiting 再点同设备 → 取消回 Idle 并清设备", () =>
            {
                var sm = new PanelStateMachine();
                sm.SelectDevice("设备A");
                Assert.True(sm.SelectDevice("设备A"));
                Assert.Equal(PanelState.Idle, sm.State);
                Assert.Null(sm.CurrentDevice);
            });

            TestRunner.Run("状态机：Waiting 点异设备 → 切换并保持 Waiting", () =>
            {
                var sm = new PanelStateMachine();
                sm.SelectDevice("设备A");
                Assert.True(sm.SelectDevice("设备B"));
                Assert.Equal(PanelState.Waiting, sm.State);
                Assert.Equal("设备B", sm.CurrentDevice);
            });

            TestRunner.Run("状态机：Waiting Cancel → Idle 并清设备", () =>
            {
                var sm = new PanelStateMachine();
                sm.SelectDevice("设备A");
                Assert.True(sm.Cancel());
                Assert.Equal(PanelState.Idle, sm.State);
                Assert.Null(sm.CurrentDevice);
            });

            TestRunner.Run("状态机：Idle Cancel → false 且不变", () =>
            {
                var sm = new PanelStateMachine();
                Assert.False(sm.Cancel());
                Assert.Equal(PanelState.Idle, sm.State);
            });

            TestRunner.Run("状态机：Idle BeginCapture → false 且不变、请求号不变", () =>
            {
                var sm = new PanelStateMachine();
                Assert.False(sm.BeginCapture());
                Assert.Equal(PanelState.Idle, sm.State);
                Assert.Equal(0, sm.CurrentRequestId);
            });

            TestRunner.Run("状态机：Waiting BeginCapture → Capturing，请求号从 1 起", () =>
            {
                var sm = new PanelStateMachine();
                sm.SelectDevice("设备A");
                Assert.True(sm.BeginCapture());
                Assert.Equal(PanelState.Capturing, sm.State);
                Assert.Equal("设备A", sm.CurrentDevice);
                Assert.Equal(1, sm.CurrentRequestId);
            });

            TestRunner.Run("状态机：第二次 BeginCapture 请求号递增到 2", () =>
            {
                var sm = new PanelStateMachine();
                sm.SelectDevice("设备A");
                sm.BeginCapture();
                sm.CompleteCapture();
                sm.SelectDevice("设备B");
                Assert.True(sm.BeginCapture());
                Assert.Equal(2, sm.CurrentRequestId);
                Assert.Equal("设备B", sm.CurrentDevice);
            });

            TestRunner.Run("状态机：Capturing 中 SelectDevice 被拒且不变", () =>
            {
                var sm = new PanelStateMachine();
                sm.SelectDevice("设备A");
                sm.BeginCapture();
                Assert.False(sm.SelectDevice("设备B"));
                Assert.False(sm.SelectDevice("设备A"));
                Assert.Equal(PanelState.Capturing, sm.State);
                Assert.Equal("设备A", sm.CurrentDevice);
                Assert.Equal(1, sm.CurrentRequestId);
            });

            TestRunner.Run("状态机：Capturing 中 Cancel 被拒（处理中不可取消）", () =>
            {
                var sm = new PanelStateMachine();
                sm.SelectDevice("设备A");
                sm.BeginCapture();
                Assert.False(sm.Cancel());
                Assert.Equal(PanelState.Capturing, sm.State);
                Assert.Equal("设备A", sm.CurrentDevice);
            });

            TestRunner.Run("状态机：Capturing 中 BeginCapture 重入 → false 且请求号不变", () =>
            {
                var sm = new PanelStateMachine();
                sm.SelectDevice("设备A");
                sm.BeginCapture();
                Assert.False(sm.BeginCapture());
                Assert.Equal(PanelState.Capturing, sm.State);
                Assert.Equal(1, sm.CurrentRequestId);
            });

            TestRunner.Run("状态机：Capturing CompleteCapture → Idle 并清设备", () =>
            {
                var sm = new PanelStateMachine();
                sm.SelectDevice("设备A");
                sm.BeginCapture();
                Assert.True(sm.CompleteCapture());
                Assert.Equal(PanelState.Idle, sm.State);
                Assert.Null(sm.CurrentDevice);
            });

            TestRunner.Run("状态机：Capturing FailCapture → Idle 并清设备", () =>
            {
                var sm = new PanelStateMachine();
                sm.SelectDevice("设备A");
                sm.BeginCapture();
                Assert.True(sm.FailCapture());
                Assert.Equal(PanelState.Idle, sm.State);
                Assert.Null(sm.CurrentDevice);
            });

            TestRunner.Run("状态机：Idle 时迟到 CompleteCapture → false 不抛异常", () =>
            {
                var sm = new PanelStateMachine();
                Assert.False(sm.CompleteCapture());
                Assert.Equal(PanelState.Idle, sm.State);
            });

            TestRunner.Run("状态机：Idle 时迟到 FailCapture → false 不抛异常", () =>
            {
                var sm = new PanelStateMachine();
                Assert.False(sm.FailCapture());
                Assert.Equal(PanelState.Idle, sm.State);
            });

            TestRunner.Run("状态机：Waiting 时 CompleteCapture → false 且保持 Waiting", () =>
            {
                var sm = new PanelStateMachine();
                sm.SelectDevice("设备A");
                Assert.False(sm.CompleteCapture());
                Assert.Equal(PanelState.Waiting, sm.State);
                Assert.Equal("设备A", sm.CurrentDevice);
            });

            TestRunner.Run("状态机：Waiting 时 FailCapture → false 且保持 Waiting", () =>
            {
                var sm = new PanelStateMachine();
                sm.SelectDevice("设备A");
                Assert.False(sm.FailCapture());
                Assert.Equal(PanelState.Waiting, sm.State);
                Assert.Equal("设备A", sm.CurrentDevice);
            });

            TestRunner.Run("状态机：取消后可重新选择设备", () =>
            {
                var sm = new PanelStateMachine();
                sm.SelectDevice("设备A");
                sm.Cancel();
                Assert.True(sm.SelectDevice("设备B"));
                Assert.Equal(PanelState.Waiting, sm.State);
                Assert.Equal("设备B", sm.CurrentDevice);
            });

            TestRunner.Run("状态机：失败后可重新走完整流程且请求号继续递增", () =>
            {
                var sm = new PanelStateMachine();
                sm.SelectDevice("设备A");
                sm.BeginCapture();
                sm.FailCapture();
                sm.SelectDevice("设备A");
                Assert.True(sm.BeginCapture());
                Assert.Equal(2, sm.CurrentRequestId);
                Assert.True(sm.CompleteCapture());
                Assert.Equal(PanelState.Idle, sm.State);
            });

            TestRunner.Run("钩子：未启动与重复 Dispose 安全无异常", () =>
            {
                var hooks = new HookManager();
                Assert.False(hooks.UpdateSelfBounds(new System.Drawing.Rectangle(10, 20, 100, 200)));
                hooks.Stop();
                hooks.Dispose();
                hooks.Dispose();
            });

            TestRunner.Run("钩子：右键取消成对吞掉 Down/Up 且在 Stop 后仍吞掉配对 Up 防弹菜单", () =>
            {
                using (var hooks = new HookManager())
                {
                    int cancelCount = 0;
                    hooks.CancelRequested += delegate
                    {
                        cancelCount++;
                        hooks.Stop();
                    };

                    var session = hooks.CreateSessionForTests(new Rectangle(0, 0, 100, 100));

                    // 孤立 Up 不吞
                    Assert.False(hooks.HandleMouseMessage(session, NativeMethods.WM_RBUTTONUP, new Point(200, 200)));

                    // 任意位置右键按下：吞掉并触发一次 CancelRequested，且 Stop() 因等 Up 而延迟收尾
                    Assert.True(hooks.HandleMouseMessage(session, NativeMethods.WM_RBUTTONDOWN, new Point(200, 200)));
                    Assert.Equal(1, cancelCount);
                    Assert.True(session.TeardownRequested);
                    Assert.True(session.SwallowNextRightUp);
                    Assert.False(session.TornDown);

                    // TeardownRequested 期间再次右键按下放行，不误吞新操作
                    Assert.False(hooks.HandleMouseMessage(session, NativeMethods.WM_RBUTTONDOWN, new Point(200, 200)));

                    // 配对 WM_RBUTTONUP 到达：吞掉并完成收尾
                    Assert.True(hooks.HandleMouseMessage(session, NativeMethods.WM_RBUTTONUP, new Point(200, 200)));
                    Assert.False(session.SwallowNextRightUp);
                    Assert.True(session.TornDown);
                    Assert.False(hooks.IsRunning);
                }
            });

            TestRunner.Run("钩子：右键按下组件内部同样取消并吞掉配对 Up；Esc 取消与延迟丢弃", () =>
            {
                using (var hooks = new HookManager())
                {
                    int cancelCount = 0;
                    hooks.CancelRequested += delegate { cancelCount++; };

                    var selfBounds = new Rectangle(10, 10, 200, 300);
                    var session = hooks.CreateSessionForTests(selfBounds);

                    // 组件内部左键放行，右键依然取消并成对吞掉
                    Assert.False(hooks.HandleMouseMessage(session, NativeMethods.WM_LBUTTONDOWN, new Point(50, 50)));
                    Assert.True(hooks.HandleMouseMessage(session, NativeMethods.WM_RBUTTONDOWN, new Point(50, 50)));
                    Assert.Equal(1, cancelCount);
                    Assert.True(hooks.HandleMouseMessage(session, NativeMethods.WM_RBUTTONUP, new Point(50, 50)));

                    // 新会话中 Esc 取消；Stop 后 Esc 放行
                    var session2 = hooks.CreateSessionForTests(selfBounds);
                    Assert.True(hooks.HandleKeyboardMessage(session2, NativeMethods.WM_KEYDOWN, NativeMethods.VK_ESCAPE));
                    Assert.Equal(2, cancelCount);
                    hooks.Stop();
                    Assert.False(hooks.HandleKeyboardMessage(session2, NativeMethods.WM_KEYDOWN, NativeMethods.VK_ESCAPE));
                    Assert.Equal(2, cancelCount);
                }
            });
        }
    }
}
