using System;
using System.Collections.Generic;
using System.IO;
using InspectionWidget.Config;

namespace InspectionWidget.Tests
{
    /// <summary>新配置字段的旧配置、往返、深克隆、草稿贯通与交换兼容。</summary>
    public static class QuickLinkConfigTests
    {
        public static void RunAll()
        {
            TestRunner.Run("配置：默认配置无链接、计划模式关闭", () =>
            {
                AppConfig cfg = AppConfig.CreateDefault();
                Assert.NotNull(cfg.QuickLinks);
                Assert.Equal(0, cfg.QuickLinks.Count);
                Assert.False(cfg.PlanModeEnabled);
            });

            TestRunner.Run("配置：老配置缺新字段 → 空链接与 false 开关", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    File.WriteAllText(ConfigFile(dir),
                        "{\"RootPath\":\"D:/shots\",\"Devices\":[\"设备甲\"]}");
                    LoadResult r = MakeStore(dir).Load();
                    Assert.Equal(ConfigLoadKind.Defaulted, r.Kind);
                    Assert.NotNull(r.Config.QuickLinks);
                    Assert.Equal(0, r.Config.QuickLinks.Count);
                    Assert.False(r.Config.PlanModeEnabled);
                    Assert.Equal("设备甲", r.Config.Devices[0]);
                }
                finally { Cleanup(dir); }
            });

