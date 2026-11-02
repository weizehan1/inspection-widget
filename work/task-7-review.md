# Task 7 审查：设置窗口（db488ad..417853e）

审查对象：简报、实现方报告、审查包全文，并对源码（SettingsForm.cs / MainForm.cs 接线段 / AppConfig.cs / DeviceNameValidator.cs）与测试做了独立复跑核对。只审查，未改生产代码。

## ① 规格结论：❌（一处不符，其余全部符合）

### 保存制 ✅
- 构造时 `AppConfigCloner.Clone(config)` 深拷贝：AppConfig 全部 9 字段（RootPath / Devices / Credentials 三字段逐条新建 / IsCredentialCollapsed / AutoClearClipboard / CaptureMode / WindowLeft / WindowTop / LastDevice）均复制，取消只关窗不触原对象，零副作用成立。
- 校验顺序与简报一致：`ValidateAll`（设备名）→ `ValidateFullPathLength`（RootPath 非空时，入参为「根目录\当日日期目录」+「设备名.png」，与 ScreenshotSaver 同口径）→ 凭据条数 ≤10 → 逐行收集（名称必填在此拦）。任一失败 `ShowError` 切到对应组 + 焦点定位 + 红字，不保存不关窗。
- `ConfigStore.Save` 返回 Saved=false 与抛异常两路均反馈「设置未保存：<Error>」且不关窗；成功才置 ResultConfig、发 Saved、关窗。

### 设备管理 ✅
- 改名（应用修改）走 `Validate(其余设备名查重集)`；添加走 `Validate`，编辑框为空时取第一个校验通过的「设备N」占位名并选中待改名；上移/下移/删除为列表序操作，最终由保存时 `ValidateAll` 兜底（与简报「保存前再 ValidateAll」口径一致）。
- 注记在位且原文一致：「删除设备只删名单，不删除已保存的历史截图。」

### 凭据管理 ❌（一处校验可绕过，见质量 Important-1）
- 内容不回显 ✅：全 diff 与源码搜查确认——SecretBox 创建时无 Text 赋值，全文唯一赋值是清除时置空；无 Unprotect 调用、无「显示/查看」按钮、无 ToolTip 回显；已存明文从不进入任何控件（行内只保存密文 OriginalSecret）。
- 留空不改（保留原密文）/ 输入覆盖 / 清除只清内容保留条目名称 / 删除整条 ✅；满 10 条添加按钮禁用 + 点击路径上限提示兜底 ✅；加密只在保存收集时对「有新输入且未清除」的行调 `Protect` 一次 ✅。
- ❌ 例外：新条目必填可被「清除」绕过（详见下）。

### 存储与通用 ✅
- 根目录只读显示 + FolderBrowserDialog 更改；状态字：空→红字「尚未设置，截图暂不可用」、未改→「已保存 · 启动时不再询问」（另多一态「已更改，点保存后生效」，如实反映编辑态，不改变既定两态含义，可接受）。
- 实际配置路径显示 `store.AuthoritativePath`，IsFallback 时红字注明「（exe 目录不可写，已回退）」✅；自动清空剪贴板 CheckBox ✅；截图模式三档文案「自动（推荐）」「仅 PrintWindow」「仅屏幕拷贝（置前抓屏）」与枚举 Auto / PrintWindowOnly / BitBltOnly 加载、保存双向映射正确 ✅。

### 关于组 ✅
- 产品名大字、版本徽标 v1.0、简介原文、仓库 LinkLabel（Process.Start + 失败忽略）、技术构成一行、配置文件位置、截图根目录（切组时运行时刷新，与存储组同值）——各
...[truncated 3427 chars]