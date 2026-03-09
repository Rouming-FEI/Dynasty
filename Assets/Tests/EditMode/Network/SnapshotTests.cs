using AI;
using Core;
using Core.GAS;
using Core.Sync;
using Gameplay.Characters;
using Gameplay.Combat;
using Gameplay.Items;
using Gameplay.Judgment;
using Gameplay.Locations;
using Gameplay.Movement;
using Network.Local;
using NUnit.Framework;

namespace Dynasty.Tests.Network
{
    /// <summary>WorldState 全量快照序列化：往返一致性（哈希自校验）+ 损坏数据拒绝 + 逐字段抽查。</summary>
    public class SnapshotTests
    {
        private static LocalLockstep CreateLockstep(WorldState state)
        {
            var ais = new SimpleAI[state.PlayerCount];
            for (int i = 0; i < ais.Length; i++) ais[i] = new SimpleAI(i);
            return new LocalLockstep(state, ais);
        }

        /// <summary>固定非平筹码（总计 R=5/S=3/P=4，石头唯一最高），避免全平作废死循环。</summary>
        private static ChipAllocation FixedChip(int pid)
        {
            var a = new ChipAllocation(pid);
            switch (pid)
            {
                case 0: a.Set(3, 0, 0); break;
                case 1: a.Set(2, 1, 0); break;
                case 2: a.Set(0, 2, 1); break;
                default: a.Set(0, 0, 3); break;
            }
            return a;
        }

        [Test]
        public void 完整对局_3回合_快照往返哈希一致()
        {
            var state = new WorldState(4, 42);
            var lockstep = CreateLockstep(state);
            lockstep.StartGame(new[]
            {
                CharacterDefCatalog.Get(1), // 守护者
                CharacterDefCatalog.Get(2), // 魅惑精灵
                CharacterDefCatalog.Get(5), // 魔术师
                CharacterDefCatalog.Get(7)  // 时间窃贼
            });
            lockstep.JudgmentInputProvider = FixedChip;

            // 跑满 3 回合（AI 行动天然覆盖技能/道具/移动/GE 挂载）
            int guard = 0;
            while (state.Phase != TurnPhase.GameOver && state.CurrentTurn <= 3 && guard++ < 120)
                lockstep.RunTurn();
            Assert.That(state.Phase, Is.Not.EqualTo(TurnPhase.GameOver), "3 回合内不应终局");
            Assert.That(state.CurrentTurn, Is.EqualTo(4), "应跑满 3 回合");

            byte[] bytes = WorldStateSerializer.Serialize(state);
            WorldState restored = WorldStateSerializer.Deserialize(bytes);

            Assert.That(restored.ComputeStateHash(), Is.EqualTo(state.ComputeStateHash()),
                "反序列化后哈希必须与序列化前一致（快照自校验）");
            AssertStateEqual(state, restored);

            // 随机流续流一致
            Assert.That(restored.Random.NextLong(), Is.EqualTo(state.Random.NextLong()), "随机续流一致");
        }

        [Test]
        public void 空快照_Waiting初始状态_往返一致()
        {
            var state = new WorldState(4, 42);   // TurnManager 为 null 的空状态
            byte[] bytes = WorldStateSerializer.Serialize(state);
            WorldState restored = WorldStateSerializer.Deserialize(bytes);

            Assert.That(restored.ComputeStateHash(), Is.EqualTo(state.ComputeStateHash()));
            Assert.That(restored.PlayerCount, Is.EqualTo(4));
            Assert.That(restored.Phase, Is.EqualTo(TurnPhase.Waiting));
            Assert.That(restored.CurrentTick, Is.EqualTo(0));
            Assert.That(restored.CurrentTurn, Is.EqualTo(0));
            Assert.That(restored.Random.State, Is.EqualTo(state.Random.State));
        }

        [Test]
        public void 损坏Magic_拒绝()
        {
            var state = new WorldState(4, 42);
            byte[] bytes = WorldStateSerializer.Serialize(state);
            bytes[0] ^= 0xFF;   // 篡改 Magic 首字节
            Assert.Throws<System.IO.InvalidDataException>(() => WorldStateSerializer.Deserialize(bytes));
        }

