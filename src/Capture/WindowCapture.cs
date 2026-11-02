using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using InspectionWidget.Config;
using InspectionWidget.Native;

namespace InspectionWidget.Capture
{
    /// <summary>截图结果分类。</summary>
    public enum CaptureStatus
    {
        Ok,
        SuspectProtected,
        WindowGone,
        Failed
    }

    /// <summary>截图结果：Status=Ok 时 Image 所有权归调用方；Message 仅日志/诊断与失败提示用。</summary>
    public sealed class CaptureOutcome
    {
        public CaptureStatus Status;
        public Bitmap Image;
        public string Message;
    }

    /// <summary>
    /// 截图引擎：PrintWindow（完整矩形抓 + DWM 裁边）优先，黑图/失败/超时回退
    /// BitBlt 屏幕拷贝（置前核查 + 自遮挡隐藏）。同步方法，调用方负责在工作
    /// 线程调用；类内部不开新线程做 UI 操作，hide/show 由注入方切线程执行。
    /// PrintWindow/BitBlt 均为 Windows 专属路径，本机 Mono 仅保证编译。
    /// </summary>
    public sealed class WindowCapture
    {
        private const uint PwRenderFullContent = 0x00000002;
        private const int PrintWindowTimeoutMilliseconds = 3000;

        // ---------- PrintWindow 超时迟到回收 ----------

        /// <summary>
        /// 一次 PrintWindow 尝试：超时后位图仍被迟到线程使用，禁止 Dispose，
        /// 登记到回收表，迟到线程完成后自行 Dispose 并摘除。
        /// </summary>
        private sealed class PrintAttempt
        {
            public Bitmap Bitmap;
            public bool Ok;
            public volatile bool Finished;
            public volatile bool Abandoned;
        }

        private static readonly object LateLock = new object();
        private static readonly List<PrintAttempt> LateAttempts = new List<PrintAttempt>();

        /// <summary>迟到回收表中未完成条目数（诊断用）。</summary>
        public static int LateAttemptCount
        {
            get { lock (LateLock) { return LateAttempts.Count; } }
        }

        public CaptureOutcome Capture(IntPtr hwnd, CaptureMode mode, Rectangle selfBounds,
            Action hideSelf, Action showSelf)
        {
            // 1. 复核：窗口已关闭/最小化 → WindowGone
            if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd) || NativeMethods.IsIconic(hwnd))
                return Gone("窗口已关闭或最小化");

            Rectangle winRect;
            if (!TryGetWindowRect(hwnd, out winRect) || winRect.Width <= 0 || winRect.Height <= 0)
                return Gone("窗口已关闭或无法取得尺寸");

