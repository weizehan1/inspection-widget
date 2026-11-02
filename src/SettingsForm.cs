using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using InspectionWidget.Config;
using InspectionWidget.Credentials;

namespace InspectionWidget
{
    /// <summary>配置深拷贝（设置窗编辑态与原配置隔离：取消零副作用）。</summary>
    public static class AppConfigCloner
    {
        public static AppConfig Clone(AppConfig source)
        {
            if (source == null)
                return AppConfig.CreateDefault();

            var copy = new AppConfig
            {
                RootPath = source.RootPath,
                Devices = source.Devices != null ? new List<string>(source.Devices) : new List<string>(),
                Credentials = new List<CredentialEntry>(),
                IsCredentialCollapsed = source.IsCredentialCollapsed,
                AutoClearClipboard = source.AutoClearClipboard,
                CaptureMode = source.CaptureMode,
                WindowLeft = source.WindowLeft,
                WindowTop = source.WindowTop,
                LastDevice = source.LastDevice
            };
            if (source.Credentials != null)
            {
                foreach (CredentialEntry entry in source.Credentials)
                {
                    copy.Credentials.Add(new CredentialEntry
                    {
                        Title = entry.Title,
                        EncryptedSecret = entry.EncryptedSecret,
                        NeedsReset = entry.NeedsReset
                    });
                }
            }
            return copy;
        }
    }

    /// <summary>
    /// 凭据行保存收集的纯逻辑（与控件无关，可单测）：决定一行凭据最终落盘的密文。
    /// 规则优先级：既有条目的清除标记 → 空密文；有新输入 → 经 Protector 加密覆盖；
    /// 已存条目留空 → 保留原密文；新条目无输入 → 报错。新增条目（IsNew）从未
    /// 保存过内容，清除对其无效：忽略其清除标记，内容为空时一律按新条目报错，
    /// 不得借清除存下「有名称、无内容」的空凭据。凭据原文永不回显，
    /// 本逻辑也不接触已存明文（只有密文进出）。
    /// </summary>
    public static class CredentialRowCollector
    {
        /// <summary>凭据条数上限（白皮书口径）。</summary>
        public const int MaxCredentials = 10;

        /// <summary>一行凭据的编辑态输入（纯数据）。</summary>
        public sealed class RowInput
        {
            public string Title;
            /// <summary>已存条目的原密文；新条目为 null。</summary>
            public string OriginalSecret;
            public bool IsNew;
            public bool Cleared;
            /// <summary>本次新输入的内容；null/空表示未输入。</summary>
            public string NewPlainText;
        }

        /// <summary>
        /// 收集一行：返回最终密文；不合格时返回 null 并置 error（中文原因）。
        /// </summary>
        public static string CollectSecret(RowInput row, ICredentialProtector protector, out string error)
        {
            error = null;
            if (row == null)
            {
                error = "凭据行数据为空。";
                return null;
            }
            if (string.IsNullOrWhiteSpace(row.Title))
            {
                error = "凭据名称不能为空。";
                return null;
            }
            // 清除只对既有条目（非新增）有效：新增条目忽略清除标记，
            // 继续走下方新输入/新条目空内容校验，空内容必然报错。
            if (row.Cleared && !row.IsNew)
                return string.Empty;
            if (!string.IsNullOrEmpty(row.NewPlainText))
            {
                try
                {
                    return protector.Protect(row.NewPlainText);
                }
                catch (Exception ex)
                {
                    error = "凭据加密失败：" + ex.Message;
                    return null;
                }
            }
            if (row.IsNew)
            {
                error = "新凭据的内容不能为空。";
                return null;
            }
            return row.OriginalSecret ?? string.Empty;
        }
    }

    /// <summary>
    /// 设置窗口（保存制）：四组——设备管理 / 凭据管理 / 存储与通用 / 关于。
    /// 所有修改只落在窗内深拷贝编辑态，点「保存」统一校验 → ConfigStore.Save，
    /// 成功才发 Saved 并关窗；校验不过或保存失败都停留在窗内并指出问题。
    /// 凭据内容绝不回显原文、不提供查看入口：已存条目内容框恒为空白，
    /// 留空不修改、输入新内容则覆盖（保存时才加密）。
    /// </summary>
    public class SettingsForm : Form
    {
        private static readonly Color AccentColor = Color.FromArgb(0x00, 0x67, 0xC0);
        private static readonly Color ErrorColor = Color.FromArgb(0xC4, 0x2B, 0x1C);
        private static readonly Color PrimaryTextColor = Color.FromArgb(0x1B, 0x1B, 0x1B);
        private static readonly Color SecondaryTextColor = Color.FromArgb(0x42, 0x42, 0x42);
        private static readonly Color WeakTextColor = Color.FromArgb(0x61, 0x61, 0x61);
        private static readonly Color PanelBackColor = Color.FromArgb(0xF9, 0xF9, 0xF9);
        private static readonly Color SelectedNavBackColor = Color.FromArgb(0xEF, 0xF6, 0xFC);

