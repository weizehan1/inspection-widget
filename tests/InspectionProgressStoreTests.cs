using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using InspectionWidget.Planning;

namespace InspectionWidget.Tests
{
    /// <summary>
    /// 每日进度快照的独立文件存储：首次缺失返回空快照、字段往返、
    /// 原子替换与临时文件清理、坏数据/不可写路径如实失败。
    /// </summary>
    public static class InspectionProgressStoreTests
    {
        private static readonly DateTime Day1 = new DateTime(2026, 11, 14, 9, 30, 0);

        /// <summary>已存在目标、替换必失败的文件操作，用于验证同一路径的原子失败保护。</summary>
        private sealed class FailingReplaceOperations
            : InspectionWidget.Config.AtomicConfigWriter.IFileOperations
        {
            public int ReplaceCalls;
            public int MoveCalls;

            public bool Exists(string path)
            {
                return true;
            }

            public void Replace(string source, string destination)
            {
                ReplaceCalls++;
                throw new IOException("模拟替换失败");
            }

            public void Move(string source, string destination)
            {
                MoveCalls++;
                File.Move(source, destination);
            }
        }

        public static void RunAll()
        {
            TestRunner.Run("进度存储：首次文件不存在返回空快照且无错误", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    var store = new InspectionProgressStore(Path.Combine(dir, "progress.json"));
                    InspectionProgressSnapshot snapshot;
                    string error;
                    Assert.True(store.TryLoad(out snapshot, out error));
                    Assert.Null(snapshot);
                    Assert.Null(error);
                }
                finally { Cleanup(dir); }
            });

