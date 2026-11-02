using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using InspectionWidget.Capture;
using InspectionWidget.Config;
using InspectionWidget.Credentials;
using IClock = InspectionWidget.Storage.IClock;
using ScreenshotSaver = InspectionWidget.Storage.ScreenshotSaver;
using StorageSaveResult = InspectionWidget.Storage.SaveResult;
using SystemClock = InspectionWidget.Storage.SystemClock;

namespace InspectionWidget
{
    /// <summary>
    /// 主面板：无边框置顶面板。标题栏（短名+日期+设置/最小化）、设备按钮列、
    /// 可折叠凭据区、底部反馈行与根目录行；高度变化一律 Bottom 锚定。
    /// 配置加载/刷新走同一入口 RefreshConfigState（含设备名单校验 Minor-5 接线）。
    /// </summary>
    public class MainForm : Form
    {
        // 颜色令牌（计划 Global Constraints）
        private static readonly Color AccentColor = Color.FromArgb(0x00, 0x67, 0xC0);
        private static readonly Color SuccessColor = Color.FromArgb(0x0F, 0x7B, 0x0F);
        private static readonly Color ErrorColor = Color.FromArgb(0xC4, 0x2B, 0x1C);
        private static readonly Color PrimaryTextColor = Color.FromArgb(0x1B, 0x1B, 0x1B);
        private static readonly Color SecondaryTextColor = Color.FromArgb(0x42, 0x42, 0x42);
        private static readonly Color WeakTextColor = Color.FromArgb(0x61, 0x61, 0x61);
        private static readonly Color PanelBackColor = Color.FromArgb(0xF9, 0xF9, 0xF9);
        private static readonly Color BorderColor = Color.FromArgb(0xE0, 0xE0, 0xE0);
        private static readonly Color LastDeviceBackColor = Color.FromArgb(0xEF, 0xF6, 0xFC);
        private static readonly Color ButtonBackColor = Color.White;

        private const int SideMargin = 10;

        // 界面缩放（v1.0.1）：度量一律经 M() 按当前配置档位缩放，布局公式在 PanelLayout。
        private double ScaleFactor
        {
            get { return _config == null ? 1.0 : UiScale.Factor(_config.UiScalePercent); }
        }

        private int M(int value) { return PanelLayout.Scale(value, ScaleFactor); }

        private int PanelWidthNow { get { return PanelLayout.ScaledPanelWidth(ScaleFactor); } }

        private int ContentWidthNow { get { return PanelWidthNow - M(SideMargin) * 2; } }

        private readonly ConfigStore _store;
        private readonly PanelStateMachine _stateMachine;
        private readonly HookManager _hooks;
        private AppConfig _config;

        // ---------- 凭据模块（Task 6） ----------

        private readonly ICredentialProtector _protector = new DpapiProtector();
        private readonly IClipboardGateway _clipboardGateway = new WindowsClipboardGateway();
        private ClipboardGuard _clipboardGuard;
        private CredentialService _credentialService;

        // ---------- 保存链路（Task 5） ----------

        /// <summary>
        /// 一次截图请求快照：进入 Capturing 时组装，保存与失败重试全程共用；
        /// 日期在此刻以 IClock 固定，保存/重试都用它，跨午夜不串日。
        /// </summary>
        private sealed class CaptureRequest
        {
            public string Device;
            public string RootPath;
            public DateTime Date;
            public int RequestId;
        }

        private readonly IClock _clock = new SystemClock();
        private readonly ScreenshotSaver _saver = new ScreenshotSaver();
        private CaptureRequest _currentRequest;
        // 保存失败待重试的图与其请求：Saver 不释放传入 Bitmap，所有权在本方；
        // 新一次截图开始时若仍未处理，先 Dispose 旧图（见 OnTargetResolved）；
        // 但保存（重试）在途（_saveInFlight）时该图正被工作线程使用，不得 Dispose。
        private Bitmap _pendingImage;
        private CaptureRequest _pendingRequest;
        // 待保存图登记代次（修复轮二，M-1 变体 B）：_pendingImage 每次被 Dispose、
        // 替换或清除登记时递增。BeginSave 发起时捕获当前代次、随回调带回，
        // OnSaveCompleted 失败分支重登记前比对代次仍一致才登记——代次已变的
        // 迟到结果说明该图登记已被释放/替换，直接丢弃，不登记可能已释放的图。
        private int _pendingGeneration;
        private bool _saveInFlight;
        private ToolTip _rootToolTip;

        // 设备名单校验错误（手改 config 造成）：非 null 时常驻提示并禁用设备按钮
        private string _deviceListError;
        private bool _rootPromptShown;

        // Waiting 会话代次守卫（Task 3 审查 M-3）：进入 Waiting 时记录 HookManager
        // 返回的钩子会话号，ExternalClick 消费前比对，上一会话的迟到事件直接丢弃。
        private int _waitingSessionId;

        private Panel _titleBarPanel;
        private Label _titleLabel;
        private Label _dateLabel;
        private Button _settingsButton;
        private Button _minimizeButton;
        private Label _deviceHeaderLabel;
        private Panel _deviceZonePanel;
        private Panel _credentialHeaderPanel;
        private Label _credentialHeaderLabel;
        private Label _credentialArrowLabel;
        private Panel _credentialZonePanel;
        private Label _feedbackLabel;
        private Label _rootLabel;
        private Timer _feedbackTimer;
        private bool _feedbackPersistent;

        private Control _deviceHint;
        private readonly List<Button> _deviceButtons = new List<Button>();
        private readonly List<Control> _credentialControls = new List<Control>();

        private bool _dragging;
        private Point _dragOffset;

        // 缓存字体（13 常规 / 13 加粗 / 11 三档）：全窗体复用，避免每次刷新状态或
        // 重建按钮时 CreateFont 新建字体又不释放造成 GDI 泄漏（审查 M-4）。
        // 按钮/标签只是引用缓存字体，WinForms 控件 Dispose 不释放所赋 Font，故无重复释放。
        private Font _font13;
        private Font _font13Bold;
        private Font _font11;

        /// <summary>因子感知字体缓存：目标字号随当前缩放档位变，变了才重建（旧字体先释放）。</summary>
        private Font GetScaledFont(ref Font cache, float basePx, FontStyle style)
        {
            float target = basePx * (float)ScaleFactor;
            if (cache == null || cache.Size != target)
            {
                if (cache != null) cache.Dispose();
                cache = CreateFont(target, style);
            }
            return cache;
        }

        private Font Font13
        {
            get { return GetScaledFont(ref _font13, 13f, FontStyle.Regular); }
        }

        private Font Font13Bold
        {
            get { return GetScaledFont(ref _font13Bold, 13f, FontStyle.Bold); }
        }

