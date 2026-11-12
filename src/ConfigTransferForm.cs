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
        private readonly double _uiScaleFactor;

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
            MaximizeBox = false;
            // 与设置窗一致：只使用应用档位的手动缩放，不让系统 DPI 再乘一次。
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(760, 620);
            MinimumSize = new Size(680, 540);
            Icon appIcon = AppIcon.Load();
            if (appIcon != null) Icon = appIcon;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(18),
                BackColor = UiTheme.PageBackColor
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = UiTheme.PageBackColor };
            var bodyLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1,
                BackColor = UiTheme.PageBackColor
            };
            body.Controls.Add(bodyLayout);

            var title = new Label
            {
                Text = "导入到当前草稿",
                Font = UiTheme.TitleFont,
                ForeColor = UiTheme.PrimaryTextColor,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 8)
            };
            bodyLayout.Controls.Add(title);

            string format = document.Kind == ConfigSourceKind.Exchange
                ? "新交换格式 v1 · " + document.CredentialMode
                : "旧裸 AppConfig JSON";
            var source = new Label
            {
                Text = "来源：" + document.SourcePath + "\n格式：" + format,
                Font = UiTheme.SmallFont,
                ForeColor = UiTheme.WeakTextColor,
                AutoSize = true,
                MaximumSize = new Size(680, 0),
                Margin = new Padding(0, 0, 0, 12)
            };
            bodyLayout.Controls.Add(source);

            int currentCount = current.Devices == null ? 0 : current.Devices.Count;
            int importedCount = document.Config.Devices == null ? 0 : document.Config.Devices.Count;
            var counts = new Label
            {
                Text = "设备：当前 " + currentCount + " 台，源文件 " + importedCount + " 台",
                Font = UiTheme.BodyFont,
                ForeColor = UiTheme.PrimaryTextColor,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 10)
            };
            bodyLayout.Controls.Add(counts);

            var modeGroup = new GroupBox
            {
                Text = "导入方式",
                Font = UiTheme.BodyFont,
                AutoSize = true,
                Dock = DockStyle.Top,
                Padding = new Padding(12),
                Margin = new Padding(0, 0, 0, 10)
            };
            var modeLayout = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false
            };
            _mergeRadio = new RadioButton
            {
                Text = "仅合并设备名单（默认）",
                Font = UiTheme.BodyFont,
                AutoSize = true,
                Checked = true
            };
            _replaceRadio = new RadioButton
            {
                Text = "替换参与导入的配置",
                Font = UiTheme.BodyFont,
                AutoSize = true
            };
            modeLayout.Controls.Add(_mergeRadio);
            modeLayout.Controls.Add(_replaceRadio);
            modeGroup.Controls.Add(modeLayout);
            bodyLayout.Controls.Add(modeGroup);

            var optionsGroup = new GroupBox
            {
                Text = "替换模式选项",
                Font = UiTheme.BodyFont,
                AutoSize = true,
                Dock = DockStyle.Top,
                Padding = new Padding(12),
                Margin = new Padding(0, 0, 0, 10)
            };
            var optionsLayout = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false
            };
            _importRootCheck = new CheckBox
            {
                Text = "导入截图保存目录",
                Font = UiTheme.BodyFont,
                AutoSize = true,
                Enabled = document.Has("RootPath")
            };
            _replaceCredentialsCheck = new CheckBox
            {
                Text = "替换凭据（整表替换；空数组表示清空）",
                Font = UiTheme.BodyFont,
                AutoSize = true,
                Enabled = document.CredentialsPresent
            };
            _restoreLocalCheck = new CheckBox
            {
                Text = "恢复本机字段（窗口位置 / 上次设备 / 托盘提示）",
                Font = UiTheme.BodyFont,
                AutoSize = true,
                Enabled = document.IsFullBackup
            };
            optionsLayout.Controls.Add(_importRootCheck);
            optionsLayout.Controls.Add(_replaceCredentialsCheck);
            optionsLayout.Controls.Add(_restoreLocalCheck);
            optionsGroup.Controls.Add(optionsLayout);
            bodyLayout.Controls.Add(optionsGroup);

            _summaryLabel = new Label
            {
                Font = UiTheme.SmallFont,
                ForeColor = UiTheme.SecondaryTextColor,
                AutoSize = true,
                MaximumSize = new Size(680, 0),
                Margin = new Padding(0, 0, 0, 10)
            };
            bodyLayout.Controls.Add(_summaryLabel);

            _deviceChangeBox = new TextBox
            {
                ReadOnly = true,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = false,
                Font = UiTheme.SmallFont,
                BackColor = Color.White,
                ForeColor = UiTheme.PrimaryTextColor,
                Height = 150,
                Width = 680,
                Margin = new Padding(0, 0, 0, 10),
                AccessibleName = "设备变化预览"
            };
            bodyLayout.Controls.Add(_deviceChangeBox);

            var credentialNote = new Label
            {
                Text = document.CredentialsPresent
                    ? CredentialSummary(document)
                    : "凭据：源文件未参与导入，当前凭据保持不变。",
                Font = UiTheme.SmallFont,
                ForeColor = UiTheme.WeakTextColor,
                AutoSize = true,
                MaximumSize = new Size(680, 0)
            };
            bodyLayout.Controls.Add(credentialNote);
            root.Controls.Add(body, 0, 0);

            _errorLabel = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(680, 0),
                ForeColor = UiTheme.ErrorColor,
                Font = UiTheme.SmallFont,
                Margin = new Padding(0, 4, 0, 8)
            };
            root.Controls.Add(_errorLabel, 0, 1);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                AutoSize = true
            };
            var importButton = UiTheme.PrimaryButton("导入到草稿");
            importButton.Width = 120;
            importButton.Click += delegate { Accept(); };
            var cancelButton = UiTheme.SecondaryButton("取消");
            cancelButton.Width = 88;
            cancelButton.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            buttons.Controls.Add(importButton);
            buttons.Controls.Add(cancelButton);
            root.Controls.Add(buttons, 0, 2);
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
            ConfigImportOptions options = CurrentOptions();
            ConfigImportPlan plan;
            string error;
            if (!ConfigTransfer.TryBuildPlan(_document, _current, options, _protector,
                out plan, out error))
            {
                _summaryLabel.Text = "当前选择无法生成导入方案。";
                _deviceChangeBox.Text = string.Empty;
                _errorLabel.Text = error;
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
                return;
            }
            Plan = plan;
            Result = new ConfigImportFormResult { Options = options };
            DialogResult = DialogResult.OK;
            Close();
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
