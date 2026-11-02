using System;
using InspectionWidget.Credentials;

namespace InspectionWidget.Tests
{
    /// <summary>
    /// ClipboardGuard 决策与编排（假网关）：ShouldClear 纯函数六分支 +
    /// Copy 回执滚动、TryClearNow 匹配/不匹配/同文字新复制（Review Focus 第 3 条）。
    /// 定时器在用例中以超长间隔（3600 秒）构造，避免真实到期干扰断言。
    /// </summary>
    public static class ClipboardGuardTests
    {
        private static ClipboardReceipt Receipt(uint sequence, string token)
        {
            return new ClipboardReceipt { Sequence = sequence, Token = token, Sha256 = "hash" };
        }

        private static ClipboardState State(uint sequence, string token)
        {
            return new ClipboardState { Sequence = sequence, Token = token };
        }

        public static void RunAll()
        {
            TestRunner.Run("决策：序列号与令牌都同 → ShouldClear=true", () =>
            {
                Assert.True(ClipboardGuard.ShouldClear(Receipt(7, "tok-a"), State(7, "tok-a")));
            });

            TestRunner.Run("决策：仅序列号变 → ShouldClear=false", () =>
            {
                Assert.False(ClipboardGuard.ShouldClear(Receipt(7, "tok-a"), State(8, "tok-a")));
            });

            TestRunner.Run("决策：仅令牌变 → ShouldClear=false", () =>
            {
                Assert.False(ClipboardGuard.ShouldClear(Receipt(7, "tok-a"), State(7, "tok-b")));
            });

            TestRunner.Run("决策：同文字新复制（序列号与令牌都变、哈希相同）→ ShouldClear=false", () =>
            {
                // Review Focus 第 3 条锚定：文字相同（Sha256 相同）不构成可清依据，
                // 只认序列号＋令牌这一对同锁凭证。
                var receipt = new ClipboardReceipt { Sequence = 3, Token = "tok-old", Sha256 = "same-hash" };
                var current = new ClipboardState { Sequence = 4, Token = "tok-new" };
                Assert.False(ClipboardGuard.ShouldClear(receipt, current),
                    "同文字新复制后不得误清（Review Focus 第 3 条）");
            });

            TestRunner.Run("决策：receipt 为 null → ShouldClear=false", () =>
            {
                Assert.False(ClipboardGuard.ShouldClear(null, State(1, "tok")));
            });

            TestRunner.Run("决策：current 为 null → ShouldClear=false", () =>
            {
                Assert.False(ClipboardGuard.ShouldClear(Receipt(1, "tok"), null));
            });

            TestRunner.Run("编排：Copy 两次后 TryClearNow 只认第二次回执", () =>
            {
                var gateway = new FakeClipboardGateway();
                using (var guard = new ClipboardGuard(gateway, true, 3600))
                {
                    guard.Copy("first-secret");
                    string firstToken = gateway.ReadState().Token;

                    guard.Copy("second-secret");
                    string secondToken = gateway.ReadState().Token;
                    Assert.False(string.Equals(firstToken, secondToken, StringComparison.Ordinal),
                        "两次写入令牌应不同（构造前提）");

                    Assert.True(guard.TryClearNow(), "当前回执与剪贴板一致，应清成功");
                    Assert.NotNull(gateway.LastClearReceipt);
                    Assert.Equal(secondToken, gateway.LastClearReceipt.Token,
                        "清空核对必须用第二次回执，不得用第一次");
                    Assert.Null(gateway.CurrentText, "清空后假网关内容应为空");
                }
            });

            TestRunner.Run("编排：状态匹配时 TryClearNow 清空并返回 true", () =>
            {
                var gateway = new FakeClipboardGateway();
                using (var guard = new ClipboardGuard(gateway, true, 3600))
                {
                    guard.Copy("pw-123");
                    Assert.True(guard.TryClearNow());
                    Assert.Null(gateway.CurrentText);
                    Assert.Equal(1, gateway.ClearAttempts);
                    // 已清后再调：无回执，直接 false 且不再调网关
                    Assert.False(guard.TryClearNow());
                    Assert.Equal(1, gateway.ClearAttempts);
                }
            });

            TestRunner.Run("编排：同文字新复制后 TryClearNow 不误清、内容保留、触发 ClearSkipped", () =>
            {
                var gateway = new FakeClipboardGateway();
                using (var guard = new ClipboardGuard(gateway, true, 3600))
                {
                    bool skipped = false;
                    guard.ClearSkipped += delegate { skipped = true; };

                    guard.Copy("same-text");
                    gateway.SimulateExternalCopy("same-text"); // 文字相同，序列号+令牌已换

                    Assert.False(guard.TryClearNow(), "同文字新复制不得误清（Review Focus 第 3 条）");
                    Assert.Equal("same-text", gateway.CurrentText, "外部新复制的内容必须保留");
                    Assert.True(skipped, "不匹配跳过应触发 ClearSkipped 通知");
                }
            });

            TestRunner.Run("编排：网关返回 false 时 TryClearNow 不抛异常", () =>
            {
                var gateway = new FakeClipboardGateway();
                using (var guard = new ClipboardGuard(gateway, true, 3600))
                {
                    guard.Copy("abc");
                    gateway.SimulateExternalCopy("other-text"); // 状态不匹配 → 网关返回 false
                    bool result = guard.TryClearNow(); // 不抛即达标
                    Assert.False(result);
                }
            });

            TestRunner.Run("编排：网关清空抛异常时 TryClearNow 返回 false 且不抛异常", () =>
            {
                var gateway = new FakeClipboardGateway();
                using (var guard = new ClipboardGuard(gateway, true, 3600))
                {
                    guard.Copy("abc");
                    gateway.ThrowOnClear = true; // 网关在清空路径抛异常（修复轮 Minor-3 锚点）
                    bool result = guard.TryClearNow(); // 不抛即达标：Guard 须吞掉网关异常
                    Assert.False(result, "网关清空抛异常时 TryClearNow 应返回 false");
                    Assert.Equal("abc", gateway.CurrentText, "清空未成功，假网关内容应保留");
                }
            });

            TestRunner.Run("编排：已 Dispose 的 Guard 调 Copy 抛 ObjectDisposedException 且不写网关", () =>
            {
                var gateway = new FakeClipboardGateway();
                var guard = new ClipboardGuard(gateway, true, 3600);
                guard.Dispose();
                // 修复轮 Minor-2 锚点：写剪贴板之前就须拦下，绝不写出无回执的明文
                Assert.Throws<ObjectDisposedException>(delegate { guard.Copy("must-not-write"); });
                Assert.Equal(0, gateway.WriteCount, "已释放的 Guard 不得调网关写入");
                Assert.Null(gateway.CurrentText, "剪贴板不得留下无人负责清空的明文");
            });

            TestRunner.Run("编排：从未 Copy 时 TryClearNow 返回 false 且不调网关", () =>
            {
                var gateway = new FakeClipboardGateway();
                using (var guard = new ClipboardGuard(gateway, true, 3600))
                {
                    Assert.False(guard.TryClearNow());
                    Assert.Equal(0, gateway.ClearAttempts);
                }
            });
        }
    }
}
