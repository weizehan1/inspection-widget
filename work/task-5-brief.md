# Task 5 简报：保存与命名（TDD）

项目：巡检截图小组件 InspectionWidget（C# 7.3 / .NET Framework 4.8 / 零第三方依赖）。
工作目录：~/workspace/inspection-widget-src。前置：Task 0–4 已过关（WindowCapture 产出 Bitmap；MainForm 有 `OnTargetResolved` 接入点与反馈行）。
权威：docs/实施计划.md Task 5 节；白皮书第 7 节；开发步骤阶段 5。

## 纪律
保存逻辑严格 TDD（临时目录真文件）：先 FAIL 后 PASS。路径长度校验复用 Task 1 的 `DeviceNameValidator.ValidateFullPathLength(rootPath, name)`（签名已定；若不存在说明 Task 1 漏做，在报告中标注并在本任务补齐该函数与测试）。

## A. src/Storage/IClock.cs
```csharp
public interface IClock { DateTime Now { get; } }
public sealed class SystemClock : IClock { public DateTime Now { get { return DateTime.Now; } } }
```
tests 里 FakeClock（可设 Now）。

## B. src/Storage/ScreenshotSaver.cs（签名为准）
```csharp
public sealed class SaveResult {
    public bool Saved; public string FullPath; public bool UsedFallback; public string Error;
}
public sealed class ScreenshotSaver {
    // exeBaseDir / localAppDataDir 注入便于测试；生产构造用 BaseDirectory 与 LocalAppData
    public ScreenshotSaver(string exeBaseDir, string localAppDataDir);
    public SaveResult Save(System.Drawing.Bitmap image, string rootPath, string deviceName, DateTime captureDate);
    public string AllocatePathForTest(string dir, string deviceName); // 见下，命名可为 AllocatePath
}
```
行为（逐条实现、逐条测）：
1. 目标目录 = `<rootPath>\<yyyy-MM-dd>`（captureDate 格式化），不存在则创建。文件名顺延：候选依次 `设备名.png`、`设备名-1.png`、…、`设备名-n.png`，用 `FileStream(path, FileMode.CreateNew)` 原子占用：遇 IOException 且文件已存在（判断：File.Exists(path) 为真）→下一序号；其他 IOException→视为该目录写入失败，进暂存链。有界重试：序号上限 10000，超限报错。
2. 保存动作：先 Allocate 到候选路径（CreateNew 占位空文件），再用另一个流写 PNG？口径修正：CreateNew 建文件后立即用同一 FileStream 交给 `image.Save(stream, ImageFormat.Png)` 编码写入，成功 Flush+关闭后才算成功；写入中途异常→删除本次不完整文件（仅删本次建的）、进暂存链重试同名顺延。绝不覆盖任何既有文件。
3. 暂存链：主目录（根目录下的日期夹）失败→`<exeBaseDir>\FallbackScreenshots\<日期>`→`<localAppDataDir>\InspectionWidget\FallbackScreenshots\<日期>`，每级都走同样的顺延+CreateNew；成功时 UsedFallback=true、FullPath 为实际路径。三级全失败→Saved=false、Error 含「尚未保存」与最后异常摘要；**不释放传入 Bitmap**（调用方持有供重试），函数内也不 Dispose 它。
4. captureDate 归属：文件名与文件夹只用传入的 captureDate，不在函数内取系统时间（跨午夜口径：采集开始时由 MainForm 用 IClock 固定）。
5. 根目录为空字符串/null→直接返回 Saved=false、Error「未设置保存根目录」，不进暂存（无根目录是配置问题不是磁盘故障；暂存只兜磁盘故障）。此条写测试锚定。

## C. 测试 tests/ScreenshotSaverTests.cs（临时目录，测后清理）
内存 Bitmap 用 `new Bitmap(4,4)` 涂色即可（Mono libgdiplus 可用；若本机 libgdiplus 缺失导致 Bitmap 无法构造，在报告中注明并改为测 AllocatePath + 用假图接口——但先尝试真 Bitmap）。
用例：
1. 空目录保存→`<root>/2026-11-02/设备A.png` 存在且非空；
2. 预置 `设备A.png` 后保存→`设备A-1.png`，原文件内容不变；
3. 预置 base、-1、-3→取 -2；
4. FakeClock/直接传不同 captureDate→跨天新文件夹从 base 开始；
5. rootPath 指向不可写位置（如临时目录下建一个**文件**占住路径，使其子目录创建失败）→落 exe 暂存、UsedFallback=true、文件名顺延规则一致；
6. exe 暂存也不可写（同法占位）+ LocalAppData 暂存可用→落三级；
7. 三级全不可写→Saved=false、Error 含「尚未保存」、Bitmap 未被 Dispose（保存后仍可读 Pixel 不抛 ObjectDisposedException）；
8. rootPath 为空→Saved=false 且不产生任何暂存文件。

## D. MainForm 接线
- 状态机进 Capturing 时组 `CaptureRequest { Device, RootPath=当前配置, Date=_clock.Now, RequestId }`（MainForm 持 IClock 字段，默认 SystemClock）；Task 4 的 CaptureOutcome 成功→在工作线程调 ScreenshotSaver.Save→BeginInvoke 回 UI：成功→反馈行「✓ <文件名>」2 秒（若 UsedFallback 反馈改警告式文案「已暂存到 <实际目录>」持续显示不自动消失？口径：反馈行显示「✓ 文件名（已暂存）」并在根目录行下方？简单口径：反馈行文案含实际完整路径，颜色用错误色以引注意，仍 2 秒消失但同时把最近保存路径记到根目录行 tooltip——实现取：反馈行显示 5 秒。写注释说明此口径）；失败→反馈错误色「尚未保存：<Error>」+ 保留 Bitmap 在 MainForm 字段 `_pendingImage` 并提供反馈行点击重试？口径简化：失败时保留 _pendingImage，状态回 Idle，反馈行显示「尚未保存，点击此处重试」且反馈行可点击→用同一 CaptureRequest 日期重试 Save；新一次截图开始时若 _pendingImage 未处理，先 Dispose 旧图。此口径写注释。
- 根目录行点击：打开今日文件夹并选中最新一张图（explorer.exe /select），目录不存在只提示不建夹（Task 2 已有打开/提示，本任务补 /select 最新图）。

## 验证与提交
- `bash build.sh` 退出 0；新测试 PASS、全部既有不回归。
- commit：`feat(stage-5): 保存命名（顺延/CreateNew/两级暂存）`
- 报告 work/task-5-report.md（状态、TDD 证据、用例总数、哈希、遗留）。返回只给状态/哈希/一行摘要/concerns。
全程简体中文。不要派生子代理。

> **补充约束（R6，Task 0 审查裁决）**：全部代码只许 C# 7.2 以内语法（Mono mcs 上限），不得用 7.3 独有语法。
