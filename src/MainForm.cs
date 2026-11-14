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
using InspectionWidget.Links;
using InspectionWidget.Planning;
using IClock = InspectionWidget.Storage.IClock;
using InspectionFolderNavigator = InspectionWidget.Storage.InspectionFolderNavigator;
using ScreenshotSaver = InspectionWidget.Storage.ScreenshotSaver;
using StorageSaveResult = InspectionWidget.Storage.SaveResult;
using SystemClock = InspectionWidget.Storage.SystemClock;

namespace InspectionWidget
{
    /// <summary>
    /// 主面板（v1.1.0 方案 B）：无边框置顶面板，352px 宽，含标题与完整日期、
    /// 快捷链接、今日巡检计划卡、两列凭据、带已点状态与恢复未点的截图设备区，
    /// 以及固定底部“打开巡检文件夹”主操作、反馈区和路径显示。
    /// </summary>
    public class MainForm : Form
    {
        // 颜色令牌（v1.1.0 方案 B 与 UiTheme 对齐）
        private static readonly Color AccentColor = UiTheme.AccentColor;
        private static readonly Color AccentSoftColor = UiTheme.AccentSoftColor;
        private static readonly Color AccentBorderColor = UiTheme.AccentBorderColor;
        private static readonly Color SuccessColor = UiTheme.SuccessColor;
        private static readonly Color SuccessSoftColor = UiTheme.SuccessSoftColor;
        private static readonly Color ErrorColor = UiTheme.ErrorColor;
        private static readonly Color WarningColor = UiTheme.WarningColor;
        private static readonly Color WarningSoftColor = UiTheme.WarningSoftColor;
        private static readonly Color PrimaryTextColor = UiTheme.PrimaryTextColor;
        private static readonly Color SecondaryTextColor = UiTheme.SecondaryTextColor;
        private static readonly Color WeakTextColor = UiTheme.WeakTextColor;
        private static readonly Color PanelBackColor = UiTheme.PageBackColor;
        private static readonly Color SurfaceColor = UiTheme.SurfaceColor;
        private static readonly Color BorderColor = UiTheme.BorderColor;
        private static readonly Color BorderSoftColor = UiTheme.BorderSoftColor;
        private static readonly Color LastDeviceBackColor = UiTheme.AccentSoftColor;
        private static readonly Color ButtonBackColor = Color.White;

        private const int SideMargin = 12;

        // 界面缩放（v1.0.1）：度量一律经 M() 按当前配置档位缩放，布局公式在 PanelLayout。
        private double ScaleFactor
        {
            get
            {
                if (_config == null) return 1.0;
                double system;
                try
                {
                    system = Math.Max(1.0, DeviceDpi / 96.0);
                }
                catch
                {
                    system = 1.0;
                }
                return UiScale.Factor(_config.UiScalePercent) * system;
            }
        }

        private int M(int value) { return PanelLayout.Scale(value, ScaleFactor); }

        private int PanelWidthNow { get { return PanelLayout.ScaledSchemeBPanelWidth(ScaleFactor); } }

        private int ContentWidthNow
        {
            get
            {
                return Math.Max(1, PanelWidthNow - M(SideMargin) * 2
                    - SystemInformation.VerticalScrollBarWidth);
            }
        }

        private readonly ConfigStore _store;
        private readonly PanelStateMachine _stateMachine;
        private readonly HookManager _hooks;
        private readonly QuickLinkLauncher _linkLauncher = new QuickLinkLauncher();
        private readonly InspectionFolderNavigator _folderNavigator = new InspectionFolderNavigator();
        private InspectionProgressStore _progressStore;
        private InspectionSessionController _session;
        private InspectionPlanToken _waitingPlanToken;
        private AppConfig _config;
        private string _configRevision = string.Empty;
        private bool _configWriteProtected;
        private string _configLoadError;

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
            // 日期文件夹名模式快照（v1.0.2）：与 Date 同刻固定，保存/重试共用
            public string FolderPattern;
            public InspectionPlanToken PlanToken;
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
        private bool _settingsDialogOpen;
        private bool _hiddenForCapture;
        private Timer _dayRolloverTimer;

        private Panel _titleBarPanel;
        private Label _titleLabel;
        private Label _dateLabel;
        private Button _settingsButton;
        private Button _minimizeButton;
        private Button _closeButton;
        private ToolTip _titleToolTip;
        private ToolTip _deviceToolTip;
        private Font _fontGear;
        private Bitmap _gearImage;
        private bool _settingsUsesGearImage;

        // 系统托盘（v1.0.2）：常驻 NotifyIcon，最小化 = 隐藏到托盘，托盘恢复/退出。
        private NotifyIcon _trayIcon;

        private Panel _scrollHost;
        private Panel _quickLinkHeaderPanel;
        private Label _quickLinkHeaderLabel;
        private Label _quickLinkSubLabel;
        private Panel _quickLinkZonePanel;
        private readonly List<Control> _quickLinkControls = new List<Control>();

        private Panel _planCardPanel;
        private Label _planTitleLabel;
        private CheckBox _planToggleButton;
        private Label _planProgressLabel;
        private Label _planCountLabel;
        private Panel _planTrackPanel;
        private Panel _planFillPanel;
        private Panel _planActionPanel;
        private Button _showVisitedButton;
        private Button _resetRoundButton;
        private Label _planDisabledHintLabel;

        private Panel _deviceHeaderPanel;
        private Label _deviceHeaderLabel;
        private Label _deviceSubLabel;
        private Panel _credentialHeaderPanel;
        private Label _credentialHeaderLabel;
        private Label _credentialArrowLabel;
        private Label _deviceArrowLabel;
        private Panel _credentialZonePanel;
        private Panel _deviceZonePanel;
        private Panel _bottomPanel;
        private Button _openFolderButton;
        private Label _feedbackLabel;
        private Label _rootLabel;
        private Timer _feedbackTimer;
        private bool _feedbackPersistent;

        private Control _deviceHint;
        private DeviceDisplaySnapshot _deviceDisplay = new DeviceDisplaySnapshot
        {
            Items = new List<DeviceDisplayItem>(),
            EmptyKind = DeviceListEmptyKind.None
        };
        private readonly List<DeviceRowWidget> _deviceRows = new List<DeviceRowWidget>();
        private readonly List<Control> _credentialControls = new List<Control>();

        private sealed class DeviceRowWidget
        {
            public string Device;
            public bool Visited;
            public Panel Panel;
            public Label NameLabel;
            public Label StatusLabel;
            public Button UnmarkButton;
        }

        private bool _dragging;
        private Point _dragOffset;

        // 缓存字体（15 加粗 / 13 常规 / 13 加粗 / 12 常规 / 12 加粗 / 11 常规）：全窗体复用。
        private Font _font15Bold;
        private Font _font13;
        private Font _font13Bold;
        private Font _font12;
        private Font _font12Bold;
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

        private Font Font15Bold
        {
            get { return GetScaledFont(ref _font15Bold, 15f, FontStyle.Bold); }
        }

        private Font Font13
        {
            get { return GetScaledFont(ref _font13, 13f, FontStyle.Regular); }
        }

        private Font Font13Bold
        {
            get { return GetScaledFont(ref _font13Bold, 13f, FontStyle.Bold); }
        }

        private Font Font12
        {
            get { return GetScaledFont(ref _font12, 12f, FontStyle.Regular); }
        }

        private Font Font12Bold
        {
            get { return GetScaledFont(ref _font12Bold, 12f, FontStyle.Bold); }
        }

        private Font Font11
        {
            get { return GetScaledFont(ref _font11, 11f, FontStyle.Regular); }
        }

        // 设置按钮齿轮（v1.0.2）：Segoe UI Symbol 的 ⚙ 才是标准齿轮字形，
        // 基准 16px（比正文 13px 大一号，一眼可辨），随界面缩放因子放大。
        private Font GearFont
        {
            get
            {
                float target = 16f * (float)ScaleFactor;
                if (_fontGear == null || _fontGear.Size != target)
                {
                    if (_fontGear != null) _fontGear.Dispose();
                    _fontGear = new Font("Segoe UI Symbol", target, FontStyle.Regular, GraphicsUnit.Pixel);
                }
                return _fontGear;
            }
        }

        private static bool HasSymbolFont
        {
            get
            {
                foreach (FontFamily family in FontFamily.Families)
                {
                    if (family.Name == "Segoe UI Symbol")
                        return true;
                }
                return false;
            }
        }

        /// <summary>
        /// 设置按钮视觉（v1.0.2）：有 Segoe UI Symbol → ⚙ 字形 + 齿轮字体；
        /// 字体缺失 → GearArt 代码绘制的齿轮位图兜底（文字清空、走 Image）。
        /// </summary>
        private void ApplySettingsButtonVisual()
        {
            if (HasSymbolFont)
            {
                _settingsButton.Image = null;
                _settingsButton.Text = "⚙";
                _settingsButton.Font = GearFont;
                _settingsUsesGearImage = false;
            }
            else
            {
                _settingsButton.Text = string.Empty;
                _settingsButton.Font = Font13;
                int imageSize = M(18);
                if (_gearImage == null || _gearImage.Width != imageSize)
                {
                    if (_gearImage != null) _gearImage.Dispose();
                    _gearImage = GearArt.DrawGear(imageSize, SecondaryTextColor);
                }
                _settingsButton.Image = _gearImage;
                _settingsUsesGearImage = true;
            }
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
                if (_titleToolTip != null) _titleToolTip.Dispose();
                if (_deviceToolTip != null) _deviceToolTip.Dispose();
                if (_trayIcon != null) _trayIcon.Dispose();
                if (_feedbackTimer != null) _feedbackTimer.Dispose();
                if (_dayRolloverTimer != null) _dayRolloverTimer.Dispose();
                if (_font15Bold != null) _font15Bold.Dispose();
                if (_font13 != null) _font13.Dispose();
                if (_font13Bold != null) _font13Bold.Dispose();
                if (_font12 != null) _font12.Dispose();
                if (_font12Bold != null) _font12Bold.Dispose();
                if (_font11 != null) _font11.Dispose();
                if (_fontGear != null) _fontGear.Dispose();
                if (_gearImage != null) _gearImage.Dispose();
            }
            base.Dispose(disposing);
        }

        private static string FormatFullDate(DateTime date)
        {
            string[] weekdays = { "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六" };
            return date.ToString("yyyy年M月d日 ") + weekdays[(int)date.DayOfWeek];
        }

