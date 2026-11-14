using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using InspectionWidget;
using InspectionWidget.Config;

namespace InspectionWidget.Tests
{
    /// <summary>
    /// 真实 WinForms 控件回归；仅在 --windows-ui 且 Windows STA 线程执行。
    /// 默认纯逻辑入口不会把这些用例计为通过。
    /// </summary>
    public static class SettingsUiTests
    {
        public const int CaseCount = 13;

        public static void RunAll()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            TestRunner.Run("UI：设置窗五页与关键按钮/网格可创建", () =>
            {
                Console.WriteLine("[START] 设置窗五页与关键按钮/网格可创建");
                using (var env = new UiEnv())
                {
                    SettingsForm form = env.Form;
                    Assert.NotNull(env.Field(form, "_deviceEditor"));
                    Assert.NotNull(env.Field(form, "_credentialRowsPanel"));
                    Assert.NotNull(env.Field(form, "_feedbackLabel"));
                    Assert.NotNull(env.Field(form, "_applyButton"));
                    Assert.NotNull(env.Field(form, "_saveButton"));
                    var panels = (Array)env.Field(form, "_groupPanels");
                    Assert.Equal(5, panels.Length);
                    var buttons = (Array)env.Field(form, "_navButtons");
                    Assert.Equal(5, buttons.Length);
                    Assert.Equal("应用设置", ((Button)env.Field(form, "_applyButton")).AccessibleName);
                    Assert.Equal("保存设置并关闭", ((Button)env.Field(form, "_saveButton")).AccessibleName);
                    Assert.Equal("设备列表", ((DeviceEditorControl)env.Field(form, "_deviceEditor")).Grid.AccessibleName);
                }
            });

            TestRunner.Run("UI：新增空白设备行后应用失败，Saved 不触发且草稿保留", () =>
            {
                Console.WriteLine("[START] 新增空白设备行");
                using (var env = new UiEnv())
                {
                    Console.WriteLine("[STEP] UiEnv 已显示");
                    int saved = 0;
                    env.Form.Saved += delegate { saved++; };
                    var editor = (DeviceEditorControl)env.Field(env.Form, "_deviceEditor");
                    Console.WriteLine("[STEP] 准备调用 AddDevice");
                    env.Invoke(editor, "AddDevice");
                    Console.WriteLine("[STEP] AddDevice 返回");
                    Assert.Equal(3, ((SettingsDraft)env.Field(env.Form, "_draft")).Devices.Count);
                    Assert.Equal(3, editor.Grid.Rows.Count);
                    Assert.Equal("1", editor.Grid.Rows[0].HeaderCell.Value as string);
                    Assert.Equal("2", editor.Grid.Rows[1].HeaderCell.Value as string);
                    Assert.Equal("3", editor.Grid.Rows[2].HeaderCell.Value as string);

                    Console.WriteLine("[STEP] 准备调用 ApplyInternal");
                    bool applied = (bool)env.Invoke(env.Form, "ApplyInternal", false);
                    Console.WriteLine("[STEP] ApplyInternal 返回：" + applied);
                    Assert.False(applied, "不应应用成功，反馈：" + env.FeedbackText());
                    Assert.Equal(0, saved);
                    Assert.True(env.Config.Devices.Count >= 1);
                    Assert.True(((Label)env.Field(env.Form, "_feedbackLabel")).Text.Contains("第"));
                }
            });

            TestRunner.Run("UI：改名后直接应用，LastDevice 跟随且一次提交一次 Saved", () =>
            {
                Console.WriteLine("[START] 改名后直接应用");
                using (var env = new UiEnv())
                {
                    int saved = 0;
                    env.Form.Saved += delegate { saved++; };
                    var draft = (SettingsDraft)env.Field(env.Form, "_draft");
                    draft.LastDevice = "设备A";
                    var editor = (DeviceEditorControl)env.Field(env.Form, "_deviceEditor");
                    DataGridView grid = editor.Grid;
                    grid.CurrentCell = grid.Rows[0].Cells[0];
                    grid.BeginEdit(true);
                    Application.DoEvents();
                    var box = grid.EditingControl as TextBox;
                    Assert.NotNull(box, "应进入真实文本框编辑态");
                    box.Text = "新名";
                    grid.EndEdit();

                    bool applied = (bool)env.Invoke(env.Form, "ApplyInternal", false);
                    Assert.True(applied, "应用失败，反馈：" + env.FeedbackText());
                    Assert.Equal(1, saved);
                    Assert.Equal("新名", draft.Devices[0].Name);
                    Assert.Equal("新名", draft.LastDevice);
                    Assert.Equal("新名", env.Form.ResultConfig.Devices[0]);
                    Assert.Equal("新名", env.Form.ResultConfig.LastDevice);
                }
            });

