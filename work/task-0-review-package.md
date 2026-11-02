# Review package Task 0: 3cb364c..7457764
7457764 feat(stage-0): 工程骨架、manifest、Mono 构建链

 InspectionWidget.sln        | 21 ++++++++++++++
 README.md                   | 25 +++++++++++++++++
 app.manifest                | 23 ++++++++++++++++
 build.sh                    | 67 +++++++++++++++++++++++++++++++++++++++++++++
 src/InspectionWidget.csproj | 59 +++++++++++++++++++++++++++++++++++++++
 src/MainForm.cs             | 26 ++++++++++++++++++
 src/Program.cs              | 41 +++++++++++++++++++++++++++
 7 files changed, 262 insertions(+)

diff --git a/InspectionWidget.sln b/InspectionWidget.sln
new file mode 100644
index 0000000..a79329f
--- /dev/null
+++ b/InspectionWidget.sln
@@ -0,0 +1,21 @@
+Microsoft Visual Studio Solution File, Format Version 12.00
+# Visual Studio Version 17
+VisualStudioVersion = 17.0.31903.59
+MinimumVisualStudioVersion = 10.0.40219.1
+Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "InspectionWidget", "src\InspectionWidget.csproj", "{C3CF6B60-4871-4E2E-B57E-82EFD26A79F2}"
+EndProject
+Global
+	GlobalSection(SolutionConfigurationPlatforms) = preSolution
+		Debug|Any CPU = Debug|Any CPU
+		Release|Any CPU = Release|Any CPU
+	EndGlobalSection
+	GlobalSection(ProjectConfigurationPlatforms) = postSolution
+		{C3CF6B60-4871-4E2E-B57E-82EFD26A79F2}.Debug|Any CPU.ActiveCfg = Debug|AnyCPU
+		{C3CF6B60-4871-4E2E-B57E-82EFD26A79F2}.Debug|Any CPU.Build.0 = Debug|AnyCPU
+		{C3CF6B60-4871-4E2E-B57E-82EFD26A79F2}.Release|Any CPU.ActiveCfg = Release|AnyCPU
+		{C3CF6B60-4871-4E2E-B57E-82EFD26A79F2}.Release|Any CPU.Build.0 = Release|AnyCPU
+	EndGlobalSection
+	GlobalSection(SolutionProperties) = preSolution
+		HideSolutionNode = FALSE
+	EndGlobalSection
+EndGlobal
diff --git a/README.md b/README.md
new file mode 100644
index 0000000..082efc0
--- /dev/null
+++ b/README.md
@@ -0,0 +1,25 @@
+# 巡检截图小组件（InspectionWidget）
+
+一个 Windows 桌面小组件：点选设备（集群）→ 点选目标窗口 → 一键只截该窗口，截图按「根目录\日期\设备名.png」自动归档（重名自动顺延 -1、-2）。基于 C# WinForms / .NET Framework 4.8，零第三方依赖。
+
+## 编译方法
+
+方式一（Windows）：用 Visual Studio 2022 打开 `InspectionWidget.sln`，以 Release 配置、目标框架 .NET Framework 4.8 构建。
+
+方式二（本机 / Linux）：安装 Mono 后在仓库根目录执行：
+
+```bash
+bash build.sh
+```
+
+脚本会用 `msbuild`（找不到则 `xbuild`）以 Release 配置构建 `src/InspectionWidget.csproj`，并把 `InspectionWidget.exe` 与 `InspectionWidget.exe.manifest` 放进 `build/`；若存在 tests 工程会一并构建并运行测试。
+
+## 运行说明
+
+双击 `InspectionWidget.exe` 启动；程序在清单中声明了管理员权限，启动时会出现管理员确认（UAC）提示，确认后运行。首次启动时会请你选择截图保存的根目录，之后截图会按日期与设备名自动归档到该目录下。
+
+## 版本记录
+
+| 版本 | 日期 | 说明 |
+| ---- | ---- | ---- |
+|      |      |      |
diff --git a/app.manifest b/app.manifest
new file mode 100644
index 0000000..289d4da
--- /dev/null
+++ b/app.manifest
@@ -0,0 +1,23 @@
+<?xml version="1.0" encoding="utf-8"?>
+<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
+  <assemblyIdentity version="1.0.0.0" name="InspectionWidget" />
+  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
+    <security>
+      <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
+        <requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
+      </requestedPrivileges>
+    </security>
+  </trustInfo>
+  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
+    <application>
+      <!-- Windows 10 / Windows 11 -->
+      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" />
+    </application>
+  </compatibility>
+  <application xmlns="urn:schemas-microsoft-com:asm.v3">
+    <windowsSettings>
+      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">True/PM</dpiAware>
+      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2,PerMonitor</dpiAwareness>
+    </windowsSettings>
+  </application>
+</assembly>
diff --git a/build.sh b/build.sh
new file mode 100755
index 0000000..1369784
--- /dev/null
+++ b/build.sh
@@ -0,0 +1,67 @@
+#!/usr/bin/env bash
+# 巡检截图小组件 —— Mono 构建脚本
+# 用 msbuild（优先）或 xbuild 以 Release 配置构建，并把产物放进 build/。
+# 任一步失败以非零退出。
+set -euo pipefail
+
+cd "$(dirname "$0")"
+
+PROJECT="src/InspectionWidget.csproj"
+OUT_DIR="build"
+
+# 选择构建工具：msbuild 优先，否则 xbuild
+if command -v msbuild >/dev/null 2>&1; then
+    BUILDER="msbuild"
+elif command -v xbuild >/dev/null 2>&1; then
+    BUILDER="xbuild"
+else
+    echo "错误：未找到 msbuild 或 xbuild，请先安装 Mono。" >&2
+    exit 1
+fi
+
+echo "使用构建工具：$BUILDER"
+echo "构建 $PROJECT（Release）..."
+if [ "$BUILDER" = "msbuild" ]; then
+    msbuild /p:Configuration=Release /p:Platform=AnyCPU "$PROJECT"
+else
+    # Mono 6.8 的 mcs 最高支持 C# 7.2，故 xbuild 时覆盖 LangVersion；
+    # csproj 内仍为 7.3，VS2022 构建不受影响。
+    xbuild /p:Configuration=Release /p:Platform=AnyCPU /p:LangVersion=7.2 "$PROJECT"
+fi
+
+# 收集产物
+mkdir -p "$OUT_DIR"
+cp "src/bin/Release/InspectionWidget.exe" "$OUT_DIR/InspectionWidget.exe"
+cp "app.manifest" "$OUT_DIR/InspectionWidget.exe.manifest"
+echo "产物已放入 $OUT_DIR/："
+ls -la "$OUT_DIR"
+
+# 若存在 tests 工程则一并构建并运行测试（不存在则跳过）
+TEST_PROJECT=""
+for candidate in tests/*.csproj; do
+    if [ -f "$candidate" ]; then
+        TEST_PROJECT="$candidate"
+        break
+    fi
+done
+
+if [ -n "$TEST_PROJECT" ]; then
+    echo "发现测试工程 $TEST_PROJECT，构建中..."
+    if [ "$BUILDER" = "msbuild" ]; then
+        msbuild /p:Configuration=Release /p:Platform=AnyCPU "$TEST_PROJECT"
+    else
+        xbuild /p:Configuration=Release /p:Platform=AnyCPU /p:LangVersion=7.2 "$TEST_PROJECT"
+    fi
+
+    # 运行测试可执行文件（若构建产生了 exe）
+    for test_exe in tests/bin/Release/*.exe; do
+        if [ -f "$test_exe" ]; then
+            echo "运行测试：$test_exe"
+            mono "$test_exe"
+        fi
+    done
+else
+    echo "未发现 tests 工程，跳过测试。"
+fi
+
+echo "构建完成。"
diff --git a/src/InspectionWidget.csproj b/src/InspectionWidget.csproj
new file mode 100644
index 0000000..05ff454
--- /dev/null
+++ b/src/InspectionWidget.csproj
@@ -0,0 +1,59 @@
+<?xml version="1.0" encoding="utf-8"?>
+<Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
+  <Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props" Condition="Exists('$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props')" />
+  <PropertyGroup>
+    <Configuration Condition=" '$(Configuration)' == '' ">Debug</Configuration>
+    <Platform Condition=" '$(Platform)' == '' ">AnyCPU</Platform>
+    <ProjectGuid>{C3CF6B60-4871-4E2E-B57E-82EFD26A79F2}</ProjectGuid>
+    <OutputType>WinExe</OutputType>
+    <RootNamespace>InspectionWidget</RootNamespace>
+    <AssemblyName>InspectionWidget</AssemblyName>
+    <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
+    <FileAlignment>512</FileAlignment>
+    <LangVersion>7.3</LangVersion>
+    <ApplicationManifest>..\app.manifest</ApplicationManifest>
+    <AutoGenerateBindingRedirects>false</AutoGenerateBindingRedirects>
+    <Deterministic>true</Deterministic>
+  </PropertyGroup>
+  <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' ">
+    <PlatformTarget>AnyCPU</PlatformTarget>
+    <DebugSymbols>true</DebugSymbols>
+    <DebugType>full</DebugType>
+    <Optimize>false</Optimize>
+    <OutputPath>bin\Debug\</OutputPath>
+    <DefineConstants>DEBUG;TRACE</DefineConstants>
+    <ErrorReport>prompt</ErrorReport>
+    <WarningLevel>4</WarningLevel>
+    <Prefer32Bit>false</Prefer32Bit>
+    <LangVersion>7.3</LangVersion>
+  </PropertyGroup>
+  <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Release|AnyCPU' ">
+    <PlatformTarget>AnyCPU</PlatformTarget>
+    <DebugType>pdbonly</DebugType>
+    <DebugSymbols>true</DebugSymbols>
+    <Optimize>true</Optimize>
+    <OutputPath>bin\Release\</OutputPath>
+    <DefineConstants>TRACE</DefineConstants>
+    <ErrorReport>prompt</ErrorReport>
+    <WarningLevel>4</WarningLevel>
+    <Prefer32Bit>false</Prefer32Bit>
+    <LangVersion>7.3</LangVersion>
+  </PropertyGroup>
+  <ItemGroup>
+    <Reference Include="System" />
+    <Reference Include="System.Core" />
+    <Reference Include="System.Drawing" />
+    <Reference Include="System.Windows.Forms" />
+    <Reference Include="System.Security" />
+    <Reference Include="System.Runtime.Serialization" />
+  </ItemGroup>
+  <ItemGroup>
+    <Compile Include="**\*.cs" />
+  </ItemGroup>
+  <ItemGroup>
+    <None Include="..\app.manifest">
+      <Link>app.manifest</Link>
+    </None>
+  </ItemGroup>
+  <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
+</Project>
diff --git a/src/MainForm.cs b/src/MainForm.cs
new file mode 100644
index 0000000..fe98a3a
--- /dev/null
+++ b/src/MainForm.cs
@@ -0,0 +1,26 @@
+using System.Drawing;
+using System.Windows.Forms;
+
+namespace InspectionWidget
+{
+    /// <summary>
+    /// 最小占位窗体（Task 2 会重写为完整界面）。
+    /// </summary>
+    public class MainForm : Form
+    {
+        public MainForm()
+        {
+            Text = "巡检截图";
+            TopMost = true;
+            Size = new Size(230, 200);
+            FormBorderStyle = FormBorderStyle.FixedSingle;
+            MaximizeBox = false;
+            StartPosition = FormStartPosition.Manual;
+
+            Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
+            Location = new Point(
+                workingArea.Right - Width,
+                workingArea.Bottom - Height);
+        }
+    }
+}
diff --git a/src/Program.cs b/src/Program.cs
new file mode 100644
index 0000000..d4c95ec
--- /dev/null
+++ b/src/Program.cs
@@ -0,0 +1,41 @@
+using System;
+using System.Runtime.InteropServices;
+using System.Threading;
+using System.Windows.Forms;
+
+namespace InspectionWidget
+{
+    internal static class Program
+    {
+        // DPI 兜底声明（本任务内联；Native 模块在 Task 3 才建立）
+        [DllImport("user32.dll", SetLastError = true)]
+        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);
+
+        [STAThread]
+        private static void Main()
+        {
+            bool createdNew;
+            using (var mutex = new Mutex(true, "InspectionWidget.SingleInstance", out createdNew))
+            {
+                if (!createdNew)
+                {
+                    return;
+                }
+
+                try
+                {
+                    // PerMonitorV2 = -4；失败忽略（旧系统无此 API 时靠 manifest 声明）
+                    SetProcessDpiAwarenessContext(new IntPtr(-4));
+                }
+                catch
+                {
+                    // 忽略 DPI 设置失败
+                }
+
+                Application.EnableVisualStyles();
+                Application.SetCompatibleTextRenderingDefault(false);
+                Application.Run(new MainForm());
+            }
+        }
+    }
+}
