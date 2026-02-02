using AI;
using Core;
using Core.Command;
using Gameplay.Judgment;
using Gameplay.Movement;
using Network.Local;
using NUnit.Framework;

namespace Dynasty.Tests.Replay
{
    public class HookTests
    {
        [Test]
        public void JudgmentHooks_FireOncePerRound()
        {
            var state = new WorldState(4, 1);
            var ais = new SimpleAI[4];
            for (int i = 0; i < ais.Length; i++) ais[i] = new SimpleAI(i);
            var lockstep = new LocalLockstep(state, ais);
            int collected = 0, committed = 0;
            lockstep.OnJudgmentCollected = (pid, alloc, isHuman) => collected++;
            lockstep.OnJudgmentCommitted = allocs => committed++;
            lockstep.StartGame();
            int guard = 0;
            while (state.Phase == TurnPhase.JudgmentPhase && guard++ < 10)
                lockstep.RunTurn();
            Assert.That(collected, Is.GreaterThan(0), "判定槽收集钩子应触发");
            Assert.That(committed, Is.GreaterThan(0), "判定轮提交钩子应触发");
            Assert.That(collected % state.PlayerCount, Is.Zero, "每轮收集次数应为玩家数整数倍");
        }

        [Test]
        public void ActionHook_FiresPerActionCommand()
        {
            var state = new WorldState(4, 1);
            var ais = new SimpleAI[4];
            for (int i = 0; i < ais.Length; i++) ais[i] = new SimpleAI(i);
            var lockstep = new LocalLockstep(state, ais);
            int actions = 0;
            lockstep.OnActionCommand = (pid, cmd, isHuman) => actions++;
            lockstep.JudgmentInputProvider = pid => { var a = new ChipAllocation(pid); a.SetDefault(); return a; };
            lockstep.ManualInputProvider = pid => new WasteAPCommand(state.CurrentTick, pid);
            lockstep.StartGame();
            int guard = 0;
            // StartGame 后处于 JudgmentPhase，且单次 RunTurn = 完整回合（判定→行动→回合末），
            // 行动钩子在 RunTurn 内部触发；此处驱动回合直至脱离判定阶段或守卫耗尽
            while (state.Phase == TurnPhase.JudgmentPhase && guard++ < 20)
                lockstep.RunTurn();
            Assert.That(actions, Is.GreaterThan(0), "行动命令旁观钩子应触发");
        }

        [Test]
        public void EventBusRecorderHook_FiresOnDispatch()
        {
            var state = new WorldState(4, 1);
            int recorded = 0;
            // C# 9（Unity 2022 对齐）不支持 lambda ref 参数修饰符，用匿名方法语法
            state.Events.RecorderHook = delegate(int type, ref Core.GAS.GameplayEvent e) { recorded++; };
            var evt = new Core.GAS.GameplayEvent(Core.GAS.GameplayEventTypes.TurnStart, -1, -1);
            state.Events.Dispatch(Core.GAS.GameplayEventTypes.TurnStart, ref evt);
            Assert.That(recorded, Is.EqualTo(1), "旁路钩子应记录 1 次");
        }

        [Test]
        public void HumanSlot_Resume_DoesNotRecountCollected()
        {
            var state = new WorldState(2, 3);
            var ais = new SimpleAI[2];
            for (int i = 0; i < ais.Length; i++) ais[i] = new SimpleAI(i);
            var lockstep = new LocalLockstep(state, ais);
            lockstep.HumanPlayers = new[] { true, false };
            int collected = 0, committed = 0;
            lockstep.OnJudgmentCollected = (pid, alloc, isHuman) => collected++;
            lockstep.OnJudgmentCommitted = allocs => committed++;
            // 真人判定：第一次 null（未提交→暂停），第二次返回筹码（提交）
            int humanCalls = 0;
            lockstep.HumanJudgmentInput = pid =>
            {
                humanCalls++;
                if (humanCalls == 1) return null;
                var a = new ChipAllocation(pid);
                a.Set(2, 1, 1);
                return a;
            };
            lockstep.StartGame();
            lockstep.RunTurn(); // 真人未提交 → 暂停；仅 AI 槽收集
            Assert.That(collected, Is.EqualTo(1), "暂停时仅 AI 槽收集");
            lockstep.RunTurn(); // 恢复：补查只补真人槽 → 提交
            Assert.That(collected, Is.EqualTo(2), "恢复补查不重复计数已填槽");
            Assert.That(committed, Is.EqualTo(1), "恢复后完成一轮提交");
        }
    }
}
