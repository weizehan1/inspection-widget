using System.Drawing;

namespace InspectionWidget.Capture
{
    /// <summary>
    /// 黑图判定（只判「可疑」）：15×15 网格均匀采样，纯黑 (R=G=B=0) 或
    /// 全透明 (A=0) 的采样点占比 &gt; 98% 时判可疑。采样仅 225 点，用
    /// GetPixel 实现即可；GetPixel 对 32bpp ARGB 返回含 alpha 的颜色，
    /// 对不透明格式 A=255。
    /// </summary>
    public static class BlackFrameDetector
    {
        private const int GridSize = 15;
        private const double SuspectRatio = 0.98;

        public static bool IsSuspect(Bitmap bmp)
        {
            if (bmp == null)
                return true;
            if (bmp.Width <= 0 || bmp.Height <= 0)
                return true;

            int suspect = 0;
            int total = GridSize * GridSize;
            for (int gy = 0; gy < GridSize; gy++)
            {
                for (int gx = 0; gx < GridSize; gx++)
                {
                    int x = (int)((gx + 0.5) * bmp.Width / (double)GridSize);
                    int y = (int)((gy + 0.5) * bmp.Height / (double)GridSize);
                    if (x >= bmp.Width) x = bmp.Width - 1;
                    if (y >= bmp.Height) y = bmp.Height - 1;

                    Color c = bmp.GetPixel(x, y);
                    if (c.A == 0 || (c.R == 0 && c.G == 0 && c.B == 0))
                        suspect++;
                }
            }
            return suspect / (double)total > SuspectRatio;
        }
    }
}
