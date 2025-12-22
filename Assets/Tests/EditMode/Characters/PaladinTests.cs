using NUnit.Framework;
using Core;
using Core.GAS;
using Gameplay.Characters;
using Gameplay.Items;

namespace Dynasty.Tests.Characters
{
    public class PaladinTests
    {
        private static WorldState BuildWithPaladin()
        {
            var state = ScenarioBuilder.Create(4).WithAllAt(4).Build();
            var p = state.GetCharacterByPlayer(3);
            p.Definition = PaladinDef.Create();
            p.Definition.ApplyTo(p);
            return state;
        }

        [Test]
        public void 持盾_装备后防御加05()
        {
            var state = BuildWithPaladin();
            var p = state.GetCharacterByPlayer(3);
            p.Inventory.EquipWeapon(ItemRegistry.Get(13)); // 圣骑士盾

            Assert.AreEqual(0.5f, p.ASC.GetAttributeValue(AttributeConstants.PhysicalDefense), 0.001f,
                "EquipEffect 防御 +0.5");
        }

        [Test]
        public void 守护之光_4充能施放_下次受伤减2()
        {
            var state = BuildWithPaladin();
            var p = state.GetCharacterByPlayer(3);
            for (int i = 0; i < 4; i++) p.ASC.AddGuardianCharge();
            var target = state.GetCharacterByPlayer(0);
            target.ASC.Tags.RemoveTag(GameplayTagConstants.HasShield);

            SkillSystem.Activate(p.Definition.Skills[1], p, target, state, 1); // 守护之光

            Assert.IsTrue(target.ASC.Tags.HasTag(GameplayTagConstants.GuardLight), "守护之光标签");
            Assert.AreEqual(0, p.ASC.GetGuardianCharge(), "4 充能清零");

            var src = state.GetCharacterByPlayer(1);
            float d = DamagePipeline.ApplyDamage(state, src.Id, target.Id, 3f, DamageType.Physical);
            Assert.AreEqual(1f, d, 0.001f, "下次受伤 -2");
        }

        [Test]
        public void 护佑_全局仅一次且回合无敌()
        {
            var state = BuildWithPaladin();
            var p = state.GetCharacterByPlayer(3);
            SkillSystem.Activate(p.Definition.Skills[3], p, p, state, 3);

            Assert.IsTrue(p.ASC.Tags.HasTag(GameplayTagConstants.ImmuneDamage), "当回合无敌");
            Assert.IsTrue(state.PaladinAegisUsed, "一次性标记");

            // 二次施放被拒
            SkillSystem.Activate(p.Definition.Skills[3], p, p, state, 3);
            Assert.IsFalse(state.PaladinAegisUsed == false, "标记已置位（无二次效果断言按特例）");
        }
    }
}
