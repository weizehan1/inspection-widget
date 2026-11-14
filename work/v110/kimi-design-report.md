# Kimi 前端设计交付报告

日期：2026-11-14。仅写指定设计与本报告路径；未修改产品源码、未提交、未安装依赖。

## 产物

- `docs/design/v110/preview.html`：单文件、无网络依赖；A/B 主面板、五个设置分类、八个业务场景、完成询问、凭据折叠、放大文字及模拟保存。
- `docs/design/v110/recommendation.md`：现状依据、推荐 A、尺寸、适配与 WinForms 实现建议。
- `docs/design/v110/design-contract.md`：共用视觉、控件状态映射与唯一待选结构差异。

## 检查

使用本机 `agent-browser` / Chrome 以 `file://` 直接打开，无 HTTP 服务。检查 A/B 正常、等待选窗、保存中、失败、全勾、空名单、写保护和长名单；检查设置五分类及草稿/保存中/保存完成。修复了预览初期方案 A 计划栏重复拼接，以及长内容把底栏挤出的问题。

浏览器脚本结果：各场景均有预期文字且无横向溢出；长名单/放大文字时中间区可滚动、底栏保持在面板内。补查 390px 窄屏设置预览：五个分类可横向滚动，页面无横向溢出。视觉检查截图在 `/tmp/v110-preview-fixed.png`、`/tmp/v110-preview-large.png` 与 `/tmp/v110-settings-narrow.png`，均为临时检查文件。

## 预览复核修复

按主负责人实看反馈，修复 B 底部按钮文字纵向排列、默认三设备/两凭据被遮挡，以及设置输入触发整页重建导致值和焦点丢失。1440 × 980 正常场景下 A/B 默认内容均完整可见，文件夹按钮高 38px、文字单行；长名单仍使用中间滚动区。设置输入现保存到演示草稿，输入时不重建字段，切页返回后名称与地址保留。

已在 Chrome 逐字输入“物资平台入口”和 `https://ops.example.invalid/inspection`，切换“设备 → 快捷链接”后回读值一致，应用按钮可用。复核截图：`/tmp/opencode/inspection-v110/preview-main-fixed.png`、`/tmp/opencode/inspection-v110/preview-settings-fixed.png`。本轮未改产品源码、未提交。

## 未验证 / 遗留

2026-11-14 用户选定方案 B「入口与清单」。已新增 `docs/design/v110/DESIGN.md`，并将 `design-contract.md` 与 `recommendation.md` 标为已选定；HTML 保留 A/B 供对照，不代表仍需再次选择。

没有 Windows 实机，未验证真实 WinForms 字体、DPI、混合多屏、键盘焦点、选窗、截图时隐藏、托盘、剪贴板或文件落盘。HTML 仅验证设计原型交互，不能替代产品测试。

“恢复未点”“重置本轮”等控件在 HTML 中只演示反馈，不实现持久化业务。等待用户选择 A 或 B 后再固化正式 `DESIGN.md` 并实施。
