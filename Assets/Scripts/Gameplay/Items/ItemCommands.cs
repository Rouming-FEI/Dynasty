using Core;
using Core.Command;
using Core.GAS;
using Gameplay.Characters;

namespace Gameplay.Items
{
    /// <summary>拾取道具：消耗 1 AP，将道具放入背包。背包满则拾取失败且不消耗 AP。</summary>
    public class PickupItemCommand : ICommand
    {
        public int Tick { get; set; }
        public int PlayerId { get; set; }
        public int ItemId { get; set; }

        public PickupItemCommand() { }
        public PickupItemCommand(int tick, int playerId, int itemId)
        {
            Tick = tick; PlayerId = playerId; ItemId = itemId;
        }

        public void Execute(WorldState state)
        {
            Character c = state.GetCharacterByPlayer(PlayerId);
            if (c == null || !c.CanAct || c.ActionPoints <= 0) return;

            ItemDef item = ItemRegistry.Get(ItemId);
            if (item == null) return;

            // 背包满 → 拾取失败，不扣 AP
            if (!c.Inventory.AddConsumable(item)) return;

            c.ASC.ModifyAttributeCurrentValue(AttributeConstants.ActionPoints, -1f);
        }
    }

    /// <summary>
    /// 使用道具（刀/血包）：目标合法性校验 → 扣 AP → 对目标应用 UseEffect → 移出背包。
    /// Self 类道具只能对自己使用；SameLocation 类道具要求双方同地点。
    /// </summary>
    public class UseItemCommand : ICommand
    {
        public int Tick { get; set; }
        public int PlayerId { get; set; }
        public int ItemIndex { get; set; }
        public int TargetPlayerId { get; set; }

        public UseItemCommand() { }
        public UseItemCommand(int tick, int playerId, int itemIndex, int targetPlayerId)
        {
            Tick = tick; PlayerId = playerId; ItemIndex = itemIndex; TargetPlayerId = targetPlayerId;
        }

        public void Execute(WorldState state)
        {
            Character c = state.GetCharacterByPlayer(PlayerId);
            if (c == null || !c.CanAct) return;

            if (ItemIndex < 0 || ItemIndex >= c.Inventory.Consumables.Count) return;
            ItemDef item = c.Inventory.Consumables[ItemIndex];
            if (item == null) return;
            if (c.ActionPoints < item.UseAPCost) return;

            Character target = state.GetCharacterByPlayer(TargetPlayerId);
            if (target == null || target.IsDead) return;

            // 目标合法性：Self 只能对自己用（血包），SameLocation 要求同地点（刀/锤），AnyPlayer 不限（枪）
            switch (item.TargetType)
            {
                case ItemTargetType.Self:
                    if (TargetPlayerId != PlayerId) return;
                    break;
                case ItemTargetType.SameLocation:
                    if (target.CurrentLocationId != c.CurrentLocationId) return;
                    break;
            }

            c.ASC.ModifyAttributeCurrentValue(AttributeConstants.ActionPoints, -(float)item.UseAPCost);

            // 触发式炸弹 (Id=7)：在自身位置安放陷阱
            if (item.Id == 7)
            {
                TrapSystem.Plant(state, c.CurrentLocationId, 7);
                c.Inventory.RemoveConsumable(ItemIndex);
                return;
            }

            // 应急处理装置 (Id=8)：HoT + DeathSave 标签 + ErosionLimit（见 EmergencyDeviceEffect）
            if (item.Id == 8)
            {
                EmergencyDeviceEffect.Apply(state, target);
                c.Inventory.RemoveConsumable(ItemIndex);
                return;
            }

            // 武器类道具走 WeaponSystem 路由，消耗品类仍直接应用 UseEffect
            if (item.Type == ItemType.Weapon)
            {
                ExecuteWeaponUse(state, c, target, item);
                // 武器使用后不从背包移除（耐久/弹药由 WeaponSystem 管理）
                return;
            }

            if (item.UseEffect != null)
            {
                float damage = GetSanityDamage(item.UseEffect);
                if (damage > 0f)
                {
                    DamagePipeline.ApplyDamage(state, c.Id, target.Id, damage, DamageType.Physical);
                }
                else
                {
                    GameplayEffectSpec spec = target.ASC.RentSpec();
                    spec.Definition = item.UseEffect;
                    spec.SourceId = c.Id;
                    spec.TargetId = target.Id;
                    target.ASC.ApplyGameplayEffect(spec);

                    // 荆棘素 (Id=6)：激活全局 DOT 回合计数器
                    if (item.Id == 6)
                        state.ThornPotionTurnsRemaining = 3;
                }
            }

            // 消耗品使用后移除
            c.Inventory.RemoveConsumable(ItemIndex);
        }

        /// <summary>武器使用路由：根据武器类型分发到 WeaponSystem。</summary>
        private static void ExecuteWeaponUse(WorldState state, Character c, Character target, ItemDef item)
        {
            switch (item.Id)
            {
                case 3: // 枪 — 远程攻击，需弹药
                    WeaponSystem.RangedAttack(state, c, target, item, 2f);
                    break;
                case 4: // 施工锤 — 近战攻击（投掷/拆除由独立 Command 处理）
                    DamagePipeline.ApplyDamage(state, c.Id, target.Id, 1.5f, DamageType.Physical);
                    break;
                case 5: // 核弹 — 嵌入 RPS 判定，胜者全体 3 真实伤害
                    NukeSystem.Launch(state, c);
                    break;
                default: // 其他武器（刀等）— 走 UseEffect 伤害
                    float damage = GetSanityDamage(item.UseEffect);
                    if (damage > 0f)
                        DamagePipeline.ApplyDamage(state, c.Id, target.Id, damage, DamageType.Physical);
                    break;
            }
        }

