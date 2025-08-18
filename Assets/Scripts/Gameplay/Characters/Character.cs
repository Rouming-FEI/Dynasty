using Core;
using Core.GAS;
using Core.Command;
using Gameplay.Items;

namespace Gameplay.Characters
{
    public class Character
    {
        public int Id;
        public int PlayerId;
        public string Name;
        public AbilitySystemComponent ASC;
        public int CurrentLocationId;
        public InventoryComponent Inventory { get; private set; }
        public CharacterDef Definition;   // 角色定义（含技能表），由 CharacterDef.ApplyTo 赋值

        public Character(int id, int playerId, string name)
        {
            Id = id;
            PlayerId = playerId;
            Name = name;
            ASC = new AbilitySystemComponent(id);
            ASC.SetAttributeSet(new DefaultAttributeSet());
            CurrentLocationId = -1;
            Inventory = new InventoryComponent();
        }

        public int ActionPoints => (int)ASC.GetAttributeValue(AttributeConstants.ActionPoints);
        public float Sanity => ASC.GetAttributeValue(AttributeConstants.Sanity);
        public float MaxSanity => ASC.GetAttributeValue(AttributeConstants.MaxSanity);
        public float ErosionLimit => ASC.GetAttributeValue(AttributeConstants.ErosionLimit);
        public bool IsDead => ASC.IsDead;
        public bool CanAct => ASC.CanAct;
        public bool CanMove => ASC.CanMove;
    }
}
