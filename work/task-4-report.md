# Task 4 执行报告：截图引擎

## 状态

**DONE**（本机可验证部分全过；PrintWindow/BitBlt/钩子等 Windows 专属路径仅保证编译通过，实机清单见「遗留」）。

实现提交：`cdfa92e`（`feat(stage-4): 截图引擎（PrintWindow裁边/黑图检测/BitBlt回退）`，父提交为基线 `8ceea89`，已用 `git log --oneline | head -2` 自核）。本报告随其后的报告提交一同入库，哈希以协调者 `git log` 核对为准。

## 交付内容

| 文件 | 内容 |
|---|---|
| `src/Capture/CaptureGeometry.cs`（新建） | `CropResult { Valid, Crop }` + `CaptureGeometry.ComputeCrop(winRect, dwmRect)` 纯逻辑裁边 |
| `src/Capture/BlackFrameDetector.cs`（新建） | `BlackFrameDetector.IsSuspect(Bitmap)`：15×15 网格采样、纯黑或全透明占比 > 98% 判可疑，null → true |
| `src/Capture/WindowCapture.cs`（新建） | `WindowCapture.Capture(hwnd, mode, selfBounds, hideSelf, showSelf)` → `CaptureOutcome { Status, Image, Message }`，`CaptureStatus { Ok, SuspectProtected, WindowGone, Failed }` |
| `src/Native/NativeMethods.cs`（追加） | `PrintWindow`、`DwmGetWindowAttribute` + 常量 `DWMWA_EXTENDED_FRAME_BOUNDS = 9`（`GetForegroundWindow` Task 3 已有，本任务未重复加） |
| `src/Capture/HookManager.cs`（修改） | M-3 补接线：`ExternalClick` 改为携带会话号 `Action<Point, int>`，`Start` 改为返回钩子会话号 `int`（见下节） |
| `src/MainForm.cs`（修改） | `OnTargetResolved` 真实接线（工作线程截图 + BeginInvoke 回 UI + requestId 迟到校验）；Waiting 会话代次守卫（M-3） |
| `tests/CaptureGeometryTests.cs`（新建） | 几何 8 条 |
| `tests/BlackFrameDetectorTests.cs`（新建） | 黑图 7 条（Mono + libgdiplus 真 Bitmap） |
| `tests/Program.cs`、`tests/InspectionWidget.Tests.csproj`（修改） | 注册两组新测试；测试工程补 `System.Drawing` 引用（Bitmap/Rectangle 直接使用所需） |

全程 C# 7.2 以内语法（R6）：`build.sh` 走 xbuild + `/p:LangVersion=7.2` 编译通过，无 7.3 独有语法。

## TDD 证据（先 FAIL 后 PASS）

### 几何（CaptureGeometryTests，8 条）
- **RED**：先只写测试与注册，运行 `bash build.sh` 退出码 1，测试工程编译失败——`CS0246: The type or namespace name 'CropResult' could not be found`、`CS0103: The name 'BlackFrameDetector' does not exist` 等共 39 个错误（日志 `/tmp/task4-red.log`，实现类尚不存在，符合预期）。
- **GREEN**：实现 `CaptureGeometry` 后 8 条全 PASS：标准偏移 (100,100,816,616)/(107,100,802,609)→(7,0,802,609)；两矩形相同→(0,0,816,616)；负坐标副屏 (-1200,50,…)/(-1193,50,…)→(7,0,802,609)；dwm 宽 0→无效且 Crop=Empty；winRect 宽 0→无效；dwm 左侧部分越界 (90,100,816,616)→夹紧 (0,0,806,616)；dwm 右下部分越界 (107,100,900,700)→夹紧 (7,0,809,616)；dwm 完全越界 (2000,100,100,100)→夹紧后宽 ≤0 无效。
- 夹紧算法：先算相对偏移与尺寸，偏移 <0 时归 0 并相应扣减宽/高，右下超出按 win 尺寸截断，截断后宽或高 ≤0 → Valid=false。

