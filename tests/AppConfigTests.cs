using InspectionWidget.Config;

namespace InspectionWidget.Tests
{
    /// <summary>AppConfig 模型默认值。</summary>
    public static class AppConfigTests
    {
        public static void RunAll()
        {
            TestRunner.Run("CreateDefault：示例设备两条（设备1、设备2）", () =>
            {
                AppConfig cfg = AppConfig.CreateDefault();
                Assert.NotNull(cfg.Devices);
                Assert.Equal(2, cfg.Devices.Count);
                Assert.Equal("设备1", cfg.Devices[0]);
                Assert.Equal("设备2", cfg.Devices[1]);
            });
            TestRunner.Run("CreateDefault：AutoClearClipboard 默认 true", () =>
                Assert.True(AppConfig.CreateDefault().AutoClearClipboard));
            TestRunner.Run("CreateDefault：IsCredentialCollapsed 默认 false", () =>
                Assert.False(AppConfig.CreateDefault().IsCredentialCollapsed));
            TestRunner.Run("CreateDefault：CaptureMode 默认 Auto", () =>
                Assert.Equal(CaptureMode.Auto, AppConfig.CreateDefault().CaptureMode));
            TestRunner.Run("CreateDefault：Credentials 为空表（非 null）", () =>
            {
                AppConfig cfg = AppConfig.CreateDefault();
                Assert.NotNull(cfg.Credentials);
                Assert.Equal(0, cfg.Credentials.Count);
            });
            TestRunner.Run("CreateDefault：WindowLeft/WindowTop 为 null、LastDevice 为 null", () =>
            {
                AppConfig cfg = AppConfig.CreateDefault();
                Assert.Null(cfg.WindowLeft);
                Assert.Null(cfg.WindowTop);
                Assert.Null(cfg.LastDevice);
            });
        }
    }
}
