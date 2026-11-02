# Task 9 报告：本地收口（README、版本记录、入库红线核对、本地标签、试用说明）

- 状态：DONE
- 提交：`8c956b9e6f908bad3d13fa1c9687fb19ed6bad9c`（`docs(stage-9): README 与 v1.0.0 版本记录`，仅 README.md 一个文件，+16/-8）
- 本地标签：`v1.0.0` → 指向 8c956b9（提交后新建，此前无同名标签；`git tag -l` 输出仅 `v1.0.0`）

## 交付物

- README.md（33 行）：补「这是什么」「怎么编译」（VS 2022 +「.NET 桌面开发」+ .NET Framework 4.8 目标包、Release 构建；Mono `bash build.sh` 为备选、manifest 外置、正式交付以 VS 为准）「怎么运行」「版本记录」「远程仓库」五节；原文「设备（集群）」的旧称已去除，全文无「集群」字样。
- build/试用说明.md（30 行，新建）：按简报大纲 6 节（怎么运行 / 第一次要做的事 / 怎么截图 / 凭据怎么加 / SmartScreen 提示怎么办 / 试跑重点看这几项），简体中文、每节两三句；该文件在 build/ 内、被 .gitignore 排除、不入库（用户定的交付方式，`git check-ignore` 已验证命中 `.gitignore:35:build/`）。
- exe 实测（提交前亲跑，非抄报告）：`build/InspectionWidget.exe` 83,456 字节，SHA256 `597ab0ee1f1e6eabac0a4ad65b2f0e592ca59c50f21d9dcd82f84239d8d24e79`；README 版本记录所填与此一致，提交后复跑仍一致。
- 测试：未重编（避免产物哈希漂移），直接以 mono 运行既有 tests/bin/Release 测试 exe，结果 191 通过 / 0 失败，与基线一致；运行前后 build exe 哈希不变。

## 红线核对结果

- 不加 remote、不 push：`git remote -v` 输出为空（0 行）；全程仅本地 commit 与本地 `git tag`，无任何网络发布。README「远程仓库」如实写明远程同步与标签推送延期、未推送。
- `git status`：本任务开始时干净；提交后干净（本报告文件除外，待协调者关账提交）。
- `git ls-files` 扫描：无 config.json 及变体、无 *.png、无 error.log、无 bin/、无 obj/、无 *.exe。
- .gitignore：第 35 行含 `build/`，无需补行。
- 凭据明文 grep：命中的均为虚构测试值，清单见下节。

## 测试假数据清单（均为虚构，非真实凭据）

- tests/ClipboardGuardTests.cs:65,68：`first-secret`、`second-secret`（剪贴板守卫用例的占位字符串）。
- tests/ConfigStoreTests.cs:43-44,63,189：`QUJDZA==`、`RUZHSA==`、`QUJD`（Base64 假密文，配合 FakeProtector 直存口径）。
- tests/SettingsFormTests.cs:34-126：`原密文XYZ`、`新内容abc`、`内容1`、`密文`、`改密文`（设置收集用例的占位值）。
- 生产代码侧 grep 命中的仅为字段名与逻辑（EncryptedSecret、PasswordChar 掩码等），无任何明文凭据。

## git log --oneline 前 12 条摘要

8c956b9 docs(stage-9) 本任务提交 → 748aaa0 Task 8 关账 → b285328 stage-8 修复 → a5bb6ca stage-8 验收记录 → a8b197b stage-8 兜底 → 376f84b Task 7 关账 → b85f128 stage-7 修复 → 417853e stage-7 设置窗口 → db488ad Task 6 关账 → fcb8f98 stage-6 修复 → d5e1b91 stage-6 凭据模块 → 330ae4b Task 5 关账。

## 遗留 / concerns

1. **exe 哈希与 docs/验收记录.md 记载不一致**：验收记录与 Task 8 报告记的是 83,456 字节 / SHA256 `64e2c940…`，本机实测同为 83,456 字节但 SHA256 为 `597ab0ee…`——Task 8 修复轮（b285328）后重编产物未回写该文档所致。按任务纪律未动 docs/，README 以实测为准；该文档订正留协调者决定。
2. **build/InspectionWidget.exe.manifest 仍被 git 跟踪**：自 Task 0 提交 e6a81d4 起入库，早于 .gitignore 的 `build/` 规则（.gitignore 只挡未跟踪文件）；该文件仅为外置 manifest 文本、无敏感内容，exe 本体未入库。本任务未擅自 `git rm --cached`，是否清退留协调者裁决。
3. Windows 实机验收（15 项中的「仅编译通过/待实机」共 12 项）仍待用户在单位 Windows 机器按 build/试用说明.md 试跑；远程仓库同步与标签推送按计划延期，未做。
