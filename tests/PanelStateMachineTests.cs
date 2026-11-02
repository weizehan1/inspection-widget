using InspectionWidget.Capture;

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
        }
    }
}
