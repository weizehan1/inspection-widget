using System;
using System.Drawing;

namespace InspectionWidget.Capture
{
    /// <summary>
    /// 主面板布局令牌与高度计算（纯逻辑，96 DPI 逻辑像素）。
    /// 总高 = 标题栏 36 + 分区标题 28 + 设备区 + 分区标题 28 + 凭据区 + 底部 58。
    /// v1.0.1 起全部度量可带缩放因子（界面缩放档位）：每个令牌先各自取整再组合，
    /// 与 MainForm 逐控件缩放的口径一致；不带因子的旧签名以 factor=1.0 委托，数值不变。
    /// </summary>
    public static class PanelLayout
    {
        public const int PanelWidth = 230;
        public const int TitleBarHeight = 36;
        public const int SectionHeaderHeight = 28;
        public const int BottomStatusHeight = 58;
        public const int DeviceRowHeight = 33;
        public const int DeviceRowGap = 5;
        public const int CredentialRowHeight = 33;
        public const int CredentialRowGap = 8;
        public const int EmptyRowHeight = 33;

        /// <summary>单个度量按因子缩放并四舍五入（AwayFromZero，0.5 进位）。</summary>
        public static int Scale(int value, double factor)
        {
            return (int)Math.Round(value * factor, MidpointRounding.AwayFromZero);
        }

        /// <summary>缩放后的面板宽。</summary>
        public static int ScaledPanelWidth(double factor)
        {
            return Scale(PanelWidth, factor);
        }

        /// <summary>设备区高度：0 台时为一行空态提示（33），否则 n*33+(n-1)*5。</summary>
        public static int DeviceZoneHeight(int deviceCount)
        {
            return DeviceZoneHeight(deviceCount, 1.0);
        }

        /// <summary>缩放版设备区高度（每令牌先取整再组合）。</summary>
        public static int DeviceZoneHeight(int deviceCount, double factor)
        {
            if (deviceCount <= 0)
                return Scale(EmptyRowHeight, factor);
            return deviceCount * Scale(DeviceRowHeight, factor)
                + (deviceCount - 1) * Scale(DeviceRowGap, factor);
        }

        /// <summary>凭据区高度：折叠时 0；展开 0 条时为一行提示（33），否则 n*33+(n-1)*8。</summary>
        public static int CredentialZoneHeight(int credentialCount, bool collapsed)
        {
            return CredentialZoneHeight(credentialCount, collapsed, 1.0);
        }

        /// <summary>缩放版凭据区高度（每令牌先取整再组合）。</summary>
        public static int CredentialZoneHeight(int credentialCount, bool collapsed, double factor)
        {
            if (collapsed)
                return 0;
            if (credentialCount <= 0)
                return Scale(EmptyRowHeight, factor);
            return credentialCount * Scale(CredentialRowHeight, factor)
                + (credentialCount - 1) * Scale(CredentialRowGap, factor);
        }

        /// <summary>面板总高（ClientSize 高度）。</summary>
        public static int ComputeHeight(int deviceCount, int credentialCount, bool collapsed)
        {
            return ComputeHeight(deviceCount, credentialCount, collapsed, 1.0);
        }

        /// <summary>缩放版面板总高（ClientSize 高度）。</summary>
        public static int ComputeHeight(int deviceCount, int credentialCount, bool collapsed, double factor)
        {
            return Scale(TitleBarHeight, factor)
                + Scale(SectionHeaderHeight, factor)
                + DeviceZoneHeight(deviceCount, factor)
                + Scale(SectionHeaderHeight, factor)
                + CredentialZoneHeight(credentialCount, collapsed, factor)
                + Scale(BottomStatusHeight, factor);
        }

        /// <summary>
        /// 以窗口 Bottom 为锚点反算 Top：Top = Bottom - newHeight，
        /// 且不得越过工作区上沿（钳制到 workingAreaTop）。
        /// </summary>
        public static int ComputeTop(int currentBottom, int newHeight, int workingAreaTop)
        {
            return Math.Max(workingAreaTop, currentBottom - newHeight);
        }

        /// <summary>
        /// 把窗体居中放到工作区中央（设置窗口打开位置，纯函数）：
        /// 窗体大于工作区时钳制到工作区左上角，不溢出。
        /// </summary>
        public static Point CenterInWorkingArea(Rectangle workingArea, Size formSize)
        {
            int x = workingArea.Left + Math.Max(0, (workingArea.Width - formSize.Width) / 2);
            int y = workingArea.Top + Math.Max(0, (workingArea.Height - formSize.Height) / 2);
            return new Point(x, y);
        }
    }
}
