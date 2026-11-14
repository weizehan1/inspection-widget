# v1.1.0-rc 代码验收与 Windows 验证交付

代码验收结论：439项本地逻辑回归通过；独立Hydrogen复审原10项均ADDRESSED，限定修复diff无新发现。Windows19条UI用例与真实桌面交互尚未验证，因此本结论不等于Windows正式发布验收。

受审产品源码提交：`8fe14eae6bb0f58e9ca2d83e9a6c667bc7ff105f`。之后仅新增协调/验证说明；Codex用Git diff核对当前src/tests/assets/README与受审产品树一致。
原始审计文件保留未关闭的历史状态；修复是否解决以 `hydrogen-re-review-verdicts.json` 与 `hydrogen-re-review.md` 为准，复审新问题清单为空数组。

候选程序SHA256：`a8d5b91bdc5fbd15c38e7af17977c1377cedd65ce2d0b6b96df5d93b7316e8cf`。
验证包：`/tmp/opencode/inspection-v110/InspectionWidget-v1.1.0-rc-windows-validation.zip`，副本在本worktree的 `build/`。
验证包SHA256：`c92f1fcf16d102432e46cd821a6cfd8cc78b4ae746b77e0f5623206ab20bad6a`。
包内有程序exe、外置manifest、测试exe、PowerShell脚本、说明和逐文件哈希清单；无真实配置、凭据或用户截图。ZIP CRC与所有包内文件SHA256已读回核对。

在Windows10/11的可写目录解压后，按README运行 `windows-acceptance.ps1`；自动测试应439+19=458条均通过且无跳过。该脚本尚未在Windows实跑；真实截图、浏览器/目录打开、托盘和混合DPI仍需另外检查。

分支保留 `feat/v1.1.0-inspection-redesign`，worktree保留 `/Users/zach/.paseo/worktrees/1lns2uv4/inspection-widget-v110`。没有合并到main，没有推送、打tag或发布Release。
结构化交付记录：`/tmp/opencode/inspection-v110/delivery.json`、`final-acceptance.json`；构建日志 `codex-fixed-final-build.log`，本地测试 `codex-fixed-final-tests.json`。
