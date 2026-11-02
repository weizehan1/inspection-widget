# Task 1 简报：配置层（TDD）

项目：巡检截图小组件 InspectionWidget（C# 7.3 / .NET Framework 4.8 / 零第三方依赖，禁 NuGet）。
工作目录：~/workspace/inspection-widget-src。前置：Task 0 已完成（工程骨架 + build.sh 可用）。
权威规格：docs/实施计划.md 的 Global Constraints 与 Task 1 节；白皮书第 8 节（路径见计划 Spec 行）。

## 纪律（TDD 铁律）
先写测试 → 当场运行看它 FAIL（失败原因必须是功能缺失）→ 写最小实现 → 运行看 PASS → 再下一组。不许先写实现后补测试。测试输出摘要要写进报告。

## 先建测试基础设施
- `tests/InspectionWidget.Tests.csproj`：控制台工程（OutputType=Exe），引用 src 工程（ProjectReference 到 `..\src\InspectionWidget.csproj`），Compile 通配 tests 下 .cs。
- `tests/TestFramework.cs`：微型框架——静态 `Assert`（True/False/Equal/Null/NotNull/Throws）、用例注册（简单起见：每个测试类一个 `RunAll()`，在 `tests/Program.cs` 里逐个调用，统计通过/失败，失败打印用例名与原因，进程退出码=失败数>0?1:0）。
- `tests/Program.cs`：测试运行器入口。
- 同步更新 `build.sh`：构建 tests 并运行，测试失败则脚本非零退出（Task 0 的脚本已预留「存在才跑」，按其结构接上；若结构不合可改 build.sh，但不得破坏 Task 0 的构建行为）。

## 生产代码（四个文件，签名以此为准）

### src/Config/AppConfig.cs
```csharp
public enum CaptureMode { Auto, PrintWindowOnly, BitBltOnly }

[DataContract]
public sealed class CredentialEntry {
    [DataMember] public string Title { get; set; }
    [DataMember] public string EncryptedSecret { get; set; }
    [IgnoreDataMember] public bool NeedsReset { get; set; }   // 运行时标记，不落盘
}

[DataContract]
public sealed class AppConfig {
    [DataMember] public string RootPath { get; set; }
    [DataMember] public List<string> Devices { get; set; }
    [DataMember] public List<CredentialEntry> Credentials { get; set; }
    [DataMember] public bool? IsCredentialCollapsedNullable ... // 不要这种写法
    [DataMember] public bool IsCredentialCollapsed { get; set; }   // 默认 false
    [DataMember] public bool AutoClearClipboard { get; set; }      // 默认 true，见下方默认处理
    [DataMember] public CaptureMode CaptureMode { get; set; }      // 默认 Auto
    [DataMember] public int? WindowLeft { get; set; }
    [DataMember] public int? WindowTop { get; set; }
    [DataMember] public string LastDevice { get; set; }
    public static AppConfig CreateDefault();
}
```
默认处理要点：DataContractJsonSerializer 缺字段时 bool 默认 false，与 `AutoClearClipboard` 默认 true 冲突。解决口径：加 `[OnDeserializing]` 回调先把字段设为默认值（AutoClearClipboard=true、列表初始化为空表）再反序列化；`CreateDefault()` 返回 Devices=["设备1","设备2"]、其余默认。测试必须覆盖「JSON 缺 AutoClearClipboard 字段时加载结果为 true」。

### src/Config/DeviceNameValidator.cs
```csharp
public static class DeviceNameValidator {
    public static string Validate(string name, System.Collections.Generic.IReadOnlyList<string> existing);
    // 返回 null=合法；否则返回给人看的中文错误原因
    public static string ValidateAll(System.Collections.Generic.IReadOnlyList<string> names);
}
```
规则（全部要测）：null/空/纯空格→非法；含 `\ / : * ? " < > |` 任一→非法；含控制字符（char.IsControl）→非法；`.`、`..`→非法；尾部空格或点→非法；保留名 CON/PRN/AUX/NUL/COM1–COM9/LPT1–LPT9（含带扩展名形式如 `CON.txt`、`nul.png`，取第一个点前主名大小写不敏感比对）→非法；与 existing 大小写不敏感重名→非法；ValidateAll 还要查列表内部重名与逐个合法性。

