#!/usr/bin/env bash
# 巡检截图小组件 —— Mono 构建脚本
# 用 msbuild（优先）或 xbuild 以 Release 配置构建，并把产物放进 build/。
# 任一步失败以非零退出。
set -euo pipefail

cd "$(dirname "$0")"

PROJECT="src/InspectionWidget.csproj"
OUT_DIR="build"

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
echo "构建 $PROJECT（Release）..."
if [ "$BUILDER" = "msbuild" ]; then
    msbuild /p:Configuration=Release /p:Platform=AnyCPU "$PROJECT"
else
    # Mono 6.8 的 mcs 最高支持 C# 7.2，故 xbuild 时覆盖 LangVersion；
    # csproj 内仍为 7.3，VS2022 构建不受影响。
    xbuild /p:Configuration=Release /p:Platform=AnyCPU /p:LangVersion=7.2 "$PROJECT"
fi

# 收集产物（v1.0.2 起 AssemblyName 为「巡检助手」）
mkdir -p "$OUT_DIR"
cp "src/bin/Release/巡检助手.exe" "$OUT_DIR/巡检助手.exe"
cp "app.manifest" "$OUT_DIR/巡检助手.exe.manifest"
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
    echo "发现测试工程 $TEST_PROJECT，构建中..."
    if [ "$BUILDER" = "msbuild" ]; then
        msbuild /p:Configuration=Release /p:Platform=AnyCPU "$TEST_PROJECT"
    else
        xbuild /p:Configuration=Release /p:Platform=AnyCPU /p:LangVersion=7.2 "$TEST_PROJECT"
    fi

    # 运行测试可执行文件（若构建产生了 exe）
    # 只跑 *.Tests.exe：tests 输出目录里还有 ProjectReference 依赖拷贝的主程序
    # 巡检助手.exe（GUI 程序，无显示环境必崩），不能当测试运行（Task 1 接线时修正）。
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
