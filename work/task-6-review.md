# Task 6 审查报告：凭据模块（涉密模块从严审查）

审查范围：330ae4b..d5e1b91（简报、执行报告、审查包三方对照＋关键代码逐行核＋独立复跑）。

## 一、规格结论：✅ 通过

逐条核对简报 A–E，全部落实：

- **DpapiProtector（A）✅**：`ProtectedData.Protect/Unprotect` 均传 `null` 熵、`DataProtectionScope.CurrentUser`；明文 UTF-8 编码、密文 Base64；空密文先抛 `CryptographicException`，Base64 格式错误捕 `FormatException` 后包成 `CryptographicException` 再抛，DPAPI 本体失败亦为该异常，异常口径统一。
- **WindowsClipboardGateway（B）✅（同锁为硬性项，重点核）**：`TryWriteOnce` 在**一次** `OpenClipboard` 内依次 EmptyClipboard → 写 CF_UNICODETEXT(13) 文本 → 注册并写入令牌格式 `InspectionWidget.CredentialToken`（GUID 字符串）→ 写 `CanIncludeInClipboardHistory`、`CanUploadToCloudClipboard` 两个 DWORD 0 格式 → 在 `CloseClipboard` 前、同一临界区内读 `GetClipboardSequenceNumber` 填回执；**不存在**「SetText 后释放锁再读序列号」的拆锁写法。Sha256 在锁外对原文计算（简报允许）。`TryClearOnce` 同样在一次 Open 内读序列号＋令牌、经 `ShouldClear` 核对、一致才 EmptyClipboard。占用重试为初试＋50ms×3 次重试：写入最终失败抛 `InvalidOperationException`（不假装成功），清空最终失败返回 false。`SetClipboardData` 内存所有权正确：`GlobalAlloc(GMEM_MOVEABLE)` 分配，成功后所有权转移给系统**不释放**，失败路径（Set 返回 Zero、GlobalLock 失败）均 `GlobalFree` 自行释放。STA 封装不死锁：每次操作新建 STA 线程同步 Join，被调函数为静态方法、不回抢网关锁、不依赖调用方线程泵消息；异常经 `ExceptionDispatchInfo` 原样透传。
- **ClipboardGuard（C）✅**：`ShouldClear` 纯函数口径准确——receipt/current 任一 null→false，序列号相等**且**令牌 Ordinal 相等→true，哈希不参与判定。`Copy` 网关写入成功后才滚动回执并 `RestartTimerLocked`（内部先 Dispose 旧 Timer），只认最后一次回执（有 ReferenceEquals 防护，不误废新回执）。`TryClearNow` 网关异常被 catch 吞掉返回 false 并触发 ClearSkipped，不抛异常。
- **CredentialService（D）✅**：空密文→NeedsReset（不调保护器、不写网关）；`CryptographicException`→NeedsReset；其余解密异常与 Guard.Copy 异常→Failed；成功→Copied，三分支与简报一致。
- **明文不落盘/不进反馈（全 diff 搜查）✅**：明文仅以局部变量 `plain` 在内存中由 Unprotect 流向 Guard.Copy；全 diff 无日志、无 Console/Debug 输出、无临时文件写入包含明文；MainForm 三种反馈均为固定文案（「已复制」／「该凭据需重新设置，请到设置中重新填写」／「复制失败，请重试」），不含条目标题外的凭据内容；异常消息均为固定字符串。`NeedsReset` 为 `[IgnoreDataMember]`，运行时标记经窗口位置保存链路也不会落盘（已核 AppConfig 定义）。
- **MainForm 接线（E）✅**：反馈文案与简报逐字一致；NeedsReset 时同步置 `entry.NeedsReset=true` 并 `RebuildCredentialControls()` 刷新按钮（显示「（需重设）」）；`FormClosing` 全文件仅一处订阅、一个处理函数，函数内先 `_clipboardGuard.TryClearNow()` 再 `Dispose()` Guard，最后走原有 hooks/位置保存；`RebuildClipboardGuard()` 存在且构造函数初始化时调用一次（按 `AutoClearClipboard` 构造、旧 Guard 先 Dispose）；加载时 `EnsureConfigLists()` 对空密文条目标 NeedsReset（构造与 ReloadConfig 共用）。
- **语言子集与引用 ✅**：新增/修改代码无 7.3+ 语法（无 switch 表达式、`??=`、using 声明等），且独立复跑的 build.sh 强制 `/p:LangVersion=7.2` 构建通过即为实证；csproj 引用仅 System.* 框架程序集（含 System.Security），零第三方引用，diff 无 packages.config 类变更。

## 二、质量结论：不通过（Critical 0 / Important 1 / Minor 4）

### Important（须修后复审）

