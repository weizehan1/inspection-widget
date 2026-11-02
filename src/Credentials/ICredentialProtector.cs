namespace InspectionWidget.Credentials
{
    /// <summary>凭据加解密抽象：明文只在内存中短暂存在，落盘的只有 Base64 密文。</summary>
    public interface ICredentialProtector
    {
        /// <summary>加密明文，返回 Base64 密文。</summary>
        string Protect(string plainText);

        /// <summary>解密 Base64 密文；失败抛 CryptographicException（统一异常口径）。</summary>
        string Unprotect(string base64);
    }
}