        private const int GroupDevices = 0;
        private const int GroupCredentials = 1;
        private const int GroupStorage = 2;
        private const int GroupAbout = 3;

        private readonly ConfigStore _store;
        private readonly ICredentialProtector _protector;
        /// <summary>传入配置的深拷贝：窗内一切编辑都落在这里，取消不影响原配置。</summary>
        private readonly AppConfig _working;

        /// <summary>保存成功后的新配置（Saved 后由 MainForm 取用刷新）。</summary>
        public AppConfig ResultConfig { get; private set; }

        /// <summary>保存成功（已落盘）后触发，MainForm 据 ResultConfig 刷新。</summary>
        public event EventHandler Saved;

        // 字体缓存（与 MainForm 同口径）：全窗复用，Dispose 时统一释放
        private Font _font13;
        private Font _font13Bold;
        private Font _font11;
        private Font _font20Bold;

        private Font Font13 { get { if (_font13 == null) _font13 = CreateFont(13f, FontStyle.Regular); return _font13; } }
        private Font Font13Bold { get { if (_font13Bold == null) _font13Bold = CreateFont(13f, FontStyle.Bold); return _font13Bold; } }
        private Font Font11 { get { if (_font11 == null) _font11 = CreateFont(11f, FontStyle.Regular); return _font11; } }
        private Font Font20Bold { get { if (_font20Bold == null) _font20Bold = CreateFont(20f, FontStyle.Bold); return _font20Bold; } }

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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_font13 != null) _font13.Dispose();
                if (_font13Bold != null) _font13Bold.Dispose();
                if (_font11 != null) _font11.Dispose();
                if (_font20Bold != null) _font20Bold.Dispose();
            }
            base.Dispose(disposing);
        }

        // ---------- 导航与骨架 ----------

        private readonly Button[] _navButtons = new Button[4];
        private readonly Panel[] _groupPanels = new Panel[4];
        private Panel _navAccent;
        private Label _feedbackLabel;

        // ---------- 设备组 ----------

        private ListBox _deviceListBox;
        private TextBox _deviceNameTextBox;

        // ---------- 凭据组 ----------

        private sealed class CredentialRow
        {
            public TextBox NameBox;
            public TextBox SecretBox;
            public Label StatusLabel;
            public Button ClearButton;
            public Button DeleteButton;
            public string OriginalSecret;
            public bool IsNew;
            public bool Cleared;
            public bool OriginalNeedsReset;
        }

        private readonly List<CredentialRow> _credentialRows = new List<CredentialRow>();
        private Panel _credentialRowsPanel;
        private Button _addCredentialButton;
        private Label _credentialCountLabel;

        // ---------- 存储组 ----------

        private string _editRootPath;
        private bool _rootPathChanged;
        private TextBox _rootPathTextBox;
        private Label _rootStatusLabel;
        private CheckBox _autoClearCheckBox;
        private RadioButton _modeAutoRadio;
        private RadioButton _modePrintWindowRadio;
        private RadioButton _modeBitBltRadio;

        // ---------- 关于组 ----------

        private Label _aboutConfigPathLabel;
        private Label _aboutRootPathLabel;

        public SettingsForm(AppConfig config, ConfigStore store, ICredentialProtector protector)
        {
            if (store == null) throw new ArgumentNullException("store");
            if (protector == null) throw new ArgumentNullException("protector");
            _store = store;
            _protector = protector;
            _working = AppConfigCloner.Clone(config);
            _editRootPath = _working.RootPath;

            Text = "设置 · 巡检截图小组件";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(744, 430);
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = PanelBackColor;

            BuildLayout();
            LoadDeviceList();
            LoadCredentialRows();
            LoadStorageValues();
            SelectGroup(GroupDevices);
        }

        private void BuildLayout()
        {
            // 左侧导航列
            var navPanel = new Panel { Location = new Point(0, 0), Size = new Size(130, 382), BackColor = PanelBackColor };
            string[] navTexts = { "设备管理", "凭据管理", "存储与通用", "关于" };
            for (int i = 0; i < navTexts.Length; i++)
            {
                int index = i;
                var button = new Button
                {
                    Text = navTexts[i],
                    Font = Font13,
                    ForeColor = PrimaryTextColor,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = PanelBackColor,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(14, 0, 0, 0),
                    Location = new Point(0, 12 + i * 46),
                    Size = new Size(130, 40)
                };
                button.FlatAppearance.BorderSize = 0;
                button.Click += delegate { SelectGroup(index); };
                _navButtons[i] = button;
                navPanel.Controls.Add(button);
            }
            _navAccent = new Panel { BackColor = AccentColor, Size = new Size(4, 40), Location = new Point(0, 12) };
            navPanel.Controls.Add(_navAccent);
            _navAccent.BringToFront();
            Controls.Add(navPanel);

            // 右侧内容区：四组各一块 Panel，切换可见性（不用 TabControl）
            var contentPanel = new Panel { Location = new Point(130, 0), Size = new Size(614, 382), BackColor = PanelBackColor };
            Controls.Add(contentPanel);
            _groupPanels[GroupDevices] = BuildDevicesGroup();
            _groupPanels[GroupCredentials] = BuildCredentialsGroup();
            _groupPanels[GroupStorage] = BuildStorageGroup();
            _groupPanels[GroupAbout] = BuildAboutGroup();
            for (int i = 0; i < _groupPanels.Length; i++)
                contentPanel.Controls.Add(_groupPanels[i]);

            // 底部固定栏：左反馈（错误红字），右保存/取消
            _feedbackLabel = new Label
            {
                Font = Font13,
                ForeColor = ErrorColor,
                AutoSize = false,
                AutoEllipsis = true,
                Size = new Size(524, 32),
                Location = new Point(12, 394)
            };
            Controls.Add(_feedbackLabel);
            var saveButton = new Button
            {
                Text = "保存",
                Font = Font13,
                Size = new Size(84, 32),
                Location = new Point(552, 390)
            };
            saveButton.Click += OnSaveClicked;
            Controls.Add(saveButton);
            var cancelButton = new Button
            {
                Text = "取消",
                Font = Font13,
                Size = new Size(84, 32),
                Location = new Point(644, 390)
            };
            cancelButton.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancelButton);
            CancelButton = cancelButton;
        }

        private void SelectGroup(int index)
        {
            for (int i = 0; i < _groupPanels.Length; i++)
            {
                _groupPanels[i].Visible = i == index;
                _navButtons[i].Font = i == index ? Font13Bold : Font13;
                _navButtons[i].BackColor = i == index ? SelectedNavBackColor : PanelBackColor;
            }
            _navAccent.Location = new Point(0, _navButtons[index].Top);
            _navAccent.BringToFront();
            if (index == GroupAbout)
                RefreshAboutLabels();
        }

        private Panel NewGroupPanel()
        {
            return new Panel { Location = new Point(0, 0), Size = new Size(614, 382), BackColor = PanelBackColor };
        }

        // ---------- 设备管理组 ----------

        private Panel BuildDevicesGroup()
        {
            Panel panel = NewGroupPanel();

            _deviceListBox = new ListBox
            {
                Font = Font13,
                Location = new Point(16, 16),
                Size = new Size(320, 288)
            };
            _deviceListBox.SelectedIndexChanged += OnDeviceSelectionChanged;
            panel.Controls.Add(_deviceListBox);

            var editLabel = new Label
            {
                Text = "设备名（选中项在此编辑）",
                Font = Font13,
                ForeColor = SecondaryTextColor,
                AutoSize = true,
                Location = new Point(352, 16)
            };
            panel.Controls.Add(editLabel);

            _deviceNameTextBox = new TextBox
            {
                Font = Font13,
                Location = new Point(352, 42),
                Size = new Size(246, 30)
            };
            panel.Controls.Add(_deviceNameTextBox);

            var applyButton = new Button { Text = "应用修改", Font = Font13, Location = new Point(352, 84), Size = new Size(246, 32) };
            applyButton.Click += OnDeviceApplyClicked;
            panel.Controls.Add(applyButton);

            var addButton = new Button { Text = "添加", Font = Font13, Location = new Point(352, 128), Size = new Size(118, 32) };
            addButton.Click += OnDeviceAddClicked;
            panel.Controls.Add(addButton);

            var deleteButton = new Button { Text = "删除", Font = Font13, Location = new Point(480, 128), Size = new Size(118, 32) };
            deleteButton.Click += OnDeviceDeleteClicked;
            panel.Controls.Add(deleteButton);

            var upButton = new Button { Text = "上移", Font = Font13, Location = new Point(352, 172), Size = new Size(118, 32) };
            upButton.Click += delegate { MoveDevice(-1); };
            panel.Controls.Add(upButton);

            var downButton = new Button { Text = "下移", Font = Font13, Location = new Point(480, 172), Size = new Size(118, 32) };
            downButton.Click += delegate { MoveDevice(1); };
            panel.Controls.Add(downButton);

            var note = new Label
            {
                Text = "删除设备只删名单，不删除已保存的历史截图。",
                Font = Font11,
                ForeColor = WeakTextColor,
                AutoSize = true,
                Location = new Point(16, 322)
            };
            panel.Controls.Add(note);
            return panel;
        }

        private void LoadDeviceList()
        {
            _deviceListBox.Items.Clear();
            foreach (string device in _working.Devices)
                _deviceListBox.Items.Add(device);
            if (_deviceListBox.Items.Count > 0)
                _deviceListBox.SelectedIndex = 0;
        }

        private List<string> CurrentDeviceNames()
        {
            var names = new List<string>();
            foreach (object item in _deviceListBox.Items)
                names.Add((string)item);
            return names;
        }

        private void OnDeviceSelectionChanged(object sender, EventArgs e)
        {
            int index = _deviceListBox.SelectedIndex;
            _deviceNameTextBox.Text = index >= 0 ? (string)_deviceListBox.Items[index] : string.Empty;
        }

        private void OnDeviceApplyClicked(object sender, EventArgs e)
        {
            int index = _deviceListBox.SelectedIndex;
            if (index < 0)
            {
                ShowError(GroupDevices, "设备管理：请先在列表中选择要修改的设备。", _deviceListBox);
                return;
            }
            string newName = _deviceNameTextBox.Text;
            var others = CurrentDeviceNames();
            others.RemoveAt(index);
            string error = DeviceNameValidator.Validate(newName, others);
            if (error != null)
            {
                ShowError(GroupDevices, "设备管理：" + error, _deviceNameTextBox);
                return;
            }
            _deviceListBox.Items[index] = newName;
            ClearFeedback();
        }

        private void OnDeviceAddClicked(object sender, EventArgs e)
        {
            string text = _deviceNameTextBox.Text;
            List<string> existing = CurrentDeviceNames();
            if (string.IsNullOrWhiteSpace(text))
            {
                // 编辑框为空：在列表尾加「设备N」占位名（取第一个校验通过的序号）并选中编辑
                int n = existing.Count + 1;
                string candidate = "设备" + n;
                while (DeviceNameValidator.Validate(candidate, existing) != null)
                {
                    n++;
                    candidate = "设备" + n;
                }
                _deviceListBox.Items.Add(candidate);
                _deviceListBox.SelectedIndex = _deviceListBox.Items.Count - 1;
                _deviceNameTextBox.Focus();
                _deviceNameTextBox.SelectAll();
                ClearFeedback();
                return;
            }
            string error = DeviceNameValidator.Validate(text, existing);
            if (error != null)
            {
                ShowError(GroupDevices, "设备管理：" + error, _deviceNameTextBox);
                return;
            }
            _deviceListBox.Items.Add(text);
            _deviceListBox.SelectedIndex = _deviceListBox.Items.Count - 1;
            ClearFeedback();
        }

        private void OnDeviceDeleteClicked(object sender, EventArgs e)
        {
            int index = _deviceListBox.SelectedIndex;
            if (index < 0)
            {
                ShowError(GroupDevices, "设备管理：请先在列表中选择要删除的设备。", _deviceListBox);
                return;
            }
            _deviceListBox.Items.RemoveAt(index);
            if (_deviceListBox.Items.Count > 0)
                _deviceListBox.SelectedIndex = Math.Min(index, _deviceListBox.Items.Count - 1);
            else
                _deviceNameTextBox.Text = string.Empty;
            ClearFeedback();
        }

        private void MoveDevice(int delta)
        {
            int index = _deviceListBox.SelectedIndex;
            int target = index + delta;
            if (index < 0 || target < 0 || target >= _deviceListBox.Items.Count)
                return;
            object moving = _deviceListBox.Items[index];
            _deviceListBox.Items[index] = _deviceListBox.Items[target];
            _deviceListBox.Items[target] = moving;
            _deviceListBox.SelectedIndex = target;
        }

        // ---------- 凭据管理组 ----------

        private Panel BuildCredentialsGroup()
        {
            Panel panel = NewGroupPanel();

            _addCredentialButton = new Button { Text = "+ 添加凭据", Font = Font13, Location = new Point(16, 12), Size = new Size(130, 30) };
            _addCredentialButton.Click += OnAddCredentialClicked;
            panel.Controls.Add(_addCredentialButton);

            _credentialCountLabel = new Label
            {
                Font = Font13,
                ForeColor = SecondaryTextColor,
                AutoSize = true,
                Location = new Point(158, 18)
            };
            panel.Controls.Add(_credentialCountLabel);

            var note = new Label
            {
                Text = "已保存的凭据内容不回显：留空不修改，输入新内容则覆盖；清除只清内容、保留条目。",
                Font = Font11,
                ForeColor = WeakTextColor,
                AutoSize = true,
                Location = new Point(16, 50)
            };
            panel.Controls.Add(note);

            var nameHeader = new Label { Text = "名称", Font = Font11, ForeColor = WeakTextColor, AutoSize = true, Location = new Point(16, 76) };
            var secretHeader = new Label { Text = "内容", Font = Font11, ForeColor = WeakTextColor, AutoSize = true, Location = new Point(174, 76) };
            var statusHeader = new Label { Text = "状态", Font = Font11, ForeColor = WeakTextColor, AutoSize = true, Location = new Point(342, 76) };
            panel.Controls.Add(nameHeader);
            panel.Controls.Add(secretHeader);
            panel.Controls.Add(statusHeader);

            _credentialRowsPanel = new Panel
            {
                Location = new Point(16, 98),
                Size = new Size(582, 272),
                AutoScroll = true,
                BackColor = PanelBackColor
            };
            panel.Controls.Add(_credentialRowsPanel);
            return panel;
        }

        private void LoadCredentialRows()
        {
            foreach (CredentialEntry entry in _working.Credentials)
            {
                CreateCredentialRow(entry.Title, entry.EncryptedSecret, entry.NeedsReset, false);
            }
            LayoutCredentialRows();
            UpdateCredentialAddState();
        }

        private CredentialRow CreateCredentialRow(string title, string originalSecret, bool needsReset, bool isNew)
        {
            var row = new CredentialRow
            {
                OriginalSecret = originalSecret,
                IsNew = isNew,
                Cleared = false,
                OriginalNeedsReset = needsReset
            };
            row.NameBox = new TextBox { Font = Font13, Size = new Size(150, 30), Text = title ?? string.Empty };
            // 内容框恒为空白：已存内容绝不回显；PasswordChar 掩码新输入
            row.SecretBox = new TextBox { Font = Font13, Size = new Size(160, 30), PasswordChar = '●' };
            row.SecretBox.TextChanged += delegate { RefreshRowStatus(row); };
            row.StatusLabel = new Label { Font = Font11, AutoSize = false, Size = new Size(120, 36) };
            row.ClearButton = new Button { Text = "清除", Font = Font11, Size = new Size(50, 30) };
            // 新增条目尚无已存密文可清，「清除」禁用；既有条目才可清除内容
            row.ClearButton.Enabled = !isNew;
            row.ClearButton.Click += delegate { OnCredentialClearClicked(row); };
            row.DeleteButton = new Button { Text = "删除", Font = Font11, Size = new Size(50, 30) };
            row.DeleteButton.Click += delegate { OnCredentialDeleteClicked(row); };

            _credentialRowsPanel.Controls.Add(row.NameBox);
            _credentialRowsPanel.Controls.Add(row.SecretBox);
            _credentialRowsPanel.Controls.Add(row.StatusLabel);
            _credentialRowsPanel.Controls.Add(row.ClearButton);
            _credentialRowsPanel.Controls.Add(row.DeleteButton);
            _credentialRows.Add(row);
            RefreshRowStatus(row);
            return row;
        }

        private void LayoutCredentialRows()
        {
            for (int i = 0; i < _credentialRows.Count; i++)
            {
                CredentialRow row = _credentialRows[i];
                int y = i * 46;
                row.NameBox.Location = new Point(0, y);
                row.SecretBox.Location = new Point(158, y);
                row.StatusLabel.Location = new Point(326, y);
                row.ClearButton.Location = new Point(452, y);
                row.DeleteButton.Location = new Point(508, y);
            }
        }

        private void RefreshRowStatus(CredentialRow row)
        {
            if (row.Cleared)
            {
                row.StatusLabel.Text = "已清除（保存后生效）";
                row.StatusLabel.ForeColor = WeakTextColor;
                return;
            }
            if (row.IsNew)
            {
                row.StatusLabel.Text = "新凭据：名称与内容必填";
                row.StatusLabel.ForeColor = SecondaryTextColor;
                return;
            }
            if (!string.IsNullOrEmpty(row.SecretBox.Text))
            {
                row.StatusLabel.Text = "已输入新内容（保存后覆盖）";
                row.StatusLabel.ForeColor = SecondaryTextColor;
                return;
            }
            if (row.OriginalNeedsReset || string.IsNullOrEmpty(row.OriginalSecret))
            {
                row.StatusLabel.Text = "需重新设置";
                row.StatusLabel.ForeColor = ErrorColor;
                return;
            }
            row.StatusLabel.Text = "已设置（留空不修改，输入新内容则覆盖）";
            row.StatusLabel.ForeColor = SecondaryTextColor;
        }

        private void OnCredentialClearClicked(CredentialRow row)
        {
            // 新增条目无已存内容可清：点击无效（按钮已禁用，此处再兜底守卫）
            if (row.IsNew)
                return;
            // 清除只清内容、保留条目与名称：保存后该条密文置空，面板点击将提示需重新设置
            row.Cleared = true;
            row.SecretBox.Text = string.Empty;
            row.SecretBox.Enabled = false;
            RefreshRowStatus(row);
        }

        private void OnCredentialDeleteClicked(CredentialRow row)
        {
            _credentialRows.Remove(row);
            _credentialRowsPanel.Controls.Remove(row.NameBox);
            _credentialRowsPanel.Controls.Remove(row.SecretBox);
            _credentialRowsPanel.Controls.Remove(row.StatusLabel);
            _credentialRowsPanel.Controls.Remove(row.ClearButton);
            _credentialRowsPanel.Controls.Remove(row.DeleteButton);
            row.NameBox.Dispose();
            row.SecretBox.Dispose();
            row.StatusLabel.Dispose();
            row.ClearButton.Dispose();
            row.DeleteButton.Dispose();
            LayoutCredentialRows();
            UpdateCredentialAddState();
        }

        private void OnAddCredentialClicked(object sender, EventArgs e)
        {
            if (_credentialRows.Count >= CredentialRowCollector.MaxCredentials)
            {
                ShowError(GroupCredentials, "凭据管理：凭据已达上限（" + CredentialRowCollector.MaxCredentials + " 条）。", _addCredentialButton);
                return;
            }
            CredentialRow row = CreateCredentialRow(string.Empty, null, false, true);
            LayoutCredentialRows();
            UpdateCredentialAddState();
            row.NameBox.Focus();
            ClearFeedback();
        }

        private void UpdateCredentialAddState()
        {
            _credentialCountLabel.Text = "（" + _credentialRows.Count + " / " + CredentialRowCollector.MaxCredentials + "）";
            _addCredentialButton.Enabled = _credentialRows.Count < CredentialRowCollector.MaxCredentials;
        }

        // ---------- 存储与通用组 ----------

        private Panel BuildStorageGroup()
        {
            Panel panel = NewGroupPanel();

            var rootLabel = new Label { Text = "截图根目录", Font = Font13, ForeColor = SecondaryTextColor, AutoSize = true, Location = new Point(16, 16) };
            panel.Controls.Add(rootLabel);

            _rootPathTextBox = new TextBox { Font = Font13, ReadOnly = true, Location = new Point(16, 44), Size = new Size(440, 30) };
            panel.Controls.Add(_rootPathTextBox);

            var changeButton = new Button { Text = "更改…", Font = Font13, Location = new Point(468, 42), Size = new Size(110, 32) };
            changeButton.Click += OnChangeRootClicked;
            panel.Controls.Add(changeButton);

            _rootStatusLabel = new Label { Font = Font11, AutoSize = true, Location = new Point(16, 82) };
            panel.Controls.Add(_rootStatusLabel);

            var configLabel = new Label { Text = "配置文件实际路径", Font = Font13, ForeColor = SecondaryTextColor, AutoSize = true, Location = new Point(16, 118) };
            panel.Controls.Add(configLabel);

            var configPathTextBox = new TextBox
            {
                Font = Font11,
                ReadOnly = true,
                Text = _store.AuthoritativePath,
                Location = new Point(16, 146),
                Size = new Size(582, 26)
            };
            panel.Controls.Add(configPathTextBox);

            if (_store.IsFallback)
            {
                var fallbackNote = new Label
                {
                    Text = "（exe 目录不可写，已回退）",
                    Font = Font11,
                    ForeColor = ErrorColor,
                    AutoSize = true,
                    Location = new Point(16, 178)
                };
                panel.Controls.Add(fallbackNote);
            }

            _autoClearCheckBox = new CheckBox
            {
                Text = "复制后 30 秒自动清空剪贴板",
                Font = Font13,
                ForeColor = PrimaryTextColor,
                AutoSize = true,
                Location = new Point(16, 214)
            };
            panel.Controls.Add(_autoClearCheckBox);

            var modeGroup = new GroupBox { Text = "截图模式", Font = Font13, Location = new Point(16, 252), Size = new Size(582, 84) };
            _modeAutoRadio = new RadioButton { Text = "自动（推荐）", Font = Font13, AutoSize = true, Location = new Point(16, 34) };
            _modePrintWindowRadio = new RadioButton { Text = "仅 PrintWindow", Font = Font13, AutoSize = true, Location = new Point(150, 34) };
            _modeBitBltRadio = new RadioButton { Text = "仅屏幕拷贝（置前抓屏）", Font = Font13, AutoSize = true, Location = new Point(320, 34) };
            modeGroup.Controls.Add(_modeAutoRadio);
            modeGroup.Controls.Add(_modePrintWindowRadio);
            modeGroup.Controls.Add(_modeBitBltRadio);
            panel.Controls.Add(modeGroup);
            return panel;
        }

        private void LoadStorageValues()
        {
            _rootPathTextBox.Text = _editRootPath ?? string.Empty;
            RefreshRootStatus();
            _autoClearCheckBox.Checked = _working.AutoClearClipboard;
            _modeAutoRadio.Checked = _working.CaptureMode == CaptureMode.Auto;
            _modePrintWindowRadio.Checked = _working.CaptureMode == CaptureMode.PrintWindowOnly;
            _modeBitBltRadio.Checked = _working.CaptureMode == CaptureMode.BitBltOnly;
        }

        private void RefreshRootStatus()
        {
            if (string.IsNullOrEmpty(_editRootPath))
            {
                _rootStatusLabel.Text = "尚未设置，截图暂不可用";
                _rootStatusLabel.ForeColor = ErrorColor;
            }
            else if (_rootPathChanged)
            {
                _rootStatusLabel.Text = "已更改，点「保存」后生效 · 启动时不再询问";
                _rootStatusLabel.ForeColor = SecondaryTextColor;
            }
            else
            {
                _rootStatusLabel.Text = "已保存 · 启动时不再询问";
                _rootStatusLabel.ForeColor = SecondaryTextColor;
            }
        }

        private void OnChangeRootClicked(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择巡检截图保存根目录";
                dialog.ShowNewFolderButton = true;
                if (!string.IsNullOrEmpty(_editRootPath))
                    dialog.SelectedPath = _editRootPath;
                if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(dialog.SelectedPath))
                {
                    _editRootPath = dialog.SelectedPath;
                    _rootPathChanged = true;
                    _rootPathTextBox.Text = _editRootPath;
                    RefreshRootStatus();
                    ClearFeedback();
                }
            }
        }

        // ---------- 关于组 ----------

        private Panel BuildAboutGroup()
        {
            Panel panel = NewGroupPanel();

            var productLabel = new Label
            {
                Text = "巡检截图小组件",
                Font = Font20Bold,
                ForeColor = PrimaryTextColor,
                AutoSize = true,
                Location = new Point(16, 20)
            };
            panel.Controls.Add(productLabel);

            var versionBadge = new Label
            {
                Text = "v1.0",
                Font = Font13Bold,
                ForeColor = Color.White,
                BackColor = AccentColor,
                AutoSize = true,
                Padding = new Padding(8, 2, 8, 2),
                Location = new Point(16, 66)
            };
            panel.Controls.Add(versionBadge);

            var introLabel = new Label
            {
                Text = "点设备名、点窗口，截图自动按日期归档；常用凭据一键复制。",
                Font = Font13,
                ForeColor = SecondaryTextColor,
                AutoSize = true,
                Location = new Point(16, 108)
            };
            panel.Controls.Add(introLabel);

            var repoLink = new LinkLabel
            {
                Text = "https://github.com/weizehan1/inspection-widget",
                Font = Font13,
                AutoSize = true,
                Location = new Point(16, 144)
            };
            repoLink.LinkClicked += delegate
            {
                try
                {
                    Process.Start("https://github.com/weizehan1/inspection-widget");
                }
                catch
                {
                    // 打开浏览器失败忽略（简报口径）
                }
            };
            panel.Controls.Add(repoLink);

            var techLabel = new Label
            {
                Text = "C# WinForms · .NET Framework 4.8 · 零第三方依赖",
                Font = Font13,
                ForeColor = SecondaryTextColor,
                AutoSize = true,
                Location = new Point(16, 180)
            };
            panel.Controls.Add(techLabel);

            _aboutConfigPathLabel = new Label { Font = Font11, ForeColor = WeakTextColor, AutoSize = true, Location = new Point(16, 224) };
            panel.Controls.Add(_aboutConfigPathLabel);

            _aboutRootPathLabel = new Label { Font = Font11, ForeColor = WeakTextColor, AutoSize = true, Location = new Point(16, 250) };
            panel.Controls.Add(_aboutRootPathLabel);
            return panel;
        }

        private void RefreshAboutLabels()
        {
            // 运行时读取当前值（与存储组同值：配置文件取 store、根目录取窗内编辑态）
            _aboutConfigPathLabel.Text = "配置文件：" + _store.AuthoritativePath;
            _aboutRootPathLabel.Text = "截图根目录：" + (string.IsNullOrEmpty(_editRootPath) ? "尚未设置" : _editRootPath);
        }

        // ---------- 反馈 ----------

        /// <summary>报错：反馈区红字 + 切到对应组 + 焦点定位；不保存不关窗。</summary>
        private void ShowError(int groupIndex, string message, Control focus)
        {
            SelectGroup(groupIndex);
            _feedbackLabel.Text = message;
            if (focus != null)
                focus.Focus();
        }

        private void ShowSaveError(string message)
        {
            _feedbackLabel.Text = message;
        }

        private void ClearFeedback()
        {
            _feedbackLabel.Text = string.Empty;
        }

        // ---------- 保存流程 ----------

        private void OnSaveClicked(object sender, EventArgs e)
        {
            ClearFeedback();

            // 1) 设备名整体校验（合法性 + 列表内查重）
            List<string> devices = CurrentDeviceNames();
            string deviceError = DeviceNameValidator.ValidateAll(devices);
            if (deviceError != null)
            {
                ShowError(GroupDevices, "设备管理：设备名不合法——" + deviceError, _deviceListBox);
                return;
            }

            // 路径长度：与 ScreenshotSaver 同口径——根目录\日期子目录\设备名.png 全路径
            if (!string.IsNullOrEmpty(_editRootPath))
            {
                string todayDir = Path.Combine(_editRootPath, DateTime.Now.ToString("yyyy-MM-dd"));
                for (int i = 0; i < devices.Count; i++)
                {
                    string lengthError = DeviceNameValidator.ValidateFullPathLength(todayDir, devices[i] + ".png");
                    if (lengthError != null)
                    {
                        ShowError(GroupDevices, "设备管理：设备「" + devices[i] + "」" + lengthError, _deviceListBox);
                        return;
                    }
                }
            }

            // 2) 凭据：条数上限 → 逐行收集（名称必填；新内容在此刻才加密）
            if (_credentialRows.Count > CredentialRowCollector.MaxCredentials)
            {
                ShowError(GroupCredentials, "凭据管理：凭据最多 " + CredentialRowCollector.MaxCredentials + " 条。", _addCredentialButton);
                return;
            }
            var credentials = new List<CredentialEntry>();
            for (int i = 0; i < _credentialRows.Count; i++)
            {
                CredentialRow row = _credentialRows[i];
                var input = new CredentialRowCollector.RowInput
                {
                    Title = row.NameBox.Text,
                    OriginalSecret = row.OriginalSecret,
                    IsNew = row.IsNew,
                    Cleared = row.Cleared,
                    NewPlainText = row.SecretBox.Text
                };
                string error;
                string secret = CredentialRowCollector.CollectSecret(input, _protector, out error);
                if (error != null)
                {
                    Control focus = string.IsNullOrWhiteSpace(row.NameBox.Text) ? (Control)row.NameBox : row.SecretBox;
                    ShowError(GroupCredentials,
                        "凭据管理：第 " + (i + 1) + " 条凭据——" + error, focus);
                    return;
                }
                credentials.Add(new CredentialEntry
                {
                    Title = row.NameBox.Text,
                    EncryptedSecret = secret,
                    // 密文为空（清除后/从未设置）运行时即需重设，与 MainForm 口径一致
                    NeedsReset = string.IsNullOrEmpty(secret)
                });
            }

            // 3) 组装新配置：窗内不编辑的字段（折叠态/窗口位置）沿用原配置值；
            // LastDevice 若指向已被删设备则置 null
            AppConfig result = AppConfigCloner.Clone(_working);
            result.Devices = devices;
            result.Credentials = credentials;
            result.RootPath = _editRootPath;
            result.AutoClearClipboard = _autoClearCheckBox.Checked;
            result.CaptureMode = _modePrintWindowRadio.Checked
                ? CaptureMode.PrintWindowOnly
                : (_modeBitBltRadio.Checked ? CaptureMode.BitBltOnly : CaptureMode.Auto);
            if (result.LastDevice != null && !devices.Contains(result.LastDevice))
                result.LastDevice = null;

            // 4) 落盘：失败停留在窗内并明示，绝不带错误配置关窗
            SaveResult save;
            try
            {
                save = _store.Save(result);
            }
            catch (Exception ex)
            {
                ShowSaveError("设置未保存：" + ex.Message);
                return;
            }
            if (save == null || !save.Saved)
            {
                ShowSaveError("设置未保存：" + (save != null ? save.Error : "未知错误"));
                return;
            }

            ResultConfig = result;
            EventHandler handler = Saved;
            if (handler != null)
                handler(this, EventArgs.Empty);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
