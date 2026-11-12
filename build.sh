#!/usr/bin/env bash
# 巡检截图小组件 —— Mono 构建脚本
# 用 msbuild（优先）或 xbuild 以 Release 配置构建，并把产物放进 build/。
# 任一步失败以非零退出。
set -euo pipefail

cd "$(dirname "$0")"

PROJECT="src/InspectionWidget.csproj"
OUT_DIR="build"

# 应用图标源文件以 base64 文本入库；构建前按需还原。
# 已有合法 .ico（文件头 00 00 01 00）时保留，不覆盖；解码失败即退出。
restore_app_icon() {
    local icon="assets/app.ico"
    local b64="assets/app.ico.base64.txt"
    local header=""
    if [ -s "$icon" ]; then
        header=$(od -An -tx1 -N4 "$icon" 2>/dev/null | tr -d ' \n')
        if [ "$header" = "00000100" ]; then
            echo "应用图标已存在且有效，保留：$icon"
            return 0
        fi
    fi
    if [ ! -s "$b64" ]; then
        echo "错误：缺少 $b64，无法还原应用图标。" >&2
        return 1
    fi

    local tmp="assets/.app.ico.restore.$$"
    rm -f "$tmp"
    if base64 --decode "$b64" > "$tmp" 2>/dev/null; then
        :
    elif base64 -d "$b64" > "$tmp" 2>/dev/null; then
        :
    elif base64 -D "$b64" > "$tmp" 2>/dev/null; then
        :
    elif command -v openssl >/dev/null 2>&1 \
        && openssl base64 -d -A -in "$b64" -out "$tmp" 2>/dev/null; then
        :
    else
        rm -f "$tmp"
        echo "错误：当前平台没有可用的 base64 解码工具，无法还原应用图标。" >&2
        return 1
    fi
    header=$(od -An -tx1 -N4 "$tmp" 2>/dev/null | tr -d ' \n')
    if [ "$header" != "00000100" ]; then
        rm -f "$tmp"
        echo "错误：还原出的 app.ico 不是有效 ICO。" >&2
        return 1
    fi
    mv -f "$tmp" "$icon"
    echo "已还原应用图标：$icon"
}

restore_app_icon

# 选择构建工具：msbuild 优先，否则 xbuild
if command -v msbuild >/dev/null 2>&1; then
    BUILDER="msbuild"
elif command -v xbuild >/dev/null 2>&1; then
    BUILDER="xbuild"
else
    echo "错误：未找到 msbuild 或 xbuild，请先安装 Mono。" >&2
    exit 1
fi

echo "使用构建工具：$BUILDER"
echo "构建 ${PROJECT}（Release）..."
if [ "$BUILDER" = "msbuild" ]; then
    msbuild /p:Configuration=Release /p:Platform=AnyCPU "$PROJECT"
else
    # Mono 6.8 的 mcs 最高支持 C# 7.2，故 xbuild 时覆盖 LangVersion；
    # csproj 内仍为 7.3，VS2022 构建不受影响。
    xbuild /p:Configuration=Release /p:Platform=AnyCPU /p:LangVersion=7.2 "$PROJECT"
fi

# 收集产物：程序集名与交付名同为「InspectionWidget」（见 csproj 注释），直接收集，
# manifest 同名跟随；顺带清理 build/ 里可能残留的旧中文交付名产物。
mkdir -p "$OUT_DIR"
rm -f "$OUT_DIR/InspectionWidget.exe" "$OUT_DIR/InspectionWidget.exe.manifest" "$OUT_DIR/巡检助手.exe" "$OUT_DIR/巡检助手.exe.manifest"
cp "src/bin/Release/InspectionWidget.exe" "$OUT_DIR/InspectionWidget.exe"
cp "app.manifest" "$OUT_DIR/InspectionWidget.exe.manifest"
echo "产物已放入 $OUT_DIR/："
ls -la "$OUT_DIR"

# 若存在 tests 工程则一并构建并运行测试（不存在则跳过）
TEST_PROJECT=""
for candidate in tests/*.csproj; do
    if [ -f "$candidate" ]; then
        TEST_PROJECT="$candidate"
        break
    fi
done

if [ -n "$TEST_PROJECT" ]; then
    echo "发现测试工程 ${TEST_PROJECT}，构建中..."
    if [ "$BUILDER" = "msbuild" ]; then
        msbuild /p:Configuration=Release /p:Platform=AnyCPU "$TEST_PROJECT"
    else
        xbuild /p:Configuration=Release /p:Platform=AnyCPU /p:LangVersion=7.2 "$TEST_PROJECT"
    fi

    # 运行测试可执行文件（若构建产生了 exe）
    # 只跑 *.Tests.exe：tests 输出目录里还有 ProjectReference 依赖拷贝的主程序
    # InspectionWidget.exe（GUI 程序，无显示环境必崩），不能当测试运行（Task 1 接线时修正）。
    for test_exe in tests/bin/Release/*.Tests.exe; do
        if [ -f "$test_exe" ]; then
            echo "运行测试：$test_exe"
            mono "$test_exe"
        fi
    done
else
    echo "未发现 tests 工程，跳过测试。"
fi

echo "构建完成。"
