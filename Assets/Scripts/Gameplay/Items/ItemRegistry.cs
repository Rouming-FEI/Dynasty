using Core.GAS;

namespace Gameplay.Items
{
    /// <summary>
    /// P1 硬编码道具注册表。后续可替换为 ScriptableObject 配置加载。
    /// Get 位于 Command.Execute Hot Path，使用 for 循环查找，避免 LINQ 闭包分配。
    /// </summary>
    public static class ItemRegistry
    {
        private static ItemDef[] _items;
        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            // 刀 (Id=1) — 近战武器，使用对同地点目标造成 1.5 理智伤害
            var knife = new ItemDef
            {
                Id = 1,
                Name = "刀",
                Type = ItemType.Weapon,
                UseAPCost = 1,
                TargetType = ItemTargetType.SameLocation,
                UseEffect = new GameplayEffect
                {
                    Id = 100,
                    DurationType = GameplayEffectDurationType.Instant,
                    Modifiers = new[]
                    {
                        new AttributeModifier(AttributeConstants.Sanity, AttributeModifierOp.Add, -1.5f)
                    }
                }
            };

            // 血包 (Id=2) — 消耗品，使用回复自身 3 理智
            var healthPack = new ItemDef
            {
                Id = 2,
                Name = "血包",
                Type = ItemType.Consumable,
                UseAPCost = 1,
                TargetType = ItemTargetType.Self,
                UseEffect = new GameplayEffect
                {
                    Id = 101,
                    DurationType = GameplayEffectDurationType.Instant,
                    Modifiers = new[]
                    {
                        new AttributeModifier(AttributeConstants.Sanity, AttributeModifierOp.Add, 3f)
                    }
                }
            };

            // 枪 (Id=3) — 远程武器，对任意玩家造成 2 理智伤害，弹药由 InventoryComponent 追踪
            var gun = new ItemDef
            {
                Id = 3,
                Name = "枪",
                Type = ItemType.Weapon,
                UseAPCost = 1,
                TargetType = ItemTargetType.AnyPlayer,
                EquipEffect = null,
                UseEffect = new GameplayEffect
                {
                    Id = 102,
                    DurationType = GameplayEffectDurationType.Instant,
                    Modifiers = new[]
                    {
                        new AttributeModifier(AttributeConstants.Sanity, AttributeModifierOp.Add, -2f)
                    }
                }
            };

            // 施工锤 (Id=4) — 近战武器，对同地点目标造成 1.5 理智伤害
            var hammer = new ItemDef
            {
                Id = 4,
                Name = "施工锤",
                Type = ItemType.Weapon,
                UseAPCost = 1,
                TargetType = ItemTargetType.SameLocation,
                UseEffect = new GameplayEffect
                {
                    Id = 103,
                    DurationType = GameplayEffectDurationType.Instant,
                    Modifiers = new[]
                    {
                        new AttributeModifier(AttributeConstants.Sanity, AttributeModifierOp.Add, -1.5f)
                    }
                }
            };

            // 核弹 (Id=5) — 特殊武器，对任意玩家造成 3 理智伤害，判定逻辑由 NukeSystem 处理
            var nuke = new ItemDef
            {
                Id = 5,
                Name = "核弹",
                Type = ItemType.Weapon,
                UseAPCost = 2,
                TargetType = ItemTargetType.AnyPlayer,
                UseEffect = new GameplayEffect
                {
                    Id = 104,
                    DurationType = GameplayEffectDurationType.Instant,
                    Modifiers = new[]
                    {
                        new AttributeModifier(AttributeConstants.Sanity, AttributeModifierOp.Add, -3f)
                    }
                }
            };

            // 荆棘素 (Id=6) — 消耗品，为自身添加 3 回合荆棘标签，DOT 由 TurnManager 全局处理
            var thornPotion = new ItemDef
            {
                Id = 6,
                Name = "荆棘素",
                Type = ItemType.Consumable,
                UseAPCost = 0,
                TargetType = ItemTargetType.Self,
                UseEffect = new GameplayEffect
                {
                    Id = 105,
                    DurationType = GameplayEffectDurationType.HasDuration,
                    DurationTurns = 3,
                    GrantTags = new[] { GameplayTagConstants.ThornPotionActive }
                }
            };

