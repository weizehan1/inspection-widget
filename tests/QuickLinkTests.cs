using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using InspectionWidget.Config;
using InspectionWidget.Links;

namespace InspectionWidget.Tests
{
    /// <summary>快捷链接模型、校验与启动器；外部启动全部使用注入 fake。</summary>
    public static class QuickLinkTests
    {
        public static void RunAll()
        {
            TestRunner.Run("快捷链接：新条目 Enabled 默认 true，显式 false 保留", () =>
            {
                Assert.True(new QuickLinkEntry().Enabled);
                Assert.False(new QuickLinkEntry { Enabled = false }.Enabled);
            });

            TestRunner.Run("快捷链接：反序列化缺 Enabled 默认 true，显式 false 保留", () =>
            {
                AppConfig cfg = Deserialize("{\"Devices\":[],\"QuickLinks\":["
                    + "{\"Title\":\"甲\",\"Url\":\"http://10.0.0.1/a\"},"
                    + "{\"Title\":\"乙\",\"Url\":\"https://10.0.0.2/b\",\"Enabled\":false}]}");
                Assert.Equal(2, cfg.QuickLinks.Count);
                Assert.Equal("甲", cfg.QuickLinks[0].Title);
                Assert.True(cfg.QuickLinks[0].Enabled);
                Assert.False(cfg.QuickLinks[1].Enabled);
            });

            TestRunner.Run("校验：内网 IP/主机/端口/路径/中文/查询串的 http(s) 均有效", () =>
            {
                string[] urls = {
                    "http://10.20.30.40:8080/巡检/日报?d=2026-11-14",
                    "https://192.168.1.2/",
                    "http://oa.example.invalid:8090/path/to/page",
                    "HTTP://INTRANET:81/a",
                    "https://example.com/中文 路径",
                    "http://host/path with space?q=1 2"
                };
                for (int i = 0; i < urls.Length; i++)
                    Assert.Null(QuickLinkValidator.Validate(
                        new QuickLinkEntry { Title = "链接", Url = urls[i] }),
                        "第 " + (i + 1) + " 个合法地址被误拒：" + urls[i]);
            });

            TestRunner.Run("校验：file/javascript/ftp/mailto/相对地址/无主机均被拒", () =>
            {
                string[] urls = {
                    "file:///C:/x", "javascript:alert(1)", "/relative/path",
                    "//host/path", "ftp://host/x", "mailto:a@b.com", "http://", "www.example.com/a"
                };
                for (int i = 0; i < urls.Length; i++)
                    Assert.NotNull(QuickLinkValidator.Validate(
                        new QuickLinkEntry { Title = "链接", Url = urls[i] }),
                        "第 " + (i + 1) + " 个非法地址被放行：" + urls[i]);
            });

            TestRunner.Run("校验：名称与 URL 必填，空行草稿、null 条目被拒", () =>
            {
                Assert.NotNull(QuickLinkValidator.Validate(null));
                Assert.NotNull(QuickLinkValidator.Validate(new QuickLinkEntry()));
                Assert.NotNull(QuickLinkValidator.Validate(new QuickLinkEntry { Title = "  ", Url = "http://a" }));
                Assert.NotNull(QuickLinkValidator.Validate(new QuickLinkEntry { Title = "甲" }));
                Assert.NotNull(QuickLinkValidator.Validate(new QuickLinkEntry { Title = "甲", Url = "   " }));
            });

            TestRunner.Run("校验：ValidateAll 接受 null/空列表，定位第 N 条无效项", () =>
            {
                Assert.Null(QuickLinkValidator.ValidateAll(null));
                Assert.Null(QuickLinkValidator.ValidateAll(new List<QuickLinkEntry>()));
                var rows = new List<QuickLinkEntry> {
                    new QuickLinkEntry { Title = "甲", Url = "http://10.0.0.1/a" },
                    new QuickLinkEntry { Title = "乙", Url = "javascript:alert(1)" }
                };
                string error = QuickLinkValidator.ValidateAll(rows);
                Assert.NotNull(error);
                Assert.True(error.Contains("第 2 条"), error);
                Assert.NotNull(QuickLinkValidator.ValidateAll(new List<QuickLinkEntry> { null }));
            });

            TestRunner.Run("克隆：QuickLinkCloner 逐条深拷贝，空/缺列表不泄漏引用", () =>
            {
                var source = new List<QuickLinkEntry> {
                    new QuickLinkEntry { Title = "甲", Url = "http://10.0.0.1/a", Enabled = false }
                };
                List<QuickLinkEntry> copy = QuickLinkCloner.CloneList(source);
                Assert.False(object.ReferenceEquals(source, copy));
                Assert.False(object.ReferenceEquals(source[0], copy[0]));
                Assert.Equal("甲", copy[0].Title);
                Assert.False(copy[0].Enabled);
                copy[0].Title = "改过";
                Assert.Equal("甲", source[0].Title);
                Assert.Equal(0, QuickLinkCloner.CloneList(null).Count);

                var binding = QuickLinkCloner.CloneToBindingList(source);
                Assert.False(object.ReferenceEquals(binding[0], source[0]));
                QuickLinkCloner.ReplaceInto(binding, null);
                Assert.Equal(0, binding.Count);
            });

            TestRunner.Run("启动器：合法 http/https 交给注入 fake，error 为 null", () =>
            {
                var opened = new List<string>();
                var launcher = new QuickLinkLauncher(delegate(string url) { opened.Add(url); });
                string error;
                Assert.True(launcher.TryOpen("http://10.20.30.40:8080/巡检", out error));
                Assert.Null(error);
                Assert.Equal(1, opened.Count);
                Assert.Equal("http://10.20.30.40:8080/巡检", opened[0]);
                Assert.True(launcher.TryOpen("https://oa.example.invalid/a", out error));
                Assert.Null(error);
                Assert.Equal(2, opened.Count);
            });

            TestRunner.Run("启动器：TryOpen 自身拒绝 file/javascript/相对地址且不调用 fake", () =>
            {
                int calls = 0;
                var launcher = new QuickLinkLauncher(delegate(string url) { calls++; });
                string[] bad = { "file:///C:/x", "javascript:alert(1)", "/relative", "ftp://host/x", "http://", "" };
                for (int i = 0; i < bad.Length; i++)
                {
                    string error;
                    Assert.False(launcher.TryOpen(bad[i], out error), bad[i]);
                    Assert.NotNull(error);
                }
                Assert.Equal(0, calls);
            });

            TestRunner.Run("启动器：fake 抛异常转可读错误", () =>
            {
                var launcher = new QuickLinkLauncher(delegate(string url)
                { throw new InvalidOperationException("默认浏览器被占用"); });
                string error;
                Assert.False(launcher.TryOpen("http://10.0.0.1/a", out error));
                Assert.NotNull(error);
                Assert.True(error.Contains("默认浏览器被占用"), error);
            });

            TestRunner.Run("启动器：生产构造可用（构造不启动浏览器）", () =>
                Assert.NotNull(new QuickLinkLauncher()));
        }

        private static AppConfig Deserialize(string json)
        {
            var serializer = new DataContractJsonSerializer(typeof(AppConfig));
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return (AppConfig)serializer.ReadObject(stream);
        }
    }
}
