# Task 7 修复轮范围复审：新增凭据「清除」绕过必填校验

- 复审范围：仅本轮一项缺陷（`417853e..b85f128`，改动 `src/SettingsForm.cs`、`tests/SettingsFormTests.cs`），不扩大。
- 复审方式：逐项看代码（`CollectSecret`、`CreateCredentialRow`、`OnCredentialClearClicked`、保存收集段、新增用例），不只看修复说明。测试总数以协调者亲跑的 186/186 为准，本轮未重跑。

## 结论：✅ 通过

## 核对一：新旧条目区分依据可靠 —— ✅

- 区分用显式标记 `IsNew`，不用 `OriginalSecret` 是否为空来推断：`CreateCredentialRow(..., isNew)` 在建行时一次写入 `CredentialRow.IsNew`，全仓无其他赋值点，行生命周期内不会翻转。
- 既有条目入口唯一：`LoadCredentialRows` 对 `_working.Credentials` 每条都传 `isNew=false`（并带入原密文）；新增入口唯一：`OnAddCredentialClicked` 传 `isNew=true`、原密文 null。
- 因此原密文为 null/空的残缺既有条目仍是既有条目（`IsNew=false`），不会被误判为新增而误伤；既有用例「已存条目原密文为 null 时留空得空密文」仍走原路径。
- 「既有条目先输新内容再清除」语义未被误伤：点击处理仍置 `Cleared=true` 并清空内容框；保存收集时 `CollectSecret` 中 `row.Cleared && !row.IsNew` 优先于新输入分支、返回空密文，与修复前一致；既有用例「清除标记优先于新输入，密文置空」（`Row("门禁","原密文XYZ",false,true,"新内容abc")` → 空）未改动、仍锚定此语义。

## 核对二：UI 禁用与保存校验兜底两层均真实存在 —— ✅

- UI 层（两道）：`CreateCredentialRow` 中 `row.ClearButton.Enabled = !isNew`，新增行「清除」按钮建行即禁用（`IsNew` 建行后不变，无状态过期问题）；`OnCredentialClearClicked` 开头另有 `if (row.IsNew) return;` 守卫，即便点击事件被程序化触发也无效。
- 保存校验兜底：`CollectSecret` 改为仅 `row.Cleared && !row.IsNew` 才返回空密文；新增条目带清除标记会穿过该分支，内容为空时落到 `row.IsNew` 分支，返回 error「新凭据的内容不能为空。」且 secret 为 null。
- 绕过 UI 仍拦得住：保存流程逐行以行对象字段原样组装 `RowInput`（`IsNew`、`Cleared`、`SecretBox.Text` 直传），`CollectSecret` 返回 error 非空即 `ShowError` 定位该行并 `return`——不保存、不关窗。故即便程序化把新增行 `Cleared` 置 true，也无法存下空凭据；新增行若确有新输入内容，则忽略清除标记正常加密落盘，符合「内容为空必报错」的口径。

## 核对三：新增 2 条用例真锚定两个场景 —— ✅

- 用例①「新增条目带清除标记且空内容校验失败」：`Row("新凭据", null, true, true, string.Empty)` → 断言 error 非空、secret 为 null，正是缺陷场景（新增＋清除＋空 → 失败）。
- 用例②「既有条目清除校验通过且密文为空」：`Row("门禁", "原密文XYZ", false, true, string.Empty)` → 断言 error 为 null、secret 为 `string.Empty`，正是既有清除的保留语义（既有＋清除 → 通过且密文空）。
- 两条均走与生产保存路径同一入口 `CredentialRowCollector.CollectSecret`，非另设旁路；与报告所述 184＋2＝186、协调者亲跑 186/186 一致。

## 总评

✅ 通过。两层修复与缺陷口径一一对应：新增行清除在 UI 层禁用＋处理器守卫，保存校验在纯逻辑层对新增条目忽略清除标记、空内容必报错；既有条目清除语义（含先输后清）零变化；无范围外改动（diff 仅上述两文件、+30/−3）。
