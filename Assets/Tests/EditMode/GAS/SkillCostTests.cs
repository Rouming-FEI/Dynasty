using NUnit.Framework;
using Core;
using Core.GAS;
using Gameplay.Characters;

namespace Dynasty.Tests.GAS
{
    public class SkillCostTests
    {
        private static SkillDef MakeSkill(SkillCostType type, float amount, int chargeThreshold = 0)
            => new SkillDef
            {
                Id = 990,
                Cost = new SkillCost(type, amount),
                Activation = SkillActivationType.Active,
                Target = SkillTargetType.Self,
                ChargeThreshold = chargeThreshold
            };

        [Test]
        public void 理智消耗技能_理智不足不可施放()
        {
            var state = ScenarioBuilder.Create(2).WithAllAt(4).WithSanity(0, 1f).Build();
            var c = state.GetCharacterByPlayer(0);
            SkillDef s = MakeSkill(SkillCostType.Sanity, 2f);

            Assert.IsFalse(SkillSystem.CanActivate(s, c));
        }

        [Test]
        public void 理智消耗技能_足够时扣理智()
        {
            var state = ScenarioBuilder.Create(2).WithAllAt(4).WithSanity(0, 2.5f).Build();
            var c = state.GetCharacterByPlayer(0);
            SkillDef s = MakeSkill(SkillCostType.Sanity, 1.5f);

            Assert.IsTrue(SkillSystem.CanActivate(s, c));
            SkillSystem.Activate(s, c, c, state);
            Assert.AreEqual(1f, c.Sanity, 0.001f);
        }

        [Test]
        public void 侵蚀上限消耗技能_不足不可施放_足够则扣()
        {
            var state = ScenarioBuilder.Create(2).WithAllAt(4).Build();
            var c = state.GetCharacterByPlayer(0);
            // 守护者默认侵蚀上限 3
            SkillDef big = MakeSkill(SkillCostType.ErosionLimit, 4f);
            Assert.IsFalse(SkillSystem.CanActivate(big, c));

            SkillDef ok = MakeSkill(SkillCostType.ErosionLimit, 1f);
            Assert.IsTrue(SkillSystem.CanActivate(ok, c));
            SkillSystem.Activate(ok, c, c, state);
            Assert.AreEqual(2f, c.ASC.GetAttributeValue(AttributeConstants.ErosionLimit), 0.001f);
        }

        [Test]
        public void 充能消耗技能_充能不足不可施放_足够则清零()
        {
            var state = ScenarioBuilder.Create(2).WithAllAt(4).Build();
            var c = state.GetCharacterByPlayer(0);
            SkillDef s = MakeSkill(SkillCostType.Charge, 3f, chargeThreshold: 3);

            Assert.IsFalse(SkillSystem.CanActivate(s, c), "0 充能不可施放");
            c.ASC.AddGuardianCharge();
            c.ASC.AddGuardianCharge();
            c.ASC.AddGuardianCharge();
            Assert.IsTrue(SkillSystem.CanActivate(s, c));
            SkillSystem.Activate(s, c, c, state);
            Assert.AreEqual(0, c.ASC.GetGuardianCharge(), "消耗后充能清零");
        }

        [Test]
        public void 额外成本与前置条件_全部满足才可施放()
        {
            var state = ScenarioBuilder.Create(2).WithAllAt(4).WithAP(0, 1).WithSanity(0, 3f).Build();
            var c = state.GetCharacterByPlayer(0);
            var s = MakeSkill(SkillCostType.AP, 1f);
            s.ExtraCosts = new[] { new SkillCost(SkillCostType.Sanity, 0.5f) };
            s.Restrictions = new[] { new SkillRestriction(AttributeConstants.Sanity, 1.5f) };

            Assert.IsTrue(SkillSystem.CanActivate(s, c));

            var strict = MakeSkill(SkillCostType.AP, 1f);
            strict.Restrictions = new[] { new SkillRestriction(AttributeConstants.Sanity, 9f) };
            Assert.IsFalse(SkillSystem.CanActivate(strict, c), "前置条件不满足");
        }
    }
}
