# Review package Task 6: 330ae4b..HEAD
d5e1b91 feat(stage-6): 凭据模块（DPAPI/剪贴板令牌核对/30秒清空）

 src/Credentials/ClipboardGuard.cs          | 136 ++++++++++++++
 src/Credentials/CredentialService.cs       |  64 +++++++
 src/Credentials/DpapiProtector.cs          |  41 +++++
 src/Credentials/IClipboardGateway.cs       |  36 ++++
 src/Credentials/ICredentialProtector.cs    |  12 ++
 src/Credentials/WindowsClipboardGateway.cs | 280 +++++++++++++++++++++++++++++
 src/MainForm.cs                            |  59 +++++-
 src/Native/NativeMethods.cs                |  51 ++++++
 tests/ClipboardGuardTests.cs               | 136 ++++++++++++++
 tests/CredentialServiceTests.cs            |  78 ++++++++
 tests/Fakes.cs                             | 112 ++++++++++++
 tests/Program.cs                           |   2 +
 work/task-6-report.md                      |  86 +++++++++
 13 files changed, 1086 insertions(+), 7 deletions(-)

diff --git a/src/Credentials/ClipboardGuard.cs b/src/Credentials/ClipboardGuard.cs
new file mode 100644
index 0000000..3825f83
--- /dev/null
+++ b/src/Credentials/ClipboardGuard.cs
@@ -0,0 +1,136 @@
+using System;
+using System.Threading;
+
+namespace InspectionWidget.Credentials
+{
+    /// <summary>
+    /// 剪贴板自动清空守卫：Copy 时经网关写入并保存回执、（启用自动清空时）
+    /// 重置清空定时器；到期或退出兜底统一走 TryClearNow——清空与否只认
+    /// 「序列号＋令牌」同锁核对（ShouldClear），同文字新复制一律不清。
+    /// </summary>
+    public sealed class ClipboardGuard : IDisposable
+    {
+        private readonly IClipboardGateway _gateway;
+        private readonly bool _autoClearEnabled;
+        private readonly int _clearAfterSeconds;
+        private readonly object _sync = new object();
+
+        private ClipboardReceipt _receipt;
+        private Timer _timer;
+        private bool _disposed;
+
+        /// <summary>核对不匹配/网关未能清空而跳过清空时触发（供 UI 提示，可空挂）。</summary>
+        public event Action ClearSkipped;
+
+        public ClipboardGuard(IClipboardGateway gateway, bool autoClearEnabled, int clearAfterSeconds = 30)
+        {
+            if (gateway == null) throw new ArgumentNullException("gateway");
+            if (clearAfterSeconds <= 0) throw new ArgumentOutOfRangeException("clearAfterSeconds");
+            _gateway = gateway;
+            _autoClearEnabled = autoClearEnabled;
+            _clearAfterSeconds = clearAfterSeconds;
+        }
+
+        /// <summary>
+        /// 纯决策：receipt 与 current 都非 null，且序列号相等、令牌 Ordinal 相等才清。
+        /// 文字内容（哈希）不参与判定——同文字新复制的序列号与令牌都已变，天然被拒。
+        /// </summary>
+        public static bool ShouldClear(ClipboardReceipt receipt, ClipboardState current)
+        {
+            if (receipt == null || current == null)
+                return false;
+            return receipt.Sequence == current.Sequence
+                && string.Equals(receipt.Token, current.Token, StringComparison.Ordinal);
+        }
+
+        /// <summary>
+        /// 写入凭据并滚动回执：网关写入成功后才替换旧回执并重置定时器；
+        /// 写入失败异常上抛（旧回执与旧定时器保持，上一条凭据仍受保护）。
+        /// </summary>
+        public void Copy(string text)
+        {
+            ClipboardReceipt receipt = _gateway.WriteCredential(text);
+            lock (_sync)
+            {
+                if (_disposed)
+                    return;
+                _receipt = receipt;
+                RestartTimerLocked();
+            }
+        }
+
+        /// <summary>
+        /// 立即按当前回执核对清空（定时到期与退出兜底共用）。无回执→false 且不调网关；
+        /// 网关异常吞掉返回 false（兜底路径不得因剪贴板异常阻断退出）；
+        /// 未清掉时触发 ClearSkipped，清掉后回执作废。
+        /// </summary>
+        public bool TryClearNow()
+        {
+            ClipboardReceipt receipt;
+            lock (_sync)
+            {
+                receipt = _receipt;
+                StopTimerLocked();
+            }
+            if (receipt == null)
+                return false;
+
+            bool cleared;
+            try
+            {
+                cleared = _gateway.TryClearIfMatches(receipt);
+            }
+            catch (Exception)
+            {
+                cleared = false;
+            }
+
+            if (cleared)
+            {
+                lock (_sync)
+                {
+                    if (object.ReferenceEquals(_receipt, receipt))
+                        _receipt = null;
+                }
+            }
+            else
+            {
+                Action handler = ClearSkipped;
+                if (handler != null)
+                    handler();
+            }
+            return cleared;
+        }
+
+        private void RestartTimerLocked()
+        {
+            StopTimerLocked();
+            if (!_autoClearEnabled || _disposed)
+                return;
+            _timer = new Timer(OnTimerElapsed, null, _clearAfterSeconds * 1000, Timeout.Infinite);
+        }
+
+        private void StopTimerLocked()
+        {
+            if (_timer != null)
+            {
+                _timer.Dispose();
+                _timer = null;
+            }
+        }
+
+        private void OnTimerElapsed(object state)
+        {
+            TryClearNow();
+        }
+
+        public void Dispose()
+        {
+            lock (_sync)
+            {
+                _disposed = true;
+                StopTimerLocked();
+            }
+        }
+    }
+}
diff --git a/src/Credentials/CredentialService.cs b/src/Credentials/CredentialService.cs
new file mode 100644
index 0000000..7430303
--- /dev/null
+++ b/src/Credentials/CredentialService.cs
@@ -0,0 +1,64 @@
+using System;
+using System.Security.Cryptography;
+using InspectionWidget.Config;
+
+namespace InspectionWidget.Credentials
+{
+    /// <summary>凭据复制结果：成功 / 密文解不开需重设 / 其他失败。</summary>
+    public enum CopyResult
+    {
+        Copied,
+        NeedsReset,
+        Failed
+    }
+
+    /// <summary>
+    /// 凭据复制编排：Unprotect → Guard.Copy。异常口径：密文为空与
+    /// CryptographicException → NeedsReset（不触碰剪贴板）；其余异常 → Failed。
+    /// </summary>
+    public sealed class CredentialService
+    {
+        private readonly ICredentialProtector _protector;
+        private readonly ClipboardGuard _guard;
+
+        public CredentialService(ICredentialProtector protector, ClipboardGuard guard)
+        {
+            if (protector == null) throw new ArgumentNullException("protector");
+            if (guard == null) throw new ArgumentNullException("guard");
+            _protector = protector;
+            _guard = guard;
+        }
+
+        public CopyResult Copy(CredentialEntry entry)
+        {
+            if (entry == null)
+                return CopyResult.Failed;
+            if (string.IsNullOrEmpty(entry.EncryptedSecret))
+                return CopyResult.NeedsReset;
+
+            string plain;
+            try
+            {
+                plain = _protector.Unprotect(entry.EncryptedSecret);
+            }
+            catch (CryptographicException)
+            {
+                return CopyResult.NeedsReset;
+            }
+            catch (Exception)
+            {
+                return CopyResult.Failed;
+            }
+
+            try
+            {
+                _guard.Copy(plain);
+            }
+            catch (Exception)
+            {
+                return CopyResult.Failed;
+            }
+            return CopyResult.Copied;
+        }
+    }
+}
diff --git a/src/Credentials/DpapiProtector.cs b/src/Credentials/DpapiProtector.cs
new file mode 100644
index 0000000..23df802
--- /dev/null
+++ b/src/Credentials/DpapiProtector.cs
@@ -0,0 +1,41 @@
+using System;
+using System.Security.Cryptography;
+using System.Text;
+
+namespace InspectionWidget.Credentials
+{
+    /// <summary>
+    /// DPAPI 实现（CurrentUser、无额外熵、UTF-8）：密文与当前 Windows 用户绑定，
+    /// 换机/换用户解不开——Unprotect 失败统一抛 CryptographicException
+    /// （含 Base64 格式错误，先包一层再抛），上层据此提示需重设。
+    /// </summary>
+    public sealed class DpapiProtector : ICredentialProtector
+    {
+        public string Protect(string plainText)
+        {
+            if (plainText == null) throw new ArgumentNullException("plainText");
+            byte[] data = Encoding.UTF8.GetBytes(plainText);
+            byte[] encrypted = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
+            return Convert.ToBase64String(encrypted);
+        }
+
+        public string Unprotect(string base64)
+        {
+            if (string.IsNullOrEmpty(base64))
+                throw new CryptographicException("凭据密文为空。");
+
+            byte[] encrypted;
+            try
+            {
+                encrypted = Convert.FromBase64String(base64);
+            }
+            catch (FormatException ex)
+            {
+                throw new CryptographicException("凭据密文格式无效。", ex);
+            }
+
+            byte[] data = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
+            return Encoding.UTF8.GetString(data);
+        }
+    }
+}
diff --git a/src/Credentials/IClipboardGateway.cs b/src/Credentials/IClipboardGateway.cs
new file mode 100644
index 0000000..e7e5c2f
--- /dev/null
+++ b/src/Credentials/IClipboardGateway.cs
@@ -0,0 +1,36 @@
+using System;
+
+namespace InspectionWidget.Credentials
+{
+    /// <summary>一次凭据写入的回执：序列号＋令牌是同锁核对凭证，Sha256 仅记录内容哈希（不作核对依据）。</summary>
+    public sealed class ClipboardReceipt
+    {
+        public uint Sequence;
+        public string Token;
+        public string Sha256;
+    }
+
+    /// <summary>剪贴板当前状态快照（核对用）。</summary>
+    public sealed class ClipboardState
+    {
+        public uint Sequence;
+        public string Token;
+    }
+
+    /// <summary>
+    /// 剪贴板抽象。实现须保证：WriteCredential 的写入与回执读取在同一次
+    /// OpenClipboard 临界区内完成；TryClearIfMatches 的序列号＋令牌核对与
+    /// 清空在同一次 OpenClipboard 临界区内完成（同锁核对，防竞态误清）。
+    /// </summary>
+    public interface IClipboardGateway
+    {
+        /// <summary>写入凭据文本与本应用令牌，返回回执。失败（占用重试后仍失败）抛 InvalidOperationException。</summary>
+        ClipboardReceipt WriteCredential(string text);
+
+        /// <summary>序列号与令牌都与回执一致才清空，返回是否真的清了。</summary>
+        bool TryClearIfMatches(ClipboardReceipt receipt);
+
+        /// <summary>读取当前序列号与令牌（令牌读不到时为 null）。</summary>
+        ClipboardState ReadState();
+    }
+}
diff --git a/src/Credentials/ICredentialProtector.cs b/src/Credentials/ICredentialProtector.cs
new file mode 100644
index 0000000..760bd5f
--- /dev/null
+++ b/src/Credentials/ICredentialProtector.cs
@@ -0,0 +1,12 @@
+namespace InspectionWidget.Credentials
+{
+    /// <summary>凭据加解密抽象：明文只在内存中短暂存在，落盘的只有 Base64 密文。</summary>
+    public interface ICredentialProtector
+    {
+        /// <summary>加密明文，返回 Base64 密文。</summary>
+        string Protect(string plainText);
+
+        /// <summary>解密 Base64 密文；失败抛 CryptographicException（统一异常口径）。</summary>
+        string Unprotect(string base64);
+    }
+}
diff --git a/src/Credentials/WindowsClipboardGateway.cs b/src/Credentials/WindowsClipboardGateway.cs
new file mode 100644
index 0000000..d54984c
--- /dev/null
+++ b/src/Credentials/WindowsClipboardGateway.cs
@@ -0,0 +1,280 @@
+using System;
+using System.Runtime.ExceptionServices;
+using System.Runtime.InteropServices;
+using System.Security.Cryptography;
+using System.Text;
+using System.Threading;
+using InspectionWidget.Native;
+
+namespace InspectionWidget.Credentials
+{
+    /// <summary>
+    /// 真实剪贴板网关（Windows）：三个操作各自在专用 STA 线程上串行执行
+    /// （网关级锁保证多调用方不交错），调用方线程无关。
+    /// 同锁语义（硬性）：WriteCredential 在一次 OpenClipboard 内完成清空、
+    /// 写文本、写令牌格式、写禁历史/云同步格式，并在 CloseClipboard 前读
+    /// 序列号填回执；TryClearIfMatches 在一次 OpenClipboard 内读序列号与
+    /// 令牌、经 ClipboardGuard.ShouldClear 核对、一致才 EmptyClipboard。
+    /// 剪贴板被占用：50ms 间隔重试 3 次；写入最终失败抛 InvalidOperationException，
+    /// 清空最终失败返回 false（不冒险误清）。
+    /// </summary>
+    public sealed class WindowsClipboardGateway : IClipboardGateway
+    {
+        private const string TokenFormatName = "InspectionWidget.CredentialToken";
+        private const string HistoryFormatName = "CanIncludeInClipboardHistory";
+        private const string CloudFormatName = "CanUploadToCloudClipboard";
+        private const int MaxRetries = 3;
+        private const int RetryDelayMs = 50;
+
+        /// <summary>串行化全部剪贴板操作，避免多线程调用交错破坏同锁语义。</summary>
+        private readonly object _sync = new object();
+
+        public ClipboardReceipt WriteCredential(string text)
+        {
+            if (text == null) throw new ArgumentNullException("text");
+            string sha256 = ComputeSha256(text);
+            string token = Guid.NewGuid().ToString();
+
+            lock (_sync)
+            {
+                for (int attempt = 0; attempt <= MaxRetries; attempt++)
+                {
+                    if (attempt > 0)
+                        Thread.Sleep(RetryDelayMs);
+                    ClipboardReceipt receipt = RunOnSta<ClipboardReceipt>(delegate
+                    {
+                        return TryWriteOnce(text, token);
+                    });
+                    if (receipt != null)
+                    {
+                        receipt.Sha256 = sha256;
+                        return receipt;
+                    }
+                }
+            }
+            throw new InvalidOperationException("复制失败：剪贴板被占用或不可用。");
+        }
+
+        public bool TryClearIfMatches(ClipboardReceipt receipt)
+        {
+            if (receipt == null)
+                return false;
+
+            lock (_sync)
+            {
+                for (int attempt = 0; attempt <= MaxRetries; attempt++)
+                {
+                    if (attempt > 0)
+                        Thread.Sleep(RetryDelayMs);
+                    int outcome = RunOnSta<int>(delegate
+                    {
+                        return TryClearOnce(receipt);
+                    });
+                    if (outcome >= 0)
+                        return outcome == 1;
+                    // outcome < 0：剪贴板打不开，继续重试
+                }
+            }
+            return false;
+        }
+
+        public ClipboardState ReadState()
+        {
+            lock (_sync)
+            {
+                // 序列号无需 OpenClipboard 即可读
+                uint sequence = NativeMethods.GetClipboardSequenceNumber();
+                string token = RunOnSta<string>(delegate
+                {
+                    return TryReadToken();
+                });
+                return new ClipboardState { Sequence = sequence, Token = token };
+            }
+        }
+
+        // ---------- 单次尝试（均在 STA 线程、单次 OpenClipboard 临界区内） ----------
+
+        /// <summary>写入一次：成功返回回执（Sha256 由调用方填），任一步失败返回 null。</summary>
+        private static ClipboardReceipt TryWriteOnce(string text, string token)
+        {
+            if (!NativeMethods.OpenClipboard(IntPtr.Zero))
+                return null;
+            try
+            {
+                if (!NativeMethods.EmptyClipboard())
+                    return null;
+                if (!SetUnicodeText(NativeMethods.CF_UNICODETEXT, text))
+                    return null;
+
+                uint tokenFormat = NativeMethods.RegisterClipboardFormat(TokenFormatName);
+                if (tokenFormat == 0)
+                    return null;
+                if (!SetUnicodeText(tokenFormat, token))
+                    return null;
+
+                if (!SetDwordZero(HistoryFormatName))
+                    return null;
+                if (!SetDwordZero(CloudFormatName))
+                    return null;
+
+                // CloseClipboard 前、同一临界区内读序列号填回执
+                uint sequence = NativeMethods.GetClipboardSequenceNumber();
+                return new ClipboardReceipt { Sequence = sequence, Token = token };
+            }
+            finally
+            {
+                NativeMethods.CloseClipboard();
+            }
+        }
+
+        /// <summary>核对清空一次：1=已清，0=不匹配未清，-1=剪贴板打不开（上层重试）。</summary>
+        private static int TryClearOnce(ClipboardReceipt receipt)
+        {
+            if (!NativeMethods.OpenClipboard(IntPtr.Zero))
+                return -1;
+            try
+            {
+                var current = new ClipboardState
+                {
+                    Sequence = NativeMethods.GetClipboardSequenceNumber(),
+                    Token = ReadTokenLocked()
+                };
+                if (!ClipboardGuard.ShouldClear(receipt, current))
+                    return 0;
+                return NativeMethods.EmptyClipboard() ? 1 : 0;
+            }
+            finally
+            {
+                NativeMethods.CloseClipboard();
+            }
+        }
+
+        /// <summary>读令牌一次：打不开或无令牌格式都返回 null。</summary>
+        private static string TryReadToken()
+        {
+            if (!NativeMethods.OpenClipboard(IntPtr.Zero))
+                return null;
+            try
+            {
+                return ReadTokenLocked();
+            }
+            finally
+            {
+                NativeMethods.CloseClipboard();
+            }
+        }
+
+        /// <summary>调用方须已 OpenClipboard：读本应用令牌格式的 Unicode 字符串。</summary>
+        private static string ReadTokenLocked()
+        {
+            uint tokenFormat = NativeMethods.RegisterClipboardFormat(TokenFormatName);
+            if (tokenFormat == 0)
+                return null;
+            if (!NativeMethods.IsClipboardFormatAvailable(tokenFormat))
+                return null;
+            IntPtr handle = NativeMethods.GetClipboardData(tokenFormat);
+            if (handle == IntPtr.Zero)
+                return null;
+            IntPtr ptr = NativeMethods.GlobalLock(handle);
+            if (ptr == IntPtr.Zero)
+                return null;
+            try
+            {
+                return Marshal.PtrToStringUni(ptr);
+            }
+            finally
+            {
+                NativeMethods.GlobalUnlock(handle);
+            }
+        }
+
+        // ---------- 剪贴板数据块写入（所有权转移给系统，成功后不释放） ----------
+
+        private static bool SetUnicodeText(uint format, string value)
+        {
+            byte[] bytes = Encoding.Unicode.GetBytes(value + "\0");
+            IntPtr handle = AllocAndFill(bytes);
+            if (handle == IntPtr.Zero)
+                return false;
+            if (NativeMethods.SetClipboardData(format, handle) == IntPtr.Zero)
+            {
+                NativeMethods.GlobalFree(handle);
+                return false;
+            }
+            return true;
+        }
+
+        /// <summary>禁历史/云同步格式：注册格式名并写入一个 DWORD 0。</summary>
+        private static bool SetDwordZero(string formatName)
+        {
+            uint format = NativeMethods.RegisterClipboardFormat(formatName);
+            if (format == 0)
+                return false;
+            byte[] bytes = new byte[] { 0, 0, 0, 0 };
+            IntPtr handle = AllocAndFill(bytes);
+            if (handle == IntPtr.Zero)
+                return false;
+            if (NativeMethods.SetClipboardData(format, handle) == IntPtr.Zero)
+            {
+                NativeMethods.GlobalFree(handle);
+                return false;
+            }
+            return true;
+        }
+
+        private static IntPtr AllocAndFill(byte[] bytes)
+        {
+            IntPtr handle = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, (UIntPtr)bytes.Length);
+            if (handle == IntPtr.Zero)
+                return IntPtr.Zero;
+            IntPtr ptr = NativeMethods.GlobalLock(handle);
+            if (ptr == IntPtr.Zero)
+            {
+                NativeMethods.GlobalFree(handle);
+                return IntPtr.Zero;
+            }
+            Marshal.Copy(bytes, 0, ptr, bytes.Length);
+            NativeMethods.GlobalUnlock(handle);
+            return handle;
+        }
+
+        // ---------- 工具 ----------
+
+        private static string ComputeSha256(string text)
+        {
+            using (SHA256 sha = SHA256.Create())
+            {
+                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
+                var sb = new StringBuilder(hash.Length * 2);
+                for (int i = 0; i < hash.Length; i++)
+                    sb.Append(hash[i].ToString("x2"));
+                return sb.ToString();
+            }
+        }
+
+        /// <summary>在专用 STA 线程上同步执行（剪贴板 API 的线程亲和要求），异常原样透传。</summary>
+        private static T RunOnSta<T>(Func<T> func)
+        {
+            T result = default(T);
+            Exception error = null;
+            var thread = new Thread(new ThreadStart(delegate
+            {
+                try
+                {
+                    result = func();
+                }
+                catch (Exception ex)
+                {
+                    error = ex;
+                }
+            }));
+            thread.SetApartmentState(ApartmentState.STA);
+            thread.IsBackground = true;
+            thread.Start();
+            thread.Join();
+            if (error != null)
+                ExceptionDispatchInfo.Capture(error).Throw();
+            return result;
+        }
+    }
+}
diff --git a/src/MainForm.cs b/src/MainForm.cs
index 5276895..4976524 100644
--- a/src/MainForm.cs
+++ b/src/MainForm.cs
@@ -5,10 +5,11 @@ using System.Drawing;
 using System.Drawing.Drawing2D;
 using System.IO;
 using System.Windows.Forms;
 using InspectionWidget.Capture;
 using InspectionWidget.Config;
