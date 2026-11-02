# Review package Task 5: 237a52d..HEAD
25d57c3 docs: Task 5 执行报告
cdf9892 feat(stage-5): 保存命名（顺延/CreateNew/两级暂存）

 src/MainForm.cs                | 214 +++++++++++++++++++++++++++++--
 src/Storage/IClock.cs          |  22 ++++
 src/Storage/ScreenshotSaver.cs | 247 ++++++++++++++++++++++++++++++++++++
 tests/Program.cs               |   1 +
 tests/ScreenshotSaverTests.cs  | 278 +++++++++++++++++++++++++++++++++++++++++
 work/task-5-report.md          |  70 +++++++++++
 6 files changed, 823 insertions(+), 9 deletions(-)

diff --git a/src/MainForm.cs b/src/MainForm.cs
index cfe281e..1f650d0 100644
--- a/src/MainForm.cs
+++ b/src/MainForm.cs
@@ -5,10 +5,14 @@ using System.Drawing;
 using System.Drawing.Drawing2D;
 using System.IO;
 using System.Windows.Forms;
 using InspectionWidget.Capture;
 using InspectionWidget.Config;
+using IClock = InspectionWidget.Storage.IClock;
+using ScreenshotSaver = InspectionWidget.Storage.ScreenshotSaver;
+using StorageSaveResult = InspectionWidget.Storage.SaveResult;
+using SystemClock = InspectionWidget.Storage.SystemClock;
 
 namespace InspectionWidget
 {
     /// <summary>
     /// 主面板：无边框置顶面板。标题栏（短名+日期+设置/最小化）、设备按钮列、
@@ -35,10 +39,34 @@ namespace InspectionWidget
         private readonly ConfigStore _store;
         private readonly PanelStateMachine _stateMachine;
         private readonly HookManager _hooks;
         private AppConfig _config;
 
+        // ---------- 保存链路（Task 5） ----------
+
+        /// <summary>
+        /// 一次截图请求快照：进入 Capturing 时组装，保存与失败重试全程共用；
+        /// 日期在此刻以 IClock 固定，保存/重试都用它，跨午夜不串日。
+        /// </summary>
+        private sealed class CaptureRequest
+        {
+            public string Device;
+            public string RootPath;
+            public DateTime Date;
+            public int RequestId;
+        }
+
+        private readonly IClock _clock = new SystemClock();
+        private readonly ScreenshotSaver _saver = new ScreenshotSaver();
+        private CaptureRequest _currentRequest;
+        // 保存失败待重试的图与其请求：Saver 不释放传入 Bitmap，所有权在本方；
+        // 新一次截图开始时若仍未处理，先 Dispose 旧图（见 OnTargetResolved）。
+        private Bitmap _pendingImage;
+        private CaptureRequest _pendingRequest;
+        private bool _saveInFlight;
+        private ToolTip _rootToolTip;
+
         // 设备名单校验错误（手改 config 造成）：非 null 时常驻提示并禁用设备按钮
         private string _deviceListError;
         private bool _rootPromptShown;
 
         // Waiting 会话代次守卫（Task 3 审查 M-3）：进入 Waiting 时记录 HookManager
@@ -100,10 +128,12 @@ namespace InspectionWidget
         protected override void Dispose(bool disposing)
         {
             if (disposing)
             {
                 if (_hooks != null) _hooks.Dispose();
+                if (_pendingImage != null) _pendingImage.Dispose();
+                if (_rootToolTip != null) _rootToolTip.Dispose();
                 if (_font13 != null) _font13.Dispose();
                 if (_font13Bold != null) _font13Bold.Dispose();
                 if (_font11 != null) _font11.Dispose();
             }
             base.Dispose(disposing);
@@ -260,10 +290,13 @@ namespace InspectionWidget
                 ForeColor = SecondaryTextColor,
                 AutoSize = false,
                 Size = new Size(ContentWidth, 24),
                 Location = new Point(SideMargin, 4)
             };
+            // 保存失败时反馈行即重试入口（Task 5 口径，见 OnSaveCompleted）
+            _feedbackLabel.Click += OnFeedbackLabelClicked;
+            _rootToolTip = new ToolTip();
             _rootLabel = new Label
             {
                 Font = font11,
                 ForeColor = WeakTextColor,
                 AutoSize = false,
@@ -622,19 +655,43 @@ namespace InspectionWidget
             {
                 // 未设根目录与「今日目录不存在」是两回事，文案须准确（审查 M-5）
                 ShowFeedback("尚未设置保存目录，请先在设置中选择", true);
                 return;
             }
-            string todayDir = Path.Combine(_config.RootPath, DateTime.Now.ToString("yyyy-MM-dd"));
+            string todayDir = Path.Combine(_config.RootPath, _clock.Now.ToString("yyyy-MM-dd"));
             if (!Directory.Exists(todayDir))
             {
+                // 只提示不建夹：没有今日截图就不该凭空多出一个空日期夹
                 ShowFeedback("今天尚无截图", true);
                 return;
             }
+            // 打开今日文件夹并以 explorer /select 选中最新一张图（Task 5）；
+            // 枚举失败或夹内无图时退化为只打开目录。
+            string latest = null;
+            DateTime latestTime = DateTime.MinValue;
+            try
+            {
+                foreach (string file in Directory.GetFiles(todayDir, "*.png"))
+                {
+                    DateTime written = File.GetLastWriteTime(file);
+                    if (latest == null || written > latestTime)
+                    {
+                        latest = file;
+                        latestTime = written;
+                    }
+                }
+            }
+            catch
+            {
+                latest = null;
+            }
             try
             {
-                Process.Start("explorer.exe", "\"" + todayDir + "\"");
+                if (latest != null)
+                    Process.Start("explorer.exe", "/select,\"" + latest + "\"");
+                else
+                    Process.Start("explorer.exe", "\"" + todayDir + "\"");
             }
             catch (Exception ex)
             {
                 ShowFeedback("打开文件夹失败：" + ex.Message, true);
             }
@@ -741,10 +798,19 @@ namespace InspectionWidget
                     break;
                 case ResolveReason.Ok:
                     if (!_stateMachine.BeginCapture())
                         return;
                     OnStateChanged(); // Capturing → 停钩
+                    // 进 Capturing 即组请求快照（Task 5）：根目录取当前配置、
+                    // 日期在此刻以 IClock 固定，保存与失败重试全程共用。
+                    _currentRequest = new CaptureRequest
+                    {
+                        Device = _stateMachine.CurrentDevice,
+                        RootPath = _config.RootPath,
+                        Date = _clock.Now,
+                        RequestId = _stateMachine.CurrentRequestId
+                    };
                     OnTargetResolved(result, _stateMachine.CurrentRequestId);
                     break;
                 default: // Gone
                     ShowFeedback("该窗口已关闭，请重新点选", true);
                     RestartHooksWhileWaiting();
@@ -803,16 +869,24 @@ namespace InspectionWidget
 
         /// <summary>
         /// Task 4 接入点：状态机已 BeginCapture（Capturing）。截图引擎放工作线程
         /// 执行（PrintWindow 同步阻塞不能压 UI/钩子线程），结果 BeginInvoke 回 UI
         /// 线程处理；hideSelf/showSelf 由此处注入、用 Invoke 切 UI 线程改 Opacity
-        /// 并等待完成（BitBlt 防自遮挡）。成功图暂只反馈并 Dispose，保存由
-        /// Task 5 接入；全部路径 CompleteCapture/FailCapture 回 Idle 且经
+        /// 并等待完成（BitBlt 防自遮挡）。成功图由 OnCaptureCompleted 交 Task 5
+        /// 保存链路（BeginSave）；失败路径 FailCapture 回 Idle 且经
         /// OnStateChanged 集中点停钩。
         /// </summary>
         private void OnTargetResolved(ResolveResult result, int requestId)
         {
+            // 新一次截图开始：上次保存失败待重试的旧图若仍未处理，先释放（Task 5 口径）
+            if (_pendingImage != null)
+            {
+                _pendingImage.Dispose();
+                _pendingImage = null;
+                _pendingRequest = null;
+            }
+
             IntPtr hwnd = result.Hwnd;
             string title = string.IsNullOrEmpty(result.Title) ? "（无标题）" : result.Title;
             CaptureMode mode = _config.CaptureMode;
             Rectangle selfBounds = this.Bounds;
 
@@ -879,18 +953,32 @@ namespace InspectionWidget
             {
                 if (outcome.Image != null) outcome.Image.Dispose();
                 return;
             }
 
-            bool completed;
+            bool completed = false;
             switch (outcome.Status)
             {
                 case CaptureStatus.Ok:
-                    // Task 5 保存接入前：先反馈并释放图（所有权在本方）
-                    if (outcome.Image != null) outcome.Image.Dispose();
-                    ShowSuccessFeedback("已截图：" + title + "（保存阶段 5 接入）");
-                    completed = _stateMachine.CompleteCapture();
+                    // Task 5 保存链路：图交 BeginSave 在工作线程落盘，结果回 UI 后
+                    // 才 CompleteCapture 回 Idle（见 OnSaveCompleted），此处不释放图。
+                    if (outcome.Image == null)
+                    {
+                        ShowFeedback("截图失败，请重试", true);
+                        completed = _stateMachine.FailCapture();
+                        break;
+                    }
+                    CaptureRequest request = _currentRequest != null && _currentRequest.RequestId == requestId
+                        ? _currentRequest
+                        : new CaptureRequest
+                        {
+                            Device = _stateMachine.CurrentDevice,
+                            RootPath = _config.RootPath,
+                            Date = _clock.Now,
+                            RequestId = requestId
+                        };
+                    BeginSave(outcome.Image, request, true);
                     break;
                 case CaptureStatus.SuspectProtected:
                     ShowFeedback("该窗口可能受系统保护，截不到画面", true);
                     completed = _stateMachine.FailCapture();
                     break;
@@ -906,10 +994,111 @@ namespace InspectionWidget
 
             if (completed)
                 OnStateChanged(); // 回 Idle → 集中点停钩（Capturing 时钩已停，幂等）
         }
 
+        // ---------- 保存接线（Task 5） ----------
+
+        /// <summary>
+        /// 保存走工作线程（磁盘 IO 不压 UI 线程），结果 BeginInvoke 回 UI 线程。
+        /// completesCapture=true 表示这是截图链路的保存：结果回来才结束 Capturing。
+        /// </summary>
+        private void BeginSave(Bitmap image, CaptureRequest request, bool completesCapture)
+        {
+            _saveInFlight = true;
+            var worker = new System.Threading.Thread(new System.Threading.ThreadStart(delegate
+            {
+                StorageSaveResult result;
+                try
+                {
+                    result = _saver.Save(image, request.RootPath, request.Device, request.Date);
+                }
+                catch (Exception ex)
+                {
+                    result = new StorageSaveResult { Saved = false, Error = "尚未保存：" + ex.Message };
+                }
+
+                if (IsDisposed)
+                {
+                    image.Dispose();
+                    return;
+                }
+                try
+                {
+                    BeginInvoke(new Action(delegate { OnSaveCompleted(result, image, request, completesCapture); }));
+                }
+                catch (Exception)
+                {
+                    // 窗体正在关闭：迟到结果直接释放丢弃
+                    image.Dispose();
+                }
+            }));
+            worker.IsBackground = true;
+            worker.Name = "InspectionWidget.SaveWorker";
+            worker.Start();
+        }
+
+        /// <summary>
+        /// 保存结果回 UI 线程处理。成功：释放图；暂存成功按简报 D 口径——反馈行以
+        /// 错误色显示「✓ 文件名（已暂存）：完整路径」5 秒（比普通 2 秒长以引注意），
+        /// 同时完整路径记入根目录行 tooltip，提示消失后仍可查。
+        /// 失败口径（简报 D 简化版）：保留 Bitmap 在 _pendingImage、状态回 Idle，
+        /// 反馈行常驻「尚未保存，点击此处重试」，点击反馈行以同一 CaptureRequest
+        /// （含原日期）重试；新一次截图开始时未处理的旧图先被 Dispose。
+        /// </summary>
+        private void OnSaveCompleted(StorageSaveResult result, Bitmap image, CaptureRequest request, bool completesCapture)
+        {
+            _saveInFlight = false;
+
+            if (completesCapture && _stateMachine.CompleteCapture())
+                OnStateChanged(); // 回 Idle → 集中点停钩（幂等）
+
+            if (result != null && result.Saved)
+            {
+                if (object.ReferenceEquals(_pendingImage, image))
+                {
+                    _pendingImage = null;
+                    _pendingRequest = null;
+                }
+                image.Dispose();
+                _rootToolTip.SetToolTip(_rootLabel, result.FullPath);
+                if (result.UsedFallback)
+                {
+                    ShowFeedbackInternal("✓ " + Path.GetFileName(result.FullPath) + "（已暂存）：" + result.FullPath,
+                        ErrorColor, false, 5000);
+                }
+                else
+                {
+                    ShowSuccessFeedback("✓ " + Path.GetFileName(result.FullPath));
+                }
+                return;
+            }
+
+            // 失败：保留原图供重试（Saver 从不 Dispose 传入图，所有权在本方）
+            if (_pendingImage != null && !object.ReferenceEquals(_pendingImage, image))
+                _pendingImage.Dispose();
+            _pendingImage = image;
+            _pendingRequest = request;
+            string detail = result != null ? result.Error : null;
+            if (!string.IsNullOrEmpty(detail) && detail.StartsWith("尚未保存：", StringComparison.Ordinal))
+                detail = detail.Substring("尚未保存：".Length);
+            ShowPersistentFeedback(string.IsNullOrEmpty(detail)
+                ? "尚未保存，点击此处重试"
+                : "尚未保存，点击此处重试：" + detail, ErrorColor);
+        }
+
+        /// <summary>反馈行点击：仅当有待重试的图时作为重试入口，其余情况忽略。</summary>
+        private void OnFeedbackLabelClicked(object sender, EventArgs e)
+        {
+            if (_saveInFlight)
+                return;
+            if (_pendingImage == null || _pendingRequest == null)
+                return;
+            // 字段保留登记：成功后由 OnSaveCompleted 按引用清掉，再失败会重新登记同一图
+            BeginSave(_pendingImage, _pendingRequest, false);
+        }
+
         protected override void OnPaint(PaintEventArgs e)
         {
             base.OnPaint(e);
             bool waiting = _stateMachine.State == PanelState.Waiting;
             if (waiting)
@@ -952,15 +1141,22 @@ namespace InspectionWidget
         {
             ShowPersistentFeedback("设备名单有问题：" + _deviceListError + "，请到设置中修正", ErrorColor);
         }
 
         private void ShowFeedbackInternal(string text, Color color, bool persistent)
+        {
+            ShowFeedbackInternal(text, color, persistent, 2000);
+        }
+
+        /// <summary>带显示时长的反馈：暂存成功提示用 5 秒（简报 D 口径），其余 2 秒。</summary>
+        private void ShowFeedbackInternal(string text, Color color, bool persistent, int milliseconds)
         {
             _feedbackPersistent = persistent;
             _feedbackLabel.Text = text;
             _feedbackLabel.ForeColor = color;
             _feedbackTimer.Stop();
+            _feedbackTimer.Interval = milliseconds;
             if (!persistent)
                 _feedbackTimer.Start();
         }
 
         // ---------- 首次根目录 ----------
diff --git a/src/Storage/IClock.cs b/src/Storage/IClock.cs
new file mode 100644
index 0000000..cc45a87
--- /dev/null
+++ b/src/Storage/IClock.cs
@@ -0,0 +1,22 @@
+using System;
+
+namespace InspectionWidget.Storage
+{
+    /// <summary>
+    /// 时钟抽象：截图日期归属在采集开始时由 MainForm 以 IClock 固定，
+    /// 保存层只用传入的 captureDate 命名，跨午夜不串日；测试以 FakeClock 注入。
+    /// </summary>
+    public interface IClock
+    {
+        DateTime Now { get; }
+    }
+
+    /// <summary>生产时钟：系统本地时间。</summary>
+    public sealed class SystemClock : IClock
+    {
+        public DateTime Now
+        {
+            get { return DateTime.Now; }
+        }
+    }
+}
diff --git a/src/Storage/ScreenshotSaver.cs b/src/Storage/ScreenshotSaver.cs
new file mode 100644
index 0000000..a595623
--- /dev/null
+++ b/src/Storage/ScreenshotSaver.cs
@@ -0,0 +1,247 @@
+using System;
+using System.Drawing;
+using System.Drawing.Imaging;
+using System.IO;
+using InspectionWidget.Config;
+
+namespace InspectionWidget.Storage
+{
+    /// <summary>保存结果：Saved 时 FullPath 为实际落盘路径，UsedFallback 表示走了暂存链。</summary>
+    public sealed class SaveResult
+    {
+        public bool Saved;
+        public string FullPath;
+        public bool UsedFallback;
+        public string Error;
+    }
+
+    /// <summary>
+    /// 截图保存与命名：目标目录 `&lt;根目录&gt;\yyyy-MM-dd`，文件名按
+    /// `设备名.png`、`设备名-1.png`… 顺延，以 FileMode.CreateNew 原子占用，绝不覆盖。
+    /// 主目录写入失败走两级暂存：exe 目录、LocalAppData；三级全败返回「尚未保存」。
+    /// 传入 Bitmap 的所有权始终在调用方：本类从不 Dispose 它，保存失败时
+    /// 调用方保留原图重试。
+    /// </summary>
+    public sealed class ScreenshotSaver
+    {
+        /// <summary>文件名顺延序号上限（0..10000），超限视为该目录写入失败。</summary>
+        private const int MaxSequence = 10000;
+
+        private readonly string _exeBaseDir;
+        private readonly string _localAppDataDir;
+
+        /// <summary>生产构造：exe 所在目录与系统 LocalAppData。</summary>
+        public ScreenshotSaver()
+            : this(AppDomain.CurrentDomain.BaseDirectory,
+                   Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
+        {
+        }
+
+        /// <summary>注入构造（测试用）：两级暂存的根分别取自 exeBaseDir 与 localAppDataDir。</summary>
+        public ScreenshotSaver(string exeBaseDir, string localAppDataDir)
+        {
+            _exeBaseDir = exeBaseDir;
+            _localAppDataDir = localAppDataDir;
+        }
+
+        /// <summary>
+        /// 保存截图。captureDate 只由调用方传入（采集开始时固定），本方法内不取系统时间。
+        /// 根目录为空是配置问题：直接失败、不进暂存；磁盘故障才走暂存链。
+        /// </summary>
+        public SaveResult Save(Bitmap image, string rootPath, string deviceName, DateTime captureDate)
+        {
+            if (string.IsNullOrEmpty(rootPath))
+                return new SaveResult { Saved = false, Error = "未设置保存根目录" };
+            if (image == null)
+                return new SaveResult { Saved = false, Error = "尚未保存：没有可保存的截图" };
+            if (string.IsNullOrEmpty(deviceName))
+                return new SaveResult { Saved = false, Error = "尚未保存：设备名为空" };
+
+            string datePart = captureDate.ToString("yyyy-MM-dd");
+            string primaryDir = Path.Combine(rootPath, datePart);
+
+            // 路径长度校验（复用 Task 1）：根目录过深是命名/配置问题，拦下并说明，
+            // 不进暂存（三级路径长度互不相关，暂存成功也会造成同名文件下次仍超限）。
+            string lengthError = DeviceNameValidator.ValidateFullPathLength(primaryDir, deviceName + ".png");
+            if (lengthError != null)
+                return new SaveResult { Saved = false, Error = lengthError };
+
+            string[] chain =
+            {
+                primaryDir,
+                CombineOrNull(_exeBaseDir, "FallbackScreenshots", datePart),
+                CombineOrNull(_localAppDataDir, "InspectionWidget", "FallbackScreenshots", datePart)
+            };
+
+            string lastError = null;
+            for (int i = 0; i < chain.Length; i++)
+            {
+                string fullPath;
+                string error;
+                if (TrySaveToDirectory(chain[i], image, deviceName, out fullPath, out error))
+                {
+                    return new SaveResult
+                    {
+                        Saved = true,
+                        FullPath = fullPath,
+                        UsedFallback = i > 0,
+                        Error = null
+                    };
+                }
+                lastError = error;
+            }
+
+            return new SaveResult
+            {
+                Saved = false,
+                Error = "尚未保存：主目录与两级暂存均写入失败（" + lastError + "）"
+            };
+        }
+
+        /// <summary>
+        /// 顺延分配并以 CreateNew 真占位一个空文件，返回其完整路径（测试/内部用）。
+        /// 序号超上限或遇非重名 IO 异常时抛 IOException。
+        /// </summary>
+        public string AllocatePath(string dir, string deviceName)
+        {
+            if (string.IsNullOrEmpty(dir))
+                throw new ArgumentException("目录为空。", "dir");
+            if (string.IsNullOrEmpty(deviceName))
+                throw new ArgumentException("设备名为空。", "deviceName");
+
+            Directory.CreateDirectory(dir);
+            for (int n = 0; n <= MaxSequence; n++)
+            {
+                string candidate = Path.Combine(dir, FileNameFor(deviceName, n));
+                try
+                {
+                    using (new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None))
+                    {
+                        // CreateNew 成功即占用该名：关闭后空文件保留，即「已分配」
+                    }
+                    return candidate;
+                }
+                catch (IOException)
+                {
+                    if (File.Exists(candidate))
+                        continue; // 名称已被占用 → 下一序号
+                    throw;
+                }
+            }
+            throw new IOException("文件名顺延超过上限 " + MaxSequence + "。");
+        }
+
+        /// <summary>简报签名别名，与 <see cref="AllocatePath"/> 同行为。</summary>
+        public string AllocatePathForTest(string dir, string deviceName)
+        {
+            return AllocatePath(dir, deviceName);
+        }
+
+        /// <summary>
+        /// 向单个目录保存：建目录后按顺延序号 CreateNew 占名，并用同一流写入 PNG。
+        /// 名称冲突（CreateNew 抛 IOException 且文件已存在）→ 下一序号；
+        /// 其余失败（含写流中途异常）→ 删除本次不完整文件、返回 false 交暂存链。
+        /// </summary>
+        private static bool TrySaveToDirectory(string dir, Bitmap image, string deviceName,
+            out string fullPath, out string error)
+        {
+            fullPath = null;
+            error = null;
+
+            if (string.IsNullOrEmpty(dir))
+            {
+                error = "目录路径为空";
+                return false;
+            }
+
+            try
+            {
+                Directory.CreateDirectory(dir);
+            }
+            catch (Exception ex)
+            {
+                error = Describe(ex);
+                return false;
+            }
+
+            for (int n = 0; n <= MaxSequence; n++)
+            {
+                string candidate = Path.Combine(dir, FileNameFor(deviceName, n));
+                FileStream stream = null;
+                bool created = false;
+                try
+                {
+                    stream = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None);
+                    created = true;
+                    image.Save(stream, ImageFormat.Png);
+                    stream.Flush();
+                    stream.Dispose();
+                    stream = null;
+                    fullPath = candidate;
+                    return true;
+                }
+                catch (IOException ex)
+                {
+                    CloseQuietly(stream);
+                    if (!created && File.Exists(candidate))
+                        continue; // 名称已被占用 → 下一序号
+                    if (created)
+                        TryDelete(candidate); // 本次写坏的不完整文件，仅删本次建的
+                    error = Describe(ex);
+                    return false;
+                }
+                catch (Exception ex)
+                {
+                    // GDI+ 编码等非 IO 异常同样视为本目录写入失败，走暂存链
+                    CloseQuietly(stream);
+                    if (created)
+                        TryDelete(candidate);
+                    error = Describe(ex);
+                    return false;
+                }
+            }
+
+            error = "文件名顺延超过上限 " + MaxSequence;
+            return false;
+        }
+
+        private static string FileNameFor(string deviceName, int sequence)
+        {
+            return sequence == 0
+                ? deviceName + ".png"
+                : deviceName + "-" + sequence + ".png";
+        }
+
+        private static string CombineOrNull(string baseDir, params string[] parts)
+        {
+            if (string.IsNullOrEmpty(baseDir))
+                return null;
+            string result = baseDir;
+            for (int i = 0; i < parts.Length; i++)
+                result = Path.Combine(result, parts[i]);
+            return result;
+        }
+
+        private static void CloseQuietly(FileStream stream)
+        {
+            if (stream == null) return;
+            try { stream.Dispose(); }
+            catch { /* 关闭失败不掩盖原始错误 */ }
+        }
+
+        private static void TryDelete(string path)
+        {
+            try
+            {
+                if (File.Exists(path))
+                    File.Delete(path);
+            }
+            catch { /* 删除失败不掩盖原始错误；残留空/坏文件只占一个顺延序号 */ }
+        }
+
+        private static string Describe(Exception ex)
+        {
+            return ex.GetType().Name + "：" + ex.Message;
+        }
+    }
+}
diff --git a/tests/Program.cs b/tests/Program.cs
index b4629dd..5a93634 100644
--- a/tests/Program.cs
+++ b/tests/Program.cs
@@ -14,10 +14,11 @@ namespace InspectionWidget.Tests
             PanelLayoutTests.RunAll();
             PanelStateMachineTests.RunAll();
             TargetClassifierTests.RunAll();
             CaptureGeometryTests.RunAll();
             BlackFrameDetectorTests.RunAll();
+            ScreenshotSaverTests.RunAll();
 
             Console.WriteLine("----------------------------------------");
             Console.WriteLine("用例总数：" + TestRunner.TotalCount
                 + "，通过：" + TestRunner.PassedCount
                 + "，失败：" + TestRunner.FailedCount);
diff --git a/tests/ScreenshotSaverTests.cs b/tests/ScreenshotSaverTests.cs
new file mode 100644
index 0000000..fa06eaf
--- /dev/null
+++ b/tests/ScreenshotSaverTests.cs
@@ -0,0 +1,278 @@
+using System;
+using System.IO;
+using System.Drawing;
+using InspectionWidget.Storage;
+
+namespace InspectionWidget.Tests
+{
+    /// <summary>可设当前时间的假时钟（Task 5 用例 4 跨天归属）。</summary>
+    public sealed class FakeClock : IClock
+    {
+        public DateTime Now { get; set; }
+
+        public FakeClock(DateTime now)
+        {
+            Now = now;
+        }
+    }
+
+    /// <summary>
+    /// ScreenshotSaver 保存与命名（临时目录真文件操作，测后清理）：
+    /// 日期文件夹、顺延 + CreateNew、两级暂存链、空根目录短路、位图所有权。
+    /// 不可写位置用「以普通文件占住路径」模拟：其下建子目录必然失败。
+    /// </summary>
+    public static class ScreenshotSaverTests
+    {
+        private static readonly DateTime Day1 = new DateTime(2026, 11, 2);
+        private static readonly DateTime Day2 = new DateTime(2026, 11, 3);
+        private const string Device = "设备A";
+
+        /// <summary>一组隔离的临时目录：root / exe / lad 三个根，测后整组删除。</summary>
+        private sealed class TempDirs : IDisposable
+        {
+            public string BaseDir;
+            public string Root;
+            public string Exe;
+            public string Lad;
+
+            public TempDirs()
+            {
+                BaseDir = Path.Combine(Path.GetTempPath(), "iw-saver-" + Guid.NewGuid().ToString("N"));
+                Root = Path.Combine(BaseDir, "root");
+                Exe = Path.Combine(BaseDir, "exe");
+                Lad = Path.Combine(BaseDir, "lad");
+                Directory.CreateDirectory(Root);
+                Directory.CreateDirectory(Exe);
+                Directory.CreateDirectory(Lad);
+            }
+
+            public void Dispose()
+            {
+                try
+                {
+                    if (Directory.Exists(BaseDir))
+                        Directory.Delete(BaseDir, true);
+                }
+                catch
+                {
+                    // 清理失败不影响用例结论
+                }
+            }
+        }
+
+        private static Bitmap MakeBitmap()
+        {
+            var bmp = new Bitmap(4, 4);
+            for (int y = 0; y < 4; y++)
+                for (int x = 0; x < 4; x++)
+                    bmp.SetPixel(x, y, Color.Red);
+            return bmp;
+        }
+
+        /// <summary>以普通文件占住路径：返回该文件路径，其下任何子目录创建都会失败。</summary>
+        private static string BlockWithFile(TempDirs dirs, string name)
+        {
+            string blocker = Path.Combine(dirs.BaseDir, name);
+            File.WriteAllText(blocker, "block");
+            return blocker;
+        }
+
+        public static void RunAll()
+        {
+            TestRunner.Run("保存：空目录 → 日期夹/设备A.png 存在且为真 PNG", () =>
+            {
+                using (var dirs = new TempDirs())
+                using (Bitmap bmp = MakeBitmap())
+                {
+                    var saver = new ScreenshotSaver(dirs.Exe, dirs.Lad);
+                    SaveResult result = saver.Save(bmp, dirs.Root, Device, Day1);
+
+                    string expected = Path.Combine(dirs.Root, "2026-11-02", Device + ".png");
+                    Assert.True(result.Saved, "应保存成功：" + result.Error);
+                    Assert.False(result.UsedFallback);
+                    Assert.Equal(expected, result.FullPath);
+                    Assert.True(File.Exists(expected));
+                    byte[] bytes = File.ReadAllBytes(expected);
+                    Assert.True(bytes.Length > 0, "文件应非空");
+                    Assert.Equal((byte)0x89, bytes[0], "PNG 签名首字节");
+                    Assert.Equal((byte)'P', bytes[1], "PNG 签名次字节");
+                }
+            });
+
+            TestRunner.Run("保存：已有设备A.png → 顺延 -1 且原文件不变", () =>
+            {
+                using (var dirs = new TempDirs())
+                using (Bitmap bmp = MakeBitmap())
+                {
+                    string dateDir = Path.Combine(dirs.Root, "2026-11-02");
+                    Directory.CreateDirectory(dateDir);
+                    string existing = Path.Combine(dateDir, Device + ".png");
+                    byte[] sentinel = new byte[] { 1, 2, 3, 4 };
+                    File.WriteAllBytes(existing, sentinel);
+
+                    var saver = new ScreenshotSaver(dirs.Exe, dirs.Lad);
+                    SaveResult result = saver.Save(bmp, dirs.Root, Device, Day1);
+
+                    string expected = Path.Combine(dateDir, Device + "-1.png");
+                    Assert.True(result.Saved, "应保存成功：" + result.Error);
+                    Assert.Equal(expected, result.FullPath);
+                    Assert.True(File.Exists(expected));
+                    byte[] after = File.ReadAllBytes(existing);
+                    Assert.Equal(sentinel.Length, after.Length);
+                    for (int i = 0; i < sentinel.Length; i++)
+                        Assert.Equal(sentinel[i], after[i], "原文件内容不得被覆盖");
+                }
+            });
+
+            TestRunner.Run("保存：已有 base、-1、-3 → 取最小未占用 -2", () =>
+            {
+                using (var dirs = new TempDirs())
+                using (Bitmap bmp = MakeBitmap())
+                {
+                    string dateDir = Path.Combine(dirs.Root, "2026-11-02");
+                    Directory.CreateDirectory(dateDir);
+                    File.WriteAllBytes(Path.Combine(dateDir, Device + ".png"), new byte[] { 9 });
+                    File.WriteAllBytes(Path.Combine(dateDir, Device + "-1.png"), new byte[] { 9 });
+                    File.WriteAllBytes(Path.Combine(dateDir, Device + "-3.png"), new byte[] { 9 });
+
+                    var saver = new ScreenshotSaver(dirs.Exe, dirs.Lad);
+                    SaveResult result = saver.Save(bmp, dirs.Root, Device, Day1);
+
+                    Assert.True(result.Saved, "应保存成功：" + result.Error);
+                    Assert.Equal(Path.Combine(dateDir, Device + "-2.png"), result.FullPath);
+                    Assert.True(File.Exists(result.FullPath));
+                }
+            });
+
+            TestRunner.Run("保存：FakeClock 跨天 → 新日期夹从 base 名开始", () =>
+            {
+                using (var dirs = new TempDirs())
+                using (Bitmap bmp = MakeBitmap())
+                {
+                    var clock = new FakeClock(Day1);
+                    var saver = new ScreenshotSaver(dirs.Exe, dirs.Lad);
+
+                    SaveResult first = saver.Save(bmp, dirs.Root, Device, clock.Now);
+                    Assert.True(first.Saved, "第一天应保存成功：" + first.Error);
+                    Assert.Equal(Path.Combine(dirs.Root, "2026-11-02", Device + ".png"), first.FullPath);
+
+                    clock.Now = Day2;
+                    SaveResult second = saver.Save(bmp, dirs.Root, Device, clock.Now);
+                    Assert.True(second.Saved, "第二天应保存成功：" + second.Error);
+                    Assert.Equal(Path.Combine(dirs.Root, "2026-11-03", Device + ".png"), second.FullPath);
+                }
+            });
+
+            TestRunner.Run("保存：根目录不可写 → 落 exe 暂存且顺延规则一致", () =>
+            {
+                using (var dirs = new TempDirs())
+                using (Bitmap bmp = MakeBitmap())
+                {
+                    string blockedRoot = BlockWithFile(dirs, "root-blocked");
+                    // exe 暂存日期夹预置 base 名，验证暂存级同样顺延
+                    string fallbackDir = Path.Combine(dirs.Exe, "FallbackScreenshots", "2026-11-02");
+                    Directory.CreateDirectory(fallbackDir);
+                    File.WriteAllBytes(Path.Combine(fallbackDir, Device + ".png"), new byte[] { 7 });
+
+                    var saver = new ScreenshotSaver(dirs.Exe, dirs.Lad);
+                    SaveResult result = saver.Save(bmp, blockedRoot, Device, Day1);
+
+                    Assert.True(result.Saved, "应落暂存成功：" + result.Error);
+                    Assert.True(result.UsedFallback);
+                    Assert.Equal(Path.Combine(fallbackDir, Device + "-1.png"), result.FullPath);
+                    Assert.True(File.Exists(result.FullPath));
+                }
+            });
+
+            TestRunner.Run("保存：exe 暂存也不可写 → 落 LocalAppData 三级暂存", () =>
+            {
+                using (var dirs = new TempDirs())
+                using (Bitmap bmp = MakeBitmap())
+                {
+                    string blockedRoot = BlockWithFile(dirs, "root-blocked");
+                    string blockedExe = BlockWithFile(dirs, "exe-blocked");
+
+                    var saver = new ScreenshotSaver(blockedExe, dirs.Lad);
+                    SaveResult result = saver.Save(bmp, blockedRoot, Device, Day1);
+
+                    string expected = Path.Combine(dirs.Lad, "InspectionWidget", "FallbackScreenshots", "2026-11-02", Device + ".png");
+                    Assert.True(result.Saved, "应落三级暂存成功：" + result.Error);
+                    Assert.True(result.UsedFallback);
+                    Assert.Equal(expected, result.FullPath);
+                    Assert.True(File.Exists(expected));
+                }
+            });
+
+            TestRunner.Run("保存：三级全不可写 → Saved=false、含「尚未保存」、Bitmap 不被释放", () =>
+            {
+                using (var dirs = new TempDirs())
+                {
+                    Bitmap bmp = MakeBitmap();
+                    try
+                    {
+                        string blockedRoot = BlockWithFile(dirs, "root-blocked");
+                        string blockedExe = BlockWithFile(dirs, "exe-blocked");
+                        string blockedLad = BlockWithFile(dirs, "lad-blocked");
+
+                        var saver = new ScreenshotSaver(blockedExe, blockedLad);
+                        SaveResult result = saver.Save(bmp, blockedRoot, Device, Day1);
+
+                        Assert.False(result.Saved);
+                        Assert.NotNull(result.Error);
+                        Assert.True(result.Error.IndexOf("尚未保存", StringComparison.Ordinal) >= 0,
+                            "Error 应含「尚未保存」，实际：" + result.Error);
+                        // 位图所有权在调用方：保存失败后仍可读像素，不得抛 ObjectDisposedException
+                        Color pixel = bmp.GetPixel(0, 0);
+                        Assert.Equal(Color.Red.ToArgb(), pixel.ToArgb());
+                    }
+                    finally
+                    {
+                        bmp.Dispose();
+                    }
+                }
+            });
+
+            TestRunner.Run("保存：根目录为空/null → 直接失败且不产生任何暂存文件", () =>
+            {
+                using (var dirs = new TempDirs())
+                using (Bitmap bmp = MakeBitmap())
+                {
+                    var saver = new ScreenshotSaver(dirs.Exe, dirs.Lad);
+
+                    SaveResult empty = saver.Save(bmp, string.Empty, Device, Day1);
+                    Assert.False(empty.Saved);
+                    Assert.NotNull(empty.Error);
+                    Assert.True(empty.Error.IndexOf("未设置保存根目录", StringComparison.Ordinal) >= 0,
+                        "Error 应含「未设置保存根目录」，实际：" + empty.Error);
+
+                    SaveResult nullRoot = saver.Save(bmp, null, Device, Day1);
+                    Assert.False(nullRoot.Saved);
+
+                    Assert.False(Directory.Exists(Path.Combine(dirs.Exe, "FallbackScreenshots")),
+                        "空根目录不得进暂存（exe 级）");
+                    Assert.False(Directory.Exists(Path.Combine(dirs.Lad, "InspectionWidget")),
+                        "空根目录不得进暂存（LocalAppData 级）");
+                }
+            });
+
+            TestRunner.Run("AllocatePath：CreateNew 占位顺延，第二次取 -1", () =>
+            {
+                using (var dirs = new TempDirs())
+                {
+                    var saver = new ScreenshotSaver(dirs.Exe, dirs.Lad);
+                    string first = saver.AllocatePath(dirs.Root, Device);
+                    Assert.Equal(Path.Combine(dirs.Root, Device + ".png"), first);
+                    Assert.True(File.Exists(first), "Allocate 应以 CreateNew 真占位");
+
+                    string second = saver.AllocatePath(dirs.Root, Device);
+                    Assert.Equal(Path.Combine(dirs.Root, Device + "-1.png"), second);
+                    Assert.True(File.Exists(second));
+
+                    // 简报别名与主名同行为
+                    string third = saver.AllocatePathForTest(dirs.Root, Device);
+                    Assert.Equal(Path.Combine(dirs.Root, Device + "-2.png"), third);
+                }
+            });
+        }
+    }
+}
diff --git a/work/task-5-report.md b/work/task-5-report.md
new file mode 100644
index 0000000..d520a26
--- /dev/null
+++ b/work/task-5-report.md
@@ -0,0 +1,70 @@
+# Task 5 执行报告：保存与命名
+
+## 状态
+
+**DONE**（本机可验证部分全过；MainForm 接线中的 explorer /select、反馈行点击重试等 Windows/交互路径仅保证编译通过，实机项见「遗留」）。
+
+实现提交：`cdf9892`（`feat(stage-5): 保存命名（顺延/CreateNew/两级暂存）`，父提交为基线 `237a52d`，已用 `git log --oneline | head -2` 自核）。本报告随其后的报告提交一同入库，哈希以协调者 `git log` 核对为准。
+
+前置核对：`DeviceNameValidator.ValidateFullPathLength(rootPath, name)` 已在 Task 1 交付（`src/Config/DeviceNameValidator.cs`，上限 259），本任务直接复用，无需补齐。
+
+## 交付内容
+
+| 文件 | 内容 |
+|---|---|
+| `src/Storage/IClock.cs`（新建） | 按简报签名：`IClock { DateTime Now { get; } }` + `SystemClock`（`DateTime.Now`） |
+| `src/Storage/ScreenshotSaver.cs`（新建） | `SaveResult { Saved, FullPath, UsedFallback, Error }`；`ScreenshotSaver(exeBaseDir, localAppDataDir)` 注入构造 + 无参生产构造（`AppDomain.BaseDirectory` + `LocalApplicationData`）；`Save(Bitmap, rootPath, deviceName, captureDate)`；`AllocatePath(dir, deviceName)` 与简报别名 `AllocatePathForTest`（同实现） |
+| `src/MainForm.cs`（修改） | IClock 字段（默认 SystemClock）、ScreenshotSaver 字段、嵌套 `CaptureRequest`、保存链路与失败重试、根目录行 /select 最新图 |
+| `tests/ScreenshotSaverTests.cs`（新建） | 保存测试 9 条（含 FakeClock，临时目录真文件、测后清理） |
+| `tests/Program.cs`（修改） | 注册 `ScreenshotSaverTests.RunAll()` |
+
+全程 C# 7.2 以内语法（R6）：`build.sh` 走 xbuild + `/p:LangVersion=7.2` 编译通过，无 7.3 独有语法。
+
+## ScreenshotSaver 行为落实（对照简报 B 逐条）
+
+1. **目标目录与顺延**：目录 = `<rootPath>\<yyyy-MM-dd>`（仅由传入 captureDate 格式化），不存在则创建；候选依次 `设备名.png`、`设备名-1.png`…，`FileStream(path, FileMode.CreateNew)` 原子占用：IOException 且 `File.Exists(path)` 为真 → 下一序号；其他 IOException → 该目录写入失败、进暂存链。序号上限 10000（0..10000），超限报错进下一级。
+2. **保存动作**：按简报口径修正——CreateNew 建文件后不关流，直接用同一 FileStream 交 `image.Save(stream, ImageFormat.Png)`，Flush+Dispose 后才算成功；写入中途异常（IO 或 GDI+ 非 IO 异常）→ 删除本次不完整文件（仅删本次建的）、进暂存链在下一级以同名顺延重试。任何路径都不覆盖既有文件（CreateNew 保证）。
+3. **暂存链**：主目录 → `<exeBaseDir>\FallbackScreenshots\<日期>` → `<localAppDataDir>\InspectionWidget\FallbackScreenshots\<日期>`，每级同一套顺延+CreateNew；成功时 UsedFallback=true、FullPath 为实际路径。三级全败 → Saved=false、Error =「尚未保存：主目录与两级暂存均写入失败（最后异常 类型：消息）」。**函数内从不 Dispose 传入 Bitmap**（用例 7 锚定：失败后 GetPixel 正常读出）。
+4. **captureDate 归属**：文件夹名只用传入 captureDate，Saver 内不取系统时间；跨天由 MainForm 在进 Capturing 时以 IClock 固定（见接线）。
+5. **空根目录短路**：rootPath 为 null/空字符串 → 直接 Saved=false、Error=「未设置保存根目录」，不进暂存、不产生任何文件（用例 8 对两种入参都断言 exe/LAD 两级暂存目录未被创建）。
+
+补充（简报纪律行）：保存前复用 `DeviceNameValidator.ValidateFullPathLength(日期夹, 设备名.png)` 做完整路径长度校验，超 259 直接以校验错误失败（命名/配置问题，不进暂存——三级路径长度互不相关，暂存侥幸成功也会让同一设备下次在主目录继续超限）。
+
+## TDD 证据（先 FAIL 后 PASS）
+
+- **RED**：先只写 `tests/ScreenshotSaverTests.cs`（9 条）并注册到 `tests/Program.cs`，运行 `bash build.sh` 退出码 **1**，测试工程编译失败——`CS0234: The type or namespace name 'Storage' does not exist in the namespace 'InspectionWidget'`、`CS0246: The type or namespace name 'IClock' could not be found`（日志 `/tmp/task5-red.log`，实现尚不存在，符合预期）。
+- **GREEN**：实现 `IClock`/`ScreenshotSaver` 与 MainForm 接线后，`bash build.sh` 退出码 **0**，9 条新用例全 PASS（日志 `/tmp/task5-green.log`）。真 Bitmap 路径成立：本机 libgdiplus 依赖已在 Task 4 补齐，`new Bitmap(4,4)` 涂色、PNG 编码落盘均真跑，用例 1 还断言了 PNG 签名首二字节 `0x89 'P'`，无需退化为假图接口。
+
+### 9 条用例明细
+
+| # | 用例 | 结果 |
+|---|---|---|
+| 1 | 空目录保存 → `<root>/2026-11-02/设备A.png` 存在、非空、PNG 签名正确、UsedFallback=false、FullPath 精确相等 | PASS |
+| 2 | 预置 `设备A.png`（哨兵字节）→ 落 `设备A-1.png`，原文件逐字节不变 | PASS |
+| 3 | 预置 base、-1、-3 → 取最小未占用 -2 | PASS |
+| 4 | FakeClock 两天各保存一次 → 2026-11-02、2026-11-03 两夹均从 base 名开始 | PASS |
+| 5 | rootPath 被普通文件占住（建子目录必失败）→ 落 exe 暂存，UsedFallback=true；暂存夹预置 base 后仍顺延到 -1 | PASS |
+| 6 | root 与 exe 均被文件占住 → 落 LocalAppData 三级 `InspectionWidget/FallbackScreenshots/2026-11-02/设备A.png` | PASS |
+| 7 | 三级全被文件占住 → Saved=false、Error 含「尚未保存」、Bitmap 未 Dispose（GetPixel 读出红色不抛 ObjectDisposedException） | PASS |
+| 8 | rootPath 空字符串与 null → 均 Saved=false、Error 含「未设置保存根目录」，且 exe/LAD 两级不产生任何暂存目录/文件 | PASS |
+| 9 | AllocatePath：CreateNew 真占位 → 依次 base、-1；别名 AllocatePathForTest → -2 | PASS |
+
+不可写模拟口径：以普通文件占住路径（其下创建子目录在 Mono/Linux 与 Windows 上均失败），比只读权限位更贴近「根目录指向坏位置」的真实故障形态。
+
+### 总账
+
+- `bash build.sh` 退出 **0**；**用例总数 156（基线 147 + 新增 9），通过 156，失败 0**，既有用例无回归。
+
+## MainForm 接线落实（对照简报 D）
+
+- **CaptureRequest 组装**：`OnHookExternalClick` 的 Ok 分支在 `BeginCapture()` 成功后组 `_currentRequest = { Device=状态机当前设备, RootPath=_config.RootPath（当前配置）, Date=_clock.Now, RequestId }`；MainForm 持 `IClock _clock = new SystemClock()` 与 `ScreenshotSaver _saver = new ScreenshotSaver()`（生产构造）字段。`SaveResult` 与 Config 层同名，MainForm 用别名 `StorageSaveResult`/`ScreenshotSaver`/`IClock`/`SystemClock` 引用 Storage 类型，未改 Config 侧命名。
+- **保存链路**：`OnCaptureCompleted` 的 Ok 分支不再直接 Dispose 图，改调 `BeginSave(image, request, completesCapture: true)`——工作线程调 `_saver.Save(image, request.RootPath, request.Device, request.Date)`，`BeginInvoke` 回 UI 后 `OnSaveCompleted` 才 `CompleteCapture()` 回 Idle。迟到校验（requestId/状态）在 OnCaptureCompleted 既有逻辑不变；窗体关闭时迟到图由工作线程 Dispose。
+- **成功反馈**：普通成功 → 成功色「✓ <文件名>」2 秒（既有 ShowSuccessFeedback）。暂存成功按简报实现口径：反馈行**错误色**显示「✓ 文件名（已暂存）：<实际完整路径>」**5 秒**（ShowFeedbackInternal 增加毫秒参数），同时完整路径写入根目录行 tooltip（`_rootToolTip`），提示消失后仍可查；代码注释已写明此口径。
+- **失败口径（简报简化版，已注释）**：状态回 Idle；Bitmap 保留在 `_pendingImage`（含 `_pendingRequest`），反馈行常驻错误色「尚未保存，点击此处重试：<详情>」；反馈行 Click（`OnFeedbackLabelClicked`）→ 以同一 CaptureRequest（含原日期）重试，`_saveInFlight` 防重复触发；重试成功后按引用清掉 pending 并释放图；新一次截图开始（`OnTargetResolved` 入口）若 pending 未处理，先 Dispose 旧图。窗体 Dispose 时一并释放 `_pendingImage` 与 tooltip。
+- **根目录行点击**：今日夹改以 `_clock.Now` 取日期；目录不存在只提示「今天尚无截图」不建夹（原行为保留）；存在时枚举 `*.png` 按 LastWriteTime 取最新一张，`explorer.exe /select,"<最新图>"` 打开并选中；枚举失败或夹内无图退化为只打开目录。
+
+## 遗留
+
+- MainForm 接线为 Windows/交互路径，本机仅编译通过：explorer /select 选中效果、反馈行点击重试的实际手感、5 秒暂存提示与 tooltip 显示、跨午夜真实归属，均待 Windows 实机验收（与前序任务同口径）。
+- 路径长度校验的失败不进暂存为本任务判断口径（见上「补充」），简报未明示；若审查要求改走暂存，只需调整 `ScreenshotSaver.Save` 中该校验的位置即可。
+- 暂存成功后根目录行 tooltip 记的是最近一张的完整路径；根目录行正文仍显示配置根目录，未做「最近保存」独立展示（按简报简单口径取舍）。
