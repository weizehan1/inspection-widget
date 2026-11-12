using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using InspectionWidget.Capture;
using InspectionWidget.Config;

namespace InspectionWidget.Tests
{
    /// <summary>
    /// v1.0.3 用例：凭据排序纯逻辑（ListReorder.Move）、截图区分折叠的布局高度
    /// （DeviceZoneHeight 折叠重载 / ComputeHeight 双折叠重载）、IsDeviceCollapsed
    /// 配置四态（默认 / 深拷贝 / 往返 / 缺字段自愈）、凭据顺序经排序后往返保持、
    /// 以及保存/应用共用的 SettingsSaveCoordinator（校验失败不落盘、成功往返一致）。
    /// 设置窗与主面板本体为 WinForms 交互，无自动化用例（实机验收）。
    /// </summary>
    public static class V103Tests
    {
        public static void RunAll()
        {
            // ---------- ListReorder.Move（凭据/列表排序共用纯逻辑） ----------

            TestRunner.Run("排序：中间元素上移与前一位交换", () =>
            {
                var list = new List<string> { "甲", "乙", "丙" };
                Assert.True(ListReorder.Move(list, 1, -1));
                Assert.Equal("乙", list[0]);
                Assert.Equal("甲", list[1]);
                Assert.Equal("丙", list[2]);
            });

            TestRunner.Run("排序：中间元素下移与后一位交换", () =>
            {
                var list = new List<string> { "甲", "乙", "丙" };
                Assert.True(ListReorder.Move(list, 1, 1));
                Assert.Equal("甲", list[0]);
                Assert.Equal("丙", list[1]);
                Assert.Equal("乙", list[2]);
            });

            TestRunner.Run("排序：首行上移为 no-op 返回 false 且列表不变", () =>
            {
                var list = new List<string> { "甲", "乙", "丙" };
                Assert.False(ListReorder.Move(list, 0, -1));
                Assert.Equal("甲", list[0]);
                Assert.Equal("乙", list[1]);
                Assert.Equal("丙", list[2]);
            });

            TestRunner.Run("排序：末行下移为 no-op 返回 false 且列表不变", () =>
            {
                var list = new List<string> { "甲", "乙", "丙" };
                Assert.False(ListReorder.Move(list, 2, 1));
                Assert.Equal("甲", list[0]);
                Assert.Equal("丙", list[2]);
            });

            TestRunner.Run("排序：索引无效或步长越界返回 false", () =>
            {
                var list = new List<string> { "甲", "乙" };
                Assert.False(ListReorder.Move(list, -1, 1));
                Assert.False(ListReorder.Move(list, 2, -1));
                Assert.False(ListReorder.Move(list, 0, 5));
                Assert.False(ListReorder.Move(list, 0, 0));
                Assert.Equal("甲", list[0]);
                Assert.Equal("乙", list[1]);
            });

            TestRunner.Run("排序：不足两条时任何移动都返回 false", () =>
            {
                var one = new List<string> { "甲" };
                Assert.False(ListReorder.Move(one, 0, 1));
                Assert.False(ListReorder.Move(one, 0, -1));
                var empty = new List<string>();
                Assert.False(ListReorder.Move(empty, 0, 1));
            });

            // ---------- 截图区折叠布局 ----------

            TestRunner.Run("布局：设备区折叠时高度恒为 0（条数与因子无关）", () =>
            {
                Assert.Equal(0, PanelLayout.DeviceZoneHeight(0, true, 1.0));
                Assert.Equal(0, PanelLayout.DeviceZoneHeight(1, true, 1.0));
                Assert.Equal(0, PanelLayout.DeviceZoneHeight(10, true, 1.0));
                Assert.Equal(0, PanelLayout.DeviceZoneHeight(10, true, 2.0));
            });

            TestRunner.Run("布局：设备区展开高度与旧签名一致", () =>
            {
                Assert.Equal(PanelLayout.DeviceZoneHeight(0, 1.0), PanelLayout.DeviceZoneHeight(0, false, 1.0));
                Assert.Equal(PanelLayout.DeviceZoneHeight(5, 1.0), PanelLayout.DeviceZoneHeight(5, false, 1.0));
                Assert.Equal(185, PanelLayout.DeviceZoneHeight(5, false, 1.0));
            });

            TestRunner.Run("布局：双折叠组合总高（1.0 档）", () =>
            {
                // 都展开与都折叠的凭据侧旧值对齐（851 / 449 见 PanelLayoutTests）
                Assert.Equal(851, PanelLayout.ComputeHeight(8, false, 10, false, 1.0));
                Assert.Equal(449, PanelLayout.ComputeHeight(8, false, 10, true, 1.0));
                // 只折叠截图区：851 − 设备区 299 = 552
                Assert.Equal(552, PanelLayout.ComputeHeight(8, true, 10, false, 1.0));
                // 两区都折叠：只剩标题栏＋两行分区标题＋底部 = 36+28+0+28+0+58
                Assert.Equal(150, PanelLayout.ComputeHeight(8, true, 10, true, 1.0));
                Assert.Equal(150, PanelLayout.ComputeHeight(0, true, 0, true, 1.0));
            });

            TestRunner.Run("布局：双折叠总高（1.5 档）", () =>
            {
                // 54 + 42 + 0 + 42 + 0 + 87 = 225
                Assert.Equal(225, PanelLayout.ComputeHeight(8, true, 10, true, 1.5));
                // 都展开与旧签名同值 1289
                Assert.Equal(1289, PanelLayout.ComputeHeight(8, false, 10, false, 1.5));
            });

            // ---------- IsDeviceCollapsed 配置四态 ----------

            TestRunner.Run("配置：IsDeviceCollapsed 默认 false（CreateDefault 与缺字段反序列化）", () =>
            {
                Assert.False(AppConfig.CreateDefault().IsDeviceCollapsed);
            });

            TestRunner.Run("配置：深拷贝保留 IsDeviceCollapsed", () =>
            {
                var source = AppConfig.CreateDefault();
                source.IsDeviceCollapsed = true;
                AppConfig copy = AppConfigCloner.Clone(source);
                Assert.True(copy.IsDeviceCollapsed);
                source.IsDeviceCollapsed = false;
                Assert.True(copy.IsDeviceCollapsed, "深拷贝须独立于源对象后续修改");
            });

            TestRunner.Run("配置：IsDeviceCollapsed=true 往返保留且 Kind=Ok", () =>
            {
                string dir = ConfigStoreTests.MakeTempDir();
                try
                {
                    var cfg = new AppConfig
                    {
                        RootPath = "D:\\巡检截图",
                        Devices = new List<string> { "机房A" },
                        Credentials = new List<CredentialEntry>(),
                        IsCredentialCollapsed = false,
                        IsDeviceCollapsed = true,
                        AutoClearClipboard = true,
                        CaptureMode = CaptureMode.Auto,
                        WindowLeft = null,
                        WindowTop = null,
                        LastDevice = null,
                        UiScalePercent = 100,
                        FolderNamePattern = FolderNamePattern.Default,
                        TrayHintShown = false
                    };
                    SaveResult saved = ConfigStoreTests.MakeStore(dir).Save(cfg);
                    Assert.True(saved.Saved, saved.Error);
                    LoadResult r = ConfigStoreTests.MakeStore(dir).Load();
                    Assert.Equal(ConfigLoadKind.Ok, r.Kind);
                    Assert.True(r.Config.IsDeviceCollapsed);
                }
                finally { ConfigStoreTests.Cleanup(dir); }
            });

            TestRunner.Run("配置：手写 JSON 独缺 IsDeviceCollapsed → Defaulted 且补 false、已写字段保留", () =>
            {
                string dir = ConfigStoreTests.MakeTempDir();
                try
                {
                    File.WriteAllText(ConfigStoreTests.ConfigFile(dir),
                        "{\"RootPath\":\"D:/shots\",\"Devices\":[\"设备甲\"],\"Credentials\":[],"
                        + "\"IsCredentialCollapsed\":true,\"AutoClearClipboard\":true,\"CaptureMode\":0,"
                        + "\"WindowLeft\":null,\"WindowTop\":null,\"LastDevice\":null,"
                        + "\"UiScalePercent\":100,\"FolderNamePattern\":\"yyyy-MM-dd\",\"TrayHintShown\":false}");
                    LoadResult r = ConfigStoreTests.MakeStore(dir).Load();
                    Assert.Equal(ConfigLoadKind.Defaulted, r.Kind);
                    Assert.False(r.Config.IsDeviceCollapsed);
                    Assert.True(r.Config.IsCredentialCollapsed, "已写字段保留");
                    Assert.Equal("设备甲", r.Config.Devices[0]);
                }
                finally { ConfigStoreTests.Cleanup(dir); }
            });

            // ---------- 凭据顺序：排序后往返保持 ----------

            TestRunner.Run("凭据顺序：Move 调序后保存→加载顺序一致", () =>
            {
                string dir = ConfigStoreTests.MakeTempDir();
                try
                {
                    var entries = new List<CredentialEntry>
                    {
                        new CredentialEntry { Title = "堡垒机", EncryptedSecret = "QQ==" },
                        new CredentialEntry { Title = "交换机", EncryptedSecret = "Qg==" },
                        new CredentialEntry { Title = "门禁", EncryptedSecret = "Qw==" }
                    };
                    // 末条上移两次 → 门禁, 堡垒机, 交换机
                    Assert.True(ListReorder.Move(entries, 2, -1));
                    Assert.True(ListReorder.Move(entries, 1, -1));

                    var cfg = AppConfig.CreateDefault();
                    cfg.Credentials = entries;
                    SaveResult saved = ConfigStoreTests.MakeStore(dir).Save(cfg);
                    Assert.True(saved.Saved, saved.Error);

                    LoadResult r = ConfigStoreTests.MakeStore(dir).Load();
                    Assert.Equal(3, r.Config.Credentials.Count);
                    Assert.Equal("门禁", r.Config.Credentials[0].Title);
                    Assert.Equal("堡垒机", r.Config.Credentials[1].Title);
                    Assert.Equal("交换机", r.Config.Credentials[2].Title);
                    Assert.Equal("Qw==", r.Config.Credentials[0].EncryptedSecret);
                }
                finally { ConfigStoreTests.Cleanup(dir); }
            });

            // ---------- SettingsSaveCoordinator（保存/应用共用校验＋落盘） ----------

            TestRunner.Run("保存协调：设备名非法失败且不落盘", () =>
            {
                string dir = ConfigStoreTests.MakeTempDir();
                try
                {
                    ConfigStore store = ConfigStoreTests.MakeStore(dir);
                    Assert.True(store.Save(AppConfig.CreateDefault()).Saved);

                    SettingsSaveCoordinator.Outcome outcome = SettingsSaveCoordinator.Execute(
                        AppConfig.CreateDefault(),
                        new List<string> { "非法/名" },
                        new List<CredentialRowCollector.RowInput>(),
                        "D:\\巡检", "巡检-yyyy-MM-dd", true, 100, CaptureMode.Auto,
                        new FakeProtector(), store);
                    Assert.False(outcome.Saved);
                    Assert.Equal(0, outcome.ErrorGroup);

                    LoadResult r = store.Load();
                    Assert.Equal(2, r.Config.Devices.Count); // 旧配置完好未动
                    Assert.Equal("设备1", r.Config.Devices[0]);
                }
                finally { ConfigStoreTests.Cleanup(dir); }
            });

            TestRunner.Run("保存协调：日期文件夹名非法失败且组别为存储组", () =>
            {
                string dir = ConfigStoreTests.MakeTempDir();
                try
                {
                    ConfigStore store = ConfigStoreTests.MakeStore(dir);
                    Assert.True(store.Save(AppConfig.CreateDefault()).Saved);

                    SettingsSaveCoordinator.Outcome outcome = SettingsSaveCoordinator.Execute(
                        AppConfig.CreateDefault(),
                        new List<string> { "设备1" },
                        new List<CredentialRowCollector.RowInput>(),
                        "D:\\巡检", "yyyy:MM", true, 100, CaptureMode.Auto,
                        new FakeProtector(), store);
                    Assert.False(outcome.Saved);
                    Assert.Equal(2, outcome.ErrorGroup);
                    Assert.Equal(2, store.Load().Config.Devices.Count);
                }
                finally { ConfigStoreTests.Cleanup(dir); }
            });

            TestRunner.Run("保存协调：新凭据空内容失败并带行号与焦点提示", () =>
            {
                string dir = ConfigStoreTests.MakeTempDir();
                try
                {
                    ConfigStore store = ConfigStoreTests.MakeStore(dir);
                    Assert.True(store.Save(AppConfig.CreateDefault()).Saved);

                    var inputs = new List<CredentialRowCollector.RowInput>
                    {
                        new CredentialRowCollector.RowInput { Title = "新凭据", IsNew = true, NewPlainText = "" }
                    };
                    SettingsSaveCoordinator.Outcome outcome = SettingsSaveCoordinator.Execute(
                        AppConfig.CreateDefault(),
                        new List<string> { "设备1" },
                        inputs,
                        "D:\\巡检", "巡检-yyyy-MM-dd", true, 100, CaptureMode.Auto,
                        new FakeProtector(), store);
                    Assert.False(outcome.Saved);
                    Assert.Equal(1, outcome.ErrorGroup);
                    Assert.Equal(0, outcome.CredentialErrorIndex);
                    Assert.False(outcome.CredentialErrorOnTitle);
                    Assert.Equal(0, store.Load().Config.Credentials.Count); // 未落盘
                }
                finally { ConfigStoreTests.Cleanup(dir); }
            });

            TestRunner.Run("保存协调：凭据名称为空时焦点提示落在名称框", () =>
            {
                string dir = ConfigStoreTests.MakeTempDir();
                try
                {
                    ConfigStore store = ConfigStoreTests.MakeStore(dir);
                    var inputs = new List<CredentialRowCollector.RowInput>
                    {
                        new CredentialRowCollector.RowInput { Title = " ", IsNew = true, NewPlainText = "x" }
                    };
                    SettingsSaveCoordinator.Outcome outcome = SettingsSaveCoordinator.Execute(
                        AppConfig.CreateDefault(),
                        new List<string> { "设备1" },
                        inputs,
                        "D:\\巡检", "巡检-yyyy-MM-dd", true, 100, CaptureMode.Auto,
                        new FakeProtector(), store);
                    Assert.False(outcome.Saved);
                    Assert.True(outcome.CredentialErrorOnTitle);
                }
                finally { ConfigStoreTests.Cleanup(dir); }
            });

            TestRunner.Run("保存协调：成功落盘且往返值与入参一致", () =>
            {
                string dir = ConfigStoreTests.MakeTempDir();
                try
                {
                    ConfigStore store = ConfigStoreTests.MakeStore(dir);
                    AppConfig working = AppConfig.CreateDefault();
                    working.IsDeviceCollapsed = true;
                    working.LastDevice = "已删设备";
                    var inputs = new List<CredentialRowCollector.RowInput>
                    {
                        new CredentialRowCollector.RowInput { Title = "堡垒机", OriginalSecret = "QQ==", IsNew = false, Cleared = false, NewPlainText = "" },
                        new CredentialRowCollector.RowInput { Title = "新凭据", IsNew = true, NewPlainText = "pw-2" }
                    };
                    SettingsSaveCoordinator.Outcome outcome = SettingsSaveCoordinator.Execute(
                        working,
                        new List<string> { "设备乙", "设备甲" },
                        inputs,
                        "D:\\巡检", "巡检-yyyyMMdd", false, 150, CaptureMode.BitBltOnly,
                        new FakeProtector(), store);
                    Assert.True(outcome.Saved, outcome.Error);
                    Assert.NotNull(outcome.Result);
                    Assert.Equal("设备乙", outcome.Result.Devices[0]);
                    Assert.Equal("设备甲", outcome.Result.Devices[1]);
                    Assert.Equal("巡检-yyyyMMdd", outcome.Result.FolderNamePattern);
                    Assert.Equal(150, outcome.Result.UiScalePercent);
                    Assert.Equal(CaptureMode.BitBltOnly, outcome.Result.CaptureMode);
                    Assert.False(outcome.Result.AutoClearClipboard);
                    Assert.True(outcome.Result.IsDeviceCollapsed); // 未编辑字段沿用工作副本
                    Assert.Null(outcome.Result.LastDevice);        // 指向已删设备 → 置 null
                    Assert.Equal(2, outcome.Result.Credentials.Count);
                    Assert.Equal("QQ==", outcome.Result.Credentials[0].EncryptedSecret); // 留空保留
                    Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("pw-2")),
                        outcome.Result.Credentials[1].EncryptedSecret); // 新内容经 Protector

                    LoadResult r = store.Load();
                    Assert.Equal(ConfigLoadKind.Ok, r.Kind);
                    Assert.Equal("设备乙", r.Config.Devices[0]);
                    Assert.Equal(2, r.Config.Credentials.Count);
                    Assert.Equal("新凭据", r.Config.Credentials[1].Title);
                    Assert.Equal(150, r.Config.UiScalePercent);
                    Assert.True(r.Config.IsDeviceCollapsed);
                }
                finally { ConfigStoreTests.Cleanup(dir); }
            });

            // ---------- 选窗钩子矩形更新 ----------

            TestRunner.Run("钩子：未运行时不新建会话，更新自身矩形返回 false", () =>
            {
                using (var hooks = new HookManager())
                {
                    Assert.False(hooks.IsRunning);
                    Assert.False(hooks.UpdateSelfBounds(new System.Drawing.Rectangle(10, 10, 320, 480)));
                    Assert.False(hooks.IsRunning, "更新矩形不得隐式启动会话");
                }
            });

            TestRunner.Run("钩子：已释放后更新自身矩形抛 ObjectDisposedException", () =>
            {
                var hooks = new HookManager();
                hooks.Dispose();
                Assert.Throws<ObjectDisposedException>(delegate
                {
                    hooks.UpdateSelfBounds(new System.Drawing.Rectangle(0, 0, 1, 1));
                });
            });
        }
    }
}
