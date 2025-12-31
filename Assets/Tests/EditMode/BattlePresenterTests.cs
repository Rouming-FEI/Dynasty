using Core;
using Core.GAS;
using UI.Presenter;
using NUnit.Framework;

namespace Dynasty.Tests
{
    /// <summary>BattlePresenter 快照构建与战斗日志队列（纯 C# 表现层，无 UnityEngine）。</summary>
    public class BattlePresenterTests
    {
        private static WorldState NewState()
        {
            var state = new WorldState(4, 909);
            var tm = new TurnManager();
            state.TurnManager = tm;
            tm.StartGame(state);
            return state;
        }

        [Test]
        public void Snapshot_InitialGame_FourRowsAndTenNodes()
        {
            var presenter = new BattlePresenter(NewState());

            var snap = presenter.BuildSnapshot();

            Assert.AreEqual(4, snap.CharacterRows.Count);
            Assert.AreEqual(10, snap.MapNodes.Count);
            Assert.AreEqual("守护者", snap.CharacterRows[0].Name);
            Assert.AreEqual("下潜入口A", snap.MapNodes[0].Name);
            Assert.IsFalse(snap.GameOver);
            Assert.AreEqual(4, snap.AliveCount);
        }

        [Test]
        public void Snapshot_AfterRelocation_PlayerOnNewNodeOnly()
        {
            WorldState state = NewState();
            state.GetCharacterByPlayer(2).CurrentLocationId = 8;   // 普通人 → 地狱前哨
            var presenter = new BattlePresenter(state);

            var snap = presenter.BuildSnapshot();

            Assert.AreEqual(0, snap.MapNodes[2].PlayerNames.Count);
            Assert.AreEqual(1, snap.MapNodes[8].PlayerNames.Count);
            Assert.AreEqual("普通人", snap.MapNodes[8].PlayerNames[0]);
        }

        [Test]
        public void Snapshot_HiddenPlayer_MaskedInRowAndAbsentFromMap()
        {
            WorldState state = NewState();
            state.GetCharacterByPlayer(1).ASC.Tags.AddTag(GameplayTagConstants.HideoutHidden);
            var presenter = new BattlePresenter(state);

            var snap = presenter.BuildSnapshot();

            Assert.IsTrue(snap.CharacterRows[1].IsHidden);
            Assert.AreEqual("??", snap.CharacterRows[1].LocationName);
            Assert.AreEqual(0, snap.MapNodes[1].PlayerNames.Count);
        }

        [Test]
        public void Log_DeathEvent_AppendsToLogTail()
        {
            WorldState state = NewState();
            var presenter = new BattlePresenter(state);

            var evt = new GameplayEvent(GameplayEventTypes.PlayerDied, 1, 2) { IntArg1 = 0 };
            state.Events.Dispatch(GameplayEventTypes.PlayerDied, ref evt);

            var snap = presenter.BuildSnapshot();

            Assert.Greater(snap.LogTail.Count, 0);
            StringAssert.Contains("击杀", snap.LogTail[snap.LogTail.Count - 1]);
        }
    }
}
