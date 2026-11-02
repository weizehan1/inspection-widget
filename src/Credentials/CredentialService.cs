using System;
using System.Security.Cryptography;
using InspectionWidget.Config;

namespace InspectionWidget.Credentials
{
    /// <summary>凭据复制结果：成功 / 密文解不开需重设 / 其他失败。</summary>
    public enum CopyResult
    {
        Copied,
        NeedsReset,
        Failed
    }

    /// <summary>
    /// 凭据复制编排：Unprotect → Guard.Copy。异常口径：密文为空与
    /// CryptographicException → NeedsReset（不触碰剪贴板）；其余异常 → Failed。
    /// </summary>
    public sealed class CredentialService
    {
        private readonly ICredentialProtector _protector;
        private readonly ClipboardGuard _guard;

        public CredentialService(ICredentialProtector protector, ClipboardGuard guard)
        {
            if (protector == null) throw new ArgumentNullException("protector");
            if (guard == null) throw new ArgumentNullException("guard");
            _protector = protector;
            _guard = guard;
        }

        public CopyResult Copy(CredentialEntry entry)
        {
            if (entry == null)
                return CopyResult.Failed;
            if (string.IsNullOrEmpty(entry.EncryptedSecret))
                return CopyResult.NeedsReset;

            string plain;
            try
            {
                plain = _protector.Unprotect(entry.EncryptedSecret);
            }
            catch (CryptographicException)
            {
                return CopyResult.NeedsReset;
            }
            catch (Exception)
            {
                return CopyResult.Failed;
            }

            try
            {
                _guard.Copy(plain);
            }
            catch (Exception)
            {
                return CopyResult.Failed;
            }
            return CopyResult.Copied;
        }
    }
}
