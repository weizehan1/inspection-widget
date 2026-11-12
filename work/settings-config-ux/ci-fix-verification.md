# Windows CI 递归修复 · 验证记录

## 1. 失败证据与修正

Windows PR CI run `43153266629`、job `130967218760`：MSBuild 与 342 条默认纯逻辑测试通过；`--windows-ui` 第一条用例通过，第二条“新增空白设备行”附近以 `StackOverflowException` 退出，退出码 `-1073741571`。

触发链：

```text
CellValueChanged（未过滤行头/列头事件）
  → RaiseChanged
  → UpdateCountAndButtonsCore
  → Rows[i].HeaderCell.Value = ...
  → 再次触发 CellValueChanged
  → 无界递归
```

修正：

- `CellValueChanged` 改为具名处理，只接受 `RowIndex >= 0 && ColumnIndex >= 0` 的设备名称数据单元格；`_loading` 期间仍不触发 dirty。
- 行号刷新只在值实际变化时赋给 `HeaderCell.Value`，避免重复赋值再次触发事件。
- 保留新增空白行、编辑、改名与 `LastDevice` 跟随、排序、删除、dirty 反馈、Esc 和保存行为。
- 每个 Windows 窗体用例进入前输出 `[START]`；第二条用例在环境显示、`AddDevice`、`ApplyInternal` 前后输出 `[STEP]`，便于下一轮 CI 精确定位。
- 第二条用例增加三行行号 `1/2/3` 断言，覆盖本次递归路径。

## 2. 本轮本地验证

```bash
PATH="$PWD/../tools/mono/mono/6.14.1/bin:$PATH" \
DYLD_LIBRARY_PATH="$PWD/../tools/bottles/mono-libgdiplus-6.2/6.2/lib:$PWD/../tools/bottles/libexif-0.6.26/0.6.26/lib${DYLD_LIBRARY_PATH:+:$DYLD_LIBRARY_PATH}" \
bash build.sh
```

| 项目 | 结果 |
| --- | --- |
| 退出码 | `0` |
| 主工程 | `0 Error`，35 个警告 |
| 测试工程 | `0 Error`，3 个警告 |
| 默认测试 | 总数 `342`，通过 `342`，失败 `0` |
| `git diff --check` | 通过 |

警告仍来自 xbuild 14.0 不支持 `v4.8` 的固定提示、源码通配 `CS2002` 重复项与未使用字段；没有编译错误。

`--windows-ui` 在 macOS 上退出码为 `2`，输出 `[SKIP] ... 未执行 11 条`。Windows 窗体测试、实际 CI 结果与退出码仍须由主审推送后取得，本机不能宣称通过。

## 3. 改动与备份

本轮仅修改：

```text
src/DeviceEditorControl.cs
tests/SettingsUiTests.cs
```

修改前备份：

```text
/Users/zach/Documents/Codex/2026-11-12/inspection-widget-backups/ci-fix-20261112-121547
```

原文件 SHA-256：

```text
a521254cf11230c66138bbadeb8acabe4b3cc9e9d34a6342081044005bdb7892  src/DeviceEditorControl.cs
1dee7b2937f040be0e2231dcd5d79991459ca43085b515db01b51f14c29fb6cd  tests/SettingsUiTests.cs
```

工作树未提交；未推送、未修改 PR、未触发新的 CI。构建生成的 `assets/app.ico` 与 `/tmp/iw-ci-fix-*.log` 已清理。
