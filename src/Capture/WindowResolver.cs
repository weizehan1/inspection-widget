using System;
using System.Drawing;
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
        public static ResolveResult ResolveTopLevel(Point pt, IntPtr selfHwnd)
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

            bool isSelf = root == selfHwnd;
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

            result.Reason = ResolveReason.Ok;
            return result;
        }

        /// <summary>截图前复核：句柄仍是有效窗口且未被最小化。</summary>
        public static bool StillValid(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return false;
            return NativeMethods.IsWindow(hwnd) && !NativeMethods.IsIconic(hwnd);
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
