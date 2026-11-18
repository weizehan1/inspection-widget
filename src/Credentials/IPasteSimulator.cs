namespace InspectionWidget.Credentials
{
    /// <summary>向当前前台焦点窗口模拟发送粘贴按键（Ctrl+V）。</summary>
    public interface IPasteSimulator
    {
        bool TryPaste(out string error);
    }
}
