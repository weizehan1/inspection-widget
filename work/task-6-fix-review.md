# Task 6 修复轮范围复审（d5e1b91..fcb8f98）

复审范围：仅原审查 Important 1 条 + Minor 1–3，不扩大。核法：修复 diff 与现行源码逐行看控制流 + 独立复跑 `bash build.sh`。

## 逐项结论

### 1. Important：TryWriteOnce 写入中途失败回滚清空 —— ✅ 通过

逐行核 `WindowsClipboardGateway.TryWriteOnce` 控制流：

- `OpenClipboard` 失败在进入 `try` 之前 `return null`，未动剪贴板、不触发回滚（符合本轮口径：Open 阶段失败不误触发回滚亦可接受）；第一次 `EmptyClipboard()` 失败同样在回滚区外 `return null`——清空未成功、本函数未改动剪贴板，无需回滚。
- 第一次 `EmptyClipboard()` 成功后进入内层 `try`，其后**全部**失败路径（写正文 `SetUnicodeText` 返回 false、令牌格式 `RegisterClipboardFormat` 返回 0、写令牌返回 false、两个 `SetDwordZero` 返回 false，共 5 条）均先调 `RollbackLocked()` 再 `return null`；步骤抛异常的路径由内层 `catch (Exception)` 捕获后 `RollbackLocked(); throw;` 原样上抛。两类路径都发生在同一次 Open 临界区内：回滚调用点位于外层 `try` 内，`CloseClipboard` 在 `finally` 中，回滚必在 Close 之前执行。Empty 成功与内层 `try` 之间无其他语句，不存在绕过回滚的缝隙。
- `RollbackLocked()` 本体只再调一次 `EmptyClipboard()`，返回值不查、内部 `catch (Exception)` 吞掉自身异常，回滚失败不改变原始失败语义：返回 false 的分支仍返回 null，上层 `WriteCredential` 按 50ms×3 重试、每次尝试同样先 Empty 且失败同样回滚，最终抛 `InvalidOperationException`——「复制失败 ⇒ 剪贴板无凭据残留」且最终异常口径与接口约定一致。异常分支回滚后以裸 `throw;` 保留原始异常（不被回滚失败或包装掩盖），符合「不掩盖原始失败」。
- 真实剪贴板时序在 Linux 不可测，本项为控制流层面的核实；实机注入验证仍按原报告遗留清单执行。

### 2. Minor-2：ClipboardGuard.Copy 写前 disposed 校验 —— ✅ 通过

`Copy` 进门先 `lock (_sync)` 查 `_disposed`，已释放直接抛 `ObjectDisposedException`，校验点位于 `_gateway.WriteCredential(text)` 调用**之前**——已 Dispose 状态下调用 Copy 时网关零写入，明文绝不进剪贴板。写后锁内原有的 `_disposed` 复查保留（写入期间被释放时不滚动回执，修复说明已如实披露该竞态口径，未冒充已消除）。锚点用例断言 `WriteCount=0` 且假网关内容为 null，与代码一致。

### 3. Minor-1：ReadState 非原子标注 —— ✅ 通过

接口 `IClipboardGateway.ReadState` 与实现 `WindowsClipboardGateway.ReadState` 两处注释均已标注且与实现如实对应：非原子快照、序列号在 OpenClipboard 之外读、令牌在另一次 Open 内读、两者可能来自不同代剪贴板内容、**生产决策禁用本方法做清空判定**、清空判定只走 `TryClearIfMatches` 同锁核对、本方法仅供测试与状态展示。diff 显示 ReadState 实现行为未改，仅加注释（审查给定的二选一中的注释方案）。

### 4. Minor-3：网关抛异常路径测试锚点 —— ✅ 通过

`FakeClipboardGateway` 新增 `ThrowOnClear` 开关，置 true 时 `TryClearIfMatches` 在计数与核对之前直接抛 `InvalidOperationException`，是真抛而非返回 false。新用例真实断言成立：先 `Copy("abc")` 取得回执，置 `ThrowOnClear=true` 后调 `TryClearNow()`，断言返回 false 且假网关内容 `"abc"` 保留；「不抛」由 `TestRunner.Run` 的 try/catch 语义锚定——被测调用若抛异常该用例即记 FAIL，本次复跑该用例为 PASS，故「网关抛异常 → TryClearNow 返回 false 不抛」已被真实覆盖（Guard 侧 catch 全部异常转 false 的代码亦已复核）。

## 计数对账：确切总数 174

- **独立复跑**：复审人亲跑 `bash build.sh`，退出 0，输出「用例总数 174、通过 174、失败 0」，与协调者亲跑的 174/174 一致。
- **构成对账**：修复前基线 172（原审查独立复跑确证）+ 本轮 diff 新增用例恰 2 条（Minor-3 清空抛异常 1 条、Minor-2 释放守卫 1 条，均在 `tests/ClipboardGuardTests.cs`）= 174；另按源码字面数 `TestRunner.Run` 共 166 行，其中 DeviceNameValidatorTests 非法字符用例是 1 行 foreach、运行时展开为 9 条（9 个非法字符各 1 条），净增 8，166 + 8 = 174，与运行计数严丝合缝。
- **关于「修复方称 173」**：全仓文本 grep 不存在 173 这一数字（唯一命中是无关文件的 git blob 哈希片段）；修复说明（work/task-6-report.md「修复轮」一节）两处均写 174。173 比确切总数少 1，与任何可复现口径都对不上，最接近的成因是只计入新增 2 条中的 1 条（172 + 1 = 173），仅作推测、无文本证据。以运行输出 174 为准。

## 总评

✅ 通过（4/4）。Important 回滚逻辑控制流完整、无绕过路径、不掩盖原始失败；Minor 1–3 均按原审查口径落实且有真实测试锚点；计数确切总数为 **174**，修复方口头数字 173 不成立。
