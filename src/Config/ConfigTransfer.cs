using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using InspectionWidget.Credentials;

namespace InspectionWidget.Config
{
    public sealed class ConfigImportOptions
    {
        /// <summary>默认：只把导入设备按序并入当前草稿。</summary>
        public bool MergeDevicesOnly = true;
        public bool ImportRootPath;
        public bool ReplaceCredentials;
        public bool RestoreLocalFields;
    }

    public sealed class ConfigImportPlan
    {
        public AppConfig Result;
        public List<string> Devices;
        public List<CredentialEntry> Credentials;
        public int AddedDeviceCount;
        public int RemovedDeviceCount;
        public int RetainedDeviceCount;
        public int UsableCredentialCount;
        public int ResetCredentialCount;
        public bool CredentialsReplaced;
        public readonly List<string> SettingChanges = new List<string>();
        public readonly List<string> FieldSources = new List<string>();
    }

    /// <summary>
    /// 配置交换：显式字段集导出、外部文档预览合并、原子文件写入。
    /// 凭据密文只在内存中做可解密性检查，明文立即失去引用且不进入日志/界面。
    /// </summary>
    public static class ConfigTransfer
    {
        public static string BuildExportJson(AppConfig config, string credentialMode)
        {
            if (config == null) throw new ArgumentNullException("config");
            if (credentialMode != ConfigJsonReader.CredentialModeExcluded
                && credentialMode != ConfigJsonReader.CredentialModeDpapi)
            {
                throw new ArgumentException("未知凭据模式。", "credentialMode");
            }

            var json = new StringBuilder();
            json.Append("{\n");
            json.Append("  \"Format\": \"").Append(JsonEscape(ConfigJsonReader.ExchangeFormat)).Append("\",\n");
            json.Append("  \"Version\": ").Append(ConfigJsonReader.ExchangeVersion).Append(",\n");
            json.Append("  \"CredentialMode\": \"").Append(JsonEscape(credentialMode)).Append("\",\n");
            json.Append("  \"Config\": {\n");

            var fields = new List<string>();
            fields.Add("    \"Devices\": " + StringArrayJson(config.Devices));
            fields.Add("    \"RootPath\": " + NullableStringJson(config.RootPath));
            fields.Add("    \"FolderNamePattern\": " + NullableStringJson(config.FolderNamePattern));
            fields.Add("    \"CaptureMode\": " + ((int)config.CaptureMode).ToString());
            fields.Add("    \"UiScalePercent\": " + config.UiScalePercent.ToString());
            fields.Add("    \"AutoClearClipboard\": " + BoolJson(config.AutoClearClipboard));
            fields.Add("    \"IsDeviceCollapsed\": " + BoolJson(config.IsDeviceCollapsed));
            fields.Add("    \"IsCredentialCollapsed\": " + BoolJson(config.IsCredentialCollapsed));

            if (credentialMode == ConfigJsonReader.CredentialModeDpapi)
            {
                fields.Add("    \"Credentials\": " + CredentialsJson(config.Credentials));
                fields.Add("    \"LastDevice\": " + NullableStringJson(config.LastDevice));
                fields.Add("    \"WindowLeft\": " + NullableIntJson(config.WindowLeft));
                fields.Add("    \"WindowTop\": " + NullableIntJson(config.WindowTop));
                fields.Add("    \"TrayHintShown\": " + BoolJson(config.TrayHintShown));
            }

            for (int i = 0; i < fields.Count; i++)
            {
                json.Append(fields[i]);
                if (i < fields.Count - 1) json.Append(',');
                json.Append('\n');
            }
            json.Append("  }\n");
            json.Append("}\n");
            return json.ToString();
        }

