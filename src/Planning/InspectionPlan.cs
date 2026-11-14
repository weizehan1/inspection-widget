using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using InspectionWidget.Storage;

namespace InspectionWidget.Planning
{
    /// <summary>
    /// 每日巡检进度：以纯状态为核心，不依赖 UI、真实截图或外部程序。
    /// <para>语义：对勾表示“这次点击已被截图状态机接受”。UI 只有在点击被接受后
    /// 才调用 <see cref="MarkVisited"/>；截图取消或保存失败不调用，因此也不会撤销。</para>
    /// <para>轮次：按系统本地日期（<see cref="IClock"/>）划分。同一天重启恢复；
    /// 跨日 <see cref="EnsureCurrentDay"/> 开新轮并清空对勾与通知状态；
    /// 重置或替换设备清单同样开新轮。旧日期/旧轮次的 token 一律不被接受。</para>
    /// <para>完成通知：只有“有效点击使非空清单从未完成变为全部完成”才产生待询问状态；
    /// <see cref="TryClaimCompletionPrompt"/> 在忙碌时返回 false 且不消耗，
    /// 空闲时每轮最多消费一次。</para>
    /// </summary>
    public sealed class InspectionPlan
    {
        /// <summary>进度快照的磁盘结构版本；与 <see cref="InspectionProgressSnapshot"/> 同步演进。</summary>
        public const int SnapshotSchemaVersion = 1;

        private readonly IClock _clock;
        private readonly List<string> _devices = new List<string>();
        private readonly ReadOnlyCollection<string> _devicesView;
        private readonly HashSet<string> _visited =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _savedPaths = new List<string>();

        private DateTime _date;
        private string _roundId;
        private bool _completionPrompted;
        private bool _completionPending;

        /// <summary>
        /// 创建设备清单已确定的计划。<paramref name="devices"/> 由调用者（配置层）传入，
        /// 空名与重复项（OrdinalIgnoreCase）被忽略，顺序保持不变。
        /// </summary>
        public InspectionPlan(IClock clock, IEnumerable<string> devices)
        {
            if (clock == null) throw new ArgumentNullException("clock");
            _clock = clock;
            _devices.AddRange(NormalizeDevices(devices));
            _devicesView = new ReadOnlyCollection<string>(_devices);
            StartNewRound(_clock.Now.Date, NewRoundId());
        }

        /// <summary>本轮归属日期（本地日期部分）。</summary>
        public DateTime Date
        {
            get { return _date; }
        }

        /// <summary>本轮的不可变标识 ID；跨日、重置、替换设备清单都会换新。</summary>
        public string RoundId
        {
            get { return _roundId; }
        }

        /// <summary>本轮设备清单（配置顺序，只读视图）。</summary>
        public IReadOnlyList<string> Devices
        {
            get { return _devicesView; }
        }

        public int TotalCount
        {
            get { return _devices.Count; }
        }

        /// <summary>已接受点击的设备数（只统计当前清单内的设备）。</summary>
        public int VisitedCount
        {
            get { return _visited.Count; }
        }

        /// <summary>清单非空且所有设备都已点过。</summary>
        public bool IsComplete
        {
            get { return _devices.Count > 0 && _visited.Count == _devices.Count; }
        }

        /// <summary>本轮完成通知是否已消费（每轮最多一次）。</summary>
        public bool CompletionPrompted
        {
            get { return _completionPrompted; }
        }

        /// <summary>供 UI 原样传回当前轮次标识。</summary>
        public InspectionPlanToken CurrentToken
        {
            get { return new InspectionPlanToken(_date, _roundId); }
        }

        /// <summary>本轮已保存的成功截图路径（按记录顺序，去重）。</summary>
        public IEnumerable<string> SavedPaths
        {
            get { return _savedPaths.ToArray(); }
        }

        /// <summary>
        /// 若系统日期已变化则开新轮（新 ID、清空对勾/保存路径/通知状态），设备清单保持不变。
        /// 同日重复调用不改变任何状态。UI 在每次点击、保存恢复和定时刷新前调用。
        /// </summary>
        public void EnsureCurrentDay()
        {
            DateTime today = _clock.Now.Date;
            if (today == _date) return;
            StartNewRound(today, NewRoundId());
        }

        /// <summary>
        /// 记录一次被截图状态机接受的点击。只接受当前日期/当前轮次且在当前清单内的设备；
        /// 重复点击返回 false 且不累计。返回 true 表示这次点击已被计入本轮进度。
        /// </summary>
        public bool MarkVisited(string device, InspectionPlanToken token)
        {
            if (!IsCurrentToken(token)) return false;
            int index = IndexOfDevice(device);
            if (index < 0) return false;
            string canonical = _devices[index];
            if (_visited.Contains(canonical)) return false;

            bool wasComplete = IsComplete;
            _visited.Add(canonical);
            if (!wasComplete && IsComplete && !_completionPrompted)
                _completionPending = true;
            return true;
        }

