using System;
using System.Collections.Generic;
using Core;
using Core.GAS;
using Gameplay.Characters;
using Gameplay.Items;

namespace Dynasty.Tests
{
    /// <summary>
    /// 声明式测试场景搭建器。Build() 先走真实的 TurnManager.StartGame 拿到生产基线
    /// （角色创建、CharacterDef 应用、初始道具、守护者开局护盾），再叠加覆盖项，
    /// 最后把 Phase 置为 ActionPhase，使指令可以直接执行。
    /// </summary>
    public class ScenarioBuilder
    {
        private readonly int _playerCount;
        private readonly int _seed;
        private readonly List<Action<WorldState>> _overrides = new List<Action<WorldState>>();

        private ScenarioBuilder(int playerCount, int seed)
        {
            _playerCount = playerCount;
            _seed = seed;
        }

        public static ScenarioBuilder Create(int playerCount, int seed = 42)
            => new ScenarioBuilder(playerCount, seed);

        // ---------------- 位置 ----------------

        public ScenarioBuilder WithCharacterAt(int playerId, int locationId)
        {
            _overrides.Add(s => s.GetCharacterByPlayer(playerId).CurrentLocationId = locationId);
            return this;
        }

        public ScenarioBuilder WithAllAt(int locationId)
        {
            _overrides.Add(s =>
            {
                for (int i = 0; i < s.PlayerCount; i++)
                {
                    Character c = s.GetCharacterByPlayer(i);
                    if (c != null) c.CurrentLocationId = locationId;
                }
            });
            return this;
        }

        // ---------------- 属性 ----------------

        public ScenarioBuilder WithSanity(int playerId, float sanity)
        {
            _overrides.Add(s => s.GetCharacterByPlayer(playerId).ASC
                .SetAttributeBaseValue(AttributeConstants.Sanity, sanity));
            return this;
        }

        public ScenarioBuilder WithAP(int playerId, int ap)
        {
            _overrides.Add(s => s.GetCharacterByPlayer(playerId).ASC
                .SetAttributeBaseValue(AttributeConstants.ActionPoints, ap));
            return this;
        }

        /// <summary>给所有存活角色统一发 AP，省去逐个调用。</summary>
        public ScenarioBuilder WithAllAP(int ap)
        {
            _overrides.Add(s =>
            {
                for (int i = 0; i < s.PlayerCount; i++)
                    s.GetCharacterByPlayer(i)?.ASC
                        .SetAttributeBaseValue(AttributeConstants.ActionPoints, ap);
            });
            return this;
        }

        public ScenarioBuilder WithAttribute(int playerId, int attributeId, float value)
        {
            _overrides.Add(s => s.GetCharacterByPlayer(playerId).ASC
                .SetAttributeBaseValue(attributeId, value));
            return this;
        }

        // ---------------- 状态标签 / GE ----------------

        public ScenarioBuilder WithTag(int playerId, GameplayTag tag)
        {
            _overrides.Add(s => s.GetCharacterByPlayer(playerId).ASC.Tags.AddTag(tag));
            return this;
        }

        public ScenarioBuilder WithoutTag(int playerId, GameplayTag tag)
        {
            _overrides.Add(s => s.GetCharacterByPlayer(playerId).ASC.Tags.RemoveTag(tag));
            return this;
        }

        public ScenarioBuilder WithGuardianCharge(int playerId, int charge)
        {
            _overrides.Add(s =>
            {
                AbilitySystemComponent asc = s.GetCharacterByPlayer(playerId).ASC;
                asc.ResetGuardianCharge();
                for (int i = 0; i < charge; i++) asc.AddGuardianCharge();
            });
            return this;
        }

        /// <summary>
        /// 挂载持续型 GE。RemainingTurns 由 ApplyGameplayEffect 按 DurationTurns 初始化，
        /// 周期型 GE 的 PeriodTick 也在那里初始化为 Interval，此处无需手动设置。
        /// </summary>
        public ScenarioBuilder WithActiveEffect(int playerId, GameplayEffect ge)
        {
            _overrides.Add(s =>
            {
                Character c = s.GetCharacterByPlayer(playerId);
                GameplayEffectSpec spec = c.ASC.RentSpec();
                spec.Definition = ge;
                spec.SourceId = c.Id;
                spec.TargetId = c.Id;
                c.ASC.ApplyGameplayEffect(spec);
            });
            return this;
        }

        // ---------------- 道具 ----------------

        /// <summary>清空 StartGame 默认发放的刀与血包，避免污染道具测试。</summary>
        public ScenarioBuilder WithClearInventory(int playerId)
        {
            _overrides.Add(s =>
            {
                InventoryComponent inv = s.GetCharacterByPlayer(playerId).Inventory;
                inv.EquippedWeapon = null;
                inv.EquippedWeaponAmmo = 0;
                inv.Consumables.Clear();
            });
            return this;
        }

        public ScenarioBuilder WithItem(int playerId, int itemId)
        {
            _overrides.Add(s => s.GetCharacterByPlayer(playerId).Inventory
                .AddConsumable(ItemRegistry.Get(itemId)));
            return this;
        }

        public ScenarioBuilder WithEquippedWeapon(int playerId, int itemId, int ammo = 0)
        {
            _overrides.Add(s =>
            {
                InventoryComponent inv = s.GetCharacterByPlayer(playerId).Inventory;
                inv.EquipWeapon(ItemRegistry.Get(itemId));
                inv.EquippedWeaponAmmo = ammo;
            });
            return this;
        }

        // ---------------- 地点 ----------------

        public ScenarioBuilder WithTrapAt(int locationId, int count = 1)
        {
            _overrides.Add(s =>
            {
                for (int i = 0; i < count; i++) s.PlantTrap(locationId);
            });
            return this;
        }

        // ---------------- 构建 ----------------

        public WorldState Build()
        {
            var state = new WorldState(_playerCount, _seed);
            var turnManager = new TurnManager();
            state.TurnManager = turnManager;

            // 走真实初始化路径，保证测试贴合生产行为
            turnManager.StartGame(state);

            for (int i = 0; i < _overrides.Count; i++)
                _overrides[i](state);

            // 直接站在行动阶段，跳过判定，测试可立即入队指令
            state.Phase = TurnPhase.ActionPhase;
            return state;
        }
    }
}