### 黑图（BlackFrameDetectorTests，7 条，Mono + libgdiplus 真跑）
- **RED**：同上（类不存在编译失败）。
- **环境插曲（如实记录）**：首次 GREEN 运行几何 8 条全 PASS、黑图测试因 `DllNotFoundException: libgdiplus.so.0` 失败——libgdiplus 本体已装（/usr/lib/libgdiplus.so.0 在），但其依赖 `libgif.so.7`、`libexif.so.12` 缺失（`ldd` 实测 not found），且本机 apt 索引为空、`apt-get update` 因外部仓库 404 失败拿不到候选包。处置：从 Ubuntu 官方池直下 noble 对应 deb 并 `dpkg -i`：`libgif7_5.2.2-1ubuntu1_amd64.deb`、`libexif12_0.6.24-1build2_amd64.deb`；`ldd` 复查依赖齐全后黑图测试真跑通过。此为本机测试环境修复，不涉及产品代码。
- **GREEN**：7 条全 PASS：全黑 100×100→true；全透明（32bpp ARGB 新建默认 A=0）→true；全白→false；黑底中央白块（40–59 区间，采样网格必含 43/50/56 等白点）→false；null→true；**98% 边界两条按简报采样点构造法落实**——测试内以与实现同口径公式 (i+0.5)/15 比例取整算出 225 个采样坐标，先全黑、再精准涂白：涂 3 点（222 黑，98.67% > 98%）→true；涂 5 点（220 黑，97.8% ≤ 98%）→false。

### 总账
- `bash build.sh` 退出 0；**用例总数 146（基线 131 + 几何 8 + 黑图 7），通过 146，失败 0**，既有用例无回归。

## WindowCapture 流程落实（对照简报逐条）

1. **复核**：`!IsWindow(hwnd)` 或 `IsIconic` → `WindowGone`；`GetWindowRect` 取 winRect；`DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS=9)` 取 dwmRect，失败（HRESULT ≠ 0 或尺寸无效）则 dwmRect=winRect 且 Message 注明「DWM 查询失败，可能含透明边」（Message 仅日志/诊断与失败提示用）。
2. **PrintWindow 路径**（mode != BitBltOnly）：按 winRect 尺寸建 Bitmap，`Graphics.GetHdc()` → `PrintWindow(hwnd, hdc, 0x00000002 PW_RENDERFULLCONTENT)` → `ReleaseHdc`。该步在独立后台线程执行，调用线程 `Join(3000)` 等待：
   - 超时且线程未完成 → **不 Dispose 位图**，将 attempt 登记到静态「迟到回收表」（`LateAttempts`，含位图与完成/放弃标志，锁保护）；迟到线程在自己的 finally 段（锁内）发现 Abandoned 后自行 Dispose 并摘除。等待与迟到完成恰好相撞时以锁串行化：若线程已 Finished 则按完成结果继续处理，不误登记。超时后 Auto 档走 BitBlt 回退；PrintWindowOnly 档直接 Failed 并注明「PrintWindow 超时（3 秒）」。
   - 返回 false → 回退（PrintWindowOnly → Failed）。
   - 成功 → `ComputeCrop` 裁边：Valid 且裁剪区与整幅不同才 `Clone` 产生新图并 Dispose 原图；无效用整幅。
   - 结果图 `IsSuspect` → Dispose 并回退（PrintWindowOnly → SuspectProtected）。
