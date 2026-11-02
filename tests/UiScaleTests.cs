using InspectionWidget.Config;

namespace InspectionWidget.Tests
{
    /// <summary>UiScale 档位表、归一、因子换算与下拉索引映射（v1.0.1）。</summary>
    public static class UiScaleTests
    {
        public static void RunAll()
        {
            TestRunner.Run("缩放：默认档位为 100", () =>
                Assert.Equal(100, UiScale.DefaultPercent));

            TestRunner.Run("缩放：合法档位为 100/125/150/175/200 共五档", () =>
            {
                Assert.Equal(5, UiScale.Count);
                Assert.True(UiScale.IsValid(100));
                Assert.True(UiScale.IsValid(125));
                Assert.True(UiScale.IsValid(150));
                Assert.True(UiScale.IsValid(175));
                Assert.True(UiScale.IsValid(200));
            });

            TestRunner.Run("缩放：非法档位判定（0/137/负数/300）", () =>
            {
                Assert.False(UiScale.IsValid(0));
                Assert.False(UiScale.IsValid(137));
                Assert.False(UiScale.IsValid(-100));
                Assert.False(UiScale.IsValid(300));
            });

            TestRunner.Run("缩放：Normalize 合法值原样、非法归 100", () =>
            {
                Assert.Equal(150, UiScale.Normalize(150));
                Assert.Equal(200, UiScale.Normalize(200));
                Assert.Equal(100, UiScale.Normalize(0));
                Assert.Equal(100, UiScale.Normalize(137));
                Assert.Equal(100, UiScale.Normalize(-50));
            });

            TestRunner.Run("缩放：Factor 五档换算", () =>
            {
                Assert.Equal(1.0, UiScale.Factor(100));
                Assert.Equal(1.25, UiScale.Factor(125));
                Assert.Equal(1.5, UiScale.Factor(150));
                Assert.Equal(1.75, UiScale.Factor(175));
                Assert.Equal(2.0, UiScale.Factor(200));
            });

            TestRunner.Run("缩放：Factor 非法值回 1.0", () =>
            {
                Assert.Equal(1.0, UiScale.Factor(0));
                Assert.Equal(1.0, UiScale.Factor(137));
            });

            TestRunner.Run("缩放：下拉索引↔档位映射", () =>
            {
                Assert.Equal(0, UiScale.IndexForPercent(100));
                Assert.Equal(1, UiScale.IndexForPercent(125));
                Assert.Equal(2, UiScale.IndexForPercent(150));
                Assert.Equal(3, UiScale.IndexForPercent(175));
                Assert.Equal(4, UiScale.IndexForPercent(200));
                Assert.Equal(100, UiScale.PercentForIndex(0));
                Assert.Equal(125, UiScale.PercentForIndex(1));
                Assert.Equal(150, UiScale.PercentForIndex(2));
                Assert.Equal(175, UiScale.PercentForIndex(3));
                Assert.Equal(200, UiScale.PercentForIndex(4));
            });

            TestRunner.Run("缩放：索引映射越界钳制（非法档位→首项、负/超界索引→首/末档）", () =>
            {
                Assert.Equal(0, UiScale.IndexForPercent(137));
                Assert.Equal(100, UiScale.PercentForIndex(-1));
                Assert.Equal(200, UiScale.PercentForIndex(99));
            });

            TestRunner.Run("缩放：显示文本为百分比（非法先归一）", () =>
            {
                Assert.Equal("100%", UiScale.TextForPercent(100));
                Assert.Equal("150%", UiScale.TextForPercent(150));
                Assert.Equal("100%", UiScale.TextForPercent(137));
            });
        }
    }
}
