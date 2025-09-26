using NUnit.Framework;
using Core;
using Core.GAS;

namespace Dynasty.Tests
{
    public class ScenarioBuilderTests
    {
        [Test]
        public void 默认三人局_角色齐全且处于行动阶段()
        {
            var state = ScenarioBuilder.Create(3).Build();

            Assert.AreEqual(3, state.PlayerCount);
            Assert.AreEqual(TurnPhase.ActionPhase, state.Phase);
            Assert.AreEqual("守护者", state.GetCharacterByPlayer(0).Name);
            Assert.AreEqual("魑魅", state.GetCharacterByPlayer(1).Name);
            Assert.AreEqual("普通人", state.GetCharacterByPlayer(2).Name);
        }

        [Test]
        public void WithAllAt_把所有人放到同一地点()
        {
            var state = ScenarioBuilder.Create(3).WithAllAt(4).Build();

            for (int i = 0; i < 3; i++)
                Assert.AreEqual(4, state.GetCharacterByPlayer(i).CurrentLocationId);
        }

        [Test]
        public void WithSanity与WithAP_直接注入属性()
        {
            var state = ScenarioBuilder.Create(2)
                .WithSanity(0, 1.5f)
                .WithAP(0, 3)
                .Build();

            var c = state.GetCharacterByPlayer(0);
            Assert.AreEqual(1.5f, c.Sanity, 0.001f);
            Assert.AreEqual(3, c.ActionPoints);
        }

        [Test]
        public void WithGuardianCharge_一步攒满充能()
        {
            var state = ScenarioBuilder.Create(2).WithGuardianCharge(0, 3).Build();

            Assert.AreEqual(3, state.GetCharacterByPlayer(0).ASC.GetGuardianCharge());
        }

        [Test]
        public void WithClearInventory_清空默认发放的道具()
        {
            var state = ScenarioBuilder.Create(2).WithClearInventory(0).Build();

            var inv = state.GetCharacterByPlayer(0).Inventory;
            Assert.AreEqual(0, inv.Consumables.Count);
            Assert.IsNull(inv.EquippedWeapon);
            Assert.AreEqual(0, inv.EquippedWeaponAmmo);
        }

        [Test]
        public void WithEquippedWeapon_装备枪并设置弹药()
        {
            var state = ScenarioBuilder.Create(2)
                .WithClearInventory(0)
                .WithEquippedWeapon(0, 3, ammo: 1)
                .Build();

            var inv = state.GetCharacterByPlayer(0).Inventory;
            Assert.AreEqual(3, inv.EquippedWeapon.Id);
            Assert.AreEqual(1, inv.EquippedWeaponAmmo);
        }

        [Test]
        public void WithTag_注入标签()
        {
            var state = ScenarioBuilder.Create(2)
                .WithTag(1, GameplayTagConstants.HideoutHidden)
                .Build();

            Assert.IsTrue(state.GetCharacterByPlayer(1).ASC.Tags
                .HasTag(GameplayTagConstants.HideoutHidden));
        }

        [Test]
        public void WithActiveEffect_挂载持续型GE()
        {
            var ge = new GameplayEffect
            {
                Id = 900,
                DurationType = GameplayEffectDurationType.HasDuration,
                DurationTurns = 3,
                Modifiers = new[]
                {
                    new AttributeModifier(AttributeConstants.AttackBonus, AttributeModifierOp.Add, 1f)
                }
            };

            var state = ScenarioBuilder.Create(2).WithActiveEffect(0, ge).Build();

            Assert.AreEqual(1, state.GetCharacterByPlayer(0).ASC.ActiveEffectCount);
        }

        [Test]
        public void 相同种子_两次构建的状态哈希一致()
        {
            var a = ScenarioBuilder.Create(3, seed: 99).WithAllAt(4).Build();
            var b = ScenarioBuilder.Create(3, seed: 99).WithAllAt(4).Build();

            Assert.AreEqual(a.ComputeStateHash(), b.ComputeStateHash());
        }
    }
}