3. **BitBlt 回退**（mode != PrintWindowOnly）：`SetForegroundWindow` + `BringWindowToTop` 后以 `GetForegroundWindow()` 核查是否等于 hwnd，不等 → 等 100ms 再查一次，仍不等 → Failed，Message 为简报原文「窗口置前被拒，请手动点开目标窗口后重试」；selfBounds 与 dwmRect 相交 → `hideSelf()`、`Thread.Sleep(60)`；`Graphics.CopyFromScreen(dwmRect.Location, Point.Empty, dwmRect.Size)` 按物理像素抓 dwmRect；`finally showSelf()`（隐藏过才恢复，恢复异常不掩盖截图结果）。抓后 `IsSuspect` → Dispose、SuspectProtected（Message 注明「截图疑似黑图，可能受系统保护」）；否则 Ok。
4. **截图后复核**（两路都查）：再次 `GetWindowRect` 与开始时比对，窗口不在或位置/尺寸变化 → Dispose、WindowGone，Message「窗口已移动，请重试」。
5. **资源纪律**：中间 Bitmap/HDC 全部 using/finally 释放；返回的 Image 所有权归调用方；异常兜底捕获转 Failed（MainForm 侧再包一层异常转 Failed）。

注：简报 C 节接口签名按计划文档口径落地为实例类 `WindowCapture`（非 `IWindowCapture` 接口，与简报给出的 `public sealed class WindowCapture` 一致）。

## MainForm 接线（简报 D 节）

- `OnTargetResolved(result, requestId)`：调用方已 `BeginCapture` 进 Capturing。本方法组装 `(result.Hwnd, _config.CaptureMode, this.Bounds)`，起后台工作线程执行 `WindowCapture.Capture`；`hideSelf/showSelf` 以 `Invoke`（阻塞式）切 UI 线程改 `Opacity`（0 / 1）并等待完成，满足「类内部不开新线程做 UI 操作、hide/show 由注入方切线程」口径。
- 结果以 `BeginInvoke` 回 UI 线程 `OnCaptureCompleted`：**先做迟到校验**——requestId 与状态机 `CurrentRequestId` 不一致或状态已非 Capturing → 直接 Dispose 图并丢弃，不反馈不转移。
- 分支反馈：Ok → 成功反馈「已截图：<标题>（保存阶段 5 接入）」并 Dispose 图、`CompleteCapture` 回 Idle；SuspectProtected → 错误反馈「该窗口可能受系统保护，截不到画面」、`FailCapture`；WindowGone → 反馈其 Message、`FailCapture`；Failed → 反馈其 Message、`FailCapture`。全部回 Idle 路径都经既有 `OnStateChanged` 集中点（停钩幂等）。

## 补接线：Waiting 会话代次守卫（Task 3 审查 M-3，本任务必做项）

做法（事件携带会话号方案）：
- `HookManager` 复用其内部既有钩子会话号（`HookSession.Id`，每次新建会话自增）：`Start(Rectangle)` 改为返回 `int` 会话号——新建会话返回新号；已在运行（Waiting 内切换设备）只更新 SelfBounds 并返回**现行**会话号，不误伤当前会话。
- `ExternalClick` 事件签名由 `Action<Point>` 改为 `Action<Point, int>`：Up 投递时在闭包内捕获所属 `session.Id` 随事件带回（投递走 SynchronizationContext.Post，值在投递时刻已固化，不受后续会话变化影响）。
- `MainForm` 新增字段 `_waitingSessionId`：`OnStateChanged` 进 Waiting 成功 Start 后记录返回值；`RestartHooksWhileWaiting`（Stop+Start 重启）后更新为新会话号；Start 失败路径清 0。`OnHookExternalClick(pt, sessionId)` 在切回 UI 线程后、状态检查之前先比对 `sessionId != _waitingSessionId` → 直接丢弃（不解析窗口、不转移状态、不重启钩子）。
- CancelRequested / TimeoutElapsed 未携带会话号：二者迟到最坏后果是多一次 Cancel（状态机非 Waiting 时 Cancel 返回 false 且无副作用）或超时后自停，风险与本守卫目标（迟到点击误触发截图）不同，本轮按任务书范围只守 ExternalClick。

## 遗留（待 Windows 实机，本机仅编译通过）

