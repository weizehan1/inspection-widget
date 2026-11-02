# Review package Task 8: 376f84b..HEAD
a8b197b fix(stage-8): 未处理异常兜底与单实例互斥接管

 src/Config/ConfigStore.cs      |  13 ++++
 src/ErrorLog.cs                | 167 +++++++++++++++++++++++++++++++++++++++++
 src/MainForm.cs                |  31 ++++++++
 src/Program.cs                 | 111 ++++++++++++++++++++++++---
 src/Properties/AssemblyInfo.cs |   5 ++
 tests/ConfigStoreTests.cs      |  20 +++++
 tests/ErrorLogTests.cs         | 136 +++++++++++++++++++++++++++++++++
 tests/Program.cs               |   1 +
 8 files changed, 472 insertions(+), 12 deletions(-)

diff --git a/src/Config/ConfigStore.cs b/src/Config/ConfigStore.cs
index 6e3800c..04d51bf 100644
--- a/src/Config/ConfigStore.cs
+++ b/src/Config/ConfigStore.cs
@@ -219,10 +219,16 @@ namespace InspectionWidget.Config
             }
             catch (UnauthorizedAccessException ex)
             {
                 return new SaveResult { Saved = false, Error = "无权锁定配置文件，设置未保存：" + ex.Message };
             }
+            catch (Exception ex)
+            {
+                // 取锁段兜底（Task 8，Task 1 审查 Minor-3）：预期外异常同样
+                // 不穿透 Save，一律落为「未保存」结果，正式文件不动。
+                return new SaveResult { Saved = false, Error = "锁定配置文件异常，设置未保存：" + ex.Message };
+            }
 
             using (lockStream)
             {
                 string tmp = Path.Combine(dir, "config." + Guid.NewGuid().ToString("N") + ".tmp");
                 try
@@ -242,10 +248,17 @@ namespace InspectionWidget.Config
                 }
                 catch (UnauthorizedAccessException ex)
                 {
                     return new SaveResult { Saved = false, Error = "写入配置被拒绝，设置未保存：" + ex.Message };
                 }
