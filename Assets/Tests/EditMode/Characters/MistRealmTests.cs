using NUnit.Framework;
using Core;
using Core.GAS;
using Gameplay.Characters;

namespace Dynasty.Tests.Characters
{
    public class MistRealmTests
    {
        // 魑魅 = player 1；开启迷离之境后攻击判定
        private static WorldState BuildMistRealm()
        {
            var state = ScenarioBuilder.Create(2).WithAllAt(4).Build();
            var c = state.GetCharacterByPlayer(1);
            c.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 3);
            SkillSystem.Activate(c.Definition.Skills[0], c, c, state, 0); // 迷离之境
            return state;
        }

        [Test]
        public void 迷离之境_攻击判定两态且确定性()
        {
            var s1 = BuildMistRealm();
            var s2 = BuildMistRealm();
            var c1 = s1.GetCharacterByPlayer(1);
            var c2 = s2.GetCharacterByPlayer(1);
            var t1 = s1.GetCharacterByPlayer(0);
            var t2 = s2.GetCharacterByPlayer(0);
            t1.ASC.Tags.RemoveTag(GameplayTagConstants.HasShield);
            t2.ASC.Tags.RemoveTag(GameplayTagConstants.HasShield);

            float d1 = DamagePipeline.ApplyDamage(s1, c1.Id, t1.Id, 1f, DamageType.Physical);
            float d2 = DamagePipeline.ApplyDamage(s2, c2.Id, t2.Id, 1f, DamageType.Physical);

            Assert.AreEqual(d1, d2, 0.001f, "同 seed 确定性");
            Assert.IsTrue(d1 == 1f || d1 == 1.5f, $"胜 1.5 / 败 1.0，实际 {d1}");
        }

        [Test]
        public void 心雾一层_受到物理伤害增加05()
        {
            var state = BuildMistRealm();
            var c = state.GetCharacterByPlayer(1);
            c.ASC.AddMistLayer(); // 心雾 1
            var src = state.GetCharacterByPlayer(0);

            float d = DamagePipeline.ApplyDamage(state, src.Id, c.Id, 2f, DamageType.Physical);

            Assert.AreEqual(2.5f, d, 0.001f, "一层：物理伤 +0.5");
        }

        [Test]
        public void 心雾两层_每回合初扣05理智不致死()
        {
            var state = BuildMistRealm();
            var c = state.GetCharacterByPlayer(1);
            c.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 1f);
            c.ASC.AddMistLayer();
            c.ASC.AddMistLayer();

            c.ASC.TickEffects(state); // 回合末触发周期负载

            Assert.AreEqual(0.5f, c.Sanity, 0.001f, "两层：-0.5 不致死");
        }

        [Test]
        public void 心雾三层_扣1理智并退出迷离()
        {
            var state = BuildMistRealm();
            var c = state.GetCharacterByPlayer(1);
            c.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
            c.ASC.AddMistLayer();
            c.ASC.AddMistLayer();
            c.ASC.AddMistLayer();

            c.ASC.TickEffects(state);

            Assert.AreEqual(2f, c.Sanity, 0.001f, "三层：-1");
            Assert.IsFalse(c.ASC.Tags.HasTag(GameplayTagConstants.MistState), "退出迷离");
            Assert.AreEqual(0, c.ASC.GetMistLayers(), "层数清零");
        }
    }
}
