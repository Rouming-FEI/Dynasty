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

            presenter.SubmitChipAllocation(0, rock: 1, scissors: 0, paper: 2);

            ChipAllocation? first = ls.HumanJudgmentInput(0);
            ChipAllocation? second = ls.HumanJudgmentInput(0);
            Assert.IsTrue(first.HasValue);
            Assert.AreEqual(1, first.Value.Rock);
            Assert.AreEqual(2, first.Value.Paper);
            Assert.IsFalse(second.HasValue);
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
    }
}
