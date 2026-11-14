# P2 交付报告：每日巡检进度与完成通知

执行者：PI DeepSeek（P2）
范围：仅新增 `src/Planning/`、`tests/InspectionPlanTests.cs`、`tests/InspectionProgressStoreTests.cs`，本报告。
未修改 AppConfig / SettingsForm / MainForm / tests/Program.cs，未提交、未推送、未安装依赖。

## 1. 新增文件

| 文件 | 内容 |
| --- | --- |
| `src/Planning/InspectionPlanToken.cs` | 不可变日期 + 轮次 ID 标识，值相等语义（Ordinal） |
| `src/Planning/InspectionProgressSnapshot.cs` | `[DataContract]` 快照，即磁盘 JSON 契约 |
| `src/Planning/InspectionPlan.cs` | 纯状态核心：进度、轮次、完成通知、设备对账、恢复 |
| `src/Planning/InspectionProgressStore.cs` | 独立进度文件读写（DataContract JSON + 原子替换） |
| `tests/InspectionPlanTests.cs` | 22 条行为用例 |
| `tests/InspectionProgressStoreTests.cs` | 11 条存储契约用例 |

两个工程都用 `<Compile Include="**\*.cs" />` 通配，新文件自动进入编译，无需改 csproj。

## 2. 真实公共接口（namespace `InspectionWidget.Planning`）

```csharp
public sealed class InspectionPlanToken
{
    public InspectionPlanToken(DateTime date, string roundId);
    public DateTime Date { get; }
    public string RoundId { get; }
    // IEquatable<InspectionPlanToken>，ToString() => "yyyy-MM-dd/roundId"
}

public sealed class InspectionPlan
{
    public const int SnapshotSchemaVersion = 1;

    public InspectionPlan(IClock clock, IEnumerable<string> devices);

    public DateTime Date { get; }
    public string RoundId { get; }
    public IReadOnlyList<string> Devices { get; }   // 只读视图，配置顺序
    public int TotalCount { get; }
    public int VisitedCount { get; }
    public bool IsComplete { get; }
    public bool CompletionPrompted { get; }
    public InspectionPlanToken CurrentToken { get; }
    public IEnumerable<string> SavedPaths { get; }

    public void EnsureCurrentDay();
    public bool MarkVisited(string device, InspectionPlanToken token);
    public bool IsVisited(string device);
    public void Unmark(string device);
    public void ResetRound();
    public void ReconcileDevices(IEnumerable<string> devices);
    public void RenameDevice(string oldName, string newName);
    public void ReplaceDevices(IEnumerable<string> devices);
    public bool TryClaimCompletionPrompt(bool captureOrSaveBusy);
    public bool RecordSavedPath(string fullPath, InspectionPlanToken token);

    public InspectionProgressSnapshot CreateSnapshot();
    public bool Restore(InspectionProgressSnapshot snapshot, out string error);
}

public sealed class InspectionProgressSnapshot   // [DataContract]
{
    public int SchemaVersion { get; set; }
    public DateTime Date { get; set; }
    public string RoundId { get; set; }
    public List<string> Devices { get; set; }
    public List<string> VisitedDevices { get; set; }
    public bool CompletionPrompted { get; set; }
    public bool CompletionPending { get; set; }
    public List<string> SavedPaths { get; set; }
}

public sealed class InspectionProgressStore
{
    public const long MaxFileBytes = 1L * 1024L * 1024L;
    public InspectionProgressStore(string path);   // 空路径抛 ArgumentException
    internal InspectionProgressStore(string path, AtomicConfigWriter writer); // 测试注入同一目标原子写
    public string Path { get; }
    public bool TryLoad(out InspectionProgressSnapshot snapshot, out string error);
    public bool TrySave(InspectionProgressSnapshot snapshot, out string error);
}
```

