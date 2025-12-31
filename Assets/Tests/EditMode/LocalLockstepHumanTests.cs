using Core;
using Gameplay.Judgment;
using Network.Local;
using AI;
using NUnit.Framework;

namespace Dynasty.Tests
{
    /// <summary>LocalLockstep 真人掩码分流：判定阶段真人待提交则暂停，AI 分配跨暂停缓存。</summary>
    public class LocalLockstepHumanTests
    {
        private static LocalLockstep CreateLs(out WorldState state)
        {
            state = new WorldState(4, 77);
            var tm = new TurnManager();
            state.TurnManager = tm;
            SimpleAI[] ais = { new SimpleAI(0), new SimpleAI(1), new SimpleAI(2), new SimpleAI(3) };
            return new LocalLockstep(state, ais);
        }

        [Test]
        public void Judgment_HumanNotSubmitted_PausesInJudgmentPhase()
        {
            LocalLockstep ls = CreateLs(out WorldState state);
            ls.HumanPlayers = new bool[] { true, false, false, false };
            ls.HumanJudgmentInput = _ => null;

            ls.StartGame();
            bool running = ls.RunTurn();

            Assert.IsTrue(running);
            Assert.AreEqual(TurnPhase.JudgmentPhase, state.Phase);
            Assert.IsFalse(state.HasJudgmentResult);
        }

        [Test]
        public void Judgment_HumanSubmits_TurnAdvancesPastJudgment()
        {
            LocalLockstep ls = CreateLs(out WorldState state);
            ls.HumanPlayers = new bool[] { true, false, false, false };
            ChipAllocation? pending = null;
            ls.HumanJudgmentInput = _ => pending;

            ls.StartGame();
            bool paused = ls.RunTurn();
            Assert.IsTrue(paused);

            pending = new ChipAllocation(0) { Rock = 1, Scissors = 1, Paper = 1 };
            bool running = ls.RunTurn();

            Assert.IsTrue(running);
            // 完整回合已跑通：判定出结果、回合数推进（EndTurn 后 Phase 回卷至
            // 下一回合 JudgmentPhase，故不能用 Phase != JudgmentPhase 断言）
            Assert.IsTrue(state.HasJudgmentResult);
            Assert.AreEqual(2, state.CurrentTurn);
            Assert.AreEqual(3, state.LastAllocations[0].Total);
        }

        [Test]
        public void Judgment_MultipleHumanSlots_EachSlotIndependent()
        {
            LocalLockstep ls = CreateLs(out WorldState state);
            ls.HumanPlayers = new bool[] { true, false, true, false };
            int[] submittedBy = { 0, 0, 0, 0 };
            ls.HumanJudgmentInput = pid =>
            {
                submittedBy[pid]++;
                if (pid == 0) return new ChipAllocation(0) { Rock = 3 };
                return null; // pid 2 未提交
            };

            ls.StartGame();
            bool paused = ls.RunTurn();

            Assert.IsTrue(paused);
            Assert.AreEqual(1, submittedBy[0]);
            Assert.AreEqual(1, submittedBy[2]);
            Assert.AreEqual(TurnPhase.JudgmentPhase, state.Phase);

            ls.HumanJudgmentInput = pid => new ChipAllocation(pid) { Rock = 1, Scissors = 1, Paper = 1 };
            bool running = ls.RunTurn();

            Assert.IsTrue(running);
            Assert.IsTrue(state.HasJudgmentResult);
        }

        [Test]
        public void Judgment_HumanDead_SkipsHumanInput()
        {
            LocalLockstep ls = CreateLs(out WorldState state);
            ls.HumanPlayers = new bool[] { true, false, false, false };
            // 若该钩子被死亡真人调用会返回 null，RunTurn 将暂停在判定阶段，末尾断言随之失败
            ls.HumanJudgmentInput = _ => null;

            ls.StartGame();
            var c0 = state.GetCharacterByPlayer(0);
            c0.ASC.SetAttributeBaseValue(Core.GAS.AttributeConstants.Sanity, 0f);
            c0.ASC.Tags.AddTag(Core.GAS.GameplayTagConstants.Dead);

            bool running = ls.RunTurn();

            Assert.IsTrue(running);
            // 死亡真人未被轮询（钩子恒返回 null，若被调用会暂停）：
            // 回合完整跑通即证明跳过
            Assert.IsTrue(state.HasJudgmentResult);
            Assert.AreEqual(2, state.CurrentTurn);
            Assert.AreEqual(0, state.LastAllocations[0].Total);
        }
    }
}
