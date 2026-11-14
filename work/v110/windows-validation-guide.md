# Windows 候选版验证包

这是本地候选版的 Windows 验证材料，不是已通过 Windows 验收的正式 Release。

验证包预期包含：`InspectionWidget.exe`、配套外置 `InspectionWidget.exe.manifest`、`InspectionWidget.Tests.exe`、`windows-acceptance.ps1`、`verification-manifest.json` 与本说明。以包内 manifest 的源码 SHA 和文件哈希为准，不能混用别次构建文件。

在 Windows 10/11、有 .NET Framework 4.8 的本地可写目录解压。保持 exe 与外置 manifest 同目录；本候选包由 Mono 构建，Windows MSBuild 单文件内嵌清单打包尚未验证。

自动验证：在解压目录用 PowerShell 运行 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\windows-acceptance.ps1`。该参数只针对本次 PowerShell 进程，不修改全局执行策略。脚本先核对包内文件哈希，再运行测试，结果写入新建的 `verification-日期-唯一后缀` 目录，不覆盖既有报告。

当前候选预期439条逻辑用例和19条Windows窗体用例均通过，共458条、退出0；任何跳过或失败都不能记为通过。以实际日志为准。首次Windows运行前，本脚本本身也标记为未在Windows实跑。

人工抽检与自动化分开：
- 在单独目录启动候选主程序，使用测试设备名、测试截图目录、无敏感内容的普通窗口试运行。
- 检查B方案布局、完整日期、长名单滚动与字体缩放；窗口移到不同DPI屏幕后检查排版。
- 开计划模式，点击设备后隐藏；“查看已点过”、恢复未点、重置、取消选窗和补截都应正常；关闭隐藏后保留对勾。
- 最后一项选窗/保存结束后询问一次打开文件夹；托盘隐藏时不弹，恢复后再提示。截图失败时要如实说明，图片保存成功与进度写失败不能混淆。
- 验证默认浏览器与真实截图目录打开，包含暂存目录；检查托盘、右键/Esc、真实剪贴板清理与截图回退。

不得为测试损坏日常在用的配置/进度。测试异常文件仅在隔离目录/专用测试配置下进行。自动测试使用临时配置目录及注入的窗口钩子，不能替代真实截图引擎和桌面交互验收。
