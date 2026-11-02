namespace InspectionWidget.Capture
{
    /// <summary>主面板三态：空闲 / 等待点选目标窗口 / 正在截图。</summary>
    public enum PanelState
    {
        Idle,
        Waiting,
        Capturing
    }

    /// <summary>
    /// 截图流程状态机（纯逻辑）。非法转移一律返回 false 且状态不变、不抛异常，
    /// 以便迟到的完成/失败回调被安全忽略。
    /// </summary>
    public sealed class PanelStateMachine
    {
        public PanelState State { get; private set; }

        /// <summary>当前选中的设备名；仅 Waiting/Capturing 时有效，其余为 null。</summary>
        public string CurrentDevice { get; private set; }

        /// <summary>截图请求号：每次 BeginCapture 成功递增，从 1 起。</summary>
        public int CurrentRequestId { get; private set; }

        public PanelStateMachine()
        {
            State = PanelState.Idle;
            CurrentDevice = null;
            CurrentRequestId = 0;
        }

        /// <summary>
        /// 点设备按钮：Idle → Waiting 并记录设备；
        /// Waiting 点同一设备 → 取消回 Idle（返回 true，调用方以 State 区分取消语义）；
        /// Waiting 点另一设备 → 切换设备并保持 Waiting；
        /// Capturing → 返回 false，状态不变。
        /// </summary>
        public bool SelectDevice(string device)
        {
            if (string.IsNullOrEmpty(device))
                return false;

            switch (State)
            {
                case PanelState.Idle:
                    CurrentDevice = device;
                    State = PanelState.Waiting;
                    return true;
                case PanelState.Waiting:
                    if (CurrentDevice == device)
                    {
                        CurrentDevice = null;
                        State = PanelState.Idle;
                    }
                    else
                    {
                        CurrentDevice = device;
                    }
                    return true;
                default: // Capturing
                    return false;
            }
        }

        /// <summary>取消等待：Waiting → Idle；Capturing 处理中不可取消、Idle 无可取消，均返回 false。</summary>
        public bool Cancel()
        {
            if (State != PanelState.Waiting)
                return false;
            CurrentDevice = null;
            State = PanelState.Idle;
            return true;
        }

        /// <summary>开始截图：Waiting → Capturing 并递增请求号；其他态返回 false。</summary>
        public bool BeginCapture()
        {
            if (State != PanelState.Waiting)
                return false;
            CurrentRequestId++;
            State = PanelState.Capturing;
            return true;
        }

        /// <summary>截图完成：Capturing → Idle 并清设备；其他态（迟到回调）返回 false。</summary>
        public bool CompleteCapture()
        {
            if (State != PanelState.Capturing)
                return false;
            CurrentDevice = null;
            State = PanelState.Idle;
            return true;
        }

        /// <summary>截图失败：Capturing → Idle 并清设备；其他态（迟到回调）返回 false。</summary>
        public bool FailCapture()
        {
            if (State != PanelState.Capturing)
                return false;
            CurrentDevice = null;
            State = PanelState.Idle;
            return true;
        }
    }
}