            TestRunner.Run("UI：设备改名输入后按 Esc 取消，草稿与保存仍为原名", () =>
            {
                Console.WriteLine("[START] 设备改名 Esc 取消");
                using (var env = new UiEnv())
                {
                    var draft = (SettingsDraft)env.Field(env.Form, "_draft");
                    draft.LastDevice = "设备A";
                    var editor = (DeviceEditorControl)env.Field(env.Form, "_deviceEditor");
                    DataGridView grid = editor.Grid;
                    grid.CurrentCell = grid.Rows[0].Cells[0];
                    grid.BeginEdit(true);
                    Application.DoEvents();
                    var box = grid.EditingControl as TextBox;
                    Assert.NotNull(box, "应进入真实文本框编辑态");

                    box.Text = "临时改名";
                    Assert.True(env.SendEscape(), "编辑态 Esc 应由设置窗处理");
                    Application.DoEvents();
                    Console.WriteLine("[STEP] 第一次取消：" + editor.LastCancelDiagnostic);
                    Assert.False(grid.IsCurrentCellInEditMode,
                        "Esc 后必须退出编辑态：" + editor.LastCancelDiagnostic);
                    Assert.Equal("设备A", draft.Devices[0].Name);
                    Assert.Equal("设备A", draft.LastDevice);
                    Assert.Equal("设备A", env.Load().Config.Devices[0]);

                    box = null;
                    grid.CurrentCell = grid.Rows[0].Cells[0];
                    grid.BeginEdit(true);
                    Application.DoEvents();
                    box = grid.EditingControl as TextBox;
                    Assert.NotNull(box, "非法输入前应能再次进入编辑态");
                    box.Text = "CON";
                    Assert.True(env.SendEscape(), "非法输入时 Esc 也应由设置窗处理");
                    Application.DoEvents();
                    Console.WriteLine("[STEP] 第二次取消：" + editor.LastCancelDiagnostic);
                    Assert.False(grid.IsCurrentCellInEditMode,
                        "非法输入 Esc 也应退出编辑态：" + editor.LastCancelDiagnostic);
                    Assert.Equal("设备A", draft.Devices[0].Name);
                    Assert.Equal("设备A", draft.LastDevice);
                    Assert.Equal("设备A", env.Load().Config.Devices[0]);

                    bool applied = (bool)env.Invoke(env.Form, "ApplyInternal", false);
                    Assert.True(applied, "应用失败，反馈：" + env.FeedbackText());
                    Assert.Equal("设备A", env.Load().Config.Devices[0]);
                }
            });

            TestRunner.Run("UI：重复名阻止应用，原草稿输入保留且磁盘不变", () =>
            {
                Console.WriteLine("[START] 重复名阻止应用");
                using (var env = new UiEnv())
                {
                    int saved = 0;
                    env.Form.Saved += delegate { saved++; };
                    var draft = (SettingsDraft)env.Field(env.Form, "_draft");
                    draft.Devices[0].Name = "设备B";
                    bool applied = (bool)env.Invoke(env.Form, "ApplyInternal", false);
                    Assert.False(applied, "不应应用成功，反馈：" + env.FeedbackText());
                    Assert.Equal(0, saved);
                    Assert.Equal("设备B", draft.Devices[0].Name);
                    Assert.Equal("设备A", env.Load().Config.Devices[0]);
                }
            });

