using Core.GAS;

namespace Gameplay.Characters
{
    public static class TimeThiefDef
    {
        public static CharacterDef Create()
        {
            return new CharacterDef
            {
                Id = 7, Name = "时间盗贼",
                BaseSanity = 3, BaseErosionLimit = 3,
                BasePhysicalDefense = 0, BaseMagicDefense = 0,
                InitialItem = 1,
                Skills = new[]
                {
                    new SkillDef
                    {
                        Id = 50, Name = "静止时空",
                        Cost = new SkillCost(SkillCostType.Sanity, 1),
                        Activation = SkillActivationType.Active,
                        Target = SkillTargetType.Self,
                        Restrictions = new[] { new SkillRestriction(AttributeConstants.Sanity, 1.5f) },
                        BlockingTags = new[] { GameplayTagConstants.TimeStop },
                        Effects = new[]
                        {
                            new GameplayEffect
                            {
                                Id = 250,
                                DurationType = GameplayEffectDurationType.HasDuration,
                                DurationTurns = 2,
                                GrantTags = new[] { GameplayTagConstants.TimeStop }
                            }
                        }
                    }
                }
            };
        }
    }
}
