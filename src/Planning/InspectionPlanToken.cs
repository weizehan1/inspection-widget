using System;

namespace InspectionWidget.Planning
{
    /// <summary>
    /// 一轮巡检的不可变标识：系统本地日期 + 轮次 ID。
    /// UI 把点击开始时取得的 token 原样传回；计划只接受与当前日期/轮次一致的回调，
    /// 从而拒绝跨日迟到、上一轮重置后的回调。
    /// </summary>
    public sealed class InspectionPlanToken : IEquatable<InspectionPlanToken>
    {
        public InspectionPlanToken(DateTime date, string roundId)
        {
            Date = date.Date;
            RoundId = roundId ?? string.Empty;
        }

        /// <summary>轮次归属日期（只保留日期部分）。</summary>
        public DateTime Date { get; private set; }

        /// <summary>同一日期内唯一的轮次 ID。</summary>
        public string RoundId { get; private set; }

        public bool Equals(InspectionPlanToken other)
        {
            if (ReferenceEquals(other, null)) return false;
            return Date == other.Date
                && string.Equals(RoundId, other.RoundId, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as InspectionPlanToken);
        }

        public override int GetHashCode()
        {
            return Date.GetHashCode() ^ StringComparer.Ordinal.GetHashCode(RoundId);
        }

        public override string ToString()
        {
            return Date.ToString("yyyy-MM-dd") + "/" + RoundId;
        }
    }
}
