using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
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
    }

    public sealed class SaveResult
    {
        public bool Saved;
        public string Error;
    }

    /// <summary>
    /// 配置存取：DataContractJsonSerializer 序列化、损坏自愈分类、
    /// 同目录唯一临时文件 + File.Copy 原子写、锁文件（FileShare.None）跨会话互斥。
    /// </summary>
    public sealed class ConfigStore
    {
        // 「可选字段」全集：文本扫描 JSON 是否含各字段名，缺任一即 Defaulted（简报既定口径）
        private static readonly string[] OptionalFieldNames =
        {
            "RootPath", "Devices", "Credentials", "IsCredentialCollapsed",
            "AutoClearClipboard", "CaptureMode", "WindowLeft", "WindowTop", "LastDevice"
        };

        private readonly ConfigPaths _paths;
        private readonly Func<string, bool> _dirWritableProbe;

        public ConfigStore()
            : this(DefaultPaths(), null)
        {
        }

        public ConfigStore(ConfigPaths paths, Func<string, bool> dirWritableProbe)
        {
            if (paths == null) throw new ArgumentNullException("paths");
            _paths = paths;
            _dirWritableProbe = dirWritableProbe != null ? dirWritableProbe : new Func<string, bool>(ProbeDirWritable);
        }

        public string AuthoritativePath
        {
            get { return _paths.AuthoritativePath; }
        }

        public bool IsFallback
        {
            get { return _paths.IsFallback; }
        }

        private static ConfigPaths DefaultPaths()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return ConfigPathResolver.Resolve(exeDir, localAppData,
                new Func<string, bool>(File.Exists),
                new Func<string, bool>(ProbeDirWritable));
        }

        /// <summary>真实可写探针：在目录内建唯一临时文件再删除。</summary>
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

            if (!File.Exists(target))
            {
                return new LoadResult { Config = AppConfig.CreateDefault(), Kind = ConfigLoadKind.Missing };
            }

            string text;
            try
            {
                text = File.ReadAllText(target, Encoding.UTF8);
            }
            catch (IOException ex)
            {
                return IoError(ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                return IoError(ex);
            }

            AppConfig config;
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(AppConfig));
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(text)))
                {
                    config = (AppConfig)serializer.ReadObject(stream);
                }
                if (config == null)
                    throw new SerializationException("配置内容为空。");
            }
            catch (Exception ex)
            {
                // 语法/结构损坏：先备份原件，备份成功才返回默认；备份失败按 I/O 失败处理，绝不覆盖原件
                string backupPath;
                try
                {
                    backupPath = BackupCorruptFile(target);
                }
                catch (Exception backupEx)
                {
                    return new LoadResult
                    {
                        Config = AppConfig.CreateDefault(),
                        Kind = ConfigLoadKind.IoErrorKept,
                        Error = "配置损坏且备份失败，未做任何覆盖。备份错误：" + backupEx.Message + "；原始错误：" + ex.Message
                    };
                }
                return new LoadResult
                {
                    Config = AppConfig.CreateDefault(),
                    Kind = ConfigLoadKind.CorruptBackedUp,
                    BackupPath = backupPath,
                    Error = ex.Message
                };
            }

            bool defaulted = false;
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

            for (int i = 0; i < OptionalFieldNames.Length; i++)
            {
                if (!text.Contains("\"" + OptionalFieldNames[i] + "\""))
                {
                    defaulted = true;
                    break;
                }
            }

            return new LoadResult
            {
                Config = config,
                Kind = defaulted ? ConfigLoadKind.Defaulted : ConfigLoadKind.Ok
            };
        }

        public SaveResult Save(AppConfig config)
        {
            if (config == null)
                return new SaveResult { Saved = false, Error = "配置为空，设置未保存。" };

            string target = AuthoritativePath;
            string dir = Path.GetDirectoryName(target);
            if (string.IsNullOrEmpty(dir))
                return new SaveResult { Saved = false, Error = "配置路径无效，设置未保存。" };

            if (!_dirWritableProbe(dir))
                return new SaveResult { Saved = false, Error = "配置目录不可写：" + dir + "，设置未保存。" };

            try
            {
                Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                return new SaveResult { Saved = false, Error = "无法创建配置目录：" + ex.Message };
            }

            // 锁文件独占，跨会话互斥；拿不到锁不写入、正式文件不动
            string lockPath = target + ".lock";
            FileStream lockStream;
            try
            {
                lockStream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException ex)
            {
                return new SaveResult { Saved = false, Error = "配置文件正被占用，设置未保存：" + ex.Message };
            }
            catch (UnauthorizedAccessException ex)
            {
                return new SaveResult { Saved = false, Error = "无权锁定配置文件，设置未保存：" + ex.Message };
            }
            catch (Exception ex)
            {
                // 取锁段兜底（Task 8，Task 1 审查 Minor-3）：预期外异常同样
                // 不穿透 Save，一律落为「未保存」结果，正式文件不动。
                return new SaveResult { Saved = false, Error = "锁定配置文件异常，设置未保存：" + ex.Message };
            }

            using (lockStream)
            {
                string tmp = Path.Combine(dir, "config." + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    var serializer = new DataContractJsonSerializer(typeof(AppConfig));
                    using (var stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        serializer.WriteObject(stream, config);
                        stream.Flush();
                    }
                    File.Copy(tmp, target, true);
                    return new SaveResult { Saved = true };
                }
                catch (IOException ex)
                {
                    return new SaveResult { Saved = false, Error = "写入配置失败，设置未保存：" + ex.Message };
                }
                catch (UnauthorizedAccessException ex)
                {
                    return new SaveResult { Saved = false, Error = "写入配置被拒绝，设置未保存：" + ex.Message };
                }
                catch (Exception ex)
                {
                    // 写段兜底（Task 8，Task 1 审查 Minor-3）：序列化等预期外
                    // 异常（如 SerializationException）不许穿透 Save，一律落为
                    // 「未保存」结果；临时文件仍由 finally 清理，正式文件不动。
                    return new SaveResult { Saved = false, Error = "写入配置异常，设置未保存：" + ex.Message };
                }
                finally
                {
                    try
                    {
                        if (File.Exists(tmp))
                            File.Delete(tmp);
                    }
                    catch
                    {
                        // 临时文件清理失败不影响结论
                    }
                }
            }
        }

        private static LoadResult IoError(Exception ex)
        {
            return new LoadResult
            {
                Config = AppConfig.CreateDefault(),
                Kind = ConfigLoadKind.IoErrorKept,
                Error = "读取配置失败（原文件保留未动）：" + ex.Message
            };
        }

        /// <summary>把损坏原件复制为同目录 config.corrupt.时间戳.json，重名加序号。</summary>
        private static string BackupCorruptFile(string target)
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
            File.Copy(target, backup, false);
            return backup;
        }
    }
}
