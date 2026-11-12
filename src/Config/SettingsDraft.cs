using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace InspectionWidget.Config
{
    /// <summary>设备表格的一行；空名称是允许的草稿态，提交时才校验必填。</summary>
    public sealed class DeviceDraftItem
    {
        public DeviceDraftItem()
        {
        }

        public DeviceDraftItem(string name)
        {
            Name = name;
        }

        public string Name { get; set; }
    }

    /// <summary>凭据草稿行：只保存密文基线与编辑状态，不保存明文引用。</summary>
    public sealed class CredentialDraft
    {
        public string Title;
        public string OriginalSecret;
        public bool IsNew;
        public bool Cleared;
        public bool OriginalNeedsReset;

        public CredentialDraft Clone()
        {
            return new CredentialDraft
            {
                Title = Title,
                OriginalSecret = OriginalSecret,
                IsNew = IsNew,
                Cleared = Cleared,
                OriginalNeedsReset = OriginalNeedsReset
            };
        }
    }

    /// <summary>
    /// 设置窗口唯一编辑态：DataGridView、凭据行和导入候选都引用这里，
    /// 不再维护第二份可漂移的字符串列表。
    /// </summary>
    public sealed class SettingsDraft
    {
        public readonly BindingList<DeviceDraftItem> Devices = new BindingList<DeviceDraftItem>();
        public readonly List<CredentialDraft> Credentials = new List<CredentialDraft>();

        public string RootPath;
        public string FolderPattern;
        public bool AutoClearClipboard;
        public int UiScalePercent;
        public CaptureMode CaptureMode;
        public bool IsDeviceCollapsed;
        public bool IsCredentialCollapsed;
        public string LastDevice;
        public int? WindowLeft;
        public int? WindowTop;
        public bool TrayHintShown;

        public string SavedRevision;
        public bool ImportPending;

        public static SettingsDraft FromConfig(AppConfig config, string revision)
        {
            if (config == null) config = AppConfig.CreateDefault();
            var draft = new SettingsDraft();
            draft.LoadFrom(config);
            draft.SavedRevision = revision;
            return draft;
        }

        public void LoadFrom(AppConfig config)
        {
            if (config == null) config = AppConfig.CreateDefault();
            Devices.RaiseListChangedEvents = false;
            try
            {
                Devices.Clear();
                if (config.Devices != null)
                {
                    for (int i = 0; i < config.Devices.Count; i++)
                        Devices.Add(new DeviceDraftItem(config.Devices[i] ?? string.Empty));
                }
            }
            finally
            {
                Devices.RaiseListChangedEvents = true;
                Devices.ResetBindings();
            }

            Credentials.Clear();
            if (config.Credentials != null)
            {
                for (int i = 0; i < config.Credentials.Count; i++)
                {
                    CredentialEntry entry = config.Credentials[i];
                    if (entry == null) continue;
                    Credentials.Add(new CredentialDraft
                    {
                        Title = entry.Title,
                        OriginalSecret = entry.EncryptedSecret,
                        IsNew = false,
                        Cleared = false,
                        OriginalNeedsReset = entry.NeedsReset
                            || string.IsNullOrEmpty(entry.EncryptedSecret)
                    });
                }
            }

            RootPath = config.RootPath;
            FolderPattern = FolderNamePattern.Normalize(config.FolderNamePattern);
            AutoClearClipboard = config.AutoClearClipboard;
            UiScalePercent = UiScale.Normalize(config.UiScalePercent);
            CaptureMode = config.CaptureMode;
            IsDeviceCollapsed = config.IsDeviceCollapsed;
            IsCredentialCollapsed = config.IsCredentialCollapsed;
            LastDevice = config.LastDevice;
            WindowLeft = config.WindowLeft;
            WindowTop = config.WindowTop;
            TrayHintShown = config.TrayHintShown;
        }

        public List<string> DeviceNames()
        {
            var result = new List<string>();
            for (int i = 0; i < Devices.Count; i++)
                result.Add(Devices[i] == null ? null : Devices[i].Name);
            return result;
        }

        public int AddBlankDevice()
        {
            var item = new DeviceDraftItem(string.Empty);
            Devices.Add(item);
            return Devices.Count - 1;
        }

        public bool RemoveDeviceAt(int index)
        {
            if (index < 0 || index >= Devices.Count)
                return false;
            string removed = Devices[index] == null ? null : Devices[index].Name;
            Devices.RemoveAt(index);
            if (removed != null && string.Equals(LastDevice, removed, StringComparison.OrdinalIgnoreCase))
                LastDevice = null;
            return true;
        }

        public bool MoveDevice(int index, int delta)
        {
            return ListReorder.Move(Devices, index, delta);
        }

        /// <summary>旧名称的 LastDevice 跟随新名称；大小写改名也按不区分大小写跟随。</summary>
        public void ApplyDeviceRename(string oldName, string newName)
        {
            if (oldName == null || newName == null)
                return;
            if (string.Equals(LastDevice, oldName, StringComparison.OrdinalIgnoreCase))
                LastDevice = newName;
        }

        public bool ContainsDevice(string name)
        {
            if (name == null) return false;
            for (int i = 0; i < Devices.Count; i++)
            {
                string current = Devices[i] == null ? null : Devices[i].Name;
                if (string.Equals(current, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public void ReplaceDevices(IList<string> names)
        {
            Devices.RaiseListChangedEvents = false;
            try
            {
                Devices.Clear();
                if (names != null)
                {
                    for (int i = 0; i < names.Count; i++)
                        Devices.Add(new DeviceDraftItem(names[i]));
                }
            }
            finally
            {
                Devices.RaiseListChangedEvents = true;
                Devices.ResetBindings();
            }
        }

        public void MergeDevices(IList<string> incoming)
        {
            List<string> merged = ConfigTransfer.MergeDeviceNames(DeviceNames(), incoming);
            ReplaceDevices(merged);
        }

        public void ApplyPlan(ConfigImportPlan plan, bool mergeOnly)
        {
            if (plan == null) return;
            ReplaceDevices(plan.Devices);
            LastDevice = plan.Result.LastDevice;
            if (!mergeOnly)
            {
                RootPath = plan.Result.RootPath;
                FolderPattern = FolderNamePattern.Normalize(plan.Result.FolderNamePattern);
                AutoClearClipboard = plan.Result.AutoClearClipboard;
                UiScalePercent = UiScale.Normalize(plan.Result.UiScalePercent);
                CaptureMode = plan.Result.CaptureMode;
                IsDeviceCollapsed = plan.Result.IsDeviceCollapsed;
                IsCredentialCollapsed = plan.Result.IsCredentialCollapsed;
                WindowLeft = plan.Result.WindowLeft;
                WindowTop = plan.Result.WindowTop;
                TrayHintShown = plan.Result.TrayHintShown;
            }

            if (plan.CredentialsReplaced)
            {
                Credentials.Clear();
                if (plan.Credentials != null)
                {
                    for (int i = 0; i < plan.Credentials.Count; i++)
                    {
                        CredentialEntry entry = plan.Credentials[i];
                        Credentials.Add(new CredentialDraft
                        {
                            Title = entry.Title,
                            OriginalSecret = entry.EncryptedSecret,
                            IsNew = false,
                            Cleared = false,
                            OriginalNeedsReset = entry.NeedsReset
                                || string.IsNullOrEmpty(entry.EncryptedSecret)
                        });
                    }
                }
            }

            ImportPending = true;
        }

        /// <summary>应用成功后把整个草稿的已保存基线对齐到落盘结果。</summary>
        public void AcceptSaved(AppConfig saved, string revision)
        {
            LoadFrom(saved);
            SavedRevision = revision;
            ImportPending = false;
        }

        public AppConfig SnapshotForTransferPreview()
        {
            var config = new AppConfig
            {
                RootPath = RootPath,
                Devices = DeviceNames(),
                Credentials = new List<CredentialEntry>(),
                IsCredentialCollapsed = IsCredentialCollapsed,
                IsDeviceCollapsed = IsDeviceCollapsed,
                AutoClearClipboard = AutoClearClipboard,
                CaptureMode = CaptureMode,
                WindowLeft = WindowLeft,
                WindowTop = WindowTop,
                LastDevice = LastDevice,
                UiScalePercent = UiScalePercent,
                FolderNamePattern = FolderPattern,
                TrayHintShown = TrayHintShown
            };
            for (int i = 0; i < Credentials.Count; i++)
            {
                CredentialDraft row = Credentials[i];
                config.Credentials.Add(new CredentialEntry
                {
                    Title = row.Title,
                    EncryptedSecret = row.Cleared ? string.Empty : row.OriginalSecret,
                    NeedsReset = row.Cleared || row.OriginalNeedsReset
                });
            }
            return config;
        }
    }
}
