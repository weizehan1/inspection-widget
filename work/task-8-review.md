# Task 8 审查：异常兜底与发布验收记录

审查对象：376f84b..a5bb6ca（代码提交 a8b197b + 文档提交 a5bb6ca）。
审查方式：通读简报、报告、审查包与 `docs/验收记录.md` 本体，并独立复核源码、独立重跑构建与全量测试、独立 grep 资源点位、独立核对两 csproj 与产物哈希。不只抄报告自查结论。

## 结论（先行）

- **规格：❌**——按简报 A–E 逐条核，主体全部达标（见下）；但审查透镜中 Program.cs 的两项口径未落实（无 `SetUnhandledExceptionMode` 显式设置、无「不可恢复时友好提示并退出」分支）。须注意：这两项与简报 B 的明文口径（ThreadException 处理完不退出、UnhandledException 在 IsTerminating 时不拦截）存在冲突，代码是严格照简报 B 写的；❌ 是按透镜口径给出的，冲突请协调者裁决以哪份为准（详见 Important-1）。
- **质量：不通过**——Critical 0 / Important 2 / Minor 3（另观察项 2）。Important 均为一行级修复（补一行模式设置或协调者裁决后补分支；改验收记录第 13 项一句子项措辞），无结构性问题。
- **确切测试总数：191**（独立复跑 191/191，下有对账）。

## 一、Program.cs 与 ErrorLog（透镜 1、2）

### ErrorLog（简报 A）✅

- `src/ErrorLog.cs`：主路径 `AppDomain.CurrentDomain.BaseDirectory` + `error.log`（PrimaryDir 默认取 BaseDirectory，属实）；主路径写失败回退 `%LocalAppData%\InspectionWidget\error.log`；再失败静默放弃。
- Write 绝不抛：外层 try/catch 全吞，`TryAppend` 内部再 catch 返回 false，`Format` 也在外层 try 内。写失败吞掉属实。
- 格式 `[yyyy-MM-dd HH:mm:ss] 环节 异常类型: 消息` + 堆栈，追加写，与简报一致。
- 不含凭据明文：独立 grep `ErrorLog.Write` 全部调用点——生产代码仅 Program.cs 三处，context 均为固定环节名（「UI线程」「UI线程恢复待命」「非UI线程」），本类签名只收 context + Exception、不收业务数据。异常 Message/堆栈理论上由抛出点决定，现行调用链无凭据拼入点，判定通过。
- 测试 `tests/ErrorLogTests.cs` 4 条真锚定：主目录落盘且含环节名/异常类型/消息且 CurrentLogPath 指向主路径、追加写两条同文件、以真实文件占父目录稳定构造不可写验证回退落盘、两处均不可写含 ex=null/context=null 不抛。注入点经 `InternalsVisibleTo("InspectionWidget.Tests")` 开放，产品对外面不变。

### ThreadException / UnhandledException（简报 B）⚠️ 主体达标、透镜口径两项未落实

达标部分（逐行核 `src/Program.cs`）：

- `Application.ThreadException += OnThreadException` 与 `AppDomain.CurrentDomain.UnhandledException += OnUnhandledException` 均在 `Application.Run` 之前挂载。
- OnThreadException：先 `ErrorLog.Write("UI线程", …)`，再经 `Application.OpenForms` 找 MainForm 调 `RecoverToIdle()`；找/调失败只记「UI线程恢复待命」日志，不退出。与简报「处理完不退出（可恢复）」一致。
- `MainForm.RecoverToIdle()` 本体核过：非 UI 线程先 BeginInvoke 切回；Waiting 走 `Cancel()`、Capturing 走 `FailCapture()` 回 Idle，并经 `OnStateChanged()` 集中点停钩（该方法在非 Waiting 分支调 `_hooks.Stop()`，幂等属实）；`_pendingImage` 全程未被释放，有待保存图时恢复常驻「尚未保存，点击此处重试」，无图时给「发生未预期异常，已恢复待命」一条反馈——待保存图保留属实。
- OnUnhandledException：非 Exception 对象有兜底包装后写「非UI线程」日志；IsTerminating 不拦截、由系统终止，与简报一致；处理本身不抛（Write 已全吞）。