            // 触发式炸弹 (Id=7) — 消耗品，在自身位置安放陷阱，由 TrapSystem 处理
            var trapBomb = new ItemDef
            {
                Id = 7,
                Name = "触发式炸弹",
                Type = ItemType.Consumable,
                UseAPCost = 0,
                TargetType = ItemTargetType.Self,
                UseEffect = new GameplayEffect
                {
                    Id = 106,
                    DurationType = GameplayEffectDurationType.Instant
                }
            };

            // 应急处理装置 (Id=8) — 消耗品，由 EmergencyDeviceEffect 拆分为 3 个 GE：
            //   HoT(Period(1)x5, Sanity+0.5) + DeathSave(Duration=5, DeathSaveActive 标签) + ErosionUp(Instant, ErosionLimit+1)
            var emergencyDevice = new ItemDef
            {
                Id = 8,
                Name = "应急处理装置",
                Type = ItemType.Consumable,
                UseAPCost = 0,
                TargetType = ItemTargetType.Self,
                UseEffect = null  // handled by EmergencyDeviceEffect.Apply (ItemCommands special case)
            };

            // 净化药剂 (Id=9) — 消耗品，清除负面状态（标签清单 + 心雾清零）+ 3 回合防/攻增益；
            // 实验室中 0 AP（UseItemCommand 判定）。GE 由 PurifierEffect.Apply 挂载。
            var purifier = new ItemDef
            {
                Id = 9,
                Name = "净化药剂",
                Type = ItemType.Consumable,
                UseAPCost = 1,
                TargetType = ItemTargetType.Self,
                UseEffect = new GameplayEffect
                {
                    Id = 290,
                    DurationType = GameplayEffectDurationType.HasDuration,
                    DurationTurns = 3,
                    Modifiers = new[]
                    {
                        new AttributeModifier(AttributeConstants.PhysicalDefense, AttributeModifierOp.Add, 0.5f),
                        new AttributeModifier(AttributeConstants.AttackBonus, AttributeModifierOp.Add, 0.5f)
                    }
                }
            };

            // 幻剑 (Id=12) — 海幻的近战武器，对同地点目标造成 1.5 理智伤害；
            // 归鞘持刀 0.5 全减伤与拔刀激活蜃景由 SkillSystem/DamagePipeline 按 BladeSheathed 标签处理
            var mirageBlade = new ItemDef
            {
                Id = 12,
                Name = "幻剑",
                Type = ItemType.Weapon,
                UseAPCost = 1,
                TargetType = ItemTargetType.SameLocation,
                UseEffect = new GameplayEffect
                {
                    Id = 107,
                    DurationType = GameplayEffectDurationType.Instant,
                    Modifiers = new[]
                    {
                        new AttributeModifier(AttributeConstants.Sanity, AttributeModifierOp.Add, -1.5f)
                    }
                }
            };

            // 圣骑士盾 (Id=13) — 武器，装备后 +0.5 物理防御（EquipEffect 长挂 999 回合，卸下时移除）
            var paladinShield = new ItemDef
            {
                Id = 13,
                Name = "圣骑士盾",
                Type = ItemType.Weapon,
                UseAPCost = 1,
                TargetType = ItemTargetType.AnyPlayer,
                EquipEffect = new GameplayEffect
                {
                    Id = 321,
                    DurationType = GameplayEffectDurationType.HasDuration,
                    DurationTurns = 999,
                    Modifiers = new[]
                    {
                        new AttributeModifier(AttributeConstants.PhysicalDefense, AttributeModifierOp.Add, 0.5f)
                    }
                }
            };

            _items = new ItemDef[] { knife, healthPack, gun, hammer, nuke, thornPotion, trapBomb, emergencyDevice, purifier, mirageBlade, paladinShield };
        }

        public static ItemDef Get(int id)
        {
            if (_items == null) return null;
            for (int i = 0; i < _items.Length; i++)
                if (_items[i].Id == id) return _items[i];
            return null;
        }
    }
}
