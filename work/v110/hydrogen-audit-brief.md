# v1.1.0-rc 独立审计任务（只读产品源码）

你是全新的反重力 Hydrogen 审计上下文，没有参与本轮实现；只对实际源码、测试与日志判断，不相信实施报告中的通过结论。

## 对象与证据

- 工作区：当前 `inspection-widget-v110` worktree。
- 基线 `b9044e1`（v1.0.4）；候选提交 **`96165980503e3c764eb4a45811afdbbad54b0553`**。
- 用户要求：可配置默认浏览器链接；每日点击即对勾、重启恢复/跨日新轮；全勾待选窗/保存结束后只询问一次打开实际截图目录；路径入口明确；按选定 B 重做主面板与设置；新增已点过默认隐藏/设置可保留/可查看补截且不改总数。
- 需求 `docs/superpowers/specs/2026-11-14-next-version-design.md`，正式设计 `docs/design/v110/DESIGN.md`，执行授权与细节 `work/v110/hydrogen-handoff.md`。
- 主负责人独立构建命令 `DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib bash build.sh` 退出0，433/433通过；日志 `/tmp/opencode/inspection-v110/codex-candidate-build.log`。Windows GUI尚未运行。
- 自查报告 `work/v110/hydrogen-implementation-report.md` 仅是执行者输入，不代表独立验收。

## 审计重点

检查整个 `git diff b9044e1..9616598 -- src tests README.md`，优先查看真实 MainForm 与 SettingsForm 接线，不能只读纯 controller 测试就下结论。重点验证：
1. 点击接受后立刻隐藏，重建列表不会损坏原状态机/钩子/当前请求；等待态切换、Esc/右键/超时、保存/重试、过期代次与跨午夜仍正确。失败提示不能被普通成功反馈覆盖。
2. 最后一项的完成弹窗必须在保存结果（含失败登记）已处理后才弹；等待/截图/保存/设置窗口/托盘隐藏时的条件正确，通知状态落盘失败及重启不被伪报为已可靠去重。
3. 配置加载/草稿/应用取消/导入导出兼容；设备改名迁移和非合并导入替换的判定符合真实源字段；设置保存后进度写失败能被用户看见。
4. 普通截图模式（计划关闭）与计划模式都能打开真实落盘目录，包含全部暂存、部分暂存、多目录、重启、目录不在；不因 PlanModeEnabled=false 丢掉本轮真实暂存路径。
5. progress.json 读取/恢复/写入错误是否被吞掉，避免无声丢进度或配置一保存就覆盖损坏进度。文件保存异常、写保护恢复与凭据流程是否退化。
6. 真实 UI 结构是否贴近已选 B，长名称/默认多设备/高 DPI 的固定尺寸计算是否明显相交、剪裁；五页设置字段、校验归页、Edit/Escape/Apply 与旧 Windows UI 用例是否仍成立。
7. 测试有无假覆盖（断言与实际保存/回调不一致、用纯方法名叫端到端却未走产品入口）；新功能至少存在必要行为验证。

主负责人初读产生的待验证问题（请独立证实或排除，不机械照抄）：Controller 构造可能丢弃 TryLoad/Restore 错误；MainForm 应用设置的 progressErr 可能被忽略；RecordSaveSucceeded 在计划关闭时直接返回，可能导致暂存目录导航丢失。需要根据完整调用链判断实际影响，不仅引用片段。

## 方法与权限

只读产品源码，不改 src/tests/README/构建设置；不提交/推送/另派 agent/安装软件/访问真实用户 config或截图。可在 `/tmp/opencode/inspection-v110/audit/` 新建独立复现程序和日志，使用实际编译程序集与 fakes 复现问题；不要改共享bin/obj或用替代实现证明问题。
不要重复整仓构建浪费时间，主负责人已经完成；确有新复现需求用独立输出。无Windows环境就标明相应部分未验证，不把源码检查当成实机通过，不要求用户先手动测试才能做本次代码审计。

## 输出

- `work/v110/hydrogen-audit.md`：受审SHA、已检查范围、关键发现、可复现触发与影响、最小修复建议、Windows验证缺口、结论（需修复/代码范围通过/证据不足）。
- `work/v110/hydrogen-audit-findings.json`：数组 `{severity:"high"|"medium"|"low",title,file,line,detail,fix,status:"open"}`；每条有真实文件行号，不列纯风格偏好或假想风险。无发现则空数组。
- `work/v110/hydrogen-audit-evidence.json`：数组 `{name,status:"passed"|"failed"|"untested",note,evidence}`，区分真实复现、源码推断和Windows未测。

完成后交给主负责人确认和返修；自己不修实现，不声称产品已获Windows验收。
