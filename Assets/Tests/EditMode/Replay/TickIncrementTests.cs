using AI;
using Core;
using Core.Command;
using Core.GAS;
using Gameplay.Characters;
using Gameplay.Judgment;
using Gameplay.Movement;
using Network.Local;
using NUnit.Framework;

namespace Dynasty.Tests.Replay
{
    public class TickIncrementTests
    {
        private static LocalLockstep CreateLockstep(WorldState state)
        {
            var ais = new SimpleAI[state.PlayerCount];
            for (int i = 0; i < ais.Length; i++) ais[i] = new SimpleAI(i);
            return new LocalLockstep(state, ais);
        }

        /// <summary>固定筹码（全 1-1-1 平筹码）+ 暂停钩子（行动返回 null），
        /// 推进到行动阶段并停在首位行动者处（暂停不消耗 AP、不加 Tick）。</summary>
        private static void PauseAtActionPhase(LocalLockstep lockstep)
        {
            lockstep.JudgmentInputProvider = pid =>
            {
                var a = new ChipAllocation(pid);
                a.SetDefault();
                return a;
            };
            lockstep.ManualInputProvider = pid => null;
            int guard = 0;
            while (lockstep.State.Phase != TurnPhase.ActionPhase
                   && lockstep.State.Phase != TurnPhase.GameOver && guard++ < 20)
                lockstep.RunTurn();
            Assert.That(lockstep.State.Phase, Is.EqualTo(TurnPhase.ActionPhase),
                "应暂停在行动阶段首位行动者处");
        }

        [Test]
        public void JudgmentResolution_IncrementsTick()
        {
            var state = new WorldState(4, 42);
            var lockstep = CreateLockstep(state);
            lockstep.StartGame();
            int before = state.CurrentTick;
            PauseAtActionPhase(lockstep);
            // 作废重判轮不递增；成功判定恰 +1（暂停点在行动段首位行动者，无命令 Tick）
            Assert.That(state.CurrentTick, Is.EqualTo(before + 1), "判定结算应恰 +1 Tick");
        }

        [Test]
        public void ActionCommand_IncrementsTick()
        {
            var state = new WorldState(4, 42);
            var lockstep = CreateLockstep(state);
            lockstep.StartGame();
            PauseAtActionPhase(lockstep);
            int before = state.CurrentTick;
            // 保底全员 1 AP：首位行动者 WasteAP +1 Tick 后，第二位行动者必存在
            // → provider 第二次调用返回 null 暂停，此时 Tick 恰 = before+1
            int calls = 0;
            lockstep.ManualInputProvider = pid => calls++ == 0
                ? (ICommand)new WasteAPCommand(state.CurrentTick, pid)
                : null;
            lockstep.RunTurn();
            Assert.That(state.CurrentTick, Is.EqualTo(before + 1), "行动命令执行应恰 +1 Tick");
        }

        [Test]
        public void TurnEnd_IncrementsTick()
        {
            var state = new WorldState(4, 42);
            var lockstep = CreateLockstep(state);
            lockstep.StartGame();
            PauseAtActionPhase(lockstep);
            int before = state.CurrentTick;
            // 全员 AP 清零：行动段无行动者 → Advance → EndTurn（回合末恰 +1 Tick）。
            // SetAttributeBaseValue 同时写 base+current；充能初始 0 且第一回合
            // 无战斗事件累加，GetNextActor 不会因充能放行任何人
            for (int i = 0; i < state.PlayerCount; i++)
            {
                Character c = state.GetCharacterByPlayer(i);
                if (c != null)
                    c.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 0f);
            }
            lockstep.RunTurn();
            Assert.That(state.CurrentTick, Is.EqualTo(before + 1), "回合末结算应恰 +1 Tick");
            Assert.That(state.CurrentTurn, Is.EqualTo(2), "应推进到第二回合");
        }

        [Test]
        public void Tick_IsNonDecreasing()
        {
            var state = new WorldState(4, 7);
            var lockstep = CreateLockstep(state);
            lockstep.StartGame();
            int last = -1;
            for (int i = 0; i < 30; i++)
            {
                lockstep.RunTurn();
                Assert.That(state.CurrentTick, Is.GreaterThanOrEqualTo(last),
                    "Tick 必须单调不减（全局平作废轮不递增）");
                last = state.CurrentTick;
                if (state.Phase == TurnPhase.GameOver) break;
            }
        }

        [Test]
        public void OnTickAdvanced_FiresOncePerTickIncreasing()
        {
            var state = new WorldState(4, 7);
            var lockstep = CreateLockstep(state);
            var ticks = new System.Collections.Generic.List<int>();
            state.OnTickAdvanced = t => ticks.Add(t);
            lockstep.StartGame();
            int guard = 0;
            while (state.Phase != TurnPhase.GameOver && guard++ < 10)
                lockstep.RunTurn();
            Assert.That(ticks.Count, Is.EqualTo(state.CurrentTick), "每个 Tick 恰回调一次");
            for (int i = 1; i < ticks.Count; i++)
                Assert.That(ticks[i], Is.EqualTo(ticks[i - 1] + 1), "回调值应连续递增");
        }
    }
}
