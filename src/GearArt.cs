using System.Drawing;
using System.Drawing.Drawing2D;

namespace InspectionWidget
{
    /// <summary>
    /// 代码绘制的简易齿轮位图（v1.0.2）：设置按钮字形兜底——系统缺
    /// Segoe UI Symbol 字体时，用同为矢量绘制的白色齿轮小图代替 ⚙ 字形。
    /// 造型口径与 app 图标一致（几何绘制、中心孔透明）。
    /// </summary>
    public static class GearArt
    {
        /// <summary>画一个 size×size 的齿轮：8 齿 + 轮体圆 + 中心孔（透明）。调用方负责 Dispose。</summary>
        public static Bitmap DrawGear(int size, Color color)
        {
            if (size <= 0)
                size = 16;
            var bitmap = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                float center = size / 2f;
                float bodyRadius = size * 0.30f;   // 轮体半径
                float toothInner = size * 0.24f;    // 齿根
                float toothOuter = size * 0.46f;    // 齿顶
                float toothWidth = size * 0.17f;    // 齿宽

                using (var brush = new SolidBrush(color))
                {
                    for (int i = 0; i < 8; i++)
                    {
                        g.ResetTransform();
                        g.TranslateTransform(center, center);
                        g.RotateTransform(i * 45f);
                        g.FillRectangle(brush, toothInner, -toothWidth / 2f,
                            toothOuter - toothInner, toothWidth);
                    }
                    g.ResetTransform();
                    g.FillEllipse(brush, center - bodyRadius, center - bodyRadius,
                        bodyRadius * 2f, bodyRadius * 2f);
                }

                // 中心孔：SourceCopy 以透明色冲孔（不清掉轮体其余部分）
                g.CompositingMode = CompositingMode.SourceCopy;
                float holeRadius = size * 0.13f;
                using (var clear = new SolidBrush(Color.Transparent))
                {
                    g.FillEllipse(clear, center - holeRadius, center - holeRadius,
                        holeRadius * 2f, holeRadius * 2f);
                }
                g.CompositingMode = CompositingMode.SourceOver;
            }
            return bitmap;
        }
    }
}
