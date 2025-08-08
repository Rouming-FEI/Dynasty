using Core.GAS;

namespace Gameplay.Items
{
    public enum ItemType : byte { Weapon, Consumable }
    public enum ItemTargetType : byte { Self, SameLocation, AnyPlayer }

    public class ItemDef
    {
        public int Id;
        public string Name;
        public ItemType Type;
        public GameplayEffect EquipEffect;
        public GameplayEffect UseEffect;
        public int UseAPCost;
        public ItemTargetType TargetType;
    }
}