未落实部分（透镜口径）：

1. **全仓无 `Application.SetUnhandledExceptionMode(...)` 调用**（独立 grep 确认）。默认 `UnhandledExceptionMode.Automatic` 的语义是「路由到 ThreadException 处理器，除非应用配置文件另有指定」（如 app.config 的 jitDebugging 设置）——微软官方示例正是为此显式设 `CatchException` 以保证与配置无关。本项目交付不带 .config，默认配置下处理器能生效，故不构成 Critical；但「不弹崩溃框/调试器框」在透镜口径下没有被代码强制保证，属真实缺口，列 Important-1。
2. **无「不可恢复 → 只弹一个友好提示并退出」分支**：UI 线程异常一律按可恢复处理（面板内一条反馈 + 继续运行）；非 UI 线程终止性异常只写日志、由系统终止，届时可能出现系统级崩溃对话框而非友好提示。此点与实施计划 Task 8 Step 2「不可恢复提示后退出」同向、与简报 B 明文冲突（见结论处裁决请求）。另：`RecoverToIdle()` 若自身失败，现状是只记日志、面板停留在未知状态继续运行，没有升级到提示退出路径。

## 二、AbandonedMutexException 接管（透镜 3）✅

- `src/Program.cs` Main：`new Mutex(true, name, out createdNew)` 抛 `AbandonedMutexException` 时按其语义（本线程已接管所有权）置 `createdNew = true` 继续启动，不误判「已在运行」——属实。
- 细节核过：构造未完成的 Mutex 不被依赖，另开 `new Mutex(false, MutexName)` 同名句柄保活命名互斥至进程结束——后续实例构造时 createdNew 必为 false，拦截行为不变；正常重复启动路径（createdNew=false 即 Dispose 并 return）与改前一致。
- 开保活句柄失败只降级（mutex=null、无保活直接 RunApplication），报告已披露；此降级下单实例防护失效但不阻断启动，列观察项（见 Minor 后）。

## 三、ConfigStore.Save 序列化兜底（透镜 4）✅（用例锚定强度见 Minor-1）

- `src/Config/ConfigStore.cs`：取锁段与写段在 IOException/UnauthorizedAccessException 之外各补 `catch (Exception)` → `SaveResult{ Saved=false, Error=摘要 }`，不穿透。
- 写段 try 的覆盖范围逐行核过：`DataContractJsonSerializer` 构造、`WriteObject`、`File.Copy` 全在 try 内；finally 仍清理临时文件；正式文件在异常路径不被 Copy 覆盖。捕获层位置正确。
- 新增用例存在且已接线运行（ConfigStoreTests 第 9 条，本次 +1 之一）。其锚定强度问题见 Minor-1，报告自查 6 已如实披露「无可达触发点、不冒充已验证异常路径」，态度属实。

## 四、计数对账（透镜 8）

- **独立复跑**：`bash build.sh` 退出 0（xbuild Release），汇总行 191/191/0；另直接 `mono tests/bin/Release/InspectionWidget.Tests.exe` 复跑，`[PASS]` 191 条、`[FAIL]` 0 条。**确切总数 = 191**。
- 静态口径：全仓 `TestRunner.Run(` 调用点 HEAD 为 183 个；基线 376f84b 为 178 个；本次新增恰 5 个（ErrorLogTests 4 + ConfigStoreTests 1），与报告「基线 186 + 新增 5」吻合。
- 静态 183 与运行时 191 差 +8 的来源已定位：`tests/DeviceNameValidatorTests.cs` 中 1 个调用点位于 `foreach` 循环内，对 9 个非法字符（`\ / : * ? " < > |`）逐个生成用例，1 个调用点展开为 9 条（+8）。基线同样如此：178 静态 + 8 = 186 运行时，与 Task 7 关账的 186 一致。
- 「实现方称 188」与任何可复现口径都不符（静态 183 / 基线运行时 186 / 现运行时 191 均非 188），比确切总数少 3。最可能的来源（推测）：183（已含新增 5 条的静态总数）再叠加一次新增 5 条 = 188，即静态计数与新增数重复叠加。无论来源如何，以运行时 191 为准，协调者亲跑的 191/191 正确。

