using Core;
using Core.GAS;
using Gameplay.Judgment;

namespace Gameplay.Characters
{
    /// <summary>
    /// 响应式技能分派：DamagePipeline 埋点调用，按 skillId 分派特例。
    /// 目标无 Reactive 技能时零开销跳过（只读遍历技能数组，无堆分配）。
    /// </summary>
    public static class ReactiveSystem
    {
        /// <summary>伤害应用前：响应技能可修改 finalDamage。返回修改后的伤害。</summary>
        public static void TryOnPreDamage(WorldState state, Character source, Character target, ref float finalDamage)
        {
            if (target == null || target.Definition == null || target.Definition.Skills == null) return;
            SkillDef[] skills = target.Definition.Skills;
            for (int i = 0; i < skills.Length; i++)
            {
                SkillDef s = skills[i];
                if (s == null || s.Activation != SkillActivationType.Reactive) continue;
                if (s.ReactiveTrigger != ReactiveTriggerType.OnPreDamage) continue;
                DispatchOnPreDamage(state, source, target, s.Id, ref finalDamage);
            }
        }

        public static void TryOnKill(WorldState state, Character killer, Character victim)
        {
            if (killer == null || killer.Definition == null || killer.Definition.Skills == null) return;
            SkillDef[] skills = killer.Definition.Skills;
            for (int i = 0; i < skills.Length; i++)
            {
                SkillDef s = skills[i];
                if (s == null || s.Activation != SkillActivationType.Reactive) continue;
                if (s.ReactiveTrigger != ReactiveTriggerType.OnKill) continue;
                DispatchOnKill(state, killer, victim, s.Id);
            }
        }

        // ---- 测试分派与正式分派按 skillId 分支 ----

        private static void DispatchOnPreDamage(WorldState state, Character source, Character target, int skillId, ref float finalDamage)
        {
            switch (skillId)
            {
                case 900: // 测试语义：伤害 +1
                    finalDamage += 1f;
                    break;
                case 30:  // 魔术师镜花水月（Task 3 实现）
                    MagicianMirror.Resolve(state, source, target, ref finalDamage);
                    break;
            }
        }

        private static void DispatchOnKill(WorldState state, Character killer, Character victim, int skillId)
        {
            switch (skillId)
            {
                case 900: // 测试语义：击杀者 +1 AttackBonus
                    killer.ASC.ModifyAttributeCurrentValue(AttributeConstants.AttackBonus, 1f);
                    break;
                case 60:  // 倾听者地狱的呓语（Task 6 实现）
                    ListenerWhisper.OnKill(killer);
                    break;
            }
        }
    }

    /// <summary>魔术师镜花水月（简化版）：受伤害时判定，胜 → 伤害抵消；败 → 伤害 +1。</summary>
    public static class MagicianMirror
    {
        public static void Resolve(WorldState state, Character source, Character target, ref float finalDamage)
        {
            bool win = JudgmentDuel.Resolve(state, target, source); // 受伤害方 vs 来源
            if (win)
                finalDamage = 0f;
            else
                finalDamage += 1f;
        }
    }

    /// <summary>倾听者击杀回复：理智与侵蚀上限回复至 4（理智钳 MaxSanity）。</summary>
    public static class ListenerWhisper
    {
        public static void OnKill(Character killer)
        {
            killer.ASC.SetAttributeBaseValue(AttributeConstants.ErosionLimit, 4f);
            killer.ASC.SetAttributeBaseValue(AttributeConstants.Sanity,
                killer.ASC.GetAttributeValue(AttributeConstants.MaxSanity));
        }
    }
}