            // DWM 扩展边框矩形：失败则退回 winRect 并在 Message 注明
            string dwmNote = null;
            Rectangle dwmRect = winRect;
            var dwmNative = new NativeMethods.RECT();
            int hr = NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                out dwmNative, System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.RECT)));
            if (hr == 0)
            {
                Rectangle candidate = dwmNative.ToRectangle();
                if (candidate.Width > 0 && candidate.Height > 0)
                    dwmRect = candidate;
                else
                    dwmNote = "DWM 查询失败，可能含透明边";
            }
            else
            {
                dwmNote = "DWM 查询失败，可能含透明边";
            }

            // 2. PrintWindow 路径
            if (mode != CaptureMode.BitBltOnly)
            {
                CaptureOutcome pwOutcome = TryPrintWindow(hwnd, winRect, dwmRect, mode, dwmNote);
                if (pwOutcome != null)
                    return pwOutcome;
                // null 表示走 BitBlt 回退
                if (mode == CaptureMode.PrintWindowOnly)
                    return Failed("PrintWindow 截图失败", dwmNote); // 防御：TryPrintWindow 在 Only 档不返回 null
            }

            // 3. BitBlt 回退
            return CaptureViaBitBlt(hwnd, winRect, dwmRect, selfBounds, hideSelf, showSelf, dwmNote);
        }

        // ---------- PrintWindow ----------

        /// <summary>返回 null 表示应走 BitBlt 回退（仅 Auto 档）。</summary>
        private CaptureOutcome TryPrintWindow(IntPtr hwnd, Rectangle winRect, Rectangle dwmRect, CaptureMode mode, string dwmNote)
        {
            var bmp = new Bitmap(winRect.Width, winRect.Height);
            var attempt = new PrintAttempt { Bitmap = bmp };

            var thread = new Thread(delegate()
            {
                bool ok = false;
                try
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        IntPtr hdc = g.GetHdc();
                        try
                        {
                            ok = NativeMethods.PrintWindow(hwnd, hdc, PwRenderFullContent);
                        }
                        finally
                        {
                            g.ReleaseHdc(hdc);
                        }
                    }
                }
                catch
                {
                    ok = false;
                }
                attempt.Ok = ok;
                lock (LateLock)
                {
                    attempt.Finished = true;
                    if (attempt.Abandoned)
                    {
                        // 调用方已超时放弃：迟到线程负责释放位图并摘除登记
                        bmp.Dispose();
                        LateAttempts.Remove(attempt);
                    }
                }
            });
            thread.IsBackground = true;
            thread.Name = "InspectionWidget.PrintWindow";
            thread.Start();

            if (!thread.Join(PrintWindowTimeoutMilliseconds))
            {
                bool finishedLate;
                lock (LateLock)
                {
                    // 与迟到线程的 finally 串行化：若它恰已完成，按完成处理
                    finishedLate = attempt.Finished;
                    if (!finishedLate)
                    {
                        attempt.Abandoned = true;
                        LateAttempts.Add(attempt);
                    }
                }
                if (!finishedLate)
                {
                    // 超时：不 Dispose 位图（迟到线程仍在用），已登记回收表
                    if (mode == CaptureMode.PrintWindowOnly)
                        return Failed("PrintWindow 超时（3 秒）", dwmNote);
                    return null;
                }
            }

            if (!attempt.Ok)
            {
                bmp.Dispose();
                if (mode == CaptureMode.PrintWindowOnly)
                    return Failed("PrintWindow 返回失败", dwmNote);
                return null;
            }

            // 裁边：Valid 才裁（裁剪产生新 Bitmap 后 Dispose 原图），无效用整幅
            Bitmap result = bmp;
            CropResult crop = CaptureGeometry.ComputeCrop(winRect, dwmRect);
            if (crop.Valid && (crop.Crop.X != 0 || crop.Crop.Y != 0
                || crop.Crop.Width != winRect.Width || crop.Crop.Height != winRect.Height))
            {
                // PrintWindow 已返回，此处 bmp 所有权在本方法（非迟到回收表）：
                // Clone 抛异常时 finally 释放原图，异常继续上抛；成功时同样
                // 在此释放原图、改用裁剪图，常态行为不变。
                Bitmap cropped = null;
                try
                {
                    cropped = bmp.Clone(crop.Crop, bmp.PixelFormat);
                }
                finally
                {
                    bmp.Dispose();
                }
                result = cropped;
            }

            // 4. 截图后复核：窗口位置/尺寸变了 → WindowGone
            if (WindowMoved(hwnd, winRect))
            {
                result.Dispose();
                return Gone("窗口已移动，请重试");
            }

            if (BlackFrameDetector.IsSuspect(result))
            {
                result.Dispose();
                if (mode == CaptureMode.PrintWindowOnly)
                    return new CaptureOutcome
                    {
                        Status = CaptureStatus.SuspectProtected,
                        Image = null,
                        Message = AppendNote("截图疑似黑图，可能受系统保护", dwmNote)
                    };
                return null;
            }

            return new CaptureOutcome
            {
                Status = CaptureStatus.Ok,
                Image = result,
                Message = dwmNote
            };
        }

        // ---------- BitBlt 回退 ----------

        private CaptureOutcome CaptureViaBitBlt(IntPtr hwnd, Rectangle winRect, Rectangle dwmRect,
            Rectangle selfBounds, Action hideSelf, Action showSelf, string dwmNote)
        {
            if (dwmRect.Width <= 0 || dwmRect.Height <= 0)
                return Failed("目标窗口尺寸无效", dwmNote);

            // 置前并核查结果：不等 → 等 100ms 再查一次，仍不等 → Failed
            NativeMethods.SetForegroundWindow(hwnd);
            NativeMethods.BringWindowToTop(hwnd);
            if (NativeMethods.GetForegroundWindow() != hwnd)
            {
                Thread.Sleep(100);
                if (NativeMethods.GetForegroundWindow() != hwnd)
                    return Failed("窗口置前被拒，请手动点开目标窗口后重试", dwmNote);
            }

            bool hidden = false;
            Bitmap bmp = null;
            try
            {
                if (selfBounds.Width > 0 && selfBounds.Height > 0
                    && selfBounds.IntersectsWith(dwmRect) && hideSelf != null)
                {
                    hideSelf();
                    hidden = true;
                    Thread.Sleep(60);
                }

                bmp = new Bitmap(dwmRect.Width, dwmRect.Height);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(dwmRect.Location, Point.Empty, dwmRect.Size);
                }
            }
            catch (Exception ex)
            {
                if (bmp != null) bmp.Dispose();
                return Failed("屏幕拷贝失败：" + ex.Message, dwmNote);
            }
            finally
            {
                if (hidden && showSelf != null)
                {
                    try { showSelf(); }
                    catch { /* 恢复失败不掩盖截图结果 */ }
                }
            }

            // 4. 截图后复核
            if (WindowMoved(hwnd, winRect))
            {
                bmp.Dispose();
                return Gone("窗口已移动，请重试");
            }

            if (BlackFrameDetector.IsSuspect(bmp))
            {
                bmp.Dispose();
                return new CaptureOutcome
                {
                    Status = CaptureStatus.SuspectProtected,
                    Image = null,
                    Message = AppendNote("截图疑似黑图，可能受系统保护", dwmNote)
                };
            }

            return new CaptureOutcome
            {
                Status = CaptureStatus.Ok,
                Image = bmp,
                Message = dwmNote
            };
        }

        // ---------- 辅助 ----------

        private static bool TryGetWindowRect(IntPtr hwnd, out Rectangle rect)
        {
            rect = Rectangle.Empty;
            NativeMethods.RECT native;
            if (!NativeMethods.GetWindowRect(hwnd, out native))
                return false;
            rect = native.ToRectangle();
            return true;
        }

        /// <summary>抓图后复核：窗口不在了或矩形变了都视为已移动/关闭。</summary>
        private static bool WindowMoved(IntPtr hwnd, Rectangle startRect)
        {
            if (!NativeMethods.IsWindow(hwnd))
                return true;
            Rectangle now;
            if (!TryGetWindowRect(hwnd, out now))
                return true;
            return now != startRect;
        }

        private static CaptureOutcome Gone(string message)
        {
            return new CaptureOutcome { Status = CaptureStatus.WindowGone, Image = null, Message = message };
        }

        private static CaptureOutcome Failed(string message, string note)
        {
            return new CaptureOutcome
            {
                Status = CaptureStatus.Failed,
                Image = null,
                Message = AppendNote(message, note)
            };
        }

        private static string AppendNote(string message, string note)
        {
            if (string.IsNullOrEmpty(note))
                return message;
            if (string.IsNullOrEmpty(message))
                return note;
            return message + "（" + note + "）";
        }
    }
}
