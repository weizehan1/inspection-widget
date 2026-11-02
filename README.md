# 巡检截图小组件（InspectionWidget）

## 这是什么

一个 Windows 桌面小组件：点设备名 → 再点目标程序窗口 → 只截该窗口，截图按「根目录\日期\设备名.png」自动归档（重名自动顺延 -1、-2）。窗口被遮挡也能截完整。基于 C# WinForms / .NET Framework 4.8，零第三方依赖。

设备与凭据都在设置里按实际数量维护（可增减）：设备点一下即开始选窗截图；凭据点一下即复制到剪贴板，凭据内容经 DPAPI 加密保存，配置文件里搜不到原文，复制后 30 秒自动清空剪贴板（设置里可关）。

## 怎么编译

正式方式（Windows）：安装 Visual Studio 2022 并勾选「.NET 桌面开发」工作负载与 .NET Framework 4.8 目标包，打开 `InspectionWidget.sln`，以 Release 配置构建。

本机备选（Linux）：安装 Mono 后在仓库根目录执行：

```bash
bash build.sh
```

脚本会用 `msbuild`（找不到则 `xbuild`）以 Release 配置构建，并把 `InspectionWidget.exe` 与 `InspectionWidget.exe.manifest` 放进 `build/`；若存在 tests 工程会一并构建并运行测试。注意：Mono 备选构建的产物 manifest 为外置形式，正式交付以 VS 构建为准。

## 怎么运行

双击 `InspectionWidget.exe` → 弹出管理员确认点「是」→ 右下角出现小面板。首次启动会弹框请你选择截图保存的根目录（只问一次，之后不再问；选错了在设置 → 存储与通用里改）。截图时先点面板上的设备名（按钮变蓝），再点要截的那个程序窗口，图即自动存好；点错了按 Esc、点右键或再点一次该设备名取消。在设置里维护设备与凭据（增减、改名、折叠凭据区等）。

## 版本记录

| 版本 | 日期 | 说明 |
| ---- | ---- | ---- |
| v1.0.0 | 2026-11-02 | 首个本地版本。`build/InspectionWidget.exe`：83,456 字节，SHA256 `597ab0ee1f1e6eabac0a4ad65b2f0e592ca59c50f21d9dcd82f84239d8d24e79`（提交前本机 `sha256sum` 实测）。本版为 Linux/Mono 构建，Windows 专属能力（DPAPI、钩子、PrintWindow、真实剪贴板）待 Windows 实机验收，清单见 docs/验收记录.md。 |

## 远程仓库

源码远程同步与标签推送延期安排；当前仅在本地打了 `v1.0.0` 标签，未加 remote、未推送。
