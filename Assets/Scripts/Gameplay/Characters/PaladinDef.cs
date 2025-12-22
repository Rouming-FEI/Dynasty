using Core.GAS;

namespace Gameplay.Characters
{
    public static class PaladinDef
    {
        public static CharacterDef Create()
        {
            return new CharacterDef
            {
                Id = 12, Name = "圣骑士",
                BaseSanity = 3, BaseErosionLimit = 3,
                BasePhysicalDefense = 0, BaseMagicDefense = 0,
                InitialItem = 1,
                Skills = new[]
                {
                    // 持盾（被动：下潜入口有盾掉落，拾取后 +0.5 防御——EquipEffect 激活）
                    new SkillDef { Id = 100, Name = "持盾", Activation = SkillActivationType.Passive },
                    // 守护之光（4 充能：为任意角色施加下次受伤 -2，3 回合）
                    new SkillDef
                    {
                        Id = 101, Name = "守护之光",
                        Cost = new SkillCost(SkillCostType.Charge, 4),
                        Activation = SkillActivationType.Active,
                        Target = SkillTargetType.Any,
                        ChargeThreshold = 4
                    },
                    // 骑士的精神（1AP 开关：同地点他人受伤转移至你；受伤 +1 充能、攻击 +2 充能）
                    new SkillDef
                    {
                        Id = 102, Name = "骑士的精神",
                        Cost = new SkillCost(SkillCostType.AP, 1),
                        Activation = SkillActivationType.Active,
                        Target = SkillTargetType.Self,
                        BlockingTags = new[] { GameplayTagConstants.KnightSpirit },
                        Effects = new[]
                        {
                            new GameplayEffect
                            {
                                Id = 320,
                                DurationType = GameplayEffectDurationType.HasDuration,
                                DurationTurns = 999,
                                ExpiryPolicy = GameplayEffectExpiryPolicy.RemoveOnCondition,
                                ConditionTags = new[] { GameplayTagConstants.KnightSpirit },
                                GrantTags = new[] { GameplayTagConstants.KnightSpirit }
                            }
                        }
                    },
                    // 护佑（0AP：当回合无敌 + 充能 +1；全局仅一次）
                    new SkillDef
                    {
                        Id = 103, Name = "护佑",
                        Activation = SkillActivationType.Active,
                        Target = SkillTargetType.Self
                    }
                }
            };
        }
    }
}
