using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace InspectionWidget.Planning
{
    /// <summary>
    /// 每日巡检进度的可持久化快照。DataContract JSON 的字段名与结构即磁盘契约：
    /// <c>schemaVersion</c>、<c>date</c>、<c>roundId</c>、<c>devices</c>、
    /// <c>visitedDevices</c>、<c>completionPrompted</c>、<c>completionPending</c>、
    /// <c>savedPaths</c>。快照只承载纯状态，不含 UI/路径/凭据信息；
    /// 校验由 <see cref="InspectionPlan.Restore"/> 负责。
    /// </summary>
    [DataContract]
    public sealed class InspectionProgressSnapshot
    {
        /// <summary>当前磁盘结构版本；版本不同一律拒绝恢复。</summary>
        [DataMember(Name = "schemaVersion", Order = 0)]
        public int SchemaVersion { get; set; }

        /// <summary>轮次归属的本地日期（含时间部分时按日期比较）。</summary>
        [DataMember(Name = "date", Order = 1)]
        public DateTime Date { get; set; }

        [DataMember(Name = "roundId", Order = 2)]
        public string RoundId { get; set; }

        /// <summary>本轮设备清单（配置中的顺序）。</summary>
        [DataMember(Name = "devices", Order = 3)]
        public List<string> Devices { get; set; }

        /// <summary>已被接受点击的设备（设备清单的子集）。</summary>
        [DataMember(Name = "visitedDevices", Order = 4)]
        public List<string> VisitedDevices { get; set; }

        /// <summary>本轮完成通知是否已消费。</summary>
        [DataMember(Name = "completionPrompted", Order = 5)]
        public bool CompletionPrompted { get; set; }

        /// <summary>本轮完成通知是否已具备询问资格但尚未显示。</summary>
        [DataMember(Name = "completionPending", Order = 6)]
        public bool CompletionPending { get; set; }

        /// <summary>本轮已保存的成功截图路径（用于重启后目录导航）。</summary>
        [DataMember(Name = "savedPaths", Order = 7)]
        public List<string> SavedPaths { get; set; }
    }
}
