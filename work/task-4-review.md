# Task 4 审查报告：截图引擎（8ceea89..HEAD，feat cdfa92e）

审查方式：直读简报、执行报告、审查包与当前源码；独立复跑 `bash build.sh`；`dpkg -l` / `ldd` 核对环境插曲。未改任何生产代码。

## 一、规格结论：✅ 全部符合

### 1. CaptureGeometry ✅
- 无效口径与简报一致：winRect 或 dwmRect 宽/高 ≤0 → `Valid=false`、`Crop=Empty`。
- 偏移公式 `Crop=(dwm.X-win.X, dwm.Y-win.Y, dwm.Width, dwm.Height)` 正确；负坐标副屏用例 (-1200,50)/(-1193,50) → (7,0,802,609) 通过（公式只用相对偏移，天然坐标无关）。
- 夹紧实现正确：偏移 <0 归 0 并同步扣减宽/高，右下超出按 win 尺寸截断，截断后宽/高 ≤0 → 无效；左侧越界、右下越界、完全越界三条夹紧用例均通过（共 8 条）。

### 2. BlackFrameDetector ✅
- 采样坐标 `(int)((i+0.5) * 尺寸 / 15.0)` 并夹到末像素，与简报「(i+0.5)/15 比例取整」同口径；判据为纯黑 (R=G=B=0) 或全透明 (A=0)，占比 `> 0.98` 严格大于；`null → true`（另对宽/高 ≤0 也返回 true，属合理防御）。
- 边界用例构造真实对准采样网格，不是凑数：测试内以同口径公式独立算出 225 个采样坐标，100×100 下横纵坐标均为 3,10,16,23,30,36,43,50,56,63,70,76,83,90,96（互不重合），先全黑再精准涂白前 3 / 前 5 个采样点，可疑数恰为 222 / 220。
- 口径说明：简报 B 节规定的边界即 222 黑（98.67%）→true、220 黑（97.8%）→false，测试与之逐条一致；审查透镜提到的 221/225（98.22%）虽未单设用例，但实现常量为 `> 0.98`，221/225 > 0.98 由代码直读即成立，规格无缺口（见 Minor-3，仅测试锐度问题）。

### 3. WindowCapture ✅（逐条对 C 节七项）
- ① 按位图尺寸口径正确：位图按 **winRect** 尺寸新建，PrintWindow 后按 `ComputeCrop(winRect, dwmRect)` 裁边；**未**出现按 DWM 尺寸建图直调 PrintWindow 的错误做法。裁剪区与整幅相同时不 Clone（免无谓拷贝），不同时 Clone 新图并 Dispose 原图，裁边无效用整幅。
- ② 标志正确：`PwRenderFullContent = 0x00000002`（PW_RENDERFULLCONTENT）传入 PrintWindow；HDC 经 `Graphics.GetHdc()` 取得、`finally ReleaseHdc`，Graphics 用 using 释放。
- ③ 超时机制正确：PrintWindow 在独立后台线程执行、调用线程 `Join(3000)`；超时**不 Dispose** 在用位图，attempt（位图 + Finished/Abandoned 标志）登记静态迟到回收表（`LateAttempts`，`LateLock` 锁保护），迟到线程在自身收尾段（锁内）见 Abandoned 后自行 Dispose 并摘除；Join 超时与迟到完成相撞时以同一把锁串行化，`finishedLate` 分支按完成结果继续、不误登记、不双重释放；`PrintWindowOnly` 超时直接 Failed 并注明「PrintWindow 超时（3 秒）」，Auto 档超时回退 BitBlt。
- ④ 置前核查正确：`SetForegroundWindow` + `BringWindowToTop` 后以 `GetForegroundWindow() == hwnd` 核查，不等 → 等 100ms 再查一次，仍不等 → Failed，文案为简报原文「窗口置前被拒，请手动点开目标窗口后重试」。
- ⑤ 自遮挡正确：`selfBounds` 与抓取区 (dwmRect) 相交且 hideSelf 非空时 `hideSelf()` → `Thread.Sleep(60)`，`finally` 中仅在确实隐藏过时 `showSelf()`（恢复异常吞掉、不掩盖截图结果）；抓屏用 `CopyFromScreen(dwmRect.Location, Point.Empty, dwmRect.Size)`，源坐标为物理像素口径。
- ⑥ 移动复核两路都做：PrintWindow 路与 BitBlt 路在返回 Ok 前均再次 `GetWindowRect` 与起始 winRect 比对（且复查 IsWindow），变化 → Dispose 图、WindowGone「窗口已移动，请重试」。
- ⑦ 资源释放合格：中间位图在返回 false / 可疑 / 已移动 / 异常各分支均 Dispose（迟到位图由迟到线程释放，见 ③）；返回图所有权归调用方，注释与 MainForm 侧处理一致（异常路径的一处小缺口见 Minor-1）。
- ⑧ 三档不串味：Auto = PrintWindow 失败/超时/可疑回退 BitBlt；PrintWindowOnly 在失败 → Failed、超时 → Failed、可疑 → SuspectProtected，绝不进 BitBlt（`TryPrintWindow` 在 Only 档不返回 null，Capture 另有防御分支）；BitBltOnly 跳过 PrintWindow 直接走屏幕拷贝。枚举 `CaptureMode { Auto, PrintWindowOnly, BitBltOnly }` 与分支判断一致。