## 五、资源纪律与全局搜查（透镜 5，抽查复核非抄录）

按简报 C 清单独立 grep 全仓并逐处读代码，结论：报告/验收记录的自查结论基本属实，除已修 1 处外未发现新的确定性泄漏。

| 点位 | 独立复核结果 |
| --- | --- |
| `new Bitmap` 2 处（WindowCapture PrintWindow / BitBlt） | PrintWindow：Graphics 在 using 内；失败 Dispose；Clone 裁剪在 finally 中 Dispose 原图（Clone 抛错时异常上抛、由 MainForm 工作线程兜底成 Failed，不产生已释放图误用）；超时迟到图登记回收表由迟到线程在 lock 内 Dispose、不提前释放，属实。BitBlt：using + catch Dispose + 复核 Dispose + 成功移交，属实 |
| `Graphics.FromImage` 2 处 | 均在 using 内，属实 |
| `GetHdc` 1 处 | 与 `ReleaseHdc` 在 finally 中成对，属实 |
| `CreateCompatibleDC` 等裸 GDI 句柄 | 全仓 0 处，BitBlt 走 `Graphics.CopyFromScreen`，属实 |
| `SetWindowsHookEx` 2 处（HookManager） | 与 `UnhookWindowsHookEx` 成对：挂钩失败 catch 先卸已挂钩、消息循环退出后卸钩、FinalizeTeardown 兜底卸钩（Join 超时从外部卸），Stop/Dispose 幂等，属实 |
| `new Timer` 4 处 | HookManager 的 TimeoutTimer/DeferredTeardownTimer 在 Stop/FinalizeTeardown 中 Dispose；ClipboardGuard 重启前先 Dispose 旧 Timer、Dispose 时停表；MainForm `_feedbackTimer` 本次补进窗体 Dispose——diff 与现行代码（MainForm.cs Dispose 段）双核，修复属实 |
| `FileStream`（ConfigStore / ScreenshotSaver） | ConfigStore 锁流 `using (lockStream)`、临时流 using；ScreenshotSaver 占名流 using、写入流成功 Dispose/失败 CloseQuietly，两条异常路径都覆盖，属实 |
| 线程 try/finally 抽查 | PrintWindow 线程 catch 兜底 + HDC finally；剪贴板 STA 线程 try/catch 捕获后透传；MainForm 截图/保存两工作线程 try/catch 且窗体已释放/迟到分支均 Dispose 图。无裸奔线程 |
| Font / Pen（验收记录自愿扩查行） | Pen 均 using；MainForm/SettingsForm 缓存 Font 均在各自 Dispose 释放，属实 |

观察项（不计缺陷）：`HookSession.Ready`（ManualResetEvent）未显式 Dispose——验收记录已披露且理由成立（SafeHandle 由 GC 回收、每会话一个；在 Start 超时后补 Dispose 会让迟到钩子线程的 `Ready.Set()` 撞已释放对象），审查同意维持现状、留实机阶段再议。

## 六、Release 构建（透镜 6）✅

