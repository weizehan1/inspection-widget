# Task 2 审查报告：主面板（布局 / 状态机 / 窗体）

审查范围：提交 c6f2e0f..5127422（feat(stage-2)），对照 work/task-2-brief.md、实施计划 Global Constraints 与协调者补派项（Minor-5 接线）。
方法：通读审查包 + 源码原文（src/Capture/PanelLayout.cs、PanelStateMachine.cs、src/MainForm.cs、tests 全部），独立复跑 `bash build.sh` 并做计数对账。未改任何生产代码、未提交。

## 一、规格结论：✅ 通过

### 1. PanelLayout 常量与公式（✅）
- 九个常量与简报名逐字一致：宽 230、标题栏 36、分区标题 28、底部 58、设备行 33 / 距 5、凭据行 33 / 距 8、空态行 33。
- 公式：`DeviceZoneHeight` n≤0→33，否则 n*33+(n-1)*5；`CredentialZoneHeight` 收起→0、展开 n≤0→33，否则 n*33+(n-1)*8；`ComputeHeight`=36+28+设备区+28+凭据区+58。手工复算锚点全对：(8,10,展开)=36+28+299+28+402+58=**851**；(8,10,收起)=**449**；(6,5,展开)=36+28+223+28+197+58=**570**；(6,5,收起)=**373**；(10,10,展开)=36+28+375+28+402+58=**927**；另测试含 (10,10,收起)=525、(6,0,展开)=406、(0,0) 两态 216/183。**0 凭据展开凭据区=33**（EmptyRowHeight）有断言。
- `ComputeTop` = `Math.Max(workingAreaTop, currentBottom - newHeight)`，与简报一致；测试含上沿 0 钳制、上沿 40 钳制、恰好贴沿不钳制、高度不变 Top 不变。

### 2. PanelStateMachine 转移表（✅）
逐条核代码与 20 条用例，与简报完全一致：
- Idle 点设备→Waiting 记录设备（true）；Waiting 点**同设备→取消回 Idle**（true，调用方以 State 区分）；Waiting 点**异设备→切换保持 Waiting**。
- **Capturing 中 SelectDevice 返回 false 且状态/设备/请求号不变；Cancel 返回 false（处理中不可取消）**；Idle Cancel、Idle BeginCapture 均 false 不变。
- BeginCapture 仅 Waiting 可行，请求号从 1 起递增（两轮递增到 2 有断言）；Complete/Fail 仅 Capturing→Idle 并清设备；**迟到 Complete/Fail 在 Idle（及 Waiting）返回 false 不抛异常**（代码为守卫式判断，无抛异常路径）。

### 3. MainForm（✅，细节偏差见质量 Minor）
- **Bottom 锚定改高**：`ApplyHeightAnchored` 先取当前 `Bottom`，以 `Screen.FromControl(this).WorkingArea.Top` 为上沿调用 `ComputeTop`，再改 ClientSize 并设 Location；折叠切换与 ReloadConfig 共用此入口。✅
- **折叠状态写配置**：凭据标题行整行（含子标签）点击翻转 `IsCredentialCollapsed` → `SaveBestEffort`（try/catch 兜底、失败不阻塞 UI）→ 重建凭据控件 → 更新箭头（展开「˅」/收起「›」）→ 锚定改高。✅
- **首次 RootPath 流程**：`Shown` 一次性守卫（`_rootPromptShown`），仅 RootPath 为空时弹 FolderBrowserDialog（说明文字与简报逐字一致）；选定→Save 成功才启用并成功色反馈，Save 失败回滚 RootPath=null 并错误反馈；**取消→错误色提示「未选目录，截图暂不可用」+ 设备按钮禁用**（`ScreenshotAvailable` 因 RootPath 为空为 false，`RefreshStateVisuals` 逐个置 Enabled=false）。**全文件无任何默认路径**：RootPath 赋值仅「用户选定值」与「回滚 null」两处，AppConfig.CreateDefault 亦为 null。✅
- **位置恢复与越界归位**：WindowLeft/Top 均有值且候选矩形与任一屏 WorkingArea 相交→恢复；否则主屏 WorkingArea 右下角（初次定位同此回退）。FormClosing 与拖动 MouseUp 均 BestEffort 保存位置（最小化时不保存）。✅（相交判定的宽严见 Minor-1）
- **LastDevice 弱高亮**：Idle 且设备==config.LastDevice 时按钮底 #EFF6FC、状态仍 Idle；Waiting 选中项为强调底白字。✅
- **根目录行不建空文件夹**：点击时先判今日目录（yyyy-MM-dd）存在才 explorer 打开，不存在只反馈「今天尚无截图」；全文件无 CreateDirectory 调用。✅
- **ValidateAll 接线（协调者补派项）**：构造加载与公开 `ReloadConfig()` 共用 `RefreshConfigState()` 入口，对 config.Devices 跑 `DeviceNameValidator.ValidateAll`；有错误→反馈行以错误色**常驻**提示「设备名单有问题：<错误>，请到设置中修正」（`_feedbackPersistent` 使 2 秒 Timer 不清空）+ 全部设备按钮禁用；修正后经同一入口刷新可清除常驻提示并恢复按钮。✅
- 其余简报项：无边框/TopMost/ShowInTaskbar/Manual、标题栏短名+日期+设置占位+最小化、等待态 2px 强调边框+标题行文案/加粗/强调色、0 设备与 0 凭据提示行、NeedsReset「Title（需重设）」错误色+点击提示去设置、正常凭据仅占位提示含「（阶段 6 接入复制）」且**未实现剪贴板**、设置按钮占位、Program.cs 入口 `new MainForm()` 未动。✅

