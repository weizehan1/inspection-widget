# Task 6 执行报告：凭据模块（DPAPI / 剪贴板令牌核对 / 30 秒清空）

## 状态

**完成（逻辑层全过关，真实 DPAPI 与真实剪贴板待 Windows 实机验收）。**

基线 HEAD=330ae4b、基线测试 157/157（开工前亲跑确认）。本任务新增用例 15 条，最终 **172/172 全过、零回归**，`bash build.sh` 退出 0，产物 `build/InspectionWidget.exe` 62,464 字节。

## 交付物

| 文件 | 内容 |
|---|---|
| `src/Credentials/ICredentialProtector.cs` | 保护器抽象：Protect→Base64 密文；Unprotect 失败抛 CryptographicException |
| `src/Credentials/DpapiProtector.cs` | DPAPI CurrentUser、无额外熵、UTF-8；Base64 格式错误先包成 CryptographicException 再抛，统一异常口径 |
| `src/Credentials/IClipboardGateway.cs` | `ClipboardReceipt{Sequence, Token, Sha256}`、`ClipboardState{Sequence, Token}`、网关三方法（同锁语义写入接口注释） |
| `src/Credentials/WindowsClipboardGateway.cs` | 真实网关：专用 STA 线程串行执行（网关级锁）、同锁读写、50ms×3 重试、写入最终失败抛 InvalidOperationException、清空最终失败返回 false |
| `src/Credentials/ClipboardGuard.cs` | 纯决策 ShouldClear（序列号＋令牌同锁核对）＋ Copy 回执滚动 ＋ Threading.Timer 定时 ＋ TryClearNow（到期/退出共用）＋ ClearSkipped 事件 |
| `src/Credentials/CredentialService.cs` | 编排：空密文→NeedsReset；CryptographicException→NeedsReset；其余异常→Failed；成功→Copied |
| `src/Native/NativeMethods.cs` | 新增剪贴板 P/Invoke（OpenClipboard/CloseClipboard/EmptyClipboard/SetClipboardData/GetClipboardData/IsClipboardFormatAvailable/RegisterClipboardFormat/GetClipboardSequenceNumber）与 GlobalAlloc/Free/Lock/Unlock、CF_UNICODETEXT=13、GMEM_MOVEABLE |
| `src/MainForm.cs` | 凭据按钮接真实链路；RebuildClipboardGuard()；FormClosing 兜底；空密文加载标记 |
| `tests/Fakes.cs` | FakeProtector（Base64 直存、非法 Base64 抛 CryptographicException）、FakeClipboardGateway（同锁语义内存模拟＋SimulateExternalCopy＋ThrowOnWrite） |
| `tests/ClipboardGuardTests.cs` | 决策 6 条＋编排 5 条 |
| `tests/CredentialServiceTests.cs` | 服务 4 条 |
| `tests/Program.cs` | 注册两个新测试类 |

## 同锁语义落实（WindowsClipboardGateway，硬性口径）

- WriteCredential：一次 OpenClipboard 内依次 EmptyClipboard → SetClipboardData(CF_UNICODETEXT) 写文本 → 注册并写入令牌格式 `InspectionWidget.CredentialToken`（GUID 字符串）→ 写入禁历史/云同步格式（`CanIncludeInClipboardHistory`、`CanUploadToCloudClipboard`，各一个 DWORD 0）→ CloseClipboard 前在同一临界区读 GetClipboardSequenceNumber 填回执。Sha256 在锁外对原文计算（不依赖剪贴板状态）。
- TryClearIfMatches：一次 OpenClipboard 内读当前序列号与令牌 → 用 `ClipboardGuard.ShouldClear` 核对 → 都相等才 EmptyClipboard。网关内部直接复用纯决策函数，决策与执行口径只有一份。
- 数据块内存：GlobalAlloc(GMEM_MOVEABLE) 标准写法，SetClipboardData 成功后所有权归系统不释放，失败路径自己 GlobalFree。
- 三个操作各经专用 STA 线程同步执行（SetApartmentState(STA)＋Join，异常经 ExceptionDispatchInfo 原样透传），另有网关级锁串行化多调用方，调用方线程无关。