        [Test]
        public void 篡改哈希字段_自校验失败拒绝()
        {
            var state = new WorldState(4, 42);
            var lockstep = CreateLockstep(state);
            lockstep.StartGame(new[]
            {
                CharacterDefCatalog.Get(1), CharacterDefCatalog.Get(2),
                CharacterDefCatalog.Get(5), CharacterDefCatalog.Get(7)
            });
            lockstep.JudgmentInputProvider = FixedChip;
            int guard = 0;
            while (state.Phase != TurnPhase.GameOver && state.CurrentTurn <= 1 && guard++ < 80)
                lockstep.RunTurn();

            byte[] bytes = WorldStateSerializer.Serialize(state);
            // 头部布局：[Magic:4][Version:4][Hash:8] → hash 位于偏移 8..16
            bytes[8] ^= 0x01;
            Assert.Throws<System.IO.InvalidDataException>(() => WorldStateSerializer.Deserialize(bytes));
        }

        [Test]
        public void 含死角色对局_往返一致()
        {
            var state = new WorldState(4, 42);
            var lockstep = CreateLockstep(state);
            lockstep.StartGame(new[]
            {
                CharacterDefCatalog.Get(4), CharacterDefCatalog.Get(4),
                CharacterDefCatalog.Get(4), CharacterDefCatalog.Get(4)
            });
            lockstep.JudgmentInputProvider = FixedChip;
            int guard = 0;
            while (state.Phase != TurnPhase.GameOver && state.CurrentTurn <= 1 && guard++ < 80)
                lockstep.RunTurn();

            // 真实路径杀死 P2（剑鬼无免死被动；True 伤害穿免疫）
            Character victim = state.GetCharacterByPlayer(2);
            DamagePipeline.ApplyDamage(state, -1, victim.Id, 999f, DamageType.True);
            state.Commands.ExecuteAll(state);
            Assert.That(victim.IsDead, Is.True, "前置：P2 应已死亡");

            byte[] bytes = WorldStateSerializer.Serialize(state);
            WorldState restored = WorldStateSerializer.Deserialize(bytes);

            Assert.That(restored.ComputeStateHash(), Is.EqualTo(state.ComputeStateHash()));
            AssertStateEqual(state, restored);
            Assert.That(restored.GetCharacterByPlayer(2).IsDead, Is.True, "死角色重建后仍为死亡");
        }

