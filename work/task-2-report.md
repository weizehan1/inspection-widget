# Task 2 报告：主面板布局逻辑、状态机与窗体

状态：**完成**（待审查）
提交：见文末（提交后回填哈希，以协调者 git log 核对为准）
日期：2026-11-02

## 交付内容

| 文件 | 内容 |
|---|---|
| `src/Capture/PanelLayout.cs`（新） | 纯逻辑：全部布局常量（宽 230 / 标题 36 / 分区 28 / 底部 58 / 设备行 33·距 5 / 凭据行 33·距 8 / 空态行 33）+ `DeviceZoneHeight` / `CredentialZoneHeight` / `ComputeHeight` / `ComputeTop`，签名与简报逐字一致 |
| `src/Capture/PanelStateMachine.cs`（新） | 纯逻辑三态机：`SelectDevice`（Idle→Waiting、同设备再点取消、异设备切换、Capturing 拒绝）、`Cancel`（Capturing 不可取消）、`BeginCapture`（请求号从 1 递增）、`CompleteCapture`/`FailCapture`（非 Capturing 一律 false 不抛异常，迟到回调安全忽略） |
| `src/MainForm.cs`（完整重写） | 无边框置顶面板，详见下方行为清单 |
| `tests/PanelLayoutTests.cs`（新） | 32 用例 |
| `tests/PanelStateMachineTests.cs`（新） | 20 用例 |
| `tests/Program.cs`（改） | 注册上述两组测试 |

## MainForm 行为（对简报 C 节逐条）

- 窗体：`FormBorderStyle=None`、`TopMost=true`、`ShowInTaskbar=true`、`StartPosition=Manual`、宽 230、高由 `PanelLayout.ComputeHeight` 按当前配置算；字体 Segoe UI（遍历已装字体判定，回退 Microsoft YaHei UI）、`GraphicsUnit.Pixel` 13/11px；颜色全用 Global Constraints 令牌常量。
- 标题栏（36）：「巡检截图」13px 加粗 #1B1B1B + 日期（yyyy-MM-dd、11px #616161）+ 齿轮「⚙」设置按钮（占位 MessageBox，Task 7 接真实窗）+「—」最小化（`WindowState=Minimized`，不进托盘）。标题栏及标题/日期标签可拖动，MouseUp 后保存 WindowLeft/Top（BestEffort）。
- 边框：`OnPaint` 自绘——常态 #E0E0E0 1px；Waiting 态强调色 #0067C0 2px + 设备标题行文案改「请点击要截图的窗口」强调色加粗。
- 设备按钮列：按 `config.Devices` 动态生成（高 33、距 5、宽 210、Flat、白底 #E0E0E0 边）；点击走状态机：进 Waiting 时写 `LastDevice` 并保存、按钮变强调底白字；再点同个取消、点异个切换高亮。0 设备显示虚线边弱文本提示「暂无设备，去设置添加」（Label.Paint 自绘虚线框，WinForms 无原生虚线边框样式）。
- 凭据区：标题行整行点击切换折叠，写 `IsCredentialCollapsed` 并 Save（失败不阻塞 UI），高度变化走 `ApplyHeightAnchored`（先算 newHeight → `ComputeTop(Bottom, newHeight, WorkingArea.Top)` → 再改尺寸）。凭据按钮按实际条数生成（高 33、距 8），只显示 Title；`NeedsReset` 显示「Title（需重设）」错误色字、点击 MessageBox 提示去设置；正常条目点击只弹占位提示含「（阶段 6 接入复制）」，未实现剪贴板。0 条显示强调色虚线提示行「未设置凭据，去设置添加」。
- 底部（58，两行）：反馈行 13px，`ShowFeedback(text, isError)`（错误 #C42B1C / 普通 #424242）+ `ShowSuccessFeedback`（#0F7B0F），2 秒 Timer 自动清空（常驻型除外）；根目录行 11px #616161，常驻「保存到：<RootPath>」/「尚未设置保存目录」，点击打开今日文件夹（`yyyy-MM-dd` 子目录），不存在只提示「今天尚无截图」，绝不建空文件夹（Task 5 再加选中最新图）。
- 位置恢复：WindowLeft/Top 与任一屏 WorkingArea 相交才恢复，否则主屏 WorkingArea 右下角。
- 首次根目录：Shown 时 RootPath 为空弹 FolderBrowserDialog（说明「选择巡检截图保存根目录，只需选这一次」）；选定→Save 成功才启用并成功色反馈，Save 失败回滚 RootPath 并错误反馈；取消→错误色反馈「未选目录，截图暂不可用」+ 设备按钮禁用。不设任何默认路径。
- LastDevice：启动时对应按钮弱高亮底 #EFF6FC，状态仍 Idle。
- FormClosing：保存当前 WindowLeft/Top（BestEffort，失败忽略；最小化时不保存）。剪贴板退出兜底未做（Task 6）。

