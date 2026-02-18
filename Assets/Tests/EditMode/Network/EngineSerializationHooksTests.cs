using Core;
using Core.GAS;
using Gameplay.Characters;
using NUnit.Framework;

namespace Network.Tests
{
    public class EngineSerializationHooksTests
    {
        [Test]
        public void DeterministicRandom_RestoreState_续流一致()
        {
            var a = new DeterministicRandom(42);
            a.Next(100); a.Next(100); a.Next(100);
            ulong state = a.State;
            long expected = a.NextLong();

            var b = new DeterministicRandom(42);
            b.RestoreState(state);
            Assert.That(b.NextLong(), Is.EqualTo(expected));
        }

        [Test]
        public void TagContainer_RestoreMask_重建掩码()
        {
            var t = new GameplayTagContainer();
            t.AddTag(GameplayTagConstants.Dead);
            t.AddTag(GameplayTagConstants.HasShield);
            long mask = t.Mask;

            var t2 = new GameplayTagContainer();
            t2.RestoreMask(mask);
            Assert.That(t2.HasTag(GameplayTagConstants.Dead), Is.True);
            Assert.That(t2.HasTag(GameplayTagConstants.HasShield), Is.True);
        }

        [Test]
        public void Asc_MistLayers_可精确恢复()
        {
            var asc = new AbilitySystemComponent(1);
            asc.AddMistLayer(); asc.AddMistLayer(); asc.AddMistLayer();
            asc.SetMistLayers(1);
            Assert.That(asc.GetMistLayers(), Is.EqualTo(1));
        }

        [Test]
        public void Asc_PassiveTimers_注册后可恢复()
        {
            var asc = new AbilitySystemComponent(1);
            var def = ListenerDef.Create();          // 倾听者：周期被动 Interval=2
            for (int i = 0; i < def.Skills.Length; i++) asc.RegisterPassive(def.Skills[i]);
            asc.RestorePassiveTimers(new[] { 1 });   // 覆盖第 0 个计时器为 1
            Assert.That(asc.GetPassiveTimer(0), Is.EqualTo(1));
        }

        [Test]
        public void Asc_ActiveEffect_可枚举与恢复()
        {
            var asc = new AbilitySystemComponent(1);
            asc.SetAttributeSet(new DefaultAttributeSet());
            GameplayEffectSpec spec = asc.RentSpec();
            spec.Definition = new GameplayEffect { Id = 999, DurationType = GameplayEffectDurationType.HasDuration, DurationTurns = 5 };
            spec.RemainingTurns = 5;
            asc.ApplyGameplayEffect(spec);

            GameplayEffectSpec restored = asc.GetActiveEffect(0);
            Assert.That(asc.ActiveEffectCount, Is.EqualTo(1));
            Assert.That(restored.Definition.Id, Is.EqualTo(999));
            Assert.That(restored.RemainingTurns, Is.EqualTo(5));
        }

        [Test]
        public void CharacterDefCatalog_覆盖13角色()
        {
            for (int id = 1; id <= 13; id++)
            {
                CharacterDef def = CharacterDefCatalog.Get(id);
                Assert.That(def, Is.Not.Null, $"def {id}");
                Assert.That(def.Id, Is.EqualTo(id));
            }
        }

        [Test]
        public void TurnManager_快照访问器_往返()
        {
            var state = new WorldState(4, 42);
            state.TurnManager = new TurnManager();
            state.TurnManager.RestoreConsecutiveFlatCount(2);
            Assert.That(state.TurnManager.ConsecutiveFlatCount, Is.EqualTo(2));
            state.TurnManager.RestoreActionOrder(new[] { 3, 1, 0, 2 });
            Assert.That(state.TurnManager.ActionOrder, Is.EqualTo(new[] { 3, 1, 0, 2 }));
            state.TurnManager.RestoreActorIndex(2);
            Assert.That(state.TurnManager.CurrentActorIndex, Is.EqualTo(2));
        }
    }
}
