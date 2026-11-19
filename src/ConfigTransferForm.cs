using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using InspectionWidget.Capture;
using InspectionWidget.Config;
using InspectionWidget.Credentials;

namespace InspectionWidget
{
    /// <summary>导入候选预览：只决定如何改写窗口草稿，不在这里写正式配置。</summary>
    public sealed class ConfigTransferForm : Form
    {
        private readonly ConfigImportDocument _document;
        private readonly AppConfig _current;
        private readonly ICredentialProtector _protector;
        private readonly RadioButton _mergeRadio;
        private readonly RadioButton _replaceRadio;
        private readonly CheckBox _importRootCheck;
        private readonly CheckBox _replaceCredentialsCheck;
        private readonly CheckBox _restoreLocalCheck;
        private readonly Label _summaryLabel;
        private readonly TextBox _deviceChangeBox;
        private readonly Label _errorLabel;
        private readonly ToolTip _errorToolTip = new ToolTip();
        private readonly double _uiScaleFactor;
        private Icon _ownedAppIcon;

        public ConfigImportPlan Plan { get; private set; }

        public ConfigImportFormResult Result { get; private set; }

        public ConfigTransferForm(ConfigImportDocument document, AppConfig current,
            ICredentialProtector protector, double uiScaleFactor = 1.0)
        {
            if (document == null) throw new ArgumentNullException("document");
            if (current == null) throw new ArgumentNullException("current");
            if (uiScaleFactor <= 0)
                throw new ArgumentOutOfRangeException("uiScaleFactor");
            _document = document;
            _current = current;
            _protector = protector;
            _uiScaleFactor = uiScaleFactor;
            UiTheme.UseFontScale(_uiScaleFactor);

            Text = "导入配置预览";
            Font = UiTheme.BodyFont;
            BackColor = UiTheme.PageBackColor;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            MaximizeBox = true;
            // 与设置窗一致：只使用应用档位的手动缩放，不让系统 DPI 再乘一次。
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(780, 640);
            MinimumSize = new Size(640, 500);
            _ownedAppIcon = AppIcon.Load();
            if (_ownedAppIcon != null) Icon = _ownedAppIcon;

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
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60f));