## 补接线（Task 1 审查 Minor-5，已做）

`RefreshConfigState()` 为加载/刷新配置的同一入口（构造加载 + 公开 `ReloadConfig()` 供 Task 7 设置保存后调用）：对 `config.Devices` 跑 `DeviceNameValidator.ValidateAll`；有错误时反馈行以错误色**常驻**提示「设备名单有问题：<错误>，请到设置中修正」（不受 2 秒清空影响），且 `ScreenshotAvailable=false` 禁用全部设备按钮；修正后经同一入口刷新，常驻提示清除、按钮恢复。根目录为空同样令 `ScreenshotAvailable=false`。

## TDD 证据

- **RED**：先写 `PanelLayoutTests`（32）+ `PanelStateMachineTests`（20）并注册，`bash build.sh` 退出 1，错误为预期缺类：
  `PanelLayoutTests.cs(1,24): error CS0234: The type or namespace name 'Capture' does not exist in the namespace 'InspectionWidget'`（PanelStateMachineTests 同）。
- **GREEN**：实现两纯逻辑类 + MainForm 后，`bash build.sh` 退出 0；测试输出 `[PASS]` 120 条、`[FAIL]` 0 条。
- 布局锚点逐个断言全过：(8,10,展开)=851、(8,10,收起)=449、(6,5,展开)=570、(6,5,收起)=373、(10,10,展开)=927、(10,10,收起)=525、(6,0,展开)=406，另加 (0,0) 两态与各区高分项；ComputeTop 含钳制（上沿 0 与 40 两例）与恰好贴沿不钳制。
- 状态机覆盖计划 Step 3 全部转移 + Capturing 中 SelectDevice/Cancel/BeginCapture 重入被拒、Idle/Waiting 时迟到 Complete/Fail 返回 false、请求号跨两轮递增到 2。

## 用例总数

**120 个，全部通过，0 失败**（基线 68 不回归 + 本任务新增 52）。`bash build.sh` 退出 0（xbuild 以 `/p:LangVersion=7.2` 编译，R6 的 C# 7.2 子集由编译器直接验证）。

## 提交

`feat(stage-2): 主面板（布局/状态机/折叠/锚定伸缩）`，哈希以协调者 `git log` 核对为准（本报告随该提交一同入库，回报中给出）。

## 遗留问题 / 说明

1. 窗体视觉与交互（拖动、折叠锚定、等待态边框、首次选目录弹窗）无自动化测试，按简报口径转实机验收表；本机无显示环境未实跑 GUI。
2. 空态提示行的「虚线边」用 Label.Paint 自绘虚线矩形实现（WinForms 无原生虚线 BorderStyle），圆角同样未做（令牌注明近似为准）。
3. 设置按钮与凭据复制均为简报指定的占位（Task 7 / Task 6 接入）；`ReloadConfig()` 已预留给 Task 7 在设置保存后调用，以触发 Minor-5 的恢复路径。
4. 测试控制台输出中文在 Mono 下为乱码（既有现象，基线测试同样），计数以 [PASS]/[FAIL] 标记为准。

