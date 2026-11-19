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
        public const int CaseCount = 11;

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

            private Action _cancelRequested;

            public event Action CancelRequested
            {
                add { _cancelRequested += value; }
                remove { _cancelRequested -= value; }
            }

            public void RaiseCancelRequested()
            {
                Action handler = _cancelRequested;
                if (handler != null)
                    handler();
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

                    var planCard200 = (Panel)Field(env.Form, "_planCardPanel");
                    var planAction200 = (Panel)Field(env.Form, "_planActionPanel");
                    Assert.True(planCard200.Height >= planAction200.Bottom + 16,
                        "200% DPI 下计划卡底部操作区不应被裁切或缺少留白：cardHeight=" + planCard200.Height + ", actionBottom=" + planAction200.Bottom);
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

                    var sm = (PanelStateMachine)Field(env.Form, "_stateMachine");
                    var session = (InspectionSessionController)Field(env.Form, "_session");
                    var dateLabel = (Label)Field(env.Form, "_dateLabel");
                    var progressLabel = (Label)Field(env.Form, "_planProgressLabel");
                    var countLabel = (Label)Field(env.Form, "_planCountLabel");

                    string saveErr;
                    Invoke(env.Form, "OnDeviceRowActivated", "核心A");
                    var token = (InspectionPlanToken)Field(env.Form, "_waitingPlanToken");
                    Assert.True(sm.BeginCapture());
                    Assert.True(sm.CompleteCapture());
                    Assert.True(session.RecordSaveSucceeded("核心A", Path.Combine(env.TempDir, "Shots", "核心A.png"), token, out saveErr));
                    Invoke(env.Form, "OnStateChanged");
                    Invoke(env.Form, "RebuildPlanCard");
                    Invoke(env.Form, "RebuildDeviceButtons");
                    Assert.Equal("已点过", progressLabel.Text);
                    Assert.Equal("1 / 3", countLabel.Text);

                    // 选中下一台进入 Waiting，跨午夜时更新时间显示，保留待刷新标志而不破坏当前等待上下文
                    Invoke(env.Form, "OnDeviceRowActivated", "核心B");
                    Assert.Equal(PanelState.Waiting, sm.State);
                    Assert.Equal("1 / 3", countLabel.Text);
                    env.Clock.Now = new DateTime(2026, 11, 15, 0, 5, 0);
                    Invoke(env.Form, "OnDayRolloverTick", null, EventArgs.Empty);
                    Assert.Equal("2026年11月15日 星期日", dateLabel.Text);
                    Assert.True((bool)Field(env.Form, "_pendingDayRolloverRefresh"));
                    Assert.Equal("1 / 3", countLabel.Text);

                    // 右键取消选窗回 Idle，自动同步重绘新日 0 / 3 列表
                    env.Hooks.RaiseCancelRequested();
                    Assert.False((bool)Field(env.Form, "_pendingDayRolloverRefresh"));
                    Assert.Equal(new DateTime(2026, 11, 15), (DateTime)Field(env.Form, "_uiRenderedPlanDate"));
                    Assert.Equal("0 / 3", countLabel.Text);
                    Assert.Equal(0, session.Plan.VisitedCount);
                }
            });

            TestRunner.Run("UI：复制凭据高亮按钮、副标题倒计时递减，清空或跳过后复位样式", () =>
            {
                Console.WriteLine("[START] 复制凭据高亮与倒计时复位");
                AppConfig cfg = MainEnv.BuildDefaultConfig();
                var protector = new FakeProtector();
                cfg.Credentials[0].EncryptedSecret = protector.Protect("s3cr3t-pass");
                cfg.AutoClearClipboard = true;

                using (var env = new MainEnv(cfg))
                {
                    env.Form.Show();
                    Application.DoEvents();

                    var gateway = new FakeClipboardGateway();
                    SetField(env.Form, "_protector", protector);
                    SetField(env.Form, "_clipboardGateway", gateway);
                    Invoke(env.Form, "RebuildClipboardGuard");

                    var credControls = (IList)Field(env.Form, "_credentialControls");
                    Button credBtn = null;
                    foreach (Control c in credControls)
                    {
                        var b = c as Button;
                        if (b != null)
                        {
                            credBtn = b;
                            break;
                        }
                    }
                    Assert.NotNull(credBtn);

                    var subLabel = (Label)Field(env.Form, "_credentialSubLabel");
                    Assert.Equal(string.Empty, subLabel.Text);

                    Invoke(env.Form, "OnCredentialButtonClicked", credBtn, EventArgs.Empty);
                    Assert.Equal("s3cr3t-pass", gateway.LastWrittenText);
                    Assert.Equal("运维账号", (string)Field(env.Form, "_copiedCredentialTitle"));
                    Assert.Equal(30, (int)Field(env.Form, "_credentialCountdownSeconds"));
                    Assert.Equal(UiTheme.AccentSoftColor, credBtn.BackColor);
                    Assert.Equal(UiTheme.AccentColor, credBtn.ForeColor);
                    Assert.Equal("已复制，30 秒后清空", subLabel.Text);

                    Invoke(env.Form, "OnCredentialCountdownTick", null, EventArgs.Empty);
                    Assert.Equal(29, (int)Field(env.Form, "_credentialCountdownSeconds"));
                    Assert.Equal("已复制，29 秒后清空", subLabel.Text);

                    Invoke(env.Form, "OnClipboardCleared");
                    Assert.Null((string)Field(env.Form, "_copiedCredentialTitle"));
                    Assert.Equal(0, (int)Field(env.Form, "_credentialCountdownSeconds"));
                    Assert.Equal(Color.White, credBtn.BackColor);
                    Assert.Equal(string.Empty, subLabel.Text);

                    Invoke(env.Form, "OnCredentialButtonClicked", credBtn, EventArgs.Empty);
                    Assert.Equal("运维账号", (string)Field(env.Form, "_copiedCredentialTitle"));
                    Invoke(env.Form, "OnClipboardClearSkipped");
                    Assert.Null((string)Field(env.Form, "_copiedCredentialTitle"));
                    Assert.Equal(0, (int)Field(env.Form, "_credentialCountdownSeconds"));
                    Assert.Equal(Color.White, credBtn.BackColor);
                }
            });

            TestRunner.Run("UI：内置弹窗（进度恢复、重置确认、完成提示）在 200% 缩放下字体与尺寸放大", () =>
            {
                Console.WriteLine("[START] 内置弹窗缩放");
                try
                {
                    UiTheme.UseFontScale(2.0);
                    using (var recovery = new ProgressRecoveryDialog("D:\\progress.json", "坏文件"))
                    using (var reset = new ResetRoundConfirmDialog())
                    using (var complete = new CompletionPromptDialog(false, true, null))
                    {
                        Form[] dialogs = { recovery, reset, complete };
                        for (int i = 0; i < dialogs.Length; i++)
                        {
                            Form dlg = dialogs[i];
                            int baseWidth = dlg.ClientSize.Width;
                            UiTheme.ScaleLayout(dlg, 2.0);
                            Assert.True(dlg.Font.Size >= 20f);
                            Assert.Equal(baseWidth * 2, dlg.ClientSize.Width);
                        }
                    }
                }
                finally
                {
                    UiTheme.UseFontScale(1.0);
                }
            });

            TestRunner.Run("UI-Main：暖石色板、矢量图标无尾随符号、计划卡不裁切、Waiting 琥珀态与 SideMargin 对齐", () =>
            {
                AppConfig cfg = MainEnv.BuildDefaultConfig();
                cfg.PlanModeEnabled = true;
                cfg.HideVisitedDevices = true;

                using (var env = new MainEnv(cfg))
                {
                    env.Form.Show();
                    Application.DoEvents();

                    Assert.Equal(UiTheme.PageBackColor, env.Form.BackColor);

                    var openBtn = (Button)Field(env.Form, "_openFolderButton");
                    Assert.Equal("打开巡检文件夹", openBtn.Text);
                    Assert.Equal(UiTheme.AccentColor, openBtn.BackColor);

                    var titleLabel = (Label)Field(env.Form, "_titleLabel");
                    var planCard = (Panel)Field(env.Form, "_planCardPanel");
                    var planActionPanel = (Panel)Field(env.Form, "_planActionPanel");
                    Assert.Equal(titleLabel.Left, planCard.Left);
                    Assert.True(planCard.Height >= planActionPanel.Bottom + 8,
                        "计划卡底部操作区不应被裁切或缺少留白：cardHeight=" + planCard.Height + ", actionBottom=" + planActionPanel.Bottom);

                    var credArrow = (Label)Field(env.Form, "_credentialArrowLabel");
                    var devArrow = (Label)Field(env.Form, "_deviceArrowLabel");
                    Assert.Equal(string.Empty, credArrow.Text);
                    Assert.Equal(string.Empty, devArrow.Text);

                    var rows = (IList)Field(env.Form, "_deviceRows");
                    Assert.Equal(3, rows.Count);
                    var initialPanel = (Panel)Field(rows[0], "Panel");
                    Assert.True(initialPanel.TabStop);

                    // 开启 HideVisitedDevices 时激活核心A进入 Waiting，核心A 仍保持可见并展示 Waiting 琥珀色强调，且计数不变；
                    // 右键取消选窗后保持未点且不改变未点设备数；只有保存成功后才标记“已点过”并隐藏。
                    Invoke(env.Form, "OnDeviceRowActivated", "核心A");
                    Application.DoEvents();
                    var sm = (PanelStateMachine)Field(env.Form, "_stateMachine");
                    var session = (InspectionSessionController)Field(env.Form, "_session");
                    var waitingRows = (IList)Field(env.Form, "_deviceRows");
                    Assert.Equal(3, waitingRows.Count);
                    Assert.Equal(0, session.Plan.VisitedCount);
                    object waitingRow = waitingRows[0];
                    var waitingPanel = (Panel)Field(waitingRow, "Panel");
                    var waitingName = (Label)Field(waitingRow, "NameLabel");
                    var waitingStatus = (Label)Field(waitingRow, "StatusLabel");
                    Assert.False(waitingPanel.IsDisposed);
                    Assert.Equal("核心A", (string)Field(waitingRow, "Device"));
                    Assert.Equal(UiTheme.WarningSoftColor, waitingPanel.BackColor);
                    Assert.Equal("等待选窗", waitingStatus.Text);
                    Assert.Equal(UiTheme.WarningColor, waitingStatus.ForeColor);
                    Assert.Equal(UiTheme.WarningColor, waitingName.ForeColor);

                    // 右键/Esc 取消选窗回到 Idle，验证计数仍为 0 且状态回到“待截图”
                    env.Hooks.RaiseCancelRequested();
                    Application.DoEvents();
                    Assert.Equal(PanelState.Idle, sm.State);
                    Assert.Equal(0, session.Plan.VisitedCount);
                    var rowsAfterCancel = (IList)Field(env.Form, "_deviceRows");
                    Assert.Equal(3, rowsAfterCancel.Count);
                    Assert.Equal("待截图", ((Label)Field(rowsAfterCancel[0], "StatusLabel")).Text);

                    // 完整截图并保存成功后隐藏核心A，展开“查看已点过”验证“已点过”和浅绿背景
                    Invoke(env.Form, "OnDeviceRowActivated", "核心A");
                    var token = (InspectionPlanToken)Field(env.Form, "_waitingPlanToken");
                    Assert.True(sm.BeginCapture());
                    Type reqType = typeof(MainForm).GetNestedType("CaptureRequest", BindingFlags.NonPublic);
                    object req = Activator.CreateInstance(reqType);
                    SetField(req, "Device", "核心A");
                    SetField(req, "RootPath", cfg.RootPath);
                    SetField(req, "Date", env.Clock.Now);
                    SetField(req, "RequestId", sm.CurrentRequestId);
                    SetField(req, "FolderPattern", cfg.FolderNamePattern);
                    SetField(req, "PlanToken", token);
                    string saveDir = Path.Combine(env.TempDir, "Shots", "巡检-2026-11-14");
                    Directory.CreateDirectory(saveDir);
                    string saveFile = Path.Combine(saveDir, "核心A.png");
                    File.WriteAllText(saveFile, "img");
                    using (var okBmp = new Bitmap(8, 8))
                    {
                        Invoke(env.Form, "OnSaveCompleted",
                            new StorageSaveResult { Saved = true, FullPath = saveFile },
                            okBmp,
                            req,
                            true,
                            (int)Field(env.Form, "_pendingGeneration"));
                    }
                    Application.DoEvents();
                    Assert.Equal(1, session.Plan.VisitedCount);
                    Assert.Equal(2, ((IList)Field(env.Form, "_deviceRows")).Count);

                    Invoke(env.Form, "OnToggleShowVisitedClicked", null, EventArgs.Empty);
                    Application.DoEvents();
                    var rowsAfter = (IList)Field(env.Form, "_deviceRows");
                    Assert.Equal(3, rowsAfter.Count);
                    object visitedRow = rowsAfter[0];
                    var visitedPanel = (Panel)Field(visitedRow, "Panel");
                    var visitedStatus = (Label)Field(visitedRow, "StatusLabel");
                    Assert.False(visitedPanel.IsDisposed);
                    Assert.Equal("已点过", visitedStatus.Text);
                    Assert.Equal(UiTheme.SuccessColor, visitedStatus.ForeColor);
                    Assert.Equal(UiTheme.VisitedBackColor, visitedPanel.BackColor);
                }
            });

            TestRunner.Run("UI-Main：最后一台进入 Waiting 后右键取消不计入已点过且不触发完成弹窗", () =>
            {
                AppConfig cfg = MainEnv.BuildDefaultConfig();
                cfg.PlanModeEnabled = true;
                cfg.HideVisitedDevices = true;
                cfg.Devices = new List<string> { "核心A" };

                using (var env = new MainEnv(cfg))
                {
                    env.Form.Show();
                    Application.DoEvents();

                    var sm = (PanelStateMachine)Field(env.Form, "_stateMachine");
                    var session = (InspectionSessionController)Field(env.Form, "_session");
                    var countLabel = (Label)Field(env.Form, "_planCountLabel");
                    var feedback = (Label)Field(env.Form, "_feedbackLabel");

                    Invoke(env.Form, "OnDeviceRowActivated", "核心A");
                    Assert.Equal(PanelState.Waiting, sm.State);
                    Assert.Equal("0 / 1", countLabel.Text);

                    env.Hooks.RaiseCancelRequested();
                    Application.DoEvents();

                    Assert.Equal(PanelState.Idle, sm.State);
                    Assert.Equal(0, session.Plan.VisitedCount);
                    Assert.Equal("0 / 1", countLabel.Text);
                    Assert.False(session.Plan.CompletionPrompted);
                    Assert.Equal("已取消选窗", feedback.Text);
                    Assert.Equal(1, ((IList)Field(env.Form, "_deviceRows")).Count);
                }
            });

            TestRunner.Run("UI-Main：设备区显示待点台数、复用未变设备行、方向键切换焦点及高亮框安全隐藏", () =>
            {
                AppConfig cfg = MainEnv.BuildDefaultConfig();
                cfg.PlanModeEnabled = true;
                cfg.HideVisitedDevices = false;
                cfg.Devices = new List<string> { "核心A", "核心B" };

                using (var env = new MainEnv(cfg))
                {
                    env.Form.Show();
                    Application.DoEvents();

                    var deviceSub = (Label)Field(env.Form, "_deviceSubLabel");
                    Assert.Equal("待点 2 台", deviceSub.Text);

                    var rowsBefore = (IList)Field(env.Form, "_deviceRows");
                    var firstPanelBefore = (Panel)Field(rowsBefore[0], "Panel");
                    var secondPanelBefore = (Panel)Field(rowsBefore[1], "Panel");

                    // 方向键上下切换焦点
                    firstPanelBefore.Focus();
                    Invoke(env.Form, "FocusAdjacentDeviceRow", "核心A", 1);
                    Assert.True(object.ReferenceEquals(env.Form.ActiveControl, secondPanelBefore),
                        "方向键下移后应将表单 ActiveControl 切至下一台设备");

                    // 标记核心A后重建：名单未变时直接复用既有行控件而不释放重建
                    var sm = (PanelStateMachine)Field(env.Form, "_stateMachine");
                    var session = (InspectionSessionController)Field(env.Form, "_session");
                    Invoke(env.Form, "OnDeviceRowActivated", "核心A");
                    var token = (InspectionPlanToken)Field(env.Form, "_waitingPlanToken");
                    Assert.True(sm.BeginCapture());
                    Assert.True(sm.CompleteCapture());
                    string err;
                    Assert.True(session.RecordSaveSucceeded("核心A", Path.Combine(env.TempDir, "Shots", "核心A.png"), token, out err));
                    Invoke(env.Form, "OnStateChanged");
                    Invoke(env.Form, "RebuildDeviceButtons");
                    Application.DoEvents();

                    var rowsAfter = (IList)Field(env.Form, "_deviceRows");
                    Assert.True(object.ReferenceEquals(firstPanelBefore, Field(rowsAfter[0], "Panel")),
                        "设备名单未变化时应复用既有设备行控件");
                    Assert.False(firstPanelBefore.IsDisposed);
                    Assert.Equal("待点 1 台", deviceSub.Text);

                    // 验证 TargetHighlightOverlay 镂空显示与退出隐藏安全无异常
                    using (var overlay = new TargetHighlightOverlay())
                    {
                        overlay.ShowTarget(new Rectangle(20, 20, 300, 200), 3);
                        Assert.True(overlay.Visible);
                        Assert.NotNull(overlay.Region);
                        overlay.HideOverlay();
                        Assert.False(overlay.Visible);
                    }
                }
            });
        }
    }
}
