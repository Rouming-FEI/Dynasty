using Core;
using Core.GAS;

namespace Gameplay.Characters
{
    /// <summary>
    /// 技能激活逻辑 — 前置校验（Tag 条件 + AP 消耗）→ 支付消耗 → 对目标应用 GE。
    /// 纯静态类，无状态；Hot Path 零 GC（数组 for 循环 + Spec 池化）。
    /// </summary>
    public static class SkillSystem
    {
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
                }
            }
        }
    }
}