与 brief 的差异只有一处：新增 `Devices` 只读属性（brief 未列，属加法扩展），供 UI 渲染清单与测试核对删除/排序；其余签名与 brief 一致。`RenameDevice` 保持 `void`，按规则静默拒绝。

## 3. 语义实现要点

- 对勾 = 点击被截图状态机接受。只有 `MarkVisited` 返回 true 才计数；取消截图/保存失败时 UI 不调用任何入口，进度不回退。`Unmark` 仅用于用户主动取消勾选。
- 设备匹配统一 `OrdinalIgnoreCase`，写回时保留配置原始写法；构造、对账、替换都会去空白、忽略空名、按 `OrdinalIgnoreCase` 去重且保持传入顺序。
- 轮次：`(本地日期, Guid("N"))`。`EnsureCurrentDay()` 同日无操作；跨日新 ID、清空对勾/保存路径/通知状态，设备清单保留。`ResetRound()`、`ReplaceDevices()` 同样开新轮；`ReplaceDevices()` 额外替换清单且绝不继承旧对勾。
- 旧回调拒绝：`MarkVisited`/`RecordSavedPath` 都要求 token 日期与轮次 ID 同时等于当前值，因此跨日迟到回调、重置前回调、导入替换前回调全部返回 false。
- 完成通知：`MarkVisited` 里只有“本轮此前未完成 → 现在完成”的转换且该轮尚未通知才置 pending。`TryClaimCompletionPrompt(busy)`：busy 时 false 且不消耗；空闲且 pending、未通知、确实完成时消费一次并置 `CompletionPrompted`。
- 不触发的情况：空清单（`IsComplete` 定义为清单非空且全勾）、`ReconcileDevices` 删掉最后未点设备、仅排序、恢复时已满勾且已通知。
- `Unmark` 会清掉尚未消费的 pending（完成后又被撤勾时旧询问已失效），但保留 `CompletionPrompted`；重新点满时若本轮未通知过会重新获得一次资格。
- `ReconcileDevices`：增项未点、删项连同对勾移除、排序不改变勾选状态、对账本身永不产生 pending；若结构性增删使“已完成”不再成立，尚未消费的 pending 一并作废（纯排序仍为完成，合法 pending 保留）。
- `RenameDevice`：保留勾选；目标名已被其他设备占用（忽略大小写）则不改名；旧名不存在或新名为空则不产生设备、不标勾。

## 4. 存储契约

- 位置由调用者决定（建议 `%LocalAppData%\InspectionWidget\progress.json`），只写独立文件，不写 config、不写交换文件。
- 格式：DataContract JSON，UTF-8 无 BOM，字段名同 `[DataMember(Name=...)]`；`date` 为 DataContract 的 `\/Date(...)\/` 表示，`Restore` 只比较日期部分。
- 写入：复用 `AtomicConfigWriter`（同目录 `.<name>.<guid>.tmp` → `Flush(true)` → `File.Replace`/`File.Move`），失败清理临时文件并返回可读错误，绝不覆盖坏原件后伪报成功。
- 写入大小：序列化后超过 1 MiB 直接拒绝保存（错误可读），避免写出自己读不回的文件；失败不覆盖旧文件。
- 读取：
  - 文件不存在 → `TryLoad` 返回 true，`snapshot == null`，`error == null`（首次启动不是错误）。
  - 空文件、超过 1 MiB、JSON 无法解析、`null` 顶层 → 返回 false 与可读错误，`snapshot == null`，不抛异常。
  - 字段缺失的合法 JSON 会如实取回（缺 `date`/`roundId` 的默认值），由 `Restore` 判定为坏数据拒绝。
- `Restore` 校验（任一失败返回 false、错误可读、当前状态完全不变）：非空、`SchemaVersion == 1`、日期为今天、`RoundId` 非空白、设备清单无空行/无空白名/无重复、勾选均为清单内设备且不重复、保存路径无空值。空白设备行明确拒绝，不会被 `Normalize` 悄悄吞掉。旧日期快照不会被恢复成“当日完成”，需要 `EnsureCurrentDay()` 后自然开新轮。