        private Font Font11
        {
            get { return GetScaledFont(ref _font11, 11f, FontStyle.Regular); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_clipboardGuard != null) _clipboardGuard.Dispose();
                if (_hooks != null) _hooks.Dispose();
                if (_pendingImage != null)
                {
                    _pendingImage.Dispose();
                    _pendingGeneration++;
                }
                if (_rootToolTip != null) _rootToolTip.Dispose();
                if (_feedbackTimer != null) _feedbackTimer.Dispose();
                if (_font13 != null) _font13.Dispose();
                if (_font13Bold != null) _font13Bold.Dispose();
                if (_font11 != null) _font11.Dispose();
            }
            base.Dispose(disposing);
        }

        public MainForm()
        {
            _store = new ConfigStore();
            _stateMachine = new PanelStateMachine();
            _hooks = new HookManager();
            _hooks.ExternalClick += OnHookExternalClick;
            _hooks.CancelRequested += OnHookCancelRequested;
            _hooks.TimeoutElapsed += OnHookTimeoutElapsed;

            Text = "巡检截图";
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = PanelBackColor;
            DoubleBuffered = true;

            BuildStaticLayout();

            LoadResult load = _store.Load();
            _config = load.Config != null ? load.Config : AppConfig.CreateDefault();
            EnsureConfigLists();
            RebuildClipboardGuard();

            ApplyScaleToStaticLayout();
            ClientSize = new Size(PanelWidthNow, ComputeCurrentHeight());
            RestorePosition();
            LayoutSections();

            // 加载/刷新配置的同一入口：名单校验 + 动态按钮 + 可用性 + 常驻提示
            RefreshConfigState();

            Shown += OnFirstShown;
            FormClosing += OnFormClosing;
        }

        // ---------- 静态骨架 ----------

        private void BuildStaticLayout()
        {
            Font font13 = Font13;
            Font font13Bold = Font13Bold;
            Font font11 = Font11;

            // 标题栏
            _titleBarPanel = new Panel { BackColor = PanelBackColor, Size = new Size(PanelLayout.PanelWidth, PanelLayout.TitleBarHeight) };
            var titleLabel = _titleLabel = new Label
            {
                Text = "巡检截图",
                Font = font13Bold,
                ForeColor = PrimaryTextColor,
                AutoSize = true,
                Location = new Point(SideMargin, 9)
            };
            var dateLabel = _dateLabel = new Label
            {
                Text = DateTime.Now.ToString("yyyy-MM-dd"),
                Font = font11,
                ForeColor = WeakTextColor,
                AutoSize = true,
                Location = new Point(82, 11)
            };
            var settingsButton = _settingsButton = new Button
            {
                Text = "⚙",
                Font = font13,
                ForeColor = SecondaryTextColor,
                FlatStyle = FlatStyle.Flat,
                BackColor = PanelBackColor,
                Size = new Size(28, 28),
                Location = new Point(PanelLayout.PanelWidth - 66, 4)
            };
            settingsButton.FlatAppearance.BorderSize = 0;
            settingsButton.Click += OnSettingsClicked;
            var minimizeButton = _minimizeButton = new Button
            {
                Text = "—",
                Font = font13,
                ForeColor = SecondaryTextColor,
                FlatStyle = FlatStyle.Flat,
                BackColor = PanelBackColor,
                Size = new Size(28, 28),
                Location = new Point(PanelLayout.PanelWidth - 34, 4)
            };
            minimizeButton.FlatAppearance.BorderSize = 0;
            minimizeButton.Click += delegate { WindowState = FormWindowState.Minimized; };

            _titleBarPanel.Controls.Add(titleLabel);
            _titleBarPanel.Controls.Add(dateLabel);
            _titleBarPanel.Controls.Add(settingsButton);
            _titleBarPanel.Controls.Add(minimizeButton);
            AttachDragHandlers(_titleBarPanel);
            AttachDragHandlers(titleLabel);
            AttachDragHandlers(dateLabel);
            Controls.Add(_titleBarPanel);

            // 设备区标题行
            var deviceHeaderPanel = new Panel { BackColor = PanelBackColor, Size = new Size(PanelLayout.PanelWidth, PanelLayout.SectionHeaderHeight) };
            _deviceHeaderLabel = new Label
            {
                Text = "截图",
                Font = font13,
                ForeColor = SecondaryTextColor,
                AutoSize = true,
                Location = new Point(SideMargin, 5)
            };
            deviceHeaderPanel.Controls.Add(_deviceHeaderLabel);
            Controls.Add(deviceHeaderPanel);
            deviceHeaderPanel.Name = "DeviceHeaderPanel";
            _deviceHeaderPanel = deviceHeaderPanel;

            // 设备区
            _deviceZonePanel = new Panel { BackColor = PanelBackColor, Width = PanelLayout.PanelWidth };
            Controls.Add(_deviceZonePanel);

            // 凭据区标题行（整行点击折叠）
            _credentialHeaderPanel = new Panel { BackColor = PanelBackColor, Size = new Size(PanelLayout.PanelWidth, PanelLayout.SectionHeaderHeight) };
            var credentialHeaderLabel = _credentialHeaderLabel = new Label
            {
                Text = "凭据",
                Font = font13,
                ForeColor = SecondaryTextColor,
                AutoSize = true,
                Location = new Point(SideMargin, 5)
            };
            _credentialArrowLabel = new Label
            {
                Text = "˅",
                Font = font13,
                ForeColor = WeakTextColor,
                AutoSize = true,
                Location = new Point(PanelLayout.PanelWidth - 26, 5)
            };
            _credentialHeaderPanel.Controls.Add(credentialHeaderLabel);
            _credentialHeaderPanel.Controls.Add(_credentialArrowLabel);
            _credentialHeaderPanel.Click += OnCredentialHeaderClicked;
            credentialHeaderLabel.Click += OnCredentialHeaderClicked;
            _credentialArrowLabel.Click += OnCredentialHeaderClicked;
            Controls.Add(_credentialHeaderPanel);

            // 凭据区
            _credentialZonePanel = new Panel { BackColor = PanelBackColor, Width = PanelLayout.PanelWidth };
            Controls.Add(_credentialZonePanel);

            // 底部状态区：反馈行 + 根目录行
            var bottomPanel = new Panel { BackColor = PanelBackColor, Size = new Size(PanelLayout.PanelWidth, PanelLayout.BottomStatusHeight) };
            bottomPanel.Name = "BottomPanel";
            _feedbackLabel = new Label
            {
                Font = font13,
                ForeColor = SecondaryTextColor,
                AutoSize = false,
                Size = new Size(ContentWidthNow, 24),
                Location = new Point(SideMargin, 4)
            };
            // 保存失败时反馈行即重试入口（Task 5 口径，见 OnSaveCompleted）
            _feedbackLabel.Click += OnFeedbackLabelClicked;
            _rootToolTip = new ToolTip();
            _rootLabel = new Label
            {
                Font = font11,
                ForeColor = WeakTextColor,
                AutoSize = false,
                Size = new Size(ContentWidthNow, 22),
                Location = new Point(SideMargin, 31),
                Cursor = Cursors.Hand
            };
            _rootLabel.Click += OnRootLabelClicked;
            bottomPanel.Controls.Add(_feedbackLabel);
            bottomPanel.Controls.Add(_rootLabel);
            Controls.Add(bottomPanel);
            _bottomPanel = bottomPanel;

            _feedbackTimer = new Timer { Interval = 2000 };
            _feedbackTimer.Tick += delegate
            {
                _feedbackTimer.Stop();
                if (_feedbackPersistent)
                    return;
                if (_deviceListError != null)
                {
                    // 名单错误是常驻提示：普通反馈的 2 秒计时结束要恢复它，
                    // 不能被普通反馈覆盖后清空（审查 M-2）；按钮禁用状态由
                    // ScreenshotAvailable 决定、与反馈文案相互独立，始终一致。
                    ShowDeviceListErrorFeedback();
                }
                else
                {
                    _feedbackLabel.Text = string.Empty;
                }
            };
        }