        [Test]
        public void 特定状态构造_逐字段往返一致()
        {
            var state = new WorldState(4, 42);
            var tm = new TurnManager();
            state.TurnManager = tm;
            tm.StartGame(state, new[]
            {
                CharacterDefCatalog.Get(12), // 圣骑士（装备盾 + 长挂 GE 321）
                CharacterDefCatalog.Get(8),  // 倾听者（周期被动）
                CharacterDefCatalog.Get(1),  // 守护者（开局护盾）
                CharacterDefCatalog.Get(4)   // 剑鬼
            });

            // ---- 全局状态 ----
            state.CurrentTick = 5;
            state.CurrentTurn = 2;
            state.Phase = TurnPhase.ActionPhase;
            state.PlantTrap(4);
            state.PlantTrap(4);
            state.ThornPotionTurnsRemaining = 2;
            state.MovedThisTurn[0] = true;
            state.MovedThisTurn[3] = true;
            state.WoodSeedPlayer = 1;
            state.WoodSeedLocation = 6;
            state.PaladinAegisUsed = true;
            state.Random.Next(100);   // 消耗一段随机流
            var alloc = new ChipAllocation(0);
            alloc.Set(3, 0, 0);
            state.LastAllocations[0] = alloc;
            state.HasJudgmentResult = true;
            state.LastJudgment = new JudgmentResult
            {
                WinnerHand = 0,
                LoserHand = 1,
                NeutralHand = 2,
                IsGlobalFlat = false,
                Roles = new[] { JudgmentRole.Winner, JudgmentRole.Loser, JudgmentRole.Neutral, JudgmentRole.Neutral },
                EffectiveChips = new[] { 3, 2, 0, 0 },
                APGained = new[] { 1, 0, 0, 0 },
                WinnerCount = 1,
                LoserCount = 1
            };
            tm.RestoreConsecutiveFlatCount(1);
            tm.RestoreActionOrder(new[] { 0, 1, 2, 3 });
            tm.RestoreActorIndex(2);

            // ---- 地点道具 + 规则状态 ----
            state.Locations.DropItem(4, 6, 2);   // 安全屋 2 荆棘素
            state.Locations.DropItem(7, 2, 1);   // 实验室 1 血包
            var lab = (LabRule)state.Locations.Rules[LabRule.LabLocationId];
            lab.ToggleDoor();
            lab.ToggleSwitch();
            lab.TideTurnsElapsed = 1;
            lab.RemoveThreshold();

            // ---- P0 圣骑士：标签/充能/心雾/血印/本能/冷却/道具/GE ----
            Character p0 = state.GetCharacterByPlayer(0);
            p0.ASC.Tags.AddTag(GameplayTagConstants.HasShield);
            p0.ASC.Tags.AddTag(GameplayTagConstants.MistState);
            p0.ASC.AddMistLayer();
            p0.ASC.AddMistLayer();
            p0.ASC.AddGuardianCharge();
            p0.ASC.AddGuardianCharge();
            p0.ASC.AddGuardianCharge();
            p0.ASC.ModifyBloodSeals(2);
            p0.ASC.ModifyInstinct(3);
            p0.ASC.MarkDamageDealt(1);
            p0.ASC.SetCooldown(0, 2);
            p0.ASC.SetCooldown(1, 1);
            p0.Inventory.EquipWeapon(ItemRegistry.Get(13));  // 圣骑士盾（挂长驻 GE 321）
            p0.Inventory.AddConsumable(ItemRegistry.Get(2));
            p0.Inventory.AddConsumable(ItemRegistry.Get(6));

            // 挂一个周期型 + 条件过期 GE，随后手动推进其运行时字段
            GameplayEffectSpec spec = p0.ASC.RentSpec();
            spec.Definition = new GameplayEffect
            {
                Id = 999,
                DurationType = GameplayEffectDurationType.HasDuration,
                DurationTurns = 5,
                Modifiers = new[]
                {
                    new AttributeModifier(AttributeConstants.PhysicalDefense, AttributeModifierOp.Add, 0.5f, 2, 1),
                    new AttributeModifier(AttributeConstants.AttackBonus, AttributeModifierOp.Add, 0.25f, 1, 0)
                },
                GrantTags = new[] { GameplayTagConstants.CannotMove },
                RemoveTags = new[] { GameplayTagConstants.CannotAct },
                ApplicationRequiredTags = new[] { GameplayTagConstants.MistState },
                ApplicationBlockedTags = new[] { GameplayTagConstants.Dead },
                Period = new GameplayEffectPeriod(2, 3),
                Stacking = GameplayEffectStacking.Aggregate,
                CooldownTurns = 4,
                ExpiryPolicy = GameplayEffectExpiryPolicy.RemoveOnCondition,
                ConditionTags = new[] { GameplayTagConstants.MistState }
            };
            spec.SourceId = p0.Id;
            spec.TargetId = p0.Id;
            p0.ASC.ApplyGameplayEffect(spec);
            Assert.That(p0.ASC.ActiveEffectCount, Is.EqualTo(2), "装备长驻 321 + 自挂 999");
            GameplayEffectSpec active = p0.ASC.GetActiveEffect(1);
            active.PeriodTick = 1;
            active.PeriodRunCount = 1;
            active.StackCount = 2;
            active.CooldownRemaining = 2;
            active.MagnitudeMultiplier = 1.5f;

            // P0 属性 base/current 分离（AP 消耗后 current < base）
            p0.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 2.5f);
            p0.ASC.SetAttributeBaseValue(AttributeConstants.ErosionLimit, 2f);
            p0.ASC.ModifyAttributeCurrentValue(AttributeConstants.ActionPoints, -1f);

            // ---- P1 倾听者：被动计时器推进 ----
            Character p1 = state.GetCharacterByPlayer(1);
            Assert.That(p1.ASC.PassiveCount, Is.GreaterThan(0), "倾听者应有周期被动");
            p1.ASC.RestorePassiveTimers(new[] { 1 });

            byte[] bytes = WorldStateSerializer.Serialize(state);
            WorldState restored = WorldStateSerializer.Deserialize(bytes);

            Assert.That(restored.ComputeStateHash(), Is.EqualTo(state.ComputeStateHash()));
            AssertStateEqual(state, restored);
        }

        // ------------------------------------------------------------------
        // 逐字段断言（哈希只覆盖核心字段，此处补齐全部序列化字段）
        // ------------------------------------------------------------------

