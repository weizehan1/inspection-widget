using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace InspectionWidget
{
    /// <summary>浅色中性背景、蓝色主操作与统一控件度量；只使用系统字体。</summary>
    public static class UiTheme
    {
        private static readonly object FontGate = new object();
        private static double _fontScale = 1.0;
        private static readonly Dictionary<double, FontSet> FontCache =
            new Dictionary<double, FontSet>();

        public static readonly Color AccentColor = Color.FromArgb(0x24, 0x63, 0xA0);
        public static readonly Color AccentHoverColor = Color.FromArgb(0x19, 0x4D, 0x80);
        public static readonly Color AccentSoftColor = Color.FromArgb(0xEA, 0xF2, 0xFA);
        public static readonly Color AccentBorderColor = Color.FromArgb(0xC9, 0xD6, 0xE2);
        public static readonly Color SuccessColor = Color.FromArgb(0x21, 0x6A, 0x48);
        public static readonly Color SuccessSoftColor = Color.FromArgb(0xE9, 0xF4, 0xEE);
        public static readonly Color ErrorColor = Color.FromArgb(0xB1, 0x32, 0x32);
        public static readonly Color ErrorSoftColor = Color.FromArgb(0xFF, 0xF0, 0xEF);
        public static readonly Color WarningColor = Color.FromArgb(0x81, 0x53, 0x15);
        public static readonly Color WarningSoftColor = Color.FromArgb(0xFB, 0xF5, 0xEB);
        public static readonly Color PrimaryTextColor = Color.FromArgb(0x20, 0x2A, 0x35);
        public static readonly Color SecondaryTextColor = Color.FromArgb(0x5B, 0x68, 0x75);
        public static readonly Color WeakTextColor = Color.FromArgb(0x74, 0x80, 0x8D);
        public static readonly Color PageBackColor = Color.FromArgb(0xFF, 0xFF, 0xFF);
        public static readonly Color SurfaceColor = Color.FromArgb(0xF4, 0xF7, 0xFA);
        public static readonly Color SelectedNavBackColor = Color.FromArgb(0xEA, 0xF2, 0xFA);
        public static readonly Color BorderColor = Color.FromArgb(0xD5, 0xDD, 0xE5);
        public static readonly Color BorderSoftColor = Color.FromArgb(0xE5, 0xEB, 0xF1);

        private sealed class FontSet
        {
            public readonly Font Body;
            public readonly Font BodyBold;
            public readonly Font Small;
            public readonly Font Subtitle;
            public readonly Font PanelTitle;
            public readonly Font Title;

            public FontSet(double scale)
            {
                Body = CreateFont(13f * (float)scale, FontStyle.Regular);
                BodyBold = CreateFont(13f * (float)scale, FontStyle.Bold);
                Small = CreateFont(11f * (float)scale, FontStyle.Regular);
                Subtitle = CreateFont(12f * (float)scale, FontStyle.Regular);
                PanelTitle = CreateFont(15f * (float)scale, FontStyle.Bold);
                Title = CreateFont(20f * (float)scale, FontStyle.Bold);
            }
        }

        public static Font BodyFont
        {
            get { return CurrentFontSet.Body; }
        }

        public static Font BodyBoldFont
        {
            get { return CurrentFontSet.BodyBold; }
        }

        public static Font SmallFont
        {
            get { return CurrentFontSet.Small; }
        }

        public static Font SubtitleFont
        {
            get { return CurrentFontSet.Subtitle; }
        }

        public static Font PanelTitleFont
        {
            get { return CurrentFontSet.PanelTitle; }
        }

        public static Font TitleFont
        {
            get { return CurrentFontSet.Title; }
        }

        private static FontSet CurrentFontSet
        {
            get
            {
                lock (FontGate)
                {
                    FontSet set;
                    if (!FontCache.TryGetValue(_fontScale, out set))
                    {
                        set = new FontSet(_fontScale);
                        FontCache[_fontScale] = set;
                    }
                    return set;
                }
            }
        }

        /// <summary>
        /// 设置窗口按“打开时的配置档位”决定字体基准；应用内改档后，
        /// 本窗口保持原档位，下次打开设置才采用新值。
        /// </summary>
        public static void UseFontScale(double scale)
        {
            if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale))
                throw new ArgumentOutOfRangeException("scale");
            lock (FontGate)
            {
                if (Math.Abs(_fontScale - scale) < 0.0001)
                    return;
                _fontScale = scale;
            }
        }

        /// <summary>
        /// 缩放已创建的布局度量。字体由 UseFontScale 在创建控件前统一取档，
        /// 因此这里只处理边界、留白、绝对行列宽高和 DataGridView 列宽。
        /// </summary>
        public static void ScaleLayout(Control root, double factor)
        {
            if (root == null || factor <= 0 || Math.Abs(factor - 1.0) < 0.0001)
                return;
            root.SuspendLayout();
            try
            {
                ScaleLayoutCore(root, factor, true);
            }
            finally
            {
                root.ResumeLayout(true);
            }
        }

        private static void ScaleLayoutCore(Control control, double factor, bool isRoot)
        {
            if (isRoot && control is Form)
            {
                var form = (Form)control;
                form.ClientSize = ScaleSize(form.ClientSize, factor);
            }
            else if (!isRoot)
            {
                control.Bounds = ScaleRectangle(control.Bounds, factor);
            }

            control.Padding = ScalePadding(control.Padding, factor);
            control.Margin = ScalePadding(control.Margin, factor);
            control.MinimumSize = ScaleSizeAllowingEmpty(control.MinimumSize, factor);
            control.MaximumSize = ScaleSizeAllowingEmpty(control.MaximumSize, factor);

            var table = control as TableLayoutPanel;
            if (table != null)
            {
                for (int i = 0; i < table.ColumnStyles.Count; i++)
                {
                    ColumnStyle style = table.ColumnStyles[i];
                    if (style.SizeType == SizeType.Absolute)
                        style.Width = Scale(style.Width, factor);
                }
                for (int i = 0; i < table.RowStyles.Count; i++)
                {
                    RowStyle style = table.RowStyles[i];
                    if (style.SizeType == SizeType.Absolute)
                        style.Height = Scale(style.Height, factor);
                }
            }

            var grid = control as DataGridView;
            if (grid != null)
            {
                grid.ColumnHeadersHeight = Scale(grid.ColumnHeadersHeight, factor);
                if (grid.RowTemplate != null)
                    grid.RowTemplate.Height = Scale(grid.RowTemplate.Height, factor);
                for (int i = 0; i < grid.Columns.Count; i++)
                {
                    DataGridViewColumn column = grid.Columns[i];
                    column.Width = Scale(column.Width, factor);
                    column.MinimumWidth = Scale(column.MinimumWidth, factor);
                }
            }

            var scrollable = control as ScrollableControl;
            if (scrollable != null)
                scrollable.AutoScrollMinSize = ScaleSize(scrollable.AutoScrollMinSize, factor);

            for (int i = 0; i < control.Controls.Count; i++)
                ScaleLayoutCore(control.Controls[i], factor, false);
        }

        public static Button PrimaryButton(string text)
        {
            var button = BaseButton(text);
            button.BackColor = AccentColor;
            button.ForeColor = Color.White;
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        public static Button SecondaryButton(string text)
        {
            var button = BaseButton(text);
            button.BackColor = Color.White;
            button.ForeColor = PrimaryTextColor;
            button.FlatAppearance.BorderColor = BorderColor;
            button.FlatAppearance.BorderSize = 1;
            return button;
        }

        public static DataGridView GridStyle(DataGridView grid)
        {
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.FixedSingle;
            grid.Font = BodyFont;
            grid.GridColor = BorderColor;
            grid.ColumnHeadersHeight = 34;
            grid.RowTemplate.Height = 34;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = SurfaceColor;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = SecondaryTextColor;
            grid.DefaultCellStyle.SelectionBackColor = SelectedNavBackColor;
            grid.DefaultCellStyle.SelectionForeColor = PrimaryTextColor;
            return grid;
        }

        private static Button BaseButton(string text)
        {
            return new Button
            {
                Text = text,
                Font = BodyFont,
                AutoSize = false,
                Height = 34,
                Width = 92,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(0, 0, 8, 0),
                UseVisualStyleBackColor = false
            };
        }

        private static Font CreateFont(float sizePx, FontStyle style)
        {
            string family = "Microsoft YaHei UI";
            foreach (FontFamily installed in FontFamily.Families)
            {
                if (installed.Name == "Segoe UI")
                {
                    family = "Segoe UI";
                    break;
                }
            }
            return new Font(family, sizePx, style, GraphicsUnit.Pixel);
        }

        private static int Scale(float value, double factor)
        {
            return (int)Math.Round(value * factor, MidpointRounding.AwayFromZero);
        }

        private static Size ScaleSize(Size value, double factor)
        {
            return new Size(Scale(value.Width, factor), Scale(value.Height, factor));
        }

        private static Size ScaleSizeAllowingEmpty(Size value, double factor)
        {
            return new Size(
                value.Width <= 0 ? value.Width : Scale(value.Width, factor),
                value.Height <= 0 ? value.Height : Scale(value.Height, factor));
        }

        private static Rectangle ScaleRectangle(Rectangle value, double factor)
        {
            return new Rectangle(
                Scale(value.X, factor),
                Scale(value.Y, factor),
                Scale(value.Width, factor),
                Scale(value.Height, factor));
        }

        private static Padding ScalePadding(Padding value, double factor)
        {
            return new Padding(
                Scale(value.Left, factor),
                Scale(value.Top, factor),
                Scale(value.Right, factor),
                Scale(value.Bottom, factor));
        }
    }
}
