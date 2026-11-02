using System;
using System.IO;
using System.Drawing;
using InspectionWidget.Storage;

namespace InspectionWidget.Tests
{
    /// <summary>可设当前时间的假时钟（Task 5 用例 4 跨天归属）。</summary>
    public sealed class FakeClock : IClock
    {
        public DateTime Now { get; set; }

        public FakeClock(DateTime now)
        {
            Now = now;
        }
    }

    /// <summary>
    /// ScreenshotSaver 保存与命名（临时目录真文件操作，测后清理）：
    /// 日期文件夹、顺延 + CreateNew、两级暂存链、空根目录短路、位图所有权。
    /// 不可写位置用「以普通文件占住路径」模拟：其下建子目录必然失败。
    /// </summary>
    public static class ScreenshotSaverTests
    {
        private static readonly DateTime Day1 = new DateTime(2026, 11, 2);
        private static readonly DateTime Day2 = new DateTime(2026, 11, 3);
        private const string Device = "设备A";

        /// <summary>一组隔离的临时目录：root / exe / lad 三个根，测后整组删除。</summary>
        private sealed class TempDirs : IDisposable
        {
            public string BaseDir;
            public string Root;
            public string Exe;
            public string Lad;

            public TempDirs()
            {
                BaseDir = Path.Combine(Path.GetTempPath(), "iw-saver-" + Guid.NewGuid().ToString("N"));
                Root = Path.Combine(BaseDir, "root");
                Exe = Path.Combine(BaseDir, "exe");
                Lad = Path.Combine(BaseDir, "lad");
                Directory.CreateDirectory(Root);
                Directory.CreateDirectory(Exe);
                Directory.CreateDirectory(Lad);
            }

            public void Dispose()
            {
                try
                {
                    if (Directory.Exists(BaseDir))
                        Directory.Delete(BaseDir, true);
                }
                catch
                {
                    // 清理失败不影响用例结论
                }
            }
        }

        private static Bitmap MakeBitmap()
        {
            var bmp = new Bitmap(4, 4);
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    bmp.SetPixel(x, y, Color.Red);
            return bmp;
        }

        /// <summary>以普通文件占住路径：返回该文件路径，其下任何子目录创建都会失败。</summary>
        private static string BlockWithFile(TempDirs dirs, string name)
        {
            string blocker = Path.Combine(dirs.BaseDir, name);
            File.WriteAllText(blocker, "block");
            return blocker;
        }

