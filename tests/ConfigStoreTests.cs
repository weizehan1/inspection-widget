using System;
using System.Collections.Generic;
using System.IO;
using InspectionWidget.Config;

namespace InspectionWidget.Tests
{
    /// <summary>ConfigStore 序列化 / 自愈 / 原子写（临时目录，测后清理）。锁与路径判定在各自用例组。</summary>
    public static class ConfigStoreTests
    {
        private sealed class RecordingBackups : ConfigStore.IConfigBackupOperations
        {
            public byte[] CorruptBytes;
            public byte[] PreImportBytes;
            public bool ThrowCorrupt;
            public bool ThrowPreImport;
            public Action OnCorrupt;

            public string WriteCorruptBackup(string target, byte[] bytes)
            {
                CorruptBytes = bytes == null ? null : (byte[])bytes.Clone();
                if (OnCorrupt != null)
                    OnCorrupt();
                if (ThrowCorrupt)
                    throw new IOException("模拟损坏备份失败");
                string path = Path.Combine(Path.GetDirectoryName(target), "config.corrupt.test.json");
                File.WriteAllBytes(path, bytes ?? new byte[0]);
                return path;
            }

            public string WritePreImportBackup(string target, byte[] bytes)
            {
                PreImportBytes = bytes == null ? null : (byte[])bytes.Clone();
                if (ThrowPreImport)
                    throw new IOException("模拟导入备份失败");
                string path = Path.Combine(Path.GetDirectoryName(target), "config.preimport.test.json");
                File.WriteAllBytes(path, bytes ?? new byte[0]);
                return path;
            }
        }

        public static void RunAll()
        {
            TestRunner.Run("存储：缺文件 → Missing + 默认配置", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    ConfigStore store = MakeStore(dir);
                    LoadResult r = store.Load();
                    Assert.Equal(ConfigLoadKind.Missing, r.Kind);
                    Assert.NotNull(r.Config);
                    Assert.Equal(2, r.Config.Devices.Count);
                    Assert.Equal("设备1", r.Config.Devices[0]);
                    Assert.Equal("设备2", r.Config.Devices[1]);
                    Assert.True(r.Config.AutoClearClipboard);
                    Assert.NotNull(r.Config.Credentials);
                }
                finally { Cleanup(dir); }
            });

            TestRunner.Run("存储：首次读取时目标目录不存在 → 建目录后 Missing", () =>
            {
                string parent = MakeTempDir();
                string dir = Path.Combine(parent, "nested", "InspectionWidget");
                try
                {
                    var store = new ConfigStore(new ConfigPaths
                    {
                        AuthoritativePath = Path.Combine(dir, "config.json"),
                        IsFallback = true
                    }, delegate(string d) { return true; });
                    LoadResult result = store.Load();
                    Assert.Equal(ConfigLoadKind.Missing, result.Kind);
                    Assert.True(Directory.Exists(dir));
                    Assert.False(store.WriteProtection.IsProtected);
                }
                finally { Cleanup(parent); }
            });

