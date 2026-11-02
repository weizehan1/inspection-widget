using System;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using InspectionWidget.Native;

namespace InspectionWidget.Credentials
{
    /// <summary>
    /// 真实剪贴板网关（Windows）：三个操作各自在专用 STA 线程上串行执行
    /// （网关级锁保证多调用方不交错），调用方线程无关。
    /// 同锁语义（硬性）：WriteCredential 在一次 OpenClipboard 内完成清空、
    /// 写文本、写令牌格式、写禁历史/云同步格式，并在 CloseClipboard 前读
    /// 序列号填回执；TryClearIfMatches 在一次 OpenClipboard 内读序列号与
    /// 令牌、经 ClipboardGuard.ShouldClear 核对、一致才 EmptyClipboard。
    /// 剪贴板被占用：50ms 间隔重试 3 次；写入最终失败抛 InvalidOperationException，
    /// 清空最终失败返回 false（不冒险误清）。
    /// </summary>
    public sealed class WindowsClipboardGateway : IClipboardGateway
    {
        private const string TokenFormatName = "InspectionWidget.CredentialToken";
        private const string HistoryFormatName = "CanIncludeInClipboardHistory";
        private const string CloudFormatName = "CanUploadToCloudClipboard";
        private const int MaxRetries = 3;
        private const int RetryDelayMs = 50;

        /// <summary>串行化全部剪贴板操作，避免多线程调用交错破坏同锁语义。</summary>
        private readonly object _sync = new object();

        public ClipboardReceipt WriteCredential(string text)
        {
            if (text == null) throw new ArgumentNullException("text");
            string sha256 = ComputeSha256(text);
            string token = Guid.NewGuid().ToString();

            lock (_sync)
            {
                for (int attempt = 0; attempt <= MaxRetries; attempt++)
                {
                    if (attempt > 0)
                        Thread.Sleep(RetryDelayMs);
                    ClipboardReceipt receipt = RunOnSta<ClipboardReceipt>(delegate
                    {
                        return TryWriteOnce(text, token);
                    });
                    if (receipt != null)
                    {
                        receipt.Sha256 = sha256;
                        return receipt;
                    }
                }
            }
            throw new InvalidOperationException("复制失败：剪贴板被占用或不可用。");
        }

        public bool TryClearIfMatches(ClipboardReceipt receipt)
        {
            if (receipt == null)
                return false;

            lock (_sync)
            {
                for (int attempt = 0; attempt <= MaxRetries; attempt++)
                {
                    if (attempt > 0)
                        Thread.Sleep(RetryDelayMs);
                    int outcome = RunOnSta<int>(delegate
                    {
                        return TryClearOnce(receipt);
                    });
                    if (outcome >= 0)
                        return outcome == 1;
                    // outcome < 0：剪贴板打不开，继续重试
                }
            }
            return false;
        }

        /// <summary>
        /// 非原子快照（修复轮 Minor-1，口径同接口注释）：序列号在 OpenClipboard
        /// 之外读、令牌在另一次 Open 内读，两者非同一次 Open 内读取，可能来自
        /// 不同代剪贴板内容。生产决策不得使用本方法做清空判定——清空判定走
        /// TryClearIfMatches 的同锁核对（TryClearOnce 一次 Open 内读齐再核对）。
        /// </summary>
        public ClipboardState ReadState()
        {
            lock (_sync)
            {
                // 序列号无需 OpenClipboard 即可读
                uint sequence = NativeMethods.GetClipboardSequenceNumber();
                string token = RunOnSta<string>(delegate
                {
                    return TryReadToken();
                });
                return new ClipboardState { Sequence = sequence, Token = token };
            }
        }

        // ---------- 单次尝试（均在 STA 线程、单次 OpenClipboard 临界区内） ----------

        /// <summary>
        /// 写入一次：成功返回回执（Sha256 由调用方填），任一步失败返回 null。
        /// 回滚口径（修复轮 Important）：OpenClipboard 失败时尚未动剪贴板，直接
        /// 返回 null 无需回滚；EmptyClipboard 成功之后任一步失败（返回 false 的
        /// 分支，或步骤中抛出的异常），都必须在同一次 OpenClipboard 临界区内、
        /// CloseClipboard 之前先 best-effort 再 EmptyClipboard 一次，使
        /// 「复制失败 ⇒ 剪贴板无凭据残留」成立——否则已写入的明文会留在
        /// 剪贴板上却没有令牌/禁历史/云同步格式、Guard 也拿不到回执（无 30 秒
        /// 清空、无退出兜底）。回滚本身失败（返回 false 或抛异常）一律吞掉，
        /// 不改变原始失败语义：返回 false 的分支仍返回 null（上层重试、最终抛
        /// InvalidOperationException），异常分支回滚后原样 rethrow。
        /// </summary>
        private static ClipboardReceipt TryWriteOnce(string text, string token)
        {
            if (!NativeMethods.OpenClipboard(IntPtr.Zero))
                return null;
            try
            {
                if (!NativeMethods.EmptyClipboard())
                    return null;

                // 自此 EmptyClipboard 已成功：剪贴板已被清空，明文将逐步写入。
                // 以下每条失败路径都先 RollbackLocked() 再退出本临界区。
                try
                {
                    if (!SetUnicodeText(NativeMethods.CF_UNICODETEXT, text))
                    {
                        RollbackLocked();
                        return null;
                    }

                    uint tokenFormat = NativeMethods.RegisterClipboardFormat(TokenFormatName);
                    if (tokenFormat == 0)
                    {
                        RollbackLocked();
                        return null;
                    }
                    if (!SetUnicodeText(tokenFormat, token))
                    {
                        RollbackLocked();
                        return null;
                    }

                    if (!SetDwordZero(HistoryFormatName))
                    {
                        RollbackLocked();
                        return null;
                    }
                    if (!SetDwordZero(CloudFormatName))
                    {
                        RollbackLocked();
                        return null;
                    }

                    // CloseClipboard 前、同一临界区内读序列号填回执
                    uint sequence = NativeMethods.GetClipboardSequenceNumber();
                    return new ClipboardReceipt { Sequence = sequence, Token = token };
                }
                catch (Exception)
                {
                    // 步骤中途抛异常：明文可能已部分写入，同样先回滚再原样上抛，
                    // 不让回滚失败掩盖原始异常。
                    RollbackLocked();
                    throw;
                }
            }
            finally
            {
                NativeMethods.CloseClipboard();
            }
        }

