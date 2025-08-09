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

            _items = new ItemDef[] { knife, healthPack };
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
