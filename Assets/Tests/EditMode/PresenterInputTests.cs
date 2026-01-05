using Core;
using Core.Command;
using Gameplay.Judgment;
using Gameplay.Movement;
using Network.Local;
using AI;
using UI.Presenter;
using NUnit.Framework;

namespace Dynasty.Tests
{
    /// <summary>Presenter 输入意图通道：Bind 接管 LocalLockstep 真人输入，提交恰好消费一次。</summary>
    public class PresenterInputTests
    {
        private static LocalLockstep CreateLs(out WorldState state)
        {
            state = new WorldState(4, 303);
            SimpleAI[] ais = { new SimpleAI(0), new SimpleAI(1), new SimpleAI(2), new SimpleAI(3) };
            return new LocalLockstep(state, ais); // ctor 内部自设 state.TurnManager
        }

        [Test]
        public void ChipIntent_SubmitOnce_ConsumedOnce()
        {
            LocalLockstep ls = CreateLs(out WorldState state);
            var presenter = new BattlePresenter(ls.State);
            presenter.Bind(ls, 0);

            presenter.SubmitChipAllocation(rock: 1, scissors: 0, paper: 2);

            ChipAllocation? first = ls.HumanJudgmentInput(0);
            ChipAllocation? second = ls.HumanJudgmentInput(0);
            Assert.IsTrue(first.HasValue);
            Assert.AreEqual(1, first.Value.Rock);
            Assert.AreEqual(2, first.Value.Paper);
            Assert.IsFalse(second.HasValue);
        }

        [Test]
        public void HasPendingJudgment_Lifecycle_FalseSubmitTrueConsumeFalse()
        {
            LocalLockstep ls = CreateLs(out WorldState state);
            var presenter = new BattlePresenter(ls.State);
            presenter.Bind(ls, 0);

            Assert.IsFalse(presenter.HasPendingJudgment);
            presenter.SubmitChipAllocation(1, 1, 1);
            Assert.IsTrue(presenter.HasPendingJudgment);
            ls.HumanJudgmentInput(0);   // 消费
            Assert.IsFalse(presenter.HasPendingJudgment);
        }

        [Test]
        public void ChipIntent_DoubleSubmit_LastWriteWins()
        {
            LocalLockstep ls = CreateLs(out WorldState state);
            var presenter = new BattlePresenter(ls.State);
            presenter.Bind(ls, 0);

            presenter.SubmitChipAllocation(3, 0, 0);
            presenter.SubmitChipAllocation(0, 0, 3);

            ChipAllocation? taken = ls.HumanJudgmentInput(0);
            ChipAllocation? again = ls.HumanJudgmentInput(0);
            Assert.IsTrue(taken.HasValue);
            Assert.AreEqual(3, taken.Value.Paper);
            Assert.AreEqual(0, taken.Value.Rock);
            Assert.IsFalse(again.HasValue);
        }

        [Test]
        public void CommandIntent_SubmitOnce_ConsumedOnce()
        {
            LocalLockstep ls = CreateLs(out WorldState state);
            var presenter = new BattlePresenter(ls.State);
            presenter.Bind(ls, 0);

            var cmd = new WasteAPCommand(state.CurrentTick, 0);
            presenter.SubmitCommand(cmd);

            ICommand taken = ls.ManualInputProvider(0);
            ICommand again = ls.ManualInputProvider(0);
            Assert.AreSame(cmd, taken);
            Assert.IsNull(again);
        }

        [Test]
        public void Bind_SetsHumanMask_AndHooks()
        {
            LocalLockstep ls = CreateLs(out WorldState state);
            var presenter = new BattlePresenter(ls.State);

            Assert.IsNull(ls.HumanPlayers);

            presenter.Bind(ls, 2);

            Assert.AreEqual(4, ls.HumanPlayers.Length);
            for (int i = 0; i < 4; i++)
                Assert.AreEqual(i == 2, ls.HumanPlayers[i]);
            Assert.IsNotNull(ls.HumanJudgmentInput);
            Assert.IsNotNull(ls.ManualInputProvider);
        }

        [Test]
        public void Bind_Integration_AiActorsAct_HumanPausesOnlyForOwnSlot()
        {
            // C1 回归：掩码模式下 AI 行动槽必须走 SimpleAI。修复前行动段全局判
            // ManualInputProvider != null，首位 AI 行动者经钩子拿到 null → 暂停返回，
            // AP 永不消耗 → 阶段停在 ActionPhase、行动者停在非真人槽（静默死锁）。
            LocalLockstep ls = CreateLs(out WorldState state);
            var presenter = new BattlePresenter(state);
            presenter.Bind(ls, 0);
            // 确定性判定编排：P0=布3、P1=布2、P2=石3、P3=石3/布1 → 总量 R6/S0/P6
            // 双高(石布)+低(剪) → 胜手=布：P0 胜(eff3)、P1 胜(eff2)、P3 胜(eff1)、
            // P2 中立。行动顺序 [0,1,3,2]，P0/P1/P3 各 1 AP —— 真人先行且 AI 必然行动
            ls.JudgmentInputProvider = pid =>
            {
                switch (pid)
                {
                    case 1:  return new ChipAllocation(1) { Paper = 2 };
                    case 2:  return new ChipAllocation(2) { Rock = 3 };
                    case 3:  return new ChipAllocation(3) { Rock = 3, Paper = 1 };
                    default: return new ChipAllocation(pid) { Scissors = 1, Paper = 2 };
                }
            };
            presenter.SubmitChipAllocation(0, 0, 3);

            ls.StartGame();
            ls.RunTurn();   // 判定完成，随后应停在真人自己的行动槽上暂停

            Assert.AreEqual(TurnPhase.ActionPhase, state.Phase);
            Assert.AreEqual(0, ls.CurrentActorPlayerId);

            for (int i = 0; i < 200 && state.Phase == TurnPhase.ActionPhase; i++)
            {
                if (ls.CurrentActorPlayerId == 0)
                {
                    // 暂停语义：无提交时 RunTurn 停在真人槽——阶段不变、行动者不变
                    bool paused = ls.RunTurn();
                    Assert.IsTrue(paused);
                    Assert.AreEqual(TurnPhase.ActionPhase, state.Phase);
                    Assert.AreEqual(0, ls.CurrentActorPlayerId);
                    presenter.SubmitCommand(new WasteAPCommand(state.CurrentTick, 0));
                    ls.RunTurn();   // 消费刚提交的指令：真人 AP 耗尽，行动者推进
                }
                else
                {
                    ls.RunTurn();   // AI 槽：应经 SimpleAI 行动，不依赖任何提交
                }
            }

            Assert.AreNotEqual(TurnPhase.ActionPhase, state.Phase,
                "200 轮内必须离开行动阶段（修复前 AI 槽被 ManualInputProvider 劫持恒返回 null，死锁）");
        }
    }
}
