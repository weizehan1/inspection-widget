using System;
using System.Collections.Generic;
using InspectionWidget.Capture;
using InspectionWidget.Config;
using InspectionWidget.Storage;

namespace InspectionWidget.Planning
{
    /// <summary>
    /// 主面板与今日巡检计划的纯状态协调层：把点击设备、选窗取消/失败、
    /// 保存成功、显示过滤（隐藏已点过 / 查看已点过）、恢复未点、跨日重置、
    /// 设置生效（改名/合并/导入替换/取消）以及完成弹窗单次声明收敛为一处，
    /// 供 MainForm 调用并可直接在无窗体环境单测。
    /// </summary>
    public sealed class InspectionSessionController
    {
        private readonly PanelStateMachine _stateMachine;
        private readonly InspectionProgressStore _progressStore;
        private readonly InspectionFolderNavigator _folderNavigator;
        private readonly InspectionPlan _plan;
        private AppConfig _config;
        private bool _showVisitedOverride;
        private string _progressProtectError;
        private string _progressSaveError;
        private bool _pendingRolloverUiRefresh;

        public InspectionSessionController(
            IClock clock,
            PanelStateMachine stateMachine,
            InspectionProgressStore progressStore,
            AppConfig config)
            : this(clock, stateMachine, progressStore, config, new InspectionFolderNavigator())
        {
        }

        public InspectionSessionController(
            IClock clock,
            PanelStateMachine stateMachine,
            InspectionProgressStore progressStore,
            AppConfig config,
            InspectionFolderNavigator folderNavigator)
        {
            if (clock == null) throw new ArgumentNullException("clock");
            if (stateMachine == null) throw new ArgumentNullException("stateMachine");
            _stateMachine = stateMachine;
            _progressStore = progressStore;
            _folderNavigator = folderNavigator ?? new InspectionFolderNavigator();
            _config = config ?? AppConfig.CreateDefault();
            _plan = new InspectionPlan(clock, _config.Devices);

            string initialError;
            LoadProgressFromStore(false, out initialError);
        }

        public bool IsProgressProtected
        {
            get { return !string.IsNullOrEmpty(_progressProtectError); }
        }

        public string ProgressError
        {
            get { return _progressProtectError ?? _progressSaveError; }
        }

        public bool HasProgressError
        {
            get { return !string.IsNullOrEmpty(ProgressError); }
        }

        public bool HasPendingRolloverRefresh
        {
            get { return _pendingRolloverUiRefresh; }
        }

        public bool ConsumePendingRolloverRefresh()
        {
            bool was = _pendingRolloverUiRefresh;
            _pendingRolloverUiRefresh = false;
            return was;
        }

        public bool RetryLoadProgress(out string error)
        {
            bool ok = LoadProgressFromStore(true, out error);
            if (ok)
                _pendingRolloverUiRefresh = true;
            return ok;
        }

        public bool RetrySaveProgress(out string error)
        {
            SaveProgress(out error);
            return string.IsNullOrEmpty(error);
        }

        public bool BackupAndResetProgress(out string backupPath, out string error)
        {
            backupPath = null;
            error = null;
            if (_progressStore != null)
            {
                if (!_progressStore.TryBackupCorruptCopy(out backupPath, out error))
                {
                    // 备份失败时绝不解除保护或覆写原损坏文件。
                    _progressProtectError = "进度损坏且备份失败（未覆盖原文件）：" + (error ?? "未知错误");
                    error = _progressProtectError;
                    return false;
                }
            }

            _progressProtectError = null;
            _progressSaveError = null;
            _showVisitedOverride = false;
            _plan.ResetRound();
            _plan.ReconcileDevices(_config != null ? _config.Devices : null);
            _pendingRolloverUiRefresh = true;
            SaveProgress(out error);
            return string.IsNullOrEmpty(error);
        }

        public InspectionPlan Plan
        {
            get { return _plan; }
        }

        public bool ShowVisitedOverride
        {
            get { return _showVisitedOverride; }
        }

        public bool PlanModeEnabled
        {
            get { return _config != null && _config.PlanModeEnabled; }
        }

        public bool HideVisitedDevices
        {
            get { return _config == null || _config.HideVisitedDevices; }
        }