            TestRunner.Run("存储：Save→Load 往返全字段不丢且 Kind=Ok", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    ConfigStore store = MakeStore(dir);
                    var cfg = new AppConfig
                    {
                        RootPath = "D:\\巡检截图",
                        Devices = new List<string> { "机房A", "机房B", "机房C" },
                        Credentials = new List<CredentialEntry>
                        {
                            new CredentialEntry { Title = "堡垒机", EncryptedSecret = "QUJDZA==" },
                            new CredentialEntry { Title = "交换机", EncryptedSecret = "RUZHSA==" }
                        },
                        IsCredentialCollapsed = true,
                        AutoClearClipboard = false,
                        CaptureMode = CaptureMode.BitBltOnly,
                        WindowLeft = 123,
                        WindowTop = 45,
                        LastDevice = "机房B",
                        UiScalePercent = 150,
                        FolderNamePattern = "yyyy-MM-dd",
                        TrayHintShown = true
                    };
                    SaveResult saved = store.Save(cfg);
                    Assert.True(saved.Saved, saved.Error);

                    LoadResult r = MakeStore(dir).Load();
                    Assert.Equal(ConfigLoadKind.Ok, r.Kind);
                    Assert.Equal("D:\\巡检截图", r.Config.RootPath);
                    Assert.Equal(3, r.Config.Devices.Count);
                    Assert.Equal("机房C", r.Config.Devices[2]);
                    Assert.Equal(2, r.Config.Credentials.Count);
                    Assert.Equal("堡垒机", r.Config.Credentials[0].Title);
                    Assert.Equal("QUJDZA==", r.Config.Credentials[0].EncryptedSecret);
                    Assert.Equal("交换机", r.Config.Credentials[1].Title);
                    Assert.True(r.Config.IsCredentialCollapsed);
                    Assert.False(r.Config.AutoClearClipboard, "显式保存的 false 不能被默认 true 覆盖");
                    Assert.Equal(CaptureMode.BitBltOnly, r.Config.CaptureMode);
                    Assert.Equal((int?)123, r.Config.WindowLeft);
                    Assert.Equal((int?)45, r.Config.WindowTop);
                    Assert.Equal("机房B", r.Config.LastDevice);
                    Assert.Equal(150, r.Config.UiScalePercent);
                    Assert.Equal("yyyy-MM-dd", r.Config.FolderNamePattern);
                    Assert.True(r.Config.TrayHintShown);
                }
                finally { Cleanup(dir); }
            });

            TestRunner.Run("存储：手写 JSON 缺 AutoClearClipboard 与 WindowLeft → Defaulted 且默认值补上、已写字段保留", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    File.WriteAllText(ConfigFile(dir),
                        "{\"RootPath\":\"D:/shots\",\"Devices\":[\"设备甲\",\"设备乙\"],\"LastDevice\":\"设备甲\"}");
                    LoadResult r = MakeStore(dir).Load();
                    Assert.Equal(ConfigLoadKind.Defaulted, r.Kind);
                    Assert.True(r.Config.AutoClearClipboard, "缺字段时须补默认 true");
                    Assert.Null(r.Config.WindowLeft);
                    Assert.Equal("D:/shots", r.Config.RootPath);
                    Assert.Equal(2, r.Config.Devices.Count);
                    Assert.Equal("设备乙", r.Config.Devices[1]);
                    Assert.Equal("设备甲", r.Config.LastDevice);
                    Assert.NotNull(r.Config.Credentials);
                }
                finally { Cleanup(dir); }
            });

            TestRunner.Run("重读配置：自动清空开关变化才重建守卫，失败读取不换服务", () =>
            {
                var on = new AppConfig { AutoClearClipboard = true };
                var off = new AppConfig { AutoClearClipboard = false };
                Assert.True(ConfigStore.ShouldRebuildClipboardGuard(on, off));
                Assert.True(ConfigStore.ShouldRebuildClipboardGuard(off, on));
                Assert.False(ConfigStore.ShouldRebuildClipboardGuard(on, on));
                Assert.False(ConfigStore.ShouldRebuildClipboardGuard(off, off));

                var failed = new LoadResult
                {
                    Kind = ConfigLoadKind.IoErrorKept,
                    Config = off
                };
                Assert.False(ConfigStore.ShouldRebuildClipboardGuard(failed, on));
                var loaded = new LoadResult
                {
                    Kind = ConfigLoadKind.Ok,
                    Config = off
                };
                Assert.True(ConfigStore.ShouldRebuildClipboardGuard(loaded, on));
            });

            TestRunner.Run("保存协调：设置候选拒绝非法 RootPath 且原文件不变", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    ConfigStore store = MakeStore(dir);
                    Assert.True(store.Save(AppConfig.CreateDefault()).Saved);
                    byte[] before = File.ReadAllBytes(ConfigFile(dir));

                    SettingsSaveCoordinator.Outcome outcome = SettingsSaveCoordinator.Execute(
                        AppConfig.CreateDefault(),
                        new System.Collections.Generic.List<string> { "设备1" },
                        new System.Collections.Generic.List<CredentialRowCollector.RowInput>(),
                        "C:\\shots:bad", "巡检-yyyy-MM-dd", true, 100, CaptureMode.Auto,
                        new FakeProtector(), store);
                    Assert.False(outcome.Saved);
                    Assert.Equal(2, outcome.ErrorGroup);
                    Assert.True(outcome.Error.Contains("冒号"), outcome.Error);
                    Assert.Equal(Convert.ToBase64String(before),
                        Convert.ToBase64String(File.ReadAllBytes(ConfigFile(dir))));
                }
                finally { Cleanup(dir); }
            });

            TestRunner.Run("存储：JSON 中 Devices/Credentials 为 null → 补空表不崩溃", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    File.WriteAllText(ConfigFile(dir),
                        "{\"RootPath\":\"D:/x\",\"Devices\":null,\"Credentials\":null,"
                        + "\"IsCredentialCollapsed\":false,\"AutoClearClipboard\":true,\"CaptureMode\":0,"
                        + "\"WindowLeft\":null,\"WindowTop\":null,\"LastDevice\":null}");
                    LoadResult r = MakeStore(dir).Load();
                    Assert.NotNull(r.Config.Devices);
                    Assert.Equal(0, r.Config.Devices.Count);
                    Assert.NotNull(r.Config.Credentials);
                    Assert.Equal(0, r.Config.Credentials.Count);
                }
                finally { Cleanup(dir); }
            });

            TestRunner.Run("存储：乱码文件 → CorruptBackedUp、备份内容=原乱码、返回默认", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    string garbage = "这不是 JSON {{{ \u0001\u0002 ###";
                    File.WriteAllText(ConfigFile(dir), garbage);
                    LoadResult r = MakeStore(dir).Load();
                    Assert.Equal(ConfigLoadKind.CorruptBackedUp, r.Kind);
                    Assert.NotNull(r.BackupPath);
                    Assert.True(File.Exists(r.BackupPath), "备份文件须存在");
                    Assert.Equal(garbage, File.ReadAllText(r.BackupPath));
                    Assert.True(File.Exists(ConfigFile(dir)), "备份后原件保留不删");
                    Assert.Equal(2, r.Config.Devices.Count);
                    Assert.True(r.Config.AutoClearClipboard);
                }
                finally { Cleanup(dir); }
            });

            TestRunner.Run("存储：字段类型错误（Devices 写成字符串）→ 同损坏路径", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    string bad = "{\"Devices\":\"不是列表\"}";
                    File.WriteAllText(ConfigFile(dir), bad);
                    LoadResult r = MakeStore(dir).Load();
                    Assert.Equal(ConfigLoadKind.CorruptBackedUp, r.Kind);
                    Assert.NotNull(r.BackupPath);
                    Assert.Equal(bad, File.ReadAllText(r.BackupPath));
                    Assert.Equal(2, r.Config.Devices.Count);
                }
                finally { Cleanup(dir); }
            });

            TestRunner.Run("存储：Save 后目录无 .tmp 残留", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    ConfigStore store = MakeStore(dir);
                    SaveResult saved = store.Save(AppConfig.CreateDefault());
                    Assert.True(saved.Saved, saved.Error);
                    Assert.Equal(0, Directory.GetFiles(dir, "*.tmp").Length);
                    Assert.True(File.Exists(ConfigFile(dir)));
                }
                finally { Cleanup(dir); }
            });

            TestRunner.Run("存储：Save 对敌意配置不抛异常、只返回 SaveResult（序列化兜底口径，Task 8）", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    ConfigStore store = MakeStore(dir);
                    AppConfig cfg = AppConfig.CreateDefault();
                    // 非法枚举值：部分运行时的 DataContract 序列化会为此抛
                    // SerializationException。兜底口径只要求：异常不穿透 Save，
                    // 结果非空；Saved 时不必有 Error，未 Saved 时必须有 Error 摘要。
                    // （本机 Mono 实测该值可正常序列化，故 Saved 两种结果均接受。）
                    cfg.CaptureMode = (CaptureMode)999;
                    SaveResult r = store.Save(cfg);
                    Assert.NotNull(r);
                    Assert.True(r.Saved || !string.IsNullOrEmpty(r.Error),
                        "未保存时必须带错误摘要");
                }
                finally { Cleanup(dir); }
            });

            TestRunner.Run("存储：凭据 NeedsReset 不落盘", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    ConfigStore store = MakeStore(dir);
                    AppConfig cfg = AppConfig.CreateDefault();
                    cfg.Credentials.Add(new CredentialEntry { Title = "待重设", EncryptedSecret = "QUJD", NeedsReset = true });
                    SaveResult saved = store.Save(cfg);
                    Assert.True(saved.Saved, saved.Error);
                    string raw = File.ReadAllText(ConfigFile(dir));
                    Assert.False(raw.Contains("NeedsReset"), "运行时标记不得出现在 config.json 中");
                    LoadResult r = MakeStore(dir).Load();
                    Assert.Equal(1, r.Config.Credentials.Count);
                    Assert.False(r.Config.Credentials[0].NeedsReset);
                }
                finally { Cleanup(dir); }
            });
            TestRunner.Run("存储：UiScalePercent 往返保留（175）", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    var cfg = AppConfig.CreateDefault();
                    cfg.UiScalePercent = 175;
                    SaveResult saved = MakeStore(dir).Save(cfg);
                    Assert.True(saved.Saved, saved.Error);
                    LoadResult r = MakeStore(dir).Load();
                    Assert.Equal(ConfigLoadKind.Ok, r.Kind);
                    Assert.Equal(175, r.Config.UiScalePercent);
                }
                finally { Cleanup(dir); }
            });

            TestRunner.Run("存储：手写 JSON 缺 UiScalePercent → Defaulted 且补默认 100、已写字段保留", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    File.WriteAllText(ConfigFile(dir),
                        "{\"RootPath\":\"D:/shots\",\"Devices\":[\"设备甲\"],\"LastDevice\":\"设备甲\"}");
                    LoadResult r = MakeStore(dir).Load();
                    Assert.Equal(ConfigLoadKind.Defaulted, r.Kind);
                    Assert.Equal(100, r.Config.UiScalePercent, "缺字段时须补默认 100");
                    Assert.Equal("D:/shots", r.Config.RootPath);
                    Assert.Equal("设备甲", r.Config.Devices[0]);
                }
                finally { Cleanup(dir); }
            });

            TestRunner.Run("存储：手写 JSON 的 UiScalePercent 非法值 137 → 归一 100 且标 Defaulted、其余字段不动", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    File.WriteAllText(ConfigFile(dir),
                        "{\"RootPath\":\"D:/x\",\"Devices\":[\"设备甲\"],\"Credentials\":[],"
                        + "\"IsCredentialCollapsed\":false,\"AutoClearClipboard\":true,\"CaptureMode\":0,"
                        + "\"WindowLeft\":null,\"WindowTop\":null,\"LastDevice\":null,\"UiScalePercent\":137}");
                    LoadResult r = MakeStore(dir).Load();
                    Assert.Equal(ConfigLoadKind.Defaulted, r.Kind);
                    Assert.Equal(100, r.Config.UiScalePercent);
                    Assert.Equal("设备甲", r.Config.Devices[0]);
                }
                finally { Cleanup(dir); }
            });

            TestRunner.Run("存储：缺文件时也先取得锁，锁占用返回保护而不是 Missing", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    ConfigStore store = MakeStore(dir);
                    using (var held = new FileStream(ConfigFile(dir) + ".lock",
                        FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                    {
                        LoadResult blocked = store.Load();
                        Assert.Equal(ConfigLoadKind.IoErrorKept, blocked.Kind);
                        Assert.True(store.WriteProtection.IsProtected);
                    }
                    LoadResult missing = store.Load();
                    Assert.Equal(ConfigLoadKind.Missing, missing.Kind);
                    Assert.False(store.WriteProtection.IsProtected);
                }
                finally { Cleanup(dir); }
            });

            TestRunner.Run("存储：导入恢复备份失败时正式字节不变、不发布候选", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    var backups = new RecordingBackups { ThrowPreImport = true };
                    var paths = new ConfigPaths
                    {
                        AuthoritativePath = ConfigFile(dir),
                        IsFallback = false
                    };
                    var store = new ConfigStore(paths,
                        delegate(string d) { return true; }, new AtomicConfigWriter(), backups);
                    AppConfig before = AppConfig.CreateDefault();
                    before.Devices.Clear();
                    before.Devices.Add("原配置");
                    Assert.True(store.Save(before).Saved);
                    byte[] beforeBytes = File.ReadAllBytes(ConfigFile(dir));
                    string revision = store.CurrentRevision;

                    AppConfig candidate = AppConfig.CreateDefault();
                    candidate.Devices.Clear();
                    candidate.Devices.Add("新候选");
                    SaveResult result = store.Save(candidate, new ConfigSaveOptions
                    {
                        ExpectedRevision = revision,
                        CreatePreImportBackup = true
                    });

                    Assert.False(result.Saved);
                    Assert.True(result.Error.Contains("恢复备份失败"), result.Error);
                    Assert.Equal(Convert.ToBase64String(beforeBytes),
                        Convert.ToBase64String(File.ReadAllBytes(ConfigFile(dir))));
                    Assert.Equal(revision, store.CurrentRevision);
                    Assert.Equal(0, Directory.GetFiles(dir, "config.preimport.*.json").Length);
                }
                finally { Cleanup(dir); }
            });

            TestRunner.Run("存储：损坏备份失败进入写保护且原文件不被覆盖", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    string corrupt = "不是 JSON {{{";
                    File.WriteAllText(ConfigFile(dir), corrupt);
                    var backups = new RecordingBackups { ThrowCorrupt = true };
                    var paths = new ConfigPaths
                    {
                        AuthoritativePath = ConfigFile(dir),
                        IsFallback = false
                    };
                    var store = new ConfigStore(paths,
                        delegate(string d) { return true; }, new AtomicConfigWriter(), backups);

                    LoadResult result = store.Load();
                    Assert.Equal(ConfigLoadKind.IoErrorKept, result.Kind);
                    Assert.True(store.WriteProtection.IsProtected);
                    Assert.Equal(corrupt, File.ReadAllText(ConfigFile(dir)));
                }
                finally { Cleanup(dir); }
            });

            TestRunner.Run("存储：损坏备份使用读取时字节，备份期间目标改写也不误取", () =>
            {
                string dir = MakeTempDir();
                try
                {
                    string corrupt = "原始损坏字节";
                    File.WriteAllText(ConfigFile(dir), corrupt);
                    var backups = new RecordingBackups();
                    backups.OnCorrupt = delegate
                    {
                        File.WriteAllText(ConfigFile(dir), "{\"Devices\":[]}");
                    };
                    var paths = new ConfigPaths
                    {
                        AuthoritativePath = ConfigFile(dir),
                        IsFallback = false
                    };
                    var store = new ConfigStore(paths,
                        delegate(string d) { return true; }, new AtomicConfigWriter(), backups);

                    LoadResult result = store.Load();
                    Assert.Equal(ConfigLoadKind.CorruptBackedUp, result.Kind);
                    Assert.Equal(corrupt,
                        System.Text.Encoding.UTF8.GetString(backups.CorruptBytes));
                    Assert.Equal(corrupt, File.ReadAllText(result.BackupPath));
                }
                finally { Cleanup(dir); }
            });

        }

        internal static string MakeTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "iw-cfg-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        internal static string ConfigFile(string dir)
        {
            return Path.Combine(dir, "config.json");
        }

        internal static ConfigStore MakeStore(string dir)
        {
            var paths = new ConfigPaths { AuthoritativePath = ConfigFile(dir), IsFallback = false };
            return new ConfigStore(paths, delegate(string d) { return true; });
        }

        internal static void Cleanup(string dir)
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
