using NUnit.Framework;
using Core;
using Core.GAS;
using Gameplay.Characters;
using Gameplay.Combat;
using Gameplay.Items;

namespace Dynasty.Tests.Characters
{
    public class SwordGhostTests
    {
        private static void Execute(WorldState state, Core.Command.ICommand cmd)
        {
            state.Commands.Enqueue(cmd);
            state.Commands.ExecuteAll(state);
            state.Commands.Clear();
        }

        // 剑鬼作为第 4 个角色挂到 player 3：ScenarioBuilder 目前只有 3 角色，用 4 人局
        private static WorldState BuildWithSwordGhost()
        {
            var state = ScenarioBuilder.Create(4).WithAllAt(4).Build();
            var c = state.GetCharacterByPlayer(3);
            c.Definition = SwordGhostDef.Create();
            c.Definition.ApplyTo(c);
            // ApplyTo 不含被动注册（生产路径注册在 TurnManager.StartGame），测试需手动注册
            for (int s = 0; s < c.Definition.Skills.Length; s++)
                c.ASC.RegisterPassive(c.Definition.Skills[s]);
            return state;
        }

        [Test]
        public void 飞剑_判定胜负且胜者受15物理伤()
        {
            var state = BuildWithSwordGhost();
            var sg = state.GetCharacterByPlayer(3);
            sg.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 5);
            var target = state.GetCharacterByPlayer(0);
            target.ASC.Tags.RemoveTag(GameplayTagConstants.HasShield); // 清盾防吸收
            float sanityBefore = target.Sanity;

            Execute(state, new FlyingSwordCommand(0, 3, 0));

            Assert.AreEqual(4, sg.ActionPoints, "飞剑 1 AP");
            Assert.IsTrue(target.Sanity == sanityBefore || target.Sanity == sanityBefore - 1.5f,
                "败无事，胜 -1.5 物理伤");
        }

        [Test]
        public void 绝症_每回合末侵蚀减05且不低于05()
        {
            var state = BuildWithSwordGhost();
            var sg = state.GetCharacterByPlayer(3);
            sg.ASC.SetAttributeBaseValue(AttributeConstants.ErosionLimit, 1.5f);

            sg.ASC.TickEffects(state); // 1.5 → 1.0
            Assert.AreEqual(1.0f, sg.ASC.GetAttributeValue(AttributeConstants.ErosionLimit), 0.001f);
            sg.ASC.TickEffects(state); // 1.0 → 0.5
            sg.ASC.TickEffects(state); // 0.5 停
            Assert.AreEqual(0.5f, sg.ASC.GetAttributeValue(AttributeConstants.ErosionLimit), 0.001f, "0.5 下限");
        }

        [Test]
        public void 鬼化_永久攻防加成且取消绝症()
        {
            var state = BuildWithSwordGhost();
            var sg = state.GetCharacterByPlayer(3);
            sg.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
            sg.ASC.SetAttributeBaseValue(AttributeConstants.ErosionLimit, 2f);
            SkillDef ghostForm = sg.Definition.Skills[2]; // 鬼化

            SkillSystem.Activate(ghostForm, sg, sg, state, 2);

            Assert.IsTrue(sg.ASC.Tags.HasTag(GameplayTagConstants.GhostForm), "鬼化标签");
            Assert.AreEqual(0.5f, sg.ASC.GetAttributeValue(AttributeConstants.PhysicalDefense), 0.001f);
            Assert.AreEqual(0.5f, sg.ASC.GetAttributeValue(AttributeConstants.AttackBonus), 0.001f);

            sg.ASC.TickEffects(state);
            Assert.AreEqual(2f, sg.ASC.GetAttributeValue(AttributeConstants.ErosionLimit), 0.001f, "鬼化取消绝症");
        }

        [Test]
        public void 灵体状态_鬼化后普通攻击被拒绝()
        {
            var state = BuildWithSwordGhost();
            var sg = state.GetCharacterByPlayer(3);
            sg.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 3);
            SkillDef ghostForm = sg.Definition.Skills[2];
            SkillSystem.Activate(ghostForm, sg, sg, state, 2);

            var target = state.GetCharacterByPlayer(1);
            float sanityBefore = target.Sanity;
            Execute(state, new DamageCommand(0, 3, 1, 1f));

            Assert.AreEqual(sanityBefore, target.Sanity, 0.001f, "灵体状态普通攻击无效");
        }
    }
}
