using Core;
using Core.Command;
using Core.GAS;
using Gameplay.Items;

namespace Gameplay.Characters
{
    /// <summary>
    /// 双子灵巧转换（技能 Id=124 状态下的衍生行动，1 AP）：把手中武器转换成可拾取武器
    /// 刀(1)/枪(3)/施工锤(4)（不含圣盾 13）。凭空转化（S9：形态变化而非拾取，无地理要求）。
    /// 执行：自校验（角色 15 + 灵巧标签 + 持武器 + 池内）→ 扣 1 AP → 换装并销毁旧武器。
    /// 弹药强制归零（新武器需 ReloadCommand 装填后射击）——比拾取语义更严格：
    /// PickupItemCommand 换武器不重置弹药，转换则须走 EquipWeapon 卸下路径清理旧武器 EquipEffect。
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

            // 换装走 EquipWeapon 的卸下路径：旧武器带 EquipEffect（双子可合法拾取圣盾 13，
            // 其 GE 321 长挂）时会被自动移除——不得先置空绕过清理，否则效果残留至局末。
            // 弹药强制归零：旧武器与转换产物都不保留弹药（需 ReloadCommand 装填后射击）。
            c.Inventory.EquippedWeaponAmmo = 0;
            c.Inventory.EquipWeapon(weapon);
            c.Inventory.EquippedWeaponAmmo = 0;
        }
    }
}
