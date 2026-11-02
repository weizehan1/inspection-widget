# SDD ledger — plan: docs/实施计划.md

项目：巡检截图小组件（InspectionWidget），工作目录 ~/workspace/inspection-widget-src
约束：不加 remote、不推送 GitHub（用户 2026-11-02 指示）；全程简体中文记录。
规格权威：白皮书 v0.3 终审合并版 ＞ 开发步骤 v0.2 ＞ 设计稿 v3.5（令牌）。
裁决：见 docs/实施计划.md「裁决记录」R1–R5。
SDD skill 文件（subagent-driven-development/SKILL.md）被系统安全检查标记，未采信其内容；执行流程以任务书与 writing-plans / TDD / verification-before-completion 三个 skill 为准：每阶段实现子代理＋审查子代理、闸门不过不进下一阶段、完成结论必须附当场运行证据。

## 环境
- 2026-11-02 夜：Linux（root）。默认 apt 镜像（cogent）拉不到 universe 索引，改用 azure 官方源临时列表（/tmp/azure.list + /tmp/aptlists，未改系统源）装上 Mono 6.8.0.105（mono-devel + libgdiplus）：有 xbuild 与 mcs，无独立 msbuild 命令，build.sh 走 xbuild 回退路径。

## 阶段进度
（每阶段追加：时间、做了什么、闸门证据、遗留问题）

- 计划与简报：docs/实施计划.md 已写并自审（规格覆盖、类型一致性、Review Focus 五条均落到任务）；Task 0–7 简报在 work/ 下。初始提交 3cb364c。
- Task 0（阶段 0 骨架）：complete（提交 3cb364c..7457764，规格 ✅、质量通过，Critical 0 / Important 1 / Minor 3）。证据（审查代理独立复核）：原地与干净构建均 exit 0、警告恰 1 条（工具集不支持 v4.8 的提示，实际引用 /usr/lib/mono/4.8-api）、build/InspectionWidget.exe **4,608 字节**（早前台账误记的 14,336/SHA 出自转述失真，已更正）、git remote 为空未推送。遗留（均属实机项）：管理员提权与 PerMonitorV2 的 Windows 运行时效果待实机；Mono 产物 manifest 为外置形式（R5）。
- **Ruling R6（语言子集）**：Mono 6.8 的 mcs 最高 C# 7.2，build.sh 对 xbuild 链以 LangVersion=7.2 覆盖（csproj 仍写 7.3 供 VS2022）。自 Task 1 起全部生产与测试代码只许用 C# 7.2 以内语法，避免双链分叉；每份后续简报携带此约束。
- Task 0 审查 Minor 处置：.gitignore 补 `build/`（协调者已办）；Program.cs 的 AbandonedMutexException 兜底并入 Task 8；build.sh 的 tests 检测在 Task 1 接线时一并收紧。
- 补充交付要求（2026-11-02 晚用户定）：最终 exe 固定放 build/InspectionWidget.exe 并在 BUILD-REPORT.md 记路径/大小/SHA256；build/ 内另写《试用说明.md》。并入 Task 8/9 执行。
- Task 1 中断事件：实现代理首次回报称 34/34 通过并提交 34b6475，但现场核对（git log/全盘搜索）证实该提交与报告均不存在、ConfigStore/ConfigPathResolver 两个生产文件缺失（服务重启打断所致）。已按 systematic-debugging 定位并恢复原代理续做。恢复后协调者亲验：feat 提交 995c376 真实存在、两文件齐全、亲跑 build.sh 得 68/68 测试通过（首次回报的 34 条口径作废）。教训（已执行）：每个任务的完成结论协调者必须亲自核对 git log 与产物再落账；实现代理回报的哈希两次与实际不符，一律以 git log 实测为准。
- Task 1（阶段 1 配置层）：complete（提交 1ba9e28..995c376，审查：规格 ✅、质量通过，Critical 0 / Important 0 / Minor 5）。证据：审查代理复跑 68/68 与报告一致；两处测试修正经核为测试侧笔误（UnauthorizedAccess 非 IOException 子类、NeedsReset 断言对象应取重载结果），未掩盖生产问题。
  - **Ruling R7（程序集清单勘误）**：`System.Xml` 追认为允许的系统程序集——DataContractJsonSerializer 的公开签名依赖它（CS0012 实测），仍属「.NET Framework 系统自带、零第三方」口径；计划 Tech Stack 的「仅限六个」清单按此勘误。
  - Minor 处置：Save 序列化异常兜底并入 Task 8 简报；IoErrorKept 两分支用例列入 Windows 验收补充；Defaulted 文本扫描为简报既定口径不修；Review Focus 第 1 条（加载侧重名校验提示）已在 Task 2 派发中补接线要求。
