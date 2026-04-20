using Core;
using Gameplay.Characters;
using Gameplay.Combat;

namespace Core.GAS
{
    /// <summary>
    /// 统一伤害管线：免疫检查 → 攻击加成 → OnPreDamage → 护盾 → 抗性减免 → 应用 GE → 事件 → 死亡检查。
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

            // 骑士的精神：同地点开启者（非伤害来源、非目标本身）转移伤害
            if (source != null && !target.ASC.Tags.HasTag(GameplayTagConstants.KnightSpirit))
            {
                Character knight = null;
                for (int i = 0; i < state.Characters.Length; i++)
                {
                    Character ch = state.Characters[i];
                    if (ch == null || ch.IsDead || ch.Id == source.Id || ch.Id == target.Id) continue;
                    if (ch.CurrentLocationId == target.CurrentLocationId
                        && ch.ASC.Tags.HasTag(GameplayTagConstants.KnightSpirit))
                    { knight = ch; break; }
                }
                if (knight != null) target = knight;
            }

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

            // 2.5 响应式前置钩子（OnPreDamage）：攻击加成后调用，响应技能可抵消或增伤（如镜花水月）
            ReactiveSystem.TryOnPreDamage(state, source, target, damageType, ref finalDamage);

            // 2.6 来源侧攻击判定（魑魅迷离之境）：胜 → 本次伤害 + 1/2；败 → 心雾 +1
            ReactiveSystem.TryOnAttack(state, source, target, ref finalDamage);

            // 3. 护盾检查：吃掉本次伤害并破盾（真实伤害穿盾）
            if (damageType != DamageType.True && targetASC.Tags.HasTag(GameplayTagConstants.HasShield))
            {
                targetASC.Tags.RemoveTag(GameplayTagConstants.HasShield);

                // 护盾抵挡伤害后回复 1 点理智（SetCurrentValue 已内置 MaxSanity 钳制）
                targetASC.ModifyAttributeCurrentValue(AttributeConstants.Sanity, 1f);

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

            // 数据幽灵 V1 音刃形态：物理防御 +0.5（标签等价线性减伤，仅 V1 持有期间）
            if (damageType == DamageType.Physical
                && targetASC.Tags.HasTag(GameplayTagConstants.V1Form))
                finalDamage -= 0.5f;

            // 圣骑士守护之光：下次受伤 -2，一次性消耗（移除标签与 GE）
            if (targetASC.Tags.HasTag(GameplayTagConstants.GuardLight))
            {
                finalDamage -= 2f;
                targetASC.Tags.RemoveTag(GameplayTagConstants.GuardLight);
                targetASC.RemoveEffectsById(322);
            }

            // 海幻归鞘持刀：0.5 全减伤（物理/法术；Effect/True 不受）
            if ((damageType == DamageType.Physical || damageType == DamageType.Magic)
                && targetASC.Tags.HasTag(GameplayTagConstants.BladeSheathed))
                finalDamage -= 0.5f;

            // 迷离之境心雾一层：受到物理伤害 +0.5
            if (damageType == DamageType.Physical
                && targetASC.Tags.HasTag(GameplayTagConstants.MistState)
                && targetASC.GetMistLayers() >= 1)
                finalDamage += 0.5f;

            // 阵法大师血炼：自动消耗 1 阵盘减伤 1（心雾增伤之后）
            if (targetASC.GetBloodSeals() > 0 && finalDamage >= 1f)
            {
                targetASC.ModifyBloodSeals(-1);
                finalDamage -= 1f;
            }

            if (finalDamage < 0f) finalDamage = 0f;

            // 4.5 死亡豁免 — 应急处理装置（伤害应用前检查，避免死后再救）
            if (target.ASC.Tags.HasTag(GameplayTagConstants.DeathSaveActive) && target.Sanity <= finalDamage)
            {
                target.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 0.5f);
                target.ASC.ModifyAttributeCurrentValue(AttributeConstants.Sanity, 0f); // trigger clamp
                target.ASC.Tags.RemoveTag(GameplayTagConstants.DeathSaveActive);

                var saveEvt = new GameplayEvent(GameplayEventTypes.OnDamageTaken, sourceId, targetId)
                {
                    Magnitude = finalDamage,
                    IntArg1 = 1  // 1 = death was saved
                };
                state.Events.Dispatch(GameplayEventTypes.OnDamageTaken, ref saveEvt);

                return 0f; // damage was saved
            }

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
            {
                ReactiveSystem.TryOnKill(state, source, target);
                state.Commands.Enqueue(new DeathCommand(state.CurrentTick, target.PlayerId, source?.PlayerId ?? -1));
            }

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