        /// <summary>
        /// 回滚清空：调用方须已 OpenClipboard 且已 EmptyClipboard 成功过一次。
        /// best-effort 语义：返回值不查、异常吞掉，绝不改变调用方的失败语义。
        /// </summary>
        private static void RollbackLocked()
        {
            try
            {
                NativeMethods.EmptyClipboard();
            }
            catch (Exception)
            {
                // 吞掉：回滚失败不掩盖原始失败（上层仍按原路径返回 null/抛错）。
            }
        }

        /// <summary>核对清空一次：1=已清，0=不匹配未清，-1=剪贴板打不开（上层重试）。</summary>
        private static int TryClearOnce(ClipboardReceipt receipt)
        {
            if (!NativeMethods.OpenClipboard(IntPtr.Zero))
                return -1;
            try
            {
                var current = new ClipboardState
                {
                    Sequence = NativeMethods.GetClipboardSequenceNumber(),
                    Token = ReadTokenLocked()
                };
                if (!ClipboardGuard.ShouldClear(receipt, current))
                    return 0;
                return NativeMethods.EmptyClipboard() ? 1 : 0;
            }
            finally
            {
                NativeMethods.CloseClipboard();
            }
        }

        /// <summary>读令牌一次：打不开或无令牌格式都返回 null。</summary>
        private static string TryReadToken()
        {
            if (!NativeMethods.OpenClipboard(IntPtr.Zero))
                return null;
            try
            {
                return ReadTokenLocked();
            }
            finally
            {
                NativeMethods.CloseClipboard();
            }
        }

        /// <summary>调用方须已 OpenClipboard：读本应用令牌格式的 Unicode 字符串。</summary>
        private static string ReadTokenLocked()
        {
            uint tokenFormat = NativeMethods.RegisterClipboardFormat(TokenFormatName);
            if (tokenFormat == 0)
                return null;
            if (!NativeMethods.IsClipboardFormatAvailable(tokenFormat))
                return null;
            IntPtr handle = NativeMethods.GetClipboardData(tokenFormat);
            if (handle == IntPtr.Zero)
                return null;
            IntPtr ptr = NativeMethods.GlobalLock(handle);
            if (ptr == IntPtr.Zero)
                return null;
            try
            {
                return Marshal.PtrToStringUni(ptr);
            }
            finally
            {
                NativeMethods.GlobalUnlock(handle);
            }
        }

        // ---------- 剪贴板数据块写入（所有权转移给系统，成功后不释放） ----------

        private static bool SetUnicodeText(uint format, string value)
        {
            byte[] bytes = Encoding.Unicode.GetBytes(value + "\0");
            IntPtr handle = AllocAndFill(bytes);
            if (handle == IntPtr.Zero)
                return false;
            if (NativeMethods.SetClipboardData(format, handle) == IntPtr.Zero)
            {
                NativeMethods.GlobalFree(handle);
                return false;
            }
            return true;
        }

        /// <summary>禁历史/云同步格式：注册格式名并写入一个 DWORD 0。</summary>
        private static bool SetDwordZero(string formatName)
        {
            uint format = NativeMethods.RegisterClipboardFormat(formatName);
            if (format == 0)
                return false;
            byte[] bytes = new byte[] { 0, 0, 0, 0 };
            IntPtr handle = AllocAndFill(bytes);
            if (handle == IntPtr.Zero)
                return false;
            if (NativeMethods.SetClipboardData(format, handle) == IntPtr.Zero)
            {
                NativeMethods.GlobalFree(handle);
                return false;
            }
            return true;
        }

        private static IntPtr AllocAndFill(byte[] bytes)
        {
            IntPtr handle = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, (UIntPtr)bytes.Length);
            if (handle == IntPtr.Zero)
                return IntPtr.Zero;
            IntPtr ptr = NativeMethods.GlobalLock(handle);
            if (ptr == IntPtr.Zero)
            {
                NativeMethods.GlobalFree(handle);
                return IntPtr.Zero;
            }
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            NativeMethods.GlobalUnlock(handle);
            return handle;
        }

        // ---------- 工具 ----------

        private static string ComputeSha256(string text)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>在专用 STA 线程上同步执行（剪贴板 API 的线程亲和要求），异常原样透传。</summary>
        private static T RunOnSta<T>(Func<T> func)
        {
            T result = default(T);
            Exception error = null;
            var thread = new Thread(new ThreadStart(delegate
            {
                try
                {
                    result = func();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            }));
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();
            if (error != null)
                ExceptionDispatchInfo.Capture(error).Throw();
            return result;
        }
    }
}
