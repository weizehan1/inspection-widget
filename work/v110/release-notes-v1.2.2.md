## v1.2.2 — 全链路可靠性、安全与交互细节全面加固

### 主要更新
- **选窗钩子与截图渲染加固**：`HookManager` 通过 `PeekMessage` 预建线程消息队列、按会话校验超时、同步鼠标/键盘状态、修正延迟清理信号并过滤内部按下后的误触发；`WindowResolver` / `WindowCapture` 过滤同进程与 DWM 隐藏/不可见窗口、改用 `Format32bppRgb` 消除透明通道异常并在屏拷前调用 `DwmFlush`；`TargetHighlightOverlay` 启用 `WS_EX_LAYERED` + `WM_NCHITTEST` 穿透以消除闪烁。
- **凭据快捷键与剪贴板安全增强**：`HotkeySpec` 拦截 `Ctrl+V/C/X/A/Z` 与 `Alt+F4` 等系统保留快捷键并精确采样 Win 键；`SettingsForm` 打开或弹窗期间自动解除全局快捷键并暂停选窗等待；`GlobalHotkeyManager` 跳过待重设凭据；`WindowsPasteSimulator` 增加扫描码与 Alt 菜单屏蔽键并延长粘贴等待；`ClipboardGuard` 以回执引用对齐清理时序，DPAPI 与剪贴板路径显式擦除临时明文字节；`QuickLinkLauncher` 优先以普通权限 `explorer.exe` 启动链接。
- **U 盘同步、贴入与配置校验硬化**：`InspectionFileTransferService` 校验根目录与 U 盘目录重叠、跳过符号链接/联接点并限制递归深度、惰性创建子目录、采用 SHA-256 同内容跳过与不同内容 `-1` 自动顺延、原子临时文件落盘并保留单点文件命名；`ConfigJsonReader` / `ConfigValidator` / `FolderNamePattern` / `DeviceNameValidator` 补齐 12 条链接上限、`\uXXXX` 严格十六进制校验、保留设备名及末尾空格校验、快捷键唯一性与 5 MiB 配置文件保护上限；`InspectionProgressStore` 流式备份损坏文件、`InspectionPlan` 正确分离设备删除与重命名、`ErrorLog` 增加 2 MiB 日志轮转与内部异常记录。
- **主面板与设置窗口交互修复**：修复关闭确认 `e.Cancel` 未复位、销毁时后台保存图片并发释放、跨日状态与等待上下文竞态、`DeviceEditorControl` 行越界及 `BindingSource` 释放、`SettingsForm` `HotkeyTextBox` IME/快捷键穿透与 `AutoScrollMinSize` 同步布局、以及 `ConfigTransferForm` / `MainDialogs` 长文本换行与滚动裁切问题。
- **自动化测试扩展**：逻辑与集成测试扩展至 **467 条**，Windows UI 测试扩展至 **32 条**（共 **499 条**）。
