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
        }
    }
}
