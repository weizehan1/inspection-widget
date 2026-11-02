using System;
using System.Security.Cryptography;
using System.Text;
using InspectionWidget.Credentials;

namespace InspectionWidget.Tests
{
    /// <summary>
    /// 假保护器（Task 6）：Protect = Base64(UTF8) 直存；Unprotect 对非法 Base64
    /// 抛 CryptographicException，与 DpapiProtector 的统一异常口径一致。
    /// </summary>
    public sealed class FakeProtector : ICredentialProtector
    {
        public string Protect(string plainText)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText));
        }

        public string Unprotect(string base64)
        {
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            }
            catch (FormatException ex)
            {
                throw new CryptographicException("假保护器：密文不是合法 Base64。", ex);
            }
        }
    }

    /// <summary>
    /// 假剪贴板网关（Task 6）：内存模拟真实网关的同锁语义——写入即换新序列号
    /// 与新令牌；TryClearIfMatches 用 ClipboardGuard.ShouldClear 做核对，
    /// 匹配才清空（清空后序列号递增，与真实 EmptyClipboard 一致）。
    /// 另提供 SimulateExternalCopy 模拟「用户从别处又复制了内容」。
    /// </summary>
    public sealed class FakeClipboardGateway : IClipboardGateway
    {
        private uint _sequence;
        private string _token;
        private string _text;

        public string LastWrittenText { get; private set; }
        public int WriteCount { get; private set; }
        public int ClearAttempts { get; private set; }
        public ClipboardReceipt LastClearReceipt { get; private set; }

        /// <summary>置 true 时 WriteCredential 抛 InvalidOperationException（模拟剪贴板占用）。</summary>
        public bool ThrowOnWrite { get; set; }

        /// <summary>置 true 时 TryClearIfMatches 抛 InvalidOperationException（模拟清空路径网关异常，修复轮 Minor-3）。</summary>
        public bool ThrowOnClear { get; set; }

        public string CurrentText
        {
            get { return _text; }
        }

        public ClipboardReceipt WriteCredential(string text)
        {
            if (ThrowOnWrite)
                throw new InvalidOperationException("假网关：剪贴板被占用。");

            _sequence++;
            _token = Guid.NewGuid().ToString();
            _text = text;
            LastWrittenText = text;
            WriteCount++;
            return new ClipboardReceipt
            {
                Sequence = _sequence,
                Token = _token,
                Sha256 = ComputeSha256(text)
            };
        }

        public bool TryClearIfMatches(ClipboardReceipt receipt)
        {
            if (ThrowOnClear)
                throw new InvalidOperationException("假网关：清空时剪贴板异常。");
            ClearAttempts++;
            LastClearReceipt = receipt;
            if (!ClipboardGuard.ShouldClear(receipt, ReadState()))
                return false;
            _text = null;
            _token = null;
            _sequence++;
            return true;
        }

        public ClipboardState ReadState()
        {
            return new ClipboardState { Sequence = _sequence, Token = _token };
        }

        /// <summary>模拟外部新复制：序列号与令牌都换新，文字可与之前相同。</summary>
        public void SimulateExternalCopy(string text)
        {
            _sequence++;
            _token = Guid.NewGuid().ToString();
            _text = text;
        }

        public static string ComputeSha256(string text)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty));
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
