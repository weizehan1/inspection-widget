using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using InspectionWidget.Config;

namespace InspectionWidget.Tests
{
    /// <summary>外部配置交换、兼容解析与导入计划的纯逻辑回归。</summary>
    public static class ConfigTransferTests
    {
        public static void RunAll()
        {
            TestRunner.Run("交换：普通导出只含显式普通字段且可往返", () =>
            {
                AppConfig source = SampleConfig();
                string json = ConfigTransfer.BuildExportJson(
                    source, ConfigJsonReader.CredentialModeExcluded);
                Assert.False(json.Contains("\"Credentials\""), "普通导出不得含凭据字段");
                Assert.False(json.Contains("\"LastDevice\""), "普通导出不得含 LastDevice");
                Assert.False(json.Contains("\"WindowLeft\""), "普通导出不得含窗口位置");
                Assert.False(json.Contains("\"TrayHintShown\""), "普通导出不得含托盘提示状态");

                ConfigImportDocument document;
                string error;
                Assert.True(ConfigJsonReader.TryParse(json, out document, out error), error);
                Assert.Equal(ConfigSourceKind.Exchange, document.Kind);
                Assert.Equal(ConfigJsonReader.CredentialModeExcluded, document.CredentialMode);
                Assert.Equal(2, document.Config.Devices.Count);
                Assert.Equal("甲", document.Config.Devices[0]);
                Assert.Equal("乙", document.Config.Devices[1]);
                Assert.Equal("D:\\shots", document.Config.RootPath);
                Assert.False(document.Config.AutoClearClipboard);
                Assert.True(document.Config.IsDeviceCollapsed);
                Assert.Equal(175, document.Config.UiScalePercent);
                Assert.Equal(0, document.Config.Credentials.Count);
            });

            TestRunner.Run("交换：完整备份携带全部持久字段与原密文", () =>
            {
                AppConfig source = SampleConfig();
                source.Credentials.Add(new CredentialEntry
                {
                    Title = "堡垒机",
                    EncryptedSecret = "QUJDRA==",
                    NeedsReset = true
                });
                source.LastDevice = "乙";
                source.WindowLeft = 123;
                source.WindowTop = 45;
                source.TrayHintShown = true;

                string json = ConfigTransfer.BuildExportJson(
                    source, ConfigJsonReader.CredentialModeDpapi);
                Assert.False(json.Contains("NeedsReset"), "运行时状态不得进入备份");
                ConfigImportDocument document;
                string error;
                Assert.True(ConfigJsonReader.TryParse(json, out document, out error), error);
                Assert.True(document.IsFullBackup);
                Assert.True(document.CredentialsPresent);
                Assert.Equal("QUJDRA==", document.Config.Credentials[0].EncryptedSecret);
                Assert.Equal("乙", document.Config.LastDevice);
                Assert.Equal((int?)123, document.Config.WindowLeft);
                Assert.Equal((int?)45, document.Config.WindowTop);
                Assert.True(document.Config.TrayHintShown);
            });

            TestRunner.Run("交换：未知顶层字段、格式、版本与凭据模式均拒绝", () =>
            {
                AssertParseFails(Exchange("Excluded", "{\"Devices\":[]}", null, 1,
                    "\"Extra\":1"), "未知顶层字段");
                AssertParseFails(Exchange("Excluded", "{\"Devices\":[]}",
                    "Other.Format"), "未知配置格式");
                ConfigImportDocument ignored;
                string error;
                Assert.False(ConfigJsonReader.TryParse(
                    "{\"Format\":\"InspectionWidget.Settings\",\"Version\":2,"
                    + "\"CredentialMode\":\"Excluded\",\"Config\":{\"Devices\":[]}}",
                    out ignored, out error));
                Assert.True(error.Contains("版本"), error);
                AssertParseFails(Exchange("Bogus", "{\"Devices\":[]}"), "未知凭据模式");
            });

            TestRunner.Run("交换：重复键、尾随 JSON、非对象与随机文本拒绝", () =>
            {
                AssertParseFails(
                    "{\"Devices\":[],\"Devices\":[]}", "重复键");
                AssertParseFails(
                    "{\"Devices\":[]} {\"Devices\":[]}", "末尾");
                AssertParseFails("[]", "顶层");
                AssertParseFails("{\"Devices\":[}", "JSON");
                AssertParseFails("not json at all", "JSON");
            });

            TestRunner.Run("交换：Config 内未知字段拒绝，嵌套同名属性不算顶层字段", () =>
            {
                AssertParseFails(Exchange("Excluded",
                    "{\"Devices\":[],\"Unknown\":true}"), "未知字段");

                ConfigImportDocument document;
                string error;
                Assert.True(ConfigJsonReader.TryParse(Exchange("DpapiCurrentUser",
                    "{\"Devices\":[\"甲\"],\"Credentials\":[{\"Title\":\"Devices\","
                    + "\"EncryptedSecret\":\"QUJD\"}]}"), out document, out error), error);
                Assert.True(document.Has("Devices"));
                Assert.False(document.Has("RootPath"));
                Assert.True(document.CredentialsPresent);
                Assert.Equal("Devices", document.Config.Credentials[0].Title);
            });

            TestRunner.Run("交换：错误类型与非法枚举分别报中文错误", () =>
            {
                AssertParseFails(Exchange("Excluded",
                    "{\"Devices\":\"甲\"}"), "Devices");
                AssertParseFails(Exchange("Excluded",
                    "{\"Devices\":[],\"AutoClearClipboard\":\"false\"}"), "布尔");
                AssertParseFails(Exchange("Excluded",
                    "{\"Devices\":[],\"CaptureMode\":9}"), "截图模式");
                AssertParseFails(Exchange("Excluded",
                    "{\"Devices\":[],\"UiScalePercent\":137}"), "缩放");
                AssertParseFails(Exchange("Excluded",
                    "{\"Devices\":[],\"RootPath\":123}"), "RootPath");
                AssertParseFails(Exchange("Excluded",
                    "{\"Devices\":[],\"FolderNamePattern\":\"bad?name\"}"), "日期文件夹名");
            });

            TestRunner.Run("交换：设备大小写重复与非法设备名拒绝", () =>
            {
                AssertParseFails(Exchange("Excluded",
                    "{\"Devices\":[\"HostA\",\"hosta\"]}"), "同名");
                AssertParseFails(Exchange("Excluded",
                    "{\"Devices\":[\"CON\"]}"), "保留名");
                AssertParseFails(Exchange("Excluded",
                    "{\"Devices\":[\"名字 \"]}"), "空格");
            });

            TestRunner.Run("交换：Excluded 禁止凭据，Dpapi 必须有凭据数组", () =>
            {
                AssertParseFails(Exchange("Excluded",
                    "{\"Devices\":[],\"Credentials\":[]}"), "不得包含凭据");
                AssertParseFails(Exchange("DpapiCurrentUser",
                    "{\"Devices\":[]}"), "必须包含凭据");
                AssertParseFails(Exchange("DpapiCurrentUser",
                    "{\"Devices\":[],\"Credentials\":null}"), "不能是 null");
                AssertParseFails(Exchange("DpapiCurrentUser",
                    "{\"Devices\":[],\"Credentials\":{}}"), "必须是数组");
            });

            TestRunner.Run("凭据：null 元素、空名、重复名、超限与缺密文拒绝", () =>
            {
                AssertParseFails(Exchange("DpapiCurrentUser",
                    "{\"Devices\":[],\"Credentials\":[null]}"), "必须是对象");
                AssertParseFails(Exchange("DpapiCurrentUser",
                    "{\"Devices\":[],\"Credentials\":[{\"Title\":\"\","
                    + "\"EncryptedSecret\":\"\"}]}"), "名称为空");
                AssertParseFails(Exchange("DpapiCurrentUser",
                    "{\"Devices\":[],\"Credentials\":["
                    + "{\"Title\":\"A\",\"EncryptedSecret\":\"\"},"
                    + "{\"Title\":\"a\",\"EncryptedSecret\":\"\"}]}"), "重复");
                AssertParseFails(Exchange("DpapiCurrentUser",
                    "{\"Devices\":[],\"Credentials\":[{\"Title\":\"A\"}]}"), "缺少");

                var credentials = new StringBuilder("[");
                for (int i = 0; i < 11; i++)
                {
                    if (i > 0) credentials.Append(',');
                    credentials.Append("{\"Title\":\"C").Append(i)
                        .Append("\",\"EncryptedSecret\":\"\"}");
                }
                credentials.Append(']');
                AssertParseFails(Exchange("DpapiCurrentUser",
                    "{\"Devices\":[],\"Credentials\":" + credentials + "}"), "最多");
            });

            TestRunner.Run("旧格式凭据：缺密文/null 兼容为空密文，新格式仍拒绝", () =>
            {
                ConfigImportDocument document;
                string error;
                Assert.True(ConfigJsonReader.TryParse(
                    "{\"Devices\":[\"A\"],\"Credentials\":[{\"Title\":\"old\"}]}",
                    out document, out error), error);
                Assert.Equal(1, document.Config.Credentials.Count);
                Assert.Equal("old", document.Config.Credentials[0].Title);
                Assert.Null(document.Config.Credentials[0].EncryptedSecret);
                Assert.True(document.CredentialsPresent);

                Assert.True(ConfigJsonReader.TryParse(
                    "{\"Devices\":[\"A\"],\"Credentials\":[{\"Title\":\"nullSecret\","
                    + "\"EncryptedSecret\":null}]}", out document, out error), error);
                Assert.Null(document.Config.Credentials[0].EncryptedSecret);

                Assert.True(ConfigJsonReader.TryParse(
                    "{\"Devices\":[\"A\"],\"Credentials\":[{\"Title\":\"emptySecret\","
                    + "\"EncryptedSecret\":\"\"}]}", out document, out error), error);
                Assert.Equal(string.Empty, document.Config.Credentials[0].EncryptedSecret);

                Assert.False(ConfigJsonReader.TryParse(Exchange("DpapiCurrentUser",
                    "{\"Devices\":[\"A\"],\"Credentials\":[{\"Title\":\"old\"}]}"),
                    out document, out error));
                Assert.True(error.Contains("缺少合法密文"), error);

                Assert.True(ConfigJsonReader.TryParse(
                    "{\"Devices\":[\"A\"],\"Credentials\":null}",
                    out document, out error), error);
                Assert.True(document.CredentialsExplicitNull);
                Assert.False(document.CredentialsPresent);
                Assert.True(ConfigJsonReader.TryParse(
                    "{\"Devices\":[\"A\"]}", out document, out error), error);
                Assert.False(document.CredentialsPresent);
            });

            TestRunner.Run("旧格式凭据：缺失/null 经导入计划变为需重设且可提交", () =>
            {
                AppConfig current = SampleConfig();
                current.Credentials.Add(new CredentialEntry
                {
                    Title = "当前", EncryptedSecret = "QUJD", NeedsReset = false
                });
                ConfigImportDocument document;
                string error;
                Assert.True(ConfigJsonReader.TryParse(
                    "{\"Devices\":[\"A\"],\"Credentials\":[{\"Title\":\"old\"}]}",
                    out document, out error), error);
                ConfigImportPlan plan;
                Assert.True(ConfigTransfer.TryBuildPlan(document, current,
                    new ConfigImportOptions
                    {
                        MergeDevicesOnly = false,
                        ReplaceCredentials = true
                    }, new FakeProtector(), out plan, out error), error);
                Assert.True(plan.CredentialsReplaced);
                Assert.Equal(1, plan.ResetCredentialCount);
                Assert.True(plan.Credentials[0].NeedsReset);
                Assert.Equal(string.Empty, plan.Credentials[0].EncryptedSecret);

                ConfigImportPlan keep;
                Assert.True(ConfigTransfer.TryBuildPlan(document, current,
                    new ConfigImportOptions
                    {
                        MergeDevicesOnly = false,
                        ReplaceCredentials = false
                    }, new FakeProtector(), out keep, out error), error);
                Assert.False(keep.CredentialsReplaced);
                Assert.Equal(1, keep.Result.Credentials.Count);
                Assert.Equal("当前", keep.Result.Credentials[0].Title);
                Assert.Equal("QUJD", keep.Result.Credentials[0].EncryptedSecret);
            });

            TestRunner.Run("路径：Windows 非法冒号、保留名与不完整 UNC 拒绝", () =>
            {
                Assert.NotNull(ConfigValidator.ValidateRootPath("C:\\shots:bad"));
                Assert.NotNull(ConfigValidator.ValidateRootPath("D:\\CON\\shots"));
                Assert.NotNull(ConfigValidator.ValidateRootPath("\\\\server"));
                Assert.NotNull(ConfigValidator.ValidateRootPath("\\\\server\\"));
                Assert.NotNull(ConfigValidator.ValidateRootPath("shots"));
                Assert.NotNull(ConfigValidator.ValidateRootPath("C:\\shots."));
                Assert.Null(ConfigValidator.ValidateRootPath("C:\\shots"));
                Assert.Null(ConfigValidator.ValidateRootPath("C:/shots"));
                Assert.Null(ConfigValidator.ValidateRootPath("\\\\server\\share\\shots"));
                Assert.Null(ConfigValidator.ValidateRootPath(null));
            });

            TestRunner.Run("路径：解析、导入计划与设置候选从正常入口拒绝非法 RootPath", () =>
            {
                ConfigImportDocument document;
                string error;
                Assert.False(ConfigJsonReader.TryParse(Exchange("Excluded",
                    "{\"Devices\":[],\"RootPath\":\"C:\\\\shots:bad\"}"),
                    out document, out error));
                Assert.True(error.Contains("冒号") || error.Contains("目录"), error);

                Assert.False(ConfigJsonReader.TryParse(Exchange("Excluded",
                    "{\"Devices\":[],\"RootPath\":\"D:\\\\CON\\\\shots\"}"),
                    out document, out error));
                Assert.True(error.Contains("CON") || error.Contains("保留名"), error);

                Assert.True(ConfigJsonReader.TryParse(Exchange("Excluded",
                    "{\"Devices\":[],\"RootPath\":\"D:\\\\shots\"}"),
                    out document, out error), error);
                document.Config.RootPath = "D:\\CON\\shots";
                document.PresentFields.Add("RootPath");
                ConfigImportPlan plan;
                Assert.False(ConfigTransfer.TryBuildPlan(document, SampleConfig(),
                    new ConfigImportOptions
                    {
                        MergeDevicesOnly = false,
                        ImportRootPath = true
                    }, new FakeProtector(), out plan, out error));
                Assert.True(error.Contains("CON") || error.Contains("保留名"), error);

                string settingsError = ConfigValidator.ValidateRootPath("\\\\server");
                Assert.NotNull(settingsError);
                Assert.True(settingsError.Contains("UNC"), settingsError);
            });

            TestRunner.Run("旧格式：Devices 必填，缺失 null 和非数组拒绝", () =>
            {
                AssertParseFails("{}", "缺少 Devices");
                AssertParseFails("{\"Devices\":null}", "Devices");
                AssertParseFails("{\"Devices\":\"甲\"}", "Devices");
                AssertParseFails("{\"Devices\":[1]}", "不是字符串");
            });

            TestRunner.Run("旧格式：缺字段沿用默认，空目录名归一，false 保留", () =>
            {
                ConfigImportDocument document;
                string error;
                string json = "{\"Devices\":[\"甲\"],\"FolderNamePattern\":\"\","
                    + "\"AutoClearClipboard\":false,\"UiScalePercent\":137,"
                    + "\"Credentials\":null}";
                Assert.True(ConfigJsonReader.TryParse(json, out document, out error), error);
                Assert.True(document.CredentialsExplicitNull);
                Assert.False(document.CredentialsPresent);
                Assert.False(document.Config.AutoClearClipboard);
                Assert.Equal(100, document.Config.UiScalePercent);
                Assert.Equal(FolderNamePattern.Default, document.Config.FolderNamePattern);
                Assert.Equal("旧格式归一默认值",
                    document.FieldSources["UiScalePercent"]);
                Assert.Equal("旧格式归一默认值",
                    document.FieldSources["FolderNamePattern"]);
                Assert.Equal("旧格式默认值",
                    document.FieldSources["RootPath"]);
                Assert.True(document.Has("CaptureMode"));
                Assert.Equal(CaptureMode.Auto, document.Config.CaptureMode);
            });

            TestRunner.Run("文件读取：支持 UTF-8 BOM 且不产生修复/备份文件", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    string path = Path.Combine(dir, "backup.json");
                    byte[] body = Encoding.UTF8.GetBytes(Exchange(
                        "Excluded", "{\"Devices\":[\"甲\"]}"));
                    var bytes = new byte[body.Length + 3];
                    bytes[0] = 0xEF;
                    bytes[1] = 0xBB;
                    bytes[2] = 0xBF;
                    Buffer.BlockCopy(body, 0, bytes, 3, body.Length);
                    File.WriteAllBytes(path, bytes);
                    string before = string.Join(",", Directory.GetFiles(dir));

                    ConfigImportDocument document;
                    string error;
                    Assert.True(ConfigJsonReader.TryReadFile(path, out document, out error), error);
                    Assert.Equal(path, document.SourcePath);
                    Assert.Equal(1, document.Config.Devices.Count);
                    Assert.Equal(before, string.Join(",", Directory.GetFiles(dir)));
                }
                finally { Cleanup(dir); }
            });

