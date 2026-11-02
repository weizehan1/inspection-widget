# Task 2 修复轮范围复审

复审范围：修复区间 `5127422..cc7e107`（`fix(stage-2): 拖动偏移与审查 Minor 五项`），仅对照原审查第二节 I-1 与 M-1～M-5，不扩大范围。方法：通读修复 diff 包与修复说明，抽查当前 `src/MainForm.cs` 原文，并以 `git diff --name-status 5127422..cc7e107` 核对改动文件清单。协调者已亲跑 build.sh 120/120，本轮未重跑全量构建。

## 逐项核对

### I-1 拖动偏移换算 ✅

- `OnDragMouseDown` 已改：先将 sender 转为 `Control`（为空直接返回），再 `control.PointToScreen(e.Location)` 得到屏幕坐标、经窗体 `PointToClient(...)` 换算后存入 `_dragOffset`，即偏移已统一为相对窗体客户区坐标，不再是相对发送者标签的坐标。
- `OnDragMouseMove` 逻辑与之配套：鼠标屏幕坐标减 `_dragOffset` 设 `Location`。本窗体为 `FormBorderStyle.None`，客户区原点与窗体左上角重合，换算成立；从 titleLabel(10,9)/dateLabel(82,11) 起拖不再跳移一个标签偏移量。逻辑正确，非仅说明层面的声称。

### M-1 位置恢复过半相交判据 ✅

- `RestorePosition` 已弃用 `IntersectsWith`：逐屏用 `Rectangle.Intersect(WorkingArea, candidate)` 求相交矩形，以 `long` 计算相交面积与候选面积，判据为 `overlapArea * 2 >= candidateArea`（相交至少过半，含恰好一半）才恢复，否则走原有主屏右下角归位。无交集时 `Intersect` 返回空矩形、面积为 0，不会误恢复。判据与修复说明一致。

### M-2 名单错误常驻提示在普通反馈后恢复 ✅

- 新增共用入口 `ShowDeviceListErrorFeedback()`，`RefreshConfigState` 与反馈 Timer 均调用它，文案与颜色（错误色常驻）统一。
- Timer Tick 逻辑核过：先 `Stop()`；常驻反馈直接返回不清；非常驻反馈计时结束时，若 `_deviceListError` 仍非空则调用 `ShowDeviceListErrorFeedback()` 恢复常驻错误提示，否则才清空反馈行。普通/成功反馈经 `ShowFeedbackInternal` 置非常驻并起 Timer，2 秒后必走此恢复路径，不会再把名单错误提示覆盖后清空。按钮禁用由 `ScreenshotAvailable`（含名单错误判定）独立决定，与文案状态一致。

### M-3 Padding 处置 ✅

- 处置选择为「保留现状、不补 Padding」，属任务允许的二选一。
- 交代与代码一致：全文件无任何 `Padding` 赋值（源码检索仅二进制产物命中）；边框确由 `OnPaint` 绘制——常态 1px #E0E0E0 于 (0,0,Width-1,Height-1)，等待态 2px 强调色于 (1,1,Width-3,Height-3)（内缩 1px），与修复说明所述一致。说明给出的三条理由（全控件按 PanelLayout 绝对定位、无布局消费 Padding；Padding 改 DisplayRectangle 有 1px 错位风险；无头环境无法验证该风险）与现有布局代码（`LayoutSections` 满宽绝对定位）相符，可接受。

### M-4 字体缓存复用且无新泄漏 ✅

- 新增三档缓存字体（13 常规 / 13 加粗 / 11）及懒加载 getter `Font13`/`Font13Bold`/`Font11`；全文件 `CreateFont` 调用只剩三个 getter 内部各一处。
- 全部原新建点已改用缓存：`BuildStaticLayout` 的局部变量改为引用缓存三档；设备按钮、凭据按钮均用 `Font13`；空态提示行 `CreateHintLabel` 由 static 改为实例方法以取 `Font13`；`RefreshStateVisuals` 改为三元选择 `Font13Bold`/`Font13`，不再每次刷新新建字体。
- 释放路径成立：重写 `Dispose(bool)` 在 disposing 时统一 Dispose 三档非空缓存字体后调 `base.Dispose(disposing)`。重建时旧按钮/提示行的 `Dispose()` 不释放所赋的 `Font`（WinForms 控件语义，控件只引用不持有），故共享缓存字体不存在重复释放，也不会被提前释放后继续使用；无新泄漏引入。

### M-5 空 RootPath 文案 ✅

- `OnRootLabelClicked` 空 RootPath 分支反馈已改为「尚未设置保存目录，请先在设置中选择」（错误色普通反馈），且 return 在今日目录判断之前；今日目录不存在分支仍为「今天尚无截图」。两义已分开，与修复说明逐字一致。

## 附带核查

- **改动范围** ✅：`git diff --name-status 5127422..cc7e107` 仅两条——`src/MainForm.cs`（生产代码仅此一文件）与 `work/task-2-report.md`（修复说明），未动其他生产文件、测试或构建脚本。
- **C# 7.3 语法** ✅：diff 内仅用 `var`、`as`、三元、长整型强转、字符串拼接、属性 getter 等 7.2 子集内语法，未见 7.3 及以上独有语法（如元组新语法之外的 switch 表达式、索引/范围、using 声明等均无）；协调者以 build.sh（LangVersion 受限构建）亲跑 120/120 通过亦为旁证。

## 总评

**修复轮通过**：I-1 与 M-1～M-5 共 6 项全部真正修复（均经 diff 代码逻辑核实，非仅凭说明），附带两项核查（改动范围、C# 语法）亦无问题。未过项数：0。
