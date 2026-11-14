using System;
using System.Collections.Generic;
using System.Linq;
using InspectionWidget.Planning;

namespace InspectionWidget.Tests
{
    /// <summary>
    /// 每日巡检进度（P2）：对勾=点击被截图状态机接受；同日恢复、跨日新轮、
    /// 完成通知只消费一次、设备清单变更与旧回调拒绝。全部经由真实公开入口。
    /// </summary>
    public static class InspectionPlanTests
    {
        private static readonly DateTime Day1 = new DateTime(2026, 11, 14, 9, 30, 0);
        private static readonly DateTime Day2 = new DateTime(2026, 11, 15, 8, 0, 0);

        public static void RunAll()
        {
            TestRunner.Run("计划：新轮为当日、无勾选、未通知", () =>
            {
                var clock = new FakeClock(Day1);
                var plan = new InspectionPlan(clock, new[] { "设备A", "设备B" });
                Assert.Equal(Day1.Date, plan.Date);
                Assert.False(string.IsNullOrEmpty(plan.RoundId));
                Assert.Equal(2, plan.TotalCount);
                Assert.Equal(0, plan.VisitedCount);
                Assert.False(plan.IsComplete);
                Assert.False(plan.CompletionPrompted);
                Assert.NotNull(plan.CurrentToken);
                Assert.Equal(Day1.Date, plan.CurrentToken.Date);
                Assert.Equal(plan.RoundId, plan.CurrentToken.RoundId);
            });

            TestRunner.Run("计划：当前轮有效点击被接受，重复点击不累计", () =>
            {
                var plan = new InspectionPlan(new FakeClock(Day1), new[] { "设备A", "设备B" });
                InspectionPlanToken token = plan.CurrentToken;
                Assert.True(plan.MarkVisited("设备A", token));
                Assert.False(plan.MarkVisited("设备A", token));
                Assert.Equal(1, plan.VisitedCount);
                Assert.True(plan.IsVisited("设备A"));
                Assert.False(plan.IsVisited("设备B"));
            });

            TestRunner.Run("计划：未知设备、空设备名与空 token 不被接受", () =>
            {
                var plan = new InspectionPlan(new FakeClock(Day1), new[] { "设备A" });
                Assert.False(plan.MarkVisited("设备X", plan.CurrentToken));
                Assert.False(plan.MarkVisited(null, plan.CurrentToken));
                Assert.False(plan.MarkVisited(string.Empty, plan.CurrentToken));
                Assert.False(plan.MarkVisited("设备A", null));
                Assert.Equal(0, plan.VisitedCount);

                var empty = new InspectionPlan(new FakeClock(Day1), new string[0]);
                Assert.Equal(0, empty.TotalCount);
                Assert.False(empty.MarkVisited("设备A", empty.CurrentToken));
                Assert.False(empty.IsComplete);
            });

            TestRunner.Run("计划：设备名按 OrdinalIgnoreCase 匹配并保留配置写法", () =>
            {
                var plan = new InspectionPlan(new FakeClock(Day1), new[] { "Camera-1" });
                Assert.True(plan.MarkVisited("camera-1", plan.CurrentToken));
                Assert.True(plan.IsVisited("CAMERA-1"));
                Assert.False(plan.MarkVisited("CAMERA-1", plan.CurrentToken));
                Assert.Equal(1, plan.VisitedCount);
                Assert.Equal("Camera-1", plan.Devices[0]);
            });

            TestRunner.Run("计划：取消截图或保存失败不撤销已接受点击", () =>
            {
                var clock = new FakeClock(Day1);
                var plan = new InspectionPlan(clock, new[] { "设备A", "设备B" });
                InspectionPlanToken token = plan.CurrentToken;
                Assert.True(plan.MarkVisited("设备A", token));
                // 截图取消/保存失败时 UI 不调用任何入口：状态保持，后续点击照常。
                Assert.True(plan.IsVisited("设备A"));
                Assert.True(plan.MarkVisited("设备B", token));
                Assert.Equal(2, plan.VisitedCount);
                Assert.True(plan.IsComplete);
            });

            TestRunner.Run("计划：最后一项完成后待询问，忙碌不消耗，空闲只消费一次", () =>
            {
                var clock = new FakeClock(Day1);
                var plan = new InspectionPlan(clock, new[] { "设备A", "设备B" });
                InspectionPlanToken token = plan.CurrentToken;
                Assert.True(plan.MarkVisited("设备A", token));
                Assert.False(plan.TryClaimCompletionPrompt(false));
                Assert.True(plan.MarkVisited("设备B", token));
                Assert.True(plan.IsComplete);

                Assert.False(plan.TryClaimCompletionPrompt(true));
                Assert.False(plan.CompletionPrompted);
                Assert.True(plan.TryClaimCompletionPrompt(false));
                Assert.True(plan.CompletionPrompted);
                Assert.False(plan.TryClaimCompletionPrompt(false));

                Assert.False(plan.MarkVisited("设备A", token));
                Assert.False(plan.TryClaimCompletionPrompt(false));
            });

            TestRunner.Run("计划：空名单不产生完成通知", () =>
            {
                var plan = new InspectionPlan(new FakeClock(Day1), new string[0]);
                Assert.False(plan.IsComplete);
                Assert.False(plan.TryClaimCompletionPrompt(false));
                Assert.False(plan.CompletionPrompted);
            });

            TestRunner.Run("计划：删掉最后未点设备不弹窗，已完成已通知后加项不重复弹", () =>
            {
                var clock = new FakeClock(Day1);
                var byDelete = new InspectionPlan(clock, new[] { "设备A", "设备B" });
                Assert.True(byDelete.MarkVisited("设备A", byDelete.CurrentToken));
                byDelete.ReconcileDevices(new[] { "设备A" });
                Assert.True(byDelete.IsComplete);
                Assert.False(byDelete.TryClaimCompletionPrompt(false));
                Assert.False(byDelete.CompletionPrompted);

                var plan = new InspectionPlan(clock, new[] { "设备A", "设备B" });
                Assert.True(plan.MarkVisited("设备A", plan.CurrentToken));
                Assert.True(plan.MarkVisited("设备B", plan.CurrentToken));
                Assert.True(plan.TryClaimCompletionPrompt(false));

                plan.ReconcileDevices(new[] { "设备A", "设备B", "设备C" });
                Assert.False(plan.IsComplete);
                Assert.True(plan.MarkVisited("设备C", plan.CurrentToken));
                Assert.True(plan.IsComplete);
                Assert.False(plan.TryClaimCompletionPrompt(false));
                Assert.True(plan.CompletionPrompted);
            });

            TestRunner.Run("计划：ReconcileDevices 增项未点、删项移出、排序不改变勾选", () =>
            {
                var plan = new InspectionPlan(new FakeClock(Day1),
                    new[] { "设备A", "设备B", "设备C" });
                Assert.True(plan.MarkVisited("设备A", plan.CurrentToken));
                Assert.True(plan.MarkVisited("设备C", plan.CurrentToken));

                plan.ReconcileDevices(new[] { "设备C", "设备B", "设备A", "设备D" });
                Assert.Equal(4, plan.TotalCount);
                Assert.Equal(2, plan.VisitedCount);
                Assert.True(plan.IsVisited("设备A"));
                Assert.True(plan.IsVisited("设备C"));
                Assert.False(plan.IsVisited("设备B"));
                Assert.False(plan.IsVisited("设备D"));
                Assert.Equal("设备C", plan.Devices[0]);

                plan.ReconcileDevices(new[] { "设备B" });
                Assert.Equal(1, plan.TotalCount);
                Assert.Equal(0, plan.VisitedCount);
                Assert.False(plan.IsVisited("设备A"));
            });

            TestRunner.Run("计划：对账删除项清除失效 pending，纯排序保留合法 pending", () =>
            {
                var clock = new FakeClock(Day1);
                var plan = new InspectionPlan(clock, new[] { "设备A" });
                Assert.True(plan.MarkVisited("设备A", plan.CurrentToken));
                InspectionProgressSnapshot pendingSnapshot = plan.CreateSnapshot();
                Assert.True(pendingSnapshot.CompletionPending);

                // 新增未点设备使完成失效：残留 pending 不能再弹。
                plan.ReconcileDevices(new[] { "设备A", "设备B" });
                Assert.False(plan.IsComplete);
                Assert.False(plan.TryClaimCompletionPrompt(false));
                Assert.False(plan.CompletionPrompted);

                // 再删掉未点的 B：回到满勾也不能补弹。
                plan.ReconcileDevices(new[] { "设备A" });
                Assert.True(plan.IsComplete);
                Assert.False(plan.TryClaimCompletionPrompt(false));
                Assert.False(plan.CompletionPrompted);

                // 纯排序不改变完成事实：合法 pending 保留并可领取一次。
                var reordered = new InspectionPlan(clock, new[] { "设备A", "设备B" });
                Assert.True(reordered.MarkVisited("设备A", reordered.CurrentToken));
                Assert.True(reordered.MarkVisited("设备B", reordered.CurrentToken));
                reordered.ReconcileDevices(new[] { "设备B", "设备A" });
                Assert.True(reordered.IsComplete);
                Assert.True(reordered.TryClaimCompletionPrompt(false));
                Assert.False(reordered.TryClaimCompletionPrompt(false));
            });

            TestRunner.Run("计划：RenameDevice 保留勾选并拒绝重名与未知设备", () =>
            {
                var plan = new InspectionPlan(new FakeClock(Day1),
                    new[] { "设备A", "设备B", "设备C" });
                Assert.True(plan.MarkVisited("设备A", plan.CurrentToken));

                plan.RenameDevice("设备A", "新设备");
                Assert.Equal(3, plan.TotalCount);
                Assert.Equal(1, plan.VisitedCount);
                Assert.True(plan.IsVisited("新设备"));
                Assert.False(plan.IsVisited("设备A"));

                plan.RenameDevice("新设备", "设备B");
                Assert.Equal(3, plan.TotalCount);
                Assert.True(plan.IsVisited("新设备"));
                Assert.False(plan.IsVisited("设备B"));

                plan.RenameDevice("不存在", "设备D");
                Assert.Equal(3, plan.TotalCount);
                Assert.False(plan.IsVisited("设备D"));

                plan.RenameDevice("设备B", "设备E");
                Assert.Equal(3, plan.TotalCount);
                Assert.Equal(1, plan.VisitedCount);
                Assert.True(plan.IsVisited("新设备"));
                Assert.False(plan.IsVisited("设备E"));
            });

            TestRunner.Run("计划：ReplaceDevices 开启新轮且不继承旧勾选", () =>
            {
                var plan = new InspectionPlan(new FakeClock(Day1), new[] { "设备A", "设备B" });
                Assert.True(plan.MarkVisited("设备A", plan.CurrentToken));
                Assert.True(plan.MarkVisited("设备B", plan.CurrentToken));
                Assert.True(plan.TryClaimCompletionPrompt(false));
                plan.RecordSavedPath(@"C:\shots\a.png", plan.CurrentToken);
                string oldRound = plan.RoundId;
                InspectionPlanToken oldToken = plan.CurrentToken;

                plan.ReplaceDevices(new[] { "设备A", "设备D" });
                Assert.False(string.Equals(oldRound, plan.RoundId, StringComparison.Ordinal));
                Assert.Equal(2, plan.TotalCount);
                Assert.Equal(0, plan.VisitedCount);
                Assert.False(plan.CompletionPrompted);
                Assert.False(plan.IsVisited("设备A"));
                Assert.Equal(0, plan.SavedPaths.Count());
                Assert.False(plan.MarkVisited("设备A", oldToken));
                Assert.True(plan.MarkVisited("设备A", plan.CurrentToken));
            });

            TestRunner.Run("计划：跨日 EnsureCurrentDay 新轮清勾清通知，旧 token 失效", () =>
            {
                var clock = new FakeClock(Day1);
                var plan = new InspectionPlan(clock, new[] { "设备A" });
                InspectionPlanToken day1Token = plan.CurrentToken;
                Assert.True(plan.MarkVisited("设备A", day1Token));
                Assert.True(plan.TryClaimCompletionPrompt(false));
                string day1Round = plan.RoundId;

                plan.EnsureCurrentDay();
                Assert.Equal(1, plan.VisitedCount);
                Assert.Equal(day1Round, plan.RoundId);

                clock.Now = Day2;
                plan.EnsureCurrentDay();
                Assert.Equal(Day2.Date, plan.Date);
                Assert.False(string.Equals(day1Round, plan.RoundId, StringComparison.Ordinal));
                Assert.Equal(0, plan.VisitedCount);
                Assert.False(plan.CompletionPrompted);
                Assert.Equal(1, plan.TotalCount);
                Assert.False(plan.MarkVisited("设备A", day1Token));
                Assert.True(plan.MarkVisited("设备A", plan.CurrentToken));
                Assert.True(plan.TryClaimCompletionPrompt(false));
            });

            TestRunner.Run("计划：ResetRound 新 ID、清勾并允许新轮通知", () =>
            {
                var plan = new InspectionPlan(new FakeClock(Day1), new[] { "设备A" });
                Assert.True(plan.MarkVisited("设备A", plan.CurrentToken));
                Assert.True(plan.TryClaimCompletionPrompt(false));
                string oldRound = plan.RoundId;
                InspectionPlanToken oldToken = plan.CurrentToken;

                plan.ResetRound();
                Assert.False(string.Equals(oldRound, plan.RoundId, StringComparison.Ordinal));
                Assert.Equal(0, plan.VisitedCount);
                Assert.False(plan.CompletionPrompted);
                Assert.False(plan.MarkVisited("设备A", oldToken));
                Assert.True(plan.MarkVisited("设备A", plan.CurrentToken));
                Assert.True(plan.TryClaimCompletionPrompt(false));
            });

            TestRunner.Run("计划：Unmark 只清对勾，不重开该轮已通知资格", () =>
            {
                var plan = new InspectionPlan(new FakeClock(Day1), new[] { "设备A", "设备B" });
                Assert.True(plan.MarkVisited("设备A", plan.CurrentToken));
                Assert.True(plan.MarkVisited("设备B", plan.CurrentToken));
                Assert.True(plan.TryClaimCompletionPrompt(false));

                plan.Unmark("设备A");
                Assert.Equal(1, plan.VisitedCount);
                Assert.True(plan.CompletionPrompted);

                Assert.True(plan.MarkVisited("设备A", plan.CurrentToken));
                Assert.True(plan.IsComplete);
                Assert.False(plan.TryClaimCompletionPrompt(false));

                plan.Unmark("不存在");
                Assert.Equal(2, plan.VisitedCount);
            });

            TestRunner.Run("计划：快照恢复同日勾选、待通知、轮次与保存路径", () =>
            {
                var clock = new FakeClock(Day1);
                var plan = new InspectionPlan(clock, new[] { "设备A", "设备B" });
                Assert.True(plan.MarkVisited("设备A", plan.CurrentToken));
                Assert.True(plan.MarkVisited("设备B", plan.CurrentToken));
                Assert.True(plan.RecordSavedPath(@"C:\shots\设备A-1.png", plan.CurrentToken));
                InspectionProgressSnapshot snapshot = plan.CreateSnapshot();
                Assert.Equal(InspectionPlan.SnapshotSchemaVersion, snapshot.SchemaVersion);

                var restored = new InspectionPlan(clock, new[] { "设备A", "设备B" });
                string error;
                Assert.True(restored.Restore(snapshot, out error), error);
                Assert.Equal(plan.Date, restored.Date);
                Assert.Equal(plan.RoundId, restored.RoundId);
                Assert.Equal(2, restored.VisitedCount);
                Assert.True(restored.IsComplete);
                Assert.True(restored.IsVisited("设备B"));
                Assert.Equal(1, restored.SavedPaths.Count());
                Assert.False(restored.MarkVisited("设备A", plan.CurrentToken));
                Assert.True(restored.TryClaimCompletionPrompt(false));
            });

            TestRunner.Run("计划：旧日期快照不恢复为当日完成，也不污染当前状态", () =>
            {
                var day1Clock = new FakeClock(Day1);
                var day1 = new InspectionPlan(day1Clock, new[] { "设备A" });
                Assert.True(day1.MarkVisited("设备A", day1.CurrentToken));
                InspectionProgressSnapshot snapshot = day1.CreateSnapshot();

                var clock = new FakeClock(Day2);
                var today = new InspectionPlan(clock, new[] { "设备A" });
                string todayRound = today.RoundId;
                string error;
                Assert.False(today.Restore(snapshot, out error));
                Assert.NotNull(error);
                Assert.Equal(Day2.Date, today.Date);
                Assert.Equal(todayRound, today.RoundId);
                Assert.Equal(0, today.VisitedCount);
                Assert.False(today.IsComplete);
            });

            TestRunner.Run("计划：坏快照被拒绝且当前状态不被污染", () =>
            {
                var clock = new FakeClock(Day1);
                var plan = new InspectionPlan(clock, new[] { "设备A" });
                Assert.True(plan.MarkVisited("设备A", plan.CurrentToken));
                string round = plan.RoundId;
                string error;

                Assert.False(plan.Restore(null, out error));
                Assert.NotNull(error);

                InspectionProgressSnapshot good = plan.CreateSnapshot();

                InspectionProgressSnapshot badVersion = Clone(good);
                badVersion.SchemaVersion = 99;
                AssertRestoreRejected(plan, badVersion, round);

                InspectionProgressSnapshot badRound = Clone(good);
                badRound.RoundId = " ";
                AssertRestoreRejected(plan, badRound, round);

                InspectionProgressSnapshot unknownVisited = Clone(good);
                unknownVisited.VisitedDevices = new List<string> { "设备A", "设备Z" };
                AssertRestoreRejected(plan, unknownVisited, round);

                InspectionProgressSnapshot duplicateDevices = Clone(good);
                duplicateDevices.Devices = new List<string> { "设备A", "设备a" };
                AssertRestoreRejected(plan, duplicateDevices, round);

                InspectionProgressSnapshot blankDevice = Clone(good);
                blankDevice.Devices = new List<string> { "设备A", "  " };
                AssertRestoreRejected(plan, blankDevice, round);

                InspectionProgressSnapshot nullDevice = Clone(good);
                nullDevice.Devices = new List<string> { "设备A", null };
                AssertRestoreRejected(plan, nullDevice, round);

                InspectionProgressSnapshot duplicateVisited = Clone(good);
                duplicateVisited.VisitedDevices = new List<string> { "设备A", "设备A" };
                AssertRestoreRejected(plan, duplicateVisited, round);

                InspectionProgressSnapshot noDate = Clone(good);
                noDate.Date = default(DateTime);
                AssertRestoreRejected(plan, noDate, round);
            });

            TestRunner.Run("计划：恢复未显示的 pending 可在空闲时询问一次", () =>
            {
                var clock = new FakeClock(Day1);
                var plan = new InspectionPlan(clock, new[] { "设备A" });
                Assert.True(plan.MarkVisited("设备A", plan.CurrentToken));
                InspectionProgressSnapshot snapshot = plan.CreateSnapshot();
                Assert.True(snapshot.CompletionPending);
                Assert.False(snapshot.CompletionPrompted);

                var restored = new InspectionPlan(clock, new[] { "设备A" });
                string error;
                Assert.True(restored.Restore(snapshot, out error), error);
                Assert.False(restored.TryClaimCompletionPrompt(true));
                Assert.True(restored.TryClaimCompletionPrompt(false));
                Assert.False(restored.TryClaimCompletionPrompt(false));
            });

            TestRunner.Run("计划：恢复满勾且已通知不会重复弹窗", () =>
            {
                var clock = new FakeClock(Day1);
                var plan = new InspectionPlan(clock, new[] { "设备A" });
                Assert.True(plan.MarkVisited("设备A", plan.CurrentToken));
                Assert.True(plan.TryClaimCompletionPrompt(false));
                InspectionProgressSnapshot snapshot = plan.CreateSnapshot();
                Assert.True(snapshot.CompletionPrompted);
                Assert.False(snapshot.CompletionPending);

                var restored = new InspectionPlan(clock, new[] { "设备A" });
                string error;
                Assert.True(restored.Restore(snapshot, out error), error);
                Assert.True(restored.IsComplete);
                Assert.False(restored.TryClaimCompletionPrompt(false));
            });

            TestRunner.Run("计划：保存路径只接受当前轮并去重", () =>
            {
                var clock = new FakeClock(Day1);
                var plan = new InspectionPlan(clock, new[] { "设备A" });
                InspectionPlanToken token = plan.CurrentToken;
                Assert.False(plan.RecordSavedPath(null, token));
                Assert.False(plan.RecordSavedPath("  ", token));
                Assert.True(plan.RecordSavedPath(@"C:\shots\a.png", token));
                Assert.True(plan.RecordSavedPath(@"c:\SHOTS\A.png", token));
                Assert.Equal(1, plan.SavedPaths.Count());

                plan.ResetRound();
                Assert.False(plan.RecordSavedPath(@"C:\shots\b.png", token));
                Assert.True(plan.RecordSavedPath(@"C:\shots\b.png", plan.CurrentToken));
                Assert.Equal(1, plan.SavedPaths.Count());
                Assert.True(plan.SavedPaths.First().EndsWith("b.png", StringComparison.Ordinal));
            });

            TestRunner.Run("计划：跨日保存路径回调被拒绝", () =>
            {
                var clock = new FakeClock(Day1);
                var plan = new InspectionPlan(clock, new[] { "设备A" });
                InspectionPlanToken token = plan.CurrentToken;
                clock.Now = Day2;
                plan.EnsureCurrentDay();
                Assert.False(plan.RecordSavedPath(@"C:\shots\late.png", token));
                Assert.Equal(0, plan.SavedPaths.Count());
            });
        }

        private static void AssertRestoreRejected(InspectionPlan plan,
            InspectionProgressSnapshot snapshot, string expectedRound)
        {
            string error;
            Assert.False(plan.Restore(snapshot, out error));
            Assert.NotNull(error);
            Assert.Equal(expectedRound, plan.RoundId);
            Assert.Equal(1, plan.VisitedCount);
            Assert.True(plan.IsVisited("设备A"));
        }

        private static InspectionProgressSnapshot Clone(InspectionProgressSnapshot source)
        {
            return new InspectionProgressSnapshot
            {
                SchemaVersion = source.SchemaVersion,
                Date = source.Date,
                RoundId = source.RoundId,
                Devices = new List<string>(source.Devices),
                VisitedDevices = new List<string>(source.VisitedDevices),
                CompletionPrompted = source.CompletionPrompted,
                CompletionPending = source.CompletionPending,
                SavedPaths = new List<string>(source.SavedPaths)
            };
        }
    }
}
