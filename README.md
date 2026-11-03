# 巡检截图小组件 · Inspection Widget

[简体中文](#简体中文) | [English](#english)

---

## 简体中文

常驻 Windows 桌面右下角的置顶小面板：点设备名 → 点目标窗口 → 只截那个窗口，截图自动按日期归档；常用账号密码一键复制，用完自动清剪贴板。专为日常巡检设计。

`平台 Windows 10/11` · `运行时 .NET Framework 4.8（Win10 1903+ 系统自带）` · `版本 v1.0.3` · `单文件 exe，零第三方依赖`

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

**凭据**

- 凭据按实际条数渲染（一行一个单列），上限 10 条，可增减、改名、上下排序（顺序即面板按钮顺序），与设备同一套维护方式；
- 点凭据按钮即复制到剪贴板；复制后 30 秒自动清空，且清空前核对剪贴板序列号与本次复制令牌——30 秒内你复制了别的内容，不会误删；程序退出时若倒计时未到，先清再退；
- 内容在设置里掩码输入、不回显，面板上只显示名字；
- 截图区与凭据区都可折叠（点分区标题收起/展开），折叠状态会记住。

**设置与数据**

- 设置窗口四组：设备管理 / 凭据管理 / 存储与通用 / 关于，统一保存制（「保存」落盘并关窗、「应用」落盘不关窗继续改），校验不过不落盘；
- 设置窗口在屏幕中央打开；界面缩放可在 100%–200% 间选（高分辨率屏不再小得看不清）；
- 面板高度随设备、凭据实际数量伸缩，底边锚定在右下角不跳位；
- 配置文件损坏自动备份原件再恢复默认，不会把好配置冲掉；
- 单实例运行，重复双击不会开两个；手动打开，不开机自启；
- 面板不进任务栏、常驻系统托盘：标题栏「—」只是隐藏到托盘（点托盘图标恢复），标题栏「✕」或托盘右键「退出」才真正退出程序（2026-11-03 实机反馈改定，原口径为进任务栏不进托盘）。

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

**复制凭据**：点面板上的凭据按钮即复制，去目标处粘贴即可；30 秒后剪贴板自动清空（此开关在「存储与通用」里，可关）。

**打开今日文件夹**：点面板底部常驻的根目录行，打开当天的日期文件夹。

**最小化与退出**：点标题栏「—」面板隐藏到系统托盘（右下角图标区），程序继续运行，点托盘图标或右键「显示面板」恢复；点标题栏「✕」或托盘右键「退出」退出程序。首次隐藏到托盘时会气泡提示一次。

### 设置说明

| 分组 | 管什么 |
| --- | --- |
| 设备管理 | 设备增删、改名、拖动或上下箭头排序；删除只删名单，不删已截的历史图片 |
| 凭据管理 | 凭据增删、改名、改内容、清除单条、上下排序；内容掩码不回显，留空保存表示不改，解不开的条目标「需重新设置」 |
| 存储与通用 | 截图保存根目录（随时可改）、日期文件夹名（默认 `巡检-yyyy-MM-dd`，占位可混排固定文字，含非法字符会被拦下）、当前实际生效的配置文件路径、剪贴板 30 秒自动清空开关、截图模式（自动 / 仅屏幕拷贝等）、界面缩放 |
| 关于 | 产品名与版本、仓库链接、技术构成、配置文件与截图根目录位置 |

配置文件位置：默认存 `%LocalAppData%\InspectionWidget\config.json`；若 exe 同目录已有 `config.json`，优先用 exe 旁边的这一份（便携用法）。

### 数据与隐私

- 凭据内容用 Windows DPAPI（当前用户范围）加密后存入配置，配置文件里搜不到原文；程序不联网、不写注册表，异常日志只记时间与错误信息，不记凭据明文。
- 已知限制：DPAPI 密文与当前机器、当前 Windows 账号绑定，整个文件夹拷到另一台机器或换账号，凭据解不开（会标「需重新设置」，重新填写即可）；设备名单等普通设置可随配置带走。另外，复制到剪贴板的内容在被清空前，若被剪贴板历史、云同步或第三方剪贴板工具留存副本，不在本程序控制范围内。
- 分享给同事只发 exe 本体即可：对方首次打开走自己的初始化（选自己的根目录、加自己的设备与凭据），不会带入你的数据。

### 构建

**自动构建（GitHub Actions）**：推送源码后由 `.github/workflows/build.yml` 在 Windows 环境用 MSBuild 构建 Release 并跑测试；产物为内嵌应用清单的单文件 `InspectionWidget.exe`，从 Actions 产物或 Release 中下载。打 `v*` 版本标签时自动生成 GitHub Release 并附 exe——见 [build.yml](.github/workflows/build.yml)。

**本地构建（Windows，正式版）**：Visual Studio 2022 勾选「.NET 桌面开发」工作负载与 .NET Framework 4.8 目标包，打开 `InspectionWidget.sln`，Release 配置生成。零第三方包，无需还原 NuGet。

**本地构建（Linux，开发自检）**：安装 Mono（mono-devel、libgdiplus）后在仓库根目录执行：

```bash
bash build.sh
```

脚本以 Release 配置构建主工程与测试工程并运行全部测试，产物放入 `build/`。注意 Mono 构建的 exe 应用图标已内嵌（与 MSBuild 一致），但清单仍为外置 `.manifest` 形式，正式交付以 Actions 或 VS2022 构建为准。

### 验证状态

平台无关逻辑有自动化测试全程覆盖（v1.0.0 时 191 条全过）；15 项验收中「重名顺延与暂存兜底」「配置损坏自愈」「保存故障兜底」已验证，其余涉及管理员窗口抓图、DPAPI、真实剪贴板、150% 缩放、连截稳定性等 Windows 专属行为的项目，须在 Windows 实机逐项确认。

### 目录结构

| 路径 | 内容 |
| --- | --- |
| `src/` | 主程序源码：`MainForm.cs`（主面板）、`SettingsForm.cs`（设置窗口）、`Program.cs`（入口与单实例） |
| `src/Capture/` | 选窗钩子、窗口识别、截图引擎、面板布局与状态机 |
| `src/Config/` | 配置模型、读写与损坏自愈、设备名校验、界面缩放 |
| `src/Credentials/` | DPAPI 加解密、凭据服务、剪贴板守卫（30 秒自动清空） |
| `src/Storage/` | 截图保存、命名顺延与暂存回退 |
| `src/Native/` | Win32 API 声明 |
| `tests/` | 自动化测试工程（与主工程同跑 `build.sh`） |
| `assets/` | 应用图标源文件（`app.ico` 多尺寸图标与 `app-icon.png` 预览图） |
| `build/` | 构建产物（exe 与试用说明） |
| `.github/workflows/` | GitHub Actions 自动构建与发版 |

### 版本记录

| 版本 | 日期 | 说明 |
| --- | --- | --- |
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

`Platform Windows 10/11` · `Runtime .NET Framework 4.8 (built into Windows 10 1903+)` · `Version v1.0.3` · `Single-file exe, zero third-party dependencies`

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

**Credentials**

- Credentials are rendered by actual count (one per row, single column), up to 10 entries; they can be added, removed, renamed and reordered (the order is the order the panel buttons render in), and are maintained the same way as devices.
- Click a credential button to copy it to the clipboard. The clipboard is cleared automatically after 30 seconds, and before clearing, the clipboard sequence number and a per-copy token are verified — if you copied something else in the meantime, it is left untouched. If you quit while the countdown is still running, the clipboard is cleared first, then the app exits.
- Credential contents are typed into a masked field in Settings and never echoed; the panel shows names only.
- Both the screenshot (devices) section and the credentials section can be collapsed (click a section header to fold/unfold), and the collapsed states are remembered.

**Settings & data**

- The Settings window has four groups — Devices / Credentials / Storage & General / About — all using a save-to-apply model (Save writes and closes; Apply writes and stays open so you can keep editing): if validation fails, nothing is written.
- The Settings window opens centered on screen; UI scaling from 100%–200% keeps things readable on high-resolution displays.
- The panel's height adjusts to the actual number of devices and credentials, with its bottom edge anchored at the bottom-right corner so it never jumps around.
- A corrupted configuration file is backed up first, then reset to defaults — an intact configuration is never silently clobbered.
- Single instance: double-clicking again won't open a second copy. The app is launched manually and never starts with Windows.
- The panel stays out of the taskbar and lives in the system tray: the title-bar "—" only hides it to the tray (click the tray icon to bring it back), while the title-bar "✕" or "Exit" in the tray context menu actually quits the app (changed on 2026-11-03 based on hands-on feedback, superseding the original "taskbar button, no tray" decision).

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

**Copying a credential**: click the credential button on the panel, then paste where you need it; the clipboard is cleared automatically after 30 seconds (this can be turned off under "Storage & General").

**Opening today's folder**: click the root-folder line pinned at the bottom of the panel to open that day's date folder.

**Minimizing and quitting**: the title-bar "—" hides the panel to the system tray (the notification area at the bottom right) while the app keeps running; click the tray icon or choose "Show panel" from its context menu to restore it. The title-bar "✕" or "Exit" in the tray menu quits the app. The first time the panel hides to the tray, a one-time balloon tip explains where it went.

### Settings

| Group | What it manages |
| --- | --- |
| Devices | Add, remove and rename devices; reorder by dragging or with the up/down arrows. Deleting removes only the list entry, never the shots already taken |
| Credentials | Add, remove, rename and reorder credentials; change an entry's content or clear a single entry. Contents are masked and never echoed; saving with the field left blank means "unchanged"; entries that can't be decrypted are marked "Needs re-entry" |
| Storage & General | Screenshot root folder (changeable at any time); date folder name (default `巡检-yyyy-MM-dd`, date placeholders can be mixed with fixed text, and patterns containing illegal characters are rejected); the config file path actually in effect; the 30-second clipboard auto-clear toggle; capture mode (automatic / screen-copy only, etc.); UI scaling |
| About | Product name and version, repository link, tech stack, and where the config file and screenshot root folder are located |

Config file location: `%LocalAppData%\InspectionWidget\config.json` by default; if a `config.json` already sits next to the exe, that one takes precedence (portable usage).

### Data & privacy

- Credential contents are encrypted with Windows DPAPI (current-user scope) before being stored in the configuration, so no plaintext can be found in the config file. The app never goes online and never writes to the registry; its error log records only timestamps and error details, never credential plaintext.
- Known limitations: DPAPI ciphertext is bound to the current machine and the current Windows account. Copy the whole folder to another machine or another account and the credentials can no longer be decrypted (they will be marked "Needs re-entry" — just type them in again); ordinary settings such as the device list do travel with the config. Also, while copied content is still waiting to be cleared, copies retained by clipboard history, cloud clipboard sync or third-party clipboard tools are outside this app's control.
- To share the tool with a colleague, send just the exe: on their first launch they go through their own setup (their own root folder, devices and credentials), and none of your data comes along.

### Build

**Automatic builds (GitHub Actions)**: pushing source triggers `.github/workflows/build.yml`, which builds the Release configuration with MSBuild on a Windows runner and runs the test suite. The result is a single-file `InspectionWidget.exe` with the application manifest embedded, downloadable from the Actions artifacts or from Releases. Pushing a `v*` tag automatically creates a GitHub Release with the exe attached — see [build.yml](.github/workflows/build.yml).

**Local build (Windows, official)**: Visual Studio 2022 with the ".NET desktop development" workload and the .NET Framework 4.8 targeting pack; open `InspectionWidget.sln` and build the Release configuration. Zero third-party packages, no NuGet restore needed.

**Local build (Linux, developer self-check)**: install Mono (mono-devel, libgdiplus), then run from the repository root:

```bash
bash build.sh
```

The script builds the main project and the test project in the Release configuration, runs all tests, and places the outputs in `build/`. Note: Mono builds do embed the application icon (same as MSBuild), but the manifest remains an external `.manifest` file; use an Actions or VS2022 build for official delivery.

### Verification status

Platform-independent logic is covered by automated tests throughout (191 passing at v1.0.0). Of the 15 acceptance items, "duplicate naming with fallback parking", "corrupted-config self-healing" and "save-failure fallback" are verified; the remaining items involve Windows-only behavior — capturing elevated windows, DPAPI, the real clipboard, 150% scaling, stability over repeated captures — and must be confirmed one by one on a real Windows machine.

### Project layout

| Path | Contents |
| --- | --- |
| `src/` | Main program source: `MainForm.cs` (main panel), `SettingsForm.cs` (settings window), `Program.cs` (entry point and single-instance guard) |
| `src/Capture/` | Window-picking hook, window identification, capture engine, panel layout and state machine |
| `src/Config/` | Config model, load/save with corruption self-healing, device-name validation, UI scaling |
| `src/Credentials/` | DPAPI encryption/decryption, credential service, clipboard guard (30-second auto-clear) |
| `src/Storage/` | Screenshot saving, duplicate numbering and fallback parking |
| `src/Native/` | Win32 API declarations |
| `tests/` | Automated test project (built and run together with the main project via `build.sh`) |
| `assets/` | App icon sources (`app.ico`, a multi-size icon, and the `app-icon.png` preview) |
| `build/` | Build outputs (the exe and the trial guide) |
| `.github/workflows/` | GitHub Actions automated build & release |

### Changelog

| Version | Date | Notes |
| --- | --- | --- |
| v1.0.2 | 2026-11-03 | New app icon (one source for the exe, window and tray icons); close button (✕) added to the title bar; the exe ships as `InspectionWidget.exe`; the settings button now uses the standard gear glyph from Segoe UI Symbol (with a code-drawn bitmap fallback when the font is unavailable); date folder names are customizable (`FolderNamePattern`, default `yyyy-MM-dd`, invalid patterns rejected at save time); minimize now goes to the system tray (the panel no longer appears in the taskbar; restore/exit from the tray, with a balloon tip the first time it hides — superseding the white paper's original "taskbar, not tray" decision, changed on 2026-11-03 based on hands-on feedback); the Linux/Mono local build `build/InspectionWidget.exe` is 318,464 bytes, SHA256 `6fda0c8da3469ed0bc6d65b858cd2dd5064df702b33044b233addaad72af09db` |
| v1.0.1 | 2026-11-03 | Settings window now opens centered; UI scaling added (100%–200%); the config file now defaults to `%LocalAppData%\InspectionWidget` (a `config.json` next to the exe still takes precedence); GitHub Actions automated builds |
| v1.0.0 | 2026-11-02 | First release: capture, credentials, the four Settings groups and all core behaviors in place; the Linux/Mono local build `build/InspectionWidget.exe` is 83,456 bytes, SHA256 `597ab0ee1f1e6eabac0a4ad65b2f0e592ca59c50f21d9dcd82f84239d8d24e79` |

### License

This project is open source under the [MIT License](LICENSE).