+                catch (Exception ex)
+                {
+                    // 写段兜底（Task 8，Task 1 审查 Minor-3）：序列化等预期外
+                    // 异常（如 SerializationException）不许穿透 Save，一律落为
+                    // 「未保存」结果；临时文件仍由 finally 清理，正式文件不动。
+                    return new SaveResult { Saved = false, Error = "写入配置异常，设置未保存：" + ex.Message };
+                }
                 finally
                 {
                     try
                     {
                         if (File.Exists(tmp))
diff --git a/src/ErrorLog.cs b/src/ErrorLog.cs
new file mode 100644
index 0000000..1e95bb9
--- /dev/null
+++ b/src/ErrorLog.cs
@@ -0,0 +1,167 @@
+using System;
+using System.IO;
+using System.Text;
+
+namespace InspectionWidget
+{
+    /// <summary>
+    /// 全局异常日志（Task 8）：追加写 error.log——exe 同目录优先，不可写时回退
+    /// %LocalAppData%\InspectionWidget\error.log，两处都失败则静默放弃。
+    /// 只记时间、环节名、异常类型/消息与堆栈：调用方只传环节名，本类不接收
+    /// 凭据明文、截图内容等任何业务数据。Write 内部绝不抛异常（异常处理里
+    /// 再抛错会掩盖原始异常），全路径 try/catch 吞掉。
+    /// </summary>
+    public static class ErrorLog
+    {
+        private const string LogFileName = "error.log";
+        private const string FallbackDirName = "InspectionWidget";
+
+        private static readonly object Sync = new object();
+
+        // 测试注入点（内部）：非 null 时覆盖主/回退目录，测后须 Reset 恢复。
+        private static string _primaryDirOverride;
+        private static string _fallbackDirOverride;
+
+        // 最近一次实际写成功的日志路径；从未写过时为 null，CurrentLogPath 退回主路径。
+        private static string _lastWrittenPath;
+
+        /// <summary>实际写入路径：已有成功写入时为该次落点，否则为主路径（exe 同目录）。</summary>
+        public static string CurrentLogPath
+        {
+            get
+            {
+                lock (Sync)
+                {
+                    if (_lastWrittenPath != null)
+                        return _lastWrittenPath;
+                }
+                return PrimaryLogPath;
+            }
+        }
+
+        private static string PrimaryDir
+        {
+            get
+            {
+                return _primaryDirOverride != null
+                    ? _primaryDirOverride
+                    : AppDomain.CurrentDomain.BaseDirectory;
+            }
+        }
+
+        private static string FallbackDir
+        {
+            get
+            {
+                return _fallbackDirOverride != null
+                    ? _fallbackDirOverride
+                    : Path.Combine(
+                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
+                        FallbackDirName);
+            }
+        }
+
+        private static string PrimaryLogPath
+        {
+            get { return Path.Combine(PrimaryDir, LogFileName); }
+        }
+
+        private static string FallbackLogPath
+        {
+            get { return Path.Combine(FallbackDir, LogFileName); }
+        }
+
+        /// <summary>测试注入：覆盖主/回退目录（任一传 null 表示该项不覆盖）。</summary>
+        internal static void SetDirectoriesForTests(string primaryDir, string fallbackDir)
+        {
+            lock (Sync)
+            {
+                _primaryDirOverride = primaryDir;
+                _fallbackDirOverride = fallbackDir;
+                _lastWrittenPath = null;
+            }
+        }
+
+        /// <summary>测试收尾：清除注入目录与落点记录，恢复默认路径。</summary>
+        internal static void ResetDirectoriesForTests()
+        {
+            lock (Sync)
+            {
+                _primaryDirOverride = null;
+                _fallbackDirOverride = null;
+                _lastWrittenPath = null;
+            }
+        }
+
+        /// <summary>
+        /// 记一条异常日志。context 为环节名（如「UI线程」「非UI线程」），
+        /// 不得包含凭据/图像等业务数据。本方法绝不抛异常。
+        /// </summary>
+        public static void Write(string context, Exception ex)
+        {
+            try
+            {
+                string text = Format(context, ex);
+                lock (Sync)
+                {
+                    string primary = PrimaryLogPath;
+                    if (TryAppend(primary, text))
+                    {
+                        _lastWrittenPath = primary;
+                        return;
+                    }
+                    string fallback = FallbackLogPath;
+                    if (TryAppend(fallback, text))
+                    {
+                        _lastWrittenPath = fallback;
+                    }
+                    // 两处都写不进去：静默放弃，绝不在异常处理里再抛错。
+                }
+            }
+            catch
+            {
+                // 日志失败静默放弃（同上）。
+            }
+        }
+
+        private static string Format(string context, Exception ex)
+        {
+            var sb = new StringBuilder();
+            sb.Append('[');
+            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
+            sb.Append("] ");
+            sb.Append(string.IsNullOrEmpty(context) ? "未知环节" : context);
+            sb.Append(' ');
+            if (ex == null)
+            {
+                sb.Append("（无异常对象）");
+            }
+            else
+            {
+                sb.Append(ex.GetType().Name);
+                sb.Append(": ");
+                sb.Append(ex.Message);
+            }
+            sb.AppendLine();
+            if (ex != null && ex.StackTrace != null)
+                sb.AppendLine(ex.StackTrace);
+            return sb.ToString();
+        }
+
+        private static bool TryAppend(string path, string text)
+        {
+            try
+            {
+                string dir = Path.GetDirectoryName(path);
+                if (!string.IsNullOrEmpty(dir))
+                    Directory.CreateDirectory(dir);
+                File.AppendAllText(path, text, Encoding.UTF8);
+                return true;
+            }
+            catch
+            {
+                return false;
+            }
+        }
+    }
+}
diff --git a/src/MainForm.cs b/src/MainForm.cs
index 766e0e3..7983149 100644
--- a/src/MainForm.cs
+++ b/src/MainForm.cs
@@ -149,10 +149,11 @@ namespace InspectionWidget
                 {
                     _pendingImage.Dispose();
                     _pendingGeneration++;
                 }
                 if (_rootToolTip != null) _rootToolTip.Dispose();
+                if (_feedbackTimer != null) _feedbackTimer.Dispose();
                 if (_font13 != null) _font13.Dispose();
                 if (_font13Bold != null) _font13Bold.Dispose();
                 if (_font11 != null) _font11.Dispose();
             }
             base.Dispose(disposing);
@@ -1376,10 +1377,40 @@ namespace InspectionWidget
             _hooks.Stop();
             _hooks.Dispose();
             SaveWindowPosition();
         }
 