- Task 2（阶段 2 主面板）：complete（feat 5127422 + fix cc7e107，审查规格 ✅、质量通过；修复轮范围复审 6/6 全过）。证据：协调者亲跑 120/120 用例通过；高度账与设计令牌逐项对齐；加载侧 ValidateAll 重名接线已落实。遗留实机项：窗体视觉/拖动手感/FolderBrowserDialog 流程等待 Windows 验收（见审查「无法验证」节）。
- Task 3（阶段 3 选窗钩子）：complete（feat a3cd297 + fix 348fd8f，审查规格 ✅、质量通过；修复轮复审两项全过——报告已补全 117 行、M-1 重启逻辑收拢等价）。证据：协调者亲跑 131/131。遗留：钩子全部运行时行为待 Windows 实机逐项验收；M-3（迟到事件会话代次）已写入 Task 4 派发要求；M-2/M-4 记录备查。
- Task 5（阶段 5 保存命名）：complete（feat cdf9892 + fix e6b04ae + 微修 145ca3a；审查规格 ✅、质量通过；两轮范围复审全过——M-1 竞态经变体 B 存活校验补齐、8 个 Dispose 点位全覆盖，M-2/M-3/M-4 按 R8 落实）。证据：协调者亲跑 **157/157**。遗留实机：根目录断开/网络盘等真实故障注入待 Windows 验收。
- Task 6（阶段 6 凭据模块）：complete（feat d5e1b91 + fix fcb8f98；审查规格 ✅；修复轮范围复审 4/4 全过——写入中途失败同锁回滚清空、Guard disposed 守卫、ReadState 非原子注释、异常路径补测均落实）。证据：协调者亲跑 **174/174**。遗留实机：DPAPI 跨机/跨账号解不开、真实剪贴板时序与占用重试待 Windows 验收。
- Task 7（阶段 7 设置窗口）：complete（feat 417853e + fix b85f128；审查规格一处不符经修复轮堵死：新增凭据条目防「清除」绕过必填——UI 禁用兜底 + 保存校验 + 新用例，范围复审 ✅）。计数对账：SettingsFormTests +10 后基线 184，修复 +2，**确切总数 186**（协调者亲跑）。备忘：长审查/报告文件存在被写入截断的故障模式（Task 3 报告、Task 7 审查各一次），后续写文件一律分段。
- Task 8（阶段 8 兜底与验收记录）：complete（a8b197b + a5bb6ca + fix b285328；范围复审 2/2 通过——SetUnhandledExceptionMode 已按 R9 落位、验收记录第 13 项证据已如实降格）。证据：协调者亲跑 **191/191**；docs/验收记录.md 15 项逐项三态标注完成。
- Task 9（阶段 9 本地收口）：complete（docs 提交 8c956b9；本地标签 `v1.0.0` 指向该提交；remote 数 0、无任何推送——协调者亲查）。README 版本记录与实物一致（协调者亲测：build/InspectionWidget.exe = 83,456 字节、SHA256 597ab0ee1f1e6eabac0a4ad65b2f0e592ca59c50f21d9dcd82f84239d8d24e79；重跑构建哈希不变，构建可复现）。build/试用说明.md 已备（六节，用户明早 Windows 试跑用，不入库）。

## 全程收口自检（协调者 2026-11-03 凌晨亲验）

- `bash build.sh` 全量：退出 0，运行时用例 **191 通过 / 191 总数 / 0 失败**。
- 产物：build/InspectionWidget.exe（83,456 字节）＋同名 .exe.manifest（R5 外置清单）。
- git：分支 main、无 remote、标签 v1.0.0 在位；入库红线复查无 config.json/截图/png/日志（仅 build/InspectionWidget.exe.manifest 一个文本清单文件在库，系 R5 交付物，无害保留）。
- 总结报告：BUILD-REPORT.md（仓库根）。
- Task 4（阶段 4 截图引擎）：实现提交 cdfa92e（回报通道曾丢弃，现场核实为准）。协调者亲跑 **146/146**（基线 131 + 新增 15）。审查：规格 ✅、质量通过（Critical 0 / Important 0 / Minor 3）——环境插曲经审查核实属实（libgif7/libexif12 直下 deb 补 libgdiplus 依赖，仅测试环境、不涉产品代码）。Minor-1 与 Minor-3 已小修（fix 6d0db85）并经范围复审两项全过；Minor-2（移动复核与黑图判定顺序）记录备考不修。**Task 4 complete：147/147 用例。** 遗留实机：七格矩阵+补测六项待 Windows 验收。

