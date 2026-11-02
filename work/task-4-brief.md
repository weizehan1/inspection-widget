# Task 4 简报：截图引擎

项目：巡检截图小组件 InspectionWidget（C# 7.3 / .NET Framework 4.8 / 零第三方依赖）。
工作目录：~/workspace/inspection-widget-src。前置：Task 0–3 已过关（NativeMethods 已有 user32 基础声明、WindowResolver 可用、MainForm 有 `OnTargetResolved(result, requestId)` 接入点且状态机可进 Capturing）。
权威：docs/实施计划.md Task 4 节；白皮书 6.2/6.4；开发步骤阶段 4。裁边口径与线程口径均为硬性，逐条落实。

## A. src/Capture/CaptureGeometry.cs（纯逻辑，先 TDD）
```csharp
public struct CropResult { public bool Valid; public System.Drawing.Rectangle Crop; }
public static class CaptureGeometry {
    public static CropResult ComputeCrop(System.Drawing.Rectangle winRect, System.Drawing.Rectangle dwmRect);
}
```
规则：dwmRect 宽或高 ≤0、winRect 宽或高 ≤0→Valid=false、Crop=Empty。正常：Crop = Rectangle(dwm.X-win.X, dwm.Y-win.Y, dwm.Width, dwm.Height)，并夹紧到 winRect 范围内（偏移<0 取 0、右下超出按 win 尺寸截断）；夹紧后宽高 ≤0→Valid=false。Valid=false 时调用方回退整幅 winRect（引擎层处理）。
测试 tests/CaptureGeometryTests.cs：标准偏移 (100,100,816,616)/(107,100,802,609)→(7,0,802,609)；两矩形相同→(0,0,w,h)；负坐标副屏 winRect=(-1200,50,816,616)、dwm=(-1193,50,802,609)→(7,0,802,609)；dwm 空→无效；dwm 部分越界→夹紧结果。

## B. src/Capture/BlackFrameDetector.cs（先 TDD，Mono libgdiplus 真 Bitmap）
```csharp
public static class BlackFrameDetector {
    public static bool IsSuspect(System.Drawing.Bitmap bmp);   // bmp 为 null→true
}
```
算法：15×15 网格均匀采样（坐标按 (i+0.5)/15 比例取整），统计「纯黑 (R=G=B=0) 或全透明 (A=0)」点数，占比 > 0.98 → true。用 GetPixel 实现即可（采样 225 点，性能足够）。注意 GetPixel 对 32bpp ARGB 返回含 alpha；对不透明格式 A=255。
测试 tests/BlackFrameDetectorTests.cs：全黑 100×100→true；全透明→true；全白→false；黑底中央白块（采样必含白点）→false；225 点中 222 黑 3 白（98.67%>98%）→true、220 黑 5 白（97.8%）→false（构造时按采样网格坐标精确涂点：先全黑，再在已知采样坐标涂白 3 或 5 个点）。