        public static bool TryExportFile(AppConfig config, string credentialMode, string targetPath,
            string authoritativePath, out string error)
        {
            error = null;
            try
            {
                if (ConfigWriteProtection.IsManagedConfigPath(targetPath, authoritativePath))
                {
                    error = "不能导出到应用管理的配置、锁文件或恢复备份路径。";
                    return false;
                }
                string json = BuildExportJson(config, credentialMode);
                byte[] bytes = new UTF8Encoding(false).GetBytes(json);
                AtomicWriteResult result = new AtomicConfigWriter().WriteAllBytes(targetPath, bytes);
                if (!result.Written)
                {
                    error = result.Error;
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "导出配置失败：" + ex.Message;
                return false;
            }
        }

        public static bool TryBuildPlan(ConfigImportDocument document, AppConfig current,
            ConfigImportOptions options, ICredentialProtector protector,
            out ConfigImportPlan plan, out string error)
        {
            plan = null;
            error = null;
            if (document == null || document.Config == null)
            {
                error = "没有可导入的配置。";
                return false;
            }
            if (current == null)
            {
                error = "当前配置为空。";
                return false;
            }
            if (options == null)
                options = new ConfigImportOptions();

            AppConfig source = document.Config;
            AppConfig result = AppConfigCloner.Clone(current);
            plan = new ConfigImportPlan();

            // 外部解析与旧裸格式都可能带来仅在运行时才暴露的路径问题；
            // 在生成任何可提交候选前统一再校验一次。
            string rootValidation = ConfigValidator.ValidateRootPath(source.RootPath);
            if (rootValidation != null)
            {
                error = rootValidation;
                return false;
            }

            List<string> finalDevices;
            if (options.MergeDevicesOnly)
            {
                finalDevices = MergeDeviceNames(current.Devices, source.Devices);
                plan.SettingChanges.Add("设备名单：按当前顺序保留，新设备按导入顺序追加");
            }
            else if (document.Has("Devices"))
            {
                finalDevices = new List<string>(source.Devices ?? new List<string>());
                plan.SettingChanges.Add("设备名单：替换");
                AddSource(plan, document, "Devices");
            }
            else
            {
                finalDevices = new List<string>(current.Devices ?? new List<string>());
            }

            result.Devices = finalDevices;
            FillDeviceCounts(plan, current.Devices, finalDevices);

            if (!options.MergeDevicesOnly)
            {
                if (document.Has("RootPath") && options.ImportRootPath)
                {
                    result.RootPath = source.RootPath;
                    plan.SettingChanges.Add("截图保存目录：采用源文件中的值");
                    AddSource(plan, document, "RootPath");
                }
                else if (document.Has("RootPath"))
                {
                    plan.SettingChanges.Add("截图保存目录：保留当前值（未勾选导入）");
                }

                CopyField(document, result, source, "FolderNamePattern",
                    delegate(AppConfig c, AppConfig s) { c.FolderNamePattern = s.FolderNamePattern; },
                    plan, "日期文件夹名");
                CopyField(document, result, source, "CaptureMode",
                    delegate(AppConfig c, AppConfig s) { c.CaptureMode = s.CaptureMode; },
                    plan, "截图模式");
                CopyField(document, result, source, "UiScalePercent",
                    delegate(AppConfig c, AppConfig s) { c.UiScalePercent = s.UiScalePercent; },
                    plan, "界面缩放");
                CopyField(document, result, source, "AutoClearClipboard",
                    delegate(AppConfig c, AppConfig s) { c.AutoClearClipboard = s.AutoClearClipboard; },
                    plan, "剪贴板自动清空");
                CopyField(document, result, source, "IsDeviceCollapsed",
                    delegate(AppConfig c, AppConfig s) { c.IsDeviceCollapsed = s.IsDeviceCollapsed; },
                    plan, "截图区折叠状态");
                CopyField(document, result, source, "IsCredentialCollapsed",
                    delegate(AppConfig c, AppConfig s) { c.IsCredentialCollapsed = s.IsCredentialCollapsed; },
                    plan, "凭据区折叠状态");

                if (document.CredentialsPresent && options.ReplaceCredentials)
                {
                    if (!TryCopyCredentials(source.Credentials, protector,
                        out plan.Credentials, out plan.UsableCredentialCount,
                        out plan.ResetCredentialCount, out error))
                    {
                        return false;
                    }
                    result.Credentials = CloneCredentials(plan.Credentials);
                    plan.CredentialsReplaced = true;
                    plan.SettingChanges.Add("凭据：整表替换为 " + plan.Credentials.Count
                        + " 条（可用 " + plan.UsableCredentialCount
                        + "，需重设 " + plan.ResetCredentialCount + "）");
                    AddSource(plan, document, "Credentials");
                }
                else if (document.CredentialsPresent)
                {
                    plan.Credentials = CloneCredentials(current.Credentials);
                    plan.SettingChanges.Add("凭据：保留当前值（未勾选替换凭据）");
                }
                else
                {
                    plan.Credentials = CloneCredentials(current.Credentials);
                    plan.SettingChanges.Add("凭据：保留当前值（源文件未参与导入）");
                }

                if (options.RestoreLocalFields && document.IsFullBackup)
                {
                    if (document.Has("LastDevice"))
                    {
                        result.LastDevice = source.LastDevice;
                        AddSource(plan, document, "LastDevice");
                    }
                    if (document.Has("WindowLeft"))
                    {
                        result.WindowLeft = source.WindowLeft;
                        AddSource(plan, document, "WindowLeft");
                    }
                    if (document.Has("WindowTop"))
                    {
                        result.WindowTop = source.WindowTop;
                        AddSource(plan, document, "WindowTop");
                    }
                    if (document.Has("TrayHintShown"))
                    {
                        result.TrayHintShown = source.TrayHintShown;
                        AddSource(plan, document, "TrayHintShown");
                    }
                    plan.SettingChanges.Add("本机字段：按明确选择恢复（窗口位置稍后仍受工作区钳制）");
                }
                else
                {
                    plan.Credentials = plan.Credentials ?? CloneCredentials(current.Credentials);
                }
            }
            else
            {
                plan.Credentials = CloneCredentials(current.Credentials);
                plan.SettingChanges.Add("其他设置：保留当前草稿值");
                plan.SettingChanges.Add("凭据：保留当前草稿值");
            }

            if (result.LastDevice != null
                && !ContainsIgnoreCase(finalDevices, result.LastDevice))
            {
                result.LastDevice = null;
            }
            plan.Devices = new List<string>(finalDevices);
            plan.Result = result;
            return true;
        }

        public static List<string> MergeDeviceNames(IList<string> current, IList<string> incoming)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (current != null)
            {
                for (int i = 0; i < current.Count; i++)
                {
                    if (current[i] != null && seen.Add(current[i]))
                        result.Add(current[i]);
                }
            }
            if (incoming != null)
            {
                for (int i = 0; i < incoming.Count; i++)
                {
                    if (incoming[i] != null && seen.Add(incoming[i]))
                        result.Add(incoming[i]);
                }
            }
            return result;
        }

