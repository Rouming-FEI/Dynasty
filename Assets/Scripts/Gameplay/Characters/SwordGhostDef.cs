using Core.GAS;

namespace Gameplay.Characters
{
    public static class SwordGhostDef
    {
        public static CharacterDef Create()
        {
            return new CharacterDef
            {
                Id = 4, Name = "剑鬼",
                BaseSanity = 3, BaseErosionLimit = 4.5f,
                BasePhysicalDefense = 0, BaseMagicDefense = 0,
                InitialItem = 1, // 刀
                Skills = new[]
                {
                    // 飞剑（1AP，单独判定，胜 1.5 物理伤；鬼化后 +0.5 攻击加成自动生效）
                    new SkillDef
                    {
                        Id = 20, Name = "飞剑",
                        Cost = new SkillCost(SkillCostType.AP, 1),
                        Activation = SkillActivationType.Active,
                        Target = SkillTargetType.Enemy,
                        Judgment = new SkillJudgment
                        {
                            OnWinEffects = null, // 伤害由 FlyingSwordCommand 走管线（吃攻击加成）
                            OnLoseEffects = null,
                            CancelOnLose = true
                        }
                    },
                    // 绝症（被动：每回合末侵蚀 -0.5，最低 0.5；鬼化取消）
                    new SkillDef
                    {
                        Id = 21, Name = "绝症",
                        Activation = SkillActivationType.Passive,
                        PassiveInterval = 1,
                        PassiveEffects = new[]
                        {
                            new GameplayEffect
                            {
                                Id = 220,
                                DurationType = GameplayEffectDurationType.Instant,
                                Modifiers = System.Array.Empty<AttributeModifier>()
                            }
                        }
                    },
                    // 鬼化（1AP 永久：防御+0.5、攻击+0.5；取消绝症；禁应急装置；禁武器/普攻）
                    new SkillDef
                    {
                        Id = 22, Name = "鬼化",
                        Cost = new SkillCost(SkillCostType.AP, 1),
                        Activation = SkillActivationType.Active,
                        Target = SkillTargetType.Self,
                        BlockingTags = new[] { GameplayTagConstants.GhostForm },
                        Effects = new[]
                        {
                            new GameplayEffect
                            {
                                Id = 221,
                                DurationType = GameplayEffectDurationType.HasDuration,
                                DurationTurns = 999,
                                Modifiers = new[]
                                {
                                    new AttributeModifier(AttributeConstants.PhysicalDefense, AttributeModifierOp.Add, 0.5f),
                                    new AttributeModifier(AttributeConstants.AttackBonus, AttributeModifierOp.Add, 0.5f)
                                },
                                GrantTags = new[] { GameplayTagConstants.GhostForm }
                            }
                        }
                    },
                }
            };
        }
    }
}
