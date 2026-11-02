# Task 5 审查报告：保存与命名（保存/顺延/暂存链）

审查对象：基线 `237a52d` .. HEAD，feat 为 `cdf9892`，报告提交 `25d57c3`。
审查方式：读简报 / 执行报告 / 审查包三件套 + 直读 `src/Storage/ScreenshotSaver.cs`、`src/MainForm.cs`、`tests/ScreenshotSaverTests.cs` 源码逐行核 + 独立复跑 `bash build.sh` 对账。未改任何生产代码。

## 结论速览

- **规格：✅**（简报 A–D 逐条落实，无缺项、无偷工；两处简报未明示的附加口径见 Minor-4 与 Minor-2，均不构成规格违背）
- **质量：通过**（Critical 0 / Important 0 / Minor 5）
- **确切测试总数：156**（独立复跑：通过 156、失败 0、`build.sh` 退出 0；对账明细见文末）

## 逐条透镜核查

### 1. 顺延算法与冲突判据 —— ✅（主路径判据比简报口径还硬一层）

- 最小未占用序号：候选按 `n = 0, 1, 2, …` 顺序生成（`FileNameFor`：0 → `设备名.png`，n → `设备名-n.png`），逐个试占，第一个成功即返回，天然取最小未占用号。用例 3 预置 base/-1/-3 → 落 `-2`，PASS。
- 原子占用：`new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None)`，不存在「先 Exists 检查再建文件」的检查-使用竞态。
- 冲突 vs 其他 IOException 的判据（重点核）：`TrySaveToDirectory` 用 `created` 标志区分阶段——标志在 FileStream 构造**成功后**才置真。
  - 构造抛 IOException 且 `!created && File.Exists(candidate)` → 判定重名冲突、下一序号；
  - 构造成功后写入/Flush 阶段抛 IOException（此时文件当然存在，若只看 File.Exists 必误判为冲突）→ 因 `created == true` **不走冲突分支**，删本次文件并判该目录失败进暂存链。这一区分正是「不能靠 File.Exists 事后猜」的正确解法，实现无此误判。
  - 非重名 IOException（如建目录失败、磁盘错误）→ 返回 false 进暂存链，不空转顺延。
- 有界上限：`MaxSequence = 10000`，循环 `n <= 10000`（最大序号 10000，符合「序号上限 10000」），超限以「文件名顺延超过上限」判该目录失败进下一级，不死循环。

### 2. 写入失败只清理本次文件 —— ✅

- 删除动作仅出现在 `created == true` 的分支，且删前先 `CloseQuietly(stream)` 关句柄（Windows 下不关句柄删不掉，这点做对了）；目标仅为本次 CreateNew 建出的 candidate。
- 冲突分支（`created == false`）从不删文件；用例 2 以哨兵字节断言既有 `设备A.png` 逐字节不变，PASS。通用 Exception（GDI+ 编码等）分支同口径。

### 3. 暂存链三级与口径 —— ✅

- 顺序：主目录 `<rootPath>\<日期>` → `<exeBaseDir>\FallbackScreenshots\<日期>` → `<localAppDataDir>\InspectionWidget\FallbackScreenshots\<日期>`，与简报逐字一致；每级共用同一 `TrySaveToDirectory`（同顺延 + 同 CreateNew）。
- `UsedFallback = i > 0`，FullPath 为实际落盘路径。
- 三级全败：`Saved=false`，Error = 「尚未保存：主目录与两级暂存均写入失败（最后异常 类型：消息）」，含「尚未保存」与最后异常摘要。
- 不 Dispose 传入 Bitmap：Saver 全文无 `image.Dispose()`（只有 stream 的 Dispose）；用例 7 在三级全败后 `GetPixel` 正常读出，锚定所有权在调用方。

### 4. 根目录为空直接失败、不进暂存 —— ✅

