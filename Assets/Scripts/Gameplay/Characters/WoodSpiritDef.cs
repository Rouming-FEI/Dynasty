using Core.GAS;

namespace Gameplay.Characters
{
    public static class WoodSpiritDef
    {
        public static CharacterDef Create()
        {
            return new CharacterDef
            {
                Id = 10, Name = "木灵",
                BaseSanity = 3, BaseErosionLimit = 3,
                BasePhysicalDefense = 0, BaseMagicDefense = 0,
                InitialItem = 1,
                Skills = new[]
                {
                    // 瘟疫之源（被动：理智非满时同地点角色每回合末受 0.5 法术效果伤，不含自己）
                    new SkillDef
                    {
                        Id = 80, Name = "瘟疫之源",
                        Activation = SkillActivationType.Passive,
                        PassiveInterval = 1,
                        PassiveEffects = new[]
                        {
                            new GameplayEffect
                            {
                                Id = 300,
                                DurationType = GameplayEffectDurationType.Instant,
                                Modifiers = System.Array.Empty<AttributeModifier>()
                            }
                        }
                    },
                    // 续存（1AP + 1 侵蚀：安置种子于当前位置；死亡时复活）
                    new SkillDef
                    {
                        Id = 81, Name = "续存",
                        Cost = new SkillCost(SkillCostType.AP, 1),
                        ExtraCosts = new[] { new SkillCost(SkillCostType.ErosionLimit, 1) },
                        Activation = SkillActivationType.Active,
                        Target = SkillTargetType.Self
                    },
                    // 生命之火（0AP 开关：每回合侵蚀 -0.5、AP +1；净化药剂中止）
                    new SkillDef
                    {
                        Id = 82, Name = "生命之火",
                        Activation = SkillActivationType.Active,
                        Target = SkillTargetType.Self,
                        BlockingTags = new[] { GameplayTagConstants.LifeFire },
                        Effects = new[]
                        {
                            new GameplayEffect
                            {
                                Id = 301,
                                DurationType = GameplayEffectDurationType.HasDuration,
                                DurationTurns = 999,
                                Period = new GameplayEffectPeriod(1),
                                Modifiers = new[]
                                {
                                    new AttributeModifier(AttributeConstants.ErosionLimit, AttributeModifierOp.Add, -0.5f)
                                },
                                ExpiryPolicy = GameplayEffectExpiryPolicy.RemoveOnCondition,
                                ConditionTags = new[] { GameplayTagConstants.LifeFire },
                                GrantTags = new[] { GameplayTagConstants.LifeFire }
                            }
                        }
                    }
                }
            };
        }
    }
}
