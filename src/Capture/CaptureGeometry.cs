using System.Drawing;

namespace InspectionWidget.Capture
{
    /// <summary>裁边计算结果：Valid=false 时 Crop 为 Empty，调用方回退整幅 winRect。</summary>
    public struct CropResult
    {
        public bool Valid;
        public Rectangle Crop;
    }

    /// <summary>
    /// PrintWindow 抓图后的裁边几何（纯逻辑）：PrintWindow 按 GetWindowRect 完整
    /// 矩形抓图，DWM 扩展边框矩形相对它的偏移与尺寸即真实内容区（去掉透明边框）。
    /// </summary>
    public static class CaptureGeometry
    {
        public static CropResult ComputeCrop(Rectangle winRect, Rectangle dwmRect)
        {
            var invalid = new CropResult { Valid = false, Crop = Rectangle.Empty };
            if (winRect.Width <= 0 || winRect.Height <= 0)
                return invalid;
            if (dwmRect.Width <= 0 || dwmRect.Height <= 0)
                return invalid;

            int x = dwmRect.X - winRect.X;
            int y = dwmRect.Y - winRect.Y;
            int width = dwmRect.Width;
            int height = dwmRect.Height;

            // 夹紧到 winRect 范围内：偏移 <0 时从 0 起并相应扣减尺寸，
            // 右下超出时按 win 尺寸截断。
            if (x < 0)
            {
                width += x;
                x = 0;
            }
            if (y < 0)
            {
                height += y;
                y = 0;
            }
            if (x + width > winRect.Width)
                width = winRect.Width - x;
            if (y + height > winRect.Height)
                height = winRect.Height - y;

            if (width <= 0 || height <= 0)
                return invalid;

            return new CropResult
            {
                Valid = true,
                Crop = new Rectangle(x, y, width, height)
            };
        }
    }
}
