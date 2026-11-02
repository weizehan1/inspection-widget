using System;

namespace InspectionWidget.Tests
{
    /// <summary>断言失败异常（由 TestRunner 捕获并记为用例失败）。</summary>
    public sealed class AssertException : Exception
    {
        public AssertException(string message) : base(message) { }
    }

    /// <summary>微型断言：失败时抛 AssertException。</summary>
    public static class Assert
    {
        public static void True(bool condition, string detail = null)
        {
            if (!condition)
                throw new AssertException("期望 True，实际 False。" + Detail(detail));
        }

        public static void False(bool condition, string detail = null)
        {
            if (condition)
                throw new AssertException("期望 False，实际 True。" + Detail(detail));
        }

        public static void Equal<T>(T expected, T actual, string detail = null)
        {
            if (!object.Equals(expected, actual))
                throw new AssertException("期望 <" + Show(expected) + ">，实际 <" + Show(actual) + ">。" + Detail(detail));
        }

        public static void Null(object value, string detail = null)
        {
            if (value != null)
                throw new AssertException("期望 null，实际 <" + Show(value) + ">。" + Detail(detail));
        }

        public static void NotNull(object value, string detail = null)
        {
            if (value == null)
                throw new AssertException("期望非 null，实际 null。" + Detail(detail));
        }

        public static void Throws<TException>(Action action, string detail = null) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            catch (Exception ex)
            {
                throw new AssertException("期望抛 " + typeof(TException).Name + "，实际抛 " + ex.GetType().Name + "。" + Detail(detail));
            }
            throw new AssertException("期望抛 " + typeof(TException).Name + "，实际未抛异常。" + Detail(detail));
        }

        private static string Show(object value)
        {
            return value == null ? "(null)" : value.ToString();
        }

        private static string Detail(string detail)
        {
            return string.IsNullOrEmpty(detail) ? string.Empty : " " + detail;
        }
    }

    /// <summary>用例运行器：逐个执行、统计通过/失败、打印失败原因。</summary>
    public static class TestRunner
    {
        public static int PassedCount { get; private set; }
        public static int FailedCount { get; private set; }

        public static void Run(string name, Action body)
        {
            try
            {
                body();
                PassedCount++;
                Console.WriteLine("[PASS] " + name);
            }
            catch (Exception ex)
            {
                FailedCount++;
                Console.WriteLine("[FAIL] " + name + "：" + ex.GetType().Name + "：" + ex.Message);
            }
        }

        public static int TotalCount
        {
            get { return PassedCount + FailedCount; }
        }
    }
}
