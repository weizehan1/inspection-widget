using System;
using System.Collections.Generic;
using System.IO;
using InspectionWidget.Capture;
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
                IsDeviceCollapsed = source.IsDeviceCollapsed,
                AutoClearClipboard = source.AutoClearClipboard,
                CaptureMode = source.CaptureMode,
                WindowLeft = source.WindowLeft,
                WindowTop = source.WindowTop,
                LastDevice = source.LastDevice,
                UiScalePercent = source.UiScalePercent,
                FolderNamePattern = source.FolderNamePattern,
                TrayHintShown = source.TrayHintShown,
                QuickLinks = QuickLinkCloner.CloneList(source.QuickLinks),
                PlanModeEnabled = source.PlanModeEnabled,
                HideVisitedDevices = source.HideVisitedDevices
            };
            if (source.Credentials != null)
            {
                foreach (CredentialEntry entry in source.Credentials)
                {
                    if (entry == null) continue;
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
    /// 明文只在调用 Protect 时短暂存在；返回值只有密文或错误。
    /// </summary>
    public static class CredentialRowCollector
    {
        public const int MaxCredentials = 10;

        public sealed class RowInput
        {
            public string Title;
            public string OriginalSecret;
            public bool OriginalNeedsReset;
            public bool IsNew;
            public bool Cleared;
            public string NewPlainText;
        }

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

    /// <summary>列表单步移动的纯逻辑。</summary>
    public static class ListReorder
    {
        public static bool Move<T>(IList<T> list, int index, int delta)
        {
            if (list == null || delta == 0)
                return false;
            int target = index + delta;
            if (index < 0 || index >= list.Count || target < 0 || target >= list.Count)
                return false;
            T moving = list[index];
            list[index] = list[target];
            list[target] = moving;
            return true;
        }
    }

    /// <summary>
    /// 设置保存的校验＋组装（不落盘），供 ApplyCoordinator 与兼容 Execute 共用。
    /// 任一步失败都不产生候选配置；错误含组别与行号/名称定位信息。
    /// </summary>
    public static class SettingsSaveCoordinator
    {
        public sealed class Outcome
        {
            public bool Saved;
            public string Error;
            public int ErrorGroup;
            public int CredentialErrorIndex;
            public bool CredentialErrorOnTitle;
            public int LinkErrorIndex;
            public bool LinkErrorOnTitle;
            public AppConfig Result;
        }

        public static Outcome Execute(
            AppConfig working,
            List<string> devices,
            List<CredentialRowCollector.RowInput> credentialInputs,
            string rootPath,
            string folderPattern,
            bool autoClearClipboard,
            int uiScalePercent,
            CaptureMode captureMode,
            ICredentialProtector protector,
            ConfigStore store)
        {
            Outcome outcome = Build(working, devices, credentialInputs, rootPath,
                folderPattern, autoClearClipboard, uiScalePercent, captureMode, protector);
            if (!outcome.Saved)
                return outcome;

            SaveResult save;
            try
            {
                save = store.Save(outcome.Result);
            }
            catch (Exception ex)
            {
                return Fail("设置未保存：" + ex.Message, -1);
            }
            if (save == null || !save.Saved)
                return Fail("设置未保存：" + (save != null ? save.Error : "未知错误"), -1);
            return outcome;
        }

        public static Outcome Build(
            AppConfig working,
            List<string> devices,
            List<CredentialRowCollector.RowInput> credentialInputs,
            string rootPath,
            string folderPattern,
            bool autoClearClipboard,
            int uiScalePercent,
            CaptureMode captureMode,
            ICredentialProtector protector)
        {
            return BuildCore(
                working,
                devices,
                credentialInputs,
                working != null ? working.QuickLinks : null,
                false,
                rootPath,
                folderPattern,
                autoClearClipboard,
                uiScalePercent,
                captureMode,
                working != null && working.PlanModeEnabled,
                working == null || working.HideVisitedDevices,
                protector,
                -1,
                2);
        }

        public static Outcome Build(
            AppConfig working,
            List<string> devices,
            List<CredentialRowCollector.RowInput> credentialInputs,
            IList<QuickLinkEntry> quickLinks,
            string rootPath,
            string folderPattern,
            bool autoClearClipboard,
            int uiScalePercent,
            CaptureMode captureMode,
            bool planModeEnabled,
            bool hideVisitedDevices,
            ICredentialProtector protector)
        {
            return BuildCore(
                working,
                devices,
                credentialInputs,
                quickLinks,
                true,
                rootPath,
                folderPattern,
                autoClearClipboard,
                uiScalePercent,
                captureMode,
                planModeEnabled,
                hideVisitedDevices,
                protector,
                2,
                3);
        }

        private static Outcome BuildCore(
            AppConfig working,
            List<string> devices,
            List<CredentialRowCollector.RowInput> credentialInputs,
            IList<QuickLinkEntry> quickLinks,
            bool validateQuickLinks,
            string rootPath,
            string folderPattern,
            bool autoClearClipboard,
            int uiScalePercent,
            CaptureMode captureMode,
            bool planModeEnabled,
            bool hideVisitedDevices,
            ICredentialProtector protector,
            int linksGroup,
            int storageGroup)
        {
            string deviceError = DeviceNameValidator.ValidateAll(devices);
            if (deviceError != null)
                return Fail("设备管理：设备名不合法——" + deviceError, 0);

            string rootError = ConfigValidator.ValidateRootPath(rootPath);
            if (rootError != null)
                return Fail("存储与显示：" + rootError, storageGroup);

            string patternError = FolderNamePattern.Validate(folderPattern);
            if (patternError != null)
                return Fail("存储与显示：日期文件夹名——" + patternError, storageGroup);

            if (!string.IsNullOrEmpty(rootPath))
            {
                string todayDir = Path.Combine(rootPath, FolderNamePattern.Format(folderPattern, DateTime.Now));
                for (int i = 0; i < devices.Count; i++)
                {
                    string lengthError = DeviceNameValidator.ValidateFullPathLength(todayDir, devices[i] + ".png");
                    if (lengthError != null)
                        return Fail("设备管理：设备「" + devices[i] + "」" + lengthError, 0);
                }
            }

            if (credentialInputs.Count > CredentialRowCollector.MaxCredentials)
                return Fail("凭据管理：凭据最多 " + CredentialRowCollector.MaxCredentials + " 条。", 1);

            var credentials = new List<CredentialEntry>();
            for (int i = 0; i < credentialInputs.Count; i++)
            {
                CredentialRowCollector.RowInput input = credentialInputs[i];
                string error;
                string secret = CredentialRowCollector.CollectSecret(input, protector, out error);
                if (error != null)
                {
                    return new Outcome
                    {
                        Saved = false,
                        Error = "凭据管理：第 " + (i + 1) + " 条凭据——" + error,
                        ErrorGroup = 1,
                        CredentialErrorIndex = i,
                        CredentialErrorOnTitle = string.IsNullOrWhiteSpace(input.Title),
                        LinkErrorIndex = -1
                    };
                }
                bool newSecretEntered = !string.IsNullOrEmpty(input.NewPlainText);
                credentials.Add(new CredentialEntry
                {
                    Title = input.Title,
                    EncryptedSecret = secret,
                    NeedsReset = input.Cleared
                        || (!newSecretEntered
                            && (input.OriginalNeedsReset || string.IsNullOrEmpty(secret)))
                });
            }

            List<QuickLinkEntry> normalizedLinks = QuickLinkCloner.CloneList(quickLinks);
            if (validateQuickLinks)
            {
                if (normalizedLinks.Count > QuickLinkValidator.MaxLinks)
                {
                    return Fail("快捷链接：最多配置 " + QuickLinkValidator.MaxLinks + " 条。", linksGroup);
                }
                for (int i = 0; i < normalizedLinks.Count; i++)
                {
                    QuickLinkEntry item = normalizedLinks[i];
                    string linkError = QuickLinkValidator.Validate(item);
                    if (linkError != null)
                    {
                        return new Outcome
                        {
                            Saved = false,
                            Error = "快捷链接：第 " + (i + 1) + " 条——" + linkError,
                            ErrorGroup = linksGroup,
                            CredentialErrorIndex = -1,
                            LinkErrorIndex = i,
                            LinkErrorOnTitle = item == null || string.IsNullOrWhiteSpace(item.Title)
                        };
                    }
                    item.Title = item.Title.Trim();
                    item.Url = item.Url.Trim();
                }
                string allLinksError = QuickLinkValidator.ValidateAll(normalizedLinks);
                if (allLinksError != null)
                    return Fail("快捷链接：" + allLinksError, linksGroup);
            }

            AppConfig result = AppConfigCloner.Clone(working);
            result.Devices = new List<string>(devices);
            result.Credentials = credentials;
            result.QuickLinks = normalizedLinks;
            result.RootPath = rootPath;
            result.FolderNamePattern = folderPattern;
            result.AutoClearClipboard = autoClearClipboard;
            result.UiScalePercent = uiScalePercent;
            result.CaptureMode = captureMode;
            result.PlanModeEnabled = planModeEnabled;
            result.HideVisitedDevices = hideVisitedDevices;
            if (result.LastDevice != null)
            {
                bool found = false;
                for (int i = 0; i < devices.Count; i++)
                {
                    if (string.Equals(devices[i], result.LastDevice, StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found) result.LastDevice = null;
            }

            return new Outcome
            {
                Saved = true,
                Result = result,
                ErrorGroup = -1,
                CredentialErrorIndex = -1,
                LinkErrorIndex = -1
            };
        }

        private static Outcome Fail(string error, int group)
        {
            return new Outcome
            {
                Saved = false,
                Error = error,
                ErrorGroup = group,
                CredentialErrorIndex = -1,
                LinkErrorIndex = -1
            };
        }
    }
}
