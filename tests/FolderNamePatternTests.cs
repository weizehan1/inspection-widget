using System;
using System.Drawing;
using System.IO;
using InspectionWidget.Config;
using InspectionWidget.Storage;
using ConfigSaveResult = InspectionWidget.Config.SaveResult;
using StorageSaveResult = InspectionWidget.Storage.SaveResult;

namespace InspectionWidget.Tests
{
    /// <summary>
    /// v1.0.2 Task W5：日期文件夹名模式（FolderNamePattern）——格式化、校验、
    /// 配置默认值/自愈/往返、截图按模式落盘与非法模式回退。
    /// </summary>
    public static class FolderNamePatternTests
    {
        private static readonly DateTime Day = new DateTime(2026, 11, 3);

        public static void RunAll()
        {
            // ---------- 格式化 ----------
            TestRunner.Run("文件夹模式：Default 常量为 巡检-yyyy-MM-dd（v1.0.3 改定）", () =>
                Assert.Equal("巡检-yyyy-MM-dd", FolderNamePattern.Default));
            TestRunner.Run("文件夹模式：默认模式格式化（巡检-2026-11-03）", () =>
                Assert.Equal("巡检-2026-11-03", FolderNamePattern.Format(FolderNamePattern.Default, Day)));
            TestRunner.Run("文件夹模式：默认模式补零（2026-01-05）", () =>
                Assert.Equal("2026-01-05", FolderNamePattern.Format("yyyy-MM-dd", new DateTime(2026, 1, 5))));
            TestRunner.Run("文件夹模式：混排固定文字 巡检-yyyyMMdd", () =>
                Assert.Equal("巡检-20261103", FolderNamePattern.Format("巡检-yyyyMMdd", Day)));
            TestRunner.Run("文件夹模式：中文年月日 yyyy年MM月dd日", () =>
                Assert.Equal("2026年11月03日", FolderNamePattern.Format("yyyy年MM月dd日", Day)));
            TestRunner.Run("文件夹模式：部分占位 yyyy-MM", () =>
                Assert.Equal("2026-11", FolderNamePattern.Format("yyyy-MM", Day)));
            TestRunner.Run("文件夹模式：纯固定文字原样输出", () =>
                Assert.Equal("巡检照片", FolderNamePattern.Format("巡检照片", Day)));
            TestRunner.Run("文件夹模式：null/空模式按默认格式化", () =>
            {
                Assert.Equal("巡检-2026-11-03", FolderNamePattern.Format(null, Day));
                Assert.Equal("巡检-2026-11-03", FolderNamePattern.Format("", Day));
            });

            // ---------- 校验 ----------
            TestRunner.Run("文件夹模式校验：合法模式放行（默认/混排/年月日）", () =>
            {
                Assert.Null(FolderNamePattern.Validate("yyyy-MM-dd"));
                Assert.Null(FolderNamePattern.Validate("巡检-yyyyMMdd"));
                Assert.Null(FolderNamePattern.Validate("yyyy年MM月dd日"));
            });
            TestRunner.Run("文件夹模式校验：null/空/纯空白被拦", () =>
            {
                Assert.NotNull(FolderNamePattern.Validate(null));
                Assert.NotNull(FolderNamePattern.Validate(""));
                Assert.NotNull(FolderNamePattern.Validate("   "));
            });
            TestRunner.Run("文件夹模式校验：路径分隔符与非法字符逐个被拦", () =>
            {
                char[] bad = { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };
                for (int i = 0; i < bad.Length; i++)
                {
                    string pattern = "yyyy" + bad[i] + "MM";
                    Assert.NotNull(FolderNamePattern.Validate(pattern), "字符 " + bad[i] + " 应被拦");
                }
            });
            TestRunner.Run("文件夹模式校验：控制字符被拦", () =>
                Assert.NotNull(FolderNamePattern.Validate("yyyy\tMM")));
            TestRunner.Run("文件夹模式校验：结果为 . 或 .. 被拦", () =>
            {
                Assert.NotNull(FolderNamePattern.Validate("."));
                Assert.NotNull(FolderNamePattern.Validate(".."));
            });

            // ---------- 配置层 ----------
            TestRunner.Run("文件夹模式配置：CreateDefault 为 巡检-yyyy-MM-dd", () =>
                Assert.Equal("巡检-yyyy-MM-dd", AppConfig.CreateDefault().FolderNamePattern));
            TestRunner.Run("文件夹模式配置：往返保留自定义模式", () =>
            {
                string dir = ConfigStoreTests.MakeTempDir();
                try
                {
                    var cfg = AppConfig.CreateDefault();
                    cfg.FolderNamePattern = "巡检-yyyyMMdd";
                    ConfigSaveResult saved = ConfigStoreTests.MakeStore(dir).Save(cfg);
                    Assert.True(saved.Saved, saved.Error);
                    LoadResult r = ConfigStoreTests.MakeStore(dir).Load();
                    Assert.Equal(ConfigLoadKind.Ok, r.Kind);
                    Assert.Equal("巡检-yyyyMMdd", r.Config.FolderNamePattern);
                }
                finally { ConfigStoreTests.Cleanup(dir); }
            });
            TestRunner.Run("文件夹模式配置：手写 JSON 缺字段 → Defaulted 且补默认、已写字段保留", () =>
            {
                string dir = ConfigStoreTests.MakeTempDir();
                try
                {
                    File.WriteAllText(ConfigStoreTests.ConfigFile(dir),
                        "{\"RootPath\":\"D:/shots\",\"Devices\":[\"设备甲\"]}");
                    LoadResult r = ConfigStoreTests.MakeStore(dir).Load();
                    Assert.Equal(ConfigLoadKind.Defaulted, r.Kind);
                    Assert.Equal("巡检-yyyy-MM-dd", r.Config.FolderNamePattern);
                    Assert.Equal("设备甲", r.Config.Devices[0]);
                }
                finally { ConfigStoreTests.Cleanup(dir); }
            });
            TestRunner.Run("文件夹模式配置：手写 JSON 空串 → 自愈为默认", () =>
            {
                string dir = ConfigStoreTests.MakeTempDir();
                try
                {
                    File.WriteAllText(ConfigStoreTests.ConfigFile(dir),
                        "{\"RootPath\":\"D:/shots\",\"Devices\":[\"设备甲\"],\"FolderNamePattern\":\"\"}");
                    LoadResult r = ConfigStoreTests.MakeStore(dir).Load();
                    Assert.Equal("巡检-yyyy-MM-dd", r.Config.FolderNamePattern);
                }
                finally { ConfigStoreTests.Cleanup(dir); }
            });
            TestRunner.Run("文件夹模式配置：深拷贝保留模式", () =>
            {
                var source = AppConfig.CreateDefault();
                source.FolderNamePattern = "yyyy年MM月dd日";
                AppConfig copy = AppConfigCloner.Clone(source);
                Assert.Equal("yyyy年MM月dd日", copy.FolderNamePattern);
            });

            // ---------- 落盘 ----------
            TestRunner.Run("文件夹模式落盘：自定义模式决定日期文件夹名", () =>
            {
                string baseDir = Path.Combine(Path.GetTempPath(), "iw-fnp-" + Guid.NewGuid().ToString("N"));
                string root = Path.Combine(baseDir, "root");
                Directory.CreateDirectory(root);
                try
                {
                    var saver = new ScreenshotSaver(Path.Combine(baseDir, "exe"), Path.Combine(baseDir, "lad"));
                    using (var bmp = new Bitmap(8, 8))
                    {
                        StorageSaveResult r = saver.Save(bmp, root, "设备A", Day, "巡检-yyyyMMdd");
                        Assert.True(r.Saved, r.Error);
                        Assert.Equal(Path.Combine(root, "巡检-20261103", "设备A.png"), r.FullPath);
                        Assert.True(File.Exists(r.FullPath));
                    }
                }
                finally
                {
                    try { Directory.Delete(baseDir, true); } catch { }
                }
            });
            TestRunner.Run("文件夹模式落盘：非法模式回退默认日期目录（不写出根目录）", () =>
            {
                string baseDir = Path.Combine(Path.GetTempPath(), "iw-fnp-" + Guid.NewGuid().ToString("N"));
                string root = Path.Combine(baseDir, "root");
                Directory.CreateDirectory(root);
                try
                {
                    var saver = new ScreenshotSaver(Path.Combine(baseDir, "exe"), Path.Combine(baseDir, "lad"));
                    using (var bmp = new Bitmap(8, 8))
                    {
                        StorageSaveResult r = saver.Save(bmp, root, "设备A", Day, "../evil");
                        Assert.True(r.Saved, r.Error);
                        Assert.Equal(Path.Combine(root, "巡检-2026-11-03", "设备A.png"), r.FullPath);
                        Assert.False(Directory.Exists(Path.Combine(baseDir, "evil")));
                    }
                }
                finally
                {
                    try { Directory.Delete(baseDir, true); } catch { }
                }
            });
        }
    }
}