### 4. 令牌与约束（✅）
- 字体：`CreateFont` 一律 `GraphicsUnit.Pixel`，13px（标题加粗/按钮/正文）、11px（日期/根目录行）；优先 Segoe UI、遍历已装字体判定后回退 Microsoft YaHei UI，与简报一致。
- 颜色常量与计划 Global Constraints 逐个核对无误：强调 #0067C0、成功 #0F7B0F、错误 #C42B1C、主文本 #1B1B1B、次文本 #424242、弱文本 #616161、面板底 #F9F9F9、边框 #E0E0E0，另 LastDevice 弱高亮 #EFF6FC、按钮底白、选中白字均按简报。
- **C# 7.2 子集（R6）**：build.sh 以 `/p:LangVersion=7.2` 覆盖编译生产+测试工程，本次复跑退出 0，即语言子集由编译器直接验证通过；diff 内未见 7.3 独有语法。
- **无第三方引用**：csproj 仅 System.* 框架程序集（含 R7 追认的 System.Xml），无 PackageReference/NuGet；新增文件 using 仅 System 命名空间。

## 二、质量结论：通过（Critical 0 / Important 1 / Minor 5）

### Critical
无。

### Important
- **I-1 标题/日期标签拖动存在落点偏移（代码可判）**：`OnDragMouseDown` 把 `_dragOffset` 存为相对**发送者控件**的坐标，`OnDragMouseMove` 却以「鼠标屏幕坐标 − _dragOffset」直接设窗体 Location。标题栏 Panel 位于 (0,0) 时无碍，但 titleLabel 位于 (10,9)、dateLabel 位于 (82,11)，从这两个标签起拖（已显式给它们挂了拖动处理器、也是最自然的抓握点）窗体会先跳移一个标签偏移量（日期标签起拖横向跳约 82px），且拖后保存的也是偏移后的位置。修复：MouseDown 时把偏移换算为相对窗体坐标（如 `PointToClient` 或加上发送者在其父容器中的 Location 链）。

### Minor
- **M-1 位置恢复用 `IntersectsWith` 而非「落在 WorkingArea 内」**：候选矩形与工作区只要有 1px 相交即恢复，换屏/分辨率变更后可能恢复出大半在屏外的位置；严格按简报措辞应判完全包含（或至少过半可见）。常态保存的位置不受影响，仅边界情形。
- **M-2 常驻名单错误提示可被后续普通反馈覆盖**：`ShowFeedbackInternal` 无条件把 `_feedbackPersistent` 置 false 并起 2 秒 Timer，若名单有误期间又出现一次普通/成功反馈（如首次选目录成功提示），常驻错误文案会被覆盖并在 2 秒后清空（按钮仍保持禁用，只是提示不再常驻）。建议：存在 _deviceListError 时普通反馈走完后恢复常驻提示，或常驻期间不让 Timer 清空错误行。
- **M-3 未按简报的边框实现细节设窗体 Padding**：简报写「窗体 Padding + OnPaint 画边框」，实现仅 OnPaint 画框（全文件无 Padding 赋值）。视觉效果近似，故仅记 Minor。
- **M-4 GDI 字体未释放**：`RefreshStateVisuals` 每次调用都 `CreateFont` 新建字体赋给 `_deviceHeaderLabel.Font`，旧字体未 Dispose；设备/凭据按钮重建时各按钮的 Font 亦未随按钮 Dispose 释放。低频少量，属长期累积泄漏，建议缓存三档字体复用。
- **M-5 根目录未设置时根目录行文案提示不准**：RootPath 为空点击根目录行反馈「今天尚无截图」，实际语义应为「尚未设置保存目录」一类；不建文件夹的底线未破，仅文案问题。

