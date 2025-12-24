using NUnit.Framework;
using Core;
using Core.GAS;
using Gameplay.Characters;

namespace Dynasty.Tests.Characters
{
    public class AgentTests
    {
        private static WorldState BuildWithAgent()
        {
            var state = ScenarioBuilder.Create(4).WithAllAt(4).Build();
            var a = state.GetCharacterByPlayer(3);
            a.Definition = AgentDef.Create();
            a.Definition.ApplyTo(a);
            for (int s = 0; s < a.Definition.Skills.Length; s++)
                a.ASC.RegisterPassive(a.Definition.Skills[s]);
            return state;
        }

        [Test]
        public void 孤狼_每2回合回复05理智()
        {
            var state = BuildWithAgent();
            var a = state.GetCharacterByPlayer(3);
            a.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 1f);

            a.ASC.TickEffects(state);
            Assert.AreEqual(1f, a.Sanity, 0.001f, "第 1 回合不触发");
            a.ASC.TickEffects(state);
            Assert.AreEqual(1.5f, a.Sanity, 0.001f, "第 2 回合 +0.5");
        }

        [Test]
        public void 主的意志_每3回合无伤害则本能加1()
        {
            var state = BuildWithAgent();
            var a = state.GetCharacterByPlayer(3);
            a.ASC.SetAttributeBaseValue(AttributeConstants.ErosionLimit, 3f);

            a.ASC.TickEffects(state); // 回合 1
            a.ASC.TickEffects(state); // 回合 2
            a.ASC.TickEffects(state); // 回合 3：无伤害 → 本能 +1

            Assert.AreEqual(1, a.ASC.GetInstinct(), "本能 1");
            Assert.AreEqual(2.5f, a.ASC.GetAttributeValue(AttributeConstants.ErosionLimit), 0.001f, "侵蚀 -0.5");
            Assert.AreEqual(0.5f, a.ASC.GetAttributeValue(AttributeConstants.PhysicalDefense), 0.001f, "防御 +0.5");
        }

        [Test]
        public void 主的庇护_致命伤回05理智且本能加2()
        {
            var state = BuildWithAgent();
            var a = state.GetCharacterByPlayer(3);
            a.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 0.5f);

            state.Commands.Enqueue(new Gameplay.Combat.DeathCommand(0, 3, 0));
            state.Commands.ExecuteAll(state);
            state.Commands.Clear();

            Assert.IsFalse(a.IsDead, "庇护免死");
            Assert.AreEqual(0.5f, a.Sanity, 0.001f, "理智 0.5");
            Assert.AreEqual(2, a.ASC.GetInstinct(), "本能 +2");
        }
    }
}
