using System;
using System.IO;
using InspectionWidget.Storage;

namespace InspectionWidget.Tests
{
    public static class InspectionFileTransferServiceTests
    {
        public static void RunAll()
        {
            TestRunner.Run("剪贴板贴入：未设置根目录与空剪贴板分别报错", () =>
            {
                var svc = new InspectionFileTransferService();
                ClipboardCopyResult noRoot = svc.CopyClipboardItems(
                    new[] { "/tmp/a.txt" }, null, "巡检-yyyy-MM-dd", new DateTime(2026, 11, 18));
                Assert.False(noRoot.Success);
                Assert.True(noRoot.Error.Contains("根目录"));

                string root = CreateTempDir();
                try
                {
                    ClipboardCopyResult empty = svc.CopyClipboardItems(
                        null, root, "巡检-yyyy-MM-dd", new DateTime(2026, 11, 18));
                    Assert.False(empty.Success);

                    ClipboardCopyResult missing = svc.CopyClipboardItems(
                        new[] { Path.Combine(root, "does-not-exist.txt") },
                        root, "巡检-yyyy-MM-dd", new DateTime(2026, 11, 18));
                    Assert.False(missing.Success);
                    Assert.Equal(1, missing.SkippedInvalidCount);
                }
                finally
                {
                    CleanupDir(root);
                }
            });

            TestRunner.Run("剪贴板贴入：文件与文件夹递归拷贝且同名自动顺延编号", () =>
            {
                string temp = CreateTempDir();
                try
                {
                    string srcRoot = Path.Combine(temp, "src");
                    string targetRoot = Path.Combine(temp, "target");
                    Directory.CreateDirectory(srcRoot);

                    string fileA = Path.Combine(srcRoot, "report.txt");
                    File.WriteAllText(fileA, "first");

                    string folderB = Path.Combine(srcRoot, "logs");
                    Directory.CreateDirectory(Path.Combine(folderB, "sub"));
                    File.WriteAllText(Path.Combine(folderB, "app.log"), "log-1");
                    File.WriteAllText(Path.Combine(folderB, "sub", "detail.log"), "log-2");

                    var svc = new InspectionFileTransferService();
                    DateTime day = new DateTime(2026, 11, 18);

                    ClipboardCopyResult first = svc.CopyClipboardItems(
                        new[] { fileA, folderB }, targetRoot, "巡检-yyyy-MM-dd", day);
                    Assert.True(first.Success, first.Error);
                    Assert.Equal(1, first.CopiedFileCount);
                    Assert.Equal(1, first.CopiedDirectoryCount);

                    string todayDir = Path.Combine(targetRoot, "巡检-2026-11-18");
                    Assert.True(File.Exists(Path.Combine(todayDir, "report.txt")));
                    Assert.Equal("first", File.ReadAllText(Path.Combine(todayDir, "report.txt")));
                    Assert.Equal("log-2", File.ReadAllText(Path.Combine(todayDir, "logs", "sub", "detail.log")));

                    // 再次贴入同名文件与同名文件夹：自动顺延 -1，不覆盖原文件
                    File.WriteAllText(fileA, "second");
                    ClipboardCopyResult second = svc.CopyClipboardItems(
                        new[] { fileA, folderB }, targetRoot, "巡检-yyyy-MM-dd", day);
                    Assert.True(second.Success, second.Error);
                    Assert.Equal("first", File.ReadAllText(Path.Combine(todayDir, "report.txt")));
                    Assert.Equal("second", File.ReadAllText(Path.Combine(todayDir, "report-1.txt")));
                    Assert.True(Directory.Exists(Path.Combine(todayDir, "logs-1", "sub")));
                    Assert.Equal("log-2", File.ReadAllText(Path.Combine(todayDir, "logs-1", "sub", "detail.log")));
                }
                finally
                {
                    CleanupDir(temp);
                }
            });

            TestRunner.Run("剪贴板贴入：拒绝将目标上级目录自身拷入今日目录", () =>
            {
                string temp = CreateTempDir();
                try
                {
                    string targetRoot = Path.Combine(temp, "work");
                    Directory.CreateDirectory(targetRoot);
                    var svc = new InspectionFileTransferService();
                    ClipboardCopyResult res = svc.CopyClipboardItems(
                        new[] { targetRoot }, targetRoot, "巡检-yyyy-MM-dd", new DateTime(2026, 11, 18));
                    Assert.False(res.Success);
                }
                finally
                {
                    CleanupDir(temp);
                }
            });

            TestRunner.Run("U盘同步：未配置、盘符失联与当日目录缺失分别返回对应状态", () =>
            {
                string temp = CreateTempDir();
                try
                {
                    string usbRoot = Path.Combine(temp, "usb");
                    string targetRoot = Path.Combine(temp, "outer");
                    Directory.CreateDirectory(targetRoot);
                    var svc = new InspectionFileTransferService();
                    DateTime day = new DateTime(2026, 11, 18);

                    UsbSyncResult r1 = svc.SyncFromUsb(null, targetRoot, "巡检-yyyy-MM-dd", day);
                    Assert.Equal(UsbSyncStatus.UsbRootNotConfigured, r1.Status);

                    UsbSyncResult r2 = svc.SyncFromUsb(usbRoot, targetRoot, "巡检-yyyy-MM-dd", day);
                    Assert.Equal(UsbSyncStatus.UsbRootNotFound, r2.Status);

                    Directory.CreateDirectory(usbRoot);
                    UsbSyncResult r3 = svc.SyncFromUsb(usbRoot, targetRoot, "巡检-yyyy-MM-dd", day);
                    Assert.Equal(UsbSyncStatus.UsbTodayFolderMissing, r3.Status);

                    UsbSyncResult r4 = svc.SyncFromUsb(usbRoot, null, "巡检-yyyy-MM-dd", day);
                    Assert.Equal(UsbSyncStatus.TargetRootNotConfigured, r4.Status);
                }
                finally
                {
                    CleanupDir(temp);
                }
            });

            TestRunner.Run("U盘同步：同内容跳过、同文件名不同内容自动顺延、不覆盖本地原文件", () =>
            {
                string temp = CreateTempDir();
                try
                {
                    string usbRoot = Path.Combine(temp, "usb");
                    string targetRoot = Path.Combine(temp, "outer");
                    string dateFolder = "巡检-2026-11-18";
                    string usbToday = Path.Combine(usbRoot, dateFolder);
                    string targetToday = Path.Combine(targetRoot, dateFolder);

                    Directory.CreateDirectory(Path.Combine(usbToday, "nested"));
                    File.WriteAllText(Path.Combine(usbToday, "inner-1.png"), "new-from-usb");
                    File.WriteAllText(Path.Combine(usbToday, "same.png"), "identical-bytes");
                    File.WriteAllText(Path.Combine(usbToday, "shared.png"), "usb-copy");
                    File.WriteAllText(Path.Combine(usbToday, "nested", "db.png"), "nested-img");

                    Directory.CreateDirectory(targetToday);
                    File.WriteAllText(Path.Combine(targetToday, "same.png"), "identical-bytes");
                    File.WriteAllText(Path.Combine(targetToday, "shared.png"), "outer-existing");

                    var svc = new InspectionFileTransferService();
                    DateTime day = new DateTime(2026, 11, 18);

                    UsbSyncResult first = svc.SyncFromUsb(usbRoot, targetRoot, "巡检-yyyy-MM-dd", day);
                    Assert.Equal(UsbSyncStatus.Success, first.Status);
                    Assert.Equal(3, first.CopiedFileCount);
                    Assert.Equal(1, first.SkippedFileCount);
                    Assert.Equal("outer-existing", File.ReadAllText(Path.Combine(targetToday, "shared.png")));
                    Assert.Equal("usb-copy", File.ReadAllText(Path.Combine(targetToday, "shared-1.png")));
                    Assert.Equal("new-from-usb", File.ReadAllText(Path.Combine(targetToday, "inner-1.png")));
                    Assert.Equal("nested-img", File.ReadAllText(Path.Combine(targetToday, "nested", "db.png")));

                    UsbSyncResult second = svc.SyncFromUsb(usbRoot, targetRoot, "巡检-yyyy-MM-dd", day);
                    Assert.Equal(UsbSyncStatus.Success, second.Status);
                    Assert.Equal(0, second.CopiedFileCount);
                    Assert.Equal(4, second.SkippedFileCount);
                }
                finally
                {
                    CleanupDir(temp);
                }
            });

            TestRunner.Run("贴入与U盘同步：目录重迭拒绝、空子目录惰性创建与点文件命名保持", () =>
            {
                string temp = CreateTempDir();
                try
                {
                    Assert.True(InspectionFileTransferService.PathsOverlap(
                        Path.Combine(temp, "inspections"),
                        Path.Combine(temp, "inspections", "usb")));
                    Assert.False(InspectionFileTransferService.PathsOverlap(
                        Path.Combine(temp, "inspections"),
                        Path.Combine(temp, "inspections2")));

                    string usbToday = Path.Combine(temp, "usb", "巡检-2026-11-18");
                    string targetRoot = Path.Combine(temp, "outer");
                    Directory.CreateDirectory(Path.Combine(usbToday, "empty-sub"));
                    File.WriteAllText(Path.Combine(usbToday, ".env"), "usb-env");

                    string targetToday = Path.Combine(targetRoot, "巡检-2026-11-18");
                    Directory.CreateDirectory(targetToday);
                    File.WriteAllText(Path.Combine(targetToday, ".env"), "local-env");

                    var svc = new InspectionFileTransferService();
                    DateTime day = new DateTime(2026, 11, 18);

                    UsbSyncResult syncRes = svc.SyncFromUsb(
                        Path.Combine(temp, "usb"), targetRoot, "巡检-yyyy-MM-dd", day);
                    Assert.Equal(UsbSyncStatus.Success, syncRes.Status);
                    Assert.Equal(1, syncRes.CopiedFileCount);
                    Assert.True(File.Exists(Path.Combine(targetToday, ".env-1")));
                    Assert.False(Directory.Exists(Path.Combine(targetToday, "empty-sub")),
                        "空子目录不应被预先创建");

                    UsbSyncResult overlapRes = svc.SyncFromUsb(
                        Path.Combine(temp, "outer", "nested"),
                        Path.Combine(temp, "outer"),
                        "巡检-yyyy-MM-dd",
                        day);
                    Assert.Equal(UsbSyncStatus.Error, overlapRes.Status);
                }
                finally
                {
                    CleanupDir(temp);
                }
            });
        }

        private static string CreateTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "iw_transfer_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void CleanupDir(string dir)
        {
            try
            {
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
            catch
            {
            }
        }
    }
}
