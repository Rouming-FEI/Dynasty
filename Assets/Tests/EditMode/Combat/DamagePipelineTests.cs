using NUnit.Framework;
using Core;
using Core.GAS;
using Gameplay.Characters;

namespace Dynasty.Tests.Combat
{
    public class DamagePipelineTests
    {
        // 攻击者 playerId=0（守护者，Character.Id=1），目标 playerId=1（魑魅，Id=2）
        private static void Attack(WorldState state, int sourcePlayer, int targetPlayer, float dmg, DamageType type)
        {
            var s = state.GetCharacterByPlayer(sourcePlayer);
            var t = state.GetCharacterByPlayer(targetPlayer);
            DamagePipeline.ApplyDamage(state, s.Id, t.Id, dmg, type);
        }

        [Test]
        public void 物理伤害_受物理防御减免()
        {
            var state = ScenarioBuilder.Create(2)
                .WithAllAt(4)
                .WithAttribute(1, AttributeConstants.PhysicalDefense, 0.5f)
                .WithSanity(1, 3f)
                .Build();

            Attack(state, 0, 1, 2f, DamageType.Physical);

            Assert.AreEqual(1.5f, state.GetCharacterByPlayer(1).Sanity, 0.001f); // 2 - 0.5
        }

        [Test]
        public void 法术伤害_受法术防御减免()
        {
            var state = ScenarioBuilder.Create(2)
                .WithAllAt(4)
                .WithAttribute(1, AttributeConstants.MagicDefense, 1f)
                .WithSanity(1, 3f)
                .Build();

            Attack(state, 0, 1, 2f, DamageType.Magic);

            Assert.AreEqual(2f, state.GetCharacterByPlayer(1).Sanity, 0.001f); // 2 - 1
        }

        [Test]
        public void 效果伤害_无减免()
        {
            var state = ScenarioBuilder.Create(2)
                .WithAllAt(4)
                .WithAttribute(1, AttributeConstants.PhysicalDefense, 1f)
                .WithAttribute(1, AttributeConstants.MagicDefense, 1f)
                .WithSanity(1, 3f)
                .Build();

            Attack(state, 0, 1, 2f, DamageType.Effect);

            Assert.AreEqual(1f, state.GetCharacterByPlayer(1).Sanity, 0.001f); // 无视防御
        }

        [Test]
        public void 真实伤害_无视免疫和护盾()
        {
            var state = ScenarioBuilder.Create(2)
                .WithAllAt(4)
                .WithSanity(1, 3f)
                .WithTag(1, GameplayTagConstants.ImmuneDamage)
                .WithTag(1, GameplayTagConstants.HasShield)
                .Build();

            Attack(state, 0, 1, 2f, DamageType.True);

            var t = state.GetCharacterByPlayer(1);
            Assert.AreEqual(1f, t.Sanity, 0.001f); // 真伤直接扣血
            Assert.IsTrue(t.ASC.Tags.HasTag(GameplayTagConstants.HasShield), "真伤不破盾");
        }

        [Test]
        public void 普通伤害_被护盾吸收并回血()
        {
            var state = ScenarioBuilder.Create(2)
                .WithAllAt(4)
                .WithSanity(1, 3f)
                .WithTag(1, GameplayTagConstants.HasShield)
                .Build();

            Attack(state, 0, 1, 2f, DamageType.Physical);

            var t = state.GetCharacterByPlayer(1);
            Assert.AreEqual(3f, t.Sanity, 0.001f); // 未扣血
            Assert.IsFalse(t.ASC.Tags.HasTag(GameplayTagConstants.HasShield), "护盾已破");
        }

        [Test]
        public void 物理免疫标签_完全免疫物理伤害()
        {
            var state = ScenarioBuilder.Create(2)
                .WithAllAt(4)
                .WithSanity(1, 3f)
                .WithTag(1, GameplayTagConstants.ImmunePhysical)
                .Build();

            Attack(state, 0, 1, 2f, DamageType.Physical);

            Assert.AreEqual(3f, state.GetCharacterByPlayer(1).Sanity, 0.001f);
        }

        [Test]
        public void 死亡豁免_伤害大于等于理智时理智置05并移除标签()
        {
            var state = ScenarioBuilder.Create(2)
                .WithAllAt(4)
                .WithSanity(1, 1f)
                .WithTag(1, GameplayTagConstants.DeathSaveActive)
                .Build();

            Attack(state, 0, 1, 2f, DamageType.Physical);

            var t = state.GetCharacterByPlayer(1);
            Assert.AreEqual(0.5f, t.Sanity, 0.001f);
            Assert.IsFalse(t.ASC.Tags.HasTag(GameplayTagConstants.DeathSaveActive), "死亡豁免只触发一次");
        }

        [Test]
        public void 致死伤害_入队死亡命令()
        {
            var state = ScenarioBuilder.Create(2)
                .WithAllAt(4)
                .WithSanity(1, 0.5f)
                .Build();

            Attack(state, 0, 1, 1f, DamageType.Physical);

            Assert.Greater(state.Commands.Count, 0, "致死应入队 DeathCommand");
        }

        [Test]
        public void 护盾破盾回血_从3恢复到满血上限()
        {
            // 护盾吸收伤害后回血 1：2.5 → 3（钳制在 MaxSanity）
            var state = ScenarioBuilder.Create(2)
                .WithAllAt(4)
                .WithSanity(1, 2.5f)
                .WithTag(1, GameplayTagConstants.HasShield)
                .Build();

            Attack(state, 0, 1, 1f, DamageType.Physical);

            Assert.AreEqual(3f, state.GetCharacterByPlayer(1).Sanity, 0.001f); // 2.5 + 1 → 钳到 3
        }
    }
}
