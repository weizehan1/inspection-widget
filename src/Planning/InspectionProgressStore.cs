using System;
using System.IO;
using System.Runtime.Serialization.Json;
using InspectionWidget.Config;

namespace InspectionWidget.Planning
{
    /// <summary>
    /// 每日进度的独立文件存储（不写入 config、不写交换文件）：
    /// DataContract JSON、UTF-8（无 BOM）、同目录临时文件 + 刷盘 + 原子替换，
    /// 失败时清理临时文件并如实返回错误，绝不覆盖坏原件后伪报成功。
    /// <para>契约：首次文件不存在时 <see cref="TryLoad"/> 返回 true、
    /// <c>snapshot == null</c>、<c>error == null</c>；读取到无法解析或空文件时返回
    /// false 并给出可读错误，不抛异常。</para>
    /// </summary>
    public sealed class InspectionProgressStore
    {
        /// <summary>进度文件大小上限：正常状态只有设备名/路径，超过即视为异常文件。</summary>
        public const long MaxFileBytes = 1L * 1024L * 1024L;

        private readonly string _path;
        private readonly AtomicConfigWriter _writer;

        public InspectionProgressStore(string path)
            : this(path, new AtomicConfigWriter())
        {
        }

        /// <summary>测试注入点：使用给定原子写入器（公开构造保持生产语义）。</summary>
        internal InspectionProgressStore(string path, AtomicConfigWriter writer)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("进度文件路径为空。", "path");
            _path = path;
            _writer = writer ?? new AtomicConfigWriter();
        }

        /// <summary>进度文件完整路径（供日志/诊断显示）。</summary>
        public string Path
        {
            get { return _path; }
        }

        public bool TryLoad(out InspectionProgressSnapshot snapshot, out string error)
        {
            snapshot = null;
            error = null;
            try
            {
                if (!File.Exists(_path)) return true;
                var info = new FileInfo(_path);
                if (info.Length == 0)
                {
                    error = "巡检进度文件为空，未恢复。";
                    return false;
                }
                if (info.Length > MaxFileBytes)
                {
                    error = "巡检进度文件超过 1 MiB 上限，未读取。";
                    return false;
                }

                var serializer = new DataContractJsonSerializer(typeof(InspectionProgressSnapshot));
                using (var stream = new FileStream(_path, FileMode.Open, FileAccess.Read,
                    FileShare.Read))
                {
                    var loaded = serializer.ReadObject(stream) as InspectionProgressSnapshot;
                    if (loaded == null)
                    {
                        error = "巡检进度文件内容为空，未恢复。";
                        return false;
                    }
                    snapshot = loaded;
                    return true;
                }
            }
            catch (Exception ex)
            {
                snapshot = null;
                error = "读取巡检进度失败：" + ex.Message;
                return false;
            }
        }

        public bool TrySave(InspectionProgressSnapshot snapshot, out string error)
        {
            error = null;
            if (snapshot == null)
            {
                error = "巡检进度快照为空，未保存。";
                return false;
            }

            byte[] bytes;
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(InspectionProgressSnapshot));
                using (var stream = new MemoryStream())
                {
                    serializer.WriteObject(stream, snapshot);
                    bytes = stream.ToArray();
                }
            }
            catch (Exception ex)
            {
                error = "序列化巡检进度失败：" + ex.Message;
                return false;
            }

            if (bytes.Length > MaxFileBytes)
            {
                error = "巡检进度序列化后超过 1 MiB 上限，未保存。";
                return false;
            }

            AtomicWriteResult result = _writer.WriteAllBytes(_path, bytes);
            if (!result.Written)
            {
                error = result.Error ?? "写入巡检进度失败。";
                return false;
            }
            return true;
        }
    }
}
