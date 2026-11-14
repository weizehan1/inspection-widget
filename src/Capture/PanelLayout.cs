using System;
using System.Collections.Generic;
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

        // 标题栏右侧按钮（v1.0.2 起三枚：设置/最小化/关闭）：边长 28、右边距 6、槽距 32、顶距 4。
        public const int TitleBarButtonSize = 28;
        public const int TitleBarButtonRightMargin = 6;
        public const int TitleBarButtonStride = 32;
        public const int TitleBarButtonTop = 4;

        // v1.1.0 方案 B（Scheme B）主面板度量常量（96 DPI 逻辑像素）。
        public const int SchemeBPanelWidth = 352;
        public const int SchemeBDefaultHeight = 660;
        public const int SchemeBHeaderHeight = 60;
        public const int SchemeBQuickLinkHeight = 34;
        public const int SchemeBQuickLinkGap = 7;
        public const int SchemeBCredentialHeight = 34;
        public const int SchemeBCredentialGap = 7;
        public const int SchemeBDeviceRowHeight = 44;
        public const int SchemeBDeviceRowGap = 7;
        public const int SchemeBFooterHeight = 104;

        /// <summary>v1.1.0 方案 B 缩放后的面板宽（352 * factor）。</summary>
        public static int ScaledSchemeBPanelWidth(double factor)
        {
            return Scale(SchemeBPanelWidth, factor);
        }

        /// <summary>
        /// v1.1.0 方案 B 面板目标高度：随内容自然高度收缩至最多 660（逻辑像素），
        /// 再受当前工作区可用高度与外边距钳制，不强行设置固定最小高度。
        /// </summary>
        public static int ComputeSchemeBHeight(int naturalContentHeight, int workingAreaHeight, int margin, double factor)
        {
            int maxPreferred = Scale(SchemeBDefaultHeight, factor);
            int desired = Math.Min(Math.Max(0, naturalContentHeight), maxPreferred);
            return ClampHeight(desired, workingAreaHeight, margin);
        }

        /// <summary>
        /// 标题栏右侧按钮左缘 X：slotFromRight=0 为最右一枚。每令牌先各自取整再组合，
        /// 与全窗体缩放口径一致。1.0 档三槽为 W-34 / W-66 / W-98。
        /// </summary>
        public static int TitleBarButtonX(int panelWidth, int slotFromRight, double factor)
        {
            int inset = Scale(TitleBarButtonRightMargin + TitleBarButtonSize, factor)
                + slotFromRight * Scale(TitleBarButtonStride, factor);
            return panelWidth - inset;
        }

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
            return DeviceZoneHeight(deviceCount, false, factor);
        }

        /// <summary>设备区高度（v1.0.3 折叠重载）：折叠时 0，与凭据区折叠口径一致。</summary>
        public static int DeviceZoneHeight(int deviceCount, bool collapsed, double factor)
        {
            if (collapsed)
                return 0;
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
            return ComputeHeight(deviceCount, false, credentialCount, collapsed, factor);
        }

        /// <summary>
        /// 双折叠版面板总高（v1.0.3）：截图区与凭据区各自可折叠、互不影响；
        /// 两区都折叠时只剩标题栏＋两行分区标题＋底部状态区。
        /// </summary>
        public static int ComputeHeight(int deviceCount, bool deviceCollapsed, int credentialCount, bool credentialCollapsed, double factor)
        {
            return Scale(TitleBarHeight, factor)
                + Scale(SectionHeaderHeight, factor)
                + DeviceZoneHeight(deviceCount, deviceCollapsed, factor)
                + Scale(SectionHeaderHeight, factor)
                + CredentialZoneHeight(credentialCount, credentialCollapsed, factor)
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
        /// 面板高受当前工作区限制：长列表交给内容区滚动，标题与底栏保持可见。
        /// margin 为工作区上下预留，结果不小于 0。
        /// </summary>
        public static int ClampHeight(int desiredHeight, int workingAreaHeight, int margin)
        {
            int max = Math.Max(0, workingAreaHeight - Math.Max(0, margin));
            if (desiredHeight < 0)
                desiredHeight = 0;
            return Math.Min(desiredHeight, max);
        }

        /// <summary>
        /// 面板落到当前工作区时的最终尺寸：宽度和高度都不得超过工作区，
        /// 高度再受 margin 约束；用于拖动跨屏与 DPI 变化后的统一钳制。
        /// </summary>
        public static Size ClampPanelSize(
            Rectangle workingArea, int desiredWidth, int desiredHeight, int margin)
        {
            int width = Math.Max(0, Math.Min(desiredWidth, workingArea.Width));
            int height = ClampHeight(desiredHeight, workingArea.Height, margin);
            return new Size(width, height);
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

        /// <summary>
        /// 恢复窗口位置时要求候选矩形与任一工作区相交面积至少过半；
        /// 命中则返回候选左上角，否则返回 false 让调用方保留当前位置。
        /// </summary>
        public static bool TryChooseVisibleWindowLocation(
            IList<Rectangle> workingAreas, Rectangle candidate, out Point location)
        {
            location = candidate.Location;
            long candidateArea = (long)candidate.Width * candidate.Height;
            if (workingAreas == null || candidateArea <= 0)
                return false;
            for (int i = 0; i < workingAreas.Count; i++)
            {
                Rectangle overlap = Rectangle.Intersect(workingAreas[i], candidate);
                long overlapArea = (long)overlap.Width * overlap.Height;
                if (overlapArea * 2 >= candidateArea)
                {
                    location = ClampLocationToWorkingArea(
                        workingAreas[i], candidate.Size, candidate.Location);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 把候选左上角钳制到指定工作区；窗体大于工作区时对齐工作区左上，
        /// 保证导入或旧配置里的坐标不会让面板跑出屏幕。
        /// </summary>
        public static Point ClampLocationToWorkingArea(
            Rectangle workingArea, Size formSize, Point location)
        {
            int maxX = workingArea.Right - formSize.Width;
            int maxY = workingArea.Bottom - formSize.Height;
            int x = maxX >= workingArea.Left
                ? Math.Min(Math.Max(location.X, workingArea.Left), maxX)
                : workingArea.Left;
            int y = maxY >= workingArea.Top
                ? Math.Min(Math.Max(location.Y, workingArea.Top), maxY)
                : workingArea.Top;
            return new Point(x, y);
        }
    }
}
