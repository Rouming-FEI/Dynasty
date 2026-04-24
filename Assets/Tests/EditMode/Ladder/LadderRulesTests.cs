using Gameplay.Ladder;
using NUnit.Framework;

namespace Dynasty.Tests.Ladder
{
    public class LadderRulesTests
    {
        [Test]
        public void PlayerCountAtLayer_IncrementsFromTwo()
        {
            Assert.AreEqual(2, LadderRules.PlayerCountAtLayer(1));
            Assert.AreEqual(8, LadderRules.PlayerCountAtLayer(LadderRules.MaxLayers));
            for (int layer = 1; layer <= LadderRules.MaxLayers; layer++)
                Assert.AreEqual(layer + 1, LadderRules.PlayerCountAtLayer(layer));
        }

        [TestCase(0, 0L)]
        [TestCase(1, 10L)]
        [TestCase(2, 30L)]
        [TestCase(3, 60L)]
        [TestCase(4, 100L)]
        [TestCase(5, 150L)]
        [TestCase(6, 210L)]
        [TestCase(7, 280L)]
        public void SingleRunScore_Table(int streak, long expected)
        {
            Assert.AreEqual(expected, LadderRules.SingleRunScore(streak));
        }

        [TestCase(0, 0)]
        [TestCase(1, 1)]
        [TestCase(2, 1)]
        [TestCase(3, 2)]
        [TestCase(4, 2)]
        [TestCase(5, 3)]
        [TestCase(6, 3)]
        [TestCase(7, 4)]
        public void RankOf_Table(int streak, int rank)
        {
            Assert.AreEqual(rank, LadderRules.RankOf(streak));
        }

        [Test]
        public void RankName_Endpoints()
        {
            Assert.AreEqual("未定级", LadderRules.RankName(0));
            Assert.AreEqual("登顶", LadderRules.RankName(4));
        }

        [Test]
        public void CharacterPool_IsDocs15WithoutNormalPerson()
        {
            Assert.AreEqual(15, LadderRules.CharacterPool.Length);
            Assert.AreEqual(-1, System.Array.IndexOf(LadderRules.CharacterPool, 3));
            for (int id = 1; id <= 16; id++)
            {
                if (id == 3) continue;
                Assert.IsTrue(System.Array.IndexOf(LadderRules.CharacterPool, id) >= 0,
                    $"角色池缺 Def Id {id}");
            }
        }
    }
}