        public bool IsVisited(string device)
        {
            int index = IndexOfDevice(device);
            return index >= 0 && _visited.Contains(_devices[index]);
        }

        /// <summary>
        /// 撤销某设备的对勾（用户手动取消勾选）。不会重开本轮的完成通知资格：
        /// <see cref="CompletionPrompted"/> 保持原值；尚未消费的待询问状态被清除，
        /// 之后重新点满时会按“未通知”重新获得一次资格。
        /// </summary>
        public void Unmark(string device)
        {
            int index = IndexOfDevice(device);
            if (index < 0) return;
            if (_visited.Remove(_devices[index]))
                _completionPending = false;
        }

        /// <summary>开新轮：新 ID、清空对勾与保存路径、重新允许完成通知。日期取当前系统日期。</summary>
        public void ResetRound()
        {
            StartNewRound(_clock.Now.Date, NewRoundId());
        }

        /// <summary>
        /// 按配置变更对账设备清单：新项未点过，已删除项的勾选一并移除，
        /// 仅调整顺序不改变任何设备的勾选状态。对账本身不会触发完成通知
        /// （删掉最后一项未点设备导致“看起来完成”时也不弹窗）。
        /// </summary>
        public void ReconcileDevices(IEnumerable<string> devices)
        {
            List<string> incoming = NormalizeDevices(devices);
            var keep = new HashSet<string>(incoming, StringComparer.OrdinalIgnoreCase);
            _visited.RemoveWhere(delegate(string name) { return !keep.Contains(name); });
            _devices.Clear();
            _devices.AddRange(incoming);
            // 结构性增删让“已完成”事实失效时，尚未显示的完成通知一并作废；
            // 纯排序仍为完成，保留合法 pending。
            if (_completionPending && !IsComplete)
                _completionPending = false;
        }

        /// <summary>
        /// 设备改名：保留原设备的勾选，按 OrdinalIgnoreCase 防重名
        /// （目标名已被其他设备占用时不改名），不存在/非法名不产生新设备。
        /// </summary>
        public void RenameDevice(string oldName, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName)) return;
            int index = IndexOfDevice(oldName);
            if (index < 0) return;

            string target = newName.Trim();
            int existing = IndexOfDevice(target);
            if (existing >= 0 && existing != index) return;
            if (string.Equals(_devices[index], target, StringComparison.Ordinal)) return;