### src/Config/ConfigPathResolver.cs
```csharp
public sealed class ConfigPaths { public string AuthoritativePath; public bool IsFallback; }
public static class ConfigPathResolver {
    public static ConfigPaths Resolve(string exeDir, string localAppDataDir,
        Func<string,bool> fileExists, Func<string,bool> dirWritable);
}
```
判定顺序：回退文件已存在→回退路径优先（IsFallback=true）；否则 exeDir 可写→主路径；否则回退路径。路径一律 Path.Combine，文件名 `config.json`，回退目录为 `<localAppDataDir>\InspectionWidget\`。

### src/Config/ConfigStore.cs
```csharp
public enum ConfigLoadKind { Missing, Ok, Defaulted, CorruptBackedUp, IoErrorKept }
public sealed class LoadResult { public AppConfig Config; public ConfigLoadKind Kind; public string BackupPath; public string Error; }
public sealed class SaveResult { public bool Saved; public string Error; }
public sealed class ConfigStore {
    public ConfigStore();                       // 用 BaseDirectory 与 LocalAppData 解析
    public ConfigStore(ConfigPaths paths, Func<string,bool> dirWritableProbe); // 测试注入
    public string AuthoritativePath { get; }
    public bool IsFallback { get; }
    public LoadResult Load();
    public SaveResult Save(AppConfig config);
}
```
行为：
- Load：文件不存在→Missing + CreateDefault()。读失败（IOException/UnauthorizedAccess）→IoErrorKept + CreateDefault() + Error 记原因，不动文件。反序列化抛异常→CorruptBackedUp：先把原文件复制为同目录 `config.corrupt.yyyyMMddHHmmss.json`（重名则加序号），备份成功才返回默认配置；备份失败→按 IoErrorKept 处理（不覆盖原件、返回默认仅供界面提示且须标注未保存，Error 写明）。加载成功后做后处理：列表为 null 时补空表（计 Defaulted 的一种，但用例以「缺 AutoClearClipboard→true」为主判据）；凭据条目 EncryptedSecret 为空或后续解密失败的标记由 Task 6 负责，本层只保证结构。
  - Defaulted 判据：文件能解析，但至少一个「可选字段」在 JSON 中缺失。实现提示：先用 `System.Runtime.Serialization.Json` 之外的办法检测字段存在性——可用 `System.Web.Extensions`？不在引用清单内。改口径：用 DataContractJsonSerializer 读入后，对比一组哨兵：反序列化前用 `[OnDeserializing]` 设哨兵值的做法对 bool 不适用。**采用实现口径**：先以字符串扫描 JSON 文本是否含各字段名（`"AutoClearClipboard"` 等），缺任一可选字段即 Defaulted。这是本任务的既定实现方式，测试按此判据写。
- Save：以权威路径所在目录建唯一临时文件（`config.<guid>.tmp`），写 JSON（UTF-8），`File.Copy(tmp, target, true)`，删 tmp。写前取得锁文件 `<target>.lock` 的独占 FileStream（FileShare.None）并保持到写完释放；拿不到锁→SaveResult.Saved=false、Error 说明被占用，正式文件不动。两处位置都写不了→Saved=false 且明确 Error（供界面显示「设置未保存」）。
- 路径长度校验不在本层（校验器在设置保存路径上由 Task 7 调用；但 Validate 须提供可复用的单名校验）。

## 测试文件与用例（至少覆盖）
- `tests/DeviceNameValidatorTests.cs`：上面全部规则逐条一用例，含 Review Focus 第 1 条（`设备A` vs `设备a` 重名）。
- `tests/ConfigStoreTests.cs`（临时目录，测后清理）：
  1. 缺文件→Missing+默认（Devices 两条示例、AutoClearClipboard=true）；
  2. Save→Load 往返全字段不丢（含 Credentials 两条、CaptureMode=BitBltOnly、WindowLeft 有值）；
  3. 手写 JSON 缺 AutoClearClipboard 与 WindowLeft→Kind=Defaulted 且 AutoClearClipboard=true、已写字段保留；
  4. 乱码文件→CorruptBackedUp、备份文件存在且内容=原乱码、返回默认；
  5. JSON 字段类型错误（如 Devices 写成字符串）→同损坏路径；
  6. Save 后目录无 .tmp 残留；
  7. 先手动独占锁文件→Save 返回 false 且正式文件内容不变；
  8. Resolver 三种情形（主路径/回退/回退优先）用假探针测。
- Review Focus 第 2 条（路径长度）：在校验器加用例——给出根目录与设备名拼成的完整路径校验函数 `DeviceNameValidator.ValidateFullPathLength(string rootPath, string name)` → 超过 259 字符返回错误。此函数 Task 5/7 复用，签名照此定。

## 验证与提交
- `bash build.sh` 退出 0，测试全 PASS（报告写明用例总数与失败数）。
- commit：`feat(stage-1): 配置层（模型/校验/自愈/原子写/互斥）`
- 报告写 work/task-1-report.md：状态、RED/GREEN 证据摘要（哪组测试先失败后通过）、用例总数、提交哈希、遗留问题。返回消息只给状态/哈希/一行摘要/concerns。
全程简体中文。不要派生子代理。
