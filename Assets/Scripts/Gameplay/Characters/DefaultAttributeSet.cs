using Core;
using Core.GAS;
using Core.Command;

namespace Gameplay.Characters
{
    public class DefaultAttributeSet : AttributeSet
    {
        public DefaultAttributeSet() : base(AttributeConstants.Count)
        {
            SetBaseValue(AttributeConstants.Sanity,          GameConfig.DefaultSanity);
            SetBaseValue(AttributeConstants.MaxSanity,       GameConfig.DefaultSanity);
            SetBaseValue(AttributeConstants.ErosionLimit,    GameConfig.DefaultErosionLimit);
            SetBaseValue(AttributeConstants.PhysicalDefense, 0f);
            SetBaseValue(AttributeConstants.MagicDefense,    0f);
            SetBaseValue(AttributeConstants.AttackBonus,     0f);
            SetBaseValue(AttributeConstants.ActionPoints,    0);
            SetBaseValue(AttributeConstants.MaxActionPoints, GameConfig.ChipsPerPlayer);
        }
    }
}
