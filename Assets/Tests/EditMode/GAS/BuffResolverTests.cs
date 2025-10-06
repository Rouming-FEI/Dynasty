using NUnit.Framework;
using Core;
using Core.GAS;

namespace Dynasty.Tests.GAS
{
    public class BuffResolverTests
    {
        private static GameplayEffect DefenseBuff(float magnitude)
        {
            return new GameplayEffect
            {
                Id = 900,
                DurationType = GameplayEffectDurationType.HasDuration,
                DurationTurns = 3,
                Modifiers = new[]
                {
                    new AttributeModifier(AttributeConstants.PhysicalDefense, AttributeModifierOp.Add, magnitude)
                }
            };
        }

        [Test]
        public void 挂载时立即应用_防御增加()
        {
            var state = ScenarioBuilder.Create(2)
                .WithAllAt(4)
                .WithActiveEffect(0, DefenseBuff(0.5f))
                .Build();

            Assert.AreEqual(0.5f,
                state.GetCharacterByPlayer(0).ASC.GetAttributeValue(AttributeConstants.PhysicalDefense),
                0.001f);
        }

        [Test]
        public void 回合末结算后_持续GE加成保持()
        {
            var state = ScenarioBuilder.Create(2)
                .WithAllAt(4)
                .WithActiveEffect(0, DefenseBuff(0.5f))
                .Build();
            var c = state.GetCharacterByPlayer(0);

            c.ASC.TickEffects(state); // 模拟回合末

            Assert.AreEqual(0.5f,
                c.ASC.GetAttributeValue(AttributeConstants.PhysicalDefense),
                0.001f, "Tick 后派生属性由 BuffResolver 重建，加成应保持");
        }

        [Test]
        public void 持续3回合后过期_防御回基础值()
        {
            var state = ScenarioBuilder.Create(2)
                .WithAllAt(4)
                .WithActiveEffect(0, DefenseBuff(0.5f))
                .Build();
            var c = state.GetCharacterByPlayer(0);

            c.ASC.TickEffects(state); // 剩 2 回合
            c.ASC.TickEffects(state); // 剩 1 回合
            c.ASC.TickEffects(state); // 过期移除

            Assert.AreEqual(0f,
                c.ASC.GetAttributeValue(AttributeConstants.PhysicalDefense),
                0.001f, "GE 过期后派生属性应清回基础值");
        }

        [Test]
        public void 多个持续GE_加成按规则叠加()
        {
            var state = ScenarioBuilder.Create(2)
                .WithAllAt(4)
                .WithActiveEffect(0, DefenseBuff(0.5f))
                .WithActiveEffect(0, DefenseBuff(0.3f))
                .Build();
            var c = state.GetCharacterByPlayer(0);

            c.ASC.TickEffects(state);

            Assert.AreEqual(0.8f,
                c.ASC.GetAttributeValue(AttributeConstants.PhysicalDefense),
                0.001f, "两个 Add GE 应叠加为 0.8");
        }

        [Test]
        public void 攻击加成GE_仅作用于AttackBonus不碰理智()
        {
            var atkBuff = new GameplayEffect
            {
                Id = 901,
                DurationType = GameplayEffectDurationType.HasDuration,
                DurationTurns = 2,
                Modifiers = new[]
                {
                    new AttributeModifier(AttributeConstants.AttackBonus, AttributeModifierOp.Add, 1f)
                }
            };
            var state = ScenarioBuilder.Create(2)
                .WithAllAt(4)
                .WithActiveEffect(0, atkBuff)
                .Build();
            var c = state.GetCharacterByPlayer(0);
            float sanityBefore = c.Sanity;

            c.ASC.TickEffects(state);

            Assert.AreEqual(1f,
                c.ASC.GetAttributeValue(AttributeConstants.AttackBonus),
                0.001f);
            Assert.AreEqual(sanityBefore, c.Sanity, 0.001f, "持久资源不应被 Resolve 触碰");
        }
    }
}
