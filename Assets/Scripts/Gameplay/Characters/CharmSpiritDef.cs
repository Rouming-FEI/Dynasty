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
                        Target = SkillTargetType.Self, // 自 Buff：进入迷离状态的是施放者自己

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

                    // 鬼葬 (主动, 1AP, 3回合) — 按所在地人数三档联动结算（docs/项目介绍.md 7.12）
                    new SkillDef
                    {
                        Id = 11, Name = "鬼葬",
                        Cost = new SkillCost(SkillCostType.AP, 1),
                        Activation = SkillActivationType.Active,
                        Target = SkillTargetType.Self,
                        Effects = new[]
                        {
                            new GameplayEffect
                            {
                                Id = 211,
                                DurationType = GameplayEffectDurationType.HasDuration,
                                DurationTurns = 3,
                                Period = new GameplayEffectPeriod(1), // 每回合末
                                Modifiers = System.Array.Empty<AttributeModifier>()
                            }
                        }
                    },

                    // 心雾周期 (被动, 每回合末) — 迷离之境的周期负载：2 层 -0.5 / 3 层 -1 并退出（docs/项目介绍.md 7.12）
                    new SkillDef
                    {
                        Id = 12, Name = "心雾周期",
                        Activation = SkillActivationType.Passive,
                        PassiveInterval = 1,
                        PassiveEffects = new[]
                        {
                            new GameplayEffect
                            {
                                Id = 280,
                                DurationType = GameplayEffectDurationType.Instant,
                                Modifiers = System.Array.Empty<AttributeModifier>()
                            }
                        }
                    },
                }
            };
        }
    }
}
