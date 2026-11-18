using System;
using System.Collections.Generic;
using System.IO;
using InspectionWidget.Config;

namespace InspectionWidget.Storage
{
    public enum UsbSyncStatus
    {
        Success = 0,
        UsbRootNotConfigured = 1,
        UsbRootNotFound = 2,
        UsbTodayFolderMissing = 3,
        TargetRootNotConfigured = 4,
        Error = 5
    }

    public sealed class ClipboardCopyResult
    {
        public bool Success;
        public int CopiedFileCount;
        public int CopiedDirectoryCount;
        public int SkippedInvalidCount;
        public string TargetDirectory;
        public string Error;
    }

    public sealed class UsbSyncResult
    {
        public UsbSyncStatus Status;
        public int CopiedFileCount;
        public int SkippedFileCount;
        public string SourceDirectory;
        public string TargetDirectory;
        public string Error;
    }

    /// <summary>
    /// 剪贴板文件/文件夹存入今日巡检目录，以及 U盘巡检目录增量同步服务。
    /// </summary>
    public sealed class InspectionFileTransferService
    {
        private const int MaxCollisionSequence = 10000;

        public ClipboardCopyResult CopyClipboardItems(
            IEnumerable<string> sourcePaths,
            string targetRootPath,
            string folderPattern,
            DateTime date)
        {
            if (string.IsNullOrWhiteSpace(targetRootPath))
            {
                return new ClipboardCopyResult
                {
                    Success = false,
                    Error = "尚未设置截图保存根目录。"
                };
            }
            if (sourcePaths == null)
            {
                return new ClipboardCopyResult
                {
                    Success = false,
                    Error = "剪贴板中没有文件或文件夹。"
                };
            }

            string targetDir = ResolveTodayDirectory(targetRootPath, folderPattern, date);
            var validFiles = new List<string>();
            var validDirs = new List<string>();
            int skippedInvalid = 0;

            foreach (string rawPath in sourcePaths)
            {
                if (string.IsNullOrWhiteSpace(rawPath))
                {
                    skippedInvalid++;
                    continue;
                }
                string path = rawPath.Trim();
                if (File.Exists(path))
                {
                    validFiles.Add(path);
                }
                else if (Directory.Exists(path))
                {
                    validDirs.Add(path);
                }
                else
                {
                    skippedInvalid++;
                }
            }

            if (validFiles.Count == 0 && validDirs.Count == 0)
            {
                return new ClipboardCopyResult
                {
                    Success = false,
                    SkippedInvalidCount = skippedInvalid,
                    TargetDirectory = targetDir,
                    Error = "剪贴板中没有可复制的文件或文件夹。"
                };
            }

            try
            {
                Directory.CreateDirectory(targetDir);
            }
            catch (Exception ex)
            {
                return new ClipboardCopyResult
                {
                    Success = false,
                    SkippedInvalidCount = skippedInvalid,
                    TargetDirectory = targetDir,
                    Error = "无法创建今日巡检目录：" + ex.Message
                };
            }

            int copiedFiles = 0;
            int copiedDirs = 0;

            try
            {
                for (int i = 0; i < validFiles.Count; i++)
                {
                    string src = validFiles[i];
                    string fileName = Path.GetFileName(src);
                    if (string.IsNullOrEmpty(fileName))
                    {
                        skippedInvalid++;
                        continue;
                    }
                    string destPath = AllocateUniqueFilePath(targetDir, fileName);
                    File.Copy(src, destPath, false);
                    copiedFiles++;
                }

                for (int i = 0; i < validDirs.Count; i++)
                {
                    string srcDir = validDirs[i];
                    if (IsSameOrSubPath(srcDir, targetDir))
                    {
                        return new ClipboardCopyResult
                        {
                            Success = false,
                            CopiedFileCount = copiedFiles,
                            CopiedDirectoryCount = copiedDirs,
                            SkippedInvalidCount = skippedInvalid,
                            TargetDirectory = targetDir,
                            Error = "不能把目标目录自身或上级目录复制到今日目录中。"
                        };
                    }
                    string dirName = Path.GetFileName(TrimTrailingSeparators(srcDir));
                    if (string.IsNullOrEmpty(dirName))
                        dirName = "folder";
                    string destDir = AllocateUniqueDirectoryPath(targetDir, dirName);
                    CopyDirectoryRecursively(srcDir, destDir);
                    copiedDirs++;
                }
            }
            catch (Exception ex)
            {
                return new ClipboardCopyResult
                {
                    Success = false,
                    CopiedFileCount = copiedFiles,
                    CopiedDirectoryCount = copiedDirs,
                    SkippedInvalidCount = skippedInvalid,
                    TargetDirectory = targetDir,
                    Error = "复制失败：" + ex.Message
                };
            }

            return new ClipboardCopyResult
            {
                Success = true,
                CopiedFileCount = copiedFiles,
                CopiedDirectoryCount = copiedDirs,
                SkippedInvalidCount = skippedInvalid,
                TargetDirectory = targetDir,
                Error = null
            };
        }

