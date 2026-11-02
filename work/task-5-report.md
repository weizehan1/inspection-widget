# Task 5 执行报告：保存与命名

## 状态

**DONE**（本机可验证部分全过；MainForm 接线中的 explorer /select、反馈行点击重试等 Windows/交互路径仅保证编译通过，实机项见「遗留」）。

实现提交：`cdf9892`（`feat(stage-5): 保存命名（顺延/CreateNew/两级暂存）`，父提交为基线 `237a52d`，已用 `git log --oneline | head -2` 自核）。本报告随其后的报告提交一同入库，哈希以协调者 `git log` 核对为准。

前置核对：`DeviceNameValidator.ValidateFullPathLength(rootPath, name)` 已在 Task 1 交付（`src/Config/DeviceNameValidator.cs`，上限 259），本任务直接复用，无需补齐。

## 交付内容

| 文件 | 内容 |
|---|---|
| `src/Storage/IClock.cs`（新建） | 按简报签名：`IClock { DateTime Now { get; } }` + `SystemClock`（`DateTime.Now`） |
| `src/Storage/ScreenshotSaver.cs`（新建） | `SaveResult { Saved, FullPath, UsedFallback, Error }`；`ScreenshotSaver(exeBaseDir, localAppDataDir)` 注入构造 + 无参生产构造（`AppDomain.BaseDirectory` + `LocalApplicationData`）；`Save(Bitmap, rootPath, deviceName, captureDate)`；`AllocatePath(dir, deviceName)` 与简报别名 `AllocatePathForTest`（同实现） |
| `src/MainForm.cs`（修改） | IClock 字段（默认 SystemClock）、ScreenshotSaver 字段、嵌套 `CaptureRequest`、保存链路与失败重试、根目录行 /select 最新图 |
| `tests/ScreenshotSaverTests.cs`（新建） | 保存测试 9 条（含 FakeClock，临时目录真文件、测后清理） |
| `tests/Program.cs`（修改） | 注册 `ScreenshotSaverTests.RunAll()` |

全程 C# 7.2 以内语法（R6）：`build.sh` 走 xbuild + `/p:LangVersion=7.2` 编译通过，无 7.3 独有语法。

## ScreenshotSaver 行为落实（对照简报 B 逐条）

1. **目标目录与顺延**：目录 = `<rootPath>\<yyyy-MM-dd>`（仅由传入 captureDate 格式化），不存在则创建；候选依次 `设备名.png`、`设备名-1.png`…，`FileStream(path, FileMode.CreateNew)` 原子占用：IOException 且 `File.Exists(path)` 为真 → 下一序号；其他 IOException → 该目录写入失败、进暂存链。序号上限 10000（0..10000），超限报错进下一级。
2. **保存动作**：按简报口径修正——CreateNew 建文件后不关流，直接用同一 FileStream 交 `image.Save(stream, ImageFormat.Png)`，Flush+Dispose 后才算成功；写入中途异常（IO 或 GDI+ 非 IO 异常）→ 删除本次不完整文件（仅删本次建的）、进暂存链在下一级以同名顺延重试。任何路径都不覆盖既有文件（CreateNew 保证）。
3. **暂存链**：主目录 → `<exeBaseDir>\FallbackScreenshots\<日期>` → `<localAppDataDir>\InspectionWidget\FallbackScreenshots\<日期>`，每级同一套顺延+CreateNew；成功时 UsedFallback=true、FullPath 为实际路径。三级全败 → Saved=false、Error =「尚未保存：主目录与两级暂存均写入失败（最后异常 类型：消息）」。**函数内从不 Dispose 传入 Bitmap**（用例 7 锚定：失败后 GetPixel 正常读出）。
4. **captureDate 归属**：文件夹名只用传入 captureDate，Saver 内不取系统时间；跨天由 MainForm 在进 Capturing 时以 IClock 固定（见接线）。
5. **空根目录短路**：rootPath 为 null/空字符串 → 直接 Saved=false、Error=「未设置保存根目录」，不进暂存、不产生任何文件（用例 8 对两种入参都断言 exe/LAD 两级暂存目录未被创建）。

补充（简报纪律行）：保存前复用 `DeviceNameValidator.ValidateFullPathLength(日期夹, 设备名.png)` 做完整路径长度校验，超 259 直接以校验错误失败（命名/配置问题，不进暂存——三级路径长度互不相关，暂存侥幸成功也会让同一设备下次在主目录继续超限）。

## TDD 证据（先 FAIL 后 PASS）

- **RED**：先只写 `tests/ScreenshotSaverTests.cs`（9 条）并注册到 `tests/Program.cs`，运行 `bash build.sh` 退出码 **1**，测试工程编译失败——`CS0234: The type or namespace name 'Storage' does not exist in the namespace 'InspectionWidget'`、`CS0246: The type or namespace name 'IClock' could not be found`（日志 `/tmp/task5-red.log`，实现尚不存在，符合预期）。
- **GREEN**：实现 `IClock`/`ScreenshotSaver` 与 MainForm 接线后，`bash build.sh` 退出码 **0**，9 条新用例全 PASS（日志 `/tmp/task5-green.log`）。真 Bitmap 路径成立：本机 libgdiplus 依赖已在 Task 4 补齐，`new Bitmap(4,4)` 涂色、PNG 编码落盘均真跑，用例 1 还断言了 PNG 签名首二字节 `0x89 'P'`，无需退化为假图接口。

