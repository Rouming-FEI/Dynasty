using Core;
using Gameplay.Characters;
using Gameplay.Combat;

namespace Core.GAS
{
    /// <summary>
    /// 统一伤害管线：免疫检查 → 攻击加成 → 护盾 → 抗性减免 → 应用 GE → 事件 → 死亡检查。
    /// 所有伤害来源（普攻、技能、道具、效果）统一走此入口，保证结算顺序确定。
    /// </summary>
    public static class DamagePipeline
    {
        /// <summary>返回实际造成伤害值（减免后），0 表示被免疫或被护盾抵挡。</summary>
        public static float ApplyDamage(
            WorldState state,
            int sourceId, int targetId,
            float baseDamage,
            DamageType damageType)
        {
            Character source = state.GetCharacter(sourceId);
            Character target = state.GetCharacter(targetId);
            if (target == null || target.IsDead) return 0f;

            AbilitySystemComponent targetASC = target.ASC;

            // 1. 免疫检查（真实伤害无视一切免疫）
            if (damageType != DamageType.True)
            {
                if (targetASC.Tags.HasTag(GameplayTagConstants.ImmuneDamage)) return 0f;
                if (damageType == DamageType.Physical && targetASC.Tags.HasTag(GameplayTagConstants.ImmunePhysical)) return 0f;
                if (damageType == DamageType.Magic && targetASC.Tags.HasTag(GameplayTagConstants.ImmuneMagic)) return 0f;
            }

            // 2. 伤害计算：基础 + 攻击加成（仅物理/法术）
            float finalDamage = baseDamage;
            if (damageType == DamageType.Physical || damageType == DamageType.Magic)
                finalDamage += source?.ASC.GetAttributeValue(AttributeConstants.AttackBonus) ?? 0f;

            // 3. 护盾检查：吃掉本次伤害并破盾（真实伤害穿盾）
            if (damageType != DamageType.True && targetASC.Tags.HasTag(GameplayTagConstants.HasShield))
            {
                targetASC.Tags.RemoveTag(GameplayTagConstants.HasShield);
                var shieldEvent = new GameplayEvent(GameplayEventTypes.OnShieldBroken, sourceId, targetId);
                state.Events.Dispatch(GameplayEventTypes.OnShieldBroken, ref shieldEvent);

                // 护盾吸收也派发 DamageDealt（Magnitude = 被吸收的伤害值），保证战斗日志完整
                var absorbedEvt = new GameplayEvent(GameplayEventTypes.DamageDealt, sourceId, targetId)
                {
                    Magnitude = finalDamage,
                    IntArg1 = (int)damageType
                };
                state.Events.Dispatch(GameplayEventTypes.DamageDealt, ref absorbedEvt);
                return 0f;
            }

            // 4. 抗性减免（物理/法术），Effect / True 跳过减免
            if (damageType == DamageType.Physical)
                finalDamage -= targetASC.GetAttributeValue(AttributeConstants.PhysicalDefense);
            else if (damageType == DamageType.Magic)
                finalDamage -= targetASC.GetAttributeValue(AttributeConstants.MagicDefense);

            if (finalDamage < 0f) finalDamage = 0f;

            // 5. 应用伤害 GE（modifier 为 Sanity Add -1，乘以正的 finalDamage 得到负增量）
            GameplayEffectSpec spec = targetASC.RentSpec();
            spec.Definition = GetDamageGE(damageType);
            spec.SourceId = sourceId;
            spec.TargetId = targetId;
            spec.MagnitudeMultiplier = finalDamage;
            targetASC.ApplyGameplayEffect(spec);

            // 6. PostDamage — ASC internal + global event
            targetASC.SendEvent(GameplayEventTypes.OnDamageTaken, sourceId, targetId, finalDamage);
            var dmgEvt = new GameplayEvent(GameplayEventTypes.DamageDealt, sourceId, targetId)
            {
                Magnitude = finalDamage,
                IntArg1 = (int)damageType
            };
            state.Events.Dispatch(GameplayEventTypes.DamageDealt, ref dmgEvt);

            // 7. 死亡检查
            if (target.Sanity <= 0f)
                state.Commands.Enqueue(new DeathCommand(state.CurrentTick, target.PlayerId, source?.PlayerId ?? -1));

            return finalDamage;
        }

        private static GameplayEffect _cachedDamageGE;

        private static GameplayEffect GetDamageGE(DamageType type)
        {
            // 缓存一份 GE 模板：Sanity Add -1，实际数值由 MagnitudeMultiplier 缩放
            if (_cachedDamageGE == null)
            {
                _cachedDamageGE = new GameplayEffect
                {
                    Id = 2,
                    DurationType = GameplayEffectDurationType.Instant,
                    Modifiers = new[] { new AttributeModifier(AttributeConstants.Sanity, AttributeModifierOp.Add, -1f) },
                };
            }
            return _cachedDamageGE;
        }
    }
}
