using Core;
using Core.Command;
using Core.GAS;
using Gameplay.Items;

namespace Gameplay.Characters
{
    /// <summary>
    /// 双子灵巧转换（技能 Id=124 状态下的衍生行动，1 AP）：把手中武器转换成可拾取武器
    /// 刀(1)/枪(3)/施工锤(4)（不含圣盾 13）。凭空转化（S9：形态变化而非拾取，无地理要求）。
    /// 执行：自校验（角色 15 + 灵巧标签 + 持武器 + 池内）→ 扣 1 AP → 销毁旧武器（弹药清零）→
    /// 装备新武器（弹药默认 0，与拾取语义一致，需 ReloadCommand 装填）。
    /// </summary>
    public class TwinConvertCommand : ICommand
    {
        public int Tick { get; set; }
        public int PlayerId { get; set; }
        public int NewItemId { get; set; }

        public TwinConvertCommand() { }
        public TwinConvertCommand(int tick, int playerId, int newItemId)
        {
            Tick = tick; PlayerId = playerId; NewItemId = newItemId;
        }

        public void Execute(WorldState state)
        {
            Character c = state.GetCharacterByPlayer(PlayerId);
            if (c == null || !c.CanAct || c.ActionPoints <= 0) return;
            if (c.Definition?.Id != SkillSystem.TwinCharacterId) return;
            if (!c.ASC.Tags.HasTag(GameplayTagConstants.TwinAgile)) return;   // 非灵巧态
            if (c.Inventory.EquippedWeapon == null) return;                    // 空手

            ItemDef weapon = ItemRegistry.Get(NewItemId);
            if (weapon == null || weapon.Type != ItemType.Weapon) return;
            switch (NewItemId)
            {
                case 1: case 3: case 4: break;
                default: return; // 池外（如圣盾 13）
            }

            c.ASC.ModifyAttributeCurrentValue(AttributeConstants.ActionPoints, -1f);

            // 销毁旧武器（先置空，EquipWeapon 的卸下 EquipEffect 路径仅对带 EquipEffect 武器有效，
            // 池内武器均无 EquipEffect，置空后直接装备即可）
            c.Inventory.EquippedWeapon = null;
            c.Inventory.EquippedWeaponAmmo = 0;
            c.Inventory.EquipWeapon(weapon);
            c.Inventory.EquippedWeaponAmmo = 0;
        }
    }
}
