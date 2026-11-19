using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using InspectionWidget.Capture;
using InspectionWidget.Config;
using InspectionWidget.Credentials;
using InspectionWidget.Native;

namespace InspectionWidget
{
    /// <summary>
    /// 设置窗口：设备 / 凭据 / 快捷链接 / 存储与显示 / 配置备份（含关于信息）。
    /// 所有编辑先进入 SettingsDraft；应用与保存并关闭复用同一提交入口。
    /// Saved 只在成功落盘后触发一次，MainForm 立即刷新运行态。
    /// </summary>
    public class SettingsForm : Form
    {
        private const int GroupDevices = 0;
        private const int GroupCredentials = 1;
        private const int GroupLinks = 2;
        private const int GroupStorage = 3;
        private const int GroupTransfer = 4;

        private readonly ConfigStore _store;
        private readonly ICredentialProtector _protector;
        private readonly SettingsDraft _draft;
        private readonly double _uiScaleFactor;
        private AppConfig _savedConfig;
        private bool _dirty;
        private bool _loading;
        private bool _stale;
        private bool _closingAfterSave;
        private bool _layoutScaleApplied;
        private bool _updatingPageLayout;
        private int _currentGroup;

        public AppConfig ResultConfig { get; private set; }
        public IReadOnlyList<DeviceRenameRecord> LastAppliedDeviceRenames { get; private set; }
        public bool LastAppliedImportDeviceReplace { get; private set; }
        public event EventHandler Saved;

        private readonly Button[] _navButtons = new Button[5];
        private readonly Panel[] _groupPanels = new Panel[5];
        private readonly Dictionary<Panel, PageMeta> _pageMetas = new Dictionary<Panel, PageMeta>();
        private Panel _navAccent;
        private Panel _contentHost;
        private Label _feedbackLabel;
        private ToolTip _feedbackToolTip;
        private Button _applyButton;
        private Button _saveButton;

        private DeviceEditorControl _deviceEditor;
        private Panel _credentialRowsPanel;
        private TableLayoutPanel _credentialHeader;
        private readonly List<CredentialRow> _credentialRows = new List<CredentialRow>();
        private Button _addCredentialButton;
        private Label _credentialCountLabel;

        private Panel _linkRowsPanel;
        private TableLayoutPanel _linkHeader;
        private readonly List<LinkRow> _linkRows = new List<LinkRow>();
        private Button _addLinkButton;
        private Label _linkCountLabel;
        private Label _linkEmptyLabel;

        private TextBox _rootPathTextBox;
        private Label _rootStatusLabel;
        private TextBox _usbRootPathTextBox;
        private TextBox _folderPatternTextBox;
        private TextBox _folderPreviewTextBox;
        private CheckBox _autoClearCheckBox;
        private ComboBox _uiScaleComboBox;
        private RadioButton _modeAutoRadio;
        private RadioButton _modePrintWindowRadio;
        private RadioButton _modeBitBltRadio;
        private CheckBox _planModeCheckBox;
        private RadioButton _hideVisitedTrueRadio;
        private RadioButton _hideVisitedFalseRadio;
        private Label _configPathLabel;
        private Label _exportSourceLabel;
        private Label _importDetailLabel;
        private Label _aboutConfigPathLabel;
        private Label _aboutRootPathLabel;
        private Icon _ownedAppIcon;

        private sealed class BufferedPanel : Panel
        {
            public BufferedPanel()
            {
                DoubleBuffered = true;
            }
        }

        private sealed class BufferedTableLayoutPanel : TableLayoutPanel
        {
            public BufferedTableLayoutPanel()
            {
                DoubleBuffered = true;
            }
        }

        private sealed class HotkeyTextBox : TextBox
        {
            public event Action<string> HotkeyCaptured;
            public event Action<string> HotkeyRejected;

            public HotkeyTextBox()
            {
                ReadOnly = true;
                Cursor = Cursors.Hand;
                BackColor = UiTheme.CardBackColor;
            }

            protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
            {
                Keys keyCode = keyData & Keys.KeyCode;
                Keys mods = keyData & Keys.Modifiers;

                if (mods == Keys.None && (keyCode == Keys.Tab || keyCode == Keys.Escape))
                    return base.ProcessCmdKey(ref msg, keyData);

                if (mods == Keys.None && (keyCode == Keys.Back || keyCode == Keys.Delete))
                {
                    if (!string.IsNullOrEmpty(Text))
                    {
                        Text = string.Empty;
                        if (HotkeyCaptured != null)
                            HotkeyCaptured(string.Empty);
                    }
                    return true;
                }

                if (keyCode == Keys.ControlKey || keyCode == Keys.LControlKey || keyCode == Keys.RControlKey
                    || keyCode == Keys.Menu || keyCode == Keys.LMenu || keyCode == Keys.RMenu
                    || keyCode == Keys.ShiftKey || keyCode == Keys.LShiftKey || keyCode == Keys.RShiftKey
                    || keyCode == Keys.LWin || keyCode == Keys.RWin)
                {
                    return true;
                }

                string canonical;
                string error;
                if (HotkeySpec.TryFromKeyEvent(keyData, out canonical, out error))
                {
                    Text = canonical;
                    if (HotkeyCaptured != null)
                        HotkeyCaptured(canonical);
                }
                else if (HotkeyRejected != null && !string.IsNullOrEmpty(error))
                {
                    HotkeyRejected(error);
                }
                return true;
            }
        }

        private sealed class PageMeta
        {
            public TableLayoutPanel Layout;
            public Label Title;
            public Label Desc;
            public Action<int, int> UpdateContent;
            public int LastWidth = -1;
            public int LastHeight = -1;
        }

        private sealed class LinkRow
        {
            public QuickLinkEntry Draft;
            public TableLayoutPanel Panel;
            public CheckBox EnabledBox;
            public TextBox TitleBox;
            public TextBox UrlBox;
            public Button UpButton;
            public Button DownButton;
            public Button DeleteButton;
        }

        private sealed class CredentialRow
        {
            public CredentialDraft Draft;
            public TableLayoutPanel Panel;
            public TextBox NameBox;
            public TextBox SecretBox;
            public HotkeyTextBox HotkeyBox;
            public Label StatusLabel;
            public Button ClearButton;
            public Button UndoButton;
            public Button DeleteButton;
            public Button UpButton;
            public Button DownButton;
            public ToolTip ToolTip;
        }