## TDD 证据（先 FAIL 后 PASS）

流程：先写 Fakes＋全部 15 条新用例并注册，再写签名齐全、行为为空/恒错的桩实现（ShouldClear 恒 false、Copy 空转、Service 恒 Failed、DPAPI/网关抛 NotImplementedException），跑构建得 RED；随后逐个换真实实现得 GREEN。

- **RED**（桩实现）：总 172 条，165 过、**7 条 FAIL**，构建退出 1。失败的正是正向行为断言：
  1. 决策：序列号与令牌都同 → ShouldClear=true（期望 True 实际 False）
  2. 编排：Copy 两次后 TryClearNow 只认第二次回执
  3. 编排：状态匹配时 TryClearNow 清空并返回 true
  4. 编排：同文字新复制后 TryClearNow 不误清并触发 ClearSkipped（桩无回执，ClearSkipped 未触发而 FAIL；该用例的内容保留断言在桩下形式通过属预期——桩根本不清）
  5. 服务：正常复制 → Copied（实际 Failed）
  6. 服务：密文损坏 → NeedsReset（实际 Failed）
  7. 服务：EncryptedSecret 为空 → NeedsReset（实际 Failed）
  负向断言（仅序列号变/仅令牌变/同文字新复制的 ShouldClear=false、null 分支、网关 false 不抛、Failed 路径）在桩下形式通过，符合桩的恒假行为，不构成证据弱点：GREEN 阶段同一批用例在真实实现下全过，其中正向用例已证明实现确实会清、会复制。
- **GREEN**（真实实现＋MainForm 接线后）：`bash build.sh` 退出 0，**172/172 全过**（157 既有＋15 新增），既有用例零回归。

## 用例清单（新增 15 条）

ClipboardGuardTests（11 条）：
- 决策：都同→true；仅序列号变→false；仅令牌变→false；**同文字新复制（序列号与令牌都变、Sha256 相同）→false（Review Focus 第 3 条锚定，纯函数层）**；receipt null→false；current null→false。
- 编排：Copy 两次后 TryClearNow 只认第二次回执（断言网关收到的核对回执令牌=第二次令牌）；状态匹配→清空返回 true 且回执作废（再调不触网关）；**同文字新复制后 TryClearNow 不误清、外部内容保留、ClearSkipped 触发（Review Focus 第 3 条，编排层二次锚定）**；网关返回 false 时不抛异常；从未 Copy 时返回 false 且不调网关。

CredentialServiceTests（4 条）：正常复制→Copied 且网关写入文本=原文；密文损坏→NeedsReset 且网关写入次数 0；EncryptedSecret 为空串/null→NeedsReset 且不写网关；网关写入抛 InvalidOperationException→Failed 不外抛。

测试构造说明：Guard 用例以 3600 秒间隔构造定时器避免真实到期干扰断言；定时器真实到期路径未做自动化用例（避免时序 flaky），与退出兜底共用同一 TryClearNow 已被编排用例覆盖，到期触发本身列入实机验收。

## MainForm 接线（简报 E）

- 凭据按钮点击 → `CredentialService.Copy(entry)`：Copied→成功色反馈「已复制」；NeedsReset→置 `entry.NeedsReset=true`（内存标记不落盘）＋重建凭据按钮（显示「（需重设）」错误色）＋错误色反馈「该凭据需重新设置，请到设置中重新填写」；Failed→错误色反馈「复制失败，请重试」。原占位 MessageBox 已移除。
- `RebuildClipboardGuard()`：按 `_config.AutoClearClipboard` 构造 Guard 并重组 CredentialService，旧 Guard 先 Dispose；构造函数初始化时调用一次，设置变更后的重建留待 Task 7 保存事件接入本方法（按简报口径）。
- FormClosing：仍只有一处订阅、一个处理函数（已 grep 核对 `FormClosing += OnFormClosing` 仅 1 处）。处理函数先 `_clipboardGuard.TryClearNow()`（有回执且匹配才清）并 Dispose Guard，再走原有 `_hooks.Stop()/Dispose()` 与位置保存。
- 加载标记：`EnsureConfigLists()`（构造与 ReloadConfig 共用入口）对 EncryptedSecret 为空的条目置 NeedsReset=true，与设置层口径一致。
- Dispose(bool) 补 Guard 释放（与 FormClosing 双保险，Guard.Dispose 幂等）。

