using System;
using System.IO;
using InspectionWidget;

namespace InspectionWidget.Tests
{
    /// <summary>
    /// ErrorLog（Task 8）：目录注入点指向临时目录验证落盘与回退；
    /// 不可写场景用「已存在的文件当父目录」稳定构造（跨平台一致失败）。
    /// 每条用例测后 Reset 注入并清理临时目录。
    /// </summary>
    public static class ErrorLogTests
    {
        public static void RunAll()
        {
            TestRunner.Run("错误日志：写入主目录 → 文件存在且含环节名与异常类型、CurrentLogPath 指向主路径", () =>
            {
                string primary = MakeTempDir();
                string fallback = MakeTempDir();
                try
                {
                    ErrorLog.SetDirectoriesForTests(primary, fallback);
                    ErrorLog.Write("UI线程", new InvalidOperationException("测试异常甲"));

                    string path = Path.Combine(primary, "error.log");
                    Assert.True(File.Exists(path), "error.log 应落在主目录");
                    string text = File.ReadAllText(path);
                    Assert.True(text.Contains("UI线程"), "日志应含环节名");
                    Assert.True(text.Contains("InvalidOperationException"), "日志应含异常类型");
                    Assert.True(text.Contains("测试异常甲"), "日志应含异常消息");
                    Assert.Equal(path, ErrorLog.CurrentLogPath);
                    Assert.False(File.Exists(Path.Combine(fallback, "error.log")), "主目录可写时不应写回退目录");
                }
                finally
                {
                    ErrorLog.ResetDirectoriesForTests();
                    Cleanup(primary);
                    Cleanup(fallback);
                }
            });

            TestRunner.Run("错误日志：追加写 → 两条记录都在同一文件", () =>
            {
                string primary = MakeTempDir();
                string fallback = MakeTempDir();
                try
                {
                    ErrorLog.SetDirectoriesForTests(primary, fallback);
                    ErrorLog.Write("UI线程", new InvalidOperationException("第一条"));
                    ErrorLog.Write("非UI线程", new ApplicationException("第二条"));

                    string text = File.ReadAllText(Path.Combine(primary, "error.log"));
                    Assert.True(text.Contains("第一条"));
                    Assert.True(text.Contains("第二条"));
                    Assert.True(text.Contains("ApplicationException"));
                }
                finally
                {
                    ErrorLog.ResetDirectoriesForTests();
                    Cleanup(primary);
                    Cleanup(fallback);
                }
            });

            TestRunner.Run("错误日志：主目录不可写 → 回退目录落盘且 CurrentLogPath 指向回退路径", () =>
            {
                string sandbox = MakeTempDir();
                string fallback = MakeTempDir();
                try
                {
                    // 拿一个真实文件当父目录：其下任何建目录/写文件都必然失败
                    string blocker = Path.Combine(sandbox, "blocker");
                    File.WriteAllText(blocker, "occupied");
                    string badPrimary = Path.Combine(blocker, "sub");

                    ErrorLog.SetDirectoriesForTests(badPrimary, fallback);
                    ErrorLog.Write("非UI线程", new InvalidOperationException("走回退"));

                    string fallbackPath = Path.Combine(fallback, "error.log");
                    Assert.True(File.Exists(fallbackPath), "主目录不可写时应落到回退目录");
                    Assert.True(File.ReadAllText(fallbackPath).Contains("走回退"));
                    Assert.Equal(fallbackPath, ErrorLog.CurrentLogPath);
                }
                finally
                {
                    ErrorLog.ResetDirectoriesForTests();
                    Cleanup(sandbox);
                    Cleanup(fallback);
                }
            });

            TestRunner.Run("错误日志：主/回退目录均不可写 → Write 不抛异常（含空异常对象）", () =>
            {
                string sandbox = MakeTempDir();
                try
                {
                    string blocker = Path.Combine(sandbox, "blocker");
                    File.WriteAllText(blocker, "occupied");
                    string badA = Path.Combine(blocker, "a");
                    string badB = Path.Combine(blocker, "b");

                    ErrorLog.SetDirectoriesForTests(badA, badB);
                    // 不抛异常即通过（TestRunner 会把任何逃逸异常记为用例失败）
                    ErrorLog.Write("UI线程", new InvalidOperationException("无处可写"));
                    ErrorLog.Write("UI线程", null);
                    ErrorLog.Write(null, new Exception("环节名为空也不抛"));
                }
                finally
                {
                    ErrorLog.ResetDirectoriesForTests();
                    Cleanup(sandbox);
                }
            });
        }

        private static string MakeTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "iw-errlog-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void Cleanup(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
            catch
            {
                // 临时目录清理失败不影响用例结论
            }
        }
    }
}
