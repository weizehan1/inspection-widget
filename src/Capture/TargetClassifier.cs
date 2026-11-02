using System;

namespace InspectionWidget.Capture
{
    /// <summary>
    /// 选窗目标分类（纯逻辑）：判断点中的顶层窗口是否应被排除（不作为截图目标）。
    /// 排除口径：组件自身恒排除；桌面（Progman/WorkerW）与任务栏
    /// （Shell_TrayWnd/Shell_SecondaryTrayWnd）排除，类名大小写不敏感；
    /// 类名查不到（null/空）按排除处理——宁可让用户重点，也不能截错窗口。
    /// </summary>
    public static class TargetClassifier
    {
        private static readonly string[] ExcludedClassNames =
        {
            "Progman",
            "WorkerW",
            "Shell_TrayWnd",
            "Shell_SecondaryTrayWnd"
        };

        public static bool IsExcluded(string className, bool isSelf)
        {
            if (isSelf)
                return true;
            if (string.IsNullOrEmpty(className))
                return true;
            for (int i = 0; i < ExcludedClassNames.Length; i++)
            {
                if (string.Equals(className, ExcludedClassNames[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