        public static void RunAll()
        {
            TestRunner.Run("保存：空目录 → 日期夹/设备A.png 存在且为真 PNG", () =>
            {
                using (var dirs = new TempDirs())
                using (Bitmap bmp = MakeBitmap())
                {
                    var saver = new ScreenshotSaver(dirs.Exe, dirs.Lad);
                    SaveResult result = saver.Save(bmp, dirs.Root, Device, Day1);

                    string expected = Path.Combine(dirs.Root, "2026-11-02", Device + ".png");
                    Assert.True(result.Saved, "应保存成功：" + result.Error);
                    Assert.False(result.UsedFallback);
                    Assert.Equal(expected, result.FullPath);
                    Assert.True(File.Exists(expected));
                    byte[] bytes = File.ReadAllBytes(expected);
                    Assert.True(bytes.Length > 0, "文件应非空");
                    Assert.Equal((byte)0x89, bytes[0], "PNG 签名首字节");
                    Assert.Equal((byte)'P', bytes[1], "PNG 签名次字节");
                }
            });

            TestRunner.Run("保存：已有设备A.png → 顺延 -1 且原文件不变", () =>
            {
                using (var dirs = new TempDirs())
                using (Bitmap bmp = MakeBitmap())
                {
                    string dateDir = Path.Combine(dirs.Root, "2026-11-02");
                    Directory.CreateDirectory(dateDir);
                    string existing = Path.Combine(dateDir, Device + ".png");
                    byte[] sentinel = new byte[] { 1, 2, 3, 4 };
                    File.WriteAllBytes(existing, sentinel);

                    var saver = new ScreenshotSaver(dirs.Exe, dirs.Lad);
                    SaveResult result = saver.Save(bmp, dirs.Root, Device, Day1);

                    string expected = Path.Combine(dateDir, Device + "-1.png");
                    Assert.True(result.Saved, "应保存成功：" + result.Error);
                    Assert.Equal(expected, result.FullPath);
                    Assert.True(File.Exists(expected));
                    byte[] after = File.ReadAllBytes(existing);
                    Assert.Equal(sentinel.Length, after.Length);
                    for (int i = 0; i < sentinel.Length; i++)
                        Assert.Equal(sentinel[i], after[i], "原文件内容不得被覆盖");
                }
            });

            TestRunner.Run("保存：已有 base、-1、-3 → 取最小未占用 -2", () =>
            {
                using (var dirs = new TempDirs())
                using (Bitmap bmp = MakeBitmap())
                {
                    string dateDir = Path.Combine(dirs.Root, "2026-11-02");
                    Directory.CreateDirectory(dateDir);
                    File.WriteAllBytes(Path.Combine(dateDir, Device + ".png"), new byte[] { 9 });
                    File.WriteAllBytes(Path.Combine(dateDir, Device + "-1.png"), new byte[] { 9 });
                    File.WriteAllBytes(Path.Combine(dateDir, Device + "-3.png"), new byte[] { 9 });

                    var saver = new ScreenshotSaver(dirs.Exe, dirs.Lad);
                    SaveResult result = saver.Save(bmp, dirs.Root, Device, Day1);

                    Assert.True(result.Saved, "应保存成功：" + result.Error);
                    Assert.Equal(Path.Combine(dateDir, Device + "-2.png"), result.FullPath);
                    Assert.True(File.Exists(result.FullPath));
                }
            });

            TestRunner.Run("保存：FakeClock 跨天 → 新日期夹从 base 名开始", () =>
            {
                using (var dirs = new TempDirs())
                using (Bitmap bmp = MakeBitmap())
                {
                    var clock = new FakeClock(Day1);
                    var saver = new ScreenshotSaver(dirs.Exe, dirs.Lad);

                    SaveResult first = saver.Save(bmp, dirs.Root, Device, clock.Now);
                    Assert.True(first.Saved, "第一天应保存成功：" + first.Error);
                    Assert.Equal(Path.Combine(dirs.Root, "2026-11-02", Device + ".png"), first.FullPath);

                    clock.Now = Day2;
                    SaveResult second = saver.Save(bmp, dirs.Root, Device, clock.Now);
                    Assert.True(second.Saved, "第二天应保存成功：" + second.Error);
                    Assert.Equal(Path.Combine(dirs.Root, "2026-11-03", Device + ".png"), second.FullPath);
                }
            });

            TestRunner.Run("保存：根目录不可写 → 落 exe 暂存且顺延规则一致", () =>
            {
                using (var dirs = new TempDirs())
                using (Bitmap bmp = MakeBitmap())
                {
                    string blockedRoot = BlockWithFile(dirs, "root-blocked");
                    // exe 暂存日期夹预置 base 名，验证暂存级同样顺延
                    string fallbackDir = Path.Combine(dirs.Exe, "FallbackScreenshots", "2026-11-02");
                    Directory.CreateDirectory(fallbackDir);
                    File.WriteAllBytes(Path.Combine(fallbackDir, Device + ".png"), new byte[] { 7 });

                    var saver = new ScreenshotSaver(dirs.Exe, dirs.Lad);
                    SaveResult result = saver.Save(bmp, blockedRoot, Device, Day1);

                    Assert.True(result.Saved, "应落暂存成功：" + result.Error);
                    Assert.True(result.UsedFallback);
                    Assert.Equal(Path.Combine(fallbackDir, Device + "-1.png"), result.FullPath);
                    Assert.True(File.Exists(result.FullPath));
                }
            });

            TestRunner.Run("保存：exe 暂存也不可写 → 落 LocalAppData 三级暂存", () =>
            {
                using (var dirs = new TempDirs())
                using (Bitmap bmp = MakeBitmap())
                {
                    string blockedRoot = BlockWithFile(dirs, "root-blocked");
                    string blockedExe = BlockWithFile(dirs, "exe-blocked");

                    var saver = new ScreenshotSaver(blockedExe, dirs.Lad);
                    SaveResult result = saver.Save(bmp, blockedRoot, Device, Day1);

                    string expected = Path.Combine(dirs.Lad, "InspectionWidget", "FallbackScreenshots", "2026-11-02", Device + ".png");
                    Assert.True(result.Saved, "应落三级暂存成功：" + result.Error);
                    Assert.True(result.UsedFallback);
                    Assert.Equal(expected, result.FullPath);
                    Assert.True(File.Exists(expected));
                }
            });

            TestRunner.Run("保存：三级全不可写 → Saved=false、含「尚未保存」、Bitmap 不被释放", () =>
            {
                using (var dirs = new TempDirs())
                {
                    Bitmap bmp = MakeBitmap();
                    try
                    {
                        string blockedRoot = BlockWithFile(dirs, "root-blocked");
                        string blockedExe = BlockWithFile(dirs, "exe-blocked");
                        string blockedLad = BlockWithFile(dirs, "lad-blocked");

                        var saver = new ScreenshotSaver(blockedExe, blockedLad);
                        SaveResult result = saver.Save(bmp, blockedRoot, Device, Day1);

                        Assert.False(result.Saved);
                        Assert.NotNull(result.Error);
                        Assert.True(result.Error.IndexOf("尚未保存", StringComparison.Ordinal) >= 0,
                            "Error 应含「尚未保存」，实际：" + result.Error);
                        // 位图所有权在调用方：保存失败后仍可读像素，不得抛 ObjectDisposedException
                        Color pixel = bmp.GetPixel(0, 0);
                        Assert.Equal(Color.Red.ToArgb(), pixel.ToArgb());
                    }
                    finally
                    {
                        bmp.Dispose();
                    }
                }
            });

            TestRunner.Run("保存：根目录为空/null → 直接失败且不产生任何暂存文件", () =>
            {
                using (var dirs = new TempDirs())
                using (Bitmap bmp = MakeBitmap())
                {
                    var saver = new ScreenshotSaver(dirs.Exe, dirs.Lad);

                    SaveResult empty = saver.Save(bmp, string.Empty, Device, Day1);
                    Assert.False(empty.Saved);
                    Assert.NotNull(empty.Error);
                    Assert.True(empty.Error.IndexOf("未设置保存根目录", StringComparison.Ordinal) >= 0,
                        "Error 应含「未设置保存根目录」，实际：" + empty.Error);

                    SaveResult nullRoot = saver.Save(bmp, null, Device, Day1);
                    Assert.False(nullRoot.Saved);

                    Assert.False(Directory.Exists(Path.Combine(dirs.Exe, "FallbackScreenshots")),
                        "空根目录不得进暂存（exe 级）");
                    Assert.False(Directory.Exists(Path.Combine(dirs.Lad, "InspectionWidget")),
                        "空根目录不得进暂存（LocalAppData 级）");
                }
            });

            TestRunner.Run("保存：主目录候选路径超 259 → 该级失败进暂存、落 exe 暂存", () =>
            {
                using (var dirs = new TempDirs())
                using (Bitmap bmp = MakeBitmap())
                {
                    // 自适应构造深根目录：按实际拼出的候选完整路径长度逐层加深，
                    // 直到主目录候选（深根/日期/设备名.png）超过 259 上限为止——
                    // 不写死层数，避免测试机临时路径基准长度不同导致构造失效。
                    // 深根目录本身不需真实存在：长度校验在建目录之前就会判该级失败。
                    string deepRoot = dirs.Root;
                    string segment = new string('d', 24);
                    string primaryCandidate = Path.Combine(deepRoot, "2026-11-02", Device + ".png");
                    while (primaryCandidate.Length <= 259)
                    {
                        deepRoot = Path.Combine(deepRoot, segment);
                        primaryCandidate = Path.Combine(deepRoot, "2026-11-02", Device + ".png");
                    }
                    Assert.True(primaryCandidate.Length > 259,
                        "构造前提：主目录候选路径应超 259，实际 " + primaryCandidate.Length);

                    var saver = new ScreenshotSaver(dirs.Exe, dirs.Lad);
                    SaveResult result = saver.Save(bmp, deepRoot, Device, Day1);

                    string expected = Path.Combine(dirs.Exe, "FallbackScreenshots", "2026-11-02", Device + ".png");
                    Assert.True(result.Saved, "主目录超限应进暂存并成功：" + result.Error);
                    Assert.True(result.UsedFallback);
                    Assert.Equal(expected, result.FullPath);
                    Assert.True(File.Exists(expected));
                }
            });

            TestRunner.Run("AllocatePath：CreateNew 占位顺延，第二次取 -1", () =>
            {
                using (var dirs = new TempDirs())
                {
                    var saver = new ScreenshotSaver(dirs.Exe, dirs.Lad);
                    string first = saver.AllocatePath(dirs.Root, Device);
                    Assert.Equal(Path.Combine(dirs.Root, Device + ".png"), first);
                    Assert.True(File.Exists(first), "Allocate 应以 CreateNew 真占位");

                    string second = saver.AllocatePath(dirs.Root, Device);
                    Assert.Equal(Path.Combine(dirs.Root, Device + "-1.png"), second);
                    Assert.True(File.Exists(second));

                    // 简报别名与主名同行为
                    string third = saver.AllocatePathForTest(dirs.Root, Device);
                    Assert.Equal(Path.Combine(dirs.Root, Device + "-2.png"), third);
                }
            });
        }
    }
}
