using Core.GAS;

namespace Gameplay.Characters
{
    public static class NormalPersonDef
    {
        public static CharacterDef Create()
        {
            return new CharacterDef
            {
                Id = 3, Name = "普通人",
                BaseSanity = 3, BaseErosionLimit = 3,
                BasePhysicalDefense = 0, BaseMagicDefense = 0,
                InitialItem = 1, // 刀
                // 无任何技能——只靠普攻和道具
                Skills = System.Array.Empty<SkillDef>()
            };
        }
    }
}
