using AI;
using Core;
using Core.GAS;
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

            // 给 1 AP，确保越过入口的 AP 门槛真实走到 _random.Next(100) 决策路径。
            // 若不补 AP，角色 AP=0 时 GetActionInput 在入口直接 return null，
            // 断言永远绿（覆盖空洞：AI 根本没决策，测试绿不是因为隔离正确）。
            var c = state.GetCharacterByPlayer(0);
            c.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 1);
            var cmd = ais[0].GetActionInput(state, 0);
            Assert.That(cmd, Is.Not.Null, "给 1 AP 后必须真实执行决策路径并产出命令");
            Assert.That(state.Random.State, Is.EqualTo(before), "行动决策不得消耗世界随机流");
        }

        [Test]
        public void SimpleAI_同playerId决策序列确定()
        {
            var s1 = BuildState();
            var s2 = BuildState();
            var a1 = new SimpleAI(2);
            var a2 = new SimpleAI(2);
            bool varied = false;
            int firstRock = 0, firstScissors = 0, firstPaper = 0;
            for (int i = 0; i < 5; i++)
            {
                ChipAllocation c1 = a1.GetJudgmentInput(s1, 2);
                ChipAllocation c2 = a2.GetJudgmentInput(s2, 2);
                Assert.That(c1.Rock, Is.EqualTo(c2.Rock));
                Assert.That(c1.Scissors, Is.EqualTo(c2.Scissors));
                Assert.That(c1.Paper, Is.EqualTo(c2.Paper));
                if (i == 0)
                {
                    firstRock = c1.Rock;
                    firstScissors = c1.Scissors;
                    firstPaper = c1.Paper;
                }
                else if (c1.Rock != firstRock || c1.Scissors != firstScissors || c1.Paper != firstPaper)
                {
                    varied = true;
                }
            }
            // 防恒真：若 DeterministicRandom 状态不被消费（每次返回相同分配），
            // 上面逐轮相等断言恒绿，这里必须出现 ≥2 种分配才证明流在推进。
            Assert.That(varied, Is.True, "5 轮判定中 AI 随机流必须推进（至少出现 2 种不同分配）");
        }

        [Test]
        public void SimpleAI_不同playerId决策序列不同()
        {
            var state = BuildState();
            var ai0 = new SimpleAI(0);
            var ai1 = new SimpleAI(1);
            ChipAllocation c0 = ai0.GetJudgmentInput(state, 0);
            ChipAllocation c1 = ai1.GetJudgmentInput(state, 1);
            bool same = c0.Rock == c1.Rock && c0.Scissors == c1.Scissors && c0.Paper == c1.Paper;
            Assert.That(same, Is.False, "不同 playerId 的 AI 首轮分配必须不同（默认 seed 按 playerId 错开）");
        }
    }
}
