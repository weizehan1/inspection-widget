using System.Collections.Generic;

namespace InspectionWidget.Planning
{
    public enum DeviceListEmptyKind
    {
        None,
        NoDevicesConfigured,
        AllVisitedHidden
    }

    public sealed class DeviceDisplayItem
    {
        public string Name { get; set; }
        public bool Visited { get; set; }
    }

    public sealed class DeviceDisplaySnapshot
    {
        public List<DeviceDisplayItem> Items { get; set; }
        public DeviceListEmptyKind EmptyKind { get; set; }
        public bool IsFilteringActive { get; set; }
        public int TotalCount { get; set; }
        public int VisitedCount { get; set; }
    }

    /// <summary>
    /// 截图设备清单纯显示过滤：只过滤视图，绝不修改配置清单、不削减计划总数、不改变完成判定。
    /// </summary>
    public static class InspectionDeviceDisplayFilter
    {
        public static DeviceDisplaySnapshot Build(
            InspectionPlan plan,
            IReadOnlyList<string> configuredDevices,
            bool planModeEnabled,
            bool hideVisitedDevices,
            bool showVisitedOverride)
        {
            var items = new List<DeviceDisplayItem>();
            IReadOnlyList<string> sourceDevices = planModeEnabled && plan != null
                ? plan.Devices
                : (configuredDevices ?? (IReadOnlyList<string>)new string[0]);

            int totalCount = planModeEnabled && plan != null ? plan.TotalCount : sourceDevices.Count;
            int visitedCount = plan != null ? plan.VisitedCount : 0;
            bool filteringActive = planModeEnabled && hideVisitedDevices && !showVisitedOverride;

            for (int i = 0; i < sourceDevices.Count; i++)
            {
                string name = sourceDevices[i];
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                bool visited = planModeEnabled && plan != null && plan.IsVisited(name);
                if (filteringActive && visited)
                    continue;
                items.Add(new DeviceDisplayItem
                {
                    Name = name,
                    Visited = visited
                });
            }

            DeviceListEmptyKind emptyKind = DeviceListEmptyKind.None;
            if (totalCount == 0)
                emptyKind = DeviceListEmptyKind.NoDevicesConfigured;
            else if (items.Count == 0)
                emptyKind = DeviceListEmptyKind.AllVisitedHidden;

            return new DeviceDisplaySnapshot
            {
                Items = items,
                EmptyKind = emptyKind,
                IsFilteringActive = filteringActive,
                TotalCount = totalCount,
                VisitedCount = visitedCount
            };
        }
    }
}
