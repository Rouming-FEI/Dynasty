using Core.GAS;

namespace Gameplay.Characters
{
    /// <summary>
    /// 数据幽灵（Id=16，侵蚀 3 / 普攻 1）。docs/项目介绍.md 7.15。
    /// 技能槽位：[0] 形态切换 125 / [1] Harmonics 126 / [2] 规诫 127 / [3] 行向深渊 128 / [4] 分身负载 129。
    /// </summary>
    public static class DataGhostDef
    {
        public static CharacterDef Create()
        {
            return new CharacterDef
            {
                Id = SkillSystem.DataGhostCharacterId, Name = "数据幽灵",
                BaseSanity = 3, BaseErosionLimit = 3,
                BasePhysicalDefense = 0, BaseMagicDefense = 0,
                InitialItem = 0, // 形态自带武器语义（无实体武器）
                Skills = new[]
                {
                    // Handsonic·形态切换（0 AP Self，每回合 1 次：BlockingTags 拦截二次切换）
                    new SkillDef
                    {
                        Id = SkillSystem.HandsonicSkillId, Name = "Handsonic·切换",
                        Cost = new SkillCost(SkillCostType.AP, 0),
                        Activation = SkillActivationType.Active,
                        Target = SkillTargetType.Self,
                        BlockingTags = new[] { GameplayTagConstants.FormSwitched }
                    },
                    // Harmonics（1 AP Self）：制造分身（WorldState.GhostCloneActive/Sanity，Task 5）
                    new SkillDef
                    {
                        Id = SkillSystem.HarmonicsSkillId, Name = "Harmonics",
                        Cost = new SkillCost(SkillCostType.AP, 1),
                        Activation = SkillActivationType.Active,
                        Target = SkillTargetType.Self
                    },
                    // 规诫（Reactive OnPreDamage）：受击解锁形态（ReactiveSystem case 127）
                    new SkillDef
                    {
                        Id = SkillSystem.DisciplineSkillId, Name = "规诫",
                        Activation = SkillActivationType.Reactive,
                        ReactiveTrigger = ReactiveTriggerType.OnPreDamage
                    },
                    // 行向深渊（Reactive OnKill）：杀人判定（ReactiveSystem case 128，Task 5）
                    new SkillDef
                    {
                        Id = SkillSystem.AbyssSkillId, Name = "行向深渊",
                        Activation = SkillActivationType.Reactive,
                        ReactiveTrigger = ReactiveTriggerType.OnKill
                    },
                    // 分身每回合末扣 1 理智（周期被动载体；负载 GE Id=341 由 ASC TickEffects Intercept 执行）
                    new SkillDef
                    {
                        Id = SkillSystem.CloneDrainSkillId, Name = "分身负载",
                        Activation = SkillActivationType.Passive,
                        PassiveInterval = 1,
                        PassiveEffects = new[]
                        {
                            new GameplayEffect
                            {
                                Id = 341,
                                DurationType = GameplayEffectDurationType.Instant,
                                Modifiers = System.Array.Empty<AttributeModifier>()
                            }
                        }
                    }
                }
            };
        }
    }
}