        private static string ResolveProgressPath(ConfigStore store)
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localAppData))
                return Path.Combine(Path.Combine(localAppData, "InspectionWidget"), "progress.json");
            string dir = store != null && !string.IsNullOrEmpty(store.AuthoritativePath)
                ? Path.GetDirectoryName(store.AuthoritativePath)
                : Path.GetTempPath();
            return Path.Combine(dir ?? Path.GetTempPath(), "progress.json");
        }

        public MainForm()
        {
            _store = new ConfigStore();
            _stateMachine = new PanelStateMachine();
            _hooks = new HookManager();
            _hooks.ExternalClick += OnHookExternalClick;
            _hooks.CancelRequested += OnHookCancelRequested;
            _hooks.TimeoutElapsed += OnHookTimeoutElapsed;

            Text = "巡检助手";
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            // v1.0.2 口径改定（2026-11-03 用户实机反馈）：不再进任务栏，常驻系统托盘。
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = PanelBackColor;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;

            // 应用图标（v1.0.2）：与 exe 图标同源（嵌入资源），任务栏/Alt-Tab 不再是默认图标
            Icon appIcon = AppIcon.Load();
            if (appIcon != null)
                Icon = appIcon;

            LoadResult load = _store.Load();
            _config = load.Config != null ? load.Config : AppConfig.CreateDefault();
            _configRevision = load.Revision ?? string.Empty;
            _configWriteProtected = load.Kind == ConfigLoadKind.IoErrorKept;
            _configLoadError = load.Error;
            EnsureConfigLists();
            _progressStore = new InspectionProgressStore(ResolveProgressPath(_store));
            _session = new InspectionSessionController(
                _clock, _stateMachine, _progressStore, _config, _folderNavigator);

            BuildStaticLayout();
            BuildTrayIcon();
            RebuildClipboardGuard();

            ApplyScaleToStaticLayout();
            RefreshConfigState();
            ClientSize = new Size(PanelWidthNow, ComputeCurrentHeight());
            RestorePosition();
            LayoutSections();

            if (_configWriteProtected)
                ShowConfigLoadErrorFeedback();

            _dayRolloverTimer = new Timer { Interval = 30000 };
            _dayRolloverTimer.Tick += OnDayRolloverTick;
            _dayRolloverTimer.Start();

            Shown += OnFirstShown;
            FormClosing += OnFormClosing;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (_config == null)
                return;
            ApplyScaleToStaticLayout();
            ClientSize = new Size(PanelWidthNow, ComputeCurrentHeight());
            LayoutSections();
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            if (_config == null)
                return;
            int scrollPosition = _scrollHost != null ? _scrollHost.VerticalScroll.Value : 0;
            ApplyScaleToStaticLayout();
            RebuildQuickLinks();
            RebuildPlanCard();
            RebuildDeviceButtons();
            RebuildCredentialControls();
            RefreshStateVisuals();
            ClientSize = new Size(PanelWidthNow, ComputeCurrentHeight());
            LayoutSections();
            if (_scrollHost != null)
            {
                int max = Math.Max(0,
                    _scrollHost.VerticalScroll.Maximum - _scrollHost.ClientSize.Height);
                _scrollHost.VerticalScroll.Value = Math.Min(scrollPosition, max);
                _scrollHost.PerformLayout();
            }
            ClampToWorkingArea();
        }

        // ---------- 静态骨架 ----------

        private void BuildStaticLayout()
        {
            Font font15Bold = Font15Bold;
            Font font13 = Font13;
            Font font13Bold = Font13Bold;
            Font font12 = Font12;
            Font font12Bold = Font12Bold;
            Font font11 = Font11;

            // 标题栏：第 1 行应用标题 + 右上三图标按钮；第 2 行独立完整日期
            _titleBarPanel = new Panel
            {
                BackColor = PanelBackColor,
                Size = new Size(PanelLayout.SchemeBPanelWidth, PanelLayout.SchemeBHeaderHeight)
            };
            _titleBarPanel.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (var pen = new Pen(BorderSoftColor, 1f))
                {
                    e.Graphics.DrawLine(pen, 1, _titleBarPanel.Height - 1, _titleBarPanel.Width - 2, _titleBarPanel.Height - 1);
                }
            };
            var titleLabel = _titleLabel = new Label
            {
                Text = "巡检助手",
                Font = font15Bold,
                ForeColor = PrimaryTextColor,
                AutoSize = true,
                Location = new Point(SideMargin, 10)
            };
            var dateLabel = _dateLabel = new Label
            {
                Text = FormatFullDate(_clock.Now),
                Font = font12,
                ForeColor = SecondaryTextColor,
                AutoSize = true,
                Location = new Point(SideMargin, 34)
            };
            _titleToolTip = new ToolTip();
            _deviceToolTip = new ToolTip();
            var settingsButton = _settingsButton = new Button
            {
                Text = "⚙",
                Font = font13,
                ForeColor = SecondaryTextColor,
                FlatStyle = FlatStyle.Flat,
                BackColor = PanelBackColor,
                Size = new Size(PanelLayout.TitleBarButtonSize, PanelLayout.TitleBarButtonSize),
                Location = new Point(PanelLayout.TitleBarButtonX(PanelLayout.SchemeBPanelWidth, 2, 1.0), 8)
            };
            settingsButton.FlatAppearance.BorderSize = 0;
            settingsButton.Click += OnSettingsClicked;
            settingsButton.AccessibleName = "打开设置";
            _titleToolTip.SetToolTip(settingsButton, "设置");
            var minimizeButton = _minimizeButton = new Button
            {
                Text = "—",
                Font = font13,
                ForeColor = SecondaryTextColor,
                FlatStyle = FlatStyle.Flat,
                BackColor = PanelBackColor,
                Size = new Size(PanelLayout.TitleBarButtonSize, PanelLayout.TitleBarButtonSize),
                Location = new Point(PanelLayout.TitleBarButtonX(PanelLayout.SchemeBPanelWidth, 1, 1.0), 8)
            };
            minimizeButton.FlatAppearance.BorderSize = 0;
            minimizeButton.Click += OnMinimizeClicked;
            minimizeButton.AccessibleName = "最小化到托盘";
            _titleToolTip.SetToolTip(minimizeButton, "最小化");
            var closeButton = _closeButton = new Button
            {
                Text = "✕",
                Font = font13,
                ForeColor = SecondaryTextColor,
                FlatStyle = FlatStyle.Flat,
                BackColor = PanelBackColor,
                Size = new Size(PanelLayout.TitleBarButtonSize, PanelLayout.TitleBarButtonSize),
                Location = new Point(PanelLayout.TitleBarButtonX(PanelLayout.SchemeBPanelWidth, 0, 1.0), 8)
            };
            closeButton.FlatAppearance.BorderSize = 0;
            closeButton.Click += delegate { Close(); };
            closeButton.AccessibleName = "退出巡检助手";
            _titleToolTip.SetToolTip(closeButton, "关闭");

            _titleBarPanel.Controls.Add(titleLabel);
            _titleBarPanel.Controls.Add(dateLabel);
            _titleBarPanel.Controls.Add(settingsButton);
            _titleBarPanel.Controls.Add(minimizeButton);
            _titleBarPanel.Controls.Add(closeButton);
            AttachDragHandlers(_titleBarPanel);
            AttachDragHandlers(titleLabel);
            AttachDragHandlers(dateLabel);
            Controls.Add(_titleBarPanel);

            _scrollHost = new Panel
            {
                BackColor = PanelBackColor,
                AutoScroll = true,
                Name = "ScrollableSections"
            };
            Controls.Add(_scrollHost);

            // 1. 快捷链接区
            _quickLinkHeaderPanel = new Panel { BackColor = PanelBackColor, Size = new Size(PanelLayout.SchemeBPanelWidth, 24) };
            _quickLinkHeaderLabel = new Label
            {
                Text = "快捷链接",
                Font = font12Bold,
                ForeColor = SecondaryTextColor,
                AutoSize = true,
                Location = new Point(SideMargin, 2)
            };
            _quickLinkSubLabel = new Label
            {
                Text = "在浏览器打开",
                Font = font11,
                ForeColor = WeakTextColor,
                AutoSize = true,
                Location = new Point(PanelLayout.SchemeBPanelWidth - 82, 4)
            };
            _quickLinkHeaderPanel.Controls.Add(_quickLinkHeaderLabel);
            _quickLinkHeaderPanel.Controls.Add(_quickLinkSubLabel);
            _scrollHost.Controls.Add(_quickLinkHeaderPanel);

            _quickLinkZonePanel = new Panel { BackColor = PanelBackColor, Width = PanelLayout.SchemeBPanelWidth };
            _scrollHost.Controls.Add(_quickLinkZonePanel);

            // 2. 今日巡检计划卡
            _planCardPanel = new Panel
            {
                BackColor = AccentSoftColor,
                Width = PanelLayout.SchemeBPanelWidth - SideMargin * 2
            };
            _planCardPanel.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (var pen = new Pen(AccentBorderColor, 1f))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, _planCardPanel.Width - 1, _planCardPanel.Height - 1);
                }
            };
            _planTitleLabel = new Label
            {
                Text = "今日巡检计划",
                Font = font13Bold,
                ForeColor = AccentColor,
                AutoSize = true
            };
            _planToggleButton = new CheckBox
            {
                Appearance = Appearance.Button,
                FlatStyle = FlatStyle.Flat,
                Text = "计划",
                Font = font11,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand,
                AccessibleName = "开启或关闭今日巡检计划"
            };
            _planToggleButton.FlatAppearance.BorderSize = 1;
            _planToggleButton.Click += OnPlanToggleClicked;
            _planProgressLabel = new Label
            {
                Text = "今日进度",
                Font = font12,
                ForeColor = SecondaryTextColor,
                AutoSize = true
            };
            _planCountLabel = new Label
            {
                Text = "已点过 0 / 0",
                Font = font12Bold,
                ForeColor = AccentColor,
                AutoSize = true
            };
            _planTrackPanel = new Panel
            {
                BackColor = Color.FromArgb(0xD6, 0xE2, 0xEE)
            };
            _planFillPanel = new Panel
            {
                BackColor = AccentColor,
                Location = new Point(0, 0)
            };
            _planTrackPanel.Controls.Add(_planFillPanel);

            _planActionPanel = new Panel { BackColor = AccentSoftColor };
            _showVisitedButton = new Button
            {
                Text = "查看已点过",
                Font = font11,
                FlatStyle = FlatStyle.Flat,
                BackColor = ButtonBackColor,
                ForeColor = SecondaryTextColor,
                Cursor = Cursors.Hand,
                AccessibleName = "切换显示已点过设备"
            };
            _showVisitedButton.FlatAppearance.BorderColor = AccentBorderColor;
            _showVisitedButton.FlatAppearance.BorderSize = 1;
            _showVisitedButton.Click += OnToggleShowVisitedClicked;

            _resetRoundButton = new Button
            {
                Text = "重置本轮",
                Font = font11,
                FlatStyle = FlatStyle.Flat,
                BackColor = ButtonBackColor,
                ForeColor = ErrorColor,
                Cursor = Cursors.Hand,
                AccessibleName = "重置本轮巡检进度"
            };
            _resetRoundButton.FlatAppearance.BorderColor = Color.FromArgb(0xE6, 0xC2, 0xC2);
            _resetRoundButton.FlatAppearance.BorderSize = 1;
            _resetRoundButton.Click += OnResetRoundClicked;

            _planActionPanel.Controls.Add(_showVisitedButton);
            _planActionPanel.Controls.Add(_resetRoundButton);

            _planDisabledHintLabel = new Label
            {
                Text = "开启后记录今日已点项目，不改变截图方式。",
                Font = font11,
                ForeColor = SecondaryTextColor,
                AutoSize = false
            };

            _planCardPanel.Controls.Add(_planTitleLabel);
            _planCardPanel.Controls.Add(_planToggleButton);
            _planCardPanel.Controls.Add(_planProgressLabel);
            _planCardPanel.Controls.Add(_planCountLabel);
            _planCardPanel.Controls.Add(_planTrackPanel);
            _planCardPanel.Controls.Add(_planActionPanel);
            _planCardPanel.Controls.Add(_planDisabledHintLabel);
            _scrollHost.Controls.Add(_planCardPanel);

            // 3. 凭据区（整行点击折叠，两列快捷复制）
            _credentialHeaderPanel = new Panel { BackColor = PanelBackColor, Size = new Size(PanelLayout.SchemeBPanelWidth, 24) };
            var credentialHeaderLabel = _credentialHeaderLabel = new Label
            {
                Text = "凭据",
                Font = font12Bold,
                ForeColor = SecondaryTextColor,
                AutoSize = true,
                Location = new Point(SideMargin, 2)
            };
            _credentialArrowLabel = new Label
            {
                Text = "˅",
                Font = font13,
                ForeColor = WeakTextColor,
                AutoSize = true
            };
            _credentialHeaderPanel.Controls.Add(credentialHeaderLabel);
            _credentialHeaderPanel.Controls.Add(_credentialArrowLabel);
            _credentialHeaderPanel.Click += OnCredentialHeaderClicked;
            credentialHeaderLabel.Click += OnCredentialHeaderClicked;
            _credentialArrowLabel.Click += OnCredentialHeaderClicked;
            _scrollHost.Controls.Add(_credentialHeaderPanel);

            _credentialZonePanel = new Panel { BackColor = PanelBackColor, Width = PanelLayout.SchemeBPanelWidth };
            _scrollHost.Controls.Add(_credentialZonePanel);

            // 4. 截图设备区（整行点击折叠）
            var deviceHeaderPanel = _deviceHeaderPanel = new Panel { BackColor = PanelBackColor, Size = new Size(PanelLayout.SchemeBPanelWidth, 24) };
            _deviceHeaderLabel = new Label
            {
                Text = "截图设备",
                Font = font12Bold,
                ForeColor = SecondaryTextColor,
                AutoSize = true,
                Location = new Point(SideMargin, 2)
            };
            _deviceSubLabel = new Label
            {
                Text = "点击设备，再选窗口",
                Font = font11,
                ForeColor = WeakTextColor,
                AutoSize = true
            };
            _deviceArrowLabel = new Label
            {
                Text = "˅",
                Font = font13,
                ForeColor = WeakTextColor,
                AutoSize = true
            };
            deviceHeaderPanel.Controls.Add(_deviceHeaderLabel);
            deviceHeaderPanel.Controls.Add(_deviceSubLabel);
            deviceHeaderPanel.Controls.Add(_deviceArrowLabel);
            deviceHeaderPanel.Click += OnDeviceHeaderClicked;
            _deviceHeaderLabel.Click += OnDeviceHeaderClicked;
            _deviceSubLabel.Click += OnDeviceHeaderClicked;
            _deviceArrowLabel.Click += OnDeviceHeaderClicked;
            deviceHeaderPanel.Name = "DeviceHeaderPanel";
            _scrollHost.Controls.Add(deviceHeaderPanel);

            _deviceZonePanel = new Panel { BackColor = PanelBackColor, Width = PanelLayout.SchemeBPanelWidth };
            _scrollHost.Controls.Add(_deviceZonePanel);

            // 底部固定区：反馈行 + 主操作「打开巡检文件夹」按钮 + 路径行
            var bottomPanel = _bottomPanel = new Panel
            {
                BackColor = SurfaceColor,
                Size = new Size(PanelLayout.SchemeBPanelWidth, PanelLayout.SchemeBFooterHeight)
            };
            bottomPanel.Name = "BottomPanel";
            bottomPanel.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (var pen = new Pen(BorderSoftColor, 1f))
                {
                    e.Graphics.DrawLine(pen, 1, 0, bottomPanel.Width - 2, 0);
                }
            };
            _feedbackLabel = new Label
            {
                Font = font12,
                ForeColor = SecondaryTextColor,
                BackColor = SurfaceColor,
                AutoSize = false,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
            _feedbackLabel.Click += OnFeedbackLabelClicked;
            _openFolderButton = new Button
            {
                Text = "打开巡检文件夹 ↗",
                Font = font13Bold,
                FlatStyle = FlatStyle.Flat,
                BackColor = AccentColor,
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                AccessibleName = "打开巡检文件夹"
            };
            _openFolderButton.FlatAppearance.BorderSize = 0;
            _openFolderButton.FlatAppearance.MouseOverBackColor = UiTheme.AccentHoverColor;
            _openFolderButton.Click += OnRootLabelClicked;

            _rootToolTip = new ToolTip();
            _rootLabel = new Label
            {
                Font = font11,
                ForeColor = WeakTextColor,
                BackColor = SurfaceColor,
                AutoSize = false,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Cursor = Cursors.Hand
            };
            _rootLabel.Click += OnRootLabelClicked;
            bottomPanel.Controls.Add(_feedbackLabel);
            bottomPanel.Controls.Add(_openFolderButton);
            bottomPanel.Controls.Add(_rootLabel);
            Controls.Add(bottomPanel);

            _feedbackTimer = new Timer { Interval = 2000 };
            _feedbackTimer.Tick += delegate
            {
                _feedbackTimer.Stop();
                if (_feedbackPersistent)
                    return;
                if (_deviceListError != null)
                {
                    ShowDeviceListErrorFeedback();
                }
                else
                {
                    _feedbackLabel.Text = string.Empty;
                }
            };
        }

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

        private void ApplyScaleToStaticLayout()
        {
            int w = PanelWidthNow;
            int margin = M(SideMargin);
            int fullContent = w - margin * 2;

            _titleBarPanel.Size = new Size(w, M(PanelLayout.SchemeBHeaderHeight));
            _titleLabel.Font = Font15Bold;
            _titleLabel.Location = new Point(M(14), M(10));
            _dateLabel.Font = Font12;
            _dateLabel.Text = FormatFullDate(_clock.Now);
            _dateLabel.Location = new Point(M(14), M(34));
            ApplySettingsButtonVisual();
            int btnSize = M(PanelLayout.TitleBarButtonSize);
            int btnTop = M(8);
            _settingsButton.Size = new Size(btnSize, btnSize);
            _settingsButton.Location = new Point(PanelLayout.TitleBarButtonX(w, 2, ScaleFactor), btnTop);
            _minimizeButton.Font = Font13;
            _minimizeButton.Size = new Size(btnSize, btnSize);
            _minimizeButton.Location = new Point(PanelLayout.TitleBarButtonX(w, 1, ScaleFactor), btnTop);
            _closeButton.Font = Font13;
            _closeButton.Size = new Size(btnSize, btnSize);
            _closeButton.Location = new Point(PanelLayout.TitleBarButtonX(w, 0, ScaleFactor), btnTop);

            _quickLinkHeaderLabel.Font = Font12Bold;
            _quickLinkSubLabel.Font = Font11;
            _planTitleLabel.Font = Font13Bold;
            _planToggleButton.Font = Font11;
            _planProgressLabel.Font = Font12;
            _planCountLabel.Font = Font12Bold;
            _showVisitedButton.Font = Font11;
            _resetRoundButton.Font = Font11;
            _planDisabledHintLabel.Font = Font11;

            _credentialHeaderLabel.Font = Font12Bold;
            _credentialArrowLabel.Font = Font13;
            _deviceHeaderLabel.Font = Font12Bold;
            _deviceSubLabel.Font = Font11;
            _deviceArrowLabel.Font = Font13;

            _bottomPanel.Size = new Size(w, M(PanelLayout.SchemeBFooterHeight));
            _feedbackLabel.Font = Font12;
            _feedbackLabel.Size = new Size(fullContent, M(24));
            _feedbackLabel.Location = new Point(margin, M(8));
            _openFolderButton.Font = Font13Bold;
            _openFolderButton.Size = new Size(fullContent, M(36));
            _openFolderButton.Location = new Point(margin, M(36));
            _rootLabel.Font = Font11;
            _rootLabel.Size = new Size(fullContent, M(20));
            _rootLabel.Location = new Point(margin, M(78));
        }

        private int ComputePlanCardHeight()
        {
            if (_session != null && _session.PlanModeEnabled)
                return M(104);
            return M(66);
        }

        private int ComputeQuickLinksHeight()
        {
            int enabledCount = CountEnabledQuickLinks();
            if (enabledCount <= 0)
                return M(34);
            int rows = (enabledCount + 1) / 2;
            return rows * M(PanelLayout.SchemeBQuickLinkHeight)
                + (rows - 1) * M(PanelLayout.SchemeBQuickLinkGap);
        }

        private int ComputeCredentialsZoneHeight()
        {
            if (_config != null && _config.IsCredentialCollapsed)
                return 0;
            int count = _config != null && _config.Credentials != null ? _config.Credentials.Count : 0;
            if (count <= 0)
                return M(34);
            int rows = (count + 1) / 2;
            return rows * M(PanelLayout.SchemeBCredentialHeight)
                + (rows - 1) * M(PanelLayout.SchemeBCredentialGap);
        }

        private int ComputeDevicesZoneHeight()
        {
            if (_config != null && _config.IsDeviceCollapsed)
                return 0;
            int count = _deviceDisplay != null ? _deviceDisplay.Items.Count : 0;
            if (count <= 0)
            {
                return _deviceDisplay != null && _deviceDisplay.EmptyKind == DeviceListEmptyKind.NoDevicesConfigured
                    ? M(58)
                    : M(46);
            }
            return count * M(PanelLayout.SchemeBDeviceRowHeight)
                + (count - 1) * M(PanelLayout.SchemeBDeviceRowGap);
        }

        private int ComputeScrollContentHeight()
        {
            int headerH = M(24);
            int quickH = ComputeQuickLinksHeight();
            int planH = ComputePlanCardHeight();
            int credH = ComputeCredentialsZoneHeight();
            int devH = ComputeDevicesZoneHeight();

            int y = M(10);
            y += headerH + M(4) + quickH + M(12);
            y += planH + M(12);
            y += headerH + (credH > 0 ? M(4) + credH : 0) + M(12);
            y += headerH + (devH > 0 ? M(4) + devH : 0) + M(12);
            return y;
        }

        private int ComputeCurrentHeight()
        {
            int natural = M(PanelLayout.SchemeBHeaderHeight)
                + ComputeScrollContentHeight()
                + M(PanelLayout.SchemeBFooterHeight);
            int workingHeight = Screen.FromControl(this).WorkingArea.Height;
            return PanelLayout.ClampHeight(natural, workingHeight, M(8));
        }

        /// <summary>标题与底栏固定；中间内容放入滚动宿主并随可用宽度自动对齐（无横向滚动条）。</summary>
        private void LayoutSections()
        {
            int titleHeight = M(PanelLayout.SchemeBHeaderHeight);
            int bottomHeight = M(PanelLayout.SchemeBFooterHeight);
            int scrollHeight = Math.Max(0, ClientSize.Height - titleHeight - bottomHeight);
            int totalContentHeight = ComputeScrollContentHeight();
            bool needsVScroll = totalContentHeight > scrollHeight;
            int w = PanelWidthNow;
            int margin = M(SideMargin);
            int hostWidth = w - (needsVScroll ? SystemInformation.VerticalScrollBarWidth : 0);
            int innerWidth = Math.Max(M(180), hostWidth - margin * 2);

            _titleBarPanel.Location = new Point(0, 0);
            int y = M(10);
            int headerH = M(24);

            // 1. 快捷链接
            _quickLinkHeaderPanel.Size = new Size(hostWidth, headerH);
            _quickLinkHeaderPanel.Location = new Point(0, y);
            _quickLinkHeaderLabel.Location = new Point(margin, M(3));
            _quickLinkSubLabel.Location = new Point(Math.Max(margin, hostWidth - margin - _quickLinkSubLabel.Width), M(4));
            y += headerH + M(4);

            int quickH = ComputeQuickLinksHeight();
            _quickLinkZonePanel.Size = new Size(hostWidth, quickH);
            _quickLinkZonePanel.Location = new Point(0, y);
            LayoutTwoColumnControls(_quickLinkControls, _quickLinkZonePanel, innerWidth,
                M(PanelLayout.SchemeBQuickLinkHeight), M(PanelLayout.SchemeBQuickLinkGap));
            y += quickH + M(12);

            // 2. 今日巡检计划卡
            int planH = ComputePlanCardHeight();
            _planCardPanel.Size = new Size(innerWidth, planH);
            _planCardPanel.Location = new Point(margin, y);
            LayoutPlanCardInner(innerWidth);
            y += planH + M(12);

            // 3. 凭据
            _credentialHeaderPanel.Size = new Size(hostWidth, headerH);
            _credentialHeaderPanel.Location = new Point(0, y);
            _credentialHeaderLabel.Location = new Point(margin, M(3));
            _credentialArrowLabel.Location = new Point(hostWidth - margin - M(14), M(2));
            y += headerH;

            int credH = ComputeCredentialsZoneHeight();
            if (credH > 0) y += M(4);
            _credentialZonePanel.Size = new Size(hostWidth, credH);
            _credentialZonePanel.Location = new Point(0, y);
            LayoutTwoColumnControls(_credentialControls, _credentialZonePanel, innerWidth,
                M(PanelLayout.SchemeBCredentialHeight), M(PanelLayout.SchemeBCredentialGap));
            y += credH + M(12);

            // 4. 截图设备
            _deviceHeaderPanel.Size = new Size(hostWidth, headerH);
            _deviceHeaderPanel.Location = new Point(0, y);
            _deviceHeaderLabel.Location = new Point(margin, M(3));
            _deviceArrowLabel.Location = new Point(hostWidth - margin - M(14), M(2));
            _deviceSubLabel.Location = new Point(
                Math.Max(_deviceHeaderLabel.Right + M(8), _deviceArrowLabel.Left - _deviceSubLabel.Width - M(6)),
                M(4));
            y += headerH;

            int devH = ComputeDevicesZoneHeight();
            if (devH > 0) y += M(4);
            _deviceZonePanel.Size = new Size(hostWidth, devH);
            _deviceZonePanel.Location = new Point(0, y);
            LayoutDeviceZoneInner(innerWidth);
            y += devH + M(12);

            _scrollHost.Location = new Point(0, titleHeight);
            _scrollHost.Size = new Size(w, scrollHeight);
            _scrollHost.AutoScrollMinSize = new Size(0, y);
            _bottomPanel.Location = new Point(0, ClientSize.Height - bottomHeight);
        }

        private void LayoutPlanCardInner(int cardWidth)
        {
            int padX = M(12);
            int padY = M(10);
            int innerW = Math.Max(M(120), cardWidth - padX * 2);

            _planTitleLabel.Location = new Point(padX, padY + M(2));
            int toggleW = M(56);
            int toggleH = M(22);
            _planToggleButton.Size = new Size(toggleW, toggleH);
            _planToggleButton.Location = new Point(cardWidth - padX - toggleW, padY);

            bool enabled = _session != null && _session.PlanModeEnabled;
            _planProgressLabel.Visible = enabled;
            _planCountLabel.Visible = enabled;
            _planTrackPanel.Visible = enabled;
            _planActionPanel.Visible = enabled;
            _planDisabledHintLabel.Visible = !enabled;

            if (!enabled)
            {
                _planDisabledHintLabel.Size = new Size(innerW, M(22));
                _planDisabledHintLabel.Location = new Point(padX, padY + M(28));
                return;
            }

            int row2Y = padY + M(28);
            _planProgressLabel.Location = new Point(padX, row2Y);
            _planCountLabel.Location = new Point(Math.Max(padX, cardWidth - padX - _planCountLabel.Width), row2Y);

            int barY = row2Y + M(20);
            _planTrackPanel.Size = new Size(innerW, M(4));
            _planTrackPanel.Location = new Point(padX, barY);
            int total = _session.Plan.TotalCount;
            int visited = _session.Plan.VisitedCount;
            int fillW = total > 0 ? (int)Math.Round(innerW * ((double)visited / total)) : 0;
            if (visited > 0 && fillW < M(4)) fillW = M(4);
            _planFillPanel.Size = new Size(Math.Min(innerW, fillW), M(4));

            int actionY = barY + M(10);
            _planActionPanel.Size = new Size(innerW, M(26));
            _planActionPanel.Location = new Point(padX, actionY);
            int resetW = M(72);
            int showW = M(88);
            _resetRoundButton.Size = new Size(resetW, M(24));
            _resetRoundButton.Location = new Point(innerW - resetW, 0);
            _showVisitedButton.Visible = _session.HideVisitedDevices;
            if (_showVisitedButton.Visible)
            {
                _showVisitedButton.Size = new Size(showW, M(24));
                _showVisitedButton.Location = new Point(Math.Max(0, _resetRoundButton.Left - showW - M(6)), 0);
            }
        }

        private void LayoutTwoColumnControls(List<Control> items, Panel host, int innerWidth, int rowHeight, int gap)
        {
            if (items.Count == 0)
                return;
            int margin = M(SideMargin);
            if (items.Count == 1 && !(items[0] is Button))
            {
                items[0].Size = new Size(innerWidth, rowHeight);
                items[0].Location = new Point(margin, 0);
                return;
            }
            int colW = Math.Max(M(60), (innerWidth - gap) / 2);
            int rightColW = Math.Max(M(60), innerWidth - gap - colW);
            for (int i = 0; i < items.Count; i++)
            {
                int col = i % 2;
                int row = i / 2;
                int x = col == 0 ? margin : (margin + colW + gap);
                int w = col == 0 ? colW : rightColW;
                int y = row * (rowHeight + gap);
                items[i].Size = new Size(w, rowHeight);
                items[i].Location = new Point(x, y);
            }
        }

        private void LayoutDeviceZoneInner(int innerWidth)
        {
            int margin = M(SideMargin);
            if (_deviceHint != null)
            {
                _deviceHint.Size = new Size(innerWidth, _deviceHint.Height);
                _deviceHint.Location = new Point(margin, 0);
                var innerBtn = _deviceHint.Tag as Button;
                if (innerBtn != null)
                {
                    innerBtn.Location = new Point(Math.Max(M(8), innerWidth - innerBtn.Width - M(10)),
                        (_deviceHint.Height - innerBtn.Height) / 2);
                }
                return;
            }

            int rowH = M(PanelLayout.SchemeBDeviceRowHeight);
            int gap = M(PanelLayout.SchemeBDeviceRowGap);
            for (int i = 0; i < _deviceRows.Count; i++)
            {
                DeviceRowWidget row = _deviceRows[i];
                row.Panel.Size = new Size(innerWidth, rowH);
                row.Panel.Location = new Point(margin, i * (rowH + gap));
                LayoutSingleDeviceRow(row, innerWidth, rowH);
            }
        }

        private void LayoutSingleDeviceRow(DeviceRowWidget row, int width, int height)
        {
            int leftX = M(34);
            int rightPad = M(10);
            int rightEdge = width - rightPad;

            if (row.UnmarkButton != null && row.UnmarkButton.Visible)
            {
                int btnW = M(62);
                int btnH = M(24);
                row.UnmarkButton.Size = new Size(btnW, btnH);
                row.UnmarkButton.Location = new Point(rightEdge - btnW, (height - btnH) / 2);
                rightEdge = row.UnmarkButton.Left - M(6);
            }

            int statusW = M(66);
            row.StatusLabel.Size = new Size(statusW, M(20));
            row.StatusLabel.Location = new Point(rightEdge - statusW, (height - M(20)) / 2);

            int nameW = Math.Max(M(40), row.StatusLabel.Left - leftX - M(6));
            row.NameLabel.Size = new Size(nameW, M(22));
            row.NameLabel.Location = new Point(leftX, (height - M(22)) / 2);
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
                Point location;
                var candidate = new Rectangle(_config.WindowLeft.Value,
                    _config.WindowTop.Value, size.Width, size.Height);
                if (PanelLayout.TryChooseVisibleWindowLocation(
                    WorkingAreas(), candidate, out location))
                {
                    Location = location;
                    return;
                }
            }
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - size.Width, wa.Bottom - size.Height);
        }

        private static List<Rectangle> WorkingAreas()
        {
            var areas = new List<Rectangle>();
            foreach (Screen screen in Screen.AllScreens)
                areas.Add(screen.WorkingArea);
            return areas;
        }

        private void ApplyConfiguredWindowPositionIfVisible()
        {
            if (!_config.WindowLeft.HasValue || !_config.WindowTop.HasValue)
                return;
            Point location;
            var candidate = new Rectangle(_config.WindowLeft.Value,
                _config.WindowTop.Value, ClientSize.Width, ClientSize.Height);
            if (PanelLayout.TryChooseVisibleWindowLocation(
                WorkingAreas(), candidate, out location))
            {
                Location = location;
            }
        }

        // ---------- 配置加载/刷新同一入口（含 Minor-5 名单校验接线） ----------

        /// <summary>
        /// 设置保存后可调用的刷新入口：重读配置并走与加载相同的校验/重建流程。
        /// </summary>
        public void ReloadConfig()
        {
            RetryConfigLoad();
        }

        /// <summary>
        /// 读取失败后的显式重试。成功才重建运行态并解除写入保护；
        /// 失败继续保留原文件，绝不让旧默认草稿覆盖。
        /// </summary>
        private bool RetryConfigLoad()
        {
            LoadResult load = _store.Load();
            if (load.Kind == ConfigLoadKind.IoErrorKept)
            {
                _configWriteProtected = true;
                _configLoadError = load.Error;
                ShowConfigLoadErrorFeedback();
                return false;
            }

            AppConfig next = load.Config ?? AppConfig.CreateDefault();
            bool autoClearChanged = ConfigStore.ShouldRebuildClipboardGuard(_config, next);
            _configWriteProtected = false;
            _configLoadError = null;
            _configRevision = load.Revision ?? string.Empty;
            _config = next;
            EnsureConfigLists();
            string progressErr;
            if (_session != null)
                _session.ReloadConfig(_config, out progressErr);
            if (autoClearChanged)
                RebuildClipboardGuard();
            ApplyScaleToStaticLayout();
            RefreshConfigState();
            ApplyHeightAnchored();
            if (load.Kind == ConfigLoadKind.CorruptBackedUp)
            {
                ShowPersistentFeedback("配置损坏已备份并恢复默认："
                    + (load.BackupPath ?? "备份路径未知"), ErrorColor);
            }
            else
            {
                ShowSuccessFeedback("配置已重新读取");
            }
            return true;
        }

        private void EnsureConfigLists()
        {
            if (_config.Devices == null) _config.Devices = new List<string>();
            if (_config.Credentials == null) _config.Credentials = new List<CredentialEntry>();
            if (_config.QuickLinks == null) _config.QuickLinks = new List<QuickLinkEntry>();
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

            RebuildQuickLinks();
            RebuildPlanCard();
            RebuildDeviceButtons();
            RebuildCredentialControls();
            UpdateCredentialHeader();
            UpdateDeviceHeader();
            UpdateRootLabel();
            RefreshStateVisuals();

            if (_configWriteProtected)
            {
                ShowConfigLoadErrorFeedback();
            }
            else if (_deviceListError != null)
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
            get
            {
                return !_configWriteProtected
                    && _deviceListError == null
                    && !string.IsNullOrEmpty(_config.RootPath);
            }
        }

        private int CountEnabledQuickLinks()
        {
            if (_config == null || _config.QuickLinks == null)
                return 0;
            int count = 0;
            for (int i = 0; i < _config.QuickLinks.Count; i++)
            {
                QuickLinkEntry entry = _config.QuickLinks[i];
                if (entry != null && entry.Enabled && !string.IsNullOrWhiteSpace(entry.Title))
                    count++;
            }
            return count;
        }

        private void RebuildQuickLinks()
        {
            foreach (Control old in _quickLinkControls)
            {
                _quickLinkZonePanel.Controls.Remove(old);
                old.Dispose();
            }
            _quickLinkControls.Clear();

            int enabled = CountEnabledQuickLinks();
            _quickLinkSubLabel.Visible = enabled > 0;
            if (enabled <= 0)
            {
                Control hint = CreateHintLabel("尚未启用快捷链接，点击去设置添加", WeakTextColor, M(PanelLayout.SchemeBQuickLinkHeight));
                hint.Cursor = Cursors.Hand;
                hint.Click += OnSettingsClicked;
                _quickLinkControls.Add(hint);
                _quickLinkZonePanel.Controls.Add(hint);
                return;
            }

            for (int i = 0; i < _config.QuickLinks.Count; i++)
            {
                QuickLinkEntry entry = _config.QuickLinks[i];
                if (entry == null || !entry.Enabled || string.IsNullOrWhiteSpace(entry.Title))
                    continue;

                var button = new Button
                {
                    Text = entry.Title,
                    Tag = entry,
                    Font = Font12,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = ButtonBackColor,
                    ForeColor = PrimaryTextColor,
                    TextAlign = ContentAlignment.MiddleLeft,
                    AutoEllipsis = true,
                    Padding = new Padding(M(8), 0, M(18), 0),
                    Cursor = Cursors.Hand,
                    AccessibleName = "快捷链接：" + entry.Title
                };
                button.FlatAppearance.BorderColor = BorderColor;
                button.FlatAppearance.BorderSize = 1;
                button.FlatAppearance.MouseOverBackColor = AccentSoftColor;
                button.Paint += delegate(object sender, PaintEventArgs e)
                {
                    var btn = (Button)sender;
                    using (var arrowFont = new Font(Font11.FontFamily, Font11.Size, FontStyle.Regular, GraphicsUnit.Pixel))
                    {
                        TextRenderer.DrawText(
                            e.Graphics,
                            "↗",
                            arrowFont,
                            new Rectangle(btn.Width - M(18), 0, M(14), btn.Height),
                            WeakTextColor,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    }
                };
                button.Click += OnQuickLinkButtonClicked;
                _quickLinkControls.Add(button);
                _quickLinkZonePanel.Controls.Add(button);
                if (!string.IsNullOrEmpty(entry.Url))
                    _deviceToolTip.SetToolTip(button, entry.Url);
            }
        }

        private void RebuildPlanCard()
        {
            if (_session == null || _planToggleButton == null)
                return;

            bool enabled = _session.PlanModeEnabled;
            _planToggleButton.Checked = enabled;
            _planToggleButton.Text = enabled ? "已开启" : "已关闭";
            _planToggleButton.BackColor = enabled ? AccentColor : ButtonBackColor;
            _planToggleButton.ForeColor = enabled ? Color.White : SecondaryTextColor;
            _planToggleButton.FlatAppearance.BorderColor = enabled ? AccentColor : BorderColor;

            int visited = _session.Plan.VisitedCount;
            int total = _session.Plan.TotalCount;
            bool complete = enabled && total > 0 && visited >= total;

            _planCountLabel.Text = "已点过 " + visited + " / " + total;
            _planCountLabel.ForeColor = complete ? SuccessColor : AccentColor;
            _planFillPanel.BackColor = complete ? SuccessColor : AccentColor;

            _showVisitedButton.Text = _session.ShowVisitedOverride ? "只看未点过" : "查看已点过";
            _showVisitedButton.Visible = enabled && _session.HideVisitedDevices;
            _resetRoundButton.Enabled = enabled && (visited > 0 || HasSavedPaths());
        }

        private bool HasSavedPaths()
        {
            if (_session == null) return false;
            foreach (string path in _session.Plan.SavedPaths)
            {
                if (!string.IsNullOrEmpty(path))
                    return true;
            }
            return false;
        }

        private void RebuildDeviceButtons()
        {
            foreach (DeviceRowWidget old in _deviceRows)
            {
                _deviceZonePanel.Controls.Remove(old.Panel);
                old.Panel.Dispose();
            }
            _deviceRows.Clear();
            if (_deviceHint != null)
            {
                _deviceZonePanel.Controls.Remove(_deviceHint);
                _deviceHint.Dispose();
                _deviceHint = null;
            }

            _deviceDisplay = _session != null
                ? _session.BuildDeviceDisplay()
                : new DeviceDisplaySnapshot
                {
                    Items = new List<DeviceDisplayItem>(),
                    EmptyKind = DeviceListEmptyKind.None
                };

            // 截图区折叠（v1.0.3）：区高为 0，不建行控件；展开时照常重建。
            if (_config.IsDeviceCollapsed)
                return;

            if (_deviceDisplay.Items.Count == 0)
            {
                if (_deviceDisplay.EmptyKind == DeviceListEmptyKind.AllVisitedHidden)
                {
                    _deviceHint = CreateAllVisitedEmptyCard();
                }
                else
                {
                    _deviceHint = CreateNoDevicesEmptyCard();
                }
                _deviceZonePanel.Controls.Add(_deviceHint);
                return;
            }

            for (int i = 0; i < _deviceDisplay.Items.Count; i++)
            {
                DeviceDisplayItem item = _deviceDisplay.Items[i];
                DeviceRowWidget widget = CreateDeviceRowWidget(item);
                _deviceRows.Add(widget);
                _deviceZonePanel.Controls.Add(widget.Panel);
            }
        }

        private Control CreateNoDevicesEmptyCard()
        {
            var card = new Panel
            {
                BackColor = SurfaceColor,
                Size = new Size(ContentWidthNow, M(58))
            };
            card.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (var pen = new Pen(BorderColor))
                {
                    pen.DashStyle = DashStyle.Dash;
                    e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
                }
            };
            var text = new Label
            {
                Text = "尚未配置截图设备",
                Font = Font12,
                ForeColor = SecondaryTextColor,
                AutoSize = true,
                Location = new Point(M(12), M(19))
            };
            var addBtn = new Button
            {
                Text = "去设置添加",
                Font = Font11,
                FlatStyle = FlatStyle.Flat,
                BackColor = ButtonBackColor,
                ForeColor = AccentColor,
                Size = new Size(M(84), M(28)),
                Cursor = Cursors.Hand,
                AccessibleName = "打开设置添加截图设备"
            };
            addBtn.FlatAppearance.BorderColor = AccentBorderColor;
            addBtn.FlatAppearance.BorderSize = 1;
            addBtn.Click += OnSettingsClicked;
            card.Controls.Add(text);
            card.Controls.Add(addBtn);
            card.Tag = addBtn;
            return card;
        }

        private Control CreateAllVisitedEmptyCard()
        {
            var card = new Panel
            {
                BackColor = SuccessSoftColor,
                Size = new Size(ContentWidthNow, M(46))
            };
            card.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (var pen = new Pen(Color.FromArgb(0xB8, 0xDB, 0xC8), 1f))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
                }
            };
            var label = new Label
            {
                Text = "✓ 本轮已全部点过",
                Font = Font13Bold,
                ForeColor = SuccessColor,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            };
            card.Controls.Add(label);
            return card;
        }

        private DeviceRowWidget CreateDeviceRowWidget(DeviceDisplayItem item)
        {
            string device = item.Name;
            var rowPanel = new Panel
            {
                BackColor = ButtonBackColor,
                Size = new Size(ContentWidthNow, M(PanelLayout.SchemeBDeviceRowHeight)),
                Cursor = Cursors.Hand,
                AccessibleName = "设备：" + device
            };
            var nameLabel = new Label
            {
                Text = device,
                Font = Font13,
                ForeColor = PrimaryTextColor,
                AutoSize = false,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Cursor = Cursors.Hand
            };
            var statusLabel = new Label
            {
                Text = item.Visited ? "✓ 已点过" : "待截图",
                Font = Font11,
                ForeColor = item.Visited ? SuccessColor : WeakTextColor,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
                Cursor = Cursors.Hand
            };
            var unmarkBtn = new Button
            {
                Text = "恢复未点",
                Font = Font11,
                FlatStyle = FlatStyle.Flat,
                BackColor = SurfaceColor,
                ForeColor = SecondaryTextColor,
                Visible = item.Visited,
                Cursor = Cursors.Hand,
                AccessibleName = "恢复未点：" + device
            };
            unmarkBtn.FlatAppearance.BorderColor = BorderColor;
            unmarkBtn.FlatAppearance.BorderSize = 1;

            var widget = new DeviceRowWidget
            {
                Device = device,
                Visited = item.Visited,
                Panel = rowPanel,
                NameLabel = nameLabel,
                StatusLabel = statusLabel,
                UnmarkButton = unmarkBtn
            };

            rowPanel.Tag = widget;
            rowPanel.Paint += OnDeviceRowPaint;
            rowPanel.Click += delegate { OnDeviceRowActivated(device); };
            nameLabel.Click += delegate { OnDeviceRowActivated(device); };
            statusLabel.Click += delegate { OnDeviceRowActivated(device); };
            unmarkBtn.Click += delegate { OnUnmarkDeviceClicked(device); };

            rowPanel.Controls.Add(nameLabel);
            rowPanel.Controls.Add(statusLabel);
            rowPanel.Controls.Add(unmarkBtn);
            _deviceToolTip.SetToolTip(rowPanel, device);
            _deviceToolTip.SetToolTip(nameLabel, device);
            return widget;
        }

        private void OnDeviceRowPaint(object sender, PaintEventArgs e)
        {
            var panel = (Panel)sender;
            var widget = panel.Tag as DeviceRowWidget;
            if (widget == null)
                return;

            bool waitingTarget = _stateMachine.State == PanelState.Waiting
                && string.Equals(widget.Device, _stateMachine.CurrentDevice, StringComparison.OrdinalIgnoreCase);
            bool capturingTarget = _stateMachine.State == PanelState.Capturing
                && string.Equals(widget.Device, _stateMachine.CurrentDevice, StringComparison.OrdinalIgnoreCase);

            Color border = waitingTarget
                ? AccentColor
                : (capturingTarget ? WarningColor : BorderColor);
            using (var pen = new Pen(border, waitingTarget ? 1.5f : 1f))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, panel.Width - 1, panel.Height - 1);
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int circleSize = M(16);
            int cx = M(10);
            int cy = (panel.Height - circleSize) / 2;

            if (waitingTarget)
            {
                using (var brush = new SolidBrush(AccentColor))
                    e.Graphics.FillEllipse(brush, cx, cy, circleSize, circleSize);
                int inner = Math.Max(4, M(6));
                using (var brush = new SolidBrush(Color.White))
                    e.Graphics.FillEllipse(brush, cx + (circleSize - inner) / 2, cy + (circleSize - inner) / 2, inner, inner);
            }
            else if (widget.Visited)
            {
                using (var brush = new SolidBrush(SuccessColor))
                    e.Graphics.FillEllipse(brush, cx, cy, circleSize, circleSize);
                using (var pen = new Pen(Color.White, Math.Max(1.5f, (float)(1.8 * ScaleFactor))))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    Point[] pts = new[]
                    {
                        new Point(cx + M(4), cy + M(8)),
                        new Point(cx + M(7), cy + M(11)),
                        new Point(cx + M(12), cy + M(5))
                    };
                    e.Graphics.DrawLines(pen, pts);
                }
            }
            else
            {
                using (var pen = new Pen(Color.FromArgb(0xB8, 0xC4, 0xD0), Math.Max(1f, (float)ScaleFactor)))
                    e.Graphics.DrawEllipse(pen, cx, cy, circleSize, circleSize);
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
                Control hint = CreateHintLabel("尚未配置凭据，点击去设置添加", AccentColor, M(PanelLayout.SchemeBCredentialHeight));
                hint.Cursor = Cursors.Hand;
                hint.Click += OnSettingsClicked;
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
                    Font = Font12,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = ButtonBackColor,
                    ForeColor = entry.NeedsReset ? ErrorColor : PrimaryTextColor,
                    TextAlign = ContentAlignment.MiddleLeft,
                    AutoEllipsis = true,
                    Padding = new Padding(M(22), 0, M(6), 0),
                    Cursor = Cursors.Hand,
                    AccessibleName = "凭据：" + entry.Title
                };
                button.FlatAppearance.BorderColor = BorderColor;
                button.FlatAppearance.BorderSize = 1;
                button.FlatAppearance.MouseOverBackColor = SurfaceColor;
                button.Paint += PaintCredentialCopyIcon;
                button.Click += OnCredentialButtonClicked;
                _credentialControls.Add(button);
                _credentialZonePanel.Controls.Add(button);
                _deviceToolTip.SetToolTip(button, "复制凭据：" + entry.Title);
            }
        }

        private void PaintCredentialCopyIcon(object sender, PaintEventArgs e)
        {
            var btn = (Button)sender;
            var entry = btn.Tag as CredentialEntry;
            Color ink = entry != null && entry.NeedsReset ? ErrorColor : WeakTextColor;
            int iconX = M(8);
            int iconY = (btn.Height - M(12)) / 2;
            int backW = M(7);
            int backH = M(9);
            using (var pen = new Pen(ink, 1f))
            {
                e.Graphics.DrawRectangle(pen, iconX, iconY, backW, backH);
                using (var bg = new SolidBrush(btn.BackColor))
                    e.Graphics.FillRectangle(bg, iconX + M(3), iconY + M(3), backW, backH);
                e.Graphics.DrawRectangle(pen, iconX + M(3), iconY + M(3), backW, backH);
            }
        }

        /// <summary>空态提示行：虚线边（Paint 自绘）、指定文字色。</summary>
        private Label CreateHintLabel(string text, Color foreColor, int height)
        {
            var hint = new Label
            {
                Text = text,
                Font = Font12,
                ForeColor = foreColor,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(M(10), 0, M(8), 0),
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

        private void UpdateDeviceHeader()
        {
            _deviceArrowLabel.Text = _config.IsDeviceCollapsed ? "›" : "˅";
        }

        /// <summary>
        /// 截图区折叠切换（v1.0.3，与凭据区同一套）：翻转状态 → BestEffort 保存 →
        /// 重建设备区 → 更新箭头 → Bottom 锚定重算高度。折叠不触碰选窗进行中状态。
        /// </summary>
        private void OnDeviceHeaderClicked(object sender, EventArgs e)
        {
            _config.IsDeviceCollapsed = !_config.IsDeviceCollapsed;
            SaveBestEffort(); // 失败不阻塞 UI（SaveBestEffort 内部处理）
            RebuildDeviceButtons();
            UpdateDeviceHeader();
            ApplyHeightAnchored();
        }

        private void UpdateRootLabel()
        {
            if (_configWriteProtected)
            {
                _rootLabel.Text = "配置读取失败，点击此处重试（保存已暂停）";
                _rootLabel.ForeColor = ErrorColor;
                _openFolderButton.Text = "重试读取配置";
                return;
            }
            _openFolderButton.Text = "打开巡检文件夹 ↗";
            _rootLabel.Text = string.IsNullOrEmpty(_config.RootPath)
                ? "尚未设置保存目录"
                : _config.RootPath;
            _rootLabel.ForeColor = WeakTextColor;
            if (!string.IsNullOrEmpty(_config.RootPath))
                _rootToolTip.SetToolTip(_rootLabel, _config.RootPath);
        }

        // ---------- 交互 ----------

        private void OnQuickLinkButtonClicked(object sender, EventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            var entry = btn.Tag as QuickLinkEntry;
            if (entry == null) return;
            string error;
            if (!_linkLauncher.TryOpen(entry.Url, out error))
                ShowFeedback("打开链接失败：" + error, true);
        }

        private void OnPlanToggleClicked(object sender, EventArgs e)
        {
            if (_session == null) return;
            bool next = !_session.PlanModeEnabled;
            _session.SetPlanModeEnabled(next);
            SaveBestEffort();
            RebuildPlanCard();
            RebuildDeviceButtons();
            RefreshStateVisuals();
            ApplyHeightAnchored();
        }

        private void OnToggleShowVisitedClicked(object sender, EventArgs e)
        {
            if (_session == null) return;
            _session.ToggleShowVisited();
            RebuildPlanCard();
            RebuildDeviceButtons();
            RefreshStateVisuals();
            ApplyHeightAnchored();
        }

        private void OnResetRoundClicked(object sender, EventArgs e)
        {
            if (_session == null) return;
            using (var confirm = new ResetRoundConfirmDialog())
            {
                UiTheme.ScaleLayout(confirm, ScaleFactor);
                if (confirm.ShowDialog(this) != DialogResult.Yes)
                    return;
            }
            string progressErr;
            _session.ResetRound(out progressErr);
            RebuildPlanCard();
            RebuildDeviceButtons();
            RefreshStateVisuals();
            ApplyHeightAnchored();
            if (!string.IsNullOrEmpty(progressErr))
                ShowFeedback("进度保存失败：" + progressErr, true);
            else
                ShowSuccessFeedback("已重置本轮巡检进度");
        }

        private void OnUnmarkDeviceClicked(string device)
        {
            if (_session == null || string.IsNullOrEmpty(device))
                return;
            string progressErr;
            if (_session.UnmarkDevice(device, out progressErr))
            {
                RebuildPlanCard();
                RebuildDeviceButtons();
                RefreshStateVisuals();
                ApplyHeightAnchored();
                if (!string.IsNullOrEmpty(progressErr))
                    ShowFeedback("进度保存失败：" + progressErr, true);
                else
                    ShowFeedback("已恢复未点：" + device, false);
            }
        }

        private void OnDeviceRowActivated(string device)
        {
            if (string.IsNullOrEmpty(device) || !ScreenshotAvailable)
                return;

            string progressErr = null;
            InspectionPlanToken token = default(InspectionPlanToken);
            if (_session == null
                ? !_stateMachine.SelectDevice(device)
                : !_session.TrySelectDevice(device, out token, out progressErr))
            {
                return;
            }
            if (_session != null)
            {
                // 若正在切换目标或从空闲点下设备，绑定当前等待轮次令牌并刷新可见列表
                _waitingPlanToken = token;
            }

            if (_stateMachine.State == PanelState.Waiting)
            {
                _config.LastDevice = _stateMachine.CurrentDevice;
                SaveBestEffort();
                RebuildPlanCard();
                RebuildDeviceButtons();
                ApplyHeightAnchored();
            }
            OnStateChanged();
            if (_stateMachine.State == PanelState.Waiting)
            {
                ShowFeedback(
                    string.IsNullOrEmpty(progressErr)
                        ? "请点目标窗口截取「" + _stateMachine.CurrentDevice + "」（Esc 或右键取消）"
                        : "请点目标窗口（进度暂未落盘：" + progressErr + "）",
                    !string.IsNullOrEmpty(progressErr));
            }
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
                    ShowSuccessFeedback("已复制「" + entry.Title + "」");
                    break;
                case CopyResult.NeedsReset:
                    // 运行时标记并刷新按钮显示（内存标记，不落盘）
                    entry.NeedsReset = true;
                    RebuildCredentialControls();
                    ApplyHeightAnchored();
                    ShowFeedback("该凭据需重新设置，请到设置中重新填写", true);
                    break;
                default: // Failed
                    ShowFeedback("复制失败，请重试", true);
                    break;
            }
        }

        private void OnSettingsClicked(object sender, EventArgs e)
        {
            if (_configWriteProtected && !RetryConfigLoad())
                return;

            double systemDpiScale;
            try
            {
                systemDpiScale = Math.Max(1.0, DeviceDpi / 96.0);
            }
            catch
            {
                systemDpiScale = 1.0;
            }
            _settingsDialogOpen = true;
            try
            {
                using (var form = new SettingsForm(_config, _store, _protector, systemDpiScale))
                {
                    form.Saved += delegate
                    {
                        // 成功落盘即刷新运行态，不等 ShowDialog 返回；一次提交只走一次。
                        bool autoClearChanged =
                            ConfigStore.ShouldRebuildClipboardGuard(_config, form.ResultConfig);
                        _config = AppConfigCloner.Clone(form.ResultConfig);
                        _configRevision = _store.CurrentRevision;
                        _configWriteProtected = false;
                        _configLoadError = null;
                        EnsureConfigLists();
                        if (_session != null)
                        {
                            string progressErr;
                            _session.ApplySavedConfig(
                                _config,
                                form.LastAppliedDeviceRenames,
                                form.LastAppliedImportDeviceReplace,
                                out progressErr);
                        }
                        ApplyScaleToStaticLayout();
                        if (autoClearChanged)
                            RebuildClipboardGuard();
                        RefreshConfigState();
                        ApplyHeightAnchored();
                        ApplyConfiguredWindowPositionIfVisible();
                        ShowSuccessFeedback("设置已应用");
                    };
                    form.ShowDialog(this);
                }
            }
            finally
            {
                _settingsDialogOpen = false;
            }
            TryMaybeShowCompletionPrompt();
        }

        private void OnRootLabelClicked(object sender, EventArgs e)
        {
            OpenInspectionFolders();
        }

        private void OpenInspectionFolders()
        {
            if (_configWriteProtected)
            {
                if (RetryConfigLoad())
                    ShowSuccessFeedback("配置读取已恢复，保存已解除保护");
                return;
            }
            if (string.IsNullOrEmpty(_config.RootPath))
            {
                ShowFeedback("尚未设置保存目录，请先在设置中选择", true);
                return;
            }

            List<string> folders = _session != null
                ? _session.ResolveOpenFolders()
                : _folderNavigator.ResolveFolders(
                    _config.RootPath, _config.FolderNamePattern, _clock.Now, null);
            if (folders.Count == 0)
            {
                ShowFeedback(HasSavedPaths() ? "已保存的目录已不存在" : "今天尚无截图", true);
                return;
            }
            if (folders.Count == 1)
            {
                OpenSingleInspectionFolder(folders[0]);
                return;
            }

            // 本轮有主目录和暂存目录等多处文件：让用户清晰选择打开哪一处，避免漏看暂存图
            var menu = new ContextMenuStrip();
            for (int i = 0; i < folders.Count; i++)
            {
                string folder = folders[i];
                var item = new ToolStripMenuItem((i == 0 ? "今日目录：" : "暂存目录：") + folder);
                item.Click += delegate { OpenSingleInspectionFolder(folder); };
                menu.Items.Add(item);
            }
            menu.Items.Add(new ToolStripSeparator());
            var openAll = new ToolStripMenuItem("全部打开（" + folders.Count + " 个目录）");
            openAll.Click += delegate
            {
                for (int i = 0; i < folders.Count; i++)
                    OpenSingleInspectionFolder(folders[i]);
            };
            menu.Items.Add(openAll);
            menu.Show(_openFolderButton, new Point(0, 0));
        }

        private void OpenSingleInspectionFolder(string dir)
        {
            string latest = null;
            DateTime latestTime = DateTime.MinValue;
            try
            {
                foreach (string file in Directory.GetFiles(dir, "*.png"))
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
                {
                    string err;
                    if (!_folderNavigator.TryOpenFolder(dir, out err))
                        ShowFeedback("打开文件夹失败：" + err, true);
                }
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
            bool capturing = _stateMachine.State == PanelState.Capturing;

            _deviceHeaderLabel.Text = waiting
                ? "请点击要截图的窗口"
                : (capturing ? "正在截图…" : "截图设备");
            _deviceHeaderLabel.Font = waiting ? Font13Bold : Font12Bold;
            _deviceHeaderLabel.ForeColor = waiting ? AccentColor : (capturing ? WarningColor : SecondaryTextColor);
            _deviceSubLabel.Text = waiting
                ? (_stateMachine.CurrentDevice ?? "等待选窗")
                : (capturing ? (_stateMachine.CurrentDevice ?? "截图中") : "点击设备，再选窗口");
            _deviceSubLabel.ForeColor = waiting ? AccentColor : WeakTextColor;

            for (int i = 0; i < _deviceRows.Count; i++)
            {
                DeviceRowWidget row = _deviceRows[i];
                bool isWaitingTarget = waiting && string.Equals(row.Device, _stateMachine.CurrentDevice, StringComparison.OrdinalIgnoreCase);
                bool isCapturingTarget = capturing && string.Equals(row.Device, _stateMachine.CurrentDevice, StringComparison.OrdinalIgnoreCase);

                if (isWaitingTarget)
                {
                    row.Panel.BackColor = AccentSoftColor;
                    row.NameLabel.ForeColor = AccentColor;
                    row.NameLabel.Font = Font13Bold;
                    row.StatusLabel.Text = "等待选窗";
                    row.StatusLabel.ForeColor = AccentColor;
                }
                else if (isCapturingTarget)
                {
                    row.Panel.BackColor = WarningSoftColor;
                    row.NameLabel.ForeColor = WarningColor;
                    row.NameLabel.Font = Font13Bold;
                    row.StatusLabel.Text = "截图中";
                    row.StatusLabel.ForeColor = WarningColor;
                }
                else if (!waiting && !capturing
                    && string.Equals(row.Device, _config.LastDevice, StringComparison.OrdinalIgnoreCase))
                {
                    row.Panel.BackColor = row.Visited ? Color.FromArgb(0xF6, 0xFB, 0xF8) : LastDeviceBackColor;
                    row.NameLabel.ForeColor = PrimaryTextColor;
                    row.NameLabel.Font = Font13;
                    row.StatusLabel.Text = row.Visited ? "✓ 已点过" : "待截图";
                    row.StatusLabel.ForeColor = row.Visited ? SuccessColor : WeakTextColor;
                }
                else
                {
                    row.Panel.BackColor = ButtonBackColor;
                    row.NameLabel.ForeColor = ScreenshotAvailable ? PrimaryTextColor : WeakTextColor;
                    row.NameLabel.Font = Font13;
                    row.StatusLabel.Text = row.Visited ? "✓ 已点过" : "待截图";
                    row.StatusLabel.ForeColor = row.Visited ? SuccessColor : WeakTextColor;
                }

                row.UnmarkButton.Visible = row.Visited && !isWaitingTarget && !isCapturingTarget;
                row.Panel.Enabled = ScreenshotAvailable;
                LayoutSingleDeviceRow(row, row.Panel.Width, row.Panel.Height);
                row.Panel.Invalidate();
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
                    TryMaybeShowCompletionPrompt();
                    break;
                case ResolveReason.Ok:
                    if (!_stateMachine.BeginCapture())
                        return;
                    OnStateChanged(); // Capturing → 停钩
                    // 进 Capturing 即组请求快照（Task 5）：根目录取当前配置、
                    // 日期与计划代次在此刻固定，保存与失败重试全程共用。
                    _currentRequest = new CaptureRequest
                    {
                        Device = _stateMachine.CurrentDevice,
                        RootPath = _config.RootPath,
                        Date = _clock.Now,
                        RequestId = _stateMachine.CurrentRequestId,
                        FolderPattern = _config.FolderNamePattern,
                        PlanToken = _waitingPlanToken
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
            {
                ShowFeedback("已取消选窗", false);
                TryMaybeShowCompletionPrompt();
            }
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
            {
                ShowFeedback("等待超时，已取消", true);
                TryMaybeShowCompletionPrompt();
            }
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
                Invoke(new Action(delegate
                {
                    _hiddenForCapture = true;
                    Opacity = 0;
                }));
            };
            Action showSelf = delegate
            {
                if (IsDisposed) return;
                Invoke(new Action(delegate
                {
                    Opacity = 1;
                    _hiddenForCapture = false;
                }));
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
            {
                OnStateChanged(); // 回 Idle → 集中点停钩（Capturing 时钩已停，幂等）
                TryMaybeShowCompletionPrompt();
            }
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
                    result = _saver.Save(image, request.RootPath, request.Device, request.Date, request.FolderPattern);
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
                if (_session != null)
                {
                    string progressErr;
                    InspectionPlanToken token = request != null ? request.PlanToken : default(InspectionPlanToken);
                    _session.RecordSaveSucceeded(result.FullPath, token, out progressErr);
                    RebuildPlanCard();
                    ApplyHeightAnchored();
                }
                if (result.UsedFallback)
                {
                    ShowFeedbackInternal("✓ " + Path.GetFileName(result.FullPath) + "（已暂存）：" + result.FullPath,
                        ErrorColor, false, 5000);
                }
                else
                {
                    ShowSuccessFeedback("✓ " + Path.GetFileName(result.FullPath));
                }
                TryMaybeShowCompletionPrompt();
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
                TryMaybeShowCompletionPrompt();
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
            TryMaybeShowCompletionPrompt();
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

        private void ShowConfigLoadErrorFeedback()
        {
            string detail = string.IsNullOrEmpty(_configLoadError)
                ? "配置读取失败，原文件已保留。"
                : _configLoadError;
            ShowPersistentFeedback("保存已暂停：" + detail + " 点根目录行重试读取。", ErrorColor);
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
            if (_rootToolTip != null)
                _rootToolTip.SetToolTip(_feedbackLabel, text);
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
            if (_configWriteProtected)
            {
                ShowConfigLoadErrorFeedback();
                return;
            }
            if (!string.IsNullOrEmpty(_config.RootPath)) return;

            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择截图保存根目录，只需选这一次";
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
            // 跨屏拖动后工作区可能变小；先钳制位置和尺寸，再保存最终坐标。
            ClampToWorkingArea();
            SaveWindowPosition();
        }

        /// <summary>
        /// 按窗体当前所在屏幕的工作区重新限制尺寸与位置；尺寸变化后保留底边。
        /// 选窗等待中不重建钩子，只调整 Bounds 并让钩子使用当前矩形。
        /// </summary>
        private void ClampToWorkingArea()
        {
            if (_config == null) return;
            Rectangle work = Screen.FromControl(this).WorkingArea;
            Size clamped = PanelLayout.ClampPanelSize(
                work, PanelWidthNow, ClientSize.Height, M(8));
            int width = clamped.Width;
            int height = clamped.Height;
            if (width != ClientSize.Width || height != ClientSize.Height)
            {
                int bottom = Bottom;
                ClientSize = new Size(width, height);
                Location = new Point(Left,
                    PanelLayout.ComputeTop(bottom, height, work.Top));
            }
            Location = PanelLayout.ClampLocationToWorkingArea(work, Size, Location);
            LayoutSections();
            if (_stateMachine.State == PanelState.Waiting)
                _hooks.UpdateSelfBounds(Bounds);
        }

        private void SaveWindowPosition()
        {
            if (WindowState != FormWindowState.Normal) return;
            _config.WindowLeft = Left;
            _config.WindowTop = Top;
            SaveBestEffort();
        }

        // ---------- 系统托盘（v1.0.2） ----------

        private void BuildTrayIcon()
        {
            var menu = new ContextMenuStrip();
            var showItem = new ToolStripMenuItem("显示面板");
            showItem.Click += delegate { ShowPanelFromTray(); };
            var exitItem = new ToolStripMenuItem("退出");
            // 退出与标题栏关闭按钮同一路径：Close() → FormClosing（先清剪贴板再退）
            exitItem.Click += delegate { Close(); };
            menu.Items.Add(showItem);
            menu.Items.Add(exitItem);

            _trayIcon = new NotifyIcon
            {
                Icon = Icon,
                Text = "巡检助手",
                ContextMenuStrip = menu,
                Visible = true
            };
            _trayIcon.MouseClick += OnTrayMouseClick;
            _trayIcon.DoubleClick += delegate { ShowPanelFromTray(); };
        }

        private void OnTrayMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                ShowPanelFromTray();
        }

        /// <summary>从托盘恢复：显示面板（位置不变、TopMost 照旧）并激活。</summary>
        private void ShowPanelFromTray()
        {
            if (!Visible)
                Show();
            if (WindowState != FormWindowState.Normal)
                WindowState = FormWindowState.Normal;
            Activate();
        }

        /// <summary>
        /// 最小化按钮（v1.0.2）：隐藏面板到托盘，程序继续运行；
        /// 首次隐藏给一次气泡提示，标记落盘后不再提示。
        /// </summary>
        private void OnMinimizeClicked(object sender, EventArgs e)
        {
            Hide();
            if (_config != null && !_config.TrayHintShown)
            {
                _config.TrayHintShown = true;
                SaveBestEffort();
                if (_trayIcon != null)
                    _trayIcon.ShowBalloonTip(3000, "巡检助手", "已最小化到托盘，点图标恢复", ToolTipIcon.Info);
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            // 托盘图标先隐藏并释放，避免进程退出后托盘区残影。
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
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
            if (_configWriteProtected || _store.WriteProtection.IsProtected)
                return new SaveResult
                {
                    Saved = false,
                    Error = "配置处于读取失败保护状态，保存已暂停。"
                };
            try
            {
                SaveResult result = _store.Save(_config, new ConfigSaveOptions
                {
                    ExpectedRevision = _configRevision
                });
                if (result != null && result.Saved)
                    _configRevision = result.Revision ?? _configRevision;
                return result;
            }
            catch
            {
                return null;
            }
        }

        // ---------- 跨日自动重置与全部点过提示 ----------

        private void OnDayRolloverTick(object sender, EventArgs e)
        {
            if (_session == null || IsDisposed)
                return;
            bool rolled;
            _session.EnsureCurrentDayResetIfRolled(out rolled);
            string todayText = FormatFullDate(_clock.Now);
            if (_dateLabel != null && !string.Equals(_dateLabel.Text, todayText, StringComparison.Ordinal))
                _dateLabel.Text = todayText;
            if (rolled && _stateMachine.State == PanelState.Idle && !_saveInFlight)
            {
                RebuildPlanCard();
                RebuildDeviceButtons();
                RefreshStateVisuals();
                ApplyHeightAnchored();
            }
        }

        private void TryMaybeShowCompletionPrompt()
        {
            if (_session == null || IsDisposed)
                return;
            string progressErr;
            if (!_session.TryClaimCompletionPrompt(_saveInFlight, _settingsDialogOpen, _hiddenForCapture || Opacity < 0.5, out progressErr))
                return;

            bool hasPendingFailure = _pendingImage != null && _pendingRequest != null;
            bool hasSavedFiles = HasSavedPaths();
            using (var dialog = new CompletionPromptDialog(hasPendingFailure, hasSavedFiles))
            {
                UiTheme.ScaleLayout(dialog, ScaleFactor);
                DialogResult answer = Visible
                    ? dialog.ShowDialog(this)
                    : dialog.ShowDialog();
                if (answer == DialogResult.Yes)
                    OpenInspectionFolders();
            }
        }

        private sealed class ResetRoundConfirmDialog : Form
        {
            public ResetRoundConfirmDialog()
            {
                Text = "重置本轮进度";
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MinimizeBox = false;
                MaximizeBox = false;
                TopMost = true;
                AutoScaleMode = AutoScaleMode.None;
                ClientSize = new Size(420, 148);
                BackColor = UiTheme.PageBackColor;
                Font = UiTheme.BodyFont;

                var message = new Label
                {
                    Text = "确定清空今日所有设备的“已点过”记录并重新开始本轮吗？\n\n已保存的截图文件不会被删除。",
                    AutoSize = false,
                    Dock = DockStyle.Fill,
                    Padding = new Padding(18),
                    ForeColor = UiTheme.PrimaryTextColor
                };
                var buttons = new FlowLayoutPanel
                {
                    Dock = DockStyle.Bottom,
                    Height = 52,
                    FlowDirection = FlowDirection.RightToLeft,
                    Padding = new Padding(12)
                };
                var confirm = UiTheme.PrimaryButton("确定重置");
                confirm.BackColor = UiTheme.ErrorColor;
                confirm.Width = 104;
                confirm.DialogResult = DialogResult.Yes;
                var cancel = UiTheme.SecondaryButton("取消");
                cancel.Width = 84;
                cancel.DialogResult = DialogResult.No;
                buttons.Controls.Add(confirm);
                buttons.Controls.Add(cancel);
                Controls.Add(message);
                Controls.Add(buttons);
                AcceptButton = cancel;
                CancelButton = cancel;
            }
        }

        private sealed class CompletionPromptDialog : Form
        {
            public CompletionPromptDialog(bool hasPendingFailure, bool hasSavedFiles)
            {
                Text = "今日巡检进度";
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MinimizeBox = false;
                MaximizeBox = false;
                TopMost = true;
                AutoScaleMode = AutoScaleMode.None;
                ClientSize = new Size(440, 160);
                BackColor = UiTheme.PageBackColor;
                Font = UiTheme.BodyFont;

                string body;
                if (hasPendingFailure)
                {
                    body = "今日所有设备均已点过，但当前仍有截图尚未保存成功，请先点击底部反馈区重试。\n\n是否仍要打开巡检文件夹？";
                }
                else if (!hasSavedFiles)
                {
                    body = "今日所有设备均已点过，但本轮尚未记录到已保存的截图文件。\n\n是否检查并打开巡检文件夹？";
                }
                else
                {
                    body = "今日所有设备均已点过。\n\n是否立即打开巡检文件夹？";
                }

                var message = new Label
                {
                    Text = body,
                    AutoSize = false,
                    Dock = DockStyle.Fill,
                    Padding = new Padding(18),
                    ForeColor = UiTheme.PrimaryTextColor
                };
                var buttons = new FlowLayoutPanel
                {
                    Dock = DockStyle.Bottom,
                    Height = 52,
                    FlowDirection = FlowDirection.RightToLeft,
                    Padding = new Padding(12)
                };
                var open = UiTheme.PrimaryButton("打开文件夹");
                open.Width = 116;
                open.DialogResult = DialogResult.Yes;
                var later = UiTheme.SecondaryButton("稍后");
                later.Width = 84;
                later.DialogResult = DialogResult.No;
                buttons.Controls.Add(open);
                buttons.Controls.Add(later);
                Controls.Add(message);
                Controls.Add(buttons);
                AcceptButton = open;
                CancelButton = later;
            }
        }
    }
}