### 9 条用例明细

| # | 用例 | 结果 |
|---|---|---|
| 1 | 空目录保存 → `<root>/2026-11-02/设备A.png` 存在、非空、PNG 签名正确、UsedFallback=false、FullPath 精确相等 | PASS |
| 2 | 预置 `设备A.png`（哨兵字节）→ 落 `设备A-1.png`，原文件逐字节不变 | PASS |
| 3 | 预置 base、-1、-3 → 取最小未占用 -2 | PASS |
| 4 | FakeClock 两天各保存一次 → 2026-11-02、2026-11-03 两夹均从 base 名开始 | PASS |
| 5 | rootPath 被普通文件占住（建子目录必失败）→ 落 exe 暂存，UsedFallback=true；暂存夹预置 base 后仍顺延到 -1 | PASS |
| 6 | root 与 exe 均被文件占住 → 落 LocalAppData 三级 `InspectionWidget/FallbackScreenshots/2026-11-02/设备A.png` | PASS |
| 7 | 三级全被文件占住 → Saved=false、Error 含「尚未保存」、Bitmap 未 Dispose（GetPixel 读出红色不抛 ObjectDisposedException） | PASS |
| 8 | rootPath 空字符串与 null → 均 Saved=false、Error 含「未设置保存根目录」，且 exe/LAD 两级不产生任何暂存目录/文件 | PASS |
| 9 | AllocatePath：CreateNew 真占位 → 依次 base、-1；别名 AllocatePathForTest → -2 | PASS |

不可写模拟口径：以普通文件占住路径（其下创建子目录在 Mono/Linux 与 Windows 上均失败），比只读权限位更贴近「根目录指向坏位置」的真实故障形态。

### 总账

- `bash build.sh` 退出 **0**；**用例总数 156（基线 147 + 新增 9），通过 156，失败 0**，既有用例无回归。

## MainForm 接线落实（对照简报 D）

- **CaptureRequest 组装**：`OnHookExternalClick` 的 Ok 分支在 `BeginCapture()` 成功后组 `_currentRequest = { Device=状态机当前设备, RootPath=_config.RootPath（当前配置）, Date=_clock.Now, RequestId }`；MainForm 持 `IClock _clock = new SystemClock()` 与 `ScreenshotSaver _saver = new ScreenshotSaver()`（生产构造）字段。`SaveResult` 与 Config 层同名，MainForm 用别名 `StorageSaveResult`/`ScreenshotSaver`/`IClock`/`SystemClock` 引用 Storage 类型，未改 Config 侧命名。
- **保存链路**：`OnCaptureCompleted` 的 Ok 分支不再直接 Dispose 图，改调 `BeginSave(image, request, completesCapture: true)`——工作线程调 `_saver.Save(image, request.RootPath, request.Device, request.Date)`，`BeginInvoke` 回 UI 后 `OnSaveCompleted` 才 `CompleteCapture()` 回 Idle。迟到校验（requestId/状态）在 OnCaptureCompleted 既有逻辑不变；窗体关闭时迟到图由工作线程 Dispose。
- **成功反馈**：普通成功 → 成功色「✓ <文件名>」2 秒（既有 ShowSuccessFeedback）。暂存成功按简报实现口径：反馈行**错误色**显示「✓ 文件名（已暂存）：<实际完整路径>」**5 秒**（ShowFeedbackInternal 增加毫秒参数），同时完整路径写入根目录行 tooltip（`_rootToolTip`），提示消失后仍可查；代码注释已写明此口径。
- **失败口径（简报简化版，已注释）**：状态回 Idle；Bitmap 保留在 `_pendingImage`（含 `_pendingRequest`），反馈行常驻错误色「尚未保存，点击此处重试：<详情>」；反馈行 Click（`OnFeedbackLabelClicked`）→ 以同一 CaptureRequest（含原日期）重试，`_saveInFlight` 防重复触发；重试成功后按引用清掉 pending 并释放图；新一次截图开始（`OnTargetResolved` 入口）若 pending 未处理，先 Dispose 旧图。窗体 Dispose 时一并释放 `_pendingImage` 与 tooltip。
- **根目录行点击**：今日夹改以 `_clock.Now` 取日期；目录不存在只提示「今天尚无截图」不建夹（原行为保留）；存在时枚举 `*.png` 按 LastWriteTime 取最新一张，`explorer.exe /select,"<最新图>"` 打开并选中；枚举失败或夹内无图退化为只打开目录。

## 遗留

