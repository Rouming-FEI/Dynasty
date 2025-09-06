using System.Collections.Generic;

namespace Gameplay.Items
{
    public class InventoryComponent
    {
        public ItemDef EquippedWeapon;
        public List<ItemDef> Consumables;
        public const int MaxConsumables = 4;

        /// <summary>枪弹药数：装填一次可射击一发，击发后归零，需 ReloadCommand 重新装填。</summary>
        public int EquippedWeaponAmmo;

        public InventoryComponent()
        {
            Consumables = new List<ItemDef>(MaxConsumables);
            EquippedWeaponAmmo = 0;
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