        /// <summary>
        /// 导入预览的设备变化文本：最终顺序逐行编号，并列出新增/移除名称。
        /// 大小写不同视为同一设备，顺序变化也会显式提示。
        /// </summary>
        public static string BuildDeviceChangeText(IList<string> current, IList<string> final)
        {
            var currentNames = new List<string>();
            var finalNames = new List<string>();
            var currentSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var finalSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (current != null)
            {
                for (int i = 0; i < current.Count; i++)
                {
                    if (current[i] == null) continue;
                    currentNames.Add(current[i]);
                    currentSet.Add(current[i]);
                }
            }
            if (final != null)
            {
                for (int i = 0; i < final.Count; i++)
                {
                    if (final[i] == null) continue;
                    finalNames.Add(final[i]);
                    finalSet.Add(final[i]);
                }
            }

            var added = new List<string>();
            for (int i = 0; i < finalNames.Count; i++)
            {
                if (!currentSet.Contains(finalNames[i]))
                    added.Add(finalNames[i]);
            }
            var removed = new List<string>();
            for (int i = 0; i < currentNames.Count; i++)
            {
                if (!finalSet.Contains(currentNames[i]))
                    removed.Add(currentNames[i]);
            }

            var lines = new List<string>();
            lines.Add("最终设备顺序（共 " + finalNames.Count + " 台）：");
            if (finalNames.Count == 0)
                lines.Add("  （无设备）");
            for (int i = 0; i < finalNames.Count; i++)
                lines.Add("  " + (i + 1) + ". " + finalNames[i]);
            lines.Add(added.Count == 0
                ? "新增设备：无"
                : "新增设备：" + string.Join("、", added.ToArray()));
            lines.Add(removed.Count == 0
                ? "移除设备：无"
                : "移除设备：" + string.Join("、", removed.ToArray()));

            bool sameOrder = currentNames.Count == finalNames.Count;
            if (sameOrder)
            {
                for (int i = 0; i < currentNames.Count; i++)
                {
                    if (!string.Equals(currentNames[i], finalNames[i],
                        StringComparison.OrdinalIgnoreCase))
                    {
                        sameOrder = false;
                        break;
                    }
                }
            }
            if (sameOrder && added.Count == 0 && removed.Count == 0)
                lines.Add("设备顺序未变化。");
            return string.Join("\n", lines.ToArray());
        }

        private static void FillDeviceCounts(ConfigImportPlan plan,
            IList<string> current, IList<string> final)
        {
            var currentSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var finalSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (current != null)
            {
                for (int i = 0; i < current.Count; i++)
                    if (current[i] != null) currentSet.Add(current[i]);
            }
            if (final != null)
            {
                for (int i = 0; i < final.Count; i++)
                    if (final[i] != null) finalSet.Add(final[i]);
            }
            foreach (string name in finalSet)
                if (!currentSet.Contains(name)) plan.AddedDeviceCount++;
            foreach (string name in currentSet)
                if (!finalSet.Contains(name)) plan.RemovedDeviceCount++;
            foreach (string name in finalSet)
                if (currentSet.Contains(name)) plan.RetainedDeviceCount++;
        }

