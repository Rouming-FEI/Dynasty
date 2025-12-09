using Core.GAS;

namespace Gameplay.Characters
{
    public static class ListenerDef
    {
        public static CharacterDef Create()
        {
            return new CharacterDef
            {
                Id = 8, Name = "倾听者",
                BaseSanity = 3, BaseErosionLimit = 4,
                BasePhysicalDefense = 0, BaseMagicDefense = 0,
                InitialItem = 1,
                Skills = new[]
                {
                    // 地狱的呓语（周期被动：每 2 回合与一人判定，胜 +0.5 攻击，败 侵蚀 -1）
                    new SkillDef
                    {
                        Id = 60, Name = "地狱的呓语",
                        Activation = SkillActivationType.Passive,
                        PassiveInterval = 2,
                        PassiveEffects = new[]
                        {
                            new GameplayEffect
                            {
                                Id = 260,
                                DurationType = GameplayEffectDurationType.Instant,
                                Modifiers = System.Array.Empty<AttributeModifier>()
                            }
                        }
                    },
                    // 呓语击杀回复（Reactive OnKill：理智与侵蚀上限回复至 4）
                    new SkillDef
                    {
                        Id = 61, Name = "呓语击杀回复",
                        Activation = SkillActivationType.Reactive,
                        ReactiveTrigger = ReactiveTriggerType.OnKill
                    }
                }
            };
        }
    }
}
