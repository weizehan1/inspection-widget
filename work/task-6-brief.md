# Task 6 简报：凭据模块（TDD 决策逻辑）

项目：巡检截图小组件 InspectionWidget（C# 7.3 / .NET Framework 4.8 / 零第三方依赖）。
工作目录：~/workspace/inspection-widget-src。前置：Task 0–5 已过关（配置层有 CredentialEntry{Title, EncryptedSecret, NeedsReset}；MainForm 凭据按钮为 Task 2 占位、Task 5 已接保存链路）。
权威：docs/实施计划.md Task 6 节；白皮书 8.3；开发步骤阶段 6。清空核对口径（序列号＋令牌同锁核对）为硬性要求。

## 纪律
决策与编排逻辑严格 TDD（假网关/假保护器）：先 FAIL 后 PASS。真实 DPAPI 与真实剪贴板只保证编译通过（Linux 不能真跑），报告里如实标注待 Windows 验收。

## A. src/Credentials/ICredentialProtector.cs + DpapiProtector.cs
```csharp
public interface ICredentialProtector {
    string Protect(string plainText);     // 返回 Base64 密文
    string Unprotect(string base64);      // 失败抛 CryptographicException
}
public sealed class DpapiProtector : ICredentialProtector { ... }
```
实现：`ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser)` → Convert.ToBase64String；Unprotect 逆向（Convert.FromBase64String 失败也要包成 CryptographicException 抛出，统一异常口径）。

## B. src/Credentials/IClipboardGateway.cs + WindowsClipboardGateway.cs
```csharp
public sealed class ClipboardReceipt { public uint Sequence; public string Token; public string Sha256; }
public sealed class ClipboardState { public uint Sequence; public string Token; }
public interface IClipboardGateway {
    ClipboardReceipt WriteCredential(string text);          // 写文本+令牌，返回回执
    bool TryClearIfMatches(ClipboardReceipt receipt);       // 同锁内核对序列号+令牌，一致才清空，返回是否清了
    ClipboardState ReadState();
}
```
WindowsClipboardGateway 实现要点：
- 全部操作须在 STA 线程执行：内部用一个专用 STA 线程 + 同步调用封装（或由 MainForm 在 UI 线程调用——口径：网关内部自建 STA 线程串行执行三个操作，调用方线程无关）。加锁语义：WriteCredential 在一次 OpenClipboard 内完成：EmptyClipboard、SetClipboardData(CF_UNICODETEXT=13)、注册并写入令牌格式 `RegisterClipboardFormat("InspectionWidget.CredentialToken")` 存 GUID 字符串、写入禁历史/云同步格式（格式名 `CanIncludeInClipboardHistory`、`CanUploadToCloudClipboard`，各写一个 DWORD 0 的内存块）；CloseClipboard 前在同一临界区读 GetClipboardSequenceNumber 填回执；Sha256 在锁外对 text 计算亦可（内容哈希不依赖剪贴板状态）。
- TryClearIfMatches：OpenClipboard 后读当前序列号与令牌格式内容，两者与 receipt 都相等→EmptyClipboard 返回 true；任一不符→不清空返回 false；无法打开剪贴板（被占用）→按 50ms 间隔重试 3 次，仍失败返回 false（不冒险）。
- WriteCredential 失败（剪贴板占用）同样 50ms×3 重试，最终失败抛 InvalidOperationException 供上层提示「复制失败」，不得假装成功。
- SetClipboardData 的内存用 GlobalAlloc(GMEM_MOVEABLE) 标准写法，所有权转移给系统后不释放；失败路径自己释放。

## C. src/Credentials/ClipboardGuard.cs（含纯决策，先 TDD）
```csharp
public sealed class ClipboardGuard : IDisposable {
    public ClipboardGuard(IClipboardGateway gateway, bool autoClearEnabled, int clearAfterSeconds = 30);
    public static bool ShouldClear(ClipboardReceipt receipt, ClipboardState current);  // 纯函数
    public void Copy(string text);            // gateway.WriteCredential + 存回执 + 重置定时器（先 Dispose 旧 Timer）
    public bool TryClearNow();                // 立即按回执核对清空（退出兜底与定时到期共用）
    public event Action ClearSkipped;         // 无法核对/不匹配被跳过时的通知（供 UI 提示，可空挂）
}
```
定时用 System.Threading.Timer，到期调 TryClearNow。ShouldClear 规则：receipt 为 null→false；current 为 null→false；Sequence 相等 **且** Token 相等（Ordinal）→true；其余 false。**同文字新复制**：文字相同但 Token 与 Sequence 都已变→false（这是 Review Focus 第 3 条，测试必须锚定：用 Fake 网关模拟「同文字、不同令牌/序列号」断言不清）。
决策测试 tests/ClipboardGuardTests.cs：都同→true；仅序列号变→false；仅令牌变→false；同文字新复制→false；receipt null→false；Copy 两次后只认第二次回执（Fake 网关记录 Clear 调用时校验的 receipt）；TryClearNow 在网关返回 false 时不抛异常。

## D. src/Credentials/CredentialService.cs
```csharp
public enum CopyResult { Copied, NeedsReset, Failed }
public sealed class CredentialService {
    public CredentialService(ICredentialProtector protector, ClipboardGuard guard);
    public CopyResult Copy(CredentialEntry entry);   // Unprotect→guard.Copy；CryptographicException→NeedsReset；其他异常→Failed
}
```
测试 tests/CredentialServiceTests.cs（FakeProtector：Protect=Base64(UTF8) 直存、Unprotect 对非法 Base64 抛 CryptographicException；FakeClipboardGateway 记录写入文本与清空）：正常复制→Copied 且网关注入文本=原文；密文损坏→NeedsReset 且网关未被写入；条目 EncryptedSecret 为空→NeedsReset。

## E. MainForm 接线
- 凭据按钮点击→`CredentialService.Copy(entry)`：Copied→反馈「已复制」；NeedsReset→反馈错误色「该凭据需重新设置，请到设置中重新填写」；Failed→反馈「复制失败，请重试」。
- AutoClearClipboard 取自配置，构造 ClipboardGuard 时传入；设置变更后重建 Guard（Task 7 保存事件里处理，本任务在 MainForm 留 `RebuildClipboardGuard()` 方法并在初始化调用）。
- FormClosing：在原有保存位置逻辑前先调 `_guard.TryClearNow()`（有回执且匹配才清，Guard 内部处理），然后 Dispose Guard。把 Task 2/5 的 FormClosing 逻辑合并到同一处理函数，不许出现两个 FormClosing 订阅。
- 加载配置时凭据条目 EncryptedSecret 为空字符串的，构造时标 NeedsReset=true（与设置层口径一致）；解密失败的运行时标记：Copy 返回 NeedsReset 时同步置 entry.NeedsReset=true 并刷新该按钮显示（内存标记，不落盘）。

## 验证与提交
- `bash build.sh` 退出 0；新测试 PASS、全部既有不回归（总数写报告）。
- commit：`feat(stage-6): 凭据模块（DPAPI/剪贴板令牌核对/30秒清空）`
- 报告 work/task-6-report.md：状态、TDD 证据、用例总数、哈希、遗留（DPAPI 跨机/真实剪贴板时序等实机项）。返回只给状态/哈希/一行摘要/concerns。
全程简体中文。不要派生子代理。

> **补充约束（R6，Task 0 审查裁决）**：全部代码只许 C# 7.2 以内语法（Mono mcs 上限），不得用 7.3 独有语法。
