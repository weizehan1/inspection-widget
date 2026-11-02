using System;

namespace InspectionWidget.Credentials
{
    /// <summary>一次凭据写入的回执：序列号＋令牌是同锁核对凭证，Sha256 仅记录内容哈希（不作核对依据）。</summary>
    public sealed class ClipboardReceipt
    {
        public uint Sequence;
        public string Token;
        public string Sha256;
    }

    /// <summary>剪贴板当前状态快照（核对用）。</summary>
    public sealed class ClipboardState
    {
        public uint Sequence;
        public string Token;
    }

    /// <summary>
    /// 剪贴板抽象。实现须保证：WriteCredential 的写入与回执读取在同一次
    /// OpenClipboard 临界区内完成；TryClearIfMatches 的序列号＋令牌核对与
    /// 清空在同一次 OpenClipboard 临界区内完成（同锁核对，防竞态误清）。
    /// </summary>
    public interface IClipboardGateway
    {
        /// <summary>写入凭据文本与本应用令牌，返回回执。失败（占用重试后仍失败）抛 InvalidOperationException。</summary>
        ClipboardReceipt WriteCredential(string text);

        /// <summary>序列号与令牌都与回执一致才清空，返回是否真的清了。</summary>
        bool TryClearIfMatches(ClipboardReceipt receipt);

        /// <summary>
        /// 读取当前序列号与令牌（令牌读不到时为 null）。
        /// 注意（修复轮 Minor-1）：这是非原子快照——序列号与令牌并非在同一次
        /// OpenClipboard 临界区内读取（WindowsClipboardGateway 实现中序列号在
        /// Open 之外读、令牌在另一次 Open 内读），两者可能来自不同代剪贴板
        /// 内容。生产决策不得使用本方法做清空判定；清空判定只走
        /// TryClearIfMatches 的同锁核对。本方法仅供测试与状态展示。
        /// </summary>
        ClipboardState ReadState();
    }
}
