# Task 2 简报：主面板布局逻辑、状态机与窗体

项目：巡检截图小组件 InspectionWidget（C# 7.3 / .NET Framework 4.8 / 零第三方依赖）。
工作目录：~/workspace/inspection-widget-src。前置：Task 0、Task 1 已过关（配置层可用：AppConfig / ConfigStore / DeviceNameValidator）。
权威：docs/实施计划.md Global Constraints + Task 2 节；白皮书第 4 节；设计稿令牌（数值已全部写进本简报，直接用）。

## 纪律
纯逻辑（PanelLayout / PanelStateMachine）严格 TDD：先写测试看 FAIL，再实现看 PASS，证据写报告。窗体代码不写自动化测试，但构建必须过、既有全部测试不得回归。

## A. src/Capture/PanelLayout.cs（纯逻辑，签名为准）
```csharp
public static class PanelLayout {
    public const int PanelWidth = 230;
    public const int TitleBarHeight = 36;
    public const int SectionHeaderHeight = 28;
    public const int BottomStatusHeight = 58;
    public const int DeviceRowHeight = 33;
    public const int DeviceRowGap = 5;
    public const int CredentialRowHeight = 33;
    public const int CredentialRowGap = 8;
    public const int EmptyRowHeight = 33;
    public static int DeviceZoneHeight(int deviceCount);        // n=0→33；n>0→n*33+(n-1)*5
    public static int CredentialZoneHeight(int credentialCount, bool collapsed); // collapsed→0；n=0→33；n>0→n*33+(n-1)*8
    public static int ComputeHeight(int deviceCount, int credentialCount, bool collapsed);
        // = 36 + 28 + DeviceZone + 28 + CredentialZone + 58
    public static int ComputeTop(int currentBottom, int newHeight, int workingAreaTop);
        // = max(workingAreaTop, currentBottom - newHeight)
}
```
布局测试锚点（设计稿高度账，必须逐个断言）：(8,10,展开)=851；(8,10,收起)=449；(6,5,展开)=570；(6,5,收起)=373；(10,10,展开)=927；(10,10,收起)=525；(6,0,展开)=36+28+223+28+33+58=406。ComputeTop：bottom=1000,height=500,top 限制 0→500；bottom=400,height=500→钳制为 workingAreaTop（如 0）。

## B. src/Capture/PanelStateMachine.cs（纯逻辑，签名为准）
```csharp
public enum PanelState { Idle, Waiting, Capturing }
public sealed class PanelStateMachine {
    public PanelState State { get; }
    public string CurrentDevice { get; }       // Waiting/Capturing 时有效
    public int CurrentRequestId { get; }       // 每次 BeginCapture 递增，从 1 起
    public bool SelectDevice(string device);   // Idle→Waiting 记录设备；Waiting 点同设备→取消回 Idle（返回 true，属取消语义，调用方用 State 判断）；Waiting 点异设备→切换并保持 Waiting；Capturing→false 不变
    public bool Cancel();                      // Waiting→Idle；Capturing→false（处理中不可取消）；Idle→false
    public bool BeginCapture();                // Waiting→Capturing，RequestId++；其他态 false
    public bool CompleteCapture();             // Capturing→Idle，清 CurrentDevice
    public bool FailCapture();                 // Capturing→Idle，清 CurrentDevice
}
```
状态机测试覆盖计划 Task 2 Step 3 全部转移，含 Capturing 中 SelectDevice/Cancel 被拒、迟到 Complete 在 Idle 时返回 false 不抛异常。

