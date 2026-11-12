using System;
using System.IO;

namespace InspectionWidget.Config
{
    /// <summary>
    /// 配置写入保护：读取失败或损坏备份失败时进入保护态。
    /// 保护态由 MainForm 与 SettingsForm 共用同一个 ConfigStore 实例，
    /// 因而所有保存入口都会在这里被统一拦截。
    /// </summary>
    public sealed class ConfigWriteProtection
    {
        private readonly object _gate = new object();
        private bool _protected;
        private long _generation;
        private string _reason;

        public bool IsProtected
        {
            get
            {
                lock (_gate)
                    return _protected;
            }
        }

        public string Reason
        {
            get
            {
                lock (_gate)
                    return _reason;
            }
        }

        /// <summary>每次进入保护递增；提交开始时捕获，写盘前在同一锁内复核。</summary>
        public long Generation
        {
            get
            {
                lock (_gate)
                    return _generation;
            }
        }

        public void Enter(string reason)
        {
            lock (_gate)
            {
                _protected = true;
                _generation++;
                _reason = string.IsNullOrEmpty(reason) ? "配置读取失败，保存已暂停。" : reason;
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _protected = false;
                _reason = null;
            }
        }

        /// <summary>
        /// 在同一锁内复核保护代次并执行最终写盘：保护在这之前进入则拒绝；
        /// 保护在写盘期间进入会等待本次写入结束，不会出现“已保护后仍落盘”的窗口。
        /// </summary>
        internal bool TryWriteIfUnchanged(long generation, Func<bool> write, out bool attempted)
        {
            lock (_gate)
            {
                if (_protected || _generation != generation)
                {
                    attempted = false;
                    return false;
                }
                attempted = true;
                return write();
            }
        }

        /// <summary>
        /// 目标是否为应用自管路径：权威配置、其锁文件、损坏备份与导入恢复备份。
        /// 比较规范化后的完整路径且不区分大小写。
        /// </summary>
        public static bool IsManagedConfigPath(string targetPath, string authoritativePath)
        {
            if (string.IsNullOrEmpty(targetPath) || string.IsNullOrEmpty(authoritativePath))
                return false;
            try
            {
                string target = Path.GetFullPath(targetPath);
                string auth = Path.GetFullPath(authoritativePath);
                if (string.Equals(target, auth, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (string.Equals(target, auth + ".lock", StringComparison.OrdinalIgnoreCase))
                    return true;

                string targetName = Path.GetFileName(target);
                string authDir = Path.GetDirectoryName(auth);
                string targetDir = Path.GetDirectoryName(target);
                if (!string.Equals(authDir, targetDir, StringComparison.OrdinalIgnoreCase))
                    return false;
                if (targetName.StartsWith("config.preimport.", StringComparison.OrdinalIgnoreCase)
                    && targetName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (targetName.StartsWith("config.corrupt.", StringComparison.OrdinalIgnoreCase)
                    && targetName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch
            {
                return true;
            }
            return false;
        }
    }
}
