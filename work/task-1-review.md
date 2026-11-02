# Task 1 审查报告：配置层

审查对象：提交区间 `1ba9e28..995c376`（`feat(stage-1): 配置层（模型/校验/自愈/原子写/互斥）`）
审查材料：`work/task-1-brief.md`、`work/task-1-report.md`、`work/task-1-review-package.md`，并直读四个生产文件与全部测试文件核对 diff 之外细节。
审查方法：只读审查，未改任何生产/测试文件、未提交；复跑 `bash build.sh` 一次，退出码 0，输出用例总数 68、通过 68、失败 0，与报告一致。

## 一、规格符合性：✅ 符合（无规格缺口）

逐条核对结果：

1. **AppConfig 字段与默认值**：字段与简报签名逐一对应——`RootPath`、`Devices`、`Credentials`、`IsCredentialCollapsed`（默认 false）、`AutoClearClipboard`（默认 true）、`CaptureMode`（默认 Auto）、`WindowLeft`/`WindowTop`（`int?`）、`LastDevice`；`CredentialEntry` 为 `Title` + `EncryptedSecret`，`NeedsReset` 带 `[IgnoreDataMember]` 不落盘。`[OnDeserializing]` 回调先置 `AutoClearClipboard=true`、两列表空表、其余标量默认值，反序列化再以 JSON 值覆盖——往返用例证明显式保存的 `AutoClearClipboard=false` 不被默认 true 吞掉，缺字段用例证明缺失时补 true。`CreateDefault()` 返回 `Devices=["设备1","设备2"]`，其余默认，测试全字段核对。✅
2. **DeviceNameValidator 三函数**：`Validate(name, existing)`、`ValidateAll(names)`、`ValidateFullPathLength(rootPath, name)` 签名与简报一致。规则齐全：null/空/纯空格非法；九个非法字符 `\ / : * ? " < > |` 逐个有用例；控制字符（`char.IsControl`）非法；`.`、`..` 非法；尾部空格或点非法；保留名 CON/PRN/AUX/NUL/COM1–COM9/LPT1–LPT9 用 `OrdinalIgnoreCase` 的 HashSet、取第一个点前主名比对，`CON.txt`、`nul.png` 等带扩展名形式均拦下，COM0、CONSOLE 不误伤有用例；与 existing 大小写不敏感查重（`设备A` vs `设备a` 英中文各有用例）；`ValidateAll` 以「逐个对余下全体 Validate」实现内部重名＋逐个合法性，口径正确。`ValidateFullPathLength`：`Path.Combine` 后总长 > 259 返回错误，恰 259 合法、260 非法、深根＋`-99.png` 非法均有用例，与「< 260」口径一致。✅
3. **ConfigPathResolver 判定顺序**：先判回退文件已存在→回退优先（`IsFallback=true`，且此时不调用可写探针），再判 exe 目录可写→主路径，否则回退；路径全走 `Path.Combine`，文件名 `config.json`，回退目录 `<LocalAppData>\InspectionWidget\`。三种情形加「两处不可写仍给回退」均有假探针用例覆盖，顺序与简报一致。✅
4. **ConfigStore.Load 五种分类**：缺文件→`Missing`＋默认；读失败（`IOException`/`UnauthorizedAccessException` 分开捕获）→`IoErrorKept`＋默认＋Error，不动原文件；反序列化异常→先 `BackupCorruptFile` 复制为同目录 `config.corrupt.yyyyMMddHHmmss.json`（重名加序号），备份成功→`CorruptBackedUp`＋默认，备份失败→转 `IoErrorKept` 并在 Error 写明双份错误、全程不写目标文件、原件不被覆盖；解析成功后 null 列表补空表并计 `Defaulted`，其余按简报既定口径文本扫描九个可选字段名、缺任一计 `Defaulted`，全有计 `Ok`。乱码与字段类型错误两条损坏路径均有真实文件用例且断言备份内容＝原件。✅
5. **ConfigStore.Save 原子写＋锁互斥**：可写探针不过→`Saved=false` 且不产文件（有用例）；建目录→取 `<target>.lock` 的 `FileShare.None` 独占流并保持到写完释放，拿不到锁（IOException/Unauthorized）→`Saved=false`、正式文件不动（有用例断言字节不变、释锁后恢复）；锁内以同目录唯一 `config.<guid>.tmp` 写 JSON（DataContractJsonSerializer 写流，默认 UTF-8）、`File.Copy(tmp, target, true)`、`finally` 删 tmp——与简报指定写法逐字对应，成功后无 `.tmp` 残留有用例。✅
6. **C# 7.2 子集（R6）与零第三方引用**：`build.sh` 以 `LangVersion=7.2` 覆盖构建生产与测试两工程并通过，代码未见 7.3 以上语法；全部引用为 .NET Framework 系统程序集，无 NuGet、无第三方包。✅（System.Xml 一项见 Minor-1 口径说明。）
7. **测试测真实行为**：校验器/解析器直调真实静态逻辑；Resolver 的假探针是简报设计的注入依赖，断言对象是 Resolver/Store 的输出而非探针本身；ConfigStore 用例走真实临时目录文件 I/O、真实序列化往返；锁用例以真实 `FileStream` 独占验证互斥。未见「测假对象本身」的用例。✅

## 二、特别核查：报告所述两处测试修正

先说明取证边界：**这两处修正的文字记载并不在 `task-1-report.md` 正文、也不在提交 diff 中**（报告正文只记了路径长度用例的数据笔误修正；两处修正属提交前的 TDD 迭代过程，单提交 diff 只能看到最终形态，过程本身单列于第四节「无法从 diff 验证」）。以下按最终落盘代码评判修正是否合理：

1. **UnauthorizedAccessException 改 IOException 构造：合理，是测试写错、非生产缺陷。** 在 .NET Framework 中 `UnauthorizedAccessException` 直接派生自 `SystemException`，**不是** `IOException` 的子类；而文件被独占/共享冲突在 Mono 与 .NET 运行时实际抛的是 `IOException`（sharing violation）。若测试按 Unauthorized 构造/期望失败场景，与运行时真实异常分类对不上。关键证据：生产代码在三处（Load 读文件、Save 取锁、Save 写文件）都**分别**捕获 `IOException` 与 `UnauthorizedAccessException` 并作相同处置，因此把测试改到 IOException 一侧没有掩盖任何生产分支缺失——换作真实 Unauthorized 发生，生产同样接住。此修正属按平台真实异常分类对齐测试，成立。
2. **NeedsReset 用例断言对象修正：合理，不偷工。** 最终用例（`ConfigStoreTests`「凭据 NeedsReset 不落盘」）断言了两件真实落盘事实：① 保存后的 config.json 原始文本不含 `NeedsReset`；② 用**新 Store 重新 Load 出来的** `r.Config.Credentials[0].NeedsReset == false`。原内存中的 `cfg` 对象在 Save 后本就仍为 true（NeedsReset 是运行时标记，Save 无义务也不应回写改动入参对象），若断言对象是原对象，那测的是一个与「落盘」无关的量、且必然失败——原断言对象确属写错。改到重载对象＋原始文本双断言，是语义正确且更强的测法，与 `[IgnoreDataMember]` 的生产实现互相印证。生产行为与简报一致（标记不落盘、重载为 false）。

结论：两处均为测试侧修正，生产行为无对应规格缺口。

## 三、代码质量：通过（Critical 0 条 / Important 0 条 / Minor 5 条）

- **Minor-1（口径待追认，非实现缺陷）**：`src/InspectionWidget.csproj` 新增 `System.Xml` 引用，与实施计划 Tech Stack「系统程序集仅限六个」的字面清单多一个。按本次审查透镜「零第三方引用」衡量：System.Xml 是 .NET Framework 系统自带程序集、非第三方、非 NuGet，合规；且 `DataContractJsonSerializer` 的基类 `XmlObjectSerializer` 公开签名含 XmlReader/XmlWriter，简报又指定必须用 DCJS，六清单与指定序列化路线本身自相矛盾，实现方已在报告中主动披露并给出 CS0012 实测理由。建议协调者对计划文本做勘误追认（六清单加 System.Xml 例外）；若协调者坚持六清单字面口径，此项需其裁决后再定性，本审查不据此判规格不符。
- **Minor-2（覆盖缺口，报告已部分披露）**：Load 的 `IoErrorKept`（读失败）与「备份失败转 IoErrorKept」两个分支无自动化用例。已直读代码核对逻辑无误（不动原件、Error 信息齐全），报告对前者已说明 Mono/Linux 下难模拟；建议 Windows 验收时补这两条用例。
- **Minor-3（健壮性）**：Save 的写文件段与取锁段只捕获 `IOException`/`UnauthorizedAccessException`，序列化异常（如 `SerializationException`）会穿透为未处理异常而非 `SaveResult{ Saved=false }`。本模型字段固定、无循环引用，实际触发概率极低；后续若顺手，可对写段补一层兜底捕获并回填 Error。
- **Minor-4（已知口径局限，简报既定）**：`Defaulted` 的文本扫描判据在「字段名恰好出现在字符串值里」时可能误判为字段存在（如某设备名就叫 `"AutoClearClipboard"` 并带引号写入值中——实际 JSON 转义下概率极低）。此为简报明确采用的实现口径，不作缺陷计；另 null 列表用例未顺带断言 `Kind == Defaulted`（代码实际会置 Defaulted），属断言可加强项。
- **Minor-5（后续任务提醒，非本层缺口）**：`ConfigStore.Load` 后处理按简报只补空表，不对已加载设备名跑 `ValidateAll`；实施计划 Review Focus 第 1 条「手改出大小写重名设备→加载校验标记并提示」目前在 Task 1 层面只有校验器级用例兜住，加载侧的标记/提示须由后续任务（面板/设置保存链路）接线时落实，请协调者勿遗失此条。

观察（不计级）：Save 后目录常驻空的 `config.json.lock` 文件，属锁文件机制预期产物，简报未要求删（删反而引入竞态）；`ValidateAll` 为 O(n²) 构造余表，设备量级下无实际影响；测试控制台输出中文在本机 Mono 终端呈乱码，为终端编码问题，退出码与计数不受影响。

## 四、无法从 diff 验证（单列）

1. 两处特别核查修正及全部 RED→GREEN 的**发生顺序与过程**：单个提交只呈现最终代码，diff 无法证明迭代过程；报告正文亦未记载这两处修正。本审查只能确认最终代码/断言合理且最终 GREEN 可复现（68/68 已复跑）。
2. **Windows / VS2022 / .NET Framework 实机行为**：本机为 Mono/Linux，锁冲突与权限拒绝在 Windows 上的具体异常类型、缺 System.Xml 时 VS2022 是否同样报 CS0012、真实 `%LocalAppData%` 回退链路，均无法在此环境验证，留待 Windows 验收。
3. 备份失败分支与 Load 读失败分支的**运行时**行为：仅经代码直读确认，无用例实测（见 Minor-2）。

## 结论摘要

规格 ✅、质量通过：签名、默认值、校验规则、路径判定顺序、五种加载分类、损坏自愈与原子写＋锁互斥均与简报逐条对上，68/68 复跑属实；两处测试修正经最终代码核对均为测试侧笔误修正（异常分类对齐运行时、断言对象改到重载结果），未掩盖生产问题；仅余 5 条 Minor（以 System.Xml 清单口径待协调者追认、两个自愈分支缺用例为要）。
