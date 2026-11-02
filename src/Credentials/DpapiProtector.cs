using System;
using System.Security.Cryptography;
using System.Text;

namespace InspectionWidget.Credentials
{
    /// <summary>
    /// DPAPI 实现（CurrentUser、无额外熵、UTF-8）：密文与当前 Windows 用户绑定，
    /// 换机/换用户解不开——Unprotect 失败统一抛 CryptographicException
    /// （含 Base64 格式错误，先包一层再抛），上层据此提示需重设。
    /// </summary>
    public sealed class DpapiProtector : ICredentialProtector
    {
        public string Protect(string plainText)
        {
            if (plainText == null) throw new ArgumentNullException("plainText");
            byte[] data = Encoding.UTF8.GetBytes(plainText);
            byte[] encrypted = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }

        public string Unprotect(string base64)
        {
            if (string.IsNullOrEmpty(base64))
                throw new CryptographicException("凭据密文为空。");

            byte[] encrypted;
            try
            {
                encrypted = Convert.FromBase64String(base64);
            }
            catch (FormatException ex)
            {
                throw new CryptographicException("凭据密文格式无效。", ex);
            }

            byte[] data = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(data);
        }
    }
}
