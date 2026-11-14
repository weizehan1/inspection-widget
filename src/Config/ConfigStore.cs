using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace InspectionWidget.Config
{
    /// <summary>配置加载结果分类（损坏自愈口径）。</summary>
    public enum ConfigLoadKind
    {
        Missing,
        Ok,
        Defaulted,
        CorruptBackedUp,
        IoErrorKept
    }

    public sealed class LoadResult
    {
        public AppConfig Config;
        public ConfigLoadKind Kind;
        public string BackupPath;
        public string Error;
        /// <summary>本次读取所用字节的 SHA-256；文件不存在时为空字符串。</summary>
        public string Revision;
    }

    public sealed class SaveResult
    {
        public bool Saved;
        public string Error;
        /// <summary>写盘后的配置代次。</summary>
        public string Revision;
        /// <summary>期望代次已变化，草稿不得覆盖新配置。</summary>
        public bool Stale;
        /// <summary>锁竞争等可重试故障。</summary>
        public bool Retryable;
        public string PreImportBackupPath;
        public bool FirstCreation;
    }

    public sealed class ConfigSaveOptions
    {
        /// <summary>
        /// 草稿读取/上次提交时的代次。设置协调器要求非 null 的有效代次；
        /// 读取失败后的不可提交标识会被明确拒绝，null 仅为历史兼容 Save 调用保留。
        /// </summary>
        public string ExpectedRevision;
        /// <summary>本次是导入草稿提交，成功前需要备份现有正式配置。</summary>
        public bool CreatePreImportBackup;
    }

    /// <summary>
    /// 配置存取：DataContractJsonSerializer 序列化、损坏自愈分类、
    /// 同目录唯一临时文件 + 原子替换、锁文件（FileShare.None）跨会话互斥。
    /// </summary>
    public sealed class ConfigStore
    {
        /// <summary>读取失败草稿的不可提交基线：与 null（旧调用不校验）明确区分。</summary>
        public const string UnusableRevision = "unusable:load-failed";

        internal static bool IsUnusableRevision(string revision)
        {
            return string.Equals(revision, UnusableRevision, StringComparison.Ordinal);
        }

        private static readonly string[] OptionalFieldNames =
        {
            "RootPath", "Devices", "Credentials", "IsCredentialCollapsed", "IsDeviceCollapsed",
            "AutoClearClipboard", "CaptureMode", "WindowLeft", "WindowTop", "LastDevice",
            "UiScalePercent", "FolderNamePattern", "TrayHintShown",
            "QuickLinks", "PlanModeEnabled", "HideVisitedDevices"
        };

        private readonly ConfigPaths _paths;
        private readonly Func<string, bool> _dirWritableProbe;
        private readonly AtomicConfigWriter _writer;
        private readonly IConfigBackupOperations _backups;
        private readonly ConfigWriteProtection _writeProtection;
        private readonly Action _beforeProtectedCommit;
        private string _currentRevision = string.Empty;

        /// <summary>
        /// 备份写入的测试接缝。生产实现与测试注入都只接收同一份已锁定
        /// 读取的字节，避免备份时重新读取一个可能已经变化的文件。
        /// </summary>
        internal interface IConfigBackupOperations
        {
            string WriteCorruptBackup(string target, byte[] bytes);
            string WritePreImportBackup(string target, byte[] bytes);
        }

        private sealed class DefaultBackupOperations : IConfigBackupOperations
        {
            public string WriteCorruptBackup(string target, byte[] bytes)
            {
                return WriteCorruptBackupFile(target, bytes);
            }

            public string WritePreImportBackup(string target, byte[] bytes)
            {
                return WritePreImportBackupFile(target, bytes);
            }
        }

        public ConfigStore()
            : this(DefaultPaths(), null)
        {
        }

        public ConfigStore(ConfigPaths paths, Func<string, bool> dirWritableProbe)
            : this(paths, dirWritableProbe, new AtomicConfigWriter(),
                new DefaultBackupOperations())
        {
        }

        internal ConfigStore(ConfigPaths paths, Func<string, bool> dirWritableProbe,
            AtomicConfigWriter writer)
            : this(paths, dirWritableProbe, writer, new DefaultBackupOperations())
        {
        }

        internal ConfigStore(ConfigPaths paths, Func<string, bool> dirWritableProbe,
            AtomicConfigWriter writer, IConfigBackupOperations backups)
            : this(paths, dirWritableProbe, writer, backups, null)
        {
        }

        internal ConfigStore(ConfigPaths paths, Func<string, bool> dirWritableProbe,
            AtomicConfigWriter writer, IConfigBackupOperations backups,
            ConfigWriteProtection writeProtection)
            : this(paths, dirWritableProbe, writer, backups, writeProtection, null)
        {
        }

        internal ConfigStore(ConfigPaths paths, Func<string, bool> dirWritableProbe,
            AtomicConfigWriter writer, IConfigBackupOperations backups,
            ConfigWriteProtection writeProtection, Action beforeProtectedCommit)
        {
            if (paths == null) throw new ArgumentNullException("paths");
            _paths = paths;
            _dirWritableProbe = dirWritableProbe != null ? dirWritableProbe : new Func<string, bool>(ProbeDirWritable);
            _writer = writer ?? new AtomicConfigWriter();
            _backups = backups ?? new DefaultBackupOperations();
            _writeProtection = writeProtection ?? new ConfigWriteProtection();
            _beforeProtectedCommit = beforeProtectedCommit;
        }

        public string AuthoritativePath
        {
            get { return _paths.AuthoritativePath; }
        }

        public bool IsFallback
        {
            get { return _paths.IsFallback; }
        }

        public ConfigWriteProtection WriteProtection
        {
            get { return _writeProtection; }
        }

        public string CurrentRevision
        {
            get { return _currentRevision; }
        }

        /// <summary>
        /// 重读配置后是否必须换掉剪贴板守卫：开关不变时保留原守卫和未到期计时；
        /// 读取失败（IoErrorKept）不替换任何运行态服务。
        /// </summary>
        public static bool ShouldRebuildClipboardGuard(LoadResult load, AppConfig current)
        {
            if (load == null || load.Kind == ConfigLoadKind.IoErrorKept)
                return false;
            return ShouldRebuildClipboardGuard(current, load.Config);
        }

        public static bool ShouldRebuildClipboardGuard(AppConfig current, AppConfig next)
        {
            AppConfig candidate = next ?? AppConfig.CreateDefault();
            return current == null
                || current.AutoClearClipboard != candidate.AutoClearClipboard;
        }

        private static ConfigPaths DefaultPaths()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return ConfigPathResolver.Resolve(exeDir, localAppData,
                new Func<string, bool>(File.Exists),
                new Func<string, bool>(ProbeDirWritable));
        }

        internal static bool ProbeDirWritable(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir)) return false;
                Directory.CreateDirectory(dir);
                string probe = Path.Combine(dir, ".iw-probe-" + Guid.NewGuid().ToString("N") + ".tmp");
                using (File.Create(probe)) { }
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public LoadResult Load()
        {
            string target = AuthoritativePath;
            string targetDir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(targetDir))
            {
                try
                {
                    Directory.CreateDirectory(targetDir);
                }
                catch (Exception ex)
                {
                    return ProtectIo("无法准备配置目录，读取未完成且原文件未动：" + ex.Message);
                }
            }

            FileStream lockStream;
            try
            {
                lockStream = new FileStream(target + ".lock", FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None);
            }
            catch (Exception ex)
            {
                return ProtectIo("配置文件正被占用或无法锁定，读取未完成且原文件未动：" + ex.Message);
            }

            using (lockStream)
            {
                // 文件不存在也先取得与 Save 相同的锁，避免把写入者的瞬时中间态
                // 误判为 Missing，随后用默认配置覆盖刚出现的正式文件。
                if (!File.Exists(target))
                {
                    _currentRevision = string.Empty;
                    _writeProtection.Clear();
                    return new LoadResult
                    {
                        Config = AppConfig.CreateDefault(),
                        Kind = ConfigLoadKind.Missing,
                        Revision = string.Empty
                    };
                }

                byte[] bytes;
                try
                {
                    bytes = File.ReadAllBytes(target);
                }
                catch (Exception ex)
                {
                    return ProtectIo("读取配置失败（原文件保留未动）：" + ex.Message);
                }

                string revision = RevisionOf(bytes);
                AppConfig config;
                string parseError;
                if (!TryDeserialize(bytes, out config, out parseError))
                    return Corrupt(target, bytes, revision, parseError);

                string structuralError = ValidateLoadedStructure(config);
                if (structuralError != null)
                    return Corrupt(target, bytes, revision, structuralError);

                bool defaulted = Normalize(config);
                string text = Encoding.UTF8.GetString(RemoveUtf8Bom(bytes));
                for (int i = 0; i < OptionalFieldNames.Length; i++)
                {
                    if (text.IndexOf("\"" + OptionalFieldNames[i] + "\"", StringComparison.Ordinal) < 0)
                    {
                        defaulted = true;
                        break;
                    }
                }

                _currentRevision = revision;
                _writeProtection.Clear();
                return new LoadResult
                {
                    Config = config,
                    Kind = defaulted ? ConfigLoadKind.Defaulted : ConfigLoadKind.Ok,
                    Revision = revision
                };
            }
        }

        public SaveResult Save(AppConfig config)
        {
            return Save(config, null);
        }

        public SaveResult Save(AppConfig config, ConfigSaveOptions options)
        {
            if (config == null)
                return Fail("配置为空，设置未保存。");
            long protectionGeneration = _writeProtection.Generation;
            if (_writeProtection.IsProtected)
                return Fail("配置处于读取失败保护状态，设置未保存：" + _writeProtection.Reason);

            string target = AuthoritativePath;
            string dir = Path.GetDirectoryName(target);
            if (string.IsNullOrEmpty(dir))
                return Fail("配置路径无效，设置未保存。");

            if (!_dirWritableProbe(dir))
                return Fail("配置目录不可写：" + dir + "，设置未保存。");

            try
            {
                Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                return Fail("无法创建配置目录：" + ex.Message);
            }

            FileStream lockStream;
            try
            {
                lockStream = new FileStream(target + ".lock", FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException ex)
            {
                return Retry("配置文件正被占用，设置未保存：" + ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Fail("无权锁定配置文件，设置未保存：" + ex.Message);
            }
            catch (Exception ex)
            {
                return Fail("锁定配置文件异常，设置未保存：" + ex.Message);
            }

            using (lockStream)
            {
                byte[] currentBytes = null;
                bool exists = File.Exists(target);
                if (exists)
                {
                    try
                    {
                        currentBytes = File.ReadAllBytes(target);
                    }
                    catch (Exception ex)
                    {
                        return Fail("无法核对当前配置，设置未保存：" + ex.Message);
                    }
                }

                string currentRevision = currentBytes == null ? string.Empty : RevisionOf(currentBytes);
                if (options != null && options.ExpectedRevision != null
                    && IsUnusableRevision(options.ExpectedRevision))
                {
                    return new SaveResult
                    {
                        Saved = false,
                        Stale = true,
                        Error = "配置基线已失效，请重新读取配置后再保存。"
                    };
                }
                if (options != null && options.ExpectedRevision != null
                    && !string.Equals(options.ExpectedRevision, currentRevision, StringComparison.Ordinal))
                {
                    return new SaveResult
                    {
                        Saved = false,
                        Stale = true,
                        Error = "配置已被其他程序修改，请重新读取并确认后再保存。"
                    };
                }

                byte[] candidateBytes;
                try
                {
                    candidateBytes = Serialize(config);
                }
                catch (Exception ex)
                {
                    return Fail("写入配置异常，设置未保存：" + ex.Message);
                }

                string backupPath = null;
                bool firstCreation = !exists;
                if (options != null && options.CreatePreImportBackup && exists)
                {
                    try
                    {
                        backupPath = _backups.WritePreImportBackup(target, currentBytes);
                    }
                    catch (Exception ex)
                    {
                        return Fail("导入恢复备份失败，设置未保存：" + ex.Message);
                    }
                }

                if (_beforeProtectedCommit != null)
                    _beforeProtectedCommit();
                AtomicWriteResult written = null;
                bool attempted;
                _writeProtection.TryWriteIfUnchanged(
                    protectionGeneration, delegate
                    {
                        written = _writer.WriteAllBytes(target, candidateBytes);
                        return written != null && written.Written;
                    }, out attempted);
                if (!attempted)
                    return Fail("配置在提交期间进入读取失败保护状态，设置未保存。");
                if (written == null || !written.Written)
                    return Fail("写入配置失败，设置未保存："
                        + (written == null ? "未知错误" : written.Error));

                _currentRevision = RevisionOf(candidateBytes);
                return new SaveResult
                {
                    Saved = true,
                    Revision = _currentRevision,
                    PreImportBackupPath = backupPath,
                    FirstCreation = firstCreation
                };
            }
        }

        private static bool TryDeserialize(byte[] bytes, out AppConfig config, out string error)
        {
            config = null;
            error = null;
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(AppConfig));
                using (var stream = new MemoryStream(RemoveUtf8Bom(bytes)))
                {
                    config = (AppConfig)serializer.ReadObject(stream);
                }
                if (config == null)
                {
                    error = "配置内容为空。";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string ValidateLoadedStructure(AppConfig config)
        {
            if (config.Devices != null)
            {
                for (int i = 0; i < config.Devices.Count; i++)
                {
                    if (config.Devices[i] == null)
                        return "设备名单第 " + (i + 1) + " 项为空。";
                }
            }
            if (config.Credentials != null)
            {
                for (int i = 0; i < config.Credentials.Count; i++)
                {
                    CredentialEntry entry = config.Credentials[i];
                    if (entry == null)
                        return "凭据列表第 " + (i + 1) + " 项为空。";
                    if (entry.Title == null)
                        return "凭据列表第 " + (i + 1) + " 项名称为空。";
                }
            }
            if (config.QuickLinks != null)
            {
                for (int i = 0; i < config.QuickLinks.Count; i++)
                {
                    if (config.QuickLinks[i] == null)
                        return "快捷链接列表第 " + (i + 1) + " 项为空。";
                }
            }
            return null;
        }

        private static bool Normalize(AppConfig config)
        {
            bool defaulted = false;
            if (!UiScale.IsValid(config.UiScalePercent))
            {
                config.UiScalePercent = UiScale.DefaultPercent;
                defaulted = true;
            }
            if (string.IsNullOrEmpty(config.FolderNamePattern))
            {
                config.FolderNamePattern = FolderNamePattern.Default;
                defaulted = true;
            }
            if (config.Devices == null)
            {
                config.Devices = new List<string>();
                defaulted = true;
            }
            if (config.Credentials == null)
            {
                config.Credentials = new List<CredentialEntry>();
                defaulted = true;
            }
            // 快捷链接允许显式 null（可空列表），归一为空表但不额外标 Defaulted，
            // 避免把老版本程序写出的 null 列表当成需要重写的缺字段配置。
            if (config.QuickLinks == null)
                config.QuickLinks = new List<QuickLinkEntry>();
            foreach (CredentialEntry entry in config.Credentials)
                entry.NeedsReset = string.IsNullOrEmpty(entry.EncryptedSecret);
            return defaulted;
        }

        private LoadResult Corrupt(string target, byte[] originalBytes, string revision, string error)
        {
            string backup;
            try
            {
                backup = _backups.WriteCorruptBackup(target, originalBytes);
            }
            catch (Exception backupEx)
            {
                _writeProtection.Enter("配置损坏且备份失败，保存已暂停。");
                _currentRevision = UnusableRevision;
                return new LoadResult
                {
                    Config = AppConfig.CreateDefault(),
                    Kind = ConfigLoadKind.IoErrorKept,
                    Revision = UnusableRevision,
                    Error = "配置损坏且备份失败，未做任何覆盖。备份错误：" + backupEx.Message
                        + "；原始错误：" + error
                };
            }

            _currentRevision = revision;
            _writeProtection.Clear();
            return new LoadResult
            {
                Config = AppConfig.CreateDefault(),
                Kind = ConfigLoadKind.CorruptBackedUp,
                BackupPath = backup,
                Revision = revision,
                Error = error
            };
        }

        private LoadResult ProtectIo(string error)
        {
            _writeProtection.Enter(error);
            // 失败读取没有任何可提交基线：不能把上一次成功读取的代次
            // 继续交给草稿，否则默认配置会以旧 Revision 通过校验。
            _currentRevision = UnusableRevision;
            return new LoadResult
            {
                Config = AppConfig.CreateDefault(),
                Kind = ConfigLoadKind.IoErrorKept,
                Error = error,
                Revision = UnusableRevision
            };
        }

        private static byte[] Serialize(AppConfig config)
        {
            var serializer = new DataContractJsonSerializer(typeof(AppConfig));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, config);
                return stream.ToArray();
            }
        }

        private static string RevisionOf(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes ?? new byte[0]);
                var text = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    text.Append(hash[i].ToString("x2"));
                return text.ToString();
            }
        }

        private static byte[] RemoveUtf8Bom(byte[] bytes)
        {
            if (bytes != null && bytes.Length >= 3
                && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                var copy = new byte[bytes.Length - 3];
                Buffer.BlockCopy(bytes, 3, copy, 0, copy.Length);
                return copy;
            }
            return bytes ?? new byte[0];
        }

        private static string WriteCorruptBackupFile(string target, byte[] bytes)
        {
            string dir = Path.GetDirectoryName(target);
            string stamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            string backup = Path.Combine(dir, "config.corrupt." + stamp + ".json");
            int seq = 1;
            while (File.Exists(backup))
            {
                backup = Path.Combine(dir, "config.corrupt." + stamp + "." + seq + ".json");
                seq++;
            }
            using (var stream = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            return backup;
        }

        private static string WritePreImportBackupFile(string target, byte[] bytes)
        {
            string dir = Path.GetDirectoryName(target);
            string stamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            string unique = Guid.NewGuid().ToString("N").Substring(0, 8);
            string backup = Path.Combine(dir, "config.preimport." + stamp + "." + unique + ".json");
            using (var stream = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            return backup;
        }

        private static SaveResult Fail(string error)
        {
            return new SaveResult { Saved = false, Error = error };
        }

        private static SaveResult Retry(string error)
        {
            return new SaveResult { Saved = false, Retryable = true, Error = error };
        }
    }
}