            TestRunner.Run("进度存储：保存后字段完整往返（含中文设备名与保存路径）", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    string path = Path.Combine(dir, "nested", "progress.json");
                    var store = new InspectionProgressStore(path);
                    InspectionProgressSnapshot source = MakeSnapshot();
                    string error;
                    Assert.True(store.TrySave(source, out error), error);
                    Assert.True(File.Exists(path));

                    InspectionProgressSnapshot loaded;
                    Assert.True(store.TryLoad(out loaded, out error), error);
                    Assert.Equal(source.SchemaVersion, loaded.SchemaVersion);
                    Assert.Equal(source.Date, loaded.Date);
                    Assert.Equal(source.RoundId, loaded.RoundId);
                    Assert.Equal(string.Join("|", source.Devices), string.Join("|", loaded.Devices));
                    Assert.Equal(string.Join("|", source.VisitedDevices),
                        string.Join("|", loaded.VisitedDevices));
                    Assert.True(loaded.CompletionPrompted);
                    Assert.False(loaded.CompletionPending);
                    Assert.Equal(string.Join("|", source.SavedPaths),
                        string.Join("|", loaded.SavedPaths));
                    Assert.Equal("设备甲", loaded.Devices[0]);
                }
                finally { Cleanup(dir); }
            });

            TestRunner.Run("进度存储：覆盖保存原子替换且不残留临时文件", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    string path = Path.Combine(dir, "progress.json");
                    var store = new InspectionProgressStore(path);
                    string error;
                    InspectionProgressSnapshot first = MakeSnapshot();
                    Assert.True(store.TrySave(first, out error), error);

                    InspectionProgressSnapshot second = MakeSnapshot();
                    second.VisitedDevices = new List<string> { "设备甲", "设备乙" };
                    second.CompletionPending = true;
                    Assert.True(store.TrySave(second, out error), error);

                    Assert.Equal(0, Directory.GetFiles(dir, "*.tmp").Length);
                    InspectionProgressSnapshot loaded;
                    Assert.True(store.TryLoad(out loaded, out error), error);
                    Assert.Equal(2, loaded.VisitedDevices.Count);
                    Assert.True(loaded.CompletionPending);
                }
                finally { Cleanup(dir); }
            });

            TestRunner.Run("进度存储：坏 JSON 与空文件如实失败，不使用默认快照冒充", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    string path = Path.Combine(dir, "progress.json");
                    var store = new InspectionProgressStore(path);

                    File.WriteAllText(path, "{ 不是 JSON ", new UTF8Encoding(false));
                    InspectionProgressSnapshot snapshot;
                    string error;
                    Assert.False(store.TryLoad(out snapshot, out error));
                    Assert.Null(snapshot);
                    Assert.NotNull(error);
                    Assert.True(error.Contains("巡检进度"), error);

                    File.WriteAllBytes(path, new byte[0]);
                    Assert.False(store.TryLoad(out snapshot, out error));
                    Assert.NotNull(error);

                    File.WriteAllText(path, "null", new UTF8Encoding(false));
                    Assert.False(store.TryLoad(out snapshot, out error));
                    Assert.NotNull(error);
                }
                finally { Cleanup(dir); }
            });

            TestRunner.Run("进度存储：字段缺失的合法 JSON 如实取回，由计划层判定坏数据", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    string path = Path.Combine(dir, "progress.json");
                    var store = new InspectionProgressStore(path);
                    File.WriteAllText(path, "{\"schemaVersion\":1}", new UTF8Encoding(false));
                    InspectionProgressSnapshot snapshot;
                    string error;
                    Assert.True(store.TryLoad(out snapshot, out error));
                    Assert.NotNull(snapshot);
                    Assert.Equal(1, snapshot.SchemaVersion);
                    // 缺日期/轮次属于坏数据，由 InspectionPlan.Restore 拒绝，存储层只负责如实取回。
                    Assert.Equal(default(DateTime), snapshot.Date);
                    Assert.Null(snapshot.RoundId);
                }
                finally { Cleanup(dir); }
            });

            TestRunner.Run("进度存储：不可写路径返回失败且不产生目标文件", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    string blocker = Path.Combine(dir, "blocked");
                    File.WriteAllText(blocker, "file");
                    var store = new InspectionProgressStore(Path.Combine(blocker, "progress.json"));
                    string error;
                    Assert.False(store.TrySave(MakeSnapshot(), out error));
                    Assert.NotNull(error);
                    Assert.True(error.Length > 0);
                    Assert.False(File.Exists(Path.Combine(blocker, "progress.json")));
                }
                finally { Cleanup(dir); }
            });

            TestRunner.Run("进度存储：保存到被占路径失败不影响其他已存在的进度文件", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    string path = Path.Combine(dir, "progress.json");
                    var store = new InspectionProgressStore(path);
                    string error;
                    Assert.True(store.TrySave(MakeSnapshot(), out error), error);

                    // 用同名目录占位制造保存冲突，旧文件仍在且可读。
                    string occupied = Path.Combine(dir, "occupied.json");
                    Directory.CreateDirectory(Path.Combine(occupied, "child"));
                    var occupiedStore = new InspectionProgressStore(occupied);
                    Assert.False(occupiedStore.TrySave(MakeSnapshot(), out error));
                    Assert.NotNull(error);
                    Assert.True(File.Exists(path));
                    InspectionProgressSnapshot loaded;
                    Assert.True(store.TryLoad(out loaded, out error), error);
                    Assert.Equal("设备甲", loaded.Devices[0]);
                }
                finally { Cleanup(dir); }
            });

            TestRunner.Run("进度存储：同一目标原子替换失败保留原字节并清理临时文件", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    string path = Path.Combine(dir, "progress.json");
                    var store = new InspectionProgressStore(path);
                    string error;
                    Assert.True(store.TrySave(MakeSnapshot(), out error), error);
                    byte[] original = File.ReadAllBytes(path);

                    InspectionProgressSnapshot changed = MakeSnapshot();
                    changed.VisitedDevices = new List<string> { "设备甲", "设备乙" };
                    changed.CompletionPending = true;
                    var fake = new FailingReplaceOperations();
                    var injected = new InspectionProgressStore(
                        path, InspectionWidget.Config.AtomicConfigWriter.ForTests(fake));

                    Assert.False(injected.TrySave(changed, out error));
                    Assert.NotNull(error);
                    Assert.True(error.Contains("模拟替换失败"), error);
                    Assert.Equal(1, fake.ReplaceCalls);
                    Assert.Equal(0, fake.MoveCalls);
                    Assert.Equal(Convert.ToBase64String(original),
                        Convert.ToBase64String(File.ReadAllBytes(path)));
                    Assert.Equal(0, Directory.GetFiles(dir, "*.tmp").Length);

                    InspectionProgressSnapshot loaded;
                    Assert.True(store.TryLoad(out loaded, out error), error);
                    Assert.Equal(1, loaded.VisitedDevices.Count);
                }
                finally { Cleanup(dir); }
            });

            TestRunner.Run("进度存储：序列化后超过上限拒绝保存且不覆盖旧文件", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    string path = Path.Combine(dir, "progress.json");
                    var store = new InspectionProgressStore(path);
                    string error;
                    Assert.True(store.TrySave(MakeSnapshot(), out error), error);
                    byte[] original = File.ReadAllBytes(path);

                    InspectionProgressSnapshot huge = MakeSnapshot();
                    huge.SavedPaths = new List<string>();
                    for (int i = 0; i < 4000; i++)
                        huge.SavedPaths.Add(@"C:\shots\" + i + "-" + new string('x', 400) + ".png");
                    Assert.False(store.TrySave(huge, out error));
                    Assert.NotNull(error);
                    Assert.True(error.Contains("1 MiB"), error);

                    Assert.Equal(Convert.ToBase64String(original),
                        Convert.ToBase64String(File.ReadAllBytes(path)));
                    Assert.Equal(0, Directory.GetFiles(dir, "*.tmp").Length);
                }
                finally { Cleanup(dir); }
            });

            TestRunner.Run("进度存储：空参数如实失败", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    var store = new InspectionProgressStore(Path.Combine(dir, "progress.json"));
                    string error;
                    Assert.False(store.TrySave(null, out error));
                    Assert.NotNull(error);

                    bool threw = false;
                    try
                    {
                        new InspectionProgressStore("  ");
                    }
                    catch (ArgumentException)
                    {
                        threw = true;
                    }
                    Assert.True(threw, "空路径应在构造时拒绝。");
                }
                finally { Cleanup(dir); }
            });

            TestRunner.Run("进度存储：落盘 JSON 为 UTF-8 且可被计划恢复", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    string path = Path.Combine(dir, "progress.json");
                    var store = new InspectionProgressStore(path);
                    var clock = new FakeClock(Day1);
                    var plan = new InspectionPlan(clock, new[] { "设备甲", "设备乙" });
                    Assert.True(plan.MarkVisited("设备甲", plan.CurrentToken));
                    string error;
                    Assert.True(store.TrySave(plan.CreateSnapshot(), out error), error);

                    byte[] bytes = File.ReadAllBytes(path);
                    string json = new UTF8Encoding(false).GetString(bytes);
                    Assert.True(json.Contains("设备甲"), json);
                    Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB
                        && bytes[2] == 0xBF, "进度文件不应带 UTF-8 BOM。");

                    InspectionProgressSnapshot snapshot;
                    Assert.True(store.TryLoad(out snapshot, out error), error);
                    var restored = new InspectionPlan(clock, new[] { "设备甲", "设备乙" });
                    Assert.True(restored.Restore(snapshot, out error), error);
                    Assert.True(restored.IsVisited("设备甲"));
                    Assert.Equal(1, restored.VisitedCount);
                }
                finally { Cleanup(dir); }
            });
        }

        private static InspectionProgressSnapshot MakeSnapshot()
        {
            return new InspectionProgressSnapshot
            {
                SchemaVersion = InspectionPlan.SnapshotSchemaVersion,
                Date = Day1.Date,
                RoundId = "round-2026-11-14",
                Devices = new List<string> { "设备甲", "设备乙" },
                VisitedDevices = new List<string> { "设备甲" },
                CompletionPrompted = true,
                CompletionPending = false,
                SavedPaths = new List<string> { @"C:\shots\设备甲-1.png" }
            };
        }

        private static string MakeTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(),
                "iw-planning-tests-" + Guid.NewGuid().ToString("N"));
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
