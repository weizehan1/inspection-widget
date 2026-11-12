using System;
using System.Threading;

namespace InspectionWidget.Tests
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            bool windowsUiRequested = Array.IndexOf(args ?? new string[0], "--windows-ui") >= 0;

            DeviceNameValidatorTests.RunAll();
            AppConfigTests.RunAll();
            ConfigStoreTests.RunAll();
            ConfigPathResolverTests.RunAll();
            ConfigStoreLockTests.RunAll();
            PanelLayoutTests.RunAll();
            UiScaleTests.RunAll();
            PanelStateMachineTests.RunAll();
            TargetClassifierTests.RunAll();
            CaptureGeometryTests.RunAll();
            BlackFrameDetectorTests.RunAll();
            ClipboardGuardTests.RunAll();
            CredentialServiceTests.RunAll();
            ScreenshotSaverTests.RunAll();
            SettingsFormTests.RunAll();
            ErrorLogTests.RunAll();
            FolderNamePatternTests.RunAll();
            V102Tests.RunAll();
            V103Tests.RunAll();
            ConfigTransferTests.RunAll();
            SettingsDraftTests.RunAll();
            ConfigWriteProtectionTests.RunAll();
            AtomicConfigWriterTests.RunAll();
            SettingsApplyCoordinatorTests.RunAll();

            bool windowsUiSkipped = false;
            if (windowsUiRequested)
            {
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                    RunWindowsUiTests();
                else
                {
                    windowsUiSkipped = true;
                    Console.WriteLine("[SKIP] Windows UI：当前不是 Windows，未执行 "
                        + SettingsUiTests.CaseCount + " 条；这些用例不计为通过。");
                }
            }

            Console.WriteLine("----------------------------------------");
            Console.WriteLine("用例总数：" + TestRunner.TotalCount
                + "，通过：" + TestRunner.PassedCount
                + "，失败：" + TestRunner.FailedCount);
            if (TestRunner.FailedCount > 0)
                return 1;
            return windowsUiSkipped ? 2 : 0;
        }

        private static void RunWindowsUiTests()
        {
            Exception threadError = null;
            var thread = new Thread(delegate()
            {
                try
                {
                    SettingsUiTests.RunAll();
                }
                catch (Exception ex)
                {
                    threadError = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (threadError != null)
            {
                TestRunner.Run("Windows UI：测试线程异常", delegate()
                {
                    throw threadError;
                });
            }
        }
    }
}