        private static void CopyField(ConfigImportDocument document, AppConfig result,
            AppConfig source, string field, Action<AppConfig, AppConfig> copy,
            ConfigImportPlan plan, string label)
        {
            if (!document.Has(field))
                return;
            copy(result, source);
            plan.SettingChanges.Add(label + "：采用源文件中的值");
            AddSource(plan, document, field);
        }

        private static void AddSource(ConfigImportPlan plan,
            ConfigImportDocument document, string field)
        {
            string source;
            if (document.FieldSources.TryGetValue(field, out source)
                && !string.Equals(source, "源文件", StringComparison.Ordinal))
            {
                plan.FieldSources.Add(field + "：" + source);
            }
        }

        private static bool TryCopyCredentials(List<CredentialEntry> source,
            ICredentialProtector protector, out List<CredentialEntry> credentials,
            out int usable, out int reset, out string error)
        {
            credentials = new List<CredentialEntry>();
            usable = 0;
            reset = 0;
            error = null;
            if (source == null)
            {
                error = "凭据列表为空值。";
                return false;
            }
            string validation = ConfigValidator.ValidateCredentialList(source);
            if (validation != null)
            {
                error = validation;
                return false;
            }

            for (int i = 0; i < source.Count; i++)
            {
                CredentialEntry entry = source[i];
                bool needsReset = string.IsNullOrEmpty(entry.EncryptedSecret);
                if (!needsReset)
                {
                    if (protector == null)
                    {
                        // 不能把“未检查”当成可用；导入方必须给出当前环境保护器。
                        needsReset = true;
                    }
                    else
                    {
                        try
                        {
                            string plain = protector.Unprotect(entry.EncryptedSecret);
                            plain = null;
                            needsReset = false;
                        }
                        catch (Exception)
                        {
                            needsReset = true;
                        }
                    }
                }
                if (needsReset) reset++;
                else usable++;
                credentials.Add(new CredentialEntry
                {
                    Title = entry.Title,
                    EncryptedSecret = entry.EncryptedSecret ?? string.Empty,
                    NeedsReset = needsReset
                });
            }
            return true;
        }

        private static List<CredentialEntry> CloneCredentials(List<CredentialEntry> source)
        {
            var result = new List<CredentialEntry>();
            if (source == null)
                return result;
            for (int i = 0; i < source.Count; i++)
            {
                CredentialEntry entry = source[i];
                if (entry == null) continue;
                result.Add(new CredentialEntry
                {
                    Title = entry.Title,
                    EncryptedSecret = entry.EncryptedSecret,
                    NeedsReset = entry.NeedsReset
                });
            }
            return result;
        }

        private static bool ContainsIgnoreCase(IList<string> list, string value)
        {
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], value, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string StringArrayJson(IList<string> values)
        {
            if (values == null) return "[]";
            var json = new StringBuilder("[");
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0) json.Append(", ");
                json.Append('"').Append(JsonEscape(values[i] ?? string.Empty)).Append('"');
            }
            json.Append(']');
            return json.ToString();
        }

        private static string CredentialsJson(IList<CredentialEntry> credentials)
        {
            if (credentials == null) return "[]";
            var json = new StringBuilder("[");
            for (int i = 0; i < credentials.Count; i++)
            {
                if (i > 0) json.Append(", ");
                CredentialEntry entry = credentials[i];
                json.Append("{\"Title\": \"").Append(JsonEscape(entry.Title ?? string.Empty))
                    .Append("\", \"EncryptedSecret\": \"")
                    .Append(JsonEscape(entry.EncryptedSecret ?? string.Empty)).Append("\"}");
            }
            json.Append(']');
            return json.ToString();
        }

        private static string NullableStringJson(string value)
        {
            return value == null ? "null" : "\"" + JsonEscape(value) + "\"";
        }

        private static string NullableIntJson(int? value)
        {
            return value.HasValue ? value.Value.ToString() : "null";
        }

        private static string BoolJson(bool value)
        {
            return value ? "true" : "false";
        }

        private static string JsonEscape(string value)
        {
            var json = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"': json.Append("\\\""); break;
                    case '\\': json.Append("\\\\"); break;
                    case '\b': json.Append("\\b"); break;
                    case '\f': json.Append("\\f"); break;
                    case '\n': json.Append("\\n"); break;
                    case '\r': json.Append("\\r"); break;
                    case '\t': json.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            json.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            json.Append(c);
                        break;
                }
            }
            return json.ToString();
        }
    }
}