- 独立重跑 `bash build.sh`：使用 xbuild、Release 配置、退出 0；产物仍在 `build/`，仅 `InspectionWidget.exe` + `InspectionWidget.exe.manifest` 两文件，无 .pdb/.mdb 被拷入（Mono 的 .mdb 只留在 `src/bin/Release`）。
- 两 csproj Release 属性组逐字段 diff：`PlatformTarget / DebugType=pdbonly / DebugSymbols=true / Optimize=true / OutputPath / DefineConstants / ErrorReport / WarningLevel / Prefer32Bit / LangVersion=7.3` 完全一致（Mono 构建由 build.sh 以 `/p:LangVersion=7.2` 统一覆盖）；报告「完全一致」属实。
- 产物实测：83,456 字节，SHA256 `64e2c940bf1027e79239bc237f11a2af6c66458c5e6b82db73ce2fa0625fb37a`，与报告及验收记录记载逐字一致；≤300KB 达标属实。
- C# 7.2 子集（R6）：新增/改动代码未见 7.3+ 语法，且 build.sh 以 LangVersion=7.2 实际编译通过，为最强证据。csproj 内仍写 7.3 属既有口径（VS 构建用），与报告说明一致。

## 七、验收记录 15 项（透镜 7）

状态分布核对：已验证 3 项（7/10/13）、仅编译通过 6 项（1/2/3/4/8/9）、待实机 6 项（5/6/11/12/14/15），合计 15，与简报 D 映射对照如下——**没有任何一项把「仅编译通过/待实机」拔高成「已验证」**：

- 第 1/3/8 项标仅编译通过：manifest 的 requireAdministrator 与 PerMonitorV2、BitBlt 前 hideSelf、DPAPI 代码均实读确认存在，状态与证据相符。
- 第 2/4 项标仅编译通过并注明「几何逻辑已验证、成图待实机」：CaptureGeometryTests（负坐标副屏、裁边）与 BlackFrameDetectorTests 确实存在且全过，注明属实。
- 第 9 项两层口径属实：状态标仅编译通过（未拔高），证据写明决策逻辑已验证（ClipboardGuardTests 的 ShouldClear 全矩阵 + CredentialServiceTests + MainForm 退出 TryClearNow 接线均实读确认）、真实剪贴板待实机。小瑕疵见 Minor-2。
- 第 15 项标待实机、注明高度逻辑已验证（PanelLayoutTests 折叠/展开与行数矩阵属实）、折叠保留在实机判据内，未拔高。
- 第 5/6/11/12/14 项标待实机且均写明实机判据；第 11 项附资源纪律核对结论作支撑材料，口径正确。
- 本地已验证清单与待实机清单齐全性：15 项表本身即两清单合一（状态列 + 证据/判据列），文首另有全局异常兜底表、文末遗留明确「12 项须真机逐项实测、全过才算第一版完成」，判定齐全。

逐项证据抽查（7/10/13 重点）：

- **第 7 项证据属实**：ScreenshotSaverTests 中顺延 -1（且原文件哨兵字节不变）、base/-1/-3 取 -2 回填、AllocatePath CreateNew 占位、根目录不可写落 exe 暂存、exe 不可写落 LocalAppData，逐条对得上。
- **第 10 项证据属实**：ConfigStoreTests 中缺字段 Defaulted 且保留已写字段、乱码与类型错误走 CorruptBackedUp 且备份内容=原件、Save→Load 往返不丢字段，逐条对得上。
- **第 13 项证据有一子项不实（Important-2）**：证据句称「写中途失败删本次不完整文件并保留原图待重试等用例全过」——全仓 tests 中不存在写中途失败的注入用例（无坏图/已释放图/写流中途抛错类用例；最接近的是「三级全不可写 → Saved=false、Bitmap 不被释放」，那是建文件前失败、不是写中途失败）。该行为在 ScreenshotSaver 代码中确有实现（catch 中 CloseQuietly + TryDelete 本次文件），但「用例全过」的表述无证据。第 13 项状态本身仍可维持已验证（简报 D 给定的证据范围是「两级暂存与不覆盖」，此两项用例属实且已抽查），问题只在证据句多写了一个未经测试的子项，须删改或降格为「代码实现、待实机/待补测」。

