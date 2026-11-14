# P1：配置、链接与目录业务服务

你是用户指定的 PI 混元4 执行者。当前目录是隔离 worktree，其他人同时设计前端与实现独立 Planning 模块。仅修改本任务文件，不提交/推送，不另派 agent，不改 MainForm 或设置布局，不执行安装。构建工具由 Codex 准备，先写用例，未经 Codex 告知构建锁不要运行全量构建（共享 bin/obj）。

## 文件归属
- 可修改 `src/Config/AppConfig.cs`、`ConfigJsonReader.cs`、`ConfigStore.cs`、`ConfigTransfer.cs`、`SettingsDraft.cs`，可新增相关纯业务文件。
- `src/SettingsForm.cs` 只可改开头 AppConfigCloner 的新增字段深拷贝，不碰布局/控件或其他代码。
- 可新增 `src/Links/QuickLinkLauncher.cs`、`src/Storage/InspectionFolderNavigator.cs`。
- 测试新增 `tests/QuickLinkTests.cs`、`tests/InspectionFolderNavigatorTests.cs`、`tests/QuickLinkConfigTests.cs`（各自 public static RunAll）；不要改 tests/Program.cs 或现有测试以免并行冲突。

## 公共契约（除确有无法实现问题，名称不改）
namespace `InspectionWidget.Config`：
- `[DataContract] public sealed class QuickLinkEntry`，`[DataMember] public string Title {get;set;}`、`Url`、`bool Enabled {get;set;}`，缺 Enabled 的新条目默认 true（反序列化同样处理），列表排序即显示顺序。
- `AppConfig.QuickLinks : List<QuickLinkEntry>`，缺字段为空；`AppConfig.PlanModeEnabled : bool`，缺字段 false。
- `SettingsDraft.QuickLinks : BindingList<QuickLinkEntry>` 与 `PlanModeEnabled : bool`，LoadFrom、SnapshotForTransferPreview、ApplyPlan、AcceptSaved 深拷贝并贯通，不泄漏配置原列表引用。
- `QuickLinkValidator.Validate(QuickLinkEntry entry) : string`、`ValidateAll(IEnumerable<QuickLinkEntry> entries) : string` 返回 null 代表有效。名称和 URL 必填；绝对 http/https URI，接受内网主机/IP、端口与路径，不联网判定，不接受 file/javascript/相对地址。避免额外不必要的字符限制。

namespace `InspectionWidget.Links`：
- `public sealed class QuickLinkLauncher`：无参生产构造；`QuickLinkLauncher(Action<string> open)` 测试注入；`bool TryOpen(string url, out string error)`。生产用 ProcessStartInfo 的 UseShellExecute=true 打开默认浏览器，不拼 shell 命令；异常转可读错误，测试不启动浏览器。

namespace `InspectionWidget.Storage`：
- `public sealed class InspectionFolderNavigator`：无参生产构造；`InspectionFolderNavigator(Func<string,bool> directoryExists, Action<string> open)` 测试注入。
- `List<string> ResolveFolders(string rootPath, string folderPattern, DateTime date, IEnumerable<string> savedPaths)`：savedPaths 有当前轮实际成功落盘路径时，只取实际存在的所属目录，去重，配置当日目录若确有该轮截图则排首；无 savedPaths 时退化至已存在的配置日期目录；不创建目录。
- `bool TryOpenFolder(string folder, out string error)`：目录不存在/启动失败返回 false 和可读错误，不执行 shell 拼接，不凭空创建目录。

## 配置交换与兼容
新字段必须进入读取白名单/可选字段集、规范化、序列化、普通导出和 DPAPI 备份、导入预览、替换应用、旧配置默认值、深克隆。保留现有 ExchangeVersion=1（新增可选字段），不破坏 v1 普通设备导入。
默认 MergeDevicesOnly 不覆盖新字段；显式非 merge 的替换设置时，如果源存在新字段才替换，缺字段不得抹除本机现有链接和开关。ConfigImportPlan 设置变化文案要准确；普通导出包括链接但不包括每日进度/凭据正文。
无效新字段应在导入预览时拒绝（避免覆盖损坏），不要削弱旧原子写入/备份/保护机制。可空列表；空行草稿在提交前校验，由后续 UI 调用 validator。

## 必须验证的行为
新增字段老配置/往返/深克隆、MergeDevicesOnly 不覆盖、非 merge 缺字段保留；合法内网 HTTP(S) 与非法协议；全部暂存/部分暂存多目录、空目录、路径带空格与中文、打不开时错误；所有外部启动测试用注入 fake。

报告写 `work/v110/hy4-business-report.md`：改动文件、真实接口、用例名称、验证实际执行与否、已知缺口和给 Kimi 的调用示例（不写实现大段）。无需自行改 README。