        public DeviceDisplaySnapshot BuildDeviceDisplay()
        {
            bool rolled;
            EnsureCurrentDayResetIfRolled(out rolled);
            return InspectionDeviceDisplayFilter.Build(
                _plan,
                _config != null ? _config.Devices : null,
                PlanModeEnabled,
                HideVisitedDevices,
                _showVisitedOverride);
        }

        public bool ToggleShowVisited()
        {
            _showVisitedOverride = !_showVisitedOverride;
            return _showVisitedOverride;
        }

        public void SetShowVisitedOverride(bool value)
        {
            _showVisitedOverride = value;
        }

        public void SetPlanModeEnabled(bool enabled)
        {
            if (_config != null)
                _config.PlanModeEnabled = enabled;
            bool rolled;
            EnsureCurrentDayResetIfRolled(out rolled);
            _plan.ReconcileDevices(_config != null ? _config.Devices : null);
        }

        public bool TrySelectDevice(
            string device,
            out InspectionPlanToken capturedToken,
            out string progressSaveError)
        {
            capturedToken = default(InspectionPlanToken);
            progressSaveError = null;
            bool rolled;
            EnsureCurrentDayResetIfRolled(out rolled);
            InspectionPlanToken token = _plan.CurrentToken;
            if (!_stateMachine.SelectDevice(device))
                return false;

            if (_stateMachine.State == PanelState.Waiting)
            {
                if (_config != null)
                    _config.LastDevice = _stateMachine.CurrentDevice;
                capturedToken = token;
                if (PlanModeEnabled && _plan.MarkVisited(device, token))
                    SaveProgress(out progressSaveError);
                else if (HasProgressError)
                    progressSaveError = ProgressError;
            }
            return true;
        }

        public bool RecordSaveSucceeded(
            string fullPath,
            InspectionPlanToken token,
            out string progressSaveError)
        {
            progressSaveError = null;
            bool rolled;
            EnsureCurrentDayResetIfRolled(out rolled);
            if (string.IsNullOrWhiteSpace(fullPath))
                return false;
            if (!_plan.RecordSavedPath(fullPath, token))
            {
                if (HasProgressError)
                    progressSaveError = ProgressError;
                return false;
            }
            SaveProgress(out progressSaveError);
            return true;
        }

        public bool UnmarkDevice(string device, out string progressSaveError)
        {
            progressSaveError = null;
            bool rolled;
            EnsureCurrentDayResetIfRolled(out rolled);
            bool wasVisited = _plan.IsVisited(device);
            _plan.Unmark(device);
            if (wasVisited && !_plan.IsVisited(device))
            {
                SaveProgress(out progressSaveError);
                return true;
            }
            return false;
        }

        public void ResetRound(out string progressSaveError)
        {
            progressSaveError = null;
            bool rolled;
            EnsureCurrentDayResetIfRolled(out rolled);
            _plan.ResetRound();
            _showVisitedOverride = false;
            SaveProgress(out progressSaveError);
        }

        public void ApplySavedConfig(
            AppConfig savedConfig,
            IReadOnlyList<DeviceRenameRecord> renames,
            bool importReplacedDevices,
            out string progressSaveError)
        {
            progressSaveError = null;
            _config = savedConfig ?? AppConfig.CreateDefault();
            bool rolled;
            EnsureCurrentDayResetIfRolled(out rolled);
            if (importReplacedDevices)
            {
                _plan.ReplaceDevices(_config.Devices);
                _showVisitedOverride = false;
            }
            else
            {
                if (renames != null)
                {
                    for (int i = 0; i < renames.Count; i++)
                    {
                        DeviceRenameRecord r = renames[i];
                        if (r != null)
                            _plan.RenameDevice(r.OldName, r.NewName);
                    }
                }
                _plan.ReconcileDevices(_config.Devices);
            }
            if (!HideVisitedDevices)
                _showVisitedOverride = false;
            SaveProgress(out progressSaveError);
        }

        public void ReloadConfig(AppConfig nextConfig, out string progressSaveError)
        {
            progressSaveError = null;
            _config = nextConfig ?? AppConfig.CreateDefault();
            bool rolled;
            EnsureCurrentDayResetIfRolled(out rolled);
            _plan.ReconcileDevices(_config.Devices);
            if (!HideVisitedDevices)
                _showVisitedOverride = false;
            SaveProgress(out progressSaveError);
        }