## 修复轮（Task 2 审查 I-1 + M-1～M-5）

审查报告 `work/task-2-review.md` 第二节共 1 Important + 5 Minor，全部在 `src/MainForm.cs` 内修复：

- **I-1 拖动落点偏移**：`OnDragMouseDown` 原存 `e.X/e.Y`（相对发送者控件坐标），`OnDragMouseMove` 却按窗体坐标使用，从 titleLabel(10,9)/dateLabel(82,11) 起拖会跳移一个标签偏移量。已改：MouseDown 时经发送者 `PointToScreen(e.Location)` 再窗体 `PointToClient(...)` 换算，`_dragOffset` 统一为相对窗体客户区坐标，MouseMove 逻辑不变。未抽纯函数、未加新用例（换算依赖 WinForms 坐标 API，无头环境不可测，留实机验收）。
- **M-1 位置恢复判据**：`RestorePosition` 由 `IntersectsWith`（1px 相交即恢复）改为面积判据：候选矩形与某个屏幕 WorkingArea 的相交面积（`Rectangle.Intersect` 后宽×高，long 计算）至少达候选面积一半才恢复，否则走原有右下角归位。
- **M-2 常驻名单错误被普通反馈覆盖**：抽出 `ShowDeviceListErrorFeedback()` 供 `RefreshConfigState` 与反馈 Timer 共用；Timer Tick 改为：非常驻反馈计时结束时，若 `_deviceListError` 仍存在则恢复常驻错误提示，否则才清空。设备按钮禁用由 `ScreenshotAvailable`（含名单错误判定）决定、与反馈文案独立，提示与禁用状态保持一致。
- **M-3 窗体 Padding（处置：保留现状，仅 OnPaint 画边框）**：不补 Padding，理由有三——(1) 本窗体全部子控件由 `LayoutSections` 按 PanelLayout 像素公式手动绝对定位（各分区满宽 230、自 (0, y) 起排），无任何 Dock/锚边布局消费 Padding，补上也不会产生简报预期的内缩效果，属无效赋值；(2) Padding 会改变窗体 `DisplayRectangle`，在实机上反而可能让满宽分区的布局基准与 PanelLayout 坐标产生 1px 级错位风险；(3) 本无头环境缺 libgdiplus、WinForms GUI 根本无法运行（尝试实测验证时 `System.Drawing.GDIPlus` 初始化即失败），该风险无法在此验证、更不能带进实机。边框视觉已由 OnPaint 在窗体外矩形精确绘制（常态 1px #E0E0E0、等待态 2px 强调色内缩 1px），与简报视觉目标一致。故保留现状，待实机验收时如确需内缩再议。
- **M-4 字体泄漏**：新增三档缓存字体字段（13 常规 / 13 加粗 / 11，懒加载 getter `Font13`/`Font13Bold`/`Font11`），`BuildStaticLayout`、设备/凭据按钮重建、空态提示行、`RefreshStateVisuals` 全部改用缓存字体，不再每次 `CreateFont`；窗体 `Dispose(bool)` 重写中统一释放三档缓存。按钮/标签仅引用缓存字体，WinForms 控件 Dispose 不释放所赋 Font，不存在重复释放。
- **M-5 根目录未设置文案**：`OnRootLabelClicked` 在 RootPath 为空时反馈由「今天尚无截图」改为「尚未设置保存目录，请先在设置中选择」；今日目录不存在分支仍为「今天尚无截图」，两义已分开。

验证：`bash build.sh` 退出 0（构建 0 错误），测试 **120 个全部通过、0 失败**，与修复前一致、无回归（本轮未新增用例，总数不变）。提交信息：`fix(stage-2): 拖动偏移与审查 Minor 五项`，哈希见回报（以 `git log` 为准）。
