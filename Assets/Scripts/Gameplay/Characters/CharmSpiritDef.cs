using Core.GAS;

namespace Gameplay.Characters
{
    public static class CharmSpiritDef
    {
        public static CharacterDef Create()
        {
            return new CharacterDef
            {
                Id = 2, Name = "魑魅",
                BaseSanity = 3, BaseErosionLimit = 3,
                BasePhysicalDefense = 0, BaseMagicDefense = 0,
                InitialItem = 1, // 刀
                Skills = new[]
                {
                    // 迷离之境 (主动, 1AP 开关)
                    new SkillDef
                    {
                        Id = 10, Name = "迷离之境",
                        Cost = new SkillCost(SkillCostType.AP, 1),
                        Activation = SkillActivationType.Active,
                        Effects = new[]
                        {
                            new GameplayEffect
                            {
                                Id = 210,
                                DurationType = GameplayEffectDurationType.HasDuration,
                                DurationTurns = 999, // 持续直到退出
                                ExpiryPolicy = GameplayEffectExpiryPolicy.RemoveOnCondition,
                                GrantTags = new[] { GameplayTagConstants.MistState }
                            }
                        }
                    },

                    // 鬼葬 (主动, 1AP, 3回合)
                    new SkillDef
                    {
                        Id = 11, Name = "鬼葬",
                        Cost = new SkillCost(SkillCostType.AP, 1),
                        Activation = SkillActivationType.Active,
                        Effects = new[]
                        {
                            new GameplayEffect
                            {
                                Id = 211,
                                DurationType = GameplayEffectDurationType.HasDuration,
                                DurationTurns = 3,
                                Period = new GameplayEffectPeriod(1), // 每回合末
                                // 具体效果由鬼葬系统计算（地点人数相关）
                            }
                        }
                    },
                }
            };
        }
    }
}