            bool wasVisited = _visited.Remove(_devices[index]);
            _devices[index] = target;
            if (wasVisited) _visited.Add(target);
        }

        /// <summary>
        /// 导入替换设备清单：明确开新轮（新 ID、清空对勾/保存路径/通知状态），
        /// 绝不把旧清单的对勾继承到新清单。
        /// </summary>
        public void ReplaceDevices(IEnumerable<string> devices)
        {
            List<string> incoming = NormalizeDevices(devices);
            _devices.Clear();
            _devices.AddRange(incoming);
            StartNewRound(_clock.Now.Date, NewRoundId());
        }

        /// <summary>
        /// 尝试领取完成通知的显示资格。
        /// <paramref name="captureOrSaveBusy"/> 为 true（截图/保存进行中）时返回 false
        /// 且不消耗资格；空闲时若存在待询问状态则消费并返回 true，每轮最多一次。
        /// 空清单、删项/排序、恢复时已完成且已通知都不会触发。
        /// </summary>
        public bool TryClaimCompletionPrompt(bool captureOrSaveBusy)
        {
            if (captureOrSaveBusy) return false;
            if (!_completionPending) return false;
            if (_completionPrompted) return false;
            if (!IsComplete) return false;
            _completionPrompted = true;
            _completionPending = false;
            return true;
        }

        /// <summary>
        /// 记录一次成功保存的截图路径（用于同日重启后的目录导航）。
        /// 只接受当前日期/轮次的回调；重复路径（OrdinalIgnoreCase）幂等。
        /// 旧轮/旧日回调返回 false 且不写入。
        /// </summary>
        public bool RecordSavedPath(string fullPath, InspectionPlanToken token)
        {
            if (string.IsNullOrWhiteSpace(fullPath)) return false;
            if (!IsCurrentToken(token)) return false;
            if (_savedPaths.Any(delegate(string existing)
                {
                    return string.Equals(existing, fullPath, StringComparison.OrdinalIgnoreCase);
                }))
                return true;
            _savedPaths.Add(fullPath);
            return true;
        }

        /// <summary>导出当前状态的独立副本，供 <see cref="InspectionProgressStore"/> 保存。</summary>
        public InspectionProgressSnapshot CreateSnapshot()
        {
            return new InspectionProgressSnapshot
            {
                SchemaVersion = SnapshotSchemaVersion,
                Date = _date,
                RoundId = _roundId,
                Devices = new List<string>(_devices),
                VisitedDevices = _visited
                    .OrderBy(delegate(string name) { return IndexOfDevice(name); })
                    .ToList(),
                CompletionPrompted = _completionPrompted,
                CompletionPending = _completionPending,
                SavedPaths = new List<string>(_savedPaths)
            };
        }

        /// <summary>
        /// 恢复同日进度。只接受结构版本一致、日期为今天、轮次非空、
        /// 设备清单无重复、对勾均为清单内设备的快照；任何校验失败都返回 false
        /// 并保持当前状态不变（坏数据/旧日期快照不会污染状态，也不会被当成当日完成）。
        /// 首次没有文件的情况由存储层返回空快照表示，不需要调用本方法。
        /// </summary>
        public bool Restore(InspectionProgressSnapshot snapshot, out string error)
        {
            error = null;
            if (snapshot == null)
            {
                error = "进度快照为空，未恢复。";
                return false;
            }
            if (snapshot.SchemaVersion != SnapshotSchemaVersion)
            {
                error = "进度快照版本不受支持（" + snapshot.SchemaVersion + "），未恢复。";
                return false;
            }
            if (snapshot.Date == default(DateTime) || snapshot.Date.Date != _clock.Now.Date)
            {
                error = "进度快照不是今天的记录，未恢复为当日进度。";
                return false;
            }
            if (string.IsNullOrWhiteSpace(snapshot.RoundId))
            {
                error = "进度快照缺少轮次标识，未恢复。";
                return false;
            }

            var devices = new List<string>();
            if (snapshot.Devices != null)
            {
                var seenDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string raw in snapshot.Devices)
                {
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        error = "进度快照的设备清单含空行，未恢复。";
                        return false;
                    }
                    string name = raw.Trim();
                    if (!seenDevices.Add(name))
                    {
                        error = "进度快照的设备清单重复，未恢复。";
                        return false;
                    }
                    devices.Add(name);
                }
            }

            var deviceSet = new HashSet<string>(devices, StringComparer.OrdinalIgnoreCase);
            var visited = new List<string>();
            if (snapshot.VisitedDevices != null)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string name in snapshot.VisitedDevices)
                {
                    string trimmed = name == null ? null : name.Trim();
                    if (string.IsNullOrEmpty(trimmed))
                    {
                        error = "进度快照的勾选列表含空设备名，未恢复。";
                        return false;
                    }
                    if (!deviceSet.Contains(trimmed))
                    {
                        error = "进度快照勾选了不在清单内的设备「" + trimmed + "」，未恢复。";
                        return false;
                    }
                    if (!seen.Add(trimmed))
                    {
                        error = "进度快照的勾选列表重复，未恢复。";
                        return false;
                    }
                    visited.Add(trimmed);
                }
            }

            var savedPaths = new List<string>();
            if (snapshot.SavedPaths != null)
            {
                var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string path in snapshot.SavedPaths)
                {
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        error = "进度快照的保存路径含空值，未恢复。";
                        return false;
                    }
                    if (seenPaths.Add(path)) savedPaths.Add(path);
                }
            }

            // 校验全部通过后才提交，避免半途失败留下混合状态。
            _date = snapshot.Date.Date;
            _roundId = snapshot.RoundId;
            _devices.Clear();
            _devices.AddRange(devices);
            _visited.Clear();
            foreach (string name in visited) _visited.Add(name);
            _savedPaths.Clear();
            _savedPaths.AddRange(savedPaths);
            _completionPrompted = snapshot.CompletionPrompted;
            _completionPending = snapshot.CompletionPending;
            return true;
        }

        private bool IsCurrentToken(InspectionPlanToken token)
        {
            if (token == null) return false;
            if (token.Date != _date) return false;
            return string.Equals(token.RoundId, _roundId, StringComparison.Ordinal);
        }

        private int IndexOfDevice(string device)
        {
            if (string.IsNullOrWhiteSpace(device)) return -1;
            string trimmed = device.Trim();
            for (int i = 0; i < _devices.Count; i++)
            {
                if (string.Equals(_devices[i], trimmed, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        private void StartNewRound(DateTime date, string roundId)
        {
            _date = date.Date;
            _roundId = roundId;
            _visited.Clear();
            _savedPaths.Clear();
            _completionPrompted = false;
            _completionPending = false;
        }

        private static string NewRoundId()
        {
            return Guid.NewGuid().ToString("N");
        }

        private static List<string> NormalizeDevices(IEnumerable<string> devices)
        {
            var result = new List<string>();
            if (devices == null) return result;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in devices)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string name = raw.Trim();
                if (seen.Add(name)) result.Add(name);
            }
            return result;
        }
    }
}
