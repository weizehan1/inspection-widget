# v1.1.0 Implementation Plan

> For agentic workers: user selected Kimi 3.1 Pro for frontend, PI Hunyuan4/DeepSeek for implementation, Hydrogen for audit and Codex for final verification. Follow the task briefs and file ownership; do not substitute models or add more agents.

> 2026-11-14 execution amendment: 用户已取消卡滞的混元4，剩余 P1/P3/P4 实现统一交 Hydrogen；已选 B 设计，P2 已通过 Codex 模块复核。按 `work/v110/hydrogen-handoff.md` 接手当前未提交产物，并加入已点过设备隐藏/保留设置；本修订优先于下文旧分工和“待选”措辞。

**Goal:** 重做巡检组件界面，并加入可配置链接、每日点击进度和完成后的目录入口。
**Architecture:** 保留 WinForms 和原截图状态机，新增纯业务服务与独立进度存储，UI 消费接口。PD 设计预览先行；P1/P2 不依赖视觉选择，可先并行；P3 等用户选定具体预览。
**Tech Stack:** C# 7.3（Mono 构建兼容 7.2）、.NET Framework 4.8、WinForms、自定义 TestRunner。
**Spec:** `docs/superpowers/specs/2026-11-14-next-version-design.md`

## Global Constraints
- 单文件 exe，无新增第三方运行依赖，不改变截图/凭据/托盘/写保护的业务契约。
- 对勾表示设备点击已被接受，取消/失败不撤销；保存状态单独反馈。
- 新增 HideVisitedDevices（默认 true），计划模式下点击后隐藏或按设置保留；临时查看已点过不改进度、清单和统计，全部隐藏空态与未配置设备区分。
- 同日恢复、次日新轮、非空清单全勾只询问一次；弹窗不抢选窗和保存焦点。
- 每个执行者只修改自己任务的文件；不提交、推送、发布、安装依赖或另派 agent。Codex 负责登记测试入口和提交。
- 无 Windows 证据的 UI/默认浏览器/资源管理器/混合 DPI 不算验收通过。

## Review Focus
- 快照日期/轮次与午夜旧回调：P2 测试旧 token 不能影响新轮。
- 配置加载/克隆/预览/导出/应用完整往返：P1 测试新增字段及旧配置缺字段。
- 全勾最后一项仍等待截图时不弹：P2 token/请求测试，P3 真入口接线验证。
- 全部暂存/多目录/不存在的目录：P1 注入文件系统检查，不启动外部程序。
- 关闭模式/重启/删项/替换导入：P2 测试不会重复弹窗或错误继承。

## Task PD — Kimi：双方案预览
- [ ] 按 `work/v110/kimi-design-brief.md` 交付自包含预览、推荐和设计契约。
- [ ] Codex 检查文件和交互，将具体方案展示给用户选择；不以预览替代真实 WinForms 验证。

## Task P1 — 混元4：配置、链接与目录服务
Brief: `work/v110/hy4-business-brief.md`
- [ ] 阅读现有配置链路，先补回归用例，记录未实现时的失败（环境不可用须明确记录）。
- [ ] 新增 QuickLinkEntry、QuickLinks、PlanModeEnabled 并贯通配置链路；实现可注入的链接/目录服务。
- [ ] 不改 UI 布局，只允许 SettingsForm 中现有 AppConfigCloner 的字段复制；主负责人后续登记测试入口。
- [ ] 交付报告、精确接口和适用测试；不与 P2 同时构建共享 bin/obj。

## Task P2 — DeepSeek：计划与进度存储
Brief: `work/v110/deepseek-planning-brief.md`
- [ ] 先定义日期/轮次 token、点击计数和完成通知状态的用例，再实现纯逻辑。
- [ ] 独立 progress.json 数据文件，原子写入；写失败返回错误不伪报成功。
- [ ] 旧 token 无效，重启仍恢复已通知标记；配置/设备映射调用明确，不读取 AppConfig 新字段。
- [ ] 报告接口和测试，Codex 统一登记入口及运行集成验证。

## Task P3 — Kimi：正式 UI 实现（待视觉选择）
- [ ] 用户选择设计后固化 DESIGN.md；明确 MainForm/SettingsForm/PanelLayout/UiTheme 归 Kimi。
- [ ] 消费 P1/P2 实际接口，接入所有状态与文件夹入口。
- [ ] Windows 控件/布局测试保留业务断言，按新结构更新页数等外形约束。

## Task P4/P5/P6 — 回归、审计与验收
- [ ] DeepSeek 收集回归与交付包；先由 Codex 核实源码与日志。
- [ ] Hydrogen 审计最终 diff、需求及运行证据，问题退回原执行者，审计必须绑定最终源码 SHA。
- [ ] Codex 独立验收，区分本地逻辑验证、Windows UI 自动化和实机证据，交付候选版。

## Verification Commands
- macOS: `bash build.sh`，日志保存至 `/tmp/opencode/inspection-v110/`，成功需要进程退出 0 和用例失败数 0。
- Windows: MSBuild 主程序与 tests 工程 Release；运行 `InspectionWidget.Tests.exe` 以及 `InspectionWidget.Tests.exe --windows-ui`，均需退出 0。
- 状态/链接/目录新增用例由 Codex 在 `tests/Program.cs` 登记，不能只新增测试文件而不执行。