## 语言子集（R6）

全部新增/修改代码仅用 C# 7.2 以内语法（无 switch 表达式、无 ??=、无 7.3 独有构造），xbuild 以 LangVersion=7.2 构建通过即为实证。

## 构建警告说明

CS2002（源文件被指定多次）为 xbuild 通配符存量怪癖：基线 330ae4b 干净 worktree 构建已有 30 条，本次 54 条随文件总数等比增加，非本次引入的新类警告。

## 遗留（Windows 实机验收项）

1. **DPAPI 跨机/跨用户**：密文拷到另一台机器或另一 Windows 用户下 Unprotect 应抛 CryptographicException → UI 走 NeedsReset 提示，需实机验证；同机同用户加解密往返亦待实机。
2. **真实剪贴板时序**：STA 线程写/读/清的实际行为、GetClipboardSequenceNumber 与回执的对应关系、令牌格式与禁历史/云同步格式在真实剪贴板（含 Windows 剪贴板历史 Win+V、云剪贴板同步）中的效果，待实机逐项验。
3. **30 秒定时到期**：真实 Timer 到期自动清空、以及到期前从别处复制同文字内容不误清的端到端时序，待实机验。
4. **剪贴板占用重试**：被其他进程占用时 50ms×3 重试后写入抛「复制失败」/清空返回 false 的真实表现，待实机注入验证。
5. **退出兜底**：复制后立即关窗，剪贴板被清（匹配时）/保留（已被外部新复制替换时）的实机确认。

## 提交

- 提交信息：`feat(stage-6): 凭据模块（DPAPI/剪贴板令牌核对/30秒清空）`
- 提交哈希：见本报告随附的 git log 核对（提交后由协调者以 `git log --oneline` 为准；本文件随同一提交入库，故不在正文内自填哈希）。
- 未加 remote、未 push；未动 PROGRESS.md 与 docs/。

---

## 修复轮（审查 Important 1 + Minor 1–3）

审查结论：规格 ✅、质量不通过（Important 1 / Minor 4；Minor 4 为表达层面提醒、本轮不动）。本轮修 Important 与 Minor 1–3，Minor-2 附带补了锚点用例，共新增用例 2 条，**174/174 全过（172 + 2），`bash build.sh` 退出 0、零错误**，提交信息 `fix(stage-6): 写入中途失败回滚清空与 Guard 释放守卫`。

### Important：TryWriteOnce 写入中途失败回滚清空（`src/Credentials/WindowsClipboardGateway.cs`）

真实剪贴板路径在 Linux 不可测，只保证编译（构建以 LangVersion=7.2 通过）；回滚逻辑全部写在 `TryWriteOnce` 的结构内，控制流逐行如下：

1. `OpenClipboard` 失败 → 直接 `return null`：此时尚未动剪贴板，**不需回滚**（审查口径一致），上层按占用重试。
2. `OpenClipboard` 成功进入 `try`，`finally` 保证 `CloseClipboard`——下面所有路径都在这一次 Open 临界区内、Close 之前完成。
3. 第一次 `EmptyClipboard()` 失败 → `return null`：清空本身没成功，剪贴板内容未被本函数改动，不进入回滚区。
4. 第一次 `EmptyClipboard()` 成功后，进入内层 `try`：自此剪贴板已被清空、明文将逐步写入，**每条失败路径都先调 `RollbackLocked()` 再退出**：
   - `SetUnicodeText(CF_UNICODETEXT, text)` 返回 false（明文可能已写入）→ `RollbackLocked(); return null;`
   - `RegisterClipboardFormat(令牌格式)` 返回 0 → `RollbackLocked(); return null;`
   - `SetUnicodeText(令牌格式, token)` 返回 false → `RollbackLocked(); return null;`
   - `SetDwordZero(CanIncludeInClipboardHistory)` 返回 false → `RollbackLocked(); return null;`
   - `SetDwordZero(CanUploadToCloudClipboard)` 返回 false → `RollbackLocked(); return null;`
   - 内层 `catch (Exception)`：任一步骤抛异常（而非返回 false）→ `RollbackLocked(); throw;` 原样上抛，不吞原始异常。
