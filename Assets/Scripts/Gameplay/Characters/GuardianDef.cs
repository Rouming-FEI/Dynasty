using Core.GAS;

namespace Gameplay.Characters
{
    public static class GuardianDef
    {
        public static CharacterDef Create()
        {
            return new CharacterDef
            {
                Id = 1, Name = "守护者",
                BaseSanity = 3, BaseErosionLimit = 3,
                BasePhysicalDefense = 0, BaseMagicDefense = 0,
                InitialItem = 1, // 刀
                Skills = new[]
                {
                    // 重装 (被动) — StartGame 时通过 ASC 加 HasShield 标签
                    new SkillDef { Id = 1, Name = "重装", Activation = SkillActivationType.Passive },

                    // 守护 (主动, 3AP 可攒) — 每次消耗 1AP 攒 1 充能（跨回合保留），
                    // 攒满 3 充能自动兑换 1 护盾并清零。充能与兑换逻辑见 SkillSystem.Activate，
                    // 故此处不携带 GE（护盾统一由 ShieldSystem.GrantShield 授予）。
                    new SkillDef
                    {
                        Id = 2, Name = "守护",
                        Cost = new SkillCost(SkillCostType.AP, 1),
                        Activation = SkillActivationType.Active,
                        Target = SkillTargetType.Self, // 自 Buff：AI 只对自己施放
                    },

                    // 守护之光 (被动, 4充能) — 每次攻击 +1 充能，满 4 自动触发
                    new SkillDef
                    {
                        Id = 3, Name = "守护之光", Activation = SkillActivationType.Reactive,
                        CooldownTurns = 0,
                        Effects = new[]
                        {
                            new GameplayEffect
                            {
                                Id = 200, DurationType = GameplayEffectDurationType.HasDuration,
                                DurationTurns = 3,
                                Modifiers = new[]
                                {
                                    new AttributeModifier(AttributeConstants.PhysicalDefense, AttributeModifierOp.Add, 0.5f),
                                    new AttributeModifier(AttributeConstants.Sanity, AttributeModifierOp.Add, 0f) // damage reduction handled by PreDamage
                                },
                                GrantTags = new[] { GameplayTagConstants.HasShield }
                            }
                        }
                    },
                }
            };
        }
    }
}
