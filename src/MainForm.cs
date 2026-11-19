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
using InspectionWidget.Native;
using InspectionWidget.Planning;
using IClock = InspectionWidget.Storage.IClock;
using InspectionFolderNavigator = InspectionWidget.Storage.InspectionFolderNavigator;
using ScreenshotSaver = InspectionWidget.Storage.ScreenshotSaver;
using StorageSaveResult = InspectionWidget.Storage.SaveResult;
using SystemClock = InspectionWidget.Storage.SystemClock;
using ClipboardCopyResult = InspectionWidget.Storage.ClipboardCopyResult;
using InspectionFileTransferService = InspectionWidget.Storage.InspectionFileTransferService;
using UsbSyncResult = InspectionWidget.Storage.UsbSyncResult;
using UsbSyncStatus = InspectionWidget.Storage.UsbSyncStatus;

namespace InspectionWidget
{
    /// <summary>
    /// 主面板（v1.1.0 方案 B）：无边框置顶面板，352px 宽，含标题与完整日期、
    /// 快捷链接、今日巡检计划卡、两列凭据、带已点状态与恢复未点的截图设备区，
    /// 以及固定底部“打开巡检文件夹”主操作、反馈区和路径显示。
    /// </summary>
    public class MainForm : Form
    {
        // 颜色令牌（暖石板 + 深海青终端风，与 UiTheme 对齐）
        private static readonly Color AccentColor = UiTheme.AccentColor;
        private static readonly Color AccentHoverColor = UiTheme.AccentHoverColor;
        private static readonly Color AccentSoftColor = UiTheme.AccentSoftColor;
        private static readonly Color AccentBorderColor = UiTheme.AccentBorderColor;
        private static readonly Color SuccessColor = UiTheme.SuccessColor;
        private static readonly Color SuccessSoftColor = UiTheme.SuccessSoftColor;
        private static readonly Color SuccessBorderColor = UiTheme.SuccessBorderColor;
        private static readonly Color ErrorColor = UiTheme.ErrorColor;
        private static readonly Color WarningColor = UiTheme.WarningColor;
        private static readonly Color WarningSoftColor = UiTheme.WarningSoftColor;
        private static readonly Color PrimaryTextColor = UiTheme.PrimaryTextColor;
        private static readonly Color SecondaryTextColor = UiTheme.SecondaryTextColor;
        private static readonly Color WeakTextColor = UiTheme.WeakTextColor;
        private static readonly Color PanelBackColor = UiTheme.PageBackColor;
        private static readonly Color CardBackColor = UiTheme.CardBackColor;
        private static readonly Color SurfaceColor = UiTheme.SurfaceColor;
        private static readonly Color BorderColor = UiTheme.BorderColor;
        private static readonly Color BorderSoftColor = UiTheme.BorderSoftColor;
        private static readonly Color ButtonBackColor = Color.White;

        private const int SideMargin = 14;

        // 界面缩放（v1.0.1）：度量一律经 M() 按当前配置档位缩放，布局公式在 PanelLayout。
        private double ScaleFactor
        {
            get
            {
                if (_config == null) return 1.0;
                return UiScale.Factor(_config.UiScalePercent) * GetSystemDpiScale();
            }
        }

        protected virtual double GetSystemDpiScale()
        {
            try
            {
                return Math.Max(1.0, DeviceDpi / 96.0);
            }
            catch
            {
                return 1.0;
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
        private readonly IWindowPickHooks _hooks;
        private bool _hooksDisposed;
        private readonly QuickLinkLauncher _linkLauncher = new QuickLinkLauncher();
        private readonly InspectionFolderNavigator _folderNavigator;
        private InspectionProgressStore _progressStore;
        private InspectionSessionController _session;
        private InspectionPlanToken _waitingPlanToken;
        private double _appliedScaleFactor = double.NaN;
        private DateTime _uiRenderedPlanDate;
        private bool _pendingDayRolloverRefresh;
        private AppConfig _config;
        private string _configRevision = string.Empty;
        private bool _configWriteProtected;
        private string _configLoadError;

        // ---------- 凭据模块（Task 6） ----------

        private readonly ICredentialProtector _protector = new DpapiProtector();
        private readonly IClipboardGateway _clipboardGateway = new WindowsClipboardGateway();
        private ClipboardGuard _clipboardGuard;
        private CredentialService _credentialService;
        private readonly GlobalHotkeyManager _hotkeyManager = new GlobalHotkeyManager();
        private IPasteSimulator _pasteSimulator = new WindowsPasteSimulator();
        private readonly InspectionFileTransferService _fileTransferService = new InspectionFileTransferService();
        private string _hotkeyWarning;

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

        private readonly IClock _clock;
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
        private ToolTip _deviceRowToolTip;
        private ContextMenuStrip _deviceRowMenu;
        private TargetHighlightOverlay _targetOverlay;
        private Timer _targetHoverTimer;
        private Bitmap _gearImage;
        private Bitmap _gearHoverImage;
        private bool _settingsHovered;
        private bool _minimizeHovered;
        private bool _closeHovered;

        // 系统托盘（v1.0.2）：常驻 NotifyIcon，最小化 = 隐藏到托盘，托盘恢复/退出。
        private NotifyIcon _trayIcon;
        private ContextMenuStrip _trayMenu;
        private ContextMenuStrip _folderMenu;
        private Icon _ownedAppIcon;
        private bool _formClosing;

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
        private Button _planRecoveryButton;
        private Label _planDisabledHintLabel;

        private Panel _deviceHeaderPanel;
        private Label _deviceHeaderLabel;
        private Label _deviceSubLabel;
        private Panel _credentialHeaderPanel;
        private Label _credentialHeaderLabel;
        private Label _credentialSubLabel;
        private Label _credentialArrowLabel;
        private string _copiedCredentialTitle;
        private int _credentialCountdownSeconds;
        private Timer _credentialCountdownTimer;
        private Label _deviceArrowLabel;
        private Panel _credentialZonePanel;
        private Panel _deviceZonePanel;
        private Panel _bottomPanel;
        private Button _pasteFileButton;
        private Button _usbSyncButton;
        private ContextMenuStrip _usbSyncMenu;
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

        private sealed class DeviceRowPanel : Panel
        {
            public Action ActivateAction;
            public Action<int> NavigateAction;

            public DeviceRowPanel()
            {
                SetStyle(ControlStyles.Selectable
                    | ControlStyles.UserPaint
                    | ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw, true);
                DoubleBuffered = true;
                ResizeRedraw = true;
                TabStop = true;
            }

            protected override bool IsInputKey(Keys keyData)
            {
                if (keyData == Keys.Enter || keyData == Keys.Space
                    || keyData == Keys.Up || keyData == Keys.Down)
                    return true;
                return base.IsInputKey(keyData);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                if (!e.Handled && (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space))
                {
                    e.Handled = true;
                    if (ActivateAction != null)
                        ActivateAction();
                    return;
                }
                if (!e.Handled && e.KeyCode == Keys.Up && NavigateAction != null)
                {
                    e.Handled = true;
                    NavigateAction(-1);
                    return;
                }
                if (!e.Handled && e.KeyCode == Keys.Down && NavigateAction != null)
                {
                    e.Handled = true;
                    NavigateAction(1);
                }
            }

            protected override void OnEnter(EventArgs e)
            {
                base.OnEnter(e);
                Invalidate();
            }

            protected override void OnLeave(EventArgs e)
            {
                base.OnLeave(e);
                Invalidate();
            }
        }

        private sealed class DeviceRowWidget
        {
            public string Device;
            public bool Visited;
            public bool Hovered;
            public Color BaseBackColor = ButtonBackColor;
            public Panel Panel;
            public Label NameLabel;
            public Label StatusLabel;
            public Button UnmarkButton;
        }

        private bool _dragging;
        private Point _dragOffset;

        // 缓存字体（18 等宽加粗 / 15 加粗 / 13 常规 / 13 加粗 / 12 常规 / 12 加粗 / 11 常规）：全窗体复用。
        private Font _font18BoldMono;
        private Font _font15Bold;
        private Font _font13;
        private Font _font13Bold;
        private Font _font12;
        private Font _font12Bold;
        private Font _font11;

        /// <summary>
        /// 延迟收集旧 Font：切换缩放时先把新字体分配给所有控件再统一释放旧字体，
        /// 避免任何控件在过渡期间仍持有已释放的 Font。
        /// </summary>
        private static Font SwapScaledFont(ref Font cache, float basePx, FontStyle style, double scale, List<IDisposable> retired)
        {
            float target = basePx * (float)scale;
            if (cache == null || Math.Abs(cache.Size - target) > 0.01f)
            {
                if (cache != null)
                {
                    if (retired != null) retired.Add(cache);
                    else cache.Dispose();
                }
                cache = UiTheme.CreateFont(target, style);
            }
            return cache;
        }

        private void EnsureScaledFonts(List<IDisposable> retired)
        {
            double scale = ScaleFactor;
            float monoTarget = 18f * (float)scale;
            if (_font18BoldMono == null || Math.Abs(_font18BoldMono.Size - monoTarget) > 0.01f)
            {
                if (_font18BoldMono != null)
                {
                    if (retired != null) retired.Add(_font18BoldMono);
                    else _font18BoldMono.Dispose();
                }
                _font18BoldMono = UiTheme.CreateMonoFont(monoTarget, FontStyle.Bold);
            }
            SwapScaledFont(ref _font15Bold, 15f, FontStyle.Bold, scale, retired);
            SwapScaledFont(ref _font13, 13f, FontStyle.Regular, scale, retired);
            SwapScaledFont(ref _font13Bold, 13f, FontStyle.Bold, scale, retired);
            SwapScaledFont(ref _font12, 12f, FontStyle.Regular, scale, retired);
            SwapScaledFont(ref _font12Bold, 12f, FontStyle.Bold, scale, retired);
            SwapScaledFont(ref _font11, 11f, FontStyle.Regular, scale, retired);

            int imageSize = M(16);
            if (_gearImage == null || _gearImage.Width != imageSize)
            {
                if (_gearImage != null)
                {
                    if (retired != null) retired.Add(_gearImage);
                    else _gearImage.Dispose();
                }
                _gearImage = GearArt.DrawGear(imageSize, SecondaryTextColor);
            }
            if (_gearHoverImage == null || _gearHoverImage.Width != imageSize)
            {
                if (_gearHoverImage != null)
                {
                    if (retired != null) retired.Add(_gearHoverImage);
                    else _gearHoverImage.Dispose();
                }
                _gearHoverImage = GearArt.DrawGear(imageSize, PrimaryTextColor);
            }
        }

        private Font Font18BoldMono
        {
            get
            {
                if (_font18BoldMono == null)
                    _font18BoldMono = UiTheme.CreateMonoFont(18f * (float)ScaleFactor, FontStyle.Bold);
                return _font18BoldMono;
            }
        }

        private Font Font15Bold
        {
            get { return _font15Bold ?? SwapScaledFont(ref _font15Bold, 15f, FontStyle.Bold, ScaleFactor, null); }
        }

        private Font Font13
        {
            get { return _font13 ?? SwapScaledFont(ref _font13, 13f, FontStyle.Regular, ScaleFactor, null); }
        }

        private Font Font13Bold
        {
            get { return _font13Bold ?? SwapScaledFont(ref _font13Bold, 13f, FontStyle.Bold, ScaleFactor, null); }
        }

        private Font Font12
        {
            get { return _font12 ?? SwapScaledFont(ref _font12, 12f, FontStyle.Regular, ScaleFactor, null); }
        }

        private Font Font12Bold
        {
            get { return _font12Bold ?? SwapScaledFont(ref _font12Bold, 12f, FontStyle.Bold, ScaleFactor, null); }
        }

        private Font Font11
        {
            get { return _font11 ?? SwapScaledFont(ref _font11, 11f, FontStyle.Regular, ScaleFactor, null); }
        }

        private void ApplySettingsButtonVisual()
        {
            _settingsButton.Text = string.Empty;
            _settingsButton.Font = Font13;
            int imageSize = M(16);
            if (_gearImage == null || _gearImage.Width != imageSize)
            {
                Bitmap old = _gearImage;
                _gearImage = GearArt.DrawGear(imageSize, SecondaryTextColor);
                if (old != null) old.Dispose();
            }
            if (_gearHoverImage == null || _gearHoverImage.Width != imageSize)
            {
                Bitmap oldHover = _gearHoverImage;
                _gearHoverImage = GearArt.DrawGear(imageSize, PrimaryTextColor);
                if (oldHover != null) oldHover.Dispose();
            }
            _settingsButton.Image = _settingsHovered ? _gearHoverImage : _gearImage;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_clipboardGuard != null)
                {
                    _clipboardGuard.Cleared -= OnClipboardCleared;
                    _clipboardGuard.ClearSkipped -= OnClipboardClearSkipped;
                    _clipboardGuard.Dispose();
                }
                if (_hooks != null && !_hooksDisposed)
                {
                    _hooksDisposed = true;
                    _hooks.Dispose();
                }
                if (_pendingImage != null)
                {
                    _pendingImage.Dispose();
                    _pendingGeneration++;
                }
                if (_rootToolTip != null) _rootToolTip.Dispose();
                if (_titleToolTip != null) _titleToolTip.Dispose();
                if (_deviceToolTip != null) _deviceToolTip.Dispose();
                if (_deviceRowToolTip != null) _deviceRowToolTip.Dispose();
                if (_deviceRowMenu != null)
                {
                    _deviceRowMenu.Dispose();
                    _deviceRowMenu = null;
                }
                if (_targetHoverTimer != null)
                {
                    _targetHoverTimer.Stop();
                    _targetHoverTimer.Dispose();
                    _targetHoverTimer = null;
                }
                if (_targetOverlay != null)
                {
                    _targetOverlay.Dispose();
                    _targetOverlay = null;
                }
                if (_folderMenu != null)
                {
                    _folderMenu.Dispose();
                    _folderMenu = null;
                }
                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                    _trayIcon.Dispose();
                    _trayIcon = null;
                }
                if (_trayMenu != null)
                {
                    _trayMenu.Dispose();
                    _trayMenu = null;
                }
                if (_ownedAppIcon != null)
                {
                    Icon = null;
                    _ownedAppIcon.Dispose();
                    _ownedAppIcon = null;
                }
                if (_credentialCountdownTimer != null)
                {
                    _credentialCountdownTimer.Stop();
                    _credentialCountdownTimer.Dispose();
                }
                if (_feedbackTimer != null) _feedbackTimer.Dispose();
                if (_dayRolloverTimer != null) _dayRolloverTimer.Dispose();
                if (_font18BoldMono != null) _font18BoldMono.Dispose();
                if (_font15Bold != null) _font15Bold.Dispose();
                if (_font13 != null) _font13.Dispose();
                if (_font13Bold != null) _font13Bold.Dispose();
                if (_font12 != null) _font12.Dispose();
                if (_font12Bold != null) _font12Bold.Dispose();
                if (_font11 != null) _font11.Dispose();
                if (_gearImage != null) _gearImage.Dispose();
                if (_gearHoverImage != null) _gearHoverImage.Dispose();
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
            : this(null, null, null, null, null)
        {
        }

        internal MainForm(
            ConfigStore store,
            InspectionProgressStore progressStore,
            IClock clock,
            IWindowPickHooks hooks,
            InspectionFolderNavigator folderNavigator)
        {
            _store = store ?? new ConfigStore();
            _clock = clock ?? new SystemClock();
            _folderNavigator = folderNavigator ?? new InspectionFolderNavigator();
            _stateMachine = new PanelStateMachine();
            _hooks = hooks ?? new HookManager();
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
            _ownedAppIcon = AppIcon.Load();
            if (_ownedAppIcon != null)
                Icon = _ownedAppIcon;

            LoadResult load = _store.Load();
            _config = load.Config != null ? load.Config : AppConfig.CreateDefault();
            _configRevision = load.Revision ?? string.Empty;
            _configWriteProtected = load.Kind == ConfigLoadKind.IoErrorKept;
            _configLoadError = load.Error;
            EnsureConfigLists();
            _progressStore = progressStore ?? new InspectionProgressStore(ResolveProgressPath(_store));
            _session = new InspectionSessionController(
                _clock, _stateMachine, _progressStore, _config, _folderNavigator);

            BuildStaticLayout();
            BuildTrayIcon();
            RebuildClipboardGuard();

            ApplyScaleAndRebuild(true, false);
            RestorePosition();
            LayoutSections();

            if (_configWriteProtected)
                ShowConfigLoadErrorFeedback();
            else if (_deviceListError == null && _session.HasProgressError)
                ShowProgressErrorFeedback();

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
            if (double.IsNaN(_appliedScaleFactor) || Math.Abs(ScaleFactor - _appliedScaleFactor) > 0.001)
                ApplyScaleAndRebuild(true, true);
            else
                LayoutSections();
            RebindCredentialHotkeys();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY && _hotkeyManager != null)
            {
                int credIndex;
                if (_hotkeyManager.TryResolveHotkey(m.WParam.ToInt32(), out credIndex))
                {
                    OnCredentialHotkeyTriggered(credIndex);
                    return;
                }
            }
            base.WndProc(ref m);
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            if (_config == null)
                return;
            ApplyScaleAndRebuild(true, true);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateWaitingHookSelfBounds();
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            UpdateWaitingHookSelfBounds();
        }