            TestRunner.Run("UI：非法设备名阻止切页，不丢输入", () =>
            {
                Console.WriteLine("[START] 非法设备名阻止切页");
                using (var env = new UiEnv())
                {
                    var draft = (SettingsDraft)env.Field(env.Form, "_draft");
                    draft.Devices[0].Name = "CON";
                    env.Invoke(env.Form, "OnNavClicked", 1);
                    var panels = (Array)env.Field(env.Form, "_groupPanels");
                    Assert.True(((Control)panels.GetValue(0)).Visible);
                    Assert.False(((Control)panels.GetValue(1)).Visible);
                    Assert.Equal("CON", draft.Devices[0].Name);
                }
            });

            TestRunner.Run("UI：应用后继续修改再关闭，磁盘保留最后一次应用", () =>
            {
                Console.WriteLine("[START] 应用后继续修改再关闭");
                using (var env = new UiEnv())
                {
                    var draft = (SettingsDraft)env.Field(env.Form, "_draft");
                    bool first = (bool)env.Invoke(env.Form, "ApplyInternal", false);
                    Assert.True(first);
                    string savedRoot = env.Form.ResultConfig.RootPath;

                    draft.RootPath = "E:\\以后再说";
                    env.Invoke(env.Form, "MarkDirty");
                    env.CloseWithoutPrompt();
                    Assert.Equal(savedRoot, env.Load().Config.RootPath);
                }
            });

            TestRunner.Run("UI：凭据清除可撤销，NeedsReset 保留到提交结果", () =>
            {
                Console.WriteLine("[START] 凭据清除可撤销");
                using (var env = new UiEnv())
                {
                    var draft = (SettingsDraft)env.Field(env.Form, "_draft");
                    Assert.Equal(1, draft.Credentials.Count);
                    draft.Credentials[0].OriginalNeedsReset = true;
                    var rows = (IList)env.Field(env.Form, "_credentialRows");
                    object row = rows[0];
                    env.Invoke(env.Form, "ClearCredential", row);
                    Assert.True(draft.Credentials[0].Cleared);
                    Assert.True(draft.Credentials[0].OriginalNeedsReset);
                    env.Invoke(env.Form, "UndoClearCredential", row);
                    Assert.False(draft.Credentials[0].Cleared);

                    bool applied = (bool)env.Invoke(env.Form, "ApplyInternal", false);
                    Assert.True(applied, "应用失败，反馈：" + env.FeedbackText());
                    Assert.True(env.Form.ResultConfig.Credentials[0].NeedsReset);
                }
            });

            TestRunner.Run("UI：导入前校验保留当前凭据明文输入", () =>
            {
                Console.WriteLine("[START] 导入前校验保留凭据输入");
                using (var env = new UiEnv())
                {
                    var rows = (IList)env.Field(env.Form, "_credentialRows");
                    object row = rows[0];
                    TextBox name = (TextBox)env.Field(row, "NameBox");
                    TextBox secret = (TextBox)env.Field(row, "SecretBox");
                    name.Text = "临时改名";
                    secret.Text = "当前明文";
                    bool valid = (bool)env.Invoke(env.Form, "ValidateCredentialInputsForImport");
                    Assert.True(valid);
                    Assert.Equal("临时改名", name.Text);
                    Assert.Equal("当前明文", secret.Text);
                    Assert.True(object.ReferenceEquals(row,
                        ((IList)env.Field(env.Form, "_credentialRows"))[0]));
                }
            });

            TestRunner.Run("UI：设置窗口按 200% 初始化缩放且布局已应用", () =>
            {
                Console.WriteLine("[START] 200% 初始化缩放");
                using (var env = new UiEnv(200))
                {
                    Assert.Equal(2.0,
                        (double)env.Field(env.Form, "_uiScaleFactor"));
                    Assert.True((bool)env.Field(env.Form, "_layoutScaleApplied"));
                    var editor = (DeviceEditorControl)env.Field(env.Form, "_deviceEditor");
                    Assert.True(editor.Grid.Columns[0].Width >= 1200,
                        "设备名称列应随 200% 档位放大");
                }
            });

