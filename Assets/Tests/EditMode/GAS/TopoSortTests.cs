using NUnit.Framework;
using Core;
using Core.GAS;

namespace Dynasty.Tests.GAS
{
    public class TopoSortTests
    {
        // 依赖链：modifier B（Channel=2, Override 置 1.0）依赖 A（Channel=1, Add 0.5）先结算，
        // B 用 Override 会覆盖 A 的结果；若顺序反了（B 先 A 后），最终值 = 1.5 而非 1.0
        private static GameplayEffect DependentGE()
            => new GameplayEffect
            {
                Id = 940,
                DurationType = GameplayEffectDurationType.HasDuration,
                DurationTurns = 3,
                Modifiers = new[]
                {
                    new AttributeModifier(AttributeConstants.PhysicalDefense, AttributeModifierOp.Override, 1.0f, channel: 2, dependsOn: 1),
                    new AttributeModifier(AttributeConstants.PhysicalDefense, AttributeModifierOp.Add, 0.5f, channel: 1)
                }
            };

        [Test]
        public void 依赖声明的modifier_被依赖者先结算()
        {
            var state = ScenarioBuilder.Create(2).WithAllAt(4).WithActiveEffect(0, DependentGE()).Build();
            var c = state.GetCharacterByPlayer(0);

            c.ASC.TickEffects(state);

            // A(Add 0.5) 先 → B(Override 1.0) 后：最终 1.0
            // 若旧冒泡排序：Override 优先级高于 Add，B 先 → A 后：最终 1.5
            Assert.AreEqual(1.0f, c.ASC.GetAttributeValue(AttributeConstants.PhysicalDefense), 0.001f,
                "拓扑顺序：被依赖者先结算");
        }

        [Test]
        public void 无依赖声明时_沿用优先级排序()
        {
            var ge = new GameplayEffect
            {
                Id = 941,
                DurationType = GameplayEffectDurationType.HasDuration,
                DurationTurns = 3,
                Modifiers = new[]
                {
                    new AttributeModifier(AttributeConstants.PhysicalDefense, AttributeModifierOp.Override, 1.0f, channel: 1),
                    new AttributeModifier(AttributeConstants.PhysicalDefense, AttributeModifierOp.Add, 0.5f, channel: 2)
                }
            };
            var state = ScenarioBuilder.Create(2).WithAllAt(4).WithActiveEffect(0, ge).Build();
            var c = state.GetCharacterByPlayer(0);

            c.ASC.TickEffects(state);

            Assert.AreEqual(1.5f, c.ASC.GetAttributeValue(AttributeConstants.PhysicalDefense), 0.001f,
                "无依赖：Override 先于 Add（旧行为）");
        }

        [Test]
        public void 循环依赖_确定性裁决且不崩溃()
        {
            var ge = new GameplayEffect
            {
                Id = 942,
                DurationType = GameplayEffectDurationType.HasDuration,
                DurationTurns = 3,
                Modifiers = new[]
                {
                    new AttributeModifier(AttributeConstants.PhysicalDefense, AttributeModifierOp.Add, 0.5f, channel: 1, dependsOn: 2),
                    new AttributeModifier(AttributeConstants.PhysicalDefense, AttributeModifierOp.Add, 0.5f, channel: 2, dependsOn: 1)
                }
            };
            var s1 = ScenarioBuilder.Create(2).WithAllAt(4).WithActiveEffect(0, ge).Build();
            var s2 = ScenarioBuilder.Create(2).WithAllAt(4).WithActiveEffect(0, ge).Build();

            s1.GetCharacterByPlayer(0).ASC.TickEffects(s1);
            s2.GetCharacterByPlayer(0).ASC.TickEffects(s2);

            float v1 = s1.GetCharacterByPlayer(0).ASC.GetAttributeValue(AttributeConstants.PhysicalDefense);
            float v2 = s2.GetCharacterByPlayer(0).ASC.GetAttributeValue(AttributeConstants.PhysicalDefense);
            Assert.AreEqual(v1, v2, 0.001f, "循环裁决必须确定");
            Assert.AreEqual(1.0f, v1, 0.001f, "两 Add 都应用（按挂载顺序裁决断环）");
        }

        [Test]
        public void 无依赖默认频道节点与依赖节点混合_顺序正确()
        {
            // 挂载顺序：C(Override 5, DependsOn=1) → A(Add 0.1, Channel=0) → B(Add 0.2, Channel=1)
            // 正确拓扑：A(0.1) → B(0.2) → C(Override 5.0) = 5.0
            // bug 版（误减断环）：A → C(Override 5) → B(Add 0.2) = 5.2
            var ge = new GameplayEffect
            {
                Id = 943,
                DurationType = GameplayEffectDurationType.HasDuration,
                DurationTurns = 3,
                Modifiers = new[]
                {
                    new AttributeModifier(AttributeConstants.PhysicalDefense, AttributeModifierOp.Override, 5.0f, channel: 2, dependsOn: 1),
                    new AttributeModifier(AttributeConstants.PhysicalDefense, AttributeModifierOp.Add, 0.1f, channel: 0),
                    new AttributeModifier(AttributeConstants.PhysicalDefense, AttributeModifierOp.Add, 0.2f, channel: 1)
                }
            };
            var state = ScenarioBuilder.Create(2).WithAllAt(4).WithActiveEffect(0, ge).Build();
            state.GetCharacterByPlayer(0).ASC.TickEffects(state);

            Assert.AreEqual(5.0f, state.GetCharacterByPlayer(0).ASC.GetAttributeValue(AttributeConstants.PhysicalDefense), 0.001f);
        }
    }
}
