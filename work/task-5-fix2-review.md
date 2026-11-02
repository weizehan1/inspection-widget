# Task 5 微修范围复审：M-1 残余（竞态变体 B）

复审范围：仅 M-1 残余一项，对象为 diff `e6b04ae..145ca3a` 的 `src/MainForm.cs`（另含报告文档追加，非代码）。未改任何生产代码；测试计数采用协调者亲跑结果 157/157，本轮未重跑。

## 代次机制核对

- 机制成立：新增 `_pendingGeneration` 单调递增（全文无重置/递减）；`BeginSave` 发起时在 UI 线程捕获 `saveGeneration` 并随回调带回，`OnSaveCompleted` 失败分支重登记前先比对 `saveGeneration != _pendingGeneration`，任一代次不符或 `IsImageUsable` 不通过即丢弃，不重登记。正常路径无误伤：新截图保存前待重试图已在 `OnTargetResolved` 清除、代次静止，重试保存捕获的正是重登记后递增的代次，失败回调比对一致可正常再登记。

## 逐一核过的 Dispose/替换/清除点位（均伴随代次递增）

1. 窗体 `Dispose(bool)`：`_pendingImage.Dispose()` 后递增（字段未置空，但随后失败丢弃分支按同引用清除，见下）。
2. `OnTargetResolved` 新截图清旧图：Dispose 后递增，再置空 `_pendingImage`/`_pendingRequest`。
3. `OnSaveCompleted` 成功分支：同引用时先置空登记并递增，再 `image.Dispose()`——释放发生在递增之后，无「释放后代次未变」窗口。
4. `OnSaveCompleted` 失败丢弃分支：同引用时置空登记并递增。
5. `OnSaveCompleted` 失败重登记分支替换异引用旧图：Dispose 旧图后递增；随后登记新图再递增。
6. 另核非登记图释放点，均不构成重登记向量：`BeginSave` 工作线程在窗体已释放/BeginInvoke 失败时 `image.Dispose()` 后直接返回、永不回调 `OnSaveCompleted`；`OnCaptureCompleted`/截图工作线程释放的 `outcome.Image` 此时尚未登记为 `_pendingImage`。全文 `_pendingImage` 的赋值点仅上述各处，无遗漏的无递增赋值/释放路径。

## 迟到结果丢弃后的状态

- `_saveInFlight` 已在入口复位；同引用时登记与请求一并清除（反馈行重试入口随之失效，不会对已释放图再重试）；异引用时说明已有更新的登记，丢弃分支不动它、保留其待重试状态。丢弃分支不二次 Dispose 该图（其释放已由触发代次变化的那条路径完成），无双重释放。

## 其他行为

- 改动仅限待保存图代次字段、`BeginSave`/`OnSaveCompleted` 私有签名与上述递增/比对；成功、失败提示文案与既有 M-1 失效口径不变，保存器、截图、状态机均未触及。

## 结论

✅ 通过：一句依据——每次 Dispose/替换/清除 `_pendingImage` 均递增代次、失败重登记前强制比对代次，图已释放或登记已被替换的迟到结果必被丢弃且丢弃后登记状态干净，正常保存/重试路径代次比对一致不受影响。
