using System.Collections.Generic;
using Core.GAS;

namespace Gameplay.Items
{
    public class InventoryComponent
    {
        public ItemDef EquippedWeapon;
        public List<ItemDef> Consumables;
        public const int MaxConsumables = 4;

        /// <summary>枪弹药数：装填一次可射击一发，击发后归零，需 ReloadCommand 重新装填。</summary>
        public int EquippedWeaponAmmo;

        private AbilitySystemComponent _owner;

        public InventoryComponent()
        {
            Consumables = new List<ItemDef>(MaxConsumables);
            EquippedWeaponAmmo = 0;
        }

        /// <summary>注入 owner ASC 引用（由 Character 构造时调用），供装备效果应用/移除。</summary>
        public void SetOwner(AbilitySystemComponent owner) => _owner = owner;

        public void EquipWeapon(ItemDef weapon)
        {
            // 卸下旧武器：移除其 EquipEffect（长挂 GE）
            if (EquippedWeapon != null && EquippedWeapon != weapon && EquippedWeapon.EquipEffect != null)
                _owner?.RemoveEffectsById(EquippedWeapon.EquipEffect.Id);

            EquippedWeapon = weapon;

            // 装备时应用 EquipEffect（长挂 GE，卸下时由 RemoveEffectsById 移除）
            if (weapon?.EquipEffect != null && _owner != null)
            {
                GameplayEffectSpec spec = _owner.RentSpec();
                spec.Definition = weapon.EquipEffect;
                spec.SourceId = _owner.OwnerId;
                spec.TargetId = _owner.OwnerId;
                spec.RemainingTurns = weapon.EquipEffect.DurationTurns;
                _owner.ApplyGameplayEffect(spec);
            }
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
