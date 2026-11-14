using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using InspectionWidget.Capture;
using InspectionWidget.Config;
using InspectionWidget.Planning;
using InspectionWidget.Storage;
using StorageSaveResult = InspectionWidget.Storage.SaveResult;

namespace InspectionWidget.Tests
{
    public static class MainFormUiTests
    {
        public const int CaseCount = 6;

        private sealed class StepClock : IClock
        {
            public DateTime Now { get; set; }
        }

        private sealed class FakeWindowPickHooks : IWindowPickHooks
        {
            private int _nextSessionId;
            public int StartCallCount { get; private set; }
            public int UpdateBoundsCallCount { get; private set; }
            public int StopCallCount { get; private set; }
            public bool Disposed { get; private set; }
            public Rectangle CurrentSelfBounds { get; private set; }

            public event Action<Point, int> ExternalClick
            {
                add { }
                remove { }
            }

            public event Action CancelRequested
            {
                add { }
                remove { }
            }

            public event Action TimeoutElapsed
            {
                add { }
                remove { }
            }

            public int Start(Rectangle selfBounds)
            {
                StartCallCount++;
                CurrentSelfBounds = selfBounds;
                return ++_nextSessionId;
            }

            public bool UpdateSelfBounds(Rectangle selfBounds)
            {
                UpdateBoundsCallCount++;
                CurrentSelfBounds = selfBounds;
                return true;
            }

            public void Stop()
            {
                StopCallCount++;
            }

            public void Dispose()
            {
                Disposed = true;
            }
        }

        private sealed class TestMainForm : MainForm
        {
            private readonly Func<double> _dpiProvider;

            public TestMainForm(
                ConfigStore store,
                InspectionProgressStore progressStore,
                IClock clock,
                IWindowPickHooks hooks,
                InspectionFolderNavigator navigator,
                Func<double> dpiProvider)
                : base(store, progressStore, clock, hooks, navigator)
            {
                _dpiProvider = dpiProvider ?? (() => 1.0);
            }

            protected override double GetSystemDpiScale()
            {
                return _dpiProvider != null ? _dpiProvider() : 1.0;
            }

            public void TriggerRecreateHandle()
            {
                RecreateHandle();
            }
        }

        private sealed class MainEnv : IDisposable
        {
            public readonly string TempDir;
            public readonly string ConfigPath;
            public readonly string ProgressPath;
            public readonly StepClock Clock;
            public readonly FakeWindowPickHooks Hooks;
            public readonly ConfigStore Store;
            public readonly InspectionProgressStore ProgressStore;
            public double SystemDpi = 1.0;
            public TestMainForm Form;

            public MainEnv(AppConfig seedConfig = null, string rawProgressJson = null, DateTime? now = null)
            {
                TempDir = Path.Combine(Path.GetTempPath(), "iw-main-ui-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(TempDir);
                ConfigPath = Path.Combine(TempDir, "config.json");
                ProgressPath = Path.Combine(TempDir, "progress.json");
                string shotsDir = Path.Combine(TempDir, "Shots");
                Directory.CreateDirectory(shotsDir);

                AppConfig cfg = seedConfig ?? BuildDefaultConfig(shotsDir);
                if (string.IsNullOrEmpty(cfg.RootPath))
                    cfg.RootPath = shotsDir;
                Store = new ConfigStore(
                    new ConfigPaths { AuthoritativePath = ConfigPath, IsFallback = false },
                    delegate { return true; });
                var saveRes = Store.Save(cfg);
                if (!saveRes.Saved)
                    throw new InvalidOperationException("保存测试配置失败：" + saveRes.Error);

                if (rawProgressJson != null)
                    File.WriteAllText(ProgressPath, rawProgressJson);

                Clock = new StepClock { Now = now ?? new DateTime(2026, 11, 14, 9, 30, 0) };
                Hooks = new FakeWindowPickHooks();
                ProgressStore = new InspectionProgressStore(ProgressPath);
                var navigator = new InspectionFolderNavigator(delegate { return true; }, delegate { });
                Form = new TestMainForm(Store, ProgressStore, Clock, Hooks, navigator, () => SystemDpi);
            }

            public static AppConfig BuildDefaultConfig(string shotsDir = null)
            {
                AppConfig cfg = AppConfig.CreateDefault();
                cfg.RootPath = shotsDir ?? "D:\\Shots";
                cfg.UiScalePercent = 100;
                cfg.PlanModeEnabled = true;
                cfg.HideVisitedDevices = false;
                cfg.Devices = new List<string> { "核心A", "核心B", "汇聚C" };
                cfg.QuickLinks = new List<QuickLinkEntry>
                {
                    new QuickLinkEntry { Title = "监控门户", Url = "https://monitor.example.com" },
                    new QuickLinkEntry { Title = "工单台", Url = "https://ticket.example.com" }
                };
                cfg.Credentials = new List<CredentialEntry>
                {
                    new CredentialEntry { Title = "运维账号", EncryptedSecret = string.Empty }
                };
                return cfg;
            }

            public void Dispose()
            {
                if (Form != null && !Form.IsDisposed)
                    Form.Dispose();
                try
                {
                    if (Directory.Exists(TempDir))
                        Directory.Delete(TempDir, true);
                }
                catch
                {
                }
            }
        }

