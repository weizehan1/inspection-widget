using System;

namespace InspectionWidget.Storage
{
    /// <summary>
    /// 时钟抽象：截图日期归属在采集开始时由 MainForm 以 IClock 固定，
    /// 保存层只用传入的 captureDate 命名，跨午夜不串日；测试以 FakeClock 注入。
    /// </summary>
    public interface IClock
    {
        DateTime Now { get; }
    }

    /// <summary>生产时钟：系统本地时间。</summary>
    public sealed class SystemClock : IClock
    {
        public DateTime Now
        {
            get { return DateTime.Now; }
        }
    }
}
