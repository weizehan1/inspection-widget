using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
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
        public string SampleSavedFilePath;
        public string Error;
    }

    public sealed class UsbSyncResult
    {
        public UsbSyncStatus Status;
        public int CopiedFileCount;
        public int SkippedFileCount;
        public string SourceDirectory;
        public string TargetDirectory;
        public string SampleSavedFilePath;
        public string Error;
    }

    /// <summary>
    /// 剪贴板文件/文件夹存入今日巡检目录，以及 U盘巡检目录增量同步服务。
    /// </summary>
    public sealed class InspectionFileTransferService
    {
        private const int MaxCollisionSequence = 10000;
        public const int MaxRecursionDepth = 64;

        internal static bool PathsOverlap(string pathA, string pathB)
        {
            if (string.IsNullOrWhiteSpace(pathA) || string.IsNullOrWhiteSpace(pathB))
                return false;
            return IsSameOrSubPath(pathA, pathB) || IsSameOrSubPath(pathB, pathA);
        }

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

            string targetDir;
            try
            {
                targetDir = ResolveTodayDirectory(targetRootPath, folderPattern, date);
            }
            catch (Exception ex)
            {
                return new ClipboardCopyResult
                {
                    Success = false,
                    Error = "目标目录路径无效：" + ex.Message
                };
            }

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
                try
                {
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
                catch
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

            for (int i = 0; i < validDirs.Count; i++)
            {
                string srcDir = validDirs[i];
                if (IsReparsePoint(srcDir))
                {
                    return new ClipboardCopyResult
                    {
                        Success = false,
                        SkippedInvalidCount = skippedInvalid,
                        TargetDirectory = targetDir,
                        Error = "不能复制符号链接或目录联接点，请复制真实文件夹。"
                    };
                }
                if (IsSameOrSubPath(srcDir, targetDir))
                {
                    return new ClipboardCopyResult
                    {
                        Success = false,
                        SkippedInvalidCount = skippedInvalid,
                        TargetDirectory = targetDir,
                        Error = "不能把目标目录自身或上级目录复制到今日目录中。"
                    };
                }
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
            string sampleSavedPath = null;

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
                    string saved = CopyFileAtomicallyToUniquePath(src, targetDir, fileName);
                    if (sampleSavedPath == null)
                        sampleSavedPath = saved;
                    copiedFiles++;
                }

                for (int i = 0; i < validDirs.Count; i++)
                {
                    string srcDir = validDirs[i];
                    string normalizedSrc = NormalizeFullPath(srcDir);
                    string dirName = Path.GetFileName(normalizedSrc);
                    if (string.IsNullOrEmpty(dirName) || dirName == "." || dirName == "..")
                        dirName = "folder";
                    string destDir = AllocateUniqueDirectoryPath(targetDir, dirName);
                    CopyDirectoryRecursively(srcDir, destDir, targetDir, 0, ref sampleSavedPath);
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
                    SampleSavedFilePath = sampleSavedPath,
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
                SampleSavedFilePath = sampleSavedPath,
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

            string sourceDir;
            string targetDir;
            string dateFolder;
            try
            {
                if (PathsOverlap(usbRootPath, targetRootPath))
                {
                    return new UsbSyncResult
                    {
                        Status = UsbSyncStatus.Error,
                        Error = "U盘目录不能与截图根目录相同或互相包含。"
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

                dateFolder = FormatDateFolder(folderPattern, date);
                sourceDir = Path.Combine(usbRootPath, dateFolder);
                targetDir = Path.Combine(targetRootPath, dateFolder);
            }
            catch (Exception ex)
            {
                return new UsbSyncResult
                {
                    Status = UsbSyncStatus.Error,
                    Error = "路径无效：" + ex.Message
                };
            }

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

            if (IsReparsePoint(sourceDir))
            {
                return new UsbSyncResult
                {
                    Status = UsbSyncStatus.Error,
                    SourceDirectory = sourceDir,
                    TargetDirectory = targetDir,
                    Error = "U盘今日目录不能为符号链接或目录联接点。"
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

            int copiedCount = 0;
            int skippedCount = 0;
            string samplePath = null;
            try
            {
                SyncDirectoryIncremental(sourceDir, targetDir, targetDir, 0,
                    ref copiedCount, ref skippedCount, ref samplePath);
                return new UsbSyncResult
                {
                    Status = UsbSyncStatus.Success,
                    CopiedFileCount = copiedCount,
                    SkippedFileCount = skippedCount,
                    SourceDirectory = sourceDir,
                    TargetDirectory = targetDir,
                    SampleSavedFilePath = samplePath,
                    Error = null
                };
            }
            catch (Exception ex)
            {
                return new UsbSyncResult
                {
                    Status = UsbSyncStatus.Error,
                    CopiedFileCount = copiedCount,
                    SkippedFileCount = skippedCount,
                    SourceDirectory = sourceDir,
                    TargetDirectory = targetDir,
                    SampleSavedFilePath = samplePath,
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

        private static void SplitFileNameForSuffix(string fileName, out string baseName, out string ext)
        {
            if (fileName.StartsWith(".", StringComparison.Ordinal) && fileName.IndexOf('.', 1) < 0)
            {
                baseName = fileName;
                ext = string.Empty;
                return;
            }
            baseName = Path.GetFileNameWithoutExtension(fileName);
            ext = Path.GetExtension(fileName);
        }

        private static string FormatNumberedFileName(string fileName, int seq)
        {
            if (seq == 0)
                return fileName;
            string baseName;
            string ext;
            SplitFileNameForSuffix(fileName, out baseName, out ext);
            return baseName + "-" + seq + ext;
        }

        private static string AllocateUniqueDirectoryPath(string targetDir, string dirName)
        {
            for (int seq = 0; seq <= MaxCollisionSequence; seq++)
            {
                string name = seq == 0 ? dirName : dirName + "-" + seq;
                string candidate = Path.Combine(targetDir, name);
                EnsurePathWithinLimit(candidate, true);
                if (!Directory.Exists(candidate) && !File.Exists(candidate))
                    return candidate;
            }
            throw new IOException("目标目录同名文件夹过多，无法分配新名称：" + dirName);
        }

        private static string CopyFileAtomicallyToUniquePath(string srcFile, string targetDir, string fileName)
        {
            Directory.CreateDirectory(targetDir);
            string tmpPath = Path.Combine(targetDir, ".iw-copy-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp");
            EnsurePathWithinLimit(tmpPath, false);
            File.Copy(srcFile, tmpPath, false);
            try
            {
                for (int seq = 0; seq <= MaxCollisionSequence; seq++)
                {
                    string nextName = FormatNumberedFileName(fileName, seq);
                    string candidate = Path.Combine(targetDir, nextName);
                    EnsurePathWithinLimit(candidate, false);
                    if (File.Exists(candidate) || Directory.Exists(candidate))
                        continue;
                    try
                    {
                        File.Move(tmpPath, candidate);
                        return candidate;
                    }
                    catch (IOException)
                    {
                        if (File.Exists(candidate) || Directory.Exists(candidate))
                            continue;
                        throw;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        if (Directory.Exists(candidate))
                            continue;
                        throw;
                    }
                }
                throw new IOException("目标目录同名文件过多，无法分配新文件名：" + fileName);
            }
            finally
            {
                TryDeleteQuietly(tmpPath);
            }
        }

        private static void CopyDirectoryRecursively(
            string sourceDir,
            string destDir,
            string destRoot,
            int depth,
            ref string sampleSavedPath)
        {
            if (depth > MaxRecursionDepth)
                throw new IOException("目录嵌套层级过深，已中止复制。");
            EnsurePathWithinLimit(destDir, true);

            string[] files = Directory.GetFiles(sourceDir);
            string[] subDirs = Directory.GetDirectories(sourceDir);
            Directory.CreateDirectory(destDir);

            for (int i = 0; i < files.Length; i++)
            {
                string fileName = Path.GetFileName(files[i]);
                if (string.IsNullOrEmpty(fileName))
                    continue;
                string saved = CopyFileAtomicallyToUniquePath(files[i], destDir, fileName);
                if (sampleSavedPath == null)
                    sampleSavedPath = saved;
            }

            for (int i = 0; i < subDirs.Length; i++)
            {
                string subDir = subDirs[i];
                if (IsReparsePoint(subDir) || IsSameOrSubPath(subDir, destRoot))
                    continue;
                string dirName = Path.GetFileName(TrimTrailingSeparators(subDir));
                if (string.IsNullOrEmpty(dirName) || dirName == "." || dirName == "..")
                    continue;
                CopyDirectoryRecursively(subDir, Path.Combine(destDir, dirName), destRoot, depth + 1, ref sampleSavedPath);
            }
        }

        private static void SyncDirectoryIncremental(
            string sourceDir,
            string destDir,
            string destRoot,
            int depth,
            ref int copiedCount,
            ref int skippedCount,
            ref string samplePath)
        {
            if (depth > MaxRecursionDepth)
                throw new IOException("U盘目录嵌套层级过深，已中止同步。");

            string[] files = Directory.GetFiles(sourceDir);
            string[] subDirs = Directory.GetDirectories(sourceDir);

            for (int i = 0; i < files.Length; i++)
            {
                string srcFile = files[i];
                string fileName = Path.GetFileName(srcFile);
                if (string.IsNullOrEmpty(fileName))
                    continue;

                if (Directory.Exists(destDir) && TryFindMatchingSibling(srcFile, destDir, fileName))
                {
                    skippedCount++;
                    continue;
                }

                Directory.CreateDirectory(destDir);
                string saved = CopyFileAtomicallyToUniquePath(srcFile, destDir, fileName);
                if (samplePath == null)
                    samplePath = saved;
                copiedCount++;
            }

            for (int i = 0; i < subDirs.Length; i++)
            {
                string subDir = subDirs[i];
                if (IsReparsePoint(subDir) || IsSameOrSubPath(subDir, destRoot))
                    continue;
                string dirName = Path.GetFileName(TrimTrailingSeparators(subDir));
                if (string.IsNullOrEmpty(dirName) || dirName == "." || dirName == "..")
                    continue;

                string destSubDir = Path.Combine(destDir, dirName);
                if (File.Exists(destSubDir))
                    destSubDir = AllocateUniqueDirectoryPath(destDir, dirName);

                SyncDirectoryIncremental(
                    subDir,
                    destSubDir,
                    destRoot,
                    depth + 1,
                    ref copiedCount,
                    ref skippedCount,
                    ref samplePath);
            }
        }

        private static bool TryFindMatchingSibling(string srcFile, string destDir, string fileName)
        {
            for (int seq = 0; seq <= MaxCollisionSequence; seq++)
            {
                string candidate = Path.Combine(destDir, FormatNumberedFileName(fileName, seq));
                if (candidate.Length > DeviceNameValidator.MaxFullPathLength)
                    break;
                if (Directory.Exists(candidate))
                    continue;
                if (!File.Exists(candidate))
                {
                    if (seq == 0)
                        return false;
                    break;
                }
                if (FilesHaveSameContent(srcFile, candidate))
                    return true;
            }
            return false;
        }

        private static bool FilesHaveSameContent(string fileA, string fileB)
        {
            try
            {
                var infoA = new FileInfo(fileA);
                var infoB = new FileInfo(fileB);
                if (infoA.Length != infoB.Length)
                    return false;
                if (infoA.Length == 0)
                    return true;
                byte[] hashA = ComputeFileSha256(fileA);
                byte[] hashB = ComputeFileSha256(fileB);
                if (hashA.Length != hashB.Length)
                    return false;
                for (int i = 0; i < hashA.Length; i++)
                {
                    if (hashA[i] != hashB[i])
                        return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static byte[] ComputeFileSha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return sha.ComputeHash(stream);
            }
        }

        private static void EnsurePathWithinLimit(string path, bool isDirectory)
        {
            int limit = isDirectory ? DeviceNameValidator.MaxFullPathLength - 12 : DeviceNameValidator.MaxFullPathLength;
            if (path.Length > limit)
                throw new PathTooLongException("目标路径过长（" + path.Length + " 字符，上限 " + limit + "）：" + path);
        }

        private static void TryDeleteQuietly(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }

        private static bool IsReparsePoint(string path)
        {
            try
            {
                FileAttributes attrs = File.GetAttributes(path);
                return (attrs & FileAttributes.ReparsePoint) != 0;
            }
            catch
            {
                return false;
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
