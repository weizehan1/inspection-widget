# Review package Task 2: c6f2e0f..5127422
5127422 feat(stage-2): 主面板（布局/状态机/折叠/锚定伸缩）

 src/Capture/PanelLayout.cs       |  59 ++++
 src/Capture/PanelStateMachine.cs | 105 ++++++
 src/MainForm.cs                  | 745 ++++++++++++++++++++++++++++++++++++++-
 tests/PanelLayoutTests.cs        |  86 +++++
 tests/PanelStateMachineTests.cs  | 198 +++++++++++
 tests/Program.cs                 |   2 +
 work/task-2-report.md            |  56 +++
 7 files changed, 1243 insertions(+), 8 deletions(-)

diff --git a/src/Capture/PanelLayout.cs b/src/Capture/PanelLayout.cs
new file mode 100644
index 0000000..97b5680
--- /dev/null
+++ b/src/Capture/PanelLayout.cs
@@ -0,0 +1,59 @@
+using System;
+
+namespace InspectionWidget.Capture
+{
+    /// <summary>
+    /// 主面板布局令牌与高度计算（纯逻辑，96 DPI 逻辑像素）。
+    /// 总高 = 标题栏 36 + 分区标题 28 + 设备区 + 分区标题 28 + 凭据区 + 底部 58。
+    /// </summary>
+    public static class PanelLayout
+    {
+        public const int PanelWidth = 230;
+        public const int TitleBarHeight = 36;
+        public const int SectionHeaderHeight = 28;
+        public const int BottomStatusHeight = 58;
+        public const int DeviceRowHeight = 33;
+        public const int DeviceRowGap = 5;
+        public const int CredentialRowHeight = 33;
+        public const int CredentialRowGap = 8;
+        public const int EmptyRowHeight = 33;
+
+        /// <summary>设备区高度：0 台时为一行空态提示（33），否则 n*33+(n-1)*5。</summary>
+        public static int DeviceZoneHeight(int deviceCount)
+        {
+            if (deviceCount <= 0)
+                return EmptyRowHeight;
+            return deviceCount * DeviceRowHeight + (deviceCount - 1) * DeviceRowGap;
+        }
+
+        /// <summary>凭据区高度：折叠时 0；展开 0 条时为一行提示（33），否则 n*33+(n-1)*8。</summary>
+        public static int CredentialZoneHeight(int credentialCount, bool collapsed)
+        {
+            if (collapsed)
+                return 0;
+            if (credentialCount <= 0)
+                return EmptyRowHeight;
+            return credentialCount * CredentialRowHeight + (credentialCount - 1) * CredentialRowGap;
+        }
+
+        /// <summary>面板总高（ClientSize 高度）。</summary>
+        public static int ComputeHeight(int deviceCount, int credentialCount, bool collapsed)
+        {
+            return TitleBarHeight
+                + SectionHeaderHeight
+                + DeviceZoneHeight(deviceCount)
+                + SectionHeaderHeight
+                + CredentialZoneHeight(credentialCount, collapsed)
+                + BottomStatusHeight;
+        }
+
+        /// <summary>
+        /// 以窗口 Bottom 为锚点反算 Top：Top = Bottom - newHeight，
+        /// 且不得越过工作区上沿（钳制到 workingAreaTop）。
+        /// </summary>
+        public static int ComputeTop(int currentBottom, int newHeight, int workingAreaTop)
+        {
+            return Math.Max(workingAreaTop, currentBottom - newHeight);
+        }
+    }
+}
diff --git a/src/Capture/PanelStateMachine.cs b/src/Capture/PanelStateMachine.cs
new file mode 100644
index 0000000..608db8b
--- /dev/null
+++ b/src/Capture/PanelStateMachine.cs
@@ -0,0 +1,105 @@
+namespace InspectionWidget.Capture
+{
+    /// <summary>主面板三态：空闲 / 等待点选目标窗口 / 正在截图。</summary>
+    public enum PanelState
+    {
+        Idle,
+        Waiting,
+        Capturing
+    }
+
+    /// <summary>
+    /// 截图流程状态机（纯逻辑）。非法转移一律返回 false 且状态不变、不抛异常，
+    /// 以便迟到的完成/失败回调被安全忽略。
+    /// </summary>
+    public sealed class PanelStateMachine
+    {
+        public PanelState State { get; private set; }
+
+        /// <summary>当前选中的设备名；仅 Waiting/Capturing 时有效，其余为 null。</summary>
+        public string CurrentDevice { get; private set; }
+
+        /// <summary>截图请求号：每次 BeginCapture 成功递增，从 1 起。</summary>
+        public int CurrentRequestId { get; private set; }
+
+        public PanelStateMachine()
+        {
+            State = PanelState.Idle;
+            CurrentDevice = null;
+            CurrentRequestId = 0;
+        }
+
+        /// <summary>
+        /// 点设备按钮：Idle → Waiting 并记录设备；
+        /// Waiting 点同一设备 → 取消回 Idle（返回 true，调用方以 State 区分取消语义）；
+        /// Waiting 点另一设备 → 切换设备并保持 Waiting；
+        /// Capturing → 返回 false，状态不变。
+        /// </summary>
+        public bool SelectDevice(string device)
+        {
+            if (string.IsNullOrEmpty(device))
+                return false;
+
+            switch (State)
+            {
+                case PanelState.Idle:
+                    CurrentDevice = device;
+                    State = PanelState.Waiting;
+                    return true;
+                case PanelState.Waiting:
+                    if (CurrentDevice == device)
+                    {
+                        CurrentDevice = null;
+                        State = PanelState.Idle;
+                    }
+                    else
+                    {
+                        CurrentDevice = device;
+                    }
+                    return true;
+                default: // Capturing
+                    return false;
+            }
+        }
+
+        /// <summary>取消等待：Waiting → Idle；Capturing 处理中不可取消、Idle 无可取消，均返回 false。</summary>
+        public bool Cancel()
+        {
+            if (State != PanelState.Waiting)
+                return false;
+            CurrentDevice = null;
+            State = PanelState.Idle;
+            return true;
+        }
+
+        /// <summary>开始截图：Waiting → Capturing 并递增请求号；其他态返回 false。</summary>
+        public bool BeginCapture()
+        {
+            if (State != PanelState.Waiting)
+                return false;
+            CurrentRequestId++;
+            State = PanelState.Capturing;
+            return true;
+        }
+
+        /// <summary>截图完成：Capturing → Idle 并清设备；其他态（迟到回调）返回 false。</summary>
+        public bool CompleteCapture()
+        {
+            if (State != PanelState.Capturing)
+                return false;
+            CurrentDevice = null;
+            State = PanelState.Idle;
+            return true;
+        }
+
+        /// <summary>截图失败：Capturing → Idle 并清设备；其他态（迟到回调）返回 false。</summary>
+        public bool FailCapture()
+        {
+            if (State != PanelState.Capturing)
+                return false;
+            CurrentDevice = null;
+            State = PanelState.Idle;
+            return true;
+        }
+    }
+}
diff --git a/src/MainForm.cs b/src/MainForm.cs
index fe98a3a..7a659c1 100644
--- a/src/MainForm.cs
+++ b/src/MainForm.cs
@@ -1,26 +1,755 @@
+using System;
+using System.Collections.Generic;
+using System.Diagnostics;
 using System.Drawing;
+using System.Drawing.Drawing2D;
+using System.IO;
 using System.Windows.Forms;
