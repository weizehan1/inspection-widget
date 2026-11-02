using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using InspectionWidget.Capture;

namespace InspectionWidget.Tests
{
    /// <summary>
    /// BlackFrameDetector 黑图判定（Mono + libgdiplus 真 Bitmap）：
    /// 15×15 网格采样、纯黑或全透明占比 &gt; 98% 为可疑。
    /// 98% 边界两条按采样点构造法落实：先全黑，再在已知采样坐标精准涂白。
    /// </summary>
    public static class BlackFrameDetectorTests
    {
        private const int Size = 100;

        /// <summary>与实现同口径的采样坐标：(i+0.5)/15 比例取整。</summary>
        private static List<Point> SamplePoints(int width, int height)
        {
            var points = new List<Point>();
            for (int gy = 0; gy < 15; gy++)
            {
                for (int gx = 0; gx < 15; gx++)
                {
                    int x = (int)((gx + 0.5) * width / 15.0);
                    int y = (int)((gy + 0.5) * height / 15.0);
                    if (x >= width) x = width - 1;
                    if (y >= height) y = height - 1;
                    points.Add(new Point(x, y));
                }
            }
            return points;
        }

        private static Bitmap FilledBitmap(Color color)
        {
            var bmp = new Bitmap(Size, Size, PixelFormat.Format32bppArgb);
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    bmp.SetPixel(x, y, color);
            return bmp;
        }

        public static void RunAll()
        {
            TestRunner.Run("黑图：全黑 100×100 → 可疑", () =>
            {
                using (Bitmap bmp = FilledBitmap(Color.Black))
                    Assert.True(BlackFrameDetector.IsSuspect(bmp));
            });

            TestRunner.Run("黑图：全透明 → 可疑", () =>
            {
                // 新建 32bpp ARGB 位图默认全透明（A=0）
                using (var bmp = new Bitmap(Size, Size, PixelFormat.Format32bppArgb))
                    Assert.True(BlackFrameDetector.IsSuspect(bmp));
            });

            TestRunner.Run("黑图：全白 → 不可疑", () =>
            {
                using (Bitmap bmp = FilledBitmap(Color.White))
                    Assert.False(BlackFrameDetector.IsSuspect(bmp));
            });

            TestRunner.Run("黑图：黑底中央白块（采样必含白点）→ 不可疑", () =>
            {
                using (Bitmap bmp = FilledBitmap(Color.Black))
                {
                    for (int y = 40; y < 60; y++)
                        for (int x = 40; x < 60; x++)
                            bmp.SetPixel(x, y, Color.White);
                    Assert.False(BlackFrameDetector.IsSuspect(bmp));
                }
            });

            TestRunner.Run("黑图：null → 可疑", () =>
            {
                Assert.True(BlackFrameDetector.IsSuspect(null));
            });

            TestRunner.Run("黑图：边界 222 黑 3 白（98.67% > 98%）→ 可疑", () =>
            {
                using (Bitmap bmp = FilledBitmap(Color.Black))
                {
                    List<Point> points = SamplePoints(Size, Size);
                    Assert.Equal(225, points.Count);
                    for (int i = 0; i < 3; i++)
                        bmp.SetPixel(points[i].X, points[i].Y, Color.White);
                    Assert.True(BlackFrameDetector.IsSuspect(bmp));
                }
            });

            TestRunner.Run("黑图：边界 221 黑 4 白（98.22% > 98%）→ 可疑", () =>
            {
                using (Bitmap bmp = FilledBitmap(Color.Black))
                {
                    List<Point> points = SamplePoints(Size, Size);
                    Assert.Equal(225, points.Count);
                    for (int i = 0; i < 4; i++)
                        bmp.SetPixel(points[i].X, points[i].Y, Color.White);
                    Assert.True(BlackFrameDetector.IsSuspect(bmp));
                }
            });

            TestRunner.Run("黑图：边界 220 黑 5 白（97.8% ≤ 98%）→ 不可疑", () =>
            {
                using (Bitmap bmp = FilledBitmap(Color.Black))
                {
                    List<Point> points = SamplePoints(Size, Size);
                    for (int i = 0; i < 5; i++)
                        bmp.SetPixel(points[i].X, points[i].Y, Color.White);
                    Assert.False(BlackFrameDetector.IsSuspect(bmp));
                }
            });
        }
    }
}
