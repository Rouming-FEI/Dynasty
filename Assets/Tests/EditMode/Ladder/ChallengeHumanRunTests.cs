using System;
using Core;
using Gameplay.Characters;
using Gameplay.Ladder;
using NUnit.Framework;
using LayerRunInfo = Gameplay.Ladder.ChallengeRunner.LayerRunInfo;

namespace Dynasty.Tests.Ladder
{
    /// <summary>会话模式（真人宿主）测试：判定纯逻辑，用构造的 LayerRunInfo 回报——不真实跑局。
    /// 吃鸡回报 = Phase==GameOver && Slot0Alive；失败 = !Slot0Alive。</summary>
    public class ChallengeHumanRunTests
    {
        private static ChallengeRunner NewRunner(ChallengeConfig cfg, out InMemoryProfileStore store)
        {
            store = new InMemoryProfileStore();
            return new ChallengeRunner(cfg, store);
        }

        private static LayerRunInfo Win() => new LayerRunInfo
        { Phase = TurnPhase.GameOver, Slot0Alive = true, Rounds = 10 };

        private static LayerRunInfo Lose() => new LayerRunInfo
        { Phase = TurnPhase.GameOver, Slot0Alive = false, Rounds = 10 };

        /// <summary>会话待跑层 defs 的 Def Id 数组（抽签序断言用）。</summary>
        private static int[] DefIdsOf(ChallengeRunSession s)
        {
            var ids = new int[s.CurrentDefs.Length];
            for (int i = 0; i < ids.Length; i++) ids[i] = s.CurrentDefs[i].Id;
            return ids;
        }

        [Test]
        public void BeginHumanRun_Layer1Defs_ActiveWithTwoPlayers()
        {
            var r = NewRunner(new ChallengeConfig { Seed = 100 }, out var store);
            ChallengeRunSession s = r.BeginHumanRun(100);

            Assert.IsTrue(s.IsActive);
            Assert.IsFalse(s.IsComplete);
            Assert.IsNull(s.Result);
            Assert.AreEqual(1, s.CurrentLayer);
            Assert.AreEqual(2, s.CurrentDefs.Length);          // 层 1 = 2 人局
            Assert.AreEqual(0, store.Load().Attempts);         // 开局不结算
        }

        [Test]
        public void ReportWin_AdvancesLayer_NoSettlementYet()
        {
            var r = NewRunner(new ChallengeConfig { Seed = 100 }, out var store);
            r.BeginHumanRun(100);
            ChallengeRunSession s = r.ReportHumanLayerEnd(Win());

            Assert.IsTrue(s.IsActive);
            Assert.AreEqual(2, s.CurrentLayer);
            Assert.AreEqual(3, s.CurrentDefs.Length);          // 层 2 = 3 人局
            Assert.AreEqual(0, store.Load().Attempts);         // 吃鸡未到终局不结算
        }

        [Test]
        public void ReportLoss_CompletesAndSettlesOnce()
        {
            var r = NewRunner(new ChallengeConfig { Seed = 100 }, out var store);
            r.BeginHumanRun(100);
            ChallengeRunSession s = r.ReportHumanLayerEnd(Lose());

            Assert.IsTrue(s.IsComplete);
            Assert.IsFalse(s.IsActive);
            Assert.IsNotNull(s.Result);
            Assert.AreEqual(0, s.Result.Streak);
            Assert.AreEqual(0L, s.Result.Score);
            Assert.AreEqual(1, s.Result.FailedAtLayer);
            Assert.IsFalse(s.Result.Won);
            Assert.AreEqual(1, store.Load().Attempts);
        }

        [Test]
        public void ReportAfterComplete_ThrowsAndDoesNotDoubleSettle()
        {
            var r = NewRunner(new ChallengeConfig { Seed = 100 }, out var store);
            r.BeginHumanRun(100);
            r.ReportHumanLayerEnd(Lose());

            Assert.Throws<InvalidOperationException>(() => r.ReportHumanLayerEnd(Lose()));
            Assert.AreEqual(1, store.Load().Attempts);         // 防二次结算
        }

        [Test]
        public void TwoWinsOnTwoLayerCap_CompletesWithWon()
        {
            var r = NewRunner(new ChallengeConfig { Seed = 100, LayerCap = 2 }, out var store);
            r.BeginHumanRun(100);
            r.ReportHumanLayerEnd(Win());
            ChallengeRunSession s = r.ReportHumanLayerEnd(Win());

            Assert.IsTrue(s.IsComplete);
            Assert.IsTrue(s.Result.Won);
            Assert.AreEqual(2, s.Result.Streak);
            Assert.AreEqual(30L, s.Result.Score);              // 10 + 20
            Assert.AreEqual(0, s.Result.FailedAtLayer);
            Assert.AreEqual(1, store.Load().Attempts);
            Assert.AreEqual(2, store.Load().BestStreak);
        }