+using InspectionWidget.Capture;
+using InspectionWidget.Config;
 
 namespace InspectionWidget
 {
     /// <summary>
-    /// 最小占位窗体（Task 2 会重写为完整界面）。
+    /// 主面板：无边框置顶面板。标题栏（短名+日期+设置/最小化）、设备按钮列、
+    /// 可折叠凭据区、底部反馈行与根目录行；高度变化一律 Bottom 锚定。
+    /// 配置加载/刷新走同一入口 RefreshConfigState（含设备名单校验 Minor-5 接线）。
     /// </summary>
     public class MainForm : Form
     {
+        // 颜色令牌（计划 Global Constraints）
+        private static readonly Color AccentColor = Color.FromArgb(0x00, 0x67, 0xC0);
+        private static readonly Color SuccessColor = Color.FromArgb(0x0F, 0x7B, 0x0F);
+        private static readonly Color ErrorColor = Color.FromArgb(0xC4, 0x2B, 0x1C);
+        private static readonly Color PrimaryTextColor = Color.FromArgb(0x1B, 0x1B, 0x1B);
+        private static readonly Color SecondaryTextColor = Color.FromArgb(0x42, 0x42, 0x42);
+        private static readonly Color WeakTextColor = Color.FromArgb(0x61, 0x61, 0x61);
+        private static readonly Color PanelBackColor = Color.FromArgb(0xF9, 0xF9, 0xF9);
+        private static readonly Color BorderColor = Color.FromArgb(0xE0, 0xE0, 0xE0);
+        private static readonly Color LastDeviceBackColor = Color.FromArgb(0xEF, 0xF6, 0xFC);
+        private static readonly Color ButtonBackColor = Color.White;
+
+        private const int SideMargin = 10;
+        private const int ContentWidth = PanelLayout.PanelWidth - SideMargin * 2; // 210
+
+        private readonly ConfigStore _store;
+        private readonly PanelStateMachine _stateMachine;
+        private AppConfig _config;
+
+        // 设备名单校验错误（手改 config 造成）：非 null 时常驻提示并禁用设备按钮
+        private string _deviceListError;
+        private bool _rootPromptShown;
+
+        private Panel _titleBarPanel;
+        private Label _deviceHeaderLabel;
+        private Panel _deviceZonePanel;
+        private Panel _credentialHeaderPanel;
+        private Label _credentialArrowLabel;
+        private Panel _credentialZonePanel;
+        private Label _feedbackLabel;
+        private Label _rootLabel;
+        private Timer _feedbackTimer;
+        private bool _feedbackPersistent;
+
+        private Control _deviceHint;
+        private readonly List<Button> _deviceButtons = new List<Button>();
+        private readonly List<Control> _credentialControls = new List<Control>();
+
+        private bool _dragging;
+        private Point _dragOffset;
+
         public MainForm()
         {
+            _store = new ConfigStore();
+            _stateMachine = new PanelStateMachine();
+
             Text = "巡检截图";
+            FormBorderStyle = FormBorderStyle.None;
             TopMost = true;
-            Size = new Size(230, 200);
-            FormBorderStyle = FormBorderStyle.FixedSingle;
-            MaximizeBox = false;
+            ShowInTaskbar = true;
             StartPosition = FormStartPosition.Manual;
+            BackColor = PanelBackColor;
+            DoubleBuffered = true;
+
+            BuildStaticLayout();
+
+            LoadResult load = _store.Load();
+            _config = load.Config != null ? load.Config : AppConfig.CreateDefault();
+            EnsureConfigLists();
+
+            ClientSize = new Size(PanelLayout.PanelWidth, ComputeCurrentHeight());
+            RestorePosition();
+            LayoutSections();
+
+            // 加载/刷新配置的同一入口：名单校验 + 动态按钮 + 可用性 + 常驻提示
+            RefreshConfigState();
+
+            Shown += OnFirstShown;
+            FormClosing += OnFormClosing;
+        }
+
+        // ---------- 静态骨架 ----------
+
+        private void BuildStaticLayout()
+        {
+            Font font13 = CreateFont(13f, FontStyle.Regular);
+            Font font13Bold = CreateFont(13f, FontStyle.Bold);
+            Font font11 = CreateFont(11f, FontStyle.Regular);
+
+            // 标题栏
+            _titleBarPanel = new Panel { BackColor = PanelBackColor, Size = new Size(PanelLayout.PanelWidth, PanelLayout.TitleBarHeight) };
+            var titleLabel = new Label
+            {
+                Text = "巡检截图",
+                Font = font13Bold,
+                ForeColor = PrimaryTextColor,
+                AutoSize = true,
+                Location = new Point(SideMargin, 9)
+            };
+            var dateLabel = new Label
+            {
+                Text = DateTime.Now.ToString("yyyy-MM-dd"),
+                Font = font11,
+                ForeColor = WeakTextColor,
+                AutoSize = true,
+                Location = new Point(82, 11)
+            };
+            var settingsButton = new Button
+            {
+                Text = "⚙",
+                Font = font13,
+                ForeColor = SecondaryTextColor,
+                FlatStyle = FlatStyle.Flat,
+                BackColor = PanelBackColor,
+                Size = new Size(28, 28),
+                Location = new Point(PanelLayout.PanelWidth - 66, 4)
+            };
+            settingsButton.FlatAppearance.BorderSize = 0;
+            settingsButton.Click += OnSettingsClicked;
+            var minimizeButton = new Button
+            {
+                Text = "—",
+                Font = font13,
+                ForeColor = SecondaryTextColor,
+                FlatStyle = FlatStyle.Flat,
+                BackColor = PanelBackColor,
+                Size = new Size(28, 28),
+                Location = new Point(PanelLayout.PanelWidth - 34, 4)
+            };
+            minimizeButton.FlatAppearance.BorderSize = 0;
+            minimizeButton.Click += delegate { WindowState = FormWindowState.Minimized; };
+
+            _titleBarPanel.Controls.Add(titleLabel);
+            _titleBarPanel.Controls.Add(dateLabel);
+            _titleBarPanel.Controls.Add(settingsButton);
+            _titleBarPanel.Controls.Add(minimizeButton);
+            AttachDragHandlers(_titleBarPanel);
+            AttachDragHandlers(titleLabel);
+            AttachDragHandlers(dateLabel);
+            Controls.Add(_titleBarPanel);
+
+            // 设备区标题行
+            var deviceHeaderPanel = new Panel { BackColor = PanelBackColor, Size = new Size(PanelLayout.PanelWidth, PanelLayout.SectionHeaderHeight) };
+            _deviceHeaderLabel = new Label
+            {
+                Text = "截图",
+                Font = font13,
+                ForeColor = SecondaryTextColor,
+                AutoSize = true,
+                Location = new Point(SideMargin, 5)
+            };
+            deviceHeaderPanel.Controls.Add(_deviceHeaderLabel);
+            Controls.Add(deviceHeaderPanel);
+            deviceHeaderPanel.Name = "DeviceHeaderPanel";
+            _deviceHeaderPanel = deviceHeaderPanel;
+
+            // 设备区
+            _deviceZonePanel = new Panel { BackColor = PanelBackColor, Width = PanelLayout.PanelWidth };
+            Controls.Add(_deviceZonePanel);
+
+            // 凭据区标题行（整行点击折叠）
+            _credentialHeaderPanel = new Panel { BackColor = PanelBackColor, Size = new Size(PanelLayout.PanelWidth, PanelLayout.SectionHeaderHeight) };
+            var credentialHeaderLabel = new Label
+            {
+                Text = "凭据",
+                Font = font13,
+                ForeColor = SecondaryTextColor,
+                AutoSize = true,
+                Location = new Point(SideMargin, 5)
+            };
+            _credentialArrowLabel = new Label
+            {
+                Text = "˅",
+                Font = font13,
+                ForeColor = WeakTextColor,
+                AutoSize = true,
+                Location = new Point(PanelLayout.PanelWidth - 26, 5)
+            };
+            _credentialHeaderPanel.Controls.Add(credentialHeaderLabel);
+            _credentialHeaderPanel.Controls.Add(_credentialArrowLabel);
+            _credentialHeaderPanel.Click += OnCredentialHeaderClicked;
+            credentialHeaderLabel.Click += OnCredentialHeaderClicked;
+            _credentialArrowLabel.Click += OnCredentialHeaderClicked;
+            Controls.Add(_credentialHeaderPanel);
+
+            // 凭据区
+            _credentialZonePanel = new Panel { BackColor = PanelBackColor, Width = PanelLayout.PanelWidth };
+            Controls.Add(_credentialZonePanel);
+
+            // 底部状态区：反馈行 + 根目录行
+            var bottomPanel = new Panel { BackColor = PanelBackColor, Size = new Size(PanelLayout.PanelWidth, PanelLayout.BottomStatusHeight) };
+            bottomPanel.Name = "BottomPanel";
+            _feedbackLabel = new Label
+            {
+                Font = font13,
+                ForeColor = SecondaryTextColor,
+                AutoSize = false,
+                Size = new Size(ContentWidth, 24),
+                Location = new Point(SideMargin, 4)
+            };
+            _rootLabel = new Label
+            {
+                Font = font11,
+                ForeColor = WeakTextColor,
+                AutoSize = false,
+                Size = new Size(ContentWidth, 22),
+                Location = new Point(SideMargin, 31),
+                Cursor = Cursors.Hand
+            };
+            _rootLabel.Click += OnRootLabelClicked;
+            bottomPanel.Controls.Add(_feedbackLabel);
+            bottomPanel.Controls.Add(_rootLabel);
+            Controls.Add(bottomPanel);
+            _bottomPanel = bottomPanel;
+
+            _feedbackTimer = new Timer { Interval = 2000 };
+            _feedbackTimer.Tick += delegate
+            {
+                _feedbackTimer.Stop();
+                if (!_feedbackPersistent)
+                    _feedbackLabel.Text = string.Empty;
+            };
+        }
+
+        private Panel _deviceHeaderPanel;
+        private Panel _bottomPanel;
+
+        private static Font CreateFont(float sizePx, FontStyle style)
+        {
+            string family = "Microsoft YaHei UI";
+            foreach (FontFamily installed in FontFamily.Families)
+            {
+                if (installed.Name == "Segoe UI")
+                {
+                    family = "Segoe UI";
+                    break;
+                }
+            }
+            return new Font(family, sizePx, style, GraphicsUnit.Pixel);
+        }
+
+        // ---------- 布局 ----------
+
+        private int ComputeCurrentHeight()
+        {
+            int devices = _config != null && _config.Devices != null ? _config.Devices.Count : 0;
+            int credentials = _config != null && _config.Credentials != null ? _config.Credentials.Count : 0;
+            bool collapsed = _config != null && _config.IsCredentialCollapsed;
+            return PanelLayout.ComputeHeight(devices, credentials, collapsed);
+        }
+
+        /// <summary>按区高依次摆放各分区（ClientSize 须已按 ComputeCurrentHeight 设好）。</summary>
+        private void LayoutSections()
+        {
+            int deviceZone = PanelLayout.DeviceZoneHeight(_config.Devices.Count);
+            int credentialZone = PanelLayout.CredentialZoneHeight(_config.Credentials.Count, _config.IsCredentialCollapsed);
+            int total = ComputeCurrentHeight();
+
+            _titleBarPanel.Location = new Point(0, 0);
+            int y = PanelLayout.TitleBarHeight;
+            _deviceHeaderPanel.Location = new Point(0, y);
+            y += PanelLayout.SectionHeaderHeight;
+            _deviceZonePanel.Location = new Point(0, y);
+            _deviceZonePanel.Height = deviceZone;
+            y += deviceZone;
+            _credentialHeaderPanel.Location = new Point(0, y);
+            y += PanelLayout.SectionHeaderHeight;
+            _credentialZonePanel.Location = new Point(0, y);
+            _credentialZonePanel.Height = credentialZone;
+            y += credentialZone;
+            _bottomPanel.Location = new Point(0, total - PanelLayout.BottomStatusHeight);
+        }
 
-            Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
-            Location = new Point(
-                workingArea.Right - Width,
-                workingArea.Bottom - Height);
+        /// <summary>高度变化统一入口：先按 Bottom 锚定算 Top，再改尺寸与分区布局。</summary>
+        private void ApplyHeightAnchored()
+        {
+            int newHeight = ComputeCurrentHeight();
+            if (ClientSize.Height != newHeight)
+            {
+                int bottom = Bottom;
+                int workingTop = Screen.FromControl(this).WorkingArea.Top;
+                int newTop = PanelLayout.ComputeTop(bottom, newHeight, workingTop);
+                ClientSize = new Size(PanelLayout.PanelWidth, newHeight);
+                Location = new Point(Left, newTop);
+            }
+            LayoutSections();
+        }
+
+        private void RestorePosition()
+        {
+            var size = new Size(PanelLayout.PanelWidth, ClientSize.Height);
+            if (_config.WindowLeft.HasValue && _config.WindowTop.HasValue)
+            {
+                var candidate = new Rectangle(_config.WindowLeft.Value, _config.WindowTop.Value, size.Width, size.Height);
+                foreach (Screen screen in Screen.AllScreens)
+                {
+                    if (screen.WorkingArea.IntersectsWith(candidate))
+                    {
+                        Location = candidate.Location;
+                        return;
+                    }
+                }
+            }
+            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
+            Location = new Point(wa.Right - size.Width, wa.Bottom - size.Height);
+        }
+
+        // ---------- 配置加载/刷新同一入口（含 Minor-5 名单校验接线） ----------
+
+        /// <summary>
+        /// 设置保存后可调用的刷新入口：重读配置并走与加载相同的校验/重建流程。
+        /// </summary>
+        public void ReloadConfig()
+        {
+            LoadResult load = _store.Load();
+            _config = load.Config != null ? load.Config : AppConfig.CreateDefault();
+            EnsureConfigLists();
+            RefreshConfigState();
+            ApplyHeightAnchored();
+        }
+
+        private void EnsureConfigLists()
+        {
+            if (_config.Devices == null) _config.Devices = new List<string>();
+            if (_config.Credentials == null) _config.Credentials = new List<CredentialEntry>();
+        }
+
+        /// <summary>
+        /// 加载/刷新配置的同一入口：跑 ValidateAll，名单有错时常驻错误提示并禁用
+        /// 设备按钮（截图不可用），修正后自动恢复；随后重建动态按钮与根目录行。
+        /// </summary>
+        private void RefreshConfigState()
+        {
+            _deviceListError = DeviceNameValidator.ValidateAll(_config.Devices);
+
+            RebuildDeviceButtons();
+            RebuildCredentialControls();
+            UpdateCredentialHeader();
+            UpdateRootLabel();
+            RefreshStateVisuals();
+
+            if (_deviceListError != null)
+            {
+                ShowPersistentFeedback("设备名单有问题：" + _deviceListError + "，请到设置中修正", ErrorColor);
+            }
+            else if (_feedbackPersistent)
+            {
+                // 之前的常驻错误已消除，清掉提示恢复普通反馈
+                _feedbackPersistent = false;
+                _feedbackTimer.Stop();
+                _feedbackLabel.Text = string.Empty;
+            }
+        }
+
+        /// <summary>截图是否可用：名单无误且已设置根目录。</summary>
+        private bool ScreenshotAvailable
+        {
+            get { return _deviceListError == null && !string.IsNullOrEmpty(_config.RootPath); }
+        }
+
+        private void RebuildDeviceButtons()
+        {
+            foreach (Button old in _deviceButtons)
+            {
+                _deviceZonePanel.Controls.Remove(old);
+                old.Dispose();
+            }
+            _deviceButtons.Clear();
+            if (_deviceHint != null)
+            {
+                _deviceZonePanel.Controls.Remove(_deviceHint);
+                _deviceHint.Dispose();
+                _deviceHint = null;
+            }
+
+            if (_config.Devices.Count == 0)
+            {
+                _deviceHint = CreateHintLabel("暂无设备，去设置添加", WeakTextColor, PanelLayout.DeviceRowHeight);
+                _deviceZonePanel.Controls.Add(_deviceHint);
+                return;
+            }
+
+            for (int i = 0; i < _config.Devices.Count; i++)
+            {
+                string device = _config.Devices[i];
+                var button = new Button
+                {
+                    Text = device,
+                    Tag = device,
+                    Font = CreateFont(13f, FontStyle.Regular),
+                    FlatStyle = FlatStyle.Flat,
+                    BackColor = ButtonBackColor,
+                    ForeColor = PrimaryTextColor,
+                    TextAlign = ContentAlignment.MiddleLeft,
+                    Size = new Size(ContentWidth, PanelLayout.DeviceRowHeight),
+                    Location = new Point(SideMargin, i * (PanelLayout.DeviceRowHeight + PanelLayout.DeviceRowGap))
+                };
+                button.FlatAppearance.BorderColor = BorderColor;
+                button.FlatAppearance.BorderSize = 1;
+                button.Click += OnDeviceButtonClicked;
+                _deviceButtons.Add(button);
+                _deviceZonePanel.Controls.Add(button);
+            }
+        }
+
+        private void RebuildCredentialControls()
+        {
+            foreach (Control old in _credentialControls)
+            {
+                _credentialZonePanel.Controls.Remove(old);
+                old.Dispose();
+            }
+            _credentialControls.Clear();
+
+            if (_config.IsCredentialCollapsed)
+                return;
+
+            if (_config.Credentials.Count == 0)
+            {
+                Control hint = CreateHintLabel("未设置凭据，去设置添加", AccentColor, PanelLayout.CredentialRowHeight);
+                _credentialControls.Add(hint);
+                _credentialZonePanel.Controls.Add(hint);
+                return;
+            }
+
+            for (int i = 0; i < _config.Credentials.Count; i++)
+            {
+                CredentialEntry entry = _config.Credentials[i];
+                var button = new Button
+                {
+                    Text = entry.NeedsReset ? entry.Title + "（需重设）" : entry.Title,
+                    Tag = entry,
+                    Font = CreateFont(13f, FontStyle.Regular),
+                    FlatStyle = FlatStyle.Flat,
+                    BackColor = ButtonBackColor,
+                    ForeColor = entry.NeedsReset ? ErrorColor : PrimaryTextColor,
+                    TextAlign = ContentAlignment.MiddleLeft,
+                    Size = new Size(ContentWidth, PanelLayout.CredentialRowHeight),
+                    Location = new Point(SideMargin, i * (PanelLayout.CredentialRowHeight + PanelLayout.CredentialRowGap))
+                };
+                button.FlatAppearance.BorderColor = BorderColor;
+                button.FlatAppearance.BorderSize = 1;
+                button.Click += OnCredentialButtonClicked;
+                _credentialControls.Add(button);
+                _credentialZonePanel.Controls.Add(button);
+            }
+        }
+
+        /// <summary>空态提示行：虚线边（Paint 自绘）、指定文字色。</summary>
+        private static Label CreateHintLabel(string text, Color foreColor, int height)
+        {
+            var hint = new Label
+            {
+                Text = text,
+                Font = CreateFont(13f, FontStyle.Regular),
+                ForeColor = foreColor,
+                TextAlign = ContentAlignment.MiddleLeft,
+                Size = new Size(ContentWidth, height),
+                Location = new Point(SideMargin, 0)
+            };
+            hint.Paint += delegate(object sender, PaintEventArgs e)
+            {
+                var label = (Label)sender;
+                using (var pen = new Pen(BorderColor))
+                {
+                    pen.DashStyle = DashStyle.Dash;
+                    e.Graphics.DrawRectangle(pen, 0, 0, label.Width - 1, label.Height - 1);
+                }
+            };
+            return hint;
+        }
+
+        private void UpdateCredentialHeader()
+        {
+            _credentialArrowLabel.Text = _config.IsCredentialCollapsed ? "›" : "˅";
+        }
+
+        private void UpdateRootLabel()
+        {
+            _rootLabel.Text = string.IsNullOrEmpty(_config.RootPath)
+                ? "尚未设置保存目录"
+                : "保存到：" + _config.RootPath;
+        }
+
+        // ---------- 交互 ----------
+
+        private void OnDeviceButtonClicked(object sender, EventArgs e)
+        {
+            var button = sender as Button;
+            if (button == null) return;
+            string device = button.Tag as string;
+            if (device == null) return;
+
+            if (!_stateMachine.SelectDevice(device))
+                return;
+
+            if (_stateMachine.State == PanelState.Waiting)
+            {
+                _config.LastDevice = _stateMachine.CurrentDevice;
+                SaveBestEffort();
+            }
+            RefreshStateVisuals();
+        }
+
+        private void OnCredentialHeaderClicked(object sender, EventArgs e)
+        {
+            _config.IsCredentialCollapsed = !_config.IsCredentialCollapsed;
+            SaveBestEffort(); // 失败不阻塞 UI
+            RebuildCredentialControls();
+            UpdateCredentialHeader();
+            ApplyHeightAnchored();
+        }
+
+        private void OnCredentialButtonClicked(object sender, EventArgs e)
+        {
+            var button = sender as Button;
+            if (button == null) return;
+            var entry = button.Tag as CredentialEntry;
+            if (entry == null) return;
+
+            if (entry.NeedsReset)
+            {
+                MessageBox.Show(this, "凭据「" + entry.Title + "」需要重新设置，请到设置中处理。", "巡检截图",
+                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
+                return;
+            }
+            // 本任务只做占位提示，Task 6 接真实复制
+            MessageBox.Show(this, "凭据「" + entry.Title + "」（阶段 6 接入复制）", "巡检截图",
+                MessageBoxButtons.OK, MessageBoxIcon.Information);
+        }
+
+        private void OnSettingsClicked(object sender, EventArgs e)
+        {
+            // 本任务占位，Task 7 接真实 SettingsForm
+            MessageBox.Show(this, "设置功能将在阶段 7 接入。", "巡检截图",
+                MessageBoxButtons.OK, MessageBoxIcon.Information);
+        }
+
+        private void OnRootLabelClicked(object sender, EventArgs e)
+        {
+            if (string.IsNullOrEmpty(_config.RootPath))
+            {
+                ShowFeedback("今天尚无截图", true);
+                return;
+            }
+            string todayDir = Path.Combine(_config.RootPath, DateTime.Now.ToString("yyyy-MM-dd"));
+            if (!Directory.Exists(todayDir))
+            {
+                ShowFeedback("今天尚无截图", true);
+                return;
+            }
+            try
+            {
+                Process.Start("explorer.exe", "\"" + todayDir + "\"");
+            }
+            catch (Exception ex)
+            {
+                ShowFeedback("打开文件夹失败：" + ex.Message, true);
+            }
+        }
+
+        /// <summary>等待态视觉：设备标题行文案/颜色、按钮高亮与可用性、窗体边框。</summary>
+        private void RefreshStateVisuals()
+        {
+            bool waiting = _stateMachine.State == PanelState.Waiting;
+
+            _deviceHeaderLabel.Text = waiting ? "请点击要截图的窗口" : "截图";
+            _deviceHeaderLabel.Font = CreateFont(13f, waiting ? FontStyle.Bold : FontStyle.Regular);
+            _deviceHeaderLabel.ForeColor = waiting ? AccentColor : SecondaryTextColor;
+
+            foreach (Button button in _deviceButtons)
+            {
+                string device = button.Tag as string;
+                if (waiting && device == _stateMachine.CurrentDevice)
+                {
+                    button.BackColor = AccentColor;
+                    button.ForeColor = Color.White;
+                }
+                else if (!waiting && _stateMachine.State == PanelState.Idle
+                    && device != null && device == _config.LastDevice)
+                {
+                    button.BackColor = LastDeviceBackColor;
+                    button.ForeColor = PrimaryTextColor;
+                }
+                else
+                {
+                    button.BackColor = ButtonBackColor;
+                    button.ForeColor = PrimaryTextColor;
+                }
+                button.Enabled = ScreenshotAvailable;
+            }
+
+            Invalidate();
+        }
+
+        protected override void OnPaint(PaintEventArgs e)
+        {
+            base.OnPaint(e);
+            bool waiting = _stateMachine.State == PanelState.Waiting;
+            if (waiting)
+            {
+                using (var pen = new Pen(AccentColor, 2f))
+                {
+                    e.Graphics.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
+                }
+            }
+            else
+            {
+                using (var pen = new Pen(BorderColor, 1f))
+                {
+                    e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
+                }
+            }
+        }
+
+        // ---------- 反馈行 ----------
+
+        /// <summary>普通反馈：显示后 2 秒自动清空。isError 用错误色，否则次文本色。</summary>
+        public void ShowFeedback(string text, bool isError)
+        {
+            ShowFeedbackInternal(text, isError ? ErrorColor : SecondaryTextColor, false);
+        }
+
+        /// <summary>成功反馈（成功色，2 秒自动清空）。</summary>
+        public void ShowSuccessFeedback(string text)
+        {
+            ShowFeedbackInternal(text, SuccessColor, false);
+        }
+
+        private void ShowPersistentFeedback(string text, Color color)
+        {
+            ShowFeedbackInternal(text, color, true);
+        }
+
+        private void ShowFeedbackInternal(string text, Color color, bool persistent)
+        {
+            _feedbackPersistent = persistent;
+            _feedbackLabel.Text = text;
+            _feedbackLabel.ForeColor = color;
+            _feedbackTimer.Stop();
+            if (!persistent)
+                _feedbackTimer.Start();
+        }
+
+        // ---------- 首次根目录 ----------
+
+        private void OnFirstShown(object sender, EventArgs e)
+        {
+            if (_rootPromptShown) return;
+            _rootPromptShown = true;
+            if (!string.IsNullOrEmpty(_config.RootPath)) return;
+
+            using (var dialog = new FolderBrowserDialog())
+            {
+                dialog.Description = "选择巡检截图保存根目录，只需选这一次";
+                dialog.ShowNewFolderButton = true;
+                if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(dialog.SelectedPath))
+                {
+                    string chosen = dialog.SelectedPath;
+                    _config.RootPath = chosen;
+                    SaveResult saved = SaveBestEffortResult();
+                    if (saved != null && saved.Saved)
+                    {
+                        UpdateRootLabel();
+                        RefreshStateVisuals();
+                        ShowSuccessFeedback("保存目录已设置");
+                    }
+                    else
+                    {
+                        _config.RootPath = null;
+                        UpdateRootLabel();
+                        RefreshStateVisuals();
+                        ShowFeedback("保存目录设置失败：" + (saved != null ? saved.Error : "未知错误"), true);
+                    }
+                }
+                else
+                {
+                    RefreshStateVisuals(); // RootPath 为空 → 设备按钮保持禁用
+                    ShowFeedback("未选目录，截图暂不可用", true);
+                }
+            }
+        }
+
+        // ---------- 拖动与位置保存 ----------
+
+        private void AttachDragHandlers(Control control)
+        {
+            control.MouseDown += OnDragMouseDown;
+            control.MouseMove += OnDragMouseMove;
+            control.MouseUp += OnDragMouseUp;
+        }
+
+        private void OnDragMouseDown(object sender, MouseEventArgs e)
+        {
+            if (e.Button != MouseButtons.Left) return;
+            _dragging = true;
+            _dragOffset = new Point(e.X, e.Y);
+        }
+
+        private void OnDragMouseMove(object sender, MouseEventArgs e)
+        {
+            if (!_dragging) return;
+            Point screen = ((Control)sender).PointToScreen(e.Location);
+            Location = new Point(screen.X - _dragOffset.X, screen.Y - _dragOffset.Y);
+        }
+
+        private void OnDragMouseUp(object sender, MouseEventArgs e)
+        {
+            if (!_dragging) return;
+            _dragging = false;
+            SaveWindowPosition();
+        }
+
+        private void SaveWindowPosition()
+        {
+            if (WindowState != FormWindowState.Normal) return;
+            _config.WindowLeft = Left;
+            _config.WindowTop = Top;
+            SaveBestEffort();
+        }
+
+        private void OnFormClosing(object sender, FormClosingEventArgs e)
+        {
+            SaveWindowPosition();
+        }
+
+        // ---------- 保存（BestEffort） ----------
+
+        private void SaveBestEffort()
+        {
+            SaveBestEffortResult();
+        }
+
+        private SaveResult SaveBestEffortResult()
+        {
+            try
+            {
+                return _store.Save(_config);
+            }
+            catch
+            {
+                return null;
+            }
         }
     }
 }
