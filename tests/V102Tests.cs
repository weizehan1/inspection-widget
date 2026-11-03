using System;
using System.Drawing;
using System.IO;
using InspectionWidget.Capture;
using InspectionWidget.Config;

namespace InspectionWidget.Tests
{
    /// <summary>
    /// v1.0.2：标题栏三按钮槽位（W2）、齿轮位图兜底（W4）、嵌入图标（W1）、
    /// 托盘气泡一次性标记 TrayHintShown 的配置三态（W6）。
    /// </summary>
    public static class V102Tests
    {
        public static void RunAll()
        {
            // ---------- W2：标题栏按钮槽位 ----------
            TestRunner.Run("标题栏按钮：边长常量 28", () =>
                Assert.Equal(28, PanelLayout.TitleBarButtonSize));
            TestRunner.Run("标题栏按钮：230 宽三槽坐标（右起 196/164/132）", () =>
            {
                Assert.Equal(196, PanelLayout.TitleBarButtonX(230, 0, 1.0));
                Assert.Equal(164, PanelLayout.TitleBarButtonX(230, 1, 1.0));
                Assert.Equal(132, PanelLayout.TitleBarButtonX(230, 2, 1.0));
            });
            TestRunner.Run("标题栏按钮：2.0 档 460 宽三槽坐标（392/328/264）", () =>
            {
                Assert.Equal(392, PanelLayout.TitleBarButtonX(460, 0, 2.0));
                Assert.Equal(328, PanelLayout.TitleBarButtonX(460, 1, 2.0));
                Assert.Equal(264, PanelLayout.TitleBarButtonX(460, 2, 2.0));
            });
            TestRunner.Run("标题栏按钮：任意档位槽间不重叠且不越右界", () =>
            {
                double[] factors = { 1.0, 1.25, 1.5, 1.75, 2.0 };
                for (int i = 0; i < factors.Length; i++)
                {
                    double f = factors[i];
                    int w = PanelLayout.ScaledPanelWidth(f);
                    int size = PanelLayout.Scale(PanelLayout.TitleBarButtonSize, f);
                    int x0 = PanelLayout.TitleBarButtonX(w, 0, f);
                    int x1 = PanelLayout.TitleBarButtonX(w, 1, f);
                    int x2 = PanelLayout.TitleBarButtonX(w, 2, f);
                    Assert.True(x0 + size <= w, "右界 factor=" + f);
                    Assert.True(x2 + size <= x1, "槽2 与槽1 不重叠 factor=" + f);
                    Assert.True(x1 + size <= x0, "槽1 与槽0 不重叠 factor=" + f);
                    Assert.True(x2 >= 0, "不越左界 factor=" + f);
                }
            });

            // ---------- W4：齿轮位图兜底 ----------
            TestRunner.Run("齿轮位图：尺寸正确、中心孔与边角透明、齿与轮体不透明", () =>
            {
                using (Bitmap bmp = GearArt.DrawGear(48, Color.White))
                {
                    Assert.NotNull(bmp);
                    Assert.Equal(48, bmp.Width);
                    Assert.Equal(48, bmp.Height);
                    Assert.Equal(0, bmp.GetPixel(24, 24).A, "中心孔应透明");
                    Assert.Equal(0, bmp.GetPixel(1, 1).A, "边角应透明");
                    Assert.True(bmp.GetPixel(41, 24).A > 200, "0° 齿部应不透明");
                    Assert.True(bmp.GetPixel(34, 34).A > 200, "45° 轮体应不透明");
                }
            });
            TestRunner.Run("齿轮位图：小尺寸（18）可绘制且中心孔透明", () =>
            {
                using (Bitmap bmp = GearArt.DrawGear(18, Color.White))
                {
                    Assert.Equal(18, bmp.Width);
                    Assert.Equal(0, bmp.GetPixel(9, 9).A);
                }
            });

            // ---------- W1：嵌入图标 ----------
            TestRunner.Run("应用图标：主程序集清单资源含 .ico", () =>
            {
                string[] names = typeof(AppConfig).Assembly.GetManifestResourceNames();
                bool found = false;
                for (int i = 0; i < names.Length; i++)
                {
                    if (names[i].EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                        found = true;
                }
                Assert.True(found, "资源清单：" + string.Join(",", names));
            });
            TestRunner.Run("应用图标：AppIcon.Load 可加载且尺寸有效", () =>
            {
                using (Icon icon = AppIcon.Load())
                {
                    Assert.NotNull(icon);
                    Assert.True(icon.Width >= 16, "图标宽 " + (icon == null ? -1 : icon.Width));
                    Assert.True(icon.Height >= 16);
                }
            });

            // ---------- W6：TrayHintShown 配置三态 ----------
            TestRunner.Run("托盘标记：CreateDefault 为 false", () =>
                Assert.False(AppConfig.CreateDefault().TrayHintShown));
            TestRunner.Run("托盘标记：深拷贝保留 true", () =>
            {
                var source = AppConfig.CreateDefault();
                source.TrayHintShown = true;
                Assert.True(AppConfigCloner.Clone(source).TrayHintShown);
            });
            TestRunner.Run("托盘标记：往返保留 true 且 Kind=Ok", () =>
            {
                string dir = ConfigStoreTests.MakeTempDir();
                try
                {
                    var cfg = AppConfig.CreateDefault();
                    cfg.TrayHintShown = true;
                    SaveResult saved = ConfigStoreTests.MakeStore(dir).Save(cfg);
                    Assert.True(saved.Saved, saved.Error);
                    LoadResult r = ConfigStoreTests.MakeStore(dir).Load();
                    Assert.Equal(ConfigLoadKind.Ok, r.Kind);
                    Assert.True(r.Config.TrayHintShown);
                }
                finally { ConfigStoreTests.Cleanup(dir); }
            });
            TestRunner.Run("托盘标记：手写 JSON 缺字段 → Defaulted 且为 false", () =>
            {
                string dir = ConfigStoreTests.MakeTempDir();
                try
                {
                    File.WriteAllText(ConfigStoreTests.ConfigFile(dir),
                        "{\"RootPath\":\"D:/shots\",\"Devices\":[\"设备甲\"]}");
                    LoadResult r = ConfigStoreTests.MakeStore(dir).Load();
                    Assert.Equal(ConfigLoadKind.Defaulted, r.Kind);
                    Assert.False(r.Config.TrayHintShown);
                }
                finally { ConfigStoreTests.Cleanup(dir); }
            });
        }
    }
}
