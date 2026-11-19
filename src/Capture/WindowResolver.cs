using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using InspectionWidget.Native;

namespace InspectionWidget.Capture
{
    /// <summary>选窗解析结果分类。</summary>
    public enum ResolveReason
    {
        Ok,
        Excluded,
        Minimized,
        Gone
    }

    /// <summary>选窗解析结果：标题只作提示用，Hwnd 为顶层窗口句柄。</summary>
    public sealed class ResolveResult
    {
        public IntPtr Hwnd;
        public string Title;
        public string ClassName;
        public ResolveReason Reason;
    }

    /// <summary>
    /// 由屏幕坐标反查顶层窗口：WindowFromPoint → GetAncestor(GA_ROOT) →
    /// 自身/桌面/任务栏排除 → 最小化判定 → 取标题（只作提示）。
    /// 本类含 P/Invoke，只能在 Windows 实机运行；判定口径复用纯逻辑 TargetClassifier。
    /// </summary>
    public static class WindowResolver
    {
        private static readonly uint CurrentProcessId = ResolveCurrentProcessId();

        private static uint ResolveCurrentProcessId()
        {
            try
            {
                using (Process current = Process.GetCurrentProcess())
                {
                    return (uint)current.Id;
                }
            }
            catch
            {
                return 0;
            }
        }

        public static ResolveResult ResolveTopLevel(Point pt, IntPtr selfHwnd)
        {
            return ResolveTopLevel(pt, selfHwnd, IntPtr.Zero);
        }

        public static ResolveResult ResolveTopLevel(Point pt, IntPtr selfHwnd, IntPtr ignoredHwnd)
        {
            var result = new ResolveResult
            {
                Hwnd = IntPtr.Zero,
                Title = string.Empty,
                ClassName = null,
                Reason = ResolveReason.Gone
            };

            var nativePoint = new NativeMethods.POINT { X = pt.X, Y = pt.Y };
            IntPtr hwnd = NativeMethods.WindowFromPoint(nativePoint);
            if (hwnd == IntPtr.Zero)
                return result;

            IntPtr root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
            if (root == IntPtr.Zero)
                root = hwnd;
            result.Hwnd = root;

            if (!NativeMethods.IsWindow(root))
            {
                result.Reason = ResolveReason.Gone;
                return result;
            }

            bool isSelf = root == selfHwnd
                || (ignoredHwnd != IntPtr.Zero && root == ignoredHwnd)
                || IsSameProcess(root);
            string className = GetClassNameText(root);
            result.ClassName = className;
            if (TargetClassifier.IsExcluded(className, isSelf))
            {
                result.Reason = ResolveReason.Excluded;
                return result;
            }

            result.Title = GetWindowTitle(root);
            if (NativeMethods.IsIconic(root))
            {
                result.Reason = ResolveReason.Minimized;
                return result;
            }

            if (IsWindowHiddenOrCloaked(root))
            {
                result.Reason = ResolveReason.Gone;
                return result;
            }

            result.Reason = ResolveReason.Ok;
            return result;
        }

        /// <summary>截图前复核：句柄仍是有效窗口且未被最小化或隐藏。</summary>
        public static bool StillValid(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return false;
            return NativeMethods.IsWindow(hwnd)
                && !NativeMethods.IsIconic(hwnd)
                && !IsWindowHiddenOrCloaked(hwnd);
        }

        internal static bool IsWindowHiddenOrCloaked(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !NativeMethods.IsWindowVisible(hwnd))
                return true;
            try
            {
                int cloaked;
                if (NativeMethods.DwmGetWindowAttribute(
                    hwnd,
                    NativeMethods.DWMWA_CLOAKED,
                    out cloaked,
                    Marshal.SizeOf(typeof(int))) == 0)
                {
                    return cloaked != 0;
                }
            }
            catch
            {
            }
            return false;
        }

        private static bool IsSameProcess(IntPtr hwnd)
        {
            if (CurrentProcessId == 0 || hwnd == IntPtr.Zero)
                return false;
            try
            {
                uint pid;
                NativeMethods.GetWindowThreadProcessId(hwnd, out pid);
                return pid != 0 && pid == CurrentProcessId;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>读取目标窗口视觉外框（优先 DWM 扩展边框以避开透明阴影）。</summary>
        public static bool TryGetTargetBounds(IntPtr hwnd, out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
                return false;

            NativeMethods.RECT dwmNative;
            if (NativeMethods.DwmGetWindowAttribute(
                hwnd,
                NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                out dwmNative,
                System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.RECT))) == 0)
            {
                Rectangle dwmRect = dwmNative.ToRectangle();
                if (dwmRect.Width > 0 && dwmRect.Height > 0)
                {
                    bounds = dwmRect;
                    return true;
                }
            }

            NativeMethods.RECT winNative;
            if (!NativeMethods.GetWindowRect(hwnd, out winNative))
                return false;
            Rectangle winRect = winNative.ToRectangle();
            if (winRect.Width <= 0 || winRect.Height <= 0)
                return false;
            bounds = winRect;
            return true;
        }

        private static string GetClassNameText(IntPtr hwnd)
        {
            var builder = new StringBuilder(256);
            int length = NativeMethods.GetClassName(hwnd, builder, builder.Capacity);
            if (length <= 0)
                return null;
            return builder.ToString();
        }

        private static string GetWindowTitle(IntPtr hwnd)
        {
            int length = NativeMethods.GetWindowTextLength(hwnd);
            if (length <= 0)
                return string.Empty;
            var builder = new StringBuilder(length + 1);
            int copied = NativeMethods.GetWindowText(hwnd, builder, builder.Capacity);
            if (copied <= 0)
                return string.Empty;
            return builder.ToString();
        }
    }
}
