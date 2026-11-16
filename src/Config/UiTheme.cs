using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace InspectionWidget
{
    /// <summary>暖石板画布、纯白卡片、深海青主操作与琥珀等待态；只使用系统字体。</summary>
    public static class UiTheme
    {
        private static readonly object FontGate = new object();
        private static double _fontScale = 1.0;
        private static readonly Dictionary<double, FontSet> FontCache =
            new Dictionary<double, FontSet>();
        private static string _preferredFontFamily;
        private static string _preferredMonoFontFamily;
        private static bool? _hasSymbolFont;

        public static readonly Color AccentColor = Color.FromArgb(0x0F, 0x5C, 0x6E);
        public static readonly Color AccentHoverColor = Color.FromArgb(0x0B, 0x47, 0x55);
        public static readonly Color AccentSoftColor = Color.FromArgb(0xE6, 0xF1, 0xF3);
        public static readonly Color AccentBorderColor = Color.FromArgb(0xB6, 0xD3, 0xDA);

        public static readonly Color SuccessColor = Color.FromArgb(0x1B, 0x6B, 0x4A);
        public static readonly Color SuccessSoftColor = Color.FromArgb(0xE8, 0xF4, 0xEE);
        public static readonly Color SuccessBorderColor = Color.FromArgb(0xB4, 0xD8, 0xC6);

        public static readonly Color ErrorColor = Color.FromArgb(0xB4, 0x23, 0x18);
        public static readonly Color ErrorHoverColor = Color.FromArgb(0x91, 0x1B, 0x12);
        public static readonly Color ErrorSoftColor = Color.FromArgb(0xFE, 0xF0, 0xEE);
        public static readonly Color ErrorBorderColor = Color.FromArgb(0xEC, 0xC2, 0xBE);

        public static readonly Color WarningColor = Color.FromArgb(0xB4, 0x53, 0x09);
        public static readonly Color WarningSoftColor = Color.FromArgb(0xFE, 0xF3, 0xE2);
        public static readonly Color WarningBorderColor = Color.FromArgb(0xE9, 0xC8, 0x9B);

        public static readonly Color PrimaryTextColor = Color.FromArgb(0x1F, 0x24, 0x21);
        public static readonly Color SecondaryTextColor = Color.FromArgb(0x57, 0x5B, 0x58);
        public static readonly Color WeakTextColor = Color.FromArgb(0x76, 0x7A, 0x75);

        public static readonly Color PageBackColor = Color.FromArgb(0xF6, 0xF5, 0xF1);
        public static readonly Color CardBackColor = Color.FromArgb(0xFF, 0xFF, 0xFF);
        public static readonly Color SurfaceColor = Color.FromArgb(0xED, 0xEB, 0xE4);
        public static readonly Color TrackBackColor = Color.FromArgb(0xDF, 0xDC, 0xD2);
        public static readonly Color NavHoverBackColor = Color.FromArgb(0xE3, 0xE0, 0xD7);
        public static readonly Color SelectedNavBackColor = Color.FromArgb(0xE6, 0xF1, 0xF3);
        public static readonly Color VisitedBackColor = Color.FromArgb(0xF4, 0xFA, 0xF7);
        public static readonly Color LastDeviceBackColor = Color.FromArgb(0xF1, 0xF7, 0xF8);
        public static readonly Color AlternatingRowBackColor = Color.FromArgb(0xFA, 0xF9, 0xF6);
        public static readonly Color BorderColor = Color.FromArgb(0xDC, 0xD9, 0xD0);
        public static readonly Color BorderSoftColor = Color.FromArgb(0xE7, 0xE4, 0xDC);

        private sealed class FontSet
        {
            public readonly Font Body;
            public readonly Font BodyBold;
            public readonly Font Small;
            public readonly Font SmallBold;
            public readonly Font Subtitle;
            public readonly Font PanelTitle;
            public readonly Font Title;
            public readonly Font MonoCounter;

            public FontSet(double scale)
            {
                Body = CreateFont(13f * (float)scale, FontStyle.Regular);
                BodyBold = CreateFont(13f * (float)scale, FontStyle.Bold);
                Small = CreateFont(11f * (float)scale, FontStyle.Regular);
                SmallBold = CreateFont(11f * (float)scale, FontStyle.Bold);
                Subtitle = CreateFont(12f * (float)scale, FontStyle.Regular);
                PanelTitle = CreateFont(15f * (float)scale, FontStyle.Bold);
                Title = CreateFont(20f * (float)scale, FontStyle.Bold);
                MonoCounter = CreateMonoFont(18f * (float)scale, FontStyle.Bold);
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

        public static Font SmallBoldFont
        {
            get { return CurrentFontSet.SmallBold; }
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

        public static Font MonoCounterFont
        {
            get { return CurrentFontSet.MonoCounter; }
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
            SuspendLayoutRecursive(root);
            try
            {
                ScaleLayoutCore(root, factor, true);
            }
            finally
            {
                ResumeLayoutRecursive(root, true);
            }
        }

        private static void SuspendLayoutRecursive(Control control)
        {
            if (control == null)
                return;
            control.SuspendLayout();
            for (int i = 0; i < control.Controls.Count; i++)
                SuspendLayoutRecursive(control.Controls[i]);
        }

        private static void ResumeLayoutRecursive(Control control, bool performLayout)
        {
            if (control == null)
                return;
            for (int i = 0; i < control.Controls.Count; i++)
                ResumeLayoutRecursive(control.Controls[i], performLayout);
            control.ResumeLayout(performLayout);
        }

        private static void ScaleLayoutCore(Control control, double factor, bool isRoot)
        {
            if (isRoot)
            {
                if (control is Form)
                {
                    var form = (Form)control;
                    form.ClientSize = ScaleSize(form.ClientSize, factor);
                }
                else
                {
                    control.Size = ScaleSize(control.Size, factor);
                }
            }
            else if (control.Dock == DockStyle.None)
            {
                control.Bounds = ScaleRectangle(control.Bounds, factor);
            }
            else if (control.Dock == DockStyle.Top || control.Dock == DockStyle.Bottom)
            {
                control.Height = Scale(control.Height, factor);
            }
            else if (control.Dock == DockStyle.Left || control.Dock == DockStyle.Right)
            {
                control.Width = Scale(control.Width, factor);
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
                if (grid.RowHeadersWidth > 0)
                    grid.RowHeadersWidth = Scale(grid.RowHeadersWidth, factor);
                if (grid.RowTemplate != null)
                    grid.RowTemplate.Height = Scale(grid.RowTemplate.Height, factor);
                if (grid.ColumnHeadersDefaultCellStyle != null)
                    grid.ColumnHeadersDefaultCellStyle.Padding = ScalePadding(grid.ColumnHeadersDefaultCellStyle.Padding, factor);
                if (grid.DefaultCellStyle != null)
                    grid.DefaultCellStyle.Padding = ScalePadding(grid.DefaultCellStyle.Padding, factor);
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
            button.FlatAppearance.MouseOverBackColor = AccentHoverColor;
            return button;
        }

        public static Button SecondaryButton(string text)
        {
            var button = BaseButton(text);
            button.BackColor = CardBackColor;
            button.ForeColor = PrimaryTextColor;
            button.FlatAppearance.BorderColor = BorderColor;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = SurfaceColor;
            return button;
        }

        public static Button DangerButton(string text)
        {
            var button = BaseButton(text);
            button.BackColor = ErrorColor;
            button.ForeColor = Color.White;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = ErrorHoverColor;
            return button;
        }

        public static DataGridView GridStyle(DataGridView grid)
        {
            grid.BackgroundColor = CardBackColor;
            grid.BorderStyle = BorderStyle.FixedSingle;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            grid.Font = BodyFont;
            grid.GridColor = BorderSoftColor;
            grid.ColumnHeadersHeight = 34;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.RowHeadersWidth = 44;
            grid.RowTemplate.Height = 36;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = SurfaceColor;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = SecondaryTextColor;
            grid.ColumnHeadersDefaultCellStyle.Font = SmallBoldFont;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = SurfaceColor;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = SecondaryTextColor;
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 0, 8, 0);
            grid.RowHeadersDefaultCellStyle.BackColor = SurfaceColor;
            grid.RowHeadersDefaultCellStyle.ForeColor = WeakTextColor;
            grid.RowHeadersDefaultCellStyle.Font = SmallFont;
            grid.RowHeadersDefaultCellStyle.SelectionBackColor = SelectedNavBackColor;
            grid.RowHeadersDefaultCellStyle.SelectionForeColor = AccentColor;
            grid.RowHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.DefaultCellStyle.BackColor = CardBackColor;
            grid.DefaultCellStyle.ForeColor = PrimaryTextColor;
            grid.DefaultCellStyle.SelectionBackColor = SelectedNavBackColor;
            grid.DefaultCellStyle.SelectionForeColor = PrimaryTextColor;
            grid.DefaultCellStyle.Padding = new Padding(8, 0, 8, 0);
            grid.AlternatingRowsDefaultCellStyle.BackColor = AlternatingRowBackColor;
            return grid;
        }

        /// <summary>现代纯白分组卡（替代传统 GroupBox），自带 1px 暖灰描边与分组标题。</summary>
        public static Panel CreateSectionCard(string title, out FlowLayoutPanel bodyStack)
        {
            var card = new Panel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                BackColor = CardBackColor,
                Padding = new Padding(14, 12, 14, 12),
                Margin = new Padding(0, 0, 0, 12)
            };
            card.Paint += delegate(object sender, PaintEventArgs e)
            {
                var p = (Panel)sender;
                if (p.Width <= 1 || p.Height <= 1) return;
                using (var pen = new Pen(BorderColor, 1f))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
                }
            };

            bodyStack = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = CardBackColor,
                Margin = new Padding(0)
            };

            if (!string.IsNullOrEmpty(title))
            {
                var heading = new Label
                {
                    Text = title,
                    Font = BodyBoldFont,
                    ForeColor = PrimaryTextColor,
                    AutoSize = true,
                    Margin = new Padding(0, 0, 0, 8)
                };
                bodyStack.Controls.Add(heading);
            }

            card.Controls.Add(bodyStack);
            return card;
        }

        /// <summary>创建全宽内容卡片（支持 TableLayoutPanel 等非垂直流式子布局）。</summary>
        public static Panel CreateSectionCard(string title, Control innerContent)
        {
            var card = new Panel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                BackColor = CardBackColor,
                Padding = new Padding(14, 12, 14, 12),
                Margin = new Padding(0, 0, 0, 12)
            };
            card.Paint += delegate(object sender, PaintEventArgs e)
            {
                var p = (Panel)sender;
                if (p.Width <= 1 || p.Height <= 1) return;
                using (var pen = new Pen(BorderColor, 1f))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
                }
            };

            var stack = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                BackColor = CardBackColor,
                Margin = new Padding(0)
            };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            if (!string.IsNullOrEmpty(title))
            {
                var heading = new Label
                {
                    Text = title,
                    Font = BodyBoldFont,
                    ForeColor = PrimaryTextColor,
                    AutoSize = true,
                    Margin = new Padding(0, 0, 0, 8)
                };
                stack.Controls.Add(heading);
            }

            if (innerContent != null)
                stack.Controls.Add(innerContent);

            card.Controls.Add(stack);
            return card;
        }

        public static void FillRoundedPill(Graphics g, Color color, Rectangle rect)
        {
            if (g == null || rect.Width <= 0 || rect.Height <= 0)
                return;
            int radius = Math.Min(rect.Width, rect.Height);
            using (var brush = new SolidBrush(color))
            {
                if (radius <= 2)
                {
                    g.FillRectangle(brush, rect);
                    return;
                }
                SmoothingMode prev = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = CreateCapsulePath(rect, radius))
                {
                    g.FillPath(brush, path);
                }
                g.SmoothingMode = prev;
            }
        }

        private static GraphicsPath CreateCapsulePath(Rectangle rect, int diameter)
        {
            var path = new GraphicsPath();
            int d = Math.Max(1, Math.Min(diameter, Math.Min(rect.Width, rect.Height)));
            var arc = new Rectangle(rect.X, rect.Y, d, d);
            path.AddArc(arc, 90, 180);
            arc.X = rect.Right - d;
            path.AddArc(arc, 270, 180);
            path.CloseFigure();
            return path;
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
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 8, 0),
                UseVisualStyleBackColor = false
            };
        }

        internal static bool HasSymbolFont
        {
            get
            {
                EnsureFontCapabilities();
                return _hasSymbolFont.Value;
            }
        }

        internal static Font CreateFont(float sizePx, FontStyle style)
        {
            EnsureFontCapabilities();
            return new Font(_preferredFontFamily, sizePx, style, GraphicsUnit.Pixel);
        }

        internal static Font CreateMonoFont(float sizePx, FontStyle style)
        {
            EnsureFontCapabilities();
            return new Font(_preferredMonoFontFamily, sizePx, style, GraphicsUnit.Pixel);
        }

        private static void EnsureFontCapabilities()
        {
            lock (FontGate)
            {
                if (_preferredFontFamily != null
                    && _preferredMonoFontFamily != null
                    && _hasSymbolFont.HasValue)
                    return;

                string preferred = "Microsoft YaHei UI";
                string preferredMono = null;
                int monoPriority = int.MaxValue;
                bool hasSymbol = false;
                FontFamily[] families = FontFamily.Families;
                try
                {
                    for (int i = 0; i < families.Length; i++)
                    {
                        FontFamily family = families[i];
                        if (family == null)
                            continue;
                        string name = family.Name;
                        if (name == "Segoe UI")
                            preferred = "Segoe UI";
                        else if (name == "Segoe UI Symbol")
                            hasSymbol = true;

                        int p = MonoFontPriority(name);
                        if (p < monoPriority
                            && family.IsStyleAvailable(FontStyle.Regular)
                            && family.IsStyleAvailable(FontStyle.Bold))
                        {
                            monoPriority = p;
                            preferredMono = name;
                        }
                    }
                }
                finally
                {
                    for (int i = 0; i < families.Length; i++)
                    {
                        if (families[i] != null)
                            families[i].Dispose();
                    }
                }
                _preferredFontFamily = preferred;
                _preferredMonoFontFamily = preferredMono ?? preferred;
                _hasSymbolFont = hasSymbol;
            }
        }

        private static int MonoFontPriority(string name)
        {
            if (name == "Cascadia Mono") return 0;
            if (name == "Consolas") return 1;
            if (name == "Menlo") return 2;
            if (name == "SF Mono") return 3;
            if (name == "Courier New") return 4;
            return int.MaxValue;
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
