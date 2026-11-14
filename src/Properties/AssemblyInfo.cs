using System.Reflection;
using System.Runtime.CompilerServices;

// 测试工程需要触达 ErrorLog 的内部目录注入点（SetDirectoriesForTests），
// 仅向测试程序集开放内部成员，产品对外面不变。
[assembly: InternalsVisibleTo("InspectionWidget.Tests")]

[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]
[assembly: AssemblyInformationalVersion("1.1.0-rc")]