        [Test]
        public void SameRunner_TwoAutoRuns_IdenticalAndClean()
        {
            // 同一 runner 连续两次 Run：状态机必须干净重置（无字段残留），结果与档案一致
            var cfg = new ChallengeConfig { Seed = 777, LayerCap = 3 };
            var store = new InMemoryProfileStore();
            var runner = new ChallengeRunner(cfg, store);
            ChallengeResult a = runner.Run();
            LadderProfile afterA = store.Load();
            ChallengeResult b = runner.Run();
            LadderProfile afterB = store.Load();

            Assert.AreEqual(a.Streak, b.Streak);
            Assert.AreEqual(a.Score, b.Score);
            Assert.AreEqual(a.FailedAtLayer, b.FailedAtLayer);
            Assert.AreEqual(a.TimedOut, b.TimedOut);
            Assert.AreEqual(afterA.TotalScore * 2, afterB.TotalScore);   // 两次各结算一次
            Assert.AreEqual(2, afterB.Attempts);
        }

        [Test]
        public void ResumeHumanRun_ReproducesDrawSequence()
        {
            var cfg = new ChallengeConfig { Seed = 2026 };
            // 参考系：新挑战吃鸡层 1 → 会话停在层 2（待跑），取层 2 defs 的 id 序
            var refRunner = NewRunner(cfg, out _);
            ChallengeRunSession refSession = refRunner.BeginHumanRun(2026);
            refSession = refRunner.ReportHumanLayerEnd(Win());
            Assert.AreEqual(2, refSession.CurrentLayer);
            int[] expected = DefIdsOf(refSession);
            Assert.AreEqual(3, expected.Length);          // 层 2 = 3 人局

            // 续跑路径：Resume(seed, 2) → defs 与参考逐槽一致（消费序同源）
            var r2 = NewRunner(cfg, out var store);
            ChallengeRunSession resumed = r2.ResumeHumanRun(2026, 2);

            Assert.IsTrue(resumed.IsActive);
            Assert.AreEqual(2, resumed.CurrentLayer);
            CollectionAssert.AreEqual(expected, DefIdsOf(resumed));
            Assert.AreEqual(0, store.Load().Attempts);    // 续跑本身不结算
        }

        [Test]
        public void Resume_ThenLoss_SettlesStreakAsLayersAlreadyWon()
        {
            // 存档点语义：Resume(atLayer=3) = 前两层已吃鸡 → 层 3 失败 → streak=2、score=30
            var r = NewRunner(new ChallengeConfig { Seed = 99 }, out var store);
            ChallengeRunSession s = r.ResumeHumanRun(99, 3);
            s = r.ReportHumanLayerEnd(Lose());

            Assert.IsTrue(s.IsComplete);
            Assert.AreEqual(2, s.Result.Streak);
            Assert.AreEqual(30L, s.Result.Score);         // 10 + 20
            Assert.AreEqual(3, s.Result.FailedAtLayer);
            Assert.IsFalse(s.Result.Won);
            Assert.AreEqual(1, store.Load().Attempts);
            Assert.AreEqual(2, store.Load().BestStreak);
        }

        [Test]
        public void Resume_InvalidLayer_Throws()
        {
            var r = NewRunner(new ChallengeConfig { Seed = 1 }, out _);
            Assert.Throws<ArgumentOutOfRangeException>(() => r.ResumeHumanRun(1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => r.ResumeHumanRun(1, LadderRules.MaxLayers + 1));
        }

        [Test]
        public void AbandonShape_UnfinishedLayerWithDeadSlot0_SettlesAsLossNotTimeout()
        {
            // Task 7 弃局按钮的回报形态：层局未决（Phase 非 GameOver）但槽 0 判败
            var r = NewRunner(new ChallengeConfig { Seed = 5 }, out var store);
            r.BeginHumanRun(5);
            var abandonInfo = new LayerRunInfo
            {
                Phase = TurnPhase.ActionPhase,
                Slot0Alive = false,
                Rounds = 0
            };
            ChallengeRunSession s = r.ReportHumanLayerEnd(abandonInfo);

            Assert.IsTrue(s.IsComplete);
            Assert.IsFalse(s.Result.TimedOut);
            Assert.AreEqual(1, s.Result.FailedAtLayer);
            Assert.AreEqual(0, s.Result.Streak);
        }
    }
}
