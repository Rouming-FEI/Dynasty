using Core.GAS;

namespace Gameplay.Characters
{
    public static class AgentDef
    {
        public static CharacterDef Create()
        {
            return new CharacterDef
            {
                Id = 13, Name = "代行者",
                BaseSanity = 3, BaseErosionLimit = 3,
                BasePhysicalDefense = 0, BaseMagicDefense = 0,
                InitialItem = 3, // 枪
                Skills = new[]
                {
                    new SkillDef { Id = 110, Name = "孤狼", Activation = SkillActivationType.Passive, PassiveInterval = 2,
                        PassiveEffects = new[] { new GameplayEffect { Id = 330, DurationType = GameplayEffectDurationType.Instant, Modifiers = System.Array.Empty<AttributeModifier>() } } },
                    new SkillDef { Id = 111, Name = "主的意志", Activation = SkillActivationType.Passive, PassiveInterval = 3,
                        PassiveEffects = new[] { new GameplayEffect { Id = 331, DurationType = GameplayEffectDurationType.Instant, Modifiers = System.Array.Empty<AttributeModifier>() } } },
                    new SkillDef { Id = 112, Name = "主的庇护", Activation = SkillActivationType.Passive },
                    new SkillDef { Id = 113, Name = "主的苏醒", Activation = SkillActivationType.Passive }
                }
            };
        }
    }
}