        public SettingsForm(AppConfig config, ConfigStore store, ICredentialProtector protector,
            double systemDpiScale = 1.0)
        {
            if (store == null) throw new ArgumentNullException("store");
            if (protector == null) throw new ArgumentNullException("protector");
            if (systemDpiScale <= 0 || double.IsNaN(systemDpiScale)
                || double.IsInfinity(systemDpiScale))
                throw new ArgumentOutOfRangeException("systemDpiScale");
            _store = store;
            _protector = protector;
            _savedConfig = AppConfigCloner.Clone(config);
            _draft = SettingsDraft.FromConfig(config, store.CurrentRevision);
            // 与主面板同一口径：应用档位 × 系统 DPI，且只由手动布局乘一次。
            _uiScaleFactor = UiScale.Factor(_draft.UiScalePercent) * systemDpiScale;
            UiTheme.UseFontScale(_uiScaleFactor);

            Text = "巡检助手设置";
            StartPosition = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            MaximizeBox = true;
            ShowInTaskbar = false;
            BackColor = UiTheme.PageBackColor;
            Font = UiTheme.BodyFont;
            // 应用档位与系统 DPI 已合并进 _uiScaleFactor，由 UiTheme.ScaleLayout
            // 统一处理；关闭 WinForms 自动 DPI 缩放，避免再乘一次造成二次缩放。
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(840, 680);
            MinimumSize = new Size(620, 460);
            KeyPreview = true;
            _ownedAppIcon = AppIcon.Load();
            if (_ownedAppIcon != null) Icon = _ownedAppIcon;

            _feedbackToolTip = new ToolTip();
            LastAppliedDeviceRenames = new List<DeviceRenameRecord>();
            BuildLayout();
            SyncAllControls();
            SetGroup(GroupDevices);
            UiTheme.ScaleLayout(this, _uiScaleFactor);
            _layoutScaleApplied = true;
            UpdateAllPageLayouts();
            LayoutCredentialRows();
            LayoutLinkRows();
            _dirty = false;
            UpdateTitle();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Form owner = Owner;
            Rectangle work = owner != null ? Screen.FromControl(owner).WorkingArea : Screen.PrimaryScreen.WorkingArea;
            int availableWidth = Math.Max(320, work.Width - 16);
            int availableHeight = Math.Max(240, work.Height - 16);
            int minimumWidth = Math.Min(MinimumSize.Width, availableWidth);
            int minimumHeight = Math.Min(MinimumSize.Height, availableHeight);
            MinimumSize = new Size(minimumWidth, minimumHeight);
            int width = Math.Max(minimumWidth, Math.Min(Width, availableWidth));
            int height = Math.Max(minimumHeight, Math.Min(Height, availableHeight));
            Size = new Size(width, height);
            Location = PanelLayout.CenterInWorkingArea(work, Size);
            UpdateAllPageLayouts();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            UpdatePageLayout(_groupPanels[_currentGroup]);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            bool pendingDeviceEdit = _deviceEditor != null
                && _deviceEditor.Grid.IsCurrentCellInEditMode;
            if (!_closingAfterSave && (_dirty || pendingDeviceEdit))
            {
                using (var dialog = new DiscardChangesDialog())
                {
                    UiTheme.ScaleLayout(dialog, _uiScaleFactor);
                    if (dialog.ShowDialog(this) != DialogResult.Yes)
                    {
                        e.Cancel = true;
                        return;
                    }
                    if (pendingDeviceEdit && !_deviceEditor.CancelCurrentEdit())
                    {
                        ShowError(GroupDevices, _deviceEditor.LastCancelDiagnostic,
                            _deviceEditor.Grid);
                        e.Cancel = true;
                        return;
                    }
                }
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_feedbackToolTip != null)
            {
                _feedbackToolTip.Dispose();
                _feedbackToolTip = null;
            }
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_feedbackToolTip != null)
                {
                    _feedbackToolTip.Dispose();
                    _feedbackToolTip = null;
                }
                if (_ownedAppIcon != null)
                {
                    Icon = null;
                    _ownedAppIcon.Dispose();
                    _ownedAppIcon = null;
                }
            }
            base.Dispose(disposing);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape && _deviceEditor != null
                && _deviceEditor.Grid.IsCurrentCellInEditMode)
            {
                if (!_deviceEditor.CancelCurrentEdit())
                    ShowError(GroupDevices, _deviceEditor.LastCancelDiagnostic,
                        _deviceEditor.Grid);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0),
                BackColor = UiTheme.PageBackColor
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62f));

            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0),
                BackColor = UiTheme.PageBackColor
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148f));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var navPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                BackColor = UiTheme.SurfaceColor,
                Padding = new Padding(0, 0, 1, 0)
            };
            navPanel.Paint += delegate(object sender, PaintEventArgs e)
            {
                var p = (Panel)sender;
                using (var pen = new Pen(UiTheme.BorderColor, 1f))
                {
                    e.Graphics.DrawLine(pen, p.Width - 1, 0, p.Width - 1, p.Height);
                }
            };
            string[] navTexts = { "设备", "凭据", "快捷链接", "存储与显示", "配置备份" };
            for (int i = 0; i < navTexts.Length; i++)
            {
                int index = i;
                var button = new Button
                {
                    Text = navTexts[i],
                    Dock = DockStyle.None,
                    Width = 147,
                    Height = 42,
                    Location = new Point(0, 12 + i * 42),
                    Font = UiTheme.BodyFont,
                    ForeColor = UiTheme.PrimaryTextColor,
                    BackColor = UiTheme.SurfaceColor,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(16, 0, 0, 0),
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand,
                    TabIndex = i
                };
                button.FlatAppearance.BorderSize = 0;
                button.FlatAppearance.MouseOverBackColor = UiTheme.NavHoverBackColor;
                button.Click += delegate { OnNavClicked(index); };
                _navButtons[i] = button;
                navPanel.Controls.Add(button);
            }
            navPanel.SizeChanged += delegate
            {
                int btnWidth = Math.Max(1, navPanel.ClientSize.Width - Math.Max(1, navPanel.Padding.Right));
                for (int i = 0; i < _navButtons.Length; i++)
                {
                    if (_navButtons[i] != null)
                        _navButtons[i].Width = btnWidth;
                }
            };
            _navAccent = new Panel
            {
                BackColor = UiTheme.AccentColor,
                Width = 3,
                Height = 42,
                Location = new Point(0, 12)
            };
            navPanel.Controls.Add(_navAccent);
            _navAccent.BringToFront();
            body.Controls.Add(navPanel, 0, 0);

            _contentHost = new BufferedPanel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                BackColor = UiTheme.PageBackColor
            };
            body.Controls.Add(_contentHost, 1, 0);

            _groupPanels[GroupDevices] = BuildDevicesPage();
            _groupPanels[GroupCredentials] = BuildCredentialsPage();
            _groupPanels[GroupLinks] = BuildLinksPage();
            _groupPanels[GroupStorage] = BuildStoragePage();
            _groupPanels[GroupTransfer] = BuildTransferPage();
            for (int i = 0; i < _groupPanels.Length; i++)
                _contentHost.Controls.Add(_groupPanels[i]);

            root.Controls.Add(body, 0, 0);

            var bottom = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(14, 10, 14, 10),
                BackColor = UiTheme.CardBackColor
            };
            bottom.Paint += delegate(object sender, PaintEventArgs e)
            {
                var p = (Control)sender;
                using (var pen = new Pen(UiTheme.BorderColor, 1f))
                {
                    e.Graphics.DrawLine(pen, 0, 0, p.Width, 0);
                }
            };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            _feedbackLabel = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                AutoEllipsis = true,
                Font = UiTheme.SmallFont,
                ForeColor = UiTheme.SecondaryTextColor,
                BackColor = UiTheme.CardBackColor,
                TextAlign = ContentAlignment.MiddleLeft,
                UseMnemonic = false
            };
            bottom.Controls.Add(_feedbackLabel, 0, 0);

            var actions = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = UiTheme.CardBackColor,
                Padding = new Padding(0, 2, 0, 0)
            };
            _applyButton = UiTheme.SecondaryButton("应用");
            _applyButton.Width = 84;
            _applyButton.AccessibleName = "应用设置";
            _applyButton.Click += delegate { ApplyInternal(false); };
            _saveButton = UiTheme.PrimaryButton("保存并关闭");
            _saveButton.Width = 112;
            _saveButton.AccessibleName = "保存设置并关闭";
            _saveButton.Click += delegate { ApplyInternal(true); };
            var cancelButton = UiTheme.SecondaryButton("取消");
            cancelButton.Width = 84;
            cancelButton.Margin = new Padding(0);
            cancelButton.AccessibleName = "取消设置";
            cancelButton.Click += delegate { Close(); };
            actions.Controls.Add(_applyButton);
            actions.Controls.Add(_saveButton);
            actions.Controls.Add(cancelButton);
            bottom.Controls.Add(actions, 1, 0);
            CancelButton = cancelButton;
            AcceptButton = _saveButton;

            root.Controls.Add(bottom, 0, 1);
            Controls.Add(root);
        }

        private Panel NewPage(string titleText, string description)
        {
            var page = new BufferedPanel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                AutoScroll = true,
                AutoScrollMargin = new Size(0, 12),
                BackColor = UiTheme.PageBackColor,
                Visible = false
            };
            var layout = new BufferedTableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(22, 18, 22, 18),
                BackColor = UiTheme.PageBackColor
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            bool hasDesc = !string.IsNullOrWhiteSpace(description);
            var title = new Label
            {
                Text = titleText,
                Font = UiTheme.TitleFont,
                ForeColor = UiTheme.PrimaryTextColor,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, hasDesc ? 4 : 12)
            };
            layout.Controls.Add(title);
            Label desc = null;
            if (hasDesc)
            {
                desc = new Label
                {
                    Text = description,
                    Font = UiTheme.SmallFont,
                    ForeColor = UiTheme.WeakTextColor,
                    AutoSize = true,
                    MaximumSize = new Size(420, 0),
                    Margin = new Padding(0, 0, 0, 14)
                };
                layout.Controls.Add(desc);
            }
            page.Controls.Add(layout);
            page.Tag = layout;
            var meta = new PageMeta
            {
                Layout = layout,
                Title = title,
                Desc = desc
            };
            _pageMetas[page] = meta;
            page.SizeChanged += delegate
            {
                if (_layoutScaleApplied && page.Visible)
                    UpdatePageLayout(page);
            };
            return page;
        }

        private static TableLayoutPanel PageLayout(Panel page)
        {
            return (TableLayoutPanel)page.Tag;
        }

        private void UpdateAllPageLayouts()
        {
            for (int i = 0; i < _groupPanels.Length; i++)
                UpdatePageLayout(_groupPanels[i], true);
        }

        private void InvalidatePageLayout(Panel page)
        {
            PageMeta meta;
            if (page != null && _pageMetas.TryGetValue(page, out meta))
            {
                meta.LastWidth = -1;
                meta.LastHeight = -1;
            }
        }

        private void UpdatePageLayout(Panel page)
        {
            UpdatePageLayout(page, false);
        }

        private void UpdatePageLayout(Panel page, bool force)
        {
            if (page == null || _updatingPageLayout || !_layoutScaleApplied)
                return;
            PageMeta meta;
            if (!_pageMetas.TryGetValue(page, out meta) || meta.Layout == null)
                return;
            int w = page.ClientSize.Width;
            int h = page.ClientSize.Height;
            if (w <= 0)
                return;
            if (!force && meta.LastWidth == w && meta.LastHeight == h)
                return;

            _updatingPageLayout = true;
            meta.Layout.SuspendLayout();
            try
            {
                int scrollReserve = page.VerticalScroll.Visible ? 0 : SystemInformation.VerticalScrollBarWidth;
                int layoutMaxWidth = Math.Max(L(220), w - scrollReserve);
                if (meta.Layout.MaximumSize.Width != layoutMaxWidth)
                    meta.Layout.MaximumSize = new Size(layoutMaxWidth, 0);

                int contentWidth = Math.Max(L(180), layoutMaxWidth - meta.Layout.Padding.Horizontal);
                SetWrappedWidth(meta.Title, contentWidth);
                if (meta.Desc != null)
                    SetWrappedWidth(meta.Desc, contentWidth);

                if (meta.UpdateContent != null)
                {
                    int headerUsed = meta.Layout.Padding.Vertical
                        + (meta.Title != null ? meta.Title.Height + meta.Title.Margin.Vertical : 0)
                        + (meta.Desc != null ? meta.Desc.Height + meta.Desc.Margin.Vertical : 0);
                    int remainingHeight = Math.Max(L(220), h - headerUsed);
                    meta.UpdateContent(contentWidth, remainingHeight);
                }
                if (page.Visible)
                {
                    meta.LastWidth = w;
                    meta.LastHeight = h;
                }
            }
            finally
            {
                meta.Layout.ResumeLayout(true);
                _updatingPageLayout = false;
            }
        }

        private void SetWrappedWidth(Control control, int containerInnerWidth)
        {
            if (control == null)
                return;
            int maxWidth = Math.Max(L(80), containerInnerWidth - control.Margin.Horizontal);
            if (control.MaximumSize.Width != maxWidth)
                control.MaximumSize = new Size(maxWidth, 0);
        }

        private Panel BuildDevicesPage()
        {
            Panel page = NewPage("设备", null);
            _deviceEditor = new DeviceEditorControl(_draft)
            {
                Height = 365,
                Dock = DockStyle.Top
            };
            _deviceEditor.DraftChanged += delegate { MarkDirty(); ClearFeedback(); };
            _deviceEditor.ValidationFailed += delegate(string error)
            {
                ShowError(GroupDevices, error, _deviceEditor.Grid);
            };
            PageLayout(page).Controls.Add(_deviceEditor);
            _pageMetas[page].UpdateContent = delegate(int contentWidth, int remainingHeight)
            {
                int targetHeight = Math.Max(L(300), remainingHeight - L(4));
                if (_deviceEditor.Height != targetHeight)
                    _deviceEditor.Height = targetHeight;
            };
            return page;
        }

        private Panel BuildCredentialsPage()
        {
            Panel page = NewPage("凭据", null);
            var layout = PageLayout(page);

            var top = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(0, 0, 0, 8)
            };
            _addCredentialButton = UiTheme.PrimaryButton("添加凭据");
            _addCredentialButton.Width = 100;
            _addCredentialButton.Click += delegate { AddCredential(); };
            _credentialCountLabel = new Label
            {
                AutoSize = true,
                Font = UiTheme.BodyFont,
                ForeColor = UiTheme.SecondaryTextColor,
                Padding = new Padding(4, 9, 0, 0)
            };
            top.Controls.Add(_addCredentialButton);
            top.Controls.Add(_credentialCountLabel);
            layout.Controls.Add(top);

            _credentialHeader = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 30,
                ColumnCount = 9,
                RowCount = 1,
                Padding = new Padding(8, 0, 8, 0),
                Margin = new Padding(0, 0, 0, 4),
                BackColor = UiTheme.SurfaceColor
            };
            _credentialHeader.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            _credentialHeader.Paint += delegate(object sender, PaintEventArgs e)
            {
                var p = (Control)sender;
                if (p.Width <= 1 || p.Height <= 1) return;
                using (var pen = new Pen(UiTheme.BorderColor, 1f))
                {
                    e.Graphics.DrawLine(pen, 0, p.Height - 1, p.Width, p.Height - 1);
                }
            };
            string[] headers = { "名称", "内容", "快捷键", "状态", "清除", "撤销", "删除", "上移", "下移" };
            int[] widths = { 104, 100, 104, 72, 44, 44, 44, 30, 30 };
            for (int i = 0; i < 9; i++)
            {
                if (i == 3) _credentialHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                else _credentialHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, widths[i]));
                _credentialHeader.Controls.Add(new Label
                {
                    Text = headers[i],
                    Font = UiTheme.SmallBoldFont,
                    ForeColor = UiTheme.SecondaryTextColor,
                    BackColor = UiTheme.SurfaceColor,
                    Dock = DockStyle.Fill,
                    TextAlign = i >= 4 ? ContentAlignment.MiddleCenter : ContentAlignment.MiddleLeft
                }, i, 0);
            }
            layout.Controls.Add(_credentialHeader);

            _credentialRowsPanel = new BufferedPanel
            {
                Height = 340,
                Dock = DockStyle.Top,
                AutoScroll = true,
                BackColor = UiTheme.PageBackColor
            };
            _credentialRowsPanel.Resize += delegate
            {
                if (!_loading && _layoutScaleApplied)
                    LayoutCredentialRows();
            };
            layout.Controls.Add(_credentialRowsPanel);
            _pageMetas[page].UpdateContent = delegate(int contentWidth, int remainingHeight)
            {
                top.MaximumSize = new Size(contentWidth, 0);
                int used = top.Height + top.Margin.Vertical + _credentialHeader.Height + _credentialHeader.Margin.Vertical;
                int targetHeight = Math.Max(L(220), Math.Min(L(400), remainingHeight - used));
                if (_credentialRowsPanel.Height != targetHeight)
                    _credentialRowsPanel.Height = targetHeight;
                LayoutCredentialRows();
            };
            return page;
        }

        private Panel BuildLinksPage()
        {
            Panel page = NewPage("快捷链接", null);
            var layout = PageLayout(page);

            var top = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(0, 0, 0, 8)
            };
            _addLinkButton = UiTheme.PrimaryButton("添加链接");
            _addLinkButton.Width = 100;
            _addLinkButton.AccessibleName = "添加快捷链接";
            _addLinkButton.Click += delegate { AddLink(); };
            _linkCountLabel = new Label
            {
                AutoSize = true,
                Font = UiTheme.BodyFont,
                ForeColor = UiTheme.SecondaryTextColor,
                Padding = new Padding(4, 9, 0, 0)
            };
            top.Controls.Add(_addLinkButton);
            top.Controls.Add(_linkCountLabel);
            layout.Controls.Add(top);

            _linkHeader = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 30,
                ColumnCount = 6,
                RowCount = 1,
                Padding = new Padding(8, 0, 8, 0),
                Margin = new Padding(0, 0, 0, 4),
                BackColor = UiTheme.SurfaceColor
            };
            _linkHeader.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            _linkHeader.Paint += delegate(object sender, PaintEventArgs e)
            {
                var p = (Control)sender;
                if (p.Width <= 1 || p.Height <= 1) return;
                using (var pen = new Pen(UiTheme.BorderColor, 1f))
                {
                    e.Graphics.DrawLine(pen, 0, p.Height - 1, p.Width, p.Height - 1);
                }
            };
            string[] headers = { "启用", "名称", "网址", "上移", "下移", "删除" };
            int[] widths = { 46, 136, 180, 34, 34, 54 };
            for (int i = 0; i < 6; i++)
            {
                if (i == 2) _linkHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                else _linkHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, widths[i]));
                _linkHeader.Controls.Add(new Label
                {
                    Text = headers[i],
                    Font = UiTheme.SmallBoldFont,
                    ForeColor = UiTheme.SecondaryTextColor,
                    BackColor = UiTheme.SurfaceColor,
                    Dock = DockStyle.Fill,
                    TextAlign = (i == 0 || i >= 3) ? ContentAlignment.MiddleCenter : ContentAlignment.MiddleLeft
                }, i, 0);
            }
            layout.Controls.Add(_linkHeader);

            _linkRowsPanel = new BufferedPanel
            {
                Height = 340,
                Dock = DockStyle.Top,
                AutoScroll = true,
                BackColor = UiTheme.PageBackColor
            };
            _linkRowsPanel.Resize += delegate
            {
                if (!_loading && _layoutScaleApplied)
                    LayoutLinkRows();
            };
            _linkEmptyLabel = new Label
            {
                Text = "暂无快捷链接，点击「添加链接」新增。",
                AutoSize = true,
                Font = UiTheme.BodyFont,
                ForeColor = UiTheme.WeakTextColor,
                Padding = new Padding(8, 12, 8, 12),
                Location = new Point(0, 4),
                Visible = false
            };
            layout.Controls.Add(_linkRowsPanel);
            _pageMetas[page].UpdateContent = delegate(int contentWidth, int remainingHeight)
            {
                top.MaximumSize = new Size(contentWidth, 0);
                int used = top.Height + top.Margin.Vertical + _linkHeader.Height + _linkHeader.Margin.Vertical;
                int targetHeight = Math.Max(L(220), Math.Min(L(400), remainingHeight - used));
                if (_linkRowsPanel.Height != targetHeight)
                    _linkRowsPanel.Height = targetHeight;
                if (_linkEmptyLabel != null)
                    _linkEmptyLabel.MaximumSize = new Size(Math.Max(L(180), contentWidth - L(16)), 0);
                LayoutLinkRows();
            };
            return page;
        }

        private Panel BuildStoragePage()
        {
            Panel page = NewPage("存储与显示", null);
            var outer = PageLayout(page);
            var settings = new BufferedTableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                ColumnCount = 2,
                Margin = new Padding(0),
                BackColor = UiTheme.CardBackColor
            };
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112f));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            _rootPathTextBox = new TextBox
            {
                ReadOnly = true,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Font = UiTheme.BodyFont,
                BackColor = UiTheme.PageBackColor,
                Margin = new Padding(0, 0, 8, 0),
                AccessibleName = "截图根目录"
            };
            var rootRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0),
                BackColor = UiTheme.CardBackColor
            };
            rootRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            rootRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            rootRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rootRow.Controls.Add(_rootPathTextBox, 0, 0);
            var changeButton = UiTheme.SecondaryButton("更改…");
            changeButton.Width = 76;
            changeButton.Margin = new Padding(0);
            changeButton.Click += delegate { ChangeRootPath(); };
            rootRow.Controls.Add(changeButton, 1, 0);
            AddSettingRow(settings, "截图根目录", rootRow);

            _rootStatusLabel = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(360, 0),
                Font = UiTheme.SmallFont,
                ForeColor = UiTheme.WeakTextColor,
                BackColor = UiTheme.CardBackColor,
                Visible = false
            };
            AddSettingFullRow(settings, _rootStatusLabel);

            _usbRootPathTextBox = new TextBox
            {
                ReadOnly = true,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Font = UiTheme.BodyFont,
                BackColor = UiTheme.PageBackColor,
                Margin = new Padding(0, 0, 8, 0),
                AccessibleName = "U盘联动目录"
            };
            NativeMethods.SetCueBanner(_usbRootPathTextBox, "未设置（如 D:\\巡检）");
            var usbRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0),
                BackColor = UiTheme.CardBackColor
            };
            usbRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            usbRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            usbRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            usbRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            usbRow.Controls.Add(_usbRootPathTextBox, 0, 0);
            var usbSelectBtn = UiTheme.SecondaryButton("选择…");
            usbSelectBtn.Width = 76;
            usbSelectBtn.Margin = new Padding(0, 0, 6, 0);
            usbSelectBtn.Click += delegate { ChangeUsbRootPath(); };
            var usbClearBtn = UiTheme.SecondaryButton("清除");
            usbClearBtn.Width = 60;
            usbClearBtn.Margin = new Padding(0);
            usbClearBtn.Click += delegate { ClearUsbRootPath(); };
            usbRow.Controls.Add(usbSelectBtn, 1, 0);
            usbRow.Controls.Add(usbClearBtn, 2, 0);
            AddSettingRow(settings, "U盘目录", usbRow);

            _folderPatternTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Font = UiTheme.BodyFont,
                AccessibleName = "日期文件夹名模式"
            };
            _folderPatternTextBox.TextChanged += delegate
            {
                if (_loading) return;
                _draft.FolderPattern = _folderPatternTextBox.Text;
                MarkDirty();
                RefreshFolderPreview();
                ClearFeedback();
            };
            AddSettingRow(settings, "日期文件夹名", _folderPatternTextBox);

            var patternHelp = new Label
            {
                Text = "示例：巡检-yyyy-MM-dd",
                AutoSize = true,
                MaximumSize = new Size(360, 0),
                Font = UiTheme.SmallFont,
                ForeColor = UiTheme.WeakTextColor,
                BackColor = UiTheme.CardBackColor
            };
            AddSettingFullRow(settings, patternHelp);

            _folderPreviewTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Font = UiTheme.SmallFont,
                BackColor = UiTheme.PageBackColor,
                AccessibleName = "当前日期文件夹完整路径预览"
            };
            AddSettingRow(settings, "当前路径预览", _folderPreviewTextBox);

            _autoClearCheckBox = new CheckBox
            {
                Text = "复制凭据后 30 秒自动清空剪贴板",
                AutoSize = true,
                MaximumSize = new Size(360, 0),
                Font = UiTheme.BodyFont,
                ForeColor = UiTheme.PrimaryTextColor,
                BackColor = UiTheme.CardBackColor,
                AccessibleName = "复制后自动清空剪贴板"
            };
            _autoClearCheckBox.CheckedChanged += delegate
            {
                if (_loading) return;
                _draft.AutoClearClipboard = _autoClearCheckBox.Checked;
                MarkDirty();
            };
            AddSettingFullRow(settings, _autoClearCheckBox);

            _uiScaleComboBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 150,
                MinimumSize = new Size(140, 0),
                Font = UiTheme.BodyFont,
                AccessibleName = "界面缩放"
            };
            for (int i = 0; i < UiScale.Count; i++)
                _uiScaleComboBox.Items.Add(UiScale.TextForIndex(i));
            _uiScaleComboBox.SelectedIndexChanged += delegate
            {
                if (_loading) return;
                _draft.UiScalePercent = UiScale.PercentForIndex(_uiScaleComboBox.SelectedIndex);
                MarkDirty();
            };
            AddSettingRow(settings, "界面缩放", _uiScaleComboBox);

            Panel storageCard = UiTheme.CreateSectionCard("存储与界面", settings);
            outer.Controls.Add(storageCard);

            FlowLayoutPanel planStack;
            Panel planCard = UiTheme.CreateSectionCard("巡检计划", out planStack);
            _planModeCheckBox = new CheckBox
            {
                Text = "开启今日巡检计划",
                AutoSize = true,
                MaximumSize = new Size(360, 0),
                Font = UiTheme.BodyFont,
                ForeColor = UiTheme.PrimaryTextColor,
                BackColor = UiTheme.CardBackColor,
                Margin = new Padding(0, 0, 0, 2),
                AccessibleName = "开启今日巡检计划"
            };
            _planModeCheckBox.CheckedChanged += delegate
            {
                if (_loading) return;
                _draft.PlanModeEnabled = _planModeCheckBox.Checked;
                MarkDirty();
            };
            var hideTitle = new Label
            {
                Text = "已点过设备",
                AutoSize = true,
                MaximumSize = new Size(360, 0),
                Font = UiTheme.SmallFont,
                ForeColor = UiTheme.SecondaryTextColor,
                BackColor = UiTheme.CardBackColor,
                Margin = new Padding(0, 8, 0, 4)
            };
            _hideVisitedTrueRadio = new RadioButton
            {
                Text = "隐藏已点过",
                AutoSize = true,
                MaximumSize = new Size(360, 0),
                Font = UiTheme.BodyFont,
                ForeColor = UiTheme.PrimaryTextColor,
                BackColor = UiTheme.CardBackColor,
                Margin = new Padding(0, 0, 0, 3),
                AccessibleName = "隐藏已点过设备"
            };
            _hideVisitedFalseRadio = new RadioButton
            {
                Text = "保留并显示已点过",
                AutoSize = true,
                MaximumSize = new Size(360, 0),
                Font = UiTheme.BodyFont,
                ForeColor = UiTheme.PrimaryTextColor,
                BackColor = UiTheme.CardBackColor,
                Margin = new Padding(0),
                AccessibleName = "保留已点过设备并显示对勾"
            };
            _hideVisitedTrueRadio.CheckedChanged += delegate
            {
                if (_loading || !_hideVisitedTrueRadio.Checked) return;
                _draft.HideVisitedDevices = true;
                MarkDirty();
            };
            _hideVisitedFalseRadio.CheckedChanged += delegate
            {
                if (_loading || !_hideVisitedFalseRadio.Checked) return;
                _draft.HideVisitedDevices = false;
                MarkDirty();
            };
            planStack.Controls.Add(_planModeCheckBox);
            planStack.Controls.Add(hideTitle);
            planStack.Controls.Add(_hideVisitedTrueRadio);
            planStack.Controls.Add(_hideVisitedFalseRadio);
            outer.Controls.Add(planCard);

            var modes = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(0),
                BackColor = UiTheme.CardBackColor
            };
            _modeAutoRadio = new RadioButton
            {
                Text = "自动（推荐）",
                AutoSize = true,
                Font = UiTheme.BodyFont,
                ForeColor = UiTheme.PrimaryTextColor,
                BackColor = UiTheme.CardBackColor,
                Margin = new Padding(0, 0, 14, 0)
            };
            _modePrintWindowRadio = new RadioButton
            {
                Text = "仅 PrintWindow",
                AutoSize = true,
                Font = UiTheme.BodyFont,
                ForeColor = UiTheme.PrimaryTextColor,
                BackColor = UiTheme.CardBackColor,
                Margin = new Padding(0, 0, 14, 0)
            };
            _modeBitBltRadio = new RadioButton
            {
                Text = "仅屏幕拷贝",
                AutoSize = true,
                Font = UiTheme.BodyFont,
                ForeColor = UiTheme.PrimaryTextColor,
                BackColor = UiTheme.CardBackColor,
                Margin = new Padding(0)
            };
            _modeAutoRadio.CheckedChanged += delegate { if (!_loading && _modeAutoRadio.Checked) SetCaptureMode(CaptureMode.Auto); };
            _modePrintWindowRadio.CheckedChanged += delegate { if (!_loading && _modePrintWindowRadio.Checked) SetCaptureMode(CaptureMode.PrintWindowOnly); };
            _modeBitBltRadio.CheckedChanged += delegate { if (!_loading && _modeBitBltRadio.Checked) SetCaptureMode(CaptureMode.BitBltOnly); };
            modes.Controls.Add(_modeAutoRadio);
            modes.Controls.Add(_modePrintWindowRadio);
            modes.Controls.Add(_modeBitBltRadio);
            Panel modeCard = UiTheme.CreateSectionCard("截图模式", modes);
            outer.Controls.Add(modeCard);

            _pageMetas[page].UpdateContent = delegate(int contentWidth, int remainingHeight)
            {
                SetWrappedWidth(storageCard, contentWidth);
                int storageInner = Math.Max(L(160), contentWidth - storageCard.Padding.Horizontal);
                SetWrappedWidth(settings, storageInner);
                SetWrappedWidth(_rootStatusLabel, storageInner);
                SetWrappedWidth(patternHelp, storageInner);
                SetWrappedWidth(_autoClearCheckBox, storageInner);

                SetWrappedWidth(planCard, contentWidth);
                int planInner = Math.Max(L(160), contentWidth - planCard.Padding.Horizontal);
                SetWrappedWidth(planStack, planInner);
                SetWrappedWidth(_planModeCheckBox, planInner);
                SetWrappedWidth(_hideVisitedTrueRadio, planInner);
                SetWrappedWidth(_hideVisitedFalseRadio, planInner);

                SetWrappedWidth(modeCard, contentWidth);
                int modeInner = Math.Max(L(160), contentWidth - modeCard.Padding.Horizontal);
                SetWrappedWidth(modes, modeInner);
            };
            return page;
        }

        private Panel BuildTransferPage()
        {
            Panel page = NewPage("配置备份", null);
            var layout = PageLayout(page);

            FlowLayoutPanel transferStack;
            Panel transferCard = UiTheme.CreateSectionCard("导入与导出", out transferStack);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                BackColor = UiTheme.CardBackColor,
                Margin = new Padding(0, 0, 0, 4)
            };
            var importButton = UiTheme.PrimaryButton("导入配置…");
            importButton.Width = 114;
            importButton.AccessibleName = "导入外部配置";
            importButton.Click += delegate { ImportConfig(); };
            var exportButton = UiTheme.SecondaryButton("导出普通配置…");
            exportButton.Width = 132;
            exportButton.AccessibleName = "导出不包含凭据的普通配置";
            exportButton.Click += delegate { ExportConfig(false); };
            var backupButton = UiTheme.SecondaryButton("导出完整备份…");
            backupButton.Width = 132;
            backupButton.AccessibleName = "导出包含凭据密文的完整备份";
            backupButton.Click += delegate { ExportConfig(true); };
            buttons.Controls.Add(importButton);
            buttons.Controls.Add(exportButton);
            buttons.Controls.Add(backupButton);
            transferStack.Controls.Add(buttons);

            _exportSourceLabel = new Label
            {
                Text = string.Empty,
                AutoSize = true,
                MaximumSize = new Size(380, 0),
                Font = UiTheme.SmallFont,
                ForeColor = UiTheme.SecondaryTextColor,
                BackColor = UiTheme.CardBackColor,
                Margin = new Padding(0, 6, 0, 0),
                Visible = false
            };
            transferStack.Controls.Add(_exportSourceLabel);

            _importDetailLabel = new Label
            {
                Text = string.Empty,
                AutoSize = true,
                MaximumSize = new Size(380, 0),
                Font = UiTheme.SmallFont,
                ForeColor = UiTheme.SecondaryTextColor,
                BackColor = UiTheme.CardBackColor,
                Margin = new Padding(0, 4, 0, 0),
                Visible = false
            };
            transferStack.Controls.Add(_importDetailLabel);
            layout.Controls.Add(transferCard);

            var aboutCard = new Panel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                BackColor = UiTheme.CardBackColor,
                Padding = new Padding(17, 12, 14, 12),
                Margin = new Padding(0, 0, 0, 12)
            };
            aboutCard.Paint += delegate(object sender, PaintEventArgs e)
            {
                var p = (Panel)sender;
                if (p.Width <= 2 || p.Height <= 2) return;
                using (var pen = new Pen(UiTheme.BorderColor, 1f))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
                }
                int barW = Math.Max(3, p.Padding.Left / 5);
                using (var brush = new SolidBrush(UiTheme.AccentColor))
                {
                    e.Graphics.FillRectangle(brush, 0, 0, barW, p.Height);
                }
            };
            var aboutFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = new Padding(0),
                BackColor = UiTheme.CardBackColor
            };
            var aboutTitle = new Label
            {
                Text = "关于巡检助手",
                Font = UiTheme.BodyBoldFont,
                ForeColor = UiTheme.PrimaryTextColor,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 6)
            };
            var aboutVersion = new Label
            {
                Text = "巡检助手 v1.2.1",
                Font = UiTheme.SmallFont,
                ForeColor = UiTheme.SecondaryTextColor,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 6)
            };
            _aboutConfigPathLabel = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(380, 0),
                Font = UiTheme.SmallFont,
                ForeColor = UiTheme.SecondaryTextColor,
                Margin = new Padding(0, 0, 0, 3)
            };
            _aboutRootPathLabel = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(380, 0),
                Font = UiTheme.SmallFont,
                ForeColor = UiTheme.SecondaryTextColor,
                Margin = new Padding(0, 0, 0, 6)
            };
            var repo = new LinkLabel
            {
                Text = "https://github.com/weizehan1/inspection-widget",
                Font = UiTheme.SmallFont,
                LinkColor = UiTheme.AccentColor,
                ActiveLinkColor = UiTheme.AccentHoverColor,
                VisitedLinkColor = UiTheme.AccentColor,
                Margin = new Padding(0),
                AutoSize = true
            };
            repo.LinkClicked += delegate
            {
                try { Process.Start("https://github.com/weizehan1/inspection-widget"); }
                catch { }
            };
            _configPathLabel = _aboutConfigPathLabel;
            aboutFlow.Controls.Add(aboutTitle);
            aboutFlow.Controls.Add(aboutVersion);
            aboutFlow.Controls.Add(_aboutConfigPathLabel);
            aboutFlow.Controls.Add(_aboutRootPathLabel);
            aboutFlow.Controls.Add(repo);
            aboutCard.Controls.Add(aboutFlow);
            layout.Controls.Add(aboutCard);

            _pageMetas[page].UpdateContent = delegate(int contentWidth, int remainingHeight)
            {
                SetWrappedWidth(transferCard, contentWidth);
                int transferInner = Math.Max(L(160), contentWidth - transferCard.Padding.Horizontal);
                SetWrappedWidth(transferStack, transferInner);
                SetWrappedWidth(buttons, transferInner);
                SetWrappedWidth(_exportSourceLabel, transferInner);
                SetWrappedWidth(_importDetailLabel, transferInner);

                SetWrappedWidth(aboutCard, contentWidth);
                int aboutInner = Math.Max(L(160), contentWidth - aboutCard.Padding.Horizontal);
                SetWrappedWidth(aboutFlow, aboutInner);
                SetWrappedWidth(_aboutConfigPathLabel, aboutInner);
                SetWrappedWidth(_aboutRootPathLabel, aboutInner);
            };
            return page;
        }

        private static void AddSettingRow(TableLayoutPanel table, string label, Control control)
        {
            int row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var caption = new Label
            {
                Text = label,
                Font = UiTheme.BodyFont,
                ForeColor = UiTheme.SecondaryTextColor,
                AutoSize = true,
                Padding = new Padding(0, 8, 0, 8)
            };
            control.Margin = new Padding(0, 6, 0, 6);
            table.Controls.Add(caption, 0, row);
            table.Controls.Add(control, 1, row);
        }

        private static void AddSettingFullRow(TableLayoutPanel table, Control control)
        {
            int row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            control.Margin = new Padding(0, 4, 0, 8);
            table.Controls.Add(control, 0, row);
            table.SetColumnSpan(control, 2);
        }

        private void OnNavClicked(int index)
        {
            if (index == _currentGroup)
                return;
            if (!CommitDeviceOperation())
                return;
            SetGroup(index);
        }

        private void SetGroup(int index)
        {
            _currentGroup = index;
            for (int i = 0; i < _groupPanels.Length; i++)
            {
                bool active = i == index;
                if (_groupPanels[i].Visible != active)
                    _groupPanels[i].Visible = active;
                _navButtons[i].Font = active ? UiTheme.BodyBoldFont : UiTheme.BodyFont;
                _navButtons[i].ForeColor = active ? UiTheme.AccentColor : UiTheme.PrimaryTextColor;
                _navButtons[i].BackColor = active ? UiTheme.SelectedNavBackColor : UiTheme.SurfaceColor;
            }
            _navAccent.Location = new Point(0, _navButtons[index].Top);
            _navAccent.BringToFront();
            if (index == GroupTransfer)
                RefreshAboutLabels();
            UpdatePageLayout(_groupPanels[index]);
        }

        private bool CommitDeviceOperation()
        {
            string error;
            if (_deviceEditor != null && !_deviceEditor.CommitEdit(out error))
            {
                ShowError(GroupDevices, error, _deviceEditor.Grid);
                return false;
            }
            return true;
        }

        private void SyncAllControls()
        {
            _loading = true;
            try
            {
                if (_deviceEditor != null)
                    _deviceEditor.Refresh();
                RebuildCredentialRows();
                RebuildLinkRows();
                SyncStorageControls();
                RefreshAboutLabels();
                RefreshRootStatus();
                RefreshFolderPreview();
            }
            finally
            {
                _loading = false;
            }
        }

        private void SyncStorageControls()
        {
            if (_rootPathTextBox == null) return;
            _rootPathTextBox.Text = _draft.RootPath ?? string.Empty;
            if (_usbRootPathTextBox != null)
                _usbRootPathTextBox.Text = _draft.UsbSyncRootPath ?? string.Empty;
            if (_feedbackToolTip != null)
            {
                _feedbackToolTip.SetToolTip(_rootPathTextBox, _rootPathTextBox.Text);
                if (_usbRootPathTextBox != null)
                    _feedbackToolTip.SetToolTip(_usbRootPathTextBox, _usbRootPathTextBox.Text);
            }
            _folderPatternTextBox.Text = FolderNamePattern.Normalize(_draft.FolderPattern);
            _autoClearCheckBox.Checked = _draft.AutoClearClipboard;
            _uiScaleComboBox.SelectedIndex = UiScale.IndexForPercent(_draft.UiScalePercent);
            _modeAutoRadio.Checked = _draft.CaptureMode == CaptureMode.Auto;
            _modePrintWindowRadio.Checked = _draft.CaptureMode == CaptureMode.PrintWindowOnly;
            _modeBitBltRadio.Checked = _draft.CaptureMode == CaptureMode.BitBltOnly;
            if (_planModeCheckBox != null)
                _planModeCheckBox.Checked = _draft.PlanModeEnabled;
            if (_hideVisitedTrueRadio != null && _hideVisitedFalseRadio != null)
            {
                _hideVisitedTrueRadio.Checked = _draft.HideVisitedDevices;
                _hideVisitedFalseRadio.Checked = !_draft.HideVisitedDevices;
            }
            RefreshFolderPreview();
        }

        private void RefreshRootStatus()
        {
            bool visible;
            if (string.IsNullOrEmpty(_draft.RootPath))
            {
                _rootStatusLabel.Text = "尚未设置保存目录";
                _rootStatusLabel.ForeColor = UiTheme.ErrorColor;
                visible = true;
            }
            else if (!string.Equals(_draft.RootPath, _savedConfig.RootPath, StringComparison.OrdinalIgnoreCase))
            {
                _rootStatusLabel.Text = "已更改，应用后生效";
                _rootStatusLabel.ForeColor = UiTheme.SecondaryTextColor;
                visible = true;
            }
            else
            {
                _rootStatusLabel.Text = string.Empty;
                visible = false;
            }
            if (_rootStatusLabel.Visible != visible)
            {
                _rootStatusLabel.Visible = visible;
                InvalidatePageLayout(_groupPanels[GroupStorage]);
            }
            if (_feedbackToolTip != null && _rootPathTextBox != null)
                _feedbackToolTip.SetToolTip(_rootPathTextBox, _rootPathTextBox.Text);
        }

        private void RefreshFolderPreview()
        {
            string pattern = _folderPatternTextBox == null ? _draft.FolderPattern : _folderPatternTextBox.Text;
            string folder = FolderNamePattern.Format(FolderNamePattern.Normalize(pattern), DateTime.Now);
            _folderPreviewTextBox.Text = string.IsNullOrEmpty(_draft.RootPath)
                ? folder + "（未设置根目录）"
                : Path.Combine(_draft.RootPath, folder);
            if (_feedbackToolTip != null)
                _feedbackToolTip.SetToolTip(_folderPreviewTextBox, _folderPreviewTextBox.Text);
        }

        private void ChangeRootPath()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择截图保存根目录";
                dialog.ShowNewFolderButton = true;
                if (!string.IsNullOrEmpty(_draft.RootPath))
                    dialog.SelectedPath = _draft.RootPath;
                if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(dialog.SelectedPath))
                {
                    _draft.RootPath = dialog.SelectedPath;
                    _rootPathTextBox.Text = dialog.SelectedPath;
                    MarkDirty();
                    RefreshRootStatus();
                    RefreshFolderPreview();
                    UpdatePageLayout(_groupPanels[_currentGroup], true);
                    ClearFeedback();
                }
            }
        }

        private void ChangeUsbRootPath()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择 U盘巡检根目录";
                dialog.ShowNewFolderButton = false;
                if (!string.IsNullOrEmpty(_draft.UsbSyncRootPath))
                    dialog.SelectedPath = _draft.UsbSyncRootPath;
                if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(dialog.SelectedPath))
                {
                    _draft.UsbSyncRootPath = dialog.SelectedPath;
                    if (_usbRootPathTextBox != null)
                    {
                        _usbRootPathTextBox.Text = dialog.SelectedPath;
                        if (_feedbackToolTip != null)
                            _feedbackToolTip.SetToolTip(_usbRootPathTextBox, dialog.SelectedPath);
                    }
                    MarkDirty();
                    ClearFeedback();
                }
            }
        }

        private void ClearUsbRootPath()
        {
            if (string.IsNullOrEmpty(_draft.UsbSyncRootPath))
                return;
            _draft.UsbSyncRootPath = null;
            if (_usbRootPathTextBox != null)
            {
                _usbRootPathTextBox.Text = string.Empty;
                if (_feedbackToolTip != null)
                    _feedbackToolTip.SetToolTip(_usbRootPathTextBox, string.Empty);
            }
            MarkDirty();
            ClearFeedback();
        }

        private void SetCaptureMode(CaptureMode mode)
        {
            _draft.CaptureMode = mode;
            MarkDirty();
        }

        private void RebuildCredentialRows()
        {
            foreach (CredentialRow row in _credentialRows)
                DisposeCredentialRow(row);
            _credentialRows.Clear();
            if (_credentialRowsPanel != null)
                _credentialRowsPanel.Controls.Clear();

            for (int i = 0; i < _draft.Credentials.Count; i++)
            {
                CredentialRow row = CreateCredentialRow(_draft.Credentials[i]);
                _credentialRows.Add(row);
                _credentialRowsPanel.Controls.Add(row.Panel);
            }
            LayoutCredentialRows();
            UpdateCredentialAddState();
        }

        private CredentialRow CreateCredentialRow(CredentialDraft draft)
        {
            var row = new CredentialRow { Draft = draft };
            row.ToolTip = new ToolTip();
            row.Panel = new TableLayoutPanel
            {
                Height = 48,
                ColumnCount = 9,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 6),
                Padding = new Padding(8, 4, 8, 4),
                BackColor = UiTheme.CardBackColor
            };
            row.Panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            row.Panel.Paint += delegate(object sender, PaintEventArgs e)
            {
                var p = (Control)sender;
                if (p.Width <= 1 || p.Height <= 1) return;
                using (var pen = new Pen(UiTheme.BorderSoftColor, 1f))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
                }
            };
            int[] widths = { 104, 100, 104, 72, 44, 44, 44, 30, 30 };
            for (int i = 0; i < 9; i++)
            {
                row.Panel.ColumnStyles.Add(i == 3
                    ? new ColumnStyle(SizeType.Percent, 100f)
                    : new ColumnStyle(SizeType.Absolute, widths[i]));
            }

            row.NameBox = new TextBox
            {
                Text = draft.Title ?? string.Empty,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = new Padding(0, 0, 6, 0),
                Font = UiTheme.BodyFont,
                AccessibleName = "凭据名称"
            };
            NativeMethods.SetCueBanner(row.NameBox, "例如：运维账号");
            row.NameBox.TextChanged += delegate
            {
                draft.Title = row.NameBox.Text;
                if (!_loading) MarkDirty();
                RefreshRowStatus(row);
            };
            row.SecretBox = new TextBox
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = new Padding(0, 0, 6, 0),
                Font = UiTheme.BodyFont,
                PasswordChar = '●',
                Enabled = !draft.Cleared,
                AccessibleName = "凭据新内容"
            };
            NativeMethods.SetCueBanner(row.SecretBox, draft.IsNew ? "输入密码" : "留空不改");
            row.SecretBox.TextChanged += delegate
            {
                if (!_loading) MarkDirty();
                RefreshRowStatus(row);
            };
            row.HotkeyBox = new HotkeyTextBox
            {
                Text = draft.Hotkey ?? string.Empty,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = new Padding(0, 0, 6, 0),
                Font = UiTheme.SmallFont,
                AccessibleName = "凭据快捷键"
            };
            NativeMethods.SetCueBanner(row.HotkeyBox, "按键绑定");
            row.HotkeyBox.HotkeyCaptured += delegate(string hk)
            {
                draft.Hotkey = string.IsNullOrEmpty(hk) ? null : hk;
                if (!_loading) MarkDirty();
                ClearFeedback();
            };
            row.HotkeyBox.HotkeyRejected += delegate(string err)
            {
                ShowError(GroupCredentials, err, row.HotkeyBox);
            };
            row.StatusLabel = new Label
            {
                Dock = DockStyle.Fill,
                Font = UiTheme.SmallFont,
                AutoSize = false,
                AutoEllipsis = true,
                Padding = new Padding(0, 0, 4, 0),
                TextAlign = ContentAlignment.MiddleLeft
            };
            row.ClearButton = UiTheme.SecondaryButton("清除");
            row.ClearButton.Width = 40;
            row.ClearButton.Height = 30;
            row.ClearButton.Anchor = AnchorStyles.None;
            row.ClearButton.AccessibleName = "清除当前凭据内容";
            row.ClearButton.Margin = new Padding(2, 0, 2, 0);
            row.ClearButton.Click += delegate { ClearCredential(row); };
            row.UndoButton = UiTheme.SecondaryButton("撤销");
            row.UndoButton.Width = 40;
            row.UndoButton.Height = 30;
            row.UndoButton.Anchor = AnchorStyles.None;
            row.UndoButton.AccessibleName = "撤销清除凭据内容";
            row.UndoButton.Margin = new Padding(2, 0, 2, 0);
            row.UndoButton.Click += delegate { UndoClearCredential(row); };
            row.DeleteButton = UiTheme.SecondaryButton("删除");
            row.DeleteButton.Width = 40;
            row.DeleteButton.Height = 30;
            row.DeleteButton.ForeColor = UiTheme.ErrorColor;
            row.DeleteButton.Anchor = AnchorStyles.None;
            row.DeleteButton.AccessibleName = "删除当前凭据";
            row.DeleteButton.Margin = new Padding(2, 0, 2, 0);
            row.DeleteButton.Click += delegate { DeleteCredential(row); };
            row.UpButton = UiTheme.SecondaryButton("↑");
            row.UpButton.Width = 26;
            row.UpButton.Height = 30;
            row.UpButton.Anchor = AnchorStyles.None;
            row.UpButton.Margin = new Padding(2, 0, 2, 0);
            row.UpButton.TabStop = true;
            row.UpButton.AccessibleName = "凭据上移";
            row.UpButton.Click += delegate { MoveCredential(row, -1); };
            row.DownButton = UiTheme.SecondaryButton("↓");
            row.DownButton.Width = 26;
            row.DownButton.Height = 30;
            row.DownButton.Anchor = AnchorStyles.None;
            row.DownButton.Margin = new Padding(2, 0, 0, 0);
            row.DownButton.TabStop = true;
            row.DownButton.AccessibleName = "凭据下移";
            row.DownButton.Click += delegate { MoveCredential(row, 1); };

            row.Panel.Controls.Add(row.NameBox, 0, 0);
            row.Panel.Controls.Add(row.SecretBox, 1, 0);
            row.Panel.Controls.Add(row.HotkeyBox, 2, 0);
            row.Panel.Controls.Add(row.StatusLabel, 3, 0);
            row.Panel.Controls.Add(row.ClearButton, 4, 0);
            row.Panel.Controls.Add(row.UndoButton, 5, 0);
            row.Panel.Controls.Add(row.DeleteButton, 6, 0);
            row.Panel.Controls.Add(row.UpButton, 7, 0);
            row.Panel.Controls.Add(row.DownButton, 8, 0);
            row.ToolTip.SetToolTip(row.HotkeyBox, "按下组合键绑定，Backspace 清除");
            if (_layoutScaleApplied)
                UiTheme.ScaleLayout(row.Panel, _uiScaleFactor);
            RefreshRowStatus(row);
            return row;
        }

        private static void PrepareAutoScrollPanel(
            Panel panel,
            int totalHeight,
            int minimumWidth,
            out int rowWidth,
            out int offsetX,
            out int offsetY)
        {
            if (panel == null)
            {
                rowWidth = minimumWidth;
                offsetX = 0;
                offsetY = 0;
                return;
            }
            int desiredScrollY = Math.Max(0, -panel.AutoScrollPosition.Y);
            panel.AutoScrollMinSize = new Size(0, Math.Max(0, totalHeight));
            int maxScrollY = Math.Max(0, totalHeight - panel.ClientSize.Height);
            int clampedScrollY = Math.Max(0, Math.Min(desiredScrollY, maxScrollY));
            if (-panel.AutoScrollPosition.Y != clampedScrollY || panel.AutoScrollPosition.X != 0)
                panel.AutoScrollPosition = new Point(0, clampedScrollY);
            rowWidth = Math.Max(minimumWidth, panel.ClientSize.Width);
            offsetX = panel.AutoScrollPosition.X;
            offsetY = panel.AutoScrollPosition.Y;
        }

        private void LayoutCredentialRows()
        {
            int step = L(56);
            int totalHeight = _credentialRows.Count * step;
            int minimumWidth = L(520);
            int width;
            int offsetX;
            int offsetY;
            if (_credentialRowsPanel != null) _credentialRowsPanel.SuspendLayout();
            try
            {
                PrepareAutoScrollPanel(_credentialRowsPanel, totalHeight, minimumWidth, out width, out offsetX, out offsetY);
                if (_credentialRowsPanel != null)
                {
                    int rightGap = L(2);
                    width = Math.Max(minimumWidth, _credentialRowsPanel.ClientSize.Width - rightGap);
                    if (_credentialHeader != null)
                    {
                        int scrollPad = _credentialRowsPanel.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0;
                        _credentialHeader.Padding = new Padding(L(8), 0, L(8) + rightGap + scrollPad, 0);
                    }
                }
                for (int i = 0; i < _credentialRows.Count; i++)
                {
                    CredentialRow row = _credentialRows[i];
                    row.Panel.Width = width;
                    row.Panel.Location = new Point(offsetX, i * step + offsetY);
                    row.UpButton.Enabled = i > 0;
                    row.DownButton.Enabled = i < _credentialRows.Count - 1;
                    row.ClearButton.Enabled = !row.Draft.IsNew && !row.Draft.Cleared;
                    row.UndoButton.Enabled = row.Draft.Cleared;
                }
            }
            finally
            {
                if (_credentialRowsPanel != null) _credentialRowsPanel.ResumeLayout(true);
            }
        }

        private void RebuildLinkRows()
        {
            if (_linkRowsPanel == null)
                return;
            foreach (LinkRow row in _linkRows)
            {
                if (row.Panel != null) row.Panel.Dispose();
            }
            _linkRows.Clear();
            _linkRowsPanel.Controls.Clear();
            if (_linkEmptyLabel != null)
                _linkRowsPanel.Controls.Add(_linkEmptyLabel);

            for (int i = 0; i < _draft.QuickLinks.Count; i++)
            {
                LinkRow row = CreateLinkRow(_draft.QuickLinks[i]);
                _linkRows.Add(row);
                _linkRowsPanel.Controls.Add(row.Panel);
            }
            LayoutLinkRows();
            UpdateLinkAddState();
        }

        private LinkRow CreateLinkRow(QuickLinkEntry entry)
        {
            var row = new LinkRow { Draft = entry };
            row.Panel = new TableLayoutPanel
            {
                Height = 48,
                ColumnCount = 6,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 6),
                Padding = new Padding(8, 4, 8, 4),
                BackColor = UiTheme.CardBackColor
            };
            row.Panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            row.Panel.Paint += delegate(object sender, PaintEventArgs e)
            {
                var p = (Control)sender;
                if (p.Width <= 1 || p.Height <= 1) return;
                using (var pen = new Pen(UiTheme.BorderSoftColor, 1f))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
                }
            };
            int[] widths = { 46, 136, 180, 34, 34, 54 };
            for (int i = 0; i < 6; i++)
            {
                row.Panel.ColumnStyles.Add(i == 2
                    ? new ColumnStyle(SizeType.Percent, 100f)
                    : new ColumnStyle(SizeType.Absolute, widths[i]));
            }

            row.EnabledBox = new CheckBox
            {
                Checked = entry.Enabled,
                Dock = DockStyle.Fill,
                CheckAlign = ContentAlignment.MiddleCenter,
                AccessibleName = "启用快捷链接"
            };
            row.TitleBox = new TextBox
            {
                Text = entry.Title ?? string.Empty,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = new Padding(0, 0, 6, 0),
                Font = UiTheme.BodyFont,
                ForeColor = entry.Enabled ? UiTheme.PrimaryTextColor : UiTheme.SecondaryTextColor,
                AccessibleName = "快捷链接名称"
            };
            NativeMethods.SetCueBanner(row.TitleBox, "例如：监控大盘");
            row.TitleBox.TextChanged += delegate
            {
                entry.Title = row.TitleBox.Text;
                if (!_loading) MarkDirty();
            };

            row.UrlBox = new TextBox
            {
                Text = entry.Url ?? string.Empty,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = new Padding(0, 0, 6, 0),
                Font = UiTheme.BodyFont,
                ForeColor = entry.Enabled ? UiTheme.PrimaryTextColor : UiTheme.SecondaryTextColor,
                AccessibleName = "快捷链接网址"
            };
            NativeMethods.SetCueBanner(row.UrlBox, "https://...");
            row.UrlBox.TextChanged += delegate
            {
                entry.Url = row.UrlBox.Text;
                if (_feedbackToolTip != null)
                    _feedbackToolTip.SetToolTip(row.UrlBox, row.UrlBox.Text);
                if (!_loading) MarkDirty();
            };
            row.EnabledBox.CheckedChanged += delegate
            {
                entry.Enabled = row.EnabledBox.Checked;
                Color textColor = entry.Enabled ? UiTheme.PrimaryTextColor : UiTheme.SecondaryTextColor;
                row.TitleBox.ForeColor = textColor;
                row.UrlBox.ForeColor = textColor;
                if (!_loading) MarkDirty();
            };

            row.UpButton = UiTheme.SecondaryButton("↑");
            row.UpButton.Width = 30;
            row.UpButton.Height = 30;
            row.UpButton.Anchor = AnchorStyles.None;
            row.UpButton.Margin = new Padding(2, 0, 2, 0);
            row.UpButton.AccessibleName = "快捷链接上移";
            row.UpButton.Click += delegate { MoveLink(row, -1); };

            row.DownButton = UiTheme.SecondaryButton("↓");
            row.DownButton.Width = 30;
            row.DownButton.Height = 30;
            row.DownButton.Anchor = AnchorStyles.None;
            row.DownButton.Margin = new Padding(2, 0, 2, 0);
            row.DownButton.AccessibleName = "快捷链接下移";
            row.DownButton.Click += delegate { MoveLink(row, 1); };

            row.DeleteButton = UiTheme.SecondaryButton("删除");
            row.DeleteButton.Width = 48;
            row.DeleteButton.Height = 30;
            row.DeleteButton.ForeColor = UiTheme.ErrorColor;
            row.DeleteButton.Anchor = AnchorStyles.None;
            row.DeleteButton.Margin = new Padding(4, 0, 0, 0);
            row.DeleteButton.AccessibleName = "删除快捷链接";
            row.DeleteButton.Click += delegate { DeleteLink(row); };

            if (_feedbackToolTip != null)
            {
                _feedbackToolTip.SetToolTip(row.EnabledBox, "勾选后在主面板顶部显示");
                _feedbackToolTip.SetToolTip(row.UrlBox, row.UrlBox.Text);
                _feedbackToolTip.SetToolTip(row.UpButton, "上移");
                _feedbackToolTip.SetToolTip(row.DownButton, "下移");
                _feedbackToolTip.SetToolTip(row.DeleteButton, "删除该快捷链接");
            }

            row.Panel.Controls.Add(row.EnabledBox, 0, 0);
            row.Panel.Controls.Add(row.TitleBox, 1, 0);
            row.Panel.Controls.Add(row.UrlBox, 2, 0);
            row.Panel.Controls.Add(row.UpButton, 3, 0);
            row.Panel.Controls.Add(row.DownButton, 4, 0);
            row.Panel.Controls.Add(row.DeleteButton, 5, 0);
            if (_layoutScaleApplied)
                UiTheme.ScaleLayout(row.Panel, _uiScaleFactor);
            return row;
        }

        private void LayoutLinkRows()
        {
            if (_linkRowsPanel == null)
                return;
            int step = L(56);
            int totalHeight = _linkRows.Count * step;
            int minimumWidth = L(400);
            int width;
            int offsetX;
            int offsetY;
            _linkRowsPanel.SuspendLayout();
            try
            {
                PrepareAutoScrollPanel(_linkRowsPanel, totalHeight, minimumWidth, out width, out offsetX, out offsetY);
                int rightGap = L(2);
                width = Math.Max(minimumWidth, _linkRowsPanel.ClientSize.Width - rightGap);
                if (_linkHeader != null)
                {
                    int scrollPad = _linkRowsPanel.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0;
                    _linkHeader.Padding = new Padding(L(8), 0, L(8) + rightGap + scrollPad, 0);
                }
                for (int i = 0; i < _linkRows.Count; i++)
                {
                    LinkRow row = _linkRows[i];
                    row.Panel.Width = width;
                    row.Panel.Location = new Point(offsetX, i * step + offsetY);
                    row.UpButton.Enabled = i > 0;
                    row.DownButton.Enabled = i < _linkRows.Count - 1;
                }
                if (_linkEmptyLabel != null)
                {
                    _linkEmptyLabel.Visible = _linkRows.Count == 0;
                    if (_linkEmptyLabel.Visible)
                        _linkEmptyLabel.Location = new Point(offsetX, L(4) + offsetY);
                }
            }
            finally
            {
                _linkRowsPanel.ResumeLayout(true);
            }
        }

        private void AddLink()
        {
            if (_draft.QuickLinks.Count >= QuickLinkValidator.MaxLinks)
            {
                ShowError(GroupLinks,
                    "快捷链接：最多配置 " + QuickLinkValidator.MaxLinks + " 条。",
                    _addLinkButton);
                return;
            }
            var entry = new QuickLinkEntry { Enabled = true };
            _draft.QuickLinks.Add(entry);
            LinkRow row = CreateLinkRow(entry);
            _linkRows.Add(row);
            _linkRowsPanel.Controls.Add(row.Panel);
            LayoutLinkRows();
            UpdateLinkAddState();
            MarkDirty();
            _linkRowsPanel.ScrollControlIntoView(row.Panel);
            row.TitleBox.Focus();
            ClearFeedback();
        }

        private void DeleteLink(LinkRow row)
        {
            int index = _linkRows.IndexOf(row);
            if (index < 0) return;
            _draft.QuickLinks.RemoveAt(index);
            _linkRows.RemoveAt(index);
            _linkRowsPanel.Controls.Remove(row.Panel);
            if (row.Panel != null) row.Panel.Dispose();
            LayoutLinkRows();
            UpdateLinkAddState();
            MarkDirty();
        }

        private void MoveLink(LinkRow row, int delta)
        {
            int index = _linkRows.IndexOf(row);
            if (!ListReorder.Move(_draft.QuickLinks, index, delta))
                return;
            ListReorder.Move(_linkRows, index, delta);
            for (int i = 0; i < _linkRows.Count; i++)
                _linkRowsPanel.Controls.SetChildIndex(_linkRows[i].Panel, i);
            LayoutLinkRows();
            MarkDirty();
            row.TitleBox.Focus();
        }

        private void UpdateLinkAddState()
        {
            if (_linkCountLabel != null)
                _linkCountLabel.Text = "共 " + _draft.QuickLinks.Count + " / " + QuickLinkValidator.MaxLinks + " 条";
            if (_addLinkButton != null)
                _addLinkButton.Enabled = _draft.QuickLinks.Count < QuickLinkValidator.MaxLinks;
        }

        private int L(int value)
        {
            return _layoutScaleApplied ? PanelLayout.Scale(value, _uiScaleFactor) : value;
        }

        private void RefreshRowStatus(CredentialRow row)
        {
            if (row.Draft.Cleared)
            {
                row.StatusLabel.Text = "已清除";
                row.StatusLabel.ForeColor = UiTheme.WeakTextColor;
            }
            else if (row.Draft.IsNew)
            {
                row.StatusLabel.Text = "新凭据";
                row.StatusLabel.ForeColor = UiTheme.SecondaryTextColor;
            }
            else if (!string.IsNullOrEmpty(row.SecretBox.Text))
            {
                row.StatusLabel.Text = "已修改";
                row.StatusLabel.ForeColor = UiTheme.SecondaryTextColor;
            }
            else if (row.Draft.OriginalNeedsReset || string.IsNullOrEmpty(row.Draft.OriginalSecret))
            {
                row.StatusLabel.Text = "需重设";
                row.StatusLabel.ForeColor = UiTheme.ErrorColor;
            }
            else
            {
                row.StatusLabel.Text = "已设置";
                row.StatusLabel.ForeColor = UiTheme.SecondaryTextColor;
            }
            row.ToolTip.SetToolTip(row.StatusLabel, row.StatusLabel.Text);
        }

        private void AddCredential()
        {
            if (_draft.Credentials.Count >= CredentialRowCollector.MaxCredentials)
            {
                ShowError(GroupCredentials,
                    "凭据管理：凭据已达上限（" + CredentialRowCollector.MaxCredentials + " 条）。",
                    _addCredentialButton);
                return;
            }
            var draft = new CredentialDraft { IsNew = true };
            _draft.Credentials.Add(draft);
            CredentialRow row = CreateCredentialRow(draft);
            _credentialRows.Add(row);
            _credentialRowsPanel.Controls.Add(row.Panel);
            LayoutCredentialRows();
            UpdateCredentialAddState();
            MarkDirty();
            _credentialRowsPanel.ScrollControlIntoView(row.Panel);
            row.NameBox.Focus();
            ClearFeedback();
        }

        private void ClearCredential(CredentialRow row)
        {
            if (row.Draft.IsNew)
                return;
            row.Draft.Cleared = true;
            _loading = true;
            try
            {
                row.SecretBox.Text = string.Empty;
                row.SecretBox.Enabled = false;
            }
            finally
            {
                _loading = false;
            }
            LayoutCredentialRows();
            RefreshRowStatus(row);
            MarkDirty();
        }

        private void UndoClearCredential(CredentialRow row)
        {
            row.Draft.Cleared = false;
            row.SecretBox.Enabled = true;
            LayoutCredentialRows();
            RefreshRowStatus(row);
            MarkDirty();
        }

        private void DeleteCredential(CredentialRow row)
        {
            int index = _credentialRows.IndexOf(row);
            if (index < 0) return;
            _draft.Credentials.RemoveAt(index);
            _credentialRows.RemoveAt(index);
            _credentialRowsPanel.Controls.Remove(row.Panel);
            DisposeCredentialRow(row);
            LayoutCredentialRows();
            UpdateCredentialAddState();
            MarkDirty();
        }

        private void MoveCredential(CredentialRow row, int delta)
        {
            int index = _credentialRows.IndexOf(row);
            if (!ListReorder.Move(_draft.Credentials, index, delta))
                return;
            ListReorder.Move(_credentialRows, index, delta);
            for (int i = 0; i < _credentialRows.Count; i++)
                _credentialRowsPanel.Controls.SetChildIndex(_credentialRows[i].Panel, i);
            LayoutCredentialRows();
            MarkDirty();
            row.NameBox.Focus();
        }

        private void DisposeCredentialRow(CredentialRow row)
        {
            if (row.Panel != null)
            {
                row.Panel.Dispose();
            }
            if (row.ToolTip != null)
                row.ToolTip.Dispose();
        }

        private void UpdateCredentialAddState()
        {
            _credentialCountLabel.Text = "共 " + _draft.Credentials.Count + " / "
                + CredentialRowCollector.MaxCredentials + " 条";
            _addCredentialButton.Enabled = _draft.Credentials.Count < CredentialRowCollector.MaxCredentials;
        }

        private void MarkDirty()
        {
            if (_loading) return;
            _dirty = true;
            UpdateTitle();
        }

        private void UpdateTitle()
        {
            Text = "巡检助手设置" + (_dirty ? " *" : string.Empty);
        }

        private List<CredentialRowCollector.RowInput> CollectCredentialInputs()
        {
            var inputs = new List<CredentialRowCollector.RowInput>();
            for (int i = 0; i < _credentialRows.Count; i++)
            {
                CredentialRow row = _credentialRows[i];
                inputs.Add(new CredentialRowCollector.RowInput
                {
                    Title = row.NameBox.Text,
                    OriginalSecret = row.Draft.OriginalSecret,
                    Hotkey = row.HotkeyBox != null ? row.HotkeyBox.Text : row.Draft.Hotkey,
                    OriginalNeedsReset = row.Draft.OriginalNeedsReset,
                    IsNew = row.Draft.IsNew,
                    Cleared = row.Draft.Cleared,
                    NewPlainText = row.SecretBox.Text
                });
            }
            return inputs;
        }

        private bool TryBuildCandidate(out AppConfig candidate, out SettingsSaveCoordinator.Outcome outcome)
        {
            candidate = null;
            outcome = null;
            string deviceError;
            if (!_deviceEditor.CommitEdit(out deviceError)
                || !_deviceEditor.ValidateDraft(false, out deviceError))
            {
                ShowError(GroupDevices, deviceError, _deviceEditor.Grid);
                return false;
            }

            int scale = _uiScaleComboBox.SelectedIndex >= 0
                ? UiScale.PercentForIndex(_uiScaleComboBox.SelectedIndex)
                : _draft.UiScalePercent;
            CaptureMode mode = _modePrintWindowRadio.Checked
                ? CaptureMode.PrintWindowOnly
                : (_modeBitBltRadio.Checked ? CaptureMode.BitBltOnly : CaptureMode.Auto);
            bool planMode = _planModeCheckBox != null ? _planModeCheckBox.Checked : _draft.PlanModeEnabled;
            bool hideVisited = _hideVisitedTrueRadio != null ? _hideVisitedTrueRadio.Checked : _draft.HideVisitedDevices;
            string pattern = (_folderPatternTextBox.Text ?? string.Empty).Trim();
            outcome = SettingsSaveCoordinator.Build(
                _draft.SnapshotForTransferPreview(),
                _draft.DeviceNames(),
                CollectCredentialInputs(),
                _draft.QuickLinks,
                _draft.RootPath,
                _draft.UsbSyncRootPath,
                pattern,
                _autoClearCheckBox.Checked,
                scale,
                mode,
                planMode,
                hideVisited,
                _protector);
            if (!outcome.Saved)
            {
                Control focus = null;
                if (outcome.ErrorGroup == GroupDevices)
                    focus = _deviceEditor.Grid;
                else if (outcome.ErrorGroup == GroupLinks
                    && outcome.LinkErrorIndex >= 0
                    && outcome.LinkErrorIndex < _linkRows.Count)
                {
                    LinkRow linkRow = _linkRows[outcome.LinkErrorIndex];
                    focus = outcome.LinkErrorOnTitle ? (Control)linkRow.TitleBox : linkRow.UrlBox;
                }
                else if (outcome.ErrorGroup == GroupStorage)
                    focus = _folderPatternTextBox;
                else if (outcome.CredentialErrorIndex >= 0
                    && outcome.CredentialErrorIndex < _credentialRows.Count)
                {
                    CredentialRow row = _credentialRows[outcome.CredentialErrorIndex];
                    if (outcome.CredentialErrorOnTitle)
                        focus = row.NameBox;
                    else if (outcome.CredentialErrorOnHotkey && row.HotkeyBox != null)
                        focus = row.HotkeyBox;
                    else
                        focus = row.SecretBox;
                }
                ShowError(outcome.ErrorGroup, outcome.Error, focus);
                return false;
            }
            candidate = outcome.Result;
            return true;
        }

        private bool ApplyInternal(bool closeAfterSave)
        {
            ClearFeedback();
            if (_stale)
            {
                ShowError(GroupTransfer,
                    "配置已被其他程序修改。请关闭设置并重新打开，当前草稿不能覆盖新配置。",
                    _applyButton);
                return false;
            }

            AppConfig candidate;
            SettingsSaveCoordinator.Outcome buildOutcome;
            if (!TryBuildCandidate(out candidate, out buildOutcome))
                return false;

            List<DeviceRenameRecord> appliedRenames = new List<DeviceRenameRecord>(_draft.PendingDeviceRenames);
            bool appliedImportReplace = _draft.PendingImportDeviceReplace;

            SettingsApplyCoordinator.Outcome applied = SettingsApplyCoordinator.Execute(
                candidate, _store, _draft.SavedRevision, _draft.ImportPending);
            if (!applied.Applied)
            {
                if (applied.Stale)
                {
                    _stale = true;
                    _applyButton.Enabled = false;
                    _saveButton.Enabled = false;
                }
                ShowError(GroupTransfer, applied.Error, _applyButton);
                return false;
            }

            LastAppliedDeviceRenames = appliedRenames;
            LastAppliedImportDeviceReplace = appliedImportReplace;
            ResultConfig = applied.Result;
            _savedConfig = AppConfigCloner.Clone(applied.Result);
            _draft.AcceptSaved(applied.Result, applied.Save.Revision);
            _loading = true;
            try
            {
                RebuildCredentialRows();
                RebuildLinkRows();
                SyncStorageControls();
                RefreshAboutLabels();
            }
            finally
            {
                _loading = false;
            }
            _dirty = false;
            _stale = false;
            UpdateTitle();
            RefreshRootStatus();
            InvalidatePageLayout(_groupPanels[_currentGroup]);
            UpdatePageLayout(_groupPanels[_currentGroup], true);
            ShowSuccess("已应用");

            EventHandler handler = Saved;
            if (handler != null)
                handler(this, EventArgs.Empty);

            if (closeAfterSave)
            {
                _closingAfterSave = true;
                DialogResult = DialogResult.OK;
                Close();
            }
            return true;
        }

        private void ImportConfig()
        {
            ClearFeedback();
            if (_stale)
            {
                ShowError(GroupTransfer, "配置已在外部改变，请重新打开设置后再导入。", _applyButton);
                return;
            }
            if (!CommitDeviceOperation())
                return;
            if (!ValidateCredentialInputsForImport())
                return;

            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "选择要导入的配置";
                dialog.Filter = "JSON 配置 (*.json)|*.json|所有文件 (*.*)|*.*";
                dialog.CheckFileExists = true;
                dialog.Multiselect = false;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                ConfigImportDocument document;
                string error;
                if (!ConfigJsonReader.TryReadFile(dialog.FileName, out document, out error))
                {
                    ShowError(GroupTransfer, "导入未开始：" + error, _applyButton);
                    return;
                }

                AppConfig previewCurrent = _draft.SnapshotForTransferPreview();
                using (var preview = new ConfigTransferForm(
                    document, previewCurrent, _protector, _uiScaleFactor))
                {
                    if (preview.ShowDialog(this) != DialogResult.OK
                        || preview.Plan == null || preview.Result == null)
                    {
                        ShowInfo("已取消导入，当前草稿未改变。");
                        return;
                    }

                    _loading = true;
                    try
                    {
                        _draft.ApplyPlan(preview.Plan, preview.Result.Options.MergeDevicesOnly);
                        if (preview.Plan.CredentialsReplaced)
                            RebuildCredentialRows();
                        RebuildLinkRows();
                        SyncStorageControls();
                        RefreshRootStatus();
                        RefreshAboutLabels();
                    }
                    finally
                    {
                        _loading = false;
                    }
                    MarkDirty();
                    _dirty = true;
                    UpdateTitle();
                    _importDetailLabel.Text = "已导入到草稿：" + Path.GetFileName(document.SourcePath) + "（需应用后生效）";
                    _importDetailLabel.Visible = true;
                    InvalidatePageLayout(_groupPanels[_currentGroup]);
                    UpdatePageLayout(_groupPanels[_currentGroup], true);
                    ShowInfo("已导入到草稿，点击“应用”生效");
                }
            }
        }

        /// <summary>
        /// 导入只改草稿；先确认当前凭据输入可保留。未替换凭据时保留原行与
        /// 尚未提交的明文输入，不重建控件，避免把用户刚输入的内容清掉。
        /// </summary>
        private bool ValidateCredentialInputsForImport()
        {
            for (int i = 0; i < _credentialRows.Count; i++)
            {
                CredentialRow row = _credentialRows[i];
                if (string.IsNullOrWhiteSpace(row.NameBox.Text))
                {
                    ShowError(GroupCredentials,
                        "凭据管理：第 " + (i + 1) + " 条凭据名称不能为空；导入前请先修正当前输入。",
                        row.NameBox);
                    return false;
                }
                if (row.Draft.IsNew && string.IsNullOrEmpty(row.SecretBox.Text))
                {
                    ShowError(GroupCredentials,
                        "凭据管理：第 " + (i + 1) + " 条新凭据内容不能为空；导入前请先修正当前输入。",
                        row.SecretBox);
                    return false;
                }
            }
            return true;
        }

        private void ExportConfig(bool fullBackup)
        {
            ClearFeedback();
            if (_stale)
            {
                ShowError(GroupTransfer, "配置已在外部改变，请重新打开设置后再导出。", _applyButton);
                return;
            }
            if (!CommitDeviceOperation())
                return;

            if (_dirty)
            {
                using (var choice = new ExportChoiceDialog())
                {
                    UiTheme.ScaleLayout(choice, _uiScaleFactor);
                    DialogResult result = choice.ShowDialog(this);
                    if (result == DialogResult.Cancel)
                    {
                        ShowInfo("已取消导出。");
                        return;
                    }
                    if (result == DialogResult.Yes)
                    {
                        if (!ApplyInternal(false))
                            return;
                    }
                }
            }

            AppConfig source = AppConfigCloner.Clone(_savedConfig);
            string mode = fullBackup
                ? ConfigJsonReader.CredentialModeDpapi
                : ConfigJsonReader.CredentialModeExcluded;
            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = fullBackup ? "导出本机完整备份" : "导出普通配置";
                dialog.Filter = "JSON 配置 (*.json)|*.json";
                dialog.OverwritePrompt = true;
                dialog.AddExtension = true;
                dialog.DefaultExt = "json";
                dialog.FileName = (fullBackup ? "巡检助手-完整备份-" : "巡检助手-配置-")
                    + DateTime.Now.ToString("yyyyMMdd") + ".json";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    ShowInfo("已取消导出。");
                    return;
                }

                string error;
                if (!ConfigTransfer.TryExportFile(source, mode, dialog.FileName,
                    _store.AuthoritativePath, out error))
                {
                    ShowError(GroupTransfer, "导出失败：" + error, _applyButton);
                    return;
                }
                _exportSourceLabel.Text = "已导出：" + dialog.FileName
                    + (fullBackup ? "（完整备份）" : "（普通配置）");
                _exportSourceLabel.Visible = true;
                InvalidatePageLayout(_groupPanels[_currentGroup]);
                UpdatePageLayout(_groupPanels[_currentGroup], true);
                ShowSuccess("导出完成：" + Path.GetFileName(dialog.FileName));
            }
        }

        private void RefreshAboutLabels()
        {
            if (_aboutConfigPathLabel == null) return;
            _aboutConfigPathLabel.Text = "配置文件：" + _store.AuthoritativePath;
            _aboutRootPathLabel.Text = "截图根目录："
                + (string.IsNullOrEmpty(_draft.RootPath) ? "尚未设置" : _draft.RootPath);
        }

        private void ShowError(int group, string message, Control focus)
        {
            if (group >= 0 && group < _groupPanels.Length)
                SetGroup(group);
            _feedbackLabel.ForeColor = UiTheme.ErrorColor;
            _feedbackLabel.Text = message;
            _feedbackToolTip.SetToolTip(_feedbackLabel, message);
            if (focus != null)
                focus.Focus();
        }

        private void ShowInfo(string message)
        {
            _feedbackLabel.ForeColor = UiTheme.SecondaryTextColor;
            _feedbackLabel.Text = message;
            _feedbackToolTip.SetToolTip(_feedbackLabel, message);
        }

        private void ShowSuccess(string message)
        {
            _feedbackLabel.ForeColor = UiTheme.SuccessColor;
            _feedbackLabel.Text = message;
            _feedbackToolTip.SetToolTip(_feedbackLabel, message);
        }

        private void ClearFeedback()
        {
            if (_feedbackLabel != null)
                _feedbackLabel.Text = string.Empty;
        }

        private sealed class DiscardChangesDialog : Form
        {
            public DiscardChangesDialog()
            {
                Text = "未保存的更改";
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MinimizeBox = false;
                MaximizeBox = false;
                // 父窗口传入的因子已含系统 DPI，这里只做一次手动缩放。
                AutoScaleMode = AutoScaleMode.None;
                ClientSize = new Size(440, 162);
                BackColor = UiTheme.PageBackColor;
                Font = UiTheme.BodyFont;

                var content = DialogCardHelper.CreateBodyHost(
                    UiTheme.WarningColor,
                    "当前有未应用的更改，确定放弃？");
                var buttons = DialogCardHelper.CreateButtonDock();

                var discard = UiTheme.DangerButton("放弃更改");
                discard.Width = 112;
                discard.Margin = new Padding(0);
                discard.DialogResult = DialogResult.Yes;
                var keep = UiTheme.SecondaryButton("继续编辑");
                keep.Width = 104;
                keep.Margin = new Padding(0, 0, 8, 0);
                keep.DialogResult = DialogResult.No;
                buttons.Controls.Add(discard);
                buttons.Controls.Add(keep);
                Controls.Add(content);
                Controls.Add(buttons);
                AcceptButton = keep;
                CancelButton = keep;
            }

            protected override void OnLoad(EventArgs e)
            {
                base.OnLoad(e);
                SettingsForm.FitDialogToOwner(this);
            }
        }

        private sealed class ExportChoiceDialog : Form
        {
            public ExportChoiceDialog()
            {
                Text = "存在未应用的草稿";
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MinimizeBox = false;
                MaximizeBox = false;
                // 父窗口传入的因子已含系统 DPI，这里只做一次手动缩放。
                AutoScaleMode = AutoScaleMode.None;
                ClientSize = new Size(500, 192);
                BackColor = UiTheme.PageBackColor;
                Font = UiTheme.BodyFont;

                var content = DialogCardHelper.CreateBodyHost(
                    UiTheme.AccentColor,
                    "存在未应用的草稿，是否先应用再导出？");
                var buttons = DialogCardHelper.CreateButtonDock();

                var apply = UiTheme.PrimaryButton("应用后导出");
                apply.Width = 112;
                apply.Margin = new Padding(0);
                apply.DialogResult = DialogResult.Yes;
                var saved = UiTheme.SecondaryButton("导出已保存配置");
                saved.Width = 140;
                saved.Margin = new Padding(0, 0, 8, 0);
                saved.DialogResult = DialogResult.No;
                var cancel = UiTheme.SecondaryButton("取消");
                cancel.Width = 80;
                cancel.Margin = new Padding(0, 0, 8, 0);
                cancel.DialogResult = DialogResult.Cancel;
                buttons.Controls.Add(apply);
                buttons.Controls.Add(saved);
                buttons.Controls.Add(cancel);
                Controls.Add(content);
                Controls.Add(buttons);
                AcceptButton = apply;
                CancelButton = cancel;
            }

            protected override void OnLoad(EventArgs e)
            {
                base.OnLoad(e);
                SettingsForm.FitDialogToOwner(this);
            }
        }

        /// <summary>确认弹窗也受 owner 所在屏幕工作区约束，按钮区不被裁掉。</summary>
        private static void FitDialogToOwner(Form dialog)
        {
            Form owner = dialog.Owner;
            Rectangle work = owner != null
                ? Screen.FromControl(owner).WorkingArea
                : Screen.PrimaryScreen.WorkingArea;
            int availableWidth = Math.Max(320, work.Width - 16);
            int availableHeight = Math.Max(240, work.Height - 16);
            int width = Math.Min(dialog.Width, availableWidth);
            int height = Math.Min(dialog.Height, availableHeight);
            dialog.Size = new Size(width, height);
            dialog.Location = PanelLayout.CenterInWorkingArea(work, dialog.Size);
        }
    }
}
