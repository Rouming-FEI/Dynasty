using NUnit.Framework;
using Core;
using Core.GAS;
using Gameplay.Characters;

namespace Dynasty.Tests.Characters
{
    public class WoodSpiritTests
    {
        private static void Execute(WorldState state, Core.Command.ICommand cmd)
        {
            state.Commands.Enqueue(cmd);
            state.Commands.ExecuteAll(state);
            state.Commands.Clear();
        }

        private static WorldState BuildWithWoodSpirit()
        {
            var state = ScenarioBuilder.Create(4).WithAllAt(4).Build();
            var w = state.GetCharacterByPlayer(3);
            w.Definition = WoodSpiritDef.Create();
            w.Definition.ApplyTo(w);
            for (int s = 0; s < w.Definition.Skills.Length; s++)
                w.ASC.RegisterPassive(w.Definition.Skills[s]);
            return state;
        }

        [Test]
        public void 瘟疫之源_理智非满时同地点角色受伤()
        {
            var state = BuildWithWoodSpirit();
            var w = state.GetCharacterByPlayer(3);
            w.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 2f); // 非满
            var other = state.GetCharacterByPlayer(0);
            other.ASC.Tags.RemoveTag(GameplayTagConstants.HasShield);
            float before = other.Sanity;

            w.ASC.TickEffects(state);

            Assert.AreEqual(before - 0.5f, other.Sanity, 0.001f, "同地点角色受 0.5 法术效果伤");
        }

        [Test]
        public void 续存_死亡时从种子复活()
        {
            var state = BuildWithWoodSpirit();
            var w = state.GetCharacterByPlayer(3);
            w.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 1);
            SkillSystem.Activate(w.Definition.Skills[1], w, w, state, 1); // 安置种子于位置 4
            w.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 0.5f);

            Execute(state, new Gameplay.Combat.DamageCommand(0, 0, 3, 1f)); // 他人击杀？改用直接死亡命令
            // 简化：直接执行 DeathCommand
            state.Commands.Enqueue(new Gameplay.Combat.DeathCommand(0, 3, 0));
            state.Commands.ExecuteAll(state);
            state.Commands.Clear();

            Assert.IsFalse(w.IsDead, "种子复活不死亡");
            Assert.AreEqual(0.5f, w.Sanity, 0.001f, "复活理智 0.5");
            Assert.AreEqual(3f, w.ASC.GetAttributeValue(AttributeConstants.ErosionLimit), 0.001f, "复活侵蚀 3");
            Assert.AreEqual(4, w.CurrentLocationId, "复活在种子地点");
        }

        [Test]
        public void 生命之火_每回合初侵蚀减05()
        {
            var state = BuildWithWoodSpirit();
            var w = state.GetCharacterByPlayer(3);
            w.ASC.SetAttributeBaseValue(AttributeConstants.ErosionLimit, 3f);
            SkillSystem.Activate(w.Definition.Skills[2], w, w, state, 2); // 开启生命之火

            w.ASC.TickEffects(state);

            Assert.AreEqual(2.5f, w.ASC.GetAttributeValue(AttributeConstants.ErosionLimit), 0.001f, "侵蚀 -0.5");
            Assert.IsTrue(w.ASC.Tags.HasTag(GameplayTagConstants.LifeFire), "开关标签");
        }
    }
}