        private static object Field(object target, string name)
        {
            Type type = target.GetType();
            while (type != null)
            {
                FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (field != null)
                    return field.GetValue(target);
                type = type.BaseType;
            }
            throw new MissingFieldException(target.GetType().FullName, name);
        }

        private static void SetField(object target, string name, object value)
        {
            Type type = target.GetType();
            while (type != null)
            {
                FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }
                type = type.BaseType;
            }
            throw new MissingFieldException(target.GetType().FullName, name);
        }

        private static object Invoke(object target, string name, params object[] args)
        {
            Type type = target.GetType();
            while (type != null)
            {
                MethodInfo method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (method != null)
                    return method.Invoke(target, args);
                type = type.BaseType;
            }
            throw new MissingMethodException(target.GetType().FullName, name);
        }

        private static void CollectControls(Control root, List<Control> results)
        {
            if (root == null) return;
            results.Add(root);
            foreach (Control child in root.Controls)
                CollectControls(child, results);
        }

        private static void AssertAllFontsAlive(Control root)
        {
            var all = new List<Control>();
            CollectControls(root, all);
            Assert.True(all.Count > 5);
            foreach (Control ctrl in all)
            {
                Assert.NotNull(ctrl.Font, "控件缺少 Font：" + ctrl.GetType().Name + " / " + ctrl.Text);
                Size measured = TextRenderer.MeasureText("巡检A1", ctrl.Font);
                Assert.True(measured.Height > 0 && measured.Width > 0, "字体已失效或被释放");
            }
        }

