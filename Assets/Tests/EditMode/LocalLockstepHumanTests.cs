using Core;
using Gameplay.Judgment;
using Network.Local;
using AI;
using NUnit.Framework;
using System.Text;

namespace Dynasty.Tests
{
    /// <summary>LocalLockstep 真人掩码分流：判定阶段真人待提交则暂停，AI 分配跨暂停缓存。</summary>
    public class LocalLockstepHumanTests
    {
        private const int TurnCap = 60;

        private static LocalLockstep CreateLs(out WorldState state)
        {
            state = new WorldState(4, 77);
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
            // P0 暂停前提交的 Rock=3 在恢复后原样生效——槽位独立性证据链
            Assert.AreEqual(3, state.LastAllocations[0].Rock);
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
            c0.ASC.Tags.AddTag(Core.GAS.GameplayTagConstants.Dead);

            bool running = ls.RunTurn();

            Assert.IsTrue(running);
            // 死亡真人未被轮询（钩子恒返回 null，若被调用会暂停）：
            // 回合完整跑通即证明跳过
            Assert.IsTrue(state.HasJudgmentResult);
            Assert.AreEqual(2, state.CurrentTurn);
            Assert.AreEqual(0, state.LastAllocations[0].Total);
        }

        [Test]
        public void Judgment_PauseResume_ProducesIdenticalHash_AsImmediateSubmit()
        {
            long immediateHash = RunHumanGameHash(123, pauseResume: false);
            long pauseResumeHash = RunHumanGameHash(123, pauseResume: true);

            Assert.AreEqual(immediateHash, pauseResumeHash,
                "暂停-恢复与立即提交必须产生相同状态哈希（AI 分配只收集一次，随机流不漂移）");
        }

        [Test]
        public void StartGame_AbandonedMidJudgmentPause_RecollsAIAllocations()
        {
            // 参照局：暂停一次后提交 —— AI 用的是暂停时缓存的第 1 次随机流取样
            LocalLockstep refLs = CreateLs(out WorldState refState);
            refLs.HumanPlayers = new bool[] { true, false, false, false };
            bool refSubmitted = false;
            refLs.HumanJudgmentInput = _ => refSubmitted
                ? new ChipAllocation(0) { Rock = 1, Scissors = 1, Paper = 1 }
                : (ChipAllocation?)null;
            refLs.StartGame();
            refLs.RunTurn();
            refSubmitted = true;
            refLs.RunTurn();

            // 重启局：暂停（AI 已消耗第 1 潮随机流取样）后 StartGame 弃局重开 ——
            // 必须重新收集，不得复用陈旧缓存（否则随机流漂移、判定用旧值）
            LocalLockstep ls = CreateLs(out WorldState state);
            ls.HumanPlayers = new bool[] { true, false, false, false };
            ChipAllocation? pending = null;
            ls.HumanJudgmentInput = _ => pending;
            ls.StartGame();
            ls.RunTurn();
            pending = new ChipAllocation(0) { Rock = 1, Scissors = 1, Paper = 1 };
            ls.StartGame();
            bool running = ls.RunTurn();

            Assert.IsTrue(running);
            Assert.IsTrue(state.HasJudgmentResult);
            Assert.AreEqual(3, state.LastAllocations[0].Total);
            Assert.AreNotEqual(FormatAiAllocations(refState), FormatAiAllocations(state),
                "重开后 AI 应取第 2 次随机流取样，而非复用暂停时的陈旧缓存");
        }

        /// <summary>
        /// 跑一局带真人 P0 的 4 人对战并返回最终状态哈希。
        /// pauseResume=false：每次判定钩子立即提交 1-1-1；
        /// pauseResume=true：每次判定先返回 null（暂停），检测到暂停后再提交继续。
        /// 两种方式的随机流消耗必须完全一致。
        /// </summary>
        private static long RunHumanGameHash(int seed, bool pauseResume)
        {
            var state = new WorldState(4, seed);
            SimpleAI[] ais = { new SimpleAI(0), new SimpleAI(1), new SimpleAI(2), new SimpleAI(3) };
            var ls = new LocalLockstep(state, ais);
            ls.HumanPlayers = new bool[] { true, false, false, false };
            bool submitted = !pauseResume;
            ls.HumanJudgmentInput = _ => submitted
                ? new ChipAllocation(0) { Rock = 1, Scissors = 1, Paper = 1 }
                : (ChipAllocation?)null;

            ls.StartGame();
            for (int guard = 0; guard < 1000
                 && state.Phase != TurnPhase.GameOver
                 && state.CurrentTurn < TurnCap; guard++)
            {
                int turnBefore = state.CurrentTurn;
                ls.RunTurn();
                // 判定暂停判定依据：回合数未推进且停在判定阶段（全局平重判同样
                // 满足此条件，下轮循环会再次走暂停-恢复，行为一致）
                if (pauseResume && state.CurrentTurn == turnBefore
                    && state.Phase == TurnPhase.JudgmentPhase)
                {
                    submitted = true;
                    ls.RunTurn();
                    submitted = false;
                }
            }
            return state.ComputeStateHash();
        }

        private static string FormatAiAllocations(WorldState state)
        {
            var sb = new StringBuilder();
            for (int i = 1; i < state.PlayerCount; i++)
                sb.Append(state.LastAllocations[i].Rock).Append('/')
                  .Append(state.LastAllocations[i].Scissors).Append('/')
                  .Append(state.LastAllocations[i].Paper).Append(' ');
            return sb.ToString();
        }
    }
}