            TestRunner.Run("配置：新字段序列化往返保留顺序、中文/空格与 Enabled=false", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    var cfg = AppConfig.CreateDefault();
                    cfg.PlanModeEnabled = true;
                    cfg.QuickLinks.Add(new QuickLinkEntry
                    { Title = "内网 首页", Url = "http://10.20.30.40:8080/巡检 首页", Enabled = false });
                    cfg.QuickLinks.Add(new QuickLinkEntry
                    { Title = "日报", Url = "https://oa.example.invalid/日报" });
                    SaveResult saved = MakeStore(dir).Save(cfg);
                    Assert.True(saved.Saved, saved.Error);
                    LoadResult r = MakeStore(dir).Load();
                    Assert.Equal(ConfigLoadKind.Ok, r.Kind);
                    Assert.Equal(2, r.Config.QuickLinks.Count);
                    Assert.Equal("内网 首页", r.Config.QuickLinks[0].Title);
                    Assert.False(r.Config.QuickLinks[0].Enabled);
                    Assert.True(r.Config.QuickLinks[1].Enabled);
                    Assert.True(r.Config.PlanModeEnabled);
                }
                finally { Cleanup(dir); }
            });

            TestRunner.Run("配置：深克隆不共享列表与条目引用，改副本不动原件", () =>
            {
                var cfg = AppConfig.CreateDefault();
                cfg.PlanModeEnabled = true;
                cfg.QuickLinks.Add(new QuickLinkEntry { Title = "甲", Url = "http://10.0.0.1/a" });
                AppConfig clone = global::InspectionWidget.AppConfigCloner.Clone(cfg);
                Assert.False(object.ReferenceEquals(cfg.QuickLinks, clone.QuickLinks));
                Assert.False(object.ReferenceEquals(cfg.QuickLinks[0], clone.QuickLinks[0]));
                Assert.True(clone.PlanModeEnabled);
                clone.QuickLinks[0].Title = "改过";
                clone.QuickLinks.Add(new QuickLinkEntry());
                Assert.Equal("甲", cfg.QuickLinks[0].Title);
                Assert.Equal(1, cfg.QuickLinks.Count);
            });

            TestRunner.Run("草稿：LoadFrom/Snapshot/ApplyPlan/AcceptSaved 全程深拷贝", () =>
            {
                var cfg = AppConfig.CreateDefault();
                cfg.PlanModeEnabled = true;
                cfg.QuickLinks.Add(new QuickLinkEntry
                { Title = "甲", Url = "http://10.0.0.1/a", Enabled = false });
                var draft = SettingsDraft.FromConfig(cfg, "rev-1");
                Assert.False(object.ReferenceEquals(cfg.QuickLinks[0], draft.QuickLinks[0]));
                Assert.True(draft.PlanModeEnabled);

                draft.QuickLinks[0].Title = "草稿改过";
                Assert.Equal("甲", cfg.QuickLinks[0].Title);
                AppConfig snapshot = draft.SnapshotForTransferPreview();
                Assert.Equal("草稿改过", snapshot.QuickLinks[0].Title);
                Assert.False(snapshot.QuickLinks[0].Enabled);
                Assert.False(object.ReferenceEquals(draft.QuickLinks[0], snapshot.QuickLinks[0]));
                Assert.True(snapshot.PlanModeEnabled);

                var incoming = AppConfig.CreateDefault();
                incoming.QuickLinks.Add(new QuickLinkEntry { Title = "导入链接", Url = "https://oa/a" });
                var plan = new ConfigImportPlan
                { Result = incoming, Devices = new List<string> { "设备甲" } };
                draft.ApplyPlan(plan, true);
                Assert.Equal("草稿改过", draft.QuickLinks[0].Title);
                Assert.True(draft.PlanModeEnabled);
                draft.ApplyPlan(plan, false);
                Assert.Equal("导入链接", draft.QuickLinks[0].Title);
                Assert.False(object.ReferenceEquals(incoming.QuickLinks[0], draft.QuickLinks[0]));
                Assert.False(draft.PlanModeEnabled);

                var saved = AppConfig.CreateDefault();
                saved.PlanModeEnabled = true;
                saved.QuickLinks.Add(new QuickLinkEntry { Title = "落盘链接", Url = "http://10.0.0.2/a" });
                draft.AcceptSaved(saved, "rev-2");
                Assert.Equal("落盘链接", draft.QuickLinks[0].Title);
                Assert.False(object.ReferenceEquals(saved.QuickLinks[0], draft.QuickLinks[0]));
                Assert.True(draft.PlanModeEnabled);
                Assert.Equal("rev-2", draft.SavedRevision);
            });

            TestRunner.Run("交换：MergeDevicesOnly 不覆盖新字段且文案准确", () =>
            {
                AppConfig current = Current();
                string json = Exchange("{\"Devices\":[\"新设备\"],\"QuickLinks\":["
                    + "{\"Title\":\"源链接\",\"Url\":\"http://10.0.0.9/a\",\"Enabled\":true}],"
                    + "\"PlanModeEnabled\":false}");
                ConfigImportDocument document;
                string error;
                Assert.True(ConfigJsonReader.TryParse(json, out document, out error), error);
                ConfigImportPlan plan;
                Assert.True(ConfigTransfer.TryBuildPlan(document, current,
                    new ConfigImportOptions(), null, out plan, out error), error);
                Assert.Equal(1, plan.Result.QuickLinks.Count);
                Assert.Equal("本机链接", plan.Result.QuickLinks[0].Title);
                Assert.False(plan.Result.QuickLinks[0].Enabled);
                Assert.True(plan.Result.PlanModeEnabled);
                Assert.True(ChangesContain(plan, "保留"), "合并模式应说明新字段保留");
            });

            TestRunner.Run("交换：非 merge 且源缺新字段 → 保留本机链接与开关", () =>
            {
                AppConfig current = Current();
                string json = Exchange("{\"Devices\":[\"新设备\"]}");
                ConfigImportDocument document;
                string error;
                Assert.True(ConfigJsonReader.TryParse(json, out document, out error), error);
                ConfigImportPlan plan;
                Assert.True(ConfigTransfer.TryBuildPlan(document, current,
                    new ConfigImportOptions { MergeDevicesOnly = false }, null, out plan, out error), error);
                Assert.Equal(1, plan.Result.QuickLinks.Count);
                Assert.Equal("本机链接", plan.Result.QuickLinks[0].Title);
                Assert.True(plan.Result.PlanModeEnabled);
                Assert.True(ChangesContain(plan, "保留"), "缺字段应说明保留");
            });

            TestRunner.Run("交换：非 merge 且源存在新字段 → 替换并深拷贝", () =>
            {
                AppConfig current = Current();
                string json = Exchange("{\"Devices\":[\"新设备\"],\"QuickLinks\":["
                    + "{\"Title\":\"源链接\",\"Url\":\"https://oa.example.invalid/a\",\"Enabled\":true}],"
                    + "\"PlanModeEnabled\":false}");
                ConfigImportDocument document;
                string error;
                Assert.True(ConfigJsonReader.TryParse(json, out document, out error), error);
                ConfigImportPlan plan;
                Assert.True(ConfigTransfer.TryBuildPlan(document, current,
                    new ConfigImportOptions { MergeDevicesOnly = false }, null, out plan, out error), error);
                Assert.Equal(1, plan.Result.QuickLinks.Count);
                Assert.Equal("源链接", plan.Result.QuickLinks[0].Title);
                Assert.False(plan.Result.PlanModeEnabled);
                Assert.False(object.ReferenceEquals(
                    document.Config.QuickLinks[0], plan.Result.QuickLinks[0]));
                Assert.True(ChangesContain(plan, "快捷链接"), "替换时应说明快捷链接变化");
                Assert.True(ChangesContain(plan, "计划模式"), "替换时应说明计划模式变化");
            });

            TestRunner.Run("交换：普通导出含链接与计划模式，不含凭据正文与每日进度", () =>
            {
                AppConfig cfg = Current();
                cfg.Credentials.Add(new CredentialEntry { Title = "堡垒机", EncryptedSecret = "QUJD" });
                string json = ConfigTransfer.BuildExportJson(cfg, ConfigJsonReader.CredentialModeExcluded);
                Assert.True(json.Contains("\"QuickLinks\""), json);
                Assert.True(json.Contains("本机链接"), json);
                Assert.True(json.Contains("\"PlanModeEnabled\""), json);
                Assert.False(json.Contains("\"Credentials\""), "普通导出不得含凭据字段");
                Assert.False(json.Contains("QUJD"), "不得含凭据密文");
                Assert.False(json.Contains("InspectionProgress"), "不得含每日进度");
                ConfigImportDocument document;
                string error;
                Assert.True(ConfigJsonReader.TryParse(json, out document, out error), error);
                Assert.Equal(1, document.Config.QuickLinks.Count);
                Assert.False(document.Config.QuickLinks[0].Enabled);
                Assert.True(document.Config.PlanModeEnabled);
            });

            TestRunner.Run("交换：DPAPI 完整备份含新字段与凭据", () =>
            {
                AppConfig cfg = Current();
                cfg.Credentials.Add(new CredentialEntry { Title = "堡垒机", EncryptedSecret = "QUJD" });
                string json = ConfigTransfer.BuildExportJson(cfg, ConfigJsonReader.CredentialModeDpapi);
                Assert.True(json.Contains("\"QuickLinks\""), json);
                Assert.True(json.Contains("\"PlanModeEnabled\""), json);
                ConfigImportDocument document;
                string error;
                Assert.True(ConfigJsonReader.TryParse(json, out document, out error), error);
                Assert.True(document.IsFullBackup);
                Assert.Equal("本机链接", document.Config.QuickLinks[0].Title);
            });

            TestRunner.Run("交换：版本仍为 1，v1 普通设备导入不被破坏", () =>
            {
                Assert.Equal(1, ConfigJsonReader.ExchangeVersion);
                string json = Exchange("{\"Devices\":[\"甲\"]}");
                ConfigImportDocument document;
                string error;
                Assert.True(ConfigJsonReader.TryParse(json, out document, out error), error);
                Assert.False(document.Has("QuickLinks"));
                Assert.False(document.Has("PlanModeEnabled"));
                ConfigImportPlan plan;
                Assert.True(ConfigTransfer.TryBuildPlan(document, Current(),
                    new ConfigImportOptions(), null, out plan, out error), error);
                Assert.True(plan.Devices.Contains("甲"));
            });

            TestRunner.Run("导入预览：非法快捷链接与非法开关被拒", () =>
            {
                string[] bad = {
                    "{\"Devices\":[],\"QuickLinks\":[{\"Title\":\"坏\",\"Url\":\"file:///C:/x\"}]}",
                    "{\"Devices\":[],\"QuickLinks\":[{\"Title\":\"坏\",\"Url\":\"javascript:alert(1)\"}]}",
                    "{\"Devices\":[],\"QuickLinks\":[{\"Title\":\"\",\"Url\":\"http://10.0.0.1/a\"}]}",
                    "{\"Devices\":[],\"QuickLinks\":[{\"Title\":\"坏\",\"Url\":\"http://10.0.0.1/a\",\"Enabled\":\"yes\"}]}",
                    "{\"Devices\":[],\"QuickLinks\":[{\"Title\":\"坏\",\"Url\":\"http://10.0.0.1/a\",\"Bogus\":1}]}",
                    "{\"Devices\":[],\"QuickLinks\":\"不是数组\"}",
                    "{\"Devices\":[],\"PlanModeEnabled\":\"false\"}"
                };
                for (int i = 0; i < bad.Length; i++)
                {
                    ConfigImportDocument document;
                    string error;
                    Assert.False(ConfigJsonReader.TryParse(Exchange(bad[i]), out document, out error),
                        "第 " + (i + 1) + " 个损坏配置应被拒绝");
                    Assert.NotNull(error);
                }
            });

            TestRunner.Run("导入预览：QuickLinks 可空列表、缺字段不加入 PresentFields", () =>
            {
                ConfigImportDocument document;
                string error;
                Assert.True(ConfigJsonReader.TryParse(
                    Exchange("{\"Devices\":[],\"QuickLinks\":null}"), out document, out error), error);
                Assert.True(document.Has("QuickLinks"));
                Assert.NotNull(document.Config.QuickLinks);
                Assert.Equal(0, document.Config.QuickLinks.Count);
                Assert.False(document.Has("PlanModeEnabled"));
                Assert.True(ConfigJsonReader.TryParse("{\"Devices\":[\"甲\"]}", out document, out error), error);
                Assert.False(document.Has("QuickLinks"));
                Assert.False(document.Has("PlanModeEnabled"));
            });
        }

        private static bool ChangesContain(ConfigImportPlan plan, string fragment)
        {
            for (int i = 0; i < plan.SettingChanges.Count; i++)
                if (plan.SettingChanges[i].Contains(fragment)) return true;
            return false;
        }

        private static AppConfig Current()
        {
            var cfg = AppConfig.CreateDefault();
            cfg.PlanModeEnabled = true;
            cfg.QuickLinks.Add(new QuickLinkEntry
            { Title = "本机链接", Url = "http://10.0.0.1/a", Enabled = false });
            return cfg;
        }

        private static string Exchange(string config)
        {
            return "{\"Format\":\"" + ConfigJsonReader.ExchangeFormat
                + "\",\"Version\":1,\"CredentialMode\":\"Excluded\",\"Config\":" + config + "}";
        }

        private static string MakeTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "iw-p1-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string ConfigFile(string dir)
        {
            return Path.Combine(dir, "config.json");
        }

        private static ConfigStore MakeStore(string dir)
        {
            return new ConfigStore(
                new ConfigPaths { AuthoritativePath = ConfigFile(dir), IsFallback = false },
                delegate(string d) { return true; });
        }

        private static void Cleanup(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch { }
        }
    }
}