- `Save` 首行 `string.IsNullOrEmpty(rootPath)` → `Saved=false`、Error「未设置保存根目录」，发生在组装暂存链**之前**，不产生任何文件。用例 8 对空字符串与 null 两种入参都断言两级暂存目录未被创建，PASS。（简报第 5 条锚定成立。）

### 5. captureDate 口径 —— ✅

- `ScreenshotSaver` 全文不取系统时间（grep 核：`DateTime.Now` 只出现在 `IClock.cs` 的 `SystemClock` 实现里，那是其本职）；日期夹名只由传入 `captureDate.ToString("yyyy-MM-dd")` 生成。用例 4 以 FakeClock 跨天验证新夹从 base 名开始，PASS。

### 6. MainForm 接线 —— ✅（两处细节见 Minor-1、Minor-2）

- CaptureRequest 固定时机：`ResolveReason.Ok` 分支在 `BeginCapture()` 成功后立即组快照 `{ Device=状态机当前设备, RootPath=_config.RootPath, Date=_clock.Now, RequestId }`，采集开始即固定设备/根目录/日期，保存与重试全程共用同一快照。
- 工作线程保存：`BeginSave` 开后台线程调 `_saver.Save(...)`，结果 `BeginInvoke` 回 UI 线程后才 `CompleteCapture()` 回 Idle；Saver 抛异常被兜底成 `Saved=false` 结果，不炸线程。
- UsedFallback 反馈口径：错误色、文案「✓ 文件名（已暂存）：<实际完整路径>」、显示 5 秒，同时完整路径写根目录行 tooltip——正是简报 D 允许的实现取舍，代码注释写明口径。普通成功仍走 2 秒（各 Show* 入口都经三参重载复位 Interval=2000，5 秒不会泄漏到后续提示，这点已核）。
- `_pendingImage` 与重试语义：失败保留图与请求、状态回 Idle、反馈行常驻「尚未保存，点击此处重试：<详情>」；点击经 `_saveInFlight` 防重入、以同一 CaptureRequest（含原日期）重试；重试成功按引用相等清 pending 并释放图；新截图开始（`OnTargetResolved` 入口）先 Dispose 未处理旧图；窗体 Dispose 时一并释放。与简报简化口径逐项对上（竞态细节见 Minor-1）。
- 根目录行：未设根目录先行拦截；今日夹以 `_clock.Now` 取日期（浏览口径正确）；目录不存在只提示「今天尚无截图」不建夹；存在时枚举 `*.png` 按 LastWriteTime 取最新，`explorer.exe /select,"<最新图>"`；枚举失败或夹内无图退化为只打开目录。

### 7. root 下「不可写」模拟是否真实 —— ✅ 真失败构造，非假绿

- 本机复跑环境即 root（uid 0），chmod 只读对 root 无效，测试**没有**用权限位：用例 5/6/7 用 `BlockWithFile` 以普通文件占住 root / exe / LAD 路径，其下 `Directory.CreateDirectory(<文件>/<日期>)` 在 Linux/Mono 与 Windows 上都必抛 IOException（ENOTDIR 类），与 uid 无关。暂存落点、顺延（暂存夹预置 base 后落 -1）、三级全败均在此真实失败路径上断言，PASS 可信。

### 8. C# 7.2 子集与零第三方引用 —— ✅

- 独立复跑走 build.sh 的 xbuild 分支，带 `/p:LangVersion=7.2` 编译主工程与测试工程均通过（csproj 内 7.3 仅供 VS，本机构建被覆盖为 7.2）——7.2 子集由编译器实证；源码扫描无 7.3 独有语法。
- csproj 引用仅 `System.*` 框架程序集，无任何第三方包/程序集。

## Minor（5 条，均不挡通过）

