# Task 1 执行报告：配置层（模型/校验/自愈/原子写/互斥）

状态：**完成**。`bash build.sh` 退出 0，测试 68 个用例全部 PASS、0 失败。

> 说明：本任务执行中途被服务重启打断过一次。重启前已落盘校验器、配置模型与全部测试文件；重启后现场核对发现 ConfigPathResolver / ConfigStore 尚未落盘、且此前一度出现的提交并不存在（HEAD 实际为 2e54a63）。本报告与提交均以恢复后的现场实测为准，不沿用任何中断前的哈希或结论。

## 交付文件

生产代码（命名空间统一 `InspectionWidget.Config`，后续任务按此引用）：
- `src/Config/AppConfig.cs`：`CaptureMode` 枚举、`CredentialEntry`（`NeedsReset` 为 `[IgnoreDataMember]` 运行时标记）、`AppConfig`（`[OnDeserializing]` 先置默认值再反序列化：AutoClearClipboard=true、列表空表；`CreateDefault()` 给两条示例设备「设备1/设备2」）。
- `src/Config/DeviceNameValidator.cs`：`Validate` / `ValidateAll` / `ValidateFullPathLength(rootPath, name)`（root 与 name 直接 Path.Combine，总长 > 259 返回错误；调用方拼日期子目录、顺延后缀时应一并计入，口径已写进函数注释）。
- `src/Config/ConfigPathResolver.cs`：回退文件已存在→回退优先；否则 exe 目录可写→主路径；否则回退。全部 Path.Combine，文件名 config.json，回退目录 `<LocalAppData>\InspectionWidget\`。
- `src/Config/ConfigStore.cs`：Load 分 Missing / Ok / Defaulted / CorruptBackedUp / IoErrorKept 五类（Defaulted 按简报既定口径：文本扫描 JSON 是否含各可选字段名，缺任一即判；列表为 null 另补空表并计 Defaulted）；损坏先备份 `config.corrupt.yyyyMMddHHmmss.json`（重名加序号），备份失败按 IoErrorKept 且不覆盖原件；Save 走「目录可写探针 → 锁文件 FileShare.None 独占 → 同目录唯一临时文件写 JSON → File.Copy 覆盖 → 删 tmp」，拿不到锁或不可写均返回 Saved=false 且正式文件不动。

测试基础设施（零第三方包）：
- `tests/InspectionWidget.Tests.csproj`（控制台 Exe，ProjectReference 到 src 工程，Compile 通配）、`tests/TestFramework.cs`（Assert + TestRunner，退出码=有失败?1:0）、`tests/Program.cs`。
- `tests/DeviceNameValidatorTests.cs`、`tests/AppConfigTests.cs`、`tests/ConfigStoreTests.cs`、`tests/ConfigPathResolverTests.cs`、`tests/ConfigStoreLockTests.cs`。

接线与必要修正（各附原因）：
- `build.sh`：tests 构建已由 Task 0 预留结构自动接上；仅把测试运行通配由 `*.exe` 收紧为 `*.Tests.exe`——tests 输出目录含 ProjectReference 拷贝的主程序 InspectionWidget.exe（GUI、无显示必崩），原通配会误跑它导致脚本假失败（实测踩到）。主程序构建行为未动。
- `src/InspectionWidget.csproj`：新增 `System.Xml` 引用。DataContractJsonSerializer 的基类 XmlObjectSerializer 公开签名含 XmlReader/XmlWriter，不加引用编译报 CS0012（实测）；System.Xml 为 .NET Framework 系统自带程序集，非第三方包。此项与「仅限六个程序集」的清单口径有出入，但不用 DCJS 之外没有别的合规序列化路线，请审查留意。

## TDD 证据（RED → GREEN）

- 组 1（校验器 + 配置模型，中断前执行）：先写 DeviceNameValidatorTests / AppConfigTests，构建报 CS0234「命名空间 Config 不存在」→ 实现 AppConfig + DeviceNameValidator 后通过。其中一条路径长度用例曾因**测试数据算错**失败（根 240 + 后缀总长实为 251，未超限，属合法），已把数据改为 250（总长 261）并注明，属测试笔误修正而非改判据。
- 组 2–4（ConfigStore / 路径判定 / 锁互斥，恢复后执行）：先跑 `bash build.sh` 得真实 RED——退出 1，error CS0246「ConfigStore could not be found」等类型缺失错误（失败原因是功能缺失，非笔误）→ 实现 ConfigPathResolver + ConfigStore → 全量通过。锁用例在 Mono 下真实运行（同进程 FileShare.None 独占亦生效，持锁 Save 返回 false 且正式文件字节不变，释锁后恢复成功）。

## 用例分布（总 68，失败 0）

- DeviceNameValidatorTests：47（非法字符 9 条逐个、控制字符 2、`.`/`..`、尾空格/尾点、保留名与带扩展名形式、COM0/CONSOLE 非保留名、大小写不敏感重名含 `设备A`/`设备a`、ValidateAll 内部重名与逐个合法性、路径长度 259 合法 / 260 非法 / 深根+`-99.png` 非法等）
- AppConfigTests：6（CreateDefault 全字段默认值）
- ConfigStoreTests：8（缺文件 Missing+默认、往返全字段不丢且 Kind=Ok、缺可选字段 Defaulted 且 AutoClearClipboard=true、null 列表补空表、乱码与类型错误走 CorruptBackedUp 且备份内容=原件、Save 后无 .tmp 残留、NeedsReset 不落盘）
- ConfigPathResolverTests：5（主路径 / 回退 / 回退优先 / 两处不可写仍给回退 / 不可写时 Save 失败且不产生文件）
- ConfigStoreLockTests：2（持锁 Save 失败且文件不变、释锁恢复）

全程 C# 7.2 以内语法（R6）：构建走 build.sh 的 LangVersion=7.2 覆盖并通过，可资证明。

## 遗留问题

1. `System.Xml` 引用与原六程序集清单的口径出入（见上），提请审查确认。
2. Load 的 IoErrorKept 分支（读文件 I/O 失败）已按简报实现，但无独立自动化用例：Mono/Linux 下难以可靠模拟「文件存在但读取被拒」（FileShare 不拦读、权限位对 root 无效）。待 Windows 验收时可补。
3. 工作区另有未跟踪的 `work/task-1-review-package.md`（非本任务产物），未纳入本次提交，留待协调者处置。
