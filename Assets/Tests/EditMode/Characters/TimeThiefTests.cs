using NUnit.Framework;
using Core;
using Core.GAS;
using Gameplay.Characters;

namespace Dynasty.Tests.Characters
{
    public class TimeThiefTests
    {
        private static WorldState BuildWithTimeThief()
        {
            var state = ScenarioBuilder.Create(4).WithAllAt(4).Build();
            var t = state.GetCharacterByPlayer(3);
            t.Definition = TimeThiefDef.Create();
            t.Definition.ApplyTo(t);
            return state;
        }

        [Test]
        public void 静止时空_理智不足15不可施放()
        {
            var state = BuildWithTimeThief();
            var t = state.GetCharacterByPlayer(3);
            t.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 1.4f);
            SkillDef skill = t.Definition.Skills[0];

            Assert.IsFalse(SkillSystem.CanActivate(skill, t));
        }

        [Test]
        public void 静止时空_开启后理智扣至05且挂载2回合GE()
        {
            var state = BuildWithTimeThief();
            var t = state.GetCharacterByPlayer(3);
            t.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
            SkillDef skill = t.Definition.Skills[0];

            SkillSystem.Activate(skill, t, t, state, 0);

            Assert.AreEqual(0.5f, t.Sanity, 0.001f, "理智扣至 0.5");
            Assert.IsTrue(t.ASC.Tags.HasTag(GameplayTagConstants.TimeStop), "挂载标签");
        }

        [Test]
        public void 静止时空_判定AP翻倍且至少1()
        {
            var state = BuildWithTimeThief();
            var t = state.GetCharacterByPlayer(3);
            t.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
            SkillDef skill = t.Definition.Skills[0];
            SkillSystem.Activate(skill, t, t, state, 0);

            // 直接走 AP 分发钩子：APGained=1 → 2；APGained=0 → 1（保底）
            t.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 1);
            TimeStopHook.ApplyApBonus(t, 1);
            Assert.AreEqual(2, t.ActionPoints, "1×2=2");

            t.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 0);
            TimeStopHook.ApplyApBonus(t, 0);
            Assert.AreEqual(1, t.ActionPoints, "0 保底 1");
        }
    }
}