        /// <summary>返回效果中负 Sanity 修改器对应的伤害值（正数）；无伤害修改器返回 0。</summary>
        private static float GetSanityDamage(GameplayEffect effect)
        {
            AttributeModifier[] mods = effect.Modifiers;
            if (mods == null) return 0f;
            for (int i = 0; i < mods.Length; i++)
                if (mods[i].AttributeId == AttributeConstants.Sanity && mods[i].Magnitude < 0f)
                    return -mods[i].Magnitude;
            return 0f;
        }
    }

    /// <summary>
    /// 投掷施工锤：消耗 1 AP，攻击方 vs 目标 RPS 判定，命中造成 1.5 伤害。
    /// 无论命中与否，锤子掉落在目标位置并从攻击方解除装备。
    /// </summary>
    public class ThrowHammerCommand : ICommand
    {
        public int Tick { get; set; }
        public int PlayerId { get; set; }
        public int TargetPlayerId { get; set; }

        public ThrowHammerCommand() { }
        public ThrowHammerCommand(int tick, int playerId, int targetPlayerId)
        {
            Tick = tick; PlayerId = playerId; TargetPlayerId = targetPlayerId;
        }

        public void Execute(WorldState state)
        {
            Character c = state.GetCharacterByPlayer(PlayerId);
            if (c == null || !c.CanAct || c.ActionPoints <= 0) return;

            // 必须持有施工锤（装备中或在背包中）
            ItemDef hammer = c.Inventory.EquippedWeapon;
            if (hammer == null || hammer.Id != 4)
            {
                // 从背包中找
                for (int i = 0; i < c.Inventory.Consumables.Count; i++)
                {
                    if (c.Inventory.Consumables[i].Id == 4)
                    {
                        hammer = c.Inventory.Consumables[i];
                        c.Inventory.RemoveConsumable(i);
                        break;
                    }
                }
            }
            else
            {
                c.Inventory.EquippedWeapon = null;
            }

            if (hammer == null) return;

            Character target = state.GetCharacterByPlayer(TargetPlayerId);
            if (target == null || target.IsDead) return;

            c.ASC.ModifyAttributeCurrentValue(AttributeConstants.ActionPoints, -1f);

            WeaponSystem.ThrowHammer(state, c, target, hammer);
        }
    }

    /// <summary>
    /// 拆除当前地点：消耗 1 AP，清除所在地点所有掉落物。
    /// 必须持有施工锤（Id=4，装备中或背包中）。
    /// </summary>
    public class DemolishCommand : ICommand
    {
        public int Tick { get; set; }
        public int PlayerId { get; set; }

        public DemolishCommand() { }
        public DemolishCommand(int tick, int playerId)
        {
            Tick = tick; PlayerId = playerId;
        }

        public void Execute(WorldState state)
        {
            Character c = state.GetCharacterByPlayer(PlayerId);
            if (c == null || !c.CanAct || c.ActionPoints <= 0) return;

            // 检查是否持有施工锤
            bool hasHammer = (c.Inventory.EquippedWeapon != null && c.Inventory.EquippedWeapon.Id == 4);
            if (!hasHammer)
            {
                for (int i = 0; i < c.Inventory.Consumables.Count; i++)
                {
                    if (c.Inventory.Consumables[i].Id == 4)
                    {
                        hasHammer = true;
                        break;
                    }
                }
            }
            if (!hasHammer) return;

            c.ASC.ModifyAttributeCurrentValue(AttributeConstants.ActionPoints, -1f);

            WeaponSystem.DemolishLocation(state, c);
        }
    }

    /// <summary>
    /// 装填枪械：消耗 1 AP，将弹药数设为 1。
    /// 必须持有枪（Id=3，装备中或背包中）。
    /// </summary>
    public class ReloadCommand : ICommand
    {
        public int Tick { get; set; }
        public int PlayerId { get; set; }

        public ReloadCommand() { }
        public ReloadCommand(int tick, int playerId)
        {
            Tick = tick; PlayerId = playerId;
        }

        public void Execute(WorldState state)
        {
            Character c = state.GetCharacterByPlayer(PlayerId);
            if (c == null || !c.CanAct || c.ActionPoints <= 0) return;

            // 检查是否持有枪
            bool hasGun = false;
            if (c.Inventory.EquippedWeapon != null && c.Inventory.EquippedWeapon.Id == 3)
                hasGun = true;
            if (!hasGun)
            {
                for (int i = 0; i < c.Inventory.Consumables.Count; i++)
                {
                    if (c.Inventory.Consumables[i].Id == 3)
                    {
                        hasGun = true;
                        break;
                    }
                }
            }
            if (!hasGun) return;

            c.ASC.ModifyAttributeCurrentValue(AttributeConstants.ActionPoints, -1f);

            WeaponSystem.Reload(c);
        }
    }
}
