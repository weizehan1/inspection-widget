# Task 7 简报：设置窗口

项目：巡检截图小组件 InspectionWidget（C# 7.3 / .NET Framework 4.8 / 零第三方依赖）。
工作目录：~/workspace/inspection-widget-src。前置：Task 0–6 已过关（ConfigStore/DeviceNameValidator/DpapiProtector/ClipboardGuard 均可用；MainForm 设置按钮为占位、有 RebuildClipboardGuard() 与 Saved 刷新入口约定——若 Task 2/6 的实际方法名不同，按代码现状接线并在报告注明）。
权威：docs/实施计划.md Task 7 节；白皮书 8.4；开发步骤阶段 7；设计稿设置四页画面（尺寸：窗口约 744×430，行高参考稿面；细节以本简报为准）。

## 总原则
- 保存制：所有修改先落在窗体内编辑态，点「保存」统一校验→ConfigStore.Save→成功才关窗并发 Saved 事件刷主面板；校验不过或保存失败都停留在设置窗并明确指出问题，绝不带错误配置关窗。
- 深拷贝编辑：构造时把传入 AppConfig 深拷贝一份再编辑，取消不影响原配置。
- 构造签名：`SettingsForm(AppConfig config, ConfigStore store, ICredentialProtector protector)`；`public event EventHandler Saved;` 保存成功后 MainForm 用 store 重新 Load 或直接用本窗回传的配置刷新——口径：SettingsForm 暴露 `public AppConfig ResultConfig { get; }`（保存成功后的新配置），MainForm 在 Saved 后以它刷新设备列/凭据区/高度并 RebuildClipboardGuard。

## 布局
- 窗体 744×430、FormBorderStyle.FixedDialog、StartPosition CenterScreen（相对主面板居中亦可）、标题「设置 · 巡检截图小组件」。
- 左侧导航列（宽约 130）：四项 ListBox 或按钮列——设备管理 / 凭据管理 / 存储与通用 / 关于；选中态左条强调色+加粗（近似设计稿即可）。右侧内容区按选中项切换 Panel 可见性（一组一块，不用 TabControl）。
- 底部固定：左下显示校验/保存反馈（错误红字）；右下「保存」「取消」按钮。

## 四组内容
1. **设备管理**：ListBox 显示设备名（按序）；右侧/下方按钮：添加、删除、上移、下移；改名用行内 TextBox+「重命名」或双击弹输入——实现取：选中项在 TextBox 中编辑，点「应用修改」校验写入列表。添加时 TextBox 为空则在列表尾加「设备N」占位名并选中编辑。全部走 DeviceNameValidator.Validate（传入其余设备名查重）；保存前再 ValidateAll + 逐个 ValidateFullPathLength（RootPath 已有时）。窗内注记一行小字：「删除设备只删名单，不删除已保存的历史截图。」
2. **凭据管理**：条目列表（ListView 或自绘行）：每行=名称 TextBox、内容 PasswordChar='●' 的 TextBox（已存条目恒为空白+状态字「已设置（留空不修改，输入新内容则覆盖）」；NeedsReset 条目状态字红字「需重新设置」）、「清除」按钮（清内容：保存后该条 EncryptedSecret 置空且 NeedsReset 视为已清——口径：清除=删除该条凭据内容但保留条目与名称，保存后 EncryptedSecret="" 且面板点击将提示需重新设置；这与白皮书「清除单条」一致）、「删除」按钮（整条移除）。顶部「+ 添加凭据」：满 10 条禁用并提示已达上限；新条目名称+内容都必填（保存校验拦）。**不提供任何查看原文入口**。内容加密只在保存时对「有新输入」的条目调 protector.Protect。
3. **存储与通用**：根目录一行（只读 TextBox 显示全路径 + 「更改…」按钮弹 FolderBrowserDialog；状态小字「已保存 · 启动时不再询问」，RootPath 为空时显示红字「尚未设置，截图暂不可用」）；配置文件实际路径只读显示（store.AuthoritativePath，若 IsFallback 注明「（exe 目录不可写，已回退）」）；「复制后 30 秒自动清空剪贴板」CheckBox；截图模式三档 RadioButton（自动 / 强制 PrintWindow / 强制 BitBltOnly 仅屏幕拷贝）——文案：「自动（推荐）」「仅 PrintWindow」「仅屏幕拷贝（置前抓屏）」。
4. **关于**：产品名「巡检截图小组件」大字 + 版本徽标 v1.0；一句话简介「点设备名、点窗口，截图自动按日期归档；常用凭据一键复制。」；仓库链接 LinkLabel `https://github.com/weizehan1/inspection-widget`（点击 Process.Start 打开，失败忽略）；技术构成一行「C# WinForms · .NET Framework 4.8 · 零第三方依赖」；配置文件位置、截图根目录两行（与存储组同值，运行时读取）。

## 保存流程（保存按钮）
1. 收集：Devices（列表当前序）、Credentials（按行：名称必填校验；新内容有输入→Protect；留空且原条目有密文→保留原密文；清除标记→空密文；新条目无内容→报错定位到该行）、RootPath、AutoClearClipboard、CaptureMode、IsCredentialCollapsed 与窗口位置字段沿用原配置值（设置窗不改这两类，LastDevice 若指向已被删设备→置 null）。
2. 校验顺序：设备名 ValidateAll → 路径长度 → 凭据名称非空与条数 ≤10。任一失败：反馈区红字写明哪组哪项、切换到对应组、焦点定位，不保存不关窗。
3. ConfigStore.Save(ResultConfig)：Saved=false→反馈红字「设置未保存：<Error>」，不关窗；成功→触发 Saved、关窗。

## MainForm 接线
- 设置按钮打开 SettingsForm（模态 ShowDialog），传入当前配置深拷贝源（直接传当前配置对象由窗内深拷贝）、同一 ConfigStore、DpapiProtector 实例。
- Saved 后：以 ResultConfig 替换 MainForm 当前配置、重建设备按钮与凭据区、按 Bottom 锚定重算高度、RebuildClipboardGuard、反馈行提示「设置已保存」。

## 验证与提交
- `bash build.sh` 退出 0；全部既有测试不回归（设置窗无新增自动化测试，但校验路径复用已测的 DeviceNameValidator；如顺手给「凭据行收集逻辑」抽纯函数并加测，欢迎但不强制——抽了就在报告注明）。
- commit：`feat(stage-7): 设置窗口（四组/保存制）`
- 报告 work/task-7-report.md：状态、构建与测试证据、哈希、遗留（实机交互项）。返回只给状态/哈希/一行摘要/concerns。
全程简体中文。不要派生子代理。

> **补充约束（R6，Task 0 审查裁决）**：全部代码只许 C# 7.2 以内语法（Mono mcs 上限），不得用 7.3 独有语法。
