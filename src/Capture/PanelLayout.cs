using System;

namespace InspectionWidget.Capture
{
    /// <summary>
    /// 主面板布局令牌与高度计算（纯逻辑，96 DPI 逻辑像素）。
    /// 总高 = 标题栏 36 + 分区标题 28 + 设备区 + 分区标题 28 + 凭据区 + 底部 58。
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

        /// <summary>设备区高度：0 台时为一行空态提示（33），否则 n*33+(n-1)*5。</summary>
        public static int DeviceZoneHeight(int deviceCount)
        {
            if (deviceCount <= 0)
                return EmptyRowHeight;
            return deviceCount * DeviceRowHeight + (deviceCount - 1) * DeviceRowGap;
        }

        /// <summary>凭据区高度：折叠时 0；展开 0 条时为一行提示（33），否则 n*33+(n-1)*8。</summary>
        public static int CredentialZoneHeight(int credentialCount, bool collapsed)
        {
            if (collapsed)
                return 0;
            if (credentialCount <= 0)
                return EmptyRowHeight;
            return credentialCount * CredentialRowHeight + (credentialCount - 1) * CredentialRowGap;
        }

        /// <summary>面板总高（ClientSize 高度）。</summary>
        public static int ComputeHeight(int deviceCount, int credentialCount, bool collapsed)
        {
            return TitleBarHeight
                + SectionHeaderHeight
                + DeviceZoneHeight(deviceCount)
                + SectionHeaderHeight
                + CredentialZoneHeight(credentialCount, collapsed)
                + BottomStatusHeight;
        }

        /// <summary>
        /// 以窗口 Bottom 为锚点反算 Top：Top = Bottom - newHeight，
        /// 且不得越过工作区上沿（钳制到 workingAreaTop）。
        /// </summary>
        public static int ComputeTop(int currentBottom, int newHeight, int workingAreaTop)
        {
            return Math.Max(workingAreaTop, currentBottom - newHeight);
        }
    }
}
