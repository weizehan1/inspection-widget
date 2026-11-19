using System;
using System.Drawing;
using System.Windows.Forms;
using InspectionWidget.Native;

namespace InspectionWidget.Capture
{
    /// <summary>
    /// 选窗悬停目标窗口高亮框：使用镂空 Region + WS_EX_LAYERED + WS_EX_TRANSPARENT + WS_EX_NOACTIVATE，
    /// 仅绘制外围 3px 边框而不拦截鼠标点击、不抢占前台焦点，退出等待态时立即隐藏。
    /// </summary>
    internal sealed class TargetHighlightOverlay : Form
    {
        private const int WsExTransparent = 0x00000020;
        private const int WsExToolWindow = 0x00000080;
        private const int WsExLayered = 0x00080000;
        private const int WsExNoActivate = 0x08000000;
        private const int WmNcHitTest = 0x0084;
        private const int HtTransparent = -1;

        private Rectangle _currentBounds = Rectangle.Empty;
        private int _borderWidth;

        public TargetHighlightOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            TopMost = true;
            DoubleBuffered = true;
            BackColor = UiTheme.WarningColor;
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WsExLayered | WsExTransparent | WsExToolWindow | WsExNoActivate;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                try
                {
                    NativeMethods.SetLayeredWindowAttributes(Handle, 0, 255, NativeMethods.LWA_ALPHA);
                }
                catch
                {
                }
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmNcHitTest)
            {
                m.Result = (IntPtr)HtTransparent;
                return;
            }
            base.WndProc(ref m);
        }

        public void ShowTarget(Rectangle bounds, int borderWidth)
        {
            int stroke = Math.Max(2, borderWidth);
            if (bounds.Width <= stroke * 2 || bounds.Height <= stroke * 2)
            {
                HideOverlay();
                return;
            }
            if (Visible && _currentBounds == bounds && _borderWidth == stroke)
                return;

            _currentBounds = bounds;
            _borderWidth = stroke;

            var region = new Region(new Rectangle(0, 0, bounds.Width, bounds.Height));
            region.Exclude(new Rectangle(stroke, stroke, bounds.Width - stroke * 2, bounds.Height - stroke * 2));
            Region previous = Region;
            SuspendLayout();
            try
            {
                if (Visible)
                    Region = region;
                Bounds = bounds;
                if (!Visible)
                    Region = region;
            }
            finally
            {
                ResumeLayout(false);
            }
            if (previous != null)
                previous.Dispose();

            if (!Visible)
                Show();
        }

        public void HideOverlay()
        {
            _currentBounds = Rectangle.Empty;
            if (Visible)
                Hide();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (Visible)
                    Hide();
                if (Region != null)
                {
                    Region old = Region;
                    Region = null;
                    old.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
