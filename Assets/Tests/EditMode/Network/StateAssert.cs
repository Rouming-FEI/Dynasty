using Core;
using Core.GAS;
using Gameplay.Characters;
using Gameplay.Locations;
using Gameplay.Movement;
using NUnit.Framework;

namespace Dynasty.Tests.Network
{
    /// <summary>
    /// WorldState 逐字段相等断言（SnapshotTests 与 DeltaTests 共用）。
    /// ComputeStateHash 只覆盖核心字段，此处补齐全部序列化字段
    /// （LastAllocations/HasJudgmentResult/LastJudgment/TurnManager 三字段/Random.Seed 等），
    /// 保证序列化与 delta 应用不漏拷字段也能被测试发现。
    /// </summary>
    public static class StateAssert
    {
        public static void AssertEqual(WorldState expected, WorldState actual)
        {
            Assert.That(actual.PlayerCount, Is.EqualTo(expected.PlayerCount));
            Assert.That(actual.CurrentTick, Is.EqualTo(expected.CurrentTick));
            Assert.That(actual.CurrentTurn, Is.EqualTo(expected.CurrentTurn));
            Assert.That(actual.Phase, Is.EqualTo(expected.Phase));
            Assert.That(actual.Random.Seed, Is.EqualTo(expected.Random.Seed), "Random.Seed");
            Assert.That(actual.Random.State, Is.EqualTo(expected.Random.State), "Random.State");
            Assert.That(actual.ThornPotionTurnsRemaining, Is.EqualTo(expected.ThornPotionTurnsRemaining));
            Assert.That(actual.WoodSeedPlayer, Is.EqualTo(expected.WoodSeedPlayer));
            Assert.That(actual.WoodSeedLocation, Is.EqualTo(expected.WoodSeedLocation));
            Assert.That(actual.PaladinAegisUsed, Is.EqualTo(expected.PaladinAegisUsed));
            Assert.That(actual.GhostCloneActive, Is.EqualTo(expected.GhostCloneActive), "GhostCloneActive");
            Assert.That(actual.GhostCloneSanity, Is.EqualTo(expected.GhostCloneSanity), "GhostCloneSanity");
            Assert.That(actual.LocationTrapCounts, Is.EqualTo(expected.LocationTrapCounts));
            Assert.That(actual.MovedThisTurn, Is.EqualTo(expected.MovedThisTurn));

            for (int i = 0; i < expected.LastAllocations.Length; i++)
            {
                Assert.That(actual.LastAllocations[i].PlayerId, Is.EqualTo(expected.LastAllocations[i].PlayerId), $"alloc[{i}].pid");
                Assert.That(actual.LastAllocations[i].Rock, Is.EqualTo(expected.LastAllocations[i].Rock), $"alloc[{i}].rock");
                Assert.That(actual.LastAllocations[i].Scissors, Is.EqualTo(expected.LastAllocations[i].Scissors), $"alloc[{i}].scissors");
                Assert.That(actual.LastAllocations[i].Paper, Is.EqualTo(expected.LastAllocations[i].Paper), $"alloc[{i}].paper");
            }

            Assert.That(actual.HasJudgmentResult, Is.EqualTo(expected.HasJudgmentResult));
            if (expected.HasJudgmentResult)
            {
                Assert.That(actual.LastJudgment.WinnerHand, Is.EqualTo(expected.LastJudgment.WinnerHand));
                Assert.That(actual.LastJudgment.LoserHand, Is.EqualTo(expected.LastJudgment.LoserHand));
                Assert.That(actual.LastJudgment.NeutralHand, Is.EqualTo(expected.LastJudgment.NeutralHand));
                Assert.That(actual.LastJudgment.IsGlobalFlat, Is.EqualTo(expected.LastJudgment.IsGlobalFlat));
                Assert.That(actual.LastJudgment.WinnerCount, Is.EqualTo(expected.LastJudgment.WinnerCount));
                Assert.That(actual.LastJudgment.LoserCount, Is.EqualTo(expected.LastJudgment.LoserCount));
                Assert.That(actual.LastJudgment.Roles, Is.EqualTo(expected.LastJudgment.Roles));
                Assert.That(actual.LastJudgment.EffectiveChips, Is.EqualTo(expected.LastJudgment.EffectiveChips));
                Assert.That(actual.LastJudgment.APGained, Is.EqualTo(expected.LastJudgment.APGained));
            }

            Assert.That(actual.TurnManager, Is.Not.Null);
            Assert.That(actual.TurnManager.ConsecutiveFlatCount, Is.EqualTo(expected.TurnManager.ConsecutiveFlatCount));
            Assert.That(actual.TurnManager.ActionOrder, Is.EqualTo(expected.TurnManager.ActionOrder));
            Assert.That(actual.TurnManager.CurrentActorIndex, Is.EqualTo(expected.TurnManager.CurrentActorIndex));

            // 地点道具（分组）
            for (int i = 0; i < expected.Locations.LocationCount; i++)
            {
                LocatedItem[] itemsA = expected.Locations.GetLocatedItems(i);
                LocatedItem[] itemsB = actual.Locations.GetLocatedItems(i);
                Assert.That(itemsB.Length, Is.EqualTo(itemsA.Length), $"loc {i} 道具组数");
                for (int j = 0; j < itemsA.Length; j++)
                {
                    Assert.That(itemsB[j].ItemId, Is.EqualTo(itemsA[j].ItemId), $"loc {i} item {j}");
                    Assert.That(itemsB[j].Count, Is.EqualTo(itemsA[j].Count), $"loc {i} count {j}");
                }
            }

            // 地点规则
            Assert.That(actual.Locations.Rules.Length, Is.EqualTo(expected.Locations.Rules.Length));
            for (int i = 0; i < expected.Locations.Rules.Length; i++)
            {
                LocationRule ra = expected.Locations.Rules[i];
                LocationRule rb = actual.Locations.Rules[i];
                Assert.That(rb == null, Is.EqualTo(ra == null), $"rule {i} null 一致");
                if (ra == null) continue;
                Assert.That(rb.GetType(), Is.EqualTo(ra.GetType()), $"rule {i} 类型");
                Assert.That(rb.LocationId, Is.EqualTo(ra.LocationId), $"rule {i} LocationId");
                if (ra is LabRule la)
                {
                    var lb = (LabRule)rb;
                    Assert.That(lb.DoorOpen, Is.EqualTo(la.DoorOpen));
                    Assert.That(lb.SwitchOn, Is.EqualTo(la.SwitchOn));
                    Assert.That(lb.ThresholdRemoved, Is.EqualTo(la.ThresholdRemoved));
                    Assert.That(lb.TideTurnsElapsed, Is.EqualTo(la.TideTurnsElapsed));
                }
            }

            // 每角色
            for (int i = 0; i < expected.Characters.Length; i++)
            {
                Character ca = expected.Characters[i];
                Character cb = actual.Characters[i];
                Assert.That(cb == null, Is.EqualTo(ca == null), $"char {i} null 一致");
                if (ca == null) continue;
                Assert.That(cb.Id, Is.EqualTo(ca.Id), $"char {i} Id");
                Assert.That(cb.PlayerId, Is.EqualTo(ca.PlayerId), $"char {i} PlayerId");
                Assert.That(cb.Name, Is.EqualTo(ca.Name), $"char {i} Name");
                Assert.That(cb.CurrentLocationId, Is.EqualTo(ca.CurrentLocationId), $"char {i} Location");
                Assert.That(cb.Definition.Id, Is.EqualTo(ca.Definition.Id), $"char {i} DefId");
                Assert.That(cb.IsDead, Is.EqualTo(ca.IsDead), $"char {i} IsDead");
                Assert.That(cb.ASC.Tags.Mask, Is.EqualTo(ca.ASC.Tags.Mask), $"char {i} TagMask");
                Assert.That(cb.ASC.GetGuardianCharge(), Is.EqualTo(ca.ASC.GetGuardianCharge()), $"char {i} 充能");
                Assert.That(cb.ASC.GetMistLayers(), Is.EqualTo(ca.ASC.GetMistLayers()), $"char {i} 心雾");
                Assert.That(cb.ASC.GetBloodSeals(), Is.EqualTo(ca.ASC.GetBloodSeals()), $"char {i} 血印");
                Assert.That(cb.ASC.GetInstinct(), Is.EqualTo(ca.ASC.GetInstinct()), $"char {i} 本能");
                Assert.That(cb.ASC.GetLastDamageTurn(), Is.EqualTo(ca.ASC.GetLastDamageTurn()), $"char {i} 最近伤害回合");

                for (int aIdx = 1; aIdx <= AttributeConstants.Count; aIdx++)
                {
                    Assert.That(cb.ASC.GetAttributeValue(aIdx), Is.EqualTo(ca.ASC.GetAttributeValue(aIdx)),
                        $"char {i} attr {aIdx} current");
                    Assert.That(cb.ASC.GetAttributeBaseValue(aIdx), Is.EqualTo(ca.ASC.GetAttributeBaseValue(aIdx)),
                        $"char {i} attr {aIdx} base");
                }

                Assert.That(cb.ASC.ActiveEffectCount, Is.EqualTo(ca.ASC.ActiveEffectCount), $"char {i} GE 数");
                for (int e = 0; e < ca.ASC.ActiveEffectCount; e++)
                    AssertGE(ca.ASC.GetActiveEffect(e), cb.ASC.GetActiveEffect(e), $"char {i} GE {e}");

                Assert.That(cb.ASC.CooldownSlotCount, Is.EqualTo(ca.ASC.CooldownSlotCount), $"char {i} 冷却槽");
                for (int cd = 0; cd < ca.ASC.CooldownSlotCount; cd++)
                    Assert.That(cb.ASC.GetCooldown(cd), Is.EqualTo(ca.ASC.GetCooldown(cd)), $"char {i} 冷却 {cd}");

                Assert.That(cb.ASC.PassiveCount, Is.EqualTo(ca.ASC.PassiveCount), $"char {i} 被动数");
                for (int p = 0; p < ca.ASC.PassiveCount; p++)
                    Assert.That(cb.ASC.GetPassiveTimer(p), Is.EqualTo(ca.ASC.GetPassiveTimer(p)), $"char {i} 被动计时 {p}");

                Assert.That(cb.Inventory.EquippedWeapon?.Id ?? 0, Is.EqualTo(ca.Inventory.EquippedWeapon?.Id ?? 0), $"char {i} 武器");
                Assert.That(cb.Inventory.EquippedWeaponAmmo, Is.EqualTo(ca.Inventory.EquippedWeaponAmmo), $"char {i} 弹药");
                Assert.That(cb.Inventory.Consumables.Count, Is.EqualTo(ca.Inventory.Consumables.Count), $"char {i} 消耗品数");
                for (int ci = 0; ci < ca.Inventory.Consumables.Count; ci++)
                    Assert.That(cb.Inventory.Consumables[ci].Id, Is.EqualTo(ca.Inventory.Consumables[ci].Id), $"char {i} 消耗品 {ci}");
            }
        }

