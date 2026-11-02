using System.Collections.Generic;
using InspectionWidget.Config;

namespace InspectionWidget.Tests
{
    /// <summary>DeviceNameValidator 全部规则逐条一用例。</summary>
    public static class DeviceNameValidatorTests
    {
        private static readonly List<string> None = new List<string>();

        public static void RunAll()
        {
            TestRunner.Run("校验器：null 名非法", () =>
                Assert.NotNull(DeviceNameValidator.Validate(null, None)));
            TestRunner.Run("校验器：空名非法", () =>
                Assert.NotNull(DeviceNameValidator.Validate(string.Empty, None)));
            TestRunner.Run("校验器：纯空格名非法", () =>
                Assert.NotNull(DeviceNameValidator.Validate("   ", None)));

            // 非法字符逐个一用例：\ / : * ? " < > |
            foreach (char c in new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' })
            {
                char bad = c;
                TestRunner.Run("校验器：含非法字符 '" + bad + "' 非法", () =>
                    Assert.NotNull(DeviceNameValidator.Validate("设备" + bad + "名", None)));
            }

            TestRunner.Run("校验器：含控制字符（Tab）非法", () =>
                Assert.NotNull(DeviceNameValidator.Validate("设备\t名", None)));
            TestRunner.Run("校验器：含控制字符（\\u0001）非法", () =>
                Assert.NotNull(DeviceNameValidator.Validate("设备\u0001名", None)));

            TestRunner.Run("校验器：'.' 非法", () =>
                Assert.NotNull(DeviceNameValidator.Validate(".", None)));
            TestRunner.Run("校验器：'..' 非法", () =>
                Assert.NotNull(DeviceNameValidator.Validate("..", None)));
            TestRunner.Run("校验器：尾部空格非法", () =>
                Assert.NotNull(DeviceNameValidator.Validate("设备A ", None)));
            TestRunner.Run("校验器：尾部点非法", () =>
                Assert.NotNull(DeviceNameValidator.Validate("设备A.", None)));

            TestRunner.Run("校验器：保留名 CON 非法", () =>
                Assert.NotNull(DeviceNameValidator.Validate("CON", None)));
            TestRunner.Run("校验器：保留名 prn（小写）非法", () =>
                Assert.NotNull(DeviceNameValidator.Validate("prn", None)));
            TestRunner.Run("校验器：保留名 AUX 非法", () =>
                Assert.NotNull(DeviceNameValidator.Validate("AUX", None)));
            TestRunner.Run("校验器：保留名 NUL 非法", () =>
                Assert.NotNull(DeviceNameValidator.Validate("NUL", None)));
            TestRunner.Run("校验器：保留名 COM1 非法", () =>
                Assert.NotNull(DeviceNameValidator.Validate("COM1", None)));
            TestRunner.Run("校验器：保留名 com9（小写）非法", () =>
                Assert.NotNull(DeviceNameValidator.Validate("com9", None)));
            TestRunner.Run("校验器：保留名 LPT1 非法", () =>
                Assert.NotNull(DeviceNameValidator.Validate("LPT1", None)));
            TestRunner.Run("校验器：保留名 LPT9 非法", () =>
                Assert.NotNull(DeviceNameValidator.Validate("LPT9", None)));
            TestRunner.Run("校验器：带扩展名 CON.txt 非法", () =>
                Assert.NotNull(DeviceNameValidator.Validate("CON.txt", None)));
            TestRunner.Run("校验器：带扩展名 nul.png 非法", () =>
                Assert.NotNull(DeviceNameValidator.Validate("nul.png", None)));
            TestRunner.Run("校验器：COM0 不是保留名（合法）", () =>
                Assert.Null(DeviceNameValidator.Validate("COM0", None)));
            TestRunner.Run("校验器：CONSOLE 不是保留名（合法）", () =>
                Assert.Null(DeviceNameValidator.Validate("CONSOLE", None)));

            TestRunner.Run("校验器：与现有名完全相同重名非法", () =>
                Assert.NotNull(DeviceNameValidator.Validate("设备A", new List<string> { "设备A" })));
            TestRunner.Run("校验器：大小写不敏感重名（设备A vs 设备a）非法", () =>
                Assert.NotNull(DeviceNameValidator.Validate("设备a", new List<string> { "设备A" })));
            TestRunner.Run("校验器：英文名大小写不敏感重名非法", () =>
                Assert.NotNull(DeviceNameValidator.Validate("ServerA", new List<string> { "servera" })));
            TestRunner.Run("校验器：与现有名不同合法", () =>
                Assert.Null(DeviceNameValidator.Validate("设备B", new List<string> { "设备A" })));
            TestRunner.Run("校验器：existing 为 null 时只查自身合法性", () =>
                Assert.Null(DeviceNameValidator.Validate("设备A", null)));

            TestRunner.Run("校验器：合法中文名通过", () =>
                Assert.Null(DeviceNameValidator.Validate("一号机房", None)));
            TestRunner.Run("校验器：合法名含中间空格与连字符通过", () =>
                Assert.Null(DeviceNameValidator.Validate("机房 A-1", None)));
            TestRunner.Run("校验器：合法名含中间点通过（如 v2.1机房）", () =>
                Assert.Null(DeviceNameValidator.Validate("v2.1机房", None)));

            TestRunner.Run("ValidateAll：全部合法返回 null", () =>
                Assert.Null(DeviceNameValidator.ValidateAll(new List<string> { "设备A", "设备B" })));
            TestRunner.Run("ValidateAll：内部重名（大小写不敏感）非法", () =>
                Assert.NotNull(DeviceNameValidator.ValidateAll(new List<string> { "设备A", "设备a" })));
            TestRunner.Run("ValidateAll：含非法名返回错误", () =>
                Assert.NotNull(DeviceNameValidator.ValidateAll(new List<string> { "设备A", "坏/名" })));
            TestRunner.Run("ValidateAll：含保留名返回错误", () =>
                Assert.NotNull(DeviceNameValidator.ValidateAll(new List<string> { "设备A", "con.txt" })));
            TestRunner.Run("ValidateAll：空列表返回 null", () =>
                Assert.Null(DeviceNameValidator.ValidateAll(new List<string>())));

            // 路径长度（Review Focus 第 2 条）：rootPath 与 name 拼成的完整路径 > 259 报错
            TestRunner.Run("路径长度：恰好 259 字符合法", () =>
            {
                string root, name;
                BuildPathOfTotalLength(259, out root, out name);
                Assert.Equal(259, System.IO.Path.Combine(root, name).Length);
                Assert.Null(DeviceNameValidator.ValidateFullPathLength(root, name));
            });
            TestRunner.Run("路径长度：260 字符非法", () =>
            {
                string root, name;
                BuildPathOfTotalLength(260, out root, out name);
                Assert.Equal(260, System.IO.Path.Combine(root, name).Length);
                Assert.NotNull(DeviceNameValidator.ValidateFullPathLength(root, name));
            });
            TestRunner.Run("路径长度：深根目录 + 长名（模拟加 -99.png 后缀）非法", () =>
            {
                string root = new string('r', 250); // 250 + 1 分隔符 + "设备A-99.png" 10 字符 = 261 > 259
                Assert.NotNull(DeviceNameValidator.ValidateFullPathLength(root, "设备A-99.png"));
            });
            TestRunner.Run("路径长度：普通根目录与短名合法", () =>
                Assert.Null(DeviceNameValidator.ValidateFullPathLength("D:\\Screenshots", "设备A")));
        }

        /// <summary>拼出 Path.Combine(root, name) 恰好 total 字符的根与名（名取合法字符）。</summary>
        private static void BuildPathOfTotalLength(int total, out string root, out string name)
        {
            name = "设备A"; // 3 字符
            int rootLength = total - 1 /* 路径分隔符 */ - name.Length;
            root = new string('r', rootLength);
        }
    }
}
