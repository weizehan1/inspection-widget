# P2：每日巡检进度与完成通知

你是用户指定的 PI DeepSeek 执行者。仅在本 worktree 新建 `src/Planning/`、`tests/InspectionPlanTests.cs`、`tests/InspectionProgressStoreTests.cs`，以及自己的报告 `work/v110/deepseek-planning-report.md`。不修改 AppConfig/SettingsForm/MainForm/tests/Program.cs，不提交/推送、不另派 agent、不安装依赖。其他任务并行，不要覆盖其文件。构建由主负责人统一安排，避免同时构建共享 bin/obj。

设计以纯状态为核心：对勾=点击已被截图状态机接受，而非截图已保存。UI 只有被接受时才调用 MarkVisited；取消/失败不撤销。按系统本地日期新轮，同日重启恢复，模式开关只隐藏状态，不清除。设备列表由调用者传入，不引用其他 agent 正在修改的 AppConfig。

## 固定入口与建议接口
namespace `InspectionWidget.Planning`，类型 `InspectionPlan`、`InspectionPlanToken`、`InspectionProgressSnapshot`、`InspectionProgressStore`。使用现有 `InspectionWidget.Storage.IClock` 注入时钟；无 UI/真实截图/外部程序依赖。

`InspectionPlan`：构造 `(IClock clock, IEnumerable<string> devices)`；公开 `DateTime Date`、`string RoundId`、`int VisitedCount`、`int TotalCount`、`bool IsComplete`、`bool CompletionPrompted`。
- `InspectionPlanToken CurrentToken { get; }`：不可变日期/轮次标识，可从旧请求传回。
- `void EnsureCurrentDay()`：日期变化时新轮、清勾及通知状态；新轮 ID 唯一。
- `bool MarkVisited(string device, InspectionPlanToken token)`：只接受当前日期/轮次和清单设备，大小写按现有设备规则 OrdinalIgnoreCase；重复不累计。
- `bool IsVisited(string device)`；`void Unmark(string device)`（不重开该轮已通知资格）；`void ResetRound()`（新 ID、清勾、允许新轮通知）。
- `void ReconcileDevices(IEnumerable<string> devices)`：增项未点、删项移出、排序不改变状态；不得因配置删项自动产生“完成弹窗待触发”。
- `void RenameDevice(string oldName, string newName)`：映射合法设备的勾选，防重名；不把不存在设备标已点过。
- `void ReplaceDevices(IEnumerable<string> devices)`：用于导入替换，明确新轮，禁止继承旧对勾。
- `bool TryClaimCompletionPrompt(bool captureOrSaveBusy)`：只在有效点击使非空清单从未完成到全完成后 pending；忙碌时 false 且不消耗；非忙时返回 true 一次并设置 CompletionPrompted。空名单/删项/排序/恢复满勾但已通知均不重复触发。恢复未显示的 pending 可以在空闲时询问一次。
- `InspectionProgressSnapshot CreateSnapshot()`、`bool Restore(InspectionProgressSnapshot snapshot, out string error)`：恢复同日、合法清单状态和通知/pending/轮次；旧日期不恢复为当日完成，坏数据不能污染状态。

`InspectionProgressStore`：构造 `(string path)`；`bool TryLoad(out InspectionProgressSnapshot snapshot, out string error)`、`bool TrySave(InspectionProgressSnapshot snapshot, out string error)`。
建议 snapshot 包含 schema version、date、round ID、设备与勾选、CompletionPrompted、pending。DataContract JSON、独立文件、UTF-8；同目录临时文件+flush+原子替换（可复用现有 AtomicConfigWriter），清理临时文件；错误可读，不覆盖坏原件后伪报成功。首次文件不存在可以返回空快照/无错误，契约须写清。不要把状态写进 config 或交换文件。
每日进度可保存成功截图路径以支持重启后的真实目录导航；若加入请公开最小接口 `RecordSavedPath(string fullPath, InspectionPlanToken token)` 与 `IEnumerable<string> SavedPaths`，必须拒绝旧轮/旧日回调（或把该扩展清楚留给集成者）。

允许调整内部设计，但公共接口需要变化时先向主负责人说明，不要等报告最后才暴露冲突。

## 行为用例（真实新增，不镜像实现）
取消截图/保存失败不撤销 MarkVisited；重复点击；未知设备；空名单；全勾等待/保存中不弹，空闲只消费一次；复原/重启不重复；未消费 pending 可恢复；删除最后未点设备不弹；新增/改名/排序；替换导入新轮；跨日旧 token/重试回调不计新日；手动重置旧 token 失效；坏 JSON/状态版本/文件路径不可写等错误不伪报成功。
测试按仓库 TestRunner 风格，各测试类 `public static void RunAll()`；不改 tests/Program.cs，主负责人登记后统一执行。先写测试，再实现；环境不可用时如实记录未运行，不把源码阅读当通过。

交付报告：所有文件、真实接口、用例、存储契约、构建/运行情况和待集成事项。