### 4. MainForm 接线 ✅
- 截图在独立工作线程执行；hideSelf/showSelf 以阻塞式 `Invoke` 切 UI 线程改 Opacity（0/1），符合「引擎内不开线程做 UI、注入方切线程」口径。
- 结果经 `BeginInvoke` 回 UI 后**先**做迟到校验：requestId 与 `CurrentRequestId` 不一致或状态已非 Capturing → Dispose 图直接丢弃，不反馈不转移。
- 各状态分支与简报 D 节一致：Ok → 反馈「已截图：<标题>（保存阶段 5 接入）」+ Dispose + `CompleteCapture`；SuspectProtected →「该窗口可能受系统保护，截不到画面」+ `FailCapture`；WindowGone / Failed → 反馈其 Message + `FailCapture`；回 Idle 均经既有 `OnStateChanged` 集中点停钩（幂等）。工作线程另包一层异常转 Failed，窗体已 Dispose 时迟到图直接释放。
- **M-3 会话代次守卫与报告所述做法一致**（直读 HookManager + MainForm 核对）：`HookManager.Start` 改为返回钩子会话号（新建会话返回新号、已在运行只更新 SelfBounds 并返回现行号）；`ExternalClick` 改为 `Action<Point, int>`，Up 投递闭包内捕获所属 `session.Id` 随事件带回；MainForm 新增 `_waitingSessionId`，进 Waiting 的 Start、Waiting 内重启两处均更新，Start 失败清 0；`OnHookExternalClick` 切回 UI 线程后、状态检查之前先比对会话号，不一致直接丢弃（不解析、不转移、不重启钩子）。CancelRequested / TimeoutElapsed 未带会话号，报告对其风险量级的说明（迟到最坏是多一次无副作用的 Cancel）成立，按任务书范围只守 ExternalClick 可接受。

### 5. C# 7.2 子集与零第三方引用 ✅
- `build.sh` 对产品与测试工程均以 `/p:LangVersion=7.2` 覆盖编译（csproj 内 7.3 为 Task 0 既有安排，构建口径以 build.sh 为准），Mono mcs 编译通过即证明无 7.3 独有语法；新代码仅用匿名委托、`var`、对象初始化器等 7.2 内语法。
- 本区间对产品 csproj 无改动，引用仍为框架程序集（System / System.Drawing / WinForms 等）；测试工程仅新增框架引用 `System.Drawing`。零第三方引用属实。

