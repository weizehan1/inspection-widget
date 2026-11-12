using System;
using System.IO;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("InspectionWidget.Tests")]

namespace InspectionWidget.Config
{
    public sealed class AtomicWriteResult
    {
        public bool Written;
        public string Error;
    }

    /// <summary>
    /// 同目录临时文件 + 刷盘 + 原子替换。
    /// 已存在目标用 File.Replace，不存在时用同目录 File.Move；
    /// 任何平台不支持或替换失败都如实返回失败，绝不退回覆盖复制。
    /// </summary>
    public sealed class AtomicConfigWriter
    {
        internal interface IFileOperations
        {
            bool Exists(string path);
            void Replace(string source, string destination);
            void Move(string source, string destination);
        }

        private sealed class SystemFileOperations : IFileOperations
        {
            public bool Exists(string path)
            {
                return File.Exists(path);
            }

            public void Replace(string source, string destination)
            {
                File.Replace(source, destination, null);
            }

            public void Move(string source, string destination)
            {
                File.Move(source, destination);
            }
        }

        private readonly IFileOperations _files;

        public AtomicConfigWriter()
            : this(new SystemFileOperations())
        {
        }

        internal AtomicConfigWriter(IFileOperations files)
        {
            if (files == null) throw new ArgumentNullException("files");
            _files = files;
        }

        internal static AtomicConfigWriter ForTests(IFileOperations files)
        {
            return new AtomicConfigWriter(files);
        }

        public AtomicWriteResult WriteAllBytes(string targetPath, byte[] bytes)
        {
            if (bytes == null)
                return Failed("原子写入内容为空。");
            return Write(targetPath, delegate(Stream stream)
            {
                stream.Write(bytes, 0, bytes.Length);
            });
        }

        public AtomicWriteResult Write(string targetPath, Action<Stream> write)
        {
            if (string.IsNullOrEmpty(targetPath))
                return Failed("目标路径为空。");
            if (write == null)
                return Failed("原子写入回调为空。");

            string target;
            string dir;
            try
            {
                target = Path.GetFullPath(targetPath);
                dir = Path.GetDirectoryName(target);
                if (string.IsNullOrEmpty(dir))
                    return Failed("目标路径无效。");
                Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                return Failed("无法准备目标目录：" + ex.Message);
            }

            string tmp = Path.Combine(dir, "." + Path.GetFileName(target) + "."
                + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    write(stream);
                    stream.Flush(true);
                }

                if (_files.Exists(target))
                    _files.Replace(tmp, target);
                else
                    _files.Move(tmp, target);

                return new AtomicWriteResult { Written = true };
            }
            catch (Exception ex)
            {
                return Failed("原子写入失败：" + ex.Message);
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
                    // 临时文件清理失败不改变写入结论
                }
            }
        }

        private static AtomicWriteResult Failed(string error)
        {
            return new AtomicWriteResult { Written = false, Error = error };
        }
    }
}