        private static void AssertStateEqual(WorldState a, WorldState b)
        {
            Assert.That(b.PlayerCount, Is.EqualTo(a.PlayerCount));
            Assert.That(b.CurrentTick, Is.EqualTo(a.CurrentTick));
            Assert.That(b.CurrentTurn, Is.EqualTo(a.CurrentTurn));
            Assert.That(b.Phase, Is.EqualTo(a.Phase));
            Assert.That(b.Random.State, Is.EqualTo(a.Random.State), "Random.State");
            Assert.That(b.ThornPotionTurnsRemaining, Is.EqualTo(a.ThornPotionTurnsRemaining));
            Assert.That(b.WoodSeedPlayer, Is.EqualTo(a.WoodSeedPlayer));
            Assert.That(b.WoodSeedLocation, Is.EqualTo(a.WoodSeedLocation));
            Assert.That(b.PaladinAegisUsed, Is.EqualTo(a.PaladinAegisUsed));
            Assert.That(b.LocationTrapCounts, Is.EqualTo(a.LocationTrapCounts));
            Assert.That(b.MovedThisTurn, Is.EqualTo(a.MovedThisTurn));

            for (int i = 0; i < a.LastAllocations.Length; i++)
            {
                Assert.That(b.LastAllocations[i].PlayerId, Is.EqualTo(a.LastAllocations[i].PlayerId), $"alloc[{i}].pid");
                Assert.That(b.LastAllocations[i].Rock, Is.EqualTo(a.LastAllocations[i].Rock), $"alloc[{i}].rock");
                Assert.That(b.LastAllocations[i].Scissors, Is.EqualTo(a.LastAllocations[i].Scissors), $"alloc[{i}].scissors");
                Assert.That(b.LastAllocations[i].Paper, Is.EqualTo(a.LastAllocations[i].Paper), $"alloc[{i}].paper");
            }

            Assert.That(b.HasJudgmentResult, Is.EqualTo(a.HasJudgmentResult));
            if (a.HasJudgmentResult)
            {
                Assert.That(b.LastJudgment.WinnerHand, Is.EqualTo(a.LastJudgment.WinnerHand));
                Assert.That(b.LastJudgment.LoserHand, Is.EqualTo(a.LastJudgment.LoserHand));
                Assert.That(b.LastJudgment.NeutralHand, Is.EqualTo(a.LastJudgment.NeutralHand));
                Assert.That(b.LastJudgment.IsGlobalFlat, Is.EqualTo(a.LastJudgment.IsGlobalFlat));
                Assert.That(b.LastJudgment.WinnerCount, Is.EqualTo(a.LastJudgment.WinnerCount));
                Assert.That(b.LastJudgment.LoserCount, Is.EqualTo(a.LastJudgment.LoserCount));
                Assert.That(b.LastJudgment.Roles, Is.EqualTo(a.LastJudgment.Roles));
                Assert.That(b.LastJudgment.EffectiveChips, Is.EqualTo(a.LastJudgment.EffectiveChips));
                Assert.That(b.LastJudgment.APGained, Is.EqualTo(a.LastJudgment.APGained));
            }

            Assert.That(b.TurnManager, Is.Not.Null);
            Assert.That(b.TurnManager.ConsecutiveFlatCount, Is.EqualTo(a.TurnManager.ConsecutiveFlatCount));
            Assert.That(b.TurnManager.ActionOrder, Is.EqualTo(a.TurnManager.ActionOrder));
            Assert.That(b.TurnManager.CurrentActorIndex, Is.EqualTo(a.TurnManager.CurrentActorIndex));

            // 地点道具（分组）
            for (int i = 0; i < a.Locations.LocationCount; i++)
            {
                LocatedItem[] itemsA = a.Locations.GetLocatedItems(i);
                LocatedItem[] itemsB = b.Locations.GetLocatedItems(i);
                Assert.That(itemsB.Length, Is.EqualTo(itemsA.Length), $"loc {i} 道具组数");
                for (int j = 0; j < itemsA.Length; j++)
                {
                    Assert.That(itemsB[j].ItemId, Is.EqualTo(itemsA[j].ItemId), $"loc {i} item {j}");
                    Assert.That(itemsB[j].Count, Is.EqualTo(itemsA[j].Count), $"loc {i} count {j}");
                }
            }

            // 地点规则
            Assert.That(b.Locations.Rules.Length, Is.EqualTo(a.Locations.Rules.Length));
            for (int i = 0; i < a.Locations.Rules.Length; i++)
            {
                LocationRule ra = a.Locations.Rules[i];
                LocationRule rb = b.Locations.Rules[i];
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
            for (int i = 0; i < a.Characters.Length; i++)
            {
                Character ca = a.Characters[i];
                Character cb = b.Characters[i];
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
