using System;
using System.Threading;

namespace InspectionWidget.Credentials
{
    /// <summary>
    /// 剪贴板自动清空守卫：Copy 时经网关写入并保存回执、（启用自动清空时）
    /// 重置清空定时器；到期或退出兜底统一走 TryClearNow——清空与否只认
    /// 「序列号＋令牌」同锁核对（ShouldClear），同文字新复制一律不清。
    /// </summary>
    public sealed class ClipboardGuard : IDisposable
    {
        private readonly IClipboardGateway _gateway;
        private readonly bool _autoClearEnabled;
        private readonly int _clearAfterSeconds;
        private readonly object _sync = new object();

        private ClipboardReceipt _receipt;
        private Timer _timer;
        private bool _disposed;

        /// <summary>核对不匹配/网关未能清空而跳过清空时触发（供 UI 提示，可空挂）。</summary>
        public event Action ClearSkipped;

        public ClipboardGuard(IClipboardGateway gateway, bool autoClearEnabled, int clearAfterSeconds = 30)
        {
            if (gateway == null) throw new ArgumentNullException("gateway");
            if (clearAfterSeconds <= 0) throw new ArgumentOutOfRangeException("clearAfterSeconds");
            _gateway = gateway;
            _autoClearEnabled = autoClearEnabled;
            _clearAfterSeconds = clearAfterSeconds;
        }

        /// <summary>
        /// 纯决策：receipt 与 current 都非 null，且序列号相等、令牌 Ordinal 相等才清。
        /// 文字内容（哈希）不参与判定——同文字新复制的序列号与令牌都已变，天然被拒。
        /// </summary>
        public static bool ShouldClear(ClipboardReceipt receipt, ClipboardState current)
        {
            if (receipt == null || current == null)
                return false;
            return receipt.Sequence == current.Sequence
                && string.Equals(receipt.Token, current.Token, StringComparison.Ordinal);
        }

        /// <summary>
        /// 写入凭据并滚动回执：网关写入成功后才替换旧回执并重置定时器；
        /// 写入失败异常上抛（旧回执与旧定时器保持，上一条凭据仍受保护）。
        /// 释放守卫（修复轮 Minor-2）：调网关写剪贴板之前先校验 _disposed，
        /// 已 Dispose 直接抛 ObjectDisposedException（口径同 HookManager.Start），
        /// 绝不让已释放的 Guard 把明文写入剪贴板却不存回执、不设定时器——
        /// 那样的明文无人负责清空。经 CredentialService 调用时该异常被其
        /// catch 转为 Failed，不会穿透 UI。
        /// </summary>
        public void Copy(string text)
        {
            lock (_sync)
            {
                if (_disposed)
                    throw new ObjectDisposedException("ClipboardGuard");
            }
            ClipboardReceipt receipt = _gateway.WriteCredential(text);
            lock (_sync)
            {
                if (_disposed)
                    return;
                _receipt = receipt;
                RestartTimerLocked();
            }
        }

        /// <summary>
        /// 立即按当前回执核对清空（定时到期与退出兜底共用）。无回执→false 且不调网关；
        /// 网关异常吞掉返回 false（兜底路径不得因剪贴板异常阻断退出）；
        /// 未清掉时触发 ClearSkipped，清掉后回执作废。
        /// </summary>
        public bool TryClearNow()
        {
            ClipboardReceipt receipt;
            lock (_sync)
            {
                receipt = _receipt;
                StopTimerLocked();
            }
            if (receipt == null)
                return false;

            bool cleared;
            try
            {
                cleared = _gateway.TryClearIfMatches(receipt);
            }
            catch (Exception)
            {
                cleared = false;
            }

            if (cleared)
            {
                lock (_sync)
                {
                    if (object.ReferenceEquals(_receipt, receipt))
                        _receipt = null;
                }
            }
            else
            {
                Action handler = ClearSkipped;
                if (handler != null)
                    handler();
            }
            return cleared;
        }

        private void RestartTimerLocked()
        {
            StopTimerLocked();
            if (!_autoClearEnabled || _disposed)
                return;
            _timer = new Timer(OnTimerElapsed, null, _clearAfterSeconds * 1000, Timeout.Infinite);
        }

        private void StopTimerLocked()
        {
            if (_timer != null)
            {
                _timer.Dispose();
                _timer = null;
            }
        }

        private void OnTimerElapsed(object state)
        {
            TryClearNow();
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _disposed = true;
                StopTimerLocked();
            }
        }
    }
}
