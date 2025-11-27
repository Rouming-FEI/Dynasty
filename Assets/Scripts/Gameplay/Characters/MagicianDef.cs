using Core.GAS;

namespace Gameplay.Characters
{
    public static class MagicianDef
    {
        public static CharacterDef Create()
        {
            return new CharacterDef
            {
                Id = 5, Name = "魔术师",
                BaseSanity = 3, BaseErosionLimit = 3.5f,
                BasePhysicalDefense = 0, BaseMagicDefense = 0,
                InitialItem = 1,
                Skills = new[]
                {
                    new SkillDef
                    {
                        Id = 30, Name = "镜花水月",
                        Activation = SkillActivationType.Reactive,
                        ReactiveTrigger = ReactiveTriggerType.OnPreDamage
                    }
                }
            };
        }
    }
}