## 三、无法从 diff / 无头环境验证（须 Windows 实机验收，不计入上述结论）
- 窗体实机视觉：边框 1px/2px 实际渲染、等待态外观、虚线提示框（Label.Paint 自绘）、圆角近似、字体回退的实际呈现、日期标签横坐标 82 与标题「巡检截图」是否重叠或留白过大。
- 拖动实机手感与落点（I-1 的代码缺陷除外）、折叠伸缩时 Bottom 锚定的实机观感、多屏与任务栏位置下 WorkingArea 上沿钳制效果、最小化/TopMost/任务栏显示行为。
- FolderBrowserDialog 在 Shown 时机弹出的实机流程（选定/取消两路）、各 MessageBox 占位、explorer.exe 打开今日文件夹。
- 反馈行 2 秒 Timer 的实机计时与清空（WinForms Timer 在本无头 Mono 环境无法验证）。
- 管理员启动、真实 DPI 下的像素对齐。

## 四、计数对账（专节）：确切总数 = 120（运行时口径），「89」无出处

### 独立复跑证据
- 本审查独立执行 `bash build.sh` 一次：退出码 **0**；日志中「运行测试：tests/bin/Release/InspectionWidget.Tests.exe」**仅出现 1 次**、汇总行仅 1 条；`[PASS]` **120** 行、`[FAIL]` **0** 行，汇总行数字为 总数 120 / 通过 120 / 失败 0。即 build.sh 在单次运行内只跑一遍测试，不存在重复执行叠加计数；协调者亲跑的 120/120 与此一致。

### 计数单位
- 计数单位是 **TestRunner.Run 的一次运行时调用 = 1 个用例**（TestRunner.TotalCount = PassedCount + FailedCount）。一个用例内可含多条断言：全仓 `Assert.` 调用共 221 处，**221 是断言数、不是用例数**。

### 120 的构成（可交叉验证）
- 源码中 `TestRunner.Run` 调用点（静态行数）共 **112** 处：基线五组 60（AppConfig 6 + ConfigPathResolver 5 + ConfigStoreLock 2 + ConfigStore 8 + DeviceNameValidator 39）+ 本任务新增 52（PanelLayout **32** + PanelStateMachine **20**，与报告所称一致，两文件无循环，静态=运行时）。
- 静态 112 与运行时 120 差 **+8**，来源唯一：`tests/DeviceNameValidatorTests.cs` 第 21–24 行有 1 处 Run 写在 `foreach` 循环内，对 9 个非法字符（\\ / : * ? " < > |）各执行一次，静态 1 行 → 运行时 9 条，净 +8。
- 故基线运行时 = 60 − 1 + 9 = **68**（与 Task 1 已验收的 68 一致）；本任务 68 + 52 = **120**，与报告「基线 68 + 新增 52 = 120」的算术自洽。

### 「89」的结论
- 提交 5127422 内的报告原文与当前工作区报告一致，共三处写 120（GREEN 段、总数段、[PASS] 120 条），**全文无「89」**；「89」仅见于 PROGRESS.md 协调者落账的一句转述。
- 任何可复现口径都得不出 89：运行时用例 120、静态调用点 112、断言 221、基线 68、新增 52、控制台去重后 [PASS] 名称行 118（Mono 中文乱码下同名合并所致，亦非 89）。
- 结论：**确切测试总数为 120 个用例（运行时口径），全部通过、0 失败；「89」系落账转述之误**（与 Task 1 曾出现的回报数字不符先例同类），建议协调者据此更正 PROGRESS.md，报告文件本身无需改。

## 五、总评
规格 ✅；质量通过（Critical 0 / Important 1 / Minor 5）。Important-1（标签拖动偏移）建议在 Task 3/4 联调主面板交互前顺修；其余 Minor 可并入后续任务或实机验收表跟踪。
