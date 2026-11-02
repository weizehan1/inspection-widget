using System;
using System.IO;
using System.Text;

namespace InspectionWidget
{
    /// <summary>
    /// 全局异常日志（Task 8）：追加写 error.log——exe 同目录优先，不可写时回退
    /// %LocalAppData%\InspectionWidget\error.log，两处都失败则静默放弃。
    /// 只记时间、环节名、异常类型/消息与堆栈：调用方只传环节名，本类不接收
    /// 凭据明文、截图内容等任何业务数据。Write 内部绝不抛异常（异常处理里
    /// 再抛错会掩盖原始异常），全路径 try/catch 吞掉。
    /// </summary>
    public static class ErrorLog
    {
        private const string LogFileName = "error.log";
        private const string FallbackDirName = "InspectionWidget";

        private static readonly object Sync = new object();

        // 测试注入点（内部）：非 null 时覆盖主/回退目录，测后须 Reset 恢复。
        private static string _primaryDirOverride;
        private static string _fallbackDirOverride;

        // 最近一次实际写成功的日志路径；从未写过时为 null，CurrentLogPath 退回主路径。
        private static string _lastWrittenPath;

        /// <summary>实际写入路径：已有成功写入时为该次落点，否则为主路径（exe 同目录）。</summary>
        public static string CurrentLogPath
        {
            get
            {
                lock (Sync)
                {
                    if (_lastWrittenPath != null)
                        return _lastWrittenPath;
                }
                return PrimaryLogPath;
            }
        }

        private static string PrimaryDir
        {
            get
            {
                return _primaryDirOverride != null
                    ? _primaryDirOverride
                    : AppDomain.CurrentDomain.BaseDirectory;
            }
        }

        private static string FallbackDir
        {
            get
            {
                return _fallbackDirOverride != null
                    ? _fallbackDirOverride
                    : Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        FallbackDirName);
            }
        }

        private static string PrimaryLogPath
        {
            get { return Path.Combine(PrimaryDir, LogFileName); }
        }

        private static string FallbackLogPath
        {
            get { return Path.Combine(FallbackDir, LogFileName); }
        }

        /// <summary>测试注入：覆盖主/回退目录（任一传 null 表示该项不覆盖）。</summary>
        internal static void SetDirectoriesForTests(string primaryDir, string fallbackDir)
        {
            lock (Sync)
            {
                _primaryDirOverride = primaryDir;
                _fallbackDirOverride = fallbackDir;
                _lastWrittenPath = null;
            }
        }

        /// <summary>测试收尾：清除注入目录与落点记录，恢复默认路径。</summary>
        internal static void ResetDirectoriesForTests()
        {
            lock (Sync)
            {
                _primaryDirOverride = null;
                _fallbackDirOverride = null;
                _lastWrittenPath = null;
            }
        }

        /// <summary>
        /// 记一条异常日志。context 为环节名（如「UI线程」「非UI线程」），
        /// 不得包含凭据/图像等业务数据。本方法绝不抛异常。
        /// </summary>
        public static void Write(string context, Exception ex)
        {
            try
            {
                string text = Format(context, ex);
                lock (Sync)
                {
                    string primary = PrimaryLogPath;
                    if (TryAppend(primary, text))
                    {
                        _lastWrittenPath = primary;
                        return;
                    }
                    string fallback = FallbackLogPath;
                    if (TryAppend(fallback, text))
                    {
                        _lastWrittenPath = fallback;
                    }
                    // 两处都写不进去：静默放弃，绝不在异常处理里再抛错。
                }
            }
            catch
            {
                // 日志失败静默放弃（同上）。
            }
        }

        private static string Format(string context, Exception ex)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.Append("] ");
            sb.Append(string.IsNullOrEmpty(context) ? "未知环节" : context);
            sb.Append(' ');
            if (ex == null)
            {
                sb.Append("（无异常对象）");
            }
            else
            {
                sb.Append(ex.GetType().Name);
                sb.Append(": ");
                sb.Append(ex.Message);
            }
            sb.AppendLine();
            if (ex != null && ex.StackTrace != null)
                sb.AppendLine(ex.StackTrace);
            return sb.ToString();
        }

        private static bool TryAppend(string path, string text)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.AppendAllText(path, text, Encoding.UTF8);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
