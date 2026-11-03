using System;

namespace InspectionWidget.Tests
{
    internal static class Program
    {
        private static int Main()
        {
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

            Console.WriteLine("----------------------------------------");
            Console.WriteLine("用例总数：" + TestRunner.TotalCount
                + "，通过：" + TestRunner.PassedCount
                + "，失败：" + TestRunner.FailedCount);
            return TestRunner.FailedCount > 0 ? 1 : 0;
        }
    }
}