diff --git a/tests/PanelLayoutTests.cs b/tests/PanelLayoutTests.cs
new file mode 100644
index 0000000..8b3db8a
--- /dev/null
+++ b/tests/PanelLayoutTests.cs
@@ -0,0 +1,86 @@
+using InspectionWidget.Capture;
+
+namespace InspectionWidget.Tests
+{
+    /// <summary>PanelLayout 布局令牌与高度账（设计稿锚点逐个断言）。</summary>
+    public static class PanelLayoutTests
+    {
+        public static void RunAll()
+        {
+            TestRunner.Run("布局常量：面板宽 230", () =>
+                Assert.Equal(230, PanelLayout.PanelWidth));
+            TestRunner.Run("布局常量：标题栏 36", () =>
+                Assert.Equal(36, PanelLayout.TitleBarHeight));
+            TestRunner.Run("布局常量：分区标题行 28", () =>
+                Assert.Equal(28, PanelLayout.SectionHeaderHeight));
+            TestRunner.Run("布局常量：底部状态区 58", () =>
+                Assert.Equal(58, PanelLayout.BottomStatusHeight));
+            TestRunner.Run("布局常量：设备行高 33、行距 5", () =>
+            {
+                Assert.Equal(33, PanelLayout.DeviceRowHeight);
+                Assert.Equal(5, PanelLayout.DeviceRowGap);
+            });
+            TestRunner.Run("布局常量：凭据行高 33、行距 8", () =>
+            {
+                Assert.Equal(33, PanelLayout.CredentialRowHeight);
+                Assert.Equal(8, PanelLayout.CredentialRowGap);
+            });
+            TestRunner.Run("布局常量：空态提示行高 33", () =>
+                Assert.Equal(33, PanelLayout.EmptyRowHeight));
+
+            TestRunner.Run("设备区：0 台 = 33（提示行）", () =>
+                Assert.Equal(33, PanelLayout.DeviceZoneHeight(0)));
+            TestRunner.Run("设备区：1 台 = 33", () =>
+                Assert.Equal(33, PanelLayout.DeviceZoneHeight(1)));
+            TestRunner.Run("设备区：6 台 = 6*33+5*5 = 223", () =>
+                Assert.Equal(223, PanelLayout.DeviceZoneHeight(6)));
+            TestRunner.Run("设备区：8 台 = 8*33+7*5 = 299", () =>
+                Assert.Equal(299, PanelLayout.DeviceZoneHeight(8)));
+            TestRunner.Run("设备区：10 台 = 10*33+9*5 = 375", () =>
+                Assert.Equal(375, PanelLayout.DeviceZoneHeight(10)));
+
+            TestRunner.Run("凭据区：收起恒为 0（0 条）", () =>
+                Assert.Equal(0, PanelLayout.CredentialZoneHeight(0, true)));
+            TestRunner.Run("凭据区：收起恒为 0（10 条）", () =>
+                Assert.Equal(0, PanelLayout.CredentialZoneHeight(10, true)));
+            TestRunner.Run("凭据区：展开 0 条 = 33（提示行）", () =>
+                Assert.Equal(33, PanelLayout.CredentialZoneHeight(0, false)));
+            TestRunner.Run("凭据区：展开 1 条 = 33", () =>
+                Assert.Equal(33, PanelLayout.CredentialZoneHeight(1, false)));
+            TestRunner.Run("凭据区：展开 5 条 = 5*33+4*8 = 197", () =>
+                Assert.Equal(197, PanelLayout.CredentialZoneHeight(5, false)));
+            TestRunner.Run("凭据区：展开 10 条 = 10*33+9*8 = 402", () =>
+                Assert.Equal(402, PanelLayout.CredentialZoneHeight(10, false)));
+
+            TestRunner.Run("总高锚点：(8,10,展开) = 851", () =>
+                Assert.Equal(851, PanelLayout.ComputeHeight(8, 10, false)));
+            TestRunner.Run("总高锚点：(8,10,收起) = 449", () =>
+                Assert.Equal(449, PanelLayout.ComputeHeight(8, 10, true)));
+            TestRunner.Run("总高锚点：(6,5,展开) = 570", () =>
+                Assert.Equal(570, PanelLayout.ComputeHeight(6, 5, false)));
+            TestRunner.Run("总高锚点：(6,5,收起) = 373", () =>
+                Assert.Equal(373, PanelLayout.ComputeHeight(6, 5, true)));
+            TestRunner.Run("总高锚点：(10,10,展开) = 927", () =>
+                Assert.Equal(927, PanelLayout.ComputeHeight(10, 10, false)));
+            TestRunner.Run("总高锚点：(10,10,收起) = 525", () =>
+                Assert.Equal(525, PanelLayout.ComputeHeight(10, 10, true)));
+            TestRunner.Run("总高锚点：(6,0,展开) = 36+28+223+28+33+58 = 406", () =>
+                Assert.Equal(406, PanelLayout.ComputeHeight(6, 0, false)));
+            TestRunner.Run("总高锚点：(0,0,展开) = 36+28+33+28+33+58 = 216", () =>
+                Assert.Equal(216, PanelLayout.ComputeHeight(0, 0, false)));
+            TestRunner.Run("总高锚点：(0,0,收起) = 36+28+33+28+0+58 = 183", () =>
+                Assert.Equal(183, PanelLayout.ComputeHeight(0, 0, true)));
+
+            TestRunner.Run("ComputeTop：bottom=1000,height=500,上沿 0 → 500", () =>
+                Assert.Equal(500, PanelLayout.ComputeTop(1000, 500, 0)));
+            TestRunner.Run("ComputeTop：bottom=400,height=500,上沿 0 → 钳制为 0", () =>
+                Assert.Equal(0, PanelLayout.ComputeTop(400, 500, 0)));
+            TestRunner.Run("ComputeTop：bottom=400,height=500,上沿 40 → 钳制为 40", () =>
+                Assert.Equal(40, PanelLayout.ComputeTop(400, 500, 40)));
+            TestRunner.Run("ComputeTop：恰好贴上沿不钳制（bottom=540,height=500,上沿 40 → 40）", () =>
+                Assert.Equal(40, PanelLayout.ComputeTop(540, 500, 40)));
+            TestRunner.Run("ComputeTop：高度不变时 Top 不变（bottom=900,height=449 → 451）", () =>
+                Assert.Equal(451, PanelLayout.ComputeTop(900, 449, 0)));
+        }
+    }
+}
diff --git a/tests/PanelStateMachineTests.cs b/tests/PanelStateMachineTests.cs
new file mode 100644
index 0000000..1731d23
--- /dev/null
+++ b/tests/PanelStateMachineTests.cs
@@ -0,0 +1,198 @@
+using InspectionWidget.Capture;
+
+namespace InspectionWidget.Tests
+{
+    /// <summary>PanelStateMachine 全部转移：三态、取消/切换语义、请求号递增、非法转移拒绝。</summary>
+    public static class PanelStateMachineTests
+    {
+        public static void RunAll()
+        {
+            TestRunner.Run("状态机：初始为 Idle、无设备、请求号 0", () =>
+            {
+                var sm = new PanelStateMachine();
+                Assert.Equal(PanelState.Idle, sm.State);
+                Assert.Null(sm.CurrentDevice);
+                Assert.Equal(0, sm.CurrentRequestId);
+            });
+
+            TestRunner.Run("状态机：Idle 点设备 → Waiting 并记录设备", () =>
+            {
+                var sm = new PanelStateMachine();
+                Assert.True(sm.SelectDevice("设备A"));
+                Assert.Equal(PanelState.Waiting, sm.State);
+                Assert.Equal("设备A", sm.CurrentDevice);
+            });
+
+            TestRunner.Run("状态机：Waiting 再点同设备 → 取消回 Idle 并清设备", () =>
+            {
+                var sm = new PanelStateMachine();
+                sm.SelectDevice("设备A");
+                Assert.True(sm.SelectDevice("设备A"));
+                Assert.Equal(PanelState.Idle, sm.State);
+                Assert.Null(sm.CurrentDevice);
+            });
+
+            TestRunner.Run("状态机：Waiting 点异设备 → 切换并保持 Waiting", () =>
+            {
+                var sm = new PanelStateMachine();
+                sm.SelectDevice("设备A");
+                Assert.True(sm.SelectDevice("设备B"));
+                Assert.Equal(PanelState.Waiting, sm.State);
+                Assert.Equal("设备B", sm.CurrentDevice);
+            });
+
+            TestRunner.Run("状态机：Waiting Cancel → Idle 并清设备", () =>
+            {
+                var sm = new PanelStateMachine();
+                sm.SelectDevice("设备A");
+                Assert.True(sm.Cancel());
+                Assert.Equal(PanelState.Idle, sm.State);
+                Assert.Null(sm.CurrentDevice);
+            });
+
+            TestRunner.Run("状态机：Idle Cancel → false 且不变", () =>
+            {
+                var sm = new PanelStateMachine();
+                Assert.False(sm.Cancel());
+                Assert.Equal(PanelState.Idle, sm.State);
+            });
+
+            TestRunner.Run("状态机：Idle BeginCapture → false 且不变、请求号不变", () =>
+            {
+                var sm = new PanelStateMachine();
+                Assert.False(sm.BeginCapture());
+                Assert.Equal(PanelState.Idle, sm.State);
+                Assert.Equal(0, sm.CurrentRequestId);
+            });
+
+            TestRunner.Run("状态机：Waiting BeginCapture → Capturing，请求号从 1 起", () =>
+            {
+                var sm = new PanelStateMachine();
+                sm.SelectDevice("设备A");
+                Assert.True(sm.BeginCapture());
+                Assert.Equal(PanelState.Capturing, sm.State);
+                Assert.Equal("设备A", sm.CurrentDevice);
+                Assert.Equal(1, sm.CurrentRequestId);
+            });
+
+            TestRunner.Run("状态机：第二次 BeginCapture 请求号递增到 2", () =>
+            {
+                var sm = new PanelStateMachine();
+                sm.SelectDevice("设备A");
+                sm.BeginCapture();
+                sm.CompleteCapture();
+                sm.SelectDevice("设备B");
+                Assert.True(sm.BeginCapture());
+                Assert.Equal(2, sm.CurrentRequestId);
+                Assert.Equal("设备B", sm.CurrentDevice);
+            });
+
+            TestRunner.Run("状态机：Capturing 中 SelectDevice 被拒且不变", () =>
+            {
+                var sm = new PanelStateMachine();
+                sm.SelectDevice("设备A");
+                sm.BeginCapture();
+                Assert.False(sm.SelectDevice("设备B"));
+                Assert.False(sm.SelectDevice("设备A"));
+                Assert.Equal(PanelState.Capturing, sm.State);
+                Assert.Equal("设备A", sm.CurrentDevice);
+                Assert.Equal(1, sm.CurrentRequestId);
+            });
+
+            TestRunner.Run("状态机：Capturing 中 Cancel 被拒（处理中不可取消）", () =>
+            {
+                var sm = new PanelStateMachine();
+                sm.SelectDevice("设备A");
+                sm.BeginCapture();
+                Assert.False(sm.Cancel());
+                Assert.Equal(PanelState.Capturing, sm.State);
+                Assert.Equal("设备A", sm.CurrentDevice);
+            });
+
+            TestRunner.Run("状态机：Capturing 中 BeginCapture 重入 → false 且请求号不变", () =>
+            {
+                var sm = new PanelStateMachine();
+                sm.SelectDevice("设备A");
+                sm.BeginCapture();
+                Assert.False(sm.BeginCapture());
+                Assert.Equal(PanelState.Capturing, sm.State);
+                Assert.Equal(1, sm.CurrentRequestId);
+            });
+
+            TestRunner.Run("状态机：Capturing CompleteCapture → Idle 并清设备", () =>
+            {
+                var sm = new PanelStateMachine();
+                sm.SelectDevice("设备A");
+                sm.BeginCapture();
+                Assert.True(sm.CompleteCapture());
+                Assert.Equal(PanelState.Idle, sm.State);
+                Assert.Null(sm.CurrentDevice);
+            });
+
+            TestRunner.Run("状态机：Capturing FailCapture → Idle 并清设备", () =>
+            {
+                var sm = new PanelStateMachine();
+                sm.SelectDevice("设备A");
+                sm.BeginCapture();
+                Assert.True(sm.FailCapture());
+                Assert.Equal(PanelState.Idle, sm.State);
+                Assert.Null(sm.CurrentDevice);
+            });
+
+            TestRunner.Run("状态机：Idle 时迟到 CompleteCapture → false 不抛异常", () =>
+            {
+                var sm = new PanelStateMachine();
+                Assert.False(sm.CompleteCapture());
+                Assert.Equal(PanelState.Idle, sm.State);
+            });
+
+            TestRunner.Run("状态机：Idle 时迟到 FailCapture → false 不抛异常", () =>
+            {
+                var sm = new PanelStateMachine();
+                Assert.False(sm.FailCapture());
+                Assert.Equal(PanelState.Idle, sm.State);
+            });
+
+            TestRunner.Run("状态机：Waiting 时 CompleteCapture → false 且保持 Waiting", () =>
+            {
+                var sm = new PanelStateMachine();
+                sm.SelectDevice("设备A");
+                Assert.False(sm.CompleteCapture());
+                Assert.Equal(PanelState.Waiting, sm.State);
+                Assert.Equal("设备A", sm.CurrentDevice);
+            });
+
+            TestRunner.Run("状态机：Waiting 时 FailCapture → false 且保持 Waiting", () =>
+            {
+                var sm = new PanelStateMachine();
+                sm.SelectDevice("设备A");
+                Assert.False(sm.FailCapture());
+                Assert.Equal(PanelState.Waiting, sm.State);
+                Assert.Equal("设备A", sm.CurrentDevice);
+            });
+
+            TestRunner.Run("状态机：取消后可重新选择设备", () =>
+            {
+                var sm = new PanelStateMachine();
+                sm.SelectDevice("设备A");
+                sm.Cancel();
+                Assert.True(sm.SelectDevice("设备B"));
+                Assert.Equal(PanelState.Waiting, sm.State);
+                Assert.Equal("设备B", sm.CurrentDevice);
+            });
+
+            TestRunner.Run("状态机：失败后可重新走完整流程且请求号继续递增", () =>
+            {
+                var sm = new PanelStateMachine();
+                sm.SelectDevice("设备A");
+                sm.BeginCapture();
+                sm.FailCapture();
+                sm.SelectDevice("设备A");
+                Assert.True(sm.BeginCapture());
+                Assert.Equal(2, sm.CurrentRequestId);
+                Assert.True(sm.CompleteCapture());
+                Assert.Equal(PanelState.Idle, sm.State);
+            });
+        }
+    }
+}
diff --git a/tests/Program.cs b/tests/Program.cs
index 5ecf18f..5564c5e 100644
--- a/tests/Program.cs
+++ b/tests/Program.cs
@@ -9,10 +9,12 @@ namespace InspectionWidget.Tests
             DeviceNameValidatorTests.RunAll();
             AppConfigTests.RunAll();
             ConfigStoreTests.RunAll();
             ConfigPathResolverTests.RunAll();
             ConfigStoreLockTests.RunAll();
