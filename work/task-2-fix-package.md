# Re-review package Task 2 fix: 5127422..HEAD
cc7e107 fix(stage-2): 拖动偏移与审查 Minor 五项
diff --git a/src/MainForm.cs b/src/MainForm.cs
index 7a659c1..88a9471 100644
--- a/src/MainForm.cs
+++ b/src/MainForm.cs
@@ -56,10 +56,55 @@ namespace InspectionWidget
         private readonly List<Control> _credentialControls = new List<Control>();
 
         private bool _dragging;
         private Point _dragOffset;
 
+        // 缓存字体（13 常规 / 13 加粗 / 11 三档）：全窗体复用，避免每次刷新状态或
+        // 重建按钮时 CreateFont 新建字体又不释放造成 GDI 泄漏（审查 M-4）。
+        // 按钮/标签只是引用缓存字体，WinForms 控件 Dispose 不释放所赋 Font，故无重复释放。
+        private Font _font13;
+        private Font _font13Bold;
+        private Font _font11;
+
+        private Font Font13
+        {
+            get
+            {
+                if (_font13 == null) _font13 = CreateFont(13f, FontStyle.Regular);
+                return _font13;
+            }
+        }
+
+        private Font Font13Bold
+        {
+            get
+            {
+                if (_font13Bold == null) _font13Bold = CreateFont(13f, FontStyle.Bold);
+                return _font13Bold;
+            }
+        }
+
+        private Font Font11
+        {
+            get
+            {
+                if (_font11 == null) _font11 = CreateFont(11f, FontStyle.Regular);
+                return _font11;
+            }
+        }
+
+        protected override void Dispose(bool disposing)
+        {
+            if (disposing)
+            {
+                if (_font13 != null) _font13.Dispose();
+                if (_font13Bold != null) _font13Bold.Dispose();
+                if (_font11 != null) _font11.Dispose();
+            }
+            base.Dispose(disposing);
+        }
+
         public MainForm()
         {
             _store = new ConfigStore();
             _stateMachine = new PanelStateMachine();
 
@@ -90,13 +135,13 @@ namespace InspectionWidget
 
         // ---------- 静态骨架 ----------
 
         private void BuildStaticLayout()
         {
-            Font font13 = CreateFont(13f, FontStyle.Regular);
-            Font font13Bold = CreateFont(13f, FontStyle.Bold);
-            Font font11 = CreateFont(11f, FontStyle.Regular);
+            Font font13 = Font13;
+            Font font13Bold = Font13Bold;
+            Font font11 = Font11;
 
             // 标题栏
             _titleBarPanel = new Panel { BackColor = PanelBackColor, Size = new Size(PanelLayout.PanelWidth, PanelLayout.TitleBarHeight) };
             var titleLabel = new Label
             {
@@ -224,12 +269,23 @@ namespace InspectionWidget
 
             _feedbackTimer = new Timer { Interval = 2000 };
             _feedbackTimer.Tick += delegate
             {
                 _feedbackTimer.Stop();
-                if (!_feedbackPersistent)
+                if (_feedbackPersistent)
+                    return;
+                if (_deviceListError != null)
+                {
+                    // 名单错误是常驻提示：普通反馈的 2 秒计时结束要恢复它，
+                    // 不能被普通反馈覆盖后清空（审查 M-2）；按钮禁用状态由
+                    // ScreenshotAvailable 决定、与反馈文案相互独立，始终一致。
+                    ShowDeviceListErrorFeedback();
+                }
+                else
+                {
                     _feedbackLabel.Text = string.Empty;
+                }
             };
         }
 
         private Panel _deviceHeaderPanel;
         private Panel _bottomPanel;
@@ -299,13 +355,18 @@ namespace InspectionWidget
         {
             var size = new Size(PanelLayout.PanelWidth, ClientSize.Height);
             if (_config.WindowLeft.HasValue && _config.WindowTop.HasValue)
             {
                 var candidate = new Rectangle(_config.WindowLeft.Value, _config.WindowTop.Value, size.Width, size.Height);
+                long candidateArea = (long)candidate.Width * candidate.Height;
                 foreach (Screen screen in Screen.AllScreens)
                 {
-                    if (screen.WorkingArea.IntersectsWith(candidate))
+                    // 恢复判据：与某个屏幕 WorkingArea 的相交面积至少过半才恢复，
+                    // 避免换屏/改分辨率后只剩一角在屏内也被恢复（审查 M-1）。
+                    Rectangle overlap = Rectangle.Intersect(screen.WorkingArea, candidate);
+                    long overlapArea = (long)overlap.Width * overlap.Height;
+                    if (overlapArea * 2 >= candidateArea)
                     {
                         Location = candidate.Location;
                         return;
                     }
                 }
@@ -348,11 +409,11 @@ namespace InspectionWidget
             UpdateRootLabel();
             RefreshStateVisuals();
 
             if (_deviceListError != null)
             {
-                ShowPersistentFeedback("设备名单有问题：" + _deviceListError + "，请到设置中修正", ErrorColor);
+                ShowDeviceListErrorFeedback();
             }
             else if (_feedbackPersistent)
             {
                 // 之前的常驻错误已消除，清掉提示恢复普通反馈
                 _feedbackPersistent = false;
@@ -394,11 +455,11 @@ namespace InspectionWidget
                 string device = _config.Devices[i];
                 var button = new Button
                 {
                     Text = device,
                     Tag = device,
-                    Font = CreateFont(13f, FontStyle.Regular),
+                    Font = Font13,
                     FlatStyle = FlatStyle.Flat,
                     BackColor = ButtonBackColor,
                     ForeColor = PrimaryTextColor,
                     TextAlign = ContentAlignment.MiddleLeft,
                     Size = new Size(ContentWidth, PanelLayout.DeviceRowHeight),
@@ -437,11 +498,11 @@ namespace InspectionWidget
                 CredentialEntry entry = _config.Credentials[i];
                 var button = new Button
                 {
                     Text = entry.NeedsReset ? entry.Title + "（需重设）" : entry.Title,
                     Tag = entry,
-                    Font = CreateFont(13f, FontStyle.Regular),
+                    Font = Font13,
                     FlatStyle = FlatStyle.Flat,
                     BackColor = ButtonBackColor,
                     ForeColor = entry.NeedsReset ? ErrorColor : PrimaryTextColor,
                     TextAlign = ContentAlignment.MiddleLeft,
                     Size = new Size(ContentWidth, PanelLayout.CredentialRowHeight),
@@ -454,16 +515,16 @@ namespace InspectionWidget
                 _credentialZonePanel.Controls.Add(button);
             }
         }
 
         /// <summary>空态提示行：虚线边（Paint 自绘）、指定文字色。</summary>
-        private static Label CreateHintLabel(string text, Color foreColor, int height)
+        private Label CreateHintLabel(string text, Color foreColor, int height)
         {
             var hint = new Label
             {
                 Text = text,
-                Font = CreateFont(13f, FontStyle.Regular),
+                Font = Font13,
                 ForeColor = foreColor,
                 TextAlign = ContentAlignment.MiddleLeft,
                 Size = new Size(ContentWidth, height),
                 Location = new Point(SideMargin, 0)
             };
@@ -547,11 +608,12 @@ namespace InspectionWidget
 
         private void OnRootLabelClicked(object sender, EventArgs e)
         {
             if (string.IsNullOrEmpty(_config.RootPath))
             {
-                ShowFeedback("今天尚无截图", true);
+                // 未设根目录与「今日目录不存在」是两回事，文案须准确（审查 M-5）
+                ShowFeedback("尚未设置保存目录，请先在设置中选择", true);
                 return;
             }
             string todayDir = Path.Combine(_config.RootPath, DateTime.Now.ToString("yyyy-MM-dd"));
             if (!Directory.Exists(todayDir))
             {
@@ -572,11 +634,11 @@ namespace InspectionWidget
         private void RefreshStateVisuals()
         {
             bool waiting = _stateMachine.State == PanelState.Waiting;
 
             _deviceHeaderLabel.Text = waiting ? "请点击要截图的窗口" : "截图";
-            _deviceHeaderLabel.Font = CreateFont(13f, waiting ? FontStyle.Bold : FontStyle.Regular);
+            _deviceHeaderLabel.Font = waiting ? Font13Bold : Font13;
             _deviceHeaderLabel.ForeColor = waiting ? AccentColor : SecondaryTextColor;
 
             foreach (Button button in _deviceButtons)
             {
                 string device = button.Tag as string;
@@ -639,10 +701,16 @@ namespace InspectionWidget
         private void ShowPersistentFeedback(string text, Color color)
         {
             ShowFeedbackInternal(text, color, true);
         }
 
+        /// <summary>设备名单错误的常驻提示（RefreshConfigState 与反馈计时恢复共用）。</summary>
+        private void ShowDeviceListErrorFeedback()
+        {
+            ShowPersistentFeedback("设备名单有问题：" + _deviceListError + "，请到设置中修正", ErrorColor);
+        }
+
         private void ShowFeedbackInternal(string text, Color color, bool persistent)
         {
             _feedbackPersistent = persistent;
             _feedbackLabel.Text = text;
             _feedbackLabel.ForeColor = color;
@@ -700,12 +768,19 @@ namespace InspectionWidget
         }
 
         private void OnDragMouseDown(object sender, MouseEventArgs e)
         {
             if (e.Button != MouseButtons.Left) return;
+            var control = sender as Control;
+            if (control == null) return;
             _dragging = true;
-            _dragOffset = new Point(e.X, e.Y);
+            // _dragOffset 必须换算为相对窗体的坐标：发送者可能是标题栏内的标签
+            // （titleLabel 在 (10,9)、dateLabel 在 (82,11)），e.X/e.Y 是相对发送者
+            // 控件的坐标，直接存会在 MouseMove 按窗体坐标使用时让窗体跳移一个
+            // 标签偏移量（审查 I-1）。经 PointToScreen 再 PointToClient 统一到窗体坐标。
+            Point screenPoint = control.PointToScreen(e.Location);
+            _dragOffset = PointToClient(screenPoint);
         }
 
         private void OnDragMouseMove(object sender, MouseEventArgs e)
         {
             if (!_dragging) return;
diff --git a/work/task-2-report.md b/work/task-2-report.md
index d59a6c3..fe81500 100644
--- a/work/task-2-report.md
+++ b/work/task-2-report.md
@@ -52,5 +52,18 @@
 
 1. 窗体视觉与交互（拖动、折叠锚定、等待态边框、首次选目录弹窗）无自动化测试，按简报口径转实机验收表；本机无显示环境未实跑 GUI。
 2. 空态提示行的「虚线边」用 Label.Paint 自绘虚线矩形实现（WinForms 无原生虚线 BorderStyle），圆角同样未做（令牌注明近似为准）。
 3. 设置按钮与凭据复制均为简报指定的占位（Task 7 / Task 6 接入）；`ReloadConfig()` 已预留给 Task 7 在设置保存后调用，以触发 Minor-5 的恢复路径。
 4. 测试控制台输出中文在 Mono 下为乱码（既有现象，基线测试同样），计数以 [PASS]/[FAIL] 标记为准。
+
+## 修复轮（Task 2 审查 I-1 + M-1～M-5）
+
+审查报告 `work/task-2-review.md` 第二节共 1 Important + 5 Minor，全部在 `src/MainForm.cs` 内修复：
+
+- **I-1 拖动落点偏移**：`OnDragMouseDown` 原存 `e.X/e.Y`（相对发送者控件坐标），`OnDragMouseMove` 却按窗体坐标使用，从 titleLabel(10,9)/dateLabel(82,11) 起拖会跳移一个标签偏移量。已改：MouseDown 时经发送者 `PointToScreen(e.Location)` 再窗体 `PointToClient(...)` 换算，`_dragOffset` 统一为相对窗体客户区坐标，MouseMove 逻辑不变。未抽纯函数、未加新用例（换算依赖 WinForms 坐标 API，无头环境不可测，留实机验收）。
+- **M-1 位置恢复判据**：`RestorePosition` 由 `IntersectsWith`（1px 相交即恢复）改为面积判据：候选矩形与某个屏幕 WorkingArea 的相交面积（`Rectangle.Intersect` 后宽×高，long 计算）至少达候选面积一半才恢复，否则走原有右下角归位。
+- **M-2 常驻名单错误被普通反馈覆盖**：抽出 `ShowDeviceListErrorFeedback()` 供 `RefreshConfigState` 与反馈 Timer 共用；Timer Tick 改为：非常驻反馈计时结束时，若 `_deviceListError` 仍存在则恢复常驻错误提示，否则才清空。设备按钮禁用由 `ScreenshotAvailable`（含名单错误判定）决定、与反馈文案独立，提示与禁用状态保持一致。
+- **M-3 窗体 Padding（处置：保留现状，仅 OnPaint 画边框）**：不补 Padding，理由有三——(1) 本窗体全部子控件由 `LayoutSections` 按 PanelLayout 像素公式手动绝对定位（各分区满宽 230、自 (0, y) 起排），无任何 Dock/锚边布局消费 Padding，补上也不会产生简报预期的内缩效果，属无效赋值；(2) Padding 会改变窗体 `DisplayRectangle`，在实机上反而可能让满宽分区的布局基准与 PanelLayout 坐标产生 1px 级错位风险；(3) 本无头环境缺 libgdiplus、WinForms GUI 根本无法运行（尝试实测验证时 `System.Drawing.GDIPlus` 初始化即失败），该风险无法在此验证、更不能带进实机。边框视觉已由 OnPaint 在窗体外矩形精确绘制（常态 1px #E0E0E0、等待态 2px 强调色内缩 1px），与简报视觉目标一致。故保留现状，待实机验收时如确需内缩再议。
+- **M-4 字体泄漏**：新增三档缓存字体字段（13 常规 / 13 加粗 / 11，懒加载 getter `Font13`/`Font13Bold`/`Font11`），`BuildStaticLayout`、设备/凭据按钮重建、空态提示行、`RefreshStateVisuals` 全部改用缓存字体，不再每次 `CreateFont`；窗体 `Dispose(bool)` 重写中统一释放三档缓存。按钮/标签仅引用缓存字体，WinForms 控件 Dispose 不释放所赋 Font，不存在重复释放。
+- **M-5 根目录未设置文案**：`OnRootLabelClicked` 在 RootPath 为空时反馈由「今天尚无截图」改为「尚未设置保存目录，请先在设置中选择」；今日目录不存在分支仍为「今天尚无截图」，两义已分开。
+
+验证：`bash build.sh` 退出 0（构建 0 错误），测试 **120 个全部通过、0 失败**，与修复前一致、无回归（本轮未新增用例，总数不变）。提交信息：`fix(stage-2): 拖动偏移与审查 Minor 五项`，哈希见回报（以 `git log` 为准）。
