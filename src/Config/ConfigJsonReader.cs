using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace InspectionWidget.Config
{
    public enum ConfigSourceKind
    {
        Exchange,
        Legacy
    }

    /// <summary>解析后的外部配置；保留字段存在性、显式 null 与来源说明。</summary>
    public sealed class ConfigImportDocument
    {
        public string SourcePath;
        public ConfigSourceKind Kind;
        public string CredentialMode;
        public AppConfig Config;
        public readonly HashSet<string> PresentFields =
            new HashSet<string>(StringComparer.Ordinal);
        public readonly Dictionary<string, string> FieldSources =
            new Dictionary<string, string>(StringComparer.Ordinal);
        public bool CredentialsPresent;
        public bool CredentialsExplicitNull;

        public bool Has(string field)
        {
            return PresentFields.Contains(field);
        }

        public bool IsFullBackup
        {
            get { return Kind == ConfigSourceKind.Exchange
                && string.Equals(CredentialMode, "DpapiCurrentUser", StringComparison.Ordinal); }
        }
    }

    /// <summary>
    /// 外部 JSON 的无副作用读取：5 MiB 上限、严格 UTF-8、重复键/尾随 JSON 拒绝，
    /// 顶层字段按实际成员判断，不把外部文件交给 ConfigStore 的损坏自愈流程。
    /// </summary>
    public static class ConfigJsonReader
    {
        public const long MaxFileBytes = 5L * 1024L * 1024L;
        public const string ExchangeFormat = "InspectionWidget.Settings";
        public const int ExchangeVersion = 1;
        public const string CredentialModeExcluded = "Excluded";
        public const string CredentialModeDpapi = "DpapiCurrentUser";

        private sealed class JsonNull
        {
            public static readonly JsonNull Value = new JsonNull();
        }

        public static bool TryReadFile(string path, out ConfigImportDocument document, out string error)
        {
            document = null;
            error = null;
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    error = "请选择要导入的配置文件。";
                    return false;
                }
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    error = "配置文件不存在。";
                    return false;
                }
                if (info.Length > MaxFileBytes)
                {
                    error = "配置文件超过 5 MiB 上限，未读取。";
                    return false;
                }

                byte[] bytes = File.ReadAllBytes(path);
                if (bytes.Length > MaxFileBytes)
                {
                    error = "配置文件超过 5 MiB 上限，未读取。";
                    return false;
                }
                string text;
                try
                {
                    text = new UTF8Encoding(false, true).GetString(bytes);
                }
                catch (Exception)
                {
                    error = "配置文件不是有效的 UTF-8 文本。";
                    return false;
                }

                ConfigImportDocument parsed;
                if (!TryParse(text, out parsed, out error))
                    return false;
                parsed.SourcePath = Path.GetFullPath(path);
                document = parsed;
                return true;
            }
            catch (Exception ex)
            {
                error = "读取配置文件失败：" + ex.Message;
                return false;
            }
        }

        public static bool TryParse(string text, out ConfigImportDocument document, out string error)
        {
            document = null;
            error = null;
            try
            {
                object rootObject = new Parser(text).Parse();
                var root = rootObject as Dictionary<string, object>;
                if (root == null)
                {
                    error = "配置文件顶层必须是 JSON 对象。";
                    return false;
                }

                if (root.ContainsKey("Format"))
                    return TryReadExchange(root, out document, out error);
                return TryReadLegacy(root, out document, out error);
            }
            catch (JsonFormatException ex)
            {
                error = ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = "配置 JSON 解析失败：" + ex.Message;
                return false;
            }
        }

        private static bool TryReadExchange(Dictionary<string, object> root,
            out ConfigImportDocument document, out string error)
        {
            document = null;
            error = null;
            string[] allowedTop = { "Format", "Version", "CredentialMode", "Config" };
            string unknown;
            if ((unknown = FindUnknown(root, allowedTop)) != null)
            {
                error = "新格式包含未知顶层字段：" + unknown;
                return false;
            }

            string format = AsString(root, "Format");
            if (!string.Equals(format, ExchangeFormat, StringComparison.Ordinal))
            {
                error = "未知配置格式：" + (format ?? "（空）");
                return false;
            }

            int? version = AsInt(root, "Version");
            if (!version.HasValue || version.Value != ExchangeVersion)
            {
                error = "不支持的配置版本：" + (version.HasValue ? version.Value.ToString() : "非整数");
                return false;
            }

            string mode = AsString(root, "CredentialMode");
            if (mode != CredentialModeExcluded && mode != CredentialModeDpapi)
            {
                error = "未知凭据模式：" + (mode ?? "（空）");
                return false;
            }

            var configObject = GetObject(root, "Config");
            if (configObject == null)
            {
                error = "新格式缺少合法的 Config 对象。";
                return false;
            }

            string[] allowedConfig =
            {
                "Devices", "Credentials", "RootPath", "FolderNamePattern", "CaptureMode",
                "UiScalePercent", "AutoClearClipboard", "IsDeviceCollapsed",
                "IsCredentialCollapsed", "LastDevice", "WindowLeft", "WindowTop", "TrayHintShown"
            };
            if ((unknown = FindUnknown(configObject, allowedConfig)) != null)
            {
                error = "新格式 Config 包含未知字段：" + unknown;
                return false;
            }

            var doc = new ConfigImportDocument
            {
                Kind = ConfigSourceKind.Exchange,
                CredentialMode = mode,
                Config = AppConfig.CreateDefault()
            };
            doc.Config.Devices = new List<string>();
            doc.Config.Credentials = new List<CredentialEntry>();

            if (configObject.ContainsKey("Credentials"))
            {
                if (mode == CredentialModeExcluded)
                {
                    error = "CredentialMode=Excluded 时不得包含凭据。";
                    return false;
                }
                object credentialsValue = configObject["Credentials"];
                if (credentialsValue == null || credentialsValue == JsonNull.Value)
                {
                    error = "新格式的 Credentials 不能是 null。";
                    return false;
                }
                List<CredentialEntry> credentials;
                if (!TryReadCredentials(credentialsValue, true, out credentials, out error))
                    return false;
                doc.Config.Credentials = credentials;
                doc.CredentialsPresent = true;
                doc.PresentFields.Add("Credentials");
                doc.FieldSources["Credentials"] = "源文件";
            }
            else
            {
                if (mode == CredentialModeDpapi)
                {
                    error = "DpapiCurrentUser 完整备份必须包含凭据列表（空列表也要显式写出）。";
                    return false;
                }
                doc.CredentialsExplicitNull = false;
            }

            if (!TryReadConfigFields(configObject, doc, true, out error))
                return false;

            if (mode == CredentialModeExcluded)
            {
                string[] localFields = { "LastDevice", "WindowLeft", "WindowTop", "TrayHintShown" };
                for (int i = 0; i < localFields.Length; i++)
                {
                    if (doc.Has(localFields[i]))
                    {
                        error = "普通配置交换不得包含本机字段 " + localFields[i] + "。";
                        return false;
                    }
                }
            }

            document = doc;
            return true;
        }

        private static bool TryReadLegacy(Dictionary<string, object> root,
            out ConfigImportDocument document, out string error)
        {
            document = null;
            error = null;
            if (!root.ContainsKey("Devices"))
            {
                error = "旧格式缺少 Devices，不能当作配置导入。";
                return false;
            }
            List<string> devices;
            if (!TryReadStringArray(root["Devices"], "Devices", out devices, out error))
                return false;

            var doc = new ConfigImportDocument
            {
                Kind = ConfigSourceKind.Legacy,
                CredentialMode = CredentialModeExcluded,
                Config = AppConfig.CreateDefault()
            };
            doc.Config.Devices = devices;
            doc.Config.Credentials = new List<CredentialEntry>();

            if (!TryReadConfigFields(root, doc, false, out error))
                return false;

            // 旧格式缺失的普通字段沿用加载默认值并参与替换。
            string[] ordinary =
            {
                "Devices", "RootPath", "FolderNamePattern", "CaptureMode", "UiScalePercent",
                "AutoClearClipboard", "IsDeviceCollapsed", "IsCredentialCollapsed"
            };
            for (int i = 0; i < ordinary.Length; i++)
            {
                if (!doc.PresentFields.Contains(ordinary[i]))
                {
                    doc.PresentFields.Add(ordinary[i]);
                    doc.FieldSources[ordinary[i]] = "旧格式默认值";
                }
            }

            document = doc;
            return true;
        }

        private static bool TryReadConfigFields(Dictionary<string, object> source,
            ConfigImportDocument doc, bool exchange, out string error)
        {
            error = null;
            AppConfig config = doc.Config;

            if (source.ContainsKey("Devices"))
            {
                List<string> devices;
                if (!TryReadStringArray(source["Devices"], "Devices", out devices, out error))
                    return false;
                string deviceError = ConfigValidator.ValidateDevices(devices);
                if (deviceError != null)
                {
                    error = "设备名单无效：" + deviceError;
                    return false;
                }
                config.Devices = devices;
                doc.PresentFields.Add("Devices");
                doc.FieldSources["Devices"] = "源文件";
            }

            if (source.ContainsKey("Credentials") && !doc.PresentFields.Contains("Credentials"))
            {
                object credentialsValue = source["Credentials"];
                if (credentialsValue == null || credentialsValue == JsonNull.Value)
                {
                    // 旧裸配置的 null 表示不参与凭据导入；新格式已在上层拒绝。
                    doc.CredentialsExplicitNull = true;
                }
                else
                {
                    List<CredentialEntry> credentials;
                    if (!TryReadCredentials(credentialsValue, exchange,
                        out credentials, out error))
                        return false;
                    config.Credentials = credentials;
                    doc.CredentialsPresent = true;
                    doc.PresentFields.Add("Credentials");
                    doc.FieldSources["Credentials"] = "源文件";
                }
            }

            object value;
            if (source.TryGetValue("RootPath", out value))
            {
                if (value == null || value == JsonNull.Value)
                {
                    config.RootPath = null;
                }
                else
                {
                    string path = value as string;
                    if (path == null)
                    {
                        error = "RootPath 必须是字符串或 null。";
                        return false;
                    }
                    string pathError = ConfigValidator.ValidateRootPath(path);
                    if (pathError != null)
                    {
                        error = pathError;
                        return false;
                    }
                    config.RootPath = path;
                }
                doc.PresentFields.Add("RootPath");
                doc.FieldSources["RootPath"] = "源文件";
            }

            if (source.TryGetValue("FolderNamePattern", out value))
            {
                if (value == null || value == JsonNull.Value)
                {
                    if (exchange)
                    {
                        error = "新格式的 FolderNamePattern 不能是 null。";
                        return false;
                    }
                    config.FolderNamePattern = FolderNamePattern.Default;
                }
                else
                {
                    string pattern = value as string;
                    if (pattern == null)
                    {
                        error = "FolderNamePattern 必须是字符串。";
                        return false;
                    }
                    if (!exchange && string.IsNullOrEmpty(pattern))
                    {
                        // 旧版 ConfigStore 对空字符串执行加载归一；导入沿用同一口径。
                        config.FolderNamePattern = FolderNamePattern.Default;
                        doc.FieldSources["FolderNamePattern"] = "旧格式归一默认值";
                    }
                    else
                    {
                        string patternError = ConfigValidator.ValidateFolderPattern(pattern);
                        if (patternError != null)
                        {
                            error = "日期文件夹名无效：" + patternError;
                            return false;
                        }
                        config.FolderNamePattern = pattern;
                        doc.FieldSources["FolderNamePattern"] = "源文件";
                    }
                }
                doc.PresentFields.Add("FolderNamePattern");
            }

            if (source.TryGetValue("CaptureMode", out value))
            {
                int? mode = AsIntValue(value);
                string modeError = mode.HasValue ? ConfigValidator.ValidateCaptureMode(mode.Value) : "CaptureMode 必须是整数。";
                if (modeError != null)
                {
                    error = modeError;
                    return false;
                }
                config.CaptureMode = (CaptureMode)mode.Value;
                doc.PresentFields.Add("CaptureMode");
                doc.FieldSources["CaptureMode"] = "源文件";
            }

            if (source.TryGetValue("UiScalePercent", out value))
            {
                int? scale = AsIntValue(value);
                if (!scale.HasValue)
                {
                    error = "UiScalePercent 必须是整数。";
                    return false;
                }
                if (!UiScale.IsValid(scale.Value))
                {
                    if (exchange)
                    {
                        error = ConfigValidator.ValidateUiScale(scale.Value);
                        return false;
                    }
                    scale = UiScale.DefaultPercent;
                    doc.FieldSources["UiScalePercent"] = "旧格式归一默认值";
                }
                else
                {
                    doc.FieldSources["UiScalePercent"] = "源文件";
                }
                config.UiScalePercent = scale.Value;
                doc.PresentFields.Add("UiScalePercent");
            }

            if (source.TryGetValue("AutoClearClipboard", out value))
            {
                bool? flag = AsBool(value);
                if (!flag.HasValue)
                {
                    error = "AutoClearClipboard 必须是布尔值。";
                    return false;
                }
                config.AutoClearClipboard = flag.Value;
                doc.PresentFields.Add("AutoClearClipboard");
                doc.FieldSources["AutoClearClipboard"] = "源文件";
            }

            if (source.TryGetValue("IsDeviceCollapsed", out value))
            {
                bool? flag = AsBool(value);
                if (!flag.HasValue)
                {
                    error = "IsDeviceCollapsed 必须是布尔值。";
                    return false;
                }
                config.IsDeviceCollapsed = flag.Value;
                doc.PresentFields.Add("IsDeviceCollapsed");
                doc.FieldSources["IsDeviceCollapsed"] = "源文件";
            }

            if (source.TryGetValue("IsCredentialCollapsed", out value))
            {
                bool? flag = AsBool(value);
                if (!flag.HasValue)
                {
                    error = "IsCredentialCollapsed 必须是布尔值。";
                    return false;
                }
                config.IsCredentialCollapsed = flag.Value;
                doc.PresentFields.Add("IsCredentialCollapsed");
                doc.FieldSources["IsCredentialCollapsed"] = "源文件";
            }

            if (source.TryGetValue("LastDevice", out value))
            {
                if (value == null || value == JsonNull.Value)
                    config.LastDevice = null;
                else
                {
                    string name = value as string;
                    if (name == null)
                    {
                        error = "LastDevice 必须是字符串或 null。";
                        return false;
                    }
                    config.LastDevice = name;
                }
                doc.PresentFields.Add("LastDevice");
                doc.FieldSources["LastDevice"] = "源文件";
            }

            if (source.TryGetValue("WindowLeft", out value))
            {
                if (value == null || value == JsonNull.Value)
                    config.WindowLeft = null;
                else
                {
                    int? left = AsIntValue(value);
                    if (!left.HasValue)
                    {
                        error = "WindowLeft 必须是整数或 null。";
                        return false;
                    }
                    config.WindowLeft = left;
                }
                doc.PresentFields.Add("WindowLeft");
                doc.FieldSources["WindowLeft"] = "源文件";
            }

            if (source.TryGetValue("WindowTop", out value))
            {
                if (value == null || value == JsonNull.Value)
                    config.WindowTop = null;
                else
                {
                    int? top = AsIntValue(value);
                    if (!top.HasValue)
                    {
                        error = "WindowTop 必须是整数或 null。";
                        return false;
                    }
                    config.WindowTop = top;
                }
                doc.PresentFields.Add("WindowTop");
                doc.FieldSources["WindowTop"] = "源文件";
            }

            if (source.TryGetValue("TrayHintShown", out value))
            {
                bool? flag = AsBool(value);
                if (!flag.HasValue)
                {
                    error = "TrayHintShown 必须是布尔值。";
                    return false;
                }
                config.TrayHintShown = flag.Value;
                doc.PresentFields.Add("TrayHintShown");
                doc.FieldSources["TrayHintShown"] = "源文件";
            }

            return true;
        }

        private static bool TryReadStringArray(object value, string fieldName,
            out List<string> result, out string error)
        {
            result = null;
            error = null;
            var array = value as List<object>;
            if (array == null)
            {
                error = fieldName + " 必须是字符串数组。";
                return false;
            }
            result = new List<string>();
            for (int i = 0; i < array.Count; i++)
            {
                string text = array[i] as string;
                if (text == null)
                {
                    error = fieldName + " 第 " + (i + 1) + " 项不是字符串。";
                    return false;
                }
                result.Add(text);
            }
            return true;
        }

        private static bool TryReadCredentials(object value, bool strictSecret,
            out List<CredentialEntry> result, out string error)
        {
            result = null;
            error = null;
            var array = value as List<object>;
            if (array == null)
            {
                error = "Credentials 必须是数组。";
                return false;
            }
            if (array.Count > CredentialRowCollector.MaxCredentials)
            {
                error = "凭据最多 " + CredentialRowCollector.MaxCredentials + " 条。";
                return false;
            }

            var list = new List<CredentialEntry>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < array.Count; i++)
            {
                var entryObject = array[i] as Dictionary<string, object>;
                if (entryObject == null)
                {
                    error = "凭据列表第 " + (i + 1) + " 项必须是对象。";
                    return false;
                }
                string unknown = FindUnknown(entryObject, new[] { "Title", "EncryptedSecret" });
                if (unknown != null)
                {
                    error = "凭据列表第 " + (i + 1) + " 项包含未知字段：" + unknown;
                    return false;
                }
                string title = AsString(entryObject, "Title");
                if (string.IsNullOrWhiteSpace(title))
                {
                    error = "凭据列表第 " + (i + 1) + " 项名称为空。";
                    return false;
                }
                if (!names.Add(title))
                {
                    error = "凭据名称「" + title + "」重复。";
                    return false;
                }
                object secretValue;
                bool hasSecret = entryObject.TryGetValue("EncryptedSecret", out secretValue)
                    && secretValue != null && secretValue != JsonNull.Value;
                string secret = hasSecret ? secretValue as string : null;
                if (strictSecret && !hasSecret)
                {
                    error = "凭据「" + title + "」缺少合法密文字段。";
                    return false;
                }
                if (hasSecret && secret == null)
                {
                    error = "凭据「" + title + "」的密文必须是字符串。";
                    return false;
                }
                list.Add(new CredentialEntry { Title = title, EncryptedSecret = secret });
            }
            result = list;
            return true;
        }

        private static string FindUnknown(Dictionary<string, object> objectValue, string[] allowed)
        {
            var allowedSet = new HashSet<string>(allowed, StringComparer.Ordinal);
            foreach (string key in objectValue.Keys)
            {
                if (!allowedSet.Contains(key))
                    return key;
            }
            return null;
        }

        private static Dictionary<string, object> GetObject(
            Dictionary<string, object> source, string key)
        {
            object value;
            if (!source.TryGetValue(key, out value))
                return null;
            return value as Dictionary<string, object>;
        }

        private static string AsString(Dictionary<string, object> source, string key)
        {
            object value;
            if (!source.TryGetValue(key, out value))
                return null;
            return value as string;
        }

        private static int? AsInt(Dictionary<string, object> source, string key)
        {
            object value;
            return source.TryGetValue(key, out value) ? AsIntValue(value) : null;
        }

        private static int? AsIntValue(object value)
        {
            if (value is long)
            {
                long number = (long)value;
                if (number < int.MinValue || number > int.MaxValue)
                    return null;
                return (int)number;
            }
            return null;
        }

        private static bool? AsBool(object value)
        {
            return value is bool ? (bool?)value : null;
        }

        private sealed class JsonFormatException : Exception
        {
            public JsonFormatException(string message) : base(message) { }
        }

        /// <summary>最小 JSON 解析器；对象用 Dictionary，数组用 List，整数用 long。</summary>
        private sealed class Parser
        {
            private readonly string _text;
            private int _position;
            private int _depth;

            public Parser(string text)
            {
                if (text == null) throw new JsonFormatException("配置内容为空。");
                _text = text.TrimStart('\uFEFF');
            }

            public object Parse()
            {
                SkipWhitespace();
                if (_position >= _text.Length)
                    throw new JsonFormatException("配置 JSON 为空。");
                object value = ParseValue();
                SkipWhitespace();
                if (_position != _text.Length)
                    throw new JsonFormatException("JSON 末尾存在多余内容。");
                return value;
            }

            private object ParseValue()
            {
                SkipWhitespace();
                if (_position >= _text.Length)
                    throw new JsonFormatException("JSON 意外结束。");
                char c = _text[_position];
                if (c == '{') return ParseObject();
                if (c == '[') return ParseArray();
                if (c == '"') return ParseString();
                if (c == 't') { Expect("true"); return true; }
                if (c == 'f') { Expect("false"); return false; }
                if (c == 'n') { Expect("null"); return JsonNull.Value; }
                if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
                throw new JsonFormatException("JSON 值格式无效（位置 " + _position + "）。");
            }

            private Dictionary<string, object> ParseObject()
            {
                Enter();
                _position++;
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                SkipWhitespace();
                if (Consume('}'))
                {
                    _depth--;
                    return result;
                }
                while (true)
                {
                    SkipWhitespace();
                    if (_position >= _text.Length || _text[_position] != '"')
                        throw new JsonFormatException("JSON 对象键必须是字符串（位置 " + _position + "）。");
                    string key = ParseString();
                    if (result.ContainsKey(key))
                        throw new JsonFormatException("JSON 对象存在重复键：" + key);
                    SkipWhitespace();
                    if (!Consume(':'))
                        throw new JsonFormatException("JSON 对象缺少冒号（位置 " + _position + "）。");
                    result[key] = ParseValue();
                    SkipWhitespace();
                    if (Consume('}'))
                        break;
                    if (!Consume(','))
                        throw new JsonFormatException("JSON 对象缺少逗号（位置 " + _position + "）。");
                }
                _depth--;
                return result;
            }

            private List<object> ParseArray()
            {
                Enter();
                _position++;
                var result = new List<object>();
                SkipWhitespace();
                if (Consume(']'))
                {
                    _depth--;
                    return result;
                }
                while (true)
                {
                    result.Add(ParseValue());
                    SkipWhitespace();
                    if (Consume(']'))
                        break;
                    if (!Consume(','))
                        throw new JsonFormatException("JSON 数组缺少逗号（位置 " + _position + "）。");
                }
                _depth--;
                return result;
            }

            private string ParseString()
            {
                _position++;
                var text = new StringBuilder();
                while (_position < _text.Length)
                {
                    char c = _text[_position++];
                    if (c == '"')
                        return text.ToString();
                    if (c == '\\')
                    {
                        if (_position >= _text.Length)
                            throw new JsonFormatException("JSON 字符串转义不完整。");
                        char escaped = _text[_position++];
                        switch (escaped)
                        {
                            case '"': text.Append('"'); break;
                            case '\\': text.Append('\\'); break;
                            case '/': text.Append('/'); break;
                            case 'b': text.Append('\b'); break;
                            case 'f': text.Append('\f'); break;
                            case 'n': text.Append('\n'); break;
                            case 'r': text.Append('\r'); break;
                            case 't': text.Append('\t'); break;
                            case 'u':
                                if (_position + 4 > _text.Length)
                                    throw new JsonFormatException("JSON Unicode 转义不完整。");
                                int code;
                                if (!int.TryParse(_text.Substring(_position, 4),
                                    NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                                {
                                    throw new JsonFormatException("JSON Unicode 转义无效。");
                                }
                                text.Append((char)code);
                                _position += 4;
                                break;
                            default:
                                throw new JsonFormatException("JSON 字符串包含无效转义：" + escaped);
                        }
                        continue;
                    }
                    if (c < 0x20)
                        throw new JsonFormatException("JSON 字符串包含未转义控制字符。");
                    text.Append(c);
                }
                throw new JsonFormatException("JSON 字符串未闭合。");
            }

            private long ParseNumber()
            {
                int start = _position;
                if (_text[_position] == '-') _position++;
                if (_position >= _text.Length)
                    throw new JsonFormatException("JSON 数字不完整。");
                if (_text[_position] == '0')
                {
                    _position++;
                }
                else
                {
                    if (_text[_position] < '1' || _text[_position] > '9')
                        throw new JsonFormatException("JSON 数字格式无效。");
                    while (_position < _text.Length && char.IsDigit(_text[_position]))
                        _position++;
                }
                if (_position < _text.Length && _text[_position] == '.')
                {
                    // 配置字段只接受整数；小数按格式错误拒绝，而不是静默截断。
                    throw new JsonFormatException("配置中的数值必须是整数。");
                }
                if (_position < _text.Length
                    && (_text[_position] == 'e' || _text[_position] == 'E'))
                {
                    throw new JsonFormatException("配置中的数值必须是整数。");
                }
                long number;
                if (!long.TryParse(_text.Substring(start, _position - start),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                {
                    throw new JsonFormatException("JSON 整数超出范围。");
                }
                return number;
            }

            private void Expect(string literal)
            {
                if (_position + literal.Length > _text.Length
                    || string.CompareOrdinal(_text, _position, literal, 0, literal.Length) != 0)
                {
                    throw new JsonFormatException("JSON 字面量无效（位置 " + _position + "）。");
                }
                _position += literal.Length;
            }

            private void Enter()
            {
                _depth++;
                if (_depth > 64)
                    throw new JsonFormatException("JSON 嵌套层级过深。");
            }

            private void SkipWhitespace()
            {
                while (_position < _text.Length)
                {
                    char c = _text[_position];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                        _position++;
                    else
                        break;
                }
            }

            private bool Consume(char c)
            {
                if (_position < _text.Length && _text[_position] == c)
                {
                    _position++;
                    return true;
                }
                return false;
            }
        }
    }
}