            TestRunner.Run("UI：设置窗与导入预览不叠加系统 DPI 自动缩放", () =>
            {
                Console.WriteLine("[START] 系统 DPI 不叠加");
                using (var env = new UiEnv(150, 1.5))
                {
                    Assert.Equal(AutoScaleMode.None, env.Form.AutoScaleMode);
                    Assert.Equal(2.25, (double)env.Field(env.Form, "_uiScaleFactor"));
                    ConfigImportDocument document;
                    string error;
                    Assert.True(ConfigJsonReader.TryParse(
                        "{\"Format\":\"InspectionWidget.Settings\",\"Version\":1,"
                        + "\"CredentialMode\":\"Excluded\",\"Config\":{\"Devices\":[]}}",
                        out document, out error), error);
                    using (var preview = new ConfigTransferForm(
                        document,
                        env.Config,
                        new FakeProtector(),
                        2.25))
                    {
                        Assert.Equal(AutoScaleMode.None, preview.AutoScaleMode);
                        var changes = (TextBox)env.Field(preview, "_deviceChangeBox");
                        Assert.Equal("设备变化预览", changes.AccessibleName);
                        Assert.True(changes.ReadOnly);
                        Assert.True(changes.Multiline);
                        Assert.Equal(ScrollBars.Vertical, changes.ScrollBars);
                    }
                }
            });

            TestRunner.Run("UI：凭据列表滚动后上移与删除行位置与滚动偏移对齐且不漂移", () =>
            {
                Console.WriteLine("[START] 凭据列表 AutoScroll 位置对齐");
                using (var env = new UiEnv())
                {
                    env.Invoke(env.Form, "OnNavClicked", 1);
                    for (int i = 0; i < 8; i++)
                        env.Invoke(env.Form, "AddCredential");

                    var panel = (Panel)env.Field(env.Form, "_credentialRowsPanel");
                    var rows = (IList)env.Field(env.Form, "_credentialRows");
                    Assert.True(rows.Count >= 8);
                    panel.PerformLayout();
                    panel.AutoScrollPosition = new Point(0, 140);
                    Application.DoEvents();
                    int scrollYBefore = panel.AutoScrollPosition.Y;
                    Assert.True(scrollYBefore < 0, "应出现垂直滚动偏移");

                    env.Invoke(env.Form, "MoveCredential", rows[4], -1);
                    Application.DoEvents();
                    int scrollYAfterMove = panel.AutoScrollPosition.Y;
                    Assert.True(scrollYAfterMove < 0);
                    Control firstAfterMove = (Control)env.Field(rows[0], "Panel");
                    Control secondAfterMove = (Control)env.Field(rows[1], "Panel");
                    Assert.Equal(scrollYAfterMove, firstAfterMove.Top);
                    int step = secondAfterMove.Top - firstAfterMove.Top;
                    Assert.True(step > firstAfterMove.Height);
                    for (int i = 0; i < rows.Count; i++)
                    {
                        Control p = (Control)env.Field(rows[i], "Panel");
                        Assert.Equal(scrollYAfterMove + i * step, p.Top);
                    }

                    while (rows.Count > 1)
                        env.Invoke(env.Form, "DeleteCredential", rows[rows.Count - 1]);
                    Application.DoEvents();
                    Assert.Equal(0, panel.AutoScrollPosition.Y);
                    Assert.Equal(0, ((Control)env.Field(rows[0], "Panel")).Top);
                }
            });

