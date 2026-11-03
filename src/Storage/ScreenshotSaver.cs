using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using InspectionWidget.Config;

namespace InspectionWidget.Storage
{
    /// <summary>保存结果：Saved 时 FullPath 为实际落盘路径，UsedFallback 表示走了暂存链。</summary>
    public sealed class SaveResult
    {
        public bool Saved;
        public string FullPath;
        public bool UsedFallback;
        public string Error;
    }

    /// <summary>
    /// 截图保存与命名：目标目录 `&lt;根目录&gt;\日期文件夹（按 FolderNamePattern 模式生成）`，文件名按
    /// `设备名.png`、`设备名-1.png`… 顺延，以 FileMode.CreateNew 原子占用，绝不覆盖。
    /// 主目录写入失败走两级暂存：exe 目录、LocalAppData；三级全败返回「尚未保存」。
    /// 传入 Bitmap 的所有权始终在调用方：本类从不 Dispose 它，保存失败时
    /// 调用方保留原图重试。
    /// </summary>
    public sealed class ScreenshotSaver
    {
        /// <summary>文件名顺延序号上限（0..10000），超限视为该目录写入失败。</summary>
        private const int MaxSequence = 10000;

        private readonly string _exeBaseDir;
        private readonly string _localAppDataDir;

        /// <summary>生产构造：exe 所在目录与系统 LocalAppData。</summary>
        public ScreenshotSaver()
            : this(AppDomain.CurrentDomain.BaseDirectory,
                   Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
        {
        }

        /// <summary>注入构造（测试用）：两级暂存的根分别取自 exeBaseDir 与 localAppDataDir。</summary>
        public ScreenshotSaver(string exeBaseDir, string localAppDataDir)
        {
            _exeBaseDir = exeBaseDir;
            _localAppDataDir = localAppDataDir;
        }

        /// <summary>旧签名：日期文件夹名按当前默认模式（FolderNamePattern.Default）。</summary>
        public SaveResult Save(Bitmap image, string rootPath, string deviceName, DateTime captureDate)
        {
            return Save(image, rootPath, deviceName, captureDate, FolderNamePattern.Default);
        }

        /// <summary>
        /// 保存截图。captureDate 只由调用方传入（采集开始时固定），本方法内不取系统时间。
        /// 根目录为空是配置问题：直接失败、不进暂存；磁盘故障才走暂存链。
        /// folderNamePattern 决定日期子目录名；模式非法（手改配置绕过设置校验）时
        /// 回退默认模式，绝不让格式化出的分隔符把图写到根目录之外。
        /// </summary>
        public SaveResult Save(Bitmap image, string rootPath, string deviceName, DateTime captureDate,
            string folderNamePattern)
        {
            if (string.IsNullOrEmpty(rootPath))
                return new SaveResult { Saved = false, Error = "未设置保存根目录" };
            if (image == null)
                return new SaveResult { Saved = false, Error = "尚未保存：没有可保存的截图" };
            if (string.IsNullOrEmpty(deviceName))
                return new SaveResult { Saved = false, Error = "尚未保存：设备名为空" };

            if (FolderNamePattern.Validate(folderNamePattern) != null)
                folderNamePattern = FolderNamePattern.Default;
            string datePart = FolderNamePattern.Format(folderNamePattern, captureDate);
            string primaryDir = Path.Combine(rootPath, datePart);

            string[] chain =
            {
                primaryDir,
                CombineOrNull(_exeBaseDir, "FallbackScreenshots", datePart),
                CombineOrNull(_localAppDataDir, "InspectionWidget", "FallbackScreenshots", datePart)
            };

            string lastError = null;
            for (int i = 0; i < chain.Length; i++)
            {
                string dir = chain[i];

                // 路径长度校验（复用 Task 1，R8 裁决）：每一级各自校验其候选
                // 完整路径长度，该级超限视为该级失败、继续下一级——主目录深而
                // 暂存浅时本可暂存成功的图不应被判未保存；三级全超限/全失败
                // 才在循环后统一返回 Saved=false。
                if (string.IsNullOrEmpty(dir))
                {
                    lastError = "目录路径为空";
                    continue;
                }
                string lengthError = DeviceNameValidator.ValidateFullPathLength(dir, deviceName + ".png");
                if (lengthError != null)
                {
                    lastError = lengthError;
                    continue;
                }

                string fullPath;
                string error;
                if (TrySaveToDirectory(dir, image, deviceName, out fullPath, out error))
                {
                    return new SaveResult
                    {
                        Saved = true,
                        FullPath = fullPath,
                        UsedFallback = i > 0,
                        Error = null
                    };
                }
                lastError = error;
            }

            return new SaveResult
            {
                Saved = false,
                Error = "尚未保存：主目录与两级暂存均写入失败（" + lastError + "）"
            };
        }

        /// <summary>
        /// 顺延分配并以 CreateNew 真占位一个空文件，返回其完整路径。
        /// 本方法仅供测试/内部辅助，Save 主路径不得改走此方法（冲突判据未硬化，
        /// 见 TrySaveToDirectory 的 created 标志——本方法用裸 File.Exists 判冲突，
        /// 写入阶段失败会被误判为重名，主路径的硬化判据不要绕开）。
        /// 序号超上限或遇非重名 IO 异常时抛 IOException。
        /// </summary>
        public string AllocatePath(string dir, string deviceName)
        {
            if (string.IsNullOrEmpty(dir))
                throw new ArgumentException("目录为空。", "dir");
            if (string.IsNullOrEmpty(deviceName))
                throw new ArgumentException("设备名为空。", "deviceName");

            Directory.CreateDirectory(dir);
            for (int n = 0; n <= MaxSequence; n++)
            {
                string candidate = Path.Combine(dir, FileNameFor(deviceName, n));
                try
                {
                    using (new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        // CreateNew 成功即占用该名：关闭后空文件保留，即「已分配」
                    }
                    return candidate;
                }
                catch (IOException)
                {
                    if (File.Exists(candidate))
                        continue; // 名称已被占用 → 下一序号
                    throw;
                }
            }
            throw new IOException("文件名顺延超过上限 " + MaxSequence + "。");
        }

        /// <summary>简报签名别名，与 <see cref="AllocatePath"/> 同行为。</summary>
        public string AllocatePathForTest(string dir, string deviceName)
        {
            return AllocatePath(dir, deviceName);
        }

        /// <summary>
        /// 向单个目录保存：建目录后按顺延序号 CreateNew 占名，并用同一流写入 PNG。
        /// 名称冲突（CreateNew 抛 IOException 且文件已存在）→ 下一序号；
        /// 其余失败（含写流中途异常）→ 删除本次不完整文件、返回 false 交暂存链。
        /// </summary>
        private static bool TrySaveToDirectory(string dir, Bitmap image, string deviceName,
            out string fullPath, out string error)
        {
            fullPath = null;
            error = null;

            if (string.IsNullOrEmpty(dir))
            {
                error = "目录路径为空";
                return false;
            }

            try
            {
                Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                error = Describe(ex);
                return false;
            }

            for (int n = 0; n <= MaxSequence; n++)
            {
                string candidate = Path.Combine(dir, FileNameFor(deviceName, n));
                FileStream stream = null;
                bool created = false;
                try
                {
                    stream = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    created = true;
                    image.Save(stream, ImageFormat.Png);
                    stream.Flush();
                    stream.Dispose();
                    stream = null;
                    fullPath = candidate;
                    return true;
                }
                catch (IOException ex)
                {
                    CloseQuietly(stream);
                    if (!created && File.Exists(candidate))
                        continue; // 名称已被占用 → 下一序号
                    if (created)
                        TryDelete(candidate); // 本次写坏的不完整文件，仅删本次建的
                    error = Describe(ex);
                    return false;
                }
                catch (Exception ex)
                {
                    // GDI+ 编码等非 IO 异常同样视为本目录写入失败，走暂存链
                    CloseQuietly(stream);
                    if (created)
                        TryDelete(candidate);
                    error = Describe(ex);
                    return false;
                }
            }

            error = "文件名顺延超过上限 " + MaxSequence;
            return false;
        }

        private static string FileNameFor(string deviceName, int sequence)
        {
            return sequence == 0
                ? deviceName + ".png"
                : deviceName + "-" + sequence + ".png";
        }

        private static string CombineOrNull(string baseDir, params string[] parts)
        {
            if (string.IsNullOrEmpty(baseDir))
                return null;
            string result = baseDir;
            for (int i = 0; i < parts.Length; i++)
                result = Path.Combine(result, parts[i]);
            return result;
        }

        private static void CloseQuietly(FileStream stream)
        {
            if (stream == null) return;
            try { stream.Dispose(); }
            catch { /* 关闭失败不掩盖原始错误 */ }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch { /* 删除失败不掩盖原始错误；残留空/坏文件只占一个顺延序号 */ }
        }

        private static string Describe(Exception ex)
        {
            return ex.GetType().Name + "：" + ex.Message;
        }
    }
}
