using InspectionWidget.Config;
using InspectionWidget.Credentials;

namespace InspectionWidget.Tests
{
    /// <summary>
    /// CredentialService 编排（FakeProtector + FakeClipboardGateway + 真 ClipboardGuard）：
    /// 正常复制、密文损坏、空密文、网关失败四条路径。
    /// </summary>
    public static class CredentialServiceTests
    {
        private static CredentialEntry Entry(string encryptedSecret)
        {
            return new CredentialEntry { Title = "路由器", EncryptedSecret = encryptedSecret, NeedsReset = false };
        }

        public static void RunAll()
        {
            TestRunner.Run("服务：正常复制 → Copied 且网关写入文本=原文", () =>
            {
                var protector = new FakeProtector();
                var gateway = new FakeClipboardGateway();
                using (var guard = new ClipboardGuard(gateway, true, 3600))
                {
                    var service = new CredentialService(protector, guard);
                    CopyResult result = service.Copy(Entry(protector.Protect("s3cret-pw")));

                    Assert.Equal(CopyResult.Copied, result);
                    Assert.Equal("s3cret-pw", gateway.LastWrittenText, "网关应收到解密后的原文");
                    Assert.Equal(1, gateway.WriteCount);
                }
            });

            TestRunner.Run("服务：密文损坏 → NeedsReset 且网关未被写入", () =>
            {
                var protector = new FakeProtector();
                var gateway = new FakeClipboardGateway();
                using (var guard = new ClipboardGuard(gateway, true, 3600))
                {
                    var service = new CredentialService(protector, guard);
                    CopyResult result = service.Copy(Entry("!!!not-base64!!!"));

                    Assert.Equal(CopyResult.NeedsReset, result);
                    Assert.Equal(0, gateway.WriteCount, "解密失败不得触碰剪贴板");
                    Assert.Null(gateway.LastWrittenText);
                }
            });

            TestRunner.Run("服务：EncryptedSecret 为空 → NeedsReset 且网关未被写入", () =>
            {
                var protector = new FakeProtector();
                var gateway = new FakeClipboardGateway();
                using (var guard = new ClipboardGuard(gateway, true, 3600))
                {
                    var service = new CredentialService(protector, guard);

                    Assert.Equal(CopyResult.NeedsReset, service.Copy(Entry(string.Empty)));
                    Assert.Equal(CopyResult.NeedsReset, service.Copy(Entry(null)));
                    Assert.Equal(0, gateway.WriteCount);
                }
            });

            TestRunner.Run("服务：网关写入失败 → Failed（不抛异常）", () =>
            {
                var protector = new FakeProtector();
                var gateway = new FakeClipboardGateway();
                gateway.ThrowOnWrite = true;
                using (var guard = new ClipboardGuard(gateway, true, 3600))
                {
                    var service = new CredentialService(protector, guard);
                    CopyResult result = service.Copy(Entry(protector.Protect("pw")));

                    Assert.Equal(CopyResult.Failed, result);
                }
            });
        }
    }
}