            var body = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Margin = new Padding(0),
                BackColor = UiTheme.PageBackColor,
                Padding = new Padding(20, 16, 20, 12)
            };
            var bodyLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Margin = new Padding(0),
                BackColor = UiTheme.PageBackColor
            };
            bodyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            body.Controls.Add(bodyLayout);

            var title = new Label
            {
                Text = "导入到当前草稿",
                Font = UiTheme.TitleFont,
                ForeColor = UiTheme.PrimaryTextColor,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 6)
            };
            bodyLayout.Controls.Add(title);

            string format = document.Kind == ConfigSourceKind.Exchange
                ? "标准交换格式 v1（" + document.CredentialMode + "）"
                : "旧版 AppConfig JSON";
            int currentCount = current.Devices == null ? 0 : current.Devices.Count;
            int importedCount = document.Config.Devices == null ? 0 : document.Config.Devices.Count;
            var source = new Label
            {
                Text = "来源文件：" + document.SourcePath
                    + "\n文件格式：" + format
                    + "\n设备数量：当前 " + currentCount + " 台，源文件 " + importedCount + " 台",
                Font = UiTheme.SmallFont,
                ForeColor = UiTheme.SecondaryTextColor,
                AutoSize = true,
                MaximumSize = new Size(580, 0),
                Margin = new Padding(0, 0, 0, 12)
            };
            bodyLayout.Controls.Add(source);

            FlowLayoutPanel modeLayout;
            Panel modeCard = UiTheme.CreateSectionCard("导入方式", out modeLayout);
            _mergeRadio = new RadioButton
            {
                Text = "仅合并设备名单（默认）",
                Font = UiTheme.BodyFont,
                ForeColor = UiTheme.PrimaryTextColor,
                BackColor = UiTheme.CardBackColor,
                AutoSize = true,
                Checked = true,
                Margin = new Padding(0, 0, 0, 4)
            };
            _replaceRadio = new RadioButton
            {
                Text = "替换参与导入的配置",
                Font = UiTheme.BodyFont,
                ForeColor = UiTheme.PrimaryTextColor,
                BackColor = UiTheme.CardBackColor,
                Margin = new Padding(0),
                AutoSize = true
            };
            modeLayout.Controls.Add(_mergeRadio);
            modeLayout.Controls.Add(_replaceRadio);
            bodyLayout.Controls.Add(modeCard);

            FlowLayoutPanel optionsLayout;
            Panel optionsCard = UiTheme.CreateSectionCard("替换模式选项", out optionsLayout);
            _importRootCheck = new CheckBox
            {
                Text = "导入保存目录与 U盘目录",
                Font = UiTheme.BodyFont,
                ForeColor = UiTheme.PrimaryTextColor,
                BackColor = UiTheme.CardBackColor,
                AutoSize = true,
                Enabled = document.Has("RootPath") || document.Has("UsbSyncRootPath"),
                Margin = new Padding(0, 0, 0, 4)
            };
            _replaceCredentialsCheck = new CheckBox
            {
                Text = "替换凭据",
                Font = UiTheme.BodyFont,
                ForeColor = UiTheme.PrimaryTextColor,
                BackColor = UiTheme.CardBackColor,
                AutoSize = true,
                Enabled = document.CredentialsPresent,
                Margin = new Padding(0, 0, 0, 4)
            };
            _restoreLocalCheck = new CheckBox
            {
                Text = "恢复窗口位置与上次设备",
                Font = UiTheme.BodyFont,
                ForeColor = UiTheme.PrimaryTextColor,
                BackColor = UiTheme.CardBackColor,
                Margin = new Padding(0),
                AutoSize = true,
                Enabled = document.IsFullBackup
            };
            optionsLayout.Controls.Add(_importRootCheck);
            optionsLayout.Controls.Add(_replaceCredentialsCheck);
            optionsLayout.Controls.Add(_restoreLocalCheck);
            bodyLayout.Controls.Add(optionsCard);

            var previewTable = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                BackColor = UiTheme.CardBackColor,
                Margin = new Padding(0)
            };
            previewTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            _summaryLabel = new Label
            {
                Font = UiTheme.SmallFont,
                ForeColor = UiTheme.SecondaryTextColor,
                BackColor = UiTheme.CardBackColor,
                AutoSize = true,
                MaximumSize = new Size(560, 0),
                Margin = new Padding(0, 0, 0, 8)
            };
            previewTable.Controls.Add(_summaryLabel);

            _deviceChangeBox = new TextBox
            {
                ReadOnly = true,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                Font = UiTheme.SmallFont,
                BackColor = UiTheme.PageBackColor,
                ForeColor = UiTheme.PrimaryTextColor,
                BorderStyle = BorderStyle.FixedSingle,
                Dock = DockStyle.Top,
                Height = 140,
                Margin = new Padding(0, 0, 0, 8),
                AccessibleName = "设备变化预览"
            };
            previewTable.Controls.Add(_deviceChangeBox);

            var credentialNote = new Label
            {
                Text = document.CredentialsPresent
                    ? CredentialSummary(document)
                    : "凭据：源文件未参与导入，当前凭据保持不变。",
                Font = UiTheme.SmallFont,
                ForeColor = UiTheme.WeakTextColor,
                BackColor = UiTheme.CardBackColor,
                AutoSize = true,
                MaximumSize = new Size(560, 0)
            };
            previewTable.Controls.Add(credentialNote);
            Panel previewCard = UiTheme.CreateSectionCard("变更摘要与设备预览", previewTable);
            bodyLayout.Controls.Add(previewCard);
            body.SizeChanged += delegate
            {
                int scrollReserve = body.VerticalScroll.Visible ? 0 : SystemInformation.VerticalScrollBarWidth;
                int bodyMaxW = Math.Max(260, body.ClientSize.Width - body.Padding.Horizontal - scrollReserve);
                if (bodyLayout.MaximumSize.Width != bodyMaxW)
                    bodyLayout.MaximumSize = new Size(bodyMaxW, 0);
                int cardInner = Math.Max(220, bodyMaxW - previewCard.Padding.Horizontal);
                source.MaximumSize = new Size(Math.Max(120, bodyMaxW - source.Margin.Horizontal), 0);
                _mergeRadio.MaximumSize = new Size(Math.Max(120, cardInner - _mergeRadio.Margin.Horizontal), 0);
                _replaceRadio.MaximumSize = new Size(Math.Max(120, cardInner - _replaceRadio.Margin.Horizontal), 0);
                _importRootCheck.MaximumSize = new Size(Math.Max(120, cardInner - _importRootCheck.Margin.Horizontal), 0);
                _replaceCredentialsCheck.MaximumSize = new Size(Math.Max(120, cardInner - _replaceCredentialsCheck.Margin.Horizontal), 0);
                _restoreLocalCheck.MaximumSize = new Size(Math.Max(120, cardInner - _restoreLocalCheck.Margin.Horizontal), 0);
                _summaryLabel.MaximumSize = new Size(Math.Max(120, cardInner - _summaryLabel.Margin.Horizontal), 0);
                credentialNote.MaximumSize = new Size(Math.Max(120, cardInner - credentialNote.Margin.Horizontal), 0);
            };
            root.Controls.Add(body, 0, 0);

            var bottom = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(16, 10, 16, 10),
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

            _errorLabel = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                AutoEllipsis = true,
                ForeColor = UiTheme.ErrorColor,
                BackColor = UiTheme.CardBackColor,
                Font = UiTheme.SmallFont,
                TextAlign = ContentAlignment.MiddleLeft
            };
            bottom.Controls.Add(_errorLabel, 0, 0);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0),
                BackColor = UiTheme.CardBackColor,
                Padding = new Padding(0, 2, 0, 0)
            };
            var importButton = UiTheme.PrimaryButton("导入到草稿");
            importButton.Width = 120;
            importButton.Margin = new Padding(0);
            importButton.Click += delegate { Accept(); };
            var cancelButton = UiTheme.SecondaryButton("取消");
            cancelButton.Width = 88;
            cancelButton.Margin = new Padding(0, 0, 8, 0);
            cancelButton.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            buttons.Controls.Add(importButton);
            buttons.Controls.Add(cancelButton);
            bottom.Controls.Add(buttons, 1, 0);
            root.Controls.Add(bottom, 0, 1);
            CancelButton = cancelButton;
            AcceptButton = importButton;

            Controls.Add(root);
            UiTheme.ScaleLayout(this, _uiScaleFactor);

            _mergeRadio.CheckedChanged += delegate { UpdateMode(); };
            _replaceRadio.CheckedChanged += delegate { UpdateMode(); };
            _importRootCheck.CheckedChanged += delegate { RefreshSummary(); };
            _replaceCredentialsCheck.CheckedChanged += delegate { RefreshSummary(); };
            _restoreLocalCheck.CheckedChanged += delegate { RefreshSummary(); };
            UpdateMode();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Form owner = Owner;
            Rectangle work = owner != null
                ? Screen.FromControl(owner).WorkingArea
                : Screen.PrimaryScreen.WorkingArea;
            int availableWidth = Math.Max(320, work.Width - 16);
            int availableHeight = Math.Max(240, work.Height - 16);
            int minimumWidth = Math.Min(MinimumSize.Width, availableWidth);
            int minimumHeight = Math.Min(MinimumSize.Height, availableHeight);
            MinimumSize = new Size(minimumWidth, minimumHeight);
            int width = Math.Max(minimumWidth, Math.Min(Width, availableWidth));
            int height = Math.Max(minimumHeight, Math.Min(Height, availableHeight));
            Size = new Size(width, height);
            Location = PanelLayout.CenterInWorkingArea(work, Size);
        }

        private void UpdateMode()
        {
            bool replace = _replaceRadio.Checked;
            _importRootCheck.Enabled = replace && _document.Has("RootPath");
            _replaceCredentialsCheck.Enabled = replace && _document.CredentialsPresent;
            _restoreLocalCheck.Enabled = replace && _document.IsFullBackup;
            if (!replace)
            {
                _importRootCheck.Checked = false;
                _replaceCredentialsCheck.Checked = false;
                _restoreLocalCheck.Checked = false;
            }
            RefreshSummary();
        }

        private void RefreshSummary()
        {
            _errorLabel.Text = string.Empty;
            _errorToolTip.SetToolTip(_errorLabel, null);
            ConfigImportOptions options = CurrentOptions();
            ConfigImportPlan plan;
            string error;
            if (!ConfigTransfer.TryBuildPlan(_document, _current, options, _protector,
                out plan, out error))
            {
                _summaryLabel.Text = "当前选择无法生成导入方案。";
                _deviceChangeBox.Text = string.Empty;
                _errorLabel.Text = error;
                _errorToolTip.SetToolTip(_errorLabel, error);
                return;
            }
            var lines = new List<string>();
            lines.Add("设备：新增 " + plan.AddedDeviceCount + "，保留 " + plan.RetainedDeviceCount
                + "，移除 " + plan.RemovedDeviceCount + "，最终 " + plan.Devices.Count + " 台。");
            for (int i = 0; i < plan.SettingChanges.Count; i++)
                lines.Add(plan.SettingChanges[i]);
            for (int i = 0; i < plan.FieldSources.Count; i++)
                lines.Add("来源：" + plan.FieldSources[i]);
            if (plan.CredentialsReplaced)
            {
                lines.Add("凭据：可用 " + plan.UsableCredentialCount
                    + " 条，需重设 " + plan.ResetCredentialCount + " 条。");
            }
            _summaryLabel.Text = string.Join("\n", lines.ToArray());
            _deviceChangeBox.Text = ConfigTransfer.BuildDeviceChangeText(
                _current.Devices, plan.Devices);
        }

        private ConfigImportOptions CurrentOptions()
        {
            return new ConfigImportOptions
            {
                MergeDevicesOnly = !_replaceRadio.Checked,
                ImportRootPath = _importRootCheck.Checked,
                ReplaceCredentials = _replaceCredentialsCheck.Checked,
                RestoreLocalFields = _restoreLocalCheck.Checked
            };
        }

        private void Accept()
        {
            ConfigImportOptions options = CurrentOptions();
            ConfigImportPlan plan;
            string error;
            if (!ConfigTransfer.TryBuildPlan(_document, _current, options, _protector,
                out plan, out error))
            {
                _errorLabel.Text = error;
                _errorToolTip.SetToolTip(_errorLabel, error);
                return;
            }
            Plan = plan;
            Result = new ConfigImportFormResult { Options = options };
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_errorToolTip != null)
                    _errorToolTip.Dispose();
                if (_ownedAppIcon != null)
                {
                    Icon = null;
                    _ownedAppIcon.Dispose();
                    _ownedAppIcon = null;
                }
            }
            base.Dispose(disposing);
        }

        private static string CredentialSummary(ConfigImportDocument document)
        {
            int count = document.Config.Credentials == null ? 0 : document.Config.Credentials.Count;
            return "凭据：" + count + " 条；仅显示数量与可用/需重设状态，不显示密文或明文。";
        }
    }

    public sealed class ConfigImportFormResult
    {
        public ConfigImportOptions Options;
    }
}
