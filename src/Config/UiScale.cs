using System;

namespace InspectionWidget.Config
{
    /// <summary>
    /// 界面缩放档位（v1.0.1，为 4K 等高分辨率屏准备）：合法档位固定五档，
    /// 配置里的百分比经 Normalize 归一后才使用——手改出的非法值一律按 100 处理，
    /// 布局侧只拿 Factor（1.0–2.0）做度量乘算。下拉索引映射也收在这里供设置窗与测试共用。
    /// </summary>
    public static class UiScale
    {
        public const int DefaultPercent = 100;

        private static readonly int[] Percents = { 100, 125, 150, 175, 200 };

        public static int Count
        {
            get { return Percents.Length; }
        }

        public static bool IsValid(int percent)
        {
            for (int i = 0; i < Percents.Length; i++)
            {
                if (Percents[i] == percent)
                    return true;
            }
            return false;
        }

        /// <summary>非法值归一为默认 100，合法值原样。</summary>
        public static int Normalize(int percent)
        {
            return IsValid(percent) ? percent : DefaultPercent;
        }

        /// <summary>百分比 → 布局因子（1.0–2.0）；非法值按 100 处理。</summary>
        public static double Factor(int percent)
        {
            return Normalize(percent) / 100.0;
        }

        /// <summary>档位 → 下拉索引；非法档位回首项（100%）。</summary>
        public static int IndexForPercent(int percent)
        {
            int normalized = Normalize(percent);
            for (int i = 0; i < Percents.Length; i++)
            {
                if (Percents[i] == normalized)
                    return i;
            }
            return 0;
        }

        /// <summary>下拉索引 → 档位；越界钳制到首/末档。</summary>
        public static int PercentForIndex(int index)
        {
            if (index < 0)
                index = 0;
            if (index >= Percents.Length)
                index = Percents.Length - 1;
            return Percents[index];
        }

        /// <summary>下拉显示文本（如 "150%"）；非法值先归一。</summary>
        public static string TextForPercent(int percent)
        {
            return Normalize(percent).ToString() + "%";
        }

        /// <summary>按索引取显示文本（设置窗填充下拉用）。</summary>
        public static string TextForIndex(int index)
        {
            return TextForPercent(PercentForIndex(index));
        }
    }
}