- **Minor-1（重试在途竞态）**：`OnTargetResolved` 无条件 Dispose `_pendingImage`，不查 `_saveInFlight`。若用户点反馈行重试后、在保存工作线程完成前又发起新截图（Idle 态设备按钮未被 `_saveInFlight` 拦），正在编码的 Bitmap 会被释放：该次重试必失败，且其 `OnSaveCompleted` 失败分支会把**已释放的图**重新登记为 `_pendingImage`，此后点反馈行重试将对已释放图反复保存、反复失败，直到下一次截图开始才清掉。窗口窄（需在一次 PNG 编码时长内完成点设备+点目标窗全流程）、不崩溃、不伤新图，但状态会脏。建议：Dispose 前加 `_saveInFlight` 守卫，或失败分支登记前校验请求代次。
- **Minor-2（防御分支日期口径）**：`OnCaptureCompleted` 中 `_currentRequest` 为 null 或 RequestId 不一致时，会用**完成时刻**的 `_clock.Now`、`_config.RootPath`、`_stateMachine.CurrentDevice` 现组请求，与「日期在采集开始固定」口径不符。该分支在主路径上不可达（前有迟到校验、组快照在 BeginCapture 后且 Capturing 期间 RequestId 不变），属死代码级隐患；建议改为直接丢弃图并 FailCapture，消除第二套口径。
- **Minor-3（AllocatePath 判据不一致）**：`AllocatePath` 用裸 `File.Exists` 判冲突，无 `created` 标志。因其唯一 IO 就是 CreateNew 空文件、且仅为测试/内部辅助（`Save` 主路径不经它），实际误判面极小；但与主路径的硬化判据不一致，后续若有人把 Save 改走 AllocatePath 会把坑带回来，建议加注释钉住「Save 不得改走此方法」或同步硬化。
- **Minor-4（路径长度校验的附加口径，待协调者确认）**：简报 B 未明示此校验的失败处置；实现只校验主目录完整路径、超限直接判失败且**不进暂存链**（报告已如实披露为遗留）。理由（三级长度互不相关）成立且 MainForm 会保留 pending 供重试，但重试同判、永不落暂存；若主目录深而暂存浅，本可暂存成功的图会被判未保存。口径可接受，但属简报外裁量，请协调者拍板留痕。
- **Minor-5（两条行为无测试锚定 + 一处极窄竞态）**：①「写入中途失败只删本次不完整文件」与「序号上限 10000」无用例（中途失败用真 Bitmap 难以构造、上限用例代价过高），靠本次代码审查确认，建议在报告遗留中留痕；②冲突竞态的另一极窄窗口：CreateNew 因重名失败后、File.Exists 前文件被外部删除 → 误判目录失败转暂存——只降级、不覆盖、不丢图，可接受不修。

## 计数对账（确切总数）

- 独立复跑 `bash build.sh`：退出 0，测试程序自报**总数 156 / 通过 156 / 失败 0**，日志 `[PASS]` 行数亦为 156，与协调者亲跑 156/156 一致。
- 账目：基线 `237a52d` 运行时 147 + Task 5 新增 9（`ScreenshotSaverTests` 9 条 Run 一一对应）= **156**。
- 「157」差异来源：该数字**不出自执行报告**——`work/task-5-report.md` 第 56 行原文即「用例总数 156（基线 147 + 新增 9）」；157 仅见于 `PROGRESS.md` 协调者转记的一行（未入库的工作区改动），属转记多记 1 的笔误，不存在第 157 条用例，建议协调者顺手订正 PROGRESS.md。
- 附带说明（防后续对账再踩）：静态 grep `TestRunner.Run` 调用点只有 148 处，比运行时少 8——`DeviceNameValidatorTests` 有 1 处 Run 写在 foreach 里循环执行 9 次（9 个非法字符各一条），运行时比调用点多 8。基线同理：139 调用点 + 8 = 147。以运行时计数（TestRunner.TotalCount）为准，勿用 grep 调用点数对账。

## 无法验证（与执行报告遗留一致，转 Windows 实机验收）

- explorer `/select` 实际选中效果、反馈行点击重试手感、暂存 5 秒提示与 tooltip 显示、跨午夜真实归属、GDI+ 在 Windows 上对已释放图的异常类型——本机（Mono/Linux）仅能编译与逻辑核验，结论以实机验收为准。
