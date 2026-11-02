# Review package Task 1: 1ba9e28..995c376
995c376 feat(stage-1): 配置层（模型/校验/自愈/原子写/互斥）

 build.sh                            |   4 +-
 src/Config/AppConfig.cs             |  91 ++++++++++++
 src/Config/ConfigPathResolver.cs    |  39 +++++
 src/Config/ConfigStore.cs           | 288 ++++++++++++++++++++++++++++++++++++
 src/Config/DeviceNameValidator.cs   | 101 +++++++++++++
 src/InspectionWidget.csproj         |   3 +
 tests/AppConfigTests.cs             |  39 +++++
 tests/ConfigPathResolverTests.cs    |  74 +++++++++
 tests/ConfigStoreLockTests.cs       |  55 +++++++
 tests/ConfigStoreTests.cs           | 213 ++++++++++++++++++++++++++
 tests/DeviceNameValidatorTests.cs   | 128 ++++++++++++++++
 tests/InspectionWidget.Tests.csproj |  56 +++++++
 tests/Program.cs                    |  22 +++
 tests/TestFramework.cs              |  98 ++++++++++++
 work/task-1-report.md               |  42 ++++++
 15 files changed, 1252 insertions(+), 1 deletion(-)

diff --git a/build.sh b/build.sh
index 1369784..d7c42fc 100755
--- a/build.sh
+++ b/build.sh
@@ -52,11 +52,13 @@ if [ -n "$TEST_PROJECT" ]; then
     else
         xbuild /p:Configuration=Release /p:Platform=AnyCPU /p:LangVersion=7.2 "$TEST_PROJECT"
     fi
 
     # 运行测试可执行文件（若构建产生了 exe）