            TestRunner.Run("文件读取：5 MiB 上限与非法 UTF-8 拒绝且不读入候选", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    string large = Path.Combine(dir, "large.json");
                    File.WriteAllBytes(large, new byte[ConfigJsonReader.MaxFileBytes + 1]);
                    ConfigImportDocument document;
                    string error;
                    Assert.False(ConfigJsonReader.TryReadFile(large, out document, out error));
                    Assert.True(error.Contains("5 MiB"), error);

                    string invalid = Path.Combine(dir, "invalid.json");
                    File.WriteAllBytes(invalid, new byte[] { 0xC3, 0x28 });
                    Assert.False(ConfigJsonReader.TryReadFile(invalid, out document, out error));
                    Assert.True(error.Contains("UTF-8"), error);
                }
                finally { Cleanup(dir); }
            });

            TestRunner.Run("导入计划：默认仅合并设备且重复导入幂等", () =>
            {
                AppConfig current = SampleConfig();
                current.Credentials.Add(new CredentialEntry
                {
                    Title = "原凭据", EncryptedSecret = "QUJD", NeedsReset = true
                });
                ConfigImportDocument document;
                string error;
                Assert.True(ConfigJsonReader.TryParse(Exchange("Excluded",
                    "{\"Devices\":[\"乙\",\"丙\",\"丁\"],\"RootPath\":\"E:\\\\other\","
                    + "\"AutoClearClipboard\":true}"), out document, out error), error);

                ConfigImportPlan plan;
                var options = new ConfigImportOptions { MergeDevicesOnly = true };
                Assert.True(ConfigTransfer.TryBuildPlan(
                    document, current, options, new FakeProtector(), out plan, out error), error);
                Assert.Equal("甲,乙,丙,丁", string.Join(",", plan.Devices.ToArray()));
                Assert.Equal("D:\\shots", plan.Result.RootPath);
                Assert.False(plan.Result.AutoClearClipboard);
                Assert.Equal(2, plan.AddedDeviceCount);
                Assert.Equal(2, plan.RetainedDeviceCount);
                Assert.Equal(0, plan.RemovedDeviceCount);
                Assert.Equal(1, plan.Credentials.Count);
                Assert.True(plan.Credentials[0].NeedsReset);

                ConfigImportPlan second;
                Assert.True(ConfigTransfer.TryBuildPlan(
                    document, plan.Result, options, new FakeProtector(), out second, out error), error);
                Assert.Equal("甲,乙,丙,丁",
                    string.Join(",", second.Devices.ToArray()));
                Assert.Equal(0, second.AddedDeviceCount);
            });

            TestRunner.Run("导入计划：替换仅改参与字段，RootPath 默认保留", () =>
            {
                AppConfig current = SampleConfig();
                current.WindowLeft = 11;
                current.WindowTop = 22;
                current.TrayHintShown = true;
                current.LastDevice = "乙";

                ConfigImportDocument document;
                string error;
                Assert.True(ConfigJsonReader.TryParse(Exchange("Excluded",
                    "{\"Devices\":[\"丙\"],\"RootPath\":\"E:\\\\new\","
                    + "\"AutoClearClipboard\":true,\"UiScalePercent\":200,"
                    + "\"CaptureMode\":2,\"IsDeviceCollapsed\":false}"),
                    out document, out error), error);

                ConfigImportPlan plan;
                var options = new ConfigImportOptions
                {
                    MergeDevicesOnly = false,
                    ImportRootPath = false
                };
                Assert.True(ConfigTransfer.TryBuildPlan(
                    document, current, options, new FakeProtector(), out plan, out error), error);
                Assert.Equal("丙", plan.Result.Devices[0]);
                Assert.Equal("D:\\shots", plan.Result.RootPath);
                Assert.True(plan.Result.AutoClearClipboard);
                Assert.Equal(200, plan.Result.UiScalePercent);
                Assert.Equal(CaptureMode.BitBltOnly, plan.Result.CaptureMode);
                Assert.Null(plan.Result.LastDevice);
                Assert.Equal((int?)11, plan.Result.WindowLeft);
                Assert.Equal((int?)22, plan.Result.WindowTop);
                Assert.True(plan.Result.TrayHintShown);
            });

            TestRunner.Run("导入计划：显式选择后才导入根目录与本机字段", () =>
            {
                AppConfig current = SampleConfig();
                current.WindowLeft = 11;
                current.WindowTop = 22;
                ConfigImportDocument document;
                string error;
                Assert.True(ConfigJsonReader.TryParse(Exchange("DpapiCurrentUser",
                    "{\"Devices\":[\"丙\"],\"RootPath\":\"E:\\\\new\","
                    + "\"Credentials\":[],\"LastDevice\":\"丙\","
                    + "\"WindowLeft\":300,\"WindowTop\":400,\"TrayHintShown\":true}"),
                    out document, out error), error);

                ConfigImportPlan plan;
                var options = new ConfigImportOptions
                {
                    MergeDevicesOnly = false,
                    ImportRootPath = true,
                    RestoreLocalFields = true
                };
                Assert.True(ConfigTransfer.TryBuildPlan(
                    document, current, options, new FakeProtector(), out plan, out error), error);
                Assert.Equal("E:\\new", plan.Result.RootPath);
                Assert.Equal("丙", plan.Result.LastDevice);
                Assert.Equal((int?)300, plan.Result.WindowLeft);
                Assert.Equal((int?)400, plan.Result.WindowTop);
                Assert.True(plan.Result.TrayHintShown);
            });

            TestRunner.Run("导入计划：旧裸格式即使请求也不能恢复本机字段", () =>
            {
                AppConfig current = SampleConfig();
                current.WindowLeft = 11;
                current.WindowTop = 22;
                current.LastDevice = "甲";
                ConfigImportDocument document;
                string error;
                Assert.True(ConfigJsonReader.TryParse(
                    "{\"Devices\":[\"丙\"],\"LastDevice\":\"丙\","
                    + "\"WindowLeft\":300,\"WindowTop\":400,\"TrayHintShown\":true}",
                    out document, out error), error);

                ConfigImportPlan plan;
                Assert.True(ConfigTransfer.TryBuildPlan(document, current,
                    new ConfigImportOptions
                    {
                        MergeDevicesOnly = false,
                        RestoreLocalFields = true
                    }, new FakeProtector(), out plan, out error), error);
                Assert.Equal((int?)11, plan.Result.WindowLeft);
                Assert.Equal((int?)22, plan.Result.WindowTop);
                Assert.False(plan.Result.TrayHintShown);
                Assert.Null(plan.Result.LastDevice);
            });

            TestRunner.Run("导入计划：缺字段不参与替换，显式 false 与空数组生效", () =>
            {
                AppConfig current = SampleConfig();
                current.Credentials.Add(new CredentialEntry
                {
                    Title = "保留我", EncryptedSecret = "QUJD"
                });
                ConfigImportDocument document;
                string error;
                Assert.True(ConfigJsonReader.TryParse(Exchange("DpapiCurrentUser",
                    "{\"Devices\":[],\"AutoClearClipboard\":false,"
                    + "\"Credentials\":[]}"), out document, out error), error);

                ConfigImportPlan plan;
                var options = new ConfigImportOptions
                {
                    MergeDevicesOnly = false,
                    ReplaceCredentials = true
                };
                Assert.True(ConfigTransfer.TryBuildPlan(
                    document, current, options, new FakeProtector(), out plan, out error), error);
                Assert.Equal(0, plan.Result.Devices.Count);
                Assert.False(plan.Result.AutoClearClipboard);
                Assert.Equal("D:\\shots", plan.Result.RootPath);
                Assert.Equal(0, plan.Result.Credentials.Count);
                Assert.True(plan.CredentialsReplaced);
            });

            TestRunner.Run("导入计划：未勾选替换凭据时 Excluded/缺字段均保留当前凭据", () =>
            {
                AppConfig current = SampleConfig();
                current.Credentials.Add(new CredentialEntry
                {
                    Title = "保留", EncryptedSecret = "QUJD", NeedsReset = true
                });
                ConfigImportDocument document;
                string error;
                Assert.True(ConfigJsonReader.TryParse(Exchange("Excluded",
                    "{\"Devices\":[\"丙\"]}"), out document, out error), error);

                ConfigImportPlan plan;
                Assert.True(ConfigTransfer.TryBuildPlan(document, current,
                    new ConfigImportOptions
                    {
                        MergeDevicesOnly = false,
                        ReplaceCredentials = true
                    }, new FakeProtector(), out plan, out error), error);
                Assert.False(plan.CredentialsReplaced);
                Assert.Equal(1, plan.Result.Credentials.Count);
                Assert.Equal("保留", plan.Result.Credentials[0].Title);
                Assert.True(plan.Result.Credentials[0].NeedsReset);
            });

            TestRunner.Run("导入计划：密文可解/不可解分别计为可用/需重设且密文原样保留", () =>
            {
                AppConfig current = SampleConfig();
                ConfigImportDocument document;
                string error;
                Assert.True(ConfigJsonReader.TryParse(Exchange("DpapiCurrentUser",
                    "{\"Devices\":[],\"Credentials\":["
                    + "{\"Title\":\"可用\",\"EncryptedSecret\":\"c2VjcmV0\"},"
                    + "{\"Title\":\"坏密文\",\"EncryptedSecret\":\"%%%\"},"
                    + "{\"Title\":\"空密文\",\"EncryptedSecret\":\"\"}]}"),
                    out document, out error), error);

                ConfigImportPlan plan;
                Assert.True(ConfigTransfer.TryBuildPlan(document, current,
                    new ConfigImportOptions
                    {
                        MergeDevicesOnly = false,
                        ReplaceCredentials = true
                    }, new FakeProtector(), out plan, out error), error);
                Assert.True(plan.CredentialsReplaced);
                Assert.Equal(1, plan.UsableCredentialCount);
                Assert.Equal(2, plan.ResetCredentialCount);
                Assert.False(plan.Credentials[0].NeedsReset);
                Assert.Equal("c2VjcmV0", plan.Credentials[0].EncryptedSecret);
                Assert.True(plan.Credentials[1].NeedsReset);
                Assert.Equal("%%%", plan.Credentials[1].EncryptedSecret);
                Assert.True(plan.Credentials[2].NeedsReset);
            });

            TestRunner.Run("导入计划：无保护器时不把未检查密文当可用", () =>
            {
                AppConfig current = SampleConfig();
                ConfigImportDocument document;
                string error;
                Assert.True(ConfigJsonReader.TryParse(Exchange("DpapiCurrentUser",
                    "{\"Devices\":[],\"Credentials\":[{\"Title\":\"A\","
                    + "\"EncryptedSecret\":\"QUJD\"}]}"), out document, out error), error);
                ConfigImportPlan plan;
                Assert.True(ConfigTransfer.TryBuildPlan(document, current,
                    new ConfigImportOptions
                    {
                        MergeDevicesOnly = false,
                        ReplaceCredentials = true
                    }, null, out plan, out error), error);
                Assert.Equal(0, plan.UsableCredentialCount);
                Assert.Equal(1, plan.ResetCredentialCount);
                Assert.True(plan.Credentials[0].NeedsReset);
            });

            TestRunner.Run("导入预览：构建计划不修改当前配置，取消后仍保持原草稿", () =>
            {
                AppConfig current = SampleConfig();
                current.Credentials.Add(new CredentialEntry
                {
                    Title = "当前凭据", EncryptedSecret = "QUJD", NeedsReset = true
                });
                string beforeDevices = string.Join(",", current.Devices.ToArray());
                string beforeRoot = current.RootPath;
                string beforeSecret = current.Credentials[0].EncryptedSecret;
                ConfigImportDocument document;
                string error;
                Assert.True(ConfigJsonReader.TryParse(Exchange("Excluded",
                    "{\"Devices\":[\"新设备\"],\"RootPath\":\"E:\\\\new\"}"),
                    out document, out error), error);

                ConfigImportPlan plan;
                Assert.True(ConfigTransfer.TryBuildPlan(document, current,
                    new ConfigImportOptions
                    {
                        MergeDevicesOnly = false,
                        ImportRootPath = true
                    }, new FakeProtector(), out plan, out error), error);
                Assert.NotNull(plan);
                Assert.Equal(beforeDevices, string.Join(",", current.Devices.ToArray()));
                Assert.Equal(beforeRoot, current.RootPath);
                Assert.Equal(beforeSecret, current.Credentials[0].EncryptedSecret);
                Assert.True(current.Credentials[0].NeedsReset);
            });

            TestRunner.Run("导入预览：设备变化列出最终顺序、新增与移除", () =>
            {
                string text = ConfigTransfer.BuildDeviceChangeText(
                    new List<string> { "甲", "乙" },
                    new List<string> { "甲", "丙", "丁" });
                Assert.True(text.Contains("最终设备顺序（共 3 台）"), text);
                Assert.True(text.Contains("1. 甲"), text);
                Assert.True(text.Contains("2. 丙"), text);
                Assert.True(text.Contains("3. 丁"), text);
                Assert.True(text.Contains("新增设备：丙、丁"), text);
                Assert.True(text.Contains("移除设备：乙"), text);
                Assert.False(text.Contains("设备顺序未变化"), text);

                string unchanged = ConfigTransfer.BuildDeviceChangeText(
                    new List<string> { "甲", "乙" },
                    new List<string> { "甲", "乙" });
                Assert.True(unchanged.Contains("设备顺序未变化"), unchanged);
                string reordered = ConfigTransfer.BuildDeviceChangeText(
                    new List<string> { "甲", "乙" },
                    new List<string> { "乙", "甲" });
                Assert.False(reordered.Contains("设备顺序未变化"), reordered);
            });

            TestRunner.Run("导出：阻止覆盖权威配置、锁文件和恢复备份", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    string auth = Path.Combine(dir, "config.json");
                    File.WriteAllText(auth, "{\"Devices\":[]}");
                    string error;
                    Assert.False(ConfigTransfer.TryExportFile(SampleConfig(),
                        ConfigJsonReader.CredentialModeExcluded,
                        auth, auth, out error));
                    Assert.True(error.Contains("不能导出"), error);
                    Assert.False(ConfigTransfer.TryExportFile(SampleConfig(),
                        ConfigJsonReader.CredentialModeExcluded,
                        auth + ".lock", auth, out error));
                    Assert.False(ConfigTransfer.TryExportFile(SampleConfig(),
                        ConfigJsonReader.CredentialModeExcluded,
                        Path.Combine(dir, "config.preimport.20260101T000000.abc.json"),
                        auth, out error));
                    Assert.False(ConfigTransfer.TryExportFile(SampleConfig(),
                        ConfigJsonReader.CredentialModeExcluded,
                        Path.Combine(dir, "config.corrupt.20260101.json"),
                        auth, out error));
                    Assert.Equal("{\"Devices\":[]}", File.ReadAllText(auth));
                }
                finally { Cleanup(dir); }
            });

            TestRunner.Run("导出：普通目标原子写入、可覆盖旧文件且无临时残留", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    string target = Path.Combine(dir, "export.json");
                    File.WriteAllText(target, "old");
                    string error;
                    Assert.True(ConfigTransfer.TryExportFile(SampleConfig(),
                        ConfigJsonReader.CredentialModeExcluded,
                        target, Path.Combine(dir, "config.json"), out error), error);
                    Assert.Equal(0, Directory.GetFiles(dir, "*.tmp").Length);
                    ConfigImportDocument document;
                    Assert.True(ConfigJsonReader.TryReadFile(target, out document, out error), error);
                    Assert.Equal(2, document.Config.Devices.Count);
                }
                finally { Cleanup(dir); }
            });

            TestRunner.Run("导出：目标为目录时失败且不改变应用配置路径", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    string auth = Path.Combine(dir, "config.json");
                    File.WriteAllText(auth, "stable");
                    string targetDir = Path.Combine(dir, "target");
                    Directory.CreateDirectory(targetDir);
                    string error;
                    Assert.False(ConfigTransfer.TryExportFile(SampleConfig(),
                        ConfigJsonReader.CredentialModeExcluded,
                        targetDir, auth, out error));
                    Assert.True(Directory.Exists(targetDir));
                    Assert.Equal("stable", File.ReadAllText(auth));
                    Assert.Equal(0, Directory.GetFiles(dir, "*.tmp").Length);
                }
                finally { Cleanup(dir); }
            });
        }

        private static AppConfig SampleConfig()
        {
            return new AppConfig
            {
                RootPath = "D:\\shots",
                Devices = new List<string> { "甲", "乙" },
                Credentials = new List<CredentialEntry>(),
                AutoClearClipboard = false,
                CaptureMode = CaptureMode.PrintWindowOnly,
                IsDeviceCollapsed = true,
                IsCredentialCollapsed = false,
                UiScalePercent = 175,
                FolderNamePattern = "巡检-yyyyMMdd",
                LastDevice = "甲",
                WindowLeft = 9,
                WindowTop = 7,
                TrayHintShown = false
            };
        }

        private static string Exchange(string mode, string config, string format = null,
            int version = 1, string extraTop = null)
        {
            if (format == null) format = ConfigJsonReader.ExchangeFormat;
            string extra = string.IsNullOrEmpty(extraTop) ? string.Empty : "," + extraTop;
            return "{\"Format\":\"" + format + "\",\"Version\":" + version
                + ",\"CredentialMode\":\"" + mode + "\",\"Config\":" + config
                + extra + "}";
        }

        private static void AssertParseFails(string json, string expectedFragment)
        {
            ConfigImportDocument document;
            string error;
            Assert.False(ConfigJsonReader.TryParse(json, out document, out error),
                "解析应失败：" + json);
            Assert.NotNull(error);
            Assert.True(error.Contains(expectedFragment),
                "错误应含「" + expectedFragment + "」，实际：" + error);
        }

        private static string MakeTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(),
                "iw-transfer-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void Cleanup(string dir)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch
            {
            }
        }
    }
}