        private static void AssertGE(GameplayEffectSpec a, GameplayEffectSpec b, string what)
        {
            Assert.That(b.Definition.Id, Is.EqualTo(a.Definition.Id), $"{what} Def.Id");
            Assert.That(b.Definition.DurationType, Is.EqualTo(a.Definition.DurationType), $"{what} DurationType");
            Assert.That(b.Definition.DurationTurns, Is.EqualTo(a.Definition.DurationTurns), $"{what} DurationTurns");
            AssertModifiers(a.Definition.Modifiers, b.Definition.Modifiers, what);
            AssertTags(a.Definition.GrantTags, b.Definition.GrantTags, what + " GrantTags");
            AssertTags(a.Definition.RemoveTags, b.Definition.RemoveTags, what + " RemoveTags");
            AssertTags(a.Definition.ApplicationRequiredTags, b.Definition.ApplicationRequiredTags, what + " AppReq");
            AssertTags(a.Definition.ApplicationBlockedTags, b.Definition.ApplicationBlockedTags, what + " AppBlocked");
            Assert.That(b.Definition.Period.HasValue, Is.EqualTo(a.Definition.Period.HasValue), $"{what} Period.Has");
            if (a.Definition.Period.HasValue)
            {
                Assert.That(b.Definition.Period.Value.Interval, Is.EqualTo(a.Definition.Period.Value.Interval), $"{what} Period.Interval");
                Assert.That(b.Definition.Period.Value.ExecuteCount, Is.EqualTo(a.Definition.Period.Value.ExecuteCount), $"{what} Period.ExecuteCount");
            }
            Assert.That(b.Definition.Stacking, Is.EqualTo(a.Definition.Stacking), $"{what} Stacking");
            Assert.That(b.Definition.CooldownTurns, Is.EqualTo(a.Definition.CooldownTurns), $"{what} CooldownTurns");
            Assert.That(b.Definition.ExpiryPolicy, Is.EqualTo(a.Definition.ExpiryPolicy), $"{what} ExpiryPolicy");
            AssertTags(a.Definition.ConditionTags, b.Definition.ConditionTags, what + " ConditionTags");
            // spec 运行时
            Assert.That(b.RemainingTurns, Is.EqualTo(a.RemainingTurns), $"{what} RemainingTurns");
            Assert.That(b.MagnitudeMultiplier, Is.EqualTo(a.MagnitudeMultiplier), $"{what} MagnitudeMultiplier");
            Assert.That(b.SourceId, Is.EqualTo(a.SourceId), $"{what} SourceId");
            Assert.That(b.TargetId, Is.EqualTo(a.TargetId), $"{what} TargetId");
            Assert.That(b.PeriodTick, Is.EqualTo(a.PeriodTick), $"{what} PeriodTick");
            Assert.That(b.PeriodRunCount, Is.EqualTo(a.PeriodRunCount), $"{what} PeriodRunCount");
            Assert.That(b.StackCount, Is.EqualTo(a.StackCount), $"{what} StackCount");
            Assert.That(b.CooldownRemaining, Is.EqualTo(a.CooldownRemaining), $"{what} CooldownRemaining");
        }