            TestRunner.Run("UI：快捷链接滚动后上移与删除行位置对齐且缩减时钳制滚动", () =>
            {
                Console.WriteLine("[START] 快捷链接 AutoScroll 位置对齐");
                using (var env = new UiEnv())
                {
                    env.Invoke(env.Form, "OnNavClicked", 2);
                    for (int i = 0; i < 10; i++)
                        env.Invoke(env.Form, "AddLink");

                    var panel = (Panel)env.Field(env.Form, "_linkRowsPanel");
                    var rows = (IList)env.Field(env.Form, "_linkRows");
                    Assert.True(rows.Count >= 10);
                    panel.PerformLayout();
                    panel.AutoScrollPosition = new Point(0, 120);
                    Application.DoEvents();
                    int scrollYBefore = panel.AutoScrollPosition.Y;
                    Assert.True(scrollYBefore < 0, "快捷链接应出现垂直滚动偏移");

                    env.Invoke(env.Form, "MoveLink", rows[5], -1);
                    Application.DoEvents();
                    int scrollYAfterMove = panel.AutoScrollPosition.Y;
                    Assert.True(scrollYAfterMove < 0);
                    Control firstAfterMove = (Control)env.Field(rows[0], "Panel");
                    Control secondAfterMove = (Control)env.Field(rows[1], "Panel");
                    Assert.Equal(scrollYAfterMove, firstAfterMove.Top);
                    int step = secondAfterMove.Top - firstAfterMove.Top;
                    Assert.True(step > firstAfterMove.Height);
                    for (int i = 0; i < rows.Count; i++)
                    {
                        Control p = (Control)env.Field(rows[i], "Panel");
                        Assert.Equal(scrollYAfterMove + i * step, p.Top);
                    }

                    while (rows.Count > 1)
                        env.Invoke(env.Form, "DeleteLink", rows[rows.Count - 1]);
                    Application.DoEvents();
                    Assert.Equal(0, panel.AutoScrollPosition.Y);
                    Assert.Equal(0, ((Control)env.Field(rows[0], "Panel")).Top);
                }
            });
        }

        private sealed class UiEnv : IDisposable
        {
            private readonly string _dir;
            private readonly ConfigStore _store;
            public AppConfig Config { get; private set; }
            public SettingsForm Form { get; private set; }

            public UiEnv(int scalePercent = 100, double systemDpiScale = 1.0)
            {
                _dir = Path.Combine(Path.GetTempPath(),
                    "iw-ui-tests-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_dir);
                _store = new ConfigStore(new ConfigPaths
                {
                    AuthoritativePath = Path.Combine(_dir, "config.json"),
                    IsFallback = false
                }, delegate { return true; });
                Config = AppConfig.CreateDefault();
                Config.Devices.Clear();
                Config.Devices.Add("设备A");
                Config.Devices.Add("设备B");
                Config.RootPath = "D:\\shots";
                Config.UiScalePercent = scalePercent;
                Config.Credentials.Add(new CredentialEntry
                {
                    Title = "凭据A",
                    EncryptedSecret = "QUJD"
                });
                Assert.True(_store.Save(Config).Saved);
                _store.Load();
                Form = new SettingsForm(Config, _store, new FakeProtector(), systemDpiScale);
                Form.ShowInTaskbar = false;
                Form.StartPosition = FormStartPosition.Manual;
                Form.Location = new Point(0, 0);
                Form.Show();
                Application.DoEvents();
            }

            public object Field(object target, string name)
            {
                FieldInfo field = target.GetType().GetField(name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null) throw new AssertException("找不到字段：" + name);
                return field.GetValue(target);
            }

            public object Invoke(object target, string name, params object[] args)
            {
                MethodInfo method = target.GetType().GetMethod(name,
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (method == null) throw new AssertException("找不到方法：" + name);
                return method.Invoke(target, args);
            }

            public LoadResult Load()
            {
                return new ConfigStore(new ConfigPaths
                {
                    AuthoritativePath = Path.Combine(_dir, "config.json"),
                    IsFallback = false
                }, delegate { return true; }).Load();
            }

            public string FeedbackText()
            {
                Label label = (Label)Field(Form, "_feedbackLabel");
                return label == null ? "(无反馈标签)" : label.Text;
            }

            public bool SendEscape()
            {
                var message = new Message
                {
                    Msg = 0x0100,
                    WParam = (IntPtr)Keys.Escape
                };
                MethodInfo method = typeof(SettingsForm).GetMethod("ProcessCmdKey",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (method == null) throw new AssertException("找不到 ProcessCmdKey");
                return (bool)method.Invoke(Form, new object[] { message, Keys.Escape });
            }

            public void CloseWithoutPrompt()
            {
                FieldInfo flag = Form.GetType().GetField("_closingAfterSave",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                flag.SetValue(Form, true);
                Form.Close();
            }

            public void Dispose()
            {
                if (Form != null && !Form.IsDisposed)
                {
                    CloseWithoutPrompt();
                    Form.Dispose();
                }
                try
                {
                    if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
                }
                catch
                {
                }
            }
        }
    }
}
