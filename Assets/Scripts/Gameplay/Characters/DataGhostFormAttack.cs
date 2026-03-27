using Core;
using Core.GAS;
using Gameplay.Combat;

namespace Gameplay.Characters
{
    /// <summary>
    /// 数据幽灵形态系统：当前形态标签（V1Form..V5Form 独占）+ 永久解锁标签（V2Unlocked..V5Unlocked）。
    /// 普通攻击统一由 DamageCommand.Execute 按角色 16 分派到本类（形态自带武器语义）。
    /// 本文件亦承载 Task 5 的镜像双发（MirrorCopy 由 Task 5 在该 Execute 尾部补调用点）。
    /// </summary>
    public static class DataGhostFormAttack
    {
        /// <summary>读取当前形态（1-5）；无标签兜底 V1。</summary>
        public static int CurrentForm(Character c)
        {
            if (c.ASC.Tags.HasTag(GameplayTagConstants.V5Form)) return 5;
            if (c.ASC.Tags.HasTag(GameplayTagConstants.V4Form)) return 4;
            if (c.ASC.Tags.HasTag(GameplayTagConstants.V3Form)) return 3;
            if (c.ASC.Tags.HasTag(GameplayTagConstants.V2Form)) return 2;
            return 1;
        }

        /// <summary>形态是否已解锁（V1 恒解锁）。</summary>
        public static bool IsUnlocked(Character c, int form)
        {
            switch (form)
            {
                case 1: return true;
                case 2: return c.ASC.Tags.HasTag(GameplayTagConstants.V2Unlocked);
                case 3: return c.ASC.Tags.HasTag(GameplayTagConstants.V3Unlocked);
                case 4: return c.ASC.Tags.HasTag(GameplayTagConstants.V4Unlocked);
                case 5: return c.ASC.Tags.HasTag(GameplayTagConstants.V5Unlocked);
                default: return false;
            }
        }

        /// <summary>切换到「当前形态之后按序第一个已解锁形态」（V1→V2→V3→V4→V5→V1 循环）。
        /// 仅 V1 且无其他解锁 → 无效返回（不置 FormSwitched）。</summary>
        public static void SwitchForm(Character c)
        {
            int current = CurrentForm(c);
            int next = current;
            for (int step = 1; step <= 5; step++)
            {
                int cand = current + step;
                if (cand > 5) cand -= 5;
                if (IsUnlocked(c, cand)) { next = cand; break; }
            }
            if (next == current) return;

            c.ASC.Tags.RemoveTag(GameplayTagConstants.V1Form);
            c.ASC.Tags.RemoveTag(GameplayTagConstants.V2Form);
            c.ASC.Tags.RemoveTag(GameplayTagConstants.V3Form);
            c.ASC.Tags.RemoveTag(GameplayTagConstants.V4Form);
            c.ASC.Tags.RemoveTag(GameplayTagConstants.V5Form);
            switch (next)
            {
                case 2: c.ASC.Tags.AddTag(GameplayTagConstants.V2Form); break;
                case 3: c.ASC.Tags.AddTag(GameplayTagConstants.V3Form); break;
                case 4: c.ASC.Tags.AddTag(GameplayTagConstants.V4Form); break;
                case 5: c.ASC.Tags.AddTag(GameplayTagConstants.V5Form); break;
                default: c.ASC.Tags.AddTag(GameplayTagConstants.V1Form); break;
            }
            c.ASC.Tags.AddTag(GameplayTagConstants.FormSwitched);
        }

        /// <summary>
        /// 数据幽灵普攻行动（DamageCommand 分派入口；已通过 CanAct/AP>0/非鬼化守卫）。
        /// 各形态：V1 近战 1 / V2 近战 1.5 / V3 一次行动 3 连 0.5（目标中途死亡停）/
        /// V4 花 = 自愈 0.5（回复不走伤害管线，不触发规诫/荆棘等）/ V5 远程 1（任意地点、免弹药、
        /// 藏匿不可选 → 返回且不扣 AP）。全部消耗 1 AP（有效目标校验后扣）。
        /// </summary>
        public static void Execute(WorldState state, Character source, Character target)
        {
            int form = CurrentForm(source);

            // V5 藏匿拦截（与 WeaponSystem.RangedAttack 同规则：审判者无视藏匿）
            if (form == 5
                && target.ASC.Tags.HasTag(GameplayTagConstants.HideoutHidden)
                && !source.ASC.Tags.HasTag(GameplayTagConstants.HideoutHidden)
                && !source.ASC.Tags.HasTag(GameplayTagConstants.JudgeMode))
                return; // 不扣 AP

            source.ASC.ModifyAttributeCurrentValue(AttributeConstants.ActionPoints, -1f);

            if (form == 4) // V4 花：自愈（S7：不参与分身复制）
            {
                source.ASC.ModifyAttributeCurrentValue(AttributeConstants.Sanity, 0.5f);
                return;
            }

            ApplyHits(state, source, target, form);

            // Task 5 在此插入 MirrorCopy 调用点（同步双发）
        }

        /// <summary>按形态施放命中序列（V3 = 3 × 0.5，目标死亡即停；其余单发）。</summary>
        public static void ApplyHits(WorldState state, Character source, Character target, int form)
        {
            if (form == 3)
            {
                for (int hit = 0; hit < 3; hit++)
                {
                    if (target.IsDead) break;
                    DamagePipeline.ApplyDamage(state, source.Id, target.Id, 0.5f, DamageType.Physical);
                }
                return;
            }
            float dmg = form == 2 ? 1.5f : 1f;
            DamagePipeline.ApplyDamage(state, source.Id, target.Id, dmg, DamageType.Physical);
        }
    }
}
