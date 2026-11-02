using System;
using System.Collections.Generic;
using System.IO;
using InspectionWidget.Config;

namespace InspectionWidget.Tests
{
    /// <summary>ConfigStore 序列化 / 自愈 / 原子写（临时目录，测后清理）。锁与路径判定在各自用例组。</summary>
    public static class ConfigStoreTests
    {
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
                        UiScalePercent = 150
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