+        // ---------- 全局异常恢复（Task 8） ----------
+
+        /// <summary>
+        /// Program 的 Application.ThreadException 兜底调用：UI 线程出现未预料
+        /// 异常后，把面板恢复到待命——Waiting/Capturing 经状态机回 Idle 并走
+        /// OnStateChanged 集中点停钩；保存失败的待重试图（_pendingImage）刻意
+        /// 保留不释放，反馈行恢复「尚未保存，点击此处重试」常驻提示，用户仍可
+        /// 点反馈行重试或开始新截图（新截图时旧图按既有口径释放）。
+        /// 本方法在非 UI 线程被调用时先 BeginInvoke 切回 UI 线程。
+        /// </summary>
+        public void RecoverToIdle()
+        {
+            if (InvokeRequired)
+            {
+                BeginInvoke(new Action(RecoverToIdle));
+                return;
+            }
+
+            if (_stateMachine.State == PanelState.Waiting)
+                _stateMachine.Cancel();
+            else if (_stateMachine.State == PanelState.Capturing)
+                _stateMachine.FailCapture();
+            OnStateChanged(); // 回 Idle 集中点：刷新视觉 + 停钩（幂等）
+
+            if (_pendingImage != null && _pendingRequest != null)
+                ShowPersistentFeedback("尚未保存，点击此处重试", ErrorColor);
+            else
+                ShowFeedback("发生未预期异常，已恢复待命", true);
+        }
+
         // ---------- 保存（BestEffort） ----------
 
         private void SaveBestEffort()
         {
             SaveBestEffortResult();
diff --git a/src/Program.cs b/src/Program.cs
index d4c95ec..566da90 100644
--- a/src/Program.cs
+++ b/src/Program.cs
@@ -5,37 +5,124 @@ using System.Windows.Forms;
 
 namespace InspectionWidget
 {
     internal static class Program
     {
+        private const string MutexName = "InspectionWidget.SingleInstance";
+
         // DPI 兜底声明（本任务内联；Native 模块在 Task 3 才建立）
         [DllImport("user32.dll", SetLastError = true)]
         private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);
 
         [STAThread]
         private static void Main()
         {
+            // 单实例互斥（Task 0 审查 Minor-3 兜底）：首个实例异常退出后，
+            // 命名互斥被废弃，下一个实例在构造 Mutex 时会抛
+            // AbandonedMutexException——该异常的语义即「本线程已接管所有权」，
+            // 捕获后视为首次启动继续，不当作已有实例在运行而误退。
+            Mutex mutex = null;
             bool createdNew;
-            using (var mutex = new Mutex(true, "InspectionWidget.SingleInstance", out createdNew))
+            try
             {
-                if (!createdNew)
-                {
-                    return;
-                }
-
+                mutex = new Mutex(true, MutexName, out createdNew);
+            }
+            catch (AbandonedMutexException)
+            {
+                createdNew = true;
+                // 构造未完成的 Mutex 对象不能依赖：另开一个同名句柄把命名
+                // 互斥保活到本进程结束，保证后续实例仍能探到它已存在；
+                // 开句柄失败也只降级为无保活句柄，不阻断启动。
                 try
                 {
-                    // PerMonitorV2 = -4；失败忽略（旧系统无此 API 时靠 manifest 声明）
-                    SetProcessDpiAwarenessContext(new IntPtr(-4));
+                    mutex = new Mutex(false, MutexName);
                 }
                 catch
                 {
-                    // 忽略 DPI 设置失败
+                    mutex = null;
                 }
+            }
 
-                Application.EnableVisualStyles();
-                Application.SetCompatibleTextRenderingDefault(false);
-                Application.Run(new MainForm());
+            if (!createdNew)
+            {
+                if (mutex != null)
+                    mutex.Dispose();
+                return;
+            }
+
+            if (mutex != null)
+            {
+                using (mutex)
+                {
+                    RunApplication();
+                }
+            }
+            else
+            {
+                RunApplication();
+            }
+        }
+
+        private static void RunApplication()
+        {
+            try
+            {
+                // PerMonitorV2 = -4；失败忽略（旧系统无此 API 时靠 manifest 声明）
+                SetProcessDpiAwarenessContext(new IntPtr(-4));
+            }
+            catch
+            {
+                // 忽略 DPI 设置失败
+            }
+
+            // 全局异常兜底（Task 8）：UI 线程可恢复异常记日志并尝试让主面板
+            // 回到待命、保留待保存图，不退出；非 UI 线程未处理异常只尽力记
+            // 日志，IsTerminating 时不拦截、由系统终止。两处处理本身都不得
+            // 再抛异常（ErrorLog.Write 内部已全吞，恢复调用另有 try/catch）。
+            Application.ThreadException += OnThreadException;
+            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
+
+            Application.EnableVisualStyles();
+            Application.SetCompatibleTextRenderingDefault(false);
+            Application.Run(new MainForm());
+        }
+
+        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
+        {
+            ErrorLog.Write("UI线程", e.Exception);
+            try
+            {
+                MainForm mainForm = FindMainForm();
+                if (mainForm != null)
+                    mainForm.RecoverToIdle();
+            }
+            catch (Exception recoverEx)
+            {
+                // 恢复失败只记日志：处理完不退出，面板保持现状等用户处理。
+                ErrorLog.Write("UI线程恢复待命", recoverEx);
+            }
+        }
+
+        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
+        {
+            Exception ex = e.ExceptionObject as Exception;
+            if (ex == null)
+            {
+                ex = new Exception("非UI线程未处理异常（原始对象："
+                    + (e.ExceptionObject == null ? "null" : e.ExceptionObject.ToString()) + "）");
+            }
+            ErrorLog.Write("非UI线程", ex);
+            // IsTerminating 时进程即将被系统终止，这里不再拦截、不承诺恢复。
+        }
+
+        private static MainForm FindMainForm()
+        {
+            foreach (Form form in Application.OpenForms)
+            {
+                MainForm mainForm = form as MainForm;
+                if (mainForm != null)
+                    return mainForm;
             }
+            return null;
         }
     }
 }
