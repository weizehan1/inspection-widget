using System;
using System.Text;
using InspectionWidget.Config;

namespace InspectionWidget.Tests
{
    /// <summary>
    /// Task 7 纯逻辑用例：凭据行保存收集（CredentialRowCollector）与配置深拷贝
    /// （AppConfigCloner）。设置窗本体为 WinForms 交互，无自动化用例（实机验收）。
    /// </summary>
    public static class SettingsFormTests
    {
        private static CredentialRowCollector.RowInput Row(
            string title, string originalSecret, bool isNew, bool cleared, string newPlainText)
        {
            return new CredentialRowCollector.RowInput
            {
                Title = title,
                OriginalSecret = originalSecret,
                IsNew = isNew,
                Cleared = cleared,
                NewPlainText = newPlainText
            };
        }

        public static void RunAll()
        {
            var protector = new FakeProtector();

            TestRunner.Run("设置收集：已存条目留空保留原密文", () =>
            {
                string error;
                string secret = CredentialRowCollector.CollectSecret(
                    Row("门禁", "原密文XYZ", false, false, string.Empty), protector, out error);
                Assert.Null(error);
                Assert.Equal("原密文XYZ", secret);
            });

            TestRunner.Run("设置收集：已存条目输入新内容经 Protector 覆盖", () =>
            {
                string error;
                string secret = CredentialRowCollector.CollectSecret(
                    Row("门禁", "原密文XYZ", false, false, "新内容abc"), protector, out error);
                Assert.Null(error);
                Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("新内容abc")), secret);
            });

            TestRunner.Run("设置收集：清除标记优先于新输入，密文置空", () =>
            {
                string error;
                string secret = CredentialRowCollector.CollectSecret(
                    Row("门禁", "原密文XYZ", false, true, "新内容abc"), protector, out error);
                Assert.Null(error);
                Assert.Equal(string.Empty, secret);
            });

            TestRunner.Run("设置收集：新增条目带清除标记且空内容校验失败", () =>
            {
                string error;
                string secret = CredentialRowCollector.CollectSecret(
                    Row("新凭据", null, true, true, string.Empty), protector, out error);
                Assert.NotNull(error);
                Assert.Null(secret);
            });

            TestRunner.Run("设置收集：既有条目清除校验通过且密文为空", () =>
            {
                string error;
                string secret = CredentialRowCollector.CollectSecret(
                    Row("门禁", "原密文XYZ", false, true, string.Empty), protector, out error);
                Assert.Null(error);
                Assert.Equal(string.Empty, secret);
            });

            TestRunner.Run("设置收集：新条目有内容正常加密", () =>
            {
                string error;
                string secret = CredentialRowCollector.CollectSecret(
                    Row("新凭据", null, true, false, "内容1"), protector, out error);
                Assert.Null(error);
                Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("内容1")), secret);
            });

            TestRunner.Run("设置收集：新条目无内容报错", () =>
            {
                string error;
                string secret = CredentialRowCollector.CollectSecret(
                    Row("新凭据", null, true, false, string.Empty), protector, out error);
                Assert.NotNull(error);
                Assert.Null(secret);
            });

            TestRunner.Run("设置收集：名称空白报错（优先于内容收集）", () =>
            {
                string error;
                string secret = CredentialRowCollector.CollectSecret(
                    Row("   ", null, true, false, "内容1"), protector, out error);
                Assert.NotNull(error);
                Assert.Null(secret);
            });

            TestRunner.Run("设置收集：已存条目原密文为 null 时留空得空密文", () =>
            {
                string error;
                string secret = CredentialRowCollector.CollectSecret(
                    Row("残缺条目", null, false, false, string.Empty), protector, out error);
                Assert.Null(error);
                Assert.Equal(string.Empty, secret);
            });

            TestRunner.Run("深拷贝：改副本设备与凭据不影响原配置", () =>
            {
                var source = AppConfig.CreateDefault();
                source.Credentials.Add(new CredentialEntry { Title = "门禁", EncryptedSecret = "密文" });

                AppConfig copy = AppConfigCloner.Clone(source);
                copy.Devices.Add("新设备");
                copy.Devices[0] = "改名";
                copy.Credentials[0].Title = "改标题";
                copy.Credentials[0].EncryptedSecret = "改密文";
                copy.RootPath = "D:\\改后";

                Assert.Equal(2, source.Devices.Count);
                Assert.Equal("设备1", source.Devices[0]);
                Assert.Equal("门禁", source.Credentials[0].Title);
                Assert.Equal("密文", source.Credentials[0].EncryptedSecret);
                Assert.Null(source.RootPath);
            });

            TestRunner.Run("深拷贝：null 输入得默认配置", () =>
            {
                AppConfig copy = AppConfigCloner.Clone(null);
                Assert.NotNull(copy);
                Assert.NotNull(copy.Devices);
                Assert.NotNull(copy.Credentials);
            });

            TestRunner.Run("深拷贝：UiScalePercent 保留（设置保存不丢缩放档位）", () =>
            {
                var source = AppConfig.CreateDefault();
                source.UiScalePercent = 175;
                AppConfig copy = AppConfigCloner.Clone(source);
                Assert.Equal(175, copy.UiScalePercent);
                source.UiScalePercent = 125;
                Assert.Equal(175, copy.UiScalePercent, "深拷贝须独立于源对象后续修改");
            });

            TestRunner.Run("深拷贝：沿用不编辑字段（折叠态/窗口位置/LastDevice）", () =>
            {
                var source = AppConfig.CreateDefault();
                source.IsCredentialCollapsed = true;
                source.WindowLeft = 123;
                source.WindowTop = 45;
                source.LastDevice = "设备2";

                AppConfig copy = AppConfigCloner.Clone(source);
                Assert.True(copy.IsCredentialCollapsed);
                Assert.Equal(123, copy.WindowLeft.Value);
                Assert.Equal(45, copy.WindowTop.Value);
                Assert.Equal("设备2", copy.LastDevice);
            });
        }
    }
}