-    for test_exe in tests/bin/Release/*.exe; do
+    # 只跑 *.Tests.exe：tests 输出目录里还有 ProjectReference 依赖拷贝的主程序
+    # InspectionWidget.exe（GUI 程序，无显示环境必崩），不能当测试运行（Task 1 接线时修正）。
+    for test_exe in tests/bin/Release/*.Tests.exe; do
         if [ -f "$test_exe" ]; then
             echo "运行测试：$test_exe"
             mono "$test_exe"
         fi
     done
diff --git a/src/Config/AppConfig.cs b/src/Config/AppConfig.cs
new file mode 100644
index 0000000..41a7c6d
--- /dev/null
+++ b/src/Config/AppConfig.cs
@@ -0,0 +1,91 @@
+using System.Collections.Generic;
+using System.Runtime.Serialization;
+
+namespace InspectionWidget.Config
+{
+    /// <summary>截图模式：自动 / 强制 PrintWindow / 强制 BitBlt。</summary>
+    public enum CaptureMode
+    {
+        Auto,
+        PrintWindowOnly,
+        BitBltOnly
+    }
+
+    /// <summary>一条凭据：名称 + DPAPI 密文（Base64）。明文永不落盘。</summary>
+    [DataContract]
+    public sealed class CredentialEntry
+    {
+        [DataMember]
+        public string Title { get; set; }
+
+        [DataMember]
+        public string EncryptedSecret { get; set; }
+
+        /// <summary>运行时标记：密文解不开时置 true，仅界面提示用，不落盘。</summary>
+        [IgnoreDataMember]
+        public bool NeedsReset { get; set; }
+    }
+
+    /// <summary>应用配置（config.json 的 DataContract 模型）。</summary>
+    [DataContract]
+    public sealed class AppConfig
+    {
+        [DataMember]
+        public string RootPath { get; set; }
+
+        [DataMember]
+        public List<string> Devices { get; set; }
+
+        [DataMember]
+        public List<CredentialEntry> Credentials { get; set; }
+
+        /// <summary>凭据区分区是否折叠，默认 false。</summary>
+        [DataMember]
+        public bool IsCredentialCollapsed { get; set; }
+
+        /// <summary>复制凭据后 30 秒自动清空剪贴板，默认 true（缺字段时靠 OnDeserializing 兜底）。</summary>
+        [DataMember]
+        public bool AutoClearClipboard { get; set; }
+
+        [DataMember]
+        public CaptureMode CaptureMode { get; set; }
+
+        [DataMember]
+        public int? WindowLeft { get; set; }
+
+        [DataMember]
+        public int? WindowTop { get; set; }
+
+        [DataMember]
+        public string LastDevice { get; set; }
+
+        // DataContract 反序列化不走构造函数：先把字段设成默认值，JSON 里有的字段再覆盖。
+        // 这样缺 AutoClearClipboard 字段的老/手改配置加载后仍是默认 true，列表也不会是 null。
+        [OnDeserializing]
+        private void OnDeserializing(StreamingContext context)
+        {
+            AutoClearClipboard = true;
+            IsCredentialCollapsed = false;
+            CaptureMode = CaptureMode.Auto;
+            Devices = new List<string>();
+            Credentials = new List<CredentialEntry>();
+        }
+
+        /// <summary>默认配置：两条示例设备，其余按约定默认值。</summary>
+        public static AppConfig CreateDefault()
+        {
+            return new AppConfig
+            {
+                RootPath = null,
+                Devices = new List<string> { "设备1", "设备2" },
+                Credentials = new List<CredentialEntry>(),
+                IsCredentialCollapsed = false,
+                AutoClearClipboard = true,
+                CaptureMode = CaptureMode.Auto,
+                WindowLeft = null,
+                WindowTop = null,
+                LastDevice = null
+            };
+        }
+    }
+}
diff --git a/src/Config/ConfigPathResolver.cs b/src/Config/ConfigPathResolver.cs
new file mode 100644
index 0000000..5410446
--- /dev/null
+++ b/src/Config/ConfigPathResolver.cs
@@ -0,0 +1,39 @@
+using System;
+using System.IO;
+
+namespace InspectionWidget.Config
+{
+    /// <summary>权威配置路径判定结果。</summary>
+    public sealed class ConfigPaths
+    {
+        public string AuthoritativePath;
+        public bool IsFallback;
+    }
+
+    /// <summary>
+    /// 权威路径判定（纯逻辑，探针注入便于测试）：
+    /// 回退文件已存在 → 回退优先；否则 exe 目录可写 → 主路径；否则回退路径。
+    /// 一次只认一个权威位置，杜绝两处配置分叉。
+    /// </summary>
+    public static class ConfigPathResolver
+    {
+        public const string ConfigFileName = "config.json";
+        public const string FallbackDirName = "InspectionWidget";
+
+        public static ConfigPaths Resolve(string exeDir, string localAppDataDir,
+            Func<string, bool> fileExists, Func<string, bool> dirWritable)
+        {
+            string fallbackDir = Path.Combine(localAppDataDir, FallbackDirName);
+            string fallbackPath = Path.Combine(fallbackDir, ConfigFileName);
+
+            if (fileExists != null && fileExists(fallbackPath))
+                return new ConfigPaths { AuthoritativePath = fallbackPath, IsFallback = true };
+
+            string mainPath = Path.Combine(exeDir, ConfigFileName);
+            if (dirWritable != null && dirWritable(exeDir))
+                return new ConfigPaths { AuthoritativePath = mainPath, IsFallback = false };
+
+            return new ConfigPaths { AuthoritativePath = fallbackPath, IsFallback = true };
+        }
+    }
+}
diff --git a/src/Config/ConfigStore.cs b/src/Config/ConfigStore.cs
new file mode 100644
index 0000000..6e3800c
--- /dev/null
+++ b/src/Config/ConfigStore.cs
@@ -0,0 +1,288 @@
+using System;
+using System.Collections.Generic;
+using System.IO;
+using System.Runtime.Serialization;
+using System.Runtime.Serialization.Json;
+using System.Text;
+
+namespace InspectionWidget.Config
+{
+    /// <summary>配置加载结果分类（损坏自愈口径）。</summary>
+    public enum ConfigLoadKind
+    {
+        Missing,
+        Ok,
+        Defaulted,
+        CorruptBackedUp,
+        IoErrorKept
+    }
+
+    public sealed class LoadResult
+    {
+        public AppConfig Config;
+        public ConfigLoadKind Kind;
+        public string BackupPath;
+        public string Error;
+    }
+
+    public sealed class SaveResult
+    {
+        public bool Saved;
+        public string Error;
+    }
+
+    /// <summary>
+    /// 配置存取：DataContractJsonSerializer 序列化、损坏自愈分类、
+    /// 同目录唯一临时文件 + File.Copy 原子写、锁文件（FileShare.None）跨会话互斥。
+    /// </summary>
+    public sealed class ConfigStore
+    {
+        // 「可选字段」全集：文本扫描 JSON 是否含各字段名，缺任一即 Defaulted（简报既定口径）
+        private static readonly string[] OptionalFieldNames =
+        {
+            "RootPath", "Devices", "Credentials", "IsCredentialCollapsed",
+            "AutoClearClipboard", "CaptureMode", "WindowLeft", "WindowTop", "LastDevice"
+        };
+
+        private readonly ConfigPaths _paths;
+        private readonly Func<string, bool> _dirWritableProbe;
+
+        public ConfigStore()
+            : this(DefaultPaths(), null)
+        {
+        }
+
+        public ConfigStore(ConfigPaths paths, Func<string, bool> dirWritableProbe)
+        {
+            if (paths == null) throw new ArgumentNullException("paths");
+            _paths = paths;
+            _dirWritableProbe = dirWritableProbe != null ? dirWritableProbe : new Func<string, bool>(ProbeDirWritable);
+        }
+
+        public string AuthoritativePath
+        {
+            get { return _paths.AuthoritativePath; }
+        }
+
+        public bool IsFallback
+        {
+            get { return _paths.IsFallback; }
+        }
+
+        private static ConfigPaths DefaultPaths()
+        {
+            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
+            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
+            return ConfigPathResolver.Resolve(exeDir, localAppData,
+                new Func<string, bool>(File.Exists),
+                new Func<string, bool>(ProbeDirWritable));
+        }
+
+        /// <summary>真实可写探针：在目录内建唯一临时文件再删除。</summary>
+        internal static bool ProbeDirWritable(string dir)
+        {
+            try
+            {
+                if (string.IsNullOrEmpty(dir)) return false;
+                Directory.CreateDirectory(dir);
+                string probe = Path.Combine(dir, ".iw-probe-" + Guid.NewGuid().ToString("N") + ".tmp");
+                using (File.Create(probe)) { }
+                File.Delete(probe);
+                return true;
+            }
+            catch
+            {
+                return false;
+            }
+        }
+
+        public LoadResult Load()
+        {
+            string target = AuthoritativePath;
+
+            if (!File.Exists(target))
+            {
+                return new LoadResult { Config = AppConfig.CreateDefault(), Kind = ConfigLoadKind.Missing };
+            }
+
+            string text;
+            try
+            {
+                text = File.ReadAllText(target, Encoding.UTF8);
+            }
+            catch (IOException ex)
+            {
+                return IoError(ex);
+            }
+            catch (UnauthorizedAccessException ex)
+            {
+                return IoError(ex);
+            }
+
+            AppConfig config;
+            try
+            {
+                var serializer = new DataContractJsonSerializer(typeof(AppConfig));
+                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(text)))
+                {
+                    config = (AppConfig)serializer.ReadObject(stream);
+                }
+                if (config == null)
+                    throw new SerializationException("配置内容为空。");
+            }
+            catch (Exception ex)
+            {
+                // 语法/结构损坏：先备份原件，备份成功才返回默认；备份失败按 I/O 失败处理，绝不覆盖原件
+                string backupPath;
+                try
+                {
+                    backupPath = BackupCorruptFile(target);
+                }
+                catch (Exception backupEx)
+                {
+                    return new LoadResult
+                    {
+                        Config = AppConfig.CreateDefault(),
+                        Kind = ConfigLoadKind.IoErrorKept,
+                        Error = "配置损坏且备份失败，未做任何覆盖。备份错误：" + backupEx.Message + "；原始错误：" + ex.Message
+                    };
+                }
+                return new LoadResult
+                {
+                    Config = AppConfig.CreateDefault(),
+                    Kind = ConfigLoadKind.CorruptBackedUp,
+                    BackupPath = backupPath,
+                    Error = ex.Message
+                };
+            }
+
+            bool defaulted = false;
+            if (config.Devices == null)
+            {
+                config.Devices = new List<string>();
+                defaulted = true;
+            }
+            if (config.Credentials == null)
+            {
+                config.Credentials = new List<CredentialEntry>();
+                defaulted = true;
+            }
+
+            for (int i = 0; i < OptionalFieldNames.Length; i++)
+            {
+                if (!text.Contains("\"" + OptionalFieldNames[i] + "\""))
+                {
+                    defaulted = true;
+                    break;
+                }
+            }
+
+            return new LoadResult
+            {
+                Config = config,
+                Kind = defaulted ? ConfigLoadKind.Defaulted : ConfigLoadKind.Ok
+            };
+        }
+
+        public SaveResult Save(AppConfig config)
+        {
+            if (config == null)
+                return new SaveResult { Saved = false, Error = "配置为空，设置未保存。" };
+
+            string target = AuthoritativePath;
+            string dir = Path.GetDirectoryName(target);
+            if (string.IsNullOrEmpty(dir))
+                return new SaveResult { Saved = false, Error = "配置路径无效，设置未保存。" };
+
+            if (!_dirWritableProbe(dir))
+                return new SaveResult { Saved = false, Error = "配置目录不可写：" + dir + "，设置未保存。" };
+
+            try
+            {
+                Directory.CreateDirectory(dir);
+            }
+            catch (Exception ex)
+            {
+                return new SaveResult { Saved = false, Error = "无法创建配置目录：" + ex.Message };
+            }
+
+            // 锁文件独占，跨会话互斥；拿不到锁不写入、正式文件不动
+            string lockPath = target + ".lock";
+            FileStream lockStream;
+            try
+            {
+                lockStream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
+            }
+            catch (IOException ex)
+            {
+                return new SaveResult { Saved = false, Error = "配置文件正被占用，设置未保存：" + ex.Message };
+            }
+            catch (UnauthorizedAccessException ex)
+            {
+                return new SaveResult { Saved = false, Error = "无权锁定配置文件，设置未保存：" + ex.Message };
+            }
+
+            using (lockStream)
+            {
+                string tmp = Path.Combine(dir, "config." + Guid.NewGuid().ToString("N") + ".tmp");
+                try
+                {
+                    var serializer = new DataContractJsonSerializer(typeof(AppConfig));
+                    using (var stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
+                    {
+                        serializer.WriteObject(stream, config);
+                        stream.Flush();
+                    }
+                    File.Copy(tmp, target, true);
+                    return new SaveResult { Saved = true };
+                }
+                catch (IOException ex)
+                {
+                    return new SaveResult { Saved = false, Error = "写入配置失败，设置未保存：" + ex.Message };
+                }
+                catch (UnauthorizedAccessException ex)
+                {
+                    return new SaveResult { Saved = false, Error = "写入配置被拒绝，设置未保存：" + ex.Message };
+                }
+                finally
+                {
+                    try
+                    {
+                        if (File.Exists(tmp))
+                            File.Delete(tmp);
+                    }
+                    catch
+                    {
+                        // 临时文件清理失败不影响结论
+                    }
+                }
+            }
+        }
+
+        private static LoadResult IoError(Exception ex)
+        {
+            return new LoadResult
+            {
+                Config = AppConfig.CreateDefault(),
+                Kind = ConfigLoadKind.IoErrorKept,
+                Error = "读取配置失败（原文件保留未动）：" + ex.Message
+            };
+        }
+
+        /// <summary>把损坏原件复制为同目录 config.corrupt.时间戳.json，重名加序号。</summary>
+        private static string BackupCorruptFile(string target)
+        {
+            string dir = Path.GetDirectoryName(target);
+            string stamp = DateTime.Now.ToString("yyyyMMddHHmmss");
+            string backup = Path.Combine(dir, "config.corrupt." + stamp + ".json");
+            int seq = 1;
+            while (File.Exists(backup))
+            {
+                backup = Path.Combine(dir, "config.corrupt." + stamp + "." + seq + ".json");
+                seq++;
+            }
+            File.Copy(target, backup, false);
+            return backup;
+        }
+    }
+}
diff --git a/src/Config/DeviceNameValidator.cs b/src/Config/DeviceNameValidator.cs
new file mode 100644
index 0000000..779be60
--- /dev/null
+++ b/src/Config/DeviceNameValidator.cs
@@ -0,0 +1,101 @@
+using System;
+using System.Collections.Generic;
+using System.IO;
+
+namespace InspectionWidget.Config
+{
+    /// <summary>
+    /// 设备名校验（纯逻辑）：设备名会直接用作文件名拼路径，必须拦下
+    /// 非法字符、控制字符、空名、`.`/`..`、尾部空格或点、Windows 设备保留名，
+    /// 并按大小写不敏感查重。返回 null 表示合法，否则返回中文错误原因。
+    /// </summary>
+    public static class DeviceNameValidator
+    {
+        private const string InvalidChars = "\\/:*?\"<>|";
+
+        private static readonly HashSet<string> ReservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
+        {
+            "CON", "PRN", "AUX", "NUL",
+            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
+            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
+        };
+
+        /// <summary>完整路径长度上限：须 &lt; 260，即最长 259。</summary>
+        public const int MaxFullPathLength = 259;
+
+        public static string Validate(string name, IReadOnlyList<string> existing)
+        {
+            if (string.IsNullOrWhiteSpace(name))
+                return "设备名不能为空。";
+
+            for (int i = 0; i < name.Length; i++)
+            {
+                char c = name[i];
+                if (char.IsControl(c))
+                    return "设备名不能包含控制字符。";
+                if (InvalidChars.IndexOf(c) >= 0)
+                    return "设备名不能包含 \\ / : * ? \" < > | 等字符。";
+            }
+
+            if (name == "." || name == "..")
+                return "设备名不能是 . 或 ..。";
+
+            if (name.EndsWith(" ", StringComparison.Ordinal) || name.EndsWith(".", StringComparison.Ordinal))
+                return "设备名末尾不能是空格或点。";
+
+            // 保留名按第一个点前的主名比对：CON、con.txt、nul.png 都算保留名
+            string mainName = name.Split('.')[0];
+            if (ReservedNames.Contains(mainName))
+                return "设备名不能使用 Windows 保留名（如 CON、PRN、NUL、COM1、LPT1）。";
+
+            if (existing != null)
+            {
+                for (int i = 0; i < existing.Count; i++)
+                {
+                    if (string.Equals(existing[i], name, StringComparison.OrdinalIgnoreCase))
+                        return "已有同名设备（设备名不区分大小写）。";
+                }
+            }
+
+            return null;
+        }
+
+        /// <summary>整表校验：逐个合法性 + 列表内部（含大小写不敏感）重名，返回第一个错误。</summary>
+        public static string ValidateAll(IReadOnlyList<string> names)
+        {
+            if (names == null)
+                return "设备列表为空。";
+
+            for (int i = 0; i < names.Count; i++)
+            {
+                var others = new List<string>();
+                for (int j = 0; j < names.Count; j++)
+                {
+                    if (j != i)
+                        others.Add(names[j]);
+                }
+                string error = Validate(names[i], others);
+                if (error != null)
+                    return error;
+            }
+            return null;
+        }
+
+        /// <summary>
+        /// 完整路径长度校验：rootPath 与 name 直接 Path.Combine 后的总长度超过 259 返回错误。
+        /// 调用方（Task 5/7）应把日期子目录等也算进 rootPath 再调，或以最终文件名（含顺延后缀与扩展名）作 name 传入。
+        /// </summary>
+        public static string ValidateFullPathLength(string rootPath, string name)
+        {
+            if (string.IsNullOrEmpty(rootPath))
+                return "根目录为空，无法校验完整路径。";
+            if (string.IsNullOrEmpty(name))
+                return "设备名不能为空。";
+
+            string fullPath = Path.Combine(rootPath, name);
+            if (fullPath.Length > MaxFullPathLength)
+                return "完整路径过长（" + fullPath.Length + " 字符，上限 " + MaxFullPathLength + "），请缩短根目录或设备名。";
+            return null;
+        }
+    }
+}
diff --git a/src/InspectionWidget.csproj b/src/InspectionWidget.csproj
index 05ff454..98223d4 100644
--- a/src/InspectionWidget.csproj
+++ b/src/InspectionWidget.csproj
@@ -44,10 +44,13 @@
     <Reference Include="System.Core" />
     <Reference Include="System.Drawing" />
     <Reference Include="System.Windows.Forms" />
     <Reference Include="System.Security" />
     <Reference Include="System.Runtime.Serialization" />
+    <!-- DataContractJsonSerializer 的基类 XmlObjectSerializer 公开签名含 XmlReader/XmlWriter，
+         无此引用编译报 CS0012；System.Xml 为 .NET Framework 系统自带程序集，非第三方包。 -->
+    <Reference Include="System.Xml" />
   </ItemGroup>
   <ItemGroup>
     <Compile Include="**\*.cs" />
   </ItemGroup>
   <ItemGroup>
diff --git a/tests/AppConfigTests.cs b/tests/AppConfigTests.cs
new file mode 100644
index 0000000..0df928a
--- /dev/null
+++ b/tests/AppConfigTests.cs
@@ -0,0 +1,39 @@
+using InspectionWidget.Config;
+
+namespace InspectionWidget.Tests
+{
+    /// <summary>AppConfig 模型默认值。</summary>
+    public static class AppConfigTests
+    {
+        public static void RunAll()
+        {
+            TestRunner.Run("CreateDefault：示例设备两条（设备1、设备2）", () =>
+            {
+                AppConfig cfg = AppConfig.CreateDefault();
+                Assert.NotNull(cfg.Devices);
+                Assert.Equal(2, cfg.Devices.Count);
+                Assert.Equal("设备1", cfg.Devices[0]);
+                Assert.Equal("设备2", cfg.Devices[1]);
+            });
+            TestRunner.Run("CreateDefault：AutoClearClipboard 默认 true", () =>
+                Assert.True(AppConfig.CreateDefault().AutoClearClipboard));
+            TestRunner.Run("CreateDefault：IsCredentialCollapsed 默认 false", () =>
+                Assert.False(AppConfig.CreateDefault().IsCredentialCollapsed));
+            TestRunner.Run("CreateDefault：CaptureMode 默认 Auto", () =>
+                Assert.Equal(CaptureMode.Auto, AppConfig.CreateDefault().CaptureMode));
+            TestRunner.Run("CreateDefault：Credentials 为空表（非 null）", () =>
+            {
+                AppConfig cfg = AppConfig.CreateDefault();
+                Assert.NotNull(cfg.Credentials);
+                Assert.Equal(0, cfg.Credentials.Count);
+            });
+            TestRunner.Run("CreateDefault：WindowLeft/WindowTop 为 null、LastDevice 为 null", () =>
+            {
+                AppConfig cfg = AppConfig.CreateDefault();
+                Assert.Null(cfg.WindowLeft);
+                Assert.Null(cfg.WindowTop);
+                Assert.Null(cfg.LastDevice);
+            });
+        }
+    }
+}
diff --git a/tests/ConfigPathResolverTests.cs b/tests/ConfigPathResolverTests.cs
new file mode 100644
index 0000000..6cbf806
--- /dev/null
+++ b/tests/ConfigPathResolverTests.cs
@@ -0,0 +1,74 @@
+using System.IO;
+using InspectionWidget.Config;
+
+namespace InspectionWidget.Tests
+{
+    /// <summary>ConfigPathResolver 三种情形（假探针）+ 两处不可写时 Save 失败不落盘。</summary>
+    public static class ConfigPathResolverTests
+    {
+        public static void RunAll()
+        {
+            TestRunner.Run("路径：exe 目录可写且无回退文件 → 主路径", () =>
+            {
+                ConfigPaths p = ConfigPathResolver.Resolve("C:\\App", "C:\\Users\\u\\AppData\\Local",
+                    delegate(string f) { return false; },
+                    delegate(string d) { return d == "C:\\App"; });
+                Assert.False(p.IsFallback);
+                Assert.Equal(Path.Combine("C:\\App", "config.json"), p.AuthoritativePath);
+            });
+
+            TestRunner.Run("路径：exe 目录不可写 → 回退路径", () =>
+            {
+                ConfigPaths p = ConfigPathResolver.Resolve("C:\\App", "C:\\Users\\u\\AppData\\Local",
+                    delegate(string f) { return false; },
+                    delegate(string d) { return false; });
+                Assert.True(p.IsFallback);
+                string expected = Path.Combine(Path.Combine("C:\\Users\\u\\AppData\\Local", "InspectionWidget"), "config.json");
+                Assert.Equal(expected, p.AuthoritativePath);
+            });
+
+            TestRunner.Run("路径：回退文件已存在 → 即使 exe 可写也回退优先", () =>
+            {
+                string fallback = Path.Combine(Path.Combine("C:\\Users\\u\\AppData\\Local", "InspectionWidget"), "config.json");
+                ConfigPaths p = ConfigPathResolver.Resolve("C:\\App", "C:\\Users\\u\\AppData\\Local",
+                    delegate(string f) { return f == fallback; },
+                    delegate(string d) { return true; });
+                Assert.True(p.IsFallback);
+                Assert.Equal(fallback, p.AuthoritativePath);
+            });
+
+            TestRunner.Run("路径：两处都不可写且无回退文件 → 仍给回退路径", () =>
+            {
+                ConfigPaths p = ConfigPathResolver.Resolve("C:\\App", "C:\\Local",
+                    delegate(string f) { return false; },
+                    delegate(string d) { return false; });
+                Assert.True(p.IsFallback);
+                Assert.Equal(Path.Combine(Path.Combine("C:\\Local", "InspectionWidget"), "config.json"), p.AuthoritativePath);
+            });
+
+            TestRunner.Run("路径：目录不可写时 Save 返回失败且不产生配置文件", () =>
+            {
+                string exeDir = ConfigStoreTests.MakeTempDir();
+                string localDir = ConfigStoreTests.MakeTempDir();
+                try
+                {
+                    ConfigPaths p = ConfigPathResolver.Resolve(exeDir, localDir,
+                        delegate(string f) { return false; },
+                        delegate(string d) { return false; });
+                    Assert.True(p.IsFallback);
+                    var store = new ConfigStore(p, delegate(string d) { return false; });
+                    SaveResult r = store.Save(AppConfig.CreateDefault());
+                    Assert.False(r.Saved);
+                    Assert.NotNull(r.Error);
+                    Assert.True(r.Error.Length > 0);
+                    Assert.False(File.Exists(p.AuthoritativePath), "写不了时不得产生正式文件");
+                }
+                finally
+                {
+                    ConfigStoreTests.Cleanup(exeDir);
+                    ConfigStoreTests.Cleanup(localDir);
+                }
+            });
+        }
+    }
+}
diff --git a/tests/ConfigStoreLockTests.cs b/tests/ConfigStoreLockTests.cs
new file mode 100644
index 0000000..efb9a96
--- /dev/null
+++ b/tests/ConfigStoreLockTests.cs
@@ -0,0 +1,55 @@
+using System.IO;
+using InspectionWidget.Config;
+
+namespace InspectionWidget.Tests
+{
+    /// <summary>ConfigStore 锁文件互斥：持锁时 Save 失败且正式文件不变；释锁后恢复可写。</summary>
+    public static class ConfigStoreLockTests
+    {
+        public static void RunAll()
+        {
+            TestRunner.Run("锁：锁文件被独占时 Save 返回失败且正式文件内容不变", () =>
+            {
+                string dir = ConfigStoreTests.MakeTempDir();
+                try
+                {
+                    ConfigStore store = ConfigStoreTests.MakeStore(dir);
+                    SaveResult first = store.Save(AppConfig.CreateDefault());
+                    Assert.True(first.Saved, first.Error);
+                    string before = File.ReadAllText(ConfigStoreTests.ConfigFile(dir));
+
+                    string lockPath = ConfigStoreTests.ConfigFile(dir) + ".lock";
+                    using (var held = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
+                    {
+                        AppConfig changed = AppConfig.CreateDefault();
+                        changed.LastDevice = "不应写入";
+                        SaveResult r = store.Save(changed);
+                        Assert.False(r.Saved, "持锁时 Save 必须失败");
+                        Assert.NotNull(r.Error);
+                        Assert.True(r.Error.Length > 0);
+                        Assert.Equal(before, File.ReadAllText(ConfigStoreTests.ConfigFile(dir)));
+                    }
+                }
+                finally { ConfigStoreTests.Cleanup(dir); }
+            });
+
+            TestRunner.Run("锁：锁释放后 Save 恢复成功", () =>
+            {
+                string dir = ConfigStoreTests.MakeTempDir();
+                try
+                {
+                    ConfigStore store = ConfigStoreTests.MakeStore(dir);
+                    string lockPath = ConfigStoreTests.ConfigFile(dir) + ".lock";
+                    using (var held = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
+                    {
+                        Assert.False(store.Save(AppConfig.CreateDefault()).Saved);
+                    }
+                    SaveResult r = store.Save(AppConfig.CreateDefault());
+                    Assert.True(r.Saved, r.Error);
+                    Assert.True(File.Exists(ConfigStoreTests.ConfigFile(dir)));
+                }
+                finally { ConfigStoreTests.Cleanup(dir); }
+            });
+        }
+    }
+}
diff --git a/tests/ConfigStoreTests.cs b/tests/ConfigStoreTests.cs
new file mode 100644
index 0000000..c01ee19
--- /dev/null
+++ b/tests/ConfigStoreTests.cs
@@ -0,0 +1,213 @@
+using System;
+using System.Collections.Generic;
+using System.IO;
+using InspectionWidget.Config;
+
+namespace InspectionWidget.Tests
+{
+    /// <summary>ConfigStore 序列化 / 自愈 / 原子写（临时目录，测后清理）。锁与路径判定在各自用例组。</summary>
+    public static class ConfigStoreTests
+    {
+        public static void RunAll()
+        {
+            TestRunner.Run("存储：缺文件 → Missing + 默认配置", () =>
+            {
+                string dir = MakeTempDir();
+                try
+                {
+                    ConfigStore store = MakeStore(dir);
+                    LoadResult r = store.Load();
+                    Assert.Equal(ConfigLoadKind.Missing, r.Kind);
+                    Assert.NotNull(r.Config);
+                    Assert.Equal(2, r.Config.Devices.Count);
+                    Assert.Equal("设备1", r.Config.Devices[0]);
+                    Assert.Equal("设备2", r.Config.Devices[1]);
+                    Assert.True(r.Config.AutoClearClipboard);
+                    Assert.NotNull(r.Config.Credentials);
+                }
+                finally { Cleanup(dir); }
+            });
+
+            TestRunner.Run("存储：Save→Load 往返全字段不丢且 Kind=Ok", () =>
+            {
+                string dir = MakeTempDir();
+                try
+                {
+                    ConfigStore store = MakeStore(dir);
+                    var cfg = new AppConfig
+                    {
+                        RootPath = "D:\\巡检截图",
+                        Devices = new List<string> { "机房A", "机房B", "机房C" },
+                        Credentials = new List<CredentialEntry>
+                        {
+                            new CredentialEntry { Title = "堡垒机", EncryptedSecret = "QUJDZA==" },
+                            new CredentialEntry { Title = "交换机", EncryptedSecret = "RUZHSA==" }
+                        },
+                        IsCredentialCollapsed = true,
+                        AutoClearClipboard = false,
+                        CaptureMode = CaptureMode.BitBltOnly,
+                        WindowLeft = 123,
+                        WindowTop = 45,
+                        LastDevice = "机房B"
+                    };
+                    SaveResult saved = store.Save(cfg);
+                    Assert.True(saved.Saved, saved.Error);
+
+                    LoadResult r = MakeStore(dir).Load();
+                    Assert.Equal(ConfigLoadKind.Ok, r.Kind);
+                    Assert.Equal("D:\\巡检截图", r.Config.RootPath);
+                    Assert.Equal(3, r.Config.Devices.Count);
+                    Assert.Equal("机房C", r.Config.Devices[2]);
+                    Assert.Equal(2, r.Config.Credentials.Count);
+                    Assert.Equal("堡垒机", r.Config.Credentials[0].Title);
+                    Assert.Equal("QUJDZA==", r.Config.Credentials[0].EncryptedSecret);
+                    Assert.Equal("交换机", r.Config.Credentials[1].Title);
+                    Assert.True(r.Config.IsCredentialCollapsed);
+                    Assert.False(r.Config.AutoClearClipboard, "显式保存的 false 不能被默认 true 覆盖");
+                    Assert.Equal(CaptureMode.BitBltOnly, r.Config.CaptureMode);
+                    Assert.Equal((int?)123, r.Config.WindowLeft);
+                    Assert.Equal((int?)45, r.Config.WindowTop);
+                    Assert.Equal("机房B", r.Config.LastDevice);
+                }
+                finally { Cleanup(dir); }
+            });
+
+            TestRunner.Run("存储：手写 JSON 缺 AutoClearClipboard 与 WindowLeft → Defaulted 且默认值补上、已写字段保留", () =>
+            {
+                string dir = MakeTempDir();
+                try
+                {
+                    File.WriteAllText(ConfigFile(dir),
+                        "{\"RootPath\":\"D:/shots\",\"Devices\":[\"设备甲\",\"设备乙\"],\"LastDevice\":\"设备甲\"}");
+                    LoadResult r = MakeStore(dir).Load();
+                    Assert.Equal(ConfigLoadKind.Defaulted, r.Kind);
+                    Assert.True(r.Config.AutoClearClipboard, "缺字段时须补默认 true");
+                    Assert.Null(r.Config.WindowLeft);
+                    Assert.Equal("D:/shots", r.Config.RootPath);
+                    Assert.Equal(2, r.Config.Devices.Count);
+                    Assert.Equal("设备乙", r.Config.Devices[1]);
+                    Assert.Equal("设备甲", r.Config.LastDevice);
+                    Assert.NotNull(r.Config.Credentials);
+                }
+                finally { Cleanup(dir); }
+            });
+
+            TestRunner.Run("存储：JSON 中 Devices/Credentials 为 null → 补空表不崩溃", () =>
+            {
+                string dir = MakeTempDir();
+                try
+                {
+                    File.WriteAllText(ConfigFile(dir),
+                        "{\"RootPath\":\"D:/x\",\"Devices\":null,\"Credentials\":null,"
+                        + "\"IsCredentialCollapsed\":false,\"AutoClearClipboard\":true,\"CaptureMode\":0,"
+                        + "\"WindowLeft\":null,\"WindowTop\":null,\"LastDevice\":null}");
+                    LoadResult r = MakeStore(dir).Load();
+                    Assert.NotNull(r.Config.Devices);
+                    Assert.Equal(0, r.Config.Devices.Count);
+                    Assert.NotNull(r.Config.Credentials);
+                    Assert.Equal(0, r.Config.Credentials.Count);
+                }
+                finally { Cleanup(dir); }
+            });
+
+            TestRunner.Run("存储：乱码文件 → CorruptBackedUp、备份内容=原乱码、返回默认", () =>
+            {
+                string dir = MakeTempDir();
+                try
+                {
+                    string garbage = "这不是 JSON {{{ \u0001\u0002 ###";
+                    File.WriteAllText(ConfigFile(dir), garbage);
+                    LoadResult r = MakeStore(dir).Load();
+                    Assert.Equal(ConfigLoadKind.CorruptBackedUp, r.Kind);
+                    Assert.NotNull(r.BackupPath);
+                    Assert.True(File.Exists(r.BackupPath), "备份文件须存在");
+                    Assert.Equal(garbage, File.ReadAllText(r.BackupPath));
+                    Assert.True(File.Exists(ConfigFile(dir)), "备份后原件保留不删");
+                    Assert.Equal(2, r.Config.Devices.Count);
+                    Assert.True(r.Config.AutoClearClipboard);
+                }
+                finally { Cleanup(dir); }
+            });
+
+            TestRunner.Run("存储：字段类型错误（Devices 写成字符串）→ 同损坏路径", () =>
+            {
+                string dir = MakeTempDir();
+                try
+                {
+                    string bad = "{\"Devices\":\"不是列表\"}";
+                    File.WriteAllText(ConfigFile(dir), bad);
+                    LoadResult r = MakeStore(dir).Load();
+                    Assert.Equal(ConfigLoadKind.CorruptBackedUp, r.Kind);
+                    Assert.NotNull(r.BackupPath);
+                    Assert.Equal(bad, File.ReadAllText(r.BackupPath));
+                    Assert.Equal(2, r.Config.Devices.Count);
+                }
+                finally { Cleanup(dir); }
+            });
+
+            TestRunner.Run("存储：Save 后目录无 .tmp 残留", () =>
+            {
+                string dir = MakeTempDir();
+                try
+                {
+                    ConfigStore store = MakeStore(dir);
+                    SaveResult saved = store.Save(AppConfig.CreateDefault());
+                    Assert.True(saved.Saved, saved.Error);
+                    Assert.Equal(0, Directory.GetFiles(dir, "*.tmp").Length);
+                    Assert.True(File.Exists(ConfigFile(dir)));
+                }
+                finally { Cleanup(dir); }
+            });
+
+            TestRunner.Run("存储：凭据 NeedsReset 不落盘", () =>
+            {
+                string dir = MakeTempDir();
+                try
+                {
+                    ConfigStore store = MakeStore(dir);
+                    AppConfig cfg = AppConfig.CreateDefault();
+                    cfg.Credentials.Add(new CredentialEntry { Title = "待重设", EncryptedSecret = "QUJD", NeedsReset = true });
+                    SaveResult saved = store.Save(cfg);
+                    Assert.True(saved.Saved, saved.Error);
+                    string raw = File.ReadAllText(ConfigFile(dir));
+                    Assert.False(raw.Contains("NeedsReset"), "运行时标记不得出现在 config.json 中");
+                    LoadResult r = MakeStore(dir).Load();
+                    Assert.Equal(1, r.Config.Credentials.Count);
+                    Assert.False(r.Config.Credentials[0].NeedsReset);
+                }
+                finally { Cleanup(dir); }
+            });
+        }
+
+        internal static string MakeTempDir()
+        {
+            string dir = Path.Combine(Path.GetTempPath(), "iw-cfg-tests-" + Guid.NewGuid().ToString("N"));
+            Directory.CreateDirectory(dir);
+            return dir;
+        }
+
+        internal static string ConfigFile(string dir)
+        {
+            return Path.Combine(dir, "config.json");
+        }
+
+        internal static ConfigStore MakeStore(string dir)
+        {
+            var paths = new ConfigPaths { AuthoritativePath = ConfigFile(dir), IsFallback = false };
+            return new ConfigStore(paths, delegate(string d) { return true; });
+        }
+
+        internal static void Cleanup(string dir)
+        {
+            try
+            {
+                if (Directory.Exists(dir))
+                    Directory.Delete(dir, true);
+            }
+            catch
+            {
+                // 临时目录清理失败不影响用例结论
+            }
+        }
+    }
+}
diff --git a/tests/DeviceNameValidatorTests.cs b/tests/DeviceNameValidatorTests.cs
new file mode 100644
index 0000000..a2e592d
--- /dev/null
+++ b/tests/DeviceNameValidatorTests.cs
@@ -0,0 +1,128 @@
+using System.Collections.Generic;
+using InspectionWidget.Config;
+
+namespace InspectionWidget.Tests
+{
+    /// <summary>DeviceNameValidator 全部规则逐条一用例。</summary>
+    public static class DeviceNameValidatorTests
+    {
+        private static readonly List<string> None = new List<string>();
+
+        public static void RunAll()
+        {
+            TestRunner.Run("校验器：null 名非法", () =>
+                Assert.NotNull(DeviceNameValidator.Validate(null, None)));
+            TestRunner.Run("校验器：空名非法", () =>
+                Assert.NotNull(DeviceNameValidator.Validate(string.Empty, None)));
+            TestRunner.Run("校验器：纯空格名非法", () =>
+                Assert.NotNull(DeviceNameValidator.Validate("   ", None)));
+
+            // 非法字符逐个一用例：\ / : * ? " < > |
+            foreach (char c in new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' })
+            {
+                char bad = c;
+                TestRunner.Run("校验器：含非法字符 '" + bad + "' 非法", () =>
+                    Assert.NotNull(DeviceNameValidator.Validate("设备" + bad + "名", None)));
+            }
+
+            TestRunner.Run("校验器：含控制字符（Tab）非法", () =>
+                Assert.NotNull(DeviceNameValidator.Validate("设备\t名", None)));
+            TestRunner.Run("校验器：含控制字符（\\u0001）非法", () =>
+                Assert.NotNull(DeviceNameValidator.Validate("设备\u0001名", None)));
+
+            TestRunner.Run("校验器：'.' 非法", () =>
+                Assert.NotNull(DeviceNameValidator.Validate(".", None)));
+            TestRunner.Run("校验器：'..' 非法", () =>
+                Assert.NotNull(DeviceNameValidator.Validate("..", None)));
+            TestRunner.Run("校验器：尾部空格非法", () =>
+                Assert.NotNull(DeviceNameValidator.Validate("设备A ", None)));
+            TestRunner.Run("校验器：尾部点非法", () =>
+                Assert.NotNull(DeviceNameValidator.Validate("设备A.", None)));
+
+            TestRunner.Run("校验器：保留名 CON 非法", () =>
+                Assert.NotNull(DeviceNameValidator.Validate("CON", None)));
+            TestRunner.Run("校验器：保留名 prn（小写）非法", () =>
+                Assert.NotNull(DeviceNameValidator.Validate("prn", None)));
+            TestRunner.Run("校验器：保留名 AUX 非法", () =>
+                Assert.NotNull(DeviceNameValidator.Validate("AUX", None)));
+            TestRunner.Run("校验器：保留名 NUL 非法", () =>
+                Assert.NotNull(DeviceNameValidator.Validate("NUL", None)));
+            TestRunner.Run("校验器：保留名 COM1 非法", () =>
+                Assert.NotNull(DeviceNameValidator.Validate("COM1", None)));
+            TestRunner.Run("校验器：保留名 com9（小写）非法", () =>
+                Assert.NotNull(DeviceNameValidator.Validate("com9", None)));
+            TestRunner.Run("校验器：保留名 LPT1 非法", () =>
+                Assert.NotNull(DeviceNameValidator.Validate("LPT1", None)));
+            TestRunner.Run("校验器：保留名 LPT9 非法", () =>
+                Assert.NotNull(DeviceNameValidator.Validate("LPT9", None)));
+            TestRunner.Run("校验器：带扩展名 CON.txt 非法", () =>
+                Assert.NotNull(DeviceNameValidator.Validate("CON.txt", None)));
+            TestRunner.Run("校验器：带扩展名 nul.png 非法", () =>
+                Assert.NotNull(DeviceNameValidator.Validate("nul.png", None)));
+            TestRunner.Run("校验器：COM0 不是保留名（合法）", () =>
+                Assert.Null(DeviceNameValidator.Validate("COM0", None)));
+            TestRunner.Run("校验器：CONSOLE 不是保留名（合法）", () =>
+                Assert.Null(DeviceNameValidator.Validate("CONSOLE", None)));
+
+            TestRunner.Run("校验器：与现有名完全相同重名非法", () =>
+                Assert.NotNull(DeviceNameValidator.Validate("设备A", new List<string> { "设备A" })));
+            TestRunner.Run("校验器：大小写不敏感重名（设备A vs 设备a）非法", () =>
+                Assert.NotNull(DeviceNameValidator.Validate("设备a", new List<string> { "设备A" })));
+            TestRunner.Run("校验器：英文名大小写不敏感重名非法", () =>
+                Assert.NotNull(DeviceNameValidator.Validate("ServerA", new List<string> { "servera" })));
+            TestRunner.Run("校验器：与现有名不同合法", () =>
+                Assert.Null(DeviceNameValidator.Validate("设备B", new List<string> { "设备A" })));
+            TestRunner.Run("校验器：existing 为 null 时只查自身合法性", () =>
+                Assert.Null(DeviceNameValidator.Validate("设备A", null)));
+
+            TestRunner.Run("校验器：合法中文名通过", () =>
+                Assert.Null(DeviceNameValidator.Validate("一号机房", None)));
+            TestRunner.Run("校验器：合法名含中间空格与连字符通过", () =>
+                Assert.Null(DeviceNameValidator.Validate("机房 A-1", None)));
+            TestRunner.Run("校验器：合法名含中间点通过（如 v2.1机房）", () =>
+                Assert.Null(DeviceNameValidator.Validate("v2.1机房", None)));
+
+            TestRunner.Run("ValidateAll：全部合法返回 null", () =>
+                Assert.Null(DeviceNameValidator.ValidateAll(new List<string> { "设备A", "设备B" })));
+            TestRunner.Run("ValidateAll：内部重名（大小写不敏感）非法", () =>
+                Assert.NotNull(DeviceNameValidator.ValidateAll(new List<string> { "设备A", "设备a" })));
+            TestRunner.Run("ValidateAll：含非法名返回错误", () =>
+                Assert.NotNull(DeviceNameValidator.ValidateAll(new List<string> { "设备A", "坏/名" })));
+            TestRunner.Run("ValidateAll：含保留名返回错误", () =>
+                Assert.NotNull(DeviceNameValidator.ValidateAll(new List<string> { "设备A", "con.txt" })));
+            TestRunner.Run("ValidateAll：空列表返回 null", () =>
+                Assert.Null(DeviceNameValidator.ValidateAll(new List<string>())));
+
+            // 路径长度（Review Focus 第 2 条）：rootPath 与 name 拼成的完整路径 > 259 报错
+            TestRunner.Run("路径长度：恰好 259 字符合法", () =>
+            {
+                string root, name;
+                BuildPathOfTotalLength(259, out root, out name);
+                Assert.Equal(259, System.IO.Path.Combine(root, name).Length);
+                Assert.Null(DeviceNameValidator.ValidateFullPathLength(root, name));
+            });
+            TestRunner.Run("路径长度：260 字符非法", () =>
+            {
+                string root, name;
+                BuildPathOfTotalLength(260, out root, out name);
+                Assert.Equal(260, System.IO.Path.Combine(root, name).Length);
+                Assert.NotNull(DeviceNameValidator.ValidateFullPathLength(root, name));
+            });
+            TestRunner.Run("路径长度：深根目录 + 长名（模拟加 -99.png 后缀）非法", () =>
+            {
+                string root = new string('r', 250); // 250 + 1 分隔符 + "设备A-99.png" 10 字符 = 261 > 259
+                Assert.NotNull(DeviceNameValidator.ValidateFullPathLength(root, "设备A-99.png"));
+            });
+            TestRunner.Run("路径长度：普通根目录与短名合法", () =>
+                Assert.Null(DeviceNameValidator.ValidateFullPathLength("D:\\Screenshots", "设备A")));
+        }
+
+        /// <summary>拼出 Path.Combine(root, name) 恰好 total 字符的根与名（名取合法字符）。</summary>
+        private static void BuildPathOfTotalLength(int total, out string root, out string name)
+        {
+            name = "设备A"; // 3 字符
+            int rootLength = total - 1 /* 路径分隔符 */ - name.Length;
+            root = new string('r', rootLength);
+        }
+    }
+}
diff --git a/tests/InspectionWidget.Tests.csproj b/tests/InspectionWidget.Tests.csproj
new file mode 100644
index 0000000..6d3d827
--- /dev/null
+++ b/tests/InspectionWidget.Tests.csproj
@@ -0,0 +1,56 @@
+<?xml version="1.0" encoding="utf-8"?>
+<Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
+  <Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props" Condition="Exists('$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props')" />
+  <PropertyGroup>
+    <Configuration Condition=" '$(Configuration)' == '' ">Debug</Configuration>
+    <Platform Condition=" '$(Platform)' == '' ">AnyCPU</Platform>
+    <ProjectGuid>{8F2A5C41-9D3B-4E7A-A1C6-5B0D2E4F6A81}</ProjectGuid>
+    <OutputType>Exe</OutputType>
+    <RootNamespace>InspectionWidget.Tests</RootNamespace>
+    <AssemblyName>InspectionWidget.Tests</AssemblyName>
+    <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
+    <FileAlignment>512</FileAlignment>
+    <LangVersion>7.3</LangVersion>
+    <AutoGenerateBindingRedirects>false</AutoGenerateBindingRedirects>
+    <Deterministic>true</Deterministic>
+  </PropertyGroup>
+  <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' ">
+    <PlatformTarget>AnyCPU</PlatformTarget>
+    <DebugSymbols>true</DebugSymbols>
+    <DebugType>full</DebugType>
+    <Optimize>false</Optimize>
+    <OutputPath>bin\Debug\</OutputPath>
+    <DefineConstants>DEBUG;TRACE</DefineConstants>
+    <ErrorReport>prompt</ErrorReport>
+    <WarningLevel>4</WarningLevel>
+    <Prefer32Bit>false</Prefer32Bit>
+    <LangVersion>7.3</LangVersion>
+  </PropertyGroup>
+  <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Release|AnyCPU' ">
+    <PlatformTarget>AnyCPU</PlatformTarget>
+    <DebugType>pdbonly</DebugType>
+    <DebugSymbols>true</DebugSymbols>
+    <Optimize>true</Optimize>
+    <OutputPath>bin\Release\</OutputPath>
+    <DefineConstants>TRACE</DefineConstants>
+    <ErrorReport>prompt</ErrorReport>
+    <WarningLevel>4</WarningLevel>
+    <Prefer32Bit>false</Prefer32Bit>
+    <LangVersion>7.3</LangVersion>
+  </PropertyGroup>
+  <ItemGroup>
+    <Reference Include="System" />
+    <Reference Include="System.Core" />
+    <Reference Include="System.Runtime.Serialization" />
+  </ItemGroup>
+  <ItemGroup>
+    <Compile Include="**\*.cs" />
+  </ItemGroup>
+  <ItemGroup>
+    <ProjectReference Include="..\src\InspectionWidget.csproj">
+      <Project>{C3CF6B60-4871-4E2E-B57E-82EFD26A79F2}</Project>
+      <Name>InspectionWidget</Name>
+    </ProjectReference>
+  </ItemGroup>
+  <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
+</Project>
diff --git a/tests/Program.cs b/tests/Program.cs
new file mode 100644
index 0000000..5ecf18f
--- /dev/null
+++ b/tests/Program.cs
@@ -0,0 +1,22 @@
+using System;
+
+namespace InspectionWidget.Tests
+{
+    internal static class Program
+    {
+        private static int Main()
+        {
+            DeviceNameValidatorTests.RunAll();
+            AppConfigTests.RunAll();
+            ConfigStoreTests.RunAll();
+            ConfigPathResolverTests.RunAll();
+            ConfigStoreLockTests.RunAll();
+
+            Console.WriteLine("----------------------------------------");
+            Console.WriteLine("用例总数：" + TestRunner.TotalCount
+                + "，通过：" + TestRunner.PassedCount
+                + "，失败：" + TestRunner.FailedCount);
+            return TestRunner.FailedCount > 0 ? 1 : 0;
+        }
+    }
+}
diff --git a/tests/TestFramework.cs b/tests/TestFramework.cs
new file mode 100644
index 0000000..535e798
--- /dev/null
+++ b/tests/TestFramework.cs
@@ -0,0 +1,98 @@
+using System;
+
+namespace InspectionWidget.Tests
+{
+    /// <summary>断言失败异常（由 TestRunner 捕获并记为用例失败）。</summary>
+    public sealed class AssertException : Exception
+    {
+        public AssertException(string message) : base(message) { }
+    }
+
+    /// <summary>微型断言：失败时抛 AssertException。</summary>
+    public static class Assert
+    {
+        public static void True(bool condition, string detail = null)
+        {
+            if (!condition)
+                throw new AssertException("期望 True，实际 False。" + Detail(detail));
+        }
+
+        public static void False(bool condition, string detail = null)
+        {
+            if (condition)
+                throw new AssertException("期望 False，实际 True。" + Detail(detail));
+        }
+
+        public static void Equal<T>(T expected, T actual, string detail = null)
+        {
+            if (!object.Equals(expected, actual))
+                throw new AssertException("期望 <" + Show(expected) + ">，实际 <" + Show(actual) + ">。" + Detail(detail));
+        }
+
+        public static void Null(object value, string detail = null)
+        {
+            if (value != null)
+                throw new AssertException("期望 null，实际 <" + Show(value) + ">。" + Detail(detail));
+        }
+
+        public static void NotNull(object value, string detail = null)
+        {
+            if (value == null)
+                throw new AssertException("期望非 null，实际 null。" + Detail(detail));
+        }
+
+        public static void Throws<TException>(Action action, string detail = null) where TException : Exception
+        {
+            try
+            {
+                action();
+            }
+            catch (TException)
+            {
+                return;
+            }
+            catch (Exception ex)
+            {
+                throw new AssertException("期望抛 " + typeof(TException).Name + "，实际抛 " + ex.GetType().Name + "。" + Detail(detail));
+            }
+            throw new AssertException("期望抛 " + typeof(TException).Name + "，实际未抛异常。" + Detail(detail));
+        }
+
+        private static string Show(object value)
+        {
+            return value == null ? "(null)" : value.ToString();
+        }
+
+        private static string Detail(string detail)
+        {
+            return string.IsNullOrEmpty(detail) ? string.Empty : " " + detail;
+        }
+    }
+
+    /// <summary>用例运行器：逐个执行、统计通过/失败、打印失败原因。</summary>
+    public static class TestRunner
+    {
+        public static int PassedCount { get; private set; }
+        public static int FailedCount { get; private set; }
+
+        public static void Run(string name, Action body)
+        {
+            try
+            {
+                body();
+                PassedCount++;
+                Console.WriteLine("[PASS] " + name);
+            }
+            catch (Exception ex)
+            {
+                FailedCount++;
+                Console.WriteLine("[FAIL] " + name + "：" + ex.GetType().Name + "：" + ex.Message);
+            }
+        }
+
+        public static int TotalCount
+        {
+            get { return PassedCount + FailedCount; }
+        }
+    }
+}
diff --git a/work/task-1-report.md b/work/task-1-report.md
new file mode 100644
index 0000000..024c984
--- /dev/null
+++ b/work/task-1-report.md
@@ -0,0 +1,42 @@
+# Task 1 执行报告：配置层（模型/校验/自愈/原子写/互斥）
+
+状态：**完成**。`bash build.sh` 退出 0，测试 68 个用例全部 PASS、0 失败。
+
+> 说明：本任务执行中途被服务重启打断过一次。重启前已落盘校验器、配置模型与全部测试文件；重启后现场核对发现 ConfigPathResolver / ConfigStore 尚未落盘、且此前一度出现的提交并不存在（HEAD 实际为 2e54a63）。本报告与提交均以恢复后的现场实测为准，不沿用任何中断前的哈希或结论。
+
+## 交付文件
+
+生产代码（命名空间统一 `InspectionWidget.Config`，后续任务按此引用）：
+- `src/Config/AppConfig.cs`：`CaptureMode` 枚举、`CredentialEntry`（`NeedsReset` 为 `[IgnoreDataMember]` 运行时标记）、`AppConfig`（`[OnDeserializing]` 先置默认值再反序列化：AutoClearClipboard=true、列表空表；`CreateDefault()` 给两条示例设备「设备1/设备2」）。
+- `src/Config/DeviceNameValidator.cs`：`Validate` / `ValidateAll` / `ValidateFullPathLength(rootPath, name)`（root 与 name 直接 Path.Combine，总长 > 259 返回错误；调用方拼日期子目录、顺延后缀时应一并计入，口径已写进函数注释）。
+- `src/Config/ConfigPathResolver.cs`：回退文件已存在→回退优先；否则 exe 目录可写→主路径；否则回退。全部 Path.Combine，文件名 config.json，回退目录 `<LocalAppData>\InspectionWidget\`。
+- `src/Config/ConfigStore.cs`：Load 分 Missing / Ok / Defaulted / CorruptBackedUp / IoErrorKept 五类（Defaulted 按简报既定口径：文本扫描 JSON 是否含各可选字段名，缺任一即判；列表为 null 另补空表并计 Defaulted）；损坏先备份 `config.corrupt.yyyyMMddHHmmss.json`（重名加序号），备份失败按 IoErrorKept 且不覆盖原件；Save 走「目录可写探针 → 锁文件 FileShare.None 独占 → 同目录唯一临时文件写 JSON → File.Copy 覆盖 → 删 tmp」，拿不到锁或不可写均返回 Saved=false 且正式文件不动。
+
+测试基础设施（零第三方包）：
+- `tests/InspectionWidget.Tests.csproj`（控制台 Exe，ProjectReference 到 src 工程，Compile 通配）、`tests/TestFramework.cs`（Assert + TestRunner，退出码=有失败?1:0）、`tests/Program.cs`。
+- `tests/DeviceNameValidatorTests.cs`、`tests/AppConfigTests.cs`、`tests/ConfigStoreTests.cs`、`tests/ConfigPathResolverTests.cs`、`tests/ConfigStoreLockTests.cs`。
+
+接线与必要修正（各附原因）：
+- `build.sh`：tests 构建已由 Task 0 预留结构自动接上；仅把测试运行通配由 `*.exe` 收紧为 `*.Tests.exe`——tests 输出目录含 ProjectReference 拷贝的主程序 InspectionWidget.exe（GUI、无显示必崩），原通配会误跑它导致脚本假失败（实测踩到）。主程序构建行为未动。
+- `src/InspectionWidget.csproj`：新增 `System.Xml` 引用。DataContractJsonSerializer 的基类 XmlObjectSerializer 公开签名含 XmlReader/XmlWriter，不加引用编译报 CS0012（实测）；System.Xml 为 .NET Framework 系统自带程序集，非第三方包。此项与「仅限六个程序集」的清单口径有出入，但不用 DCJS 之外没有别的合规序列化路线，请审查留意。
+
+## TDD 证据（RED → GREEN）
+
+- 组 1（校验器 + 配置模型，中断前执行）：先写 DeviceNameValidatorTests / AppConfigTests，构建报 CS0234「命名空间 Config 不存在」→ 实现 AppConfig + DeviceNameValidator 后通过。其中一条路径长度用例曾因**测试数据算错**失败（根 240 + 后缀总长实为 251，未超限，属合法），已把数据改为 250（总长 261）并注明，属测试笔误修正而非改判据。
+- 组 2–4（ConfigStore / 路径判定 / 锁互斥，恢复后执行）：先跑 `bash build.sh` 得真实 RED——退出 1，error CS0246「ConfigStore could not be found」等类型缺失错误（失败原因是功能缺失，非笔误）→ 实现 ConfigPathResolver + ConfigStore → 全量通过。锁用例在 Mono 下真实运行（同进程 FileShare.None 独占亦生效，持锁 Save 返回 false 且正式文件字节不变，释锁后恢复成功）。
+
+## 用例分布（总 68，失败 0）
+
+- DeviceNameValidatorTests：47（非法字符 9 条逐个、控制字符 2、`.`/`..`、尾空格/尾点、保留名与带扩展名形式、COM0/CONSOLE 非保留名、大小写不敏感重名含 `设备A`/`设备a`、ValidateAll 内部重名与逐个合法性、路径长度 259 合法 / 260 非法 / 深根+`-99.png` 非法等）
+- AppConfigTests：6（CreateDefault 全字段默认值）
+- ConfigStoreTests：8（缺文件 Missing+默认、往返全字段不丢且 Kind=Ok、缺可选字段 Defaulted 且 AutoClearClipboard=true、null 列表补空表、乱码与类型错误走 CorruptBackedUp 且备份内容=原件、Save 后无 .tmp 残留、NeedsReset 不落盘）
+- ConfigPathResolverTests：5（主路径 / 回退 / 回退优先 / 两处不可写仍给回退 / 不可写时 Save 失败且不产生文件）
+- ConfigStoreLockTests：2（持锁 Save 失败且文件不变、释锁恢复）
+
+全程 C# 7.2 以内语法（R6）：构建走 build.sh 的 LangVersion=7.2 覆盖并通过，可资证明。
+
+## 遗留问题
+
+1. `System.Xml` 引用与原六程序集清单的口径出入（见上），提请审查确认。
+2. Load 的 IoErrorKept 分支（读文件 I/O 失败）已按简报实现，但无独立自动化用例：Mono/Linux 下难以可靠模拟「文件存在但读取被拒」（FileShare 不拦读、权限位对 root 无效）。待 Windows 验收时可补。
+3. 工作区另有未跟踪的 `work/task-1-review-package.md`（非本任务产物），未纳入本次提交，留待协调者处置。