        public UsbSyncResult SyncFromUsb(
            string usbRootPath,
            string targetRootPath,
            string folderPattern,
            DateTime date)
        {
            if (string.IsNullOrWhiteSpace(usbRootPath))
            {
                return new UsbSyncResult
                {
                    Status = UsbSyncStatus.UsbRootNotConfigured,
                    Error = "尚未设置 U盘目录。"
                };
            }
            if (string.IsNullOrWhiteSpace(targetRootPath))
            {
                return new UsbSyncResult
                {
                    Status = UsbSyncStatus.TargetRootNotConfigured,
                    Error = "尚未设置截图保存根目录。"
                };
            }
            if (!Directory.Exists(usbRootPath))
            {
                return new UsbSyncResult
                {
                    Status = UsbSyncStatus.UsbRootNotFound,
                    Error = "未找到 U盘目录，请检查U盘或右键重设。"
                };
            }

            string dateFolder = FormatDateFolder(folderPattern, date);
            string sourceDir = Path.Combine(usbRootPath, dateFolder);
            string targetDir = Path.Combine(targetRootPath, dateFolder);

            if (!Directory.Exists(sourceDir))
            {
                return new UsbSyncResult
                {
                    Status = UsbSyncStatus.UsbTodayFolderMissing,
                    SourceDirectory = sourceDir,
                    TargetDirectory = targetDir,
                    Error = "U盘中暂无今日巡检目录：" + dateFolder
                };
            }

            if (IsSameOrSubPath(sourceDir, targetDir) || IsSameOrSubPath(targetDir, sourceDir))
            {
                return new UsbSyncResult
                {
                    Status = UsbSyncStatus.Error,
                    SourceDirectory = sourceDir,
                    TargetDirectory = targetDir,
                    Error = "U盘目录与当前截图目录不能重叠。"
                };
            }

            try
            {
                Directory.CreateDirectory(targetDir);
                int copiedCount = 0;
                int skippedCount = 0;
                SyncDirectoryIncremental(sourceDir, targetDir, ref copiedCount, ref skippedCount);
                return new UsbSyncResult
                {
                    Status = UsbSyncStatus.Success,
                    CopiedFileCount = copiedCount,
                    SkippedFileCount = skippedCount,
                    SourceDirectory = sourceDir,
                    TargetDirectory = targetDir,
                    Error = null
                };
            }
            catch (Exception ex)
            {
                return new UsbSyncResult
                {
                    Status = UsbSyncStatus.Error,
                    SourceDirectory = sourceDir,
                    TargetDirectory = targetDir,
                    Error = "U盘同步失败：" + ex.Message
                };
            }
        }

        public static string ResolveTodayDirectory(string rootPath, string folderPattern, DateTime date)
        {
            return Path.Combine(rootPath, FormatDateFolder(folderPattern, date));
        }

        public static string FormatDateFolder(string folderPattern, DateTime date)
        {
            string pattern = FolderNamePattern.Validate(folderPattern) == null
                ? folderPattern
                : FolderNamePattern.Default;
            return FolderNamePattern.Format(pattern, date);
        }

