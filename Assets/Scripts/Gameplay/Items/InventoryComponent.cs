using System.Collections.Generic;

namespace Gameplay.Items
{
    public class InventoryComponent
    {
        public ItemDef EquippedWeapon;
        public List<ItemDef> Consumables;
        public const int MaxConsumables = 4;

        public InventoryComponent()
        {
            Consumables = new List<ItemDef>(MaxConsumables);
        }

        public void EquipWeapon(ItemDef weapon)
        {
            EquippedWeapon = weapon;
        }

        public bool AddConsumable(ItemDef item)
        {
            if (Consumables.Count >= MaxConsumables) return false;
            Consumables.Add(item);
            return true;
        }

        public ItemDef RemoveConsumable(int index)
        {
            if (index < 0 || index >= Consumables.Count) return null;
            var item = Consumables[index];
            Consumables.RemoveAt(index);
            return item;
        }
    }
}