## C. src/Capture/WindowCapture.cs
```csharp
public enum CaptureStatus { Ok, SuspectProtected, WindowGone, Failed }
public sealed class CaptureOutcome { public CaptureStatus Status; public System.Drawing.Bitmap Image; public string Message; }
public sealed class WindowCapture {
    // hideSelf/showSelf：BitBlt 防自遮挡时隐藏/恢复组件，由 MainForm 注入（内部用 Opacity）
    public CaptureOutcome Capture(IntPtr hwnd, CaptureMode mode, System.Drawing.Rectangle selfBounds,
        Action hideSelf, Action showSelf);
}
```
流程（同步方法，调用方负责在工作线程调用；类内部不开新线程做 UI 操作，hide/show 由注入方切线程——MainForm 注入时用 Invoke 到 UI 线程执行 Opacity 切换并等待完成）：
1. 复核：!IsWindow(hwnd) 或 IsIconic→WindowGone。取 winRect（GetWindowRect）、dwmRect（DwmGetWindowAttribute DWMWA_EXTENDED_FRAME_BOUNDS=9，失败则 dwmRect=winRect 并在结果 Message 注明「DWM 查询失败，可能含透明边」——Message 仅日志/诊断用）。
2. PrintWindow 路径（mode != BitBltOnly）：按 winRect 尺寸建 Bitmap，GetHdc→CreateCompatibleDC 流程用 Graphics.GetHdc 或直接 BitBlt 风格：标准做法——`using (Graphics g = Graphics.FromImage(bmp)) { IntPtr hdc = g.GetHdc(); bool ok = PrintWindow(hwnd, hdc, 0x00000002); g.ReleaseHdc(hdc); }`。**超时处理**：PrintWindow 同步不可中断——把这一步放到独立后台线程执行，调用线程等待最多 3000ms：超时→标记本次请求失效、**不 Dispose 该位图**（迟到线程仍在用），将位图与线程登记到静态「迟到回收表」，迟到线程完成后自行 Dispose 并摘除；本次直接走 BitBlt 回退（mode==PrintWindowOnly 时超时直接 Failed 并注明）。等待完成且 ok→按 ComputeCrop 裁边（Valid 才裁，无效用整幅）得到结果图（裁剪产生新 Bitmap 后 Dispose 原图）；IsSuspect(结果图)→Dispose 并走回退（PrintWindowOnly 则 SuspectProtected）。
   - 若 PrintWindow 返回 false→走回退（PrintWindowOnly 则 Failed）。
3. BitBlt 回退（mode != PrintWindowOnly）：先 SetForegroundWindow+BringWindowToTop，**核查结果**：调用后用 GetForegroundWindow 比对是否等于 hwnd，不等→等待 100ms 再查一次，仍不等→Failed 且 Message「窗口置前被拒，请手动点开目标窗口后重试」。与 selfBounds 相交→hideSelf()、Thread.Sleep(60)；用 `Graphics.CopyFromScreen(dwmRect.Location, Point.Empty, dwmRect.Size)` 抓屏（源坐标用物理像素）；finally showSelf()。抓后 IsSuspect→Dispose、SuspectProtected（Message 注明可能受系统保护）；否则 Ok。
4. 截图前后复核：抓图完成后再次 GetWindowRect 与开始时比对，位置/尺寸变了→Dispose、WindowGone（Message「窗口已移动，请重试」）。（PrintWindow 与 BitBlt 两路都查。）
5. 所有中间 Bitmap/HDC 用 using/finally 释放；返回的 Image 所有权归调用方。

NativeMethods 追加：PrintWindow、DwmGetWindowAttribute、GetForegroundWindow（若 Task 3 未加）。

## D. MainForm 接线（替换 Task 3 的占位）
`OnTargetResolved`：状态机 BeginCapture 成功后，组 CaptureRequest（Task 5 的保存尚是占位——本任务成功拿到图后先反馈「已截图：<标题>（保存阶段 5 接入）」并 Dispose 图、CompleteCapture 回 Idle）；放工作线程执行 `WindowCapture.Capture`（hideSelf/showSelf 用 Invoke 切 UI 线程改 Opacity）；结果 BeginInvoke 回 UI：Ok→上述反馈；SuspectProtected→错误反馈「该窗口可能受系统保护，截不到画面」；WindowGone→反馈其 Message；Failed→反馈 Message。全部路径 CompleteCapture/FailCapture 回 Idle 且停钩（走既有 OnStateChanged 集中点）。迟到结果校验：回 UI 时比对 requestId 与状态机 CurrentRequestId，不一致直接 Dispose 图并丢弃。

## 验证与提交
- `bash build.sh` 退出 0；几何+黑图测试 PASS、全部既有不回归（报告写总数）。
- commit：`feat(stage-4): 截图引擎（PrintWindow裁边/黑图检测/BitBlt回退）`
- 报告 work/task-4-report.md：状态、TDD 证据、用例总数、哈希、遗留（七格矩阵等实机项如实列）。返回只给状态/哈希/一行摘要/concerns。
全程简体中文。不要派生子代理。

> **补充约束（R6，Task 0 审查裁决）**：全部代码只许 C# 7.2 以内语法（Mono mcs 上限），不得用 7.3 独有语法。
