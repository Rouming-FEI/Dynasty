using NUnit.Framework;
using Core;
using Core.GAS;
using Gameplay.Characters;

namespace Dynasty.Tests.Characters
{
    public class PassiveCounterTests
    {
        // 构造一个带周期被动（每 2 回合给自己加 0.5 攻击）的角色定义，
        // 直接操作 ASC 计时器验证框架（角色定义整体校验见 Task 9 回归）。
        private static SkillDef EveryTwoTurns()
            => new SkillDef
            {
                Id = 960,
                Name = "测试周期被动",
                Activation = SkillActivationType.Passive,
                PassiveInterval = 2,
                PassiveEffects = new[]
                {
                    new GameplayEffect
                    {
                        Id = 961,
                        DurationType = GameplayEffectDurationType.Instant,
                        Modifiers = new[]
                        {
                            new AttributeModifier(AttributeConstants.AttackBonus, AttributeModifierOp.Add, 0.5f)
                        }
                    }
                }
            };

        [Test]
        public void 周期被动_每两回合触发一次()
        {
            var state = ScenarioBuilder.Create(2).WithAllAt(4).Build();
            var c = state.GetCharacterByPlayer(0);
            SkillDef passive = EveryTwoTurns();
            c.ASC.RegisterPassive(passive);

            c.ASC.TickEffects(state); // 第 1 回合
            Assert.AreEqual(0f, c.ASC.GetAttributeValue(AttributeConstants.AttackBonus), 0.001f, "未到期不触发");

            c.ASC.TickEffects(state); // 第 2 回合
            Assert.AreEqual(0.5f, c.ASC.GetAttributeValue(AttributeConstants.AttackBonus), 0.001f, "到期触发一次");
        }

        [Test]
        public void 死亡角色_被动计时暂停()
        {
            var state = ScenarioBuilder.Create(2).WithAllAt(4).Build();
            var c = state.GetCharacterByPlayer(0);
            c.ASC.RegisterPassive(EveryTwoTurns());
            c.ASC.Tags.AddTag(GameplayTagConstants.Dead);

            c.ASC.TickEffects(state);
            c.ASC.TickEffects(state);

            Assert.AreEqual(0f, c.ASC.GetAttributeValue(AttributeConstants.AttackBonus), 0.001f, "Dead 跳过被动");
        }
    }
}