PrintWindow/BitBlt/CopyFromScreen/钩子会话号投递均为 Windows 专属路径，本机 Mono 只保证编译，下列逐项待实机（对应开发步骤阶段 4 过关标准）：

1. **七格矩阵逐格验收**：目标窗口分别处于「被其他窗口遮一半、完全未遮挡、位于右下角与小组件重叠、在副屏（负坐标）上、所在屏缩放 150%、最大化、普通窗口化」七种情形下，产出图均须完整、无阴影黑边、无小组件残影且文字清晰；黑图路径须给提示且不落盘黑图。
2. **补测六项**：跨屏不同缩放、纯色合法窗口、目标移动（本实现对应 WindowGone「窗口已移动，请重试」分支）、置前被拒（对应 Failed 文案分支）、窗口部分出屏、PrintWindow 阻塞（对应 3 秒超时 + 迟到回收表路径，须实测迟到位图被回收、无句柄/内存泄漏、`LateAttemptCount` 归零）。
3. **DWM 裁边实机核对**：透明边窗口（资源管理器/浏览器类）裁后无透明边、无内容截断；DWM 查询失败回退整幅的行为核对。
4. **M-3 守卫实机验证**：上一 Waiting 会话迟到的 ExternalClick（如重启钩子瞬间在途的点击）被丢弃、新会话点击正常进 Capturing；Waiting 内切换设备不误杀当前会话事件。
5. **BitBlt 自遮挡时序**：组件与目标重叠时 Opacity 隐藏 + 60ms 等待足够消除残影；置前核查在 UAC/管理员窗口组合下行为（组件为管理员启动，置前一般应成功，被拒分支待触发验证）。
6. **PrintWindowOnly / BitBltOnly 强制档**三档模式实机各跑一轮：Only 档不切换方法、超时/黑图分别落 Failed / SuspectProtected。
7. **R4 既有偏差**（计划裁决，非本任务新增）：黑图无独立预览确认窗，回退亦黑时提示不落盘；待实机评估后补。

## 修复轮（审查 Minor 小修）

针对审查报告第二节 Minor-1 与 Minor-3 两项顺手修（Minor-2 按审查意见仅记录备考、不修）：

- **Minor-1（裁边异常释放）**：`src/Capture/WindowCapture.cs` 的 `TryPrintWindow` 裁边分支收拢为 try/finally——`bmp.Clone(crop.Crop, bmp.PixelFormat)` 的结果先落局部变量，`finally` 中 `bmp.Dispose()`，再赋值给 `result`。常态路径行为不变（Clone 成功仍释放原图、改用裁剪图）；Clone 抛异常时原图在 finally 中释放、异常继续上抛至 MainForm 转 Failed，不再漏放。此处 bmp 为 PrintWindow 已返回后的本地所有权，与超时迟到路径严格区分：迟到路径的 bmp 所有权在迟到回收表（`LateAttempts`），本改动不触及该分支，迟到位图不会被提前释放。
- **Minor-3（黑图最紧界值用例）**：`tests/BlackFrameDetectorTests.cs` 新增一条「221 黑 4 白（98.22% > 98%）→ 可疑」：构造法与既有 222 用例同法——测试内按实现同口径公式 `(i+0.5)/15` 算出 225 个采样坐标，先全黑再精准涂白前 4 个采样点，留 221 个黑/透明采样点。已核对测试辅助公式与实现 `BlackFrameDetector` 的采样坐标公式逐字同口径（`(gx + 0.5) * Width / (double)GridSize`，越界夹末像素），221/225 = 98.22% > 98% 阈值，`IsSuspect` 实测为 true，未改阈值。
- **验证**：亲跑 `bash build.sh` 退出码 0、0 错误；用例总数 **147**（146 + 新增 1），通过 147、失败 0，既有用例无回归。全程 C# 7.2 语法（try/finally 与局部变量，无新语法）。
