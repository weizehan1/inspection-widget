## v1.1.2 — 前端视觉重塑与全界面统一

### 主要更新
- **Warm Slate + Deep Sea Teal 主题系统（`UiTheme` / `PanelLayout`）**：主面板、五页设置窗口、设备编辑器、配置导入导出与内置弹窗全面统一为暖灰纸底（`#F6F5F1`）、纯白卡片（`#FFFFFF`）、凹陷槽位（`#EDEBE4`）与深海青强调色（`#0F5C6E`），待选窗状态使用琥珀提示色（`#B45309`）。
- **今日巡检计划焦点重构**：计划卡片改用 18px 粗体等宽计数器（`X / X`）、圆角胶囊进度条与左侧 3px 强调色条，强化终端 HUD 层次感并保留紧凑面板密度。
- **全套设置与弹窗去模板化**：移除各页面多余 `GroupBox` 边框，改用无边框轻量分区与统一的 `UiTheme.GridStyle` 表格样式；同步更新 `MainDialogs`、已点过管理弹窗与配置导入导出视图。
- **文案与设计契约同步**：去除底部主按钮冗余箭头及状态文案中的机械符号拼接（如 `"打开巡检文件夹"`、`"已复制，30 秒后清空"`），同步更新 `docs/design/v110/DESIGN.md` 与 `docs/design/v110/design-contract.md`。
- **测试扩展**：自动化验证扩展至 **443 条纯逻辑/集成 + 23 条 Windows 窗体 UI 用例（共 466 条）**，并在 GitHub Actions `windows-latest` 全量门禁通过后产出单文件 `InspectionWidget.exe`。
