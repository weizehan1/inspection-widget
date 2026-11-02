using System.Drawing;
using InspectionWidget.Capture;

namespace InspectionWidget.Tests
{
    /// <summary>CaptureGeometry 裁边几何：标准偏移、全幅、负坐标副屏、异常矩形、部分越界夹紧。</summary>
    public static class CaptureGeometryTests
    {
        public static void RunAll()
        {
            TestRunner.Run("几何：标准偏移裁边 (7,0,802,609)", () =>
            {
                CropResult r = CaptureGeometry.ComputeCrop(
                    new Rectangle(100, 100, 816, 616),
                    new Rectangle(107, 100, 802, 609));
                Assert.True(r.Valid);
                Assert.Equal(new Rectangle(7, 0, 802, 609), r.Crop);
            });

            TestRunner.Run("几何：两矩形相同 → 全幅裁边", () =>
            {
                CropResult r = CaptureGeometry.ComputeCrop(
                    new Rectangle(100, 100, 816, 616),
                    new Rectangle(100, 100, 816, 616));
                Assert.True(r.Valid);
                Assert.Equal(new Rectangle(0, 0, 816, 616), r.Crop);
            });

            TestRunner.Run("几何：负坐标副屏偏移一致", () =>
            {
                CropResult r = CaptureGeometry.ComputeCrop(
                    new Rectangle(-1200, 50, 816, 616),
                    new Rectangle(-1193, 50, 802, 609));
                Assert.True(r.Valid);
                Assert.Equal(new Rectangle(7, 0, 802, 609), r.Crop);
            });

            TestRunner.Run("几何：dwm 宽为 0 → 无效且 Crop 为 Empty", () =>
            {
                CropResult r = CaptureGeometry.ComputeCrop(
                    new Rectangle(100, 100, 816, 616),
                    new Rectangle(107, 100, 0, 609));
                Assert.False(r.Valid);
                Assert.Equal(Rectangle.Empty, r.Crop);
            });

            TestRunner.Run("几何：winRect 宽高 ≤0 → 无效", () =>
            {
                CropResult r = CaptureGeometry.ComputeCrop(
                    new Rectangle(100, 100, 0, 616),
                    new Rectangle(107, 100, 802, 609));
                Assert.False(r.Valid);
                Assert.Equal(Rectangle.Empty, r.Crop);
            });

            TestRunner.Run("几何：dwm 左侧部分越界 → 夹紧", () =>
            {
                // dwm 比 win 左移 10：相对偏移 -10、宽 816 → 夹紧为 (0,0,806,616)
                CropResult r = CaptureGeometry.ComputeCrop(
                    new Rectangle(100, 100, 816, 616),
                    new Rectangle(90, 100, 816, 616));
                Assert.True(r.Valid);
                Assert.Equal(new Rectangle(0, 0, 806, 616), r.Crop);
            });

            TestRunner.Run("几何：dwm 右下部分越界 → 夹紧", () =>
            {
                // 偏移 (7,0)、宽 900 超出 win 宽 816 → 截为 809；高 700 超出 616 → 截为 616
                CropResult r = CaptureGeometry.ComputeCrop(
                    new Rectangle(100, 100, 816, 616),
                    new Rectangle(107, 100, 900, 700));
                Assert.True(r.Valid);
                Assert.Equal(new Rectangle(7, 0, 809, 616), r.Crop);
            });

            TestRunner.Run("几何：dwm 完全越界 → 夹紧后宽 ≤0 无效", () =>
            {
                CropResult r = CaptureGeometry.ComputeCrop(
                    new Rectangle(100, 100, 816, 616),
                    new Rectangle(2000, 100, 100, 100));
                Assert.False(r.Valid);
                Assert.Equal(Rectangle.Empty, r.Crop);
            });
        }
    }
}