+            PanelLayoutTests.RunAll();
+            PanelStateMachineTests.RunAll();
 
             Console.WriteLine("----------------------------------------");
             Console.WriteLine("用例总数：" + TestRunner.TotalCount
                 + "，通过：" + TestRunner.PassedCount
                 + "，失败：" + TestRunner.FailedCount);
diff --git a/work/task-2-report.md b/work/task-2-report.md
new file mode 100644
index 0000000..d59a6c3
--- /dev/null
+++ b/work/task-2-report.md
@@ -0,0 +1,56 @@
+# Task 2 报告：主面板布局逻辑、状态机与窗体
+
+状态：**完成**（待审查）
+提交：见文末（提交后回填哈希，以协调者 git log 核对为准）
+日期：2026-11-02
+
+## 交付内容
+
+| 文件 | 内容 |
+|---|---|
+| `src/Capture/PanelLayout.cs`（新） | 纯逻辑：全部布局常量（宽 230 / 标题 36 / 分区 28 / 底部 58 / 设备行 33·距 5 / 凭据行 33·距 8 / 空态行 33）+ `DeviceZoneHeight` / `CredentialZoneHeight` / `ComputeHeight` / `ComputeTop`，签名与简报逐字一致 |
+| `src/Capture/PanelStateMachine.cs`（新） | 纯逻辑三态机：`SelectDevice`（Idle→Waiting、同设备再点取消、异设备切换、Capturing 拒绝）、`Cancel`（Capturing 不可取消）、`BeginCapture`（请求号从 1 递增）、`CompleteCapture`/`FailCapture`（非 Capturing 一律 false 不抛异常，迟到回调安全忽略） |
+| `src/MainForm.cs`（完整重写） | 无边框置顶面板，详见下方行为清单 |
+| `tests/PanelLayoutTests.cs`（新） | 32 用例 |
+| `tests/PanelStateMachineTests.cs`（新） | 20 用例 |
+| `tests/Program.cs`（改） | 注册上述两组测试 |
+
+## MainForm 行为（对简报 C 节逐条）
+
+- 窗体：`FormBorderStyle=None`、`TopMost=true`、`ShowInTaskbar=true`、`StartPosition=Manual`、宽 230、高由 `PanelLayout.ComputeHeight` 按当前配置算；字体 Segoe UI（遍历已装字体判定，回退 Microsoft YaHei UI）、`GraphicsUnit.Pixel` 13/11px；颜色全用 Global Constraints 令牌常量。
+- 标题栏（36）：「巡检截图」13px 加粗 #1B1B1B + 日期（yyyy-MM-dd、11px #616161）+ 齿轮「⚙」设置按钮（占位 MessageBox，Task 7 接真实窗）+「—」最小化（`WindowState=Minimized`，不进托盘）。标题栏及标题/日期标签可拖动，MouseUp 后保存 WindowLeft/Top（BestEffort）。
+- 边框：`OnPaint` 自绘——常态 #E0E0E0 1px；Waiting 态强调色 #0067C0 2px + 设备标题行文案改「请点击要截图的窗口」强调色加粗。
+- 设备按钮列：按 `config.Devices` 动态生成（高 33、距 5、宽 210、Flat、白底 #E0E0E0 边）；点击走状态机：进 Waiting 时写 `LastDevice` 并保存、按钮变强调底白字；再点同个取消、点异个切换高亮。0 设备显示虚线边弱文本提示「暂无设备，去设置添加」（Label.Paint 自绘虚线框，WinForms 无原生虚线边框样式）。
+- 凭据区：标题行整行点击切换折叠，写 `IsCredentialCollapsed` 并 Save（失败不阻塞 UI），高度变化走 `ApplyHeightAnchored`（先算 newHeight → `ComputeTop(Bottom, newHeight, WorkingArea.Top)` → 再改尺寸）。凭据按钮按实际条数生成（高 33、距 8），只显示 Title；`NeedsReset` 显示「Title（需重设）」错误色字、点击 MessageBox 提示去设置；正常条目点击只弹占位提示含「（阶段 6 接入复制）」，未实现剪贴板。0 条显示强调色虚线提示行「未设置凭据，去设置添加」。
+- 底部（58，两行）：反馈行 13px，`ShowFeedback(text, isError)`（错误 #C42B1C / 普通 #424242）+ `ShowSuccessFeedback`（#0F7B0F），2 秒 Timer 自动清空（常驻型除外）；根目录行 11px #616161，常驻「保存到：<RootPath>」/「尚未设置保存目录」，点击打开今日文件夹（`yyyy-MM-dd` 子目录），不存在只提示「今天尚无截图」，绝不建空文件夹（Task 5 再加选中最新图）。
+- 位置恢复：WindowLeft/Top 与任一屏 WorkingArea 相交才恢复，否则主屏 WorkingArea 右下角。
+- 首次根目录：Shown 时 RootPath 为空弹 FolderBrowserDialog（说明「选择巡检截图保存根目录，只需选这一次」）；选定→Save 成功才启用并成功色反馈，Save 失败回滚 RootPath 并错误反馈；取消→错误色反馈「未选目录，截图暂不可用」+ 设备按钮禁用。不设任何默认路径。
+- LastDevice：启动时对应按钮弱高亮底 #EFF6FC，状态仍 Idle。
+- FormClosing：保存当前 WindowLeft/Top（BestEffort，失败忽略；最小化时不保存）。剪贴板退出兜底未做（Task 6）。
+
+## 补接线（Task 1 审查 Minor-5，已做）
+
+`RefreshConfigState()` 为加载/刷新配置的同一入口（构造加载 + 公开 `ReloadConfig()` 供 Task 7 设置保存后调用）：对 `config.Devices` 跑 `DeviceNameValidator.ValidateAll`；有错误时反馈行以错误色**常驻**提示「设备名单有问题：<错误>，请到设置中修正」（不受 2 秒清空影响），且 `ScreenshotAvailable=false` 禁用全部设备按钮；修正后经同一入口刷新，常驻提示清除、按钮恢复。根目录为空同样令 `ScreenshotAvailable=false`。
+
+## TDD 证据
+
+- **RED**：先写 `PanelLayoutTests`（32）+ `PanelStateMachineTests`（20）并注册，`bash build.sh` 退出 1，错误为预期缺类：
+  `PanelLayoutTests.cs(1,24): error CS0234: The type or namespace name 'Capture' does not exist in the namespace 'InspectionWidget'`（PanelStateMachineTests 同）。
+- **GREEN**：实现两纯逻辑类 + MainForm 后，`bash build.sh` 退出 0；测试输出 `[PASS]` 120 条、`[FAIL]` 0 条。
+- 布局锚点逐个断言全过：(8,10,展开)=851、(8,10,收起)=449、(6,5,展开)=570、(6,5,收起)=373、(10,10,展开)=927、(10,10,收起)=525、(6,0,展开)=406，另加 (0,0) 两态与各区高分项；ComputeTop 含钳制（上沿 0 与 40 两例）与恰好贴沿不钳制。
+- 状态机覆盖计划 Step 3 全部转移 + Capturing 中 SelectDevice/Cancel/BeginCapture 重入被拒、Idle/Waiting 时迟到 Complete/Fail 返回 false、请求号跨两轮递增到 2。
+
+## 用例总数
+
+**120 个，全部通过，0 失败**（基线 68 不回归 + 本任务新增 52）。`bash build.sh` 退出 0（xbuild 以 `/p:LangVersion=7.2` 编译，R6 的 C# 7.2 子集由编译器直接验证）。
+
+## 提交
+
+`feat(stage-2): 主面板（布局/状态机/折叠/锚定伸缩）`，哈希以协调者 `git log` 核对为准（本报告随该提交一同入库，回报中给出）。
+
+## 遗留问题 / 说明
+
+1. 窗体视觉与交互（拖动、折叠锚定、等待态边框、首次选目录弹窗）无自动化测试，按简报口径转实机验收表；本机无显示环境未实跑 GUI。
+2. 空态提示行的「虚线边」用 Label.Paint 自绘虚线矩形实现（WinForms 无原生虚线 BorderStyle），圆角同样未做（令牌注明近似为准）。
+3. 设置按钮与凭据复制均为简报指定的占位（Task 7 / Task 6 接入）；`ReloadConfig()` 已预留给 Task 7 在设置保存后调用，以触发 Minor-5 的恢复路径。
+4. 测试控制台输出中文在 Mono 下为乱码（既有现象，基线测试同样），计数以 [PASS]/[FAIL] 标记为准。