        public static void RunAll()
        {
            TestRunner.Run("UI-Main：OnHandleCreated 系统 DPI 从 1.0 变为 2.0 时重算面板并无已释放 Font", () =>
            {
                using (var env = new MainEnv())
                {
                    int widthAt100 = env.Form.ClientSize.Width;
                    double appliedBefore = (double)Field(env.Form, "_appliedScaleFactor");
                    Assert.Equal(1.0, appliedBefore);
                    Assert.Equal(PanelLayout.ScaledSchemeBPanelWidth(1.0), widthAt100);

                    env.Form.Show();
                    Application.DoEvents();
                    Assert.True(env.Form.IsHandleCreated);

                    env.SystemDpi = 2.0;
                    env.Form.TriggerRecreateHandle();
                    Application.DoEvents();

                    double appliedAfter = (double)Field(env.Form, "_appliedScaleFactor");
                    Assert.Equal(2.0, appliedAfter);
                    Assert.True(env.Form.ClientSize.Width > widthAt100, "200% DPI 下主面板宽度应扩大");
                    AssertAllFontsAlive(env.Form);
                }
            });

            TestRunner.Run("UI-Main：主面板 _scrollHost 滚动后折叠区块仍保持子控件偏移一致且缩减时钳制", () =>
            {
                AppConfig cfg = MainEnv.BuildDefaultConfig();
                cfg.Devices = new List<string>();
                for (int i = 1; i <= 36; i++)
                    cfg.Devices.Add("核心设备-" + i.ToString("00"));
                for (int i = 1; i <= 8; i++)
                    cfg.Credentials.Add(new CredentialEntry { Title = "凭据-" + i, EncryptedSecret = string.Empty });
                for (int i = 1; i <= 6; i++)
                    cfg.QuickLinks.Add(new QuickLinkEntry { Title = "链接-" + i, Url = "https://example.com/" + i });

                using (var env = new MainEnv(cfg))
                {
                    env.Form.Show();
                    Application.DoEvents();

                    var scrollHost = (Panel)Field(env.Form, "_scrollHost");
                    var quickHeader = (Panel)Field(env.Form, "_quickLinkHeaderPanel");
                    Assert.True(scrollHost.AutoScrollMinSize.Height > scrollHost.ClientSize.Height,
                        "超长列表应触发 _scrollHost 垂直滚动");

                    int logicalQuickTop = quickHeader.Top;
                    scrollHost.AutoScrollPosition = new Point(0, 140);
                    Application.DoEvents();
                    int scrollYBefore = scrollHost.AutoScrollPosition.Y;
                    Assert.True(scrollYBefore < 0);
                    Assert.Equal(logicalQuickTop + scrollYBefore, quickHeader.Top);

                    // 折叠设备区减少逻辑高度，验证子控件位置对齐与工作区钳制
                    Invoke(env.Form, "OnDeviceHeaderClicked", null, EventArgs.Empty);
                    Application.DoEvents();
                    int scrollYAfter = scrollHost.AutoScrollPosition.Y;
                    Assert.True(scrollYAfter <= 0);
                    Assert.Equal(logicalQuickTop + scrollYAfter, quickHeader.Top);

                    Rectangle work = Screen.FromControl(env.Form).WorkingArea;
                    Assert.True(env.Form.Bottom <= work.Bottom);
                    Assert.True(env.Form.Top >= work.Top);
                }
            });

            TestRunner.Run("UI-Main：Waiting 态折叠区块或切换计划开关同步更新钩子 SelfBounds", () =>
            {
                using (var env = new MainEnv())
                {
                    env.Form.Show();
                    Application.DoEvents();

                    Invoke(env.Form, "OnDeviceRowActivated", "核心A");
                    Assert.Equal(1, env.Hooks.StartCallCount);
                    Assert.Equal(env.Form.Bounds, env.Hooks.CurrentSelfBounds);

                    int beforeUpdates = env.Hooks.UpdateBoundsCallCount;
                    int heightExpanded = env.Form.Bounds.Height;
                    Invoke(env.Form, "OnCredentialHeaderClicked", null, EventArgs.Empty);
                    Application.DoEvents();
                    Assert.True(env.Hooks.UpdateBoundsCallCount > beforeUpdates);
                    Assert.True(env.Form.Bounds.Height != heightExpanded);
                    Assert.Equal(env.Form.Bounds, env.Hooks.CurrentSelfBounds);

                    int beforePlanUpdates = env.Hooks.UpdateBoundsCallCount;
                    Invoke(env.Form, "OnPlanToggleClicked", null, EventArgs.Empty);
                    Application.DoEvents();
                    Assert.True(env.Hooks.UpdateBoundsCallCount > beforePlanUpdates);
                    Assert.Equal(env.Form.Bounds, env.Hooks.CurrentSelfBounds);
                }
            });

            TestRunner.Run("UI-Main：Waiting 态再次点击当前设备取消选窗并替换提示文案", () =>
            {
                using (var env = new MainEnv())
                {
                    env.Form.Show();
                    Application.DoEvents();

                    var sm = (PanelStateMachine)Field(env.Form, "_stateMachine");
                    var feedback = (Label)Field(env.Form, "_feedbackLabel");
                    Invoke(env.Form, "OnDeviceRowActivated", "核心A");
                    Assert.Equal(PanelState.Waiting, sm.State);
                    Assert.True(feedback.Text.Contains("请点目标窗口"));

                    Invoke(env.Form, "OnDeviceRowActivated", "核心A");
                    Assert.Equal(PanelState.Idle, sm.State);
                    Assert.True(env.Hooks.StopCallCount >= 1);
                    Assert.Equal("已取消选窗", feedback.Text);
                    Assert.Null(Field(env.Form, "_waitingPlanToken"));
                }
            });

            TestRunner.Run("UI-Main：损坏 progress 持续显示恢复按钮；保存失败先登记 _pendingImage 再回 Idle", () =>
            {
                using (var env = new MainEnv(rawProgressJson: "{broken-json"))
                {
                    env.Form.Show();
                    Application.DoEvents();

                    var recoveryBtn = (Button)Field(env.Form, "_planRecoveryButton");
                    var feedback = (Label)Field(env.Form, "_feedbackLabel");
                    var session = (InspectionSessionController)Field(env.Form, "_session");
                    Assert.True(session.IsProgressProtected);
                    Assert.True(recoveryBtn.Visible);
                    Assert.Equal("恢复进度", recoveryBtn.Text);
                    Assert.True(feedback.Text.Length > 0);
                }

                AppConfig cfg = MainEnv.BuildDefaultConfig();
                cfg.Devices = new List<string> { "核心A" };
                using (var env = new MainEnv(cfg))
                {
                    var sm = (PanelStateMachine)Field(env.Form, "_stateMachine");
                    var session = (InspectionSessionController)Field(env.Form, "_session");

                    // 窗口不可见（托盘隐藏态）即使完成也不应认领 CompletionPromptShown
                    Assert.False(env.Form.Visible);
                    Invoke(env.Form, "OnDeviceRowActivated", "核心A");
                    Assert.True(sm.BeginCapture());
                    var waitingToken = (InspectionPlanToken)Field(env.Form, "_waitingPlanToken");
                    SetField(env.Form, "_saveInFlight", true);
                    int gen = (int)Field(env.Form, "_pendingGeneration");

                    Type reqType = typeof(MainForm).GetNestedType("CaptureRequest", BindingFlags.NonPublic);
                    object req = Activator.CreateInstance(reqType);
                    SetField(req, "Device", "核心A");
                    SetField(req, "RootPath", cfg.RootPath);
                    SetField(req, "Date", env.Clock.Now);
                    SetField(req, "RequestId", sm.CurrentRequestId);
                    SetField(req, "FolderPattern", cfg.FolderNamePattern);
                    SetField(req, "PlanToken", waitingToken);

                    var failedBmp = new Bitmap(8, 8);
                    Invoke(env.Form, "OnSaveCompleted",
                        new StorageSaveResult { Saved = false, Error = "磁盘已满" },
                        failedBmp,
                        req,
                        true,
                        gen);

                    Assert.Equal(PanelState.Idle, sm.State);
                    Assert.False((bool)Field(env.Form, "_saveInFlight"));
                    Assert.True(object.ReferenceEquals(failedBmp, Field(env.Form, "_pendingImage")),
                        "保存失败时应先登记 _pendingImage 再切回 Idle");
                    Assert.False(session.Plan.CompletionPrompted,
                        "主面板不可见时不得认领当日完成弹窗");
                }
            });

            TestRunner.Run("UI-Main：跨午夜 Waiting 期间保留待刷新，回到 Idle 时同步重建新日列表", () =>
            {
                using (var env = new MainEnv())
                {
                    env.Form.Show();
                    Application.DoEvents();

                    var session = (InspectionSessionController)Field(env.Form, "_session");
                    var dateLabel = (Label)Field(env.Form, "_dateLabel");
                    var countLabel = (Label)Field(env.Form, "_planCountLabel");

                    Invoke(env.Form, "OnDeviceRowActivated", "核心A");
                    Assert.Equal("已点过 1 / 3", countLabel.Text);

                    // 午夜跨日时仍在 Waiting：更新时间显示，保留待刷新标志而不粗暴破坏当前等待上下文
                    env.Clock.Now = new DateTime(2026, 11, 15, 0, 5, 0);
                    Invoke(env.Form, "OnDayRolloverTick", null, EventArgs.Empty);
                    Assert.Equal("2026年11月15日 星期日", dateLabel.Text);
                    Assert.True((bool)Field(env.Form, "_pendingDayRolloverRefresh"));
                    Assert.Equal("已点过 1 / 3", countLabel.Text);

                    // 再次点击当前设备取消回 Idle，自动同步重绘新日 0 / 3 列表
                    Invoke(env.Form, "OnDeviceRowActivated", "核心A");
                    Assert.False((bool)Field(env.Form, "_pendingDayRolloverRefresh"));
                    Assert.Equal(new DateTime(2026, 11, 15), (DateTime)Field(env.Form, "_uiRenderedPlanDate"));
                    Assert.Equal("已点过 0 / 3", countLabel.Text);
                    Assert.Equal(0, session.Plan.VisitedCount);
                }
            });
        }
    }
}