        public bool EnsureCurrentDayResetIfRolled(out bool dayRolled)
        {
            DateTime prevDate = _plan.Date;
            _plan.EnsureCurrentDay();
            dayRolled = _plan.Date != prevDate;
            if (dayRolled)
            {
                _showVisitedOverride = false;
                _pendingRolloverUiRefresh = true;
            }
            return dayRolled;
        }

        public bool TryClaimCompletionPrompt(
            bool saveInFlight,
            bool settingsDialogOpen,
            bool panelHidden,
            out string progressSaveError)
        {
            return TryClaimCompletionPrompt(
                saveInFlight,
                settingsDialogOpen,
                panelHidden,
                true,
                out progressSaveError);
        }

        public bool TryClaimCompletionPrompt(
            bool saveInFlight,
            bool settingsDialogOpen,
            bool hiddenForCapture,
            bool panelVisible,
            out string progressSaveError)
        {
            progressSaveError = null;
            if (!PlanModeEnabled || saveInFlight || settingsDialogOpen || hiddenForCapture || !panelVisible)
                return false;
            if (_stateMachine.State != PanelState.Idle)
                return false;
            bool rolled;
            EnsureCurrentDayResetIfRolled(out rolled);
            bool claimed = _plan.TryClaimCompletionPrompt(false);
            if (claimed)
                SaveProgress(out progressSaveError);
            return claimed;
        }

        public List<string> ResolveOpenFolders()
        {
            bool rolled;
            EnsureCurrentDayResetIfRolled(out rolled);
            return _folderNavigator.ResolveFolders(
                _config != null ? _config.RootPath : null,
                _config != null ? _config.FolderNamePattern : null,
                _plan.Date,
                _plan.SavedPaths);
        }

        private bool LoadProgressFromStore(bool isRetry, out string error)
        {
            error = null;
            if (_progressStore == null)
            {
                _progressProtectError = null;
                _progressSaveError = null;
                _plan.EnsureCurrentDay();
                return true;
            }

            InspectionProgressSnapshot snapshot;
            string loadError;
            if (!_progressStore.TryLoad(out snapshot, out loadError))
            {
                _progressProtectError = loadError ?? "巡检进度读取失败，已保护原文件。";
                error = _progressProtectError;
                return false;
            }

            if (snapshot == null)
            {
                _progressProtectError = null;
                _progressSaveError = null;
                if (isRetry)
                {
                    _plan.ResetRound();
                    _plan.ReconcileDevices(_config != null ? _config.Devices : null);
                }
                _plan.EnsureCurrentDay();
                return true;
            }

            ProgressRestoreStatus status;
            string restoreError;
            if (_plan.RestoreWithStatus(snapshot, out status, out restoreError))
            {
                _progressProtectError = null;
                _progressSaveError = null;
                _plan.ReconcileDevices(_config != null ? _config.Devices : null);
                _plan.EnsureCurrentDay();
                return true;
            }

            if (status == ProgressRestoreStatus.PreviousDayValid)
            {
                // 有效的昨日进度不属于损坏：正常进入今日新轮，不开启写保护。
                _progressProtectError = null;
                _progressSaveError = null;
                if (isRetry)
                {
                    _plan.ResetRound();
                    _plan.ReconcileDevices(_config != null ? _config.Devices : null);
                }
                _plan.EnsureCurrentDay();
                return true;
            }

            _progressProtectError = restoreError ?? "巡检进度校验失败，已保护原文件。";
            error = _progressProtectError;
            return false;
        }

        private void SaveProgress(out string error)
        {
            error = null;
            if (IsProgressProtected)
            {
                error = _progressProtectError;
                return;
            }
            if (_progressStore == null)
                return;
            InspectionProgressSnapshot snapshot = _plan.CreateSnapshot();
            if (_progressStore.TrySave(snapshot, out error))
            {
                _progressSaveError = null;
            }
            else
            {
                _progressSaveError = error ?? "写入巡检进度失败。";
                error = _progressSaveError;
            }
        }
    }
}