5. 全部步骤成功 → 同临界区内读序列号、返回回执，不回滚（正常路径）。
6. `RollbackLocked()` 本身：只做一件事——在当前仍持有的 Open 临界区内再调一次 `EmptyClipboard()`；返回值不查、内部 `catch (Exception)` 吞掉自身异常，**回滚失败绝不改变原始失败语义**：返回-false 分支仍返回 null（上层 50ms×3 重试、最终由 `WriteCredential` 抛 `InvalidOperationException`，UI 报「复制失败」），异常分支仍抛原异常。

由此「复制失败 ⇒ 剪贴板无凭据残留」成立：最后一次尝试即便在中途步骤失败，退出临界区前剪贴板也已被回滚清空，不再留下无令牌、无禁历史/云同步格式、无 Guard 回执的明文。

### Minor-2：ClipboardGuard.Copy 释放守卫（`src/Credentials/ClipboardGuard.cs`）

- 选型：**抛 `ObjectDisposedException`**（不静默返回），口径与既有 `HookManager.Start` 的 disposed 守卫一致——调用已释放对象是编程错误，应显形而非假装成功。
- 位置：`Copy` 进门先 `lock (_sync)` 查 `_disposed`，在调 `_gateway.WriteCredential(text)` **之前**拦下；已释放则网关零写入，杜绝「明文写进剪贴板却不存回执、不设定时器、无人负责清空」。
- 影响面：生产链路经 `CredentialService.Copy` 调用，其 `catch (Exception)` 会把该异常转成 `Failed`（UI「复制失败，请重试」），不穿透界面；MainForm 现路径本就触发不到，属防御补强。
- 写后锁内原有的 `_disposed` 检查保留不变（写入期间被释放的竞态仍按原口径不滚动回执）。
- 锚点用例新增 1 条：已 Dispose 的 Guard 调 Copy → 抛 ObjectDisposedException、网关 `WriteCount=0`、假网关内容为 null。

### Minor-1：ReadState 非原子快照标注（`IClipboardGateway.cs` 接口注释 + `WindowsClipboardGateway.ReadState` 实现注释）

两处均已标注：**非原子快照——序列号与令牌非同一次 Open 内读取（实现中序列号在 Open 之外读、令牌在另一次 Open 内读），两者可能来自不同代剪贴板内容；生产决策不得使用本方法做清空判定，清空判定只走 `TryClearIfMatches` 的同锁核对（`TryClearOnce` 一次 Open 内读齐再核对）**；本方法仅供测试与状态展示。实现行为未改（按审查「注释标注或改为单次 Open」的二选一取注释标注，零行为风险）。

### Minor-3：TryClearNow 网关抛异常路径测试锚点（`tests/Fakes.cs` + `tests/ClipboardGuardTests.cs`）

- `FakeClipboardGateway` 新增 `ThrowOnClear` 开关：置 true 时 `TryClearIfMatches` 抛 `InvalidOperationException`（与既有 `ThrowOnWrite` 对称）。
- 新增用例 1 条：「编排：网关清空抛异常时 TryClearNow 返回 false 且不抛异常」——Copy 后置 `ThrowOnClear=true`，断言 `TryClearNow()` 返回 false、用例本身不抛（抛即 FAIL）、假网关内容保留未被误清。

### 本轮计数

新增用例 2 条（Minor-3 清空抛异常 1 条 + Minor-2 释放守卫 1 条），172 + 2 = **174，亲跑 `bash build.sh` 退出 0、174/174 全过、零回归**。Important 的真实剪贴板回滚时序无法在 Linux 验证，随原报告「遗留」清单一并留待 Windows 实机验收（建议验收项追加：写入中途注入失败时剪贴板无明文残留）。