+using InspectionWidget.Credentials;
 using IClock = InspectionWidget.Storage.IClock;
 using ScreenshotSaver = InspectionWidget.Storage.ScreenshotSaver;
 using StorageSaveResult = InspectionWidget.Storage.SaveResult;
 using SystemClock = InspectionWidget.Storage.SystemClock;
 
@@ -39,10 +40,17 @@ namespace InspectionWidget
         private readonly ConfigStore _store;
         private readonly PanelStateMachine _stateMachine;
         private readonly HookManager _hooks;
         private AppConfig _config;
 
+        // ---------- 凭据模块（Task 6） ----------
+
+        private readonly ICredentialProtector _protector = new DpapiProtector();
+        private readonly IClipboardGateway _clipboardGateway = new WindowsClipboardGateway();
+        private ClipboardGuard _clipboardGuard;
+        private CredentialService _credentialService;
+
         // ---------- 保存链路（Task 5） ----------
 
         /// <summary>
         /// 一次截图请求快照：进入 Capturing 时组装，保存与失败重试全程共用；
         /// 日期在此刻以 IClock 固定，保存/重试都用它，跨午夜不串日。
@@ -133,10 +141,11 @@ namespace InspectionWidget
 
         protected override void Dispose(bool disposing)
         {
             if (disposing)
             {
+                if (_clipboardGuard != null) _clipboardGuard.Dispose();
                 if (_hooks != null) _hooks.Dispose();
                 if (_pendingImage != null)
                 {
                     _pendingImage.Dispose();
                     _pendingGeneration++;
@@ -169,10 +178,11 @@ namespace InspectionWidget
             BuildStaticLayout();
 
             LoadResult load = _store.Load();
             _config = load.Config != null ? load.Config : AppConfig.CreateDefault();
             EnsureConfigLists();
+            RebuildClipboardGuard();
 
             ClientSize = new Size(PanelLayout.PanelWidth, ComputeCurrentHeight());
             RestorePosition();
             LayoutSections();
 
@@ -444,10 +454,29 @@ namespace InspectionWidget
 
         private void EnsureConfigLists()
         {
             if (_config.Devices == null) _config.Devices = new List<string>();
             if (_config.Credentials == null) _config.Credentials = new List<CredentialEntry>();
+            // 密文为空的条目视同需重设（与设置层口径一致，仅运行时标记不落盘）
+            foreach (CredentialEntry entry in _config.Credentials)
+            {
+                if (string.IsNullOrEmpty(entry.EncryptedSecret))
+                    entry.NeedsReset = true;
+            }
+        }
+
+        /// <summary>
+        /// 按当前配置的 AutoClearClipboard 重建剪贴板守卫与凭据服务：
+        /// 初始化时调用；设置变更后的重建由 Task 7 的保存事件接入本方法。
+        /// 旧 Guard 先 Dispose（停掉其未到期定时器）。
+        /// </summary>
+        private void RebuildClipboardGuard()
+        {
+            if (_clipboardGuard != null)
+                _clipboardGuard.Dispose();
+            _clipboardGuard = new ClipboardGuard(_clipboardGateway, _config.AutoClearClipboard);
+            _credentialService = new CredentialService(_protector, _clipboardGuard);
         }
 
         /// <summary>
         /// 加载/刷新配置的同一入口：跑 ValidateAll，名单有错时常驻错误提示并禁用
         /// 设备按钮（截图不可用），修正后自动恢复；随后重建动态按钮与根目录行。
@@ -638,20 +667,29 @@ namespace InspectionWidget
         {
             var button = sender as Button;
             if (button == null) return;
             var entry = button.Tag as CredentialEntry;
             if (entry == null) return;
+            if (_credentialService == null) return;
 
-            if (entry.NeedsReset)
+            // 全链路走 CredentialService：解密→写剪贴板+令牌→30 秒自动清空（Guard）
+            CopyResult result = _credentialService.Copy(entry);
+            switch (result)
             {
-                MessageBox.Show(this, "凭据「" + entry.Title + "」需要重新设置，请到设置中处理。", "巡检截图",
-                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
-                return;
+                case CopyResult.Copied:
+                    ShowSuccessFeedback("已复制");
+                    break;
+                case CopyResult.NeedsReset:
+                    // 运行时标记并刷新按钮显示（内存标记，不落盘）
+                    entry.NeedsReset = true;
+                    RebuildCredentialControls();
+                    ShowFeedback("该凭据需重新设置，请到设置中重新填写", true);
+                    break;
+                default: // Failed
+                    ShowFeedback("复制失败，请重试", true);
+                    break;
             }
-            // 本任务只做占位提示，Task 6 接真实复制
-            MessageBox.Show(this, "凭据「" + entry.Title + "」（阶段 6 接入复制）", "巡检截图",
-                MessageBoxButtons.OK, MessageBoxIcon.Information);
         }
 
         private void OnSettingsClicked(object sender, EventArgs e)
         {
             // 本任务占位，Task 7 接真实 SettingsForm
@@ -1312,10 +1350,17 @@ namespace InspectionWidget
             SaveBestEffort();
         }
 
         private void OnFormClosing(object sender, FormClosingEventArgs e)
         {
+            // 退出兜底（Task 6）：先按回执核对清剪贴板（有回执且序列号+令牌
+            // 匹配才清，Guard 内部处理），再 Dispose Guard，最后走原有逻辑。
+            if (_clipboardGuard != null)
+            {
+                _clipboardGuard.TryClearNow();
+                _clipboardGuard.Dispose();
+            }
             _hooks.Stop();
             _hooks.Dispose();
             SaveWindowPosition();
         }
 
diff --git a/src/Native/NativeMethods.cs b/src/Native/NativeMethods.cs
index 0daa9a1..5e9e1c5 100644
--- a/src/Native/NativeMethods.cs
+++ b/src/Native/NativeMethods.cs
@@ -166,10 +166,61 @@ namespace InspectionWidget.Native
 
         [DllImport("user32.dll", SetLastError = true)]
         [return: MarshalAs(UnmanagedType.Bool)]
         public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
 
+        // ---------- user32：剪贴板（Task 6） ----------
+
+        /// <summary>剪贴板格式：Unicode 文本。</summary>
+        public const uint CF_UNICODETEXT = 13;
+
+        [DllImport("user32.dll", SetLastError = true)]
+        [return: MarshalAs(UnmanagedType.Bool)]
+        public static extern bool OpenClipboard(IntPtr hWndNewOwner);
+
+        [DllImport("user32.dll")]
+        [return: MarshalAs(UnmanagedType.Bool)]
+        public static extern bool CloseClipboard();
+
+        [DllImport("user32.dll", SetLastError = true)]
+        [return: MarshalAs(UnmanagedType.Bool)]
+        public static extern bool EmptyClipboard();
+
+        [DllImport("user32.dll", SetLastError = true)]
+        public static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);
+
+        [DllImport("user32.dll", SetLastError = true)]
+        public static extern IntPtr GetClipboardData(uint uFormat);
+
+        [DllImport("user32.dll", SetLastError = true)]
+        [return: MarshalAs(UnmanagedType.Bool)]
+        public static extern bool IsClipboardFormatAvailable(uint format);
+
+        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
+        public static extern uint RegisterClipboardFormat(string lpszFormat);
+
+        [DllImport("user32.dll")]
+        public static extern uint GetClipboardSequenceNumber();
+
+        // ---------- kernel32：全局内存（剪贴板数据块） ----------
+
+        /// <summary>GlobalAlloc 标志：可移动内存（剪贴板 SetClipboardData 要求）。</summary>
+        public const uint GMEM_MOVEABLE = 0x0002;
+
+        [DllImport("kernel32.dll", SetLastError = true)]
+        public static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);
+
+        [DllImport("kernel32.dll", SetLastError = true)]
+        public static extern IntPtr GlobalFree(IntPtr hMem);
+
+        [DllImport("kernel32.dll", SetLastError = true)]
+        public static extern IntPtr GlobalLock(IntPtr hMem);
+
+        [DllImport("kernel32.dll")]
+        [return: MarshalAs(UnmanagedType.Bool)]
+        public static extern bool GlobalUnlock(IntPtr hMem);
+
         // ---------- dwmapi ----------
 
         /// <summary>DwmGetWindowAttribute 属性：扩展边框边界（真实内容区，含透明边修正）。</summary>
         public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
 
diff --git a/tests/ClipboardGuardTests.cs b/tests/ClipboardGuardTests.cs
new file mode 100644
index 0000000..5e5d109
--- /dev/null
+++ b/tests/ClipboardGuardTests.cs
@@ -0,0 +1,136 @@
+using System;
+using InspectionWidget.Credentials;
+
+namespace InspectionWidget.Tests
+{
+    /// <summary>
+    /// ClipboardGuard 决策与编排（假网关）：ShouldClear 纯函数六分支 +
+    /// Copy 回执滚动、TryClearNow 匹配/不匹配/同文字新复制（Review Focus 第 3 条）。
+    /// 定时器在用例中以超长间隔（3600 秒）构造，避免真实到期干扰断言。
+    /// </summary>
+    public static class ClipboardGuardTests
+    {
+        private static ClipboardReceipt Receipt(uint sequence, string token)
+        {
+            return new ClipboardReceipt { Sequence = sequence, Token = token, Sha256 = "hash" };
+        }
+
+        private static ClipboardState State(uint sequence, string token)
+        {
+            return new ClipboardState { Sequence = sequence, Token = token };
+        }
+
+        public static void RunAll()
+        {
+            TestRunner.Run("决策：序列号与令牌都同 → ShouldClear=true", () =>
+            {
+                Assert.True(ClipboardGuard.ShouldClear(Receipt(7, "tok-a"), State(7, "tok-a")));
+            });
+
+            TestRunner.Run("决策：仅序列号变 → ShouldClear=false", () =>
+            {
+                Assert.False(ClipboardGuard.ShouldClear(Receipt(7, "tok-a"), State(8, "tok-a")));
+            });
+
+            TestRunner.Run("决策：仅令牌变 → ShouldClear=false", () =>
+            {
+                Assert.False(ClipboardGuard.ShouldClear(Receipt(7, "tok-a"), State(7, "tok-b")));
+            });
+
+            TestRunner.Run("决策：同文字新复制（序列号与令牌都变、哈希相同）→ ShouldClear=false", () =>
+            {
+                // Review Focus 第 3 条锚定：文字相同（Sha256 相同）不构成可清依据，
+                // 只认序列号＋令牌这一对同锁凭证。
+                var receipt = new ClipboardReceipt { Sequence = 3, Token = "tok-old", Sha256 = "same-hash" };
+                var current = new ClipboardState { Sequence = 4, Token = "tok-new" };
+                Assert.False(ClipboardGuard.ShouldClear(receipt, current),
+                    "同文字新复制后不得误清（Review Focus 第 3 条）");
+            });
+
+            TestRunner.Run("决策：receipt 为 null → ShouldClear=false", () =>
+            {
+                Assert.False(ClipboardGuard.ShouldClear(null, State(1, "tok")));
+            });
+
+            TestRunner.Run("决策：current 为 null → ShouldClear=false", () =>
+            {
+                Assert.False(ClipboardGuard.ShouldClear(Receipt(1, "tok"), null));
+            });
+
+            TestRunner.Run("编排：Copy 两次后 TryClearNow 只认第二次回执", () =>
+            {
+                var gateway = new FakeClipboardGateway();
+                using (var guard = new ClipboardGuard(gateway, true, 3600))
+                {
+                    guard.Copy("first-secret");
+                    string firstToken = gateway.ReadState().Token;
+
+                    guard.Copy("second-secret");
+                    string secondToken = gateway.ReadState().Token;
+                    Assert.False(string.Equals(firstToken, secondToken, StringComparison.Ordinal),
+                        "两次写入令牌应不同（构造前提）");
+
+                    Assert.True(guard.TryClearNow(), "当前回执与剪贴板一致，应清成功");
+                    Assert.NotNull(gateway.LastClearReceipt);
+                    Assert.Equal(secondToken, gateway.LastClearReceipt.Token,
+                        "清空核对必须用第二次回执，不得用第一次");
+                    Assert.Null(gateway.CurrentText, "清空后假网关内容应为空");
+                }
+            });
+
+            TestRunner.Run("编排：状态匹配时 TryClearNow 清空并返回 true", () =>
+            {
+                var gateway = new FakeClipboardGateway();
+                using (var guard = new ClipboardGuard(gateway, true, 3600))
+                {
+                    guard.Copy("pw-123");
+                    Assert.True(guard.TryClearNow());
+                    Assert.Null(gateway.CurrentText);
+                    Assert.Equal(1, gateway.ClearAttempts);
+                    // 已清后再调：无回执，直接 false 且不再调网关
+                    Assert.False(guard.TryClearNow());
+                    Assert.Equal(1, gateway.ClearAttempts);
+                }
+            });
+
+            TestRunner.Run("编排：同文字新复制后 TryClearNow 不误清、内容保留、触发 ClearSkipped", () =>
+            {
+                var gateway = new FakeClipboardGateway();
+                using (var guard = new ClipboardGuard(gateway, true, 3600))
+                {
+                    bool skipped = false;
+                    guard.ClearSkipped += delegate { skipped = true; };
+
+                    guard.Copy("same-text");
+                    gateway.SimulateExternalCopy("same-text"); // 文字相同，序列号+令牌已换
+
+                    Assert.False(guard.TryClearNow(), "同文字新复制不得误清（Review Focus 第 3 条）");
+                    Assert.Equal("same-text", gateway.CurrentText, "外部新复制的内容必须保留");
+                    Assert.True(skipped, "不匹配跳过应触发 ClearSkipped 通知");
+                }
+            });
+
+            TestRunner.Run("编排：网关返回 false 时 TryClearNow 不抛异常", () =>
+            {
+                var gateway = new FakeClipboardGateway();
+                using (var guard = new ClipboardGuard(gateway, true, 3600))
+                {
+                    guard.Copy("abc");
+                    gateway.SimulateExternalCopy("other-text"); // 状态不匹配 → 网关返回 false
+                    bool result = guard.TryClearNow(); // 不抛即达标
+                    Assert.False(result);
+                }
+            });
+
+            TestRunner.Run("编排：从未 Copy 时 TryClearNow 返回 false 且不调网关", () =>
+            {
+                var gateway = new FakeClipboardGateway();
+                using (var guard = new ClipboardGuard(gateway, true, 3600))
+                {
+                    Assert.False(guard.TryClearNow());
+                    Assert.Equal(0, gateway.ClearAttempts);
+                }
+            });
+        }
+    }
+}
diff --git a/tests/CredentialServiceTests.cs b/tests/CredentialServiceTests.cs
new file mode 100644
index 0000000..82f0031
--- /dev/null
+++ b/tests/CredentialServiceTests.cs
@@ -0,0 +1,78 @@
+using InspectionWidget.Config;
+using InspectionWidget.Credentials;
+
+namespace InspectionWidget.Tests
+{
+    /// <summary>
+    /// CredentialService 编排（FakeProtector + FakeClipboardGateway + 真 ClipboardGuard）：
+    /// 正常复制、密文损坏、空密文、网关失败四条路径。
+    /// </summary>
+    public static class CredentialServiceTests
+    {
+        private static CredentialEntry Entry(string encryptedSecret)
+        {
+            return new CredentialEntry { Title = "路由器", EncryptedSecret = encryptedSecret, NeedsReset = false };
+        }
+
+        public static void RunAll()
+        {
+            TestRunner.Run("服务：正常复制 → Copied 且网关写入文本=原文", () =>
+            {
+                var protector = new FakeProtector();
+                var gateway = new FakeClipboardGateway();
+                using (var guard = new ClipboardGuard(gateway, true, 3600))
+                {
+                    var service = new CredentialService(protector, guard);
+                    CopyResult result = service.Copy(Entry(protector.Protect("s3cret-pw")));
+
+                    Assert.Equal(CopyResult.Copied, result);
+                    Assert.Equal("s3cret-pw", gateway.LastWrittenText, "网关应收到解密后的原文");
+                    Assert.Equal(1, gateway.WriteCount);
+                }
+            });
+
+            TestRunner.Run("服务：密文损坏 → NeedsReset 且网关未被写入", () =>
+            {
+                var protector = new FakeProtector();
+                var gateway = new FakeClipboardGateway();
+                using (var guard = new ClipboardGuard(gateway, true, 3600))
+                {
+                    var service = new CredentialService(protector, guard);
+                    CopyResult result = service.Copy(Entry("!!!not-base64!!!"));
+
+                    Assert.Equal(CopyResult.NeedsReset, result);
+                    Assert.Equal(0, gateway.WriteCount, "解密失败不得触碰剪贴板");
+                    Assert.Null(gateway.LastWrittenText);
+                }
+            });
+
+            TestRunner.Run("服务：EncryptedSecret 为空 → NeedsReset 且网关未被写入", () =>
+            {
+                var protector = new FakeProtector();
+                var gateway = new FakeClipboardGateway();
+                using (var guard = new ClipboardGuard(gateway, true, 3600))
+                {
+                    var service = new CredentialService(protector, guard);
+
+                    Assert.Equal(CopyResult.NeedsReset, service.Copy(Entry(string.Empty)));
+                    Assert.Equal(CopyResult.NeedsReset, service.Copy(Entry(null)));
+                    Assert.Equal(0, gateway.WriteCount);
+                }
+            });
+
+            TestRunner.Run("服务：网关写入失败 → Failed（不抛异常）", () =>
+            {
+                var protector = new FakeProtector();
+                var gateway = new FakeClipboardGateway();
+                gateway.ThrowOnWrite = true;
+                using (var guard = new ClipboardGuard(gateway, true, 3600))
+                {
+                    var service = new CredentialService(protector, guard);
+                    CopyResult result = service.Copy(Entry(protector.Protect("pw")));
+
+                    Assert.Equal(CopyResult.Failed, result);
+                }
+            });
+        }
+    }
+}
diff --git a/tests/Fakes.cs b/tests/Fakes.cs
new file mode 100644
index 0000000..a401426
--- /dev/null
+++ b/tests/Fakes.cs
@@ -0,0 +1,112 @@
+using System;
+using System.Security.Cryptography;
+using System.Text;
+using InspectionWidget.Credentials;
+
+namespace InspectionWidget.Tests
+{
+    /// <summary>
+    /// 假保护器（Task 6）：Protect = Base64(UTF8) 直存；Unprotect 对非法 Base64
+    /// 抛 CryptographicException，与 DpapiProtector 的统一异常口径一致。
+    /// </summary>
+    public sealed class FakeProtector : ICredentialProtector
+    {
+        public string Protect(string plainText)
+        {
+            return Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText));
+        }
+
+        public string Unprotect(string base64)
+        {
+            try
+            {
+                return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
+            }
+            catch (FormatException ex)
+            {
+                throw new CryptographicException("假保护器：密文不是合法 Base64。", ex);
+            }
+        }
+    }
+
+    /// <summary>
+    /// 假剪贴板网关（Task 6）：内存模拟真实网关的同锁语义——写入即换新序列号
+    /// 与新令牌；TryClearIfMatches 用 ClipboardGuard.ShouldClear 做核对，
+    /// 匹配才清空（清空后序列号递增，与真实 EmptyClipboard 一致）。
+    /// 另提供 SimulateExternalCopy 模拟「用户从别处又复制了内容」。
+    /// </summary>
+    public sealed class FakeClipboardGateway : IClipboardGateway
+    {
+        private uint _sequence;
+        private string _token;
+        private string _text;
+
+        public string LastWrittenText { get; private set; }
+        public int WriteCount { get; private set; }
+        public int ClearAttempts { get; private set; }
+        public ClipboardReceipt LastClearReceipt { get; private set; }
+
+        /// <summary>置 true 时 WriteCredential 抛 InvalidOperationException（模拟剪贴板占用）。</summary>
+        public bool ThrowOnWrite { get; set; }
+
+        public string CurrentText
+        {
+            get { return _text; }
+        }
+
+        public ClipboardReceipt WriteCredential(string text)
+        {
+            if (ThrowOnWrite)
+                throw new InvalidOperationException("假网关：剪贴板被占用。");
+
+            _sequence++;
+            _token = Guid.NewGuid().ToString();
+            _text = text;
+            LastWrittenText = text;
+            WriteCount++;
+            return new ClipboardReceipt
+            {
+                Sequence = _sequence,
+                Token = _token,
+                Sha256 = ComputeSha256(text)
+            };
+        }
+
+        public bool TryClearIfMatches(ClipboardReceipt receipt)
+        {
+            ClearAttempts++;
+            LastClearReceipt = receipt;
+            if (!ClipboardGuard.ShouldClear(receipt, ReadState()))
+                return false;
+            _text = null;
+            _token = null;
+            _sequence++;
+            return true;
+        }
+
+        public ClipboardState ReadState()
+        {
+            return new ClipboardState { Sequence = _sequence, Token = _token };
+        }
+
+        /// <summary>模拟外部新复制：序列号与令牌都换新，文字可与之前相同。</summary>
+        public void SimulateExternalCopy(string text)
+        {
+            _sequence++;
+            _token = Guid.NewGuid().ToString();
+            _text = text;
+        }
+
+        public static string ComputeSha256(string text)
+        {
+            using (SHA256 sha = SHA256.Create())
+            {
+                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty));
+                var sb = new StringBuilder(hash.Length * 2);
+                for (int i = 0; i < hash.Length; i++)
+                    sb.Append(hash[i].ToString("x2"));
+                return sb.ToString();
+            }
+        }
+    }
+}
diff --git a/tests/Program.cs b/tests/Program.cs
index 5a93634..debecb2 100644
--- a/tests/Program.cs
+++ b/tests/Program.cs
@@ -14,10 +14,12 @@ namespace InspectionWidget.Tests
             PanelLayoutTests.RunAll();
             PanelStateMachineTests.RunAll();
             TargetClassifierTests.RunAll();
             CaptureGeometryTests.RunAll();
             BlackFrameDetectorTests.RunAll();
