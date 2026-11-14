# Hydrogen：接手 v1.1.0 剩余实现（用户已授权）

## 当前指令与目标

用户已取消卡滞的混元4，并明确“把其他工作交给反重力的氢原子去执行”。Kimi 已转交用户选定 B 的结果并固化 `docs/design/v110/DESIGN.md`。用户另追加：截图设备打勾后消失，设置也能选择不消失，适配多设备和少设备场景。

你负责本工作区全部剩余实现 P1/P3/P4，包含继承/修复配置链接业务、正式 WinForms 前端按 B 实现、集成已完成的 Planning 模块、隐藏已点过设备设置和回归交付。不要重新征求是否开始或是否选择 A/B；B 已定。复杂细节按规范自行解决，存在实际阻断再向主负责人报告。

工作区：`/Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110`。
Paseo workspaceId：`wks_fa6054276984b28d`。分支：`feat/v1.1.0-inspection-redesign`。
当前已提交 HEAD 为 `3925ded`；本目录有混元4的未提交代码和 Kimi 固化 B 的未提交设计，必须保留并接着做，禁止 reset/clean/批量覆盖。

## 先读这些资料

1. `docs/design/v110/DESIGN.md`：用户已选 B，352px 默认宽，标题/日期独立、链接→计划→凭据→设备，底部主色文件夹入口。包含新增隐藏规则。
2. `docs/superpowers/specs/2026-11-14-next-version-design.md`：全部行为与回归标准，开头执行修订优先于旧角色分工。
3. `work/v110/hy4-business-brief.md`：P1 原始接口与配置交换要求；其“不得改布局/测试入口/构建”等仅针对原混元4的并发限制，对你已解除。
4. `work/v110/deepseek-planning-report.md` 与 `src/Planning/`：P2 已通过 Codex 返修复核，主负责人正式入口整仓375/375通过；复用现有接口，避免重做。
5. `docs/design/v110/preview.html` 的 B 结构作为视觉依据。HTML 是设计参考，实际产品仍为 WinForms。

## 开工现场与恢复材料

已备份当前源码（含未提交文件）、逐文件 SHA256、原始 diff 和 status；路径记录在 `/tmp/opencode/inspection-v110/pre-hydrogen-backup-path.txt`，目录中有 `source-backup.tar.gz`、`manifest.json`、`diff.patch`。保留此备份，不覆盖；若还需备份新范围，只新增带时间戳目录。
主负责人已登记 P1 的三类测试 RunAll 到 `tests/Program.cs`。接手前执行整仓构建退出1，确切错误：`tests/QuickLinkTests.cs(150,35)` CS0012，XmlReader 所属 System.Xml 未被测试工程引用。日志 `/tmp/opencode/inspection-v110/pre-hydrogen-build.log`。先在 `tests/InspectionWidget.Tests.csproj` 补 `System.Xml` 系统引用，再跑完整测试，定位后续失败；不因存在部分代码就宣称 P1 已完成。
现有 P1 文件包括 AppConfig/JsonReader/Store/Transfer/Draft/SettingsForm开头克隆、新 QuickLinkCloner/Validator/Launcher/FolderNavigator 和三类测试。逐项对照原 brief 核验和修正，不重写替换成简化桩。

## 新增功能：已点过设备隐藏/保留

- 设置“已点过设备的显示方式”：`隐藏已点过（适合多设备）` / `保留并显示对勾（适合少设备）`。配置 `HideVisitedDevices : bool`，默认 true（AppConfig 默认构造路径与 OnDeserializing 均覆盖）。只在 PlanModeEnabled 时生效；模式关闭显示全部设备，不丢当日状态。
- 当设备点击被截图状态机接受、MarkVisited 计入后即从未点列表隐藏，而非等保存成功。重建显示不能中断 Waiting/Capturing/Saving、丢失当前设备/请求/钩子；反馈区仍明确展示当前设备与阶段。Esc/右键等取消仍可用，不能依赖已经消失的按钮才能取消。
- 仅过滤视图：不删除 config.Devices、不对 Planning.ReconcileDevices 传过滤名单、不减 TotalCount，不把全部隐藏当成未配置；全隐藏空态写“本轮已全部点过”，仍按完整清单判定完成。
- 提供“查看已点过/只看未点过”临时切换；已点过仍可重截、单项恢复未点。该切换不清计数、不重开通知资格、不自动更改持久设置。
- 恢复未点、重置和次日自动重现对应设备；配置显示方式应用后立即刷新，取消不改正式配置，重启保留已保存设置。
- 完整贯通 ConfigJsonReader 可选字段/严格布尔校验、AppConfigCloner、ConfigStore、SettingsDraft、ConfigTransfer 普通导出/备份/导入预览与应用。MergeDevicesOnly 不覆盖它；源缺字段的非 merge 导入仍保留当前值。

