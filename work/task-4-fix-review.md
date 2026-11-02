# Task 4 修复轮范围复审：Minor-1 / Minor-3

审查范围：仅核对 `cdfa92e..6d0db85` 中 `src/Capture/WindowCapture.cs` 与 `tests/BlackFrameDetectorTests.cs` 两处修复（对照 `work/task-4-review.md` 第二节定义与 `work/task-4-report.md`「修复轮」一节）；未扩大审查、未改代码。测试计数采信协调者亲跑结果 147/147，未重跑。

## 1. Minor-1（裁边 Clone 异常路径原图释放）：✅

- 释放保证成立：裁边分支（`TryPrintWindow`，`WindowCapture.cs:191-200`）已改为 `Bitmap cropped = null; try { cropped = bmp.Clone(crop.Crop, bmp.PixelFormat); } finally { bmp.Dispose(); } result = cropped;`。Clone 抛异常时 finally 释放原图 bmp、异常继续上抛（`result = cropped` 不执行，不会带空图继续）；Clone 成功时 finally 同样释放原图、随后改用裁剪图。
- 未误伤超时迟到回收路径：该裁边分支位于 `thread.Join(3000)` 成功、或超时相撞时锁内确认 `attempt.Finished == true` 之后，且已过 `!attempt.Ok` 分支；`Abandoned` 仅在锁内 `!finishedLate` 时置位，置位后函数随即以 `null` / Failed 返回，根本到不了裁边处。故到达裁边时 bmp 所有权必在本方法、未登记 `LateAttempts`；diff 也未触及迟到回收表（`LateLock` / `LateAttempts` / 迟到线程锁内自释放）任何代码，迟到位图不会在此被提前释放。
- 常态行为不变：Clone 的入参（`crop.Crop`、`bmp.PixelFormat`）、触发条件（`crop.Valid` 且裁剪区与整幅不同）、成功后「释放原图 + `result` 改指裁剪图」的净效果与修复前一致，仅多了一层异常兜底。

## 2. Minor-3（黑图最紧界值 221 用例）：✅

- 真按采样网格构造，非整图比例估算：新增用例「边界 221 黑 4 白（98.22% > 98%）→ 可疑」（`BlackFrameDetectorTests.cs:93-103`）复用测试内 `SamplePoints(Size, Size)`，其公式 `(i + 0.5) * 尺寸 / 15.0` 取整并夹末像素，与实现 `BlackFrameDetector` 的采样坐标逐式同口径；先以 `FilledBitmap(Color.Black)` 全图填黑（采样点均为纯黑、计入可疑），断言采样点总数为 225，再仅在前 4 个采样坐标精准涂白。100×100 下前 4 点为首行 (3,3)、(10,3)、(16,3)、(23,3)，互不重合，恰好减掉 4 个可疑点，剩余可疑采样点恰为 221 个。
- 断言与界值正确：用例断言 `IsSuspect(bmp) == true`；221/225 = 98.22%，恰在阈值上方 1 个采样点，与相邻既有 220 黑 → false 用例形成最紧夹界，锁死阈值。
- 阈值常量未动：本轮 diff 不含 `BlackFrameDetector.cs`；实现仍为 `SuspectRatio = 0.98` 且判据为严格 `suspect / (double)total > SuspectRatio`，无放宽或收紧。

## 总评

✅ 通过。未过项数：0。Minor-1 的 try/finally 精准落在本地所有权分支且与迟到回收路径严格隔离，Minor-3 新增用例以实现同口径采样坐标真实构造 221/225 并断言可疑、阈值未改；两项修复均与审查定义和修复说明一致，不阻塞关账。