1. **写入中途失败无回滚，明文可能无保护残留剪贴板**（`WindowsClipboardGateway.TryWriteOnce`）：EmptyClipboard 成功之后任一步失败（写令牌格式、写禁历史/云同步格式等）只返回 null，上层重试虽会重新 Empty，但**最后一次尝试**若在此类中途步骤失败，函数直接关锁退出并最终抛 `InvalidOperationException`——此时剪贴板里可能留有已写入的明文，而令牌/禁历史/云同步格式未写全、Guard 也未取得回执（无 30 秒清空、无退出兜底），UI 却只报「复制失败」。占用失败发生在 Open 阶段（未动剪贴板）是安全的，危险的正是这条中途失败路径。修法：TryWriteOnce 在 Empty 成功后的失败路径上、CloseClipboard 前做一次 best-effort EmptyClipboard 回滚（仍在同一 Open 临界区内），使「复制失败 ⇒ 剪贴板无凭据」成立。

### Minor

1. **ReadState 非原子快照**：序列号在 OpenClipboard 之外读、令牌在另一次 Open 内读，两者可能来自不同代剪贴板内容。当前生产决策路径不使用 ReadState（Guard 只走 TryClearIfMatches 的同锁核对），仅测试与接口完整性在用，故影响有限；但接口注释未声明此点，未来误用会引入竞态，建议注释标注或改为单次 Open 内读取。
2. **ClipboardGuard.Copy 的 Dispose 顺序缺口**：Copy 先调网关写剪贴板、进锁后才查 `_disposed`，已 Dispose 的 Guard 被调用时会把明文写入剪贴板却不存回执、不设定时器，无人负责清空。MainForm 现路径（重建 Guard 时同步替换 Service）基本触发不到，属防御缺口，建议进锁先查 disposed 或写前校验。
3. **TryClearNow 异常路径测试锚点缺失**：「网关返回 false 不抛」用例以状态不匹配构造，未覆盖网关**抛异常**的路径（Fake 无 ThrowOnClear 开关）；代码审查确认 Guard 已 catch 全部异常转 false，属测试证据缺口而非实现缺陷。另外 ClearSkipped 事件订阅者若抛异常会穿透 TryClearNow（生产代码未订阅该事件，暂无实际影响）。
4. **纯函数层「同文字新复制」用例表达打折**：`ClipboardState` 无哈希字段，纯函数用例所谓「哈希相同」仅是回执侧的注释性构造，实际锚定的仍是序列号＋令牌双变→false；所幸编排层用例以 `SimulateExternalCopy("same-text")` 真正构造了同文本、不同序列号/令牌并断言内容保留＋ClearSkipped 触发，Review Focus 第 3 条总体锚定成立，此条仅为表达层面的提醒。

### 测试真实性核定（通过）

Fake 网关测的是决策与编排而非 Fake 本身：Fake 的 `TryClearIfMatches` 直接复用生产纯函数 `ShouldClear`（与真实网关 TryClearOnce 共用同一份决策），决策六分支另有独立于 Fake 的手造 receipt/state 直测；编排用例断言网关收到的核对回执令牌＝第二次令牌、清空次数、回执作废，测的是 Guard 的滚动与失效逻辑；Service 用例以真实 Guard＋Fake 网关/保护器验证写入次数与注入文本，NeedsReset 路径断言网关写入次数为 0。TDD 的 RED 证据（桩下 7 条正向用例 FAIL、负向断言形式通过）与报告说明自洽。

## 三、计数对账：确切总数 172

- **独立复跑**：审查人亲跑 `bash build.sh`，退出 0，输出「用例总数 172、通过 172、失败 0」；另在干净克隆中复跑基线 330ae4b 为 157/157。故新增恰 15 条（ClipboardGuardTests 11＝决策 6＋编排 5，CredentialServiceTests 4），157＋15＝172，与协调者亲跑的 172/172 一致。
- **关于「报告称 176」**：经全仓 grep（报告正文、PROGRESS.md、work 目录），**不存在 176 这一数字**——执行报告三处均写 172，PROGRESS.md 亦记「协调者亲验、亲跑 172/172」。176 与任何可复现口径都差 4，无文本出处；差额 4 恰等于 CredentialServiceTests 的条数，疑为手工相加时重复计入该 4 条所致，仅作推测，无证据。
- **另一可确证的口径差（164→172，差 8）**：按源码字面数 `TestRunner.Run` 只有 164 行，其中 DeviceNameValidatorTests 的非法字符用例是 1 行 foreach 循环，运行时展开为 9 条（9 个非法字符各 1 条），净增 8，164＋8＝172，与运行计数严丝合缝。以字面行数估总数会少算 8，以运行输出为准。

## 四、总评

规格 ✅；质量不通过，仅因 1 条 Important（写入中途失败无回滚的明文残留路径）——其余同锁核对、DPAPI 口径、明文不落盘、MainForm 接线与测试真实性均经逐行核实无误。修掉该条并补一次复跑后可放行；DPAPI 与真实剪贴板时序仍按报告所列留待 Windows 实机验收。