## CI 接入（2026-11-03，用户拍板「配 GitHub Actions 自动构建」）
- 新增 .github/workflows/build.yml：windows-latest + microsoft/setup-msbuild@v2，MSBuild Release 构建 InspectionWidget.sln，产物 src/bin/Release/InspectionWidget.exe 经 actions/upload-artifact@v4 上传（artifact 名 InspectionWidget，仅 exe 一个文件）；push 到 main、workflow_dispatch 触发构建，推送 v* 标签时另用 softprops/action-gh-release@v2 生成同名 Release 并附 exe。
- 勘查结论：src/InspectionWidget.csproj 本就设有 <ApplicationManifest>..\app.manifest</ApplicationManifest>，MSBuild 构建即内嵌清单，**csproj 无需改动**（Mono xbuild 忽略该属性、本地构建不受影响，此次未跑本地回归是因为零代码改动）；tests 工程为独立控制台 exe（不在 .sln 内），Main 按失败数返回 0/1，用例全为纯逻辑（含 SettingsFormTests 明确不实例化窗体），工作流中单独构建后直接运行、以退出码定成败。
- YAML 已用 python yaml 解析自检通过。本地提交，**未推送、未加 remote**，推送由主代理统一执行；首次云端构建结果待推送后核验（manifest 是否确已内嵌以云端产物实测为准）。

## v1.0.1 迭代（2026-11-03 晨，用户试跑反馈）

- 环境注记：本轮开工时发现 Mono 工具链不在（VM 已替换），按昨晚 PROGRESS 记载的 azure 源临时列表方法重装 Mono 6.8.0.105 + libgdiplus（未改系统源），与昨晚同版本。
- 计划先行：docs/实施计划.md 追加 v1.0.1 三个 Task（提交 e59f74a）。TDD：先写 24 条新用例跑出红灯（缺 UiScale/PanelLayout.Scale 等 API 的编译错误），再实现转绿。
- Task V1 设置窗口居中：SettingsForm 改 Manual + OnLoad 按 Screen.FromControl(Owner).WorkingArea 经 PanelLayout.CenterInWorkingArea 定位；纯函数 3 条用例（居中/副屏偏移/超尺寸钳制）。真实弹出位置待实机。
- Task V2 界面缩放：新增 src/Config/UiScale.cs（五档/归一/因子/索引映射）；AppConfig.UiScalePercent（默认 100，Load 非法归一并标 Defaulted，OptionalFieldNames 已补）；PanelLayout 带 factor 的度量重载（旧签名 1.0 委托）；MainForm 度量经 M()＋因子感知字体缓存＋ApplyScaleToStaticLayout（构造/Reload/保存后各调一次），保存后立即重排、Bottom 锚定不变；SettingsForm 自身按打开时档位缩放（S/P/Z 全几何＋缩放下拉，存「存储与通用」组，保存制，本窗下次打开生效、文案已注明）。
- Task V3 配置落点：ConfigPathResolver 新优先序（同目录已有 config 用它，否则 AppData），dirWritable 探针退出判定；SettingsForm 回退注记改中性文案「（配置文件位于系统 AppData 目录）」。路径用例按新口径重写（4 条优先序＋落盘实证＋不可写不落盘）。
- 修正一条旧用例：全字段往返（ConfigStoreTests）原对象初始化器未含 UiScalePercent（值为 0 被归一判 Defaulted），补 150 并加往返断言——属用例随新字段扩展，非生产问题。
- 闸门证据：`bash build.sh` 退出 0，全量 **215/215 通过**（191 基线零回归＋新增 24）。产物 build/InspectionWidget.exe 88,576 字节，SHA256 54a7eda8202d413cf65a53f02e3ef70d059f4e5043e3f1c62515e1f612b5e3ef；外置 manifest 照旧。验收记录已加 v1.0.1 补记（三条均标待实机及判据）。本地提交收口，不打标签、不推送、无 remote。