diff --git a/src/Properties/AssemblyInfo.cs b/src/Properties/AssemblyInfo.cs
new file mode 100644
index 0000000..c97526d
--- /dev/null
+++ b/src/Properties/AssemblyInfo.cs
@@ -0,0 +1,5 @@
+using System.Runtime.CompilerServices;
+
+// 测试工程需要触达 ErrorLog 的内部目录注入点（SetDirectoriesForTests），
+// 仅向测试程序集开放内部成员，产品对外面不变。
+[assembly: InternalsVisibleTo("InspectionWidget.Tests")]
diff --git a/tests/ConfigStoreTests.cs b/tests/ConfigStoreTests.cs
index c01ee19..2be01fb 100644
--- a/tests/ConfigStoreTests.cs
+++ b/tests/ConfigStoreTests.cs
@@ -157,10 +157,30 @@ namespace InspectionWidget.Tests
                     Assert.True(File.Exists(ConfigFile(dir)));
                 }
                 finally { Cleanup(dir); }
             });
 
+            TestRunner.Run("存储：Save 对敌意配置不抛异常、只返回 SaveResult（序列化兜底口径，Task 8）", () =>
+            {
+                string dir = MakeTempDir();
+                try
+                {
+                    ConfigStore store = MakeStore(dir);
+                    AppConfig cfg = AppConfig.CreateDefault();
+                    // 非法枚举值：部分运行时的 DataContract 序列化会为此抛
+                    // SerializationException。兜底口径只要求：异常不穿透 Save，
+                    // 结果非空；Saved 时不必有 Error，未 Saved 时必须有 Error 摘要。
+                    // （本机 Mono 实测该值可正常序列化，故 Saved 两种结果均接受。）
+                    cfg.CaptureMode = (CaptureMode)999;
+                    SaveResult r = store.Save(cfg);
+                    Assert.NotNull(r);
+                    Assert.True(r.Saved || !string.IsNullOrEmpty(r.Error),
+                        "未保存时必须带错误摘要");
+                }
+                finally { Cleanup(dir); }
+            });
+
             TestRunner.Run("存储：凭据 NeedsReset 不落盘", () =>
             {
                 string dir = MakeTempDir();
                 try
                 {
diff --git a/tests/ErrorLogTests.cs b/tests/ErrorLogTests.cs
new file mode 100644
index 0000000..f4e8954
--- /dev/null
+++ b/tests/ErrorLogTests.cs
@@ -0,0 +1,136 @@
+using System;
+using System.IO;
+using InspectionWidget;
+
+namespace InspectionWidget.Tests
+{
+    /// <summary>
+    /// ErrorLog（Task 8）：目录注入点指向临时目录验证落盘与回退；
+    /// 不可写场景用「已存在的文件当父目录」稳定构造（跨平台一致失败）。
+    /// 每条用例测后 Reset 注入并清理临时目录。
+    /// </summary>
+    public static class ErrorLogTests
+    {
+        public static void RunAll()
+        {
+            TestRunner.Run("错误日志：写入主目录 → 文件存在且含环节名与异常类型、CurrentLogPath 指向主路径", () =>
+            {
+                string primary = MakeTempDir();
+                string fallback = MakeTempDir();
+                try
+                {
+                    ErrorLog.SetDirectoriesForTests(primary, fallback);
+                    ErrorLog.Write("UI线程", new InvalidOperationException("测试异常甲"));
+
+                    string path = Path.Combine(primary, "error.log");
+                    Assert.True(File.Exists(path), "error.log 应落在主目录");
+                    string text = File.ReadAllText(path);
+                    Assert.True(text.Contains("UI线程"), "日志应含环节名");
+                    Assert.True(text.Contains("InvalidOperationException"), "日志应含异常类型");
+                    Assert.True(text.Contains("测试异常甲"), "日志应含异常消息");
+                    Assert.Equal(path, ErrorLog.CurrentLogPath);
+                    Assert.False(File.Exists(Path.Combine(fallback, "error.log")), "主目录可写时不应写回退目录");
+                }
+                finally
+                {
+                    ErrorLog.ResetDirectoriesForTests();
+                    Cleanup(primary);
+                    Cleanup(fallback);
+                }
+            });
+
+            TestRunner.Run("错误日志：追加写 → 两条记录都在同一文件", () =>
+            {
+                string primary = MakeTempDir();
+                string fallback = MakeTempDir();
+                try
+                {
+                    ErrorLog.SetDirectoriesForTests(primary, fallback);
+                    ErrorLog.Write("UI线程", new InvalidOperationException("第一条"));
+                    ErrorLog.Write("非UI线程", new ApplicationException("第二条"));
+
+                    string text = File.ReadAllText(Path.Combine(primary, "error.log"));
+                    Assert.True(text.Contains("第一条"));
+                    Assert.True(text.Contains("第二条"));
+                    Assert.True(text.Contains("ApplicationException"));
+                }
+                finally
+                {
+                    ErrorLog.ResetDirectoriesForTests();
+                    Cleanup(primary);
+                    Cleanup(fallback);
+                }
+            });
+
+            TestRunner.Run("错误日志：主目录不可写 → 回退目录落盘且 CurrentLogPath 指向回退路径", () =>
+            {
+                string sandbox = MakeTempDir();
+                string fallback = MakeTempDir();
+                try
+                {
+                    // 拿一个真实文件当父目录：其下任何建目录/写文件都必然失败
+                    string blocker = Path.Combine(sandbox, "blocker");
+                    File.WriteAllText(blocker, "occupied");
+                    string badPrimary = Path.Combine(blocker, "sub");
+
+                    ErrorLog.SetDirectoriesForTests(badPrimary, fallback);
+                    ErrorLog.Write("非UI线程", new InvalidOperationException("走回退"));
+
+                    string fallbackPath = Path.Combine(fallback, "error.log");
+                    Assert.True(File.Exists(fallbackPath), "主目录不可写时应落到回退目录");
+                    Assert.True(File.ReadAllText(fallbackPath).Contains("走回退"));
+                    Assert.Equal(fallbackPath, ErrorLog.CurrentLogPath);
+                }
+                finally
+                {
+                    ErrorLog.ResetDirectoriesForTests();
+                    Cleanup(sandbox);
+                    Cleanup(fallback);
+                }
+            });
+
+            TestRunner.Run("错误日志：主/回退目录均不可写 → Write 不抛异常（含空异常对象）", () =>
+            {
+                string sandbox = MakeTempDir();
+                try
+                {
+                    string blocker = Path.Combine(sandbox, "blocker");
+                    File.WriteAllText(blocker, "occupied");
+                    string badA = Path.Combine(blocker, "a");
+                    string badB = Path.Combine(blocker, "b");
+
+                    ErrorLog.SetDirectoriesForTests(badA, badB);
+                    // 不抛异常即通过（TestRunner 会把任何逃逸异常记为用例失败）
+                    ErrorLog.Write("UI线程", new InvalidOperationException("无处可写"));
+                    ErrorLog.Write("UI线程", null);
+                    ErrorLog.Write(null, new Exception("环节名为空也不抛"));
+                }
+                finally
+                {
+                    ErrorLog.ResetDirectoriesForTests();
+                    Cleanup(sandbox);
+                }
+            });
+        }
+
+        private static string MakeTempDir()
+        {
+            string dir = Path.Combine(Path.GetTempPath(), "iw-errlog-tests-" + Guid.NewGuid().ToString("N"));
+            Directory.CreateDirectory(dir);
+            return dir;
+        }
+
+        private static void Cleanup(string dir)
+        {
+            try
+            {
+                if (Directory.Exists(dir))
+                    Directory.Delete(dir, true);
+            }
+            catch
+            {
+                // 临时目录清理失败不影响用例结论
+            }
+        }
+    }
+}
diff --git a/tests/Program.cs b/tests/Program.cs
index 0739247..0adbbc7 100644
--- a/tests/Program.cs
+++ b/tests/Program.cs
@@ -18,10 +18,11 @@ namespace InspectionWidget.Tests
             BlackFrameDetectorTests.RunAll();
             ClipboardGuardTests.RunAll();
             CredentialServiceTests.RunAll();
             ScreenshotSaverTests.RunAll();
             SettingsFormTests.RunAll();
+            ErrorLogTests.RunAll();
 
             Console.WriteLine("----------------------------------------");
             Console.WriteLine("用例总数：" + TestRunner.TotalCount
                 + "，通过：" + TestRunner.PassedCount
                 + "，失败：" + TestRunner.FailedCount);