- MainForm 接线为 Windows/交互路径，本机仅编译通过：explorer /select 选中效果、反馈行点击重试的实际手感、5 秒暂存提示与 tooltip 显示、跨午夜真实归属，均待 Windows 实机验收（与前序任务同口径）。
- 路径长度校验的失败不进暂存为本任务判断口径（见上「补充」），简报未明示；若审查要求改走暂存，只需调整 `ScreenshotSaver.Save` 中该校验的位置即可。
- 暂存成功后根目录行 tooltip 记的是最近一张的完整路径；根目录行正文仍显示配置根目录，未做「最近保存」独立展示（按简报简单口径取舍）。

## 修复轮（审查 Minor-1～4，协调者裁决 R8）

审查提出 4 条现修 Minor，逐条落实如下；本节覆盖上文与路径长度校验相关的旧口径（「补充」段与「遗留」首条中「超限不进暂存」的表述已被 R8 裁决取代）。

- **M-1 重试在途竞态（`src/MainForm.cs`）**：所选做法为「守卫忽略」——`OnTargetResolved` 入口先查 `_saveInFlight`：保存（重试）在途时不 Dispose `_pendingImage`、不发起新截图，直接反馈「正在保存上一张，请稍候」，并把本次已 `BeginCapture` 的状态经 `FailCapture()` 回 Idle（清 `_currentRequest`），待保存完成后再截。另在 `OnSaveCompleted` 失败分支登记 `_pendingImage` 前加图可用性校验（新增 `IsImageUsable` 探针：读已释放 Bitmap 的尺寸会抛异常，借此判失效）：图已失效时不重新登记，清掉同引用的旧登记并提示「尚未保存：原图已失效，请重新截图」，杜绝此后对已释放图反复重试。
- **M-2 防御分支日期口径（`src/MainForm.cs`）**：`OnCaptureCompleted` 中 `_currentRequest` 为 null 或 RequestId 不一致时，原用完成时刻 `_clock.Now` 等现组请求的死代码分支已删除；改为直接 Dispose 图、提示「截图失败，请重试」并 `FailCapture()` 回 Idle，不再存在第二套日期口径（日期只在采集开始组快照时固定）。
- **M-3 注释钉口径（`src/Storage/ScreenshotSaver.cs`）**：`AllocatePath` 文档注释已加注：「本方法仅供测试/内部辅助，Save 主路径不得改走此方法（冲突判据未硬化，见 TrySaveToDirectory 的 created 标志）」，并说明其裸 `File.Exists` 判冲突会把写入阶段失败误判为重名的原因。
- **M-4 路径超限进暂存（R8，`src/Storage/ScreenshotSaver.cs`）**：`Save` 删除「只校验主目录、超限直接失败不进暂存」的旧逻辑，改为暂存链每一级（主目录、exe 暂存、LocalAppData 暂存）各自以 `ValidateFullPathLength(该级目录, 设备名.png)` 校验候选完整路径长度，该级超限视为该级失败并记入 lastError、继续下一级；三级全超限/全失败才返回 `Saved=false`（Error 仍含「尚未保存」与最后一级错误摘要）。

### 修复轮测试

- 新增 1 条用例（`tests/ScreenshotSaverTests.cs`）：「主目录候选路径超 259 → 该级失败进暂存、落 exe 暂存」——深根目录按实际拼出的候选完整路径长度逐层加深、自适应构造至超 259（不写死层数），断言落 exe 暂存、`UsedFallback=true`、文件真实存在。
- 复跑 `bash build.sh`：退出 0，**用例总数 157（基线 156 + 新增 1），通过 157，失败 0**，既有用例无回归。M-1/M-2 为 MainForm 交互路径，本机仅编译通过，竞态时序手感待 Windows 实机验收（与既有遗留同口径）。

## 修复轮二（范围复审 M-1 窄残余：竞态变体 B）

- **残余问题**：`OnSaveCompleted` 失败分支重登记 `_pendingImage` 前只有 `IsImageUsable` 瞬时探针——探针通过与实际登记之间若发生极端交错（图在保存发起后被 Dispose/替换），仍可能把已释放图登记为待重试图，后果为后续重试必失败（不崩溃、下次截图自愈），但状态会脏。
- **修法（仅 `src/MainForm.cs` 的 pending 相关逻辑）**：给待保存图配登记代次 `_pendingGeneration`：`_pendingImage` 每次被 Dispose、替换或清除登记时递增（覆盖窗体 `Dispose`、`OnTargetResolved` 清旧图、保存成功清登记、失败分支清登记/替换旧图/重登记五处）；`BeginSave` 发起时捕获当前代次 `saveGeneration` 并随保存结果回调带回；失败分支重登记前先比对 `saveGeneration == _pendingGeneration` 且 `IsImageUsable(image)`，任一不成立即按既有「原图已失效」口径丢弃该迟到结果（清同引用旧登记并提示重新截图），不再重登记。其他行为语义未变。
- **测试**：复跑 `bash build.sh` 退出 0，**用例总数 157（与修复轮持平，未新增用例），通过 157，失败 0**，无回归。代次守卫位于 MainForm 交互/时序路径，与 M-1 其余部分同口径，本机仅编译通过，极端交错时序仍属 Windows 实机验收范围。