        private void ApplyScaleAndRebuild(bool rebuildDynamic, bool adjustPosition)
        {
            var retired = new List<IDisposable>();
            try
            {
                EnsureScaledFonts(retired);
                ApplyScaleToStaticLayout();
                if (rebuildDynamic)
                    RefreshConfigState();
                ClientSize = new Size(PanelWidthNow, ComputeCurrentHeight());
                if (adjustPosition)
                {
                    if (_config != null && (!_config.WindowLeft.HasValue || !_config.WindowTop.HasValue))
                        RestorePosition();
                    ClampToWorkingArea();
                }
                else
                {
                    LayoutSections();
                }
                _appliedScaleFactor = ScaleFactor;
            }
            finally
            {
                for (int i = 0; i < retired.Count; i++)
                    retired[i].Dispose();
            }
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
                Location = new Point(SideMargin, 9)
            };
            var dateLabel = _dateLabel = new Label
            {
                Text = FormatFullDate(_clock.Now),
                Font = font12,
                ForeColor = SecondaryTextColor,
                AutoSize = true,
                Location = new Point(SideMargin, 31)
            };
            _titleToolTip = new ToolTip();
            _deviceToolTip = new ToolTip();
            _deviceRowToolTip = new ToolTip();
            _deviceRowMenu = new ContextMenuStrip();
            var settingsButton = _settingsButton = new Button
            {
                Text = string.Empty,
                Font = font13,
                ForeColor = SecondaryTextColor,
                FlatStyle = FlatStyle.Flat,
                BackColor = PanelBackColor,
                Cursor = Cursors.Hand,
                Size = new Size(PanelLayout.TitleBarButtonSize, PanelLayout.TitleBarButtonSize),
                Location = new Point(PanelLayout.TitleBarButtonX(PanelLayout.SchemeBPanelWidth, 2, 1.0), 8)
            };
            settingsButton.FlatAppearance.BorderSize = 0;
            settingsButton.FlatAppearance.MouseOverBackColor = SurfaceColor;
            settingsButton.MouseEnter += delegate
            {
                _settingsHovered = true;
                if (_gearHoverImage != null)
                    _settingsButton.Image = _gearHoverImage;
            };
            settingsButton.MouseLeave += delegate
            {
                _settingsHovered = false;
                if (_gearImage != null)
                    _settingsButton.Image = _gearImage;
            };
            settingsButton.Click += OnSettingsClicked;
            settingsButton.AccessibleName = "打开设置";
            _titleToolTip.SetToolTip(settingsButton, "设置");
            var minimizeButton = _minimizeButton = new Button
            {
                Text = string.Empty,
                Font = font13,
                ForeColor = SecondaryTextColor,
                FlatStyle = FlatStyle.Flat,
                BackColor = PanelBackColor,
                Cursor = Cursors.Hand,
                Size = new Size(PanelLayout.TitleBarButtonSize, PanelLayout.TitleBarButtonSize),
                Location = new Point(PanelLayout.TitleBarButtonX(PanelLayout.SchemeBPanelWidth, 1, 1.0), 8)
            };
            minimizeButton.FlatAppearance.BorderSize = 0;
            minimizeButton.FlatAppearance.MouseOverBackColor = SurfaceColor;
            minimizeButton.MouseEnter += delegate { _minimizeHovered = true; minimizeButton.Invalidate(); };
            minimizeButton.MouseLeave += delegate { _minimizeHovered = false; minimizeButton.Invalidate(); };
            minimizeButton.Paint += delegate(object sender, PaintEventArgs e)
            {
                var btn = (Button)sender;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                int half = Math.Max(4, (int)Math.Round(5 * ScaleFactor));
                int cx = btn.Width / 2;
                int cy = btn.Height / 2 + 1;
                Color ink = _minimizeHovered ? PrimaryTextColor : SecondaryTextColor;
                using (var pen = new Pen(ink, Math.Max(1.4f, (float)(1.5 * ScaleFactor))))
                {
                    e.Graphics.DrawLine(pen, cx - half, cy, cx + half, cy);
                }
            };
            minimizeButton.Click += OnMinimizeClicked;
            minimizeButton.AccessibleName = "最小化到托盘";
            _titleToolTip.SetToolTip(minimizeButton, "最小化");
            var closeButton = _closeButton = new Button
            {
                Text = string.Empty,
                Font = font13,
                ForeColor = SecondaryTextColor,
                FlatStyle = FlatStyle.Flat,
                BackColor = PanelBackColor,
                Cursor = Cursors.Hand,
                Size = new Size(PanelLayout.TitleBarButtonSize, PanelLayout.TitleBarButtonSize),
                Location = new Point(PanelLayout.TitleBarButtonX(PanelLayout.SchemeBPanelWidth, 0, 1.0), 8)
            };
            closeButton.FlatAppearance.BorderSize = 0;
            closeButton.FlatAppearance.MouseOverBackColor = UiTheme.ErrorSoftColor;
            closeButton.MouseEnter += delegate { _closeHovered = true; closeButton.Invalidate(); };
            closeButton.MouseLeave += delegate { _closeHovered = false; closeButton.Invalidate(); };
            closeButton.Paint += delegate(object sender, PaintEventArgs e)
            {
                var btn = (Button)sender;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                int half = Math.Max(4, (int)Math.Round(4.5 * ScaleFactor));
                int cx = btn.Width / 2;
                int cy = btn.Height / 2;
                Color ink = _closeHovered ? ErrorColor : SecondaryTextColor;
                using (var pen = new Pen(ink, Math.Max(1.4f, (float)(1.5 * ScaleFactor))))
                {
                    e.Graphics.DrawLine(pen, cx - half, cy - half, cx + half, cy + half);
                    e.Graphics.DrawLine(pen, cx + half, cy - half, cx - half, cy + half);
                }
            };
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
            _quickLinkHeaderPanel = new Panel { BackColor = PanelBackColor, Size = new Size(PanelLayout.SchemeBPanelWidth, 22) };
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
                Text = string.Empty,
                Font = font11,
                ForeColor = WeakTextColor,
                AutoSize = true,
                Visible = false,
                Location = new Point(PanelLayout.SchemeBPanelWidth - 82, 3)
            };
            _quickLinkHeaderPanel.Controls.Add(_quickLinkHeaderLabel);
            _quickLinkHeaderPanel.Controls.Add(_quickLinkSubLabel);
            _scrollHost.Controls.Add(_quickLinkHeaderPanel);

            _quickLinkZonePanel = new Panel { BackColor = PanelBackColor, Width = PanelLayout.SchemeBPanelWidth };
            _scrollHost.Controls.Add(_quickLinkZonePanel);