        private static void AssertModifiers(AttributeModifier[] a, AttributeModifier[] b, string what)
        {
            Assert.That(b.Length, Is.EqualTo(a.Length), $"{what} 修饰器数");
            for (int i = 0; i < a.Length; i++)
            {
                Assert.That(b[i].AttributeId, Is.EqualTo(a[i].AttributeId), $"{what} mod {i} attr");
                Assert.That(b[i].Op, Is.EqualTo(a[i].Op), $"{what} mod {i} op");
                Assert.That(b[i].Magnitude, Is.EqualTo(a[i].Magnitude), $"{what} mod {i} mag");
                Assert.That(b[i].Channel, Is.EqualTo(a[i].Channel), $"{what} mod {i} channel");
                Assert.That(b[i].DependsOn, Is.EqualTo(a[i].DependsOn), $"{what} mod {i} dependsOn");
            }
        }

        private static void AssertTags(GameplayTag[] a, GameplayTag[] b, string what)
        {
            int aLen = a == null ? 0 : a.Length;
            int bLen = b == null ? 0 : b.Length;
            Assert.That(bLen, Is.EqualTo(aLen), $"{what} 标签数");
            for (int i = 0; i < aLen; i++)
                Assert.That(b[i].Id, Is.EqualTo(a[i].Id), $"{what} tag {i}");
        }
    }
}
