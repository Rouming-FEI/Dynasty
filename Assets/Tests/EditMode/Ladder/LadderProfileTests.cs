using Gameplay.Ladder;
using NUnit.Framework;

namespace Dynasty.Tests.Ladder
{
    public class LadderProfileTests
    {
        [Test]
        public void DefaultProfile_IsZeroAndUnranked()
        {
            var p = default(LadderProfile);
            Assert.AreEqual(0, p.BestStreak);
            Assert.AreEqual(0L, p.TotalScore);
            Assert.AreEqual(0, p.Attempts);
            Assert.AreEqual(0, p.LastStreak);
            Assert.AreEqual(0, p.Rank);
            Assert.AreEqual("未定级", p.RankName);
        }

        [Test]
        public void Apply_FirstRun_SetsAll()
        {
            var p = default(LadderProfile).Apply(3, LadderRules.SingleRunScore(3));
            Assert.AreEqual(3, p.BestStreak);
            Assert.AreEqual(60L, p.TotalScore);
            Assert.AreEqual(1, p.Attempts);
            Assert.AreEqual(3, p.LastStreak);
            Assert.AreEqual(60L, p.LastScore);
            Assert.AreEqual(2, p.Rank);          // 3 连胜 = 白银
            Assert.AreEqual("白银", p.RankName);
        }

        [Test]
        public void Apply_ImprovingRuns_AccumulateAndRaiseBest()
        {
            var p = default(LadderProfile).Apply(2, 30).Apply(4, 100);
            Assert.AreEqual(4, p.BestStreak);
            Assert.AreEqual(130L, p.TotalScore);
            Assert.AreEqual(2, p.Attempts);
            Assert.AreEqual(4, p.LastStreak);
        }

        [Test]
        public void Apply_WorseRun_KeepsBestStreakAndScore()
        {
            var p = default(LadderProfile).Apply(5, 150).Apply(1, 10);
            Assert.AreEqual(5, p.BestStreak);
            Assert.AreEqual(160L, p.TotalScore);
            Assert.AreEqual(2, p.Attempts);
            Assert.AreEqual(1, p.LastStreak);
            Assert.AreEqual(3, p.Rank);          // 黄金不降
        }

        [Test]
        public void Apply_ZeroStreakRun_CountsAttemptOnly()
        {
            var p = default(LadderProfile).Apply(3, 60).Apply(0, 0);
            Assert.AreEqual(3, p.BestStreak);
            Assert.AreEqual(60L, p.TotalScore);
            Assert.AreEqual(2, p.Attempts);
        }
    }
}
