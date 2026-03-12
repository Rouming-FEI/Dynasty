using AI;
using Core;
using Core.GAS;
using Core.Sync;
using Gameplay.Characters;
using Gameplay.Combat;
using Gameplay.Items;
using Gameplay.Judgment;
using Gameplay.Locations;
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
            StateAssert.AssertEqual(state, restored);

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
            StateAssert.AssertEqual(state, restored);
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
            StateAssert.AssertEqual(state, restored);
        }
    }
}
