# 独立复审：仅审查原10项问题的修复及修复引入的新问题

你是原独立审计会话，继续复审自己的原始发现。受审修订的完整SHA由本次任务消息指定；原受审提交 `96165980503e3c764eb4a45811afdbbad54b0553`。

先读原 `hydrogen-audit-findings.json`、`hydrogen-audit.md`，再读 `hydrogen-fix-report.md`、`hydrogen-fix-execution.md`、`hydrogen-audit-fix-brief.md` 与修复diff。对10项逐条裁定 ADDRESSED/NOT_ADDRESSED，区分源码修复、实际复现和Windows未实跑；不要把实施者的报告当结论。

主负责人已经：
- 独立整仓构建439/439通过（此前433）；日志 `/tmp/opencode/inspection-v110/codex-fixed-final-build.log`。
- 重跑原AuditRepro，确认 REPRO1 progress_overwritten=False、REPRO2 recorded=True/savedPaths=1/openFolders=1、REPRO3缺Devices时PendingImportDeviceReplace=False且visitedAfter=1；日志 `/tmp/opencode/inspection-v110/codex-fixed-repro.log`。REPRO4/5核心状态机行为本身不应改变，需检查真实MainForm接线是否修复。
- 发现并让实施者修正了一条Windows测试的日期字面值（应为“2026年11月15日 星期日”）及报告中失败提示语义，不回避Windows用例未实跑事实。

复审重点：
1. 损坏原件保护/显式备份重置/备份失败保留；有效昨日记录正常新轮，不能误报损坏。
2. 普通模式与计划模式的暂存路径/重启目录；不能回退为只有计划启用才记录。
3. 所有进度错误稳定可见且可重试，保存成功与进度失败分开提示；重试恢复后状态清理一致。
4. 托盘不可见不消费资格，恢复再提醒；**按原规格，可见且选窗/截图/保存处理结束后，即使有_pendingImage待重试，也允许一次带失败说明的询问**，不能把待重试本身当永久抑制条件。
5. 只有源实际采用Devices替换才清计数；部分导入不清计数/改名映射。
6–10. 跨日待刷新信号、重复点击取消、Hook边界、DPI字体资源寿命与AutoScroll重排，检查新增Windows用例是否编译、断言是否吻合真实产品入口，而非仅反射设置字段造假。

只对修复diff中的新回归追加发现，不重复开启无关全项目审查。不改src/tests/README/原审计资料，不提交/推送/另派agent；独立复现写 `/tmp/opencode/inspection-v110/review2/`，不覆盖共享bin/obj。本地没有Windows，19条Windows用例尚未运行，保持未验证；不以源码审查代替GUI通过。
新图标已由ImageGen生成，PNG/ICO/base64与AppIcon真实消费已验证；不改图标。此次复审不需要重跑刚通过的全量headless测试。

输出：
- `work/v110/hydrogen-re-review.md`：受审SHA、逐项裁定、实际证据、Windows缺口及结论。
- `work/v110/hydrogen-re-review-findings.json`：仅未解决/新引入的问题，数组 `{severity,title,file,line,detail,fix,status:"open"}`；没有则[]。
- `work/v110/hydrogen-re-review-verdicts.json`：10个对象 `{id,title,verdict:"ADDRESSED"|"NOT_ADDRESSED",evidence,windowsStatus:"untested"|"not-applicable"}`，必须区分“代码已修复”和“Windows验证通过”。

完成后交回Codex最终验收，不自行宣布Windows版可发布。