+            ClipboardGuardTests.RunAll();
+            CredentialServiceTests.RunAll();
             ScreenshotSaverTests.RunAll();
 
             Console.WriteLine("----------------------------------------");
             Console.WriteLine("用例总数：" + TestRunner.TotalCount
                 + "，通过：" + TestRunner.PassedCount
diff --git a/work/task-6-report.md b/work/task-6-report.md
new file mode 100644
index 0000000..81e2275
--- /dev/null
+++ b/work/task-6-report.md
@@ -0,0 +1,86 @@
+# Task 6 执行报告：凭据模块（DPAPI / 剪贴板令牌核对 / 30 秒清空）
+
+## 状态
+
+**完成（逻辑层全过关，真实 DPAPI 与真实剪贴板待 Windows 实机验收）。**
+
+基线 HEAD=330ae4b、基线测试 157/157（开工前亲跑确认）。本任务新增用例 15 条，最终 **172/172 全过、零回归**，`bash build.sh` 退出 0，产物 `build/InspectionWidget.exe` 62,464 字节。
+
+## 交付物
+
+| 文件 | 内容 |
+|---|---|
+| `src/Credentials/ICredentialProtector.cs` | 保护器抽象：Protect→Base64 密文；Unprotect 失败抛 CryptographicException |
+| `src/Credentials/DpapiProtector.cs` | DPAPI CurrentUser、无额外熵、UTF-8；Base64 格式错误先包成 CryptographicException 再抛，统一异常口径 |
+| `src/Credentials/IClipboardGateway.cs` | `ClipboardReceipt{Sequence, Token, Sha256}`、`ClipboardState{Sequence, Token}`、网关三方法（同锁语义写入接口注释） |
+| `src/Credentials/WindowsClipboardGateway.cs` | 真实网关：专用 STA 线程串行执行（网关级锁）、同锁读写、50ms×3 重试、写入最终失败抛 InvalidOperationException、清空最终失败返回 false |
+| `src/Credentials/ClipboardGuard.cs` | 纯决策 ShouldClear（序列号＋令牌同锁核对）＋ Copy 回执滚动 ＋ Threading.Timer 定时 ＋ TryClearNow（到期/退出共用）＋ ClearSkipped 事件 |
+| `src/Credentials/CredentialService.cs` | 编排：空密文→NeedsReset；CryptographicException→NeedsReset；其余异常→Failed；成功→Copied |
+| `src/Native/NativeMethods.cs` | 新增剪贴板 P/Invoke（OpenClipboard/CloseClipboard/EmptyClipboard/SetClipboardData/GetClipboardData/IsClipboardFormatAvailable/RegisterClipboardFormat/GetClipboardSequenceNumber）与 GlobalAlloc/Free/Lock/Unlock、CF_UNICODETEXT=13、GMEM_MOVEABLE |
+| `src/MainForm.cs` | 凭据按钮接真实链路；RebuildClipboardGuard()；FormClosing 兜底；空密文加载标记 |
+| `tests/Fakes.cs` | FakeProtector（Base64 直存、非法 Base64 抛 CryptographicException）、FakeClipboardGateway（同锁语义内存模拟＋SimulateExternalCopy＋ThrowOnWrite） |
+| `tests/ClipboardGuardTests.cs` | 决策 6 条＋编排 5 条 |
+| `tests/CredentialServiceTests.cs` | 服务 4 条 |
+| `tests/Program.cs` | 注册两个新测试类 |
+
+## 同锁语义落实（WindowsClipboardGateway，硬性口径）
+
+- WriteCredential：一次 OpenClipboard 内依次 EmptyClipboard → SetClipboardData(CF_UNICODETEXT) 写文本 → 注册并写入令牌格式 `InspectionWidget.CredentialToken`（GUID 字符串）→ 写入禁历史/云同步格式（`CanIncludeInClipboardHistory`、`CanUploadToCloudClipboard`，各一个 DWORD 0）→ CloseClipboard 前在同一临界区读 GetClipboardSequenceNumber 填回执。Sha256 在锁外对原文计算（不依赖剪贴板状态）。
+- TryClearIfMatches：一次 OpenClipboard 内读当前序列号与令牌 → 用 `ClipboardGuard.ShouldClear` 核对 → 都相等才 EmptyClipboard。网关内部直接复用纯决策函数，决策与执行口径只有一份。
+- 数据块内存：GlobalAlloc(GMEM_MOVEABLE) 标准写法，SetClipboardData 成功后所有权归系统不释放，失败路径自己 GlobalFree。
+- 三个操作各经专用 STA 线程同步执行（SetApartmentState(STA)＋Join，异常经 ExceptionDispatchInfo 原样透传），另有网关级锁串行化多调用方，调用方线程无关。
+
+## TDD 证据（先 FAIL 后 PASS）
+
+流程：先写 Fakes＋全部 15 条新用例并注册，再写签名齐全、行为为空/恒错的桩实现（ShouldClear 恒 false、Copy 空转、Service 恒 Failed、DPAPI/网关抛 NotImplementedException），跑构建得 RED；随后逐个换真实实现得 GREEN。
+
+- **RED**（桩实现）：总 172 条，165 过、**7 条 FAIL**，构建退出 1。失败的正是正向行为断言：
+  1. 决策：序列号与令牌都同 → ShouldClear=true（期望 True 实际 False）
+  2. 编排：Copy 两次后 TryClearNow 只认第二次回执
+  3. 编排：状态匹配时 TryClearNow 清空并返回 true
+  4. 编排：同文字新复制后 TryClearNow 不误清并触发 ClearSkipped（桩无回执，ClearSkipped 未触发而 FAIL；该用例的内容保留断言在桩下形式通过属预期——桩根本不清）
+  5. 服务：正常复制 → Copied（实际 Failed）
+  6. 服务：密文损坏 → NeedsReset（实际 Failed）
+  7. 服务：EncryptedSecret 为空 → NeedsReset（实际 Failed）
+  负向断言（仅序列号变/仅令牌变/同文字新复制的 ShouldClear=false、null 分支、网关 false 不抛、Failed 路径）在桩下形式通过，符合桩的恒假行为，不构成证据弱点：GREEN 阶段同一批用例在真实实现下全过，其中正向用例已证明实现确实会清、会复制。
+- **GREEN**（真实实现＋MainForm 接线后）：`bash build.sh` 退出 0，**172/172 全过**（157 既有＋15 新增），既有用例零回归。
+
+## 用例清单（新增 15 条）
+
+ClipboardGuardTests（11 条）：
+- 决策：都同→true；仅序列号变→false；仅令牌变→false；**同文字新复制（序列号与令牌都变、Sha256 相同）→false（Review Focus 第 3 条锚定，纯函数层）**；receipt null→false；current null→false。
+- 编排：Copy 两次后 TryClearNow 只认第二次回执（断言网关收到的核对回执令牌=第二次令牌）；状态匹配→清空返回 true 且回执作废（再调不触网关）；**同文字新复制后 TryClearNow 不误清、外部内容保留、ClearSkipped 触发（Review Focus 第 3 条，编排层二次锚定）**；网关返回 false 时不抛异常；从未 Copy 时返回 false 且不调网关。
+
+CredentialServiceTests（4 条）：正常复制→Copied 且网关写入文本=原文；密文损坏→NeedsReset 且网关写入次数 0；EncryptedSecret 为空串/null→NeedsReset 且不写网关；网关写入抛 InvalidOperationException→Failed 不外抛。
+
+测试构造说明：Guard 用例以 3600 秒间隔构造定时器避免真实到期干扰断言；定时器真实到期路径未做自动化用例（避免时序 flaky），与退出兜底共用同一 TryClearNow 已被编排用例覆盖，到期触发本身列入实机验收。
+
+## MainForm 接线（简报 E）
+
+- 凭据按钮点击 → `CredentialService.Copy(entry)`：Copied→成功色反馈「已复制」；NeedsReset→置 `entry.NeedsReset=true`（内存标记不落盘）＋重建凭据按钮（显示「（需重设）」错误色）＋错误色反馈「该凭据需重新设置，请到设置中重新填写」；Failed→错误色反馈「复制失败，请重试」。原占位 MessageBox 已移除。
+- `RebuildClipboardGuard()`：按 `_config.AutoClearClipboard` 构造 Guard 并重组 CredentialService，旧 Guard 先 Dispose；构造函数初始化时调用一次，设置变更后的重建留待 Task 7 保存事件接入本方法（按简报口径）。
+- FormClosing：仍只有一处订阅、一个处理函数（已 grep 核对 `FormClosing += OnFormClosing` 仅 1 处）。处理函数先 `_clipboardGuard.TryClearNow()`（有回执且匹配才清）并 Dispose Guard，再走原有 `_hooks.Stop()/Dispose()` 与位置保存。
+- 加载标记：`EnsureConfigLists()`（构造与 ReloadConfig 共用入口）对 EncryptedSecret 为空的条目置 NeedsReset=true，与设置层口径一致。
+- Dispose(bool) 补 Guard 释放（与 FormClosing 双保险，Guard.Dispose 幂等）。
+
+## 语言子集（R6）
+
+全部新增/修改代码仅用 C# 7.2 以内语法（无 switch 表达式、无 ??=、无 7.3 独有构造），xbuild 以 LangVersion=7.2 构建通过即为实证。
+
+## 构建警告说明
+
+CS2002（源文件被指定多次）为 xbuild 通配符存量怪癖：基线 330ae4b 干净 worktree 构建已有 30 条，本次 54 条随文件总数等比增加，非本次引入的新类警告。
+
+## 遗留（Windows 实机验收项）
+
+1. **DPAPI 跨机/跨用户**：密文拷到另一台机器或另一 Windows 用户下 Unprotect 应抛 CryptographicException → UI 走 NeedsReset 提示，需实机验证；同机同用户加解密往返亦待实机。
+2. **真实剪贴板时序**：STA 线程写/读/清的实际行为、GetClipboardSequenceNumber 与回执的对应关系、令牌格式与禁历史/云同步格式在真实剪贴板（含 Windows 剪贴板历史 Win+V、云剪贴板同步）中的效果，待实机逐项验。
+3. **30 秒定时到期**：真实 Timer 到期自动清空、以及到期前从别处复制同文字内容不误清的端到端时序，待实机验。
+4. **剪贴板占用重试**：被其他进程占用时 50ms×3 重试后写入抛「复制失败」/清空返回 false 的真实表现，待实机注入验证。
+5. **退出兜底**：复制后立即关窗，剪贴板被清（匹配时）/保留（已被外部新复制替换时）的实机确认。
+
+## 提交
+
+- 提交信息：`feat(stage-6): 凭据模块（DPAPI/剪贴板令牌核对/30秒清空）`
+- 提交哈希：见本报告随附的 git log 核对（提交后由协调者以 `git log --oneline` 为准；本文件随同一提交入库，故不在正文内自填哈希）。
+- 未加 remote、未 push；未动 PROGRESS.md 与 docs/。