        private Panel _deviceHeaderPanel;
        private Panel _bottomPanel;

        private static Font CreateFont(float sizePx, FontStyle style)
        {
            string family = "Microsoft YaHei UI";
            foreach (FontFamily installed in FontFamily.Families)
            {
                if (installed.Name == "Segoe UI")
                {
                    family = "Segoe UI";
                    break;
                }
            }
            return new Font(family, sizePx, style, GraphicsUnit.Pixel);
        }

        // ---------- 布局 ----------

        /// <summary>
        /// 静态骨架的字体与几何按当前缩放档位重设（v1.0.1）：构造时配置加载后、
        /// 设置保存后各调一次；动态按钮由 RefreshConfigState 另行重建。
        /// 设备标题行字体不在此列（等待态由 RefreshStateVisuals 管 Font13/13Bold 切换）。
        /// </summary>
        private void ApplyScaleToStaticLayout()
        {
            int w = PanelWidthNow;
            int margin = M(SideMargin);

            _titleBarPanel.Size = new Size(w, M(PanelLayout.TitleBarHeight));
            _titleLabel.Font = Font13Bold;
            _titleLabel.Location = new Point(margin, M(9));
            _dateLabel.Font = Font11;
            _dateLabel.Location = new Point(M(82), M(11));
            _settingsButton.Font = Font13;
            _settingsButton.Size = new Size(M(28), M(28));
            _settingsButton.Location = new Point(w - M(66), M(4));
            _minimizeButton.Font = Font13;
            _minimizeButton.Size = new Size(M(28), M(28));
            _minimizeButton.Location = new Point(w - M(34), M(4));

            _deviceHeaderPanel.Size = new Size(w, M(PanelLayout.SectionHeaderHeight));
            _deviceHeaderLabel.Location = new Point(margin, M(5));

            _deviceZonePanel.Width = w;

            _credentialHeaderPanel.Size = new Size(w, M(PanelLayout.SectionHeaderHeight));
            _credentialHeaderLabel.Font = Font13;
            _credentialHeaderLabel.Location = new Point(margin, M(5));
            _credentialArrowLabel.Font = Font13;
            _credentialArrowLabel.Location = new Point(w - M(26), M(5));

            _credentialZonePanel.Width = w;

            _bottomPanel.Size = new Size(w, M(PanelLayout.BottomStatusHeight));
            _feedbackLabel.Font = Font13;
            _feedbackLabel.Size = new Size(ContentWidthNow, M(24));
            _feedbackLabel.Location = new Point(margin, M(4));
            _rootLabel.Font = Font11;
            _rootLabel.Size = new Size(ContentWidthNow, M(22));
            _rootLabel.Location = new Point(margin, M(31));
        }

        private int ComputeCurrentHeight()
        {
            int devices = _config != null && _config.Devices != null ? _config.Devices.Count : 0;
            int credentials = _config != null && _config.Credentials != null ? _config.Credentials.Count : 0;
            bool collapsed = _config != null && _config.IsCredentialCollapsed;
            return PanelLayout.ComputeHeight(devices, credentials, collapsed, ScaleFactor);
        }

        /// <summary>按区高依次摆放各分区（ClientSize 须已按 ComputeCurrentHeight 设好）。</summary>
        private void LayoutSections()
        {
            int deviceZone = PanelLayout.DeviceZoneHeight(_config.Devices.Count, ScaleFactor);
            int credentialZone = PanelLayout.CredentialZoneHeight(_config.Credentials.Count, _config.IsCredentialCollapsed, ScaleFactor);
            int total = ComputeCurrentHeight();

            _titleBarPanel.Location = new Point(0, 0);
            int y = M(PanelLayout.TitleBarHeight);
            _deviceHeaderPanel.Location = new Point(0, y);
            y += M(PanelLayout.SectionHeaderHeight);
            _deviceZonePanel.Location = new Point(0, y);
            _deviceZonePanel.Height = deviceZone;
            y += deviceZone;
            _credentialHeaderPanel.Location = new Point(0, y);
            y += M(PanelLayout.SectionHeaderHeight);
            _credentialZonePanel.Location = new Point(0, y);
            _credentialZonePanel.Height = credentialZone;
            y += credentialZone;
            _bottomPanel.Location = new Point(0, total - M(PanelLayout.BottomStatusHeight));
        }

        /// <summary>高度变化统一入口：先按 Bottom 锚定算 Top，再改尺寸与分区布局。</summary>
        private void ApplyHeightAnchored()
        {
            int newHeight = ComputeCurrentHeight();
            if (ClientSize.Height != newHeight || ClientSize.Width != PanelWidthNow)
            {
                int bottom = Bottom;
                int workingTop = Screen.FromControl(this).WorkingArea.Top;
                int newTop = PanelLayout.ComputeTop(bottom, newHeight, workingTop);
                ClientSize = new Size(PanelWidthNow, newHeight);
                Location = new Point(Left, newTop);
            }
            LayoutSections();
        }

