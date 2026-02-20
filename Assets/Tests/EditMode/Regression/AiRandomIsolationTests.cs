using AI;
using Core;
using Gameplay.Judgment;
using NUnit.Framework;

namespace Dynasty.Tests.Regression
{
    /// <summary>
    /// SimpleAI 独立随机流隔离（P6 网络帧同步前置）：
    /// AI 决策不得消耗世界随机流（否则主机/客户端随机流错位 → 哈希不一致）。
    /// </summary>
    public class AiRandomIsolationTests
    {
        private static WorldState BuildState()
        {
            var state = new WorldState(4, 42);
            state.TurnManager = new TurnManager();
            state.TurnManager.StartGame(state);
            return state;
        }

        [Test]
        public void SimpleAI_决策不消耗世界随机流()
        {
            var state = BuildState();
            var ais = new SimpleAI[4];
            for (int i = 0; i < 4; i++) ais[i] = new SimpleAI(i);

            ulong before = state.Random.State;
            ChipAllocation a = ais[0].GetJudgmentInput(state, 0);
            ulong afterJudgment = state.Random.State;
            Assert.That(afterJudgment, Is.EqualTo(before), "判定决策不得消耗世界随机流");

            var cmd = ais[0].GetActionInput(state, 0);
            Assert.That(state.Random.State, Is.EqualTo(before), "行动决策不得消耗世界随机流");
        }

        [Test]
        public void SimpleAI_同playerId决策序列确定()
        {
            var s1 = BuildState();
            var s2 = BuildState();
            var a1 = new SimpleAI(2);
            var a2 = new SimpleAI(2);
            for (int i = 0; i < 5; i++)
            {
                ChipAllocation c1 = a1.GetJudgmentInput(s1, 2);
                ChipAllocation c2 = a2.GetJudgmentInput(s2, 2);
                Assert.That(c1.Rock, Is.EqualTo(c2.Rock));
                Assert.That(c1.Scissors, Is.EqualTo(c2.Scissors));
                Assert.That(c1.Paper, Is.EqualTo(c2.Paper));
            }
        }
    }
}
