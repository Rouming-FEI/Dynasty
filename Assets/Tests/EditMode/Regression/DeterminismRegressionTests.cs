using NUnit.Framework;
using Core;
using AI;
using Network.Local;

namespace Dynasty.Tests.Regression
{
    public class DeterminismRegressionTests
    {
        /// <summary>跑一局 AI 对战，返回最终状态哈希。同 seed 必须完全一致。</summary>
        private static long RunGame(int playerCount, int seed, int maxTurns)
        {
            var state = new WorldState(playerCount, seed);
            var ais = new SimpleAI[playerCount];
            for (int i = 0; i < playerCount; i++)
                ais[i] = new SimpleAI(i);

            var lockstep = new LocalLockstep(state, ais);
            lockstep.RunGame(maxTurns);

            return state.ComputeStateHash();
        }

        [Test]
        public void 同一种子两次对战_最终哈希一致()
        {
            long h1 = RunGame(3, seed: 42, maxTurns: 100);
            long h2 = RunGame(3, seed: 42, maxTurns: 100);

            Assert.AreEqual(h1, h2, "同 seed 两次对局应产生完全相同的状态哈希");
        }

        [Test]
        public void 不同种子_最终哈希不同()
        {
            long h1 = RunGame(3, seed: 42, maxTurns: 100);
            long h2 = RunGame(3, seed: 43, maxTurns: 100);

            Assert.AreNotEqual(h1, h2, "不同 seed 应产生不同对局（哈希几乎必然不同）");
        }

        [Test]
        public void 四人对局_同样确定()
        {
            long h1 = RunGame(4, seed: 7, maxTurns: 80);
            long h2 = RunGame(4, seed: 7, maxTurns: 80);

            Assert.AreEqual(h1, h2);
        }

        [Test]
        public void 对局能在限制回合内结束或到达回合上限()
        {
            var state = new WorldState(3, 42);
            var ais = new SimpleAI[3];
            for (int i = 0; i < 3; i++) ais[i] = new SimpleAI(i);
            var lockstep = new LocalLockstep(state, ais);

            int turns = lockstep.RunGame(50);

            Assert.LessOrEqual(turns, 50, "不超过 maxTurns");
            Assert.IsTrue(state.Phase == TurnPhase.GameOver || turns == 50,
                "要么结束要么到达回合上限（不死循环）");
        }
    }
}
