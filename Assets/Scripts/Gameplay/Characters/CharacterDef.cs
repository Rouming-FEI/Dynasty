using Core.GAS;

namespace Gameplay.Characters
{
    public class CharacterDef
    {
        public int Id;
        public string Name;
        public float BaseSanity;
        public float BaseErosionLimit;
        public float BasePhysicalDefense;
        public float BaseMagicDefense;
        public SkillDef[] Skills;
        public int InitialItem; // starting item (0=none)

        public void ApplyTo(Character c)
        {
            c.Name = Name;
            c.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, BaseSanity);
            c.ASC.SetAttributeBaseValue(AttributeConstants.MaxSanity, BaseSanity);
            c.ASC.SetAttributeBaseValue(AttributeConstants.ErosionLimit, BaseErosionLimit);
            c.ASC.SetAttributeBaseValue(AttributeConstants.PhysicalDefense, BasePhysicalDefense);
            c.ASC.SetAttributeBaseValue(AttributeConstants.MagicDefense, BaseMagicDefense);
        }
    }
}