## C. src/MainForm.cs（完整重写 Task 0 占位）
构造依赖：无参构造内自建 ConfigStore 并 Load（MainForm 是程序入口窗体，保持 Program.cs 的 `new MainForm()` 不变）。
结构与行为（按设计令牌实现，字体用 `GraphicsUnit.Pixel`：标题 13、按钮与正文 13、次要 11；字体名 "Segoe UI" 回退 "Microsoft YaHei UI"；颜色用计划 Global Constraints 的令牌值，`Color.FromArgb` 写常量类或本文件常量）：
- 窗体：FormBorderStyle=None（自绘标题栏）、TopMost=true、ShowInTaskbar=true、StartPosition=Manual、ClientSize 宽 230、高由 PanelLayout 按当前配置算。
- 标题栏（高 36）：短名「巡检截图」（13px 加粗、#1B1B1B）+ 当天日期（11px、#616161、yyyy-MM-dd）+ 右侧设置按钮（齿轮字形「⚙」或文字「设置」小按钮）与最小化按钮（「—」）。标题栏可拖动（MouseDown/Move 实现拖移，拖完保存 WindowLeft/Top 到配置）。
- 设备区标题行（高 28）：「截图」13px #424242（等待态时此行或边框变醒目：等待态把窗体外框画强调色 2px + 标题行文案改「请点击要截图的窗口」强调色加粗）。边框实现：窗体 Padding + OnPaint 画边框，等待态强调色、常态 #E0E0E0。
- 设备按钮列：按 config.Devices 动态生成 Button（高 33、间距 5、全宽减左右边距 10×2=210）：FlatStyle.Flat、底 #FFFFFF 边 #E0E0E0、选中底强调色 #0067C0 白字。点击→状态机 SelectDevice：进 Waiting 高亮；再点同个→取消；点异个→切换高亮。0 设备时显示一行不可点提示「暂无设备，去设置添加」（虚线边、弱文本）。
- 凭据分区标题行（高 28）：左侧「凭据」+ 右侧箭头（展开「˅」/收起「›」），整行点击切换折叠：写 config.IsCredentialCollapsed 并 Save（失败不阻塞 UI），按 Bottom 锚定调整窗体高度（用 PanelLayout.ComputeTop + 当前屏幕 WorkingArea）。
- 凭据按钮列：按 config.Credentials 实际条数生成（高 33、间距 8）：只显示 Title；NeedsReset 条目显示「Title（需重设）」错误色字，点击提示去设置（本任务先 MessageBox 占位，Task 6 接真实复制）；0 条时提示行「未设置凭据，去设置添加」（强调色字、虚线边）。本任务凭据点击只做占位提示「（阶段 6 接入复制）」——不要自己实现剪贴板。
- 底部状态区（高 58，两行）：
  - 反馈行（13px）：ShowFeedback(text, isError) 显示，2 秒 Timer 后清空；成功用 #0F7B0F、错误 #C42B1C、普通 #424242。
  - 根目录行（11px、#616161）：常驻显示「保存到：<RootPath>」（空时显示「尚未设置保存目录」），整行点击=打开今日文件夹：目录存在则 explorer 打开，尚不存在只反馈提示「今天尚无截图」，绝不建空文件夹。本任务先实现打开/提示，Task 5 再加「选中最新图」。
- 位置恢复：Load 后有 WindowLeft/Top 且落在任一屏 WorkingArea 内则恢复，否则主屏 WorkingArea 右下角；初次定位亦同。
- 高度锚定：任何由折叠/条目数变化引起的高度变化，先算 newHeight，Top=PanelLayout.ComputeTop(Bottom, newHeight, wa.Top)，再设 Bounds。
- 首次根目录流程：Load 后 RootPath 为空→Shown 事件里弹 FolderBrowserDialog（说明文字「选择巡检截图保存根目录，只需选这一次」）：选定→写入配置 Save 并反馈；取消→反馈行错误色提示「未选目录，截图暂不可用」+ 设备按钮禁用（Enabled=false），设置里仍可补选（Task 7）。不许默认填任何路径。
- LastDevice：配置有 LastDevice 且在设备列表中→启动时该按钮呈「上次选中」弱高亮（底 #EFF6FC），状态仍 Idle。
- 设置按钮：本任务先弹占位提示（Task 7 接真实 SettingsForm），不要自己写设置窗。
- 最小化按钮：WindowState=Minimized；不进托盘。
- FormClosing：保存当前 WindowLeft/Top（BestEffort，失败忽略）；剪贴板退出兜底 Task 6 接，本任务不做。

## 验证与提交
- `bash build.sh` 退出 0；布局+状态机新测试 PASS 且 Task 1 测试不回归（报告写总数）。
- commit：`feat(stage-2): 主面板（布局/状态机/折叠/锚定伸缩）`
- 报告 work/task-2-report.md：状态、RED/GREEN 证据、用例总数、提交哈希、遗留问题。返回只给状态/哈希/一行摘要/concerns。
全程简体中文。不要派生子代理。

> **补充约束（R6，Task 0 审查裁决）**：全部代码只许 C# 7.2 以内语法（Mono mcs 上限），不得用 7.3 独有语法。
