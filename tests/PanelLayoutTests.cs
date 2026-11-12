using InspectionWidget.Capture;

namespace InspectionWidget.Tests
{
    /// <summary>PanelLayout 布局令牌与高度账（设计稿锚点逐个断言）。</summary>
    public static class PanelLayoutTests
    {
        public static void RunAll()
        {
            TestRunner.Run("布局常量：面板宽 230", () =>
                Assert.Equal(230, PanelLayout.PanelWidth));
            TestRunner.Run("布局常量：标题栏 36", () =>
                Assert.Equal(36, PanelLayout.TitleBarHeight));
            TestRunner.Run("布局常量：分区标题行 28", () =>
                Assert.Equal(28, PanelLayout.SectionHeaderHeight));
            TestRunner.Run("布局常量：底部状态区 58", () =>
                Assert.Equal(58, PanelLayout.BottomStatusHeight));
            TestRunner.Run("布局常量：设备行高 33、行距 5", () =>
            {
                Assert.Equal(33, PanelLayout.DeviceRowHeight);
                Assert.Equal(5, PanelLayout.DeviceRowGap);
            });
            TestRunner.Run("布局常量：凭据行高 33、行距 8", () =>
            {
                Assert.Equal(33, PanelLayout.CredentialRowHeight);
                Assert.Equal(8, PanelLayout.CredentialRowGap);
            });
            TestRunner.Run("布局常量：空态提示行高 33", () =>
                Assert.Equal(33, PanelLayout.EmptyRowHeight));

            TestRunner.Run("设备区：0 台 = 33（提示行）", () =>
                Assert.Equal(33, PanelLayout.DeviceZoneHeight(0)));
            TestRunner.Run("设备区：1 台 = 33", () =>
                Assert.Equal(33, PanelLayout.DeviceZoneHeight(1)));
            TestRunner.Run("设备区：6 台 = 6*33+5*5 = 223", () =>
                Assert.Equal(223, PanelLayout.DeviceZoneHeight(6)));
            TestRunner.Run("设备区：8 台 = 8*33+7*5 = 299", () =>
                Assert.Equal(299, PanelLayout.DeviceZoneHeight(8)));
            TestRunner.Run("设备区：10 台 = 10*33+9*5 = 375", () =>
                Assert.Equal(375, PanelLayout.DeviceZoneHeight(10)));

            TestRunner.Run("凭据区：收起恒为 0（0 条）", () =>
                Assert.Equal(0, PanelLayout.CredentialZoneHeight(0, true)));
            TestRunner.Run("凭据区：收起恒为 0（10 条）", () =>
                Assert.Equal(0, PanelLayout.CredentialZoneHeight(10, true)));
            TestRunner.Run("凭据区：展开 0 条 = 33（提示行）", () =>
                Assert.Equal(33, PanelLayout.CredentialZoneHeight(0, false)));
            TestRunner.Run("凭据区：展开 1 条 = 33", () =>
                Assert.Equal(33, PanelLayout.CredentialZoneHeight(1, false)));
            TestRunner.Run("凭据区：展开 5 条 = 5*33+4*8 = 197", () =>
                Assert.Equal(197, PanelLayout.CredentialZoneHeight(5, false)));
            TestRunner.Run("凭据区：展开 10 条 = 10*33+9*8 = 402", () =>
                Assert.Equal(402, PanelLayout.CredentialZoneHeight(10, false)));

            TestRunner.Run("总高锚点：(8,10,展开) = 851", () =>
                Assert.Equal(851, PanelLayout.ComputeHeight(8, 10, false)));
            TestRunner.Run("总高锚点：(8,10,收起) = 449", () =>
                Assert.Equal(449, PanelLayout.ComputeHeight(8, 10, true)));
            TestRunner.Run("总高锚点：(6,5,展开) = 570", () =>
                Assert.Equal(570, PanelLayout.ComputeHeight(6, 5, false)));
            TestRunner.Run("总高锚点：(6,5,收起) = 373", () =>
                Assert.Equal(373, PanelLayout.ComputeHeight(6, 5, true)));
            TestRunner.Run("总高锚点：(10,10,展开) = 927", () =>
                Assert.Equal(927, PanelLayout.ComputeHeight(10, 10, false)));
            TestRunner.Run("总高锚点：(10,10,收起) = 525", () =>
                Assert.Equal(525, PanelLayout.ComputeHeight(10, 10, true)));
            TestRunner.Run("总高锚点：(6,0,展开) = 36+28+223+28+33+58 = 406", () =>
                Assert.Equal(406, PanelLayout.ComputeHeight(6, 0, false)));
            TestRunner.Run("总高锚点：(0,0,展开) = 36+28+33+28+33+58 = 216", () =>
                Assert.Equal(216, PanelLayout.ComputeHeight(0, 0, false)));
            TestRunner.Run("总高锚点：(0,0,收起) = 36+28+33+28+0+58 = 183", () =>
                Assert.Equal(183, PanelLayout.ComputeHeight(0, 0, true)));

            TestRunner.Run("ComputeTop：bottom=1000,height=500,上沿 0 → 500", () =>
                Assert.Equal(500, PanelLayout.ComputeTop(1000, 500, 0)));
            TestRunner.Run("ComputeTop：bottom=400,height=500,上沿 0 → 钳制为 0", () =>
                Assert.Equal(0, PanelLayout.ComputeTop(400, 500, 0)));
            TestRunner.Run("ComputeTop：bottom=400,height=500,上沿 40 → 钳制为 40", () =>
                Assert.Equal(40, PanelLayout.ComputeTop(400, 500, 40)));
            TestRunner.Run("ComputeTop：恰好贴上沿不钳制（bottom=540,height=500,上沿 40 → 40）", () =>
                Assert.Equal(40, PanelLayout.ComputeTop(540, 500, 40)));
            TestRunner.Run("ComputeTop：高度不变时 Top 不变（bottom=900,height=449 → 451）", () =>
                Assert.Equal(451, PanelLayout.ComputeTop(900, 449, 0)));

            // ---------- v1.0.1：缩放度量与居中 ----------
            TestRunner.Run("缩放度量：Scale 取整（58@1.25=73、33@1.5=50、36@1.25=45、5@1.5=8）", () =>
            {
                Assert.Equal(73, PanelLayout.Scale(58, 1.25));
                Assert.Equal(50, PanelLayout.Scale(33, 1.5));
                Assert.Equal(45, PanelLayout.Scale(36, 1.25));
                Assert.Equal(8, PanelLayout.Scale(5, 1.5));
                Assert.Equal(33, PanelLayout.Scale(33, 1.0));
            });
            TestRunner.Run("缩放度量：面板宽 1.25→288、1.5→345、2.0→460", () =>
            {
                Assert.Equal(288, PanelLayout.ScaledPanelWidth(1.25));
                Assert.Equal(345, PanelLayout.ScaledPanelWidth(1.5));
                Assert.Equal(460, PanelLayout.ScaledPanelWidth(2.0));
            });
            TestRunner.Run("缩放设备区：8 台 @1.25=370、@1.5=456、@2.0=598；0 台 @1.5=50", () =>
            {
                Assert.Equal(370, PanelLayout.DeviceZoneHeight(8, 1.25));
                Assert.Equal(456, PanelLayout.DeviceZoneHeight(8, 1.5));
                Assert.Equal(598, PanelLayout.DeviceZoneHeight(8, 2.0));
                Assert.Equal(50, PanelLayout.DeviceZoneHeight(0, 1.5));
            });
            TestRunner.Run("缩放凭据区：10 条展开 @1.25=500、@1.5=608、@2.0=804；收起恒 0；0 条 @2.0=66", () =>
            {
                Assert.Equal(500, PanelLayout.CredentialZoneHeight(10, false, 1.25));
                Assert.Equal(608, PanelLayout.CredentialZoneHeight(10, false, 1.5));
                Assert.Equal(804, PanelLayout.CredentialZoneHeight(10, false, 2.0));
                Assert.Equal(0, PanelLayout.CredentialZoneHeight(10, true, 1.5));
                Assert.Equal(66, PanelLayout.CredentialZoneHeight(0, false, 2.0));
            });
            TestRunner.Run("缩放总高锚点：(8,10,展开) @1.25=1058、@1.5=1289、@2.0=1702", () =>
            {
                Assert.Equal(1058, PanelLayout.ComputeHeight(8, 10, false, 1.25));
                Assert.Equal(1289, PanelLayout.ComputeHeight(8, 10, false, 1.5));
                Assert.Equal(1702, PanelLayout.ComputeHeight(8, 10, false, 2.0));
            });
            TestRunner.Run("缩放总高锚点：(6,5,展开) @1.25=709、@1.5=863；(8,10,收起) @1.5=681；(0,0,展开) @2.0=432", () =>
            {
                Assert.Equal(709, PanelLayout.ComputeHeight(6, 5, false, 1.25));
                Assert.Equal(863, PanelLayout.ComputeHeight(6, 5, false, 1.5));
                Assert.Equal(681, PanelLayout.ComputeHeight(8, 10, true, 1.5));
                Assert.Equal(432, PanelLayout.ComputeHeight(0, 0, false, 2.0));
            });
            TestRunner.Run("居中：1920x1040 工作区放 744x430 → (588,305)", () =>
            {
                var pt = PanelLayout.CenterInWorkingArea(
                    new System.Drawing.Rectangle(0, 0, 1920, 1040),
                    new System.Drawing.Size(744, 430));
                Assert.Equal(588, pt.X);
                Assert.Equal(305, pt.Y);
            });
            TestRunner.Run("居中：副屏工作区带偏移 (1920,0,1920,1080) 放 200x100 → (2780,490)", () =>
            {
                var pt = PanelLayout.CenterInWorkingArea(
                    new System.Drawing.Rectangle(1920, 0, 1920, 1080),
                    new System.Drawing.Size(200, 100));
                Assert.Equal(2780, pt.X);
                Assert.Equal(490, pt.Y);
            });
            TestRunner.Run("居中：窗体大于工作区钳制到工作区左上 (100,50)", () =>
            {
                var pt = PanelLayout.CenterInWorkingArea(
                    new System.Drawing.Rectangle(100, 50, 800, 600),
                    new System.Drawing.Size(1000, 700));
                Assert.Equal(100, pt.X);
                Assert.Equal(50, pt.Y);
            });
            TestRunner.Run("可见位置：至少半屏相交才恢复", () =>
            {
                var areas = new[]
                {
                    new System.Drawing.Rectangle(0, 0, 1920, 1040)
                };
                System.Drawing.Point location;
                Assert.True(PanelLayout.TryChooseVisibleWindowLocation(
                    areas, new System.Drawing.Rectangle(500, 200, 230, 400),
                    out location));
                Assert.Equal(500, location.X);
                Assert.Equal(200, location.Y);
                Assert.False(PanelLayout.TryChooseVisibleWindowLocation(
                    areas, new System.Drawing.Rectangle(1910, 1000, 230, 400),
                    out location));
            });
            TestRunner.Run("可见位置：副屏偏移与空输入", () =>
            {
                var areas = new[]
                {
                    new System.Drawing.Rectangle(0, 0, 1920, 1040),
                    new System.Drawing.Rectangle(1920, 0, 1920, 1040)
                };
                System.Drawing.Point location;
                Assert.True(PanelLayout.TryChooseVisibleWindowLocation(
                    areas, new System.Drawing.Rectangle(2000, 100, 400, 300),
                    out location));
                Assert.Equal(2000, location.X);
                Assert.False(PanelLayout.TryChooseVisibleWindowLocation(
                    null, new System.Drawing.Rectangle(0, 0, 100, 100),
                    out location));
                Assert.False(PanelLayout.TryChooseVisibleWindowLocation(
                    areas, new System.Drawing.Rectangle(0, 0, 0, 100),
                    out location));
            });
            TestRunner.Run("可见位置：半屏相交也会把窗口坐标钳回工作区", () =>
            {
                var areas = new[]
                {
                    new System.Drawing.Rectangle(0, 0, 1920, 1040)
                };
                System.Drawing.Point location;
                Assert.True(PanelLayout.TryChooseVisibleWindowLocation(
                    areas, new System.Drawing.Rectangle(1700, 800, 300, 300),
                    out location));
                Assert.Equal(1620, location.X);
                Assert.Equal(740, location.Y);
            });

            TestRunner.Run("工作区钳制：150% 面板在 1366x768 与 1920x1080 内完整可达", () =>
            {
                int width = PanelLayout.ScaledPanelWidth(1.5);
                int desired = PanelLayout.ComputeHeight(10, false, 10, false, 1.5);
                var small = new System.Drawing.Rectangle(0, 0, 1366, 768);
                System.Drawing.Size size = PanelLayout.ClampPanelSize(
                    small, width, desired, 12);
                Assert.Equal(width, size.Width);
                Assert.Equal(756, size.Height);

                System.Drawing.Point at = PanelLayout.ClampLocationToWorkingArea(
                    small, size, new System.Drawing.Point(1200, 700));
                Assert.Equal(1021, at.X);
                Assert.Equal(12, at.Y);
                Assert.True(at.X >= small.Left && at.X + size.Width <= small.Right);
                Assert.True(at.Y >= small.Top && at.Y + size.Height <= small.Bottom);

                var large = new System.Drawing.Rectangle(1920, 0, 1920, 1080);
                System.Drawing.Size largeSize = PanelLayout.ClampPanelSize(
                    large, width, desired, 12);
                Assert.Equal(width, largeSize.Width);
                Assert.Equal(1068, largeSize.Height);
                System.Drawing.Point largeAt = PanelLayout.ClampLocationToWorkingArea(
                    large, largeSize, new System.Drawing.Point(4000, 1200));
                Assert.Equal(1920 + 1920 - width, largeAt.X);
                Assert.Equal(12, largeAt.Y);
            });
        }
    }
}
