using Core;
using Core.GAS;
using Gameplay.Characters;

namespace Gameplay.Items
{
    /// <summary>
    /// 应急处理装置效果：使用时依次应用 3 个 GameplayEffect。
    /// 1. HoT GE: Period(1) x 5 turns, Sanity +0.5/turn
    /// 2. DeathSave GE: Duration=5, 标签 DeathSaveActive（DamagePipeline 中检查）
    /// 3. ErosionUp GE: Instant, ErosionLimit +1
    /// </summary>
    public static class EmergencyDeviceEffect
    {
        private static GameplayEffect _hotGE;
        private static GameplayEffect _deathSaveGE;
        private static GameplayEffect _erosionUpGE;

        public static void Apply(WorldState state, Character user, bool addErosionLimit = true)
        {
            // 1. HoT GE: Period(1) x 5 turns, Sanity +0.5/turn
            if (_hotGE == null)
            {
                _hotGE = new GameplayEffect
                {
                    Id = 107,
                    DurationType = GameplayEffectDurationType.HasDuration,
                    DurationTurns = 5,
                    Period = new GameplayEffectPeriod(1),
                    Modifiers = new[]
                    {
                        new AttributeModifier(AttributeConstants.Sanity, AttributeModifierOp.Add, 0.5f)
                    }
                };
            }

            GameplayEffectSpec hotSpec = user.ASC.RentSpec();
            hotSpec.Definition = _hotGE;
            hotSpec.SourceId = user.Id;
            hotSpec.TargetId = user.Id;
            user.ASC.ApplyGameplayEffect(hotSpec);

            // 2. DeathSave GE: Duration=5, 仅授予 DeathSaveActive 标签
            if (_deathSaveGE == null)
            {
                _deathSaveGE = new GameplayEffect
                {
                    Id = 108,
                    DurationType = GameplayEffectDurationType.HasDuration,
                    DurationTurns = 5,
                    GrantTags = new[] { GameplayTagConstants.DeathSaveActive }
                };
            }

            GameplayEffectSpec deathSaveSpec = user.ASC.RentSpec();
            deathSaveSpec.Definition = _deathSaveGE;
            deathSaveSpec.SourceId = user.Id;
            deathSaveSpec.TargetId = user.Id;
            user.ASC.ApplyGameplayEffect(deathSaveSpec);

            // 3. ErosionUp GE: Instant, ErosionLimit +1（阵法回路 addErosionLimit=false 时跳过）
            if (addErosionLimit)
            {
                if (_erosionUpGE == null)
                {
                    _erosionUpGE = new GameplayEffect
                    {
                        Id = 109,
                        DurationType = GameplayEffectDurationType.Instant,
                        Modifiers = new[]
                        {
                            new AttributeModifier(AttributeConstants.ErosionLimit, AttributeModifierOp.Add, 1f)
                        }
                    };
                }

                GameplayEffectSpec erosionSpec = user.ASC.RentSpec();
                erosionSpec.Definition = _erosionUpGE;
                erosionSpec.SourceId = user.Id;
                erosionSpec.TargetId = user.Id;
                user.ASC.ApplyGameplayEffect(erosionSpec);
            }
        }
    }
}
