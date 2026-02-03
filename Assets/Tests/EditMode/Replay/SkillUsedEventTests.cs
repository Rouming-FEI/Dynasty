using Core;
using Core.Command;
using Core.GAS;
using Gameplay.Characters;
using NUnit.Framework;

namespace Dynasty.Tests.Replay
{
    public class SkillUsedEventTests
    {
        [Test]
        public void UseSkillCommand_DispatchSkillUsed()
        {
            var state = new WorldState(2, 5);
            state.TurnManager = new TurnManager();
            state.TurnManager.StartGame(state); // 默认轮换：守护者/魑魅
            int seen = 0;
            int seenSkillId = -1;
            int seenSource = -1;
            int seenTarget = -1;
            // C# 9（Unity 2022 对齐）不支持 lambda ref 参数修饰符，用匿名方法语法
            state.Events.RecorderHook = delegate(int type, ref GameplayEvent e)
            {
                if (type == GameplayEventTypes.SkillUsed)
                {
                    seen++;
                    seenSkillId = e.IntArg1;
                    seenSource = e.SourceId;
                    seenTarget = e.TargetId;
                }
            };
            Character c = state.GetCharacterByPlayer(0); // 守护者
            Character target = state.GetCharacterByPlayer(1);
            c.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 3);
            state.Commands.Enqueue(new UseSkillCommand(state.CurrentTick, 0, 0, 1)); // 技能 0 = 重装（被动，不可施放）
            state.Commands.ExecuteAll(state);
            Assert.That(seen, Is.EqualTo(0), "被动技能不可施放，不应派发 SkillUsed");

            state.Commands.Enqueue(new UseSkillCommand(state.CurrentTick, 0, 1, 1)); // 技能 1 = 守护（主动）
            state.Commands.ExecuteAll(state);
            Assert.That(seen, Is.EqualTo(1), "主动技能施放应派发 SkillUsed");
            Assert.That(seenSkillId, Is.EqualTo(2), "IntArg1 应为技能 Id");
            Assert.That(seenSource, Is.EqualTo(c.Id), "SourceId 应为施放者角色 Id");
            Assert.That(seenTarget, Is.EqualTo(target.Id), "TargetId 应为目标角色 Id");
        }

        [Test]
        public void Activate_DirectCall_DispatchSkillUsed()
        {
            var state = new WorldState(2, 5);
            state.TurnManager = new TurnManager();
            state.TurnManager.StartGame(state); // 默认轮换：守护者/魑魅
            int seen = 0;
            state.Events.RecorderHook = delegate (int type, ref GameplayEvent e)
            {
                if (type == GameplayEventTypes.SkillUsed) seen++;
            };
            Character c = state.GetCharacterByPlayer(0);
            Character target = state.GetCharacterByPlayer(1);
            c.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 3);
            SkillSystem.Activate(c.Definition.Skills[1], c, target, state, 1); // 守护（主动）
            Assert.That(seen, Is.EqualTo(1), "直调 Activate 也应派发 SkillUsed");
        }
    }
}
