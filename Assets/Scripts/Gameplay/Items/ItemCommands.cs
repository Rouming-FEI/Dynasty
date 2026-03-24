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

            // 代行者孤狼限制：禁深度稳定器（血包 Id=2）/应急装置（Id=8）；武器只可用枪（Id=3）
            if (c.Definition?.Id == 13)
            {
                if (item.Id == 2 || item.Id == 8) return;
                if (item.Type == ItemType.Weapon && item.Id != 3) return;
            }

            // 地狱的子民（技能 Id=122）拦截键 = 角色 Definition.Id==14（混沌法师），
            // 非技能 Id——勿按技能 Id 改判定键：禁拾取武器类/消耗品（应急装置 Id=8 豁免，规格 3.3）
            if (c.Definition?.Id == SkillSystem.ChaosMageCharacterId && item.Id != 8) return;

            // 鬼化：禁拾取武器
            if (c.ASC.Tags.HasTag(GameplayTagConstants.GhostForm) && item.Type == ItemType.Weapon) return;

            // 武器类进装备位（与 TurnManager.AddItemToInventory 一致）。
            // 修复：武器曾进背包，UseItemCommand 走武器路由后从不移除，
            // 核弹（Weapon 类）因此可无限复用。
            if (item.Type == ItemType.Weapon)
            {
                c.Inventory.EquipWeapon(item);
            }
            else
            {
                // 背包满 → 拾取失败，不扣 AP
                if (!c.Inventory.AddConsumable(item)) return;
            }

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

            // 鬼化：禁应急处理装置（Id=8）与武器类道具
            if (c.ASC.Tags.HasTag(GameplayTagConstants.GhostForm))
            {
                if (item.Id == 8) return;
                if (item.Type == ItemType.Weapon) return;
            }

            // 地狱的子民（技能 Id=122）拦截键 = 角色 Definition.Id==14（混沌法师），
            // 非技能 Id——勿按技能 Id 改判定键：禁使用任何道具/武器（应急装置 Id=8 豁免）
            if (c.Definition?.Id == SkillSystem.ChaosMageCharacterId && item.Id != 8) return;

            // 静止时空：期间禁治疗道具（血包 Id=2 / 应急装置 Id=8）
            if (c.ASC.Tags.HasTag(GameplayTagConstants.TimeStop) && IsHealingItem(item))
                return;

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

            // 净化药剂 (Id=9)：实验室中使用 0 AP（其余道具按 UseAPCost 扣）
            int apCost = item.UseAPCost;
            if (item.Id == 9 && c.CurrentLocationId == 7) apCost = 0;
            c.ASC.ModifyAttributeCurrentValue(AttributeConstants.ActionPoints, -apCost);

            // 触发式炸弹 (Id=7)：在自身位置安放陷阱
            if (item.Id == 7)
            {
                TrapSystem.Plant(state, c.CurrentLocationId, 7);
                c.Inventory.RemoveConsumable(ItemIndex);
                return;
            }

            // 应急处理装置 (Id=8)：HoT + DeathSave 标签 + ErosionLimit（见 EmergencyDeviceEffect）
            // 阵法回路（阵法大师）：施放者 Definition.Id == 11 时跳过侵蚀加成
            if (item.Id == 8)
            {
                EmergencyDeviceEffect.Apply(state, target, c.Definition?.Id != 11);
                c.Inventory.RemoveConsumable(ItemIndex);
                return;
            }

            // 净化药剂 (Id=9)：清除负面状态 + 挂 3 回合防/攻增益（见 PurifierEffect）
            if (item.Id == 9)
            {
                PurifierEffect.Apply(state, c);
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
                // 伤害数值单一来源 = ItemRegistry.UseEffect（GetSanityDamage），勿另设常量
                float damage = ItemRegistry.GetSanityDamage(item);
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
            // 武器伤害一律读 ItemRegistry.UseEffect（GetSanityDamage）——数值唯一来源
            float itemDamage = ItemRegistry.GetSanityDamage(item);
            switch (item.Id)
            {
                case 3: // 枪 — 远程攻击，需弹药
                    WeaponSystem.RangedAttack(state, c, target, item, itemDamage);
                    break;
                case 4: // 施工锤 — 近战攻击（投掷/拆除由独立 Command 处理）
                    DamagePipeline.ApplyDamage(state, c.Id, target.Id, itemDamage, DamageType.Physical);
                    break;
                case 5: // 核弹 — 嵌入 RPS 判定，胜者全体 3 真实伤害
                    NukeSystem.Launch(state, c);
                    break;
                default: // 其他武器（刀等）— 走 UseEffect 伤害
                    if (itemDamage > 0f)
                        DamagePipeline.ApplyDamage(state, c.Id, target.Id, itemDamage, DamageType.Physical);
                    break;
            }
        }

        /// <summary>治疗类道具判定：血包（Id=2）/ 应急处理装置（Id=8）。</summary>
        private static bool IsHealingItem(ItemDef item) =>
            item.Id == 2 || item.Id == 8;
    }

    /// <summary>
    /// 投掷施工锤：消耗 1 AP，攻击方 vs 目标 RPS 判定，命中造成 1.5 伤害。
    /// 命中与否锤子都掉落在目标位置并从攻击方移除；目标无效（不存在/死亡）
    /// 时不执行、不扣 AP，锤子保留。
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

            // 必须持有施工锤（Id=4）：背包优先，其次装备位。
            // 装备位是其他武器时不视为持锤（修复：曾把非锤武器当锤投掷）。
            ItemDef hammer = null;
            int hammerIndex = -1;
            for (int i = 0; i < c.Inventory.Consumables.Count; i++)
            {
                if (c.Inventory.Consumables[i].Id == 4)
                {
                    hammer = c.Inventory.Consumables[i];
                    hammerIndex = i;
                    break;
                }
            }
            bool fromEquipped = false;
            if (hammer == null
                && c.Inventory.EquippedWeapon != null
                && c.Inventory.EquippedWeapon.Id == 4)
            {
                hammer = c.Inventory.EquippedWeapon;
                fromEquipped = true;
            }
            if (hammer == null) return;

            Character target = state.GetCharacterByPlayer(TargetPlayerId);
            if (target == null || target.IsDead) return;

            // 目标校验通过后才移除锤子（修复：曾在校验前移除，无效目标导致锤子凭空消失）
            if (fromEquipped)
            {
                c.Inventory.EquippedWeapon = null;
                c.Inventory.EquippedWeaponAmmo = 0;
            }
            else
            {
                c.Inventory.RemoveConsumable(hammerIndex);
            }

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

    /// <summary>
    /// 用装备中的枪射击：消耗 1 AP 与 1 弹药，对任意地点目标造成 2 物理伤害。
    /// 必须装备枪（Id=3）且有弹药。目标藏匿无法瞄准时射击不执行、不扣 AP
    /// （与其他命令"无效操作不扣 AP"约定一致）。
    /// </summary>
    public class FireGunCommand : ICommand
    {
        public int Tick { get; set; }
        public int PlayerId { get; set; }
        public int TargetPlayerId { get; set; }

        public FireGunCommand() { }
        public FireGunCommand(int tick, int playerId, int targetPlayerId)
        {
            Tick = tick; PlayerId = playerId; TargetPlayerId = targetPlayerId;
        }

        public void Execute(WorldState state)
        {
            Character c = state.GetCharacterByPlayer(PlayerId);
            if (c == null || !c.CanAct || c.ActionPoints <= 0) return;

            ItemDef gun = c.Inventory.EquippedWeapon;
            if (gun == null || gun.Id != 3) return;
            if (c.Inventory.EquippedWeaponAmmo <= 0) return;

            Character target = state.GetCharacterByPlayer(TargetPlayerId);
            if (target == null || target.IsDead || target.PlayerId == PlayerId) return;

            // RangedAttack 内部处理弹药扣减与藏匿瞄准检查，失败则不扣 AP。
            // 伤害数值源 = ItemRegistry.UseEffect（GetSanityDamage），勿另设常量
            if (WeaponSystem.RangedAttack(state, c, target, gun, ItemRegistry.GetSanityDamage(gun)))
                c.ASC.ModifyAttributeCurrentValue(AttributeConstants.ActionPoints, -1f);
        }
    }

    /// <summary>净化药剂：清除负面状态（标签清单 + 心雾清零）+ 挂增益 GE。实验室中 0 AP 由 UseItemCommand 判定。</summary>
    public static class PurifierEffect
    {
        private static readonly GameplayTag[] NegativeTags =
        {
            GameplayTagConstants.MistState, GameplayTagConstants.TimeStop,
            GameplayTagConstants.ThornPotionActive,
            GameplayTagConstants.LifeFire, GameplayTagConstants.Bound, GameplayTagConstants.BoundStrong
        };

        public static void Apply(WorldState state, Character c)
        {
            for (int i = 0; i < NegativeTags.Length; i++)
                c.ASC.Tags.RemoveTag(NegativeTags[i]);
            c.ASC.ResetMistLayers();

            GameplayEffect ge = ItemRegistry.Get(9).UseEffect;
            GameplayEffectSpec spec = c.ASC.RentSpec();
            spec.Definition = ge;
            spec.SourceId = c.Id;
            spec.TargetId = c.Id;
            spec.RemainingTurns = ge.DurationTurns;
            c.ASC.ApplyGameplayEffect(spec);
        }
    }
}
