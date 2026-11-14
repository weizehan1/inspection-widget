using System;
using System.Collections.Generic;
using System.IO;
using InspectionWidget.Storage;

namespace InspectionWidget.Tests
{
    /// <summary>巡检目录导航；存在性判断与打开动作全部注入 fake，不启动资源管理器。</summary>
    public static class InspectionFolderNavigatorTests
    {
        private sealed class Fs
        {
            public readonly HashSet<string> Existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly List<string> Checked = new List<string>();
            public readonly List<string> Opened = new List<string>();
            public bool ThrowOnOpen;

            public bool Exists(string dir)
            {
                Checked.Add(dir);
                return Existing.Contains(dir);
            }

            public void Open(string dir)
            {
                if (ThrowOnOpen) throw new InvalidOperationException("资源管理器未响应");
                Opened.Add(dir);
            }

            public InspectionFolderNavigator Navigator()
            {
                return new InspectionFolderNavigator(Exists, Open);
            }
        }

        private static readonly DateTime Day = new DateTime(2026, 11, 14);
        private const string Root = @"D:\巡检 根目录";
        private static string Today { get { return Path.Combine(Root, "巡检-2026-11-14"); } }

        public static void RunAll()
        {
            TestRunner.Run("导航：无 savedPaths → 退化到已存在的配置日期目录", () =>
            {
                var fs = new Fs();
                fs.Existing.Add(Today);
                List<string> folders = fs.Navigator().ResolveFolders(Root, "巡检-yyyy-MM-dd", Day, null);
                Assert.Equal(1, folders.Count);
                Assert.Equal(Today, folders[0]);
                Assert.True(fs.Checked.Contains(Today));
            });

            TestRunner.Run("导航：空 savedPaths 且配置日期目录不存在 → 空列表", () =>
            {
                var fs = new Fs();
                Assert.Equal(0, fs.Navigator().ResolveFolders(
                    Root, "巡检-yyyy-MM-dd", Day, new List<string>()).Count);
                Assert.Equal(0, fs.Navigator().ResolveFolders(
                    null, "巡检-yyyy-MM-dd", Day, null).Count);
            });

            TestRunner.Run("导航：全部暂存时只列实际落盘目录，配置日期目录无该轮截图不列入", () =>
            {
                var fs = new Fs();
                string fallback = @"D:\暂存 A\巡检-2026-11-14";
                string fallback2 = @"D:\本地 缓存\巡检-2026-11-14";
                fs.Existing.Add(fallback);
                fs.Existing.Add(fallback2);
                fs.Existing.Add(Today);
                var saved = new List<string> {
                    Path.Combine(fallback, "设备 甲.png"),
                    Path.Combine(fallback2, "设备 乙.png")
                };
                List<string> folders = fs.Navigator().ResolveFolders(Root, "巡检-yyyy-MM-dd", Day, saved);
                Assert.Equal(2, folders.Count);
                Assert.True(folders.Contains(fallback));
                Assert.True(folders.Contains(fallback2));
                Assert.False(folders.Contains(Today));
            });

            TestRunner.Run("导航：部分暂存多目录，配置当日目录有该轮截图时排首", () =>
            {
                var fs = new Fs();
                string fallback = @"D:\暂存 A\巡检-2026-11-14";
                fs.Existing.Add(fallback);
                fs.Existing.Add(Today);
                var saved = new List<string> {
                    Path.Combine(fallback, "设备 甲.png"),
                    Path.Combine(Today, "设备 乙.png")
                };
                List<string> folders = fs.Navigator().ResolveFolders(Root, "巡检-yyyy-MM-dd", Day, saved);
                Assert.Equal(2, folders.Count);
                Assert.Equal(Today, folders[0]);
                Assert.Equal(fallback, folders[1]);
            });

            TestRunner.Run("导航：同目录多条去重，不存在目录被过滤", () =>
            {
                var fs = new Fs();
                string dir = @"D:\巡检 根目录\巡检-2026-11-14";
                string missing = @"D:\不存在的盘\巡检-2026-11-14";
                fs.Existing.Add(dir);
                var saved = new List<string> {
                    dir + "\\设备 甲.png", dir + "\\设备 乙.png",
                    missing + "\\设备 丙.png"
                };
                List<string> folders = fs.Navigator().ResolveFolders(Root, "巡检-yyyy-MM-dd", Day, saved);
                Assert.Equal(1, folders.Count);
                Assert.Equal(dir, folders[0]);
            });

            TestRunner.Run("导航：路径带空格与中文、Windows 分隔符正确取所属目录", () =>
            {
                var fs = new Fs();
                string dir = @"D:\巡检 根目录\中文 子目录\巡检-2026-11-14";
                fs.Existing.Add(dir);
                var saved = new List<string> { dir + "\\设备 甲 一号.png" };
                List<string> folders = fs.Navigator().ResolveFolders(Root, "yyyy年MM月dd日", Day, saved);
                Assert.Equal(1, folders.Count);
                Assert.Equal(dir, folders[0]);
            });

            TestRunner.Run("导航：非法日期模式回退默认且不创建任何目录", () =>
            {
                string parent = Path.Combine(Path.GetTempPath(), "iw-nav-" + Guid.NewGuid().ToString("N"));
                try
                {
                    var fs = new Fs();
                    List<string> folders = fs.Navigator().ResolveFolders(
                        parent, "..\\越界", Day, null);
                    Assert.Equal(0, folders.Count);
                    Assert.False(Directory.Exists(parent));
                    Assert.False(Directory.Exists(Path.Combine(parent, "巡检-2026-11-14")));
                }
                finally
                {
                    try { if (Directory.Exists(parent)) Directory.Delete(parent, true); }
                    catch { }
                }
            });

            TestRunner.Run("打开目录：存在 → true、error 为 null、fake 收到原路径", () =>
            {
                var fs = new Fs();
                fs.Existing.Add(@"D:\巡检 根目录\巡检-2026-11-14");
                string error;
                Assert.True(fs.Navigator().TryOpenFolder(@"D:\巡检 根目录\巡检-2026-11-14", out error));
                Assert.Null(error);
                Assert.Equal(1, fs.Opened.Count);
                Assert.Equal(@"D:\巡检 根目录\巡检-2026-11-14", fs.Opened[0]);
            });

            TestRunner.Run("打开目录：不存在 → false、可读错误、fake 未调用且不创建目录", () =>
            {
                var fs = new Fs();
                string missing = @"D:\巡检 根目录\不存在的日期";
                string error;
                Assert.False(fs.Navigator().TryOpenFolder(missing, out error));
                Assert.NotNull(error);
                Assert.True(error.Contains("不存在"), error);
                Assert.Equal(0, fs.Opened.Count);
                Assert.False(Directory.Exists(missing));
            });

            TestRunner.Run("打开目录：启动失败 → false、可读错误保留异常原因", () =>
            {
                var fs = new Fs { ThrowOnOpen = true };
                string dir = @"D:\巡检 根目录\巡检-2026-11-14";
                fs.Existing.Add(dir);
                string error;
                Assert.False(fs.Navigator().TryOpenFolder(dir, out error));
                Assert.NotNull(error);
                Assert.True(error.Contains("资源管理器未响应"), error);
            });

            TestRunner.Run("打开目录：空路径 → false、可读错误", () =>
            {
                var fs = new Fs();
                string error;
                Assert.False(fs.Navigator().TryOpenFolder(null, out error));
                Assert.NotNull(error);
                Assert.False(fs.Navigator().TryOpenFolder("   ", out error));
                Assert.NotNull(error);
                Assert.Equal(0, fs.Opened.Count);
            });

            TestRunner.Run("导航：生产构造可用（构造不打开目录）", () =>
                Assert.NotNull(new InspectionFolderNavigator()));
        }
    }
}