## 5. 行为用例

`InspectionPlanTests`（22 条）：新轮初始状态；有效点击与重复点击；未知设备/空名/空 token；`OrdinalIgnoreCase` 匹配；取消截图不撤销；完成待询问 + 忙碌不消耗 + 空闲一次；空名单不弹；删项不弹 / 已通知后加项不重复弹；对账清失效 pending + 纯排序保留 pending；对账增删排序；改名保留勾选/防重名/未知设备；替换开新轮不继承；同日 `EnsureCurrentDay` 幂等与跨日清勾、旧 token 失效；`ResetRound` 后可再次通知；`Unmark` 不重开资格；快照恢复（勾选/pending/轮次/路径）；旧日期快照拒绝且不污染；坏快照（版本、空轮次、未知勾选、重复设备、空白设备行、null 设备行、重复勾选、空日期）拒绝且状态不变；pending 恢复后空闲可询问一次；满勾已通知恢复不重弹；保存路径只收当前轮并去重；跨日保存路径回调被拒。

`InspectionProgressStoreTests`（11 条）：首次缺失；字段完整往返（中文设备名/路径）；覆盖保存原子且无 `.tmp` 残留；坏 JSON/空文件/`null`；缺字段如实取回；不可写路径失败且无目标文件；保存到被占路径失败不影响其他进度文件；同一目标替换抛错时原字节不变、返回 false、无 `.tmp`（`IFileOperations`+`ForTests` 注入）；序列化超过 1 MiB 拒绝且不覆盖旧文件；空参数；落盘 UTF-8 无 BOM 并可被 `Restore` 接受。

## 6. 构建与运行状态

- 未运行仓库共享 `bin/obj` 构建（按 brief 由主负责人统一安排），也未修改 `tests/Program.cs`，因此这两个测试类还没有进入正式测试入口，需要在 `Program.Main` 中登记：
  `InspectionPlanTests.RunAll();`、`InspectionProgressStoreTests.RunAll();`
- 已做的独立验证：在 `/tmp/p2-verify` 用本机 Mono `csc` 只编译 `src/Storage/IClock.cs`、`src/Config/AtomicConfigWriter.cs`、`src/Planning/*.cs`、`tests/TestFramework.cs` 与两个新测试文件（另加临时 `FakeClock`/`Main` shim），运行结果 **33/33 通过，退出码 0**。这是隔离验证，不等于仓库解决方案构建通过；完整 `build.sh` 仍待主负责人执行。
- 未在真实 Windows/UI 中联调（P2 无 UI 依赖），截图状态机接线由 P3 完成。

## 7. 待集成事项（给 P3 / 主负责人）

1. `tests/Program.cs` 登记两个 `RunAll()`。
2. 启动：`plan.EnsureCurrentDay()` → `store.TryLoad`（`snapshot != null` 时 `Restore`）；`Restore` 失败只记日志并继续使用当前新轮，不删除进度文件。恢复成功后必须立即以当前配置调用 `ReconcileDevices`（必要时再处理改名），不能把快照里的设备清单当成最新配置。
3. 点击路径：点击开始时取 `token = plan.CurrentToken`；截图状态机接受后再 `plan.MarkVisited(device, token)`，成功保存后 `plan.RecordSavedPath(fullPath, token)`，随后 `store.TrySave(plan.CreateSnapshot())`。
4. 空闲/状态机回到 Idle 时调用 `plan.TryClaimCompletionPrompt(captureOrSaveBusy)`，返回 true 才弹窗；忙碌期不消耗。
5. 配置应用：设备增删排序用 `ReconcileDevices`，改名用 `RenameDevice`，导入替换用 `ReplaceDevices`；这些调用后都要保存快照。
6. 进度文件路径需与主负责人确认，并纳入卸载/清理与错误日志策略。