## 业务集成要点

- 按 B 完整重做 MainForm 与 SettingsForm 的布局层，不以只改 panel 宽度、添加几个按钮冒充设计落实。无需追求网页特殊效果，以原生 WinForms 可实现的字体、间距、边界、色彩与状态接近选定预览。
- 不重写核心截图/凭据功能：继续使用原 PanelStateMachine、钩子、异步截图/保存、请求快照/代次、失败重试、文件不覆盖、暂存、凭据清理、写保护与导入预览。
- 设备被接受时获取并持有 plan token；保存回调使用对应请求 token。点击/回调/定时更新前 EnsureCurrentDay；旧轮/旧日结果不可计入新轮。Restore 后按当前配置 ReconcileDevices，改名映射保留勾选，导入替换明确新轮。
- 完成询问不能在最后一项进入 Waiting 的当下弹，必须在选窗/截图/保存结束且没有设置模态窗/隐藏截图过程时再处理；消费通知资格后持久化，失败明确提示。
- 文件夹按钮和完成询问共用实际目录解析；有多个实际保存/暂存目录时提供清楚入口，不能把暂存截图藏掉；未配置/无截图/目录丢失/打不开等文案准确。
- 快捷链接名称和地址可增删改启停排序；不写入真实内网地址，不使用测试中的伪网址当产品默认地址。打开仅 http/https，使用默认浏览器，启动异常可见。
- 设置保留设备/凭据所有编辑、上下移动、应用/保存/取消、配置备份恢复能力，增加快捷链接与隐藏设置。业务逻辑测试不得为了迁就新布局被删除或跳过；外形页数等旧断言可按真实新结构调整，并说明理由。

## 允许修改与禁区

允许：当前 worktree 的 `src/`、`tests/`、`README.md`、相关 `docs/`，新增必要的原生 UI 控件/纯业务服务，`work/v110/hydrogen-*.md`。测试工程只增加系统引用；无第三方运行依赖。可调整版本展示到1.1.0候选版，不发版。
禁止：改原 main 工作区/真实用户 config/凭据/截图目录；启动旧混元4/其他agent；安装新软件或新框架；写假测试/跳过Windows测试来伪装通过；Git reset/clean、提交/推送/merge/tag/release。保留 `.DS_Store`、生成ico等杂项不入交付清单，主负责人收尾清理。
你现在独占共享 bin/obj 的构建权，可直接运行整仓构建，不再等待旧任务的并发锁。主负责人暂不同时修改源码或构建。需要真实 Windows 环境而现场没有时如实列缺口，不声称 HTML 或 Mono 等于 Windows。

## 执行顺序与日志

按下面顺序连续推进，每段完成写入 `work/v110/hydrogen-execution.md`；不必逐段等用户批准（用户已授权剩余实现）。
1. 修复/验明 P1，运行原P1测试与现有回归。
2. 贯通 HideVisitedDevices 配置与纯显示过滤规则，先补行为用例后实现。
3. 按 B 实现正式 UI 并集成 Planning；先保持现有行为入口，再调整布局。
4. 完整回归、更新README、形成产物报告。中途出现明显问题直接修复，再跑相称测试。

日志只记录真实命令、退出码和结果，保存 build/test 输出到 `/tmp/opencode/inspection-v110/hydrogen-*.log`。各次验证绑定当时源码状态；禁止用历史375通过宣称新版本通过。

## 验证与交付判据

macOS 命令：`DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib bash build.sh`，必须退出0，并且测试总数包含P1三类、新隐藏功能及原有P2，失败0。构建工具 Mono/libgdiplus 已由主负责人安装。
Windows 自动化：若可用，MSBuild Release主程序和测试工程，分别运行普通入口与 `--windows-ui`，不能跳过后记通过；若不可用，明确未执行。
必测新增序列：点击接受→隐藏但清单总数不变；取消/失败后仍隐藏且反馈可见；查看已点过→重复截图不增计数；恢复未点→行重现；关闭隐藏→全列表保留对勾；取消设置不生效；重启/跨日；全部隐藏空态与完成通知一次；旧配置/导入缺字段保留显示方式。
对已选 B 的实际 WinForms 外观至少生成可取得的界面证据；不能取得则列为待Windows实测，不宣称设计还原验收完成。

最终交 `work/v110/hydrogen-implementation-report.md`：改动文件、接口、需求覆盖、测试命令/退出码/数量、日志、可执行文件位置、SHA256、已验证与未验证、已知问题；日志和真实源码是主负责人复核依据。产品可执行文件不可作为“发布完成”的证明。
实现后做自查，但不得把自己实现的自查称独立审计；主负责人另安排新的 Hydrogen 审计上下文并最终验收。交付后停止写文件等反馈。
