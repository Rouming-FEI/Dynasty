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
        public void AutoRun_StillWorksIdentically()
        {
            // 与 ChallengeRunnerTests.SameSeed_DefaultPool_TwoRunsIdentical 同源：重构后自动模式
            // 确定性不破（双跑一致），且结果可被会话语义解释——此处只做行为冒烟（重构回归由既有 40 用例把关）。
            var cfg = new ChallengeConfig { Seed = 777, LayerCap = 3 };
            var r1 = NewRunner(cfg, out var s1);
            var r2 = NewRunner(cfg, out var s2);
            ChallengeResult a = r1.Run();
            ChallengeResult b = r2.Run();
            Assert.AreEqual(a.Streak, b.Streak);
            Assert.AreEqual(a.Score, b.Score);
            Assert.AreEqual(s1.Load().TotalScore, s2.Load().TotalScore);
        }
    }
}
