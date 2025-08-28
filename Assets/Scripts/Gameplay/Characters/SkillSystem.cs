using Core;
using Core.GAS;
using Gameplay.Combat;

namespace Gameplay.Characters
{
    /// <summary>
    /// 技能激活逻辑 — 前置校验（Tag 条件 + AP 消耗）→ 支付消耗 → 对目标应用 GE。
    /// 纯静态类，无状态；Hot Path 零 GC（数组 for 循环 + Spec 池化）。
    /// </summary>
    public static class SkillSystem
    {
        /// <summary>守护技能 Id（GuardianDef 中定义，全技能表内唯一）。</summary>
        private const int GuardianSkillId = 2;
        /// <summary>守护充能阈值：攒满 3 充能兑换 1 护盾。</summary>
        private const int GuardianChargeThreshold = 3;
        /// <summary>技能是否可激活：仅限主动技能、施放者存活可行动、满足 RequiredTags、不含 BlockingTags、AP 足够。</summary>
        public static bool CanActivate(SkillDef skill, Character c)
        {
            if (skill == null || c == null || !c.CanAct) return false;
            if (skill.Activation != SkillActivationType.Active) return false; // 被动/响应型技能不可主动施放
            if (skill.RequiredTags != null && !c.ASC.Tags.HasAll(skill.RequiredTags)) return false;
            if (skill.BlockingTags != null && c.ASC.Tags.HasAny(skill.BlockingTags)) return false;
            if (skill.Cost.Type == SkillCostType.AP && c.ActionPoints < skill.Cost.Amount) return false;
            return true;
        }

        /// <summary>激活技能：支付消耗后，将技能携带的全部 GE 应用到目标身上。</summary>
        public static void Activate(SkillDef skill, Character c, Character target, WorldState state)
        {
            if (!CanActivate(skill, c)) return;
            if (target == null || target.IsDead) return;

            // 支付消耗
            if (skill.Cost.Type == SkillCostType.AP)
                c.ASC.ModifyAttributeCurrentValue(AttributeConstants.ActionPoints, -skill.Cost.Amount);

            // 守护 (Id=2) 充能机制：总价 3AP 可攒 —— 每次施放 1AP 攒 1 充能
            // （充能跨回合保留），攒满 3 充能兑换 1 护盾并清零。
            // 护盾统一走 ShieldSystem.GrantShield，保证 OnShieldGranted 事件派发。
            if (skill.Id == GuardianSkillId)
            {
                c.ASC.AddGuardianCharge();
                if (c.ASC.GetGuardianCharge() >= GuardianChargeThreshold)
                {
                    ShieldSystem.GrantShield(state, target.ASC, c.Id);
                    c.ASC.ResetGuardianCharge();
                }
                return; // 充能技能不携带 GE，无需走通用应用流程
            }

            // 应用效果
            if (skill.Effects != null)
            {
                for (int i = 0; i < skill.Effects.Length; i++)
                {
                    GameplayEffect ge = skill.Effects[i];
                    if (ge == null) continue;

                    GameplayEffectSpec spec = target.ASC.RentSpec();
                    spec.Definition = ge;
                    spec.SourceId = c.Id;
                    spec.TargetId = target.Id;
                    spec.RemainingTurns = ge.DurationTurns;
                    target.ASC.ApplyGameplayEffect(spec);

                    // 授予护盾的 GE 统一补走 ShieldSystem.GrantShield，
                    // 保证 OnShieldGranted 事件（ASC 内部 + 全局）正常派发。
                    // Tag 容器为位掩码，重复 AddTag 幂等，无副作用。
                    if (ge.GrantTags != null)
                    {
                        for (int t = 0; t < ge.GrantTags.Length; t++)
                        {
                            if (ge.GrantTags[t] == GameplayTagConstants.HasShield)
                            {
                                ShieldSystem.GrantShield(state, target.ASC, c.Id);
                                break;
                            }
                        }
                    }
                }
            }
        }
    }
}
