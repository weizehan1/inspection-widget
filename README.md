# 巡检截图小组件 · Inspection Widget

[简体中文](#简体中文) | [English](#english)

---

## 简体中文

常驻 Windows 桌面右下角的置顶小面板：点设备名 → 点目标窗口 → 只截那个窗口，截图自动按日期归档；常用账号密码一键复制，用完自动清剪贴板。专为日常巡检设计。

`平台 Windows 10/11` · `运行时 .NET Framework 4.8（Win10 1903+ 系统自带）` · `版本 v1.1.0` · `单文件 exe，零第三方依赖`

### 它解决什么问题

- 巡检要逐台截图，手动 PrintScreen 再裁剪、改名、归档，一轮下来重复劳动很多；
- 窗口被别的窗口挡住时截不全，还要先整理桌面；
- 同一天同设备截多张，文件名容易重名覆盖；
- 巡检系统账号密码多，反复手输慢，还怕输错、怕留在剪贴板里；
- 把工具拷给同事时，不想把自己的设备名单和配置一起带过去。

### 功能一览

**截图**

- 点设备按钮进入选窗状态，再点任意程序窗口，只截该窗口本身；
- 首选 PrintWindow 抓图，窗口被遮挡也能截完整；抓不到时自动回退屏幕拷贝（截图瞬间面板自动隐藏，不会把自己截进去）；
- Esc、右键、再点一次已选设备，三种方式都能取消；30 秒无操作自动退出选窗；
- 截图存为「根目录\日期文件夹\设备名.png」，同设备同日重名自动顺延 `-1`、`-2`，永不覆盖；日期文件夹名可自定义（默认 `巡检-yyyy-MM-dd`，可在设置里改成如「yyyy年MM月dd日」）；
- 根目录不可写时自动暂存到备用目录并明确提示，不丢图；保存失败保留图片可点提示重试。

**快捷链接与今日巡检计划**

- **快捷链接（2 列宫格）**：最多配置 12 条常用网址（支持 `http://` / `https://`、带中文空格友好展示与每条启用/停用开关），一键用系统默认浏览器打开，适合放监控看板、网管或工单入口；
- **今日巡检计划（方案 B）**：按自然日跟踪「已点过 X / Y」进度（默认开启，可切换开关与「查看已点过」临时查看），点击设备进入选窗后即按计划计入已点过；在开启「点完即隐藏已点过的设备」时自动从主列表收起已点设备、仅显示剩余待检项，支持单行「恢复未点」或二次确认「重置本轮」；全部点完后在回到空闲态时触发一次完成提示；
- 当日进度独立保存在 `%LocalAppData%\InspectionWidget\progress.json`（与 `config.json` 分离），跨日零点自动刷新并清空前一日进度。

**凭据**

- 凭据改为 2 列紧凑按钮（上限 10 条），支持增删、改名、上下排序（顺序即面板按钮顺序）；
- 点凭据按钮即复制到剪贴板，当前已复制条目展示高亮与 30 秒清空提示；清空前核对剪贴板序列号与本次复制令牌——30 秒内你复制了别的内容，不会误删；程序退出时若倒计时未到，先清再退；
- 内容在设置里掩码输入、不回显，面板上只显示名字；
- 截图设备区与凭据区都可折叠（点分区标题收起/展开），折叠状态会记住。

**设置与数据**

- 设置窗口五组：设备 / 凭据 / 快捷链接 / 存储与显示 / 配置备份（含关于卡片）。「应用」落盘但保持窗口，「保存并关闭」落盘后关窗，「取消」只放弃最近一次应用之后的草稿；校验不过不落盘；
- 设备页是单列可编辑表格，行序就是主面板按钮顺序；支持新增空白草稿行、删除、上移、下移、改名关联今日进度保持、以及 Enter/Esc 单元格编辑。空名单合法，空白设备行在保存前必须补齐或移除；
- 存储与显示页除根目录、日期文件夹模式、剪贴板自动清空与截图模式外，新增界面缩放（100%–200%）、开启今日巡检计划开关与「隐藏已点过设备」显示策略选择；
- 配置备份页支持普通导出（不含凭据）和本机完整备份（保留 DPAPI 密文）。导入先读取、校验并预览，默认仅合并设备，也可明确选择替换参与字段和凭据；预览只改草稿，应用或保存后才写正式配置；
- 配置写入使用同目录临时文件、刷盘和原子替换；导入提交先写 `config.preimport.<时间戳>.<唯一后缀>.json` 原字节备份，备份失败就停止提交。读取失败时进入写保护，原文件保留，主面板和设置入口都可重试读取；
- 352px（按缩放与工作区自适应）浮动面板，顶栏带「巡检助手」与独立中文日期行，底部提供「打开巡检文件夹 ↗」主操作（当存在暂存多目录时自动弹出选择器）；
- 配置文件损坏时先按同一份读取字节备份原件，再返回默认配置；备份失败则进入保护态，不静默覆盖；
- 单实例运行，重复双击不会开两个；手动打开，不开机自启；
- 面板不进任务栏、常驻系统托盘：标题栏「—」只是隐藏到托盘（点托盘图标恢复），标题栏「✕」或托盘右键「退出」才真正退出程序。

### 快速开始

1. 拿到 `InspectionWidget.exe`（正式版为内嵌清单的单文件；若拿到的是带同名 `.manifest` 的版本，两个文件放同一目录即可）。
2. 双击运行，在管理员确认框点「是」（程序以管理员身份运行，才能截管理员窗口）。
3. 若弹出「Windows 已保护你的电脑」（SmartScreen），点「更多信息」→「仍要运行」。本程序未做商业签名，属预期现象；若单位策略直接禁止运行，不要改系统策略。
4. 首次打开会请你选一次截图保存根目录，选完记住，之后不再问。
5. 打开设置（面板右上角齿轮），在「设备管理」加设备、在「凭据管理」加凭据，保存后即可使用。

### 使用方法

**截图三步**

1. 点面板上的设备名（按钮变蓝，进入等待点击状态）；
2. 点要截的那个程序窗口；
3. 截图自动保存，面板底部会显示「✓ 已保存 设备名.png」。点错目标时按 Esc、点右键或再点一次该设备名取消；点到已最小化的窗口会提示先还原。

**复制凭据**：点面板上的凭据按钮即复制，去目标处粘贴即可；30 秒后剪贴板自动清空（此开关在「截图与存储」里，可关）。

**打开巡检文件夹**：点面板底部主操作按钮「打开巡检文件夹 ↗」或路径行，直达当日截图所在目录；若当日存在暂存目录等多个实存目录，会弹出菜单供选直达。

**最小化与退出**：点标题栏「—」面板隐藏到系统托盘（右下角图标区），程序继续运行，点托盘图标或右键「显示面板」恢复；点标题栏「✕」或托盘右键「退出」退出程序。首次隐藏到托盘时会气泡提示一次。

### 设置说明

| 分组 | 管什么 |
| --- | --- |
| 设备 | 单列可编辑表格内新增、改名、删除、上移、下移；行序即面板顺序。删除只删名单，不删已截的历史图片 |
| 凭据 | 凭据增删、改名、改内容、清除单条、上下排序；内容掩码不回显，留空保存表示不改，清除可在当前草稿撤销，解不开的条目标「需重新设置」 |
| 快捷链接 | 增删改、排序、启用/停用最多 12 条 `http://` / `https://` 网址，校验不合法时定位到具体行与字段 |
| 存储与显示 | 截图根目录、日期文件夹名与实时预览、配置路径、剪贴板自动清空开关、截图模式、界面缩放、今日巡检计划开关与「隐藏已点过设备 / 全部显示」策略 |
| 配置备份 | 普通导出、本机完整备份、导入预览与草稿合并/替换；以及产品版本（v1.1.0）、路径信息等关于卡片 |

配置文件位置：默认存 `%LocalAppData%\InspectionWidget\config.json`；若 exe 同目录已有 `config.json`，优先用 exe 旁边的这一份（便携用法）。

### 数据与隐私

- 凭据内容用 Windows DPAPI（当前用户范围）加密后存入配置，配置文件里搜不到原文；程序不联网、不写注册表，异常日志只记时间与错误信息，不记凭据明文。
- 已知限制：DPAPI 密文与当前机器、当前 Windows 账号绑定，整个文件夹拷到另一台机器或换账号，凭据解不开（会标「需重新设置」，重新填写即可）；设备名单等普通设置可随配置带走。另外，复制到剪贴板的内容在被清空前，若被剪贴板历史、云同步或第三方剪贴板工具留存副本，不在本程序控制范围内。
- 分享给同事只发 exe 本体即可：对方首次打开走自己的初始化（选自己的根目录、加自己的设备与凭据），不会带入你的数据。

### 构建

**自动构建（GitHub Actions）**：推送源码到 `main` 后由 `.github/workflows/build.yml` 在 `windows-latest` 上用 MSBuild 构建 Release、运行 **439 条纯逻辑与集成测试 + 19 条 Windows 窗体用例（共 458 条）**，校验 `requireAdministrator` / `PerMonitorV2` 清单内嵌与 SHA-256，并留存全部构建与测试日志；推送 `v*` 标签且上述门禁通过后，校验标签版本一致性并幂等发布 GitHub Release（附单文件 exe 与 `.sha256`）——见 [build.yml](.github/workflows/build.yml)。

**本地构建（Windows，正式版）**：Visual Studio 2022 勾选「.NET 桌面开发」工作负载与 .NET Framework 4.8 目标包，打开 `InspectionWidget.sln`，Release 配置生成。零第三方包，无需还原 NuGet。

**本地构建（macOS/Linux，开发自检）**：安装可用的 Mono（含 msbuild 或 xbuild、libgdiplus）后在仓库根目录执行：

```bash
bash build.sh
```

脚本会先从 `assets/app.ico.base64.txt` 按需还原图标，再以 Release 配置构建主工程与测试工程并运行默认纯逻辑测试，产物放入 `build/`。注意 Mono 构建的清单仍为外置 `.manifest` 形式，正式 Windows 交付以 Actions 或 VS2022 的 MSBuild 构建为准；Mono 构建和默认测试不能替代 Windows 窗体、DPI、DPAPI、剪贴板或管理员窗口验收。

### 验证状态

截至本次发版准备提交前，已在 macOS Mono 环境通过 **439 条纯逻辑与方案 B 集成测试**（覆盖设备草稿、快捷链接、今日巡检计划与隐藏过滤、损坏进度只读保护与备份恢复、导入导出、原子写与锁）。显式加 `--windows-ui` 时，非 Windows 环境会报告 **19 条** Windows 窗体用例未执行并返回退出码 2（不计为通过）。MSBuild 单文件打包、**19 条** WinForms 控件用例（共 458 条）、多显示器 DPI/工作区缩放、DPAPI、真实剪贴板、托盘、默认浏览器打开与管理员窗口截图需在推送后的 GitHub Actions Windows CI 与 Windows 实机环境验收。

### 目录结构

| 路径 | 内容 |
| --- | --- |
| `src/` | 主程序源码：`MainForm.cs`（方案 B 主面板）、`SettingsForm.cs`（五页设置窗口）、`Program.cs`（入口与单实例） |
| `src/Capture/` | 选窗钩子、窗口识别、截图引擎、面板布局（含方案 B）与状态机 |
| `src/Config/` | 配置模型、草稿、快捷链接校验、交换格式、原子写、恢复备份、写保护、设备名校验、界面缩放与视觉主题 |
| `src/Planning/` | 今日巡检计划、按日进度存储、已点过隐藏过滤与会话协调器 |
| `src/Links/` | 快捷链接浏览器启动器 |
| `src/Credentials/` | DPAPI 加解密、凭据服务、剪贴板守卫（30 秒自动清空） |
| `src/Storage/` | 截图保存、命名顺延、暂存回退与智能目录导航 |
| `src/Native/` | Win32 API 声明 |
| `tests/` | 自动化测试工程（与主工程同跑 `build.sh`） |
| `assets/` | 应用图标源文件（`app.ico.base64.txt` 入库，构建时还原 `app.ico`；`app-icon.png` 为预览图） |
| `build/` | 构建产物（exe 与试用说明） |
| `.github/workflows/` | GitHub Actions 自动构建与发版 |

### 版本记录

| 版本 | 日期 | 说明 |
| --- | --- | --- |
| v1.1.0 | 2026-11-14 | 方案 B 界面与流程改版正式版：352px 浮动面板新增顶栏日期、2 列快捷链接、今日巡检计划进度卡片（「已点过 X / Y」、开启/关闭切换、查看已点过、单行恢复未点、二次确认重置、全部点完后按可见态触发一次完成提示）、2 列凭据高亮与已复制标记、「打开巡检文件夹 ↗」主按钮与多目录选择；新增 `HideVisitedDevices`（默认开启点完即隐藏）、改名与部分导入保留进度、跨日延迟刷新、独立 `progress.json` 损坏写保护与备份恢复、新生成并验证的取景框应用图标，以及五页设置窗口。macOS Mono 已通过 439 条纯逻辑/集成测试；Windows 19 条 UI 用例与 MSBuild 单文件产物校验由 GitHub Actions Windows CI 与实机验证把关。 |
| v1.0.4 | 2026-11-12 | 设备管理改为单列可编辑表格，改名直接保存，Esc 放弃当前编辑。应用后即时刷新主面板。新增配置导入导出、设备合并与替换预览、可选本机 DPAPI 完整备份。配置采用原子保存、导入前备份及读取失败保护。整理五页设置、滚动和缩放布局，修复设备表格事件递归与取消编辑问题，增加 Windows 窗体 CI。 |
| v1.0.3 | 2026-11-03 | 主界面短名改为「巡检助手」（主面板标题、托盘提示、最小化气泡；产品全名与 exe 名不变）；凭据支持 ↑/↓ 上下排序（顺序持久化、即主面板渲染顺序）；主面板「截图」区新增与凭据区一致的折叠（配置新增 `IsDeviceCollapsed` 持久化，两区互不影响；口径更正：本条最初误解为「最小化窗口自动还原截图」，2026-11-03 用户看图澄清为分区折叠）；日期文件夹名默认改为 `巡检-yyyy-MM-dd`（仅默认值与缺字段自愈，已保存配置不迁移）；设置窗口新增「应用」按钮（与保存共用校验＋落盘、成功不关窗）；发版步骤改为幂等（同名 Release 已存在时自动 `--clobber` 补挂 exe）；Linux/Mono 本机构建产物 `build/InspectionWidget.exe` 为 322,048 字节，SHA256 `f3ee66680e43011f1213d4a71d6c81fc5cb7c8d90dc397dd2cd1d22d2533f77a` |
| v1.0.3 | 2026-11-03 | The UI short name is now "Inspection Assistant" (panel title, tray tooltip and minimize balloon; the full product name and the exe name are unchanged); credentials can be reordered with ↑/↓ buttons (the order persists and is the order the panel renders); the panel's screenshot (devices) section can now be collapsed just like the credentials section (new persisted `IsDeviceCollapsed` config field; the two sections fold independently — clarified by the user on 2026-11-03 after an initial misunderstanding as "auto-restore minimized windows"); the default date folder pattern is now `巡检-yyyy-MM-dd` (default and missing-field healing only; existing saved values are not migrated); Settings gains an Apply button (same validation and save path as Save, but the window stays open on success); the release step is now idempotent (an existing same-tag release gets the exe uploaded with `--clobber` instead of failing); the Linux/Mono local build `build/InspectionWidget.exe` is 322,048 bytes, SHA256 `f3ee66680e43011f1213d4a71d6c81fc5cb7c8d90dc397dd2cd1d22d2533f77a` |
| v1.0.2 | 2026-11-03 | 新增应用图标（exe/窗口/托盘同源）；标题栏加关闭按钮（✕）；exe 交付名定为 `InspectionWidget.exe`；设置按钮改用 Segoe UI Symbol 标准齿轮字形（缺字体时代码绘制位图兜底）；日期文件夹名可自定义（`FolderNamePattern`，默认 yyyy-MM-dd，保存校验拦截非法模式）；最小化改走系统托盘（面板不再进任务栏，托盘恢复/退出，首次隐藏气泡提示一次；覆盖白皮书原「进任务栏不进托盘」口径，2026-11-03 实机反馈改定）；Linux/Mono 本机构建产物 `build/InspectionWidget.exe` 为 318,464 字节，SHA256 `6fda0c8da3469ed0bc6d65b858cd2dd5064df702b33044b233addaad72af09db` |
| v1.0.1 | 2026-11-03 | 设置窗口改为居中打开；新增界面缩放（100%–200%）；配置文件默认改存 `%LocalAppData%\InspectionWidget`（exe 旁有 config.json 时优先用旁边的）；接入 GitHub Actions 自动构建 |
| v1.0.0 | 2026-11-02 | 首个版本：截图、凭据、设置四组与全部核心口径落地；Linux/Mono 本机构建产物 `build/InspectionWidget.exe` 为 83,456 字节，SHA256 `597ab0ee1f1e6eabac0a4ad65b2f0e592ca59c50f21d9dcd82f84239d8d24e79` |

### 许可证

本项目基于 [MIT License](LICENSE) 开源。

---

## English

A small always-on-top panel that lives in the bottom-right corner of your Windows desktop: click a device name, click the target window, and only that window is captured and filed away by date automatically. Frequently used credentials are one click to copy, and the clipboard is wiped automatically afterwards. Built for day-to-day inspection rounds.

`Platform Windows 10/11` · `Runtime .NET Framework 4.8 (built into Windows 10 1903+)` · `Version v1.1.0` · `Single-file exe, zero third-party dependencies`

### Problems it solves

- Inspection rounds mean capturing window after window; doing it by hand with PrintScreen, then cropping, renaming and filing every shot, is a lot of repetitive work.
- A window hidden behind others comes out incomplete, so you have to tidy up the desktop first.
- Several shots of the same device on the same day easily collide on file names and silently overwrite each other.
- Inspection systems come with many accounts and passwords; typing them by hand is slow and error-prone, and leaves secrets sitting in the clipboard.
- When you hand the tool to a colleague, you don't want your own device list and configuration to go along with it.

### Features

**Capture**

- Click a device button to enter window-picking mode, then click any program window — only that window itself is captured.
- Captures with PrintWindow first, so a window comes out complete even when covered by others; if that fails, it falls back to a screen copy automatically (the panel hides itself for the instant of capture, so it never photographs itself).
- Cancel three ways: Esc, right-click, or clicking the selected device again; picking mode also exits on its own after 30 seconds without a click.
- Shots are saved as `root folder\date folder\device name.png`; duplicates for the same device on the same day are numbered `-1`, `-2`, … and never overwritten. The date folder name is customizable (default `巡检-yyyy-MM-dd`; change it in Settings, e.g. to `yyyy年MM月dd日`).
- If the root folder isn't writable, the shot is parked in a fallback folder with a clear notice, so no image is lost; if saving fails, the image is kept and the notice can be clicked to retry.

**Quick links & Today's inspection plan**

- **Quick links (2-column grid)**: configure up to 12 frequently used `http://` / `https://` URLs with per-link enable/disable toggles and one-click launch in your default browser.
- **Today's inspection plan (Scheme B)**: tracks daily `Visited X / Y` progress (on by default, switchable with a live toggle and "Show visited"). Selecting a device for capture marks it visited; with `HideVisitedDevices` enabled (default), visited devices collapse out of the main list so only remaining devices stay visible, while per-row "Unmark" and a confirmation-guarded "Reset round" remain available. Finishing the full round shows a single completion prompt once the panel returns to idle.
- Daily progress is stored separately in `%LocalAppData%\InspectionWidget\progress.json` (apart from `config.json`) and resets automatically on calendar day rollover.

**Credentials**

- Credentials render in a compact 2-column grid (up to 10 entries), with add/remove/rename/reorder support in Settings.
- Click a credential button to copy it to the clipboard; the active copied button highlights while a 30-second countdown runs. Before clearing, the clipboard sequence number and a per-copy token are verified — if you copied something else in the meantime, it is left untouched. If you quit while the countdown is still running, the clipboard is cleared first, then the app exits.
- Credential contents are typed into a masked field in Settings and never echoed; the panel shows names only.
- Both the devices section and the credentials section can be collapsed (click a section header to fold/unfold), and the collapsed states are remembered.

**Settings & data**

- The Settings window has five pages: Devices / Credentials / Quick Links / Storage & Display / Config & Backup (with About card). Apply writes and keeps the window open; Save & Close writes and closes; Cancel discards only changes made since the last successful Apply. Validation failures write nothing.
- The Devices page is a single-column editable grid whose row order is the main-panel button order. It supports adding a blank draft row, deleting, moving up/down, preserving daily progress across renames, and Enter/Esc cell editing. An empty list is valid, while a blank device row must be completed or removed before saving.
- Storage & Display covers root folder, date folder pattern + live preview, config path, clipboard auto-clear, capture mode, 100%–200% UI scaling, Today's inspection plan toggle, and the hide-visited vs. show-all display strategy.
- Configuration & Backup supports an ordinary export without credentials and a full local backup that preserves DPAPI ciphertext. Imports are read, validated and previewed before any change: the default is to merge only the device list, while participating settings and credentials can be replaced explicitly.
- Configuration writes use a same-directory temporary file, flush and atomic replacement. An import commit first writes a byte-for-byte `config.preimport.<timestamp>.<unique>.json` recovery copy; read failures enter write protection with a retry entry point.
- The 352px (scaled & work-area clamped) Scheme B panel includes a Chinese date subtitle line and a bottom primary action ("Open inspection folder ↗") with a multi-folder picker when shots were split across fallback folders.
- Single instance, manual launch only, and tray minimize/restore behavior.

### Quick start

1. Get `InspectionWidget.exe` — the official build is a single file with the application manifest embedded; if you received a build with a matching `.manifest` file next to it, just keep the two files in the same folder.
2. Double-click to run, and answer "Yes" to the administrator prompt (the app runs elevated so it can capture elevated windows).
3. If "Windows protected your PC" (SmartScreen) appears, click "More info" → "Run anyway". The app carries no commercial code signature, so this is expected; if your organization's policy blocks it outright, do not change system policy to work around that.
4. On first launch you will be asked to pick the screenshot root folder once; the choice is remembered and you won't be asked again.
5. Open Settings (the gear button at the panel's top right), add devices under "Devices" and credentials under "Credentials", save, and you're ready to go.

### Usage

**Capturing in three steps**

1. Click a device name on the panel (the button turns blue — the app is now waiting for your click);
2. Click the program window you want to capture;
3. The shot is saved automatically and the panel footer shows "✓ Saved device name.png". If you clicked the wrong target, press Esc, right-click, or click the same device name again to cancel; clicking a minimized window prompts you to restore it first.

**Copying a credential**: click the credential button on the panel, then paste where you need it; the clipboard is cleared automatically after 30 seconds (this can be turned off under "Capture & Storage").

**Opening today's folder**: click the footer's primary "Open inspection folder ↗" button (or path label) to jump straight to today's actual screenshot folder; when fallback parking created multiple existing folders for the round, a chooser menu lets you pick which folder to open.

**Minimizing and quitting**: the title-bar "—" hides the panel to the system tray (the notification area at the bottom right) while the app keeps running; click the tray icon or choose "Show panel" from its context menu to restore it. The title-bar "✕" or "Exit" in the tray menu quits the app. The first time the panel hides to the tray, a one-time balloon tip explains where it went.

### Settings

| Group | What it manages |
| --- | --- |
| Devices | Add, rename, remove and move devices up/down in a single-column editable grid; row order is panel order. Deleting removes only the list entry, never the shots already taken |
| Credentials | Add, remove, rename, change content, clear a single entry and reorder credentials. Contents are masked and never echoed; leaving an existing field blank means "unchanged"; a clear can be undone within the current draft, and undecryptable entries are marked "Needs re-entry" |
| Quick Links | Add, edit, toggle enable/disable, and reorder up to 12 `http://` / `https://` links with per-row/field validation |
| Storage & Display | Screenshot root folder; date folder pattern with live preview; config path; 30-second clipboard auto-clear; capture mode; UI scaling; Today's inspection plan toggle; hide-visited vs. show-all device display policy |
| Config & Backup | Ordinary export, full local backup, import preview, merge/replace into the draft, plus the About card (`v1.1.0`, repo & active paths) |

Config file location: `%LocalAppData%\InspectionWidget\config.json` by default; if a `config.json` already sits next to the exe, that one takes precedence (portable usage).

### Data & privacy

- Credential contents are encrypted with Windows DPAPI (current-user scope) before being stored in the configuration, so no plaintext can be found in the config file. The app never goes online and never writes to the registry; its error log records only timestamps and error details, never credential plaintext.
- Known limitations: DPAPI ciphertext is bound to the current machine and the current Windows account. Copy the whole folder to another machine or another account and the credentials can no longer be decrypted (they will be marked "Needs re-entry" — just type them in again); ordinary settings such as the device list do travel with the config. Also, while copied content is still waiting to be cleared, copies retained by clipboard history, cloud clipboard sync or third-party clipboard tools are outside this app's control.
- To share the tool with a colleague, send just the exe: on their first launch they go through their own setup (their own root folder, devices and credentials), and none of your data comes along.

### Build

**Automatic builds (GitHub Actions)**: pushing source to `main` triggers `.github/workflows/build.yml`, which builds Release with MSBuild on `windows-latest`, runs **439 logic/integration tests + 19 Windows Forms UI tests (458 total)**, verifies the embedded `requireAdministrator` / `PerMonitorV2` manifest and SHA-256, and retains full build/test logs even on failure. Pushing a `v*` tag after checks pass verifies tag/binary version consistency and publishes a GitHub Release with the single-file exe and `.sha256` — see [build.yml](.github/workflows/build.yml).

**Local build (Windows, official)**: Visual Studio 2022 with the ".NET desktop development" workload and the .NET Framework 4.8 targeting pack; open `InspectionWidget.sln` and build the Release configuration. Zero third-party packages, no NuGet restore needed.

**Local build (macOS/Linux, developer self-check)**: install a working Mono toolchain (msbuild or xbuild, plus libgdiplus), then run from the repository root:

```bash
bash build.sh
```

The script restores `assets/app.ico` from `assets/app.ico.base64.txt` when needed, builds the main and test projects in the Release configuration, runs the default pure-logic tests, and places the outputs in `build/`. Mono builds leave the manifest as an external `.manifest` file; use an MSBuild build from Actions or VS2022 for official Windows delivery. Mono builds and default tests do not replace Windows validation of forms, DPI, DPAPI, the clipboard, or elevated-window capture.

### Verification status

Before release tagging, **439 pure-logic and Scheme B integration tests** pass under macOS Mono (covering settings draft, quick links, today's inspection plan & hide-visited filtering, corrupt `progress.json` write protection & backup recovery, import/export, atomic writes and locking). On non-Windows systems, `--windows-ui` reports the **19** UI cases as unexecuted and exits with code 2 (not counted as passed). The MSBuild single-file build, **19** WinForms UI control tests (458 total), multi-monitor DPI/work-area scaling, cross-user DPAPI, real clipboard/tray behavior, browser launch and elevated-window capture require GitHub Actions `windows-latest` CI and Windows runtime verification.

### Project layout

| Path | Contents |
| --- | --- |
| `src/` | Main program source: `MainForm.cs` (Scheme B main panel), `SettingsForm.cs` (5-page settings window), `Program.cs` (entry point and single-instance guard) |
| `src/Capture/` | Window-picking hook, window identification, capture engine, panel layout (incl. Scheme B) and state machine |
| `src/Config/` | Config model, settings draft, quick-link validation, interchange format, atomic writes, recovery backups, write protection, device-name validation, UI scaling and `UiTheme` |
| `src/Planning/` | Today's inspection plan, daily `progress.json` store, hide-visited display filter and session controller |
| `src/Links/` | Quick-link default browser launcher |
| `src/Credentials/` | DPAPI encryption/decryption, credential service, clipboard guard (30-second auto-clear) |
| `src/Storage/` | Screenshot saving, duplicate numbering, fallback parking and smart folder navigator |
| `src/Native/` | Win32 API declarations |
| `tests/` | Automated test project (built and run together with the main project via `build.sh`) |
| `assets/` | App icon sources (`app.ico.base64.txt` is versioned and `app.ico` is restored during build; `app-icon.png` is a preview) |
| `build/` | Build outputs (the exe and the trial guide) |
| `.github/workflows/` | GitHub Actions automated build & release |

### Changelog

| Version | Date | Notes |
| --- | --- | --- |
| v1.1.0 | 2026-11-14 | Scheme B redesign release: 352px floating panel with Chinese date header, 2-column Quick Links, Today's Inspection Plan progress card (`Visited X / Y`, live toggle, Show Visited, per-row Unmark, confirmation-guarded Reset, visibility-guarded single completion prompt), 2-column Credentials with active copied check highlight, primary "Open inspection folder ↗" action with multi-folder chooser; added `HideVisitedDevices` (default true), rename and partial-import progress preservation, deferred midnight rollover, `progress.json` corruption write protection & backup recovery, redesigned viewfinder icon, and five Settings pages. Verified 439/439 logic/integration tests on macOS Mono; 19 Windows UI tests and MSBuild single-file packaging gated on GitHub Actions Windows CI and Windows runtime QA. |
| v1.0.4 | 2026-11-12 | Added an editable device grid with direct save and Esc cancellation, plus immediate refresh after Apply. Added configuration import/export, device merge and replacement previews, optional local DPAPI backups, atomic saves, pre-import recovery backups and read-failure protection. Refined the five Settings pages, scrolling and scaling, fixed recursive grid events and edit cancellation, and added Windows UI CI. |
| v1.0.3 | 2026-11-03 | The UI short name is now "Inspection Assistant" (panel title, tray tooltip and minimize balloon; the full product name and the exe name are unchanged); credentials can be reordered with ↑/↓ buttons (the order persists and is the order the panel renders); the panel's screenshot (devices) section can now be collapsed just like the credentials section (new persisted `IsDeviceCollapsed` config field; the two sections fold independently — clarified by the user on 2026-11-03 after an initial misunderstanding as "auto-restore minimized windows"); the default date folder pattern is now `巡检-yyyy-MM-dd` (default and missing-field healing only; existing saved values are not migrated); Settings gains an Apply button (same validation and save path as Save, but the window stays open on success); the release step is now idempotent (an existing same-tag release gets the exe uploaded with `--clobber` instead of failing); the Linux/Mono local build `build/InspectionWidget.exe` is 322,048 bytes, SHA256 `f3ee66680e43011f1213d4a71d6c81fc5cb7c8d90dc397dd2cd1d22d2533f77a` |
| v1.0.2 | 2026-11-03 | New app icon (one source for the exe, window and tray icons); close button (✕) added to the title bar; the exe ships as `InspectionWidget.exe`; the settings button now uses the standard gear glyph from Segoe UI Symbol (with a code-drawn bitmap fallback when the font is unavailable); date folder names are customizable (`FolderNamePattern`, default `yyyy-MM-dd`, invalid patterns rejected at save time); minimize now goes to the system tray (the panel no longer appears in the taskbar; restore/exit from the tray, with a balloon tip the first time it hides — superseding the white paper's original "taskbar, not tray" decision, changed on 2026-11-03 based on hands-on feedback); the Linux/Mono local build `build/InspectionWidget.exe` is 318,464 bytes, SHA256 `6fda0c8da3469ed0bc6d65b858cd2dd5064df702b33044b233addaad72af09db` |
| v1.0.1 | 2026-11-03 | Settings window now opens centered; UI scaling added (100%–200%); the config file now defaults to `%LocalAppData%\InspectionWidget` (a `config.json` next to the exe still takes precedence); GitHub Actions automated builds |
| v1.0.0 | 2026-11-02 | First release: capture, credentials, the four Settings groups and all core behaviors in place; the Linux/Mono local build `build/InspectionWidget.exe` is 83,456 bytes, SHA256 `597ab0ee1f1e6eabac0a4ad65b2f0e592ca59c50f21d9dcd82f84239d8d24e79` |

### License

This project is open source under the [MIT License](LICENSE).