        private static string AllocateUniqueFilePath(string targetDir, string fileName)
        {
            string candidate = Path.Combine(targetDir, fileName);
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;

            string baseName = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            for (int seq = 1; seq <= MaxCollisionSequence; seq++)
            {
                string nextName = baseName + "-" + seq + ext;
                candidate = Path.Combine(targetDir, nextName);
                if (!File.Exists(candidate) && !Directory.Exists(candidate))
                    return candidate;
            }
            throw new IOException("目标目录同名文件过多，无法分配新文件名：" + fileName);
        }

        private static string AllocateUniqueDirectoryPath(string targetDir, string dirName)
        {
            string candidate = Path.Combine(targetDir, dirName);
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
                return candidate;

            for (int seq = 1; seq <= MaxCollisionSequence; seq++)
            {
                candidate = Path.Combine(targetDir, dirName + "-" + seq);
                if (!Directory.Exists(candidate) && !File.Exists(candidate))
                    return candidate;
            }
            throw new IOException("目标目录同名文件夹过多，无法分配新名称：" + dirName);
        }

        private static void CopyDirectoryRecursively(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            string[] files = Directory.GetFiles(sourceDir);
            for (int i = 0; i < files.Length; i++)
            {
                string fileName = Path.GetFileName(files[i]);
                if (string.IsNullOrEmpty(fileName))
                    continue;
                File.Copy(files[i], Path.Combine(destDir, fileName), false);
            }

            string[] subDirs = Directory.GetDirectories(sourceDir);
            for (int i = 0; i < subDirs.Length; i++)
            {
                string dirName = Path.GetFileName(TrimTrailingSeparators(subDirs[i]));
                if (string.IsNullOrEmpty(dirName))
                    continue;
                CopyDirectoryRecursively(subDirs[i], Path.Combine(destDir, dirName));
            }
        }

        private static void SyncDirectoryIncremental(
            string sourceDir,
            string destDir,
            ref int copiedCount,
            ref int skippedCount)
        {
            Directory.CreateDirectory(destDir);
            string[] files = Directory.GetFiles(sourceDir);
            for (int i = 0; i < files.Length; i++)
            {
                string fileName = Path.GetFileName(files[i]);
                if (string.IsNullOrEmpty(fileName))
                    continue;
                string destFile = Path.Combine(destDir, fileName);
                if (File.Exists(destFile))
                {
                    skippedCount++;
                    continue;
                }
                File.Copy(files[i], destFile, false);
                copiedCount++;
            }

            string[] subDirs = Directory.GetDirectories(sourceDir);
            for (int i = 0; i < subDirs.Length; i++)
            {
                string dirName = Path.GetFileName(TrimTrailingSeparators(subDirs[i]));
                if (string.IsNullOrEmpty(dirName))
                    continue;
                SyncDirectoryIncremental(
                    subDirs[i],
                    Path.Combine(destDir, dirName),
                    ref copiedCount,
                    ref skippedCount);
            }
        }

        private static string TrimTrailingSeparators(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static string NormalizeFullPath(string path)
        {
            string full = Path.GetFullPath(path);
            string trimmed = TrimTrailingSeparators(full);
            return string.IsNullOrEmpty(trimmed) ? full : trimmed;
        }

        /// <summary>判断 candidate 是否等于 ancestor 或位于 ancestor 之下。</summary>
        private static bool IsSameOrSubPath(string ancestor, string candidate)
        {
            try
            {
                StringComparison cmp = PathComparison();
                string anc = NormalizeFullPath(ancestor);
                string cand = NormalizeFullPath(candidate);
                if (string.Equals(anc, cand, cmp))
                    return true;
                string ancWithSep = anc.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                    ? anc
                    : anc + Path.DirectorySeparatorChar;
                return cand.StartsWith(ancWithSep, cmp);
            }
            catch
            {
                return false;
            }
        }

        private static StringComparison PathComparison()
        {
            return Environment.OSVersion.Platform == PlatformID.Win32NT
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
        }
    }
}
