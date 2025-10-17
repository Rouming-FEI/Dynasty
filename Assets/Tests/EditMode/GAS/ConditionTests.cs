using NUnit.Framework;
using Core;
using Core.GAS;

namespace Dynasty.Tests.GAS
{
    public class ConditionTests
    {
        private static GameplayEffect ConditionalGE()
            => new GameplayEffect
            {
                Id = 950,
                DurationType = GameplayEffectDurationType.HasDuration,
                DurationTurns = 999,
                ExpiryPolicy = GameplayEffectExpiryPolicy.RemoveOnCondition,
                ConditionTags = new[] { GameplayTagConstants.ThornPotionActive }, // 条件标签存在才保留
                Modifiers = new[]
                {
                    new AttributeModifier(AttributeConstants.PhysicalDefense, AttributeModifierOp.Add, 0.5f)
                }
            };

        [Test]
        public void 条件标签存在时_GE保持()
        {
            var state = ScenarioBuilder.Create(2).WithAllAt(4)
                .WithTag(0, GameplayTagConstants.ThornPotionActive)
                .Build();
            var c = state.GetCharacterByPlayer(0);
            var ge = ConditionalGE();
            var spec = c.ASC.RentSpec();
            spec.Definition = ge;
            spec.SourceId = c.Id;
            spec.TargetId = c.Id;
            c.ASC.ApplyGameplayEffect(spec);

            c.ASC.TickEffects(state);

            Assert.AreEqual(1, c.ASC.ActiveEffectCount, "条件满足 GE 不消失");
            Assert.AreEqual(0.5f, c.ASC.GetAttributeValue(AttributeConstants.PhysicalDefense), 0.001f);
        }

        [Test]
        public void 条件标签缺失时_Tick移除GE并重建属性()
        {
            var state = ScenarioBuilder.Create(2).WithAllAt(4).Build();
            var c = state.GetCharacterByPlayer(0);
            var ge = ConditionalGE();
            var spec = c.ASC.RentSpec();
            spec.Definition = ge;
            spec.SourceId = c.Id;
            spec.TargetId = c.Id;
            c.ASC.ApplyGameplayEffect(spec);

            c.ASC.TickEffects(state);

            Assert.AreEqual(0, c.ASC.ActiveEffectCount, "条件缺失 GE 被移除");
            Assert.AreEqual(0f, c.ASC.GetAttributeValue(AttributeConstants.PhysicalDefense), 0.001f, "移除后派生属性重建");
        }
    }
}