            // 2. 今日巡检计划卡（暖石板白底卡 + 3px 深石青左侧强调条 + 大号等宽计数 + 圆角胶囊进度条）
            _planCardPanel = new Panel
            {
                BackColor = CardBackColor,
                Width = PanelLayout.SchemeBPanelWidth - SideMargin * 2
            };
            _planCardPanel.Paint += delegate(object sender, PaintEventArgs e)
            {
                int w = _planCardPanel.Width;
                int h = _planCardPanel.Height;
                using (var pen = new Pen(BorderColor, 1f))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, w - 1, h - 1);
                }
                bool planEnabled = _session != null && _session.PlanModeEnabled;
                bool allDone = planEnabled && _session.Plan.TotalCount > 0 && _session.Plan.VisitedCount >= _session.Plan.TotalCount;
                Color barColor = !planEnabled ? BorderColor : (allDone ? SuccessColor : AccentColor);
                int barW = Math.Max(3, M(3));
                using (var barBrush = new SolidBrush(barColor))
                {
                    e.Graphics.FillRectangle(barBrush, 0, 0, barW, h);
                }
            };
            _planTitleLabel = new Label
            {
                Text = "今日巡检计划",
                Font = font12Bold,
                ForeColor = PrimaryTextColor,
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
            _planRecoveryButton = new Button
            {
                Text = "恢复进度",
                Font = font11,
                FlatStyle = FlatStyle.Flat,
                BackColor = UiTheme.ErrorSoftColor,
                ForeColor = ErrorColor,
                Visible = false,
                Cursor = Cursors.Hand,
                AccessibleName = "恢复或重试巡检进度"
            };
            _planRecoveryButton.FlatAppearance.BorderColor = UiTheme.ErrorBorderColor;
            _planRecoveryButton.FlatAppearance.BorderSize = 1;
            _planRecoveryButton.Click += OnPlanRecoveryClicked;
            _planProgressLabel = new Label
            {
                Text = "已点过",
                Font = font12,
                ForeColor = SecondaryTextColor,
                AutoSize = true
            };
            _planCountLabel = new Label
            {
                Text = "0 / 0",
                Font = Font18BoldMono,
                ForeColor = PrimaryTextColor,
                AutoSize = true
            };
            _planTrackPanel = new Panel
            {
                BackColor = CardBackColor
            };
            _planTrackPanel.Paint += delegate(object sender, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                int tw = _planTrackPanel.Width;
                int th = _planTrackPanel.Height;
                if (tw <= 0 || th <= 0) return;
                UiTheme.FillRoundedPill(e.Graphics, UiTheme.TrackBackColor, new Rectangle(0, 0, tw, th));
                int total = _session.Plan.TotalCount;
                int visited = _session.Plan.VisitedCount;
                if (total > 0 && visited > 0)
                {
                    int fillW = (int)Math.Round(tw * (Math.Min(visited, total) / (double)total));
                    if (fillW < th) fillW = Math.Min(tw, th);
                    Color fillColor = visited >= total ? SuccessColor : AccentColor;
                    UiTheme.FillRoundedPill(e.Graphics, fillColor, new Rectangle(0, 0, fillW, th));
                }
            };
            _planFillPanel = new Panel
            {
                BackColor = AccentColor,
                Visible = false,
                Location = new Point(0, 0)
            };
            _planTrackPanel.Controls.Add(_planFillPanel);

            _planActionPanel = new Panel { BackColor = CardBackColor };
            _showVisitedButton = new Button
            {
                Text = "查看已点过",
                Font = font11,
                FlatStyle = FlatStyle.Flat,
                BackColor = PanelBackColor,
                ForeColor = SecondaryTextColor,
                Cursor = Cursors.Hand,
                AccessibleName = "切换显示已点过设备"
            };
            _showVisitedButton.FlatAppearance.BorderColor = BorderColor;
            _showVisitedButton.FlatAppearance.BorderSize = 1;
            _showVisitedButton.FlatAppearance.MouseOverBackColor = SurfaceColor;
            _showVisitedButton.Click += OnToggleShowVisitedClicked;

            _resetRoundButton = new Button
            {
                Text = "重置本轮",
                Font = font11,
                FlatStyle = FlatStyle.Flat,
                BackColor = PanelBackColor,
                ForeColor = ErrorColor,
                Cursor = Cursors.Hand,
                AccessibleName = "重置本轮巡检进度"
            };
            _resetRoundButton.FlatAppearance.BorderColor = UiTheme.ErrorBorderColor;
            _resetRoundButton.FlatAppearance.BorderSize = 1;
            _resetRoundButton.FlatAppearance.MouseOverBackColor = UiTheme.ErrorSoftColor;
            _resetRoundButton.Click += OnResetRoundClicked;

            _planActionPanel.Controls.Add(_showVisitedButton);
            _planActionPanel.Controls.Add(_resetRoundButton);

            _planDisabledHintLabel = new Label
            {
                Text = string.Empty,
                Font = font11,
                ForeColor = SecondaryTextColor,
                AutoSize = false,
                Visible = false
            };

            _planCardPanel.Controls.Add(_planTitleLabel);
            _planCardPanel.Controls.Add(_planRecoveryButton);
            _planCardPanel.Controls.Add(_planToggleButton);
            _planCardPanel.Controls.Add(_planProgressLabel);
            _planCardPanel.Controls.Add(_planCountLabel);
            _planCardPanel.Controls.Add(_planTrackPanel);
            _planCardPanel.Controls.Add(_planActionPanel);
            _planCardPanel.Controls.Add(_planDisabledHintLabel);
            _scrollHost.Controls.Add(_planCardPanel);

            // 3. 凭据区（整行点击折叠，两列快捷复制）
            _credentialHeaderPanel = new Panel { BackColor = PanelBackColor, Size = new Size(PanelLayout.SchemeBPanelWidth, 22) };
            var credentialHeaderLabel = _credentialHeaderLabel = new Label
            {
                Text = "凭据",
                Font = font12Bold,
                ForeColor = SecondaryTextColor,
                AutoSize = true,
                Location = new Point(SideMargin, 2)
            };
            _credentialSubLabel = new Label
            {
                Text = string.Empty,
                Font = font11,
                ForeColor = WeakTextColor,
                AutoSize = true
            };
            _credentialArrowLabel = new Label
            {
                Text = string.Empty,
                AutoSize = false,
                Size = new Size(14, 14),
                Cursor = Cursors.Hand
            };
            _credentialArrowLabel.Paint += delegate(object sender, PaintEventArgs e)
            {
                var lbl = (Label)sender;
                PaintCollapseChevron(e.Graphics, lbl.Width, lbl.Height, WeakTextColor,
                    _config != null && _config.IsCredentialCollapsed);
            };
            _credentialHeaderPanel.Controls.Add(credentialHeaderLabel);
            _credentialHeaderPanel.Controls.Add(_credentialSubLabel);
            _credentialHeaderPanel.Controls.Add(_credentialArrowLabel);
            _credentialHeaderPanel.Click += OnCredentialHeaderClicked;
            credentialHeaderLabel.Click += OnCredentialHeaderClicked;
            _credentialSubLabel.Click += OnCredentialHeaderClicked;
            _credentialArrowLabel.Click += OnCredentialHeaderClicked;
            _scrollHost.Controls.Add(_credentialHeaderPanel);

            _credentialZonePanel = new Panel { BackColor = PanelBackColor, Width = PanelLayout.SchemeBPanelWidth };
            _scrollHost.Controls.Add(_credentialZonePanel);

            // 4. 截图设备区（整行点击折叠）
            var deviceHeaderPanel = _deviceHeaderPanel = new Panel { BackColor = PanelBackColor, Size = new Size(PanelLayout.SchemeBPanelWidth, 22) };
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
                Text = string.Empty,
                Font = font11,
                ForeColor = WeakTextColor,
                AutoSize = true
            };
            _deviceArrowLabel = new Label
            {
                Text = string.Empty,
                AutoSize = false,
                Size = new Size(14, 14),
                Cursor = Cursors.Hand
            };
            _deviceArrowLabel.Paint += delegate(object sender, PaintEventArgs e)
            {
                var lbl = (Label)sender;
                PaintCollapseChevron(e.Graphics, lbl.Width, lbl.Height, WeakTextColor,
                    _config != null && _config.IsDeviceCollapsed);
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
                BackColor = CardBackColor,
                Size = new Size(PanelLayout.SchemeBPanelWidth, PanelLayout.SchemeBFooterHeight)
            };
            bottomPanel.Name = "BottomPanel";
            bottomPanel.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (var pen = new Pen(BorderColor, 1f))
                {
                    e.Graphics.DrawLine(pen, 1, 0, bottomPanel.Width - 2, 0);
                }
            };
            _feedbackLabel = new Label
            {
                Font = font12,
                ForeColor = SecondaryTextColor,
                BackColor = CardBackColor,
                AutoSize = false,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
            _feedbackLabel.Click += OnFeedbackLabelClicked;

            _pasteFileButton = new Button
            {
                Text = "贴入文件",
                Font = font12,
                FlatStyle = FlatStyle.Flat,
                BackColor = SurfaceColor,
                ForeColor = PrimaryTextColor,
                Cursor = Cursors.Hand,
                AccessibleName = "从剪贴板贴入文件或文件夹"
            };
            _pasteFileButton.FlatAppearance.BorderColor = BorderColor;
            _pasteFileButton.FlatAppearance.BorderSize = 1;
            _pasteFileButton.FlatAppearance.MouseOverBackColor = UiTheme.NavHoverBackColor;
            _pasteFileButton.Click += OnPasteClipboardFilesClicked;

            _usbSyncButton = new Button
            {
                Text = "U盘同步",
                Font = font12,
                FlatStyle = FlatStyle.Flat,
                BackColor = SurfaceColor,
                ForeColor = PrimaryTextColor,
                Cursor = Cursors.Hand,
                AccessibleName = "从U盘目录同步今日巡检文件"
            };
            _usbSyncButton.FlatAppearance.BorderColor = BorderColor;
            _usbSyncButton.FlatAppearance.BorderSize = 1;
            _usbSyncButton.FlatAppearance.MouseOverBackColor = UiTheme.NavHoverBackColor;
            _usbSyncButton.Click += OnUsbSyncClicked;
            _usbSyncMenu = new ContextMenuStrip();
            var setUsbItem = new ToolStripMenuItem("设置 U盘目录…");
            setUsbItem.Click += delegate { PromptAndSaveUsbRootPath(false); };
            var clearUsbItem = new ToolStripMenuItem("清除 U盘目录");
            clearUsbItem.Click += delegate { ClearUsbRootPathFromMain(); };
            _usbSyncMenu.Items.Add(setUsbItem);
            _usbSyncMenu.Items.Add(clearUsbItem);
            _usbSyncButton.ContextMenuStrip = _usbSyncMenu;

            _openFolderButton = new Button
            {
                Text = "打开巡检文件夹",
                Font = font13Bold,
                FlatStyle = FlatStyle.Flat,
                BackColor = AccentColor,
                ForeColor = Color.White,
                Padding = new Padding(M(22), 0, M(26), 0),
                Cursor = Cursors.Hand,
                AccessibleName = "打开巡检文件夹"
            };
            _openFolderButton.FlatAppearance.BorderSize = 0;
            _openFolderButton.FlatAppearance.MouseOverBackColor = UiTheme.AccentHoverColor;
            _openFolderButton.Paint += delegate(object sender, PaintEventArgs e)
            {
                var btn = (Button)sender;
                PaintFolderOrExternalIcon(e.Graphics, btn.Width, btn.Height, Color.White, _configWriteProtected);
            };
            _openFolderButton.Click += OnRootLabelClicked;

            _rootToolTip = new ToolTip();
            _rootToolTip.SetToolTip(_pasteFileButton, "将剪贴板中的文件或文件夹存入今日巡检目录");
            _rootLabel = new Label
            {
                Font = font11,
                ForeColor = WeakTextColor,
                BackColor = CardBackColor,
                AutoSize = false,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Cursor = Cursors.Hand
            };
            _rootLabel.Click += OnRootLabelClicked;
            bottomPanel.Controls.Add(_feedbackLabel);
            bottomPanel.Controls.Add(_pasteFileButton);
            bottomPanel.Controls.Add(_usbSyncButton);
            bottomPanel.Controls.Add(_openFolderButton);
            bottomPanel.Controls.Add(_rootLabel);
            Controls.Add(bottomPanel);

            _feedbackTimer = new Timer { Interval = 2000 };
            _feedbackTimer.Tick += delegate
            {
                _feedbackTimer.Stop();
                if (_feedbackPersistent)
                    return;
                if (!RestorePersistentFeedbackIfNeeded())
                    _feedbackLabel.Text = string.Empty;
            };
            _credentialCountdownTimer = new Timer { Interval = 1000 };
            _credentialCountdownTimer.Tick += OnCredentialCountdownTick;
        }

        // ---------- 布局 ----------

        private void ApplyScaleToStaticLayout()
        {
            int w = PanelWidthNow;
            int margin = M(SideMargin);
            int fullContent = w - margin * 2;

            _titleBarPanel.Size = new Size(w, M(PanelLayout.SchemeBHeaderHeight));
            _titleLabel.Font = Font15Bold;
            _titleLabel.Location = new Point(margin, M(9));
            _dateLabel.Font = Font12;
            _dateLabel.Text = FormatFullDate(_clock.Now);
            _dateLabel.Location = new Point(margin, M(31));
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
            _planTitleLabel.Font = Font12Bold;
            _planRecoveryButton.Font = Font11;
            _planToggleButton.Font = Font11;
            _planProgressLabel.Font = Font12;
            _planCountLabel.Font = Font18BoldMono;
            _showVisitedButton.Font = Font11;
            _resetRoundButton.Font = Font11;
            _planDisabledHintLabel.Font = Font11;

            _credentialHeaderLabel.Font = Font12Bold;
            _credentialSubLabel.Font = Font11;
            _credentialArrowLabel.Size = new Size(M(14), M(14));
            _deviceHeaderLabel.Font = Font12Bold;
            _deviceSubLabel.Font = Font11;
            _deviceArrowLabel.Size = new Size(M(14), M(14));

            _bottomPanel.Size = new Size(w, M(PanelLayout.SchemeBFooterHeight));
            _feedbackLabel.Font = Font12;
            _feedbackLabel.Size = new Size(fullContent, M(22));
            _feedbackLabel.Location = new Point(margin, M(6));

            int actionGap = M(8);
            int halfW = Math.Max(M(60), (fullContent - actionGap) / 2);
            int rightHalfW = Math.Max(M(60), fullContent - actionGap - halfW);
            int actionRowY = M(32);
            int actionRowH = M(28);
            _pasteFileButton.Font = Font12;
            _pasteFileButton.Size = new Size(halfW, actionRowH);
            _pasteFileButton.Location = new Point(margin, actionRowY);
            _usbSyncButton.Font = Font12;
            _usbSyncButton.Size = new Size(rightHalfW, actionRowH);
            _usbSyncButton.Location = new Point(margin + halfW + actionGap, actionRowY);

            _openFolderButton.Font = Font13Bold;
            _openFolderButton.Size = new Size(fullContent, M(34));
            _openFolderButton.Padding = new Padding(M(22), 0, M(26), 0);
            _openFolderButton.Location = new Point(margin, M(66));
            _rootLabel.Font = Font11;
            _rootLabel.Size = new Size(fullContent, M(18));
            _rootLabel.Location = new Point(margin, M(104));
        }

        private int ComputePlanCardHeight()
        {
            if (_session != null && _session.PlanModeEnabled)
            {
                int padY = M(8);
                int row2Y = padY + M(22);
                int countH = _planCountLabel != null
                    ? (_planCountLabel.PreferredHeight > 0 ? _planCountLabel.PreferredHeight : _planCountLabel.Height)
                    : 0;
                int barY = row2Y + Math.Max(M(24), countH + M(2));
                int trackH = Math.Max(4, M(6));
                int actionY = barY + trackH + M(8);
                int actionH = M(22);
                return Math.Max(M(100), actionY + actionH + padY);
            }
            return M(36);
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
            int headerH = M(22);
            int quickH = ComputeQuickLinksHeight();
            int planH = ComputePlanCardHeight();
            int credH = ComputeCredentialsZoneHeight();
            int devH = ComputeDevicesZoneHeight();

            int y = M(8);
            y += headerH + M(4) + quickH + M(10);
            y += planH + M(10);
            y += headerH + (credH > 0 ? M(4) + credH : 0) + M(10);
            y += headerH + (devH > 0 ? M(4) + devH : 0) + M(10);
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
            int y = M(8);
            int headerH = M(22);

            int quickHeaderY = y;
            _quickLinkHeaderPanel.Size = new Size(hostWidth, headerH);
            _quickLinkHeaderLabel.Location = new Point(margin, M(2));
            _quickLinkSubLabel.Location = new Point(Math.Max(margin, hostWidth - margin - _quickLinkSubLabel.Width), M(3));
            y += headerH + M(4);

            int quickH = ComputeQuickLinksHeight();
            int quickZoneY = y;
            _quickLinkZonePanel.Size = new Size(hostWidth, quickH);
            LayoutTwoColumnControls(_quickLinkControls, _quickLinkZonePanel, innerWidth,
                M(PanelLayout.SchemeBQuickLinkHeight), M(PanelLayout.SchemeBQuickLinkGap));
            y += quickH + M(10);

            int planH = ComputePlanCardHeight();
            int planCardY = y;
            _planCardPanel.Size = new Size(innerWidth, planH);
            LayoutPlanCardInner(innerWidth);
            y += planH + M(10);

            int credHeaderY = y;
            _credentialHeaderPanel.Size = new Size(hostWidth, headerH);
            _credentialHeaderLabel.Location = new Point(margin, M(2));
            _credentialArrowLabel.Location = new Point(hostWidth - margin - _credentialArrowLabel.Width, M(4));
            PositionCredentialSubLabel();
            y += headerH;

            int credH = ComputeCredentialsZoneHeight();
            if (credH > 0) y += M(4);
            int credZoneY = y;
            _credentialZonePanel.Size = new Size(hostWidth, credH);
            LayoutTwoColumnControls(_credentialControls, _credentialZonePanel, innerWidth,
                M(PanelLayout.SchemeBCredentialHeight), M(PanelLayout.SchemeBCredentialGap));
            y += credH + M(10);

            int devHeaderY = y;
            _deviceHeaderPanel.Size = new Size(hostWidth, headerH);
            _deviceHeaderLabel.Location = new Point(margin, M(2));
            _deviceArrowLabel.Location = new Point(hostWidth - margin - _deviceArrowLabel.Width, M(4));
            _deviceSubLabel.Location = new Point(
                Math.Max(_deviceHeaderLabel.Right + M(8), _deviceArrowLabel.Left - _deviceSubLabel.Width - M(6)),
                M(3));
            y += headerH;

            int devH = ComputeDevicesZoneHeight();
            if (devH > 0) y += M(4);
            int devZoneY = y;
            _deviceZonePanel.Size = new Size(hostWidth, devH);
            LayoutDeviceZoneInner(innerWidth);
            y += devH + M(10);

            _scrollHost.SuspendLayout();
            try
            {
                int desiredScrollY = Math.Max(0, -_scrollHost.AutoScrollPosition.Y);
                _scrollHost.Location = new Point(0, titleHeight);
                _scrollHost.Size = new Size(w, scrollHeight);
                _scrollHost.AutoScrollMinSize = new Size(0, y);
                int maxScrollY = Math.Max(0, y - _scrollHost.ClientSize.Height);
                int clampedScrollY = Math.Max(0, Math.Min(desiredScrollY, maxScrollY));
                if (-_scrollHost.AutoScrollPosition.Y != clampedScrollY || _scrollHost.AutoScrollPosition.X != 0)
                    _scrollHost.AutoScrollPosition = new Point(0, clampedScrollY);

                int offsetX = _scrollHost.AutoScrollPosition.X;
                int offsetY = _scrollHost.AutoScrollPosition.Y;

                // _scrollHost 为 AutoScroll 容器：重排其直接子控件必须计入当前滚动偏移，
                // 嵌套子容器内部（AutoScroll=false）保持相对自身局部坐标。
                _quickLinkHeaderPanel.Location = new Point(offsetX, quickHeaderY + offsetY);
                _quickLinkZonePanel.Location = new Point(offsetX, quickZoneY + offsetY);
                _planCardPanel.Location = new Point(margin + offsetX, planCardY + offsetY);
                _credentialHeaderPanel.Location = new Point(offsetX, credHeaderY + offsetY);
                _credentialZonePanel.Location = new Point(offsetX, credZoneY + offsetY);
                _deviceHeaderPanel.Location = new Point(offsetX, devHeaderY + offsetY);
                _deviceZonePanel.Location = new Point(offsetX, devZoneY + offsetY);
            }
            finally
            {
                _scrollHost.ResumeLayout(true);
            }
            _bottomPanel.Location = new Point(0, ClientSize.Height - bottomHeight);
        }

        private void LayoutPlanCardInner(int cardWidth)
        {
            int leftPad = M(14);
            int rightPad = M(12);
            int padY = M(8);
            int innerW = Math.Max(M(120), cardWidth - leftPad - rightPad);

            _planTitleLabel.Location = new Point(leftPad, padY + M(2));
            int toggleW = M(54);
            int toggleH = M(20);
            _planToggleButton.Size = new Size(toggleW, toggleH);
            _planToggleButton.Location = new Point(cardWidth - rightPad - toggleW, padY);
            if (_planRecoveryButton != null && _planRecoveryButton.Visible)
            {
                int recW = M(72);
                _planRecoveryButton.Size = new Size(recW, toggleH);
                _planRecoveryButton.Location = new Point(Math.Max(leftPad, _planToggleButton.Left - recW - M(6)), padY);
            }

            bool enabled = _session != null && _session.PlanModeEnabled;
            _planProgressLabel.Visible = enabled;
            _planCountLabel.Visible = enabled;
            _planTrackPanel.Visible = enabled;
            _planActionPanel.Visible = enabled;
            _planDisabledHintLabel.Visible = false;

            if (!enabled)
                return;

            int row2Y = padY + M(22);
            int countH = _planCountLabel.PreferredHeight > 0 ? _planCountLabel.PreferredHeight : _planCountLabel.Height;
            int countW = _planCountLabel.PreferredWidth > 0 ? _planCountLabel.PreferredWidth : _planCountLabel.Width;
            int progressH = _planProgressLabel.PreferredHeight > 0 ? _planProgressLabel.PreferredHeight : _planProgressLabel.Height;
            _planCountLabel.Location = new Point(Math.Max(leftPad, cardWidth - rightPad - countW), row2Y);
            _planProgressLabel.Location = new Point(leftPad, row2Y + Math.Max(0, (countH - progressH) / 2));

            int barY = row2Y + Math.Max(M(24), countH + M(2));
            int trackH = Math.Max(4, M(6));
            _planTrackPanel.Size = new Size(innerW, trackH);
            _planTrackPanel.Location = new Point(leftPad, barY);
            _planTrackPanel.Invalidate();
            int total = _session.Plan.TotalCount;
            int visited = _session.Plan.VisitedCount;
            int fillW = total > 0 ? (int)Math.Round(innerW * ((double)visited / total)) : 0;
            if (visited > 0 && fillW < trackH) fillW = trackH;
            _planFillPanel.Size = new Size(Math.Min(innerW, fillW), trackH);

            int actionY = barY + trackH + M(8);
            _planActionPanel.Size = new Size(innerW, M(22));
            _planActionPanel.Location = new Point(leftPad, actionY);
            int resetW = M(68);
            int showW = M(82);
            _resetRoundButton.Size = new Size(resetW, M(20));
            _resetRoundButton.Location = new Point(innerW - resetW, 0);
            _showVisitedButton.Visible = _session.HideVisitedDevices;
            if (_showVisitedButton.Visible)
            {
                _showVisitedButton.Size = new Size(showW, M(20));
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
                int btnW = M(56);
                int btnH = M(20);
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
            UpdateWaitingHookSelfBounds();
        }

        private void UpdateWaitingHookSelfBounds()
        {
            if (_hooks != null && !_hooksDisposed && !IsDisposed
                && _stateMachine != null && _stateMachine.State == PanelState.Waiting)
            {
                _hooks.UpdateSelfBounds(Bounds);
            }
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
            string progressErr = null;
            if (_session != null)
                _session.ReloadConfig(_config, out progressErr);
            if (autoClearChanged)
                RebuildClipboardGuard();
            ApplyScaleAndRebuild(true, true);
            if (load.Kind == ConfigLoadKind.CorruptBackedUp)
            {
                string msg = "配置损坏已备份并恢复默认：" + (load.BackupPath ?? "备份路径未知");
                if (!string.IsNullOrEmpty(progressErr))
                    msg += "；进度保存失败：" + progressErr;
                ShowPersistentFeedback(msg, ErrorColor);
            }
            else if (!string.IsNullOrEmpty(progressErr) || (_session != null && _session.HasProgressError))
            {
                string err = !string.IsNullOrEmpty(progressErr) ? progressErr : _session.ProgressError;
                ShowPersistentFeedback("配置已重新读取，但进度保存失败：" + err, ErrorColor);
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
            {
                _clipboardGuard.Cleared -= OnClipboardCleared;
                _clipboardGuard.ClearSkipped -= OnClipboardClearSkipped;
                _clipboardGuard.Dispose();
            }
            _copiedCredentialTitle = null;
            _credentialCountdownSeconds = 0;
            if (_credentialCountdownTimer != null)
                _credentialCountdownTimer.Stop();
            _clipboardGuard = new ClipboardGuard(_clipboardGateway, _config.AutoClearClipboard);
            _clipboardGuard.Cleared += OnClipboardCleared;
            _clipboardGuard.ClearSkipped += OnClipboardClearSkipped;
            _credentialService = new CredentialService(_protector, _clipboardGuard);
            if (_credentialHeaderPanel != null)
            {
                RefreshCredentialButtonVisuals();
                UpdateCredentialHeader();
            }
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
            RebindCredentialHotkeys();

            if (_configWriteProtected)
            {
                ShowConfigLoadErrorFeedback();
            }
            else if (_deviceListError != null)
            {
                ShowDeviceListErrorFeedback();
            }
            else if (_session != null && _session.HasProgressError)
            {
                ShowProgressErrorFeedback();
            }
            else if (!string.IsNullOrEmpty(_hotkeyWarning))
            {
                ShowFeedback(_hotkeyWarning, true);
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
                    PaintExternalLinkIcon(e.Graphics, btn.Width, btn.Height, WeakTextColor);
                };
                button.Click += OnQuickLinkButtonClicked;
                _quickLinkControls.Add(button);
                _quickLinkZonePanel.Controls.Add(button);
                if (!string.IsNullOrEmpty(entry.Url))
                    _deviceToolTip.SetToolTip(button, entry.Url);
            }
        }

        private void PaintCollapseChevron(Graphics g, int width, int height, Color color, bool collapsed)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float stroke = Math.Max(1.3f, (float)(1.4 * ScaleFactor));
            float cx = width / 2f;
            float cy = height / 2f;
            float arm = Math.Max(2.5f, (float)(3.0 * ScaleFactor));
            using (var pen = new Pen(color, stroke))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                if (collapsed)
                {
                    g.DrawLines(pen, new[]
                    {
                        new PointF(cx - arm * 0.5f, cy - arm),
                        new PointF(cx + arm * 0.6f, cy),
                        new PointF(cx - arm * 0.5f, cy + arm)
                    });
                }
                else
                {
                    g.DrawLines(pen, new[]
                    {
                        new PointF(cx - arm, cy - arm * 0.45f),
                        new PointF(cx, cy + arm * 0.6f),
                        new PointF(cx + arm, cy - arm * 0.45f)
                    });
                }
            }
        }

        private void PaintExternalLinkIcon(Graphics g, int width, int height, Color color)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float box = Math.Max(8f, M(9));
            float right = width - M(8);
            float left = right - box;
            float top = (height - box) / 2f;
            float bottom = top + box;
            float stroke = Math.Max(1.15f, (float)(1.25 * ScaleFactor));
            using (var pen = new Pen(color, stroke))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                float midX = left + box * 0.5f;
                float midY = top + box * 0.5f;
                // 简洁外链方框缺口 + 右上箭头
                g.DrawLines(pen, new[]
                {
                    new PointF(midX - 0.5f, top + 1f),
                    new PointF(left, top + 1f),
                    new PointF(left, bottom),
                    new PointF(right - 1f, bottom),
                    new PointF(right - 1f, midY + 0.5f)
                });
                g.DrawLine(pen, midX - 0.5f, midY + 0.5f, right, top);
                g.DrawLines(pen, new[]
                {
                    new PointF(midX + 1f, top),
                    new PointF(right, top),
                    new PointF(right, midY - 1f)
                });
            }
        }

        private void PaintFolderOrExternalIcon(Graphics g, int width, int height, Color color, bool isWarning)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int size = Math.Max(10, M(11));
            int right = width - M(14);
            int left = right - size;
            int top = (height - size) / 2;
            int bottom = top + size;
            int midY = top + size / 2;
            float stroke = Math.Max(1.4f, (float)(1.5 * ScaleFactor));
            using (var pen = new Pen(color, stroke))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                if (isWarning)
                {
                    // 警示感叹号圈（使用 FillEllipse 画底部圆点，避免 0 长度线段在不同 GDI+ 缩放下丢失）
                    g.DrawEllipse(pen, left, top, size, size);
                    float cx = left + size / 2f;
                    g.DrawLine(pen, cx, top + M(2), cx, bottom - Math.Max(3.5f, M(4)));
                    float dotD = Math.Max(1.8f, (float)(1.8 * ScaleFactor));
                    using (var dotBrush = new SolidBrush(color))
                    {
                        g.FillEllipse(dotBrush, cx - dotD / 2f, bottom - dotD - Math.Max(1f, M(1)), dotD, dotD);
                    }
                }
                else
                {
                    // 右指箭头（表示打开外部目录）
                    g.DrawLine(pen, left, midY, right, midY);
                    int head = Math.Max(3, M(4));
                    g.DrawLines(pen, new[]
                    {
                        new Point(right - head, midY - head),
                        new Point(right, midY),
                        new Point(right - head, midY + head)
                    });
                }
            }
        }

        private void RebuildPlanCard()
        {
            if (_session == null || _planToggleButton == null)
                return;

            bool enabled = _session.PlanModeEnabled;
            _planToggleButton.Checked = enabled;
            _planToggleButton.Text = enabled ? "已开启" : "已关闭";
            _planToggleButton.BackColor = enabled ? AccentSoftColor : SurfaceColor;
            _planToggleButton.ForeColor = enabled ? AccentColor : SecondaryTextColor;
            _planToggleButton.FlatAppearance.BorderColor = enabled ? UiTheme.AccentBorderColor : BorderColor;

            int visited = _session.Plan.VisitedCount;
            int total = _session.Plan.TotalCount;
            bool complete = enabled && total > 0 && visited >= total;

            _planProgressLabel.Text = "已点过";
            _planCountLabel.Text = visited + " / " + total;
            _planCountLabel.ForeColor = complete ? SuccessColor : AccentColor;
            _planFillPanel.BackColor = complete ? SuccessColor : AccentColor;
            _planTrackPanel.Invalidate();
            _planCardPanel.Invalidate();

            _showVisitedButton.Text = _session.ShowVisitedOverride ? "只看未点过" : "查看已点过";
            _showVisitedButton.Visible = enabled && _session.HideVisitedDevices;
            _resetRoundButton.Enabled = enabled && (visited > 0 || HasSavedPaths());
            if (_planRecoveryButton != null)
            {
                _planRecoveryButton.Visible = _session.HasProgressError;
                _planRecoveryButton.Text = _session.IsProgressProtected ? "恢复进度" : "重试保存";
                _rootToolTip.SetToolTip(_planRecoveryButton, _session.ProgressError ?? string.Empty);
            }
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
            _deviceDisplay = _session != null
                ? _session.BuildDeviceDisplay()
                : new DeviceDisplaySnapshot
                {
                    Items = new List<DeviceDisplayItem>(),
                    EmptyKind = DeviceListEmptyKind.None
                };
            if (_session != null)
            {
                _uiRenderedPlanDate = _session.Plan.Date;
                _session.ConsumePendingRolloverRefresh();
                _pendingDayRolloverRefresh = false;
            }

            if (!_config.IsDeviceCollapsed && _deviceHint == null && CanReuseDeviceRows(_deviceDisplay))
            {
                for (int i = 0; i < _deviceRows.Count; i++)
                    _deviceRows[i].Visited = _deviceDisplay.Items[i].Visited;
                RefreshStateVisuals();
                return;
            }

            if (_deviceRowToolTip != null)
                _deviceRowToolTip.RemoveAll();
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

        private bool CanReuseDeviceRows(DeviceDisplaySnapshot display)
        {
            if (display == null || display.Items == null || display.Items.Count == 0)
                return false;
            if (_deviceRows.Count != display.Items.Count)
                return false;
            int expectedHeight = M(PanelLayout.SchemeBDeviceRowHeight);
            for (int i = 0; i < _deviceRows.Count; i++)
            {
                DeviceRowWidget row = _deviceRows[i];
                if (row == null || row.Panel == null || row.Panel.IsDisposed)
                    return false;
                if (row.Panel.Height != expectedHeight)
                    return false;
                if (!string.Equals(row.Device, display.Items[i].Name, StringComparison.Ordinal))
                    return false;
            }
            return true;
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
                using (var pen = new Pen(UiTheme.SuccessBorderColor, 1f))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
                }
                using (var bar = new SolidBrush(SuccessColor))
                {
                    e.Graphics.FillRectangle(bar, 0, 0, Math.Max(3, M(3)), card.Height);
                }
            };
            var label = new Label
            {
                Text = "本轮已全部点过",
                Font = Font13Bold,
                ForeColor = SuccessColor,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            };
            card.Controls.Add(label);
            return card;
        }

        private void UpdateDeviceRowBackColor(DeviceRowWidget widget)
        {
            if (widget == null || widget.Panel == null)
                return;
            bool waitingTarget = _stateMachine.State == PanelState.Waiting
                && string.Equals(widget.Device, _stateMachine.CurrentDevice, StringComparison.OrdinalIgnoreCase);
            bool capturingTarget = _stateMachine.State == PanelState.Capturing
                && string.Equals(widget.Device, _stateMachine.CurrentDevice, StringComparison.OrdinalIgnoreCase);
            if (waitingTarget || capturingTarget)
            {
                widget.Panel.BackColor = widget.BaseBackColor;
                return;
            }
            widget.Panel.BackColor = widget.Hovered && widget.Panel.Enabled
                ? UiTheme.PageBackColor
                : widget.BaseBackColor;
        }

        private DeviceRowWidget CreateDeviceRowWidget(DeviceDisplayItem item)
        {
            string device = item.Name;
            var rowPanel = new DeviceRowPanel
            {
                BackColor = ButtonBackColor,
                Size = new Size(ContentWidthNow, M(PanelLayout.SchemeBDeviceRowHeight)),
                Cursor = Cursors.Hand,
                AccessibleName = "设备：" + device,
                ActivateAction = delegate { OnDeviceRowActivated(device); },
                NavigateAction = delegate(int delta) { FocusAdjacentDeviceRow(device, delta); }
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
                Text = item.Visited ? "已点过" : "待截图",
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
                BackColor = PanelBackColor,
                ForeColor = SecondaryTextColor,
                Visible = item.Visited,
                Cursor = Cursors.Hand,
                AccessibleName = "恢复未点：" + device
            };
            unmarkBtn.FlatAppearance.BorderColor = BorderSoftColor;
            unmarkBtn.FlatAppearance.BorderSize = 1;
            unmarkBtn.FlatAppearance.MouseOverBackColor = SurfaceColor;

            var widget = new DeviceRowWidget
            {
                Device = device,
                Visited = item.Visited,
                Panel = rowPanel,
                NameLabel = nameLabel,
                StatusLabel = statusLabel,
                UnmarkButton = unmarkBtn
            };

            EventHandler onEnter = delegate
            {
                if (!widget.Hovered)
                {
                    widget.Hovered = true;
                    UpdateDeviceRowBackColor(widget);
                }
            };
            EventHandler onLeave = delegate
            {
                if (!rowPanel.ClientRectangle.Contains(rowPanel.PointToClient(Cursor.Position)))
                {
                    if (widget.Hovered)
                    {
                        widget.Hovered = false;
                        UpdateDeviceRowBackColor(widget);
                    }
                }
            };

            rowPanel.Tag = widget;
            rowPanel.Paint += OnDeviceRowPaint;
            rowPanel.MouseEnter += onEnter;
            rowPanel.MouseLeave += onLeave;
            nameLabel.MouseEnter += onEnter;
            nameLabel.MouseLeave += onLeave;
            statusLabel.MouseEnter += onEnter;
            statusLabel.MouseLeave += onLeave;
            MouseEventHandler onRowMouseClick = delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                {
                    rowPanel.Focus();
                    OnDeviceRowActivated(device);
                }
                else if (e.Button == MouseButtons.Right && _stateMachine.State == PanelState.Idle)
                {
                    rowPanel.Focus();
                    ShowDeviceRowContextMenu(widget, sender as Control, e.Location);
                }
            };
            rowPanel.MouseClick += onRowMouseClick;
            nameLabel.MouseClick += onRowMouseClick;
            statusLabel.MouseClick += onRowMouseClick;
            unmarkBtn.Click += delegate { OnUnmarkDeviceClicked(device); };

            rowPanel.Controls.Add(nameLabel);
            rowPanel.Controls.Add(statusLabel);
            rowPanel.Controls.Add(unmarkBtn);
            if (_deviceRowToolTip != null)
            {
                _deviceRowToolTip.SetToolTip(rowPanel, device);
                _deviceRowToolTip.SetToolTip(nameLabel, device);
            }
            return widget;
        }

        private void FocusAdjacentDeviceRow(string currentDevice, int delta)
        {
            if (_deviceRows.Count <= 1)
                return;
            int index = -1;
            for (int i = 0; i < _deviceRows.Count; i++)
            {
                if (string.Equals(_deviceRows[i].Device, currentDevice, StringComparison.Ordinal))
                {
                    index = i;
                    break;
                }
            }
            if (index < 0)
                return;
            int next = (index + delta + _deviceRows.Count) % _deviceRows.Count;
            Panel target = _deviceRows[next].Panel;
            if (target != null && !target.IsDisposed && target.CanSelect)
            {
                target.Select();
                if (target.CanFocus)
                    target.Focus();
            }
        }

        private void ShowDeviceRowContextMenu(DeviceRowWidget widget, Control sourceControl, Point location)
        {
            if (widget == null || _deviceRowMenu == null || IsDisposed)
                return;
            string device = widget.Device;
            _deviceRowMenu.Items.Clear();

            var captureItem = new ToolStripMenuItem("截取「" + device + "」");
            captureItem.Enabled = ScreenshotAvailable;
            captureItem.Click += delegate { OnDeviceRowActivated(device); };
            _deviceRowMenu.Items.Add(captureItem);

            if (widget.Visited)
            {
                var unmarkItem = new ToolStripMenuItem("恢复未点");
                unmarkItem.Click += delegate { OnUnmarkDeviceClicked(device); };
                _deviceRowMenu.Items.Add(unmarkItem);
            }

            _deviceRowMenu.Items.Add(new ToolStripSeparator());
            var openFolderItem = new ToolStripMenuItem("打开今日巡检目录");
            openFolderItem.Click += delegate { OpenInspectionFolders(); };
            _deviceRowMenu.Items.Add(openFolderItem);

            Control anchor = sourceControl ?? widget.Panel;
            if (anchor != null && !anchor.IsDisposed)
                _deviceRowMenu.Show(anchor, location);
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

            if (waitingTarget || capturingTarget)
            {
                Color activeColor = waitingTarget ? WarningColor : AccentColor;
                using (var pen = new Pen(activeColor, 2f))
                {
                    e.Graphics.DrawRectangle(pen, 1, 1, panel.Width - 3, panel.Height - 3);
                }
                using (var bar = new SolidBrush(activeColor))
                {
                    e.Graphics.FillRectangle(bar, 0, 0, Math.Max(3, M(3)), panel.Height);
                }
            }
            else
            {
                using (var pen = new Pen(BorderColor, 1f))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, panel.Width - 1, panel.Height - 1);
                }
                if (panel.Focused)
                {
                    using (var focusPen = new Pen(UiTheme.AccentBorderColor, 1f))
                    {
                        e.Graphics.DrawRectangle(focusPen, 2, 2, panel.Width - 5, panel.Height - 5);
                    }
                }
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int circleSize = M(16);
            int cx = M(10);
            int cy = (panel.Height - circleSize) / 2;

            if (waitingTarget)
            {
                using (var brush = new SolidBrush(WarningColor))
                    e.Graphics.FillEllipse(brush, cx, cy, circleSize, circleSize);
                int inner = Math.Max(4, M(6));
                using (var brush = new SolidBrush(Color.White))
                    e.Graphics.FillEllipse(brush, cx + (circleSize - inner) / 2, cy + (circleSize - inner) / 2, inner, inner);
            }
            else if (capturingTarget)
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
                using (var pen = new Pen(BorderColor, Math.Max(1f, (float)ScaleFactor)))
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
            }
            RefreshCredentialButtonVisuals();
        }

        private bool IsActiveCopiedCredential(CredentialEntry entry)
        {
            return entry != null
                && !entry.NeedsReset
                && !string.IsNullOrEmpty(_copiedCredentialTitle)
                && string.Equals(entry.Title, _copiedCredentialTitle, StringComparison.OrdinalIgnoreCase);
        }

        private void RefreshCredentialButtonVisuals()
        {
            for (int i = 0; i < _credentialControls.Count; i++)
            {
                var btn = _credentialControls[i] as Button;
                if (btn == null) continue;
                var entry = btn.Tag as CredentialEntry;
                if (entry == null) continue;

                bool active = IsActiveCopiedCredential(entry);
                if (entry.NeedsReset)
                {
                    btn.BackColor = ButtonBackColor;
                    btn.ForeColor = ErrorColor;
                    btn.Font = Font12;
                    btn.FlatAppearance.BorderColor = BorderColor;
                    btn.FlatAppearance.MouseOverBackColor = SurfaceColor;
                }
                else if (active)
                {
                    btn.BackColor = AccentSoftColor;
                    btn.ForeColor = AccentColor;
                    btn.Font = Font12Bold;
                    btn.FlatAppearance.BorderColor = AccentColor;
                    btn.FlatAppearance.MouseOverBackColor = AccentSoftColor;
                }
                else
                {
                    btn.BackColor = ButtonBackColor;
                    btn.ForeColor = PrimaryTextColor;
                    btn.Font = Font12;
                    btn.FlatAppearance.BorderColor = BorderColor;
                    btn.FlatAppearance.MouseOverBackColor = SurfaceColor;
                }
                if (_deviceToolTip != null)
                {
                    string hkSuffix = !string.IsNullOrEmpty(entry.Hotkey) ? " [" + entry.Hotkey + "]" : string.Empty;
                    string tip = active && _config.AutoClearClipboard && _credentialCountdownSeconds > 0
                        ? "已复制「" + entry.Title + "」，" + _credentialCountdownSeconds + " 秒后自动清空" + hkSuffix
                        : (active ? "已复制「" + entry.Title + "」" + hkSuffix : "复制凭据：" + entry.Title + hkSuffix);
                    _deviceToolTip.SetToolTip(btn, tip);
                }
                btn.Invalidate();
            }
        }

        private void PaintCredentialCopyIcon(object sender, PaintEventArgs e)
        {
            var btn = (Button)sender;
            var entry = btn.Tag as CredentialEntry;
            Color ink = entry != null && entry.NeedsReset
                ? ErrorColor
                : (IsActiveCopiedCredential(entry) ? AccentColor : WeakTextColor);
            int iconX = M(8);
            int iconY = (btn.Height - M(12)) / 2;
            int backW = M(7);
            int backH = M(9);
            float stroke = Math.Max(1f, (float)ScaleFactor);
            e.Graphics.SmoothingMode = SmoothingMode.None;
            using (var pen = new Pen(ink, stroke))
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
            if (_credentialArrowLabel != null)
                _credentialArrowLabel.Invalidate();
            if (_credentialSubLabel == null)
                return;
            if (!string.IsNullOrEmpty(_copiedCredentialTitle))
            {
                if (_config.AutoClearClipboard)
                {
                    _credentialSubLabel.Text = _credentialCountdownSeconds > 0
                        ? "已复制，" + _credentialCountdownSeconds + " 秒后清空"
                        : "正在清空剪贴板…";
                }
                else
                {
                    _credentialSubLabel.Text = "已复制（自动清空已关闭）";
                }
                _credentialSubLabel.ForeColor = AccentColor;
            }
            else
            {
                _credentialSubLabel.Text = string.Empty;
                _credentialSubLabel.ForeColor = WeakTextColor;
            }
            PositionCredentialSubLabel();
        }

        private void PositionCredentialSubLabel()
        {
            if (_credentialSubLabel == null || _credentialHeaderLabel == null || _credentialArrowLabel == null)
                return;
            _credentialSubLabel.Location = new Point(
                Math.Max(_credentialHeaderLabel.Right + M(8), _credentialArrowLabel.Left - _credentialSubLabel.Width - M(6)),
                M(3));
        }

        private void UpdateDeviceHeader()
        {
            if (_deviceArrowLabel != null)
                _deviceArrowLabel.Invalidate();
            PositionDeviceSubLabel();
        }

        private void PositionDeviceSubLabel()
        {
            if (_deviceSubLabel == null || _deviceHeaderLabel == null || _deviceArrowLabel == null)
                return;
            _deviceSubLabel.Location = new Point(
                Math.Max(_deviceHeaderLabel.Right + M(8), _deviceArrowLabel.Left - _deviceSubLabel.Width - M(6)),
                M(3));
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
                if (_rootToolTip != null)
                    _rootToolTip.SetToolTip(_rootLabel, _configLoadError ?? string.Empty);
                _openFolderButton.Invalidate();
                return;
            }
            _openFolderButton.Text = "打开巡检文件夹";
            _rootLabel.Text = string.IsNullOrEmpty(_config.RootPath)
                ? "尚未设置保存目录"
                : _config.RootPath;
            _rootLabel.ForeColor = WeakTextColor;
            if (_rootToolTip != null)
            {
                _rootToolTip.SetToolTip(_rootLabel, string.IsNullOrEmpty(_config.RootPath) ? string.Empty : _config.RootPath);
                UpdateUsbSyncToolTip();
            }
            _openFolderButton.Invalidate();
        }

        private void UpdateUsbSyncToolTip()
        {
            if (_rootToolTip == null || _usbSyncButton == null || _config == null)
                return;
            string tip = string.IsNullOrEmpty(_config.UsbSyncRootPath)
                ? "设置 U盘目录并同步今日巡检文件"
                : "从U盘同步今日文件（" + _config.UsbSyncRootPath + "），右键可重设";
            _rootToolTip.SetToolTip(_usbSyncButton, tip);
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
            if (ShowScaledDialog(delegate { return new ResetRoundConfirmDialog(); }) != DialogResult.Yes)
                return;
            string progressErr;
            _session.ResetRound(out progressErr);
            RebuildPlanCard();
            RebuildDeviceButtons();
            RefreshStateVisuals();
            ApplyHeightAnchored();
            if (!string.IsNullOrEmpty(progressErr))
                ShowPersistentFeedback("进度保存失败：" + progressErr, ErrorColor);
            else if (!_configWriteProtected && _deviceListError == null)
                ShowSuccessFeedback("已重置本轮巡检进度");
        }

        private void OnPlanRecoveryClicked(object sender, EventArgs e)
        {
            HandleProgressRecoveryAction();
        }

        private void HandleProgressRecoveryAction()
        {
            if (_session == null || !_session.HasProgressError)
                return;

            string err;
            if (!_session.IsProgressProtected)
            {
                if (_session.RetrySaveProgress(out err))
                {
                    RebuildPlanCard();
                    ApplyHeightAnchored();
                    RestoreOrClearAfterProgressRecovery();
                    ShowSuccessFeedback("巡检进度已重新保存");
                }
                else
                {
                    RebuildPlanCard();
                    ApplyHeightAnchored();
                    ShowProgressErrorFeedback();
                }
                return;
            }

            _settingsDialogOpen = true;
            try
            {
                string path = _progressStore != null ? _progressStore.Path : null;
                string progressErr = _session.ProgressError;
                DialogResult choice = ShowScaledDialog(delegate { return new ProgressRecoveryDialog(path, progressErr); });
                if (choice == DialogResult.Retry)
                {
                    if (_session.RetryLoadProgress(out err))
                    {
                        RefreshConfigState();
                        ApplyHeightAnchored();
                        ShowSuccessFeedback("已恢复读取巡检进度");
                    }
                    else
                    {
                        RebuildPlanCard();
                        ApplyHeightAnchored();
                        ShowProgressErrorFeedback();
                    }
                }
                else if (choice == DialogResult.Yes)
                {
                    string backupPath;
                    if (_session.BackupAndResetProgress(out backupPath, out err))
                    {
                        RefreshConfigState();
                        ApplyHeightAnchored();
                        ShowPersistentFeedback(
                            string.IsNullOrEmpty(backupPath)
                                ? "已重置本轮巡检进度"
                                : "坏进度已备份至：" + backupPath + "（已重置新轮）",
                            WarningColor);
                    }
                    else
                    {
                        RebuildPlanCard();
                        ApplyHeightAnchored();
                        ShowProgressErrorFeedback();
                    }
                }
            }
            finally
            {
                _settingsDialogOpen = false;
            }
        }

        private void RestoreOrClearAfterProgressRecovery()
        {
            if (!RestorePersistentFeedbackIfNeeded())
            {
                _feedbackPersistent = false;
                _feedbackTimer.Stop();
                _feedbackLabel.Text = string.Empty;
            }
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
                    ShowPersistentFeedback("进度保存失败：" + progressErr, ErrorColor);
                else if (!RestorePersistentFeedbackIfNeeded())
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

            if (_stateMachine.State == PanelState.Waiting)
            {
                if (_session != null)
                {
                    // 仅绑定当前等待轮次令牌；不在点下设备时提前计入已点过，
                    // 只有真正截图并保存成功后才计入，确保右键/Esc/超时取消均不改变今日未点条数。
                    _waitingPlanToken = token;
                }
                OnStateChanged();
                if (!string.IsNullOrEmpty(progressErr))
                {
                    ShowPersistentFeedback("请点目标窗口（进度落盘失败：" + progressErr + "）", ErrorColor);
                }
                else
                {
                    ShowFeedback(
                        "请点目标窗口截取「" + _stateMachine.CurrentDevice + "」（右键、Esc 或再点取消）",
                        false);
                }
                return;
            }

            // 再次点击当前设备取消回 Idle（与 Esc/右键取消路径保持一致，不主动抢焦点）
            _waitingPlanToken = default(InspectionPlanToken);
            OnStateChanged();
            SyncRolloverUiIfIdle();
            ShowFeedback("已取消选窗", false);
            TryMaybeShowCompletionPrompt();
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
            CopyCredentialEntry(entry, false);
        }

        private void RebindCredentialHotkeys()
        {
            if (!IsHandleCreated || IsDisposed || _config == null || _hotkeyManager == null)
                return;
            List<HotkeyBindingFailure> failures = _hotkeyManager.Bind(Handle, _config.Credentials);
            _hotkeyWarning = failures.Count > 0 ? failures[0].Error : null;
        }

        private void OnCredentialHotkeyTriggered(int credentialIndex)
        {
            if (_formClosing || IsDisposed || _config == null || _config.Credentials == null)
                return;
            if (credentialIndex < 0 || credentialIndex >= _config.Credentials.Count)
                return;
            CopyCredentialEntry(_config.Credentials[credentialIndex], true);
        }

        private void CopyCredentialEntry(CredentialEntry entry, bool alsoPaste)
        {
            if (entry == null || _credentialService == null)
                return;

            CopyResult result = _credentialService.Copy(entry);
            switch (result)
            {
                case CopyResult.Copied:
                    _copiedCredentialTitle = entry.Title;
                    if (_config.AutoClearClipboard)
                    {
                        _credentialCountdownSeconds = _clipboardGuard != null ? _clipboardGuard.ClearAfterSeconds : 30;
                        if (_credentialCountdownTimer != null)
                        {
                            _credentialCountdownTimer.Stop();
                            _credentialCountdownTimer.Start();
                        }
                        if (!alsoPaste)
                            ShowSuccessFeedback("已复制「" + entry.Title + "」（" + _credentialCountdownSeconds + " 秒后清空）");
                    }
                    else
                    {
                        _credentialCountdownSeconds = 0;
                        if (_credentialCountdownTimer != null)
                            _credentialCountdownTimer.Stop();
                        if (!alsoPaste)
                            ShowSuccessFeedback("已复制「" + entry.Title + "」");
                    }
                    RefreshCredentialButtonVisuals();
                    UpdateCredentialHeader();

                    if (alsoPaste)
                    {
                        string title = entry.Title;
                        int clearSec = _config.AutoClearClipboard ? _credentialCountdownSeconds : 0;
                        IPasteSimulator simulator = _pasteSimulator;
                        var worker = new System.Threading.Thread(new System.Threading.ThreadStart(delegate
                        {
                            string pasteErr = null;
                            bool pasted = simulator != null && simulator.TryPaste(out pasteErr);
                            if (IsDisposed || _formClosing)
                                return;
                            try
                            {
                                BeginInvoke(new Action(delegate
                                {
                                    if (IsDisposed || _formClosing)
                                        return;
                                    if (pasted)
                                    {
                                        string msg = clearSec > 0
                                            ? "已复制并粘贴「" + title + "」（" + clearSec + " 秒后清空）"
                                            : "已复制并粘贴「" + title + "」";
                                        ShowSuccessFeedback(msg);
                                    }
                                    else
                                    {
                                        ShowFeedback("已复制「" + title + "」，自动粘贴失败：" + (pasteErr ?? "未知错误"), true);
                                    }
                                }));
                            }
                            catch
                            {
                            }
                        }));
                        worker.IsBackground = true;
                        worker.Start();
                    }
                    break;
                case CopyResult.NeedsReset:
                    if (string.Equals(_copiedCredentialTitle, entry.Title, StringComparison.OrdinalIgnoreCase))
                    {
                        _copiedCredentialTitle = null;
                        _credentialCountdownSeconds = 0;
                        if (_credentialCountdownTimer != null)
                            _credentialCountdownTimer.Stop();
                    }
                    entry.NeedsReset = true;
                    RebuildCredentialControls();
                    UpdateCredentialHeader();
                    ApplyHeightAnchored();
                    ShowFeedback("该凭据需重新设置，请到设置中重新填写", true);
                    break;
                default:
                    ShowFeedback("复制失败，请重试", true);
                    break;
            }
        }

        private void OnCredentialCountdownTick(object sender, EventArgs e)
        {
            if (_formClosing || IsDisposed || string.IsNullOrEmpty(_copiedCredentialTitle) || !_config.AutoClearClipboard)
            {
                if (_credentialCountdownTimer != null)
                    _credentialCountdownTimer.Stop();
                return;
            }
            if (_credentialCountdownSeconds > 1)
            {
                _credentialCountdownSeconds--;
            }
            else
            {
                _credentialCountdownSeconds = 0;
                if (_credentialCountdownTimer != null)
                    _credentialCountdownTimer.Stop();
            }
            UpdateCredentialHeader();
            RefreshCredentialButtonVisuals();
        }

        private void OnClipboardCleared()
        {
            if (_formClosing || IsDisposed)
                return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(OnClipboardCleared)); }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
                return;
            }
            if (_formClosing || IsDisposed)
                return;
            _copiedCredentialTitle = null;
            _credentialCountdownSeconds = 0;
            if (_credentialCountdownTimer != null)
                _credentialCountdownTimer.Stop();
            RefreshCredentialButtonVisuals();
            UpdateCredentialHeader();
            if (_stateMachine.State == PanelState.Idle && !_feedbackPersistent)
                ShowFeedback("剪贴板凭据已自动清空", false);
        }

        private void OnClipboardClearSkipped()
        {
            if (_formClosing || IsDisposed)
                return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(OnClipboardClearSkipped)); }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
                return;
            }
            if (_formClosing || IsDisposed)
                return;
            _copiedCredentialTitle = null;
            _credentialCountdownSeconds = 0;
            if (_credentialCountdownTimer != null)
                _credentialCountdownTimer.Stop();
            RefreshCredentialButtonVisuals();
            UpdateCredentialHeader();
            if (_stateMachine.State == PanelState.Idle && !_feedbackPersistent)
                ShowFeedback("剪贴板内容已变更，已跳过自动清空", false);
        }

        private void OnSettingsClicked(object sender, EventArgs e)
        {
            if (_configWriteProtected && !RetryConfigLoad())
                return;

            double systemDpiScale = GetSystemDpiScale();
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
                        string progressErr = null;
                        if (_session != null)
                        {
                            _session.ApplySavedConfig(
                                _config,
                                form.LastAppliedDeviceRenames,
                                form.LastAppliedImportDeviceReplace,
                                out progressErr);
                        }
                        if (autoClearChanged)
                            RebuildClipboardGuard();
                        ApplyScaleAndRebuild(true, true);
                        ApplyConfiguredWindowPositionIfVisible();
                        if (!string.IsNullOrEmpty(progressErr)
                            || (_session != null && _session.HasProgressError))
                        {
                            string err = !string.IsNullOrEmpty(progressErr) ? progressErr : _session.ProgressError;
                            ShowPersistentFeedback("设置已应用，但进度保存失败：" + err, ErrorColor);
                        }
                        else if (_deviceListError == null)
                        {
                            ShowSuccessFeedback("设置已应用");
                        }
                    };
                    form.ShowDialog(this);
                }
            }
            finally
            {
                _settingsDialogOpen = false;
            }
            SyncRolloverUiIfIdle();
            TryMaybeShowCompletionPrompt();
        }

        private void OnRootLabelClicked(object sender, EventArgs e)
        {
            OpenInspectionFolders();
        }

        private void OnPasteClipboardFilesClicked(object sender, EventArgs e)
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

            string[] paths = ReadClipboardFileDropPaths();
            if (paths == null || paths.Length == 0)
            {
                ShowFeedback("剪贴板中没有文件或文件夹", true);
                return;
            }

            string rootPath = _config.RootPath;
            string pattern = _config.FolderNamePattern;
            DateTime now = _clock.Now;
            if (_pasteFileButton != null)
                _pasteFileButton.Enabled = false;

            var worker = new System.Threading.Thread(new System.Threading.ThreadStart(delegate
            {
                ClipboardCopyResult copyResult = _fileTransferService.CopyClipboardItems(
                    paths, rootPath, pattern, now);
                if (IsDisposed || _formClosing)
                    return;
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        if (_pasteFileButton != null && !_pasteFileButton.IsDisposed)
                            _pasteFileButton.Enabled = true;
                        if (IsDisposed || _formClosing)
                            return;
                        if (copyResult.Success)
                        {
                            int total = copyResult.CopiedFileCount + copyResult.CopiedDirectoryCount;
                            ShowSuccessFeedback("已存入今日目录：" + total + " 项");
                        }
                        else
                        {
                            ShowFeedback(copyResult.Error ?? "存入剪贴板文件失败", true);
                        }
                    }));
                }
                catch
                {
                }
            }));
            worker.IsBackground = true;
            worker.Start();
        }

        private static string[] ReadClipboardFileDropPaths()
        {
            try
            {
                if (!Clipboard.ContainsFileDropList())
                    return null;
                System.Collections.Specialized.StringCollection list = Clipboard.GetFileDropList();
                if (list == null || list.Count == 0)
                    return null;
                var paths = new string[list.Count];
                list.CopyTo(paths, 0);
                return paths;
            }
            catch
            {
                return null;
            }
        }

        private void OnUsbSyncClicked(object sender, EventArgs e)
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
            if (string.IsNullOrEmpty(_config.UsbSyncRootPath))
            {
                PromptAndSaveUsbRootPath(true);
                return;
            }
            RunUsbSync();
        }

        private void PromptAndSaveUsbRootPath(bool syncAfterSelect)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择 U盘巡检根目录（例如 D:\\巡检）";
                dialog.ShowNewFolderButton = false;
                if (!string.IsNullOrEmpty(_config.UsbSyncRootPath))
                    dialog.SelectedPath = _config.UsbSyncRootPath;
                if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrEmpty(dialog.SelectedPath))
                    return;

                _config.UsbSyncRootPath = dialog.SelectedPath;
                SaveBestEffort();
                UpdateUsbSyncToolTip();
                if (syncAfterSelect)
                    RunUsbSync();
                else
                    ShowSuccessFeedback("已设置 U盘目录：" + dialog.SelectedPath);
            }
        }

        private void ClearUsbRootPathFromMain()
        {
            if (string.IsNullOrEmpty(_config.UsbSyncRootPath))
                return;
            _config.UsbSyncRootPath = null;
            SaveBestEffort();
            UpdateUsbSyncToolTip();
            ShowSuccessFeedback("已清除 U盘目录");
        }

        private void RunUsbSync()
        {
            string usbRoot = _config.UsbSyncRootPath;
            string targetRoot = _config.RootPath;
            string pattern = _config.FolderNamePattern;
            DateTime now = _clock.Now;
            if (_usbSyncButton != null)
                _usbSyncButton.Enabled = false;

            var worker = new System.Threading.Thread(new System.Threading.ThreadStart(delegate
            {
                UsbSyncResult syncResult = _fileTransferService.SyncFromUsb(
                    usbRoot, targetRoot, pattern, now);
                if (IsDisposed || _formClosing)
                    return;
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        if (_usbSyncButton != null && !_usbSyncButton.IsDisposed)
                            _usbSyncButton.Enabled = true;
                        if (IsDisposed || _formClosing)
                            return;
                        if (syncResult.Status == UsbSyncStatus.Success)
                        {
                            if (syncResult.CopiedFileCount == 0 && syncResult.SkippedFileCount == 0)
                            {
                                ShowFeedback("U盘今日巡检目录为空", false);
                            }
                            else if (syncResult.CopiedFileCount == 0)
                            {
                                ShowSuccessFeedback("U盘文件已全部存在（跳过 " + syncResult.SkippedFileCount + " 个）");
                            }
                            else
                            {
                                string msg = "已从U盘同步 " + syncResult.CopiedFileCount + " 个文件";
                                if (syncResult.SkippedFileCount > 0)
                                    msg += "（跳过 " + syncResult.SkippedFileCount + " 个）";
                                ShowSuccessFeedback(msg);
                            }
                        }
                        else
                        {
                            ShowFeedback(syncResult.Error ?? "U盘同步失败", true);
                        }
                    }));
                }
                catch
                {
                }
            }));
            worker.IsBackground = true;
            worker.Start();
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
            if (_folderMenu != null)
            {
                _folderMenu.Dispose();
                _folderMenu = null;
            }
            var menu = _folderMenu = new ContextMenuStrip();
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
            var worker = new System.Threading.Thread(new System.Threading.ThreadStart(delegate
            {
                string latest = FindLatestPngInDirectory(dir);
                try
                {
                    if (latest != null)
                    {
                        Process.Start("explorer.exe", "/select,\"" + latest + "\"");
                    }
                    else
                    {
                        string err;
                        if (!_folderNavigator.TryOpenFolder(dir, out err))
                            ReportFolderOpenError("打开文件夹失败：" + err);
                    }
                }
                catch (Exception ex)
                {
                    ReportFolderOpenError("打开文件夹失败：" + ex.Message);
                }
            }));
            worker.IsBackground = true;
            worker.Name = "InspectionWidget.OpenFolderWorker";
            worker.Start();
        }

        private static string FindLatestPngInDirectory(string dir)
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
                return null;
            }
            return latest;
        }

        private void ReportFolderOpenError(string message)
        {
            if (_formClosing || IsDisposed)
                return;
            try
            {
                BeginInvoke(new Action(delegate
                {
                    if (!_formClosing && !IsDisposed)
                        ShowFeedback(message, true);
                }));
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
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
            _deviceHeaderLabel.ForeColor = waiting ? WarningColor : (capturing ? AccentColor : SecondaryTextColor);
            if (waiting)
            {
                _deviceSubLabel.Text = _stateMachine.CurrentDevice ?? "等待选窗";
                _deviceSubLabel.ForeColor = WarningColor;
            }
            else if (capturing)
            {
                _deviceSubLabel.Text = _stateMachine.CurrentDevice ?? "截图中";
                _deviceSubLabel.ForeColor = AccentColor;
            }
            else if (_session != null && _session.PlanModeEnabled && _session.Plan.TotalCount > 0)
            {
                int remaining = Math.Max(0, _session.Plan.TotalCount - _session.Plan.VisitedCount);
                _deviceSubLabel.Text = remaining == 0 ? "今日已全点" : ("待点 " + remaining + " 台");
                _deviceSubLabel.ForeColor = remaining == 0 ? SuccessColor : WeakTextColor;
            }
            else
            {
                _deviceSubLabel.Text = string.Empty;
                _deviceSubLabel.ForeColor = WeakTextColor;
            }
            PositionDeviceSubLabel();

            for (int i = 0; i < _deviceRows.Count; i++)
            {
                DeviceRowWidget row = _deviceRows[i];
                bool isWaitingTarget = waiting && string.Equals(row.Device, _stateMachine.CurrentDevice, StringComparison.OrdinalIgnoreCase);
                bool isCapturingTarget = capturing && string.Equals(row.Device, _stateMachine.CurrentDevice, StringComparison.OrdinalIgnoreCase);

                if (isWaitingTarget)
                {
                    row.BaseBackColor = WarningSoftColor;
                    row.NameLabel.ForeColor = WarningColor;
                    row.NameLabel.Font = Font13Bold;
                    row.StatusLabel.Text = "等待选窗";
                    row.StatusLabel.ForeColor = WarningColor;
                }
                else if (isCapturingTarget)
                {
                    row.BaseBackColor = AccentSoftColor;
                    row.NameLabel.ForeColor = AccentColor;
                    row.NameLabel.Font = Font13Bold;
                    row.StatusLabel.Text = "截图中";
                    row.StatusLabel.ForeColor = AccentColor;
                }
                else if (row.Visited)
                {
                    row.BaseBackColor = UiTheme.VisitedBackColor;
                    row.NameLabel.ForeColor = ScreenshotAvailable ? PrimaryTextColor : WeakTextColor;
                    row.NameLabel.Font = Font13;
                    row.StatusLabel.Text = "已点过";
                    row.StatusLabel.ForeColor = SuccessColor;
                }
                else if (!waiting && !capturing
                    && string.Equals(row.Device, _config.LastDevice, StringComparison.OrdinalIgnoreCase))
                {
                    row.BaseBackColor = UiTheme.LastDeviceBackColor;
                    row.NameLabel.ForeColor = ScreenshotAvailable ? PrimaryTextColor : WeakTextColor;
                    row.NameLabel.Font = Font13;
                    row.StatusLabel.Text = "待截图";
                    row.StatusLabel.ForeColor = WeakTextColor;
                }
                else
                {
                    row.BaseBackColor = ButtonBackColor;
                    row.NameLabel.ForeColor = ScreenshotAvailable ? PrimaryTextColor : WeakTextColor;
                    row.NameLabel.Font = Font13;
                    row.StatusLabel.Text = "待截图";
                    row.StatusLabel.ForeColor = WeakTextColor;
                }

                row.UnmarkButton.Visible = row.Visited && !isWaitingTarget && !isCapturingTarget;
                row.Panel.Enabled = ScreenshotAvailable;
                UpdateDeviceRowBackColor(row);
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
                    StartTargetHoverHighlight();
                }
                catch (Exception ex)
                {
                    StopTargetHoverHighlight();
                    _waitingSessionId = 0;
                    _waitingPlanToken = default(InspectionPlanToken);
                    _stateMachine.Cancel();
                    RefreshStateVisuals();
                    _hooks.Stop();
                    ShowFeedback("选窗启动失败：" + ex.Message, true);
                }
            }
            else
            {
                StopTargetHoverHighlight();
                _hooks.Stop();
            }
        }

        private IntPtr GetTargetOverlayHandle()
        {
            return _targetOverlay != null && !_targetOverlay.IsDisposed && _targetOverlay.IsHandleCreated
                ? _targetOverlay.Handle
                : IntPtr.Zero;
        }

        private void StartTargetHoverHighlight()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT
                || !(_hooks is HookManager)
                || IsDisposed
                || _formClosing
                || !Visible)
            {
                return;
            }
            if (_targetOverlay == null || _targetOverlay.IsDisposed)
                _targetOverlay = new TargetHighlightOverlay();
            if (_targetHoverTimer == null)
            {
                _targetHoverTimer = new Timer { Interval = 50 };
                _targetHoverTimer.Tick += OnTargetHoverTick;
            }
            _targetHoverTimer.Start();
            UpdateTargetHoverHighlight();
        }

        private void StopTargetHoverHighlight()
        {
            if (_targetHoverTimer != null)
                _targetHoverTimer.Stop();
            if (_targetOverlay != null && !_targetOverlay.IsDisposed)
                _targetOverlay.HideOverlay();
        }

        private void OnTargetHoverTick(object sender, EventArgs e)
        {
            UpdateTargetHoverHighlight();
        }

        private void UpdateTargetHoverHighlight()
        {
            if (_targetOverlay == null || _targetOverlay.IsDisposed)
                return;
            if (_stateMachine.State != PanelState.Waiting || IsDisposed || _formClosing || !Visible)
            {
                StopTargetHoverHighlight();
                return;
            }
            try
            {
                ResolveResult hovered = WindowResolver.ResolveTopLevel(
                    Cursor.Position, this.Handle, GetTargetOverlayHandle());
                Rectangle bounds;
                if (hovered.Reason == ResolveReason.Ok
                    && WindowResolver.TryGetTargetBounds(hovered.Hwnd, out bounds))
                {
                    _targetOverlay.ShowTarget(bounds, Math.Max(2, M(3)));
                }
                else
                {
                    _targetOverlay.HideOverlay();
                }
            }
            catch
            {
                _targetOverlay.HideOverlay();
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

            ResolveResult result = WindowResolver.ResolveTopLevel(pt, this.Handle, GetTargetOverlayHandle());
            switch (result.Reason)
            {
                case ResolveReason.Excluded:
                    ShowFeedback("点到桌面/任务栏，已忽略，请点目标窗口", true);
                    // 保持 Waiting 并重启钩子（新一轮 30 秒超时）
                    RestartHooksWhileWaiting();
                    break;
                case ResolveReason.Minimized:
                    ShowFeedback("该窗口已最小化，请先打开", true);
                    _waitingPlanToken = default(InspectionPlanToken);
                    _stateMachine.Cancel();
                    OnStateChanged();
                    SyncRolloverUiIfIdle();
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
                _waitingPlanToken = default(InspectionPlanToken);
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
            if (!cancelled)
                return;
            _waitingPlanToken = default(InspectionPlanToken);
            OnStateChanged();
            SyncRolloverUiIfIdle();
            ShowFeedback("已取消选窗", false);
            TryMaybeShowCompletionPrompt();
        }

        private void OnHookTimeoutElapsed()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(OnHookTimeoutElapsed));
                return;
            }
            bool cancelled = _stateMachine.Cancel();
            if (!cancelled)
                return;
            _waitingPlanToken = default(InspectionPlanToken);
            OnStateChanged(); // 钩子已自停，Stop 幂等
            SyncRolloverUiIfIdle();
            ShowFeedback("等待超时，已取消", true);
            TryMaybeShowCompletionPrompt();
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
                SyncRolloverUiIfIdle();
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
        /// 错误色显示「文件名（已暂存）：完整路径」5 秒（比普通 2 秒长以引注意），
        /// 同时完整路径记入根目录行 tooltip，提示消失后仍可查。
        /// 失败口径（简报 D 简化版）：保留 Bitmap 在 _pendingImage、状态回 Idle，
        /// 反馈行常驻「尚未保存，点击此处重试」，点击反馈行以同一 CaptureRequest
        /// （含原日期）重试；新一次截图开始时未处理的旧图先被 Dispose。
        /// </summary>
        private void OnSaveCompleted(StorageSaveResult result, Bitmap image, CaptureRequest request, bool completesCapture, int saveGeneration)
        {
            // 先完成成功/失败与待重试登记，再切换 Idle/处理完成弹窗，避免弹窗时失败状态尚未就绪。
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

                string progressErr = null;
                if (_session != null)
                {
                    InspectionPlanToken token = request != null ? request.PlanToken : default(InspectionPlanToken);
                    string savedDevice = request != null ? request.Device : null;
                    _session.RecordSaveSucceeded(savedDevice, result.FullPath, token, out progressErr);
                }
                if (request != null && !string.IsNullOrEmpty(request.Device))
                {
                    _config.LastDevice = request.Device;
                    SaveBestEffort();
                }

                _saveInFlight = false;
                if (completesCapture && _stateMachine.CompleteCapture())
                    OnStateChanged(); // 回 Idle → 集中点停钩（幂等）

                SyncRolloverUiIfIdle();
                RebuildPlanCard();
                RebuildDeviceButtons();
                RefreshStateVisuals();
                ApplyHeightAnchored();

                string fileName = Path.GetFileName(result.FullPath);
                string unresolvedProgressErr = !string.IsNullOrEmpty(progressErr)
                    ? progressErr
                    : (_session != null && _session.HasProgressError ? _session.ProgressError : null);
                if (!string.IsNullOrEmpty(unresolvedProgressErr))
                {
                    string prefix = result.UsedFallback
                        ? fileName + "（已暂存：" + result.FullPath + "），但进度落盘失败："
                        : fileName + " 已保存，但进度落盘失败：";
                    ShowPersistentFeedback(prefix + unresolvedProgressErr, ErrorColor);
                }
                else if (result.UsedFallback)
                {
                    ShowFeedbackInternal(fileName + "（已暂存）：" + result.FullPath,
                        ErrorColor, false, 5000);
                }
                else
                {
                    ShowSuccessFeedback("已保存：" + fileName);
                }
                TryMaybeShowCompletionPrompt();
                return;
            }

            // 失败：先校验代次与图有效性并完成 _pendingImage 登记，再结束 _saveInFlight 与切换 Idle。
            string failFeedback;
            if (saveGeneration != _pendingGeneration || !IsImageUsable(image))
            {
                if (object.ReferenceEquals(_pendingImage, image))
                {
                    _pendingImage = null;
                    _pendingRequest = null;
                    _pendingGeneration++;
                }
                failFeedback = "尚未保存：原图已失效，请重新截图";
                _saveInFlight = false;
                if (completesCapture && _stateMachine.CompleteCapture())
                    OnStateChanged();
                SyncRolloverUiIfIdle();
                ShowFeedback(failFeedback, true);
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
            failFeedback = string.IsNullOrEmpty(detail)
                ? "尚未保存，点击此处重试"
                : "尚未保存，点击此处重试：" + detail;

            _saveInFlight = false;
            if (completesCapture && _stateMachine.CompleteCapture())
                OnStateChanged();
            SyncRolloverUiIfIdle();
            ShowPersistentFeedback(failFeedback, ErrorColor);
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

        /// <summary>反馈行点击：有待重试图优先重试存图；否则若存在进度读取/落盘失败则打开恢复或重试保存。</summary>
        private void OnFeedbackLabelClicked(object sender, EventArgs e)
        {
            if (_saveInFlight)
                return;
            if (_pendingImage != null && _pendingRequest != null)
            {
                // 字段保留登记：成功后由 OnSaveCompleted 按引用清掉，再失败会重新登记同一图
                BeginSave(_pendingImage, _pendingRequest, false);
                return;
            }
            if (_session != null && _session.HasProgressError)
                HandleProgressRecoveryAction();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            bool waiting = _stateMachine.State == PanelState.Waiting;
            bool capturing = _stateMachine.State == PanelState.Capturing;
            if (waiting || capturing)
            {
                using (var pen = new Pen(waiting ? WarningColor : AccentColor, 2f))
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

        private void ShowProgressErrorFeedback()
        {
            if (_session == null || !_session.HasProgressError)
                return;
            string prefix = _session.IsProgressProtected
                ? "进度读取失败（已保护原文件，点「恢复进度」处理）："
                : "进度落盘失败（点「重试保存」重试）：";
            ShowPersistentFeedback(prefix + _session.ProgressError, ErrorColor);
        }

        private bool RestorePersistentFeedbackIfNeeded()
        {
            if (_configWriteProtected)
            {
                ShowConfigLoadErrorFeedback();
                return true;
            }
            if (_deviceListError != null)
            {
                ShowDeviceListErrorFeedback();
                return true;
            }
            if (_pendingImage != null && _pendingRequest != null)
            {
                ShowPersistentFeedback("尚未保存，点击此处重试", ErrorColor);
                return true;
            }
            if (_session != null && _session.HasProgressError)
            {
                ShowProgressErrorFeedback();
                return true;
            }
            return false;
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
            var menu = _trayMenu = new ContextMenuStrip();
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

        /// <summary>从托盘恢复：显示面板（位置不变、TopMost 照旧）并激活，随后补检跨日与完成提示。</summary>
        private void ShowPanelFromTray()
        {
            if (!Visible)
                Show();
            if (WindowState != FormWindowState.Normal)
                WindowState = FormWindowState.Normal;
            Activate();
            SyncRolloverUiIfIdle();
            TryMaybeShowCompletionPrompt();
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
            _formClosing = true;
            StopTargetHoverHighlight();
            if (_credentialCountdownTimer != null)
                _credentialCountdownTimer.Stop();
            // 托盘图标先隐藏并释放，避免进程退出后托盘区残影。
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
            if (_trayMenu != null)
            {
                _trayMenu.Dispose();
                _trayMenu = null;
            }
            // 退出兜底（Task 6）：先按回执核对清剪贴板（有回执且序列号+令牌
            // 匹配才清，Guard 内部处理），再 Dispose Guard，最后走原有逻辑。
            if (_clipboardGuard != null)
            {
                _clipboardGuard.TryClearNow();
                _clipboardGuard.Cleared -= OnClipboardCleared;
                _clipboardGuard.ClearSkipped -= OnClipboardClearSkipped;
                _clipboardGuard.Dispose();
            }
            if (_usbSyncMenu != null)
            {
                _usbSyncMenu.Dispose();
                _usbSyncMenu = null;
            }
            if (_hotkeyManager != null)
                _hotkeyManager.Dispose();
            _hooks.Stop();
            if (!_hooksDisposed)
            {
                _hooksDisposed = true;
                _hooks.Dispose();
            }
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

            _waitingPlanToken = default(InspectionPlanToken);
            if (_stateMachine.State == PanelState.Waiting)
                _stateMachine.Cancel();
            else if (_stateMachine.State == PanelState.Capturing)
                _stateMachine.FailCapture();
            OnStateChanged(); // 回 Idle 集中点：刷新视觉 + 停钩（幂等）
            SyncRolloverUiIfIdle();

            if (_pendingImage != null && _pendingRequest != null)
                ShowPersistentFeedback("尚未保存，点击此处重试", ErrorColor);
            else if (_session != null && _session.HasProgressError)
                ShowProgressErrorFeedback();
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
            SyncRolloverUiIfIdle();
        }

        private void SyncRolloverUiIfIdle()
        {
            if (_session == null || IsDisposed)
                return;
            bool rolled;
            _session.EnsureCurrentDayResetIfRolled(out rolled);
            if (rolled || _session.HasPendingRolloverRefresh || _uiRenderedPlanDate != _session.Plan.Date)
                _pendingDayRolloverRefresh = true;

            string todayText = FormatFullDate(_clock.Now);
            if (_dateLabel != null && !string.Equals(_dateLabel.Text, todayText, StringComparison.Ordinal))
                _dateLabel.Text = todayText;

            if (_pendingDayRolloverRefresh && _stateMachine.State == PanelState.Idle && !_saveInFlight)
            {
                _pendingDayRolloverRefresh = false;
                _session.ConsumePendingRolloverRefresh();
                RebuildPlanCard();
                RebuildDeviceButtons();
                RefreshStateVisuals();
                ApplyHeightAnchored();
                _uiRenderedPlanDate = _session.Plan.Date;
            }
        }

        private void TryMaybeShowCompletionPrompt()
        {
            if (_session == null || IsDisposed || !Visible || WindowState == FormWindowState.Minimized)
                return;
            bool hiddenOrTransparent = _hiddenForCapture || Opacity < 0.5;
            bool panelVisible = Visible && WindowState != FormWindowState.Minimized;
            string progressErr;
            if (!_session.TryClaimCompletionPrompt(
                _saveInFlight,
                _settingsDialogOpen,
                hiddenOrTransparent,
                panelVisible,
                out progressErr))
            {
                return;
            }

            if (!string.IsNullOrEmpty(progressErr))
            {
                RebuildPlanCard();
                ApplyHeightAnchored();
                ShowPersistentFeedback("已完成全部设备，但“不再重复提醒”未能落盘（重启后可能再次提示）：" + progressErr, ErrorColor);
            }

            bool hasPendingFailure = _pendingImage != null && _pendingRequest != null;
            bool hasSavedFiles = HasSavedPaths();
            DialogResult answer = ShowScaledDialog(
                delegate { return new CompletionPromptDialog(hasPendingFailure, hasSavedFiles, progressErr); });
            if (answer == DialogResult.Yes)
                OpenInspectionFolders();
        }

        private DialogResult ShowScaledDialog(Func<Form> factory)
        {
            double scale = ScaleFactor;
            UiTheme.UseFontScale(scale);
            using (Form dialog = factory())
            {
                UiTheme.ScaleLayout(dialog, scale);
                return dialog.ShowDialog(this);
            }
        }
    }
}