### 6. 测试计数 ✅（独立复跑核对）
- 审查者独立执行 `bash build.sh`：退出码 0，输出「用例总数：146，通过：146，失败：0」。
- 与协调者亲跑的 146/146 一致；构成核对无误：基线 131 + 几何 8 + 黑图 7 = 146，既有用例无回归。

### 7. 环境插曲披露核查 ✅ 属实、且对产品代码结论零影响
- `dpkg -l` 实测：`libgif7 5.2.2-1ubuntu1`、`libexif12 0.6.24-1build2` 均为 `ii`（已安装），与报告所记直下 deb 的版本号逐字一致；`ldd /usr/lib/libgdiplus.so.0` 现显示 `libgif.so.7`、`libexif.so.12` 均正常解析、无 not found。报告「libgdiplus 本体在、两依赖缺失、装后依赖齐全」的核心事实成立（apt 索引为空/仓库 404 一节为当时过程陈述，现无法复验，不影响结论）。
- 影响判断：两 deb 是 Linux 系统运行库，仅供本机 Mono 的 System.Drawing (libgdiplus) 跑黑图测试；本区间提交文件清单中无任何依赖/打包变更，产品为 .NET Framework / Windows 程序，不引用这两库。属测试环境修复，零产品影响，报告定性准确。

## 二、质量结论：通过（Critical 0 / Important 0 / Minor 3）

- **Critical**：无。
- **Important**：无。
- **Minor-1**（资源，例外路径）：`TryPrintWindow` 裁边 `bmp.Clone(...)` 若抛异常，异常上抛至 MainForm 转 Failed，但原图 bmp 未 Dispose（常态路径释放纪律完整，仅此异常分支漏放）。建议后续顺手以 try/finally 收拢，不阻塞关账。
- **Minor-2**（顺序观察）：PrintWindow 路先做移动复核、后做黑图判定，Auto 档下「已移动 + 可疑」并发时直接 WindowGone 而不再试 BitBlt。简报未规定两判据并发时的优先级，窗口已移动时 BitBlt 同样会落 WindowGone，此行为可辩护，仅记录备考。
- **Minor-3**（测试锐度）：黑图边界用例按简报取 222/220，未取最紧界值 221（98.22%，恰在阈值上方 1 点）。实现常量经直读无误，故仅为覆盖锐度建议，后续可补一条 221 用例锁死阈值。

## 三、无法验证（Linux 环境所限，单列）

以下为 Windows 运行时行为，本机仅能保证编译与逻辑直读，无法实测，与报告「遗留」清单口径一致，须 Windows 实机验收：

1. PrintWindow 对真实窗口（含受保护/UWP/硬件加速窗口）的实际出图与 PW_RENDERFULLCONTENT 效果；PrintWindow 真卡死时 3 秒超时与迟到回收表回收、`LateAttemptCount` 归零、无句柄/内存泄漏。
2. CopyFromScreen 的物理像素/DPI 缩放行为（多屏不同缩放、负坐标副屏、150% 缩放）。
3. SetForegroundWindow 在前台锁定/UAC/管理员窗口组合下被拒分支的真实触发与文案路径。
4. DWM 扩展边框矩形对透明边窗口的实机裁边效果（无透明边、无内容截断）及 DWM 查询失败回退行为。
5. hideSelf + 60ms 对自遮挡残影的消除是否足够；钩子会话号迟到事件（M-3）在真实消息循环中的投递与丢弃。
6. 七格矩阵（遮挡/重叠/副屏/缩放/最大化等）与三档模式的实机逐格验收。

## 四、总评

规格 ✅，质量通过。截图引擎严格落实了「winRect 建图 + DWM 裁边」「独立线程 + 3000ms 超时 + 迟到回收」「置前 GetForegroundWindow 核查」「相交 hideSelf + 60ms + finally showSelf」「两路移动复核」五处硬口径，MainForm 迟到校验与 M-3 代次守卫实现与报告所述一致；独立复跑 146/146 全过。遗留均为已如实披露的 Windows 实机项与 3 条 Minor，不阻塞 Task 4 关账。