另：审查包（task-8-review-package.md）实际只含代码提交 a8b197b 的 diff，不含文档提交 a5bb6ca 的 diff，与「含代码与文档两提交」的说法不符；本次已按任务要求直读 `docs/验收记录.md` 本体补齐，不影响结论，列 Minor-3（流程项）。

## 八、质量 findings

### Critical（0）

无。

### Important（2）

- **Important-1｜Program.cs 未设 SetUnhandledExceptionMode，且无不可恢复分支**：全仓无 `Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException)`，ThreadException 路由在默认 Automatic 下受应用配置文件影响，不满足透镜「不弹崩溃框（SetUnhandledExceptionMode 口径）」的强制口径；同时无「不可恢复时只弹一个友好提示并退出」路径（RecoverToIdle 失败时仅记日志继续运行、终止性异常由系统接管）。注意冲突：此口径与简报 B 明文（不退出、不拦截）不一致，代码照简报 B 实现无误——请协调者裁决：若以透镜/实施计划 Step 2 为准，修复为一行模式设置 + 不可恢复分支；若以简报 B 为准，此项应正式豁免并回写简报，消除双口径。
- **Important-2｜验收记录第 13 项证据夸大一子项**：「写中途失败删本次不完整文件并保留原图待重试等用例全过」无对应测试（详见第七节）。验收记录是本任务核心交付物、其立身之本就是证据准确，须把该子项删去或降格为「代码实现，待补测/实机」，第 13 项状态与其他表述不动。

### Minor（3）

- **Minor-1｜ConfigStore 新用例锚定偏弱**：用例以 `(CaptureMode)999` 为敌意输入，但本机 Mono（据报告实验，.NET Framework 序列化器亦然）对该值可正常序列化 → 结果 Saved=true，断言 `Saved || Error 非空` 在成功分支恒真，实际只锚定「不抛异常 + 结果非空 + 未保存必带 Error」的口径，异常→Saved=false 的转换路径无运行时锚定。捕获层位置已经代码审查确认正确、且现行 DataContract 形状无可达触发点，报告亦如实披露，故仅 Minor；后续若配置模型引入可抛错形状，应补真触发用例。
- **Minor-2｜第 9 项证据中「30 秒到期清」措辞略宽**：ClipboardGuardTests 构造 Guard 时用 3600 秒间隔刻意规避真实到期，到期回调 `OnTimerElapsed → TryClearNow` 这一行委托未被用例直接触发；已验证的是清空决策、回执核对与退出接线。因第 9 项总状态仅标仅编译通过，不构成拔高，仅建议把该短语改为「到期清决策已验证」以求精确。
- **Minor-3｜审查包不完整（流程）**：审查包缺文档提交 a5bb6ca 的 diff（见第七节末），下次打包请把两提交都纳入，或在包头注明文档需直读。

### 观察项（2，不计入问题数）

- AbandonedMutex 接管后以 `new Mutex(false, …)` 保活句柄不持有所有权一事，靠「命名对象存续 ⇒ 后续实例 createdNew=false」实现拦截，语义成立；保活句柄打开失败时单实例防护静默失效（已披露），可接受。
- 提交信息与简报预设的单条 `feat(stage-8): …` 不同（拆为 fix + docs 两提交），报告已披露且本次任务框架本身按两提交组织，不计问题。

## 九、总判定

- 规格：❌（仅因透镜口径两项未落实且与简报 B 冲突待裁决，详见 Important-1；简报 A–E 本身逐条达标）
- 质量：不通过（Critical 0 / Important 2 / Minor 3）
- 确切测试总数：191（独立复跑 191/191，build.sh 退出 0）
- 修复量预估：Important-1 视裁决结果为一行设置或正式豁免回写；Important-2 为验收记录一句话修订。两者均不涉及结构改动，修后可快速范围复审。
