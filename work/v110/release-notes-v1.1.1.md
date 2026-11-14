## 巡检助手 v1.1.1（Windows 10/11 · .NET Framework 4.8）

### 主要更新

- **凭据复制态与倒计时反馈**
  - 为 `ClipboardGuard` 增加 `Cleared` 事件与只读参数属性；点击凭据复制后在主面板高亮对应按钮，并在分组栏实时展示 30 秒清空倒计时，清空、外部覆盖跳过或重建守卫时自动复位。
- **稳定性、并发与资源治理**
  - 打开单巡检目录时将文件数量探测与进程拉起移出 UI 线程，避免深目录/慢磁盘卡顿界面；历史记录目录全部失联时自动回退至仍存在的配置根目录。
  - 修复 `ConfigStore` 顶层键检测逻辑（通过 `ConfigJsonReader.TryReadTopLevelKeys` 解析真实 JSON 顶层键）；修正配置根目录为空时残留 tooltip 以及隐藏已点过模式下的取消提示文案。
  - 统一字体能力检测缓存并安全释放遍历产生的 `FontFamily`，清理重绘齿轮图标与窗体（主面板 / 设置 / 导入导出）中的 Icon / 菜单 / 定时器生命周期；对齐主面板内置弹窗的 DPI 缩放。
  - 加固 `HookManager` 会话拆卸锁、`SelfBounds` 读取并发与 `Ready` 事件句柄释放，消除卸载竞态。
- **结构拆解与 CI 门禁**
  - 抽离 `MainDialogs.cs` 与 `SettingsSupport.cs`，降低超大单文件维护成本；补充 4 条纯逻辑与 2 条 Windows UI 回归测试（总计 464 条：443 逻辑/集成 + 21 Windows UI），同步升级 `build.sh` 与 GitHub Actions 测试校验。

### 运行与交付说明

- 由 GitHub Actions（`windows-latest`）使用 VS2022 MSBuild 构建为单文件 `InspectionWidget.exe`（已内嵌 `requireAdministrator` 管理员权限与 `PerMonitorV2` 高 DPI 声明）；
- 首次运行如出现 Windows SmartScreen 提示，可点「更多信息」→「仍要运行」（未购买商业代码签名）；
- 真实 Windows 环境（多显示器 DPI、剪贴板实测、管理员窗口截图）留待实机验收。
