using System;
using System.Drawing;
using System.Windows.Forms;
using InspectionWidget.Config;

namespace InspectionWidget
{
    /// <summary>
    /// 设备表单列可编辑表格：行序即主面板按钮序，空行允许存在于草稿，
    /// 提交时由 SettingsDraft/SettingsSaveCoordinator 统一要求补全或移除。
    /// </summary>
    public sealed class DeviceEditorControl : UserControl
    {
        private readonly SettingsDraft _draft;
        private readonly DataGridView _grid;
        private readonly Label _countLabel;
        private readonly Button _addButton;
        private readonly Button _deleteButton;
        private readonly Button _upButton;
        private readonly Button _downButton;
        private bool _loading;
        private string _editOldName;

        public event Action DraftChanged;
        public event Action<string> ValidationFailed;

        public DeviceEditorControl(SettingsDraft draft)
        {
            if (draft == null) throw new ArgumentNullException("draft");
            _draft = draft;
            Dock = DockStyle.Fill;
            BackColor = UiTheme.PageBackColor;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = UiTheme.PageBackColor
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));

            var top = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = UiTheme.PageBackColor
            };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _countLabel = new Label
            {
                AutoSize = true,
                Font = UiTheme.BodyFont,
                ForeColor = UiTheme.SecondaryTextColor,
                Text = "共 0 台设备",
                Padding = new Padding(0, 8, 0, 8)
            };
            top.Controls.Add(_countLabel, 0, 0);

            var hint = new Label
            {
                AutoSize = true,
                Font = UiTheme.SmallFont,
                ForeColor = UiTheme.WeakTextColor,
                Text = "双击或按 Enter 编辑；行序就是主面板按钮顺序。",
                Padding = new Padding(0, 8, 8, 8)
            };
            top.Controls.Add(hint, 1, 0);
            root.Controls.Add(top, 0, 0);

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoGenerateColumns = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AllowUserToOrderColumns = false,
                RowHeadersVisible = true,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                EditMode = DataGridViewEditMode.EditOnEnter,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = UiTheme.BodyFont,
                GridColor = UiTheme.BorderColor,
                ColumnHeadersHeight = 34,
                RowTemplate = { Height = 34 },
                StandardTab = true,
                AccessibleName = "设备列表"
            };
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "DeviceName",
                HeaderText = "设备名称",
                DataPropertyName = "Name",
                Width = 620,
                MinimumWidth = 220,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            _grid.DataSource = new BindingSource { DataSource = _draft.Devices };
            _grid.CellBeginEdit += OnCellBeginEdit;
            _grid.CellValidating += OnCellValidating;
            _grid.CellEndEdit += OnCellEndEdit;
            _grid.CellValueChanged += delegate { if (!_loading) RaiseChanged(); };
            _grid.SelectionChanged += delegate { UpdateButtons(); };
            _grid.DataError += OnDataError;
            root.Controls.Add(_grid, 0, 1);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = UiTheme.PageBackColor,
                Padding = new Padding(0, 8, 0, 0)
            };
            _addButton = UiTheme.SecondaryButton("新增设备");
            _addButton.AccessibleName = "新增设备";
            _addButton.Click += delegate { AddDevice(); };
            _deleteButton = UiTheme.SecondaryButton("删除");
            _deleteButton.AccessibleName = "删除当前设备";
            _deleteButton.Click += delegate { DeleteDevice(); };
            _upButton = UiTheme.SecondaryButton("上移");
            _upButton.AccessibleName = "当前设备上移";
            _upButton.Click += delegate { MoveDevice(-1); };
            _downButton = UiTheme.SecondaryButton("下移");
            _downButton.AccessibleName = "当前设备下移";
            _downButton.Click += delegate { MoveDevice(1); };
            buttons.Controls.Add(_addButton);
            buttons.Controls.Add(_deleteButton);
            buttons.Controls.Add(_upButton);
            buttons.Controls.Add(_downButton);
            root.Controls.Add(buttons, 0, 2);

            Controls.Add(root);
            _draft.Devices.ListChanged += delegate
            {
                if (!_loading) UpdateCountAndButtons();
            };
            UpdateCountAndButtons();
        }

        public DataGridView Grid
        {
            get { return _grid; }
        }

        public bool CommitEdit(out string error)
        {
            error = null;
            try
            {
                _grid.EndEdit();
            }
            catch (Exception ex)
            {
                error = "结束设备编辑失败：" + ex.Message;
                return false;
            }
            if (_grid.IsCurrentCellInEditMode)
            {
                int row = _grid.CurrentRow != null ? _grid.CurrentRow.Index + 1 : 0;
                string reason = _grid.CurrentCell != null && !string.IsNullOrEmpty(_grid.CurrentCell.ErrorText)
                    ? _grid.CurrentCell.ErrorText
                    : "输入未通过校验";
                error = "设备管理：第 " + row + " 行" + reason + "，请修正或按 Esc 取消。";
                return false;
            }
            return ValidateDraft(true, out error);
        }

        /// <summary>提交前校验：草稿操作允许空行，真正保存时不允许。</summary>
        public bool ValidateDraft(bool allowBlank, out string error)
        {
            error = null;
            for (int i = 0; i < _draft.Devices.Count; i++)
            {
                string name = _draft.Devices[i] == null ? null : _draft.Devices[i].Name;
                if (string.IsNullOrWhiteSpace(name))
                {
                    if (allowBlank)
                        continue;
                    error = "设备管理：第 " + (i + 1) + " 行设备名称不能为空，请填写或移除该行。";
                    SelectRow(i);
                    return false;
                }
                var others = new System.Collections.Generic.List<string>();
                for (int j = 0; j < _draft.Devices.Count; j++)
                {
                    if (j != i)
                        others.Add(_draft.Devices[j] == null ? null : _draft.Devices[j].Name);
                }
                string validation = DeviceNameValidator.Validate(name, others);
                if (validation != null)
                {
                    error = "设备管理：第 " + (i + 1) + " 行「" + name + "」——" + validation;
                    SelectRow(i);
                    return false;
                }
            }
            return true;
        }

        public void FocusFirstRow()
        {
            if (_draft.Devices.Count == 0)
            {
                _addButton.Focus();
                return;
            }
            SelectRow(Math.Max(0, _grid.CurrentRow != null ? _grid.CurrentRow.Index : 0));
            _grid.Focus();
        }

        private void AddDevice()
        {
            string error;
            if (!CommitEdit(out error))
            {
                ReportValidation(error);
                return;
            }
            _loading = true;
            try
            {
                int index = _draft.AddBlankDevice();
                SelectRow(index);
            }
            finally
            {
                _loading = false;
            }
            RaiseChanged();
            UpdateCountAndButtons();
            _grid.Focus();
            _grid.BeginEdit(true);
        }

        private void DeleteDevice()
        {
            string error;
            if (!CommitEdit(out error))
            {
                ReportValidation(error);
                return;
            }
            int index = _grid.CurrentRow != null ? _grid.CurrentRow.Index : -1;
            if (index < 0)
                return;
            _loading = true;
            try
            {
                _draft.RemoveDeviceAt(index);
                if (_draft.Devices.Count > 0)
                    SelectRow(Math.Min(index, _draft.Devices.Count - 1));
            }
            finally
            {
                _loading = false;
            }
            RaiseChanged();
            UpdateCountAndButtons();
            _grid.Focus();
        }

        private void MoveDevice(int delta)
        {
            string error;
            if (!CommitEdit(out error))
            {
                ReportValidation(error);
                return;
            }
            int index = _grid.CurrentRow != null ? _grid.CurrentRow.Index : -1;
            if (!_draft.MoveDevice(index, delta))
                return;
            RaiseChanged();
            SelectRow(index + delta);
            _grid.Focus();
        }

        private void OnCellBeginEdit(object sender, DataGridViewCellCancelEventArgs e)
        {
            _editOldName = _draft.Devices[e.RowIndex] == null
                ? null : _draft.Devices[e.RowIndex].Name;
            _grid.Rows[e.RowIndex].ErrorText = string.Empty;
        }

        private void OnCellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            string proposed = e.FormattedValue == null ? string.Empty : e.FormattedValue.ToString();
            if (string.IsNullOrWhiteSpace(proposed))
                return;

            var others = new System.Collections.Generic.List<string>();
            for (int i = 0; i < _draft.Devices.Count; i++)
            {
                if (i != e.RowIndex)
                    others.Add(_draft.Devices[i] == null ? null : _draft.Devices[i].Name);
            }
            string error = DeviceNameValidator.Validate(proposed, others);
            if (error != null)
            {
                e.Cancel = true;
                string message = "设备管理：第 " + (e.RowIndex + 1) + " 行——" + error;
                _grid.Rows[e.RowIndex].ErrorText = error;
                ReportValidation(message);
            }
        }

        private void OnCellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            string newName = _draft.Devices[e.RowIndex] == null
                ? null : _draft.Devices[e.RowIndex].Name;
            if (!string.Equals(_editOldName, newName, StringComparison.Ordinal))
                _draft.ApplyDeviceRename(_editOldName, newName);
            _editOldName = null;
            _grid.Rows[e.RowIndex].ErrorText = string.Empty;
            RaiseChanged();
        }

        private void OnDataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            e.ThrowException = false;
            ReportValidation("设备表格输入无法提交：" + e.Exception.Message);
        }

        private void SelectRow(int index)
        {
            if (index < 0 || index >= _grid.Rows.Count)
                return;
            _grid.CurrentCell = _grid.Rows[index].Cells[0];
            _grid.Rows[index].Selected = true;
        }

        private void UpdateCountAndButtons()
        {
            if (_loading) return;
            UpdateCountAndButtonsCore();
        }

        private void UpdateCountAndButtonsCore()
        {
            _countLabel.Text = "共 " + _draft.Devices.Count + " 台设备";
            UpdateButtons();
            for (int i = 0; i < _grid.Rows.Count; i++)
                _grid.Rows[i].HeaderCell.Value = (i + 1).ToString();
        }

        private void UpdateButtons()
        {
            int index = _grid.CurrentRow != null ? _grid.CurrentRow.Index : -1;
            _deleteButton.Enabled = index >= 0;
            _upButton.Enabled = index > 0;
            _downButton.Enabled = index >= 0 && index < _draft.Devices.Count - 1;
        }

        private void RaiseChanged()
        {
            UpdateCountAndButtonsCore();
            Action handler = DraftChanged;
            if (handler != null) handler();
        }

        private void ReportValidation(string error)
        {
            Action<string> handler = ValidationFailed;
            if (handler != null) handler(error);
        }
    }
}