        private void RestorePosition()
        {
            var size = new Size(PanelWidthNow, ClientSize.Height);
            if (_config.WindowLeft.HasValue && _config.WindowTop.HasValue)
            {
                var candidate = new Rectangle(_config.WindowLeft.Value, _config.WindowTop.Value, size.Width, size.Height);
                long candidateArea = (long)candidate.Width * candidate.Height;
                foreach (Screen screen in Screen.AllScreens)
                {
                    // 恢复判据：与某个屏幕 WorkingArea 的相交面积至少过半才恢复，
                    // 避免换屏/改分辨率后只剩一角在屏内也被恢复（审查 M-1）。
                    Rectangle overlap = Rectangle.Intersect(screen.WorkingArea, candidate);
                    long overlapArea = (long)overlap.Width * overlap.Height;
                    if (overlapArea * 2 >= candidateArea)
                    {
                        Location = candidate.Location;
                        return;
                    }
                }
            }
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - size.Width, wa.Bottom - size.Height);
        }

        // ---------- 配置加载/刷新同一入口（含 Minor-5 名单校验接线） ----------

        /// <summary>
        /// 设置保存后可调用的刷新入口：重读配置并走与加载相同的校验/重建流程。
        /// </summary>
        public void ReloadConfig()
        {
            LoadResult load = _store.Load();
            _config = load.Config != null ? load.Config : AppConfig.CreateDefault();
            EnsureConfigLists();
            ApplyScaleToStaticLayout();
            RefreshConfigState();
            ApplyHeightAnchored();
        }

        private void EnsureConfigLists()
        {
            if (_config.Devices == null) _config.Devices = new List<string>();
            if (_config.Credentials == null) _config.Credentials = new List<CredentialEntry>();
            // 密文为空的条目视同需重设（与设置层口径一致，仅运行时标记不落盘）
            foreach (CredentialEntry entry in _config.Credentials)
            {
                if (string.IsNullOrEmpty(entry.EncryptedSecret))
                    entry.NeedsReset = true;
            }
        }

        /// <summary>
        /// 按当前配置的 AutoClearClipboard 重建剪贴板守卫与凭据服务：
        /// 初始化时调用；设置变更后的重建由 Task 7 的保存事件接入本方法。
        /// 旧 Guard 先 Dispose（停掉其未到期定时器）。
        /// </summary>
        private void RebuildClipboardGuard()
        {
            if (_clipboardGuard != null)
                _clipboardGuard.Dispose();
            _clipboardGuard = new ClipboardGuard(_clipboardGateway, _config.AutoClearClipboard);
            _credentialService = new CredentialService(_protector, _clipboardGuard);
        }

        /// <summary>
        /// 加载/刷新配置的同一入口：跑 ValidateAll，名单有错时常驻错误提示并禁用
        /// 设备按钮（截图不可用），修正后自动恢复；随后重建动态按钮与根目录行。
        /// </summary>
        private void RefreshConfigState()
        {
            _deviceListError = DeviceNameValidator.ValidateAll(_config.Devices);

            RebuildDeviceButtons();
            RebuildCredentialControls();
            UpdateCredentialHeader();
            UpdateRootLabel();
            RefreshStateVisuals();

            if (_deviceListError != null)
            {
                ShowDeviceListErrorFeedback();
            }
            else if (_feedbackPersistent)
            {
                // 之前的常驻错误已消除，清掉提示恢复普通反馈
                _feedbackPersistent = false;
                _feedbackTimer.Stop();
                _feedbackLabel.Text = string.Empty;
            }
        }

        /// <summary>截图是否可用：名单无误且已设置根目录。</summary>
        private bool ScreenshotAvailable
        {
            get { return _deviceListError == null && !string.IsNullOrEmpty(_config.RootPath); }
        }

        private void RebuildDeviceButtons()
        {
            foreach (Button old in _deviceButtons)
            {
                _deviceZonePanel.Controls.Remove(old);
                old.Dispose();
            }
            _deviceButtons.Clear();
            if (_deviceHint != null)
            {
                _deviceZonePanel.Controls.Remove(_deviceHint);
                _deviceHint.Dispose();
                _deviceHint = null;
            }

            if (_config.Devices.Count == 0)
            {
                _deviceHint = CreateHintLabel("暂无设备，去设置添加", WeakTextColor, M(PanelLayout.DeviceRowHeight));
                _deviceZonePanel.Controls.Add(_deviceHint);
                return;
            }

            for (int i = 0; i < _config.Devices.Count; i++)
            {
                string device = _config.Devices[i];
                var button = new Button
                {
                    Text = device,
                    Tag = device,
                    Font = Font13,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = ButtonBackColor,
                    ForeColor = PrimaryTextColor,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Size = new Size(ContentWidthNow, M(PanelLayout.DeviceRowHeight)),
                    Location = new Point(M(SideMargin), i * (M(PanelLayout.DeviceRowHeight) + M(PanelLayout.DeviceRowGap)))
                };
                button.FlatAppearance.BorderColor = BorderColor;
                button.FlatAppearance.BorderSize = 1;
                button.Click += OnDeviceButtonClicked;
                _deviceButtons.Add(button);
                _deviceZonePanel.Controls.Add(button);
            }
        }

        private void RebuildCredentialControls()
        {
            foreach (Control old in _credentialControls)
            {
                _credentialZonePanel.Controls.Remove(old);
                old.Dispose();
            }
            _credentialControls.Clear();

            if (_config.IsCredentialCollapsed)
                return;

            if (_config.Credentials.Count == 0)
            {
                Control hint = CreateHintLabel("未设置凭据，去设置添加", AccentColor, M(PanelLayout.CredentialRowHeight));
                _credentialControls.Add(hint);
                _credentialZonePanel.Controls.Add(hint);
                return;
            }

            for (int i = 0; i < _config.Credentials.Count; i++)
            {
                CredentialEntry entry = _config.Credentials[i];
                var button = new Button
                {
                    Text = entry.NeedsReset ? entry.Title + "（需重设）" : entry.Title,
                    Tag = entry,
                    Font = Font13,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = ButtonBackColor,
                    ForeColor = entry.NeedsReset ? ErrorColor : PrimaryTextColor,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Size = new Size(ContentWidthNow, M(PanelLayout.CredentialRowHeight)),
                    Location = new Point(M(SideMargin), i * (M(PanelLayout.CredentialRowHeight) + M(PanelLayout.CredentialRowGap)))
                };
                button.FlatAppearance.BorderColor = BorderColor;
                button.FlatAppearance.BorderSize = 1;
                button.Click += OnCredentialButtonClicked;
                _credentialControls.Add(button);
                _credentialZonePanel.Controls.Add(button);
            }
        }

        /// <summary>空态提示行：虚线边（Paint 自绘）、指定文字色。</summary>
        private Label CreateHintLabel(string text, Color foreColor, int height)
        {
            var hint = new Label
            {
                Text = text,
                Font = Font13,
                ForeColor = foreColor,
                TextAlign = ContentAlignment.MiddleLeft,
                Size = new Size(ContentWidthNow, height),
                Location = new Point(M(SideMargin), 0)
            };
            hint.Paint += delegate(object sender, PaintEventArgs e)
            {
                var label = (Label)sender;
                using (var pen = new Pen(BorderColor))
                {
                    pen.DashStyle = DashStyle.Dash;
                    e.Graphics.DrawRectangle(pen, 0, 0, label.Width - 1, label.Height - 1);
                }
            };
            return hint;
        }

        private void UpdateCredentialHeader()
        {
            _credentialArrowLabel.Text = _config.IsCredentialCollapsed ? "›" : "˅";
        }

        private void UpdateRootLabel()
        {
            _rootLabel.Text = string.IsNullOrEmpty(_config.RootPath)
                ? "尚未设置保存目录"
                : "保存到：" + _config.RootPath;
        }

        // ---------- 交互 ----------

        private void OnDeviceButtonClicked(object sender, EventArgs e)
        {
            var button = sender as Button;
            if (button == null) return;
            string device = button.Tag as string;
            if (device == null) return;

            if (!_stateMachine.SelectDevice(device))
                return;

            if (_stateMachine.State == PanelState.Waiting)
            {
                _config.LastDevice = _stateMachine.CurrentDevice;
                SaveBestEffort();
            }
            OnStateChanged();
        }

        private void OnCredentialHeaderClicked(object sender, EventArgs e)
        {
            _config.IsCredentialCollapsed = !_config.IsCredentialCollapsed;
            SaveBestEffort(); // 失败不阻塞 UI
            RebuildCredentialControls();
            UpdateCredentialHeader();
            ApplyHeightAnchored();
        }

        private void OnCredentialButtonClicked(object sender, EventArgs e)
        {
            var button = sender as Button;
            if (button == null) return;
            var entry = button.Tag as CredentialEntry;
            if (entry == null) return;
            if (_credentialService == null) return;

            // 全链路走 CredentialService：解密→写剪贴板+令牌→30 秒自动清空（Guard）
            CopyResult result = _credentialService.Copy(entry);
            switch (result)
            {
                case CopyResult.Copied:
                    ShowSuccessFeedback("已复制");
                    break;
                case CopyResult.NeedsReset:
                    // 运行时标记并刷新按钮显示（内存标记，不落盘）
                    entry.NeedsReset = true;
                    RebuildCredentialControls();
                    ShowFeedback("该凭据需重新设置，请到设置中重新填写", true);
                    break;
                default: // Failed
                    ShowFeedback("复制失败，请重试", true);
                    break;
            }
        }

        private void OnSettingsClicked(object sender, EventArgs e)
        {
            // 设置窗口（Task 7）：保存制——窗内对传入配置深拷贝编辑，本方在
            // Saved 后才以 ResultConfig 替换当前配置并全量刷新；取消零副作用。
            AppConfig savedConfig = null;
            using (var form = new SettingsForm(_config, _store, _protector))
            {
                form.Saved += delegate { savedConfig = form.ResultConfig; };
                form.ShowDialog(this);
            }
            if (savedConfig == null)
                return;

            _config = savedConfig;
            EnsureConfigLists();
            ApplyScaleToStaticLayout();
            RebuildClipboardGuard();
            RefreshConfigState();
            ApplyHeightAnchored();
            ShowSuccessFeedback("设置已保存");
        }

        private void OnRootLabelClicked(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_config.RootPath))
            {
                // 未设根目录与「今日目录不存在」是两回事，文案须准确（审查 M-5）
                ShowFeedback("尚未设置保存目录，请先在设置中选择", true);
                return;
            }
            string todayDir = Path.Combine(_config.RootPath, _clock.Now.ToString("yyyy-MM-dd"));
            if (!Directory.Exists(todayDir))
            {
                // 只提示不建夹：没有今日截图就不该凭空多出一个空日期夹
                ShowFeedback("今天尚无截图", true);
                return;
            }
            // 打开今日文件夹并以 explorer /select 选中最新一张图（Task 5）；
            // 枚举失败或夹内无图时退化为只打开目录。
            string latest = null;
            DateTime latestTime = DateTime.MinValue;
            try
            {
                foreach (string file in Directory.GetFiles(todayDir, "*.png"))
                {
                    DateTime written = File.GetLastWriteTime(file);
                    if (latest == null || written > latestTime)
                    {
                        latest = file;
                        latestTime = written;
                    }
                }
            }
            catch
            {
                latest = null;
            }
            try
            {
                if (latest != null)
                    Process.Start("explorer.exe", "/select,\"" + latest + "\"");
                else
                    Process.Start("explorer.exe", "\"" + todayDir + "\"");
            }
            catch (Exception ex)
            {
                ShowFeedback("打开文件夹失败：" + ex.Message, true);
            }
        }

        /// <summary>等待态视觉：设备标题行文案/颜色、按钮高亮与可用性、窗体边框。</summary>
        private void RefreshStateVisuals()
        {
            bool waiting = _stateMachine.State == PanelState.Waiting;

            _deviceHeaderLabel.Text = waiting ? "请点击要截图的窗口" : "截图";
            _deviceHeaderLabel.Font = waiting ? Font13Bold : Font13;
            _deviceHeaderLabel.ForeColor = waiting ? AccentColor : SecondaryTextColor;

            foreach (Button button in _deviceButtons)
            {
                string device = button.Tag as string;
                if (waiting && device == _stateMachine.CurrentDevice)
                {
                    button.BackColor = AccentColor;
                    button.ForeColor = Color.White;
                }
                else if (!waiting && _stateMachine.State == PanelState.Idle
                    && device != null && device == _config.LastDevice)
                {
                    button.BackColor = LastDeviceBackColor;
                    button.ForeColor = PrimaryTextColor;
                }
                else
                {
                    button.BackColor = ButtonBackColor;
                    button.ForeColor = PrimaryTextColor;
                }
                button.Enabled = ScreenshotAvailable;
            }

            Invalidate();
        }

        // ---------- 选窗钩子接线（Task 3） ----------

        /// <summary>
        /// 状态变化集中点：刷新视觉，并按状态统一启停钩子（Waiting→Start，其余→Stop）。
        /// 设备按钮、ExternalClick、Cancel 三条路径都只改状态机再调本方法，防钩子泄漏。
        /// </summary>
        private void OnStateChanged()
        {
            RefreshStateVisuals();
            if (_stateMachine.State == PanelState.Waiting)
            {
                try
                {
                    // Bounds 随拖动变化，Start 时取当前值；等待态期间禁用拖动（见
                    // OnDragMouseDown），保证 selfBounds 在整个等待期有效。
                    // Start 返回钩子会话号：已在运行（Waiting 内切换设备）返回现行
                    // 会话号、守卫不变；新会话则更新守卫（M-3 代次守卫）。
                    _waitingSessionId = _hooks.Start(this.Bounds);
                }
                catch (Exception ex)
                {
                    _waitingSessionId = 0;
                    _stateMachine.Cancel();
                    RefreshStateVisuals();
                    _hooks.Stop();
                    ShowFeedback("选窗启动失败：" + ex.Message, true);
                }
            }
            else
            {
                _hooks.Stop();
            }
        }

        /// <summary>
        /// 外部点击选窗：先解析目标再决定状态转移——只有命中有效窗口才
        /// BeginCapture（状态机无 Capturing→Waiting 转移，排除分支必须留在 Waiting）。
        /// </summary>
        private void OnHookExternalClick(Point pt, int sessionId)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<Point, int>(OnHookExternalClick), pt, sessionId);
                return;
            }
            // 代次守卫（M-3）：事件所属会话号与当前 Waiting 会话不一致 →
            // 上一会话的迟到事件，直接丢弃，不解析不转移状态。
            if (sessionId != _waitingSessionId)
                return;
            if (_stateMachine.State != PanelState.Waiting)
                return;

            ResolveResult result = WindowResolver.ResolveTopLevel(pt, this.Handle);
            switch (result.Reason)
            {
                case ResolveReason.Excluded:
                    ShowFeedback("点到桌面/任务栏，已忽略，请点目标窗口", true);
                    // 保持 Waiting 并重启钩子（新一轮 30 秒超时）
                    RestartHooksWhileWaiting();
                    break;
                case ResolveReason.Minimized:
                    ShowFeedback("该窗口已最小化，请先打开", true);
                    _stateMachine.Cancel();
                    OnStateChanged();
                    break;
                case ResolveReason.Ok:
                    if (!_stateMachine.BeginCapture())
                        return;
                    OnStateChanged(); // Capturing → 停钩
                    // 进 Capturing 即组请求快照（Task 5）：根目录取当前配置、
                    // 日期在此刻以 IClock 固定，保存与失败重试全程共用。
                    _currentRequest = new CaptureRequest
                    {
                        Device = _stateMachine.CurrentDevice,
                        RootPath = _config.RootPath,
                        Date = _clock.Now,
                        RequestId = _stateMachine.CurrentRequestId
                    };
                    OnTargetResolved(result, _stateMachine.CurrentRequestId);
                    break;
                default: // Gone
                    ShowFeedback("该窗口已关闭，请重新点选", true);
                    RestartHooksWhileWaiting();
                    break;
            }
        }

        /// <summary>
        /// 保持 Waiting 并重启钩子（新一轮 30 秒超时）：Excluded 与 Gone 两分支共用。
        /// 先 Stop 再 Start；Start 失败时 Cancel 状态机回 Idle、经既有集中入口
        /// OnStateChanged 停钩并刷新视觉，再给出失败反馈，与原两分支内联逻辑
        /// 的语义完全一致（OnStateChanged 在非 Waiting 时亦会 Stop，幂等无副作用）。
        /// </summary>
        private void RestartHooksWhileWaiting()
        {
            _hooks.Stop();
            try
            {
                // 重启产生新会话：更新守卫，旧会话迟到的点击事件将被丢弃（M-3）
                _waitingSessionId = _hooks.Start(this.Bounds);
            }
            catch (Exception ex)
            {
                _waitingSessionId = 0;
                _stateMachine.Cancel();
                OnStateChanged();
                ShowFeedback("选窗启动失败：" + ex.Message, true);
            }
        }

        private void OnHookCancelRequested()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(OnHookCancelRequested));
                return;
            }
            bool cancelled = _stateMachine.Cancel();
            OnStateChanged();
            if (cancelled)
                ShowFeedback("已取消选窗", false);
        }

        private void OnHookTimeoutElapsed()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(OnHookTimeoutElapsed));
                return;
            }
            bool cancelled = _stateMachine.Cancel();
            OnStateChanged(); // 钩子已自停，Stop 幂等
            if (cancelled)
                ShowFeedback("等待超时，已取消", true);
        }

        /// <summary>
        /// Task 4 接入点：状态机已 BeginCapture（Capturing）。截图引擎放工作线程
        /// 执行（PrintWindow 同步阻塞不能压 UI/钩子线程），结果 BeginInvoke 回 UI
        /// 线程处理；hideSelf/showSelf 由此处注入、用 Invoke 切 UI 线程改 Opacity
        /// 并等待完成（BitBlt 防自遮挡）。成功图由 OnCaptureCompleted 交 Task 5
        /// 保存链路（BeginSave）；失败路径 FailCapture 回 Idle 且经
        /// OnStateChanged 集中点停钩。
        /// </summary>
        private void OnTargetResolved(ResolveResult result, int requestId)
        {
            // M-1 守卫：保存（重试）在途时 _pendingImage 正在被工作线程编码，
            // 此时不得 Dispose 它、也不得发起新截图——直接忽略本次并提示，
            // 本次已 BeginCapture 的状态经 FailCapture 回 Idle，等保存完成后再截。
            if (_saveInFlight)
            {
                ShowFeedback("正在保存上一张，请稍候", true);
                _currentRequest = null;
                if (_stateMachine.FailCapture())
                    OnStateChanged();
                return;
            }

            // 新一次截图开始：上次保存失败待重试的旧图若仍未处理，先释放（Task 5 口径）
            if (_pendingImage != null)
            {
                _pendingImage.Dispose();
                _pendingGeneration++;
                _pendingImage = null;
                _pendingRequest = null;
            }

            IntPtr hwnd = result.Hwnd;
            string title = string.IsNullOrEmpty(result.Title) ? "（无标题）" : result.Title;
            CaptureMode mode = _config.CaptureMode;
            Rectangle selfBounds = this.Bounds;

            Action hideSelf = delegate
            {
                if (IsDisposed) return;
                Invoke(new Action(delegate { Opacity = 0; }));
            };
            Action showSelf = delegate
            {
                if (IsDisposed) return;
                Invoke(new Action(delegate { Opacity = 1; }));
            };

            var worker = new System.Threading.Thread(new System.Threading.ThreadStart(delegate
            {
                CaptureOutcome outcome;
                try
                {
                    outcome = new WindowCapture().Capture(hwnd, mode, selfBounds, hideSelf, showSelf);
                }
                catch (Exception ex)
                {
                    outcome = new CaptureOutcome
                    {
                        Status = CaptureStatus.Failed,
                        Image = null,
                        Message = "截图异常：" + ex.Message
                    };
                }

                if (IsDisposed)
                {
                    if (outcome.Image != null) outcome.Image.Dispose();
                    return;
                }
                try
                {
                    BeginInvoke(new Action(delegate { OnCaptureCompleted(outcome, requestId, title); }));
                }
                catch (ObjectDisposedException)
                {
                    // 窗体正在关闭：迟到结果直接释放丢弃
                    if (outcome.Image != null) outcome.Image.Dispose();
                }
                catch (InvalidOperationException)
                {
                    if (outcome.Image != null) outcome.Image.Dispose();
                }
            }));
            worker.IsBackground = true;
            worker.Name = "InspectionWidget.CaptureWorker";
            worker.Start();
        }

        /// <summary>
        /// 截图结果回 UI 线程处理：先做迟到校验（requestId 与状态机 CurrentRequestId
        /// 比对，不一致直接 Dispose 图并丢弃），再按状态反馈并回 Idle。
        /// </summary>
        private void OnCaptureCompleted(CaptureOutcome outcome, int requestId, string title)
        {
            if (requestId != _stateMachine.CurrentRequestId
                || _stateMachine.State != PanelState.Capturing)
            {
                if (outcome.Image != null) outcome.Image.Dispose();
                return;
            }

            bool completed = false;
            switch (outcome.Status)
            {
                case CaptureStatus.Ok:
                    // Task 5 保存链路：图交 BeginSave 在工作线程落盘，结果回 UI 后
                    // 才 CompleteCapture 回 Idle（见 OnSaveCompleted），此处不释放图。
                    if (outcome.Image == null)
                    {
                        ShowFeedback("截图失败，请重试", true);
                        completed = _stateMachine.FailCapture();
                        break;
                    }
                    // M-2 防御分支：请求快照缺失或代次不符时不再现组请求——现组
                    // 会用完成时刻的 _clock.Now 引入第二套日期口径，与「日期在采集
                    // 开始固定」相悖；该分支主路径不可达，直接释放图并 FailCapture。
                    if (_currentRequest == null || _currentRequest.RequestId != requestId)
                    {
                        outcome.Image.Dispose();
                        ShowFeedback("截图失败，请重试", true);
                        completed = _stateMachine.FailCapture();
                        break;
                    }
                    BeginSave(outcome.Image, _currentRequest, true);
                    break;
                case CaptureStatus.SuspectProtected:
                    ShowFeedback("该窗口可能受系统保护，截不到画面", true);
                    completed = _stateMachine.FailCapture();
                    break;
                case CaptureStatus.WindowGone:
                    ShowFeedback(string.IsNullOrEmpty(outcome.Message) ? "该窗口已关闭，请重新点选" : outcome.Message, true);
                    completed = _stateMachine.FailCapture();
                    break;
                default: // Failed
                    ShowFeedback(string.IsNullOrEmpty(outcome.Message) ? "截图失败，请重试" : outcome.Message, true);
                    completed = _stateMachine.FailCapture();
                    break;
            }

            if (completed)
                OnStateChanged(); // 回 Idle → 集中点停钩（Capturing 时钩已停，幂等）
        }

        // ---------- 保存接线（Task 5） ----------

        /// <summary>
        /// 保存走工作线程（磁盘 IO 不压 UI 线程），结果 BeginInvoke 回 UI 线程。
        /// completesCapture=true 表示这是截图链路的保存：结果回来才结束 Capturing。
        /// </summary>
        private void BeginSave(Bitmap image, CaptureRequest request, bool completesCapture)
        {
            _saveInFlight = true;
            // 捕获发起时刻的待保存图代次，随保存结果带回 OnSaveCompleted 做存活校验
            int saveGeneration = _pendingGeneration;
            var worker = new System.Threading.Thread(new System.Threading.ThreadStart(delegate
            {
                StorageSaveResult result;
                try
                {
                    result = _saver.Save(image, request.RootPath, request.Device, request.Date);
                }
                catch (Exception ex)
                {
                    result = new StorageSaveResult { Saved = false, Error = "尚未保存：" + ex.Message };
                }

                if (IsDisposed)
                {
                    image.Dispose();
                    return;
                }
                try
                {
                    BeginInvoke(new Action(delegate { OnSaveCompleted(result, image, request, completesCapture, saveGeneration); }));
                }
                catch (Exception)
                {
                    // 窗体正在关闭：迟到结果直接释放丢弃
                    image.Dispose();
                }
            }));
            worker.IsBackground = true;
            worker.Name = "InspectionWidget.SaveWorker";
            worker.Start();
        }

        /// <summary>
        /// 保存结果回 UI 线程处理。成功：释放图；暂存成功按简报 D 口径——反馈行以
        /// 错误色显示「✓ 文件名（已暂存）：完整路径」5 秒（比普通 2 秒长以引注意），
        /// 同时完整路径记入根目录行 tooltip，提示消失后仍可查。
        /// 失败口径（简报 D 简化版）：保留 Bitmap 在 _pendingImage、状态回 Idle，
        /// 反馈行常驻「尚未保存，点击此处重试」，点击反馈行以同一 CaptureRequest
        /// （含原日期）重试；新一次截图开始时未处理的旧图先被 Dispose。
        /// </summary>
        private void OnSaveCompleted(StorageSaveResult result, Bitmap image, CaptureRequest request, bool completesCapture, int saveGeneration)
        {
            _saveInFlight = false;

            if (completesCapture && _stateMachine.CompleteCapture())
                OnStateChanged(); // 回 Idle → 集中点停钩（幂等）

            if (result != null && result.Saved)
            {
                if (object.ReferenceEquals(_pendingImage, image))
                {
                    _pendingImage = null;
                    _pendingRequest = null;
                    _pendingGeneration++;
                }
                image.Dispose();
                _rootToolTip.SetToolTip(_rootLabel, result.FullPath);
                if (result.UsedFallback)
                {
                    ShowFeedbackInternal("✓ " + Path.GetFileName(result.FullPath) + "（已暂存）：" + result.FullPath,
                        ErrorColor, false, 5000);
                }
                else
                {
                    ShowSuccessFeedback("✓ " + Path.GetFileName(result.FullPath));
                }
                return;
            }

            // 失败：保留原图供重试（Saver 从不 Dispose 传入图，所有权在本方）。
            // M-1 登记前校验：图若已失效（被释放）不得重新登记为 _pendingImage，
            // 否则此后点反馈行重试会对已释放图反复保存、反复失败；清掉同引用的
            // 旧登记并提示重新截图。
            // 修复轮二（M-1 变体 B）：在此基础上再比对代次——IsImageUsable 只是
            // 登记前一刻的瞬时探针，若本次保存发起后 _pendingImage 已被 Dispose
            // 或替换（代次已递增），本结果即迟到结果：图的存活状态已不可信，
            // 直接丢弃不重登记，避免把已释放图登记成待重试图留下脏状态。
            if (saveGeneration != _pendingGeneration || !IsImageUsable(image))
            {
                if (object.ReferenceEquals(_pendingImage, image))
                {
                    _pendingImage = null;
                    _pendingRequest = null;
                    _pendingGeneration++;
                }
                ShowFeedback("尚未保存：原图已失效，请重新截图", true);
                return;
            }
            if (_pendingImage != null && !object.ReferenceEquals(_pendingImage, image))
            {
                _pendingImage.Dispose();
                _pendingGeneration++;
            }
            _pendingImage = image;
            _pendingRequest = request;
            _pendingGeneration++;
            string detail = result != null ? result.Error : null;
            if (!string.IsNullOrEmpty(detail) && detail.StartsWith("尚未保存：", StringComparison.Ordinal))
                detail = detail.Substring("尚未保存：".Length);
            ShowPersistentFeedback(string.IsNullOrEmpty(detail)
                ? "尚未保存，点击此处重试"
                : "尚未保存，点击此处重试：" + detail, ErrorColor);
        }

        /// <summary>
        /// 图可用性探针（M-1）：已释放的 Bitmap 读尺寸会抛异常（GDI+ 已释放
        /// 句柄），借此在把图登记为待重试前校验它未被 Dispose；有效图只读尺寸无副作用。
        /// </summary>
        private static bool IsImageUsable(Bitmap image)
        {
            if (image == null)
                return false;
            try
            {
                return image.Width > 0 && image.Height > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>反馈行点击：仅当有待重试的图时作为重试入口，其余情况忽略。</summary>
        private void OnFeedbackLabelClicked(object sender, EventArgs e)
        {
            if (_saveInFlight)
                return;
            if (_pendingImage == null || _pendingRequest == null)
                return;
            // 字段保留登记：成功后由 OnSaveCompleted 按引用清掉，再失败会重新登记同一图
            BeginSave(_pendingImage, _pendingRequest, false);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            bool waiting = _stateMachine.State == PanelState.Waiting;
            if (waiting)
            {
                using (var pen = new Pen(AccentColor, 2f))
                {
                    e.Graphics.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
                }
            }
            else
            {
                using (var pen = new Pen(BorderColor, 1f))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
                }
            }
        }

        // ---------- 反馈行 ----------

        /// <summary>普通反馈：显示后 2 秒自动清空。isError 用错误色，否则次文本色。</summary>
        public void ShowFeedback(string text, bool isError)
        {
            ShowFeedbackInternal(text, isError ? ErrorColor : SecondaryTextColor, false);
        }

        /// <summary>成功反馈（成功色，2 秒自动清空）。</summary>
        public void ShowSuccessFeedback(string text)
        {
            ShowFeedbackInternal(text, SuccessColor, false);
        }

        private void ShowPersistentFeedback(string text, Color color)
        {
            ShowFeedbackInternal(text, color, true);
        }

        /// <summary>设备名单错误的常驻提示（RefreshConfigState 与反馈计时恢复共用）。</summary>
        private void ShowDeviceListErrorFeedback()
        {
            ShowPersistentFeedback("设备名单有问题：" + _deviceListError + "，请到设置中修正", ErrorColor);
        }

        private void ShowFeedbackInternal(string text, Color color, bool persistent)
        {
            ShowFeedbackInternal(text, color, persistent, 2000);
        }

        /// <summary>带显示时长的反馈：暂存成功提示用 5 秒（简报 D 口径），其余 2 秒。</summary>
        private void ShowFeedbackInternal(string text, Color color, bool persistent, int milliseconds)
        {
            _feedbackPersistent = persistent;
            _feedbackLabel.Text = text;
            _feedbackLabel.ForeColor = color;
            _feedbackTimer.Stop();
            _feedbackTimer.Interval = milliseconds;
            if (!persistent)
                _feedbackTimer.Start();
        }

        // ---------- 首次根目录 ----------

        private void OnFirstShown(object sender, EventArgs e)
        {
            if (_rootPromptShown) return;
            _rootPromptShown = true;
            if (!string.IsNullOrEmpty(_config.RootPath)) return;

            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择巡检截图保存根目录，只需选这一次";
                dialog.ShowNewFolderButton = true;
                if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(dialog.SelectedPath))
                {
                    string chosen = dialog.SelectedPath;
                    _config.RootPath = chosen;
                    SaveResult saved = SaveBestEffortResult();
                    if (saved != null && saved.Saved)
                    {
                        UpdateRootLabel();
                        RefreshStateVisuals();
                        ShowSuccessFeedback("保存目录已设置");
                    }
                    else
                    {
                        _config.RootPath = null;
                        UpdateRootLabel();
                        RefreshStateVisuals();
                        ShowFeedback("保存目录设置失败：" + (saved != null ? saved.Error : "未知错误"), true);
                    }
                }
                else
                {
                    RefreshStateVisuals(); // RootPath 为空 → 设备按钮保持禁用
                    ShowFeedback("未选目录，截图暂不可用", true);
                }
            }
        }

        // ---------- 拖动与位置保存 ----------

        private void AttachDragHandlers(Control control)
        {
            control.MouseDown += OnDragMouseDown;
            control.MouseMove += OnDragMouseMove;
            control.MouseUp += OnDragMouseUp;
        }

        private void OnDragMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            // 等待态禁用拖动：钩子以 Start 时的 Bounds 判自身矩形，拖动会使其失效。
            if (_stateMachine.State == PanelState.Waiting) return;
            var control = sender as Control;
            if (control == null) return;
            _dragging = true;
            // _dragOffset 必须换算为相对窗体的坐标：发送者可能是标题栏内的标签
            // （titleLabel 在 (10,9)、dateLabel 在 (82,11)），e.X/e.Y 是相对发送者
            // 控件的坐标，直接存会在 MouseMove 按窗体坐标使用时让窗体跳移一个
            // 标签偏移量（审查 I-1）。经 PointToScreen 再 PointToClient 统一到窗体坐标。
            Point screenPoint = control.PointToScreen(e.Location);
            _dragOffset = PointToClient(screenPoint);
        }

        private void OnDragMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            Point screen = ((Control)sender).PointToScreen(e.Location);
            Location = new Point(screen.X - _dragOffset.X, screen.Y - _dragOffset.Y);
        }

        private void OnDragMouseUp(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            SaveWindowPosition();
        }

        private void SaveWindowPosition()
        {
            if (WindowState != FormWindowState.Normal) return;
            _config.WindowLeft = Left;
            _config.WindowTop = Top;
            SaveBestEffort();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            // 退出兜底（Task 6）：先按回执核对清剪贴板（有回执且序列号+令牌
            // 匹配才清，Guard 内部处理），再 Dispose Guard，最后走原有逻辑。
            if (_clipboardGuard != null)
            {
                _clipboardGuard.TryClearNow();
                _clipboardGuard.Dispose();
            }
            _hooks.Stop();
            _hooks.Dispose();
            SaveWindowPosition();
        }

        // ---------- 全局异常恢复（Task 8） ----------

        /// <summary>
        /// Program 的 Application.ThreadException 兜底调用：UI 线程出现未预料
        /// 异常后，把面板恢复到待命——Waiting/Capturing 经状态机回 Idle 并走
        /// OnStateChanged 集中点停钩；保存失败的待重试图（_pendingImage）刻意
        /// 保留不释放，反馈行恢复「尚未保存，点击此处重试」常驻提示，用户仍可
        /// 点反馈行重试或开始新截图（新截图时旧图按既有口径释放）。
        /// 本方法在非 UI 线程被调用时先 BeginInvoke 切回 UI 线程。
        /// </summary>
        public void RecoverToIdle()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(RecoverToIdle));
                return;
            }

            if (_stateMachine.State == PanelState.Waiting)
                _stateMachine.Cancel();
            else if (_stateMachine.State == PanelState.Capturing)
                _stateMachine.FailCapture();
            OnStateChanged(); // 回 Idle 集中点：刷新视觉 + 停钩（幂等）

            if (_pendingImage != null && _pendingRequest != null)
                ShowPersistentFeedback("尚未保存，点击此处重试", ErrorColor);
            else
                ShowFeedback("发生未预期异常，已恢复待命", true);
        }

        // ---------- 保存（BestEffort） ----------

        private void SaveBestEffort()
        {
            SaveBestEffortResult();
        }

        private SaveResult SaveBestEffortResult()
        {
            try
            {
                return _store.Save(_config);
            }
            catch
            {
                return null;
            }
        }
    }
}
