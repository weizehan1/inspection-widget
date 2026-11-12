using System;
using System.IO;
using System.Text;
using InspectionWidget.Config;

namespace InspectionWidget.Tests
{
    /// <summary>同目录临时文件与替换/移动语义；失败时旧目标保持完整。</summary>
    public static class AtomicConfigWriterTests
    {
        private sealed class FakeOperations : AtomicConfigWriter.IFileOperations
        {
            public bool ExistsResult;
            public bool FailReplace;
            public bool FailMove;
            public int ReplaceCalls;
            public int MoveCalls;

            public bool Exists(string path)
            {
                return ExistsResult;
            }

            public void Replace(string source, string destination)
            {
                ReplaceCalls++;
                if (FailReplace) throw new IOException("模拟替换失败");
                File.Copy(source, destination, true);
            }

            public void Move(string source, string destination)
            {
                MoveCalls++;
                if (FailMove) throw new IOException("模拟移动失败");
                File.Move(source, destination);
            }
        }

        public static void RunAll()
        {
            TestRunner.Run("原子写：目标不存在时同目录移动并清理临时文件", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    string target = Path.Combine(dir, "new.json");
                    byte[] text = Encoding.UTF8.GetBytes("{\"ok\":true}");
                    var result = new AtomicConfigWriter().WriteAllBytes(target, text);
                    Assert.True(result.Written, result.Error);
                    Assert.True(File.Exists(target));
                    Assert.Equal(Convert.ToBase64String(text),
                        Convert.ToBase64String(File.ReadAllBytes(target)));
                    Assert.Equal(0, Directory.GetFiles(dir, "*.tmp").Length);
                }
                finally { Cleanup(dir); }
            });

            TestRunner.Run("原子写：已有目标使用替换而非覆盖复制", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    string target = Path.Combine(dir, "existing.json");
                    File.WriteAllText(target, "old");
                    var result = new AtomicConfigWriter().WriteAllBytes(
                        target, Encoding.UTF8.GetBytes("new"));
                    Assert.True(result.Written, result.Error);
                    Assert.Equal("new", File.ReadAllText(target));
                    Assert.Equal(0, Directory.GetFiles(dir, "*.tmp").Length);
                }
                finally { Cleanup(dir); }
            });

            TestRunner.Run("原子写：替换失败保留旧目标且绝不回退移动覆盖", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    string target = Path.Combine(dir, "existing.json");
                    File.WriteAllText(target, "old");
                    var fake = new FakeOperations { ExistsResult = true, FailReplace = true };
                    AtomicConfigWriter writer = AtomicConfigWriter.ForTests(fake);
                    var result = writer.WriteAllBytes(target, Encoding.UTF8.GetBytes("new"));
                    Assert.False(result.Written);
                    Assert.True(result.Error.Contains("模拟替换失败"), result.Error);
                    Assert.Equal(1, fake.ReplaceCalls);
                    Assert.Equal(0, fake.MoveCalls);
                    Assert.Equal("old", File.ReadAllText(target));
                    Assert.Equal(0, Directory.GetFiles(dir, "*.tmp").Length);
                }
                finally { Cleanup(dir); }
            });

            TestRunner.Run("原子写：目标不存在时移动失败不留半文件", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    string target = Path.Combine(dir, "new.json");
                    var fake = new FakeOperations { FailMove = true };
                    AtomicConfigWriter writer = AtomicConfigWriter.ForTests(fake);
                    var result = writer.WriteAllBytes(target, Encoding.UTF8.GetBytes("new"));
                    Assert.False(result.Written);
                    Assert.False(File.Exists(target));
                    Assert.Equal(1, fake.MoveCalls);
                    Assert.Equal(0, Directory.GetFiles(dir, "*.tmp").Length);
                }
                finally { Cleanup(dir); }
            });

            TestRunner.Run("原子写：写入回调抛异常不改旧目标并清理临时文件", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    string target = Path.Combine(dir, "existing.json");
                    File.WriteAllText(target, "old");
                    var result = new AtomicConfigWriter().Write(target,
                        delegate(Stream stream) { throw new InvalidOperationException("模拟序列化失败"); });
                    Assert.False(result.Written);
                    Assert.True(result.Error.Contains("模拟序列化失败"), result.Error);
                    Assert.Equal("old", File.ReadAllText(target));
                    Assert.Equal(0, Directory.GetFiles(dir, "*.tmp").Length);
                }
                finally { Cleanup(dir); }
            });

            TestRunner.Run("原子写：空参数与不可准备目录返回失败而不抛异常", () =>
            {
                var writer = new AtomicConfigWriter();
                Assert.False(writer.WriteAllBytes(null, new byte[0]).Written);
                Assert.False(writer.WriteAllBytes("x", null).Written);
                Assert.False(writer.Write(null, delegate { }).Written);
                string blocked = Path.Combine(Path.GetTempPath(),
                    "iw-file-as-dir-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(blocked, "file");
                try
                {
                    var result = writer.WriteAllBytes(
                        Path.Combine(blocked, "child.json"), new byte[] { 1 });
                    Assert.False(result.Written);
                    Assert.NotNull(result.Error);
                }
                finally
                {
                    try { File.Delete(blocked); } catch { }
                }
            });

            TestRunner.Run("原子写：自动创建目标目录并保留 UTF-8 字节", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    string target = Path.Combine(dir, "a", "b", "文件.json");
                    byte[] bytes = new UTF8Encoding(false).GetBytes("中文\n");
                    var result = new AtomicConfigWriter().WriteAllBytes(target, bytes);
                    Assert.True(result.Written, result.Error);
                    Assert.Equal(Convert.ToBase64String(bytes),
                        Convert.ToBase64String(File.ReadAllBytes(target)));
                }
                finally { Cleanup(dir); }
            });
        }

        private static string MakeTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(),
                "iw-atomic-tests-" + Guid.NewGuid().ToString("N"));
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
