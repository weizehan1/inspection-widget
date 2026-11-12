# Windows CI Esc 取消修复 · 验证记录

## 1. 剩余失败与根因

Windows PR CI run `43154249288`、job `130970368973` 实际执行 353 条，352 条通过，唯一失败：

```text
UI：设备改名输入后按 Esc 取消
AssertException：期望 False，实际 True。 Esc 后必须退出编辑态
```

`SettingsForm.ProcessCmdKey` 与 `OnFormClosing` 原先直接调用 `_deviceEditor.Grid.CancelEdit()`。Windows 上该方法可能只丢弃绑定值而仍停留在编辑态，同时 `CellEndEdit` 会误触发 `RaiseChanged()`。测试也只直接调用了网格方法，没有覆盖窗口真实 Esc 入口。

## 2. 修正

- `DeviceEditorControl` 新增 `CancelCurrentEdit()`：未编辑时直接返回；取消期间跳过 `CellValidating`、`CellValueChanged` 和 `CellEndEdit` 的改名/dirty 逻辑；`CancelEdit()` 后仍处于编辑态时，先把编辑控件文本恢复为进入编辑时的值，再 `EndEdit()`；结束后按原值校正模型和单元格，清理行/单元格错误与编辑基线。
- 暴露 `LastCancelDiagnostic`，记录 `CancelEdit`/`EndEdit` 是否调用及最终 `editing` 状态，失败时由设置窗显示并保留窗口。
- `SettingsForm` 的 Esc 和关窗放弃设备编辑统一调用 `CancelCurrentEdit()`，不再直接调用网格取消。
- `tests/SettingsUiTests.cs` 改为通过反射调用 `SettingsForm.ProcessCmdKey(ref Message, Keys.Escape)` 的真实窗口路径；分别覆盖正常输入 `临时改名` 与非法输入 `CON` 两次取消，逐次断言退出编辑态、草稿名、`LastDevice` 与磁盘仍为 `设备A`。保留后续应用与磁盘断言。

修改前备份：

```text
/Users/zach/Documents/Codex/2026-11-12/inspection-widget-backups/esc-cancel-20261112-122923
```

## 3. 本轮本地验证

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

`--windows-ui` 在 macOS 上退出码 `2`，输出 `[SKIP] ... 未执行 11 条`，不计为通过。Windows 结果以下一轮 CI 为准。
