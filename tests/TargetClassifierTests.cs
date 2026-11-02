using InspectionWidget.Capture;

namespace InspectionWidget.Tests
{
    /// <summary>
    /// TargetClassifier 排除判定：isSelf 恒排除、桌面/任务栏类名命中（大小写不敏感）、
    /// 类名查不到（null/空）按排除处理、普通窗口类名放行。
    /// </summary>
    public static class TargetClassifierTests
    {
        public static void RunAll()
        {
            TestRunner.Run("分类器：isSelf=true 时普通类名也恒排除", () =>
            {
                Assert.True(TargetClassifier.IsExcluded("Notepad", true));
            });

            TestRunner.Run("分类器：isSelf=true 且类名为 null 也恒排除", () =>
            {
                Assert.True(TargetClassifier.IsExcluded(null, true));
            });

            TestRunner.Run("分类器：Progman（桌面）排除", () =>
            {
                Assert.True(TargetClassifier.IsExcluded("Progman", false));
            });

            TestRunner.Run("分类器：WorkerW（桌面）排除", () =>
            {
                Assert.True(TargetClassifier.IsExcluded("WorkerW", false));
            });

            TestRunner.Run("分类器：Shell_TrayWnd（任务栏）排除", () =>
            {
                Assert.True(TargetClassifier.IsExcluded("Shell_TrayWnd", false));
            });

            TestRunner.Run("分类器：Shell_SecondaryTrayWnd（副屏任务栏）排除", () =>
            {
                Assert.True(TargetClassifier.IsExcluded("Shell_SecondaryTrayWnd", false));
            });

            TestRunner.Run("分类器：类名匹配大小写不敏感", () =>
            {
                Assert.True(TargetClassifier.IsExcluded("progman", false));
                Assert.True(TargetClassifier.IsExcluded("WORKERW", false));
                Assert.True(TargetClassifier.IsExcluded("shell_traywnd", false));
                Assert.True(TargetClassifier.IsExcluded("SHELL_SECONDARYTRAYWND", false));
            });

            TestRunner.Run("分类器：类名为 null 按排除处理", () =>
            {
                Assert.True(TargetClassifier.IsExcluded(null, false));
            });

            TestRunner.Run("分类器：类名为空字符串按排除处理", () =>
            {
                Assert.True(TargetClassifier.IsExcluded(string.Empty, false));
            });

            TestRunner.Run("分类器：普通类名 Notepad 不排除", () =>
            {
                Assert.False(TargetClassifier.IsExcluded("Notepad", false));
            });

            TestRunner.Run("分类器：普通类名 Chrome_WidgetWin_1 不排除", () =>
            {
                Assert.False(TargetClassifier.IsExcluded("Chrome_WidgetWin_1", false));
            });
        }
    }
}
