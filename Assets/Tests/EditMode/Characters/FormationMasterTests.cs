using NUnit.Framework;
using Core;
using Core.GAS;
using Gameplay.Characters;
using Gameplay.Movement;

namespace Dynasty.Tests.Characters
{
    public class FormationMasterTests
    {
        private static void Execute(WorldState state, Core.Command.ICommand cmd)
        {
            state.Commands.Enqueue(cmd);
            state.Commands.ExecuteAll(state);
            state.Commands.Clear();
        }

        private static WorldState BuildWithFormation()
        {
            var state = ScenarioBuilder.Create(4).WithAllAt(4).Build();
            var f = state.GetCharacterByPlayer(3);
            f.Definition = FormationMasterDef.Create();
            f.Definition.ApplyTo(f);
            f.ASC.SetBloodSeals(2); // 初始阵盘
            return state;
        }

        [Test]
        public void 束缚_目标不可移动且每回合末受伤()
        {
            var state = BuildWithFormation();
            var f = state.GetCharacterByPlayer(3);
            f.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 1);
            var target = state.GetCharacterByPlayer(0);
            target.ASC.Tags.RemoveTag(GameplayTagConstants.HasShield);
            float before = target.Sanity;

            SkillSystem.Activate(f.Definition.Skills[0], f, target, state, 0); // 束缚

            Assert.IsTrue(target.ASC.Tags.HasTag(GameplayTagConstants.Bound), "束缚标签");
            Assert.AreEqual(1, f.ASC.GetBloodSeals(), "消耗 1 阵盘");

            // 移动拒绝
            target.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 1);
            Execute(state, new MoveCommand(0, 0, 6));
            Assert.AreEqual(4, target.CurrentLocationId, "不可移动");

            // 回合末 0.5 伤害
            target.ASC.TickEffects(state);
            Assert.AreEqual(before - 0.5f, target.Sanity, 0.001f, "回合末 0.5 物理效果伤");
        }

        [Test]
        public void 束缚_被束缚者1AP解除()
        {
            var state = BuildWithFormation();
            var f = state.GetCharacterByPlayer(3);
            f.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 1);
            var target = state.GetCharacterByPlayer(0);
            SkillSystem.Activate(f.Definition.Skills[0], f, target, state, 0);

            target.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 1);
            Execute(state, new UnbindCommand(0, 0));

            Assert.IsFalse(target.ASC.Tags.HasTag(GameplayTagConstants.Bound), "解除束缚");
        }

        [Test]
        public void 血炼_阵盘不足不可施放束缚()
        {
            var state = BuildWithFormation();
            var f = state.GetCharacterByPlayer(3);
            f.ASC.SetBloodSeals(0);
            f.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 1);
            var target = state.GetCharacterByPlayer(0);

            SkillSystem.Activate(f.Definition.Skills[0], f, target, state, 0);

            Assert.IsFalse(target.ASC.Tags.HasTag(GameplayTagConstants.Bound), "阵盘不足施放失败");
        }
    }
}
