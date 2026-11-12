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

## 4. 第二轮 Windows CI 剩余失败

Windows CI run `43153846399`、job `130969085101`：递归已消失，实际执行 353 条，350 条通过，3 条窗体用例失败。

1. “改名后直接应用”的 `Assert.True(applied)` 返回 `false`。断言现在附带 `_feedbackLabel.Text`，下一轮可直接看到真实拒绝原因。
2. “设备改名 Esc 取消”在 `grid.CancelEdit()` 后仍报告 `IsCurrentCellInEditMode=true`。根因是 `EditMode=EditOnEnter` 会在编辑结束后因当前单元格仍被选中而立即重新进入编辑，导致 `CommitEdit` 的检查误判。
3. “导入前校验保留凭据输入”报 `找不到字段：NameBox`。`CredentialRow.NameBox` 是 public 字段，而测试辅助只查 `Instance | NonPublic`。

本轮修正：

- 网格改为 `DataGridViewEditMode.EditOnKeystrokeOrF2`，保留输入与 F2 编辑；显式接线双击和 Enter 进入编辑，编辑态内的提交/移动仍交给原生行为。
- 测试辅助的字段查找加入 `BindingFlags.Public`，public 的 `NameBox`/`SecretBox` 可正常读取。
- 关键 `applied` 断言附带实际反馈文本；未删除、跳过或放宽任何窗体用例及语义断言。

复跑 Mono：退出码 `0`，主工程 `35 Warning(s), 0 Error(s)`，测试工程 `3 Warning(s), 0 Error(s)`，默认测试 `342 / 342` 通过，`git diff --check` 通过。macOS 上 `--windows-ui` 仍为退出码 `2`、跳过 11 条，不计为 Windows 通过；最终结果以主审推送后的 Windows CI 为准。
